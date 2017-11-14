using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

using HWC.Core;
using HWC.DataModel;
using HWC.CloudService;

using Amazon.Lambda.Core;

using Newtonsoft.Json;

// Assembly attribute to enable the Lambda function's JSON input to be converted into a .NET class.
[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.Json.JsonSerializer))]

namespace HWC_SeedDemoContentIntoDB
{
    public class Function
    {
        #region Data members

        public ILambdaContext Context = null;

        #endregion

        /// <summary>
        /// Seeds demo contents into HWC databases and reads
        /// </summary>
        /// <param name="input"></param>
        /// <param name="context"></param>
        /// <returns></returns>
        public async Task<string> FunctionHandlerAsync(string input, ILambdaContext context)
        {
            if (context != null)
            {
                this.Context = context;

                Config.DataClientConfig dataClientConfig = new Config.DataClientConfig(Config.DataClientConfig.RdsDbInfrastructure.Aws);

                await ConfigurationDataDemoAsync(dataClientConfig);
                await TransactionalDataDemoAsync(dataClientConfig);
                await TransientDataDemoAsync(dataClientConfig);
            }
            else
            {
                throw new Exception("Lambda context is not initialized");
            }

            return input;
        }

        /// <summary>
        /// Seeds Configuration Data
        /// </summary>
        /// <param name="dataClientConfig"></param>
        /// <returns></returns>
        private async Task ConfigurationDataDemoAsync(Config.DataClientConfig dataClientConfig)
        {
            try
            {
                using (DataClient dataClient = new DataClient(dataClientConfig))
                {
                    if (dataClient.ConfigurationData != null)
                    {
                        ConfigurationData configurationData = dataClient.ConfigurationData;

                        // Explicit loading. When the entity is first read, related data isn't retrieved.
                        // Example code that retrieves the related data. It impacts retrieval performance hence only use if it's needed.
                        await configurationData.Clients.Include(client => client.ClientSpots).LoadAsync();
                        await configurationData.ClientSpots.Include(clientSpot => clientSpot.Zones)
                                                     .Include(clientSpot => clientSpot.LocationDevices)
                                                     .Include(clientSpot => clientSpot.DisplayEndpoints)
                                                     .Include(clientSpot => clientSpot.Notifications)
                                                     .Include(clientSpot => clientSpot.Coupons).LoadAsync();
                        await configurationData.Zones.Include(zone => zone.LocationDevices)
                                                .Include(zone => zone.DisplayEndpoints).LoadAsync();
                        await configurationData.LocationDevices.Include(locationDevice => locationDevice.LocationDeviceNotifications).LoadAsync();
                        await configurationData.DisplayEndpoints.Include(displayEndpoint => displayEndpoint.DisplayEndpointNotifications).LoadAsync();
                        await configurationData.Notifications.Include(notification => notification.Coupons)
                                                        .Include(notification => notification.LocationDeviceNotifications)
                                                        .Include(notification => notification.DisplayEndpointNotifications).LoadAsync();


                        ///////// DB seeding with demo content /////////

                        // Look for any Clients
                        if (configurationData.Clients.Any())
                        {
                            string log = "Total Client found: " + configurationData.Clients.LongCount() + " | " +
                                        "Total ClientSpot found: " + configurationData.ClientSpots.LongCount() + " | " +
                                        "Total Zone found: " + configurationData.Zones.LongCount() + " | " +
                                        "Total LocationDevice found: " + configurationData.LocationDevices.LongCount() + " | " +
                                        "Total DisplayEndpoint found: " + configurationData.DisplayEndpoints.LongCount() + " | " +
                                        "Total Notification found: " + configurationData.Notifications.LongCount() + " | " +
                                        "Total Coupon found: " + configurationData.Coupons.LongCount() + " | " +
                                        "Total User found: " + configurationData.Users.LongCount();
                            Context.Logger.LogLine("[ConfigurationData Summary]");
                            Context.Logger.LogLine(log);
                            return;   // DB has been seeded already
                        }

                        // DB save operation
                        Context.Logger.LogLine("[Adding contents for ConfigurationData]");

                        // Adding Clients
                        var clients = new Client[]
                        {
                            new Client{ Name = "Demo Mart", PhoneNumber = "001-123-4567", Address = "US" }
                        };
                        foreach (Client client in clients)
                        {
                            configurationData.Clients.Add(client);
                        }
                        Context.Logger.LogLine("1 Client added");

                        // Adding ClientSpots
                        var clientSpots = new ClientSpot[]
                        {
                            new ClientSpot{ ClientID = clients[0].ClientID, Name = "DemoMart Seattle Store", PhoneNumber = "001-123-4567", Address = "Seattle, US" },
                            new ClientSpot{ ClientID = clients[0].ClientID, Name = "DemoMart LA Store", PhoneNumber = "001-123-4567", Address = "LA, US" }
                        };
                        foreach (ClientSpot clientSpot in clientSpots)
                        {
                            configurationData.ClientSpots.Add(clientSpot);
                        }
                        Context.Logger.LogLine("2 ClientSpots added");

                        // Adding Zones
                        var zones = new Zone[]
                        {
                            new Zone{ ClientSpotID = clientSpots[0].ClientSpotID, Name = "Pseudo Zone" },
                            new Zone{ ClientSpotID = clientSpots[0].ClientSpotID, Name = "Demo Entrance Zone (Scenario #1)" },
                            new Zone{ ClientSpotID = clientSpots[0].ClientSpotID, Name = "Demo Bakery Zone (Scenario #2)" },
                            new Zone{ ClientSpotID = clientSpots[0].ClientSpotID, Name = "Demo Electronics Zone (Scenario #3)" }
                        };
                        foreach (Zone zone in zones)
                        {
                            configurationData.Zones.Add(zone);
                        }
                        Context.Logger.LogLine("4 Zones added");

                        // SAVING the changes into physical DB
                        await configurationData.SaveChangesAsync();

                        // Adding LocationDevices
                        var locationDevices = new LocationDevice[]
                        {
                            new LocationDevice{ ClientSpotID = clientSpots[0].ClientSpotID, ZoneID = zones[0].ZoneID, Type = LocationDeviceType.IBeacon, DeviceID = "pseudo-uuid" },
                            new LocationDevice{ ClientSpotID = clientSpots[0].ClientSpotID, ZoneID = zones[1].ZoneID, Type = LocationDeviceType.IBeacon, DeviceID = "11111111-1111-1111-1111-111111111111" },
                            new LocationDevice{ ClientSpotID = clientSpots[0].ClientSpotID, ZoneID = zones[2].ZoneID, Type = LocationDeviceType.IBeacon, DeviceID = "22222222-2222-2222-2222-222222222222" },
                            new LocationDevice{ ClientSpotID = clientSpots[0].ClientSpotID, ZoneID = zones[3].ZoneID, Type = LocationDeviceType.IBeacon, DeviceID = "33333333-3333-3333-3333-333333333333" },
                            new LocationDevice{ ClientSpotID = clientSpots[0].ClientSpotID, ZoneID = zones[3].ZoneID, Type = LocationDeviceType.IBeacon, DeviceID = "44444444-4444-4444-4444-444444444444" }
                        };
                        foreach (LocationDevice locationDevice in locationDevices)
                        {
                            configurationData.LocationDevices.Add(locationDevice);
                        }
                        Context.Logger.LogLine("5 LocationDevices added");

                        // Adding DisplayEndpoints
                        var displayEndpoints = new DisplayEndpoint[]
                        {
                            new DisplayEndpoint{ ClientSpotID = clientSpots[0].ClientSpotID, ZoneID = zones[0].ZoneID, Name = "Pseudo Display" },
                            new DisplayEndpoint{ ClientSpotID = clientSpots[0].ClientSpotID, ZoneID = zones[1].ZoneID, Name = "Demo Display 1" },
                            new DisplayEndpoint{ ClientSpotID = clientSpots[0].ClientSpotID, ZoneID = zones[2].ZoneID, Name = "Demo Display 2" },
                            new DisplayEndpoint{ ClientSpotID = clientSpots[0].ClientSpotID, ZoneID = zones[3].ZoneID, Name = "Demo Display 3" }
                        };
                        foreach (DisplayEndpoint displayEndpoint in displayEndpoints)
                        {
                            configurationData.DisplayEndpoints.Add(displayEndpoint);
                        }
                        Context.Logger.LogLine("4 DisplayEndpoints added");

                        // Adding Notifications
                        var notifications = new Notification[]
                        {
                            new Notification{ ClientSpotID = clientSpots[0].ClientSpotID, Name = "Pseduo Notification", Timeout = 10, ContentMimeType = MimeType.ImagePng, ContentSubject = "Pseduo Notification", ContentCaption = "Pseduo advertisement", ContentBody = "http://www.abc.com/images/img1.png" },
                            new Notification{ ClientSpotID = clientSpots[0].ClientSpotID, Name = "Demo Notification 1 (Scenario #1)", SortOrder = 1, Timeout = 20, ShowProgressBar = false, ContentMimeType = MimeType.ImagePng, ContentSubject = "Welcome Greetings", ContentCaption = "Welcome to DemoMart Seattle Store!", ContentBody = "https://s3.amazonaws.com/hwconscious/notifications/demo_mart/welcome.png" },
                            new Notification{ ClientSpotID = clientSpots[0].ClientSpotID, Name = "Demo Notification 2 (Scenario #2)", SortOrder = 1, Timeout = 10, ContentMimeType = MimeType.ImageJpg, ContentSubject = "Advertisement for Doughnut", ContentCaption = "4 Delicious Doughnuts", ContentBody = "https://s3.amazonaws.com/hwconscious/notifications/demo_mart/doughnut.jpg" },
                            new Notification{ ClientSpotID = clientSpots[0].ClientSpotID, Name = "Demo Notification 3 (Scenario #2)", SortOrder = 2, Timeout = 10, ContentMimeType = MimeType.ImageJpg, ContentSubject = "Advertisement for Croissant", ContentCaption = "Croissant for breakfast needs", ContentBody = "https://s3.amazonaws.com/hwconscious/notifications/demo_mart/croissant.jpg" },
                            new Notification{ ClientSpotID = clientSpots[0].ClientSpotID, Name = "Demo Notification 4 (Scenario #2)", SortOrder = 3, Timeout = 10, ContentMimeType = MimeType.VideoMp4, ContentSubject = "Advertisement for Coke", ContentCaption = "Taste the Feeling", ContentBody = "https://s3.amazonaws.com/hwconscious/notifications/demo_mart/coke.mp4" },
                            new Notification{ ClientSpotID = clientSpots[0].ClientSpotID, Name = "Demo Notification 5 (Scenario #3)", Timeout = 10, ContentMimeType = MimeType.None, ContentSubject = "Advertisement for ", ContentCaption = "", ContentBody = "" },
                            new Notification{ ClientSpotID = clientSpots[0].ClientSpotID, Name = "Demo Notification 6 (Scenario #3)", Timeout = 10, ContentMimeType = MimeType.None, ContentSubject = "Advertisement for ", ContentCaption = "", ContentBody = "" }
                        };
                        foreach (Notification notification in notifications)
                        {
                            configurationData.Notifications.Add(notification);
                        }
                        Context.Logger.LogLine("7 Notifications added");

                        // SAVING the changes into physical DB
                        await configurationData.SaveChangesAsync();

                        // Adding LocationDeviceNotifications
                        var displayEndpointNotifications = new DisplayEndpointNotification[]
                        {
                            new DisplayEndpointNotification{ DisplayEndpointID = displayEndpoints[0].DisplayEndpointID, NotificationID = notifications[0].NotificationID },
                            new DisplayEndpointNotification{ DisplayEndpointID = displayEndpoints[1].DisplayEndpointID, NotificationID = notifications[1].NotificationID },
                            new DisplayEndpointNotification{ DisplayEndpointID = displayEndpoints[2].DisplayEndpointID, NotificationID = notifications[2].NotificationID },
                            new DisplayEndpointNotification{ DisplayEndpointID = displayEndpoints[2].DisplayEndpointID, NotificationID = notifications[3].NotificationID },
                            new DisplayEndpointNotification{ DisplayEndpointID = displayEndpoints[2].DisplayEndpointID, NotificationID = notifications[4].NotificationID }
                        };
                        foreach (DisplayEndpointNotification displayEndpointNotification in displayEndpointNotifications)
                        {
                            configurationData.DisplayEndpointNotifications.Add(displayEndpointNotification);
                        }
                        Context.Logger.LogLine("5 DisplayEndpointNotifications added");

                        // Adding LocationDeviceNotifications
                        var locationDeviceNotifications = new LocationDeviceNotification[]
                        {
                            new LocationDeviceNotification{ LocationDeviceID = locationDevices[3].LocationDeviceID, NotificationID = notifications[5].NotificationID },
                            new LocationDeviceNotification{ LocationDeviceID = locationDevices[4].LocationDeviceID, NotificationID = notifications[6].NotificationID }
                        };
                        foreach (LocationDeviceNotification locationDeviceNotification in locationDeviceNotifications)
                        {
                            configurationData.LocationDeviceNotifications.Add(locationDeviceNotification);
                        }
                        Context.Logger.LogLine("2 LocationDeviceNotifications added");

                        // Adding Coupons
                        var coupons = new Coupon[]
                        {
                            new Coupon{ ClientSpotID = clientSpots[0].ClientSpotID, NotificationID = notifications[0].NotificationID, Name = "Pseduo Coupon", CouponCode = "00000000000", Description = "Save $0.00", DiscountCents = 0.0 },
                            new Coupon{ ClientSpotID = clientSpots[0].ClientSpotID, NotificationID = notifications[2].NotificationID, Name = "Doughnut Coupon", CouponCode = "09876543210", Description = "SAVE $1.99", DiscountCents = 199.0 },
                            new Coupon{ ClientSpotID = clientSpots[0].ClientSpotID, NotificationID = notifications[3].NotificationID, Name = "Croissant Coupon", CouponCode = "92186293264", Description = "SAVE $0.49", DiscountCents = 49.0 },
                            new Coupon{ ClientSpotID = clientSpots[0].ClientSpotID, NotificationID = notifications[4].NotificationID, Name = "Coke Coupon", CouponCode = "97294957293", Description = "SAVE $0.20", DiscountCents = 20.0 }
                        };
                        foreach (Coupon coupon in coupons)
                        {
                            configurationData.Coupons.Add(coupon);
                        }
                        Context.Logger.LogLine("4 Coupons added");

                        // Adding Users
                        var users = new User[]
                        {
                            new User{ Type = UserType.Registered, Name = "Pseduo User", Email = "pseduo.user@example.com" },
                            new User{ Type = UserType.Registered, Name = "Demo User", Email = "demo.user@example.com" },
                            new User{ Type = UserType.Guest }
                        };
                        foreach (User user in users)
                        {
                            configurationData.Users.Add(user);
                        }
                        Context.Logger.LogLine("3 Users added");

                        // SAVING the changes into physical DB
                        await configurationData.SaveChangesAsync();
                    }
                }
            }
            catch (Exception ex)
            {
                Context.Logger.LogLine("ConfigurationData ERROR: " + ex.Message);
            }
        }

