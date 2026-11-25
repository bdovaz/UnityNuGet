using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace UnityNuGet.Tool
{
    internal sealed class RegistryCacheUpdater(
        Registry registry,
        RegistryCacheSingleton currentRegistryCache,
        IHostApplicationLifetime hostApplicationLifetime,
        ILogger<RegistryCacheUpdater> logger,
        ILogger<RegistryCache> registryCacheLogger,
        IOptions<RegistryOptions> registryOptionsAccessor,
        UnityPackageSigner unityPackageSigner) : BackgroundService
    {
        private const int SuccessExitCode = 0;
        private const int FailureExitCode = 1;

        private readonly Registry _registry = registry;
        private readonly RegistryCacheSingleton _currentRegistryCache = currentRegistryCache;
        private readonly IHostApplicationLifetime _hostApplicationLifetime = hostApplicationLifetime;
        private readonly ILogger _logger = logger;
        private readonly UnityPackageSigner _unityPackageSigner = unityPackageSigner;
        private readonly RegistryOptions _registryOptions = registryOptionsAccessor.Value;

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            int exitCode = SuccessExitCode;

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

                    bool buildSucceeded = await newRegistryCache.Build(stoppingToken);

                    exitCode = buildSucceeded ? SuccessExitCode : FailureExitCode;

                    break;
                }
            }
            catch (TaskCanceledException)
            {
                _logger.LogInformation("RegistryCache update canceled");

                exitCode = FailureExitCode;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while building a new registry cache");

                exitCode = FailureExitCode;
            }

            Environment.ExitCode = exitCode;

            _hostApplicationLifetime.StopApplication();
        }
    }
}
