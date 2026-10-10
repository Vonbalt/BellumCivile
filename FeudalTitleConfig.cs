using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml;
using TaleWorlds.ModuleManager;

namespace BellumCivile
{
    internal class FeudalTitleConfig
    {
        public sealed class StartingClientage
        {
            public string ClientId;
            public string SuzerainId;
            public bool Voluntary;
            public int LiberationCooldownDays;
            public string Source;
        }

        public sealed class ConfiguredTitle
        {
            public string Id;
            public FeudalTitleType Type;
            public string Name;
            public string SettlementRef;
            public string CapitalRef;
            public string ParentRef;
            public string DeJureClanRef;
            public string DeFactoClanRef;
            public string KingdomRef;
            public string CultureRef;
            public List<string> ChildRefs = new List<string>();
        }

        public sealed class TitleStyle
        {
            public string CultureRef;
            public string KingdomRef;
            public string RulerCultureRef;
            public string PrivyCouncilName;
            public readonly Dictionary<FactionType, string> CourtFactionNames =
                new Dictionary<FactionType, string>();
            public readonly Dictionary<PrivyCouncilOffice, string> CouncilOfficeNames =
                new Dictionary<PrivyCouncilOffice, string>();
            public readonly List<RankStyle> Ranks = new List<RankStyle>();

            public bool HasValues => Ranks.Count > 0
                || !string.IsNullOrWhiteSpace(PrivyCouncilName)
                || CourtFactionNames.Count > 0
                || CouncilOfficeNames.Count > 0;
        }

        public sealed class RankStyle
        {
            public FeudalTitleType Tier;
            public string TitleName;
            public string TitleFormat;
            public string HeldTitleFormat;
            public string HeroNameFormat;
            public string MaleRank;
            public string FemaleRank;
            public string SpouseRank;
            public string FemaleSpouseRank;
            public string HeirRank;
            public string FemaleHeirRank;
            public string ChildRank;
            public string FemaleChildRank;
            public string NobleRank;
            public string FemaleNobleRank;
            public string LandlessLeaderRank;
            public string FemaleLandlessLeaderRank;
            public string MercenaryLeaderRank;
            public string FemaleMercenaryLeaderRank;
            public string WandererRank;
            public string FemaleWandererRank;
        }

        private const string MainFileName = "bellum_feudal_titles.xml";
        private const string PatchFileName = "bellum_feudal_titles_patch.xml";

        private static FeudalTitleConfig _instance;
        public static FeudalTitleConfig Instance => _instance ?? (_instance = Load());

        private readonly List<ConfiguredTitle> _titles = new List<ConfiguredTitle>();
        private readonly List<TitleStyle> _styles = new List<TitleStyle>();
        private readonly List<StartingClientage> _startingClientages = new List<StartingClientage>();
        private readonly Dictionary<string, string> _titleNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyList<ConfiguredTitle> Titles => _titles;
        public IReadOnlyList<TitleStyle> Styles => _styles;
        public IReadOnlyList<StartingClientage> StartingClientages => _startingClientages;
        public IReadOnlyDictionary<string, string> TitleNames => _titleNames;

        private static FeudalTitleConfig Load()
        {
            FeudalTitleConfig config = new FeudalTitleConfig();
            try
            {
                string moduleRoot = GetModuleRoot();
                if (string.IsNullOrEmpty(moduleRoot))
                    return config;

                string mainPath = Path.Combine(moduleRoot, "ModuleData", MainFileName);
                if (File.Exists(mainPath))
                    config.LoadFile(mainPath, replaceExisting: true);
                else
                    BellumCivileLogger.Log("bellum_feudal_titles.xml not found - using dynamic feudal title setup.");

                int selectedPresetIndex = BellumCivileSettings.Instance?.TitleStylePreset?.SelectedIndex
                    ?? FeudalTitleStylePresetCatalog.GetDefaultPresetIndex();
                FeudalTitleStylePresetCatalog.Preset selectedPreset = FeudalTitleStylePresetCatalog.GetSelectedPreset(selectedPresetIndex);
                if (selectedPreset != null && File.Exists(selectedPreset.Path))
                {
                    config.LoadFile(selectedPreset.Path, replaceExisting: false, stylesOnly: true);
                    BellumCivileLogger.Log($"Loaded feudal title style preset '{selectedPreset.Id}' from {selectedPreset.Path}.");
                }

                foreach (string patchPath in FindPatchFiles())
                    config.LoadFile(patchPath, replaceExisting: false);

                BellumCivileLogger.Log($"Loaded feudal title config ({config._titles.Count} configured titles, {config._styles.Count} title styles, {config._startingClientages.Count} starting clientages).");
            }
            catch (Exception ex)
            {
                BellumCivileLogger.Log($"Failed to load feudal title config: {ex.Message}");
            }

            return config;
        }