        /// <summary>
        /// Seeds Transactional Data
        /// </summary>
        /// <param name="dataClientConfig"></param>
        /// <returns></returns>
        private async Task TransactionalDataDemoAsync(Config.DataClientConfig dataClientConfig)
        {
            try
            {
                using (DataClient dataClient = new DataClient(dataClientConfig))
                {
                    if (dataClient.TransactionalData != null)
                    {
                        TransactionalData transactionalData = dataClient.TransactionalData;

                        // Explicit loading. When the entity is first read, related data isn't retrieved.
                        // Example code that retrieves the related data. It impacts retrieval performance hence only use if it's needed.
                        await transactionalData.ClientUsers.Include(clientUser => clientUser.Client)
                                                            .Include(clientUser => clientUser.User).LoadAsync();
                        await transactionalData.UserCoupons.Include(userCoupon => userCoupon.User)
                                                            .Include(userCoupon => userCoupon.Coupon).LoadAsync();


                        ///////// DB seeding with demo content /////////

                        // Look for any Clients
                        if (transactionalData.ClientUsers.Any())
                        {
                            string log = "Total ClientUsers found: " + transactionalData.ClientUsers.LongCount() + " | " +
                                        "Total UserCoupons found: " + transactionalData.UserCoupons.LongCount();
                            Context.Logger.LogLine("[TransactionalData Summary]");
                            Context.Logger.LogLine(log);
                            return;   // DB has been seeded already
                        }

                        // DB save operation
                        Context.Logger.LogLine("[Adding contents for TransactionalData]");

                        // Adding ClientUsers
                        var clientUsers = new ClientUser[]
                        {
                            new ClientUser{ ClientID = 1, UserID = 1 }
                        };
                        foreach (ClientUser clientUser in clientUsers)
                        {
                            transactionalData.ClientUsers.Add(clientUser);
                        }
                        Context.Logger.LogLine("1 ClientUser added");

                        // Adding UserCoupons
                        var userCoupons = new UserCoupon[]
                        {
                            new UserCoupon{ UserID = 1, CouponID = 1 }
                        };
                        foreach (UserCoupon userCoupon in userCoupons)
                        {
                            transactionalData.UserCoupons.Add(userCoupon);
                        }
                        Context.Logger.LogLine("1 UserCoupon added");

                        // SAVING the changes into physical DB
                        await transactionalData.SaveChangesAsync();
                    }
                }
            }
            catch (Exception ex)
            {
                Context.Logger.LogLine("TransactionalData ERROR: " + ex.Message);
            }
        }

