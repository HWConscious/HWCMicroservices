using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

using HWC.Core;
using HWC.DataModel;
using HWC.CloudService;

using Amazon.Lambda.Core;
using Amazon.Lambda.APIGatewayEvents;

using Newtonsoft.Json;

// Assembly attribute to enable the Lambda function's JSON input to be converted into a .NET class.
[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.Json.JsonSerializer))]

namespace HWC_GetDisplayEndpointNotifications
{
    public class Function
    {
        private long? _displayEndpointID = null;
        private DataClient _dataClient = null;

        public ILambdaContext Context = null;

        /// <summary>
        /// Returns a Notification belongs to a DisplayEndpoint
        /// </summary>
        /// <param name="request"></param>
        /// <param name="context"></param>
        /// <returns></returns>
        public async Task<APIGatewayProxyResponse> FunctionHandlerAsync(APIGatewayProxyRequest request, ILambdaContext context)
        {
            if (context != null)
            {
                this.Context = context;

                if (request != null)
                {
                    // Reset data members of the Function class;
                    // It needs to be done because AWS Lambda uses the same old instance to invoke the FunctionHandler on concurrent calls
                    ResetDataMembers();

                    // Process the request
                    return await ProcessRequestAsync(request);
                }
                else
                {
                    throw new Exception("Request is null");
                }
            }
            else
            {
                throw new Exception("Lambda context is not initialized");
            }
        }

        /// <summary>
        /// Processes the input request
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        private async Task<APIGatewayProxyResponse> ProcessRequestAsync(APIGatewayProxyRequest request)
        {
            APIGatewayProxyResponse response = new APIGatewayProxyResponse();

            // Retrieve DisplayEndpointID from request path-parameters
            string displayEndpointIdRequestPathName = "display-endpoint-id";
            if (request?.PathParameters?.ContainsKey(displayEndpointIdRequestPathName) ?? false)
            {
                // Try to parse the provided UserID from string to long
                try
                {
                    _displayEndpointID = Convert.ToInt64(request.PathParameters[displayEndpointIdRequestPathName]);
                }
                catch (Exception ex)
                {
                    Context.Logger.LogLine("DisplayEndpointID conversion ERROR: " + ex.Message);
                }
            }

            // Validate the DisplayEndpointID
            if (1 > (_displayEndpointID ?? 0))
            {
                // Respond error
                Error error = new Error((int)HttpStatusCode.BadRequest)
                {
                    Description = "Invalid ID supplied",
                    ReasonPharse = "Bad Request"
                };
                response.StatusCode = error.Code;
                response.Body = JsonConvert.SerializeObject(error);
                Context.Logger.LogLine(error.Description);
            }
            else
            {
                // Initialize DataClient
                Config.DataClientConfig dataClientConfig = new Config.DataClientConfig(Config.DataClientConfig.RdsDbInfrastructure.Aws);
                using (_dataClient = new DataClient(dataClientConfig))
                {
                    // Get the DisplayEndpoint if exists in DB
                    DisplayEndpoint displayEndpoint = await GetDisplayEndpointAsync();
                    if (displayEndpoint != null)
                    {
                        // Retrieve Notification for the DisplayEndpoint (if any)
                        Notification notification = await GetNotificationAsync(displayEndpoint);
                        if (notification != null)
                        {
                            notification.DisplayEndpoint = null;
                        }

                        // Respond OK
                        response.StatusCode = (int)HttpStatusCode.OK;
                        response.Headers = new Dictionary<string, string>() { {" Access-Control-Allow-Origin", "'*'" } };
                        response.Body = JsonConvert.SerializeObject(notification);
                    }
                    else
                    {
                        // Respond error
                        Error error = new Error((int)HttpStatusCode.NotFound)
                        {
                            Description = "DisplayEndpoint not found",
                            ReasonPharse = "DisplayEndpoint Not Found"
                        };
                        response.StatusCode = error.Code;
                        response.Body = JsonConvert.SerializeObject(error);
                        Context.Logger.LogLine(error.Description);
                    }
                }
            }

            return response;
        }

