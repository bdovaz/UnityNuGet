using System.Collections.Generic;
using NuGet.Frameworks;
using NuGet.Packaging;
using NUnit.Framework;
using static NuGet.Frameworks.FrameworkConstants;

namespace UnityNuGet.Tests
{
    public class NuGetHelperTests
    {
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
