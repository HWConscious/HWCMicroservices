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
                    Config.DataClientConfig dataClientConfig = new Config.DataClientConfig(Config.DataClientConfig.RdsDbInfrastructure.Aws);
                    using (DataClient dataClient = new DataClient(dataClientConfig))
                    {
                        if (dataClient.TransientData != null)
                        {
                            await FlushDisplayConcurrentListAsync(dataClient.TransientData);
                            await FlushZoneConcurrentListAsync(dataClient.TransientData);
                        }
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
        /// Flushes DisplayConcurrentList
        /// </summary>
        /// <param name="dataClientConfig"></param>
        private async Task FlushDisplayConcurrentListAsync(TransientData transientData)
        {
            try
            {
                bool isDataModified = false;
                DisplayConcurrentList displayConcurrentList = null;

                // Read DisplayConcurrentList
                List<DisplayConcurrentList> items = await transientData.ScanAsync<DisplayConcurrentList>(null).GetNextSetAsync();
                if (items?.Any() ?? false)
                {
                    displayConcurrentList = items.FirstOrDefault();
                    if (displayConcurrentList?.DisplaySessions?.Any() ?? false)
                    {
                        // Traverse through all DisplaySessions in DisplayConcurrentList
                        foreach (DisplaySession displaySession in displayConcurrentList.DisplaySessions)
                        {
                            if (displaySession?.DisplayTouched ?? false == true)
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
                                    displaySession.DisplayTouched = false;
                                    displaySession.DisplayTouchedAt = null;
                                    displaySession.TouchedNotificationID = null;
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
                    await transientData.SaveAsync<DisplayConcurrentList>(displayConcurrentList);
                    Context.Logger.LogLine("DisplayConcurrentList updated");
                }
            }
            catch (Exception ex)
            {
                Context.Logger.LogLine("DisplayConcurrentList ERROR: " + ex.Message);
            }
        }

        /// <summary>
        /// Flushes ZoneConcurrentList
        /// </summary>
        /// <param name="dataClientConfig"></param>
        private async Task FlushZoneConcurrentListAsync(TransientData transientData)
        {
            try
            {
                bool isDataModified = false;
                ZoneConcurrentList zoneConcurrentList = null;

                // Read ZoneConcurrentList
                List<ZoneConcurrentList> items = await transientData.ScanAsync<ZoneConcurrentList>(null).GetNextSetAsync();
                if (items?.Any() ?? false)
                {
                    zoneConcurrentList = items.FirstOrDefault();
                    if (zoneConcurrentList?.ZoneSessions?.Any() ?? false)
                    {
                        // Traverse through all ZoneSessions in ZoneConcurrentList
                        foreach (ZoneSession zoneSession in zoneConcurrentList.ZoneSessions)
                        {
                            if (zoneSession?.UserConcurrentList != null)
                            {
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
                    await transientData.SaveAsync<ZoneConcurrentList>(zoneConcurrentList);
                    Context.Logger.LogLine("ZoneConcurrentList updated");
                }
            }
            catch (Exception ex)
            {
                Context.Logger.LogLine("ZoneConcurrentList ERROR: " + ex.Message);
            }
        }
    }
}
