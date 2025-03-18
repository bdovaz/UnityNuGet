﻿using System;
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
            var hostEnvironmentMock = new Mock<IHostEnvironment>();
            hostEnvironmentMock.Setup(h => h.EnvironmentName).Returns(Environments.Development);

            var loggerFactory = new LoggerFactory();
            loggerFactory.AddProvider(new FakeLoggerProvider());

            string unityPackages = Path.Combine(Path.GetDirectoryName(typeof(RegistryCacheTests).Assembly.Location)!, "unity_packages");
            Directory.Delete(unityPackages, true);

            bool errorsTriggered = false;

            var registry = new Registry(hostEnvironmentMock.Object, loggerFactory, Options.Create(new RegistryOptions { RegistryFilePath = "registry.json" }));

            await registry.StartAsync(CancellationToken.None);

            var registryCache = new RegistryCache(
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
                    new() { Name = "roslyn3.8", DefineConstraints = ["!UNITY_6000_0_OR_NEWER"] },
                    new() { Name = "roslyn4.3", DefineConstraints = ["UNITY_6000_0_OR_NEWER"] },
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
