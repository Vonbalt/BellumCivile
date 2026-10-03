using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using BellumCivile;
using HarmonyLib;
using TaleWorlds.CampaignSystem;

internal static class PartitionEstateSettlementTests
{
    internal static void Run(Action<bool, string> check)
    {
        T Entity<T>(string id)
        {
            var value = (T)FormatterServices.GetUninitializedObject(typeof(T));
            AccessTools.Property(typeof(T), "StringId").SetValue(value, id); return value;
        }
        var primary = Entity<Hero>("primary"); var royal = Entity<Hero>("royal"); var lesser = Entity<Hero>("lesser");
        var source = Entity<Clan>("source"); var crownHouse = Entity<Clan>("crown_house"); var lesserHouse = Entity<Clan>("lesser_house");
        CrownEstateDeliveryRecord Receipt(string id) => new CrownEstateDeliveryRecord {
            AssetId = id, IsTitle = true, Started = true, ActionReturned = true, Verified = true };
        var crown = new CrownPartitionPromotionRecord { CrownId = "second", Heir = royal, Founder = crownHouse,
            EstateDeliveryStarted = true, EstateVerified = true, CrownRegistrationReturned = true,
            EstateTitleIds = new List<string> { "second" }, EstateReceipts = new List<CrownEstateDeliveryRecord> { Receipt("second") } };
        var small = new CrossClanEstateShare { Heir = lesser, Recipient = lesserHouse, RootTitleId = "duchy",
            Titles = new List<string> { "duchy" }, LandedSettled = true, PartitionFounderStarted = true,
            PartitionFounderReturned = true, PartitionReceipts = new List<CrownEstateDeliveryRecord> { Receipt("duchy") } };
        var pending = new PendingPartitionSuccessionRecord("dead", "source", "realm", "", "royal|lesser", default(CampaignTime),
            true, "first|second|duchy", "first") {
            CrownBatch = new CrownPartitionBatchRecord { PrimaryCrownId = "first", HierarchyVerified = true,
                CourtFinalizationStarted = true, CourtFinalizationReturned = true,
                Recipients = new Dictionary<string, string> { ["second"] = "crown_house" } },
            CrownPromotions = new List<CrownPartitionPromotionRecord> { crown },
            EstateShares = new List<CrossClanEstateShare> {
                small,
                new CrossClanEstateShare { Primary = true, Heir = primary, Recipient = source, Titles = new List<string> { "first" } },
                new CrossClanEstateShare { Heir = royal, RootTitleId = "second", Titles = new List<string> { "second" } } } };
        var resolve = AccessTools.Method(typeof(CrownPartitionBatchRecord).Assembly.GetType("BellumCivile.PartitionEstateSettlement"), "TryResolveRecipients");
        List<Tuple<Clan, Hero>> branches = null;
        bool Resolve()
        {
            var args = new object[] { pending, null, null };
            bool ok = (bool)resolve.Invoke(null, args); branches = (List<Tuple<Clan, Hero>>)args[1];
            check(ok || branches == null && !string.IsNullOrEmpty(args[2] as string), "Invalid settlement exposes no partial beneficiary list");
            return ok;
        }
        check(Resolve() && branches.Count == 3 && branches[0].Item1 == source
            && branches.Exists(b => b.Item1 == crownHouse && b.Item2 == royal)
            && branches.Exists(b => b.Item1 == lesserHouse && b.Item2 == lesser),
            "Reward settlement combines primary, Crown and lesser beneficiaries");
        small.LandedSettled = false;
        check(!Resolve(), "Unfinished lesser share blocks all final rewards");
        small.LandedSettled = true; small.PartitionReceipts[0].Verified = false;
        check(!Resolve(), "Landed flag alone cannot bypass missing asset verification");
        small.PartitionReceipts[0].Verified = true; crown.EstateReceipts[0].ActionReturned = false;
        check(!Resolve(), "Incomplete Crown delivery blocks estate rewards");
        crown.EstateReceipts[0].ActionReturned = true; small.Titles.Add("first");
        check(!Resolve(), "Overlapping estate assets cannot settle");
        small.Titles.Remove("first"); small.Recipient = source;
        check(!Resolve(), "Duplicate recipient house cannot collect two shares");
        small.Recipient = lesserHouse; small.Heir = royal;
        check(!Resolve(), "Duplicate beneficiary cannot collect two shares");
        small.Heir = lesser; pending.CrownBatch.CourtFinalizationReturned = false;
        check(!Resolve(), "Rewards wait for shared court completion");
        pending.CrownBatch.CourtFinalizationReturned = true;
        check(Resolve(), "Restored complete estate resolves without changing saved shares");
        var announce = AccessTools.Method(typeof(CrownPartitionBatchRecord), "TryAnnounce");
        int notifications = 0;
        bool Announce(string key, Action action) => (bool)announce.Invoke(pending.CrownBatch, new object[] { key, action, null });
        check(!Announce("heir:royal", () => notifications++), "Announcements wait for verified obligations");
        pending.CrownBatch.ObligationsVerified = true;
        pending.CrownBatch.OriginalAgreements = new Dictionary<string, string>();
        check(Announce("heir:royal", () => notifications++) && Announce("heir:royal", () => notifications++)
            && notifications == 1, "Completed succession message is not repeated");
        check(!Announce("heir:lesser", () => { notifications++; throw new InvalidOperationException("message callback"); })
            && !Announce("heir:lesser", () => notifications++) && notifications == 2,
            "Interrupted succession message is blocked instead of duplicated");
        pending.CrownBatch.AnnouncementsStarted.Remove("heir:lesser");
        check(Announce("heir:lesser", () => notifications++) && Announce("treasury", () => notifications++),
            "Complete announcement fixture records each required message and treasury summary");
        pending.ClaimsStarted = pending.ClaimsReturned = true;
        pending.GoldPayments = new List<CrownAccessionRecord> {
            new CrownAccessionRecord { IncomingSourceHead = primary, GoldRecipient = royal, GoldDebited = true, GoldCredited = true },
            new CrownAccessionRecord { IncomingSourceHead = primary, GoldRecipient = lesser, GoldDebited = true, GoldCredited = true } };
        var complete = AccessTools.Method(typeof(CrownPartitionBatchRecord).Assembly.GetType("BellumCivile.PartitionBatchCompletion"), "TryComplete");
        bool Complete(bool verified) => (bool)complete.Invoke(null, new object[] { pending, verified, null });
        check(!Complete(false) && !crown.Completed && !small.Completed, "Completion cannot release guards without current campaign verification");
        pending.GoldPayments[0].GoldCredited = false;
        check(!Complete(true) && !pending.CrownBatch.Completed, "Incomplete reward prevents final completion");
        pending.GoldPayments[0].GoldCredited = true;
        pending.CrownBatch.AnnouncementsReturned.Remove("treasury");
        check(!Complete(true) && !crown.Completed, "Missing summary receipt cannot partially complete Crowns");
        pending.CrownBatch.AnnouncementsReturned.Add("treasury");
        check(Complete(true) && pending.CrownBatch.Completed && crown.Completed && small.Completed
            && crown.ObligationsSettled && crown.Announced, "Final completion publishes all share and Crown receipts together");
        check(!Complete(true), "Completed batch cannot reenter finalization");
        var behavior = new BellumCivile.Behaviors.PartitionSuccessionBehavior();
        var queue = new List<PendingPartitionSuccessionRecord> { pending };
        AccessTools.Field(typeof(BellumCivile.Behaviors.PartitionSuccessionBehavior), "_pendingPartitions").SetValue(behavior, queue);
        var finish = AccessTools.Method(typeof(BellumCivile.Behaviors.PartitionSuccessionBehavior), "TryFinishCrownBatch");
        check((bool)finish.Invoke(behavior, new object[] { pending, null }) && queue.Count == 0,
            "Previously completed batch retires without replaying native actions");
        check(!(bool)finish.Invoke(behavior, new object[] { pending, null }),
            "Retired batch cannot reenter completion");
    }
}
