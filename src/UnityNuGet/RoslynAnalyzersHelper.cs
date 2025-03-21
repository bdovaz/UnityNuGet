using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using ICSharpCode.Decompiler.CSharp;
using ICSharpCode.Decompiler;
using ICSharpCode.Decompiler.Metadata;

namespace UnityNuGet
{
    public static partial class RoslynAnalyzersHelper
    {
        private const string MicrosoftCodeAnalysisAssemblyName = "Microsoft.CodeAnalysis";

        private static readonly DecompilerSettings s_decompilerSettings = new()
        {
            ThrowOnAssemblyResolveErrors = false
        };

        // https://learn.microsoft.com/en-us/nuget/guides/analyzers-conventions#analyzers-path-format
        public static bool IsApplicableUnitySupportedRoslynVersionFolder(string file, IEnumerable<RoslynAnalyzerVersion> roslynAnalyzerVersions)
        {
            Match roslynVersionFolderNameMatch = RoslynVersionFolderName().Match(file);

            if (roslynVersionFolderNameMatch.Success)
            {
                Version resolvedVersion = new(
                    int.Parse(roslynVersionFolderNameMatch.Groups["majorVersion"].Value),
                    int.Parse(roslynVersionFolderNameMatch.Groups["minorVersion"].Value));

                return roslynAnalyzerVersions.Any(a =>
                    a.Version!.Major == resolvedVersion.Major &&
                    a.Version.Minor == resolvedVersion.Minor);
            }

            // Fallback for when it does not contain a folder in /roslyn*/ format
            // In this case, as there is no data to give us the Roslyn version,
            // we have to scan the version of the Microsoft.CodeAnalysis dependency.
            return HasApplicableUnitySupportedMicrosoftCodeAnalysisAssemblyVersion(file, roslynAnalyzerVersions);
        }

        private static bool HasApplicableUnitySupportedMicrosoftCodeAnalysisAssemblyVersion(string file, IEnumerable<RoslynAnalyzerVersion> roslynAnalyzerVersions)
        {
            CSharpDecompiler decompiler = new(file, s_decompilerSettings);

            AssemblyReference? assemblyReference = decompiler
                .TypeSystem
                .MainModule
                .MetadataFile
                .AssemblyReferences
                .FirstOrDefault(a => a.Name.Equals(MicrosoftCodeAnalysisAssemblyName));

            return assemblyReference != null &&
                assemblyReference.Version != null &&
                roslynAnalyzerVersions.Any(a =>
                    a.Version!.Major == assemblyReference.Version.Major &&
                    a.Version.Minor == assemblyReference.Version.Minor);
        }

        // https://github.com/dotnet/sdk/blob/v9.0.202/src/Tasks/Microsoft.NET.Build.Tasks/NuGetUtils.NuGet.cs
        public static bool IsApplicableAnalyzer(string file) => IsApplicableAnalyzer(file, "C#");

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

        public static bool IsApplicableAnalyzerResource(string file)
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
        [GeneratedRegex(@"/roslyn(?<majorVersion>\d+)\.(?<minorVersion>\d+)\.?\d*/")]
        private static partial Regex RoslynVersionFolderName();
    }
}
