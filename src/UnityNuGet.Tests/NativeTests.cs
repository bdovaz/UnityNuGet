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
using UnityNuGet.Npm;

namespace UnityNuGet.Tests
{
    [Ignore("Ignore native libs tests")]
    public class NativeTests
    {
        [Test]
        public async Task TestBuild()
        {
            Mock<IHostEnvironment> hostEnvironmentMock = new();
            hostEnvironmentMock.Setup(h => h.EnvironmentName).Returns(Environments.Development);

            LoggerFactory loggerFactory = new();
            loggerFactory.AddProvider(new FakeLoggerProvider());

            string unityPackages = Path.Combine(Path.GetDirectoryName(typeof(RegistryCacheTests).Assembly.Location)!, "unity_packages");
            Directory.Delete(unityPackages, true);

            bool errorsTriggered = false;

            Registry registry = new(hostEnvironmentMock.Object, loggerFactory, Options.Create(new RegistryOptions { RegistryFilePath = "registry.json" }));

            await registry.StartAsync(CancellationToken.None);

            RegistryCache registryCache = new(
                registry,
                unityPackages,
                new Uri("http://localhost/"),
                "org.nuget",
                "2019.1",
                " (NuGet)",
                [
                    new() { Name = "netstandard2.0", DefineConstraints = ["!UNITY_2021_2_OR_NEWER"] },
                    new() { Name = "netstandard2.1", DefineConstraints = ["UNITY_2021_2_OR_NEWER"] },
                ],
                [
                    new() { Version = new Version(3, 8, 0, 0), DefineConstraints = ["!UNITY_6000_0_OR_NEWER"] },
                    new() { Version = new Version(4, 3, 0, 0), DefineConstraints = ["UNITY_6000_0_OR_NEWER"] },
                ],
                new NuGetConsoleTestLogger())
            {
                Filter = "rhino3dm",
                OnError = message =>
                {
                    errorsTriggered = true;
                }
            };

            await registryCache.Build();

            Assert.That(errorsTriggered, Is.False, "The registry failed to build, check the logs");

            NpmPackageListAllResponse allResult = registryCache.All();
            string allResultJson = await allResult.ToJson(UnityNugetJsonSerializerContext.Default.NpmPackageListAllResponse);

            Assert.That(allResultJson, Does.Contain("org.nuget.rhino3dm"));

            NpmPackage? rhinoPackage = registryCache.GetPackage("org.nuget.rhino3dm");

            Assert.That(rhinoPackage, Is.Not.Null);

            string rhinopackageJson = await rhinoPackage!.ToJson(UnityNugetJsonSerializerContext.Default.NpmPackage);

            Assert.That(rhinopackageJson, Does.Contain("org.nuget.rhino3dm"));
            Assert.That(rhinopackageJson, Does.Contain("7.11.0"));
        }
    }
}
