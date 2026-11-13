using System;
using System.Collections.Generic;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using NuGet.Configuration;
using NuGet.PackageManagement;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;
using NUnit.Framework;
using UnityNuGet.Npm;

namespace UnityNuGet.Tests
{
    public class ReferencePackageTests
    {
        [TestCase("SQLitePCLRaw.bundle_e_sqlite3", "3.0.2", "meta", "SQLitePCLRaw.config.e_sqlite3")]
        [TestCase("Microsoft.CodeAnalysis.Scripting", "4.12.0", "meta", "Microsoft.CodeAnalysis.CSharp.Scripting")]
        [TestCase("Microsoft.CodeAnalysis", "4.12.0", "meta", "Microsoft.CodeAnalysis.CSharp.Workspaces")]
        [TestCase("SQLitePCLRaw.bundle_e_sqlite3", "2.1.10", "managed", "SQLitePCLRaw.provider.e_sqlite3")]
        [TestCase("SQLitePCLRaw.lib.e_sqlite3", "2.1.10", "native", "")]
        [TestCase("Grpc.Tools", "2.67.0", "tools", "")]
        public async Task ConvertsPublishedPackageAssets(string id, string version, string kind, string dependencyId)
        {
            string directory = Path.Combine(TestContext.CurrentContext.WorkDirectory, $"reference-package-{Guid.NewGuid():N}");

            Directory.CreateDirectory(directory);

            try
            {
                using LoggerFactory loggerFactory = new();

                Registry registry = new(loggerFactory, Options.Create(new RegistryOptions { RegistryFilePath = "registry.json" }));
                RegistryCache cache = CreateCache(registry, directory, loggerFactory);
                PackageIdentity identity = new(id, NuGetVersion.Parse(version));

                using DownloadResourceResult download = await DownloadPackage(identity);
                PackageReaderBase reader = download.PackageReader!;
                IEnumerable<PackageDependencyGroup> dependencyGroups = reader.NuspecReader.GetDependencyGroups();

                Mock<IPackageSearchMetadata> metadata = new();

                metadata.Setup(x => x.DependencySets).Returns(dependencyGroups);

                NpmPackageVersion npmVersion = new() { Name = $"org.nuget.{id.ToLowerInvariant()}", Version = version };

                List<string> warnings = [];

                cache.OnWarning = (_, e) => warnings.Add(e.Message);

                List<string> sourceFiles = [.. await reader.GetFilesAsync(CancellationToken.None)];

                if (kind == "meta")
                {
                    using (Assert.EnterMultipleScope())
                    {
                        Assert.That(sourceFiles, Has.None.StartsWith("lib/"));
                        Assert.That(dependencyGroups.SelectMany(x => x.Packages).Select(x => x.Id), Does.Contain(dependencyId));
                    }
                }
                else if (kind == "tools")
                {
                    Assert.That(sourceFiles, Has.Some.StartsWith("build/"));
                    Assert.That(sourceFiles, Has.Some.StartsWith("tools/"));
                }

                await cache.ConvertNuGetPackageToUnity(identity, new NpmPackageInfo(), npmVersion, metadata.Object, reader, new RegistryEntry());

                string path = Path.Combine(directory, $"org.nuget.{id.ToLowerInvariant()}-{version}.tgz");

                if (kind == "tools")
                {
                    using (Assert.EnterMultipleScope())
                    {
                        Assert.That(File.Exists(path), Is.False);
                        Assert.That(warnings, Has.Some.Contains("compatible .NET assembly"));
                    }

                    return;
                }

                Assert.That(File.Exists(path), Is.True, string.Join(Environment.NewLine, warnings));

                List<string> entries = GetTarEntryNames(path);

                Assert.That(entries, Does.Contain("package/package.json"));

                if (kind == "meta")
                {
                    Assert.That(entries.Any(x => x.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)), Is.False);
                }
                else if (kind == "managed")
                {
                    Assert.That(entries, Does.Contain("package/lib/netstandard2.0/SQLitePCLRaw.batteries_v2.dll"));
                }
                else
                {
                    Assert.That(entries, Does.Contain("package/runtimes/win-x64/native/e_sqlite3.dll"));
                    Assert.That(entries, Does.Contain("package/runtimes/linux-x64/native/libe_sqlite3.so"));
                    Assert.That(entries, Does.Contain("package/runtimes/osx-arm64/native/libe_sqlite3.dylib"));
                }
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        [TestCase("SQLitePCLRaw.bundle_e_sqlite3", "3.0.2", "SQLitePCLRaw.config.e_sqlite3", "3.0.2", "SourceGear.sqlite3", "3.50.4-2")]
        [TestCase("SQLitePCLRaw.bundle_e_sqlite3", "2.1.10", "SQLitePCLRaw.provider.e_sqlite3", "2.1.10", "SQLitePCLRaw.lib.e_sqlite3", "2.1.10")]
        [TestCase("Microsoft.CodeAnalysis.Scripting", "4.12.0", "Microsoft.CodeAnalysis.CSharp.Scripting", "4.12.0", "Microsoft.CodeAnalysis.Analyzers", "3.3.4")]
        [TestCase("Microsoft.CodeAnalysis", "4.12.0", "Microsoft.CodeAnalysis.CSharp.Workspaces", "4.12.0", "Microsoft.CodeAnalysis.VisualBasic.Workspaces", "4.12.0")]
        public async Task BuildsPackageWithDeclaredDependencies(string id, string version, string firstId, string firstVersion, string secondId, string secondVersion)
        {
            string directory = Path.Combine(TestContext.CurrentContext.WorkDirectory, $"reference-registry-{Guid.NewGuid():N}");

            Directory.CreateDirectory(directory);

            try
            {
                PackageIdentity identity = new(id, NuGetVersion.Parse(version));

                using DownloadResourceResult download = await DownloadPackage(identity);

                IEnumerable<PackageDependency> dependencies = download.PackageReader!.NuspecReader.GetDependencyGroups()
                    .SelectMany(group => group.Packages);

                Dictionary<string, object> registryEntries = new(StringComparer.OrdinalIgnoreCase)
                {
                    [id] = new { listed = true, version = $"[{version}]" }
                };

                foreach (PackageDependency dependency in dependencies)
                {
                    registryEntries.TryAdd(dependency.Id, new { listed = false, version = "0.0.0" });
                }

                string registryPath = Path.Combine(directory, "registry.json");

                await File.WriteAllTextAsync(registryPath, JsonSerializer.Serialize(registryEntries));

                using LoggerFactory loggerFactory = new();

                Registry registry = new(loggerFactory, Options.Create(new RegistryOptions { RegistryFilePath = registryPath }));

                await registry.StartAsync(CancellationToken.None);

                RegistryCache cache = CreateCache(registry, directory, loggerFactory);

                cache.Filter = $"^{Regex.Escape(id)}$";

                List<string> errors = [];
                List<string> warnings = [];

                cache.OnError = (_, e) => errors.Add(e.Message);
                cache.OnWarning = (_, e) => warnings.Add(e.Message);

                await cache.Build();

                Assert.That(errors, Is.Empty);

                string path = Path.Combine(directory, $"org.nuget.{id.ToLowerInvariant()}-{version}.tgz");

                Assert.That(File.Exists(path), Is.True, string.Join(Environment.NewLine, warnings));

                using FileStream file = File.OpenRead(path);
                using GZipStream gzip = new(file, CompressionMode.Decompress);
                using TarReader tar = new(gzip);

                string? manifest = null;

                while (tar.GetNextEntry() is TarEntry entry)
                {
                    if (entry.Name == "package/package.json")
                    {
                        using StreamReader text = new(entry.DataStream!);

                        manifest = await text.ReadToEndAsync();

                        break;
                    }
                }

                Assert.That(manifest, Is.Not.Null);

                using JsonDocument json = JsonDocument.Parse(manifest!);

                JsonElement manifestDependencies = json.RootElement.GetProperty("dependencies");

                using (Assert.EnterMultipleScope())
                {
                    Assert.That(manifestDependencies.GetProperty($"org.nuget.{firstId.ToLowerInvariant()}").GetString(), Is.EqualTo(firstVersion));
                    Assert.That(manifestDependencies.GetProperty($"org.nuget.{secondId.ToLowerInvariant()}").GetString(), Is.EqualTo(secondVersion));
                }

                if (id == "SQLitePCLRaw.bundle_e_sqlite3" && version == "2.1.10")
                {
                    using (Assert.EnterMultipleScope())
                    {
                        Assert.That(manifestDependencies.TryGetProperty("org.nuget.sqlitepclraw.provider.internal", out _), Is.False);
                        Assert.That(manifestDependencies.TryGetProperty("org.nuget.sqlitepclraw.lib.e_sqlite3.ios", out _), Is.False);
                    }
                }
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        [Test]
        public async Task BuildDoesNotPublishGrpcTools()
        {
            string directory = Path.Combine(TestContext.CurrentContext.WorkDirectory, $"grpc-tools-{Guid.NewGuid():N}");

            Directory.CreateDirectory(directory);

            try
            {
                string registryPath = Path.Combine(directory, "registry.json");

                await File.WriteAllTextAsync(registryPath, """
                    { "Grpc.Tools": { "listed": true, "version": "[2.67.0]" } }
                    """);

                using LoggerFactory loggerFactory = new();

                Registry registry = new(loggerFactory, Options.Create(new RegistryOptions { RegistryFilePath = registryPath }));

                await registry.StartAsync(CancellationToken.None);

                RegistryCache cache = CreateCache(registry, directory, loggerFactory);

                List<string> warnings = [];
                List<string> errors = [];

                cache.OnWarning = (_, e) => warnings.Add(e.Message);
                cache.OnError = (_, e) => errors.Add(e.Message);

                await cache.Build();

                using (Assert.EnterMultipleScope())
                {
                    Assert.That(errors, Is.Empty);
                    Assert.That(warnings, Has.Some.Contains("doesn't support"));
                    Assert.That(File.Exists(Path.Combine(directory, "org.nuget.grpc.tools-2.67.0.tgz")), Is.False);
                    Assert.That(cache.GetPackage("org.nuget.grpc.tools"), Is.Null);
                }
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        private static RegistryCache CreateCache(Registry registry, string directory, LoggerFactory loggerFactory)
        {
            return new RegistryCache(
                registry, directory, new Uri("http://localhost/"), "org.nuget", "2019.1", " (NuGet)", [],
                [new() { Name = "netstandard2.0" }, new() { Name = "netstandard2.1" }], [],
                new NuGetConsoleTestLogger(),
                new UnityPackageSigner(loggerFactory.CreateLogger<UnityPackageSigner>(), Options.Create(new SigningOptions())));
        }

        private static async Task<DownloadResourceResult> DownloadPackage(PackageIdentity identity)
        {
            SourceRepository repository = Repository.Factory.GetCoreV3("https://api.nuget.org/v3/index.json");
            ISettings settings = Settings.LoadDefaultSettings(root: null);

            return await PackageDownloader.GetDownloadResourceResultAsync(
                [repository], identity, new PackageDownloadContext(new SourceCacheContext()),
                SettingsUtility.GetGlobalPackagesFolder(settings), new NuGetConsoleTestLogger(), CancellationToken.None);
        }

        private static List<string> GetTarEntryNames(string path)
        {
            using FileStream file = File.OpenRead(path);
            using GZipStream gzip = new(file, CompressionMode.Decompress);
            using TarReader tar = new(gzip);

            List<string> names = [];

            while (tar.GetNextEntry() is TarEntry entry)
            {
                names.Add(entry.Name.Replace('\\', '/'));
            }

            return names;
        }
    }
}
