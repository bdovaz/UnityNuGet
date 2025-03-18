﻿using System.Collections.Generic;
using NuGet.Frameworks;
using NuGet.Packaging;
using NUnit.Framework;
using static NuGet.Frameworks.FrameworkConstants;

namespace UnityNuGet.Tests
{
    public class NuGetHelperTests
    {
        private static readonly IEnumerable<RoslynAnalyzerVersion> s_roslynAnalyzerVersions = new RoslynAnalyzerVersion[]
        {
            new() { Name = "roslyn3.8" },
            new() { Name = "roslyn4.3" }
        };

        [Test]
        [TestCase("analyzers/dotnet/roslyn3.8/cs/Test.resources.dll")]
        [TestCase("analyzers/dotnet/roslyn3.8/Test.resources.dll")]
        [TestCase("analyzers/dotnet/cs/Test.resources.dll")]
        [TestCase("analyzers/dotnet/Test.resources.dll")]
        [TestCase("analyzers/Test.resources.dll")]
        public void IsApplicableAnalyzerResource_Valid(string input)
        {
            Assert.That(NuGetHelper.IsApplicableAnalyzerResource(input), Is.True);
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
            Assert.That(NuGetHelper.IsApplicableAnalyzerResource(input), Is.False);
        }

        // Examples:
        // Meziantou.Analyzer -> analyzers/dotnet/roslyn3.8/cs/*
        // Microsoft.Unity.Analyzers -> analyzers/dotnet/cs/*
        // Microsoft.VisualStudio.Threading.Analyzers -> analyzers/cs/*
        // SonarAnalyzer.CSharp -> analyzers/*
        // StrongInject -> analyzers/dotnet/cs/* + analyzers/dotnet/roslyn3.8/cs/*
        [Test]
        [TestCase("analyzers/dotnet/roslyn3.8/cs/Test.dll")]
        [TestCase("analyzers/dotnet/roslyn3.8/Test.dll")]
        [TestCase("analyzers/dotnet/roslyn4.3/cs/Test.dll")]
        [TestCase("analyzers/dotnet/roslyn4.3/Test.dll")]
        [TestCase("analyzers/dotnet/cs/Test.dll")]
        [TestCase("analyzers/dotnet/Test.dll")]
        [TestCase("analyzers/Test.dll")]
        public void IsApplicableUnitySupportedRoslynVersionFolder_Valid(string input)
        {
            Assert.That(NuGetHelper.IsApplicableUnitySupportedRoslynVersionFolder(input, s_roslynAnalyzerVersions), Is.True);
        }

        [Test]
        [TestCase("analyzers/dotnet/roslyn4.0/cs/Test.dll")]
        [TestCase("analyzers/dotnet/roslyn4.0/Test.dll")]
        public void IsApplicableUnitySupportedRoslynVersionFolder_Invalid(string input)
        {
            Assert.That(NuGetHelper.IsApplicableUnitySupportedRoslynVersionFolder(input, s_roslynAnalyzerVersions), Is.False);
        }

        [Test]
        public void GetCompatiblePackageDependencyGroups_SpecificSingleFramework()
        {
            IList<PackageDependencyGroup> packageDependencyGroups =
            [
                new(CommonFrameworks.NetStandard13, []),
                new(CommonFrameworks.NetStandard16, []),
                new(CommonFrameworks.NetStandard20, []),
                new(CommonFrameworks.NetStandard21, [])
            ];

            IEnumerable<RegistryTargetFramework> targetFrameworks = [new() { Framework = CommonFrameworks.NetStandard20 }];

            IEnumerable<PackageDependencyGroup> compatibleDependencyGroups = NuGetHelper.GetCompatiblePackageDependencyGroups(packageDependencyGroups, targetFrameworks);

            Assert.That(compatibleDependencyGroups, Is.EqualTo(new PackageDependencyGroup[] { packageDependencyGroups[2] }).AsCollection);
        }

        [Test]
        public void GetCompatiblePackageDependencyGroups_SpecificMultipleFrameworks()
        {
            IList<PackageDependencyGroup> packageDependencyGroups =
            [
                new(CommonFrameworks.NetStandard13, []),
                new(CommonFrameworks.NetStandard16, []),
                new(CommonFrameworks.NetStandard20, []),
                new(CommonFrameworks.NetStandard21, [])
            ];

            IEnumerable<RegistryTargetFramework> targetFrameworks = [new() { Framework = CommonFrameworks.NetStandard20 }, new() { Framework = CommonFrameworks.NetStandard21 }];

            IEnumerable<PackageDependencyGroup> compatibleDependencyGroups = NuGetHelper.GetCompatiblePackageDependencyGroups(packageDependencyGroups, targetFrameworks);

            Assert.That(compatibleDependencyGroups, Is.EqualTo(new PackageDependencyGroup[] { packageDependencyGroups[2], packageDependencyGroups[3] }).AsCollection);
        }

        [Test]
        public void GetCompatiblePackageDependencyGroups_AnyFramework()
        {
            IList<PackageDependencyGroup> packageDependencyGroups =
            [
                new(new NuGetFramework(SpecialIdentifiers.Any), [])
            ];

            IEnumerable<RegistryTargetFramework> targetFrameworks = [new() { Framework = CommonFrameworks.NetStandard20 }];

            IEnumerable<PackageDependencyGroup> compatibleDependencyGroups = NuGetHelper.GetCompatiblePackageDependencyGroups(packageDependencyGroups, targetFrameworks);

            Assert.That(compatibleDependencyGroups, Is.EqualTo(packageDependencyGroups).AsCollection);
        }
    }
}
