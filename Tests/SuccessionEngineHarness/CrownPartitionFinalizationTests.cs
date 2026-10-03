using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using BellumCivile;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;

internal static class CrownPartitionFinalizationTests
{
    internal static void Run(Action<bool, string> check)
    {
        FeudalTitleRecord Title(string id, FeudalTitleType rank, string parent, string actualParent = null)
        {
            var t = new FeudalTitleRecord(id, id, rank, "retained", "retained", parent, "", "origin", 0, 0);
            if (actualParent != null) t.SetDeFactoParentTitle(actualParent);
            return t;
        }
        var affected = new HashSet<string> { "root" };
        var root = Title("root", FeudalTitleType.Kingdom, "");
        var duchy = Title("duchy", FeudalTitleType.Duchy, "other", "root");
        var county = Title("county", FeudalTitleType.County, "duchy");
        var unrelated = Title("other", FeudalTitleType.Kingdom, "");
        var expand = AccessTools.Method(typeof(PartitionSuccessionBehavior), "ExpandCrownPromotionDescendants");
        expand.Invoke(null, new object[] { affected, new[] { root, duchy, county, unrelated } });
        check(affected.SetEquals(new[] { "root", "duchy", "county" }),
            "Capture includes mixed legal/political descendant chains without unrelated roots");
        expand.Invoke(null, new object[] { affected, new[] { root, duchy, county, unrelated } });
        check(affected.Count == 3, "Descendant expansion is idempotent");
        root.SetDeFactoParentTitle("county");
        expand.Invoke(null, new object[] { affected, new[] { root, duchy, county, unrelated } });
        check(affected.Count == 3, "Corrupt ancestry cycle cannot hang descendant capture");
        root.SetDeFactoParentTitle("");

        var realm = (Kingdom)FormatterServices.GetUninitializedObject(typeof(Kingdom));
        var clan = (Clan)FormatterServices.GetUninitializedObject(typeof(Clan));
        AccessTools.Property(typeof(Kingdom), "StringId").SetValue(realm, "successor");
        AccessTools.Property(typeof(Clan), "StringId").SetValue(clan, "retained");
        var journal = new CrownPartitionPromotionRecord { RetainedHouse = clan, Successor = realm,
            SuccessorId = "successor", FounderId = "founder", CrownId = "root",
            CrownRegistrationReturned = true, HierarchyStarted = true,
            PoliticalParentTargets = new Dictionary<string, string> { { "root", "" }, { "county", "root" } } };
        journal.EstateTitleIds.Add("root");
        journal.Titles.Add(new RealmUnionTitleRecord { TitleId = "root", Rank = FeudalTitleType.Kingdom,
            LegalHolderId = "retained", ActualHolderId = "retained", LegalParentId = "", ActualParentId = "",
            CapitalId = "", OriginRealmId = "origin" });
        journal.Titles.Add(new RealmUnionTitleRecord { TitleId = "county", Rank = FeudalTitleType.County,
            LegalHolderId = "retained", ActualHolderId = "retained", LegalParentId = "duchy", ActualParentId = "duchy",
            CapitalId = "", OriginRealmId = "origin" });
        root.SetDeJureHolder("founder"); root.SetDeFactoHolder("founder"); root.SetAssociatedKingdom("successor");
        county.SetDeFactoParentTitle("root");
        var titles = new FeudalTitleBehavior();
        var registry = (Dictionary<string, FeudalTitleRecord>)AccessTools.Field(typeof(FeudalTitleBehavior), "_titlesById").GetValue(titles);
        registry.Add("root", root); registry.Add("county", county); registry.Add("duchy", duchy);
        ((Dictionary<string, string>)AccessTools.Field(typeof(FeudalTitleBehavior), "_independentRealmSourceTitleByKingdomId")
            .GetValue(titles)).Add("successor", "root");
        var verify = AccessTools.Method(typeof(FeudalTitleBehavior), "VerifyCrownPromotionHierarchy");
        bool Verify() => (bool)verify.Invoke(titles, new object[] { journal, null });
        check(Verify() && journal.HierarchyVerified, "Final hierarchy verifies political reparenting while preserving legal parents");
        county.SetParentTitle("root");
        check(!Verify() && !journal.HierarchyVerified, "Legal reparenting invalidates final hierarchy receipt");
        county.SetParentTitle("duchy");
        journal.PoliticalParentTargets["county"] = "county";
        county.SetDeFactoParentTitle("county");
        check(!Verify(), "Same-rank/self political parent cannot verify");
        journal.PoliticalParentTargets["county"] = "missing";
        county.SetDeFactoParentTitle("missing");
        check(!Verify(), "Missing political parent cannot verify");
        journal.PoliticalParentTargets["county"] = "root";
        county.SetDeFactoParentTitle("root");
        journal.PoliticalParentTargets.Add("unknown", "");
        check(!Verify(), "Extra target outside frozen title manifest is rejected");
        journal.PoliticalParentTargets.Remove("unknown");
        check(Verify(), "Unchanged final hierarchy can be reverified without mutation");

        var councilRefresh = AccessTools.Method(typeof(PrivyCouncilBehavior), "ReconcilePartitionCouncil");
        var councilCode = PatchProcessor.GetOriginalInstructions(councilRefresh);
        check(councilCode.Any(i => i.Calls(AccessTools.Method(typeof(PrivyCouncilBehavior), "ValidateHolder"))),
            "Partition council refresh uses ordinary eligibility validation");
        check(!councilCode.Any(i => i.Calls(AccessTools.Method(typeof(PrivyCouncilBehavior), "OnDailyTick"))),
            "Partition council refresh does not run another daily salary/assignment/controversy tick");
        var courtCode = PatchProcessor.GetOriginalInstructions(AccessTools.Method(typeof(IdeologyBehavior), "RefreshPartitionCourtMembership"));
        int call = courtCode.FindIndex(i => i.Calls(AccessTools.Method(typeof(IdeologyBehavior), "ProcessKingdomIdeologies")));
        check(call > 0 && courtCode[call - 1].opcode == System.Reflection.Emit.OpCodes.Ldc_I4_0,
            "Partition membership refresh explicitly disables mood advancement");

        var preparation = PatchProcessor.GetOriginalInstructions(AccessTools.Method(typeof(PartitionSuccessionBehavior), "PrepareCrownPromotionSteps"));
        int previous = -1;
        foreach (var step in new[] {
            AccessTools.Method(typeof(PartitionSuccessionBehavior), "TryPrepareCrownPromotionFounder"),
            AccessTools.Method(typeof(PartitionSuccessionBehavior), "TryDeliverCrownPromotionEstate"),
            AccessTools.Method(typeof(FeudalTitleBehavior), "TryPrepareCrownPromotionRealm"),
            AccessTools.Method(typeof(PartitionSuccessionBehavior), "TryPrepareCrownPromotionGovernment"),
            AccessTools.Method(typeof(FeudalTitleBehavior), "TryRegisterCrownPromotion"),
            AccessTools.Method(typeof(FeudalTitleBehavior), "TryFinalizeCrownPromotionHierarchy"),
            AccessTools.Method(typeof(PartitionSuccessionBehavior), "TryFinalizeCrownPromotionCourt") })
        {
            int index = preparation.FindIndex(i => i.Calls(step));
            check(index > previous, "Preparation stage order: " + step.Name);
            previous = index;
        }
        check(!preparation.Any(i => i.Calls(AccessTools.Method(typeof(CrownPartitionPromotionRecord), "TryComplete"))),
            "Preparing a Crown package cannot prematurely complete its whole estate");
        var advance = AccessTools.Method(typeof(PartitionSuccessionBehavior), "TryPrepareCrownPromotion");
        var unregistered = new object[] { new CrownPartitionPromotionRecord(), null };
        check(!(bool)advance.Invoke(new PartitionSuccessionBehavior(), unregistered) && unregistered[1] != null,
            "Preparation refuses an unregistered journal without campaign mutations");
        var multi = new PartitionSuccessionBehavior();
        var first = new CrownPartitionPromotionRecord();
        var estate = new PendingPartitionSuccessionRecord("dead", "clan", "realm", "", "", default(CampaignTime))
        { CrownPromotions = new List<CrownPartitionPromotionRecord> { first, new CrownPartitionPromotionRecord() } };
        AccessTools.Field(typeof(PartitionSuccessionBehavior), "_pendingPartitions").SetValue(multi,
            new List<PendingPartitionSuccessionRecord> { estate });
        var multiArgs = new object[] { first, null };
        check(!(bool)advance.Invoke(multi, multiArgs) && ((string)multiArgs[1]).Contains("batch") && !first.FounderCreationStarted,
            "Multi-Crown estate is deferred before first mutation until batch coordination is ready");

        var batchPreparation = PatchProcessor.GetOriginalInstructions(AccessTools.Method(typeof(PartitionSuccessionBehavior), "TryPrepareCrownBatch"));
        previous = -1;
        foreach (var step in new[] {
            AccessTools.Method(typeof(PartitionSuccessionBehavior), "TryPrepareCrownPromotionFounder"),
            AccessTools.Method(typeof(PartitionSuccessionBehavior), "TryDeliverCrownPromotionEstate"),
            AccessTools.Method(typeof(FeudalTitleBehavior), "TryPrepareCrownPromotionRealm"),
            AccessTools.Method(typeof(PartitionSuccessionBehavior), "TryPrepareCrownPromotionGovernment"),
            AccessTools.Method(typeof(FeudalTitleBehavior), "TryRegisterCrownPromotion"),
            AccessTools.Method(typeof(PartitionSuccessionBehavior), "TryMoveCrownPromotionHouses"),
            AccessTools.Method(typeof(FeudalTitleBehavior), "TryFinalizeCrownBatchHierarchy"),
            AccessTools.Method(typeof(PartitionSuccessionBehavior), "TryDeliverLesserPartitionShare"),
            AccessTools.Method(typeof(PartitionSuccessionBehavior), "TryFinalizeCrownBatchCourt"),
            AccessTools.Method(typeof(PartitionSuccessionBehavior), "TrySettleCrownBatchRewards") })
        {
            int index = batchPreparation.FindIndex(i => i.Calls(step));
            check(index > previous, "Batch preparation stage order: " + step.Name);
            previous = index;
        }
        check(!batchPreparation.Any(i => i.Calls(AccessTools.Method(typeof(CrownPartitionPromotionRecord), "TryComplete"))),
            "Batch preparation cannot retire the whole estate");
        var batchCourt = AccessTools.Method(typeof(PartitionSuccessionBehavior), "TryFinalizeCrownBatchCourt");
        var courtBatch = new CrownPartitionBatchRecord();
        estate.CrownBatch = courtBatch;
        bool FinalizeCourt(out string reason)
        {
            var input = new object[] { estate, null };
            bool ok = (bool)batchCourt.Invoke(multi, input);
            reason = input[1] as string;
            return ok;
        }
        check(!FinalizeCourt(out _) && !courtBatch.CourtFinalizationStarted,
            "Shared court cannot start before verified hierarchy");
        courtBatch.HierarchyStarted = courtBatch.HierarchyReturned = courtBatch.HierarchyVerified = true;
        courtBatch.CourtFinalizationStarted = true;
        check(!FinalizeCourt(out string interrupted) && interrupted.Contains("interrupted")
            && !courtBatch.CourtFinalizationReturned, "Interrupted shared court cannot replay membership callbacks");
        courtBatch.CourtFinalizationStarted = false;
        courtBatch.CourtFinalizationReturned = true;
        check(!FinalizeCourt(out _), "Returned court receipt without start cannot be accepted");
        var sharedCourtCode = PatchProcessor.GetOriginalInstructions(batchCourt);
        check(sharedCourtCode.Any(i => i.Calls(councilRefresh))
            && sharedCourtCode.Any(i => i.Calls(AccessTools.Method(typeof(IdeologyBehavior), "RefreshPartitionCourtMembership"))),
            "Shared courts use non-daily council and membership reconciliation");

        Hero Person(string id)
        {
            var hero = (Hero)FormatterServices.GetUninitializedObject(typeof(Hero));
            AccessTools.Property(typeof(Hero), "StringId").SetValue(hero, id);
            return hero;
        }
        var donor = Person("donor"); var elder = Person("elder"); var younger = Person("younger");
        var treasuryType = typeof(CrownPartitionBatchRecord).Assembly.GetType("BellumCivile.PartitionEstateTreasury");
        var captureGold = AccessTools.Method(treasuryType, "TryCapture");
        var goldArgs = new object[] { donor, 101, new[] { younger, elder }, null, null };
        check((bool)captureGold.Invoke(null, goldArgs), "Treasury captures all secondary beneficiaries together");
        var payments = (List<CrownAccessionRecord>)goldArgs[3];
        check(payments.Count == 2 && payments.All(p => p.EndowmentGold == 33)
            && payments[0].GoldRecipient == elder, "Equal gold shares preserve primary share and remainder with deterministic order");
        var deliverGold = AccessTools.Method(treasuryType, "TryDeliver");
        int debits = 0, credits = 0;
        bool interrupt = true;
        bool Pay(CrownAccessionRecord payment)
        {
            if (!payment.GoldDebited) { payment.DeliveredGold = payment.EndowmentGold; payment.GoldDebited = true; debits++; }
            if (interrupt) { interrupt = false; throw new InvalidOperationException("simulated interruption"); }
            payment.GoldCredited = true; credits++;
            return true;
        }
        bool Deliver() => (bool)deliverGold.Invoke(null, new object[] { payments, (Func<CrownAccessionRecord, bool>)Pay, null });
        check(!Deliver() && debits == 1 && credits == 0, "Interrupted debit preserves progress without paying later heirs");
        check(Deliver() && debits == 2 && credits == 2, "Treasury retry credits interrupted share without a second debit");
        check(Deliver() && debits == 2 && credits == 2, "Completed treasury cannot pay heirs twice");
        payments[0].GoldDebited = false;
        check(!Deliver() && debits == 2, "Contradictory gold receipts block before invoking payment");
        var duplicates = new object[] { donor, 100, new[] { elder, elder }, null, null };
        check(!(bool)captureGold.Invoke(null, duplicates) && duplicates[3] == null, "Duplicate beneficiaries cannot publish treasury shares");
        var afterCreditArgs = new object[] { donor, 10, new[] { elder }, null, null };
        captureGold.Invoke(null, afterCreditArgs);
        payments = (List<CrownAccessionRecord>)afterCreditArgs[3];
        int callbacks = 0;
        bool ThrowAfterCredit(CrownAccessionRecord payment)
        {
            payment.DeliveredGold = payment.EndowmentGold;
            payment.GoldDebited = payment.GoldCredited = true;
            callbacks++;
            throw new InvalidOperationException("trade listener failed after credit");
        }
        bool DeliverAfterCredit() => (bool)deliverGold.Invoke(null,
            new object[] { payments, (Func<CrownAccessionRecord, bool>)ThrowAfterCredit, null });
        check(!DeliverAfterCredit() && DeliverAfterCredit() && callbacks == 1,
            "Throwing trade listener after credit cannot repeat payment or its notification");
    }
}
