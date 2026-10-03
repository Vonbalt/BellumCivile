using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;

namespace BellumCivile.Behaviors
{
    public partial class PartitionSuccessionBehavior
    {
        internal bool TrySettleCrownBatchRewards(PendingPartitionSuccessionRecord pending, out string reason)
        {
            reason = "registered estate, original claims and completed beneficiaries required";
            if (pending == null || _pendingPartitions?.Contains(pending) != true || pending.OriginalClaims == null
                || !TryVerifyCrownBatchWorld(pending, out reason)
                || !PartitionEstateSettlement.TryResolveRecipients(pending, out var branches, out reason)) return false;
            var titles = Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>();
            var deceased = ResolveHero(pending.DeadLeaderId);
            var source = branches[0].Item1;
            var donor = branches[0].Item2;
            if (titles == null || deceased == null || branches.Any(b => b.Item1.IsEliminated
                || b.Item2.IsAlive != true || b.Item1.Leader != b.Item2 || b.Item2.Clan != b.Item1)
                || pending.OriginalClaims.Any(c => c == null || c.ClaimantClanId != pending.ParentClanId))
            { reason = "estate beneficiary or original claim identity changed before reward settlement"; return false; }
            if (pending.ClaimsStarted != pending.ClaimsReturned)
            { reason = "claim settlement was interrupted; rewards remain pending reconciliation"; return false; }
            var fiefs = SplitIds(pending.FiefIds).Select(ResolveTown).ToList();
            var estateTitles = SplitIds(pending.TitleIds).Select(titles.GetTitle).ToList();
            if (fiefs.Any(f => f == null) || estateTitles.Any(t => t?.IsActive != true))
            { reason = "original estate assets are no longer available for claim settlement"; return false; }
            var beneficiaries = branches.Skip(1).Select(b => b.Item2).ToList();
            if (pending.GoldPayments == null)
            {
                if (!PartitionEstateTreasury.TryCapture(donor, donor.Gold, beneficiaries, out var payments, out reason)) return false;
                pending.GoldPayments = payments;
            }
            if (pending.GoldPayments.Any(p => p == null || p.EndowmentDonor != donor)
                || pending.GoldPayments.Count != beneficiaries.Count
                || !new HashSet<Hero>(beneficiaries).SetEquals(pending.GoldPayments.Select(p => p.GoldRecipient)))
            { reason = "saved gold recipients disagree with the complete landed estate"; return false; }
            if (!TryDeliverPartitionTreasury(pending, out reason)) return false;
            if (!pending.TryApplyClaimSettlement(() => titles.RegisterPartitionHouseClaimsFromSnapshot(
                    source, deceased, branches, fiefs, estateTitles, pending.OriginalClaims), out reason)) return false;
            // Reward callbacks can affect campaign state; never infer overall completion here.
            return TryVerifyCrownBatchWorld(pending, out reason);
        }
    }
}
