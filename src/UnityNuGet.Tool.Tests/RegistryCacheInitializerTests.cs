using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;

namespace UnityNuGet.Tool.Tests
{
    public class RegistryCacheInitializerTests
    {
        [Test]
        public async Task StartUsesRootPersistentFolderWhenRegistryPathIsRooted()
        {
            string rootPersistentFolder = Path.Combine(Path.GetTempPath(), "UnityNuGet.Tests", Guid.NewGuid().ToString("N"));
            Uri serverUri = new("https://packages.example.test");
            RegistryOptions options = new()
            {
                RegistryFilePath = Path.Combine(Path.GetTempPath(), "registry.json"),
                RootPersistentFolder = rootPersistentFolder,
                RootHttpUrl = serverUri
            };

            RegistryCacheInitializerContext context = CreateContext(options);

            await context.Initializer.StartAsync(CancellationToken.None);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(context.Singleton.UnityPackageFolder, Is.EqualTo(rootPersistentFolder));
                Assert.That(context.Singleton.ServerUri, Is.SameAs(serverUri));
                Assert.That(context.Singleton.NuGetRedirectLogger, Is.Not.Null);
                Assert.That(context.Singleton.NuGetRedirectLogger!.Logger, Is.SameAs(context.Logger));
                Assert.That(context.Logger.LatestRecord.Level, Is.EqualTo(LogLevel.Information));
                Assert.That(context.Logger.LatestRecord.Message, Does.Contain(rootPersistentFolder));
            }
        }

        [Test]
        public async Task StartPrependsApplicationDirectoryInDevelopment()
        {
            RegistryOptions options = new()
            {
                RegistryFilePath = "registry.json",
                RootPersistentFolder = "cache",
                RootHttpUrl = new Uri("https://packages.example.test")
            };

            RegistryCacheInitializerContext context = CreateContext(options, Environments.Development);

            await context.Initializer.StartAsync(CancellationToken.None);

            string expected = Path.Combine(Path.GetDirectoryName(AppContext.BaseDirectory)!, "cache");

            Assert.That(context.Singleton.UnityPackageFolder, Is.EqualTo(expected));
        }

        [Test]
        public async Task StartPrependsCurrentDirectoryOutsideDevelopment()
        {
            RegistryOptions options = new()
            {
                RegistryFilePath = "registry.json",
                RootPersistentFolder = "cache",
                RootHttpUrl = new Uri("https://packages.example.test")
            };

            RegistryCacheInitializerContext context = CreateContext(options, Environments.Production);

            await context.Initializer.StartAsync(CancellationToken.None);

            string expected = Path.Combine(Directory.GetCurrentDirectory(), "cache");

            Assert.That(context.Singleton.UnityPackageFolder, Is.EqualTo(expected));
        }

        [Test]
        public async Task StopReturnsCompletedTask()
        {
            RegistryOptions options = new()
            {
                RegistryFilePath = "registry.json",
                RootPersistentFolder = "cache",
                RootHttpUrl = new Uri("https://packages.example.test")
            };

            RegistryCacheInitializerContext context = CreateContext(options);

            Task task = context.Initializer.StopAsync(CancellationToken.None);

            Assert.That(task.IsCompleted, Is.True);

            await task;
        }

        private static RegistryCacheInitializerContext CreateContext(RegistryOptions options, string environmentName = "Production")
        {
            Mock<IHostEnvironment> hostEnvironment = new();
            hostEnvironment.SetupGet(environment => environment.EnvironmentName).Returns(environmentName);

            FakeLogger<RegistryCacheInitializerTests> logger = new();
            Mock<ILoggerFactory> loggerFactory = new();
            loggerFactory.Setup(factory => factory.CreateLogger("NuGet")).Returns(logger);

            RegistryCacheSingleton singleton = new();
            RegistryCacheInitializer initializer = new(
                hostEnvironment.Object,
                loggerFactory.Object,
                Options.Create(options),
                singleton);

            return new RegistryCacheInitializerContext(initializer, singleton, logger);
        }

        private sealed class RegistryCacheInitializerContext(
            RegistryCacheInitializer initializer,
            RegistryCacheSingleton singleton,
            FakeLogger<RegistryCacheInitializerTests> logger)
        {
            public RegistryCacheInitializer Initializer { get; } = initializer;
            public RegistryCacheSingleton Singleton { get; } = singleton;
            public FakeLogger<RegistryCacheInitializerTests> Logger { get; } = logger;
        }
    }
}
