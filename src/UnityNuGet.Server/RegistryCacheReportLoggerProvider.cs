using System;
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace UnityNuGet.Server
{
    internal sealed class RegistryCacheReportLoggerProvider(RegistryCacheReport report) : ILoggerProvider
    {
        private readonly ConcurrentDictionary<string, ILogger> _loggers = new(StringComparer.Ordinal);

        public ILogger CreateLogger(string categoryName)
        {
            return _loggers.GetOrAdd(categoryName, name => new RegistryCacheReportLogger(report, name));
        }

        public void Dispose()
        {
        }
    }
}
