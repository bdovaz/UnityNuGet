using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NuGet.Configuration;
using NuGet.Frameworks;
using NuGet.PackageManagement;
using NuGet.Packaging.Core;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;
using UnityNuGet.Npm;
using ILogger = Microsoft.Extensions.Logging.ILogger;
using NuGetCommonLogger = NuGet.Common.ILogger;

namespace UnityNuGet
{
    internal delegate Task<IReadOnlyList<IPackageSearchMetadata>> PackageMetadataFetcher(
        string packageId,
        bool includePrerelease,
        bool includeUnlisted,
        CancellationToken cancellationToken);

    internal delegate Task<bool> NativeLibraryChecker(
        PackageIdentity packageIdentity,
        CancellationToken cancellationToken);

    public sealed class RegistryValidator
    {
        private readonly ILoggerFactory _loggerFactory;
        private readonly ILogger _logger;
        private readonly PackageMetadataFetcher? _metadataFetcherOverride;
        private readonly NativeLibraryChecker? _nativeLibraryCheckerOverride;

        public RegistryValidator(ILoggerFactory? loggerFactory = null)
            : this(loggerFactory, null, null)
        {
        }

        internal RegistryValidator(
            ILoggerFactory? loggerFactory,
            PackageMetadataFetcher? metadataFetcherOverride,
            NativeLibraryChecker? nativeLibraryCheckerOverride)
        {
            _loggerFactory = loggerFactory ?? NullLoggerFactory.Instance;
            _logger = _loggerFactory.CreateLogger("NuGet");
            _metadataFetcherOverride = metadataFetcherOverride;
            _nativeLibraryCheckerOverride = nativeLibraryCheckerOverride;
        }

        internal static RegistryOptions CreateDefaultRegistryOptions(string registryFilePath, string rootPersistentFolder)
        {
            return new RegistryOptions
            {
                RootHttpUrl = new Uri("https://unitynuget-registry.openupm.com/"),
                UnityScope = "org.nuget",
                MinimumUnityVersion = "2019.1",
                PackageNameNuGetPostFix = " (NuGet)",
                PackageKeywords = ["nuget"],
                RegistryFilePath = Path.GetFullPath(registryFilePath),
                RootPersistentFolder = rootPersistentFolder,
                UpdateInterval = TimeSpan.FromMinutes(10),
                TargetFrameworks =
                [
                    new RegistryTargetFramework
                    {
                        Name = "netstandard2.0",
                        DefineConstraints = ["!UNITY_2021_2_OR_NEWER"],
                    },
                    new RegistryTargetFramework
                    {
                        Name = "netstandard2.1",
                        DefineConstraints = ["UNITY_2021_2_OR_NEWER"],
                    },
                ],
                RoslynAnalyzerVersions =
                [
                    new RoslynAnalyzerVersion
                    {
                        Version = new Version(3, 8, 0, 0),
                        DefineConstraints = ["!UNITY_6000_0_OR_NEWER"],
                    },
                    new RoslynAnalyzerVersion
                    {
                        Version = new Version(4, 3, 0, 0),
                        SingleDefineConstraints = ["UNITY_6000_0_OR_NEWER"],
                        DefineConstraints = ["UNITY_6000_0_OR_NEWER"],
                    },
                ],
            };
        }

        internal static string BuildExactPackageFilterRegex(IEnumerable<string> packageIds)
        {
            string pattern = string.Join("|", packageIds.Select(Regex.Escape));
            return $"^(?:{pattern})$";
        }

