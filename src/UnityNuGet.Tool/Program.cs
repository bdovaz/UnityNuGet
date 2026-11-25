using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using UnityNuGet;
using UnityNuGet.Tool;

if (args.Length > 0 && string.Equals(args[0], "validate", StringComparison.OrdinalIgnoreCase))
{
    return await ValidateCommand.RunAsync(args[1..]);
}

if (args.Length > 0 && args[0] is "-h" or "--help")
{
    Console.WriteLine("Usage:");
    Console.WriteLine("  unityNuGet                     Build all packages from appsettings.json configuration");
    Console.WriteLine("  unityNuGet validate ...        Validate registry.json entries without requiring appsettings.json");
    Console.WriteLine();
    ValidateCommand.PrintUsage(Console.Out);
    return 0;
}

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSingleton<Registry>();
builder.Services.AddHostedService(serviceProvider => serviceProvider.GetRequiredService<Registry>());
builder.Services.AddHostedService<RegistryCacheInitializer>();
builder.Services.AddHostedService<RegistryCacheUpdater>();
builder.Services.AddSingleton<RegistryCacheSingleton>();

builder.Services.Configure<RegistryOptions>(builder.Configuration.GetSection("Registry"));
builder.Services.AddSingleton<IValidateOptions<RegistryOptions>, ValidateRegistryOptions>();
builder.Services.AddOptionsWithValidateOnStart<RegistryOptions, ValidateRegistryOptions>();

builder.Services.Configure<SigningOptions>(builder.Configuration.GetSection("Signing"));
builder.Services.AddSingleton<UnityPackageSigner>();

IHost host = builder.Build();

await host.RunAsync();
return Environment.ExitCode;
