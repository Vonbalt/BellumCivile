using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Settlements;

namespace BellumCivile.Behaviors
{
    public partial class PartitionSuccessionBehavior
    {
        internal CrownPartitionPromotionRecord GetCrownEstateDeliveryInProgress(string settlementId, Clan recipient)
        {
            if (recipient == null || string.IsNullOrWhiteSpace(settlementId) || _pendingPartitions == null) return null;
            return _pendingPartitions.Where(HasCrownPromotionJournal).SelectMany(p => p.CrownPromotions ?? Enumerable.Empty<CrownPartitionPromotionRecord>())
                .Where(j => j != null && !j.Completed && j.EstateDeliveryStarted && j.Founder == recipient
                    && j.EstateReceipts?.Any(r => r != null && !r.IsTitle && r.AssetId == settlementId
                        && r.Started && !r.ActionReturned) == true).SingleOrDefault();
        }
        // Still opt-in: the scheduler must not call this until the entire promotion path is ready.
        internal bool TryDeliverCrownPromotionEstate(CrownPartitionPromotionRecord journal,
            FeudalTitleBehavior titles, out string reason)
        {
            reason = "estate delivery requires a registered promotion and verified founder";
            if (journal == null || titles == null || journal.Completed || _pendingPartitions == null
                || !_pendingPartitions.Any(p => p?.CrownPromotions?.Contains(journal) == true)
                || !journal.TryRecoverFounder(journal.Founder) || journal.Parent.IsEliminated
                || journal.Parent.RulingClan != journal.RetainedHouse || journal.Founder.Kingdom != journal.Parent
                || journal.RealmCreationStarted) return false;
            if (!CanPrepareCrownPartitionDiplomacy(journal.Parent, null, out reason)) return false;
            var batch = GetCrownPromotionBatch(journal);
            if (batch != null && !TryVerifyCrownBatchWorld(batch, out reason)) return false;
            if (journal.EstateFiefIds == null || journal.EstateTitleIds == null || journal.Titles == null
                || !journal.EstateTitleIds.Contains(journal.CrownId)
                || journal.EstateTitleIds.Any(string.IsNullOrWhiteSpace) || journal.EstateFiefIds.Any(string.IsNullOrWhiteSpace)
                || journal.EstateTitleIds.Distinct().Count() != journal.EstateTitleIds.Count
                || journal.EstateFiefIds.Distinct().Count() != journal.EstateFiefIds.Count
                || journal.Titles.Any(t => t == null || string.IsNullOrWhiteSpace(t.TitleId))
                || journal.Titles.Select(t => t.TitleId).Distinct().Count() != journal.Titles.Count)
            { reason = "estate snapshot is incomplete or ambiguous"; return false; }
            var snapshots = journal.Titles.ToDictionary(t => t.TitleId, StringComparer.Ordinal);
            string source = journal.RetainedHouse.StringId;
            string target = journal.FounderId;
            foreach (string id in journal.EstateTitleIds)
            {
                if (!snapshots.TryGetValue(id, out var captured) || captured.LegalHolderId != source)
                { reason = "an estate title was not legally held by the predecessor's house"; return false; }
                if (captured.Rank == FeudalTitleType.Barony && captured.ActualHolderId == source
                    && !journal.EstateFiefIds.Contains(captured.CapitalId))
                { reason = "a controlled inherited barony is missing its physical estate entry"; return false; }
            }
            var fiefBaronies = new Dictionary<string, RealmUnionTitleRecord>(StringComparer.Ordinal);
            foreach (string id in journal.EstateFiefIds)
            {
                var matches = snapshots.Values.Where(t => t.Rank == FeudalTitleType.Barony && t.CapitalId == id).ToList();
                if (matches.Count != 1 || matches[0].ActualHolderId != source)
                { reason = "a personal fief has no unambiguous captured barony"; return false; }
                fiefBaronies.Add(id, matches[0]);
            }
            var expected = journal.EstateFiefIds.Select(id => new CrownEstateDeliveryRecord { AssetId = id })
                .Concat(journal.EstateTitleIds.OrderBy(id => snapshots[id].Rank).ThenBy(id => id, StringComparer.Ordinal)
                    .Select(id => new CrownEstateDeliveryRecord { AssetId = id, IsTitle = true })).ToList();
            if (journal.EstateReceipts == null)
            {
                if (journal.EstateDeliveryStarted || journal.EstateVerified)
                { reason = "started estate delivery is missing its per-asset receipts"; return false; }
                journal.EstateReceipts = expected;
            }
            if (journal.EstateReceipts.Count != expected.Count || journal.EstateReceipts.Any(r => r == null)
                || !journal.EstateReceipts.Select(r => Tuple.Create(r.IsTitle, r.AssetId))
                    .SequenceEqual(expected.Select(r => Tuple.Create(r.IsTitle, r.AssetId))))
            { reason = "estate receipt manifest no longer matches its snapshot"; return false; }
            bool TopologyMatches(FeudalTitleRecord title, RealmUnionTitleRecord captured) => title?.IsActive == true
                && title.TitleType == captured.Rank && title.ParentTitleId == captured.LegalParentId
                && title.DeFactoParentTitleId == captured.ActualParentId && title.AssociatedKingdomId == captured.OriginRealmId;
            bool DeliveredTitle(RealmUnionTitleRecord captured)
            {
                var current = titles.GetTitle(captured.TitleId);
                string actual = captured.ActualHolderId == source ? target : captured.ActualHolderId;
                return TopologyMatches(current, captured) && current.DeJureHolderClanId == target && current.DeFactoHolderClanId == actual;
            }
            journal.EstateDeliveryStarted = true;
            journal.EstateVerified = false;
            foreach (var receipt in journal.EstateReceipts)
            {
                bool ok;
                if (!receipt.IsTitle)
                {
                    var settlement = Settlement.Find(receipt.AssetId);
                    var captured = fiefBaronies[receipt.AssetId];
                    bool Available() => settlement?.Town != null && !FiefDeliberationBehavior.IsAwaitingAllocation(settlement)
                        && TopologyMatches(titles.GetTitle(captured.TitleId), captured);
                    ok = receipt.TryDeliver(
                        () => Available() && settlement.OwnerClan == journal.RetainedHouse,
                        () => ChangeOwnerOfSettlementAction.ApplyByDefault(journal.Heir, settlement),
                        () => Available() && settlement.OwnerClan == journal.Founder, out reason);
                }
                else
                {
                    var captured = snapshots[receipt.AssetId];
                    bool Original()
                    {
                        var title = titles.GetTitle(receipt.AssetId);
                        bool controlledFief = captured.Rank == FeudalTitleType.Barony && fiefBaronies.ContainsKey(captured.CapitalId);
                        return TopologyMatches(title, captured)
                            && (title.DeJureHolderClanId == source || controlledFief && title.DeJureHolderClanId == target)
                            && (title.DeFactoHolderClanId == captured.ActualHolderId || controlledFief && title.DeFactoHolderClanId == target);
                    }
                    ok = receipt.TryDeliver(Original,
                        () => titles.LegalizeTitleInheritance(journal.Founder, titles.GetTitle(receipt.AssetId),
                            "journaled Crown partition", preserveDeFacto: captured.ActualHolderId != source),
                        () => DeliveredTitle(captured), out reason);
                }
                if (!ok) { journal.PendingReason = receipt.AssetId + ": " + reason; return false; }
                if (batch != null && !TryVerifyCrownBatchWorld(batch, out reason))
                { journal.PendingReason = reason; return false; }
            }
            // Non-inherited legal rights over controlled fiefs must survive the ownership callback.
            foreach (var captured in fiefBaronies.Values.Where(t => !journal.EstateTitleIds.Contains(t.TitleId)))
            {
                var current = titles.GetTitle(captured.TitleId);
                if (!TopologyMatches(current, captured) || current.DeJureHolderClanId != captured.LegalHolderId
                    || current.DeFactoHolderClanId != target)
                { reason = "non-inherited legal rights changed during physical estate delivery"; journal.PendingReason = reason; return false; }
            }
            journal.EstateVerified = true;
            journal.PendingReason = null;
            reason = null;
            return true;
        }
    }
}
