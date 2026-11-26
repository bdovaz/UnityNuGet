using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Moq;
using NuGet.Frameworks;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;
using NUnit.Framework;
using static NuGet.Frameworks.FrameworkConstants;

namespace UnityNuGet.Tests
{
    public class RegistryValidatorTests
    {
        private string _tempDir = string.Empty;

        [SetUp]
        public void SetUp()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), $"unitynuget-validator-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(_tempDir);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }

        private static (LoggerFactory LoggerFactory, FakeLoggerProvider Provider) CreateLoggerFactory()
        {
            LoggerFactory loggerFactory = new();
            FakeLoggerProvider provider = new();
            loggerFactory.AddProvider(provider);
            return (loggerFactory, provider);
        }

        private string WriteRegistryFile(string json)
        {
            string filePath = Path.Combine(_tempDir, "registry.json");
            File.WriteAllText(filePath, json);
            return filePath;
        }

        private static IPackageSearchMetadata CreatePackageMetadata(
            string id,
            string version,
            params NuGetFramework[] targetFrameworks)
        {
            List<PackageDependencyGroup> groups = [];
            foreach (NuGetFramework framework in targetFrameworks)
            {
                groups.Add(new PackageDependencyGroup(framework, []));
            }

            Mock<IPackageSearchMetadata> mock = new();
            mock.SetupGet(m => m.Identity).Returns(new PackageIdentity(id, NuGetVersion.Parse(version)));
            mock.SetupGet(m => m.DependencySets).Returns(groups);
            return mock.Object;
        }

        [Test]
        public async Task Validate_StaticChecksPassOnRepoRegistry()
        {
            (LoggerFactory loggerFactory, FakeLoggerProvider provider) = CreateLoggerFactory();
            using (loggerFactory)
            {
                RegistryValidator validator = new(loggerFactory);
                RegistryValidationResult result = await validator.Validate(new RegistryValidationOptions
                {
                    RegistryFilePath = "registry.json",
                });

                using (Assert.EnterMultipleScope())
                {
                    Assert.That(result.IsValid, Is.True, string.Join(Environment.NewLine, provider.Collector.GetSnapshot().Select(record => record.Message)));
                    Assert.That(result.CheckedPackageCount, Is.Zero);
                }
            }
        }

        [Test]
        public async Task Validate_RejectsDuplicateKeysCaseInsensitive()
        {
            string filePath = WriteRegistryFile("""
            {
                "Acornima": { "listed": true, "version": "0.9.0" },
                "acornima": { "listed": true, "version": "0.9.0" }
            }
            """);

            (LoggerFactory loggerFactory, FakeLoggerProvider provider) = CreateLoggerFactory();
            using (loggerFactory)
            {
                RegistryValidator validator = new(loggerFactory);
                RegistryValidationResult result = await validator.Validate(new RegistryValidationOptions
                {
                    RegistryFilePath = filePath,
                });

                using (Assert.EnterMultipleScope())
                {
                    Assert.That(result.IsValid, Is.False);
                    Assert.That(provider.Collector.GetSnapshot(), Has.Some.Property("Message").Contains("Duplicate package key"));
                }
            }
        }

        [Test]
        public async Task Validate_RejectsUnsortedKeys()
        {
            string filePath = WriteRegistryFile("""
            {
                "Scriban": { "listed": true, "version": "2.0.0" },
                "Acornima": { "listed": true, "version": "0.9.0" }
            }
            """);

            (LoggerFactory loggerFactory, FakeLoggerProvider provider) = CreateLoggerFactory();
            using (loggerFactory)
            {
                RegistryValidator validator = new(loggerFactory);
                RegistryValidationResult result = await validator.Validate(new RegistryValidationOptions
                {
                    RegistryFilePath = filePath,
                });

                using (Assert.EnterMultipleScope())
                {
                    Assert.That(result.IsValid, Is.False);
                    Assert.That(provider.Collector.GetSnapshot(), Has.Some.Property("Message").Contains("not sorted alphabetically"));
                }
            }
        }

        [Test]
        public async Task Validate_RejectsNetStandard20BuiltInAssembly()
        {
            string filePath = WriteRegistryFile("""
            {
                "System.Text.RegularExpressions": { "listed": true, "version": "4.3.0" }
            }
            """);

            (LoggerFactory loggerFactory, FakeLoggerProvider provider) = CreateLoggerFactory();
            using (loggerFactory)
            {
                RegistryValidator validator = new(loggerFactory);
                RegistryValidationResult result = await validator.Validate(new RegistryValidationOptions
                {
                    RegistryFilePath = filePath,
                });

                using (Assert.EnterMultipleScope())
                {
                    Assert.That(result.IsValid, Is.False);
                    Assert.That(provider.Collector.GetSnapshot(), Has.Some.Property("Message").Contains(".NET Standard 2.0"));
                }
            }
        }

