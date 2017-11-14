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

namespace HWC_AddMovingBeaconLocation
{
    public class Function
    {
        #region Data members

        private DataClient _dataClient = null;
        private LocationDevice _locationDevice = null;

        public ILambdaContext Context = null;

        #endregion

        /// <summary>
        /// Adds an User Location and returns Coupons for the User (if any)
        /// </summary>
        /// <param name="request"></param>
        /// <param name="context"></param>
        /// <returns>APIGatewayProxyResponse</returns>
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
            
            // Initialize DataClient
            Config.DataClientConfig dataClientConfig = new Config.DataClientConfig(Config.DataClientConfig.RdsDbInfrastructure.Aws);
            using (_dataClient = new DataClient(dataClientConfig))
            {
                Location location = null;
                string locationSerialized = request?.Body;
                // Try to parse the provided json serialzed location into Location object
                try
                {
                    location = JsonConvert.DeserializeObject<Location>(locationSerialized);
                    location.LocatedAtTimestamp = location.LocatedAtTimestamp ?? DateTime.UtcNow;
                }
                catch (Exception ex)
                {
                    Context.Logger.LogLine("Location deserialization ERROR: " + ex.Message);
                }

                // Validate the Location
                if (!(location == null || location.Type == LocationDeviceType.None || string.IsNullOrEmpty(location.DeviceID)))
                {
                    // Check if corresponding LocationDevice exists in DB
                    _locationDevice = await GetLocationDeviceFromLocationAsync(location);
                    if (_locationDevice != null)
                    {
                        var notificationToReturn = await UpdateDisplayConcurrentListAsync();
  
                        // Exclude undesired properties from the returning json object
                        var jsonResolver = new IgnorableSerializerContractResolver();
                        jsonResolver.Ignore(typeof(Notification), new string[] { "ClientSpot", "LocationDeviceNotifications", "DisplayEndpointNotifications", "Coupons" });
                        
                        // Respond OK
                        response.StatusCode = (int)HttpStatusCode.OK;
                        response.Headers = new Dictionary<string, string>() { { "Access-Control-Allow-Origin", "'*'" } };
                        response.Body = JsonConvert.SerializeObject(notificationToReturn, jsonResolver.GetSerializerSettings());
                    }
                    else
                    {
                        // Respond error
                        Error error = new Error((int)HttpStatusCode.NotFound)
                        {
                            Description = "LocationDevice not found",
                            ReasonPharse = "LocationDevice Not Found"
                        };
                        response.StatusCode = error.Code;
                        response.Body = JsonConvert.SerializeObject(error);
                        Context.Logger.LogLine(error.Description);
                    }
                }
                else
                {
                    // Respond error
                    Error error = new Error((int)HttpStatusCode.NotAcceptable)
                    {
                        Description = "Invalid Location input",
                        ReasonPharse = "Not Acceptable Location"
                    };
                    response.StatusCode = error.Code;
                    response.Body = JsonConvert.SerializeObject(error);
                    Context.Logger.LogLine(error.Description);
                }
            }

            return response;
        }
        
        /// <summary>
        /// Updates the DisplayConcurrentList for DisplaySession's LocationDevice info
        /// </summary>
        /// <returns></returns>
        private async Task<Notification> UpdateDisplayConcurrentListAsync()
        {
            Notification notification = null;

            // Process the LocationDevice only if,
            // 1. there is one or more than one DisplayEndpoints associated with the LocationDevice's Zone and
            // 2. there is a Notification associated with it and it's active
            var locationDeviceNotification = _locationDevice?.LocationDeviceNotifications?.FirstOrDefault()?.Notification;
            if ((_locationDevice?.Zone?.DisplayEndpoints?.Any() ?? false) && (locationDeviceNotification?.Active ?? false == true))
            {
                try
                {
                    bool isDataModified = false;
                    var displayConcurrentList = await _dataClient?.TransientData?.ObtainDisplayConcurrentListAsync();
                    if (displayConcurrentList != null)
                    {
                        // Traverse through each DisplayEndpoints in the Zone
                        foreach (DisplayEndpoint displayEndpoint in _locationDevice.Zone.DisplayEndpoints)
                        {
                            // Retrieve the DisplayEndpoint's respective DisplaySession
                            var displaySession = displayConcurrentList.ObtainDisplaySession(displayEndpoint.DisplayEndpointID);
                            if (displaySession != null)
                            {
                                displaySession.LocationDeviceID = _locationDevice.LocationDeviceID;
                                displaySession.LocationDeviceRegisteredAt = DateTime.UtcNow;
                                isDataModified = true;
                            }
                        }
                    }

                    if (isDataModified)
                    {
                        // Save the updated DisplaySessions
                        await _dataClient.TransientData.SaveDisplayConcurrentListAsync();
                        Context.Logger.LogLine("DisplaySessions updated");
                        notification = locationDeviceNotification;
                    }
                }
                catch (Exception ex)
                {
                    Context.Logger.LogLine("DisplayConcurrentList ERROR: " + ex.Message);
                }
            }

            return notification;
        }

        #region Helper methods

        private void ResetDataMembers()
        {
            _dataClient = null;
            _locationDevice = null;
        }
        
        private async Task<LocationDevice> GetLocationDeviceFromLocationAsync(Location location)
        {
            try
            {
                if (location?.Type == LocationDeviceType.IBeacon)
                {
                    return await _dataClient?.ConfigurationData?.LocationDevices?
                        .Include(lD => lD.Zone)
                            .ThenInclude(z => z.DisplayEndpoints)
                        .Include(lD => lD.LocationDeviceNotifications)
                            .ThenInclude(lDN => lDN.Notification)
                        .AsNoTracking()
                        .SingleOrDefaultAsync(lD => lD.DeviceID.Equals(location.DeviceID, StringComparison.OrdinalIgnoreCase));
                }
            }
            catch (Exception ex)
            {
                Context.Logger.LogLine("LocationDevice search ERROR: " + ex.Message);
            }
            return null;
        }
        
        #endregion
    }
}
