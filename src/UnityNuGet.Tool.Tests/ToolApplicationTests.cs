using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using NUnit.Framework;

namespace UnityNuGet.Tool.Tests
{
    public class ToolApplicationTests
    {
        [Test]
        public async Task RootCommandUsesSelectedModuleServicesAndInvokesCacheUpdater()
        {
            string workingFolder = Path.Combine(Path.GetTempPath(), "UnityNuGet.Tool.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(workingFolder);
            string registryFilePath = Path.Combine(workingFolder, "registry.json");
            await File.WriteAllTextAsync(registryFilePath, "{}");

            Mock<IRegistryCacheUpdater> registryCacheUpdater = new();
            registryCacheUpdater
                .Setup(updater => updater.Execute(It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);

            try
            {
                int exitCode = await ToolApplication.RunAsync(
                    [],
                    services =>
                    {
                        services.AddSingleton(registryCacheUpdater.Object);
                        services.Configure<RegistryOptions>(options =>
                        {
                            options.RootHttpUrl = new Uri("https://packages.example.test");
                            options.UnityScope = "org.nuget";
                            options.MinimumUnityVersion = "2019.1";
                            options.PackageNameNuGetPostFix = string.Empty;
                            options.RegistryFilePath = registryFilePath;
                            options.RootPersistentFolder = workingFolder;
                            options.UpdateInterval = TimeSpan.FromMinutes(10);
                            options.TargetFrameworks =
                            [
                                new RegistryTargetFramework
                                {
                                    Name = "netstandard2.0",
                                    DefineConstraints = [],
                                },
                            ];
                            options.RoslynAnalyzerVersions =
                            [
                                new RoslynAnalyzerVersion
                                {
                                    Version = new Version(4, 0),
                                    DefineConstraints = [],
                                },
                            ];
                        });
                    });

                Assert.That(exitCode, Is.Zero);
                registryCacheUpdater.Verify(
                    updater => updater.Execute(It.IsAny<CancellationToken>()),
                    Times.Once);
            }
            finally
            {
                Directory.Delete(workingFolder, recursive: true);
            }
        }

        [Test]
        public async Task ValidateHelpStartsAndStopsHostedServicesWithoutResolvingValidator()
        {
            HostedServiceSpy hostedService = new();
            Mock<IRegistryValidator> registryValidator = new();
            registryValidator
                .Setup(validator => validator.Validate(It.IsAny<RegistryValidationOptions>(), It.IsAny<CancellationToken>()))
                .Throws(new AssertionException("Help must not invoke the registry validator."));

            int exitCode = await ToolApplication.RunAsync(
                ["validate", "--help"],
                services =>
                {
                    services.AddSingleton<IHostedService>(hostedService);
                    services.AddSingleton(registryValidator.Object);
                });

            using (Assert.EnterMultipleScope())
            {
                Assert.That(exitCode, Is.Zero);
                Assert.That(hostedService.StartCount, Is.EqualTo(1));
                Assert.That(hostedService.StopCount, Is.EqualTo(1));
            }

            registryValidator.Verify(
                validator => validator.Validate(It.IsAny<RegistryValidationOptions>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Test]
        public async Task ValidateCommandUsesSelectedModuleServicesAndPassesOptionsToValidator()
        {
            HostedServiceSpy hostedService = new();
            Mock<IRegistryValidator> registryValidator = new();
            RegistryValidationOptions? receivedOptions = null;
            registryValidator
                .Setup(validator => validator.Validate(It.IsAny<RegistryValidationOptions>(), It.IsAny<CancellationToken>()))
                .Callback<RegistryValidationOptions, CancellationToken>((options, _) => receivedOptions = options)
                .ReturnsAsync(new RegistryValidationResult { IsValid = true });

            int exitCode = await ToolApplication.RunAsync(
                ["validate", "packages.json", "--package", "Example.Package", "--skip-conversion"],
                services =>
                {
                    services.AddSingleton<IHostedService>(hostedService);
                    services.AddSingleton(registryValidator.Object);
                });

            using (Assert.EnterMultipleScope())
            {
                Assert.That(exitCode, Is.Zero);
                Assert.That(hostedService.StartCount, Is.EqualTo(1));
                Assert.That(hostedService.StopCount, Is.EqualTo(1));
                Assert.That(receivedOptions, Is.Not.Null);
                Assert.That(receivedOptions!.RegistryFilePath, Is.EqualTo("packages.json"));
                Assert.That(receivedOptions.PackageNames, Is.EqualTo(["Example.Package"]));
                Assert.That(receivedOptions.SkipConversion, Is.True);
            }

            registryValidator.Verify(
                validator => validator.Validate(It.IsAny<RegistryValidationOptions>(), It.IsAny<CancellationToken>()),
                Times.Once);
        }

        private sealed class HostedServiceSpy : IHostedService
        {
            public int StartCount { get; private set; }

            public int StopCount { get; private set; }

            public Task StartAsync(CancellationToken cancellationToken)
            {
                StartCount++;
                return Task.CompletedTask;
            }

            public Task StopAsync(CancellationToken cancellationToken)
            {
                StopCount++;
                return Task.CompletedTask;
            }
        }
    }
}
