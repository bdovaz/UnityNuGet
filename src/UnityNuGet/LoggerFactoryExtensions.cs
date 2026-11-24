namespace Microsoft.Extensions.Logging
{
    public static class LoggerFactoryExtensions
    {
        private const string NuGetCategoryName = "NuGet";

        public static ILogger CreateNuGetLogger(this ILoggerFactory loggerFactory)
        {
            return loggerFactory.CreateLogger(NuGetCategoryName);
        }
    }
}
