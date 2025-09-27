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
            int exitCode = 0;

            try
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    _logger.LogInformation("Starting to update RegistryCache");

                    int errorCount = 0;

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
                        _currentRegistryCache.NuGetRedirectLogger!)
                    {
                        Filter = _registryOptions.Filter,
                        // Update progress
                        OnProgress = (_, e) =>
                        {
                            _currentRegistryCache.ProgressTotalPackageCount = e.Total;
                            _currentRegistryCache.ProgressPackageIndex = e.Current;
                        },
                        OnError = (_, _) => errorCount++
                    };

                    await newRegistryCache.Build(stoppingToken);

                    exitCode = errorCount == 0 ? 0 : -1;

                    break;
                }
            }
            catch (TaskCanceledException)
            {
                string message = "RegistryCache update canceled";

                _logger.LogInformation("{Message}", message);

                exitCode = -1;
            }
            catch (Exception ex)
            {
                string message = "Error while building a new registry cache";

                _logger.LogError(ex, "{Message}", message);

                exitCode = -1;
            }

            Environment.ExitCode = exitCode;

            _hostApplicationLifetime.StopApplication();
        }
    }
}
