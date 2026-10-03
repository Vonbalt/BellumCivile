using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml;

namespace BellumCivile
{
    public enum CourtPolicyStance { Oppose = -1, Neutral = 0, Support = 1 }

    internal sealed class IdeologyPolicyAgendaConfig
    {
        private static IdeologyPolicyAgendaConfig _instance;
        public static IdeologyPolicyAgendaConfig Instance => _instance ?? (_instance = Load());
        private readonly Dictionary<FactionType, Dictionary<string, CourtPolicyStance>> _stances =
            CourtFactionRoster.Types.ToDictionary(t => t, t => new Dictionary<string, CourtPolicyStance>(StringComparer.Ordinal));
        private readonly HashSet<string> _crown = new HashSet<string>(StringComparer.Ordinal);

        public List<string> GetSupportedPolicies(FactionType type) =>
            _stances.TryGetValue(type, out var policies)
                ? policies.Where(p => p.Value == CourtPolicyStance.Support).Select(p => p.Key).ToList()
                : new List<string>();
        public CourtPolicyStance GetStance(FactionType type, string id) =>
            id != null && _stances.TryGetValue(type, out var policies) && policies.TryGetValue(id, out var stance)
                ? stance : CourtPolicyStance.Neutral;
        public bool IsCrownPolicy(string id) => id != null && _crown.Contains(id);
        public IEnumerable<string> CrownPolicies => _crown;

        private static IdeologyPolicyAgendaConfig Load()
        {
            var config = new IdeologyPolicyAgendaConfig();
            var assembly = Assembly.GetExecutingAssembly();
            using (var stream = assembly.GetManifestResourceStream("BellumCivile.ModuleData.bellum_policy_agendas.xml"))
                if (stream != null) config.Read(stream, false);
            string root = FindModuleRoot(assembly.Location);
            if (root == null) return config;
            string main = Path.Combine(root, "ModuleData", "bellum_policy_agendas.xml");
            config.ReadFile(main, false);
            string ownPatch = Path.Combine(root, "ModuleData", "bellum_policy_agendas_patch.xml");
            config.ReadFile(ownPatch, true);
            string modules = Directory.GetParent(root)?.FullName;
            if (modules != null && Directory.Exists(modules))
                foreach (string directory in Directory.GetDirectories(modules).OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
                {
                    if (string.Equals(directory, root, StringComparison.OrdinalIgnoreCase)) continue;
                    config.ReadFile(Path.Combine(directory, "ModuleData", "bellum_policy_agendas_patch.xml"), true);
                }
            return config;
        }

        private void ReadFile(string path, bool patch)
        {
            if (!File.Exists(path)) return;
            try { using (var stream = File.OpenRead(path)) Read(stream, patch); }
            catch (Exception ex) { BellumCivileLogger.Log($"Policy agenda XML {path}: {ex.Message}"); }
        }

        private void Read(Stream stream, bool patch)
        {
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
            var doc = new XmlDocument { XmlResolver = null };
            using (var reader = XmlReader.Create(stream, settings)) doc.Load(reader);
            string root = patch ? "BellumPolicyAgendaPatch" : "BellumPolicyAgendas";
            if (doc.DocumentElement?.Name != root) throw new XmlException("Unexpected policy agenda root.");
            if (!patch)
            {
                _crown.Clear();
                foreach (var values in _stances.Values) values.Clear();
            }
            foreach (XmlNode owner in doc.DocumentElement.ChildNodes)
            {
                if (owner.NodeType != XmlNodeType.Element) continue;
                bool crown = owner.Name == "Crown";
                FactionType type = default;
                if (!crown && (owner.Name != "Faction" || !Enum.TryParse(owner.Attributes?["type"]?.Value, out type)
                    || !CourtFactionRoster.Types.Contains(type)))
                {
                    BellumCivileLogger.Log($"Unknown policy agenda owner: {owner.OuterXml}");
                    continue;
                }
                foreach (XmlNode entry in owner.ChildNodes)
                {
                    if (entry.NodeType != XmlNodeType.Element) continue;
                    string id = entry.Attributes?["id"]?.Value?.Trim();
                    if (string.IsNullOrEmpty(id)) continue;
                    if (patch && entry.Name == "Remove")
                    {
                        if (crown) _crown.Remove(id); else _stances[type].Remove(id);
                        continue;
                    }
                    if (entry.Name != "Policy" && !(patch && entry.Name == "Add")) continue;
                    string text = entry.Attributes?["stance"]?.Value ?? "Support";
                    if (!Enum.TryParse(text, true, out CourtPolicyStance stance)
                        || !Enum.IsDefined(typeof(CourtPolicyStance), stance) || (crown && stance != CourtPolicyStance.Support))
                    {
                        BellumCivileLogger.Log($"Invalid policy stance: {entry.OuterXml}");
                        continue;
                    }
                    if (crown) _crown.Add(id); else _stances[type][id] = stance;
                }
            }
        }

        private static string FindModuleRoot(string location)
        {
            var directory = new DirectoryInfo(Path.GetDirectoryName(location));
            while (directory != null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "ModuleData", "bellum_policy_agendas.xml")))
                    return directory.FullName;
                directory = directory.Parent;
            }
            return null;
        }
    }
}
