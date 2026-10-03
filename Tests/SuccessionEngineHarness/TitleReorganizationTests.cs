using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using BellumCivile;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;

internal static class TitleReorganizationTests
{
    private static readonly Dictionary<string, Clan> Houses = new Dictionary<string, Clan>();
    private static Hero _leader;
    private static bool Resolve(string clanId, ref Clan __result)
    { Houses.TryGetValue(clanId, out __result); return false; }
    private static bool Leader(ref Hero __result) { __result = _leader; return false; }
    private static bool Alive(ref bool __result) { __result = false; return false; }
    private static bool Day(ref float __result) { __result = 100; return false; }

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
        }
        finally { h.UnpatchAll(h.Id); Houses.Clear(); }
    }
}