        public async Task<RegistryValidationResult> ValidateAsync(
            RegistryValidationOptions options,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(options);

            RegistryValidationResult result = new();
            string registryFilePath = Path.GetFullPath(options.RegistryFilePath);

            if (!File.Exists(registryFilePath))
            {
                result.Errors.Add($"Registry file does not exist: `{registryFilePath}`.");
                return result;
            }

            string json;
            try
            {
                json = await File.ReadAllTextAsync(registryFilePath, cancellationToken);
            }
            catch (Exception ex)
            {
                result.Errors.Add($"Failed to read `{registryFilePath}`: {ex.Message}");
                return result;
            }

            List<string> originalPackageNames = [];
            try
            {
                using JsonDocument document = JsonDocument.Parse(json);
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                {
                    result.Errors.Add("Registry JSON root must be an object mapping package IDs to entries.");
                    return result;
                }

                HashSet<string> seenKeys = new(StringComparer.OrdinalIgnoreCase);
                foreach (JsonProperty property in document.RootElement.EnumerateObject())
                {
                    if (string.IsNullOrWhiteSpace(property.Name))
                    {
                        result.Errors.Add("Registry contains an empty package key.");
                        continue;
                    }

                    if (!seenKeys.Add(property.Name))
                    {
                        result.Errors.Add($"Duplicate package key (case-insensitive) in registry.json: `{property.Name}`.");
                    }

                    originalPackageNames.Add(property.Name);
                }
            }
            catch (JsonException ex)
            {
                result.Errors.Add($"Invalid JSON in `{registryFilePath}`: {ex.Message}");
                return result;
            }

            IDictionary<string, RegistryEntry>? deserialized;
            try
            {
                deserialized = JsonSerializer.Deserialize(json, UnityNuGetJsonSerializerContext.Default.IDictionaryStringRegistryEntry);
            }
            catch (Exception ex) when (ex is JsonException or FormatException or ArgumentException)
            {
                result.Errors.Add($"Invalid registry entry schema in `{registryFilePath}`: {ex.Message}");
                return result;
            }

            if (deserialized == null)
            {
                result.Errors.Add($"Registry file `{registryFilePath}` deserialized to null.");
                return result;
            }

            List<string> sortedPackageNames = [.. originalPackageNames.OrderBy(p => p, StringComparer.OrdinalIgnoreCase)];
            for (int i = 0; i < originalPackageNames.Count; i++)
            {
                if (!string.Equals(originalPackageNames[i], sortedPackageNames[i], StringComparison.Ordinal))
                {
                    result.Errors.Add($"Registry keys are not sorted alphabetically (found `{originalPackageNames[i]}`, expected `{sortedPackageNames[i]}`).");
                    break;
                }
            }

            Dictionary<string, RegistryEntry> registryEntries = new(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, RegistryEntry> pair in deserialized)
            {
                registryEntries.TryAdd(pair.Key, pair.Value);
            }

            foreach (string packageName in originalPackageNames)
            {
                if (DotNetHelper.IsNetStandard20Assembly(packageName))
                {
                    result.Errors.Add($"Package `{packageName}` is already included in .NET Standard 2.0 and must not be in registry.json.");
                }

                if (registryEntries.TryGetValue(packageName, out RegistryEntry? entry) && !entry.Ignored && (entry.Version == null || entry.Version.MinVersion == null))
                {
                    result.Errors.Add($"Package `{packageName}` must specify a valid minimum version unless `ignore` is true.");
                }
            }

            HashSet<string>? requestedPackageSet = null;
            if (options.PackageNames.Count > 0)
            {
                requestedPackageSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (string requestedName in options.PackageNames)
                {
                    if (string.IsNullOrWhiteSpace(requestedName))
                    {
                        result.Errors.Add("Package name passed to --package must not be empty.");
                        continue;
                    }

                    if (!registryEntries.ContainsKey(requestedName))
                    {
                        result.Errors.Add($"Requested package `{requestedName}` was not found in `{options.RegistryFilePath}`.");
                        continue;
                    }

                    requestedPackageSet.Add(requestedName);
                }
            }

            Regex? filterRegex = null;
            if (!string.IsNullOrEmpty(options.Filter))
            {
                try
                {
                    filterRegex = new Regex(options.Filter, RegexOptions.IgnoreCase);
                }
                catch (ArgumentException ex)
                {
                    result.Errors.Add($"Invalid --filter regex `{options.Filter}`: {ex.Message}");
                }
            }

            bool onlineRequested = options.ValidateAll || options.PackageNames.Count > 0 || !string.IsNullOrEmpty(options.Filter);
            if (!result.IsValid || !onlineRequested)
            {
                return result;
            }

            List<KeyValuePair<string, RegistryEntry>> targetPackages = [];
            foreach (string packageName in originalPackageNames)
            {
                RegistryEntry entry = registryEntries[packageName];
                bool matches = options.ValidateAll
                    || (requestedPackageSet != null && requestedPackageSet.Contains(packageName))
                    || (filterRegex != null && filterRegex.IsMatch(packageName));

                if (!matches)
                {
                    continue;
                }

                if (entry.Ignored)
                {
                    if (requestedPackageSet != null && requestedPackageSet.Contains(packageName))
                    {
                        result.Information.Add($"Package `{packageName}` has `ignore: true` and was skipped.");
                    }

                    continue;
                }

                targetPackages.Add(new KeyValuePair<string, RegistryEntry>(packageName, entry));
            }

            result.CheckedPackageCount = targetPackages.Count;
            if (targetPackages.Count == 0)
            {
                result.Information.Add("No non-ignored packages matched the validation selection.");
                return result;
            }

            NuGetCommonLogger nuGetLogger = new NuGetRedirectLogger(_logger);
            ISettings settings = Settings.LoadDefaultSettings(root: Directory.GetCurrentDirectory());
            SourceRepositoryProvider sourceRepositoryProvider = new(new PackageSourceProvider(settings), Repository.Provider.GetCoreV3());
            IEnumerable<SourceRepository> sourceRepositories = sourceRepositoryProvider.GetRepositories();
            SourceCacheContext sourceCacheContext = new();

            PackageMetadataFetcher fetchMetadata = _metadataFetcherOverride ?? ((packageId, includePrerelease, includeUnlisted, ct) =>
                GetMetadataFromSourcesAsync(sourceRepositories, sourceCacheContext, nuGetLogger, packageId, includePrerelease, includeUnlisted, ct));

            NativeLibraryChecker checkNativeLibs = _nativeLibraryCheckerOverride ?? ((identity, ct) =>
                HasSupportedNativeLibsAsync(sourceRepositories, settings, sourceCacheContext, nuGetLogger, identity, ct));

            RegistryTargetFramework[] netStandard20Frameworks =
            [
                new RegistryTargetFramework { Name = "netstandard2.0", Framework = FrameworkConstants.CommonFrameworks.NetStandard20 },
            ];
            RegistryTargetFramework[] netStandard21Frameworks =
            [
                new RegistryTargetFramework { Name = "netstandard2.1", Framework = FrameworkConstants.CommonFrameworks.NetStandard21 },
            ];

            foreach ((string packageId, RegistryEntry entry) in targetPackages)
            {
                if (entry.Analyzer)
                {
                    continue;
                }

                IReadOnlyList<IPackageSearchMetadata> metas = await fetchMetadata(
                    packageId,
                    entry.IncludePrerelease,
                    entry.IncludeUnlisted,
                    cancellationToken);

                if (metas.Count == 0)
                {
                    result.Errors.Add($"The package `{packageId}` was not found in any NuGet registry.");
                    continue;
                }

                List<IPackageSearchMetadata> orderedMetas = [.. metas.OrderBy(m => m.Identity.Version)];
                NuGetVersion configuredMinVersion = entry.Version!.MinVersion!;

                PackageIdentity? minNs20 = NuGetHelper.GetMinimumCompatiblePackageIdentity(orderedMetas, netStandard20Frameworks, includeAny: false);
                if (minNs20 != null)
                {
                    if (configuredMinVersion < minNs20.Version)
                    {
                        result.Errors.Add($"Package `{packageId}` specifies minimum version `{configuredMinVersion}`, which is lower than the earliest compatible .NET Standard 2.0 version `{minNs20.Version}`.");
                    }
                    else if (configuredMinVersion > minNs20.Version)
                    {
                        result.Information.Add($"Package `{packageId}` starts at `{configuredMinVersion}`, which is higher than the earliest compatible .NET Standard 2.0 version `{minNs20.Version}`.");
                    }

                    continue;
                }

                PackageIdentity? minNs21 = NuGetHelper.GetMinimumCompatiblePackageIdentity(orderedMetas, netStandard21Frameworks, includeAny: false);
                if (minNs21 != null)
                {
                    if (configuredMinVersion < minNs21.Version)
                    {
                        result.Errors.Add($"Package `{packageId}` specifies minimum version `{configuredMinVersion}`, which is lower than the earliest compatible .NET Standard 2.1 version `{minNs21.Version}`.");
                    }
                    else if (configuredMinVersion > minNs21.Version)
                    {
                        result.Information.Add($"Package `{packageId}` starts at `{configuredMinVersion}`, which is higher than the earliest compatible .NET Standard 2.1 version `{minNs21.Version}`.");
                    }

                    continue;
                }

                bool hasAnyCompatibleGroup = orderedMetas.Any(meta =>
                    entry.Version.Satisfies(meta.Identity.Version) &&
                    NuGetHelper.GetCompatiblePackageDependencyGroups(meta.DependencySets, netStandard20Frameworks, includeAny: true).Any());

                if (!hasAnyCompatibleGroup)
                {
                    bool hasNativeLib = await checkNativeLibs(new PackageIdentity(packageId, configuredMinVersion), cancellationToken);
                    if (!hasNativeLib)
                    {
                        result.Errors.Add($"Package `{packageId}` at version `{configuredMinVersion}` has no compatible .NET Standard 2.0/2.1 target or supported native library.");
                    }
                }
            }

            if (!result.IsValid || options.SkipConversion)
            {
                return result;
            }

            await ValidateConversionAsync(registryFilePath, targetPackages, options.ValidateAll, result, cancellationToken);

            return result;
        }

