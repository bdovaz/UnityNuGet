using System;
using System.CommandLine;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UnityNuGet.Tool.Commands;

namespace UnityNuGet.Tool
{
    internal static class ToolApplication
    {
        public static async Task<int> RunAsync(string[] args, Action<IServiceCollection>? configureServices = null)
        {
            HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

            IHost? host = null;

            CommandModule rootModule = new(
                new UnityNuGetCommand(() => host!.Services!.GetRequiredService<IRegistryCacheUpdater>()),
                services =>
                {
                    services.AddSingleton<Registry>();
                    services.AddHostedService(serviceProvider => serviceProvider.GetRequiredService<Registry>());
                    services.AddHostedService<RegistryCacheInitializer>();
                    services.AddSingleton<IRegistryCacheUpdater, RegistryCacheUpdater>();
                    services.AddSingleton<RegistryCacheSingleton>();

                    services.Configure<RegistryOptions>(builder.Configuration.GetSection("Registry"));
                    services.AddSingleton<IValidateOptions<RegistryOptions>, ValidateRegistryOptions>();
                    services.AddOptionsWithValidateOnStart<RegistryOptions, ValidateRegistryOptions>();

                    services.Configure<SigningOptions>(builder.Configuration.GetSection("Signing"));
                    services.AddSingleton<UnityPackageSigner>();
                });

            CommandModule validateModule = new(
                new ValidateCommand(
                    () => host!.Services!.GetRequiredService<ILogger<ValidateCommand>>(),
                    () => host!.Services!.GetRequiredService<IRegistryValidator>()),
                services => services.AddRegistryValidator());

            CommandModule[] modules = [rootModule, validateModule];

            RootCommand rootCommand = (RootCommand)rootModule.Command;

            foreach (CommandModule module in modules.Skip(1))
            {
                rootCommand.Subcommands.Add(module.Command);
            }

            ParseResult parseResult = rootCommand.Parse(args);

            Command selectedCommand = parseResult.CommandResult.Command;

            CommandModule selectedModule = modules.Single(module => ReferenceEquals(module.Command, selectedCommand));
            selectedModule.ConfigureServices(builder.Services);

            configureServices?.Invoke(builder.Services);

            host = builder.Build();

            await host.StartAsync();

            try
            {
                return await parseResult.InvokeAsync();
            }
            finally
            {
                await host.StopAsync();

                host.Dispose();
            }
        }
    }
}
