using NUnit.Framework;

namespace UnityNuGet.Tool.Tests
{
    public class ValidateCommandTests
    {
        [Test]
        public void ValidateCommand_TryParseArgs_HandlesValidAndInvalidInputs()
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(
                    ValidateCommand.TryParseArgs(
                        ["registry.json", "--package", "Serilog", "-p", "Acornima", "--skip-conversion"],
                        out RegistryValidationOptions? parsed,
                        out bool showHelp,
                        out string? error),
                    Is.True);
                Assert.That(showHelp, Is.False);
                Assert.That(error, Is.Null);
                Assert.That(parsed!.RegistryFilePath, Is.EqualTo("registry.json"));
                Assert.That(parsed.PackageNames, Is.EqualTo(["Serilog", "Acornima"]).AsCollection);
                Assert.That(parsed.SkipConversion, Is.True);
                Assert.That(parsed.ValidateAll, Is.False);

                Assert.That(
                    ValidateCommand.TryParseArgs(["--help"], out _, out bool helpFlag, out _),
                    Is.True);
                Assert.That(helpFlag, Is.True);

                Assert.That(
                    ValidateCommand.TryParseArgs(["--package", "Serilog"], out _, out _, out string? missingPathError),
                    Is.False);
                Assert.That(missingPathError, Does.Contain("Missing required"));

                Assert.That(
                    ValidateCommand.TryParseArgs(["registry.json", "--unknown"], out _, out _, out string? unknownError),
                    Is.False);
                Assert.That(unknownError, Does.Contain("Unknown option"));
            }
        }
    }
}
