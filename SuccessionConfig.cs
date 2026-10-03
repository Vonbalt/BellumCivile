using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Xml;

namespace BellumCivile
{
    public sealed class SuccessionConfig
    {
        private static SuccessionConfig _instance;
        private readonly Dictionary<string, int> _cultureTerms = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> _kingdomTerms = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        public int GetDefaultMandateYears(string cultureId, string kingdomId)
        {
            if (kingdomId != null && _kingdomTerms.TryGetValue(kingdomId, out int years)) return years;
            return cultureId != null && _cultureTerms.TryGetValue(cultureId, out years) ? years : 0;
        }

        private static void ReadTerm(XmlNode node, IDictionary<string, int> terms)
        {
            string id = node.Attributes?["id"]?.Value;
            string value = node.Attributes?["electiveTerm"]?.Value;
            if (string.IsNullOrWhiteSpace(id) || value == null) return;
            if (string.Equals(value, "lifetime", StringComparison.OrdinalIgnoreCase)) terms[id] = 0;
            else if (int.TryParse(value, out int years) && (years == 1 || years == 5 || years == 10)) terms[id] = years;
            else throw new InvalidDataException("Invalid electiveTerm for " + id);
        }

        private readonly Dictionary<string, SuccessionLawSet> _cultureMap =
            new Dictionary<string, SuccessionLawSet>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, SuccessionLawSet> _kingdomMap =
            new Dictionary<string, SuccessionLawSet>(StringComparer.OrdinalIgnoreCase);

        public enum SuccessionRuleScope
        {
            Default,
            Culture,
            Kingdom
        }

        public static SuccessionConfig Instance => _instance ?? (_instance = Load());

        public static SuccessionLawSet GenericFallback =>
            new SuccessionLawSet(GenderSuccessionLaw.MalePreference, HouseSuccessionLaw.Primogeniture);

        public SuccessionLawSet GetDefaultLaws(string cultureId, string kingdomId)
        {
            return GetDefaultLaws(cultureId, kingdomId, out _);
        }

        public SuccessionLawSet GetDefaultLaws(
            string cultureId,
            string kingdomId,
            out SuccessionRuleScope scope)
        {
            if (!string.IsNullOrWhiteSpace(kingdomId)
                && _kingdomMap.TryGetValue(kingdomId, out SuccessionLawSet kingdomLaws))
            {
                scope = SuccessionRuleScope.Kingdom;
                return kingdomLaws;
            }

            if (!string.IsNullOrWhiteSpace(cultureId)
                && _cultureMap.TryGetValue(cultureId, out SuccessionLawSet cultureLaws))
            {
                scope = SuccessionRuleScope.Culture;
                return cultureLaws;
            }

            scope = SuccessionRuleScope.Default;
            return GenericFallback;
        }

        // Compatibility helpers for older integrations. New code should use the law pair.
        public int GetSuccessionType(string cultureId, string kingdomId)
        {
            return ToLegacyType(GetDefaultLaws(cultureId, kingdomId));
        }

        public int GetSuccessionTypeWithScope(
            string cultureId,
            string kingdomId,
            out SuccessionRuleScope scope)
        {
            return ToLegacyType(GetDefaultLaws(cultureId, kingdomId, out scope));
        }

        public static int ToLegacyType(SuccessionLawSet laws)
        {
            switch (laws.SuccessionLaw)
            {
                case HouseSuccessionLaw.Seniority: return 2;
                case HouseSuccessionLaw.Ultimogeniture: return 3;
                case HouseSuccessionLaw.ShuraCouncil: return 4;
                case HouseSuccessionLaw.Tanistry: return 5;
                case HouseSuccessionLaw.Kinship: return 6;
                case HouseSuccessionLaw.MilitaryAcclamation: return 8;
                case HouseSuccessionLaw.ElectiveSeniority: return 9;
                default: return laws.GenderLaw == GenderSuccessionLaw.Equal ? 7 : 1;
            }
        }

