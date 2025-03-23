using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using NuGet.Configuration;
using NuGet.PackageManagement;
using NuGet.Packaging.Core;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;
using NUnit.Framework;

namespace UnityNuGet.Tests
{
    public class RoslynAnalyzersHelperTests
    {
        private static readonly IEnumerable<RoslynAnalyzerVersion> s_roslynAnalyzerVersions = new RoslynAnalyzerVersion[]
        {
            new() { Version = new Version(3, 8, 0, 0), SingleDefineConstraints = [], DefineConstraints = ["!UNITY_6000_0_OR_NEWER"] },
            new() { Version = new Version(4, 3, 0, 0), SingleDefineConstraints = ["UNITY_6000_0_OR_NEWER"], DefineConstraints = ["UNITY_6000_0_OR_NEWER"] }
        };

        internal static object[] s_analyzersWithoutVersionData =
        [
            new object[] {
                "Microsoft.CodeAnalysis.BannedApiAnalyzers",
                "3.3.4",
                new SortedDictionary<string, IEnumerable<string>>()
                {
                    { "analyzers/dotnet/cs/Microsoft.CodeAnalysis.BannedApiAnalyzers.dll", [] },
                    { "analyzers/dotnet/cs/Microsoft.CodeAnalysis.CSharp.BannedApiAnalyzers.dll", [] },
                }
            },
            new object[] {
                "Microsoft.CodeAnalysis.NetAnalyzers",
                "9.0.0",
                new SortedDictionary<string, IEnumerable<string>>()
                {
                    { "analyzers/dotnet/cs/Microsoft.CodeAnalysis.CSharp.NetAnalyzers.dll", new string[] { "UNITY_6000_0_OR_NEWER" } },
                    { "analyzers/dotnet/cs/Microsoft.CodeAnalysis.NetAnalyzers.dll", new string[] { "UNITY_6000_0_OR_NEWER" } },
                }
            },
            new object[] {
                "Microsoft.Unity.Analyzers",
                "1.22.0",
                new SortedDictionary<string, IEnumerable<string>>()
                {
                    { "analyzers/dotnet/cs/Microsoft.Unity.Analyzers.dll", [] }
                }
            },
            new object[] {
                "Microsoft.VisualStudio.Threading.Analyzers",
                "17.13.61",
                new SortedDictionary<string, IEnumerable<string>>()
                {
                    { "analyzers/cs/Microsoft.VisualStudio.Threading.Analyzers.CodeFixes.dll", new string[] { "UNITY_6000_0_OR_NEWER" } },
                    { "analyzers/cs/Microsoft.VisualStudio.Threading.Analyzers.CSharp.dll", new string[] { "UNITY_6000_0_OR_NEWER" } },
                    { "analyzers/cs/Microsoft.VisualStudio.Threading.Analyzers.dll", new string[] { "UNITY_6000_0_OR_NEWER" } }
                }
            },
            new object[] {
                "Nullable.Extended.Analyzer",
                "1.15.6495",
                new SortedDictionary<string, IEnumerable<string>>()
                {
                    { "analyzers/dotnet/cs/Nullable.Extended.Analyzer.dll", new string[] { "UNITY_6000_0_OR_NEWER" } },
                }
            },
            new object[] {
                "NUnit.Analyzers",
                "4.6.0",
                new SortedDictionary<string, IEnumerable<string>>()
                {
                    { "analyzers/dotnet/cs/nunit.analyzers.dll", new string[] { "UNITY_6000_0_OR_NEWER" } },
                }
            },
            new object[] {
                "SonarAnalyzer.CSharp",
                "9.32.0.97167",
                new SortedDictionary<string, IEnumerable<string>>()
                {
                    { "analyzers/Google.Protobuf.dll", [] },
                    { "analyzers/SonarAnalyzer.CFG.dll", [] },
                    { "analyzers/SonarAnalyzer.CSharp.dll", [] },
                    { "analyzers/SonarAnalyzer.dll", [] },
                    { "analyzers/SonarAnalyzer.ShimLayer.dll", [] },
                }
            },
            new object[] {
                "SonarAnalyzer.CSharp",
                "10.7.0.110445",
                new SortedDictionary<string, IEnumerable<string>>()
                {
                    { "analyzers/SonarAnalyzer.CSharp.dll", [] },
                }
            },
            new object[] {
                "StyleCop.Analyzers.Unstable",
                "1.2.0.556",
                new SortedDictionary<string, IEnumerable<string>>()
                {
                    { "analyzers/dotnet/cs/StyleCop.Analyzers.CodeFixes.dll", [] },
                    { "analyzers/dotnet/cs/StyleCop.Analyzers.dll", [] },
                }
            },
        ];

