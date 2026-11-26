using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UnityNuGet;
using UnityNuGet.Tool;
using UnityNuGet.Tool.Commands;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSingleton<Registry>();
builder.Services.AddHostedService(serviceProvider => serviceProvider.GetRequiredService<Registry>());
builder.Services.AddHostedService<RegistryCacheInitializer>();
builder.Services.AddSingleton<IRegistryCacheUpdater, RegistryCacheUpdater>();
builder.Services.AddSingleton<RegistryCacheSingleton>();

builder.Services.Configure<RegistryOptions>(builder.Configuration.GetSection("Registry"));
builder.Services.AddSingleton<IValidateOptions<RegistryOptions>, ValidateRegistryOptions>();
builder.Services.AddOptionsWithValidateOnStart<RegistryOptions, ValidateRegistryOptions>();

builder.Services.Configure<SigningOptions>(builder.Configuration.GetSection("Signing"));
builder.Services.AddSingleton<UnityPackageSigner>();

builder.Services.AddRegistryValidator();

IHost host = builder.Build();

await host.StartAsync();

try
{
    UnityNuGetCommand rootCommand = new(host.Services.GetRequiredService<IRegistryCacheUpdater>());
    rootCommand.Subcommands.Add(new ValidateCommand(
        host.Services.GetRequiredService<ILogger<ValidateCommand>>(),
        host.Services.GetRequiredService<IRegistryValidator>()));

    return await rootCommand.Parse(args).InvokeAsync();
}
finally
{
    await host.StopAsync();

    host.Dispose();
}
