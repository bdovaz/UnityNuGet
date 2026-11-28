using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NuGet.Configuration;
using NuGet.Frameworks;
using NuGet.PackageManagement;
using NuGet.Packaging.Core;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;
using UnityNuGet.Npm;

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

    internal sealed class RegistryValidator : IRegistryValidator
    {
        private readonly ILoggerFactory _loggerFactory;
        private readonly ILogger _logger;
        private readonly PackageMetadataFetcher? _metadataFetcherOverride;
        private readonly NativeLibraryChecker? _nativeLibraryCheckerOverride;

        public RegistryValidator(ILoggerFactory loggerFactory) : this(loggerFactory, null, null)
        {
        }

        internal RegistryValidator(
            ILoggerFactory loggerFactory,
            PackageMetadataFetcher? metadataFetcherOverride,
            NativeLibraryChecker? nativeLibraryCheckerOverride)
        {
            _loggerFactory = loggerFactory;
            _logger = _loggerFactory.CreateNuGetLogger();
            _metadataFetcherOverride = metadataFetcherOverride;
            _nativeLibraryCheckerOverride = nativeLibraryCheckerOverride;
        }

        private static RegistryOptions CreateDefaultRegistryOptions(string registryFilePath, string rootPersistentFolder)
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

        public async Task<RegistryValidationResult> Validate(
            RegistryValidationOptions options,
            CancellationToken cancellationToken = default)
        {
            RegistryValidationResult result = new()
            {
                IsValid = true,
            };

            string registryFilePath = Path.GetFullPath(options.RegistryFilePath);

            if (!File.Exists(registryFilePath))
            {
                _logger.LogError(
                    "Registry file does not exist: `{RegistryFilePath}`.",
                    registryFilePath);

                result.IsValid = false;

                return result;
            }

            string json;
            try
            {
                json = await File.ReadAllTextAsync(registryFilePath, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to read `{RegistryFilePath}`",
                    registryFilePath);

                result.IsValid = false;

                return result;
            }

            List<string> originalPackageNames = [];

            try
            {
                using Stream stream = File.OpenRead(registryFilePath);

                using JsonDocument document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

                if (document.RootElement.ValueKind != JsonValueKind.Object)
                {
                    _logger.LogError("Registry JSON root must be an object mapping package IDs to entries.");

                    result.IsValid = false;

                    return result;
                }

                HashSet<string> seenKeys = [with(StringComparer.OrdinalIgnoreCase)];
                foreach (JsonProperty property in document.RootElement.EnumerateObject())
                {
                    if (string.IsNullOrWhiteSpace(property.Name))
                    {
                        _logger.LogError("Registry contains an empty package key.");

                        result.IsValid = false;

                        continue;
                    }

                    if (!seenKeys.Add(property.Name))
                    {
                        _logger.LogError(
                            "Duplicate package key (case-insensitive) in registry.json: `{PackageId}`.",
                            property.Name);

                        result.IsValid = false;
                    }

                    originalPackageNames.Add(property.Name);
                }
            }
            catch (JsonException ex)
            {
                _logger.LogError(
                    ex,
                    "Invalid JSON in `{RegistryFilePath}`",
                    registryFilePath);

                result.IsValid = false;

                return result;
            }

            IDictionary<string, RegistryEntry>? deserialized;

            try
            {
                using Stream stream = File.OpenRead(registryFilePath);

                deserialized = await JsonSerializer.DeserializeAsync(
                    stream,
                    UnityNuGetJsonSerializerContext.Default.IDictionaryStringRegistryEntry,
                    cancellationToken);
            }
            catch (Exception ex) when (ex is JsonException or FormatException or ArgumentException)
            {
                _logger.LogError(
                    ex,
                    "Invalid registry entry schema in `{RegistryFilePath}`",
                    registryFilePath);

                result.IsValid = false;

                return result;
            }

            if (deserialized == null)
            {
                _logger.LogError(
                    "Registry file `{RegistryFilePath}` deserialized to null.",
                    registryFilePath);

                result.IsValid = false;

                return result;
            }

            List<string> sortedPackageNames = [.. originalPackageNames.OrderBy(p => p, StringComparer.OrdinalIgnoreCase)];

            for (int i = 0; i < originalPackageNames.Count; i++)
            {
                if (!string.Equals(originalPackageNames[i], sortedPackageNames[i], StringComparison.Ordinal))
                {
                    _logger.LogError(
                        "Registry keys are not sorted alphabetically (found `{OriginalPackage}`, expected `{SortedPackage}`).",
                        originalPackageNames[i],
                        sortedPackageNames[i]);

                    result.IsValid = false;

                    break;
                }
            }

            Dictionary<string, RegistryEntry> registryEntries = [with(StringComparer.OrdinalIgnoreCase)];

            foreach (KeyValuePair<string, RegistryEntry> pair in deserialized)
            {
                registryEntries.TryAdd(pair.Key, pair.Value);
            }

            foreach (string packageName in originalPackageNames)
            {
                if (!registryEntries.TryGetValue(packageName, out RegistryEntry? entry) || entry == null)
                {
                    _logger.LogError(
                        "Package `{PackageName}` entry must not be null.",
                        packageName);

                    result.IsValid = false;

                    continue;
                }

                if (!entry.Ignored && DotNetHelper.IsNetStandard20Assembly(packageName))
                {
                    _logger.LogError(
                        "Package `{PackageName}` is already included in .NET Standard 2.0 and must not be in registry.json.",
                        packageName);

                    result.IsValid = false;
                }

                if (!entry.Ignored && (entry.Version == null || entry.Version.MinVersion == null))
                {
                    _logger.LogError(
                        "Package `{PackageName}` must specify a valid minimum version unless `ignore` is true.",
                        packageName);

                    result.IsValid = false;
                }
            }

            HashSet<string>? requestedPackageSet = null;

            if (options.PackageNames.Count > 0)
            {
                requestedPackageSet = [with(StringComparer.OrdinalIgnoreCase)];
                foreach (string requestedName in options.PackageNames)
                {
                    if (string.IsNullOrWhiteSpace(requestedName))
                    {
                        _logger.LogError("Package name passed to --package must not be empty.");

                        result.IsValid = false;

                        continue;
                    }

                    if (!registryEntries.ContainsKey(requestedName))
                    {
                        _logger.LogError(
                            "Requested package `{RequestedName}` was not found in `{RegistryFilePath}`.",
                            requestedName,
                            options.RegistryFilePath);

                        result.IsValid = false;

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
                    _logger.LogError(ex, "Invalid --filter regex `{Filter}`", options.Filter);

                    result.IsValid = false;
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
                        if (_logger.IsEnabled(LogLevel.Information))
                        {
                            _logger.LogInformation(
                                "Package `{PackageName}` has `ignore: true` and was skipped.",
                                packageName);
                        }
                    }

                    continue;
                }

                targetPackages.Add(new KeyValuePair<string, RegistryEntry>(packageName, entry));
            }

            result.CheckedPackageCount = targetPackages.Count;

            if (targetPackages.Count == 0)
            {
                _logger.LogInformation("No non-ignored packages matched the validation selection.");

                return result;
            }

            NuGet.Common.ILogger nuGetLogger = new NuGetRedirectLogger(_logger);
            ISettings settings = Settings.LoadDefaultSettings(root: Directory.GetCurrentDirectory());
            SourceRepositoryProvider sourceRepositoryProvider = new(new PackageSourceProvider(settings), Repository.Provider.GetCoreV3());
            IEnumerable<SourceRepository> sourceRepositories = sourceRepositoryProvider.GetRepositories();
            using SourceCacheContext sourceCacheContext = new();

            PackageMetadataFetcher fetchMetadata = _metadataFetcherOverride ?? ((packageId, includePrerelease, includeUnlisted, ct) =>
                GetMetadataFromSourcesAsync(sourceRepositories, sourceCacheContext, nuGetLogger, packageId, includePrerelease, includeUnlisted, ct));

            NativeLibraryChecker checkNativeLibs = _nativeLibraryCheckerOverride ?? ((identity, ct) =>
                HasSupportedNativeLibsAsync(sourceRepositories, settings, sourceCacheContext, nuGetLogger, _logger, identity, ct));

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
                    _logger.LogError(
                        "The package `{PackageId}` was not found in any NuGet registry.",
                        packageId);

                    result.IsValid = false;

                    continue;
                }

                List<IPackageSearchMetadata> orderedMetas = [.. metas.OrderBy(m => m.Identity.Version)];
                NuGetVersion configuredMinVersion = entry.Version!.MinVersion!;

                PackageIdentity? minNetStandard20 = NuGetHelper.GetMinimumCompatiblePackageIdentity(orderedMetas, netStandard20Frameworks, includeAny: false);

                if (minNetStandard20 != null)
                {
                    if (configuredMinVersion < minNetStandard20.Version)
                    {
                        _logger.LogError(
                            "Package `{PackageId}` specifies minimum version `{ConfiguredMinVersion}`, which is lower than the earliest compatible .NET Standard 2.0 version `{MinNetStandard20Version}`.",
                            packageId,
                            configuredMinVersion,
                            minNetStandard20.Version);

                        result.IsValid = false;
                    }
                    else if (configuredMinVersion > minNetStandard20.Version)
                    {
                        if (_logger.IsEnabled(LogLevel.Information))
                        {
                            _logger.LogInformation(
                                "Package `{PackageId}` starts at `{ConfiguredMinVersion}`, which is higher than the earliest compatible .NET Standard 2.0 version `{MinNetStandard20Version}`.",
                                packageId,
                                configuredMinVersion,
                                minNetStandard20.Version);
                        }
                    }

                    continue;
                }

                PackageIdentity? minNetStandard21 = NuGetHelper.GetMinimumCompatiblePackageIdentity(orderedMetas, netStandard21Frameworks, includeAny: false);

                if (minNetStandard21 != null)
                {
                    if (configuredMinVersion < minNetStandard21.Version)
                    {
                        _logger.LogError(
                            "Package `{PackageId}` specifies minimum version `{ConfiguredMinVersion}`, which is lower than the earliest compatible .NET Standard 2.1 version `{MinNetStandard21Version}`.",
                            packageId,
                            configuredMinVersion,
                            minNetStandard21.Version);

                        result.IsValid = false;
                    }
                    else if (configuredMinVersion > minNetStandard21.Version)
                    {
                        if (_logger.IsEnabled(LogLevel.Information))
                        {
                            _logger.LogInformation(
                                "Package `{PackageId}` starts at `{ConfiguredMinVersion}`, which is higher than the earliest compatible .NET Standard 2.1 version `{MinNetStandard21Version}`.",
                                packageId,
                                configuredMinVersion,
                                minNetStandard21.Version);
                        }
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
                        _logger.LogError(
                            "Package `{PackageId}` at version `{ConfiguredMinVersion}` has no compatible .NET Standard 2.0/2.1 target or supported native library.",
                            packageId,
                            configuredMinVersion);

                        result.IsValid = false;
                    }
                }
            }

            if (!result.IsValid || options.SkipConversion)
            {
                return result;
            }

            bool hasConversionErrors = await ValidateConversionAsync(registryFilePath, targetPackages, options.ValidateAll, cancellationToken);
            result.IsValid = !hasConversionErrors;

            return result;
        }

        internal static bool HasConvertedVersion(NpmPackage? convertedPackage)
        {
            if (convertedPackage == null)
            {
                return false;
            }

            foreach (NpmPackageVersion version in convertedPackage.Versions.Values)
            {
                if (!string.IsNullOrEmpty(version.Distribution.Shasum))
                {
                    return true;
                }
            }

            return false;
        }

        private async Task<bool> ValidateConversionAsync(
            string registryFilePath,
            List<KeyValuePair<string, RegistryEntry>> targetPackages,
            bool validateAll,
            CancellationToken cancellationToken)
        {
            bool hasErrors = false;

            string tempFolder = Path.Combine(Path.GetTempPath(), $"unitynuget-validate-{Guid.NewGuid():N}");
            string? previousNugetPackagesEnv = Environment.GetEnvironmentVariable("NUGET_PACKAGES");

            try
            {
                Directory.CreateDirectory(tempFolder);
                Environment.SetEnvironmentVariable("NUGET_PACKAGES", Path.Combine(tempFolder, ".nuget"));

                RegistryOptions registryOptions = CreateDefaultRegistryOptions(registryFilePath, tempFolder);
                Registry registry = new(_loggerFactory, Options.Create(registryOptions));

                await registry.StartAsync(cancellationToken);

                UnityPackageSigner signer = new(
                    _loggerFactory.CreateLogger<UnityPackageSigner>(),
                    Options.Create(new SigningOptions()));

                NuGet.Common.ILogger nuGetLogger = new NuGetRedirectLogger(_logger);
                using RegistryCache cache = new(
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
                    _loggerFactory.CreateLogger<RegistryCache>(),
                    signer)
                {
                    Filter = validateAll ? null : BuildExactPackageFilterRegex(targetPackages.Select(p => p.Key)),
                };

                if (!await cache.Build(cancellationToken))
                {
                    hasErrors = true;
                }

                foreach ((string packageId, _) in targetPackages)
                {
                    string npmPackageId = $"{registryOptions.UnityScope}.{packageId.ToLowerInvariant()}";
                    NpmPackage? convertedPackage = cache.GetPackage(npmPackageId);

                    if (!HasConvertedVersion(convertedPackage))
                    {
                        _logger.LogError(
                            "Package `{PackageId}` did not produce any converted Unity package versions.",
                            packageId);

                        hasErrors = true;
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

            return hasErrors;
        }

        private static async Task<IReadOnlyList<IPackageSearchMetadata>> GetMetadataFromSourcesAsync(
            IEnumerable<SourceRepository> sourceRepositories,
            SourceCacheContext cache,
            NuGet.Common.ILogger nuGetLogger,
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
                    nuGetLogger,
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
            NuGet.Common.ILogger nuGetLogger,
            ILogger logger,
            PackageIdentity packageIdentity,
            CancellationToken cancellationToken)
        {
            using DownloadResourceResult downloadResult = await PackageDownloader.GetDownloadResourceResultAsync(
                sourceRepositories,
                packageIdentity,
                new PackageDownloadContext(cache),
                SettingsUtility.GetGlobalPackagesFolder(settings),
                nuGetLogger,
                cancellationToken);

            if (downloadResult.Status != DownloadResourceResultStatus.Available || downloadResult.PackageReader == null)
            {
                return false;
            }

            return await NativeLibraries.GetSupportedNativeLibsAsync(downloadResult.PackageReader, logger).AnyAsync(cancellationToken);
        }
    }
}
