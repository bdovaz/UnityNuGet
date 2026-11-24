using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Testing;
using NuGet.Common;
using NUnit.Framework;
using MsLogLevel = Microsoft.Extensions.Logging.LogLevel;
using NuGetLogLevel = NuGet.Common.LogLevel;

namespace UnityNuGet.Tests
{
    public class NuGetRedirectLoggerTests
    {
        [Test]
        [TestCase(NuGetLogLevel.Debug, MsLogLevel.Debug)]
        [TestCase(NuGetLogLevel.Verbose, MsLogLevel.Trace)]
        [TestCase(NuGetLogLevel.Information, MsLogLevel.Information)]
        [TestCase(NuGetLogLevel.Minimal, MsLogLevel.Information)]
        [TestCase(NuGetLogLevel.Warning, MsLogLevel.Warning)]
        [TestCase(NuGetLogLevel.Error, MsLogLevel.Error)]
        public void LogMapsNuGetLevelAndMessage(NuGetLogLevel nuGetLevel, MsLogLevel expectedLevel)
        {
            FakeLogger<NuGetRedirectLoggerTests> logger = new();
            NuGetRedirectLogger redirectLogger = new(logger);

            redirectLogger.Log(new LogMessage(nuGetLevel, "Test message"));

            using (Assert.EnterMultipleScope())
            {
                Assert.That(redirectLogger.Logger, Is.SameAs(logger));
                Assert.That(logger.Collector.Count, Is.EqualTo(1));
                Assert.That(logger.LatestRecord.Level, Is.EqualTo(expectedLevel));
                Assert.That(logger.LatestRecord.Message, Is.EqualTo("Test message"));
            }
        }

        [Test]
        public async Task LogAsyncReturnsCompletedTaskAndLogsMessage()
        {
            FakeLogger<NuGetRedirectLoggerTests> logger = new();
            NuGetRedirectLogger redirectLogger = new(logger);

            Task task = redirectLogger.LogAsync(new LogMessage(NuGetLogLevel.Warning, "Async message"));

            Assert.That(task.IsCompleted, Is.True);

            await task;

            using (Assert.EnterMultipleScope())
            {
                Assert.That(logger.Collector.Count, Is.EqualTo(1));
                Assert.That(logger.LatestRecord.Level, Is.EqualTo(MsLogLevel.Warning));
                Assert.That(logger.LatestRecord.Message, Is.EqualTo("Async message"));
            }
        }

        [Test]
        public void LogDoesNotWriteWhenMicrosoftLoggerIsDisabled()
        {
            FakeLogger<NuGetRedirectLoggerTests> logger = new();

            logger.ControlLevel(MsLogLevel.Error, enabled: false);

            NuGetRedirectLogger redirectLogger = new(logger);

            redirectLogger.Log(new LogMessage(NuGetLogLevel.Error, "Disabled message"));

            Assert.That(logger.Collector.Count, Is.Zero);
        }

        [Test]
        public void LogThrowsForUnknownNuGetLevel()
        {
            FakeLogger<NuGetRedirectLoggerTests> logger = new();
            NuGetRedirectLogger redirectLogger = new(logger);

            Assert.That(
                () => redirectLogger.Log(new LogMessage((NuGetLogLevel)int.MaxValue, "Unknown")),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }
    }
}
