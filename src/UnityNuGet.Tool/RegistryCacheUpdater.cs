using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace UnityNuGet.Server
{
    internal sealed class RegistryCacheUpdater(Registry registry, RegistryCacheSingleton currentRegistryCache, IHostApplicationLifetime hostApplicationLifetime, ILogger<RegistryCacheUpdater> logger, IOptions<RegistryOptions> registryOptionsAccessor) : BackgroundService
    {
        private readonly Registry _registry = registry;
        private readonly RegistryCacheSingleton _currentRegistryCache = currentRegistryCache;
        private readonly IHostApplicationLifetime _hostApplicationLifetime = hostApplicationLifetime;
        private readonly ILogger _logger = logger;
        private readonly RegistryOptions _registryOptions = registryOptionsAccessor.Value;

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    _logger.LogInformation("Starting to update RegistryCache");

                    var newRegistryCache = new RegistryCache(
                        _registry,
                        _currentRegistryCache.UnityPackageFolder!,
                        _currentRegistryCache.ServerUri!,
                        _registryOptions.UnityScope!,
                        _registryOptions.MinimumUnityVersion!,
                        _registryOptions.PackageNameNuGetPostFix!,
                        _registryOptions.TargetFrameworks!,
                        _registryOptions.RoslynAnalyzerVersions!,
                        _currentRegistryCache.NuGetRedirectLogger!)
                    {
                        Filter = _registryOptions.Filter,
                        // Update progress
                        OnProgress = (current, total) =>
                        {
                            _currentRegistryCache.ProgressTotalPackageCount = total;
                            _currentRegistryCache.ProgressPackageIndex = current;
                        },
                        OnInformation = message => _logger.LogInformation("{Message}", message),
                        OnWarning = message => _logger.LogWarning("{Message}", message),
                        OnError = message => _logger.LogError("{Message}", message)
                    };

                    await newRegistryCache.Build(stoppingToken);

                    break;
                }
            }
            catch (TaskCanceledException)
            {
                string message = "RegistryCache update canceled";

                _logger.LogInformation("{Message}", message);
            }
            catch (Exception ex)
            {
                string message = "Error while building a new registry cache";

                _logger.LogError(ex, "{Message}", message);
            }

            _hostApplicationLifetime.StopApplication();
        }
    }
}
