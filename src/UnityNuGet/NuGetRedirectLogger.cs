using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NuGet.Common;
using ILogger = Microsoft.Extensions.Logging.ILogger;
using LogLevel = NuGet.Common.LogLevel;

namespace UnityNuGet
{
    /// <summary>
    /// A NuGet logger redirecting to a <see cref="ILogger"/>
    /// </summary>
    public class NuGetRedirectLogger(ILogger logger) : LoggerBase
    {
        public ILogger Logger { get; } = logger;

        public override void Log(ILogMessage message)
        {
            Microsoft.Extensions.Logging.LogLevel logLevel = GetLogLevel(message.Level);

            if (Logger.IsEnabled(logLevel))
            {
                Logger.Log(logLevel, "{Message}", message.Message);
            }
        }

        public override Task LogAsync(ILogMessage message)
        {
            Log(message);

            return Task.CompletedTask;
        }

        private static Microsoft.Extensions.Logging.LogLevel GetLogLevel(LogLevel logLevel)
        {
            return logLevel switch
            {
                LogLevel.Debug => Microsoft.Extensions.Logging.LogLevel.Debug,
                LogLevel.Verbose => Microsoft.Extensions.Logging.LogLevel.Trace,
                LogLevel.Information => Microsoft.Extensions.Logging.LogLevel.Information,
                LogLevel.Minimal => Microsoft.Extensions.Logging.LogLevel.Information,
                LogLevel.Warning => Microsoft.Extensions.Logging.LogLevel.Warning,
                LogLevel.Error => Microsoft.Extensions.Logging.LogLevel.Error,
                _ => throw new ArgumentOutOfRangeException(nameof(logLevel), logLevel, null),
            };
        }
    }
}
