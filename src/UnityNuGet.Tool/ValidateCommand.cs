using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace UnityNuGet.Tool
{
    internal static class ValidateCommand
    {
        internal static bool TryParseArgs(
            string[] args,
            out RegistryValidationOptions? options,
            out bool showHelp,
            out string? errorMessage)
        {
            options = null;
            showHelp = false;
            errorMessage = null;

            string? registryFilePath = null;
            List<string> packageNames = [];
            string? filter = null;
            bool validateAll = false;
            bool skipConversion = false;

            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];

                if (arg is "-h" or "--help")
                {
                    showHelp = true;
                    return true;
                }

                if (arg is "--package" or "-p")
                {
                    if (i + 1 >= args.Length || args[i + 1].StartsWith('-'))
                    {
                        errorMessage = $"Missing value for {arg}.";
                        return false;
                    }

                    packageNames.Add(args[++i]);
                    continue;
                }

                if (arg == "--filter")
                {
                    if (i + 1 >= args.Length || args[i + 1].StartsWith('-'))
                    {
                        errorMessage = "Missing value for --filter.";
                        return false;
                    }

                    filter = args[++i];
                    continue;
                }

                if (arg == "--all")
                {
                    validateAll = true;
                    continue;
                }

                if (arg == "--skip-conversion")
                {
                    skipConversion = true;
                    continue;
                }

                if (arg.StartsWith('-'))
                {
                    errorMessage = $"Unknown option: {arg}";
                    return false;
                }

                if (registryFilePath != null)
                {
                    errorMessage = $"Unexpected positional argument: {arg}";
                    return false;
                }

                registryFilePath = arg;
            }

            if (string.IsNullOrWhiteSpace(registryFilePath))
            {
                errorMessage = "Missing required <registry-json-path> argument.";
                return false;
            }

            options = new RegistryValidationOptions
            {
                RegistryFilePath = registryFilePath,
                PackageNames = packageNames,
                Filter = filter,
                ValidateAll = validateAll,
                SkipConversion = skipConversion,
            };

            return true;
        }

        internal static void PrintUsage(TextWriter writer)
        {
            writer.WriteLine("Usage:");
            writer.WriteLine("  unityNuGet validate <registry-json-path> [options]");
            writer.WriteLine();
            writer.WriteLine("Options:");
            writer.WriteLine("  -p, --package <id>   Exact NuGet package ID to validate online and convert (repeatable)");
            writer.WriteLine("  --filter <regex>     Regex filter for packages to validate online and convert");
            writer.WriteLine("  --all                Validate and convert all non-ignored entries online");
            writer.WriteLine("  --skip-conversion    Run static and minVersion checks without converting packages");
            writer.WriteLine("  -h, --help           Show help");
        }

        internal static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken = default)
        {
            if (!TryParseArgs(args, out RegistryValidationOptions? options, out bool showHelp, out string? errorMessage))
            {
                await Console.Error.WriteLineAsync(errorMessage, cancellationToken);
                PrintUsage(Console.Error);
                return 1;
            }

            if (showHelp)
            {
                PrintUsage(Console.Out);
                return 0;
            }

            using ILoggerFactory loggerFactory = LoggerFactory.Create(builder =>
            {
                builder.AddSimpleConsole(console =>
                {
                    console.SingleLine = true;
                });
                builder.SetMinimumLevel(LogLevel.Warning);
            });

            RegistryValidator validator = new(loggerFactory);
            RegistryValidationResult result = await validator.ValidateAsync(options!, cancellationToken);

            if (!result.IsValid)
            {
                await Console.Error.WriteLineAsync("Validation failed", cancellationToken);
                return 1;
            }

            string summary = result.CheckedPackageCount > 0
                ? $"Validation passed ({result.CheckedPackageCount} package(s) checked online)."
                : "Static validation passed.";
            Console.WriteLine(summary);
            return 0;
        }
    }
}
