using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;

namespace BellumCivile.Behaviors
{
    public partial class PartitionSuccessionBehavior
    {
        internal bool TryRecordPartitionEstateShares(PendingPartitionSuccessionRecord pending,
            FeudalInheritancePlan plan, Hero primary, IReadOnlyList<Hero> secondaryHeirs, out string reason)
        {
            reason = "complete estate capture requires an untouched registered succession";
            if (pending == null || _pendingPartitions?.Contains(pending) != true || pending.EstateShares != null
                || pending.GoldPayments != null || pending.ClaimsStarted || pending.ClaimsReturned
                || pending.CrownPromotions?.Count > 0 || plan?.ParentClan == null || primary == null
                || plan.ParentClan.StringId != pending.ParentClanId || plan.DeadLeader?.StringId != pending.DeadLeaderId
                || plan.ParentClan.Leader != primary || primary.Clan != plan.ParentClan
                || plan.PrimarySovereignTitle?.TitleId != pending.PrimarySovereignTitleId
                || secondaryHeirs == null || secondaryHeirs.Any(h => h == null || !SplitIds(pending.HeirIds).Contains(h.StringId))) return false;
            if (!new HashSet<string>(SplitIds(pending.TitleIds)).SetEquals(plan.EstateTitles.Select(t => t?.TitleId))
                || !new HashSet<string>(SplitIds(pending.FiefIds)).SetEquals(plan.EstateFiefs.Select(f => f?.Settlement?.StringId)))
            { reason = "planned estate no longer matches the recorded death assets"; return false; }
            if (!PartitionEstateManifest.TryCapture(plan, primary, secondaryHeirs, out var shares, out reason)) return false;
            pending.EstateShares = shares;
            reason = null;
            return true;
        }
    }
}
