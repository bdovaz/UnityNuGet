using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NuGet.Frameworks;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;
using NUnit.Framework;
using UnityNuGet.Tool;
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
        public async Task ValidateAsync_StaticChecksPassOnRepoRegistry()
        {
            RegistryValidator validator = new();
            RegistryValidationResult result = await validator.ValidateAsync(new RegistryValidationOptions
            {
                RegistryFilePath = "registry.json",
            });

            Assert.That(result.IsValid, Is.True, string.Join(Environment.NewLine, result.Errors));
            Assert.That(result.CheckedPackageCount, Is.EqualTo(0));
        }

        [Test]
        public async Task ValidateAsync_RejectsDuplicateKeysCaseInsensitive()
        {
            string filePath = WriteRegistryFile("""
            {
                "Acornima": { "listed": true, "version": "0.9.0" },
                "acornima": { "listed": true, "version": "0.9.0" }
            }
            """);

            RegistryValidator validator = new();
            RegistryValidationResult result = await validator.ValidateAsync(new RegistryValidationOptions
            {
                RegistryFilePath = filePath,
            });

            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Errors, Has.Some.Contains("Duplicate package key"));
        }

        [Test]
        public async Task ValidateAsync_RejectsUnsortedKeys()
        {
            string filePath = WriteRegistryFile("""
            {
                "Scriban": { "listed": true, "version": "2.0.0" },
                "Acornima": { "listed": true, "version": "0.9.0" }
            }
            """);

            RegistryValidator validator = new();
            RegistryValidationResult result = await validator.ValidateAsync(new RegistryValidationOptions
            {
                RegistryFilePath = filePath,
            });

            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Errors, Has.Some.Contains("not sorted alphabetically"));
        }

        [Test]
        public async Task ValidateAsync_RejectsNetStandard20BuiltInAssembly()
        {
            string filePath = WriteRegistryFile("""
            {
                "System.Text.RegularExpressions": { "listed": true, "version": "4.3.0" }
            }
            """);

            RegistryValidator validator = new();
            RegistryValidationResult result = await validator.ValidateAsync(new RegistryValidationOptions
            {
                RegistryFilePath = filePath,
            });

            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Errors, Has.Some.Contains(".NET Standard 2.0"));
        }

        [Test]
        public async Task ValidateAsync_RequiresVersionUnlessIgnored()
        {
            string invalidPath = WriteRegistryFile("""
            {
                "Acornima": { "listed": true }
            }
            """);

            RegistryValidator validator = new();
            RegistryValidationResult invalidResult = await validator.ValidateAsync(new RegistryValidationOptions
            {
                RegistryFilePath = invalidPath,
            });

            Assert.That(invalidResult.IsValid, Is.False);
            Assert.That(invalidResult.Errors, Has.Some.Contains("must specify a valid minimum version"));

            string ignoredPath = WriteRegistryFile("""
            {
                "Microsoft.CSharp": { "ignore": true }
            }
            """);

            RegistryValidationResult ignoredResult = await validator.ValidateAsync(new RegistryValidationOptions
            {
                RegistryFilePath = ignoredPath,
            });

            Assert.That(ignoredResult.IsValid, Is.True);
        }

        [Test]
        public async Task ValidateAsync_RejectsUnknownRequestedPackage()
        {
            string filePath = WriteRegistryFile("""
            {
                "Acornima": { "listed": true, "version": "0.9.0" }
            }
            """);

            RegistryValidator validator = new();
            RegistryValidationResult result = await validator.ValidateAsync(new RegistryValidationOptions
            {
                RegistryFilePath = filePath,
                PackageNames = ["DoesNotExist"],
                SkipConversion = true,
            });

            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Errors, Has.Some.Contains("was not found"));
        }

        [Test]
        public async Task ValidateAsync_ChecksMinVersionLowerEqualAndHigher()
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

            RegistryValidator validator = new(
                null,
                (id, _, _, _) => Task.FromResult(catalog.TryGetValue(id, out IReadOnlyList<IPackageSearchMetadata>? metas) ? metas : []),
                (_, _) => Task.FromResult(false));

            string validPath = WriteRegistryFile("""
            {
                "ExactPkg": { "listed": true, "version": "1.0.0" },
                "HigherPkg": { "listed": true, "version": "1.5.0" }
            }
            """);

            RegistryValidationResult validResult = await validator.ValidateAsync(new RegistryValidationOptions
            {
                RegistryFilePath = validPath,
                PackageNames = ["ExactPkg", "HigherPkg"],
                SkipConversion = true,
            });

            Assert.That(validResult.IsValid, Is.True, string.Join(Environment.NewLine, validResult.Errors));
            Assert.That(validResult.CheckedPackageCount, Is.EqualTo(2));
            Assert.That(validResult.Information, Has.Some.Contains("HigherPkg"));

            string tooLowPath = WriteRegistryFile("""
            {
                "TooLowPkg": { "listed": true, "version": "0.8.0" }
            }
            """);

            RegistryValidationResult tooLowResult = await validator.ValidateAsync(new RegistryValidationOptions
            {
                RegistryFilePath = tooLowPath,
                PackageNames = ["TooLowPkg"],
                SkipConversion = true,
            });

            Assert.That(tooLowResult.IsValid, Is.False);
            Assert.That(tooLowResult.Errors, Has.Some.Contains("lower than the earliest compatible .NET Standard 2.0 version `1.2.0`"));
        }

        [Test]
        public async Task ValidateAsync_SkipsMinVersionForAnalyzerAndFallsBackToNativeLibs()
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

            RegistryValidator validator = new(
                null,
                (id, _, _, _) => Task.FromResult(catalog.TryGetValue(id, out IReadOnlyList<IPackageSearchMetadata>? metas) ? metas : []),
                (identity, _) => Task.FromResult(identity.Id == "NativeOnly"));

            string filePath = WriteRegistryFile("""
            {
                "MyAnalyzer": { "listed": true, "version": "1.0.0", "analyzer": true },
                "NativeOnly": { "listed": true, "version": "2.0.0" }
            }
            """);

            RegistryValidationResult result = await validator.ValidateAsync(new RegistryValidationOptions
            {
                RegistryFilePath = filePath,
                PackageNames = ["MyAnalyzer", "NativeOnly"],
                SkipConversion = true,
            });

            Assert.That(result.IsValid, Is.True, string.Join(Environment.NewLine, result.Errors));
            Assert.That(result.CheckedPackageCount, Is.EqualTo(2));
        }

        [Test]
        public void BuildExactPackageFilterRegex_AnchorsExactIds()
        {
            string regexPattern = RegistryValidator.BuildExactPackageFilterRegex(["Serilog", "Acornima.Extras"]);
            System.Text.RegularExpressions.Regex regex = new(regexPattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);

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
        public async Task ValidateAsync_RejectsNullEntryWithoutThrowing()
        {
            string filePath = WriteRegistryFile("""
            {
                "NullPkg": null
            }
            """);

            RegistryValidator validator = new();
            RegistryValidationResult result = await validator.ValidateAsync(new RegistryValidationOptions
            {
                RegistryFilePath = filePath,
            });

            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Errors, Has.Some.Contains("must not be null"));
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

        [Test]
        public void ValidateCommand_TryParseArgs_HandlesValidAndInvalidInputs()
        {
            Assert.That(
                ValidateCommand.TryParseArgs(
                    ["registry.json", "--package", "Serilog", "-p", "Acornima", "--skip-conversion"],
                    out RegistryValidationOptions? parsed,
                    out bool showHelp,
                    out string? error),
                Is.True);
            Assert.That(showHelp, Is.False);
            Assert.That(error, Is.Null);
            Assert.That(parsed!.RegistryFilePath, Is.EqualTo("registry.json"));
            Assert.That(parsed.PackageNames, Is.EqualTo(new[] { "Serilog", "Acornima" }).AsCollection);
            Assert.That(parsed.SkipConversion, Is.True);
            Assert.That(parsed.ValidateAll, Is.False);

            Assert.That(
                ValidateCommand.TryParseArgs(["--help"], out _, out bool helpFlag, out _),
                Is.True);
            Assert.That(helpFlag, Is.True);

            Assert.That(
                ValidateCommand.TryParseArgs(["--package", "Serilog"], out _, out _, out string? missingPathError),
                Is.False);
            Assert.That(missingPathError, Does.Contain("Missing required"));

            Assert.That(
                ValidateCommand.TryParseArgs(["registry.json", "--unknown"], out _, out _, out string? unknownError),
                Is.False);
            Assert.That(unknownError, Does.Contain("Unknown option"));
        }
    }
}
