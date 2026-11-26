using System;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NUnit.Framework;

namespace UnityNuGet.Server.Tests
{
    public class RegistryCacheReportTests
    {
        [Test]
        public void NewReportHasEmptyMessagesAndIsNotRunning()
        {
            RegistryCacheReport report = CreateReport();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(report.Running, Is.False);
                Assert.That(report.InformationMeessages, Is.Empty);
                Assert.That(report.WarningMessages, Is.Empty);
                Assert.That(report.ErrorMessages, Is.Empty);
                Assert.That(report.TimeRemainingForNextUpdate, Is.Null);
            }
        }

        [Test]
        public void StartSetsRunningAndClearsPreviousMessages()
        {
            RegistryCacheReport report = CreateReport();
            report.AddInformation("Previous information");
            report.AddWarning("Previous warning");
            report.AddError("Previous error");

            report.Start();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(report.Running, Is.True);
                Assert.That(report.InformationMeessages, Is.Empty);
                Assert.That(report.WarningMessages, Is.Empty);
                Assert.That(report.ErrorMessages, Is.Empty);
            }
        }

        [Test]
        public void AddMethodsStoreMessagesInTheirRespectiveCollections()
        {
            RegistryCacheReport report = CreateReport();

            report.AddInformation("Info");
            report.AddWarning("Warning");
            report.AddError("Error");

            using (Assert.EnterMultipleScope())
            {
                Assert.That(report.InformationMeessages, Is.EqualTo(["Info"]));
                Assert.That(report.WarningMessages, Is.EqualTo(["Warning"]));
                Assert.That(report.ErrorMessages, Is.EqualTo(["Error"]));
            }
        }

        [Test]
        public void LoggerProviderCapturesOnlyRegistryCacheCategoryBySeverity()
        {
            RegistryCacheReport report = CreateReport();
            using RegistryCacheReportLoggerProvider provider = new(report);
            ILogger registryCacheLogger = provider.CreateLogger(RegistryCache.LoggerCategoryName);
            ILogger unrelatedLogger = provider.CreateLogger("NuGet");

            registryCacheLogger.LogInformation("Cache information");
            registryCacheLogger.LogWarning("Cache warning");
            registryCacheLogger.LogError("Cache error");
            unrelatedLogger.LogError("NuGet detail");

            using (Assert.EnterMultipleScope())
            {
                Assert.That(report.InformationMeessages, Is.EqualTo(["Cache information"]));
                Assert.That(report.WarningMessages, Is.EqualTo(["Cache warning"]));
                Assert.That(report.ErrorMessages, Is.EqualTo(["Cache error"]));
                Assert.That(provider.CreateLogger(RegistryCache.LoggerCategoryName), Is.SameAs(registryCacheLogger));
            }
        }

        [Test]
        public void LoggerIncludesExceptionDetailsInReport()
        {
            RegistryCacheReport report = CreateReport();
            RegistryCacheReportLogger logger = new(report, RegistryCache.LoggerCategoryName);
            InvalidOperationException exception = new("Failure details");

            logger.LogError(exception, "Cache failed");

            Assert.That(report.ErrorMessages, Is.EqualTo([$"Cache failed. Reason: {exception}"]));
        }

        [Test]
        public void ProgressIsZeroWhenTotalCountIsZero()
        {
            RegistryCacheSingleton singleton = new() { ProgressPackageIndex = 0, ProgressTotalPackageCount = 0 };
            RegistryCacheReport report = CreateReport(singleton);

            Assert.That(report.Progress, Is.Zero);
        }

        [Test]
        public void ProgressIsCalculatedFromCurrentAndTotalPackageCounts()
        {
            RegistryCacheSingleton singleton = new() { ProgressPackageIndex = 3, ProgressTotalPackageCount = 8 };
            RegistryCacheReport report = CreateReport(singleton);

            Assert.That(report.Progress, Is.EqualTo(37.5));
        }

        [Test]
        public void CompleteStopsReportAndSetsExactTimeUntilNextUpdate()
        {
            FakeTimeProvider timeProvider = new(new DateTimeOffset(2026, 11, 24, 12, 0, 0, TimeSpan.Zero));
            RegistryOptions options = new() { UpdateInterval = TimeSpan.FromMinutes(10) };
            RegistryCacheReport report = CreateReport(registryOptions: options, timeProvider: timeProvider);
            report.Start();

            report.Complete();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(report.Running, Is.False);
                Assert.That(report.TimeRemainingForNextUpdate, Is.EqualTo(TimeSpan.FromMinutes(10)));
            }
        }

        [Test]
        public void TimeUntilNextUpdateDecreasesAsTimeAdvances()
        {
            FakeTimeProvider timeProvider = new(new DateTimeOffset(2026, 11, 24, 12, 0, 0, TimeSpan.Zero));
            RegistryOptions options = new() { UpdateInterval = TimeSpan.FromMinutes(10) };
            RegistryCacheReport report = CreateReport(registryOptions: options, timeProvider: timeProvider);

            report.Complete();
            timeProvider.Advance(TimeSpan.FromMinutes(3));

            Assert.That(report.TimeRemainingForNextUpdate, Is.EqualTo(TimeSpan.FromMinutes(7)));
        }

        [Test]
        public void TimeUntilNextUpdateIsZeroWhenThereAreErrors()
        {
            RegistryCacheReport report = CreateReport(registryOptions: new RegistryOptions { UpdateInterval = TimeSpan.FromHours(1) });
            report.Complete();
            report.AddError("Build failed");

            Assert.That(report.TimeRemainingForNextUpdate, Is.EqualTo(TimeSpan.Zero));
        }

        private static RegistryCacheReport CreateReport(
            RegistryCacheSingleton? singleton = null,
            RegistryOptions? registryOptions = null,
            TimeProvider? timeProvider = null)
        {
            return new RegistryCacheReport(
                singleton ?? new RegistryCacheSingleton(),
                Options.Create(registryOptions ?? new RegistryOptions()),
                timeProvider ?? new FakeTimeProvider());
        }
    }
}
