namespace UnityNuGet
{
    public sealed class RegistryValidationResult
    {
        public bool IsValid { get; set; }

        public int CheckedPackageCount { get; set; }
    }
}
