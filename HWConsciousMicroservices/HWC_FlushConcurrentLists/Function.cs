using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

using HWC.Core;
using HWC.DataModel;
using HWC.CloudService;

using Amazon.Lambda.Core;
using Amazon.Lambda.APIGatewayEvents;

using Newtonsoft.Json;

// Assembly attribute to enable the Lambda function's JSON input to be converted into a .NET class.
[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.Json.JsonSerializer))]

namespace HWC_FlushConcurrentLists
{
    public class Function
    {
        #region Data members

        private DataClient _dataClient = null;
        private ZoneConcurrentList _zoneConcurrentList = null;

        public ILambdaContext Context = null;

        #endregion

        /// <summary>
        /// Flushes Concurrent Lists (defined in HWC.DataModel). This Lambda function function is typically invoked with scheduled calls.
        /// </summary>
        /// <param name="input"></param>
        /// <param name="context"></param>
        /// <returns></returns>
        public async Task<APIGatewayProxyResponse> FunctionHandlerAsync(APIGatewayProxyRequest request, ILambdaContext context)
        {
            APIGatewayProxyResponse response = new APIGatewayProxyResponse();

            if (context != null)
            {
                this.Context = context;

                try
                {
                    // Reset data members of the Function class;
                    // It needs to be done because AWS Lambda uses the same old instance to invoke the FunctionHandler on concurrent calls
                    ResetDataMembers();

                    // Initialize DataClient
                    Config.DataClientConfig dataClientConfig = new Config.DataClientConfig(Config.DataClientConfig.RdsDbInfrastructure.Aws);
                    using (_dataClient = new DataClient(dataClientConfig))
                    {
                        await FlushZoneConcurrentListAsync();
                        await FlushDisplayConcurrentListAsync();
                    }

                    // Respond OK
                    response.StatusCode = (int)HttpStatusCode.OK;
                    response.Headers = new Dictionary<string, string>() { { "Access-Control-Allow-Origin", "'*'" } };
                    response.Body = JsonConvert.SerializeObject(new Empty());
                }
                catch (Exception ex)
                {
                    // Respond error
                    Error error = new Error((int)HttpStatusCode.Forbidden)
                    {
                        Description = "Forbidden",
                        ReasonPharse = "Forbidden"
                    };
                    response.StatusCode = error.Code;
                    response.Body = JsonConvert.SerializeObject(error);
                    Context.Logger.LogLine("TransientData ERROR: " + ex.Message);
                }
            }
            else
            {
                throw new Exception("Lambda context is not initialized");
            }

            return response;
        }

        /// <summary>
        /// Flushes ZoneConcurrentList
        /// </summary>
        /// <param name="dataClientConfig"></param>
        private async Task FlushZoneConcurrentListAsync()
        {
            try
            {
                bool isDataModified = false;
                _zoneConcurrentList = await _dataClient?.TransientData?.ObtainZoneConcurrentListAsync();
                if (_zoneConcurrentList?.ZoneSessions?.Any() ?? false)
                {
                    // Traverse through all ZoneSessions in ZoneConcurrentList
                    foreach (ZoneSession zoneSession in _zoneConcurrentList.ZoneSessions)
                    {
                        if (zoneSession?.UserConcurrentList != null)
                        {
                            // Flush the UserConcurrentList's UserSessions
                            UserConcurrentList userConcurrentList = zoneSession.UserConcurrentList;
                            if (userConcurrentList.UserSessions?.Any() ?? false)
                            {
                                List<UserSession> userSessionsToRemove = new List<UserSession>();

                                // Traverse through all UserSessions in UserConcurrentList
                                foreach (UserSession userSession in userConcurrentList.UserSessions)
                                {
                                    if (userSession != null)
                                    {
                                        if (userSession.LastSeenInZoneAt != null)
                                        {
                                            // Remove UserSession from the Zone if the combination of the time the User is last seen in the Zone and
                                            // UserSession zone-timeout threshold is lesser than current time
                                            var combinedTs = userSession.LastSeenInZoneAt.Value.AddSeconds(Config.UserSessionZoneTimeoutThreshold).ToUniversalTime();
                                            if (combinedTs < DateTime.UtcNow)
                                            {
                                                userSessionsToRemove.Add(userSession);
                                            }
                                        }
                                        else
                                        {
                                            userSessionsToRemove.Add(userSession);
                                        }
                                    }
                                }

                                // Removing UserSessions from the UserConcurrentList
                                if (userSessionsToRemove.Any())
                                {
                                    foreach (UserSession userSessionToRemove in userSessionsToRemove)
                                    {
                                        userConcurrentList.UserSessions.Remove(userSessionToRemove);
                                    }
                                    userConcurrentList.LastFlushedAt = DateTime.UtcNow;
                                    isDataModified = true;
                                }
                            }
                        }
                    }
                }
                
                if (isDataModified)
                {
                    // Save the updated ZoneConcurrentList
                    await _dataClient.TransientData.SaveZoneConcurrentListAsync();
                    Context.Logger.LogLine("ZoneConcurrentList updated");
                }
            }
            catch (Exception ex)
            {
                Context.Logger.LogLine("ZoneConcurrentList ERROR: " + ex.Message);
            }
        }

