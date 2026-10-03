using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using BellumCivile;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;

internal static class CrownPartitionCaptureTests
{
    internal static void Run(Action<bool, string> check)
    {
        var behavior = new PartitionSuccessionBehavior();
        var pending = new PendingPartitionSuccessionRecord("dead", "parent", "realm", "", "heir", default(CampaignTime));
        var journal = new CrownPartitionPromotionRecord { CrownId = "crown" };
        journal.Titles.Add(new RealmUnionTitleRecord { TitleId = "child" });
        pending.CrownPromotions = new List<CrownPartitionPromotionRecord> { journal };
        var legacy = new PendingPartitionSuccessionRecord("dead", "parent", "realm", "fief", "heir", default(CampaignTime));
        var queue = new List<PendingPartitionSuccessionRecord> { legacy, pending };
        AccessTools.Field(typeof(PartitionSuccessionBehavior), "_pendingPartitions").SetValue(behavior, queue);
        var guard = AccessTools.Method(typeof(PartitionSuccessionBehavior), "IsCrownPromotionTitleProtected");
        bool Protected(string id) => (bool)guard.Invoke(behavior, new object[] { id });
        check(!Protected("crown"), "Capture alone does not lock hierarchy before creation begins");
        journal.FounderCreationStarted = true;
        check(Protected("crown") && Protected("child"), "Started promotion protects recorded Crown and affected titles");
        check(!Protected("unrelated") && !Protected(null), "Promotion guard does not affect unrelated hierarchy");
        journal.HierarchyVerified = true;
        check(Protected("child"), "Hierarchy receipt does not release protection before full completion");
        journal.Completed = true;
        check(!Protected("crown"), "Completed promotion releases title protection");
        AccessTools.Method(typeof(PartitionSuccessionBehavior), "PrunePendingPartitions").Invoke(behavior, null);
        var retained = (List<PendingPartitionSuccessionRecord>)AccessTools.Field(typeof(PartitionSuccessionBehavior), "_pendingPartitions").GetValue(behavior);
        check(retained.Count == 1 && ReferenceEquals(retained[0], pending), "Pruning retains journaled vassal-only estate and removes competing legacy record");
        var later = new PendingPartitionSuccessionRecord("later_death", "parent", "realm", "fief", "heir", default(CampaignTime));
        retained.Add(later);
        AccessTools.Method(typeof(PartitionSuccessionBehavior), "PrunePendingPartitions").Invoke(behavior, null);
        retained = (List<PendingPartitionSuccessionRecord>)AccessTools.Field(typeof(PartitionSuccessionBehavior), "_pendingPartitions").GetValue(behavior);
        check(retained.Contains(later) && retained.Contains(pending), "Later predecessor's estate is retained behind a journaled transition");
        journal.Completed = false;
        var loaded = new PartitionSuccessionBehavior();
        AccessTools.Field(typeof(PartitionSuccessionBehavior), "_pendingPartitions").SetValue(loaded, new List<PendingPartitionSuccessionRecord> { pending });
        check((bool)guard.Invoke(loaded, new object[] { "child" }), "Reconstructed saved journal restores guard without runtime scope");
        var process = PatchProcessor.GetOriginalInstructions(AccessTools.Method(typeof(PartitionSuccessionBehavior), "ProcessPendingPartitions"));
        var hasJournal = AccessTools.Method(typeof(PartitionSuccessionBehavior), "HasCrownPromotionJournal");
        check(process.FindIndex(i => i.Calls(hasJournal)) < process.FindIndex(i => i.Calls(AccessTools.Method(typeof(PartitionSuccessionBehavior), "IsExpired"))),
            "Journal routing precedes legacy expiry");
        var reconcile = PatchProcessor.GetOriginalInstructions(AccessTools.Method(typeof(FeudalTitleBehavior), "ReconcileDeFactoParent"));
        check(reconcile.Any(i => i.Calls(guard)), "Shared political-parent reconciliation consults saved promotion guard");
        var capture = AccessTools.Method(typeof(PartitionSuccessionBehavior), "TryCaptureCrownPromotion");
        var args = new object[] { pending, null, null, null, null, null };
        check(!(bool)capture.Invoke(behavior, args) && args[4] == null && !string.IsNullOrEmpty(args[5] as string)
            && pending.CrownPromotions.Count == 1, "Invalid capture cannot publish or overwrite an existing journal");

        Kingdom Shell(string id)
        {
            var realm = (Kingdom)FormatterServices.GetUninitializedObject(typeof(Kingdom));
            AccessTools.Property(typeof(Kingdom), "StringId").SetValue(realm, id);
            return realm;
        }
        var parent = Shell("primary");
        var successor = Shell("reserved_successor");
        journal.Parent = parent;
        journal.SuccessorId = successor.StringId;
        var realmGuard = AccessTools.Method(typeof(PartitionSuccessionBehavior), "IsCrownPromotionRealmProtected");
        bool RealmProtected(Kingdom realm) => (bool)realmGuard.Invoke(loaded, new object[] { realm });
        journal.FounderCreationStarted = false;
        check(!RealmProtected(parent) && !RealmProtected(successor), "Captured plans do not reserve realm cleanup before mutation");
        journal.FounderCreationStarted = true;
        check(RealmProtected(parent) && !RealmProtected(successor), "Founder creation reserves primary but not an uncreated successor");
        journal.RealmCreationStarted = true;
        check(RealmProtected(successor), "Reserved ID protects shell during creation callback before object receipt");
        journal.Successor = successor;
        check(RealmProtected(successor) && !RealmProtected(Shell(successor.StringId)), "Recorded shell identity rejects a different object with the same ID");
        check(!RealmProtected(null) && !RealmProtected(Shell("other")), "Unrelated realms are not protected");
        journal.HierarchyVerified = true;
        check(RealmProtected(parent) && RealmProtected(successor), "Intermediate verification does not release incomplete realm protection");
        journal.Completed = true;
        check(!RealmProtected(parent) && !RealmProtected(successor), "Completion releases both shells for ordinary cleanup");
        var patchType = typeof(PartitionSuccessionBehavior).Assembly.GetType("BellumCivile.Patches.CivilWarParentDestructionPatch");
        var destruction = PatchProcessor.GetOriginalInstructions(AccessTools.Method(patchType, "Prefix"));
        check(destruction.Any(i => i.Calls(realmGuard)), "Native destruction interception consults persisted promotion guard");
        var suppression = AccessTools.Method(typeof(FactionManagerBehavior), "IsRulerRepairSuppressed");
        check(PatchProcessor.GetOriginalInstructions(suppression).Any(i => i.Calls(realmGuard)), "Ruler repair consults persisted promotion guard");
        foreach (var name in new[] { "DestroySupersededEmptyKingdomShells", "RepairClansStuckInEliminatedKingdoms", "RepairEliminatedKingdomsWithLiveHoldings" })
        {
            // LINQ predicates may be compiled into instance methods rather than the entry body.
            var methods = typeof(FactionManagerBehavior).GetMethods(System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)
                .Where(m => m.Name == name || m.Name.Contains("<" + name + ">"));
            check(methods.Any(m => PatchProcessor.GetOriginalInstructions(m).Any(i => i.Calls(suppression))),
                "Cleanup respects transition protection: " + name);
        }

        var retainedHouse = (Clan)FormatterServices.GetUninitializedObject(typeof(Clan));
        AccessTools.Property(typeof(Clan), "StringId").SetValue(retainedHouse, "retained");
        var registration = new CrownPartitionPromotionRecord { RetainedHouse = retainedHouse,
            FounderId = "heir_house", SuccessorId = "new_realm", CrownId = "crown" };
        registration.EstateTitleIds.Add("crown");
        var match = AccessTools.Method(typeof(FeudalTitleBehavior), "MatchesCrownRegistrationTitle");
        bool Matches(RealmUnionTitleRecord before, FeudalTitleRecord now, bool done = false) =>
            (bool)match.Invoke(null, new object[] { registration, before, now, done });
        var crownBefore = new RealmUnionTitleRecord { TitleId = "crown", Rank = FeudalTitleType.Kingdom,
            LegalHolderId = "retained", ActualHolderId = "retained", LegalParentId = "historical_empire",
            ActualParentId = "primary_crown", CapitalId = "", OriginRealmId = "old_realm" };
        var crownNow = new FeudalTitleRecord("crown", "Crown", FeudalTitleType.Kingdom,
            "heir_house", "heir_house", "historical_empire", "", "old_realm", 0, 0);
        crownNow.SetDeFactoParentTitle("primary_crown");
        check(Matches(crownBefore, crownNow), "Registration verifies inherited ownership against captured Crown");
        crownNow.SetAssociatedKingdom("new_realm");
        crownNow.SetDeFactoParentTitle("");
        check(Matches(crownBefore, crownNow, true) && !Matches(crownBefore, crownNow),
            "Only registered Crown may change association and political root");
        crownNow.SetParentTitle("");
        check(!Matches(crownBefore, crownNow, true), "Registration must preserve Crown legal ancestry");
        var baronyBefore = new RealmUnionTitleRecord { TitleId = "barony", Rank = FeudalTitleType.Barony,
            LegalHolderId = "foreign_house", ActualHolderId = "retained", LegalParentId = "county",
            ActualParentId = "county", CapitalId = "town", OriginRealmId = "foreign_realm" };
        var baronyNow = new FeudalTitleRecord("barony", "Town", FeudalTitleType.Barony,
            "foreign_house", "heir_house", "county", "town", "foreign_realm", 0, 0);
        registration.EstateFiefIds.Add("town");
        check(Matches(baronyBefore, baronyNow, true), "Physical estate transfer preserves foreign legal ownership and origin");
        baronyNow.SetAssociatedKingdom("new_realm");
        check(!Matches(baronyBefore, baronyNow, true), "Registration cannot naturalize descendant land into new realm");
        baronyNow.SetAssociatedKingdom("foreign_realm");
        baronyNow.SetDeJureHolder("heir_house");
        check(!Matches(baronyBefore, baronyNow, true), "Registration cannot invent legal ownership of occupied land");
        registration.EstateFiefIds.Clear();
        registration.EstateTitleIds.Add("barony");
        baronyBefore.LegalHolderId = "retained";
        baronyBefore.ActualHolderId = "foreign_house";
        baronyNow.SetDeFactoHolder("foreign_house");
        check(Matches(baronyBefore, baronyNow, true), "Legal-only inheritance does not seize foreign-controlled land");
        baronyNow.SetDeFactoHolder("heir_house");
        check(!Matches(baronyBefore, baronyNow, true), "Invented possession blocks registration verification");
        var register = AccessTools.Method(typeof(FeudalTitleBehavior), "TryRegisterCrownPromotion");
        var movement = PatchProcessor.GetOriginalInstructions(AccessTools.Method(typeof(PartitionSuccessionBehavior), "TryMoveCrownPromotionHouses"));
        check(movement.Any(i => i.Calls(register)), "House movement revalidates registered Crown before transfers");
        var verifyBatch = AccessTools.Method(typeof(PartitionSuccessionBehavior), "TryVerifyCrownBatchWorld");
        foreach (var method in new[] { "TryPrepareCrownPromotionFounder", "TryDeliverCrownPromotionEstate", "TryMoveCrownPromotionHouses" })
            check(PatchProcessor.GetOriginalInstructions(AccessTools.Method(typeof(PartitionSuccessionBehavior), method))
                .Any(i => i.Calls(verifyBatch)), "Native preparation consults shared batch state: " + method);
        check(PatchProcessor.GetOriginalInstructions(register).Any(i => i.Calls(verifyBatch)),
            "Native Crown registration validates shared physical and title state");
        var lookup = AccessTools.Method(typeof(PartitionSuccessionBehavior), "GetCrownPromotionBatch");
        check(lookup.Invoke(loaded, new object[] { journal }) == null, "Independent promotion does not acquire a batch context");
        pending.CrownBatch = new CrownPartitionBatchRecord();
        check(ReferenceEquals(lookup.Invoke(loaded, new object[] { journal }), pending)
            && lookup.Invoke(loaded, new object[] { new CrownPartitionPromotionRecord() }) == null,
            "Batch context lookup uses registered journal object identity");
        var routed = new PendingPartitionSuccessionRecord("new_dead", "royal", "realm", "", "heir", default(CampaignTime),
            true, "primary|secondary", "primary") { CrownRoutingRequested = true };
        var routingBehavior = new PartitionSuccessionBehavior();
        AccessTools.Field(typeof(PartitionSuccessionBehavior), "_pendingPartitions").SetValue(routingBehavior,
            new List<PendingPartitionSuccessionRecord> { routed });
        AccessTools.Method(typeof(PartitionSuccessionBehavior), "PrunePendingPartitions").Invoke(routingBehavior, null);
        var routedQueue = (List<PendingPartitionSuccessionRecord>)AccessTools.Field(typeof(PartitionSuccessionBehavior), "_pendingPartitions").GetValue(routingBehavior);
        check(routedQueue.Contains(routed), "Selected Crown-only estate survives pruning before any batch exists");
        check((bool)hasJournal.Invoke(null, new object[] { routed })
            && !(bool)guard.Invoke(routingBehavior, new object[] { "primary" }),
            "Routing reservation prevents legacy fallback without locking unmutated title state");
        var resume = AccessTools.Method(typeof(PartitionSuccessionBehavior), "ResumeCrownPartition");
        int resumeIndex = process.FindIndex(i => i.Calls(resume));
        check(resumeIndex >= 0 && resumeIndex < process.FindIndex(i => i.Calls(AccessTools.Method(typeof(PartitionSuccessionBehavior), "IsExpired")))
            && resumeIndex < process.FindIndex(i => i.Calls(AccessTools.Method(typeof(PartitionSuccessionBehavior), "TryApplyPartition"))),
            "Automatic Crown resume precedes legacy expiry and execution");
        var routingCode = PatchProcessor.GetOriginalInstructions(AccessTools.Method(typeof(PartitionSuccessionBehavior), "TryRouteCrownPartition"));
        var shareCapture = AccessTools.Method(typeof(PartitionSuccessionBehavior), "TryRecordPartitionEstateShares");
        var batchCapture = AccessTools.Method(typeof(PartitionSuccessionBehavior), "TryRecordCrownPartitionBatch");
        var promotionCapture = AccessTools.Method(typeof(PartitionSuccessionBehavior), "TryCaptureCrownBatchPromotions");
        var finishBatch = AccessTools.Method(typeof(PartitionSuccessionBehavior), "TryFinishCrownBatch");
        int sharesIndex = routingCode.FindIndex(i => i.Calls(shareCapture));
        int batchIndex = routingCode.FindIndex(i => i.Calls(batchCapture));
        int promotionsIndex = routingCode.FindIndex(i => i.Calls(promotionCapture));
        int finishIndex = routingCode.FindIndex(i => i.Calls(finishBatch));
        check(sharesIndex >= 0 && sharesIndex < batchIndex && batchIndex < promotionsIndex && promotionsIndex < finishIndex,
            "Automatic routing captures whole estate and batch before native finalizer");
        var killed = PatchProcessor.GetOriginalInstructions(AccessTools.Method(typeof(PartitionSuccessionBehavior), "OnHeroKilled"));
        check(killed.Any(i => i.Calls(AccessTools.Method(typeof(PartitionSuccessionBehavior), "HasSeparableCrownEstate"))),
            "Death capture checks coequal Crowns independently of personal fief surplus");
    }
}
