using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

using Amazon.Lambda.Core;
using Amazon.Lambda.TestUtilities;

using HWC_SeedDemoContentIntoDB;

namespace HWC_SeedDemoContentIntoDB.Tests
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
            var input = "Hello World!";

            // Act
            var retValue = await _function.FunctionHandlerAsync(input, _context);   // Invoke the lambda function handler

            // Assert
            Assert.Equal(input, retValue);
        }
    }
}
