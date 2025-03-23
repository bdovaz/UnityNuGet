﻿using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using Moq;
using NuGet.Versioning;
using NUnit.Framework;
using UnityNuGet.Npm;

namespace UnityNuGet.Tests
{
    public class RegistryCacheTests
    {
        [Test]
        [TestCase("1.0.0", "1.0.0")]
        [TestCase("1.0.0.0", "1.0.0")]
        [TestCase("1.0.0.1", "1.0.0-1")]
        [TestCase("1.0.0-preview.1.24080.9", "1.0.0-preview.1.24080.9")]
        [TestCase("1.0.0.1-preview.1.24080.9", "1.0.0-1.preview.1.24080.9")]
        public void GetNpmVersion(string version, string expected)
        {
            Assert.That(RegistryCache.GetNpmVersion(NuGetVersion.Parse(version)), Is.EqualTo(expected));
        }

        [Test, Order(99)]
        public async Task TestBuild()
        {
            bool errorsTriggered = false;

            Mock<IHostEnvironment> hostEnvironmentMock = new();
            hostEnvironmentMock.Setup(h => h.EnvironmentName).Returns(Environments.Development);

            LoggerFactory loggerFactory = new();
            loggerFactory.AddProvider(new FakeLoggerProvider());

            string unityPackages = Path.Combine(Path.GetDirectoryName(typeof(RegistryCacheTests).Assembly.Location)!, "unity_packages");
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
                OnError = message =>
                {
                    errorsTriggered = true;
                }
            };

            // Uncomment when testing locally
            // registryCache.Filter = "scriban|bcl\\.asyncinterfaces|compilerservices\\.unsafe";

            await registryCache.Build();

            Assert.That(errorsTriggered, Is.False, "The registry failed to build, check the logs");

            NpmPackageListAllResponse allResult = registryCache.All();
            Assert.That(allResult.Packages, Has.Count.GreaterThanOrEqualTo(3));
            string allResultJson = await allResult.ToJson(UnityNugetJsonSerializerContext.Default.NpmPackageListAllResponse);

            Assert.That(allResultJson, Does.Contain("org.nuget.scriban"));
            Assert.That(allResultJson, Does.Contain("org.nuget.system.runtime.compilerservices.unsafe"));

            NpmPackage? scribanPackage = registryCache.GetPackage("org.nuget.scriban");
            Assert.That(scribanPackage, Is.Not.Null);
            string scribanPackageJson = await scribanPackage!.ToJson(UnityNugetJsonSerializerContext.Default.NpmPackage);
            Assert.That(scribanPackageJson, Does.Contain("org.nuget.scriban"));
            Assert.That(scribanPackageJson, Does.Contain("2.1.0"));
        }
    }
}