        [Test]
        public async Task Validate_RequiresVersionUnlessIgnored()
        {
            string invalidPath = WriteRegistryFile("""
            {
                "Acornima": { "listed": true }
            }
            """);

            (LoggerFactory loggerFactory, FakeLoggerProvider provider) = CreateLoggerFactory();
            using (loggerFactory)
            {
                RegistryValidator validator = new(loggerFactory);
                RegistryValidationResult invalidResult = await validator.Validate(new RegistryValidationOptions
                {
                    RegistryFilePath = invalidPath,
                });

                using (Assert.EnterMultipleScope())
                {
                    Assert.That(invalidResult.IsValid, Is.False);
                    Assert.That(provider.Collector.GetSnapshot(), Has.Some.Property("Message").Contains("must specify a valid minimum version"));
                }

                string ignoredPath = WriteRegistryFile("""
                {
                    "Microsoft.CSharp": { "ignore": true }
                }
                """);

                RegistryValidationResult ignoredResult = await validator.Validate(new RegistryValidationOptions
                {
                    RegistryFilePath = ignoredPath,
                });

                Assert.That(ignoredResult.IsValid, Is.True);
            }
        }

        [Test]
        public async Task Validate_RejectsUnknownRequestedPackage()
        {
            string filePath = WriteRegistryFile("""
            {
                "Acornima": { "listed": true, "version": "0.9.0" }
            }
            """);

            (LoggerFactory loggerFactory, FakeLoggerProvider provider) = CreateLoggerFactory();
            using (loggerFactory)
            {
                RegistryValidator validator = new(loggerFactory);
                RegistryValidationResult result = await validator.Validate(new RegistryValidationOptions
                {
                    RegistryFilePath = filePath,
                    PackageNames = ["DoesNotExist"],
                    SkipConversion = true,
                });

                using (Assert.EnterMultipleScope())
                {
                    Assert.That(result.IsValid, Is.False);
                    Assert.That(provider.Collector.GetSnapshot(), Has.Some.Property("Message").Contains("was not found"));
                }
            }
        }

        [Test]
        public async Task Validate_ChecksMinVersionLowerEqualAndHigher()
        {
            Dictionary<string, IReadOnlyList<IPackageSearchMetadata>> catalog = new(StringComparer.OrdinalIgnoreCase)
            {
                ["ExactPkg"] =
                [
                    CreatePackageMetadata("ExactPkg", "0.9.0", CommonFrameworks.Net45),
                    CreatePackageMetadata("ExactPkg", "1.0.0", CommonFrameworks.NetStandard20),
                ],
                ["HigherPkg"] =
                [
                    CreatePackageMetadata("HigherPkg", "1.0.0", CommonFrameworks.NetStandard20),
                    CreatePackageMetadata("HigherPkg", "1.5.0", CommonFrameworks.NetStandard20),
                ],
                ["TooLowPkg"] =
                [
                    CreatePackageMetadata("TooLowPkg", "0.8.0", CommonFrameworks.Net45),
                    CreatePackageMetadata("TooLowPkg", "1.2.0", CommonFrameworks.NetStandard20),
                ],
            };

            (LoggerFactory loggerFactory, FakeLoggerProvider provider) = CreateLoggerFactory();
            using (loggerFactory)
            {
                RegistryValidator validator = new(
                    loggerFactory,
                    (id, _, _, _) => Task.FromResult(catalog.TryGetValue(id, out IReadOnlyList<IPackageSearchMetadata>? metas) ? metas : []),
                    (_, _) => Task.FromResult(false));

                string validPath = WriteRegistryFile("""
            {
                "ExactPkg": { "listed": true, "version": "1.0.0" },
                "HigherPkg": { "listed": true, "version": "1.5.0" }
            }
            """);

            RegistryValidationResult validResult = await validator.Validate(new RegistryValidationOptions
            {
                RegistryFilePath = validPath,
                PackageNames = ["ExactPkg", "HigherPkg"],
                SkipConversion = true,
            });

            using (Assert.EnterMultipleScope())
            {
                Assert.That(validResult.IsValid, Is.True, string.Join(Environment.NewLine, provider.Collector.GetSnapshot().Select(record => record.Message)));
                Assert.That(validResult.CheckedPackageCount, Is.EqualTo(2));
                Assert.That(provider.Collector.GetSnapshot(), Has.Some.Property("Message").Contains("HigherPkg"));
            }

            string tooLowPath = WriteRegistryFile("""
            {
                "TooLowPkg": { "listed": true, "version": "0.8.0" }
            }
            """);

            RegistryValidationResult tooLowResult = await validator.Validate(new RegistryValidationOptions
            {
                RegistryFilePath = tooLowPath,
                PackageNames = ["TooLowPkg"],
                SkipConversion = true,
            });

            using (Assert.EnterMultipleScope())
            {
                Assert.That(tooLowResult.IsValid, Is.False);
                Assert.That(provider.Collector.GetSnapshot(), Has.Some.Property("Message").Contains("lower than the earliest compatible .NET Standard 2.0 version `1.2.0`"));
            }
            }
        }

