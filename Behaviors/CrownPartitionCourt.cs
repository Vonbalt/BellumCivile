using System;
using System.Linq;
using TaleWorlds.CampaignSystem;

namespace BellumCivile.Behaviors
{
    public partial class PartitionSuccessionBehavior
    {
        internal bool TryFinalizeCrownPromotionCourt(CrownPartitionPromotionRecord journal, out string reason)
        {
            reason = "court finalization requires a completed hierarchy transfer";
            if (!IsRegisteredCrownPromotion(journal) || journal.Completed || !journal.HierarchyStarted
                || !journal.HierarchyReturned || !journal.TryRecoverFounder(journal.Founder)
                || !journal.TryRecoverRealm(journal.Successor) || journal.Parent.IsEliminated
                || journal.Parent.RulingClan != journal.RetainedHouse
                || journal.Houses == null || journal.Houses.Count == 0
                || journal.Houses.Any(h => h?.Transfer?.Clan == null || h.Transfer.Clan.IsEliminated
                    || h.Transfer.Clan.Kingdom != journal.Successor)) return false;
            var titles = Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>();
            var council = Campaign.Current?.GetCampaignBehavior<PrivyCouncilBehavior>();
            var ideology = Campaign.Current?.GetCampaignBehavior<IdeologyBehavior>();
            var factions = Campaign.Current?.GetCampaignBehavior<FactionManagerBehavior>();
            if (titles == null || council == null || ideology == null || factions == null) return false;
            if (!titles.VerifyCrownPromotionHierarchy(journal, out reason)) return false;
            if (journal.CourtFinalizationReturned && !journal.CourtFinalizationStarted
                || journal.CourtFinalizationStarted && !journal.CourtFinalizationReturned)
            { reason = "court finalization was interrupted; membership callbacks will not be replayed"; return false; }
            try
            {
                if (!journal.CourtFinalizationStarted)
                {
                    journal.CourtFinalizationStarted = true;
                    foreach (var realm in new[] { journal.Parent, journal.Successor })
                    {
                        council.ReconcilePartitionCouncil(realm);
                        ideology.RefreshPartitionCourtMembership(realm);
                    }
                }
                if (factions.GetFactionsInKingdom(journal.Parent).Any(f =>
                    journal.Houses.Any(h => f.Members.Contains(h.Transfer.Clan))))
                    throw new InvalidOperationException("Transferred house still belongs to a primary-realm faction.");
                foreach (var realm in new[] { journal.Parent, journal.Successor })
                {
                    if (factions.GetFactionsInKingdom(realm).Where(f => f.IsIdeology)
                        .Any(f => f.Members.Any(c => !CourtMembershipEligibility.CanBelong(c, realm))))
                        throw new InvalidOperationException("Court contains an ineligible house after partition.");
                    foreach (var seat in council.GetOfficeRecords(realm))
                    {
                        if (string.IsNullOrEmpty(seat.HolderClanId)) continue;
                        var holder = council.GetOfficeHolder(realm, seat.Office);
                        if (holder == null || !NobleClanEligibilityHelper.IsLiveNobleClan(holder)
                            || holder.Kingdom != realm || holder == realm.RulingClan)
                            throw new InvalidOperationException("Council still contains an ineligible office holder.");
                    }
                }
                journal.CourtFinalizationReturned = true;
                journal.PendingReason = null;
                reason = null;
                return true;
            }
            catch (Exception ex)
            {
                reason = "court finalization failed: " + ex.Message;
                journal.PendingReason = reason;
                BellumCivileLogger.Log($"Crown partition {journal.CrownId}: {ex}");
                return false;
            }
        }
    }
}
