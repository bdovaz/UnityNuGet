using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ICSharpCode.Decompiler;
using ICSharpCode.Decompiler.CSharp;
using ICSharpCode.Decompiler.Metadata;
using NuGet.Packaging;
using NuGet.Packaging.Core;

namespace UnityNuGet
{
    public static partial class RoslynAnalyzersHelper
    {
        private static readonly string s_analyzersFolderName = "analyzers";
        private static readonly string s_analyzersDotnetFolderName = $"{s_analyzersFolderName}/dotnet";
        private static readonly string s_analyzersDotnetCsharpFolderName = $"{s_analyzersDotnetFolderName}/cs";

        private const string MicrosoftCodeAnalysisAssemblyName = "Microsoft.CodeAnalysis";

        private static readonly Regex s_roslynVersionFolderNameRegex = RoslynVersionFolderName();

        private static readonly DecompilerSettings s_decompilerSettings = new()
        {
            ThrowOnAssemblyResolveErrors = false
        };

        // https://learn.microsoft.com/en-us/nuget/guides/analyzers-conventions#analyzers-path-format
        public static async Task<IDictionary<string, string>> GetUnitySupportedFiles(
            PackageReaderBase packageReader,
            IEnumerable<RoslynAnalyzerVersion> roslynAnalyzerVersions,
            CancellationToken cancellationToken)
        {
            PackageIdentity packageIdentity = await packageReader
                .GetIdentityAsync(cancellationToken);

            IEnumerable<FrameworkSpecificGroup> packageFiles = await packageReader
                .GetItemsAsync(PackagingConstants.Folders.Analyzers, cancellationToken);

            string[] filePathsWithoutResources = [.. packageFiles.SelectMany(p => p.Items).Where(f => !IsApplicableAnalyzerResource(f))];

            Dictionary<RoslynAnalyzerVersion, Version> maxSupportedRoslynVersions = GetMaxSupportedRoslynVersions(
                filePathsWithoutResources,
                roslynAnalyzerVersions);

            SortedDictionary<string, string> supportedFiles = [];

            if (maxSupportedRoslynVersions.Count > 0)
            {
                GetSupportedFilesWithVersion(
                    packageIdentity,
                    filePathsWithoutResources,
                    maxSupportedRoslynVersions,
                    supportedFiles);
            }

            // Fallback for when it does not contain a folder in /roslyn*/ format
            // In this case, as there is no data to give us the Roslyn version,
            // we have to scan the version of the Microsoft.CodeAnalysis dependency.
            await GetSupportedFilesWithoutVersion(
                packageReader,
                roslynAnalyzerVersions,
                packageIdentity,
                filePathsWithoutResources,
                supportedFiles,
                cancellationToken);

            return supportedFiles;
        }

        private static void GetSupportedFilesWithVersion(
            PackageIdentity packageIdentity,
            IEnumerable<string> filePathsWithoutResources,
            IDictionary<RoslynAnalyzerVersion, Version> maxSupportedRoslynVersions,
            SortedDictionary<string, string> supportedFiles)
        {
            int count = maxSupportedRoslynVersions.GroupBy(v => v.Value).Count();

            if (count == 1)
            {
                KeyValuePair<RoslynAnalyzerVersion, Version> resolvedItem = maxSupportedRoslynVersions.First();

                RoslynAnalyzerVersion roslynAnalyzerVersion = resolvedItem.Key;
                Version version = resolvedItem.Value;

                string folderPrefix = $"{s_analyzersDotnetFolderName}/roslyn{version.Major}.{version.Minor}";

                GetSupportedFiles(
                    folderPrefix,
                    packageIdentity,
                    filePathsWithoutResources,
                    roslynAnalyzerVersion.SingleDefineConstraints!,
                    supportedFiles);
            }
            else
            {
                foreach (KeyValuePair<RoslynAnalyzerVersion, Version> kvp in maxSupportedRoslynVersions)
                {
                    string folderPrefix = $"{s_analyzersDotnetFolderName}/roslyn{kvp.Value.Major}.{kvp.Value.Minor}";

                    GetSupportedFiles(
                        folderPrefix,
                        packageIdentity,
                        filePathsWithoutResources,
                        kvp.Key.DefineConstraints!,
                        supportedFiles);
                }
            }
        }

