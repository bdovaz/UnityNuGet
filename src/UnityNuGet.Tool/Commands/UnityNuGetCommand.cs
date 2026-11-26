using System.CommandLine;

namespace UnityNuGet.Tool.Commands
{
    internal sealed class UnityNuGetCommand : RootCommand
    {
        public UnityNuGetCommand(IRegistryCacheUpdater registryCacheUpdater) : base("Build all packages from appsettings.json configuration")
        {
            SetAction(async (parseResult, cancellationToken) =>
            {
                bool result = await registryCacheUpdater.Execute(cancellationToken);

                return result ? 0 : 1;
            });
        }
    }
}
