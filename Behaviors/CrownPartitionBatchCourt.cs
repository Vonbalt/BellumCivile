using System;
using System.Linq;
using TaleWorlds.CampaignSystem;

namespace BellumCivile.Behaviors
{
    public partial class PartitionSuccessionBehavior
    {
        internal bool TryFinalizeCrownBatchCourt(PendingPartitionSuccessionRecord pending, out string reason)
        {
            reason = "shared court finalization requires a verified batch hierarchy";
            var batch = pending?.CrownBatch;
            if (batch == null || !batch.HierarchyStarted || !batch.HierarchyReturned || !batch.HierarchyVerified)
                return false;
            if (batch.CourtFinalizationStarted != batch.CourtFinalizationReturned)
            { reason = "shared court finalization was interrupted; membership callbacks will not be replayed"; return false; }
            if (!TryVerifyCrownBatchWorld(pending, out reason)) return false;
            var council = Campaign.Current?.GetCampaignBehavior<PrivyCouncilBehavior>();
            var ideology = Campaign.Current?.GetCampaignBehavior<IdeologyBehavior>();
            var factions = Campaign.Current?.GetCampaignBehavior<FactionManagerBehavior>();
            if (council == null || ideology == null || factions == null)
            { reason = "shared court services unavailable"; return false; }
            var realms = pending.CrownPromotions.Select(p => p.Successor)
                .Concat(pending.CrownPromotions.Select(p => p.Parent)).Distinct()
                .OrderBy(k => k.StringId, StringComparer.Ordinal).ToList();
            try
            {
                if (!batch.CourtFinalizationStarted)
                {
                    batch.CourtFinalizationStarted = true;
                    foreach (var realm in realms)
                    {
                        council.ReconcilePartitionCouncil(realm);
                        ideology.RefreshPartitionCourtMembership(realm);
                    }
                }
                foreach (var realm in realms)
                {
                    foreach (var faction in factions.GetFactionsInKingdom(realm))
                    {
                        if (faction.Members.Any(c => c == null || batch.Destinations.ContainsKey(c.StringId) && c.Kingdom != realm)
                            || faction.IsIdeology && faction.Members.Any(c => !CourtMembershipEligibility.CanBelong(c, realm)))
                            throw new InvalidOperationException("Court retains a house belonging to another realm or an ineligible member.");
                    }
                    foreach (var seat in council.GetOfficeRecords(realm))
                    {
                        if (string.IsNullOrEmpty(seat.HolderClanId)) continue;
                        var holder = council.GetOfficeHolder(realm, seat.Office);
                        if (holder == null || !NobleClanEligibilityHelper.IsLiveNobleClan(holder)
                            || holder.Kingdom != realm || holder == realm.RulingClan)
                            throw new InvalidOperationException("Council retains an ineligible office holder.");
                    }
                }
                if (!TryVerifyCrownBatchWorld(pending, out reason)) throw new InvalidOperationException(reason);
                batch.CourtFinalizationReturned = true;
                reason = null;
                return true;
            }
            catch (Exception ex)
            {
                reason = "shared court finalization failed: " + ex.Message;
                BellumCivileLogger.Log($"Crown batch {batch.SourceRealmId}: {ex}");
                return false;
            }
        }
    }
}
