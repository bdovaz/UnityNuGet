using System.Threading;
using System.Threading.Tasks;

namespace UnityNuGet
{
    public interface IRegistryValidator
    {
        Task<RegistryValidationResult> Validate(RegistryValidationOptions options, CancellationToken cancellationToken = default);
    }
}
