using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using BellumCivile;
using BellumCivile.Behaviors;
using BellumCivile.UI.VanillaTabs.Kingdoms.Hierarchy;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Localization;

internal static class TitleReorganizationTests
{
    private static readonly Dictionary<string, Clan> Houses = new Dictionary<string, Clan>();
    private static Hero _leader;
    private static readonly HashSet<string> ProtectedTitles = new HashSet<string>();
    private static string _starSeat;
    private static int _minimumChildren = 2;
    private static int _adjacencyChecks;
    private static bool Resolve(string clanId, ref Clan __result)
    { Houses.TryGetValue(clanId, out __result); return false; }
    private static bool Leader(ref Hero __result) { __result = _leader; return false; }
    private static bool Alive(ref bool __result) { __result = false; return false; }
    private static bool SkipNotification() => false;
    private static bool Day(ref float __result) { __result = 100; return false; }
    private static bool Minimum(ref int __result) { __result = _minimumChildren; return false; }
    private static bool Gold(ref int __result) { __result = 1_000_000; return false; }
    private static bool Adjacent(FeudalTitleRecord __0, FeudalTitleRecord __1, ref bool __result)
    {
        _adjacencyChecks++;
        __result = __0 != __1 && (_starSeat == null || __0.TitleId == _starSeat || __1.TitleId == _starSeat);
        return false;
    }
    private static bool Prosperity(ref float __result) { __result = 0; return false; }
    private static bool Capital(ref string __result) { __result = ""; return false; }
    private static bool Feuds(ref HashSet<string> __result) { __result = ProtectedTitles; return false; }
    private static bool Format(FeudalTitleRecord __0, ref string __result) { __result = __0?.Name ?? ""; return false; }

    internal static void Run(Action<bool, string> check)
    {
        var h = new Harmony("bellum.test.title_reorganization");
        var type = typeof(FeudalTitleBehavior);
        var titles = new FeudalTitleBehavior();
        var registry = (Dictionary<string, FeudalTitleRecord>)AccessTools.Field(type, "_titlesById").GetValue(titles);
        var claims = (List<FeudalClaimRecord>)AccessTools.Field(type, "_claims").GetValue(titles);
        FeudalTitleRecord Add(string id, string parent)
        {
            var t = new FeudalTitleRecord(id, id, FeudalTitleType.County, "actor", "actor", parent, "", "origin", 0, 0);
            registry[id] = t; return t;
        }
        var root = Add("root", ""); var child = Add("child", "root"); var sibling = Add("sibling", "root");
        _leader = (Hero)FormatterServices.GetUninitializedObject(typeof(Hero));
        foreach (var id in new[] { "actor", "weak", "strong", "expired" })
        {
            var clan = (Clan)FormatterServices.GetUninitializedObject(typeof(Clan)); clan.StringId = id;
            Houses[id] = clan;
        }
        void Claim(string house, string title, FeudalClaimStrength strength, float expiry = -1)
            => claims.Add(new FeudalClaimRecord(house + title, house, title, strength, "test", "", "", 0, expiry));
        Claim("actor", "child", FeudalClaimStrength.Strong);
        Claim("weak", "child", FeudalClaimStrength.Weak);
        Claim("strong", "child", FeudalClaimStrength.Weak);
        Claim("strong", "root", FeudalClaimStrength.Strong);
        Claim("expired", "child", FeudalClaimStrength.Strong, 50);
        try
        {
            h.Patch(AccessTools.Method(type, "ResolveClan"), prefix: new HarmonyMethod(typeof(TitleReorganizationTests), nameof(Resolve)));
            h.Patch(AccessTools.PropertyGetter(type, "CurrentDay"), prefix: new HarmonyMethod(typeof(TitleReorganizationTests), nameof(Day)));
            h.Patch(AccessTools.PropertyGetter(typeof(Clan), "Leader"), prefix: new HarmonyMethod(typeof(TitleReorganizationTests), nameof(Leader)));
            h.Patch(AccessTools.PropertyGetter(typeof(Clan), "IsEliminated"), prefix: new HarmonyMethod(typeof(TitleReorganizationTests), nameof(Alive)));
            h.Patch(AccessTools.PropertyGetter(typeof(Hero), "IsDead"), prefix: new HarmonyMethod(typeof(TitleReorganizationTests), nameof(Alive)));
            AccessTools.Method(type, "RebuildRuntimeIndexes").Invoke(titles, new object[] { true, true });
            bool Overlap(FeudalTitleRecord a, FeudalTitleRecord b) => (bool)AccessTools.Method(type, "TitleTreesOverlap").Invoke(titles, new object[] { a, b });
            check(Overlap(root, child) && Overlap(child, root), "Feud estate protection covers ancestor and descendant changes");
            check(!Overlap(child, sibling), "Unrelated sibling feuds do not block reorganization");
            sibling.SetDeFactoParentTitle(child.TitleId);
            check(Overlap(child, sibling), "Feud protection also follows political hierarchy");
            var reactions = titles.GetReorganizationResentment(Houses["actor"], new[] { "child", "root", "child" });
            check(reactions.Count == 2, "Resentment excludes the actor and expired claims, deduplicating houses");
            check(reactions.Single(r => r.House == Houses["weak"]).Penalty == -15, "Weak claimant resentment is minus fifteen");
            check(reactions.Single(r => r.House == Houses["strong"]).Penalty == -30, "Strongest claim determines one house penalty");
            child.SetDeliberatelyDissolved(true);
            check(titles.GetActiveClaimsByTitle(child).Count > 0, "Dissolved titles retain historical claims");
            check(!titles.CanDissolveTitle(Houses["actor"], child, out _), "A dissolved historical title cannot be dissolved again");
            RunFormationCases(check, h, titles, registry);
        }
        finally
        {
            h.UnpatchAll(h.Id); Houses.Clear(); ProtectedTitles.Clear();
            _starSeat = null; _minimumChildren = 2;
        }
    }

