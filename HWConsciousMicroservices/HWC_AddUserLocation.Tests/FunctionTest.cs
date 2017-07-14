using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

using Amazon.Lambda.Core;
using Amazon.Lambda.TestUtilities;
using Amazon.Lambda.APIGatewayEvents;

using HWC_AddUserLocation;

namespace HWC_AddUserLocation.Tests
{
    public class FunctionTest
    {
        private readonly Function _function;
        private readonly TestLambdaContext _context;

        public FunctionTest()
        {
            _function = new Function();
            _context = new TestLambdaContext();
        }

        [Fact]
        public async void TestFunctionFlowAsync()
        {
            // Arrange
            string userId = "1";
            string locationSerialzed = "{ \"Type\": \"IBeacon\", \"DeviceID\": \"0e8cedd0-ad98-11e6\" }";    // Associated with ZoneID:1
            //string locationSerialzed = "{ \"Type\": \"IBeacon\", \"DeviceID\": \"4f6cegh4-34fg-90d7\" }";    // Associated with ZoneID:2

            APIGatewayProxyRequest request = new APIGatewayProxyRequest()
            {
                PathParameters = new Dictionary<string, string>() { { "user-id", userId } },
                Body = locationSerialzed
            };

            // Act
            var retValue = await _function.FunctionHandlerAsync(request, _context);   // Invoke the lambda function handler

            // Assert
            // Nothing to assert for now
        }
    }
}
