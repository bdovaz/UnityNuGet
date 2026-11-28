using System;
using System.CommandLine;
using Microsoft.Extensions.Logging;

namespace UnityNuGet.Tool.Commands
{
    internal sealed class ValidateCommand : Command
    {
        public ValidateCommand(Func<ILogger<ValidateCommand>> loggerFactory, Func<IRegistryValidator> registryValidatorFactory) : base("validate", "Validate registry.json entries without requiring appsettings.json")
        {
            Argument<string> registryFilePathArgument = new("registryFilePath")
            {
                Description = "Path to the registry.json file to validate."
            };

            Arguments.Add(registryFilePathArgument);

            Option<string[]> packageOption = new("--package")
            {
                Description = "Exact NuGet package ID to validate online and convert (repeatable)."
            };
            packageOption.Aliases.Add("-p");

            Option<string> filterOption = new("--filter")
            {
                Description = "Regex filter for packages to validate online and convert."
            };
            Option<bool> allOption = new("--all")
            {
                Description = "Validate and convert all non-ignored entries online."
            };
            Option<bool> skipConversionOption = new("--skip-conversion")
            {
                Description = "Run static and minVersion checks without converting packages."
            };

            Options.Add(packageOption);
            Options.Add(filterOption);
            Options.Add(allOption);
            Options.Add(skipConversionOption);

            SetAction(async (parseResult, cancellationToken) =>
            {
                ILogger<ValidateCommand> logger = loggerFactory();
                IRegistryValidator registryValidator = registryValidatorFactory();
                string? registryFilePath = parseResult.GetValue(registryFilePathArgument);

                string[]? packageNames = parseResult.GetValue(packageOption);
                string? filter = parseResult.GetValue(filterOption);
                bool validateAll = parseResult.GetValue(allOption);
                bool skipConversion = parseResult.GetValue(skipConversionOption);

                RegistryValidationOptions options = new()
                {
                    RegistryFilePath = registryFilePath!,
                    PackageNames = packageNames ?? [],
                    Filter = filter,
                    ValidateAll = validateAll,
                    SkipConversion = skipConversion,
                };

                RegistryValidationResult result = await registryValidator.Validate(options, cancellationToken);

                if (!result.IsValid)
                {
                    logger.LogError("Validation failed.");

                    return 1;
                }

                if (result.CheckedPackageCount > 0)
                {
                    if (logger.IsEnabled(LogLevel.Information))
                    {
                        logger.LogInformation(
                            "Validation passed ({CheckedPackageCount} package(s) checked online).",
                            result.CheckedPackageCount);
                    }
                }
                else
                {
                    logger.LogInformation("Static validation passed.");
                }

                return 0;
            });
        }
    }
}
