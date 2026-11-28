using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using NUnit.Framework;

namespace UnityNuGet.Tool.Tests
{
    [NonParallelizable]
    public class RegistryCacheUpdaterTests
    {
        private string? _originalNuGetPackages;

        [SetUp]
        public void SetUp()
        {
            _originalNuGetPackages = Environment.GetEnvironmentVariable("NUGET_PACKAGES");
        }

        [TearDown]
        public void TearDown()
        {
            Environment.SetEnvironmentVariable("NUGET_PACKAGES", _originalNuGetPackages);
        }

        [Test]
        public async Task ExecuteLogsConstructionFailureWhenPackageFolderIsMissing()
        {
            using RegistryCacheUpdaterContext context = await CreateContext(setPackageFolder: false);

            bool success = await context.Updater.Execute(CancellationToken.None);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(success, Is.False);
                Assert.That(context.Logger.LatestRecord.Level, Is.EqualTo(LogLevel.Error));
                Assert.That(context.Logger.LatestRecord.Message, Is.EqualTo("Error while building a new registry cache"));
                Assert.That(context.Logger.LatestRecord.Exception, Is.TypeOf<ArgumentNullException>());
                Assert.That(((ArgumentNullException)context.Logger.LatestRecord.Exception!).ParamName, Is.EqualTo("path"));
            }
        }

        [Test]
        public async Task ExecuteCompletesSuccessfullyAndCopiesProgressFromRealRegistryBuild()
        {
            using RegistryCacheUpdaterContext context = await CreateContext(registryJson: """{"IgnoredPackage":{"ignore":true}}""");
            bool success = await context.Updater.Execute(CancellationToken.None);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(success, Is.True);
                Assert.That(context.Singleton.ProgressPackageIndex, Is.EqualTo(1));
                Assert.That(context.Singleton.ProgressTotalPackageCount, Is.EqualTo(1));
                Assert.That(context.Logger.Collector.GetSnapshot().Select(record => record.Message), Does.Contain("Starting to update RegistryCache"));
                Assert.That(File.Exists(Path.Combine(context.Options.RootPersistentFolder!, "version.txt")), Is.True);
            }

        }

        [Test]
        public async Task ExecuteReturnsFailureWhenRealBuildReportsAnError()
        {
            using RegistryCacheUpdaterContext context = await CreateContext(filter: "[");

            bool success = await context.Updater.Execute(CancellationToken.None);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(success, Is.False);
                Assert.That(context.RegistryCacheLogger.Collector.GetSnapshot().Any(record =>
                    record.Level == LogLevel.Error
                    && record.Exception is ArgumentException exception
                    && exception.Message.Contains("Invalid pattern", StringComparison.Ordinal)), Is.True);
            }
        }

        private static async Task<RegistryCacheUpdaterContext> CreateContext(
            string registryJson = "{}",
            string? filter = null,
            bool setPackageFolder = true)
        {
            string workingFolder = Path.Combine(Path.GetTempPath(), "UnityNuGet.Tool.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(workingFolder);
            string registryFilePath = Path.Combine(workingFolder, "registry.json");
            await File.WriteAllTextAsync(registryFilePath, registryJson);
            RegistryOptions options = new()
            {
                RootHttpUrl = new Uri("https://packages.example.test"),
                UnityScope = "org.nuget",
                MinimumUnityVersion = "2019.1",
                PackageNameNuGetPostFix = "",
                PackageKeywords = [],
                RegistryFilePath = registryFilePath,
                RootPersistentFolder = Path.Combine(workingFolder, "cache"),
                TargetFrameworks = [],
                RoslynAnalyzerVersions = [],
                Filter = filter
            };

            FakeLogger<RegistryCacheUpdater> logger = new();
            FakeLogger<RegistryCache> registryCacheLogger = new();
            Registry registry = new(NullLoggerFactory.Instance, Options.Create(options));
            await registry.StartAsync(CancellationToken.None);
            UnityPackageSigner signer = new(new FakeLogger<UnityPackageSigner>(), Options.Create(new SigningOptions()));
            RegistryCacheSingleton singleton = new()
            {
                UnityPackageFolder = setPackageFolder ? options.RootPersistentFolder : null,
                ServerUri = options.RootHttpUrl,
                NuGetRedirectLogger = new NuGetRedirectLogger(logger)
            };
            RegistryCacheUpdater updater = new(
                registry,
                singleton,
                logger,
                registryCacheLogger,
                Options.Create(options),
                signer);

            return new RegistryCacheUpdaterContext(updater, logger, registryCacheLogger, singleton, workingFolder, options);
        }

        private sealed class RegistryCacheUpdaterContext(
            RegistryCacheUpdater updater,
            FakeLogger<RegistryCacheUpdater> logger,
            FakeLogger<RegistryCache> registryCacheLogger,
            RegistryCacheSingleton singleton,
            string workingFolder,
            RegistryOptions options) : IDisposable
        {
            public RegistryCacheUpdater Updater { get; } = updater;
            public FakeLogger<RegistryCacheUpdater> Logger { get; } = logger;
            public FakeLogger<RegistryCache> RegistryCacheLogger { get; } = registryCacheLogger;
            public RegistryCacheSingleton Singleton { get; } = singleton;
            public RegistryOptions Options { get; } = options;

            public void Dispose()
            {
                if (Directory.Exists(workingFolder))
                {
                    Directory.Delete(workingFolder, recursive: true);
                }
            }
        }
    }
}
