using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

using HWC.Core;

using Amazon.Lambda.Core;
using Amazon.Lambda.TestUtilities;
using Amazon.Lambda.APIGatewayEvents;

using Newtonsoft.Json;

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
            long userID = 1;
            string deviceID = "pseudo-uuid";
            Location location = new Location(LocationDeviceType.IBeacon, deviceID);

            string userIdRequestPathName = "user-id";
            APIGatewayProxyRequest request = new APIGatewayProxyRequest()
            {
                PathParameters = new Dictionary<string, string>() { { userIdRequestPathName, userID.ToString() } },
                Body = JsonConvert.SerializeObject(location)
            };

            // Act
            var retValue = await _function.FunctionHandlerAsync(request, _context);   // Invoke the lambda function handler

            // Assert
            // Nothing to assert for now
        }
    }
}