        private async Task ValidateConversionAsync(
            string registryFilePath,
            List<KeyValuePair<string, RegistryEntry>> targetPackages,
            bool validateAll,
            RegistryValidationResult result,
            CancellationToken cancellationToken)
        {
            string tempFolder = Path.Combine(Path.GetTempPath(), $"unitynuget-validate-{Guid.NewGuid():N}");
            string? previousNugetPackagesEnv = Environment.GetEnvironmentVariable("NUGET_PACKAGES");

            try
            {
                Directory.CreateDirectory(tempFolder);

                RegistryOptions registryOptions = CreateDefaultRegistryOptions(registryFilePath, tempFolder);
                Registry registry = new(_loggerFactory, Options.Create(registryOptions));
                await registry.StartAsync(cancellationToken);

                UnityPackageSigner signer = new(
                    _loggerFactory.CreateLogger<UnityPackageSigner>(),
                    Options.Create(new SigningOptions()));

                NuGetCommonLogger nuGetLogger = new NuGetRedirectLogger(_logger);
                RegistryCache cache = new(
                    registry,
                    tempFolder,
                    registryOptions.RootHttpUrl!,
                    registryOptions.UnityScope!,
                    registryOptions.MinimumUnityVersion!,
                    registryOptions.PackageNameNuGetPostFix!,
                    registryOptions.PackageKeywords,
                    registryOptions.TargetFrameworks!,
                    registryOptions.RoslynAnalyzerVersions!,
                    nuGetLogger,
                    signer)
                {
                    Filter = validateAll ? null : BuildExactPackageFilterRegex(targetPackages.Select(p => p.Key)),
                    OnError = (_, e) => result.Errors.Add(e.Message),
                    OnWarning = (_, e) => result.Warnings.Add(e.Message),
                };

                await cache.Build(cancellationToken);

                foreach ((string packageId, _) in targetPackages)
                {
                    string npmPackageId = $"{registryOptions.UnityScope}.{packageId.ToLowerInvariant()}";
                    NpmPackage? convertedPackage = cache.GetPackage(npmPackageId);
                    if (convertedPackage == null || convertedPackage.Versions.Count == 0)
                    {
                        result.Errors.Add($"Package `{packageId}` did not produce any converted Unity package versions.");
                    }
                }
            }
            finally
            {
                Environment.SetEnvironmentVariable("NUGET_PACKAGES", previousNugetPackagesEnv);

                try
                {
                    if (Directory.Exists(tempFolder))
                    {
                        Directory.Delete(tempFolder, recursive: true);
                    }
                }
                catch
                {
                    // Best-effort temp directory cleanup.
                }
            }
        }

