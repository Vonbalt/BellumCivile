using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml;
using MCM.Common;

namespace BellumCivile
{
    internal static class FeudalTitleStylePresetCatalog
    {
        internal sealed class Preset
        {
            public string Id;
            public string Name;
            public string Path;
            public int Order;
        }

        private const string FilePrefix = "bellum_title_styles_";
        private const string DefaultPresetId = "anglicized";
        private static IReadOnlyList<Preset> _presets;

        internal static Dropdown<string> CreateDropdown()
        {
            IReadOnlyList<Preset> presets = Presets;
            string[] names = presets.Count > 0
                ? presets.Select(preset => preset.Name).ToArray()
                : new[] { "{=BC_TitleStylePreset_Embedded}Embedded defaults" };
            int selectedIndex = FindDefaultIndex(presets);
            return new Dropdown<string>(names, selectedIndex);
        }

        internal static Preset GetSelectedPreset(int selectedIndex)
        {
            IReadOnlyList<Preset> presets = Presets;
            if (selectedIndex >= 0 && selectedIndex < presets.Count)
                return presets[selectedIndex];

            int defaultIndex = FindDefaultIndex(presets);
            return defaultIndex >= 0 && defaultIndex < presets.Count ? presets[defaultIndex] : null;
        }

        internal static int GetDefaultPresetIndex()
        {
            return FindDefaultIndex(Presets);
        }

        private static IReadOnlyList<Preset> Presets => _presets ?? (_presets = DiscoverPresets());

        private static IReadOnlyList<Preset> DiscoverPresets()
        {
            List<Preset> presets = new List<Preset>();
            try
            {
                string moduleRoot = GetModuleRoot();
                if (string.IsNullOrEmpty(moduleRoot))
                    return presets;

                AddPresetsFromModule(presets, moduleRoot);

                string modulesRoot = Directory.GetParent(moduleRoot)?.FullName;
                if (!string.IsNullOrEmpty(modulesRoot) && Directory.Exists(modulesRoot))
                {
                    foreach (string moduleDirectory in Directory.GetDirectories(modulesRoot).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
                    {
                        if (!string.Equals(moduleDirectory, moduleRoot, StringComparison.OrdinalIgnoreCase))
                            AddPresetsFromModule(presets, moduleDirectory);
                    }
                }
            }
            catch (Exception ex)
            {
                BellumCivileLogger.Log($"Failed to discover feudal title style presets: {ex.Message}");
            }

            return presets
                .OrderBy(preset => preset.Order)
                .ThenBy(preset => preset.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static void AddPresetsFromModule(List<Preset> presets, string moduleRoot)
        {
            string moduleDataPath = Path.Combine(moduleRoot, "ModuleData");
            if (!Directory.Exists(moduleDataPath))
                return;

            foreach (string path in Directory.GetFiles(moduleDataPath, FilePrefix + "*.xml").OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
            {
                Preset preset = ReadPreset(path);
                if (preset == null || presets.Any(existing => string.Equals(existing.Id, preset.Id, StringComparison.OrdinalIgnoreCase)))
                    continue;

                presets.Add(preset);
            }
        }

        private static Preset ReadPreset(string path)
        {
            try
            {
                XmlDocument document = new XmlDocument();
                document.Load(path);
                XmlElement root = document.DocumentElement;
                if (root == null || !string.Equals(root.Name, "BellumFeudalTitles", StringComparison.OrdinalIgnoreCase))
                    return null;

                string fileId = Path.GetFileNameWithoutExtension(path).Substring(FilePrefix.Length);
                string id = Attribute(root, "presetId");
                string name = Attribute(root, "presetName");
                string orderText = Attribute(root, "presetOrder");
                int order = int.TryParse(orderText, out int parsedOrder) ? parsedOrder : 100;

                return new Preset
                {
                    Id = string.IsNullOrWhiteSpace(id) ? fileId : id,
                    Name = string.IsNullOrWhiteSpace(name) ? Humanize(fileId) : name,
                    Path = path,
                    Order = order
                };
            }
            catch (Exception ex)
            {
                BellumCivileLogger.Log($"Failed to read title style preset '{path}': {ex.Message}");
                return null;
            }
        }

        private static int FindDefaultIndex(IReadOnlyList<Preset> presets)
        {
            for (int index = 0; index < presets.Count; index++)
            {
                if (string.Equals(presets[index].Id, DefaultPresetId, StringComparison.OrdinalIgnoreCase))
                    return index;
            }

            return presets.Count > 0 ? 0 : 0;
        }

        private static string Attribute(XmlElement element, string name)
        {
            return element?.GetAttribute(name)?.Trim() ?? string.Empty;
        }

        private static string Humanize(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "Title style preset";

            string spaced = value.Replace('_', ' ').Replace('-', ' ').Trim();
            return char.ToUpperInvariant(spaced[0]) + spaced.Substring(1);
        }

        private static string GetModuleRoot()
        {
            string dllPath = Assembly.GetExecutingAssembly().Location;
            DirectoryInfo directory = new DirectoryInfo(Path.GetDirectoryName(dllPath));
            while (directory != null)
            {
                if (Directory.Exists(Path.Combine(directory.FullName, "ModuleData")))
                    return directory.FullName;

                directory = directory.Parent;
            }

            return Path.GetDirectoryName(dllPath);
        }
    }
}
