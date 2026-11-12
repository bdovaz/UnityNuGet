using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using NuGet.Common;
using NuGet.Packaging;

namespace UnityNuGet
{
    static class NativeLibraries
    {
        public static async IAsyncEnumerable<(string file, string[] folders, UnityOs os, UnityCpu cpu)> GetSupportedNativeLibsAsync(
            PackageReaderBase packageReader,
            ILogger logger)
        {
            IEnumerable<FrameworkSpecificGroup> versions = await packageReader.GetItemsAsync(PackagingConstants.Folders.Runtimes, CancellationToken.None);
            IEnumerable<string> files = versions.SelectMany(v => v.Items);
            PlatformDefinition platformDefs = PlatformDefinition.CreateAllPlatforms();

            foreach (string? file in files)
            {
                string folderPath = Path.GetDirectoryName(file)!;
                string[] folders = folderPath.Split(Path.DirectorySeparatorChar);

                if (folders.Length != 3 || !folders[2].Equals("native", StringComparison.OrdinalIgnoreCase))
                {
                    logger.LogInformation($"Skipping non-native library file located in the runtimes folder: {file} ...");
                    continue;
                }

                string[] system = folders[1].Split('-');

                if (system.Length < 1)
                {
                    logger.LogInformation($"Skipping file located in the runtime folder that does not specify platform: {file} ...");
                    continue;
                }

                // https://learn.microsoft.com/en-us/dotnet/core/rid-catalog
                UnityOs? os = system[0] switch
                {
                    string name when name.StartsWith("linux", StringComparison.OrdinalIgnoreCase) => UnityOs.Linux,
                    string name when name.StartsWith("osx", StringComparison.OrdinalIgnoreCase) => UnityOs.OSX,
                    string name when name.StartsWith("win", StringComparison.OrdinalIgnoreCase) => UnityOs.Windows,
                    string name when name.StartsWith("ios", StringComparison.OrdinalIgnoreCase) => UnityOs.iOS,
                    "android" => UnityOs.Android,
                    "browser" when system.Length > 1 && system[1] == "wasm" => UnityOs.WebGL,
                    _ => null
                };

                if (os is null)
                {
                    logger.LogInformation($"Skipping file for unsupported OS: {file} ...");
                    continue;
                }

                UnityCpu? cpu = null;
                if (os == UnityOs.WebGL || (system.Length == 1 && (os == UnityOs.OSX || os == UnityOs.iOS)))
                {
                    cpu = UnityCpu.AnyCpu;
                }
                else if (system.Length > 1)
                {
                    cpu = system[1] switch
                    {
                        "x86" => UnityCpu.X86,
                        "x64" => UnityCpu.X64,
                        "arm" => UnityCpu.ARMv7,
                        "arm64" => UnityCpu.ARM64,
                        _ => null
                    };
                }

                if (cpu is null)
                {
                    logger.LogInformation($"Skipping file for unsupported CPU: {file} ...");
                    continue;
                }

                if (platformDefs.Find(os.Value, cpu.Value) == null || !IsSupportedNativeFile(file, os.Value))
                {
                    logger.LogInformation($"Skipping unsupported native library: {file} ...");
                    continue;
                }

                yield return (file, folders, os.Value, cpu.Value);
            }
        }

        public static bool IsSupportedNativeFile(string file, UnityOs os)
        {
            string extension = Path.GetExtension(file);
            return extension.Equals(".dll", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".so", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".dylib", StringComparison.OrdinalIgnoreCase)
                || (extension.Equals(".a", StringComparison.OrdinalIgnoreCase) && (os == UnityOs.WebGL || os == UnityOs.iOS));
        }
    }
}
