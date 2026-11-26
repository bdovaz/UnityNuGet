using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Moq;
using NUnit.Framework;
using UnityNuGet.Tool.Commands;

namespace UnityNuGet.Tool.Tests
{
    public class ValidateCommandTests
    {
        [Test]
        public async Task InvokePassesParsedOptionsToValidator()
        {
            Mock<IRegistryValidator> registryValidator = new();
            RegistryValidationOptions? capturedOptions = null;
            registryValidator
                .Setup(validator => validator.Validate(It.IsAny<RegistryValidationOptions>(), It.IsAny<CancellationToken>()))
                .Callback<RegistryValidationOptions, CancellationToken>((options, _) => capturedOptions = options)
                .ReturnsAsync(new RegistryValidationResult
                {
                    IsValid = true,
                    CheckedPackageCount = 2,
                });

            FakeLogger<ValidateCommand> logger = new();
            ValidateCommand command = new(logger, registryValidator.Object);

            int exitCode = await command.Parse([
                "registry.json",
                "--package", "Package.One",
                "-p", "Package.Two",
                "--filter", "^Package\\.",
                "--all",
                "--skip-conversion",
            ]).InvokeAsync();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(exitCode, Is.Zero);
                Assert.That(capturedOptions, Is.Not.Null);
                Assert.That(capturedOptions!.RegistryFilePath, Is.EqualTo("registry.json"));
                Assert.That(capturedOptions.PackageNames, Is.EquivalentTo(new[] { "Package.One", "Package.Two" }));
                Assert.That(capturedOptions.Filter, Is.EqualTo("^Package\\."));
                Assert.That(capturedOptions.ValidateAll, Is.True);
                Assert.That(capturedOptions.SkipConversion, Is.True);
            }

            registryValidator.Verify(
                validator => validator.Validate(It.IsAny<RegistryValidationOptions>(), It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Test]
        public async Task InvokeReturnsFailureAndLogsErrorWhenValidationFails()
        {
            Mock<IRegistryValidator> registryValidator = new();
            registryValidator
                .Setup(validator => validator.Validate(It.IsAny<RegistryValidationOptions>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new RegistryValidationResult
                {
                    IsValid = false,
                    CheckedPackageCount = 3,
                });

            FakeLogger<ValidateCommand> logger = new();
            ValidateCommand command = new(logger, registryValidator.Object);

            int exitCode = await command.Parse(["registry.json"]).InvokeAsync();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(exitCode, Is.EqualTo(1));
                Assert.That(logger.LatestRecord.Level, Is.EqualTo(LogLevel.Error));
                Assert.That(logger.LatestRecord.Message, Is.EqualTo("Validation failed."));
            }
        }

        [Test]
        public async Task InvokeLogsOnlineValidationCountWhenCheckedPackageCountIsPositive()
        {
            Mock<IRegistryValidator> registryValidator = new();
            registryValidator
                .Setup(validator => validator.Validate(It.IsAny<RegistryValidationOptions>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new RegistryValidationResult
                {
                    IsValid = true,
                    CheckedPackageCount = 3,
                });

            FakeLogger<ValidateCommand> logger = new();
            ValidateCommand command = new(logger, registryValidator.Object);

            int exitCode = await command.Parse(["registry.json"]).InvokeAsync();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(exitCode, Is.Zero);
                Assert.That(logger.LatestRecord.Level, Is.EqualTo(LogLevel.Information));
                Assert.That(logger.LatestRecord.Message, Is.EqualTo("Validation passed (3 package(s) checked online)."));
            }
        }

        [Test]
        public async Task InvokeLogsStaticValidationPassedWhenCheckedPackageCountIsZero()
        {
            Mock<IRegistryValidator> registryValidator = new();
            registryValidator
                .Setup(validator => validator.Validate(It.IsAny<RegistryValidationOptions>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new RegistryValidationResult
                {
                    IsValid = true,
                    CheckedPackageCount = 0,
                });

            FakeLogger<ValidateCommand> logger = new();
            ValidateCommand command = new(logger, registryValidator.Object);

            int exitCode = await command.Parse(["registry.json"]).InvokeAsync();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(exitCode, Is.Zero);
                Assert.That(logger.LatestRecord.Level, Is.EqualTo(LogLevel.Information));
                Assert.That(logger.LatestRecord.Message, Is.EqualTo("Static validation passed."));
            }
        }
    }
}