        /// <summary>
        /// Returns Notification for a DisplayEndpoint (if any)
        /// </summary>
        /// <param name="displayEndpoint"></param>
        /// <returns></returns>
        private async Task<Notification> GetNotificationAsync(DisplayEndpoint displayEndpoint)
        {
            Notification notificationToReturn = null;
            try
            {
                bool isDataModified = false;
                DisplayConcurrentList displayConcurrentList = null;

                // Retrieve the list of active Notifications
                List<Notification> activeNotifications = displayEndpoint?.Notifications?
                    .Where(n => n.Active == true).ToList();

                if (activeNotifications?.Any() ?? false)
                {
                    // Get the DisplayConcurrentList
                    List<DisplayConcurrentList> items = await _dataClient?.TransientData?.ScanAsync<DisplayConcurrentList>(null).GetNextSetAsync();
                    displayConcurrentList = (items?.Any() ?? false) ? items.FirstOrDefault() : null;

                    // Retrieve DisplaySession for the DisplayEndpoint
                    DisplaySession displaySession = displayConcurrentList?.DisplaySessions?
                        .SingleOrDefault(dS => dS.DisplayEndpointID == _displayEndpointID);

                    // Generate Notification to show and save to the DisplaySession
                    if (displaySession?.IsUserExists ?? false)
                    {
                        if (displaySession.BufferedShowNotificationID == null)
                        {
                            notificationToReturn = GetARandomNotificationFromAList(activeNotifications);
                            displaySession.BufferedShowNotificationID = GetARandomNotificationFromAList(activeNotifications, notificationToReturn?.NotificationID)?.NotificationID;
                            displaySession.CurrentShowNotificationExpireAt = DateTime.UtcNow.AddSeconds(notificationToReturn?.Timeout ?? 0);
                            isDataModified = true;
                        }
                        else
                        {
                            if (DateTime.UtcNow <= displaySession.CurrentShowNotificationExpireAt.Value.ToUniversalTime())
                            {
                                notificationToReturn = activeNotifications
                                    .SingleOrDefault(n => n.NotificationID == displaySession.BufferedShowNotificationID);
                            }
                            else
                            {
                                var lastBufferedShowNotification = activeNotifications
                                    .SingleOrDefault(n => n.NotificationID == displaySession.BufferedShowNotificationID);

                                notificationToReturn = GetARandomNotificationFromAList(activeNotifications, displaySession.BufferedShowNotificationID);
                                displaySession.BufferedShowNotificationID = notificationToReturn?.NotificationID;
                                displaySession.CurrentShowNotificationExpireAt = displaySession.CurrentShowNotificationExpireAt.Value.AddSeconds(lastBufferedShowNotification?.Timeout ?? 0).ToUniversalTime();
                                isDataModified = true;
                            }
                        }
                    }
                }

                if (isDataModified)
                {
                    // Save the updated DisplaySessions
                    await _dataClient.TransientData.SaveAsync<DisplayConcurrentList>(displayConcurrentList);
                    Context.Logger.LogLine("DisplaySession updated");
                }
            }
            catch (Exception ex)
            {
                notificationToReturn = null;
                Context.Logger.LogLine("DisplayConcurrentList ERROR: " + ex.Message);
            }

            return notificationToReturn;
        }

        #region Helper methods

        private void ResetDataMembers()
        {
            _displayEndpointID = null;
            _dataClient = null;
        }

        private async Task<DisplayEndpoint> GetDisplayEndpointAsync()
        {
            try
            {
                return await _dataClient?.ConfigurationData?.DisplayEndpoints?
                    .Include(dE => dE.Notifications)
                    .AsNoTracking()
                    .SingleOrDefaultAsync(dE => dE.DisplayEndpointID == _displayEndpointID);
            }
            catch (Exception ex)
            {
                Context.Logger.LogLine("DisplayEndpoint search ERROR: " + ex.Message);
            }
            return null;
        }

        private Notification GetARandomNotificationFromAList(List<Notification> notifications, long? excludeNotificationID = null)
        {
            // Return a random Notification from the list of Notifications provided;
            // prevent returning the particular Notification if provided any with 'excludeNotificationID' param
            if (notifications?.Any() ?? false)
            {
                int totalNotifications = notifications.Count();
                if (totalNotifications == 1)
                {
                    return notifications.FirstOrDefault();
                }
                int index = -1;
                Random random = new Random();
                do
                {
                    index = random.Next(totalNotifications);
                }
                while (excludeNotificationID != null && excludeNotificationID == notifications[index].NotificationID);

                return index > -1 ? notifications[index] : null;
            }
            return null;
        }

        #endregion
    }
}
