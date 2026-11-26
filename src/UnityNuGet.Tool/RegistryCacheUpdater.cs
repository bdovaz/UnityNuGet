using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace UnityNuGet.Tool
{
    internal sealed class RegistryCacheUpdater(
        Registry registry,
        RegistryCacheSingleton currentRegistryCache,
        ILogger<RegistryCacheUpdater> logger,
        ILogger<RegistryCache> registryCacheLogger,
        IOptions<RegistryOptions> registryOptionsAccessor,
        UnityPackageSigner unityPackageSigner) : IRegistryCacheUpdater
    {
        private readonly Registry _registry = registry;
        private readonly RegistryCacheSingleton _currentRegistryCache = currentRegistryCache;
        private readonly ILogger _logger = logger;
        private readonly UnityPackageSigner _unityPackageSigner = unityPackageSigner;
        private readonly RegistryOptions _registryOptions = registryOptionsAccessor.Value;

        public async Task<bool> Execute(CancellationToken cancellationToken = default)
        {
            try
            {
                _logger.LogInformation("Starting to update RegistryCache");

                var newRegistryCache = new RegistryCache(
                    _registry,
                    _currentRegistryCache.UnityPackageFolder!,
                    _currentRegistryCache.ServerUri!,
                    _registryOptions.UnityScope!,
                    _registryOptions.MinimumUnityVersion!,
                    _registryOptions.PackageNameNuGetPostFix!,
                    _registryOptions.PackageKeywords!,
                    _registryOptions.TargetFrameworks!,
                    _registryOptions.RoslynAnalyzerVersions!,
                    _currentRegistryCache.NuGetRedirectLogger!,
                    registryCacheLogger,
                    _unityPackageSigner)
                {
                    Filter = _registryOptions.Filter,
                    OnProgress = (_, e) =>
                    {
                        _currentRegistryCache.ProgressTotalPackageCount = e.Total;
                        _currentRegistryCache.ProgressPackageIndex = e.Current;
                    }
                };

                return await newRegistryCache.Build(cancellationToken);
            }
            catch (TaskCanceledException)
            {
                _logger.LogInformation("RegistryCache update canceled");

                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while building a new registry cache");

                return false;
            }
        }
    }
}
