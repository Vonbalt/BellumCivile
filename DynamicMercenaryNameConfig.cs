using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Localization;
using TaleWorlds.ModuleManager;

namespace BellumCivile
{
    internal sealed class DynamicMercenaryNameConfig
    {
        private const string MainFileName = "dynamic_mercenary_names.xml";
        private const string PatchFileName = "dynamic_mercenary_names_patch.xml";
        private static DynamicMercenaryNameConfig _instance;

        private readonly Dictionary<string, List<string>> _patternsByCulture =
            new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _fallbackPatterns = new List<string>();

        public static DynamicMercenaryNameConfig Instance => _instance ?? (_instance = Load());

        public TextObject BuildName(string cultureId, Settlement home, Hero founder, string seedKey, Func<string, bool> isNameInUse)
        {
            List<string> patterns = GetPatterns(cultureId);
            int start = PositiveHash(seedKey) % patterns.Count;
            for (int index = 0; index < patterns.Count; index++)
            {
                TextObject candidate = Format(patterns[(start + index) % patterns.Count], home, founder);
                if (isNameInUse == null || !isNameInUse(candidate.ToString()))
                    return candidate;
            }

            TextObject fallback = new TextObject("{FOUNDER_NAME}'s Company");
            fallback.SetTextVariable("FOUNDER_NAME", founder?.FirstName ?? founder?.Name ?? new TextObject("?"));
            return fallback;
        }

        private List<string> GetPatterns(string cultureId)
        {
            if (!string.IsNullOrWhiteSpace(cultureId)
                && _patternsByCulture.TryGetValue(cultureId, out List<string> patterns)
                && patterns.Count > 0)
            {
                return patterns;
            }

            return _fallbackPatterns;
        }

        private static TextObject Format(string pattern, Settlement home, Hero founder)
        {
            TextObject text = new TextObject(pattern ?? string.Empty);
            text.SetTextVariable("SETTLEMENT", home?.Name ?? new TextObject("?"));
            text.SetTextVariable("FOUNDER_NAME", founder?.FirstName ?? founder?.Name ?? new TextObject("?"));
            return text;
        }

        private static DynamicMercenaryNameConfig Load()
        {
            DynamicMercenaryNameConfig config = new DynamicMercenaryNameConfig();
            config.LoadFallbacks();

            try
            {
                string moduleRoot = GetModuleRoot();
                string mainPath = string.IsNullOrWhiteSpace(moduleRoot)
                    ? null
                    : Path.Combine(moduleRoot, "ModuleData", MainFileName);
                if (string.IsNullOrWhiteSpace(mainPath) || !File.Exists(mainPath))
                {
                    BellumCivileLogger.Log($"{MainFileName} not found - using built-in dynamic mercenary names.");
                }
                else
                {
                    config.LoadMainFile(mainPath);
                }

                int patchCount = 0;
                foreach (string patchPath in FindPatchFiles())
                {
                    try
                    {
                        config.ApplyPatchFile(patchPath);
                        patchCount++;
                    }
                    catch (Exception ex)
                    {
                        BellumCivileLogger.Log($"Failed to load dynamic mercenary name patch {patchPath}: {ex.Message}");
                    }
                }

                config.EnsureFallbackPatterns();
                BellumCivileLogger.Log(
                    $"Loaded dynamic mercenary names ({config._patternsByCulture.Count} cultural pools, " +
                    $"{config.CountPatterns()} patterns, {patchCount} compatibility patches).");
            }
            catch (Exception ex)
            {
                BellumCivileLogger.Log($"Failed to load dynamic mercenary names: {ex.Message}");
            }

            return config;
        }

        private void LoadFallbacks()
        {
            _fallbackPatterns.Add("Free Company of {SETTLEMENT}");
            _fallbackPatterns.Add("Free Lances of {SETTLEMENT}");
            _fallbackPatterns.Add("Free Spears of {SETTLEMENT}");
        }

        private void EnsureFallbackPatterns()
        {
            if (_fallbackPatterns.Count > 0)
                return;

            BellumCivileLogger.Log("Dynamic mercenary fallback name pool was empty - restoring built-in fallbacks.");
            LoadFallbacks();
        }

