using Microsoft.Extensions.Logging;
using UnityNuGet;

namespace Microsoft.Extensions.DependencyInjection
{
    public static class ServiceCollectionExtensions
    {
        public static void AddRegistryValidator(this IServiceCollection services)
        {
            services.AddSingleton<IRegistryValidator>(serviceProvider =>
            {
                return new RegistryValidator(serviceProvider.GetRequiredService<ILoggerFactory>());
            });
        }
    }
}
