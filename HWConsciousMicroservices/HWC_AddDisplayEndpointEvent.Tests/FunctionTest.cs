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

using HWC_AddDisplayEndpointEvent;

namespace HWC_AddDisplayEndpointEvent.Tests
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
            long notificationID = 2;
            Event e = new Event(EventType.DisplayEndpoint_Touch, DateTime.UtcNow, EventSourceType.Notification, notificationID);

            string displayEndpointIdRequestPathName = "display-endpoint-id";
            APIGatewayProxyRequest request = new APIGatewayProxyRequest()
            {
                PathParameters = new Dictionary<string, string>() { { displayEndpointIdRequestPathName, displayEndpointID.ToString() } },
                Body = JsonConvert.SerializeObject(e)
            };

            // Act
            var retValue = await _function.FunctionHandlerAsync(request, _context);   // Invoke the lambda function handler

            // Assert
            // Nothing to assert for now
        }
    }
}
