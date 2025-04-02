using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace UnityNuGet.Tests
{
    public class PlatformDefinitionTests
    {
        [Test]
        public void CanFindDefinitions()
        {
            PlatformDefinition platformDefs = PlatformDefinition.CreateAllPlatforms();

            // Look-up by OS should return the most general configuration
            PlatformDefinition? win = platformDefs.Find(UnityOs.Windows);

            Assert.That(win, Is.Not.Null);
            Assert.That(win!.Cpu, Is.EqualTo(UnityCpu.AnyCpu));

            // Look-up explicit configuration
            PlatformDefinition? win64 = platformDefs.Find(UnityOs.Windows, UnityCpu.X64);

            Assert.Multiple(() =>
            {
                Assert.That(win64, Is.Not.Null);
                Assert.That(win.Os, Is.EqualTo(win64!.Os));
            });
            Assert.Multiple(() =>
            {
                Assert.That(win64?.Cpu, Is.EqualTo(UnityCpu.X64));
                Assert.That(win.Children, Does.Contain(win64));
            });

            // Look-up invalid configuration
            PlatformDefinition? and = platformDefs.Find(UnityOs.Android, UnityCpu.None);

            Assert.That(and, Is.Null);
        }

        [Test]
        public void RemainingPlatforms_NoneVisited()
        {
            PlatformDefinition platformDefs = PlatformDefinition.CreateAllPlatforms();
            HashSet<PlatformDefinition> visited = [];

            // If no platform was visited, the remaining platforms should be the (AnyOS, AnyCPU) config.
            HashSet<PlatformDefinition> remaining = platformDefs.GetRemainingPlatforms(visited);

            Assert.That(remaining, Is.Not.Null);
            Assert.Multiple(() =>
            {
                Assert.That(remaining, Has.Count.EqualTo(1));
                Assert.That(platformDefs, Is.EqualTo(remaining.First()));
            });
        }

        [Test]
        public void RemainingPlatforms_OneVisited()
        {
            PlatformDefinition platformDefs = PlatformDefinition.CreateAllPlatforms();

            foreach (PlatformDefinition child in platformDefs.Children)
            {
                HashSet<PlatformDefinition> visited = [child];
                HashSet<PlatformDefinition> remaining = platformDefs.GetRemainingPlatforms(visited);

                // We should get all other children, except the one already visited
                Assert.That(remaining.Count + 1, Is.EqualTo(platformDefs.Children.Count));

                foreach (PlatformDefinition r in remaining)
                {
                    Assert.Multiple(() =>
                    {
                        Assert.That(child, Is.Not.EqualTo(r));
                        Assert.That(platformDefs.Children, Does.Contain(r));
                    });
                }
            }
        }

        [Test]
        public void RemainingPlatforms_LeafVisited()
        {
            PlatformDefinition platformDefs = PlatformDefinition.CreateAllPlatforms();
            PlatformDefinition? win64 = platformDefs.Find(UnityOs.Windows, UnityCpu.X64);
            HashSet<PlatformDefinition> visited = [win64!];

            // The remaining platforms should be all non-windows, as well as all !x64 windows
            HashSet<PlatformDefinition?> expected =
            [
                .. platformDefs.Children
                    .Except([win64!.Parent]),
                .. win64.Parent!.Children
                    .Except([win64]),
            ];
            HashSet<PlatformDefinition> actual = platformDefs.GetRemainingPlatforms(visited);

            Assert.That(expected.SetEquals(actual), Is.True);
        }

        [TestCase("")]
        [TestCase("base")]
        public void TestConfigPath_Root(string basePath)
        {
            PlatformDefinition platformDefs = PlatformDefinition.CreateAllPlatforms();
            PlatformFile file = new("a/b/c.dll", platformDefs);

            // We don't use extra paths for the (AnyOS, AnyCPU) configuration
            string actual = file.GetDestinationPath(basePath);
            string expected = Path.Combine(
                basePath,
                Path.GetFileName(file.SourcePath));

            Assert.That(expected, Is.EqualTo(actual));
        }

        [TestCase("")]
        [TestCase("base")]
        public void TestConfigPath_OsOnly(string basePath)
        {
            PlatformDefinition platformDefs = PlatformDefinition.CreateAllPlatforms();
            PlatformDefinition? win = platformDefs.Find(UnityOs.Windows);
            PlatformFile file = new("a/b/c.dll", win!);

            string actual = file.GetDestinationPath(basePath);
            string expected = Path.Combine(
                basePath,
                "Windows",
                Path.GetFileName(file.SourcePath));

            Assert.That(expected, Is.EqualTo(actual));
        }

        [TestCase("")]
        [TestCase("base")]
        public void TestConfigPath_Full(string basePath)
        {
            PlatformDefinition platformDefs = PlatformDefinition.CreateAllPlatforms();
            PlatformDefinition? win64 = platformDefs.Find(UnityOs.Windows, UnityCpu.X64);
            PlatformFile file = new("a/b/c.dll", win64!);

            string actual = file.GetDestinationPath(basePath);
            string expected = Path.Combine(
                basePath,
                "Windows",
                "x86_64",
                Path.GetFileName(file.SourcePath));

            Assert.That(expected, Is.EqualTo(actual));
        }
    }
}