        private void LoadFile(string path, bool replaceExisting, bool stylesOnly = false)
        {
            XmlDocument doc = new XmlDocument();
            doc.Load(path);

            if (!stylesOnly)
            {
                foreach (XmlNode node in doc.SelectNodes("/BellumFeudalTitles/StartingClientage") ?? (XmlNodeList)new EmptyNodeList())
                    ReadStartingClientage(node, path);

                foreach (XmlNode node in doc.SelectNodes("/BellumFeudalTitles/Title") ?? (XmlNodeList)new EmptyNodeList())
                {
                    ConfiguredTitle title = ReadTitle(node);
                    if (title == null)
                        continue;

                    if (replaceExisting)
                        _titles.RemoveAll(t => string.Equals(t.Id, title.Id, StringComparison.OrdinalIgnoreCase));

                    int existingIndex = _titles.FindIndex(t => string.Equals(t.Id, title.Id, StringComparison.OrdinalIgnoreCase));
                    if (existingIndex >= 0)
                        _titles[existingIndex] = title;
                    else
                        _titles.Add(title);

                    if (!string.IsNullOrWhiteSpace(title.Name))
                        _titleNames[title.Id] = title.Name;
                }
            }

            foreach (XmlNode node in doc.SelectNodes("/BellumFeudalTitles/TitleName") ?? (XmlNodeList)new EmptyNodeList())
            {
                string id = FirstAttr(node, "id", "title", "titleId");
                string name = FirstAttr(node, "name", "value", "text");
                if (!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(name))
                    _titleNames[id.Trim()] = name.Trim();
            }

            foreach (XmlNode node in doc.SelectNodes("/BellumFeudalTitles/TitleStyle") ?? (XmlNodeList)new EmptyNodeList())
            {
                TitleStyle style = ReadTitleStyle(node);
                if (style == null || !style.HasValues)
                    continue;

                if (replaceExisting)
                {
                    _styles.RemoveAll(existing =>
                        string.Equals(existing.CultureRef, style.CultureRef, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(existing.KingdomRef, style.KingdomRef, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(existing.RulerCultureRef, style.RulerCultureRef, StringComparison.OrdinalIgnoreCase));
                }

                int existingIndex = _styles.FindIndex(existing =>
                    string.Equals(existing.CultureRef, style.CultureRef, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(existing.KingdomRef, style.KingdomRef, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(existing.RulerCultureRef, style.RulerCultureRef, StringComparison.OrdinalIgnoreCase));
                if (existingIndex >= 0)
                    MergeStyle(_styles[existingIndex], style);
                else
                    _styles.Add(style);
            }
        }

        private void ReadStartingClientage(XmlNode node, string path)
        {
            string client = Attr(node, "client");
            if (string.IsNullOrWhiteSpace(client))
            {
                BellumCivileLogger.Log($"Invalid StartingClientage in {path}: missing client kingdom ID.");
                return;
            }

            // A malformed override must not silently revive the earlier relationship.
            _startingClientages.RemoveAll(entry => entry.ClientId == client);
            string remove = Attr(node, "remove");
            if (!string.IsNullOrEmpty(remove))
            {
                if (!bool.TryParse(remove, out bool removed))
                {
                    BellumCivileLogger.Log($"Invalid StartingClientage for '{client}' in {path}: remove must be true or false.");
                    return;
                }
                if (removed) return;
            }

            string suzerain = Attr(node, "suzerain");
            string submission = Attr(node, "submission");
            bool voluntary = string.Equals(submission, "Voluntary", StringComparison.OrdinalIgnoreCase);
            string cooldownText = Attr(node, "liberationCooldownDays");
            int cooldown = 0;
            if (string.IsNullOrWhiteSpace(suzerain)
                || (!voluntary && !string.Equals(submission, "Forced", StringComparison.OrdinalIgnoreCase))
                || (!string.IsNullOrEmpty(cooldownText)
                    && (!int.TryParse(cooldownText, NumberStyles.None, CultureInfo.InvariantCulture, out cooldown) || cooldown < 0)))
            {
                BellumCivileLogger.Log($"Invalid StartingClientage for '{client}' in {path}: supply suzerain ID, submission=Voluntary or Forced, and an optional nonnegative whole liberationCooldownDays.");
                return;
            }

            _startingClientages.Add(new StartingClientage
            {
                ClientId = client, SuzerainId = suzerain, Voluntary = voluntary,
                LiberationCooldownDays = cooldown, Source = path
            });
        }

        private static void MergeStyle(TitleStyle target, TitleStyle source)
        {
            MergeValue(ref target.PrivyCouncilName, source.PrivyCouncilName);

            foreach (KeyValuePair<FactionType, string> entry in source.CourtFactionNames)
                target.CourtFactionNames[entry.Key] = entry.Value;

            foreach (KeyValuePair<PrivyCouncilOffice, string> entry in source.CouncilOfficeNames)
                target.CouncilOfficeNames[entry.Key] = entry.Value;

            foreach (RankStyle sourceRank in source.Ranks)
            {
                RankStyle targetRank = target.Ranks.FirstOrDefault(rank => rank.Tier == sourceRank.Tier);
                if (targetRank == null)
                {
                    target.Ranks.Add(sourceRank);
                    continue;
                }

                MergeValue(ref targetRank.TitleName, sourceRank.TitleName);
                MergeValue(ref targetRank.TitleFormat, sourceRank.TitleFormat);
                MergeValue(ref targetRank.HeldTitleFormat, sourceRank.HeldTitleFormat);
                MergeValue(ref targetRank.HeroNameFormat, sourceRank.HeroNameFormat);
                MergeValue(ref targetRank.MaleRank, sourceRank.MaleRank);
                MergeValue(ref targetRank.FemaleRank, sourceRank.FemaleRank);
                MergeValue(ref targetRank.SpouseRank, sourceRank.SpouseRank);
                MergeValue(ref targetRank.FemaleSpouseRank, sourceRank.FemaleSpouseRank);
                MergeValue(ref targetRank.HeirRank, sourceRank.HeirRank);
                MergeValue(ref targetRank.FemaleHeirRank, sourceRank.FemaleHeirRank);
                MergeValue(ref targetRank.ChildRank, sourceRank.ChildRank);
                MergeValue(ref targetRank.FemaleChildRank, sourceRank.FemaleChildRank);
                MergeValue(ref targetRank.NobleRank, sourceRank.NobleRank);
                MergeValue(ref targetRank.FemaleNobleRank, sourceRank.FemaleNobleRank);
                MergeValue(ref targetRank.LandlessLeaderRank, sourceRank.LandlessLeaderRank);
                MergeValue(ref targetRank.FemaleLandlessLeaderRank, sourceRank.FemaleLandlessLeaderRank);
                MergeValue(ref targetRank.MercenaryLeaderRank, sourceRank.MercenaryLeaderRank);
                MergeValue(ref targetRank.FemaleMercenaryLeaderRank, sourceRank.FemaleMercenaryLeaderRank);
                MergeValue(ref targetRank.WandererRank, sourceRank.WandererRank);
                MergeValue(ref targetRank.FemaleWandererRank, sourceRank.FemaleWandererRank);
            }
        }

        private static void MergeValue(ref string target, string source)
        {
            if (!string.IsNullOrWhiteSpace(source))
                target = source;
        }

        private static ConfiguredTitle ReadTitle(XmlNode node)
        {
            string id = Attr(node, "id");
            string typeText = Attr(node, "type");
            if (string.IsNullOrWhiteSpace(id) || !Enum.TryParse(typeText, true, out FeudalTitleType type))
                return null;

            ConfiguredTitle title = new ConfiguredTitle
            {
                Id = id.Trim(),
                Type = type,
                Name = Attr(node, "name"),
                SettlementRef = FirstAttr(node, "settlement", "settlementId", "settlementName"),
                CapitalRef = FirstAttr(node, "capital", "capitalSettlement", "capitalSettlementId"),
                ParentRef = FirstAttr(node, "parent", "parentTitle", "parentId"),
                DeJureClanRef = FirstAttr(node, "deJureClan", "deJure", "deJureHolder"),
                DeFactoClanRef = FirstAttr(node, "deFactoClan", "deFacto", "deFactoHolder"),
                KingdomRef = FirstAttr(node, "kingdom", "associatedKingdom", "kingdomId"),
                CultureRef = FirstAttr(node, "culture", "cultureId", "cultureName")
            };

            foreach (XmlNode child in node.ChildNodes)
            {
                if (child.NodeType != XmlNodeType.Element || !string.Equals(child.Name, "Child", StringComparison.OrdinalIgnoreCase))
                    continue;

                string childRef = FirstAttr(child, "id", "title", "settlement", "name");
                if (!string.IsNullOrWhiteSpace(childRef))
                    title.ChildRefs.Add(childRef.Trim());
            }

            return title;
        }

        private static TitleStyle ReadTitleStyle(XmlNode node)
        {
            TitleStyle style = new TitleStyle
            {
                CultureRef = FirstAttr(node, "culture", "cultureId", "cultureName"),
                KingdomRef = FirstAttr(node, "kingdom", "kingdomId", "kingdomName"),
                RulerCultureRef = FirstAttr(
                    node,
                    "rulerCulture",
                    "rulerculture",
                    "rulerCultureId",
                    "rulerCultureName",
                    "culture_ruler",
                    "ruler_culture")
            };

            if (string.IsNullOrWhiteSpace(style.CultureRef)
                && string.IsNullOrWhiteSpace(style.KingdomRef)
                && string.IsNullOrWhiteSpace(style.RulerCultureRef))
                return null;

            foreach (XmlNode child in node.ChildNodes)
            {
                if (child.NodeType != XmlNodeType.Element)
                    continue;

                if (string.Equals(child.Name, "CourtFactions", StringComparison.OrdinalIgnoreCase))
                {
                    AddConfiguredName(style.CourtFactionNames, FactionType.Glory, child, "glory");
                    AddConfiguredName(style.CourtFactionNames, FactionType.Nobility, child, "nobility");
                    AddConfiguredName(style.CourtFactionNames, FactionType.Liberty, child, "liberty");
                    continue;
                }

                if (string.Equals(child.Name, "CouncilOffices", StringComparison.OrdinalIgnoreCase))
                {
                    style.PrivyCouncilName = FirstAttr(
                        child,
                        "name",
                        "privyCouncil",
                        "privyCouncilName",
                        "councilName");
                    AddConfiguredName(style.CouncilOfficeNames, PrivyCouncilOffice.Marshal, child, "marshal");
                    AddConfiguredName(style.CouncilOfficeNames, PrivyCouncilOffice.Chancellor, child, "chancellor");
                    AddConfiguredName(style.CouncilOfficeNames, PrivyCouncilOffice.Seneschal, child, "seneschal");
                    AddConfiguredName(style.CouncilOfficeNames, PrivyCouncilOffice.Spymaster, child, "spymaster");
                    AddConfiguredName(style.CouncilOfficeNames, PrivyCouncilOffice.FirstAdvisor, child, "firstAdvisor", "first_advisor");
                    AddConfiguredName(style.CouncilOfficeNames, PrivyCouncilOffice.SecondAdvisor, child, "secondAdvisor", "second_advisor");
                    continue;
                }

                if (!string.Equals(child.Name, "Rank", StringComparison.OrdinalIgnoreCase))
                    continue;

                string tierText = FirstAttr(child, "tier", "type", "rank");
                if (!Enum.TryParse(tierText, true, out FeudalTitleType tier))
                    continue;

                style.Ranks.Add(new RankStyle
                {
                    Tier = tier,
                    TitleName = FirstAttr(child, "titleName", "landedTitle", "title"),
                    TitleFormat = FirstAttr(child, "titleFormat", "landedFormat", "format"),
                    HeldTitleFormat = FirstAttr(child, "heldTitleFormat", "heldFormat", "rankFormat"),
                    HeroNameFormat = FirstAttr(child, "heroNameFormat", "characterNameFormat", "honorificFormat"),
                    MaleRank = FirstAttr(child, "maleRank", "displayTitle", "rankTitle", "maleTitle"),
                    FemaleRank = FirstAttr(child, "femaleRank", "femaleDisplayTitle", "femaleTitle"),
                    SpouseRank = FirstAttr(child, "spouseRank", "spouseTitle", "maleSpouseTitle", "consortTitle"),
                    FemaleSpouseRank = FirstAttr(child, "femaleSpouseRank", "femaleSpouseTitle", "femaleConsortTitle"),
                    HeirRank = FirstAttr(child, "heirRank", "heirTitle", "crownTitle", "maleHeirTitle"),
                    FemaleHeirRank = FirstAttr(child, "femaleHeirRank", "femaleHeirTitle", "femaleCrownTitle"),
                    ChildRank = FirstAttr(child, "childRank", "childTitle", "princeTitle", "maleChildTitle"),
                    FemaleChildRank = FirstAttr(child, "femaleChildRank", "femaleChildTitle", "princessTitle"),
                    NobleRank = FirstAttr(child, "nobleRank", "nobleTitle", "lordTitle", "maleNobleTitle"),
                    FemaleNobleRank = FirstAttr(child, "femaleNobleRank", "femaleNobleTitle", "ladyTitle"),
                    LandlessLeaderRank = FirstAttr(child, "landlessLeaderRank"),
                    FemaleLandlessLeaderRank = FirstAttr(child, "femaleLandlessLeaderRank"),
                    MercenaryLeaderRank = FirstAttr(child, "mercenaryLeaderRank"),
                    FemaleMercenaryLeaderRank = FirstAttr(child, "femaleMercenaryLeaderRank"),
                    WandererRank = FirstAttr(child, "wandererRank", "companionRank", "retainerRank", "maleWandererTitle", "maleCompanionTitle", "maleRetainerTitle"),
                    FemaleWandererRank = FirstAttr(child, "femaleWandererRank", "femaleCompanionRank", "femaleRetainerRank", "femaleWandererTitle", "femaleCompanionTitle", "femaleRetainerTitle")
                });
            }

            return style;
        }

        private static void AddConfiguredName<T>(
            IDictionary<T, string> names,
            T key,
            XmlNode node,
            params string[] attributes)
        {
            string value = FirstAttr(node, attributes);
            if (!string.IsNullOrWhiteSpace(value))
                names[key] = value.Trim();
        }

        private static string FirstAttr(XmlNode node, params string[] names)
        {
            foreach (string name in names)
            {
                string value = Attr(node, name);
                if (!string.IsNullOrWhiteSpace(value))
                    return value;
            }

            return string.Empty;
        }

        private static string Attr(XmlNode node, string name)
        {
            return node?.Attributes?[name]?.Value?.Trim() ?? string.Empty;
        }

        private static IEnumerable<string> FindPatchFiles()
        {
            List<string> paths = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            // Include Bellum's own patch at its actual position in the active load order.
            foreach (ModuleInfo module in ModuleHelper.GetActiveModules())
            {
                if (string.IsNullOrWhiteSpace(module?.FolderPath))
                    continue;

                string patchPath = Path.GetFullPath(Path.Combine(module.FolderPath, "ModuleData", PatchFileName));
                if (File.Exists(patchPath) && seen.Add(patchPath))
                    paths.Add(patchPath);
            }

            return paths;
        }

        private static string GetModuleRoot()
        {
            string dllPath = Assembly.GetExecutingAssembly().Location;
            DirectoryInfo directory = new DirectoryInfo(Path.GetDirectoryName(dllPath));

            while (directory != null)
            {
                string moduleDataPath = Path.Combine(directory.FullName, "ModuleData");
                if (Directory.Exists(moduleDataPath))
                    return directory.FullName;

                directory = directory.Parent;
            }

            return Path.GetDirectoryName(dllPath);
        }

        private class EmptyNodeList : XmlNodeList
        {
            public override int Count => 0;
            public override XmlNode Item(int index) => null;
            public override System.Collections.IEnumerator GetEnumerator() => Array.Empty<XmlNode>().GetEnumerator();
        }
    }
}
