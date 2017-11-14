using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

using HWC.Core;
using HWC.CloudService;

using Amazon.Lambda.Core;
using Amazon.Lambda.APIGatewayEvents;

using Newtonsoft.Json;

// Assembly attribute to enable the Lambda function's JSON input to be converted into a .NET class.
[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.Json.JsonSerializer))]

namespace HWC_AddDisplayEndpointEvent
{
    public class Function
    {
        #region Data members

        private long? _displayEndpointID = null;
        private DataClient _dataClient = null;

        public ILambdaContext Context = null;

        #endregion

        /// <summary>
        /// Adds a DisplayEndpoint Event
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
                    // Check if DisplayEndpoint exists in DB
                    if (await IsDisplayEndpointExistsAsync())
                    {
                        Event e = null;
                        string eventSerialized = request?.Body;
                        // Try to parse the provided json serialzed event into Event object
                        try
                        {
                            e = JsonConvert.DeserializeObject<Event>(eventSerialized);
                        }
                        catch (Exception ex)
                        {
                            Context.Logger.LogLine("Event deserialization ERROR: " + ex.Message);
                        }

                        // Validate the Event
                        if (!(e == null || e.Type != EventType.DisplayEndpoint_Touch || e.SourceType != EventSourceType.Notification || e.SourceID < 1))
                        {
                            // Register the DisplayEndpoint's touch event
                            await RegisterDisplayEndpointTouchEvent(e);

                            // Respond OK
                            response.StatusCode = (int)HttpStatusCode.OK;
                            response.Headers = new Dictionary<string, string>() { { "Access-Control-Allow-Origin", "'*'" } };
                            response.Body = JsonConvert.SerializeObject(new Empty());
                        }
                        else
                        {
                            // Respond error
                            Error error = new Error((int)HttpStatusCode.NotAcceptable)
                            {
                                Description = "Invalid Event input",
                                ReasonPharse = "Not Acceptable Event"
                            };
                            response.StatusCode = error.Code;
                            response.Body = JsonConvert.SerializeObject(error);
                            Context.Logger.LogLine(error.Description);
                        }
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
        /// Register the DisplayEndpoint's touch event into it's DisplaySession
        /// </summary>
        /// <param name="e"></param>
        /// <returns></returns>
        private async Task RegisterDisplayEndpointTouchEvent(Event e)
        {
            try
            {
                bool isDataModified = false;

                if (e != null)
                {
                    // Retrieve the DisplayEndpoint's respective DisplaySession
                    var displaySession = (await _dataClient?.TransientData?.ObtainDisplayConcurrentListAsync())?.ObtainDisplaySession(_displayEndpointID.Value);

                    // Assign touch info and reset show-notification info
                    if(displaySession != null)
                    {
                        displaySession.DisplayTouchedNotificationID = e.SourceID;
                        displaySession.DisplayTouchedAt = e.EventAtTimestamp.ToUniversalTime();
                        displaySession.CurrentShowNotificationExpireAt = e.EventAtTimestamp.ToUniversalTime();
                        displaySession.BufferedShowNotificationID = null;
                        isDataModified = true;
                    }

                    if (isDataModified)
                    {
                        // Save the updated DisplaySessions
                        await _dataClient.TransientData.SaveDisplayConcurrentListAsync();
                        Context.Logger.LogLine("DisplaySession updated");
                    }
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
            _displayEndpointID = null;
            _dataClient = null;
        }

        private async Task<bool> IsDisplayEndpointExistsAsync()
        {
            try
            {
                return await _dataClient?.ConfigurationData?.DisplayEndpoints?
                    .AsNoTracking()
                    .SingleOrDefaultAsync(dE => dE.DisplayEndpointID == _displayEndpointID) != null ? true : false;
            }
            catch (Exception ex)
            {
                Context.Logger.LogLine("DisplayEndpoint search ERROR: " + ex.Message);
            }
            return false;
        }

        #endregion
    }
}
