using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;

namespace BellumCivile
{
    internal static class PartitionEstateSettlement
    {
        internal static bool TryResolveRecipients(PendingPartitionSuccessionRecord pending,
            out List<Tuple<Clan, Hero>> branches, out string reason)
        {
            branches = null;
            reason = "complete landed estate and court receipts required before rewards";
            var batch = pending?.CrownBatch;
            if (batch == null || !batch.HierarchyVerified || !batch.CourtFinalizationStarted || !batch.CourtFinalizationReturned
                || pending.EstateShares == null || pending.CrownPromotions == null
                || pending.EstateShares.Any(s => s?.Heir == null || s.Titles == null || s.Fiefs == null)
                || pending.EstateShares.Count(s => s.Primary) != 1) return false;
            var titleIds = pending.EstateShares.SelectMany(s => s.Titles).ToList();
            var fiefIds = pending.EstateShares.SelectMany(s => s.Fiefs).ToList();
            string[] Split(string ids) => (ids ?? "").Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries);
            if (titleIds.Distinct().Count() != titleIds.Count || fiefIds.Distinct().Count() != fiefIds.Count
                || !new HashSet<string>(Split(pending.TitleIds)).SetEquals(titleIds)
                || !new HashSet<string>(Split(pending.FiefIds)).SetEquals(fiefIds))
            { reason = "settlement shares do not cover the recorded estate exactly once"; return false; }
            var resolved = new List<Tuple<Clan, Hero>>();
            var seenHeirs = new HashSet<string>(StringComparer.Ordinal);
            var seenHouses = new HashSet<string>(StringComparer.Ordinal);
            var seenCrowns = new HashSet<string>(StringComparer.Ordinal);
            foreach (var share in pending.EstateShares.OrderByDescending(s => s.Primary))
            {
                Clan recipient;
                if (share.Primary)
                {
                    recipient = share.Recipient;
                    if (recipient?.StringId != pending.ParentClanId || !share.Titles.Contains(batch.PrimaryCrownId)) return false;
                }
                else if (batch.Recipients.TryGetValue(share.RootTitleId ?? "", out string founderId))
                {
                    var matches = pending.CrownPromotions.Where(p => p != null && p.CrownId == share.RootTitleId).ToList();
                    if (matches.Count != 1 || !seenCrowns.Add(share.RootTitleId)) return false;
                    var promotion = matches[0];
                    recipient = promotion.Founder;
                    if (promotion.Heir != share.Heir || recipient?.StringId != founderId
                        || !promotion.HasVerifiedEstateReceipts() || !promotion.CrownRegistrationReturned
                        || !new HashSet<string>(share.Titles).SetEquals(promotion.EstateTitleIds)
                        || !new HashSet<string>(share.Fiefs).SetEquals(promotion.EstateFiefIds)) return false;
                }
                else
                {
                    recipient = share.Recipient;
                    if (!share.LandedSettled || !share.PartitionFounderStarted || !share.PartitionFounderReturned
                        || share.PartitionReceipts == null || share.PartitionReceipts.Count != share.Titles.Count + share.Fiefs.Count
                        || share.PartitionReceipts.Any(r => r == null || !r.Started || !r.ActionReturned || !r.Verified)
                        || !new HashSet<string>(share.Titles).SetEquals(share.PartitionReceipts.Where(r => r.IsTitle).Select(r => r.AssetId))
                        || !new HashSet<string>(share.Fiefs).SetEquals(share.PartitionReceipts.Where(r => !r.IsTitle).Select(r => r.AssetId))) return false;
                }
                if (recipient == null || string.IsNullOrWhiteSpace(recipient.StringId) || string.IsNullOrWhiteSpace(share.Heir.StringId)
                    || !seenHouses.Add(recipient.StringId) || !seenHeirs.Add(share.Heir.StringId)) return false;
                resolved.Add(Tuple.Create(recipient, share.Heir));
            }
            if (!seenCrowns.SetEquals(batch.Recipients.Keys) || resolved.Count < 2) return false;
            branches = resolved;
            reason = null;
            return true;
        }
    }
}
