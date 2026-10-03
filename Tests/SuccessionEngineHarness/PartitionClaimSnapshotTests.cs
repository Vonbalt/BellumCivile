using System;
using System.Collections.Generic;
using BellumCivile;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;

internal static class PartitionClaimSnapshotTests
{
    internal static void Run(Action<bool, string> check)
    {
        var live = new FeudalClaimRecord("claim", "house", "title", FeudalClaimStrength.Strong,
            "source", "deceased", "origin", 12, 400, 2, "carrier");
        var snapshot = (FeudalClaimRecord)AccessTools.Method(typeof(FeudalClaimRecord), "CopyForEstate").Invoke(live, null);
        check(!ReferenceEquals(live, snapshot) && snapshot.ClaimId == live.ClaimId
            && snapshot.ClaimantClanId == live.ClaimantClanId && snapshot.TargetTitleId == live.TargetTitleId
            && snapshot.Source == live.Source && snapshot.SourceHeroId == live.SourceHeroId
            && snapshot.OriginClanId == live.OriginClanId && snapshot.CreatedDay == 12
            && snapshot.ExpiresDay == 400 && snapshot.GenerationDepth == 2 && snapshot.CarrierHeroId == "carrier",
            "Claim estate snapshot preserves identity, provenance, carrier, expiry and generation");
        live.SetActive(false); live.ReplaceCarrier(null);
        check(snapshot.IsActive && snapshot.CarrierHeroId == "carrier", "Live claim mutation cannot rewrite the saved estate");
        snapshot.SetActive(false);
        check(live.CarrierHeroId == "", "Snapshot has independent mutable state");
        var inherit = AccessTools.Method(typeof(FeudalTitleBehavior), "GetInheritedClaimStrength");
        check((FeudalClaimStrength?)inherit.Invoke(null, new object[] { snapshot }) == FeudalClaimStrength.Weak,
            "Captured strong claim still degrades to weak on inheritance");
        var weak = new FeudalClaimRecord("weak", "house", "title", FeudalClaimStrength.Weak, "fabricated", "", "", 0, -1);
        check(inherit.Invoke(null, new object[] { weak }) == null, "Weak claims remain non-inheritable");
        var pending = new PendingPartitionSuccessionRecord("dead", "house", "realm", "", "", default(CampaignTime));
        var settle = AccessTools.Method(typeof(PendingPartitionSuccessionRecord), "TryApplyClaimSettlement");
        int calls = 0;
        bool Apply(Action action) => (bool)settle.Invoke(pending, new object[] { action, null });
        check(!Apply(() => calls++) && calls == 0, "Absent original claims cannot silently use current claims");
        pending.OriginalClaims = new List<FeudalClaimRecord>();
        check(Apply(() => calls++) && Apply(() => calls++) && calls == 1,
            "Empty captured estate is valid and completed claim settlement runs once");
        pending.ClaimsStarted = pending.ClaimsReturned = false;
        check(!Apply(() => { calls++; throw new InvalidOperationException("callback"); })
            && !Apply(() => calls++) && calls == 2 && pending.ClaimsStarted && !pending.ClaimsReturned,
            "Interrupted claim settlement blocks replay of partially applied rewards");
        pending.ClaimsStarted = false; pending.ClaimsReturned = true;
        check(!Apply(() => calls++) && calls == 2, "Contradictory claim receipts block callbacks");
        var fromSnapshot = AccessTools.Method(typeof(FeudalTitleBehavior), "RegisterPartitionHouseClaimsFromSnapshot");
        bool rejected = false;
        try { fromSnapshot.Invoke(new FeudalTitleBehavior(), new object[] { null, null, null, null, null, null }); }
        catch (System.Reflection.TargetInvocationException ex) { rejected = ex.InnerException is ArgumentNullException; }
        check(rejected, "Snapshot registration refuses missing snapshots instead of falling back to live claims");
    }
}
