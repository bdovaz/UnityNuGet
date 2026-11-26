using System;
using Microsoft.Extensions.Logging;

namespace UnityNuGet.Server
{
    internal sealed class RegistryCacheReportLogger(RegistryCacheReport report, string categoryName) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel)
        {
            return categoryName == RegistryCache.LoggerCategoryName
                && logLevel >= LogLevel.Information
                && logLevel != LogLevel.None;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel))
            {
                string message = formatter(state, exception);

                if (exception != null)
                {
                    message = $"{message}. Reason: {exception}";
                }

                AddLog(logLevel, message);
            }
        }

        private void AddLog(LogLevel logLevel, string message)
        {
            switch (logLevel)
            {
                case LogLevel.Information:
                    report.AddInformation(message);
                    break;
                case LogLevel.Warning:
                    report.AddWarning(message);
                    break;
                case LogLevel.Error:
                case LogLevel.Critical:
                    report.AddError(message);
                    break;
            }
        }
    }
}