        internal static object[] s_analyzersWithVersionData =
        [
            new object[] {
                "Meziantou.Analyzer",
                "2.0.189",
                new SortedDictionary<string, IEnumerable<string>>()
                {
                    { "analyzers/dotnet/roslyn3.8/cs/Meziantou.Analyzer.dll", new string[] { "!UNITY_6000_0_OR_NEWER" } },
                    { "analyzers/dotnet/roslyn3.8/cs/Meziantou.Analyzer.CodeFixers.dll", new string[] { "!UNITY_6000_0_OR_NEWER" } },

                    { "analyzers/dotnet/roslyn4.2/cs/Meziantou.Analyzer.dll", new string[] { "UNITY_6000_0_OR_NEWER" } },
                    { "analyzers/dotnet/roslyn4.2/cs/Meziantou.Analyzer.CodeFixers.dll", new string[] { "UNITY_6000_0_OR_NEWER" } }
                }
            },
            new object[] {
                "Roslynator.Analyzers",
                "4.13.1",
                new SortedDictionary<string, IEnumerable<string>>()
                {
                    { "analyzers/dotnet/roslyn3.8/cs/Roslynator.CSharp.Analyzers.dll", [] },
                    { "analyzers/dotnet/roslyn3.8/cs/Roslynator.CSharp.Analyzers.CodeFixes.dll", [] },
                    { "analyzers/dotnet/roslyn3.8/cs/Roslynator_Analyzers_Roslynator.Common.dll", [] },
                    { "analyzers/dotnet/roslyn3.8/cs/Roslynator_Analyzers_Roslynator.Core.dll", [] },
                    { "analyzers/dotnet/roslyn3.8/cs/Roslynator_Analyzers_Roslynator.CSharp.dll", [] },
                    { "analyzers/dotnet/roslyn3.8/cs/Roslynator_Analyzers_Roslynator.CSharp.Workspaces.dll", [] },
                    { "analyzers/dotnet/roslyn3.8/cs/Roslynator_Analyzers_Roslynator.Workspaces.Common.dll", [] },
                    { "analyzers/dotnet/roslyn3.8/cs/Roslynator_Analyzers_Roslynator.Workspaces.Core.dll", [] }
                }
            },
            new object[] {
                "Roslynator.CodeAnalysis.Analyzers",
                "4.13.1",
                new SortedDictionary<string, IEnumerable<string>>()
                {
                    { "analyzers/dotnet/roslyn3.8/cs/Roslynator.CodeAnalysis.Analyzers.dll", [] },
                    { "analyzers/dotnet/roslyn3.8/cs/Roslynator.CodeAnalysis.Analyzers.CodeFixes.dll", [] },
                    { "analyzers/dotnet/roslyn3.8/cs/Roslynator_CodeAnalysis_Analyzers_Roslynator.Common.dll", [] },
                    { "analyzers/dotnet/roslyn3.8/cs/Roslynator_CodeAnalysis_Analyzers_Roslynator.Core.dll", [] },
                    { "analyzers/dotnet/roslyn3.8/cs/Roslynator_CodeAnalysis_Analyzers_Roslynator.CSharp.dll", [] },
                    { "analyzers/dotnet/roslyn3.8/cs/Roslynator_CodeAnalysis_Analyzers_Roslynator.CSharp.Workspaces.dll", [] },
                    { "analyzers/dotnet/roslyn3.8/cs/Roslynator_CodeAnalysis_Analyzers_Roslynator.Workspaces.Common.dll", [] },
                    { "analyzers/dotnet/roslyn3.8/cs/Roslynator_CodeAnalysis_Analyzers_Roslynator.Workspaces.Core.dll", [] }
                }
            },
            new object[] {
                "Roslynator.Formatting.Analyzers",
                "4.13.1",
                new SortedDictionary<string, IEnumerable<string>>()
                {
                    { "analyzers/dotnet/roslyn3.8/cs/Roslynator.Formatting.Analyzers.dll", [] },
                    { "analyzers/dotnet/roslyn3.8/cs/Roslynator.Formatting.Analyzers.CodeFixes.dll", [] },
                    { "analyzers/dotnet/roslyn3.8/cs/Roslynator_Formatting_Analyzers_Roslynator.Common.dll", [] },
                    { "analyzers/dotnet/roslyn3.8/cs/Roslynator_Formatting_Analyzers_Roslynator.Core.dll", [] },
                    { "analyzers/dotnet/roslyn3.8/cs/Roslynator_Formatting_Analyzers_Roslynator.CSharp.dll", [] },
                    { "analyzers/dotnet/roslyn3.8/cs/Roslynator_Formatting_Analyzers_Roslynator.CSharp.Workspaces.dll", [] },
                    { "analyzers/dotnet/roslyn3.8/cs/Roslynator_Formatting_Analyzers_Roslynator.Workspaces.Common.dll", [] },
                    { "analyzers/dotnet/roslyn3.8/cs/Roslynator_Formatting_Analyzers_Roslynator.Workspaces.Core.dll", [] }
                }
            }
        ];

