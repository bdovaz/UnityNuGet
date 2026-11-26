using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
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
    public class RegistryCacheSigningTests
    {
        private static readonly DateTimeOffset s_fixedPublishedDate = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        [Test]
        public async Task FirstSignedBuild_CreatesArtifactsAndSidecar_CallingSignerOnce()
        {
            string directory = CreateTempDirectory();

            try
            {
                FakeUpmSigner signerShim = CreateFakeUpmSigner(directory);
                using LoggerFactory loggerFactory = new();
                RegistryCache cache = CreateCache(directory, loggerFactory, signerShim.CreateOptions());
                PackageIdentity identity = new("Example", NuGetVersion.Parse("1.0.0"));
                NpmPackageVersion npmVersion = CreateNpmVersion("org.nuget.example", "1.0.0");

                bool converted = await ConvertPackage(cache, identity, npmVersion, "dll-v1", forceUpdate: false);

                string tgzPath = Path.Combine(directory, "org.nuget.example-1.0.0.tgz");
                string sha1Path = Path.Combine(directory, "org.nuget.example-1.0.0.sha1");
                string sidecarPath = $"{tgzPath}.unsigned.sha256";
                string unsignedPath = $"{tgzPath}.unsigned";

                using (Assert.EnterMultipleScope())
                {
                    Assert.That(converted, Is.True);
                    Assert.That(signerShim.GetCallCount(), Is.EqualTo(1));
                    Assert.That(File.Exists(tgzPath), Is.True);
                    Assert.That(File.Exists(sha1Path), Is.True);
                    Assert.That(File.Exists(sidecarPath), Is.True);
                    Assert.That(File.Exists(unsignedPath), Is.False);
                }

                string expectedSha1 = ComputeSha1Hex(await File.ReadAllBytesAsync(tgzPath));
                string recordedSha1 = await File.ReadAllTextAsync(sha1Path);
                string recordedUnsignedSha256 = (await File.ReadAllTextAsync(sidecarPath)).Trim();

                using (Assert.EnterMultipleScope())
                {
                    Assert.That(recordedSha1, Is.EqualTo(expectedSha1));
                    Assert.That(npmVersion.Distribution.Shasum, Is.EqualTo(expectedSha1));
                    Assert.That(recordedUnsignedSha256, Has.Length.EqualTo(64));
                    Assert.That(recordedUnsignedSha256, Does.Match("^[0-9a-f]{64}$"));
                }
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        [Test]
        public async Task ForcedRebuild_WithUnchangedContent_ReusesSignedPackageWithoutResigning()
        {
            string directory = CreateTempDirectory();

            try
            {
                FakeUpmSigner signerShim = CreateFakeUpmSigner(directory);
                using LoggerFactory loggerFactory = new();
                List<string> infoLogs = [];
                RegistryCache cache = CreateCache(directory, loggerFactory, signerShim.CreateOptions());
                cache.OnInformation += (_, e) => infoLogs.Add(e.Message);

                PackageIdentity identity = new("Example", NuGetVersion.Parse("1.0.0"));
                NpmPackageVersion firstVersion = CreateNpmVersion("org.nuget.example", "1.0.0");

                await ConvertPackage(cache, identity, firstVersion, "dll-v1", forceUpdate: false);

                string tgzPath = Path.Combine(directory, "org.nuget.example-1.0.0.tgz");
                string sha1Path = Path.Combine(directory, "org.nuget.example-1.0.0.sha1");
                string sidecarPath = $"{tgzPath}.unsigned.sha256";
                string unsignedPath = $"{tgzPath}.unsigned";

                byte[] firstSignedTgz = await File.ReadAllBytesAsync(tgzPath);
                string firstSha1 = await File.ReadAllTextAsync(sha1Path);
                string firstUnsignedSha256 = await File.ReadAllTextAsync(sidecarPath);

                NpmPackageVersion secondVersion = CreateNpmVersion("org.nuget.example", "1.0.0");
                bool converted = await ConvertPackage(cache, identity, secondVersion, "dll-v1", forceUpdate: true);

                using (Assert.EnterMultipleScope())
                {
                    Assert.That(converted, Is.True);
                    Assert.That(signerShim.GetCallCount(), Is.EqualTo(1));
                    Assert.That(await File.ReadAllBytesAsync(tgzPath), Is.EqualTo(firstSignedTgz));
                    Assert.That(await File.ReadAllTextAsync(sha1Path), Is.EqualTo(firstSha1));
                    Assert.That(await File.ReadAllTextAsync(sidecarPath), Is.EqualTo(firstUnsignedSha256));
                    Assert.That(secondVersion.Distribution.Shasum, Is.EqualTo(firstSha1));
                    Assert.That(File.Exists(unsignedPath), Is.False);
                    Assert.That(infoLogs, Has.Some.Contains("Reusing signed Unity package"));
                }
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        [Test]
        public async Task ForcedRebuild_WithChangedContent_ResignsAndUpdatesArtifacts()
        {
            string directory = CreateTempDirectory();

            try
            {
                FakeUpmSigner signerShim = CreateFakeUpmSigner(directory);
                using LoggerFactory loggerFactory = new();
                RegistryCache cache = CreateCache(directory, loggerFactory, signerShim.CreateOptions());
                PackageIdentity identity = new("Example", NuGetVersion.Parse("1.0.0"));
                NpmPackageVersion firstVersion = CreateNpmVersion("org.nuget.example", "1.0.0");

                await ConvertPackage(cache, identity, firstVersion, "dll-v1", forceUpdate: false);

                string tgzPath = Path.Combine(directory, "org.nuget.example-1.0.0.tgz");
                string sha1Path = Path.Combine(directory, "org.nuget.example-1.0.0.sha1");
                string sidecarPath = $"{tgzPath}.unsigned.sha256";

                byte[] firstSignedTgz = await File.ReadAllBytesAsync(tgzPath);
                string firstSha1 = await File.ReadAllTextAsync(sha1Path);
                string firstUnsignedSha256 = await File.ReadAllTextAsync(sidecarPath);

                NpmPackageVersion secondVersion = CreateNpmVersion("org.nuget.example", "1.0.0");
                bool converted = await ConvertPackage(cache, identity, secondVersion, "dll-v2-changed", forceUpdate: true);

                byte[] secondSignedTgz = await File.ReadAllBytesAsync(tgzPath);
                string secondSha1 = await File.ReadAllTextAsync(sha1Path);
                string secondUnsignedSha256 = await File.ReadAllTextAsync(sidecarPath);

                using (Assert.EnterMultipleScope())
                {
                    Assert.That(converted, Is.True);
                    Assert.That(signerShim.GetCallCount(), Is.EqualTo(2));
                    Assert.That(secondSignedTgz, Is.Not.EqualTo(firstSignedTgz));
                    Assert.That(secondSha1, Is.Not.EqualTo(firstSha1));
                    Assert.That(secondSha1, Is.EqualTo(ComputeSha1Hex(secondSignedTgz)));
                    Assert.That(secondVersion.Distribution.Shasum, Is.EqualTo(secondSha1));
                    Assert.That(secondUnsignedSha256, Is.Not.EqualTo(firstUnsignedSha256));
                    Assert.That(File.Exists($"{tgzPath}.unsigned"), Is.False);
                }
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        [TestCase("missing")]
        [TestCase("empty")]
        [TestCase("mismatched")]
        public async Task ForcedRebuild_WithInvalidOrMissingSidecar_ResignsAndUpdatesArtifacts(string mode)
        {
            string directory = CreateTempDirectory();

            try
            {
                FakeUpmSigner signerShim = CreateFakeUpmSigner(directory);
                using LoggerFactory loggerFactory = new();
                RegistryCache cache = CreateCache(directory, loggerFactory, signerShim.CreateOptions());
                PackageIdentity identity = new("Example", NuGetVersion.Parse("1.0.0"));
                NpmPackageVersion firstVersion = CreateNpmVersion("org.nuget.example", "1.0.0");

                await ConvertPackage(cache, identity, firstVersion, "dll-v1", forceUpdate: false);

                string tgzPath = Path.Combine(directory, "org.nuget.example-1.0.0.tgz");
                string sha1Path = Path.Combine(directory, "org.nuget.example-1.0.0.sha1");
                string sidecarPath = $"{tgzPath}.unsigned.sha256";

                byte[] firstSignedTgz = await File.ReadAllBytesAsync(tgzPath);
                string firstSha1 = await File.ReadAllTextAsync(sha1Path);
                string expectedUnsignedSha256 = await File.ReadAllTextAsync(sidecarPath);

                switch (mode)
                {
                    case "missing":
                        File.Delete(sidecarPath);
                        break;
                    case "empty":
                        await File.WriteAllTextAsync(sidecarPath, string.Empty);
                        break;
                    case "mismatched":
                        await File.WriteAllTextAsync(sidecarPath, new string('0', 64));
                        break;
                    default:
                        Assert.Fail($"Unexpected test mode: {mode}");
                        break;
                }

                NpmPackageVersion secondVersion = CreateNpmVersion("org.nuget.example", "1.0.0");
                bool converted = await ConvertPackage(cache, identity, secondVersion, "dll-v1", forceUpdate: true);

                byte[] secondSignedTgz = await File.ReadAllBytesAsync(tgzPath);
                string secondSha1 = await File.ReadAllTextAsync(sha1Path);

                using (Assert.EnterMultipleScope())
                {
                    Assert.That(converted, Is.True);
                    Assert.That(signerShim.GetCallCount(), Is.EqualTo(2));
                    Assert.That(secondSignedTgz, Is.Not.EqualTo(firstSignedTgz));
                    Assert.That(secondSha1, Is.Not.EqualTo(firstSha1));
                    Assert.That(secondSha1, Is.EqualTo(ComputeSha1Hex(secondSignedTgz)));
                    Assert.That(secondVersion.Distribution.Shasum, Is.EqualTo(secondSha1));
                    Assert.That(await File.ReadAllTextAsync(sidecarPath), Is.EqualTo(expectedUnsignedSha256));
                }
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        [TestCase("tgz")]
        [TestCase("sha1")]
        public async Task ForcedRebuild_WithMissingSignedArtifact_ResignsEvenWhenSidecarMatches(string missingArtifact)
        {
            string directory = CreateTempDirectory();

            try
            {
                FakeUpmSigner signerShim = CreateFakeUpmSigner(directory);
                using LoggerFactory loggerFactory = new();
                RegistryCache cache = CreateCache(directory, loggerFactory, signerShim.CreateOptions());
                PackageIdentity identity = new("Example", NuGetVersion.Parse("1.0.0"));
                NpmPackageVersion firstVersion = CreateNpmVersion("org.nuget.example", "1.0.0");

                await ConvertPackage(cache, identity, firstVersion, "dll-v1", forceUpdate: false);

                string tgzPath = Path.Combine(directory, "org.nuget.example-1.0.0.tgz");
                string sha1Path = Path.Combine(directory, "org.nuget.example-1.0.0.sha1");
                string sidecarPath = $"{tgzPath}.unsigned.sha256";

                File.Delete(missingArtifact == "tgz" ? tgzPath : sha1Path);

                NpmPackageVersion secondVersion = CreateNpmVersion("org.nuget.example", "1.0.0");
                bool converted = await ConvertPackage(cache, identity, secondVersion, "dll-v1", forceUpdate: true);

                using (Assert.EnterMultipleScope())
                {
                    Assert.That(converted, Is.True);
                    Assert.That(signerShim.GetCallCount(), Is.EqualTo(2));
                    Assert.That(File.Exists(tgzPath), Is.True);
                    Assert.That(File.Exists(sha1Path), Is.True);
                    Assert.That(File.Exists(sidecarPath), Is.True);
                    Assert.That(secondVersion.Distribution.Shasum, Is.EqualTo(ComputeSha1Hex(await File.ReadAllBytesAsync(tgzPath))));
                }
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        [Test]
        public async Task DeleteUnityPackage_RemovesSidecarAlongWithPackageAndSha1()
        {
            string directory = CreateTempDirectory();

            try
            {
                FakeUpmSigner signerShim = CreateFakeUpmSigner(directory);
                using LoggerFactory loggerFactory = new();
                RegistryCache cache = CreateCache(directory, loggerFactory, signerShim.CreateOptions());
                PackageIdentity identity = new("Example", NuGetVersion.Parse("1.0.0"));
                NpmPackageVersion npmVersion = CreateNpmVersion("org.nuget.example", "1.0.0");

                await ConvertPackage(cache, identity, npmVersion, "dll-v1", forceUpdate: false);

                string tgzPath = Path.Combine(directory, "org.nuget.example-1.0.0.tgz");
                string sha1Path = Path.Combine(directory, "org.nuget.example-1.0.0.sha1");
                string sidecarPath = $"{tgzPath}.unsigned.sha256";

                using (Assert.EnterMultipleScope())
                {
                    Assert.That(File.Exists(tgzPath), Is.True);
                    Assert.That(File.Exists(sha1Path), Is.True);
                    Assert.That(File.Exists(sidecarPath), Is.True);
                }

                cache.DeleteUnityPackage(identity, npmVersion);

                using (Assert.EnterMultipleScope())
                {
                    Assert.That(File.Exists(tgzPath), Is.False);
                    Assert.That(File.Exists(sha1Path), Is.False);
                    Assert.That(File.Exists(sidecarPath), Is.False);
                }
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        [Test]
        public async Task UnsignableCache_DoesNotWriteSidecar_AndForcedRebuildRemovesStaleSidecar()
        {
            string directory = CreateTempDirectory();

            try
            {
                using LoggerFactory loggerFactory = new();
                RegistryCache cache = CreateCache(directory, loggerFactory, new SigningOptions());
                PackageIdentity identity = new("Example", NuGetVersion.Parse("1.0.0"));
                NpmPackageVersion npmVersion = CreateNpmVersion("org.nuget.example", "1.0.0");

                bool converted = await ConvertPackage(cache, identity, npmVersion, "dll-v1", forceUpdate: false);

                string tgzPath = Path.Combine(directory, "org.nuget.example-1.0.0.tgz");
                string sha1Path = Path.Combine(directory, "org.nuget.example-1.0.0.sha1");
                string sidecarPath = $"{tgzPath}.unsigned.sha256";

                using (Assert.EnterMultipleScope())
                {
                    Assert.That(converted, Is.True);
                    Assert.That(File.Exists(tgzPath), Is.True);
                    Assert.That(File.Exists(sha1Path), Is.True);
                    Assert.That(File.Exists(sidecarPath), Is.False);
                    Assert.That(File.Exists($"{tgzPath}.unsigned"), Is.False);
                }

                await File.WriteAllTextAsync(sidecarPath, "stale-digest");

                NpmPackageVersion rebuiltVersion = CreateNpmVersion("org.nuget.example", "1.0.0");
                await ConvertPackage(cache, identity, rebuiltVersion, "dll-v1", forceUpdate: true);

                using (Assert.EnterMultipleScope())
                {
                    Assert.That(File.Exists(tgzPath), Is.True);
                    Assert.That(File.Exists(sha1Path), Is.True);
                    Assert.That(File.Exists(sidecarPath), Is.False);
                }
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        [Test]
        public async Task PackageExcludedByRegex_DoesNotInvokeSignerOrWriteSidecar()
        {
            string directory = CreateTempDirectory();

            try
            {
                FakeUpmSigner signerShim = CreateFakeUpmSigner(directory);
                SigningOptions options = signerShim.CreateOptions();
                options.PackageIdRegexPattern = "^DifferentPackage$";

                using LoggerFactory loggerFactory = new();
                RegistryCache cache = CreateCache(directory, loggerFactory, options);
                PackageIdentity identity = new("Example", NuGetVersion.Parse("1.0.0"));
                NpmPackageVersion npmVersion = CreateNpmVersion("org.nuget.example", "1.0.0");

                bool converted = await ConvertPackage(cache, identity, npmVersion, "dll-v1", forceUpdate: false);

                string tgzPath = Path.Combine(directory, "org.nuget.example-1.0.0.tgz");
                string sha1Path = Path.Combine(directory, "org.nuget.example-1.0.0.sha1");
                string sidecarPath = $"{tgzPath}.unsigned.sha256";

                using (Assert.EnterMultipleScope())
                {
                    Assert.That(converted, Is.True);
                    Assert.That(signerShim.GetCallCount(), Is.EqualTo(0));
                    Assert.That(File.Exists(tgzPath), Is.True);
                    Assert.That(File.Exists(sha1Path), Is.True);
                    Assert.That(File.Exists(sidecarPath), Is.False);
                }

                await File.WriteAllTextAsync(sidecarPath, "stale-digest");

                NpmPackageVersion rebuiltVersion = CreateNpmVersion("org.nuget.example", "1.0.0");
                await ConvertPackage(cache, identity, rebuiltVersion, "dll-v1", forceUpdate: true);

                using (Assert.EnterMultipleScope())
                {
                    Assert.That(signerShim.GetCallCount(), Is.EqualTo(0));
                    Assert.That(File.Exists(tgzPath), Is.True);
                    Assert.That(File.Exists(sha1Path), Is.True);
                    Assert.That(File.Exists(sidecarPath), Is.False);
                }
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        [Test]
        public async Task FailedSigner_OnFirstBuild_DoesNotWritePackageOrSidecar_AndRecoversWhenSignerSucceeds()
        {
            string directory = CreateTempDirectory();

            try
            {
                FakeUpmSigner signerShim = CreateFakeUpmSigner(directory);
                signerShim.SetShouldFail(true);

                using LoggerFactory loggerFactory = new();
                List<string> warnings = [];
                RegistryCache cache = CreateCache(directory, loggerFactory, signerShim.CreateOptions());
                cache.OnWarning += (_, e) => warnings.Add(e.Message);

                PackageIdentity identity = new("Example", NuGetVersion.Parse("1.0.0"));
                NpmPackageVersion firstAttempt = CreateNpmVersion("org.nuget.example", "1.0.0");

                await ConvertPackage(cache, identity, firstAttempt, "dll-v1", forceUpdate: false);

                string tgzPath = Path.Combine(directory, "org.nuget.example-1.0.0.tgz");
                string sha1Path = Path.Combine(directory, "org.nuget.example-1.0.0.sha1");
                string sidecarPath = $"{tgzPath}.unsigned.sha256";

                using (Assert.EnterMultipleScope())
                {
                    Assert.That(signerShim.GetCallCount(), Is.EqualTo(1));
                    Assert.That(warnings, Has.Some.Contains("Error while processing package"));
                    Assert.That(File.Exists(tgzPath), Is.False);
                    Assert.That(File.Exists(sha1Path), Is.False);
                    Assert.That(File.Exists(sidecarPath), Is.False);
                    Assert.That(File.Exists($"{tgzPath}.unsigned"), Is.False);
                    Assert.That(firstAttempt.Distribution.Shasum, Is.Null);
                }

                signerShim.SetShouldFail(false);

                NpmPackageVersion secondAttempt = CreateNpmVersion("org.nuget.example", "1.0.0");
                bool converted = await ConvertPackage(cache, identity, secondAttempt, "dll-v1", forceUpdate: false);

                using (Assert.EnterMultipleScope())
                {
                    Assert.That(converted, Is.True);
                    Assert.That(signerShim.GetCallCount(), Is.EqualTo(2));
                    Assert.That(File.Exists(tgzPath), Is.True);
                    Assert.That(File.Exists(sha1Path), Is.True);
                    Assert.That(File.Exists(sidecarPath), Is.True);
                    Assert.That(secondAttempt.Distribution.Shasum, Is.EqualTo(ComputeSha1Hex(await File.ReadAllBytesAsync(tgzPath))));
                }
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        [Test]
        public async Task FailedSigner_OnForcedRebuildWithChangedContent_PreservesExistingSignedArtifacts()
        {
            string directory = CreateTempDirectory();

            try
            {
                FakeUpmSigner signerShim = CreateFakeUpmSigner(directory);
                using LoggerFactory loggerFactory = new();
                List<string> warnings = [];
                RegistryCache cache = CreateCache(directory, loggerFactory, signerShim.CreateOptions());
                cache.OnWarning += (_, e) => warnings.Add(e.Message);

                PackageIdentity identity = new("Example", NuGetVersion.Parse("1.0.0"));
                NpmPackageVersion firstVersion = CreateNpmVersion("org.nuget.example", "1.0.0");

                await ConvertPackage(cache, identity, firstVersion, "dll-v1", forceUpdate: false);

                string tgzPath = Path.Combine(directory, "org.nuget.example-1.0.0.tgz");
                string sha1Path = Path.Combine(directory, "org.nuget.example-1.0.0.sha1");
                string sidecarPath = $"{tgzPath}.unsigned.sha256";

                byte[] firstSignedTgz = await File.ReadAllBytesAsync(tgzPath);
                string firstSha1 = await File.ReadAllTextAsync(sha1Path);
                string firstSidecar = await File.ReadAllTextAsync(sidecarPath);

                signerShim.SetShouldFail(true);

                NpmPackageVersion secondVersion = CreateNpmVersion("org.nuget.example", "1.0.0");
                await ConvertPackage(cache, identity, secondVersion, "dll-v2-changed", forceUpdate: true);

                using (Assert.EnterMultipleScope())
                {
                    Assert.That(signerShim.GetCallCount(), Is.EqualTo(2));
                    Assert.That(warnings, Has.Some.Contains("Error while processing package"));
                    Assert.That(File.Exists($"{tgzPath}.unsigned"), Is.False);
                    Assert.That(Directory.GetDirectories(directory, ".signing-*"), Is.Empty);
                    Assert.That(await File.ReadAllBytesAsync(tgzPath), Is.EqualTo(firstSignedTgz));
                    Assert.That(await File.ReadAllTextAsync(sha1Path), Is.EqualTo(firstSha1));
                    Assert.That(await File.ReadAllTextAsync(sidecarPath), Is.EqualTo(firstSidecar));
                }
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        [Test]
        public async Task FailedForcedRebuild_BeforeMove_PreservesExistingSignedArtifacts()
        {
            string directory = CreateTempDirectory();

            try
            {
                FakeUpmSigner signerShim = CreateFakeUpmSigner(directory);
                using LoggerFactory loggerFactory = new();
                List<string> warnings = [];
                RegistryCache cache = CreateCache(directory, loggerFactory, signerShim.CreateOptions());
                cache.OnWarning += (_, e) => warnings.Add(e.Message);

                PackageIdentity identity = new("Example", NuGetVersion.Parse("1.0.0"));
                NpmPackageVersion firstVersion = CreateNpmVersion("org.nuget.example", "1.0.0");

                await ConvertPackage(cache, identity, firstVersion, "dll-v1", forceUpdate: false);

                string tgzPath = Path.Combine(directory, "org.nuget.example-1.0.0.tgz");
                string sha1Path = Path.Combine(directory, "org.nuget.example-1.0.0.sha1");
                string sidecarPath = $"{tgzPath}.unsigned.sha256";

                byte[] firstSignedTgz = await File.ReadAllBytesAsync(tgzPath);
                string firstSha1 = await File.ReadAllTextAsync(sha1Path);
                string firstSidecar = await File.ReadAllTextAsync(sidecarPath);

                NpmPackageVersion secondVersion = CreateNpmVersion("org.nuget.example", "1.0.0");
                await ConvertIncompatiblePackage(cache, identity, secondVersion, forceUpdate: true);

                using (Assert.EnterMultipleScope())
                {
                    Assert.That(signerShim.GetCallCount(), Is.EqualTo(1));
                    Assert.That(warnings, Has.Some.Contains("Error while processing package"));
                    Assert.That(File.Exists($"{tgzPath}.unsigned"), Is.False);
                    Assert.That(await File.ReadAllBytesAsync(tgzPath), Is.EqualTo(firstSignedTgz));
                    Assert.That(await File.ReadAllTextAsync(sha1Path), Is.EqualTo(firstSha1));
                    Assert.That(await File.ReadAllTextAsync(sidecarPath), Is.EqualTo(firstSidecar));
                }
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        private static string CreateTempDirectory()
        {
            string directory = Path.Combine(TestContext.CurrentContext.WorkDirectory, $"signing-test-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);
            return directory;
        }

        private static RegistryCache CreateCache(string directory, LoggerFactory loggerFactory, SigningOptions signingOptions)
        {
            Registry registry = new(loggerFactory, Options.Create(new RegistryOptions { RegistryFilePath = "registry.json" }));

            return new RegistryCache(
                registry,
                directory,
                new Uri("http://localhost/"),
                "org.nuget",
                "2019.1",
                " (NuGet)",
                [],
                [new() { Name = "netstandard2.0" }, new() { Name = "netstandard2.1" }],
                [],
                new NuGetConsoleTestLogger(),
                new UnityPackageSigner(loggerFactory.CreateLogger<UnityPackageSigner>(), Options.Create(signingOptions)));
        }

        private static NpmPackageVersion CreateNpmVersion(string name, string version) => new() { Name = name, Version = version };

        private static async Task<bool> ConvertPackage(
            RegistryCache cache,
            PackageIdentity identity,
            NpmPackageVersion npmVersion,
            string dllContent,
            bool forceUpdate)
        {
            using MemoryStream packageStream = CreateNuGetPackageStream(identity, "lib/netstandard2.0/Example.dll", dllContent);
            using PackageArchiveReader reader = new(packageStream);
            Mock<IPackageSearchMetadata> metadata = CreatePackageMetadata();

            return await cache.ConvertNuGetToUnityPackageIfDoesNotExist(
                identity,
                new NpmPackageInfo { Name = npmVersion.Name, Description = "Test description" },
                npmVersion,
                metadata.Object,
                reader,
                forceUpdate,
                new RegistryEntry());
        }

        private static async Task<bool> ConvertIncompatiblePackage(
            RegistryCache cache,
            PackageIdentity identity,
            NpmPackageVersion npmVersion,
            bool forceUpdate)
        {
            using MemoryStream packageStream = CreateNuGetPackageStream(identity, "lib/net8.0/Example.dll", "incompatible");
            using PackageArchiveReader reader = new(packageStream);
            Mock<IPackageSearchMetadata> metadata = CreatePackageMetadata();

            return await cache.ConvertNuGetToUnityPackageIfDoesNotExist(
                identity,
                new NpmPackageInfo { Name = npmVersion.Name, Description = "Test description" },
                npmVersion,
                metadata.Object,
                reader,
                forceUpdate,
                new RegistryEntry());
        }

        private static MemoryStream CreateNuGetPackageStream(PackageIdentity identity, string entryPath, string entryContent)
        {
            MemoryStream packageStream = new();

            using (ZipArchive archive = new(packageStream, ZipArchiveMode.Create, leaveOpen: true))
            {
                WriteZipEntry(
                    archive,
                    $"{identity.Id}.nuspec",
                    $"<package><metadata><id>{identity.Id}</id><version>{identity.Version}</version><authors>Test</authors><description>Test</description></metadata></package>");
                WriteZipEntry(archive, entryPath, entryContent);
            }

            packageStream.Position = 0;
            return packageStream;
        }

        private static Mock<IPackageSearchMetadata> CreatePackageMetadata()
        {
            Mock<IPackageSearchMetadata> metadata = new();
            metadata.Setup(x => x.DependencySets).Returns([]);
            metadata.Setup(x => x.IsListed).Returns(true);
            metadata.Setup(x => x.Published).Returns(s_fixedPublishedDate);
            return metadata;
        }

        private static void WriteZipEntry(ZipArchive archive, string path, string content)
        {
            using StreamWriter writer = new(archive.CreateEntry(path).Open());
            writer.Write(content);
        }

        private static string ComputeSha1Hex(byte[] bytes)
        {
            byte[] hash = SHA1.HashData(bytes);
            StringBuilder sb = new(hash.Length * 2);

            foreach (byte b in hash)
            {
                sb.Append(b.ToString("x2"));
            }

            return sb.ToString();
        }

        private static FakeUpmSigner CreateFakeUpmSigner(string directory)
        {
            string countFilePath = Path.Combine(directory, "signer-calls.count");
            string failFlagPath = Path.Combine(directory, "signer-fail.flag");

            if (OperatingSystem.IsWindows())
            {
                string scriptPath = Path.Combine(directory, "fake-upm.cmd");
                string script = string.Join("\r\n",
                [
                    "@echo off",
                    "setlocal enabledelayedexpansion",
                    "set \"PKG=\"",
                    "set \"DEST=\"",
                    ":parse",
                    "if \"%~1\"==\"\" goto parsed",
                    "if \"%~1\"==\"pack\" (",
                    "    set \"PKG=%~2\"",
                    "    shift",
                    "    shift",
                    "    goto parse",
                    ")",
                    "if \"%~1\"==\"--destination\" (",
                    "    set \"DEST=%~2\"",
                    "    shift",
                    "    shift",
                    "    goto parse",
                    ")",
                    "if \"%~1\"==\"--organization-id\" (",
                    "    shift",
                    "    shift",
                    "    goto parse",
                    ")",
                    "shift",
                    "goto parse",
                    ":parsed",
                    "if not exist \"%PKG%\\package.json\" exit /b 1",
                    "set \"COUNT=0\"",
                    $"if exist \"{countFilePath}\" set /p COUNT=<\"{countFilePath}\"",
                    "set /a COUNT+=1",
                    $">\"{countFilePath}\" echo !COUNT!",
                    $"if exist \"{failFlagPath}\" exit /b 1",
                    "for /f \"usebackq tokens=1,2 delims=|\" %%A in (`powershell -NoProfile -Command \"$j = Get-Content -Raw -LiteralPath '%PKG%\\package.json' | ConvertFrom-Json; Write-Output ($j.name + '|' + $j.version)\"`) do (",
                    "    set \"NAME=%%A\"",
                    "    set \"VERSION=%%B\"",
                    ")",
                    "if \"!NAME!\"==\"\" exit /b 1",
                    "if \"!VERSION!\"==\"\" exit /b 1",
                    ">\"%DEST%\\!NAME!-!VERSION!.tgz\" echo signed-package-invocation-!COUNT!",
                    ""
                ]);

                File.WriteAllText(scriptPath, script, new UTF8Encoding(false));
                return new FakeUpmSigner(scriptPath, countFilePath, failFlagPath);
            }
            else
            {
                string scriptPath = Path.Combine(directory, "fake-upm.sh");
                string escapedCountFile = countFilePath.Replace("'", "'\\''");
                string escapedFailFlag = failFlagPath.Replace("'", "'\\''");
                string script = string.Join('\n',
                [
                    "#!/bin/sh",
                    "set -eu",
                    "pkg=\"\"",
                    "dest=\"\"",
                    "while [ \"$#\" -gt 0 ]; do",
                    "  case \"$1\" in",
                    "    pack) pkg=\"$2\"; shift 2 ;;",
                    "    --destination) dest=\"$2\"; shift 2 ;;",
                    "    --organization-id) shift 2 ;;",
                    "    *) shift ;;",
                    "  esac",
                    "done",
                    "if [ ! -f \"$pkg/package.json\" ]; then",
                    "  echo \"missing package.json in $pkg\" >&2",
                    "  exit 1",
                    "fi",
                    $"count_file='{escapedCountFile}'",
                    $"fail_flag='{escapedFailFlag}'",
                    "n=0",
                    "if [ -f \"$count_file\" ]; then",
                    "  n=$(cat \"$count_file\")",
                    "fi",
                    "n=$((n + 1))",
                    "printf '%s' \"$n\" > \"$count_file\"",
                    "if [ -f \"$fail_flag\" ]; then",
                    "  echo \"simulated signer failure\" >&2",
                    "  exit 1",
                    "fi",
                    "name=$(tr -d '\\r\\n' < \"$pkg/package.json\" | sed -n 's/.*\"name\":\"\\([^\"]*\\)\".*/\\1/p')",
                    "version=$(tr -d '\\r\\n' < \"$pkg/package.json\" | sed -n 's/.*\"version\":\"\\([^\"]*\\)\".*/\\1/p')",
                    "if [ -z \"$name\" ] || [ -z \"$version\" ]; then",
                    "  echo \"failed to parse name/version from $pkg/package.json\" >&2",
                    "  exit 1",
                    "fi",
                    "target=\"$dest/${name}-${version}.tgz\"",
                    "printf 'signed-package-invocation-%s\\n' \"$n\" > \"$target.tmp\"",
                    "mv \"$target.tmp\" \"$target\"",
                    ""
                ]);

                File.WriteAllText(scriptPath, script, new UTF8Encoding(false));
                File.SetUnixFileMode(
                    scriptPath,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                return new FakeUpmSigner(scriptPath, countFilePath, failFlagPath);
            }
        }

        private sealed class FakeUpmSigner(string executablePath, string countFilePath, string failFlagPath)
        {
            public SigningOptions CreateOptions() => new()
            {
                UpmServiceAccountKeyId = "test-key-id",
                UpmServiceAccountKeySecret = "test-key-secret",
                UnityOrganizationId = 12345,
                UpmExecutableFilePath = executablePath
            };

            public void SetShouldFail(bool shouldFail)
            {
                if (shouldFail)
                {
                    File.WriteAllText(failFlagPath, "1");
                }
                else if (File.Exists(failFlagPath))
                {
                    File.Delete(failFlagPath);
                }
            }

            public int GetCallCount()
            {
                if (!File.Exists(countFilePath))
                {
                    return 0;
                }

                string text = File.ReadAllText(countFilePath).Trim();
                return int.Parse(text);
            }
        }
    }
}
