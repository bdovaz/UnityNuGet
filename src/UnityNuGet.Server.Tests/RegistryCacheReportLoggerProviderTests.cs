using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NUnit.Framework;

namespace UnityNuGet.Server.Tests
{
    public class RegistryCacheReportLoggerProviderTests
    {
        [Test]
        public void CreateLoggerCachesLoggersByOrdinalCategoryName()
        {
            using RegistryCacheReportLoggerProvider provider = new(CreateReport());

            ILogger first = provider.CreateLogger(RegistryCache.LoggerCategoryName);
            ILogger sameCategory = provider.CreateLogger(RegistryCache.LoggerCategoryName);
            ILogger differentCase = provider.CreateLogger(RegistryCache.LoggerCategoryName.ToLowerInvariant());
            ILogger anotherCategory = provider.CreateLogger("Other");

            using (Assert.EnterMultipleScope())
            {
                Assert.That(sameCategory, Is.SameAs(first));
                Assert.That(differentCase, Is.Not.SameAs(first));
                Assert.That(anotherCategory, Is.Not.SameAs(first));
                Assert.That(anotherCategory, Is.Not.SameAs(differentCase));
            }
        }

        [Test]
        public void CreatedLoggersCaptureOnlyTheirRegistryCacheCategory()
        {
            RegistryCacheReport report = CreateReport();
            using RegistryCacheReportLoggerProvider provider = new(report);
            ILogger registryCacheLogger = provider.CreateLogger(RegistryCache.LoggerCategoryName);
            ILogger unrelatedLogger = provider.CreateLogger("Other");

            registryCacheLogger.LogInformation("Cache information");
            unrelatedLogger.LogError("Unrelated error");

            using (Assert.EnterMultipleScope())
            {
                Assert.That(report.InformationMeessages, Is.EqualTo(["Cache information"]));
                Assert.That(report.WarningMessages, Is.Empty);
                Assert.That(report.ErrorMessages, Is.Empty);
            }
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
