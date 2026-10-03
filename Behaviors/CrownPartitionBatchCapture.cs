using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;

namespace BellumCivile.Behaviors
{
    public partial class PartitionSuccessionBehavior
    {
        private bool HasConflictingCrownBatchReservation(PendingPartitionSuccessionRecord pending,
            CrownPartitionBatchRecord candidate)
        {
            string ShellId(string crown, string founder) => "bc_partition_indep_"
                + FeudalTitleBehavior.SanitizeTitleIdSeed(crown) + "_"
                + FeudalTitleBehavior.SanitizeTitleIdSeed(founder);
            var founders = new HashSet<string>(candidate.Recipients.Values, StringComparer.Ordinal);
            var shells = new HashSet<string>(candidate.Recipients.Select(p => ShellId(p.Key, p.Value)), StringComparer.Ordinal);
            foreach (var other in _pendingPartitions.Where(p => p != null && p != pending && HasCrownPromotionJournal(p)))
            {
                if (other.ParentClanId == candidate.RetainedHouseId || other.KingdomId == candidate.SourceRealmId)
                    return true;
                if (other.CrownBatch?.Recipients != null && other.CrownBatch.Recipients.Any(p =>
                    founders.Contains(p.Value) || shells.Contains(ShellId(p.Key, p.Value)))) return true;
                if (other.CrownPromotions?.Any(p => p != null
                    && (founders.Contains(p.FounderId) || shells.Contains(p.SuccessorId))) == true) return true;
            }
            return false;
        }