        private static async Task<IReadOnlyList<IPackageSearchMetadata>> GetMetadataFromSourcesAsync(
            IEnumerable<SourceRepository> sourceRepositories,
            SourceCacheContext cache,
            NuGetCommonLogger logger,
            string packageId,
            bool includePrerelease,
            bool includeUnlisted,
            CancellationToken cancellationToken)
        {
            foreach (SourceRepository source in sourceRepositories)
            {
                PackageMetadataResource? resource = await source.GetResourceAsync<PackageMetadataResource>(cancellationToken);
                if (resource == null)
                {
                    continue;
                }

                IEnumerable<IPackageSearchMetadata> metadata = await resource.GetMetadataAsync(
                    packageId,
                    includePrerelease,
                    includeUnlisted,
                    cache,
                    logger,
                    cancellationToken);

                List<IPackageSearchMetadata> list = [.. metadata];
                if (list.Count > 0)
                {
                    return list;
                }
            }

            return [];
        }

        private static async Task<bool> HasSupportedNativeLibsAsync(
            IEnumerable<SourceRepository> sourceRepositories,
            ISettings settings,
            SourceCacheContext cache,
            NuGetCommonLogger logger,
            PackageIdentity packageIdentity,
            CancellationToken cancellationToken)
        {
            using DownloadResourceResult downloadResult = await PackageDownloader.GetDownloadResourceResultAsync(
                sourceRepositories,
                packageIdentity,
                new PackageDownloadContext(cache),
                SettingsUtility.GetGlobalPackagesFolder(settings),
                logger,
                cancellationToken);

            if (downloadResult.Status != DownloadResourceResultStatus.Available || downloadResult.PackageReader == null)
            {
                return false;
            }

            return await NativeLibraries.GetSupportedNativeLibsAsync(downloadResult.PackageReader, logger).AnyAsync(cancellationToken);
        }
    }
}
