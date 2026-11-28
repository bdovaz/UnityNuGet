using System;
using System.CommandLine;

namespace UnityNuGet.Tool.Commands
{
    internal sealed class UnityNuGetCommand : RootCommand
    {
        public UnityNuGetCommand(Func<IRegistryCacheUpdater> registryCacheUpdaterFactory) : base("Build all packages from appsettings.json configuration")
        {
            SetAction(async (parseResult, cancellationToken) =>
            {
                bool result = await registryCacheUpdaterFactory().Execute(cancellationToken);

                return result ? 0 : 1;
            });
        }
    }
}
