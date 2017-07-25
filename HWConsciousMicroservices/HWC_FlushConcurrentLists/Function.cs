using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using HWC.DataModel;
using HWC.CloudService;

using Amazon.Lambda.Core;

// Assembly attribute to enable the Lambda function's JSON input to be converted into a .NET class.
[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.Json.JsonSerializer))]

namespace HWC_FlushConcurrentLists
{
    public class Function
    {
        public ILambdaContext Context = null;

        private DataClient _dataClient = null;
        private ZoneConcurrentList _zoneConcurrentList = null;

        /// <summary>
        /// Flushes Concurrent Lists (defined in HWC.DataModel). This Lambda function function is typically invoked with scheduled calls.
        /// </summary>
        /// <param name="input"></param>
        /// <param name="context"></param>
        /// <returns></returns>
        public async Task<string> FunctionHandlerAsync(string input, ILambdaContext context)
        {
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
                }
                catch (Exception ex)
                {
                    Context.Logger.LogLine("TransientData ERROR: " + ex.Message);
                }
            }
            else
            {
                throw new Exception("Lambda context is not initialized");
            }

            return input;
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

                // Read ZoneConcurrentList
                List<ZoneConcurrentList> items = await _dataClient?.TransientData?.ScanAsync<ZoneConcurrentList>(null).GetNextSetAsync();
                if (items?.Any() ?? false)
                {
                    _zoneConcurrentList = items.FirstOrDefault();
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
                                                var combinedTs = ((DateTime)userSession.LastSeenInZoneAt).AddSeconds(Config.UserSessionZoneTimeoutThreshold).ToUniversalTime();
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
                }

                // Update ZoneConcurrentList
                if (isDataModified)
                {
                    await _dataClient.TransientData.SaveAsync<ZoneConcurrentList>(_zoneConcurrentList);
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
                DisplayConcurrentList displayConcurrentList = null;

                // Read DisplayConcurrentList
                List<DisplayConcurrentList> items = await _dataClient?.TransientData?.ScanAsync<DisplayConcurrentList>(null).GetNextSetAsync();
                if (items?.Any() ?? false)
                {
                    displayConcurrentList = items.FirstOrDefault();
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
                                if (!(_zoneConcurrentList?.ZoneSessions?.SingleOrDefault(zS => zS.ZoneID == zoneID)?.UserConcurrentList?.UserSessions?.Any() ?? false))
                                {
                                    displaySession.IsUserExists = false;
                                    displaySession.BufferedShowNotificationID = null;
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
                                    var combinedTs = ((DateTime)displaySession.DisplayTouchedAt).AddSeconds(Config.DisplaySessionTouchTimeoutThreshold).ToUniversalTime();
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
                        }
                    }
                }

                // Update DisplayConcurrentList
                if (isDataModified)
                {
                    displayConcurrentList.LastFlushedAt = DateTime.UtcNow;
                    await _dataClient.TransientData.SaveAsync<DisplayConcurrentList>(displayConcurrentList);
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
