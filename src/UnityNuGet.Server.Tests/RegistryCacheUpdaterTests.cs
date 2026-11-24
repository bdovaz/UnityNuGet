using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using UnityNuGet.Server;

namespace UnityNuGet.Server.Tests
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
        public async Task StartUpdatesCacheWhenBuildCompletesWithoutErrors()
        {
            using RegistryCacheUpdaterContext context = await CreateContext();

            await context.Updater.StartAsync(CancellationToken.None);
            await WaitUntil(() => context.Singleton.Instance is not null);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(context.Report.Running, Is.False);
                Assert.That(context.Report.ErrorMessages, Is.Empty);
                Assert.That(context.Singleton.Instance, Is.Not.Null);
                Assert.That(context.LoggerMessages, Does.Contain("RegistryCache successfully updated"));
            }

            await context.Updater.StopAsync(CancellationToken.None);
        }

        [Test]
        public async Task StartDoesNotUpdateCacheWhenBuildReportsErrors()
        {
            using RegistryCacheUpdaterContext context = await CreateContext(filter: "[");
            RegistryCache originalInstance = CreatePlaceholderCache(context.Options);
            context.Singleton.Instance = originalInstance;

            await context.Updater.StartAsync(CancellationToken.None);
            await WaitUntil(() => !context.Report.Running && context.Report.ErrorMessages.Any());

            using (Assert.EnterMultipleScope())
            {
                Assert.That(context.Singleton.Instance, Is.SameAs(originalInstance));
                Assert.That(context.Report.ErrorMessages, Is.Not.Empty);
                Assert.That(context.LoggerMessages.Any(message => message.Contains("RegistryCache not updated due to errors", StringComparison.Ordinal)), Is.True);
            }

            await context.Updater.StopAsync(CancellationToken.None);
        }

        [Test]
        public async Task StartServesPartialCacheWhenOnlyErrorsAreMissingDependenciesAndOptionIsEnabled()
        {
            using RegistryCacheUpdaterContext context = await CreateContext(
                registryPath: Path.Combine(AppContext.BaseDirectory, "registry.test.missing-dep.json"),
                filter: "Serilog.Sinks.File",
                allowServingWithMissingDependencies: true);

            await context.Updater.StartAsync(CancellationToken.None);
            await WaitUntil(() => !context.Report.Running && context.Report.ErrorMessages.Any());

            using (Assert.EnterMultipleScope())
            {
                Assert.That(context.Report.ErrorMessages.Any(), Is.True);
                Assert.That(context.Singleton.Instance, Is.Not.Null);
                Assert.That(context.LoggerMessages.Any(message => message.Contains("Serving partial results", StringComparison.Ordinal)), Is.True);
            }

            await context.Updater.StopAsync(CancellationToken.None);
        }

        [Test]
        public async Task StartReportsUnexpectedConstructionFailure()
        {
            using RegistryCacheUpdaterContext context = await CreateContext(setPackageFolder: false);

            await context.Updater.StartAsync(CancellationToken.None);
            await context.Updater.ExecuteTask!;

            using (Assert.EnterMultipleScope())
            {
                Assert.That(context.Report.Running, Is.False);
                Assert.That(context.Report.ErrorMessages.Single(), Does.Contain("Error while building a new registry cache"));
                Assert.That(context.Report.ErrorMessages.Single(), Does.Contain("rootPersistentFolder"));
                Assert.That(context.Singleton.Instance, Is.Null);
                Assert.That(context.LoggerMessages.Any(message => message.Contains("Error while building a new registry cache", StringComparison.Ordinal)), Is.True);
            }
        }

        [Test]
        public async Task StopLogsCancellationAndCompletesReport()
        {
            using RegistryCacheUpdaterContext context = await CreateContext();

            await context.Updater.StartAsync(CancellationToken.None);
            await WaitUntil(() => context.Singleton.Instance is not null);
            await context.Updater.StopAsync(CancellationToken.None);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(context.Report.Running, Is.False);
                Assert.That(context.Report.InformationMeessages, Does.Contain("RegistryCache update canceled."));
                Assert.That(context.LoggerMessages, Does.Contain("RegistryCache update canceled"));
            }
        }

        private static async Task<RegistryCacheUpdaterContext> CreateContext(
            string? registryPath = null,
            string? filter = null,
            bool allowServingWithMissingDependencies = false,
            bool setPackageFolder = true)
        {
            string workingFolder = Path.Combine(Path.GetTempPath(), "UnityNuGet.Server.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(workingFolder);
            string registryFilePath = registryPath ?? Path.Combine(workingFolder, "registry.json");
            if (registryPath is null)
            {
                await File.WriteAllTextAsync(registryFilePath, "{}");
            }

            RegistryOptions options = new()
            {
                RootHttpUrl = new Uri("https://packages.example.test"),
                UnityScope = "org.nuget",
                MinimumUnityVersion = "2019.1",
                PackageNameNuGetPostFix = "",
                PackageKeywords = [],
                RegistryFilePath = registryFilePath,
                RootPersistentFolder = Path.Combine(workingFolder, "cache"),
                UpdateInterval = TimeSpan.FromHours(1),
                TargetFrameworks = [new RegistryTargetFramework { Name = "netstandard2.0", DefineConstraints = [] }],
                RoslynAnalyzerVersions = [],
                Filter = filter,
                AllowServingWithMissingDependencies = allowServingWithMissingDependencies
            };

            FakeLogger<RegistryCacheUpdater> logger = new();
            Registry registry = new(NullLoggerFactory.Instance, Options.Create(options));
            await registry.StartAsync(CancellationToken.None);
            RegistryCacheSingleton singleton = new()
            {
                UnityPackageFolder = setPackageFolder ? options.RootPersistentFolder : null,
                ServerUri = options.RootHttpUrl,
                NuGetRedirectLogger = new NuGetRedirectLogger(logger)
            };
            RegistryCacheReport report = new(singleton, Options.Create(options), TimeProvider.System);
            UnityPackageSigner signer = new(NullLogger<UnityPackageSigner>.Instance, Options.Create(new SigningOptions()));
            RegistryCacheUpdater updater = new(
                registry,
                report,
                singleton,
                logger,
                Options.Create(options),
                signer);

            return new RegistryCacheUpdaterContext(updater, logger, singleton, report, options, workingFolder);
        }

        private static RegistryCache CreatePlaceholderCache(RegistryOptions options)
        {
            return new RegistryCache(
                new Registry(NullLoggerFactory.Instance, Options.Create(options)),
                options.RootPersistentFolder!,
                options.RootHttpUrl!,
                options.UnityScope!,
                options.MinimumUnityVersion!,
                options.PackageNameNuGetPostFix!,
                options.PackageKeywords,
                options.TargetFrameworks!,
                options.RoslynAnalyzerVersions!,
                new NuGet.Common.NullLogger(),
                new UnityPackageSigner(NullLogger<UnityPackageSigner>.Instance, Options.Create(new SigningOptions())));
        }

        private static async Task WaitUntil(Func<bool> predicate)
        {
            using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(30));
            while (!predicate())
            {
                await Task.Delay(10, timeout.Token);
            }
        }

        private sealed class RegistryCacheUpdaterContext(
            RegistryCacheUpdater updater,
            FakeLogger<RegistryCacheUpdater> logger,
            RegistryCacheSingleton singleton,
            RegistryCacheReport report,
            RegistryOptions options,
            string workingFolder) : IDisposable
        {
            public RegistryCacheUpdater Updater { get; } = updater;
            public FakeLogger<RegistryCacheUpdater> Logger { get; } = logger;
            public RegistryCacheSingleton Singleton { get; } = singleton;
            public RegistryCacheReport Report { get; } = report;
            public RegistryOptions Options { get; } = options;
            public IEnumerable<string> LoggerMessages => Logger.Collector.GetSnapshot().Select(record => record.Message);

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
