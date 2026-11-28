using System.Threading;
using System.Threading.Tasks;

namespace UnityNuGet.Tool
{
    public interface IRegistryCacheUpdater
    {
        Task<bool> Execute(CancellationToken cancellationToken = default);
    }
}
