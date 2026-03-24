using System;
using System.Collections.Generic;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using NuGet.Versioning;
using NUnit.Framework;
using NUnit.Framework.Legacy;
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

            LoggerFactory loggerFactory = new();
            loggerFactory.AddProvider(new FakeLoggerProvider());

            string unityPackages = Path.Combine(Path.GetDirectoryName(typeof(RegistryCacheTests).Assembly.Location)!, "unity_packages");
            Registry registry = new(loggerFactory, Options.Create(new RegistryOptions { RegistryFilePath = "registry.json" }));

            await registry.StartAsync(CancellationToken.None);

            RegistryCache registryCache = new(
                registry,
                unityPackages,
                new Uri("http://localhost/"),
                "org.nuget",
                "2019.1",
                " (NuGet)",
                [
                    "nuget"
                ],
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
                OnError = (_, _) =>
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
            string allResultJson = await allResult.ToJson(UnityNuGetJsonSerializerContext.Default.NpmPackageListAllResponse);

            Assert.That(allResultJson, Does.Contain("org.nuget.scriban"));
            Assert.That(allResultJson, Does.Contain("org.nuget.system.runtime.compilerservices.unsafe"));

            NpmPackage? scribanPackage = registryCache.GetPackage("org.nuget.scriban");
            Assert.That(scribanPackage, Is.Not.Null);
            string scribanPackageJson = await scribanPackage!.ToJson(UnityNuGetJsonSerializerContext.Default.NpmPackage);
            Assert.That(scribanPackageJson, Does.Contain("org.nuget.scriban"));
            Assert.That(scribanPackageJson, Does.Contain("7.0.0"));

            List<string> entries = [];

            await using (FileStream fileStream = File.OpenRead(Path.Combine("unity_packages", "org.nuget.scriban-7.0.0.tgz")))
            await using (GZipStream gzipStream = new(fileStream, CompressionMode.Decompress, leaveOpen: false))
            await using (TarReader tarReader = new(gzipStream))
            {
                while (tarReader.GetNextEntry() is TarEntry entry)
                {
                    entries.Add(entry.Name);
                }
            }

            Assert.That(entries, Is.EqualTo(new List<string>
            {
                "package/lib/netstandard2.0/Scriban.dll",
                "package/lib/netstandard2.0/Scriban.dll.meta",
                "package/lib/netstandard2.0/Scriban.xml",
                "package/lib/netstandard2.0/Scriban.xml.meta",
                "package/lib.meta",
                "package/lib/netstandard2.0.meta",
                "package/package.json",
                "package/package.json.meta",
                "package/License.md" ,
                "package/License.md.meta",
            }).AsCollection);
        }
    }
}
