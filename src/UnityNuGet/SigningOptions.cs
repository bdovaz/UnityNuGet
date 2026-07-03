namespace UnityNuGet
{
    public class SigningOptions
    {
        public string? UpmServiceAccountKeyId { get; set; }

        public string? UpmServiceAccountKeySecret { get; set; }

        public int? UnityOrganizationId { get; set; }

        public string? UpmExecutableFilePath { get; set; }

        public string? PackageIdRegexPattern { get; set; }
    }
}
