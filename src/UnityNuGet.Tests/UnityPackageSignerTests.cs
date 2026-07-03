using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using NUnit.Framework;

namespace UnityNuGet.Tests
{
    public class UnityPackageSignerTests
    {
        [Test]
        [TestCase("testPackageId", null, true)]
        [TestCase("testPackageId", "testPackageId2", false)]
        [TestCase("testPackageId", "test.*Id", true)]
        public void CanSignPackage(string packageId, string? pattern, bool expected)
        {
            UnityPackageSigner unityPackageSigner = new(new FakeLogger<UnityPackageSigner>(), Options.Create(new SigningOptions { PackageIdRegexPattern = pattern }));

            Assert.AreEqual(expected, unityPackageSigner.CanSign(packageId));
        }
    }
}