        public static SuccessionLawSet FromLegacyType(int type)
        {
            switch (type)
            {
                case 2: return new SuccessionLawSet(GenderSuccessionLaw.MalePreference, HouseSuccessionLaw.Seniority);
                case 3: return new SuccessionLawSet(GenderSuccessionLaw.MalePreference, HouseSuccessionLaw.Ultimogeniture);
                case 4: return new SuccessionLawSet(GenderSuccessionLaw.Equal, HouseSuccessionLaw.ShuraCouncil);
                case 5: return new SuccessionLawSet(GenderSuccessionLaw.Equal, HouseSuccessionLaw.Tanistry);
                case 6: return new SuccessionLawSet(GenderSuccessionLaw.MalePreference, HouseSuccessionLaw.Kinship);
                case 7: return new SuccessionLawSet(GenderSuccessionLaw.Equal, HouseSuccessionLaw.Primogeniture);
                case 8: return new SuccessionLawSet(GenderSuccessionLaw.Equal, HouseSuccessionLaw.MilitaryAcclamation);
                case 9: return new SuccessionLawSet(GenderSuccessionLaw.MalePreference, HouseSuccessionLaw.ElectiveSeniority);
                default: return GenericFallback;
            }
        }

        private static SuccessionConfig Load()
        {
            SuccessionConfig config = new SuccessionConfig();
            try
            {
                string path = FindConfigPath();
                if (path == null || !File.Exists(path))
                {
                    BellumCivileLogger.Log("succession_config.xml not found - using built-in defaults.");
                    return config;
                }

                XmlDocument document = new XmlDocument();
                document.Load(path);

                foreach (XmlNode node in document.SelectNodes("/SuccessionConfig/Culture") ?? new EmptyNodeList())
                {
                    TryReadRule(node, config._cultureMap);
                    ReadTerm(node, config._cultureTerms);
                }

                foreach (XmlNode node in document.SelectNodes("/SuccessionConfig/Kingdom") ?? new EmptyNodeList())
                {
                    TryReadRule(node, config._kingdomMap);
                    ReadTerm(node, config._kingdomTerms);
                }

                BellumCivileLogger.Log(
                    $"Loaded succession_config.xml ({config._cultureMap.Count} cultures, {config._kingdomMap.Count} kingdoms).");
            }
            catch (Exception ex)
            {
                BellumCivileLogger.Log($"Failed to load succession_config.xml: {ex.Message}");
            }

            return config;
        }

        private static void TryReadRule(XmlNode node, IDictionary<string, SuccessionLawSet> target)
        {
            string id = node?.Attributes?["id"]?.Value?.Trim();
            if (string.IsNullOrWhiteSpace(id))
                return;

            string genderValue = node.Attributes?["genderLaw"]?.Value;
            string successionValue = node.Attributes?["successionLaw"]?.Value;
            if (TryParseGenderLaw(genderValue, out GenderSuccessionLaw genderLaw)
                && TryParseSuccessionLaw(successionValue, out HouseSuccessionLaw successionLaw))
            {
                target[id] = new SuccessionLawSet(genderLaw, successionLaw);
                return;
            }

            if (int.TryParse(node.Attributes?["type"]?.Value, out int legacyType))
                target[id] = FromLegacyType(legacyType);
        }

        private static bool TryParseGenderLaw(string value, out GenderSuccessionLaw law)
        {
            return Enum.TryParse(NormalizeEnumValue(value), true, out law);
        }

        private static bool TryParseSuccessionLaw(string value, out HouseSuccessionLaw law)
        {
            return Enum.TryParse(NormalizeEnumValue(value), true, out law);
        }

        private static string NormalizeEnumValue(string value)
        {
            return (value ?? string.Empty)
                .Replace(" ", string.Empty)
                .Replace("-", string.Empty)
                .Replace("_", string.Empty);
        }

        private static string FindConfigPath()
        {
            string dll = System.Reflection.Assembly.GetExecutingAssembly().Location;
            string moduleRoot = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(dll)));
            return moduleRoot == null
                ? null
                : Path.Combine(moduleRoot, "ModuleData", "succession_config.xml");
        }

        private sealed class EmptyNodeList : XmlNodeList
        {
            public override int Count => 0;
            public override XmlNode Item(int index) => null;
            public override IEnumerator GetEnumerator() => Array.Empty<XmlNode>().GetEnumerator();
        }
    }
}
