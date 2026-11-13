using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using NuGet.Common;
using NuGet.Packaging;
using NUnit.Framework;

namespace UnityNuGet.Tests
{
    public class NativeLibrariesTests
    {
        [TestCase("win-x86", "plugin.dll", UnityOs.Windows, UnityCpu.X86)]
        [TestCase("win10-x64", "plugin.DLL", UnityOs.Windows, UnityCpu.X64)]
        [TestCase("WIN-x64", "plugin.dll", UnityOs.Windows, UnityCpu.X64)]
        [TestCase("linux-x64", "plugin.so", UnityOs.Linux, UnityCpu.X64)]
        [TestCase("linux.6-x64", "plugin.so", UnityOs.Linux, UnityCpu.X64)]
        [TestCase("osx", "plugin.dylib", UnityOs.OSX, UnityCpu.AnyCpu)]
        [TestCase("osx-x64", "plugin.dylib", UnityOs.OSX, UnityCpu.X64)]
        [TestCase("osx-arm64", "plugin.dylib", UnityOs.OSX, UnityCpu.ARM64)]
        [TestCase("ios", "plugin.a", UnityOs.iOS, UnityCpu.AnyCpu)]
        [TestCase("android-arm", "plugin.so", UnityOs.Android, UnityCpu.ARMv7)]
        [TestCase("android-arm64", "plugin.so", UnityOs.Android, UnityCpu.ARM64)]
        [TestCase("android-x86", "plugin.so", UnityOs.Android, UnityCpu.X86)]
        [TestCase("android-x64", "plugin.so", UnityOs.Android, UnityCpu.X64)]
        [TestCase("browser-wasm", "plugin.a", UnityOs.WebGL, UnityCpu.AnyCpu)]
        public async Task FindsSupportedNativeFile(string rid, string fileName, UnityOs os, UnityCpu cpu)
        {
            string path = $"runtimes/{rid}/native/{fileName}";
            using MemoryStream package = CreatePackage(path);
            using PackageArchiveReader reader = new(package);
            RecordingLogger logger = new();

            var results = await NativeLibraries.GetSupportedNativeLibsAsync(reader, logger).ToListAsync();

            Assert.That(results, Has.Count.EqualTo(1));
            var result = results[0];
            Assert.That(result.file.Replace('\\', '/'), Is.EqualTo(path));
            Assert.That(result.folders, Is.EqualTo(new[] { "runtimes", rid, "native" }));
            Assert.That(result.os, Is.EqualTo(os));
            Assert.That(result.cpu, Is.EqualTo(cpu));
            Assert.That(logger.Messages, Is.Empty);
        }

        [TestCase("runtimes/win-x64/lib/netstandard2.0/plugin.dll", "non-native library")]
        [TestCase("runtimes/ios/native/libopus.xcframework/ios-arm64/plugin.a", "non-native library")]
        [TestCase("runtimes/freebsd-x64/native/plugin.so", "unsupported OS")]
        [TestCase("runtimes/browser-unknown/native/plugin.a", "unsupported OS")]
        [TestCase("runtimes/browser/native/plugin.a", "unsupported OS")]
        [TestCase("runtimes/android-mips/native/plugin.so", "unsupported CPU")]
        [TestCase("runtimes/android/native/plugin.so", "unsupported CPU")]
        [TestCase("runtimes/win/native/plugin.dll", "unsupported CPU")]
        [TestCase("runtimes/linux-arm64/native/plugin.so", "unsupported native library")]
        [TestCase("runtimes/win-arm64/native/plugin.dll", "unsupported native library")]
        [TestCase("runtimes/ios-arm64/native/plugin.a", "unsupported native library")]
        [TestCase("runtimes/win-x64/native/plugin.a", "unsupported native library")]
        [TestCase("runtimes/android-arm64/native/plugin.txt", "unsupported native library")]
        public async Task SkipsUnsupportedNativeFileWithDiagnostic(string path, string diagnostic)
        {
            using MemoryStream package = CreatePackage(path);
            using PackageArchiveReader reader = new(package);
            RecordingLogger logger = new();

            var results = await NativeLibraries.GetSupportedNativeLibsAsync(reader, logger).ToListAsync();

            Assert.That(results, Is.Empty);
            Assert.That(logger.Messages, Has.Count.EqualTo(1));
            Assert.That(logger.Messages[0], Does.Contain(diagnostic));
            Assert.That(logger.Messages[0].Replace('\\', '/'), Does.Contain(path));
        }

