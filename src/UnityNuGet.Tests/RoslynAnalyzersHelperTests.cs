using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace UnityNuGet.Tests
{
    public class RoslynAnalyzersHelperTests
    {
        private static readonly IEnumerable<RoslynAnalyzerVersion> s_roslynAnalyzerVersions = new RoslynAnalyzerVersion[]
        {
            new() { Version = new Version(3, 8) },
            new() { Version = new Version(4, 3) }
        };

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
        [TestCase("data/ValidRoslynAnalyzers/case_01/analyzers/dotnet/cs/CommunityToolkit.Mvvm.SourceGenerators.dll")]
        [TestCase("data/ValidRoslynAnalyzers/case_02/analyzers/dotnet/CommunityToolkit.Mvvm.SourceGenerators.dll")]
        [TestCase("data/ValidRoslynAnalyzers/case_03/analyzers/CommunityToolkit.Mvvm.SourceGenerators.dll")]
        public void IsApplicableUnitySupportedRoslynVersionFolder_Valid(string input)
        {
            Assert.That(RoslynAnalyzersHelper.IsApplicableUnitySupportedRoslynVersionFolder(input, s_roslynAnalyzerVersions), Is.True);
        }

        [Test]
        [TestCase("analyzers/dotnet/roslyn4.0/cs/Test.dll")]
        [TestCase("analyzers/dotnet/roslyn4.0/Test.dll")]
        [TestCase("data/InvalidRoslynAnalyzers/case_01/analyzers/dotnet/cs/Microsoft.Extensions.Configuration.Binder.SourceGeneration.dll")]
        [TestCase("data/InvalidRoslynAnalyzers/case_02/analyzers/dotnet/Microsoft.Extensions.Configuration.Binder.SourceGeneration.dll")]
        [TestCase("data/InvalidRoslynAnalyzers/case_03/analyzers/Microsoft.Extensions.Configuration.Binder.SourceGeneration.dll")]
        public void IsApplicableUnitySupportedRoslynVersionFolder_Invalid(string input)
        {
            Assert.That(RoslynAnalyzersHelper.IsApplicableUnitySupportedRoslynVersionFolder(input, s_roslynAnalyzerVersions), Is.False);
        }

    }
}
