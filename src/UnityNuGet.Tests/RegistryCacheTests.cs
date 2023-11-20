﻿using System;
using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;

namespace UnityNuGet.Tests
{
    public class RegistryCacheTests
    {
        [Test]
        public async Task TestBuild()
        {
            var errorsTriggered = false;

            var unityPackages = Path.Combine(Path.GetDirectoryName(typeof(RegistryCacheTests).Assembly.Location)!, "unity_packages");
            var registryCache = new RegistryCache(
                unityPackages,
                new Uri("http://localhost/"),
                "org.nuget",
                "2019.1",
                " (NuGet)",
                [
                    new() { Name = "netstandard2.1", DefineConstraints = ["UNITY_2021_2_OR_NEWER"] },
                    new() { Name = "netstandard2.0", DefineConstraints = ["!UNITY_2021_2_OR_NEWER"] },
                ],
                new NuGetConsoleLogger())
            {
                OnError = message =>
                {
                    errorsTriggered = true;
                }
            };

            // Uncomment when testing locally
            // registryCache.Filter = "scriban|bcl\\.asyncinterfaces|compilerservices\\.unsafe";

            await registryCache.Build();

            Assert.False(errorsTriggered, "The registry failed to build, check the logs");

            var allResult = registryCache.All();
            Assert.True(allResult.Packages.Count >= 3);
            var allResultJson = allResult.ToJson();

            StringAssert.Contains("org.nuget.scriban", allResultJson);
            StringAssert.Contains("org.nuget.system.runtime.compilerservices.unsafe", allResultJson);

            var scribanPackage = registryCache.GetPackage("org.nuget.scriban");
            Assert.NotNull(scribanPackage);
            var scribanPackageJson = scribanPackage!.ToJson();
            StringAssert.Contains("org.nuget.scriban", scribanPackageJson);
            StringAssert.Contains("2.1.0", scribanPackageJson);
        }
    }
}