        internal bool TryCaptureCrownBatchPromotions(PendingPartitionSuccessionRecord pending,
            IReadOnlyDictionary<string, Hero> heirsByCrown, out string reason)
        {
            reason = "batch promotion capture requires an untouched registered batch and all assigned heirs";
            if (pending?.CrownBatch == null || heirsByCrown == null || _pendingPartitions == null || !_pendingPartitions.Contains(pending)
                || pending.CrownPromotions?.Count > 0 || !pending.ParentWasRulingClanAtDeath) return false;
            if (pending.EstateShares == null || pending.EstateShares.Any(s => s?.Heir == null)
                || pending.EstateShares.Count(s => s.Primary) != 1)
            { reason = "Crown promotion capture requires the complete saved estate-share manifest"; return false; }
            var batch = pending.CrownBatch;
            if (batch.SourceRealmId != pending.KingdomId || batch.PredecessorId != pending.DeadLeaderId
                || batch.RetainedHouseId != pending.ParentClanId || batch.PrimaryCrownId != pending.PrimarySovereignTitleId
                || !batch.TryRestorePlan(out var plan, out reason)) return false;
            if (HasConflictingCrownBatchReservation(pending, batch))
            { reason = "another pending estate reserves this source realm, house, or successor identity"; return false; }
            if (!new HashSet<string>(batch.Recipients.Keys).SetEquals(heirsByCrown.Keys)
                || heirsByCrown.Values.Any(h => h == null) || heirsByCrown.Values.Distinct().Count() != heirsByCrown.Count)
            { reason = "Crown heirs are missing or duplicated"; return false; }
            var titles = Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>();
            var realm = ResolveKingdom(batch.SourceRealmId);
            var retained = ResolveClan(batch.RetainedHouseId);
            var predecessor = ResolveHero(batch.PredecessorId);
            if (titles == null || retained == null || realm?.RulingClan != retained || retained.Kingdom != realm
                || !BellumCivileOptions.EnablePartitionSuccession || !CanPrepareCrownPartitionDiplomacy(realm, null, out reason)) return false;
            var original = batch.Titles.ToDictionary(t => t.TitleId, StringComparer.Ordinal);
            bool Under(string id, string crown)
            {
                while (!string.IsNullOrEmpty(id))
                {
                    if (id == crown) return true;
                    id = original[id].LegalParentId;
                }
                return false;
            }
            var prepared = new List<CrownPartitionPromotionRecord>();
            var reservedShells = new HashSet<string>(StringComparer.Ordinal);
            var recordedHeirs = new HashSet<string>(SplitIds(pending.HeirIds));
            var recordedTitles = new HashSet<string>(SplitIds(pending.TitleIds));
            foreach (var assignment in batch.Recipients.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                var heir = heirsByCrown[assignment.Key];
                var crown = titles.GetTitle(assignment.Key);
                if (!recordedHeirs.Contains(heir.StringId) || !recordedTitles.Contains(assignment.Key)
                    || !IsStillEligibleSecondaryHeir(heir, retained, predecessor)
                    || BuildUniqueClanIdFromSeed(retained, assignment.Key, heir) != assignment.Value
                    || Clan.All.Any(c => c.StringId == assignment.Value))
                { reason = "recorded heir or reserved founder identity changed before batch capture"; return false; }
                var residence = titles.ResolveCrownPartitionResidence(realm, crown);
                if (residence == null) { reason = "assigned Crown has no controlled residence"; return false; }
                string successorId = "bc_partition_indep_" + FeudalTitleBehavior.SanitizeTitleIdSeed(assignment.Key)
                    + "_" + FeudalTitleBehavior.SanitizeTitleIdSeed(assignment.Value);
                if (!reservedShells.Add(successorId) || Kingdom.All.Any(k => k.StringId == successorId)
                    || _pendingPartitions.Where(HasCrownPromotionJournal)
                        .SelectMany(p => p.CrownPromotions ?? Enumerable.Empty<CrownPartitionPromotionRecord>())
                        .Any(p => p != null && (p.FounderId == assignment.Value || p.SuccessorId == successorId)))
                { reason = "Crown batch founder or shell reservation conflicts with another transition"; return false; }
                var journal = new CrownPartitionPromotionRecord
                {
                    Parent = realm, RetainedHouse = retained, Heir = heir, CrownId = assignment.Key,
                    FounderId = assignment.Value, SuccessorId = successorId, Residence = residence.Settlement,
                    CapturedAt = CampaignTime.Now, Titles = plan.CopyTitleSnapshot(),
                    EstateTitleIds = original.Values.Where(t => t.LegalHolderId == retained.StringId && Under(t.TitleId, assignment.Key))
                        .OrderBy(t => t.Rank).ThenBy(t => t.TitleId, StringComparer.Ordinal).Select(t => t.TitleId).ToList(),
                    EstateFiefIds = original.Values.Where(t => t.Rank == FeudalTitleType.Barony && t.ActualHolderId == retained.StringId
                        && Under(t.TitleId, assignment.Key)).Select(t => t.CapitalId).OrderBy(id => id, StringComparer.Ordinal).ToList(),
                    PendingReason = "captured in shared batch; execution pending"
                };
                var estateShares = pending.EstateShares.Where(s => !s.Primary && s.RootTitleId == assignment.Key).ToList();
                if (estateShares.Count != 1 || estateShares[0].Heir != heir
                    || estateShares[0].Titles == null || estateShares[0].Fiefs == null
                    || !new HashSet<string>(journal.EstateTitleIds).SetEquals(estateShares[0].Titles)
                    || !new HashSet<string>(journal.EstateFiefIds).SetEquals(estateShares[0].Fiefs))
                { reason = "Crown package disagrees with the complete estate's heir or assets"; return false; }
                foreach (string id in batch.Destinations.Where(p => p.Value == assignment.Key).Select(p => p.Key).OrderBy(id => id, StringComparer.Ordinal))
                {
                    var clan = id == assignment.Value ? null : ResolveClan(id);
                    if (id != assignment.Value && (clan == null || clan.IsEliminated || clan.Kingdom != realm || clan.IsUnderMercenaryService))
                    { reason = "assigned vassal no longer belongs to the source realm"; return false; }
                    journal.Houses.Add(new CrownPartitionHouseRecord { PrincipalTitleId = batch.Principals[id],
                        Transfer = clan == null ? new RealmUnionClanRecord() : RealmUnionSnapshotService.CaptureClan(clan) });
                }
                prepared.Add(journal);
            }
            if (!CrownPartitionBatchReconciliation.TryVerifyTitles(batch, prepared, titles.GetAllTitles(), out reason)
                || !CrownPartitionBatchHoldings.TryProject(batch, prepared, out _, out _, out reason)) return false;
            if (pending.OriginalClaims == null)
                pending.OriginalClaims = titles.GetActiveClaimsByClan(retained)
                    .OrderBy(c => c.ClaimId, StringComparer.Ordinal).Select(c => c.CopyForEstate()).ToList();
            pending.CrownPromotions = prepared;
            reason = null;
            return true;
        }

        internal bool TryRecordCrownPartitionBatch(PendingPartitionSuccessionRecord pending,
            CrownPartitionBatchPlan plan, out string reason)
        {
            reason = "batch requires an untouched registered estate and matching source identities";
            if (pending == null || plan == null || _pendingPartitions == null || !_pendingPartitions.Contains(pending)
                || pending.CrownBatch != null || pending.CrownPromotions?.Count > 0
                || pending.ParentClanId != plan.RetainedHouseId || pending.PrimarySovereignTitleId != plan.PrimaryCrownId
                || string.IsNullOrWhiteSpace(pending.KingdomId) || string.IsNullOrWhiteSpace(pending.DeadLeaderId)) return false;
            var batch = CrownPartitionBatchRecord.Capture(pending.KingdomId, pending.DeadLeaderId, plan);
            if (!batch.TryRestorePlan(out _, out reason)) return false;
            if (HasConflictingCrownBatchReservation(pending, batch))
            { reason = "another pending estate reserves this source realm, house, or successor identity"; return false; }
            pending.CrownBatch = batch;
            reason = null;
            return true;
        }
    }
}