        /// <summary>
        /// Seeds Transient Data
        /// </summary>
        /// <param name="dataClientConfig"></param>
        /// <returns></returns>
        private async Task TransientDataDemoAsync(Config.DataClientConfig dataClientConfig)
        {
            try
            {
                using (DataClient dataClient = new DataClient(dataClientConfig))
                {
                    if (dataClient.TransientData != null)
                    {
                        TransientData transientData = dataClient.TransientData;


                        ///////// DB seeding with demo content /////////
                        
                        var dCL = await transientData.ObtainDisplayConcurrentListAsync();
                        var zCL = await transientData.ObtainZoneConcurrentListAsync();

                        // Look for any item (DisplaySession & ZoneSession) in DisplayConcurrentList and ZoneConcurrentList Table
                        try
                        {
                            if ((dCL?.DisplaySessions?.Any() ?? false) && (zCL?.ZoneSessions?.Any() ?? false))
                            {
                                string log1 = JsonConvert.SerializeObject(dCL);
                                string log2 = JsonConvert.SerializeObject(zCL);
                                Context.Logger.LogLine("[TransientData Summary]");
                                Context.Logger.LogLine($"Found {dCL.DisplaySessions.Count} DisplaySession(s) in DisplayConcurrentList");
                                Context.Logger.LogLine(log1);
                                Context.Logger.LogLine($"Found {zCL.ZoneSessions.Count} ZoneSession(s) in ZoneConcurrentList");
                                Context.Logger.LogLine(log2);
                                return;  // DB has been seeded already
                            }
                        }
                        catch (Exception ex)
                        {
                            Context.Logger.LogLine("TransientData scan ERROR: " + ex.Message);
                        }

                        // DB save operation
                        Context.Logger.LogLine("[Adding contents for TransientData]");

                        try
                        {
                            if ((!dCL?.DisplaySessions?.Any()) ?? false)
                            {
                                dCL.DisplaySessions.Add(new DisplaySession(1)
                                {
                                    IsUserExists = true,
                                    BufferedShowNotificationID = 1,
                                    CurrentShowNotificationExpireAt = DateTime.UtcNow.AddSeconds(10),
                                    DisplayTouchedNotificationID = 1,
                                    DisplayTouchedAt = DateTime.UtcNow.AddSeconds(5),
                                    LocationDeviceID = 1,
                                    LocationDeviceRegisteredAt = DateTime.UtcNow
                                });
                                dCL.LastFlushedAt = DateTime.UtcNow;

                                // SAVING the DisplaySession into physical DisplayConcurrentList DB Table
                                Context.Logger.LogLine("Saving a DisplaySession to DisplayConcurrentList Table");
                                await transientData.SaveDisplayConcurrentListAsync();
                            }

                            if (!zCL.ZoneSessions.Any())
                            {
                                var zoneSession = new ZoneSession(1);
                                zoneSession.UserConcurrentList.UserSessions.Add(new UserSession(1)
                                {
                                    EnteredIntoZoneAt = DateTime.UtcNow,
                                    LastSeenInZoneAt = DateTime.UtcNow,
                                    ReceivedCouponIDs = new List<long> { 1 }
                                });
                                zoneSession.UserConcurrentList.LastFlushedAt = DateTime.UtcNow;
                                zCL.ZoneSessions.Add(zoneSession);

                                // SAVING the ZoneSession into physical ZoneConcurrentList DB Table
                                Context.Logger.LogLine("Saving a ZoneSession with an UserSession to ZoneConcurrentList Table");
                                await transientData.SaveZoneConcurrentListAsync();
                            }
                        }
                        catch (Exception ex)
                        {
                            Context.Logger.LogLine("TransientData save ERROR: " + ex.Message);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Context.Logger.LogLine("TransientData ERROR: " + ex.Message);
            }
        }
    }
}
