using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace UnityNuGet
{
    public sealed class UnityPackageSigner(ILogger<UnityPackageSigner> logger, IOptions<SigningOptions> signingOptionsAccessor)
    {
        private const string UpmServiceAccountKeyIdEnvironmentVariableName = "UPM_SERVICE_ACCOUNT_KEY_ID";
        private const string UpmServiceAccountKeySecretEnvironmentVariableName = "UPM_SERVICE_ACCOUNT_KEY_SECRET";

        private readonly ILogger<UnityPackageSigner> _logger = logger;

        private readonly SigningOptions _signingOptions = signingOptionsAccessor.Value;

        public bool CanSign()
        {
            return !string.IsNullOrEmpty(_signingOptions.UpmServiceAccountKeyId)
                && !string.IsNullOrEmpty(_signingOptions.UpmServiceAccountKeySecret)
                && _signingOptions.UnityOrganizationId.HasValue
                && File.Exists(_signingOptions.UpmExecutableFilePath);
        }

        public async Task Sign(string packageDirectoryPath, string destinationDirectoryPath, CancellationToken cancellationToken = default)
        {
            ProcessStartInfo processStartInfo = new()
            {
                FileName = _signingOptions.UpmExecutableFilePath,
                Arguments = string.Join(' ', new string[]
                {
                    "pack",
                    packageDirectoryPath,
                    "--organization-id",
                    _signingOptions.UnityOrganizationId!.Value.ToString(),
                    "--destination",
                    destinationDirectoryPath
                }),
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            processStartInfo.Environment.Add(UpmServiceAccountKeyIdEnvironmentVariableName, _signingOptions.UpmServiceAccountKeyId);
            processStartInfo.Environment.Add(UpmServiceAccountKeySecretEnvironmentVariableName, _signingOptions.UpmServiceAccountKeySecret);

            Process process = new()
            {
                StartInfo = processStartInfo,
                EnableRaisingEvents = true
            };
            process.OutputDataReceived += (sender, args) =>
            {
                if (!string.IsNullOrEmpty(args.Data))
                {
                    if (_logger.IsEnabled(LogLevel.Information))
                    {
                        _logger.LogInformation("{Message}", args.Data);
                    }
                }
            };
            process.ErrorDataReceived += (sender, args) =>
            {
                if (!string.IsNullOrEmpty(args.Data))
                {
                    if (_logger.IsEnabled(LogLevel.Error))
                    {
                        _logger.LogError("{Message}", args.Data);
                    }
                }
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await process.WaitForExitAsync(cancellationToken);
        }
    }
}
