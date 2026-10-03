using System;
using System.Collections.Generic;
using System.Linq;
using BellumCivile;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;

internal static class CrownPartitionBatchTests
{
    internal static void Run(Action<bool, string> check)
    {
        var type = typeof(FeudalTitleRecord).Assembly.GetType("BellumCivile.CrownPartitionBatchPlan");
        var create = AccessTools.Method(type, "TryCreate");
        FeudalTitleRecord Title(string id, FeudalTitleType rank, string parent, string house, string land = "")
            => new FeudalTitleRecord(id, id, rank, house, house, parent, land, "origin", 0, 0);
        var titles = new List<FeudalTitleRecord> {
            Title("primary", FeudalTitleType.Kingdom, "", "ruler"),
            Title("second", FeudalTitleType.Kingdom, "", "ruler"),
            Title("third", FeudalTitleType.Kingdom, "", "ruler"),
            Title("duchy", FeudalTitleType.Duchy, "second", "mixed"),
            Title("county", FeudalTitleType.County, "third", "mixed"),
            Title("a", FeudalTitleType.Barony, "duchy", "mixed", "town_a"),
            Title("b", FeudalTitleType.Barony, "county", "mixed", "town_b"),
            Title("c", FeudalTitleType.Barony, "third", "vassal", "town_c") };
        var holdings = new Dictionary<string, string[]> { ["ruler"] = new string[0],
            ["mixed"] = new[] { "town_a", "town_b" }, ["vassal"] = new[] { "town_c" } };
        var recipients = new Dictionary<string, string> { ["second"] = "heir_a", ["third"] = "heir_b" };
        object plan = null;
        bool Plan()
        {
            var args = new object[] { "ruler", "primary", recipients, holdings, titles, null, null };
            bool ok = (bool)create.Invoke(null, args); plan = args[5];
            check(ok || plan == null && !string.IsNullOrEmpty(args[6] as string), "Invalid Crown batch publishes no partial plan");
            return ok;
        }
        IReadOnlyDictionary<string, string> Destinations() => (IReadOnlyDictionary<string, string>)
            AccessTools.Property(type, "DestinationCrownByHouse").GetValue(plan);
        List<RealmUnionTitleRecord> Snapshot() => (List<RealmUnionTitleRecord>)AccessTools.Method(type, "CopyTitleSnapshot").Invoke(plan, null);
        check(Plan(), "Three-Crown estate can plan both secondary houses against one baseline");
        var capture = AccessTools.Method(typeof(CrownPartitionBatchRecord), "Capture");
        var saved = (CrownPartitionBatchRecord)capture.Invoke(null, new object[] { "realm", "predecessor", plan });
        var restore = AccessTools.Method(typeof(CrownPartitionBatchRecord), "TryRestorePlan");
        bool Restore()
        {
            var args = new object[] { null, null };
            bool ok = (bool)restore.Invoke(saved, args);
            check(ok || args[0] == null && !string.IsNullOrWhiteSpace(args[1] as string), "Invalid saved batch cannot produce a partial restored plan");
            return ok;
        }
        check(Restore(), "Saved batch payload reconstructs the common pre-mutation plan");
        saved.Destinations["mixed"] = "third";
        check(!Restore(), "Changed saved destination cannot silently override original principal-title rules");
        saved.Destinations["mixed"] = "second";
        saved.Holdings["heir_a"].Add("town_a");
        check(!Restore(), "Reserved founder cannot acquire baseline holdings during reload");
        saved.Holdings["heir_a"].Clear();
        check(Restore(), "Restoring original batch data revalidates without campaign objects");
        var behavior = new PartitionSuccessionBehavior();
        var pending = new PendingPartitionSuccessionRecord("predecessor", "ruler", "realm", "", "", default(CampaignTime), true, "", "primary");
        var queue = new List<PendingPartitionSuccessionRecord> { pending };
        AccessTools.Field(typeof(PartitionSuccessionBehavior), "_pendingPartitions").SetValue(behavior, queue);
        var publish = AccessTools.Method(typeof(PartitionSuccessionBehavior), "TryRecordCrownPartitionBatch");
        bool Publish() => (bool)publish.Invoke(behavior, new object[] { pending, plan, null });
        check(Publish() && pending.CrownBatch != null && pending.CrownPromotions == null, "Batch publication precedes individual promotion records");
        var original = pending.CrownBatch;
        check(!Publish() && ReferenceEquals(original, pending.CrownBatch), "Batch publication cannot overwrite a saved estate baseline");
        var laterEstate = new PendingPartitionSuccessionRecord("later", "ruler", "realm", "", "", default(CampaignTime), true, "", "primary");
        queue.Add(laterEstate);
        var laterArgs = new object[] { laterEstate, plan, null };
        check(!(bool)publish.Invoke(behavior, laterArgs) && laterEstate.CrownBatch == null
            && !string.IsNullOrEmpty(laterArgs[2] as string), "Later estate cannot reserve a source realm already owned by a pending batch");
        queue.Remove(laterEstate);
        var conflict = AccessTools.Method(typeof(PartitionSuccessionBehavior), "HasConflictingCrownBatchReservation");
        var otherEstate = new PendingPartitionSuccessionRecord("other_dead", "other_house", "other_realm", "", "", default(CampaignTime));
        otherEstate.CrownBatch = new CrownPartitionBatchRecord { Recipients = new Dictionary<string, string> { ["other_crown"] = "heir_a" } };
        queue.Add(otherEstate);
        check((bool)conflict.Invoke(behavior, new object[] { pending, original }), "Batch-only founder reservation is visible before promotion capture");
        otherEstate.CrownBatch.Recipients["other_crown"] = "unrelated_heir";
        check(!(bool)conflict.Invoke(behavior, new object[] { pending, original }), "Independent nonconflicting batches can reserve different identities");
        otherEstate.CrownBatch = null;
        otherEstate.CrownPromotions = new List<CrownPartitionPromotionRecord> {
            new CrownPartitionPromotionRecord { FounderId = "heir_b" } };
        check((bool)conflict.Invoke(behavior, new object[] { pending, original }), "Legacy single-Crown reservation also blocks conflicting batch founder");
        queue.Remove(otherEstate);
        var capturePromotions = AccessTools.Method(typeof(PartitionSuccessionBehavior), "TryCaptureCrownBatchPromotions");
        var invalidCapture = new object[] { pending, new Dictionary<string, Hero>(), null };
        check(!(bool)capturePromotions.Invoke(behavior, invalidCapture) && pending.CrownPromotions == null
            && ReferenceEquals(original, pending.CrownBatch) && !string.IsNullOrEmpty(invalidCapture[2] as string),
            "Incomplete heir manifest leaves the saved batch untouched and publishes no promotions");
        check((bool)AccessTools.Method(typeof(PartitionSuccessionBehavior), "HasCrownPromotionJournal").Invoke(null, new object[] { pending }),
            "Batch-only estate bypasses legacy partition execution");
        AccessTools.Method(typeof(PartitionSuccessionBehavior), "PrunePendingPartitions").Invoke(behavior, null);
        var retained = (List<PendingPartitionSuccessionRecord>)AccessTools.Field(typeof(PartitionSuccessionBehavior), "_pendingPartitions").GetValue(behavior);
        check(retained.Contains(pending), "Crown-only batch survives physical-fief pruning");
        check(!(bool)AccessTools.Method(typeof(PartitionSuccessionBehavior), "IsCrownPromotionTitleProtected").Invoke(behavior, new object[] { "second" }),
            "Unstarted batch does not lock titles and tolerates absent promotion list");
        var destinations = Destinations();
        check(destinations["ruler"] == "primary" && destinations["heir_a"] == "second" && destinations["heir_b"] == "third",
            "Primary house stays and each founder receives its assigned Crown");
        check(destinations["mixed"] == "second" && destinations["vassal"] == "third",
            "Mixed estate follows one principal title without splitting the house");
        check(holdings.Count == 3 && holdings["mixed"].Length == 2 && titles.All(t => t.AssociatedKingdomId == "origin"),
            "Batch planning does not mutate live source titles or holdings");
        var copy = Snapshot(); copy[0].LegalHolderId = "edited";
        check(Snapshot()[0].LegalHolderId != "edited", "Copied batch snapshots cannot alter the frozen baseline");
        var frozenHoldings = (IReadOnlyDictionary<string, IReadOnlyList<string>>)AccessTools.Property(type, "HoldingsByHouse").GetValue(plan);
        holdings["mixed"][0] = "changed";
        check(frozenHoldings["mixed"].SequenceEqual(new[] { "town_a", "town_b" }), "Later source array edits cannot alter frozen physical holdings");
        holdings["mixed"][0] = "town_a";
        titles.Single(t => t.TitleId == "duchy").SetParentTitle("third");
        check(Snapshot().Single(t => t.TitleId == "duchy").LegalParentId == "second" && destinations["mixed"] == "second",
            "Later live hierarchy changes cannot rewrite the planned estate");
        titles.Single(t => t.TitleId == "duchy").SetParentTitle("second");
        titles.Reverse(); recipients = recipients.Reverse().ToDictionary(p => p.Key, p => p.Value);
        check(Plan() && Destinations().All(p => destinations[p.Key] == p.Value), "Input order does not affect batch allegiance");
        recipients["third"] = "heir_a";
        check(!Plan(), "One founder cannot be reserved for two separating Crowns");
        recipients["third"] = "mixed";
        check(!Plan(), "Reserved founder cannot overwrite an existing vassal house");
        recipients["third"] = "heir_b"; recipients["primary"] = "heir_c";
        check(!Plan(), "Primary Crown cannot be assigned to a secondary founder");
        recipients.Remove("primary");
        titles.Single(t => t.TitleId == "third").SetDeJureHolder("foreign");
        check(!Plan(), "Contested Crown blocks the entire batch before mutation");
        titles.Single(t => t.TitleId == "third").SetDeJureHolder("ruler");
        var empire = Title("empire", FeudalTitleType.Empire, "", "ruler");
        titles.Add(empire);
        titles.Single(t => t.TitleId == "primary").SetParentTitle("empire");
        titles.Single(t => t.TitleId == "third").SetParentTitle("empire");
        check(!Plan(), "Shared fully held empire binding rejects the assigned secondary Crown");
        titles.Single(t => t.TitleId == "primary").SetParentTitle("");
        titles.Single(t => t.TitleId == "third").SetParentTitle("");
        check(Plan(), "Unrelated empire does not block separating coequal Crown packages");
        holdings["vassal"] = new[] { "town_a" };
        check(!Plan(), "Conflicting physical holdings block the entire batch");
    }
}
