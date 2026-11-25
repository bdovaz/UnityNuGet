using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using NUnit.Framework;

namespace UnityNuGet.Tests
{
    public class UnityPackageSignerTests
    {
        [Test]
        public void CanSign_RequiresAllCredentialsExecutableAndOrganization()
        {
            string executablePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            try
            {
                File.WriteAllText(executablePath, string.Empty);

                UnityPackageSigner completeSigner = new(
                    new FakeLogger<UnityPackageSigner>(),
                    Options.Create(new SigningOptions
                    {
                        UpmServiceAccountKeyId = "key-id",
                        UpmServiceAccountKeySecret = "key-secret",
                        UnityOrganizationId = 123,
                        UpmExecutableFilePath = executablePath
                    }));

                UnityPackageSigner missingCredentialSigner = new(
                    new FakeLogger<UnityPackageSigner>(),
                    Options.Create(new SigningOptions
                    {
                        UpmServiceAccountKeyId = "key-id",
                        UnityOrganizationId = 123,
                        UpmExecutableFilePath = executablePath
                    }));

                using (Assert.EnterMultipleScope())
                {
                    Assert.That(completeSigner.CanSign(), Is.True);
                    Assert.That(missingCredentialSigner.CanSign(), Is.False);
                    Assert.That(completeSigner.CanSign("testPackageId"), Is.True);
                }
            }
            finally
            {
                File.Delete(executablePath);
            }
        }

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
