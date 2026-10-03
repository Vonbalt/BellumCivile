using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Xml;
using BellumCivile;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Encyclopedia;
using TaleWorlds.CampaignSystem.Encyclopedia.Pages;
using TaleWorlds.Core;
using TaleWorlds.Localization;

internal static class EncyclopediaConceptTests
{
    internal static void Run(Action<bool, string> check)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "BellumCivile.csproj"))) directory = directory.Parent;
        if (directory == null) throw new Exception("Repository not found for concept tests.");
        var xml = new XmlDocument();
        xml.Load(Path.Combine(directory.FullName, "ModuleData", "bellum_concepts.xml"));
        var concepts = new List<Concept>();
        foreach (XmlNode node in xml.DocumentElement.ChildNodes)
        {
            if (node.Name != "Concept") continue;
            var concept = new Concept();
            concept.Deserialize(null, node);
            concepts.Add(concept);
            check(concept.Title.GetID().StartsWith("BC_Concept_") && concept.Description.GetID().StartsWith("BC_Concept_"),
                "Native Concept deserializer accepts localized Bellum article: " + concept.StringId);
            MBTextManager.SetTextVariable(concept.LinkID,
                HyperlinkTexts.GetConceptHyperlinkText("Concept-" + concept.StringId, concept.Title));
        }
        MBTextManager.SetTextVariable("newline", "\n");
        check(concepts.Count == 31, "Native deserialization loads all 31 Bellum articles");
        foreach (var concept in concepts)
        {
            string text = concept.Description.ToString();
            check(!text.Contains("{BC_CONCEPT_") && !text.Contains("{newline}") && text.Contains("\n"),
                "Native article text resolves paragraphs and concept links: " + concept.StringId);
            check(text.Contains("bc_concept_bellum_civile") || concept.StringId == "bc_concept_bellum_civile",
                "Native text retains clickable overview return target: " + concept.StringId);
        }

        var patchType = typeof(RelationMemoryService).Assembly.GetType("BellumCivile.Patches.EncyclopediaConceptFilterPatch", true);
        var postfix = AccessTools.Method(patchType, "Postfix");
        var page = (DefaultEncyclopediaConceptPage)FormatterServices.GetUninitializedObject(typeof(DefaultEncyclopediaConceptPage));
        var target = AccessTools.Method(typeof(DefaultEncyclopediaConceptPage), "InitializeFilterItems");
        var groups = ((IEnumerable<EncyclopediaFilterGroup>)target.Invoke(page, null)).ToList();
        var types = groups.Single(g => g.Name.GetID() == "tBx7XXps");
        var foreignFilter = new EncyclopediaFilterItem(new TextObject("{=Test_ConceptMod}Other mod"), _ => false) { IsActive = true };
        types.Filters.Add(foreignFilter);
        var original = types.Filters.ToArray();
        var extraGroup = new EncyclopediaFilterGroup(new List<EncyclopediaFilterItem>(), new TextObject("{=Test_ConceptGroup}Extra"));
        groups.Insert(0, extraGroup);
        object[] args = { groups };
        postfix.Invoke(null, args);
        postfix.Invoke(null, args);
        var patched = ((IEnumerable<EncyclopediaFilterGroup>)args[0]).ToList();
        check(patched.Count == groups.Count && patched[0] == extraGroup, "Concept filter preserves other mods' groups and their order");
        check(original.All(types.Filters.Contains) && foreignFilter.IsActive, "Concept filter preserves native/other-mod filters and selections");
        check(types.Filters.Count == original.Length + 1, "Repeated filter integration adds Bellum exactly once");
        var bellum = types.Filters.Single(f => f.Name.GetID() == "BC_Concept_Filter");
        check(concepts.All(c => bellum.Predicate(c)) && !bellum.Predicate(new Concept { FilterGroup = "Kingdoms" })
            && !bellum.Predicate(null) && !bellum.Predicate(new object()), "Bellum filter selects only Bellum concepts");
        foreignFilter.IsActive = false;
        bellum.IsActive = true;
        check(types.Predicate(concepts[0]) && !types.Predicate(new Concept { FilterGroup = "Kingdoms" }), "Active Bellum category excludes native concepts");
        var kingdoms = new Concept { FilterGroup = "Kingdoms" };
        types.Filters.First(f => f != bellum && f.Predicate(kingdoms)).IsActive = true;
        check(types.Predicate(concepts[0]) && types.Predicate(kingdoms), "Native and Bellum selections retain Types OR semantics");
        var missingGroups = new[] { extraGroup };
        object[] missing = { missingGroups };
        postfix.Invoke(null, missing);
        check(ReferenceEquals(missing[0], missingGroups) && extraGroup.Filters.Count == 0,
            "Missing native Types group is left alone");
        object[] empty = { null };
        postfix.Invoke(null, empty);
        check(empty[0] == null, "Missing concept filter result is handled safely");

        var harmony = new Harmony("bellum.test.concept_filter");
        try
        {
            harmony.CreateClassProcessor(patchType).Patch();
            var live = ((IEnumerable<EncyclopediaFilterGroup>)target.Invoke(page, null)).ToList();
            check(live.Single(g => g.Name.GetID() == "tBx7XXps").Filters.Count(f => f.Name.GetID() == "BC_Concept_Filter") == 1,
                "Harmony registration reaches the installed native Concepts filter initializer");
        }
        finally { harmony.UnpatchAll(harmony.Id); }
    }
}
