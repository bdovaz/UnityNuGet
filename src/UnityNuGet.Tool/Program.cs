using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using UnityNuGet;
using UnityNuGet.Server;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSingleton<Registry>();
builder.Services.AddHostedService(serviceProvider => serviceProvider.GetRequiredService<Registry>());

builder.Services.AddHostedService<RegistryCacheInitializer>();

builder.Services.AddHostedService<RegistryCacheUpdater>();

builder.Services.AddSingleton<RegistryCacheSingleton>();

builder.Services.Configure<RegistryOptions>(builder.Configuration.GetSection("Registry"));
builder.Services.AddSingleton<IValidateOptions<RegistryOptions>, ValidateRegistryOptions>();
builder.Services.AddOptionsWithValidateOnStart<RegistryOptions, ValidateRegistryOptions>();

IHost host = builder.Build();

await host.RunAsync();
