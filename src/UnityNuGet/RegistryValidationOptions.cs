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
        public bool IsValid => Errors.Count == 0;

        public List<string> Errors { get; } = [];

        public List<string> Warnings { get; } = [];

        public List<string> Information { get; } = [];

        public int CheckedPackageCount { get; internal set; }
    }
}
