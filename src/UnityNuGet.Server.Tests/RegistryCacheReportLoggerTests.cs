using System;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NUnit.Framework;

namespace UnityNuGet.Server.Tests
{
    public class RegistryCacheReportLoggerTests
    {
        [TestCase(LogLevel.Trace, false)]
        [TestCase(LogLevel.Debug, false)]
        [TestCase(LogLevel.Information, true)]
        [TestCase(LogLevel.Warning, true)]
        [TestCase(LogLevel.Error, true)]
        [TestCase(LogLevel.Critical, true)]
        [TestCase(LogLevel.None, false)]
        public void IsEnabledUsesInformationThreshold(LogLevel logLevel, bool expected)
        {
            RegistryCacheReportLogger logger = CreateLogger(RegistryCache.LoggerCategoryName);

            Assert.That(logger.IsEnabled(logLevel), Is.EqualTo(expected));
        }

        [Test]
        public void IsEnabledIsFalseForOtherCategories()
        {
            RegistryCacheReportLogger logger = CreateLogger("Other");

            Assert.That(logger.IsEnabled(LogLevel.Critical), Is.False);
        }

        [Test]
        public void LogStoresMessagesBySeverity()
        {
            RegistryCacheReport report = CreateReport();
            RegistryCacheReportLogger logger = new(report, RegistryCache.LoggerCategoryName);

            logger.LogInformation("Information message");
            logger.LogWarning("Warning message");
            logger.LogError("Error message");
            logger.LogCritical("Critical message");

            using (Assert.EnterMultipleScope())
            {
                Assert.That(report.InformationMeessages, Is.EqualTo(["Information message"]));
                Assert.That(report.WarningMessages, Is.EqualTo(["Warning message"]));
                Assert.That(report.ErrorMessages, Is.EqualTo(["Error message", "Critical message"]));
            }
        }

        [Test]
        public void LogDoesNotFormatOrStoreDisabledMessages()
        {
            RegistryCacheReport report = CreateReport();
            RegistryCacheReportLogger registryLogger = new(report, RegistryCache.LoggerCategoryName);
            RegistryCacheReportLogger unrelatedLogger = new(report, "Other");
            bool formatterCalled = false;

            foreach (LogLevel logLevel in new[] { LogLevel.Trace, LogLevel.Debug, LogLevel.None })
            {
                registryLogger.Log(logLevel, default, "Message", null, (state, _) =>
                {
                    formatterCalled = true;
                    return state;
                });
            }

            unrelatedLogger.Log(LogLevel.Error, default, "Message", null, (state, _) =>
            {
                formatterCalled = true;
                return state;
            });

            using (Assert.EnterMultipleScope())
            {
                Assert.That(formatterCalled, Is.False);
                Assert.That(report.InformationMeessages, Is.Empty);
                Assert.That(report.WarningMessages, Is.Empty);
                Assert.That(report.ErrorMessages, Is.Empty);
            }
        }

        [Test]
        public void LogAppendsExceptionDetailsToMessage()
        {
            RegistryCacheReport report = CreateReport();
            RegistryCacheReportLogger logger = new(report, RegistryCache.LoggerCategoryName);
            InvalidOperationException exception = new("Failure details");

            logger.LogError(exception, "Cache failed");

            Assert.That(report.ErrorMessages, Is.EqualTo([$"Cache failed. Reason: {exception}"]));
        }

        [Test]
        public void BeginScopeReturnsNull()
        {
            RegistryCacheReportLogger logger = CreateLogger(RegistryCache.LoggerCategoryName);

            Assert.That(logger.BeginScope("scope"), Is.Null);
        }

        private static RegistryCacheReportLogger CreateLogger(string categoryName)
        {
            return new RegistryCacheReportLogger(CreateReport(), categoryName);
        }

        private static RegistryCacheReport CreateReport()
        {
            return new RegistryCacheReport(
                new RegistryCacheSingleton(),
                Options.Create(new RegistryOptions()),
                new FakeTimeProvider());
        }
    }
}
