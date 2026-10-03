using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;

namespace BellumCivile.Behaviors
{
    public partial class PartitionSuccessionBehavior
    {
        internal bool TryVerifyCrownBatchWorld(PendingPartitionSuccessionRecord pending, out string reason)
        {
            reason = "registered Crown batch and promotion manifest required";
            if (pending?.CrownBatch == null || pending.CrownPromotions == null || _pendingPartitions == null
                || !_pendingPartitions.Contains(pending)) return false;
            var batch = pending.CrownBatch;
            if (batch.SourceRealmId != pending.KingdomId || batch.PredecessorId != pending.DeadLeaderId
                || batch.RetainedHouseId != pending.ParentClanId || batch.PrimaryCrownId != pending.PrimarySovereignTitleId)
                return false;
            var titles = Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>();
            if (titles == null
                || !CrownPartitionBatchReconciliation.TryProjectTitles(batch, pending.CrownPromotions, out var expectedTitles, out reason)
                || !CrownPartitionBatchHoldings.TryProject(batch, pending.CrownPromotions, out var expectedRealms, out var expectedHoldings, out reason)
                || !PartitionLesserEstateProjection.TryApply(pending, expectedTitles, expectedRealms, expectedHoldings, out reason)
                || !CrownPartitionBatchReconciliation.TryVerifyProjectedTitles(expectedTitles, titles.GetAllTitles(), out reason)) return false;
            if (pending.CrownPromotions.Any(p => p.Parent.IsEliminated || !Kingdom.All.Contains(p.Parent)
                || p.Parent.RulingClan != p.RetainedHouse
                || p.RealmCreationStarted && (!p.RealmInitializationCompleted || !p.RealmVerified
                    || p.Successor?.StringId != p.SuccessorId || p.Successor.IsEliminated
                    || !Kingdom.All.Contains(p.Successor) || p.Successor.RulingClan != p.Founder)))
            { reason = "source or successor realm no longer has its recorded ruler"; return false; }
            var realmIds = new HashSet<string>(pending.CrownPromotions.Select(p => p.SuccessorId), StringComparer.Ordinal) { batch.SourceRealmId };
            var clans = Clan.All.Where(c => c != null && !c.IsEliminated
                && (expectedHoldings.ContainsKey(c.StringId) || realmIds.Contains(c.Kingdom?.StringId)
                    && !c.IsUnderMercenaryService && !c.IsBanditFaction && !NobleClanEligibilityHelper.IsNonPlayerMinorClan(c))).ToList();
            if (clans.Select(c => c.StringId).Distinct().Count() != clans.Count)
            { reason = "duplicate native clan identity in batch participants"; return false; }
            if (clans.Any(c => c.IsUnderMercenaryService || c.IsBanditFaction || NobleClanEligibilityHelper.IsNonPlayerMinorClan(c)))
            { reason = "recorded noble house is no longer eligible for inherited allegiance"; return false; }
            var byId = clans.ToDictionary(c => c.StringId, StringComparer.Ordinal);
            if (pending.EstateShares?.Any(s => s.PartitionFounderReturned
                && (s.Recipient == null || !byId.TryGetValue(s.Recipient.StringId, out var native) || native != s.Recipient
                    || s.Heir?.IsAlive != true || s.Heir.Clan != s.Recipient || s.Recipient.Leader != s.Heir)) == true)
            { reason = "lesser estate founder identity changed"; return false; }
            if (pending.CrownPromotions.Any(p => p.Houses == null || p.Houses.Any(h => h?.Transfer == null
                || h.Transfer.Clan != null && (!byId.TryGetValue(h.Transfer.Clan.StringId, out var native) || native != h.Transfer.Clan))
                || p.FounderCreationStarted && (p.Heir == null || p.Heir.IsDead || p.Founder?.Leader != p.Heir || p.Heir.Clan != p.Founder)))
            { reason = "recorded house or founder identity no longer matches the native campaign"; return false; }
            var realms = clans.ToDictionary(c => c.StringId, c => c.Kingdom?.StringId, StringComparer.Ordinal);
            var holdings = clans.ToDictionary(c => c.StringId,
                c => c.Settlements.Select(s => s.StringId).ToArray(), StringComparer.Ordinal);
            var owners = holdings.Values.SelectMany(ids => ids).Distinct().ToDictionary(id => id,
                id => Settlement.Find(id)?.OwnerClan?.StringId, StringComparer.Ordinal);
            return CrownPartitionBatchHoldings.TryVerifyProjected(expectedRealms, expectedHoldings, realms, holdings, owners, out reason);
        }
    }
}
