using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;

namespace UnityNuGet.Tests
{
    public class LoggerFactoryExtensionsTests
    {
        [Test]
        public void CreateNuGetLogger_CreatesLoggerWithNuGetCategory()
        {
            Mock<ILogger> logger = new();
            Mock<ILoggerFactory> loggerFactory = new();
            loggerFactory
                .Setup(factory => factory.CreateLogger("NuGet"))
                .Returns(logger.Object);

            ILogger result = loggerFactory.Object.CreateNuGetLogger();

            Assert.That(result, Is.SameAs(logger.Object));
            loggerFactory.Verify(factory => factory.CreateLogger("NuGet"), Times.Once);
        }
    }
}