        private static async Task GetSupportedFilesWithoutVersion(
            PackageReaderBase packageReader,
            IEnumerable<RoslynAnalyzerVersion> roslynAnalyzerVersions,
            PackageIdentity packageIdentity,
            IEnumerable<string> filePathsWithoutResources,
            SortedDictionary<string, string> supportedFiles,
            CancellationToken cancellationToken = default)
        {
            string[] filePathsByLanguage = [.. filePathsWithoutResources.Where(IsApplicableAnalyzer)];

            string folderPrefix = filePathsByLanguage.Length > 0 ? s_analyzersDotnetCsharpFolderName : s_analyzersFolderName;

            IEnumerable<string> filteredFilePaths = filePathsByLanguage.Length > 0
                ? filePathsByLanguage
                : filePathsWithoutResources;

            foreach (string filePath in filteredFilePaths.Where(f => !s_roslynVersionFolderNameRegex.IsMatch(f)))
            {
                string fileName = Path.GetFileName(filePath);

                using Stream stream = await packageReader.GetStreamAsync(filePath, cancellationToken);

                string? meta = null;

                if (TryToGetMicrosoftCodeAnalysisDependencyAssemblyVersion(fileName, stream, out Version? version))
                {
                    RoslynAnalyzerVersion? resolvedVersion = roslynAnalyzerVersions
                        .Select(v => new { Version = v, MaxSupportedVersion = GetMaxSupportedRoslynVersion([version!], v) })
                        .Where(v => v.MaxSupportedVersion != null)
                        .DistinctBy(v => v.Version.ToString())
                        .Select(v => v.Version)
                        .FirstOrDefault();

                    meta = resolvedVersion != null ? GetMeta(packageIdentity, filePath, resolvedVersion.SingleDefineConstraints!) : null;
                }
                else
                {
                    meta = GetMeta(packageIdentity, filePath, []);
                }

                if (!string.IsNullOrEmpty(meta))
                {
                    if (!supportedFiles.ContainsKey(filePath))
                    {
                        supportedFiles.Add(filePath, meta);
                    }
                }
            }
        }

        private static void GetSupportedFiles(
            string folderPrefix,
            PackageIdentity packageIdentity,
            IEnumerable<string> filePaths,
            IEnumerable<string> defineConstraints,
            SortedDictionary<string, string> supportedFiles)
        {
            string[] roslynVersionFilePaths = [.. filePaths.Where(f => f.StartsWith(folderPrefix))];

            foreach (string filePath in roslynVersionFilePaths)
            {
                string? meta = GetMeta(packageIdentity, filePath, defineConstraints);

                if (meta == null)
                {
                    continue;
                }

                supportedFiles.Add(filePath, meta);
            }
        }

        private static string? GetMeta(PackageIdentity packageIdentity, string filePath, IEnumerable<string> defineConstraints)
        {
            string fileExtension = Path.GetExtension(filePath);

            if (fileExtension == ".dll")
            {
                return UnityMeta.GetMetaForDll(
                    RegistryCache.GetStableGuid(packageIdentity, filePath),
                    new PlatformDefinition(UnityOs.AnyOs, UnityCpu.None, isEditorConfig: false),
                    ["RoslynAnalyzer"],
                    defineConstraints);
            }
            else
            {
                return UnityMeta.GetMetaForExtension(RegistryCache.GetStableGuid(packageIdentity, filePath), fileExtension);
            }
        }

        private static Dictionary<RoslynAnalyzerVersion, Version> GetMaxSupportedRoslynVersions(IEnumerable<string> filePaths, IEnumerable<RoslynAnalyzerVersion> roslynAnalyzerVersions)
        {
            Dictionary<RoslynAnalyzerVersion, Version> result = [];

            List<Version> availableVersions = [];

            foreach (string filePath in filePaths)
            {
                if (TryToGetRoslynAnalyzerFolderNameVersion(filePath, out Version? roslynVersion))
                {
                    availableVersions.Add(roslynVersion!);
                }
            }

            return roslynAnalyzerVersions
                .Select(v => new { Version = v, MaxSupportedVersion = GetMaxSupportedRoslynVersion(availableVersions, v) })
                .Where(v => v.MaxSupportedVersion != null)
                .ToDictionary(v => v.Version, v => v.MaxSupportedVersion!);
        }

