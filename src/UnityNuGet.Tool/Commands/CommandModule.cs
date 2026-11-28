using System;
using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;

namespace UnityNuGet.Tool.Commands
{
    internal sealed class CommandModule(Command command, Action<IServiceCollection> configureServices)
    {
        public Command Command { get; } = command;

        public void ConfigureServices(IServiceCollection services) => configureServices(services);
    }
}