    private static void RunFormationCases(Action<bool, string> check, Harmony harmony, FeudalTitleBehavior titles,
        Dictionary<string, FeudalTitleRecord> registry)
    {
        var type = typeof(FeudalTitleBehavior);
        var ui = typeof(HierarchyTitleNodeVM);
        void Patch(System.Reflection.MethodBase method, string name) =>
            harmony.Patch(method, prefix: new HarmonyMethod(typeof(TitleReorganizationTests), name));
        Patch(AccessTools.PropertyGetter(typeof(Hero), "Gold"), nameof(Gold));
        Patch(AccessTools.Method(type, "GetMinimumChildrenForFormation"), nameof(Minimum));
        Patch(AccessTools.Method(type, "AreTitlesAdjacent"), nameof(Adjacent));
        Patch(AccessTools.Method(type, "GetTitleProsperityScore"), nameof(Prosperity));
        Patch(AccessTools.Method(type, "ResolveCapitalSettlementId"), nameof(Capital));
        Patch(AccessTools.Method(type, "GetProtectedFeudTitles"), nameof(Feuds));
        Patch(AccessTools.Method(ui, "FormatTitleName", new[] { typeof(FeudalTitleRecord) }), nameof(Format));
        Patch(AccessTools.Method(typeof(NotificationHelper), nameof(NotificationHelper.ShowFeudalTitleFormed)), nameof(SkipNotification));
        void Rebuild() => AccessTools.Method(type, "RebuildRuntimeIndexes").Invoke(titles, new object[] { true, true });
        registry.Clear();
        var actor = Houses["actor"];
        FeudalTitleRecord Add(string id, string name, FeudalTitleType tier, string house, string parent = "")
        {
            var title = new FeudalTitleRecord(id, name, tier, house, house, parent, "", "origin", 0, 0);
            registry.Add(id, title);
            return title;
        }

        var county = Add("county", "County of Westmarch", FeudalTitleType.County, actor.StringId);
        var a = Add("a", "Galend", FeudalTitleType.Barony, actor.StringId, county.TitleId);
        var b = Add("b", "Talivel", FeudalTitleType.Barony, actor.StringId, county.TitleId);
        var c = Add("c", "Caleus", FeudalTitleType.Barony, "weak");
        c.SetDeFactoParentTitle(county.TitleId);
        Rebuild();
        var preview = titles.GetFormationAssessment(actor, a, true, false);
        check(!preview.CanForm && preview.EligibleTitles.SequenceEqual(new[] { a, b })
            && preview.StructuralObstacle?.ParentTitle == county && preview.StructuralObstacle.EmptiesDeJure
            && !preview.StructuralObstacle.EmptiesDeFacto, "Two qualifying baronies can still empty a named de jure parent");
        var node = (HierarchyTitleNodeVM)FormatterServices.GetUninitializedObject(ui);
        string hint = ((TextObject)AccessTools.Method(ui, "BuildFormationHint").Invoke(node, new object[] { preview })).ToString();
        check(hint.Contains("2/2") && hint.Contains("County of Westmarch") && hint.Contains("Galend, Talivel")
            && hint.Contains("de jure hierarchy"), "The actual tooltip names the qualifying lands, blocked county, and affected hierarchy");

        c.SetParentTitle(county.TitleId);
        c.SetDeFactoHolder(actor.StringId); c.SetDeJureHolder(actor.StringId);
        Rebuild();
        check(titles.GetFormationAssessment(actor, a, true, false).CanForm
            && titles.TryBuildFormationCandidate(actor, FeudalTitleType.County, new[] { "a", "b" }, "a", "New County", true, out _, out _),
            "Leaving a subordinate in both hierarchies allows a valid pair");
        object[] selection = { actor, FeudalTitleType.County, new[] { "a", "b", "c" }, "a", "New County", true, null, null, null };
        check(!(bool)AccessTools.Method(type, "TryBuildFormationCandidateWithDetails").Invoke(titles, selection)
            && selection[8] is FeudalTitleFormationObstacle block && block.ParentTitle == county
            && block.EmptiesDeJure && block.EmptiesDeFacto, "The exact combined selection identifies when both hierarchies would be emptied");

        foreach (var barony in new[] { a, b, c }) { barony.SetParentTitle(""); barony.SetDeFactoParentTitle(""); }
        Rebuild();
        check(titles.TryBuildFormationCandidate(actor, FeudalTitleType.County, new[] { "a", "b" }, "a", "New County", true,
            out FeudalTitleFormationCandidate pending, out _) && pending.Mode == FeudalTitleFormationMode.Consolidation,
            "A clean consolidation can be previewed before a feud starts");
        ProtectedTitles.Add(b.TitleId);
        object[] confirm = { actor, pending, false, FeudalSovereignElevationChoice.NotApplicable, null, null, null, null };
        check(!(bool)AccessTools.Method(type, "TryFormTitleWithDetails").Invoke(titles, confirm)
            && confirm[7] is FeudalTitleFormationObstacle stale && stale.Title == b
            && stale.Reason == FeudalTitleFormationBlockReason.SeatDisputed && registry.Count == 4,
            "A feud starting before confirmation blocks consolidation without mutating the registry");

        ProtectedTitles.Clear();
        registry.Clear();
        var duchy = Add("duchy", "Duchy of Westmarch", FeudalTitleType.Duchy, actor.StringId);
        var otherDuchy = Add("other-duchy", "Duchy of Eastmarch", FeudalTitleType.Duchy, actor.StringId);
        a = Add("a", "Galend", FeudalTitleType.Barony, actor.StringId, duchy.TitleId);
        b = Add("b", "Talivel", FeudalTitleType.Barony, actor.StringId, duchy.TitleId);
        Rebuild();
        check(titles.GetFormationAssessment(actor, a, true, false).CanForm
            && titles.TryBuildFormationCandidate(actor, FeudalTitleType.County, new[] { "a", "b" }, "a", "Galend", true,
                out pending, out _),
            "A newly formed county can replace the last two direct baronies under their duchy");

        b.SetDeFactoParentTitle(otherDuchy.TitleId);
        Rebuild();
        preview = titles.GetFormationAssessment(actor, a, true, false);
        check(!preview.CanForm && preview.StructuralObstacle?.ParentTitle == otherDuchy
            && preview.StructuralObstacle.EmptiesDeFacto && !preview.StructuralObstacle.EmptiesDeJure,
            "Inserting the new county under the legal duchy cannot empty a different actual duchy");
        confirm = new object[] { actor, pending, false, FeudalSovereignElevationChoice.NotApplicable, null, null, null, null };
        check(!(bool)AccessTools.Method(type, "TryFormTitleWithDetails").Invoke(titles, confirm)
            && confirm[7] is FeudalTitleFormationObstacle changed && changed.ParentTitle == otherDuchy
            && registry.Count == 4 && b.DeFactoParentTitleId == otherDuchy.TitleId,
            "Final confirmation rechecks the projected lieges without touching the registry if they changed");

        b.SetParentTitle(otherDuchy.TitleId);
        Rebuild();
        preview = titles.GetFormationAssessment(actor, a, true, false);
        check(!preview.CanForm && preview.StructuralObstacle?.ParentTitle == otherDuchy
            && preview.StructuralObstacle.EmptiesDeJure && preview.StructuralObstacle.EmptiesDeFacto,
            "A same-rank cross-duchy choice cannot leave the skipped-rank source empty in both trees");

        b.SetParentTitle(duchy.TitleId);
        b.SetDeFactoParentTitle(duchy.TitleId);
        Rebuild();
        check(titles.TryFormTitle(actor, pending, false, FeudalSovereignElevationChoice.NotApplicable,
                out var newCounty, out _, out _)
            && newCounty.ParentTitleId == duchy.TitleId && newCounty.DeFactoParentTitleId == duchy.TitleId
            && new[] { a, b }.All(barony => barony.ParentTitleId == newCounty.TitleId && barony.DeFactoParentTitleId == newCounty.TitleId),
            "Formation actually inserts the county beneath the retained duchy in both trees");

        registry.Clear();
        _minimumChildren = 3;
        _starSeat = "seat";
        var restrictedParent = Add("restricted", "Existing County", FeudalTitleType.County, actor.StringId);
        var seat = Add(_starSeat, "Central Barony", FeudalTitleType.Barony, actor.StringId);
        var blocked = Add("00-blocked", "Blocked Neighbor", FeudalTitleType.Barony, actor.StringId, restrictedParent.TitleId);
        for (int i = 1; i <= 11; i++)
            Add($"leaf-{i:D2}", $"Neighbor {i}", FeudalTitleType.Barony, actor.StringId);
        Rebuild();
        _adjacencyChecks = 0;
        preview = titles.GetFormationAssessment(actor, seat, true, false);
        check(preview.CanForm && preview.ContiguousEligibleTitles == 13 && preview.SearchLimited
            && preview.Candidates.All(option => option.ChildTitleIds.Contains(seat.TitleId)
                && !option.ChildTitleIds.Contains(blocked.TitleId))
            && preview.SelectableTitles.Count == 12,
            "A bounded seat search finds alternate three-barony combinations without the greedy path's blocked neighbor");
        check(_adjacencyChecks < 200, "Large-cluster candidate validation reuses adjacency rather than checking every candidate again");

        foreach (var leaf in registry.Values.Where(title => title.TitleId.StartsWith("leaf-")).ToArray())
        {
            var liege = Add("county-" + leaf.TitleId, "Existing County", FeudalTitleType.County, actor.StringId);
            leaf.SetParentTitle(liege.TitleId);
            leaf.SetDeFactoParentTitle(liege.TitleId);
        }
        Rebuild();
        preview = titles.GetFormationAssessment(actor, seat, true, false);
        hint = ((TextObject)AccessTools.Method(ui, "BuildFormationHint").Invoke(node, new object[] { preview })).ToString();
        check(!preview.CanForm && preview.SearchLimited && hint.Contains("Not all combinations could be checked from this seat"),
            "The blocked tooltip says when the search budget did not cover every combination");

        registry.Clear();
        duchy = Add("duchy", "Duchy of Westmarch", FeudalTitleType.Duchy, actor.StringId);
        otherDuchy = Add("other-duchy", "Duchy of Eastmarch", FeudalTitleType.Duchy, actor.StringId);
        county = Add("county", "County of Galend", FeudalTitleType.County, actor.StringId, duchy.TitleId);
        county.SetDeFactoParentTitle(otherDuchy.TitleId);
        a = Add("a", "Galend", FeudalTitleType.Barony, actor.StringId, county.TitleId);
        Rebuild();
        var resolveDestinations = AccessTools.Method(type, "GetDissolutionDestinations");
        (FeudalTitleRecord deJure, FeudalTitleRecord deFacto) = ((FeudalTitleRecord, FeudalTitleRecord))
            resolveDestinations.Invoke(titles, new object[] { county });
        string DestinationsText(FeudalTitleRecord legal, FeudalTitleRecord actual) => ((TextObject)
            AccessTools.Method(ui, "BuildDissolutionDestinationText").Invoke(null, new object[]
            {
                new FeudalDissolutionPreview
                {
                    DeJureChildren = 1, DeFactoChildren = 1, DeJureDestination = legal, DeFactoDestination = actual
                }
            })).ToString();
        hint = DestinationsText(deJure, deFacto);
        check(hint.Contains("De jure subordinates (1): Duchy of Westmarch")
            && hint.Contains("De facto subordinates (1): Duchy of Eastmarch"),
            "Dissolution names and counts the two different destination lieges");
        object[] dissolution = { county, true, "harness", null };
        check((bool)AccessTools.Method(type, "DissolveTitleInternal").Invoke(titles, dissolution)
            && a.ParentTitleId == deJure.TitleId && a.DeFactoParentTitleId == deFacto.TitleId,
            "Dissolution applies the same distinct lieges as the preview");

        var vacant = Add("vacant", "County without a liege", FeudalTitleType.County, actor.StringId);
        vacant.SetDeFactoParentTitle(otherDuchy.TitleId);
        otherDuchy.SetDeliberatelyDissolved(true);
        Rebuild();
        (deJure, deFacto) = ((FeudalTitleRecord, FeudalTitleRecord))resolveDestinations.Invoke(titles, new object[] { vacant });
        check(deJure == null && deFacto == null && DestinationsText(deJure, deFacto).Contains("No liege"),
            "Dissolution reports no liege instead of promoting children under a dissolved title");
        vacant.SetParentTitle(duchy.TitleId);
        (deJure, deFacto) = ((FeudalTitleRecord, FeudalTitleRecord))resolveDestinations.Invoke(titles, new object[] { vacant });
        check(deJure == duchy && deFacto == duchy, "Dissolution falls back to the active legal liege when the actual liege is invalid");
    }
}
