using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml;
using TaleWorlds.Localization;

namespace BellumCivile
{
    public sealed class RealmLawDefinition
    {
        public string Id { get; internal set; }
        public string GroupId { get; internal set; }
        public string Category { get; internal set; }
        public string Effect { get; internal set; }
        public string Value { get; internal set; }
        internal GenderSuccessionLaw? GenderValue;
        internal HouseSuccessionLaw? SuccessionValue;
        internal int? MandateYears;
        public CourtPolicyStance CrownStance { get; internal set; } = CourtPolicyStance.Neutral;
        public TextObject Name => new TextObject(NameText);
        public TextObject Description => new TextObject(DescriptionText);
        internal string NameText;
        internal string DescriptionText;
        internal readonly Dictionary<FactionType, CourtPolicyStance> Stances = new Dictionary<FactionType, CourtPolicyStance>();
        public CourtPolicyStance GetStance(FactionType faction) =>
            Stances.TryGetValue(faction, out var stance) ? stance : CourtPolicyStance.Neutral;
    }

    public sealed class RealmLawRegistry
    {
        public const string GenderGroup = "gender";
        public const string SuccessionGroup = "succession";
        public const string TermGroup = "elective_terms";
        private static readonly Lazy<RealmLawRegistry> Loaded = new Lazy<RealmLawRegistry>(Load);
        public static RealmLawRegistry Instance => Loaded.Value;
        private readonly Dictionary<string, RealmLawDefinition> _laws = new Dictionary<string, RealmLawDefinition>(StringComparer.Ordinal);
        private readonly Dictionary<string, List<RealmLawDefinition>> _groups = new Dictionary<string, List<RealmLawDefinition>>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _defaults = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<GenderSuccessionLaw, RealmLawDefinition> _gender = new Dictionary<GenderSuccessionLaw, RealmLawDefinition>();
        private readonly Dictionary<HouseSuccessionLaw, RealmLawDefinition> _succession = new Dictionary<HouseSuccessionLaw, RealmLawDefinition>();
        public IEnumerable<string> Groups => _groups.Keys;
        public RealmLawDefinition Find(string id) => id != null && _laws.TryGetValue(id, out var law) ? law : null;
        public IReadOnlyList<RealmLawDefinition> InGroup(string id) => _groups.TryGetValue(id, out var laws)
            ? laws.AsReadOnly() : (IReadOnlyList<RealmLawDefinition>)Array.Empty<RealmLawDefinition>();
        public string DefaultFor(string group) => _defaults[group];
        public RealmLawDefinition ForGender(GenderSuccessionLaw law) => _gender[law];
        public RealmLawDefinition ForSuccession(HouseSuccessionLaw law) => _succession[law];
        public RealmLawDefinition ForTerm(int years) => InGroup(TermGroup).Single(l => l.MandateYears == years);