        private void LoadMainFile(string path)
        {
            XmlDocument document = new XmlDocument();
            document.Load(path);

            XmlNode fallbackNode = document.SelectSingleNode("/DynamicMercenaryNames/Fallback");
            if (fallbackNode != null)
                ApplyPool(_fallbackPatterns, fallbackNode, replaceByDefault: true);

            foreach (XmlNode cultureNode in Nodes(document, "/DynamicMercenaryNames/Culture"))
                ApplyCultureNode(cultureNode, replaceByDefault: true);
        }

        private void ApplyPatchFile(string path)
        {
            XmlDocument document = new XmlDocument();
            document.Load(path);

            XmlNode fallbackNode = document.SelectSingleNode("/DynamicMercenaryNamesPatch/Fallback");
            if (fallbackNode != null)
                ApplyPool(_fallbackPatterns, fallbackNode, replaceByDefault: false);

            foreach (XmlNode cultureNode in Nodes(document, "/DynamicMercenaryNamesPatch/Culture"))
                ApplyCultureNode(cultureNode, replaceByDefault: false);
        }

        private void ApplyCultureNode(XmlNode cultureNode, bool replaceByDefault)
        {
            string cultureId = cultureNode?.Attributes?["id"]?.Value?.Trim();
            if (string.IsNullOrWhiteSpace(cultureId))
                return;

            if (!_patternsByCulture.TryGetValue(cultureId, out List<string> patterns))
            {
                patterns = new List<string>();
                _patternsByCulture[cultureId] = patterns;
            }

            ApplyPool(patterns, cultureNode, replaceByDefault);
            if (patterns.Count == 0)
                _patternsByCulture.Remove(cultureId);
        }

        private static void ApplyPool(List<string> patterns, XmlNode poolNode, bool replaceByDefault)
        {
            string mode = poolNode?.Attributes?["mode"]?.Value?.Trim();
            bool append = string.Equals(mode, "append", StringComparison.OrdinalIgnoreCase);
            bool replace = string.Equals(mode, "replace", StringComparison.OrdinalIgnoreCase)
                || (!append && replaceByDefault);
            if (replace)
                patterns.Clear();

            foreach (XmlNode nameNode in Nodes(poolNode, "Name"))
            {
                string pattern = nameNode?.Attributes?["text"]?.Value?.Trim();
                if (!string.IsNullOrWhiteSpace(pattern)
                    && !patterns.Any(existing => string.Equals(existing, pattern, StringComparison.Ordinal)))
                {
                    patterns.Add(pattern);
                }
            }
        }

        private int CountPatterns()
        {
            return _fallbackPatterns.Count + _patternsByCulture.Values.Sum(patterns => patterns.Count);
        }

        private static IEnumerable<XmlNode> Nodes(XmlNode node, string xpath)
        {
            XmlNodeList nodes = node?.SelectNodes(xpath);
            if (nodes == null)
                yield break;

            foreach (XmlNode child in nodes)
                yield return child;
        }

        private static IEnumerable<string> FindPatchFiles()
        {
            HashSet<string> paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ModuleInfo module in ModuleHelper.GetActiveModules() ?? Enumerable.Empty<ModuleInfo>())
            {
                if (module == null || string.IsNullOrWhiteSpace(module.FolderPath))
                    continue;

                string path = Path.Combine(module.FolderPath, "ModuleData", PatchFileName);
                if (File.Exists(path) && paths.Add(path))
                    yield return path;
            }
        }

        private static string GetModuleRoot()
        {
            string dllPath = Assembly.GetExecutingAssembly().Location;
            DirectoryInfo directory = new DirectoryInfo(Path.GetDirectoryName(dllPath));
            while (directory != null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "ModuleData", MainFileName)))
                    return directory.FullName;

                directory = directory.Parent;
            }

            return null;
        }

        private static int PositiveHash(string value)
        {
            unchecked
            {
                uint hash = 2166136261u;
                foreach (char character in value ?? string.Empty)
                {
                    hash ^= character;
                    hash *= 16777619u;
                }

                return (int)(hash & 0x7FFFFFFF);
            }
        }
    }
}
