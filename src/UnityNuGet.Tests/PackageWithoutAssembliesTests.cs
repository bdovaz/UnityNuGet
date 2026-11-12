using System;
using System.Collections.Generic;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using NuGet.Frameworks;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;
using NUnit.Framework;
using UnityNuGet.Npm;

namespace UnityNuGet.Tests
{
    public class PackageWithoutAssembliesTests
    {
        [TestCase("meta")]
        [TestCase("native")]
        [TestCase("incompatible")]
        [TestCase("incompatible-with-dependencies")]
        [TestCase("incompatible-dependencies")]
        [TestCase("unsupported-native")]
        [TestCase("empty")]
        public async Task ConvertsOnlyPackagesWithCompatibleContent(string kind)
        {
            string directory = Path.Combine(TestContext.CurrentContext.WorkDirectory, $"package-test-{Guid.NewGuid():N}");

            Directory.CreateDirectory(directory);

            try
            {
                using LoggerFactory loggerFactory = new();

                Registry registry = new(loggerFactory, Options.Create(new RegistryOptions { RegistryFilePath = "registry.json" }));

                RegistryCache cache = new(
                    registry, directory, new Uri("http://localhost/"), "org.nuget", "2019.1", " (NuGet)", [],
                    [new() { Name = "netstandard2.0" }, new() { Name = "netstandard2.1" }], [],
                    new NuGetConsoleTestLogger(),
                    new UnityPackageSigner(loggerFactory.CreateLogger<UnityPackageSigner>(), Options.Create(new SigningOptions())));

                using MemoryStream packageStream = new();

                using (ZipArchive archive = new(packageStream, ZipArchiveMode.Create, leaveOpen: true))
                {
                    WriteEntry(archive, "Example.nuspec", "<package><metadata><id>Example</id><version>1.0.0</version><authors>Test</authors><description>Test</description></metadata></package>");

                    if (kind == "native")
                    {
                        WriteEntry(archive, "runtimes/android-arm64/native/libopus.so", "android arm64");
                        WriteEntry(archive, "runtimes/android-arm/native/libopus.so", "android arm");
                        WriteEntry(archive, "runtimes/browser-wasm/native/libopus.a", "webgl");
                        WriteEntry(archive, "runtimes/win-x64/native/opus.dll", "windows");
                    }
                    else if (kind is "incompatible" or "incompatible-with-dependencies")
                    {
                        WriteEntry(archive, "lib/net8.0/Example.dll", "incompatible");
                    }
                    else if (kind == "unsupported-native")
                    {
                        WriteEntry(archive, "runtimes/ios/native/libopus.xcframework/ios-arm64/libopus.a", "unsupported bundle");
                    }
                }

                packageStream.Position = 0;

                using PackageArchiveReader reader = new(packageStream);

                Mock<IPackageSearchMetadata> metadata = new();

                metadata.Setup(x => x.DependencySets).Returns(kind switch
                {
                    "meta" or "incompatible-with-dependencies" => [new PackageDependencyGroup(NuGetFramework.Parse("netstandard2.0"), [new PackageDependency("Example.Core", VersionRange.Parse("1.0.0"))])],
                    "incompatible-dependencies" => [new PackageDependencyGroup(NuGetFramework.Parse("net8.0"), [new PackageDependency("Example.Core", VersionRange.Parse("1.0.0"))])],
                    _ => []
                });

                NpmPackageVersion npmVersion = new() { Name = "org.nuget.example", Version = "1.0.0" };

                if (kind == "meta")
                {
                    npmVersion.Dependencies.Add("org.nuget.example.core", "1.0.0");
                }

                List<string> warnings = [];

                cache.OnWarning = (_, e) => warnings.Add(e.Message);

                await cache.ConvertNuGetPackageToUnity(
                    new PackageIdentity("Example", NuGetVersion.Parse("1.0.0")),
                    new NpmPackageInfo(), npmVersion, metadata.Object, reader, new RegistryEntry());

                string outputPath = Path.Combine(directory, "org.nuget.example-1.0.0.tgz");

                if (kind is "empty" or "incompatible" or "incompatible-with-dependencies" or "incompatible-dependencies" or "unsupported-native")
                {
                    using (Assert.EnterMultipleScope())
                    {
                        Assert.That(File.Exists(outputPath), Is.False);
                        Assert.That(warnings, Has.Some.Contains("compatible .NET assembly"));
                    }

                    return;
                }

                using (Assert.EnterMultipleScope())
                {
                    Assert.That(File.Exists(outputPath), Is.True, string.Join(Environment.NewLine, warnings));
                    Assert.That(npmVersion.Distribution.Shasum, Is.Not.Null.And.Not.Empty);
                }

                Dictionary<string, string> entries = [];

                await using FileStream file = File.OpenRead(outputPath);
                await using GZipStream gzip = new(file, CompressionMode.Decompress);

                using TarReader tar = new(gzip);

                while (tar.GetNextEntry() is TarEntry entry)
                {
                    using StreamReader text = new(entry.DataStream!);

                    entries.Add(entry.Name.Replace('\\', '/'), await text.ReadToEndAsync());
                }

                using JsonDocument manifest = JsonDocument.Parse(entries["package/package.json"]);

                if (kind == "meta")
                {
                    using (Assert.EnterMultipleScope())
                    {
                        Assert.That(manifest.RootElement.GetProperty("dependencies").GetProperty("org.nuget.example.core").GetString(), Is.EqualTo("1.0.0"));
                        Assert.That(entries.Keys.Any(x => x.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)), Is.False);
                    }
                }
                else
                {
                    using (Assert.EnterMultipleScope())
                    {
                        Assert.That(entries["package/runtimes/android-arm64/native/libopus.so"], Is.EqualTo("android arm64"));
                        Assert.That(entries["package/runtimes/android-arm64/native/libopus.so.meta"], Does.Contain("Android: Android\n    second:\n      enabled: 1\n      settings:\n        CPU: ARM64"));
                        Assert.That(entries["package/runtimes/android-arm/native/libopus.so.meta"], Does.Contain("Android: Android\n    second:\n      enabled: 1\n      settings:\n        CPU: ARMv7"));
                        Assert.That(entries["package/runtimes/browser-wasm/native/libopus.a.meta"], Does.Contain("WebGL: WebGL\n    second:\n      enabled: 1"));
                        Assert.That(entries, Does.ContainKey("package/runtimes/win-x64/native/opus.dll.meta"));
                    }
                }
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        [Test]
        public async Task BuildsOpusSharpAndItsDependencies()
        {
            string directory = Path.Combine(TestContext.CurrentContext.WorkDirectory, $"opussharp-test-{Guid.NewGuid():N}");

            Directory.CreateDirectory(directory);

            try
            {
                string registryPath = Path.Combine(directory, "registry.json");

                await File.WriteAllTextAsync(registryPath, """
                    {
                        "OpusSharp": { "listed": true, "version": "[1.6.8]" },
                        "OpusSharp.Core": { "listed": true, "version": "1.6.1.4" },
                        "OpusSharp.Natives": { "listed": true, "version": "1.6.1.4" }
                    }
                    """);

                using LoggerFactory loggerFactory = new();

                Registry registry = new(loggerFactory, Options.Create(new RegistryOptions { RegistryFilePath = registryPath }));

                await registry.StartAsync(CancellationToken.None);

                RegistryCache cache = new(
                    registry, directory, new Uri("http://localhost/"), "org.nuget", "2019.1", " (NuGet)", [],
                    [new() { Name = "netstandard2.0" }, new() { Name = "netstandard2.1" }], [],
                    new NuGetConsoleTestLogger(),
                    new UnityPackageSigner(loggerFactory.CreateLogger<UnityPackageSigner>(), Options.Create(new SigningOptions())));

                List<string> errors = [];

                cache.OnError = (_, e) => errors.Add(e.Message);

                await cache.Build();

                Assert.That(errors, Is.Empty);

                string metaPath = Path.Combine(directory, "org.nuget.opussharp-1.6.8.tgz");
                string corePath = Path.Combine(directory, "org.nuget.opussharp.core-1.6.1-4.tgz");
                string nativesPath = Path.Combine(directory, "org.nuget.opussharp.natives-1.6.1-4.tgz");

                using (Assert.EnterMultipleScope())
                {
                    Assert.That(File.Exists(metaPath), Is.True);
                    Assert.That(File.Exists(corePath), Is.True);
                    Assert.That(File.Exists(nativesPath), Is.True);
                    Assert.That(GetTarEntryNames(corePath), Does.Contain("package/lib/netstandard2.0/OpusSharp.Core.dll"));
                    Assert.That(GetTarEntryNames(nativesPath), Does.Contain("package/runtimes/android-arm64/native/libopus.so"));
                }

                Assert.That(GetTarEntryNames(nativesPath), Does.Contain("package/runtimes/browser-wasm/native/libopus.a"));
                Assert.That(GetTarEntryNames(nativesPath), Does.Contain("package/runtimes/win-x64/native/opus.dll"));

                await using FileStream file = File.OpenRead(metaPath);
                await using GZipStream gzip = new(file, CompressionMode.Decompress);

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

                JsonElement dependencies = json.RootElement.GetProperty("dependencies");

                using (Assert.EnterMultipleScope())
                {
                    Assert.That(dependencies.GetProperty("org.nuget.opussharp.core").GetString(), Is.EqualTo("1.6.1-4"));
                    Assert.That(dependencies.GetProperty("org.nuget.opussharp.natives").GetString(), Is.EqualTo("1.6.1-4"));
                }
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
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

        private static void WriteEntry(ZipArchive archive, string path, string content)
        {
            using StreamWriter writer = new(archive.CreateEntry(path).Open());

            writer.Write(content);
        }
    }
}