        internal static object[] s_packagesWithAnalyzersWithVersionData =
        [
            new object[] {
                "CommunityToolkit.Mvvm",
                "8.4.0",
                new SortedDictionary<string, IEnumerable<string>>()
                {
                    { "analyzers/dotnet/roslyn4.3/cs/CommunityToolkit.Mvvm.CodeFixers.dll", new string[] { "UNITY_6000_0_OR_NEWER" } },
                    { "analyzers/dotnet/roslyn4.3/cs/CommunityToolkit.Mvvm.SourceGenerators.dll", new string[] { "UNITY_6000_0_OR_NEWER" } }
                }
            },
            new object[] {
                "Microsoft.Extensions.Configuration.Binder",
                "9.0.3",
                new SortedDictionary<string, IEnumerable<string>>()
            },
            new object[] {
                "StrongInject",
                "1.4.4",
                new SortedDictionary<string, IEnumerable<string>>()
                {
                    { "analyzers/dotnet/cs/StrongInject.Generator.dll", [] },
                    { "analyzers/dotnet/roslyn3.8/cs/StrongInject.Generator.Roslyn38.dll", new string[] { "!UNITY_6000_0_OR_NEWER" } },
                    { "analyzers/dotnet/roslyn4.0/cs/StrongInject.Generator.Roslyn40.dll", new string[] { "UNITY_6000_0_OR_NEWER" } }
                }
            },
            new object[] {
                "System.Text.Json",
                "9.0.3",
                new SortedDictionary<string, IEnumerable<string>>()
                {
                    { "analyzers/dotnet/roslyn4.0/cs/System.Text.Json.SourceGeneration.dll", new string[] { "UNITY_6000_0_OR_NEWER" } }
                }
            }
        ];

        [Test]
        [TestCaseSource(nameof(s_analyzersWithoutVersionData))]
        public async Task PackagesWithAnalyzersWithoutRoslynVersion(string package, string version, SortedDictionary<string, IEnumerable<string>> files)
        {
            NuGetConsoleTestLogger logger = new();
            SourceCacheContext cache = new();
            ISettings settings = Settings.LoadDefaultSettings(root: null);
            SourceRepository repository = Repository.Factory.GetCoreV3("https://api.nuget.org/v3/index.json");
            PackageIdentity packageIdentity = new(package, NuGetVersion.Parse(version));

            DownloadResourceResult downloadResult = await PackageDownloader.GetDownloadResourceResultAsync(
                [repository],
                packageIdentity,
                new PackageDownloadContext(cache),
                SettingsUtility.GetGlobalPackagesFolder(settings),
                logger, CancellationToken.None);

            IDictionary<string, string> supportedFiles = await RoslynAnalyzersHelper
                .GetUnitySupportedFiles(
                downloadResult.PackageReader,
                s_roslynAnalyzerVersions,
                CancellationToken.None);

            Assert.That(files, Has.Count.EqualTo(supportedFiles.Count));

            Regex labelsRegex = new(@"labels:(?<labels>[\s\S]*)PluginImporter");
            Regex defineConstraintsRegex = new(@"defineConstraints:(?<defineConstraints>(\s*-.*)*)\n\s+[^-]");

            foreach (KeyValuePair<string, IEnumerable<string>> kvp in files)
            {
                Assert.That(supportedFiles.ContainsKey(kvp.Key), Is.True);

                string meta = supportedFiles[kvp.Key];

                string[] labels = [.. labelsRegex
                    .Match(meta)
                    .Groups["labels"].Value.Trim()
                    .Split('\n')
                    .Select(l => l.Replace("- ", string.Empty))];

                Assert.That(labels, Is.EqualTo(["RoslynAnalyzer"]).AsCollection);

                string[] defineConstraints = [.. defineConstraintsRegex
                    .Match(meta)
                    .Groups["defineConstraints"].Value.Trim()
                    .Split('\n')
                    .Select(l => l.Replace("- ", string.Empty))
                    .Where(l => !string.IsNullOrEmpty(l))];

                Assert.That(defineConstraints, Is.EqualTo(kvp.Value).AsCollection);
            }
        }

