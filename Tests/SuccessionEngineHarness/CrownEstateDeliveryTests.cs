using System;
using System.Collections.Generic;
using System.Linq;
using BellumCivile;
using BellumCivile.Behaviors;
using HarmonyLib;

internal static class CrownEstateDeliveryTests
{
    internal static void Run(Action<bool, string> check)
    {
        var receipt = new CrownEstateDeliveryRecord { AssetId = "fief" };
        var deliver = AccessTools.Method(typeof(CrownEstateDeliveryRecord), "TryDeliver");
        bool original = true, delivered = false;
        int calls = 0;
        Action action = () => { calls++; delivered = true; };
        bool Run()
        {
            var args = new object[] { (Func<bool>)(() => original), action, (Func<bool>)(() => delivered), null };
            bool ok = (bool)deliver.Invoke(receipt, args);
            check(ok || !string.IsNullOrWhiteSpace(args[3] as string), "Estate refusal reports a reason");
            return ok;
        }
        check(Run() && calls == 1 && receipt.Verified, "Estate action records returned and verified delivery");
        original = false;
        check(Run() && calls == 1, "Verified estate delivery does not replay native action");
        delivered = false;
        check(!Run() && calls == 1, "Changed ownership invalidates previous receipt without replay");
        receipt = new CrownEstateDeliveryRecord { AssetId = "fief" };
        check(!Run() && !receipt.Started && calls == 1, "Changed original owner blocks action before start");
        original = true;
        action = () => { calls++; delivered = true; throw new InvalidOperationException("callback failure"); };
        check(!Run() && receipt.Started && !receipt.ActionReturned && calls == 2, "Partial native transfer retains interrupted receipt");
        check(!Run() && calls == 2, "New ownership alone cannot prove interrupted callback completion");
        receipt = new CrownEstateDeliveryRecord { AssetId = "fief", Started = true, ActionReturned = true };
        check(Run() && receipt.Verified && calls == 2, "Returned action recovers missing verification receipt without replay");
        receipt = new CrownEstateDeliveryRecord { AssetId = "fief", Verified = true };
        check(!Run(), "Contradictory estate receipts fail closed");
        var journal = new CrownPartitionPromotionRecord { CrownId = "crown", EstateDeliveryStarted = true, EstateVerified = true };
        journal.EstateTitleIds.Add("crown");
        var verify = AccessTools.Method(typeof(CrownPartitionPromotionRecord), "HasVerifiedEstateReceipts");
        bool Manifest() => (bool)verify.Invoke(journal, null);
        check(!Manifest(), "Global estate flag cannot replace per-asset receipts");
        journal.EstateReceipts = new List<CrownEstateDeliveryRecord> {
            new CrownEstateDeliveryRecord { AssetId = "crown", IsTitle = true, Started = true, ActionReturned = true, Verified = true }
        };
        check(Manifest(), "Vassal-only Crown verifies without inventing a personal fief");
        journal.EstateFiefIds.Add("fief");
        check(!Manifest(), "Undelivered physical estate blocks realm creation");
        journal.EstateReceipts.Add(new CrownEstateDeliveryRecord { AssetId = "fief", Started = true, ActionReturned = true, Verified = true });
        check(Manifest(), "Physical and legal deliveries both verify against manifest");
        journal.EstateReceipts[1].AssetId = "other";
        check(!Manifest(), "Unrelated delivered asset cannot satisfy inheritance manifest");
        var callback = PatchProcessor.GetOriginalInstructions(AccessTools.Method(typeof(FeudalTitleBehavior), "OnSettlementOwnerChanged"));
        var scoped = AccessTools.Method(typeof(FeudalTitleBehavior), "TryApplyJournaledCrownEstateOwnership");
        check(callback.Any(i => i.Calls(scoped)), "Ownership callback recognizes exact journaled Crown delivery");
        var custom = PatchProcessor.GetOriginalInstructions(scoped);
        check(!custom.Any(i => i.Calls(AccessTools.Method(typeof(FeudalTitleRecord), "SetParentTitle")))
            && !custom.Any(i => i.Calls(AccessTools.Method(typeof(FeudalTitleRecord), "SetDeJureHolder"))),
            "Special possession callback does not rewrite legal rights or hierarchy");
    }
}