        private static Version? GetMaxSupportedRoslynVersion(IEnumerable<Version> versions, RoslynAnalyzerVersion roslynAnalyzerVersion)
        {
            Version? result = null;

            foreach (Version version in versions)
            {
                if (version <= roslynAnalyzerVersion.Version && (result == null || version > result))
                {
                    result = version;
                }
            }

            return result;
        }

        private static bool TryToGetRoslynAnalyzerFolderNameVersion(string filePath, out Version? version)
        {
            Match roslynVersionFolderNameMatch = s_roslynVersionFolderNameRegex.Match(filePath);

            if (roslynVersionFolderNameMatch.Success)
            {
                version = new(
                    int.Parse(roslynVersionFolderNameMatch.Groups["majorVersion"].Value),
                    int.Parse(roslynVersionFolderNameMatch.Groups["minorVersion"].Value));
            }
            else
            {
                version = null;
            }

            return version != null;
        }

        private static bool TryToGetMicrosoftCodeAnalysisDependencyAssemblyVersion(string fileName, Stream stream, out Version? version)
        {
            return TryToGetMicrosoftCodeAnalysisDependencyAssemblyVersion(new CSharpDecompiler(new PEFile(fileName, stream), new UniversalAssemblyResolver(null, false, null), s_decompilerSettings), out version);
        }

        private static bool TryToGetMicrosoftCodeAnalysisDependencyAssemblyVersion(CSharpDecompiler decompiler, out Version? version)
        {
            AssemblyReference? assemblyReference = decompiler
                .TypeSystem
                .MainModule
                .MetadataFile
                .AssemblyReferences
                .FirstOrDefault(a => a.Name.Equals(MicrosoftCodeAnalysisAssemblyName));

            if (assemblyReference != null && assemblyReference.Version != null)
            {
                version = assemblyReference.Version;
            }
            else
            {
                version = null;
            }

            return version != null;
        }

        // https://github.com/dotnet/sdk/blob/v9.0.202/src/Tasks/Microsoft.NET.Build.Tasks/NuGetUtils.NuGet.cs
        private static bool IsApplicableAnalyzer(string file) => IsApplicableAnalyzer(file, "C#");

        private static bool IsApplicableAnalyzer(string file, string projectLanguage)
        {
            // This logic is preserved from previous implementations.
            // See https://github.com/NuGet/Home/issues/6279#issuecomment-353696160 for possible issues with it.
            bool IsAnalyzer()
            {
                return file.StartsWith("analyzers", StringComparison.Ordinal)
                    && file.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                    && !file.EndsWith(".resources.dll", StringComparison.OrdinalIgnoreCase);
            }

            bool CS() => file.Contains("/cs/", StringComparison.OrdinalIgnoreCase);
            bool VB() => file.Contains("/vb/", StringComparison.OrdinalIgnoreCase);

            bool FileMatchesProjectLanguage()
            {
                return projectLanguage switch
                {
                    "C#" => CS() || !VB(),
                    "VB" => VB() || !CS(),
                    _ => false,
                };
            }

            return IsAnalyzer() && FileMatchesProjectLanguage();
        }

        internal static bool IsApplicableAnalyzerResource(string file)
        {
            bool IsResource()
            {
                return file.StartsWith("analyzers", StringComparison.Ordinal)
                    && file.EndsWith(".resources.dll", StringComparison.OrdinalIgnoreCase);
            }

            bool CS() => file.Contains("/cs/", StringComparison.OrdinalIgnoreCase);
            bool VB() => file.Contains("/vb/", StringComparison.OrdinalIgnoreCase);

            // Czech locale is cs, catch /vb/cs/
            return IsResource() && ((!CS() && !VB()) || (CS() && !VB()));
        }

        // https://learn.microsoft.com/en-us/visualstudio/extensibility/roslyn-version-support
        [GeneratedRegex(@"analyzers/dotnet/roslyn(?<majorVersion>\d+)\.(?<minorVersion>\d+)\.?\d*/?")]
        private static partial Regex RoslynVersionFolderName();
    }
}
