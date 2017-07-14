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

namespace HWC_AddUserLocation
{
    public class Function
    {
        private long? _userID = null;
        private DataClient _dataClient = null;
        private LocationDevice _locationDevice = null;
        private DisplayConcurrentList _displayConcurrentList = null;
        private ZoneConcurrentList _zoneConcurrentList = null;
        
        public ILambdaContext Context = null;
        
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

            // Retrieve UserID from request path-parameters
            string userIdRequestPathName = "user-id";
            if (request?.PathParameters?.ContainsKey(userIdRequestPathName) ?? false)
            {
                // Try to parse the provided UserID from string to long
                try
                {
                    _userID = Convert.ToInt64(request.PathParameters[userIdRequestPathName]);
                }
                catch (Exception ex)
                {
                    Context.Logger.LogLine("UserID conversion ERROR: " + ex.Message);
                }
            }

            // Validate the UserID
            if (1 > (_userID ?? 0))
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
                InitializeDataClient();
                using (_dataClient)
                {
                    // Check if User exists in DB
                    if (await IsUserExistsAsync())
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
                            // Process the Location
                            await AddLocationAysnc(location);

                            // Retrieve Coupons for the User (if any)
                            List<Coupon> coupons = await GetCouponsAsync();
                            if (coupons != null)
                            {
                                // Respond OK
                                response.StatusCode = (int)HttpStatusCode.OK;
                                response.Headers = new Dictionary<string, string>() { { "Access-Control-Allow-Origin", "'*'" } };
                                response.Body = JsonConvert.SerializeObject(coupons);
                            }
                            else
                            {
                                Context.Logger.LogLine("List of Coupons is null");
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
                    else
                    {
                        // Respond error
                        Error error = new Error((int)HttpStatusCode.NotFound)
                        {
                            Description = "User not found",
                            ReasonPharse = "User Not Found"
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
        /// Processes a Location
        /// </summary>
        /// <param name="location"></param>
        /// <returns></returns>
        private async Task AddLocationAysnc(Location location)
        {
            // Get the LocationDevice matching with the Location provided
            _locationDevice = await GetLocationDeviceFromLocationAsync(location);
            if (_locationDevice != null)
            {
                if (_locationDevice.Zone != null)
                {
                    // Update the DisplayConcurrentList only if there is one or more than one DisplayEndpoints associated with the Zone
                    if (_locationDevice.Zone.DisplayEndpoints?.Any() ?? false)
                    {
                        await UpdateDisplayConcurrentListAsync();
                    }
                    // Update the ZoneConcurrentList
                    await UpdateZoneConcurrentListAsync();
                }
            }
            else
            {
                Context.Logger.LogLine("LocationDevice not found for the provided Location");
            }
        }

        /// <summary>
        /// Returns Coupons for the User (if any)
        /// </summary>
        /// <returns></returns>
        private async Task<List<Coupon>> GetCouponsAsync()
        {
            bool isDataModified = false;
            List<Coupon> couponsToReturn = null;

            try
            {
                // Retrieve DisplayEndpointIDs in the Zone
                var zoneDisplayEndpointIDs = _locationDevice?.Zone?.DisplayEndpoints?
                    .Select(dE => dE.DisplayEndpointID);

                // Retrieve NotificationIDs (only the ones are touched) associated with the DisplayEndpoints in the Zone
                var touchedNotificationIDs = _displayConcurrentList?.DisplaySessions?
                    .Where(dS => zoneDisplayEndpointIDs?.Contains(dS.DisplayEndpointID) ?? false)
                    .Where(dS => dS.DisplayTouched == true)
                    .Select(dS => dS.TouchedNotificationID);

                // Retrieve Coupons associated with the touched Notifications
                var coupons = _dataClient?.ConfigurationData?.Coupons?
                    .Where(c => touchedNotificationIDs.Contains(c.NotificationID));

                // Retrieve the User's respective UserSession 
                var userSession = _zoneConcurrentList?.ZoneSessions?
                    .SingleOrDefault(zS => zS.ZoneID == _locationDevice?.ZoneID).UserConcurrentList?.UserSessions?
                        .SingleOrDefault(uS => uS.UserID == _userID);

                // Create a new list of Coupons from the retrieved Coupons (associated with the touched Notifications),
                // which are not received by the User for the UserSession yet
                couponsToReturn = coupons?
                    .Where(c => !userSession.ReceivedCouponIDs.Contains(c.CouponID)).ToList();
                var couponsToReturnIDs = couponsToReturn?
                    .Select(c => c.CouponID);

                if (couponsToReturnIDs?.Any() ?? false)
                {
                    // Update the UserSession's received-coupons-registry with the new list of Coupons
                    userSession?.ReceivedCouponIDs?.AddRange(couponsToReturnIDs);

                    // Add the new list of Coupons to transactional data
                    var userCoupons = new List<UserCoupon>();
                    foreach(long couponID in couponsToReturnIDs)
                    {
                        userCoupons.Add(new UserCoupon()
                        {
                            UserID = _userID.Value,
                            CouponID = couponID,
                            ReceivedAt = DateTime.UtcNow,
                            CouponRedempted = false
                        });
                    }
                    _dataClient?.TransactionalData?.UserCoupons?.AddRange(userCoupons);

                    isDataModified = true;
                }
            }
            catch (Exception ex)
            {
                Context.Logger.LogLine(ex.Message);
            }

            if (isDataModified)
            {
                try
                {
                    // Save the updated UserSessions
                    await _dataClient.TransientData.SaveAsync<ZoneConcurrentList>(_zoneConcurrentList);
                    Context.Logger.LogLine("UserSessions updated for received-coupons-registry");
                    
                    // Save the updated UserCoupons
                    await _dataClient.TransactionalData.SaveChangesAsync();
                    Context.Logger.LogLine("UserCoupons updated for transactional data");
                }
                catch (Exception ex)
                {
                    couponsToReturn = null;
                    Context.Logger.LogLine("Data saving ERROR: " + ex.Message);
                }
            }

            return couponsToReturn;
        }

        /// <summary>
        /// Updates the DisplayConcurrentList for DisplaySession's notification invocation & expiration timestamps
        /// Also creates the DisplaySessions if doesn't exists
        /// </summary>
        /// <returns></returns>
        private async Task UpdateDisplayConcurrentListAsync()
        {
            try
            {
                bool isDataModified = false;

                // Get the DisplayConcurrentList
                if (_displayConcurrentList == null)
                {
                    List<DisplayConcurrentList> items = await _dataClient?.TransientData?.ScanAsync<DisplayConcurrentList>(null).GetNextSetAsync();
                    // Create new DisplayConcurrentList if it doesn't exists
                    _displayConcurrentList = (items?.Any() ?? false) ? items.FirstOrDefault() : InitializeDisplayConcurrentList();
                }

                if (_displayConcurrentList != null)
                {
                    _displayConcurrentList.DisplaySessions = _displayConcurrentList.DisplaySessions ?? new List<DisplaySession>();

                    // Traverse through each DisplayEndpoints in the Zone
                    foreach (DisplayEndpoint displayEndpoint in _locationDevice.Zone.DisplayEndpoints)
                    {
                        // Process the DisplayEndpoint only if there is one or more than one Notifications associated with it
                        if (displayEndpoint.Notifications?.Any() ?? false)
                        {
                            // Try to get the DisplayEndpoint's respective DisplaySession from DisplayConcurrentList, create new if not exists.
                            DisplaySession displaySession = _displayConcurrentList.DisplaySessions
                                .Where(dS => dS.DisplayEndpointID == displayEndpoint.DisplayEndpointID)
                                .FirstOrDefault();
                            if (displaySession == null)
                            {
                                // Create new DisplaySession for the DisplayEndpoint
                                displaySession = new DisplaySession() { DisplayEndpointID = displayEndpoint.DisplayEndpointID };
                                _displayConcurrentList.DisplaySessions.Add(displaySession);
                            }

                            // Update the DisplaySession with invocation (current time) & expiration (total of each Notification's timeout) timestamps for Notifications
                            int secondsToAddForExpiration = displayEndpoint.Notifications.Sum(n => n.Timeout);
                            displaySession.NotificationsInvokedAt = DateTime.UtcNow;
                            displaySession.ExpireNotificationsAt = DateTime.UtcNow.AddSeconds(secondsToAddForExpiration);
                            isDataModified = true;
                        }
                    }
                }

                if (isDataModified)
                {
                    // Save the updated DisplaySessions
                    await _dataClient.TransientData.SaveAsync<DisplayConcurrentList>(_displayConcurrentList);
                    Context.Logger.LogLine("DisplaySessions updated");
                }
            }
            catch (Exception ex)
            {
                Context.Logger.LogLine("DisplayConcurrentList ERROR: " + ex.Message);
            }
        }

        /// <summary>
        /// Updates the ZoneConcurrentList for UserSession's zone-enter and zone-last-seen timestamps
        /// Also creates the UserSessions if doesn't exists
        /// </summary>
        /// <returns></returns>
        private async Task UpdateZoneConcurrentListAsync()
        {
            try
            {
                bool isDataModified = false;

                // Get the ZoneConcurrentList
                if (_zoneConcurrentList == null)
                {
                    List<ZoneConcurrentList> items = await _dataClient?.TransientData?.ScanAsync<ZoneConcurrentList>(null).GetNextSetAsync();
                    // Create new ZoneConcurrentList if it doesn't exists
                    _zoneConcurrentList = (items?.Any() ?? false) ? items.FirstOrDefault() : InitializeZoneConcurrentList();
                }

                if (_zoneConcurrentList != null)
                {
                    _zoneConcurrentList.ZoneSessions = _zoneConcurrentList.ZoneSessions ?? new List<ZoneSession>();

                    // Try to get the Zone's respective ZoneSession from ZoneConcurrentList, create new if not exists.
                    ZoneSession zoneSession = _zoneConcurrentList.ZoneSessions
                        .Where(zS => zS.ZoneID == _locationDevice.ZoneID)
                        .FirstOrDefault();
                    if (zoneSession == null)
                    {
                        zoneSession = new ZoneSession() { ZoneID = _locationDevice.ZoneID.Value };
                        _zoneConcurrentList.ZoneSessions.Add(zoneSession);
                    }
                    zoneSession.UserConcurrentList = zoneSession.UserConcurrentList ?? new UserConcurrentList();
                    zoneSession.UserConcurrentList.UserSessions = zoneSession.UserConcurrentList.UserSessions ?? new List<UserSession>();
                    
                    // Try to get the User's respective UserSession from UserConcurrentList, create new if not exists.
                    UserSession userSession = zoneSession.UserConcurrentList.UserSessions
                        .Where(uS => uS.UserID == _userID)
                        .FirstOrDefault();
                    if (userSession == null)
                    {
                        // Create new UserSession for the User with zone-enter and zone-last-seen timestamps as current time
                        userSession = new UserSession()
                        {
                            UserID = _userID.Value,
                            EnteredIntoZoneAt = DateTime.UtcNow,
                            LastSeenInZoneAt = DateTime.UtcNow,
                            ReceivedCouponIDs = new List<long>()
                        };
                        zoneSession.UserConcurrentList.UserSessions.Add(userSession);
                    }
                    else
                    {
                        // Update the existing UserSession's zone-last-seen timestamp as current time
                        userSession.LastSeenInZoneAt = DateTime.UtcNow;
                    }
                    isDataModified = true;
                }

                if (isDataModified)
                {
                    // Save the updated UserSessions
                    await _dataClient.TransientData.SaveAsync<ZoneConcurrentList>(_zoneConcurrentList);
                    Context.Logger.LogLine("UserSessions updated");
                }
            }
            catch (Exception ex)
            {
                Context.Logger.LogLine("ZoneConcurrentList ERROR: " + ex.Message);
            }
        }

        #region Helper methods

        private void ResetDataMembers()
        {
            _userID = null;
            _dataClient = null;
            _locationDevice = null;
            _displayConcurrentList = null;
            _zoneConcurrentList = null;
        }

        private void InitializeDataClient()
        {
            if (_dataClient == null)
            {
                try
                {
                    Config.DataClientConfig dataClientConfig = new Config.DataClientConfig(Config.DataClientConfig.RdsDbInfrastructure.Aws);
                    _dataClient = new DataClient(dataClientConfig);
                }
                catch (Exception ex)
                {
                    Context.Logger.LogLine("DataClient initialization ERROR: " + ex.Message);
                }
            }
        }

        private async Task<bool> IsUserExistsAsync()
        {
            try
            {
                return await _dataClient?.ConfigurationData?.Users?.AsNoTracking().SingleOrDefaultAsync(u => u.UserID == _userID) != null ? true : false;
            }
            catch (Exception ex)
            {
                Context.Logger.LogLine("User search ERROR: " + ex.Message);
            }
            return false;
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
                                .ThenInclude(dE => dE.Notifications)
                        .AsNoTracking()
                        .SingleOrDefaultAsync(lD => lD.DeviceID == location.DeviceID);
                }
            }
            catch (Exception ex)
            {
                Context.Logger.LogLine("LocationDevice search ERROR: " + ex.Message);
            }
            return null;
        }

        private DisplayConcurrentList InitializeDisplayConcurrentList()
        {
            return new DisplayConcurrentList()
            {
                ID = Guid.NewGuid(),
                DisplaySessions = new List<DisplaySession>()
            };
        }

        private ZoneConcurrentList InitializeZoneConcurrentList()
        {
            return new ZoneConcurrentList()
            {
                ID = Guid.NewGuid(),
                ZoneSessions = new List<ZoneSession>()
            };
        }

        #endregion
    }
}
