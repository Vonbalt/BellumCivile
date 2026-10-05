using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using TaleWorlds.Localization;
using TaleWorlds.ModuleManager;

namespace BellumCivile
{
    internal static class RealmNameConfig
    {
        private sealed class Entry
        {
            public string Root;
            public string NativeName;
        }

        private static Dictionary<string, Entry> _entries;
        public static void Reset() => _entries = null;

        public static string ResolveRoot(string kingdomId, string nativeName)
            => ResolveRootText(kingdomId, nativeName)?.ToString();

        public static TextObject ResolveRootText(string kingdomId, string nativeName)
        {
            if (_entries == null) Load();
            if (kingdomId == null || !_entries.TryGetValue(kingdomId, out Entry entry)) return null;
            // Do not replace player renames or a total conversion's different realm identity.
            if (!string.IsNullOrWhiteSpace(entry.NativeName)
                && !string.Equals(new TextObject(entry.NativeName).ToString(), nativeName, StringComparison.Ordinal))
                return null;
            return new TextObject(entry.Root);
        }

        private static void Load()
        {
            _entries = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
            foreach (ModuleInfo module in ModuleHelper.GetActiveModules())
            {
                if (string.IsNullOrWhiteSpace(module?.FolderPath)) continue;
                foreach (string name in new[] { "bellum_realm_names.xml", "bellum_realm_names_patch.xml" })
                {
                    string path = Path.Combine(module.FolderPath, "ModuleData", name);
                    if (!File.Exists(path)) continue;
                    try
                    {
                        var document = new XmlDocument { XmlResolver = null };
                        document.Load(path);
                        foreach (XmlNode node in document.SelectNodes("/RealmNames/Realm[@id]"))
                        {
                            string id = node.Attributes["id"].Value.Trim();
                            string root = node.Attributes["root"]?.Value;
                            if (id.Length == 0) continue;
                            if (string.IsNullOrWhiteSpace(root)) { _entries.Remove(id); continue; }
                            _entries[id] = new Entry { Root = root, NativeName = node.Attributes["nativeName"]?.Value };
                        }
                    }
                    catch (Exception ex)
                    {
                        BellumCivileLogger.Log($"Failed to load realm name roots from {path}: {ex.Message}");
                    }
                }
            }
        }
    }
}
