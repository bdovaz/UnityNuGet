using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;

namespace UnityNuGet.Server.Tests
{
    public class RegistryCacheInitializerTests
    {
        [Test]
        public async Task StartUsesConfiguredUriAndRootPersistentFolderForRootedRegistryPath()
        {
            Uri configuredUri = new("https://packages.example.test");
            string rootPersistentFolder = Path.Combine(Path.GetTempPath(), "UnityNuGet.Server.Tests", Guid.NewGuid().ToString("N"));
            RegistryOptions options = new()
            {
                RootHttpUrl = configuredUri,
                RegistryFilePath = Path.Combine(Path.GetTempPath(), "registry.json"),
                RootPersistentFolder = rootPersistentFolder
            };
            RegistryCacheInitializerContext context = CreateContext(options);

            await context.Initializer.StartAsync(CancellationToken.None);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(context.Singleton.UnityPackageFolder, Is.EqualTo(rootPersistentFolder));
                Assert.That(context.Singleton.ServerUri, Is.EqualTo(configuredUri));
                Assert.That(context.Singleton.NuGetRedirectLogger, Is.Not.Null);
                Assert.That(context.Singleton.NuGetRedirectLogger!.Logger, Is.SameAs(NullLoggerFactory.Instance.CreateLogger("NuGet")));
            }
        }

        [Test]
        public async Task StartUsesDevelopmentHttpUrlAndApplicationRelativeFolder()
        {
            RegistryOptions options = new()
            {
                RootHttpUrl = new Uri("https://configured.example.test"),
                RegistryFilePath = "registry.json",
                RootPersistentFolder = "cache"
            };
            RegistryCacheInitializerContext context = CreateContext(
                options,
                Environments.Development,
                new Dictionary<string, string?>
                {
                    [WebHostDefaults.ServerUrlsKey] = "https://localhost:5001;http://localhost:5000"
                });

            await context.Initializer.StartAsync(CancellationToken.None);

            string expectedFolder = Path.Combine(Path.GetDirectoryName(AppContext.BaseDirectory)!, "cache");

            using (Assert.EnterMultipleScope())
            {
                Assert.That(context.Singleton.ServerUri, Is.EqualTo(new Uri("http://localhost:5000")));
                Assert.That(context.Singleton.UnityPackageFolder, Is.EqualTo(expectedFolder));
            }
        }

        [Test]
        public async Task StartReplacesWildcardHostWhenRunningInContainer()
        {
            RegistryOptions options = new()
            {
                RootHttpUrl = new Uri("https://configured.example.test"),
                RegistryFilePath = Path.Combine(Path.GetTempPath(), "registry.json"),
                RootPersistentFolder = "cache"
            };
            RegistryCacheInitializerContext context = CreateContext(
                options,
                Environments.Development,
                new Dictionary<string, string?>
                {
                    [WebHostDefaults.ServerUrlsKey] = "http://+:5000",
                    ["DOTNET_RUNNING_IN_CONTAINER"] = "true"
                });

            await context.Initializer.StartAsync(CancellationToken.None);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(context.Singleton.ServerUri, Is.EqualTo(new Uri("http://localhost:5000")));
                Assert.That(context.Singleton.UnityPackageFolder, Is.EqualTo("cache"));
            }
        }

        [Test]
        public void StartThrowsWhenDevelopmentUrlsContainNoHttpUrl()
        {
            RegistryOptions options = new()
            {
                RootHttpUrl = new Uri("https://configured.example.test"),
                RegistryFilePath = "registry.json",
                RootPersistentFolder = "cache"
            };
            RegistryCacheInitializerContext context = CreateContext(
                options,
                Environments.Development,
                new Dictionary<string, string?> { [WebHostDefaults.ServerUrlsKey] = "https://localhost:5001" });

            Assert.That(
                () => context.Initializer.StartAsync(CancellationToken.None),
                Throws.TypeOf<InvalidOperationException>().With.Message.Contains("Expecting a `http://...` URL"));
            Assert.That(context.Singleton.ServerUri, Is.Null);
        }

        [Test]
        public async Task StopReturnsCompletedTask()
        {
            RegistryCacheInitializerContext context = CreateContext(new RegistryOptions());

            Task task = context.Initializer.StopAsync(CancellationToken.None);

            Assert.That(task.IsCompleted, Is.True);

            await task;
        }

        private static RegistryCacheInitializerContext CreateContext(
            RegistryOptions options,
            string environmentName = "Production",
            IDictionary<string, string?>? configurationValues = null)
        {
            IConfiguration configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(configurationValues ?? new Dictionary<string, string?>())
                .Build();
            Mock<IHostEnvironment> hostEnvironment = new();
            hostEnvironment.SetupGet(environment => environment.EnvironmentName).Returns(environmentName);
            RegistryCacheSingleton singleton = new();
            RegistryCacheInitializer initializer = new(
                configuration,
                hostEnvironment.Object,
                NullLoggerFactory.Instance,
                Options.Create(options),
                singleton);

            return new RegistryCacheInitializerContext(initializer, singleton);
        }

        private sealed class RegistryCacheInitializerContext(
            RegistryCacheInitializer initializer,
            RegistryCacheSingleton singleton)
        {
            public RegistryCacheInitializer Initializer { get; } = initializer;
            public RegistryCacheSingleton Singleton { get; } = singleton;
        }

    }
}