        /// <summary>
        /// Flushes DisplayConcurrentList
        /// </summary>
        /// <param name="dataClientConfig"></param>
        private async Task FlushDisplayConcurrentListAsync()
        {
            try
            {
                bool isDataModified = false;
                var displayConcurrentList = await _dataClient?.TransientData?.ObtainDisplayConcurrentListAsync();
                if (displayConcurrentList?.DisplaySessions?.Any() ?? false)
                {
                    // Get a list of all DisplayEndpoints
                    var displayEndpoints = _dataClient?.ConfigurationData?.DisplayEndpoints.ToList();

                    // Traverse through all DisplaySessions in DisplayConcurrentList
                    foreach (DisplaySession displaySession in displayConcurrentList.DisplaySessions)
                    {
                        // Flush the DisplaySession's show-notification information
                        if (displaySession?.IsUserExists == true)
                        {
                            // Get the ZoneID of the DisplaySession
                            long? zoneID = displayEndpoints?
                                .SingleOrDefault(dE => dE.DisplayEndpointID == displaySession.DisplayEndpointID).ZoneID;

                            // Reset show-notification info if there is no UserSession for the Zone's ZoneSession
                            if (!(_zoneConcurrentList?.ObtainZoneSession(zoneID.Value)?.UserConcurrentList?.UserSessions?.Any() ?? false))
                            {
                                displaySession.IsUserExists = false;
                                displaySession.BufferedShowNotificationID = null;
                                if (displaySession.CurrentShowNotificationExpireAt != null && displaySession.CurrentShowNotificationExpireAt.Value.ToUniversalTime() < DateTime.UtcNow)
                                {
                                    displaySession.CurrentShowNotificationExpireAt = null;
                                }
                                isDataModified = true;
                            }
                        }

                        // Flush the DisplaySession's touch information
                        if (displaySession?.DisplayTouchedNotificationID != null)
                        {
                            bool resetTouchInfo = false;

                            if (displaySession.DisplayTouchedAt != null)
                            {
                                // Reset touch info if the combination of the time DisplayEndpoint is touched and
                                // DisplaySession touch-timeout threshold is lesser than current time
                                var combinedTs = displaySession.DisplayTouchedAt.Value.AddSeconds(Config.DisplaySessionTouchTimeoutThreshold).ToUniversalTime();
                                resetTouchInfo = combinedTs < DateTime.UtcNow ? true : false;
                            }
                            else
                            {
                                resetTouchInfo = true;
                            }

                            // Reseting DisplaySession's touch info
                            if (resetTouchInfo)
                            {
                                displaySession.DisplayTouchedNotificationID = null;
                                displaySession.DisplayTouchedAt = null;
                                isDataModified = true;
                            }
                        }

                        // Flush the DisplaySession's LocationDevice information
                        if (displaySession?.LocationDeviceID != null)
                        {
                            bool resetLocationDeviceInfo = false;

                            if (displaySession.LocationDeviceRegisteredAt != null)
                            {
                                // Reset LocationDevice info if the combination of the time it's registered into session and
                                // DisplaySession LocationDevice-timeout threshold is lesser than current time
                                var combinedTs = displaySession.LocationDeviceRegisteredAt.Value.AddSeconds(Config.DisplaySessionLocationDeviceTimeoutThreshold).ToUniversalTime();
                                resetLocationDeviceInfo = combinedTs < DateTime.UtcNow ? true : false;
                            }
                            else
                            {
                                resetLocationDeviceInfo = true;
                            }

                            // Reseting DisplaySession's LocationDevice info
                            if (resetLocationDeviceInfo)
                            {
                                displaySession.LocationDeviceID = null;
                                displaySession.LocationDeviceRegisteredAt = null;
                                isDataModified = true;
                            }
                        }
                    }
                }
                
                if (isDataModified)
                {
                    displayConcurrentList.LastFlushedAt = DateTime.UtcNow;
                    // Save the updated DisplayConcurrentList
                    await _dataClient.TransientData.SaveDisplayConcurrentListAsync();
                    Context.Logger.LogLine("DisplayConcurrentList updated");
                }
            }
            catch (Exception ex)
            {
                Context.Logger.LogLine("DisplayConcurrentList ERROR: " + ex.Message);
            }
        }

        #region Helper methods

        private void ResetDataMembers()
        {
            _dataClient = null;
            _zoneConcurrentList = null;
        }

        #endregion
    }
}
