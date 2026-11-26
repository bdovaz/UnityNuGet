using System.Collections.Generic;

namespace UnityNuGet
{
    public sealed class RegistryValidationOptions
    {
        public required string RegistryFilePath { get; init; }

        public IReadOnlyList<string> PackageNames { get; init; } = [];

        public string? Filter { get; init; }

        public bool ValidateAll { get; init; }

        public bool SkipConversion { get; init; }
    }

    public sealed class RegistryValidationResult
    {
        public bool IsValid { get; internal set; }

        public int CheckedPackageCount { get; internal set; }
    }
}
