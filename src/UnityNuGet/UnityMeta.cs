using System;
using System.Collections.Generic;
using System.Linq;
using Scriban;
using Scriban.Runtime;

namespace UnityNuGet
{
    /// <summary>
    /// Helper methods to create Unity .meta files
    /// </summary>
    internal static class UnityMeta
    {
        public static string? GetMetaForExtension(Guid guid, string extension)
        {
            switch (extension)
            {
                case ".pdb":
                    break;
                case ".asmdef":
                case ".cs":
                case ".json":
                case ".md":
                case ".txt":
                case ".xml":
                    return GetMetaForText(guid);
            }

            return null;
        }

        public static string GetMetaForDll(
            Guid guid,
            PlatformDefinition platformDef,
            IEnumerable<string> labels,
            IEnumerable<string> defineConstraints)
        {
            const string text = @"fileFormatVersion: 2
guid: {{ guid }}
{{ labels }}PluginImporter:
  externalObjects: {}
  serializedVersion: 2
  iconMap: {}
  executionOrder: {}
{{ constraints }}  isPreloaded: 0
  isOverridable: 0
  isExplicitlyReferenced: 0
  validateReferences: 1
  platformData:{{ excludePlatforms }}
  - first:
      Any:
    second:
      enabled: {{ allEnabled }}
      settings: {}{{ perPlatformSettings }}
  - first:
      Windows Store Apps: WindowsStoreApps
    second:
      enabled: 0
      settings:
        CPU: AnyCPU
  userData:
  assetBundleName:
  assetBundleVariant:
";

            var allLabels = labels.ToList();
            var allConstraints = defineConstraints.ToList();

            static string FormatList(IEnumerable<string> items) => string.Join(
                string.Empty,
                items.Select(d => $"  - {d}\n"));

            string excludePlatforms = string.Empty;
            string perPlatformSettings = @"
  - first:
      Editor: Editor
    second:
      enabled: 0
      settings:
        DefaultValueInitialized: true";

            // Render the per-platform settings
            if (platformDef.Os != UnityOs.AnyOs)
            {
                // Determine which configurations are enabled
                PlatformDefinition? platWin = platformDef.Find(UnityOs.Windows, UnityCpu.X86);
                PlatformDefinition? platWin64 = platformDef.Find(UnityOs.Windows, UnityCpu.X64);
                PlatformDefinition? platLinux64 = platformDef.Find(UnityOs.Linux, UnityCpu.X64);
                PlatformDefinition? platOsx = platformDef.Find(UnityOs.OSX);
                PlatformDefinition? platAndroid = platformDef.Find(UnityOs.Android);
                PlatformDefinition? platWasm = platformDef.Find(UnityOs.WebGL);
                PlatformDefinition? platIos = platformDef.Find(UnityOs.iOS);
                PlatformDefinition? platEditor = platformDef.FindEditor();

                ScriptObject platformScriptObject = new()
                {
                    ["enablesWin"] = (platWin != null) ? 1 : 0,
                    ["enablesWin64"] = (platWin64 != null) ? 1 : 0,
                    ["enablesLinux64"] = (platLinux64 != null) ? 1 : 0,
                    ["enablesOsx"] = (platOsx != null) ? 1 : 0,
                    ["enablesAndroid"] = (platAndroid != null) ? 1 : 0,
                    ["enablesWasm"] = (platWasm != null) ? 1 : 0,
                    ["enablesIos"] = (platIos != null) ? 1 : 0,
                    ["enablesEditor"] = (platEditor != null) ? 1 : 0,

                    ["cpuWin"] = (platWin?.Cpu ?? UnityCpu.None).GetName(),
                    ["cpuWin64"] = (platWin64?.Cpu ?? UnityCpu.None).GetName(),
                    ["cpuLinux64"] = (platLinux64?.Cpu ?? UnityCpu.None).GetName(),
                    ["cpuOsx"] = (platOsx?.Cpu ?? UnityCpu.None).GetName(),
                    ["cpuAndroid"] = (platAndroid?.Cpu ?? UnityCpu.None).GetName(),
                    ["cpuIos"] = (platIos?.Cpu ?? UnityCpu.None).GetName(),
                    ["cpuEditor"] = (platEditor?.Cpu ?? UnityCpu.None).GetName(),

                    ["osEditor"] = (platEditor?.Os ?? UnityOs.AnyOs).GetName(),
                };

                const string excludePlatformsText = @"
  - first:
      : Any
    second:
      enabled: 0
      settings:
        Exclude Android: {{ 1 - enablesAndroid }}
        Exclude Editor: {{ 1 - enablesEditor }}
        Exclude Linux64: {{ 1 - enablesLinux64 }}
        Exclude OSXUniversal: {{ 1 - enablesOsx }}
        Exclude WebGL: {{ 1 - enablesWasm }}
        Exclude Win: {{ 1 - enablesWin }}
        Exclude Win64: {{ 1 - enablesWin64 }}
        Exclude iOS: {{ 1 - enablesIos }}";

                const string perPlatformSettingsText = @"
  - first:
      Android: Android
    second:
      enabled: {{ enablesAndroid }}
      settings:
        CPU: {{ cpuAndroid }}
  - first:
      Editor: Editor
    second:
      enabled: {{ enablesEditor }}
      settings:
        CPU: {{ cpuEditor }}
        DefaultValueInitialized: true
        OS: {{ osEditor }}
  - first:
      Standalone: Linux64
    second:
      enabled: {{ enablesLinux64 }}
      settings:
        CPU: {{ cpuLinux64 }}
  - first:
      Standalone: OSXUniversal
    second:
      enabled: {{ enablesOsx }}
      settings:
        CPU: {{ cpuOsx }}
  - first:
      Standalone: Win
    second:
      enabled: {{ enablesWin }}
      settings:
        CPU: {{ cpuWin }}
  - first:
      Standalone: Win64
    second:
      enabled: {{ enablesWin64 }}
      settings:
        CPU: {{ cpuWin64 }}
  - first:
      WebGL: WebGL
    second:
      enabled: {{ enablesWasm }}
      settings: {}
  - first:
      iPhone: iOS
    second:
      enabled: {{ enablesIos }}
      settings:
        AddToEmbeddedBinaries: false
        CPU: {{ cpuIos }}
        CompileFlags:
        FrameworkDependencies: ";

                TemplateContext platformTemplateContext = new();
                platformTemplateContext.PushGlobal(platformScriptObject);

                excludePlatforms = Template
                    .Parse(excludePlatformsText)
                    .Render(platformTemplateContext);

                perPlatformSettings = Template
                    .Parse(perPlatformSettingsText)
                    .Render(platformTemplateContext);
            }

            bool allPlatformsEnabled = (platformDef.Os == UnityOs.AnyOs) && (platformDef.Cpu == UnityCpu.AnyCpu);

            ScriptObject dllMetaScriptObject = new()
            {
                ["excludePlatforms"] = excludePlatforms,
                ["perPlatformSettings"] = perPlatformSettings,
                ["guid"] = guid.ToString("N"),
                ["allEnabled"] = allPlatformsEnabled ? "1" : "0",
                ["labels"] = allLabels.Count == 0
                    ? string.Empty
                    : $"labels:\n{FormatList(allLabels)}",
                ["constraints"] = allConstraints.Count == 0
                    ? string.Empty
                    : $"  defineConstraints:\n{FormatList(allConstraints)}"
            };

            TemplateContext dllMetaTemplateContext = new();
            dllMetaTemplateContext.PushGlobal(dllMetaScriptObject);

            return Template
                .Parse(text)
                .Render(dllMetaTemplateContext)
                .StripWindowsNewlines();
        }

        public static string GetMetaForFolder(Guid guid)
        {
            return $@"fileFormatVersion: 2
guid: {guid:N}
folderAsset: yes
DefaultImporter:
  externalObjects: {{}}
  userData:
  assetBundleName:
  assetBundleVariant:
".StripWindowsNewlines();
        }

        private static string GetMetaForText(Guid guid)
        {
            return $@"fileFormatVersion: 2
guid: {guid:N}
TextScriptImporter:
  externalObjects: {{}}
  userData:
  assetBundleName:
  assetBundleVariant:
".StripWindowsNewlines();
        }

        private static string StripWindowsNewlines(this string input) => input.Replace("\r\n", "\n");
    }
}