        internal static RealmLawRegistry Read(Stream stream)
        {
            var document = new XmlDocument { XmlResolver = null };
            using (var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null }))
                document.Load(reader);
            if (document.DocumentElement?.Name != "BellumLaws") throw new InvalidDataException("Expected BellumLaws root.");
            var result = new RealmLawRegistry();
            foreach (XmlElement group in document.SelectNodes("/BellumLaws/Group"))
            {
                string groupId = Required(group, "id"), effect = Required(group, "effect");
                if (effect != "gender" && effect != "succession" && effect != TermGroup) throw new InvalidDataException("No law effect handler: " + effect);
                if (groupId != effect) throw new InvalidDataException("Initial law groups must match their effect handler.");
                if (result._groups.ContainsKey(groupId)) throw new InvalidDataException("Duplicate law group: " + groupId);
                var definitions = new List<RealmLawDefinition>();
                result._groups.Add(groupId, definitions);
                foreach (XmlElement node in group.SelectNodes("Law"))
                {
                    var law = new RealmLawDefinition { Id = Required(node, "id"), GroupId = groupId,
                        Effect = effect, Value = Required(node, "value"), Category = Required(node, "category"),
                        NameText = Required(node, "name"), DescriptionText = Required(node, "description") };
                    Type type = effect == "gender" ? typeof(GenderSuccessionLaw) : typeof(HouseSuccessionLaw);
                    if (effect == TermGroup)
                    {
                        if (!int.TryParse(law.Value, out int years) || !new[] { 0, 1, 5, 10 }.Contains(years))
                            throw new InvalidDataException("Unknown mandate length: " + law.Value);
                        law.MandateYears = years;
                    }
                    else
                    {
                        if (!Enum.GetNames(type).Contains(law.Value)) throw new InvalidDataException("Unknown law effect value: " + law.Value);
                        if (effect == "gender") law.GenderValue = (GenderSuccessionLaw)Enum.Parse(type, law.Value);
                        else law.SuccessionValue = (HouseSuccessionLaw)Enum.Parse(type, law.Value);
                    }
                    if (result._laws.ContainsKey(law.Id) || definitions.Any(d => d.Value == law.Value))
                        throw new InvalidDataException("Duplicate law identity/effect: " + law.Id);
                    if (effect == TermGroup && law.Category != TermGroup || effect == "gender" && law.Category != "gender" || effect == "succession"
                        && law.Category != SuccessionRealmRules.Classify((HouseSuccessionLaw)Enum.Parse(type, law.Value)).ToString().ToLowerInvariant())
                        throw new InvalidDataException("Law category does not match implemented effect: " + law.Id);
                    foreach (XmlElement stance in node.SelectNodes("Stance"))
                    {
                        if (!Enum.TryParse(Required(stance, "faction"), out FactionType faction)
                            || !CourtFactionRoster.Types.Contains(faction)
                            || !Enum.TryParse(Required(stance, "value"), out CourtPolicyStance value)
                            || !Enum.IsDefined(typeof(CourtPolicyStance), value) || law.Stances.ContainsKey(faction))
                            throw new InvalidDataException("Invalid/duplicate faction stance: " + law.Id);
                        law.Stances.Add(faction, value);
                    }
                    result._laws.Add(law.Id, law);
                    if (law.GenderValue.HasValue) result._gender.Add(law.GenderValue.Value, law);
                    else if (law.SuccessionValue.HasValue) result._succession.Add(law.SuccessionValue.Value, law);
                    var crowns = node.SelectNodes("Crown");
                    if (crowns.Count > 1) throw new InvalidDataException("Duplicate Crown stance: " + law.Id);
                    if (crowns.Count == 1)
                    {
                        if (!Enum.TryParse(Required((XmlElement)crowns[0], "stance"), out CourtPolicyStance stance)
                            || !Enum.IsDefined(typeof(CourtPolicyStance), stance)) throw new InvalidDataException("Invalid Crown stance: " + law.Id);
                        law.CrownStance = stance;
                    }
                    definitions.Add(law);
                }
                string defaultId = Required(group, "default");
                if (!definitions.Any(d => d.Id == defaultId)) throw new InvalidDataException("Missing group default: " + groupId);
                result._defaults.Add(groupId, defaultId);
            }
            foreach (GenderSuccessionLaw value in Enum.GetValues(typeof(GenderSuccessionLaw))) result.ForGender(value);
            foreach (HouseSuccessionLaw value in Enum.GetValues(typeof(HouseSuccessionLaw))) result.ForSuccession(value);
            foreach (int years in new[] { 0, 1, 5, 10 }) result.ForTerm(years);
            return result;
        }

        private static string Required(XmlElement node, string attribute)
        {
            string value = node.GetAttribute(attribute);
            if (string.IsNullOrWhiteSpace(value)) throw new InvalidDataException("Missing law attribute: " + attribute);
            return value;
        }

        private static RealmLawRegistry Load()
        {
            var assembly = Assembly.GetExecutingAssembly();
            var directory = new DirectoryInfo(Path.GetDirectoryName(assembly.Location));
            while (directory != null)
            {
                string path = Path.Combine(directory.FullName, "ModuleData", "bellum_laws.xml");
                if (File.Exists(path)) { using (var stream = File.OpenRead(path)) return Read(stream); }
                directory = directory.Parent;
            }
            using (var stream = assembly.GetManifestResourceStream("BellumCivile.ModuleData.bellum_laws.xml"))
            {
                if (stream == null) throw new InvalidDataException("Missing bellum_laws.xml definitions.");
                return Read(stream);
            }
        }
    }
}
