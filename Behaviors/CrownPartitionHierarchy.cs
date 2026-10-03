using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;

namespace BellumCivile.Behaviors
{
    public partial class FeudalTitleBehavior
    {
        internal bool TryFinalizeCrownPromotionHierarchy(CrownPartitionPromotionRecord journal, out string reason)
        {
            reason = "hierarchy finalization requires a registered promotion";
            var partitions = Campaign.Current?.GetCampaignBehavior<PartitionSuccessionBehavior>();
            if (journal == null || journal.Completed || partitions?.IsRegisteredCrownPromotion(journal) != true
                || !journal.TryRecoverFounder(journal.Founder) || !journal.TryRecoverRealm(journal.Successor)
                || journal.Parent.IsEliminated || journal.Parent.RulingClan != journal.RetainedHouse
                || !journal.CrownRegistrationStarted || !journal.CrownRegistrationReturned) return false;
            if (journal.HierarchyReturned && !journal.HierarchyStarted || journal.HierarchyStarted && !journal.HierarchyReturned)
            { reason = "hierarchy finalization was interrupted or has inconsistent receipts"; return false; }
            if (journal.HierarchyReturned)
                return VerifyCrownPromotionHierarchy(journal, out reason);
            if (!partitions.TryMoveCrownPromotionHouses(journal, out reason)) return false;
            // All movement must be finished before resolving cross-border service parents.
            if (journal.Houses.Any(h => h.Transfer.Clan.Kingdom != journal.Successor))
            { reason = "not all recorded houses have joined the successor"; return false; }
            var targets = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var captured in journal.Titles)
            {
                var current = GetTitle(captured.TitleId);
                var realm = ResolveClan(current.DeFactoHolderClanId)?.Kingdom;
                string parent = current.DeFactoParentTitleId;
                if (realm == journal.Parent || realm == journal.Successor)
                    parent = ResolveBestDeFactoParent(current, realm)?.TitleId ?? string.Empty;
                if (current.TitleId == journal.CrownId) parent = string.Empty;
                if (!string.IsNullOrEmpty(parent) && (GetTitle(parent)?.IsActive != true
                    || GetTitle(parent).TitleType <= current.TitleType))
                { reason = "resolved political parent is missing or not higher ranked"; return false; }
                targets.Add(current.TitleId, parent);
            }
            journal.PoliticalParentTargets = targets;
            try
            {
                journal.HierarchyStarted = true;
                foreach (var target in targets)
                {
                    var title = GetTitle(target.Key);
                    if (title.DeFactoParentTitleId == target.Value) continue;
                    title.SetDeFactoParentTitle(target.Value);
                    title.MarkSynced(CurrentDay);
                    QueueServiceReview(title, "journaled Crown partition hierarchy");
                }
                RebuildRuntimeIndexes();
                if (!VerifyCrownPromotionHierarchy(journal, out reason))
                    throw new InvalidOperationException(reason);
                journal.HierarchyReturned = true;
                journal.PendingReason = null;
                return true;
            }
            catch (Exception ex)
            {
                journal.HierarchyVerified = false;
                reason = "hierarchy finalization interrupted: " + ex.Message;
                journal.PendingReason = reason;
                BellumCivileLogger.Log($"Crown partition {journal.CrownId}: {ex}");
                return false;
            }
        }

        internal bool VerifyCrownPromotionHierarchy(CrownPartitionPromotionRecord journal, out string reason)
        {
            reason = "final political hierarchy or preserved legal title state changed";
            if (journal == null) return false;
            journal.HierarchyVerified = false;
            if (string.IsNullOrWhiteSpace(journal.CrownId) || !journal.CrownRegistrationReturned
                || !journal.HierarchyStarted || journal.Titles == null || journal.Titles.Count == 0 || journal.PoliticalParentTargets == null
                || journal.Titles.Any(t => t == null)
                || journal.Titles.Count != journal.PoliticalParentTargets.Count
                || journal.Titles.Select(t => t.TitleId).Distinct().Count() != journal.Titles.Count
                || GetIndependentRealmSovereignTitle(journal.Successor)?.TitleId != journal.CrownId) return false;
            foreach (var captured in journal.Titles)
            {
                if (!journal.PoliticalParentTargets.TryGetValue(captured.TitleId, out string parent)
                    || !MatchesCrownPromotionTitle(journal, captured, GetTitle(captured.TitleId), true, parent)) return false;
                if (!string.IsNullOrEmpty(parent) && (GetTitle(parent)?.IsActive != true
                    || GetTitle(parent).TitleType <= captured.Rank)) return false;
            }
            journal.HierarchyVerified = true;
            reason = null;
            return true;
        }
    }
}