        [Test]
        [TestCaseSource(nameof(s_analyzersWithVersionData))]
        [TestCaseSource(nameof(s_packagesWithAnalyzersWithVersionData))]
        public async Task PackagesWithAnalyzersWithRoslynVersion(string package, string version, SortedDictionary<string, IEnumerable<string>> files)
        {
            NuGetConsoleTestLogger logger = new();
            SourceCacheContext cache = new();
            ISettings settings = Settings.LoadDefaultSettings(root: null);
            SourceRepository repository = Repository.Factory.GetCoreV3("https://api.nuget.org/v3/index.json");
            PackageIdentity packageIdentity = new(package, NuGetVersion.Parse(version));

            DownloadResourceResult downloadResult = await PackageDownloader.GetDownloadResourceResultAsync(
                [repository],
                packageIdentity,
                new PackageDownloadContext(cache),
                SettingsUtility.GetGlobalPackagesFolder(settings),
                logger, CancellationToken.None);

            IDictionary<string, string> supportedFiles = await RoslynAnalyzersHelper
                .GetUnitySupportedFiles(
                    downloadResult.PackageReader,
                    s_roslynAnalyzerVersions,
                    CancellationToken.None);

            Assert.That(files, Has.Count.EqualTo(supportedFiles.Count));

            Regex labelsRegex = new(@"labels:(?<labels>[\s\S]*)PluginImporter");
            Regex defineConstraintsRegex = new(@"defineConstraints:(?<defineConstraints>(\s*-.*)*)\n\s+[^-]");

            foreach (KeyValuePair<string, IEnumerable<string>> kvp in files)
            {
                Assert.That(supportedFiles.ContainsKey(kvp.Key), Is.True);

                string meta = supportedFiles[kvp.Key];

                string[] labels = [.. labelsRegex
                    .Match(meta)
                    .Groups["labels"].Value.Trim()
                    .Split('\n')
                    .Select(l => l.Replace("- ", string.Empty))];

                Assert.That(labels, Is.EqualTo(["RoslynAnalyzer"]).AsCollection);

                string[] defineConstraints = [.. defineConstraintsRegex
                    .Match(meta)
                    .Groups["defineConstraints"].Value.Trim()
                    .Split('\n')
                    .Select(l => l.Replace("- ", string.Empty))
                    .Where(l => !string.IsNullOrEmpty(l))];

                Assert.That(defineConstraints, Is.EqualTo(kvp.Value).AsCollection);
            }
        }

        [Test]
        [TestCase("analyzers/dotnet/roslyn3.8/cs/Test.resources.dll")]
        [TestCase("analyzers/dotnet/roslyn3.8/Test.resources.dll")]
        [TestCase("analyzers/dotnet/cs/Test.resources.dll")]
        [TestCase("analyzers/dotnet/Test.resources.dll")]
        [TestCase("analyzers/Test.resources.dll")]
        public void IsApplicableAnalyzerResource_Valid(string input)
        {
            Assert.That(RoslynAnalyzersHelper.IsApplicableAnalyzerResource(input), Is.True);
        }

        [Test]
        [TestCase("analyzers/dotnet/roslyn3.8/vb/cs/Test.resources.dll")]
        [TestCase("analyzers/dotnet/roslyn3.8/cs/Test.dll")]
        [TestCase("analyzers/dotnet/roslyn3.8/Test.dll")]
        [TestCase("analyzers/dotnet/vb/Test.dll")]
        [TestCase("analyzers/dotnet/cs/Test.dll")]
        [TestCase("analyzers/dotnet/Test.dll")]
        [TestCase("analyzers/Test.dll")]
        public void IsApplicableAnalyzerResource_Invalid(string input)
        {
            Assert.That(RoslynAnalyzersHelper.IsApplicableAnalyzerResource(input), Is.False);
        }
    }
}