        [Test]
        public async Task Validate_SkipsMinVersionForAnalyzerAndFallsBackToNativeLibs()
        {
            Dictionary<string, IReadOnlyList<IPackageSearchMetadata>> catalog = new(StringComparer.OrdinalIgnoreCase)
            {
                ["MyAnalyzer"] =
                [
                    CreatePackageMetadata("MyAnalyzer", "1.0.0", CommonFrameworks.NetStandard13),
                ],
                ["NativeOnly"] =
                [
                    CreatePackageMetadata("NativeOnly", "2.0.0"),
                ],
            };

            (LoggerFactory loggerFactory, FakeLoggerProvider provider) = CreateLoggerFactory();
            using (loggerFactory)
            {
                RegistryValidator validator = new(
                    loggerFactory,
                    (id, _, _, _) => Task.FromResult(catalog.TryGetValue(id, out IReadOnlyList<IPackageSearchMetadata>? metas) ? metas : []),
                    (identity, _) => Task.FromResult(identity.Id == "NativeOnly"));

                string filePath = WriteRegistryFile("""
            {
                "MyAnalyzer": { "listed": true, "version": "1.0.0", "analyzer": true },
                "NativeOnly": { "listed": true, "version": "2.0.0" }
            }
            """);

            RegistryValidationResult result = await validator.Validate(new RegistryValidationOptions
            {
                RegistryFilePath = filePath,
                PackageNames = ["MyAnalyzer", "NativeOnly"],
                SkipConversion = true,
            });

            using (Assert.EnterMultipleScope())
            {
                Assert.That(result.IsValid, Is.True, string.Join(Environment.NewLine, provider.Collector.GetSnapshot().Select(record => record.Message)));
                Assert.That(result.CheckedPackageCount, Is.EqualTo(2));
            }
            }
        }

        [Test]
        public void BuildExactPackageFilterRegex_AnchorsExactIds()
        {
            string regexPattern = RegistryValidator.BuildExactPackageFilterRegex(["Serilog", "Acornima.Extras"]);
            Regex regex = new(regexPattern, RegexOptions.IgnoreCase);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(regex.IsMatch("Serilog"), Is.True);
                Assert.That(regex.IsMatch("serilog"), Is.True);
                Assert.That(regex.IsMatch("Serilog.Expressions"), Is.False);
                Assert.That(regex.IsMatch("Acornima.Extras"), Is.True);
                Assert.That(regex.IsMatch("AcornimaXExtras"), Is.False);
            }
        }

        [Test]
        public async Task Validate_RejectsNullEntryWithoutThrowing()
        {
            string filePath = WriteRegistryFile("""
            {
                "NullPkg": null
            }
            """);

            (LoggerFactory loggerFactory, FakeLoggerProvider provider) = CreateLoggerFactory();
            using (loggerFactory)
            {
                RegistryValidator validator = new(loggerFactory);
                RegistryValidationResult result = await validator.Validate(new RegistryValidationOptions
                {
                    RegistryFilePath = filePath,
                });

                using (Assert.EnterMultipleScope())
                {
                    Assert.That(result.IsValid, Is.False);
                    Assert.That(provider.Collector.GetSnapshot(), Has.Some.Property("Message").Contains("must not be null"));
                }
            }
        }

        [Test]
        public void HasConvertedVersion_RequiresNonEmptyShasum()
        {
            Assert.That(RegistryValidator.HasConvertedVersion(null), Is.False);

            Npm.NpmPackage emptyPkg = new();
            Assert.That(RegistryValidator.HasConvertedVersion(emptyPkg), Is.False);

            Npm.NpmPackage unbuiltVersionPkg = new();
            unbuiltVersionPkg.Versions["1.0.0"] = new Npm.NpmPackageVersion
            {
                Version = "1.0.0",
            };
            Assert.That(RegistryValidator.HasConvertedVersion(unbuiltVersionPkg), Is.False);

            Npm.NpmPackage builtVersionPkg = new();
            Npm.NpmPackageVersion builtVersion = new()
            {
                Version = "1.0.0",
            };
            builtVersion.Distribution.Shasum = "0123456789abcdef";
            builtVersionPkg.Versions["1.0.0"] = builtVersion;
            Assert.That(RegistryValidator.HasConvertedVersion(builtVersionPkg), Is.True);
        }
    }
}