        [Test]
        public async Task ReturnsNoFilesWhenPackageHasNoRuntimes()
        {
            using MemoryStream package = CreatePackage("lib/netstandard2.0/example.dll");
            using PackageArchiveReader reader = new(package);
            RecordingLogger logger = new();

            var results = await NativeLibraries.GetSupportedNativeLibsAsync(reader, logger).ToListAsync();

            Assert.That(results, Is.Empty);
            Assert.That(logger.Messages, Is.Empty);
        }

        [Test]
        public async Task ReturnsOnlySupportedFilesFromMixedPackage()
        {
            using MemoryStream package = CreatePackage(
                "runtimes/win-x64/native/first.dll",
                "runtimes/android-arm64/native/second.so",
                "runtimes/freebsd-x64/native/ignored.so",
                "runtimes/linux-x64/native/readme.txt");
            using PackageArchiveReader reader = new(package);
            RecordingLogger logger = new();

            var results = await NativeLibraries.GetSupportedNativeLibsAsync(reader, logger).ToListAsync();

            Assert.That(results.Select(x => (x.file.Replace('\\', '/'), x.os, x.cpu)), Is.EquivalentTo(new[]
            {
                ("runtimes/win-x64/native/first.dll", UnityOs.Windows, UnityCpu.X64),
                ("runtimes/android-arm64/native/second.so", UnityOs.Android, UnityCpu.ARM64)
            }));
            Assert.That(logger.Messages, Has.Count.EqualTo(2));
        }

        [TestCase("plugin.dll", UnityOs.Windows, true)]
        [TestCase("plugin.DLL", UnityOs.Windows, true)]
        [TestCase("plugin.so", UnityOs.Linux, true)]
        [TestCase("plugin.SO", UnityOs.Android, true)]
        [TestCase("plugin.dylib", UnityOs.OSX, true)]
        [TestCase("plugin.DYLIB", UnityOs.OSX, true)]
        [TestCase("plugin.a", UnityOs.iOS, true)]
        [TestCase("plugin.A", UnityOs.WebGL, true)]
        [TestCase("plugin.a", UnityOs.Windows, false)]
        [TestCase("plugin.a", UnityOs.Linux, false)]
        [TestCase("plugin.a", UnityOs.OSX, false)]
        [TestCase("plugin.a", UnityOs.Android, false)]
        [TestCase("plugin.txt", UnityOs.iOS, false)]
        [TestCase("plugin", UnityOs.WebGL, false)]
        public void MatchesSupportedNativeExtensions(string path, UnityOs os, bool expected)
        {
            Assert.That(NativeLibraries.IsSupportedNativeFile(path, os), Is.EqualTo(expected));
        }

        private static MemoryStream CreatePackage(params string[] paths)
        {
            MemoryStream stream = new();
            using (ZipArchive archive = new(stream, ZipArchiveMode.Create, leaveOpen: true))
            {
                using (StreamWriter nuspec = new(archive.CreateEntry("Example.nuspec").Open()))
                {
                    nuspec.Write("<package><metadata><id>Example</id><version>1.0.0</version><authors>Test</authors><description>Test</description></metadata></package>");
                }
                foreach (string path in paths)
                {
                    using StreamWriter writer = new(archive.CreateEntry(path).Open());
                    writer.Write("native asset");
                }
            }
            stream.Position = 0;
            return stream;
        }

        private sealed class RecordingLogger : LoggerBase
        {
            public List<string> Messages { get; } = [];

            public override void Log(ILogMessage message) => Messages.Add(message.Message);

            public override Task LogAsync(ILogMessage message)
            {
                Log(message);
                return Task.CompletedTask;
            }
        }
    }
}
