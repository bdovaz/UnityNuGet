using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using UnityNuGet.Tool.Commands;

namespace UnityNuGet.Tool.Tests
{
    public class UnityNuGetCommandTests
    {
        [TestCase(true, 0)]
        [TestCase(false, 1)]
        public async Task InvokeReturnsExitCodeFromRegistryCacheUpdater(bool executeResult, int expectedExitCode)
        {
            Mock<IRegistryCacheUpdater> registryCacheUpdater = new();
            registryCacheUpdater
                .Setup(updater => updater.Execute(It.IsAny<CancellationToken>()))
                .ReturnsAsync(executeResult);

            UnityNuGetCommand command = new(registryCacheUpdater.Object);

            int exitCode = await command.Parse([]).InvokeAsync();

            Assert.That(exitCode, Is.EqualTo(expectedExitCode));
            registryCacheUpdater.Verify(
                updater => updater.Execute(It.IsAny<CancellationToken>()),
                Times.Once);
        }
    }
}
