using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

using Amazon.Lambda.Core;
using Amazon.Lambda.TestUtilities;
using Amazon.Lambda.APIGatewayEvents;

using HWC_GetDisplayEndpointNotifications;

namespace HWC_GetDisplayEndpointNotifications.Tests
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
            long displayEndpointID = 1;

            string displayEndpointIdRequestPathName = "display-endpoint-id";
            APIGatewayProxyRequest request = new APIGatewayProxyRequest()
            {
                PathParameters = new Dictionary<string, string>() { { displayEndpointIdRequestPathName, displayEndpointID.ToString() } }
            };

            // Act
            var retValue = await _function.FunctionHandlerAsync(request, _context);   // Invoke the lambda function handler

            // Assert
            // Nothing to assert for now
        }
    }
}
