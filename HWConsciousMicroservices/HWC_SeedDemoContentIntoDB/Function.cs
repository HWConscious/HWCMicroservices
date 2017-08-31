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
        public ILambdaContext Context = null;

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
                        await configurationData.DisplayEndpoints.Include(displayEndpoint => displayEndpoint.Notifications).LoadAsync();
                        await configurationData.Notifications.Include(notification => notification.Coupons).LoadAsync();


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
                            new ClientSpot{ ClientID = 1, Name = "DemoMart Seattle Store", PhoneNumber = "001-123-4567", Address = "Seattle, US" },
                            new ClientSpot{ ClientID = 1, Name = "DemoMart LA Store", PhoneNumber = "001-123-4567", Address = "LA, US" }
                        };
                        foreach (ClientSpot clientSpot in clientSpots)
                        {
                            configurationData.ClientSpots.Add(clientSpot);
                        }
                        Context.Logger.LogLine("2 ClientSpots added");

                        // Adding Zones
                        var zones = new Zone[]
                        {
                            new Zone{ ClientSpotID = 1, Name = "Pseudo Zone" },
                            new Zone{ ClientSpotID = 1, Name = "Demo Bakery Zone" }
                        };
                        foreach (Zone zone in zones)
                        {
                            configurationData.Zones.Add(zone);
                        }
                        Context.Logger.LogLine("2 Zones added");

                        // SAVING the changes into physical DB
                        await configurationData.SaveChangesAsync();

                        // Adding LocationDevices
                        var locationDevices = new LocationDevice[]
                        {
                            new LocationDevice{ ClientSpotID = 1, ZoneID = 1, Type = LocationDeviceType.IBeacon, DeviceID = "pseudo-uuid" },
                            new LocationDevice{ ClientSpotID = 1, ZoneID = 2, Type = LocationDeviceType.IBeacon, DeviceID = "5bf0e89a-5760-4a9d-bb8a-7a895c9c99b2" },
                            new LocationDevice{ ClientSpotID = 1, ZoneID = 2, Type = LocationDeviceType.IBeacon, DeviceID = "12345678-1234-1234-1234-123456789012" }
                        };
                        foreach (LocationDevice locationDevice in locationDevices)
                        {
                            configurationData.LocationDevices.Add(locationDevice);
                        }
                        Context.Logger.LogLine("3 LocationDevices added");

                        // Adding DisplayEndpoints
                        var displayEndpoints = new DisplayEndpoint[]
                        {
                            new DisplayEndpoint{ ClientSpotID = 1, ZoneID = 1, Name = "Pseudo Display" },
                            new DisplayEndpoint{ ClientSpotID = 1, ZoneID = 2, Name = "Demo Display 1" }
                        };
                        foreach (DisplayEndpoint displayEndpoint in displayEndpoints)
                        {
                            configurationData.DisplayEndpoints.Add(displayEndpoint);
                        }
                        Context.Logger.LogLine("2 DisplayEndpoints added");

                        // Adding Notifications
                        var notifications = new Notification[]
                        {
                            new Notification{ ClientSpotID = 1, DisplayEndpointID = 1, Name = "Pseduo Notification", SortOrder = 1, Timeout = 10, Active = true, ContentMimeType = MimeType.ImagePng, ContentSubject = "Pseduo Notification", ContentCaption = "Pseduo advertisement", ContentBody = "http://www.abc.com/images/img1.png" },
                            new Notification{ ClientSpotID = 1, DisplayEndpointID = 2, Name = "Demo Notification 1", SortOrder = 1, Timeout = 10, Active = true, ContentMimeType = MimeType.ImageJpeg, ContentSubject = "Advertisement for Doughnut", ContentCaption = "4 Delicious Doughnuts", ContentBody = "https://static.pexels.com/photos/273773/pexels-photo-273773.jpeg" },
                            new Notification{ ClientSpotID = 1, DisplayEndpointID = 2, Name = "Demo Notification 2", SortOrder = 2, Timeout = 10, Active = true, ContentMimeType = MimeType.ImageJpeg, ContentSubject = "Advertisement for Croissant", ContentCaption = "Croissant for breakfast needs", ContentBody = "https://static.pexels.com/photos/41298/background-bakery-breakfast-bun-41298.jpeg" }
                        };
                        foreach (Notification notification in notifications)
                        {
                            configurationData.Notifications.Add(notification);
                        }
                        Context.Logger.LogLine("3 Notifications added");

                        // SAVING the changes into physical DB
                        await configurationData.SaveChangesAsync();

                        // Adding Coupons
                        var coupons = new Coupon[]
                        {
                            new Coupon{ ClientSpotID = 1, NotificationID = 1, Name = "Pseduo Coupon", CouponCode = "00000000000", Description = "Save $0.00", DiscountCents = 0.0 },
                            new Coupon{ ClientSpotID = 1, NotificationID = 2, Name = "Demo Doughnut Coupon", CouponCode = "09876543210", Description = "SAVE $1.99", DiscountCents = 199.0 },
                            new Coupon{ ClientSpotID = 1, NotificationID = 3, Name = "Demo Croissant Coupon", CouponCode = "09876543211", Description = "SAVE $0.49", DiscountCents = 49.0 }
                        };
                        foreach (Coupon coupon in coupons)
                        {
                            configurationData.Coupons.Add(coupon);
                        }
                        Context.Logger.LogLine("3 Coupons added");

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
                            new ClientUser{ ClientID = 1, UserID = 1, VisitedAt = DateTime.UtcNow }
                        };
                        foreach (ClientUser clientUser in clientUsers)
                        {
                            transactionalData.ClientUsers.Add(clientUser);
                        }
                        Context.Logger.LogLine("1 ClientUser added");

                        // Adding UserCoupons
                        var userCoupons = new UserCoupon[]
                        {
                            new UserCoupon{ UserID = 1, CouponID = 1, ReceivedAt = DateTime.UtcNow, CouponRedempted = false }
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

                        List<DisplayConcurrentList> dClItems = null;
                        List<ZoneConcurrentList> zClItems = null;

                        // Look for any item in DisplayConcurrentList and ZoneConcurrentList Table
                        try
                        {
                            dClItems = await transientData.ScanAsync<DisplayConcurrentList>(null).GetNextSetAsync();
                            zClItems = await transientData.ScanAsync<ZoneConcurrentList>(null).GetNextSetAsync();
                            if (dClItems.Any() && zClItems.Any())
                            {
                                string log1 = JsonConvert.SerializeObject(dClItems);
                                string log2 = JsonConvert.SerializeObject(zClItems);
                                Context.Logger.LogLine("[TransientData Summary]");
                                Context.Logger.LogLine($"Found {dClItems.Count} items in DisplayConcurrentList Table");
                                Context.Logger.LogLine(log1);
                                Context.Logger.LogLine($"Found {zClItems.Count} items in ZoneConcurrentList Table");
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
                            if (!dClItems.Any())
                            {
                                DisplayConcurrentList dClItem = new DisplayConcurrentList()
                                {
                                    ID = Guid.NewGuid(),
                                    DisplaySessions = new List<DisplaySession>()
                                    {
                                        new DisplaySession()
                                        {
                                            DisplayEndpointID = 1,
                                            IsUserExists = true,
                                            BufferedShowNotificationID = 1,
                                            CurrentShowNotificationExpireAt = DateTime.UtcNow.AddSeconds(10),
                                            DisplayTouchedNotificationID = 1,
                                            DisplayTouchedAt = DateTime.UtcNow.AddSeconds(5)
                                        }
                                    },
                                    LastFlushedAt = DateTime.UtcNow
                                };

                                // SAVING the item into physical DisplayConcurrentList DB Table
                                Context.Logger.LogLine("Saving an item to DisplayConcurrentList Table");
                                await transientData.SaveAsync<DisplayConcurrentList>(dClItem);
                            }

                            if (!zClItems.Any())
                            {
                                ZoneConcurrentList zClItem = new ZoneConcurrentList()
                                {
                                    ID = Guid.NewGuid(),
                                    ZoneSessions = new List<ZoneSession>()
                                    {
                                        new ZoneSession()
                                        {
                                            ZoneID = 1,
                                            UserConcurrentList = new UserConcurrentList()
                                            {
                                                LastFlushedAt = DateTime.UtcNow,
                                                UserSessions = new List<UserSession>()
                                                {
                                                    new UserSession()
                                                    {
                                                        UserID = 1,
                                                        EnteredIntoZoneAt = DateTime.UtcNow,
                                                        LastSeenInZoneAt = DateTime.UtcNow,
                                                        ReceivedCouponIDs = new List<long>() { }
                                                    }
                                                }
                                            }
                                        }
                                    }
                                };

                                // SAVING the item into physical ZoneConcurrentList DB Table
                                Context.Logger.LogLine("Saving an item to ZoneConcurrentList Table");
                                await transientData.SaveAsync<ZoneConcurrentList>(zClItem);
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
