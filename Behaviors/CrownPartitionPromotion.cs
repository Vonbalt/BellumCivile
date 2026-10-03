using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;

namespace BellumCivile.Behaviors
{
    public partial class PartitionSuccessionBehavior
    {
        internal static bool HasCrownPromotionJournal(PendingPartitionSuccessionRecord record)
            => record?.CrownRoutingRequested == true || record?.CrownBatch != null || record?.CrownPromotions?.Count > 0;

        internal bool IsRegisteredCrownPromotion(CrownPartitionPromotionRecord journal)
            => journal != null && _pendingPartitions?.Any(p => p?.CrownPromotions?.Contains(journal) == true) == true;

        internal PendingPartitionSuccessionRecord GetCrownPromotionBatch(CrownPartitionPromotionRecord journal)
            => journal == null ? null : _pendingPartitions?.FirstOrDefault(p => p?.CrownBatch != null
                && p.CrownPromotions?.Contains(journal) == true);

        internal bool IsCrownPromotionRealmProtected(Kingdom realm)
        {
            if (realm == null || _pendingPartitions == null) return false;
            foreach (var pending in _pendingPartitions)
            {
                if (pending?.CrownPromotions == null) continue;
                foreach (var journal in pending.CrownPromotions)
                {
                    if (journal == null || journal.Completed || !journal.FounderCreationStarted) continue;
                    if (journal.Parent == realm) return true;
                    // Creation callbacks can run before the newly created object is returned.
                    if (journal.RealmCreationStarted && !string.IsNullOrWhiteSpace(journal.SuccessorId)
                        && realm.StringId == journal.SuccessorId
                        && (journal.Successor == null || journal.Successor == realm)) return true;
                }
            }
            return false;
        }

        internal bool IsCrownPromotionTitleProtected(string titleId)
        {
            if (string.IsNullOrWhiteSpace(titleId) || _pendingPartitions == null) return false;
            return _pendingPartitions.Where(HasCrownPromotionJournal).SelectMany(r => r.CrownPromotions ?? Enumerable.Empty<CrownPartitionPromotionRecord>())
                .Any(r => r != null && !r.Completed && r.FounderCreationStarted
                    && (r.CrownId == titleId || r.Titles?.Any(t => t?.TitleId == titleId) == true));
        }

        internal bool TryPrepareCrownPromotionFounder(CrownPartitionPromotionRecord journal,
            FeudalTitleBehavior titles, out string reason)
        {
            reason = "promotion journal is not registered or has invalid participants";
            if (journal == null || titles == null || journal.Completed || _pendingPartitions == null) return false;
            var pending = _pendingPartitions.FirstOrDefault(r => r?.CrownPromotions?.Contains(journal) == true);
            if (pending == null || journal.Parent == null || journal.RetainedHouse == null || journal.Heir == null
                || string.IsNullOrWhiteSpace(journal.FounderId)) return false;
            var placeholders = journal.Houses?.Where(h => h?.Transfer != null && h.Transfer.Clan == null).ToList();
            var existingReceipts = journal.Houses?.Where(h => h?.Transfer?.Clan?.StringId == journal.FounderId).ToList();
            if (placeholders == null || existingReceipts == null
                || placeholders.Count + existingReceipts.Count != 1)
            { reason = "founder transfer receipt is missing or ambiguous"; return false; }
            var existing = Clan.All.Where(c => c.StringId == journal.FounderId).ToList();
            if (journal.FounderCreationStarted)
            {
                if (existing.Count != 1 || !journal.TryRecoverFounder(existing[0]))
                { reason = "founder creation is partial or inconsistent; native initialization will not be replayed"; return false; }
                if (placeholders.Count == 1) placeholders[0].Transfer = RealmUnionSnapshotService.CaptureClan(journal.Founder);
                reason = null;
                return true;
            }
            if (placeholders.Count != 1 || journal.Founder != null) return false;
            if (!CanPrepareCrownPartitionDiplomacy(journal.Parent, null, out reason)) return false;
            if (existing.Count != 0 || journal.Parent.IsEliminated || journal.Parent.RulingClan != journal.RetainedHouse
                || !IsStillEligibleSecondaryHeir(journal.Heir, journal.RetainedHouse, ResolveHero(pending.DeadLeaderId))) return false;
            var crown = titles.GetTitle(journal.CrownId);
            var residence = titles.ResolveCrownPartitionResidence(journal.Parent, crown);
            if (residence?.Settlement != journal.Residence || residence == null
                || journal.Titles == null || journal.Titles.Count == 0) return false;
            if (pending.CrownBatch != null && !TryVerifyCrownBatchWorld(pending, out reason)) return false;
            foreach (var captured in pending.CrownBatch == null ? journal.Titles : Enumerable.Empty<RealmUnionTitleRecord>())
            {
                var current = captured == null ? null : titles.GetTitle(captured.TitleId);
                if (current?.IsActive != true || current.DeJureHolderClanId != captured.LegalHolderId
                    || current.DeFactoHolderClanId != captured.ActualHolderId || current.ParentTitleId != captured.LegalParentId
                    || current.DeFactoParentTitleId != captured.ActualParentId || current.AssociatedKingdomId != captured.OriginRealmId)
                { reason = "captured title state changed before founder creation"; return false; }
            }
            foreach (var house in journal.Houses.Where(h => h?.Transfer?.Clan != null))
            {
                var captured = house.Transfer;
                var current = captured.Clan;
                if (current.IsEliminated || current.Kingdom != journal.Parent || current.IsUnderMercenaryService
                    || current.Influence != captured.Influence || current.DebtToKingdom != captured.Debt
                    || captured.Holdings == null || !new HashSet<string>(captured.Holdings)
                        .SetEquals(current.Settlements.Select(s => s.StringId)))
                { reason = "captured house state changed before founder creation"; return false; }
            }
            if (!journal.TryBeginFounderCreation()) return false;
            try
            {
                var created = CreatePartitionCadetAtResidence(journal.RetainedHouse, journal.Parent,
                    journal.Heir, residence, journal.CrownId, true, journal);
                if (journal.TryRecoverFounder(created))
                {
                    placeholders[0].Transfer = RealmUnionSnapshotService.CaptureClan(created);
                    reason = null;
                    return true;
                }
                reason = "founder initialization did not verify";
            }
            catch (Exception ex)
            {
                reason = "founder initialization interrupted: " + ex.Message;
                BellumCivileLogger.Log($"Crown partition {journal.CrownId}: {ex}");
            }
            journal.PendingReason = reason;
            return false;
        }

        // Capture/publication only. No current live caller starts these promotions.
        internal bool TryCaptureCrownPromotion(PendingPartitionSuccessionRecord pending, Hero heir,
            FeudalInheritancePackage package, FeudalTitleBehavior titles,
            out CrownPartitionPromotionRecord journal, out string reason)
        {
            journal = null;
            reason = "missing registered succession, eligible heir or Crown package";
            if (pending == null || _pendingPartitions == null || !_pendingPartitions.Contains(pending) || heir == null || titles == null
                || package?.RootTitle == null || !pending.ParentWasRulingClanAtDeath) return false;
            if (pending.CrownBatch != null)
            { reason = "saved batch requires shared package capture rather than independent Crown capture"; return false; }
            Clan parent = ResolveClan(pending.ParentClanId);
            Kingdom realm = ResolveKingdom(pending.KingdomId);
            Hero predecessor = ResolveHero(pending.DeadLeaderId);
            var crown = package.RootTitle;
            var primary = titles.GetTitle(pending.PrimarySovereignTitleId);
            if (realm == null || realm.IsEliminated || parent == null || realm.RulingClan != parent
                || parent.Kingdom != realm || parent.IsEliminated || parent.IsUnderMercenaryService
                || !BellumCivileOptions.EnablePartitionSuccession
                || SuccessionRealmRules.Classify(SuccessionLawHelper.GetLawsForKingdom(realm).SuccessionLaw) != RealmSuccessionSystem.Hereditary
                || BellumKingdomVisibilityHelper.IsTemporaryBellumKingdom(realm)
                || !IsStillEligibleSecondaryHeir(heir, parent, predecessor)
                || primary?.IsActive != true || crown.TitleType != primary.TitleType
                || crown.TitleType < FeudalTitleType.Kingdom || crown.TitleId == primary.TitleId
                || !crown.IsActive || crown.DeJureHolderClanId != parent.StringId || crown.DeFactoHolderClanId != parent.StringId)
                return false;
            if (!SplitIds(pending.HeirIds).Contains(heir.StringId) || !SplitIds(pending.TitleIds).Contains(crown.TitleId))
            { reason = "heir or Crown is absent from the recorded estate"; return false; }
            if (pending.CrownPromotions?.Any(r => r == null || r.CrownId == crown.TitleId || r.Heir == heir) == true)
            { reason = "this heir or Crown already has a promotion journal"; return false; }
            if (!CanPrepareCrownPartitionDiplomacy(realm, null, out reason)) return false;
            var allTitles = titles.GetAllTitles().ToList();
            if (!FeudalCrownPartitionPlan.TryCreate(parent.StringId, primary.TitleId, allTitles, out var crowns, out reason)
                || !crowns.Any(c => c.CrownId == crown.TitleId))
            { reason = reason ?? "Crown is bound to the retained sovereign package"; return false; }
            var residence = titles.ResolveCrownPartitionResidence(realm, crown);
            if (residence == null) { reason = "Crown has no controlled residence"; return false; }
            var scope = new HashSet<string>(crowns.Single(c => c.CrownId == crown.TitleId).TitleIds, StringComparer.Ordinal);
            if (package.Titles.Any(t => t == null || !scope.Contains(t.TitleId) || t.DeJureHolderClanId != parent.StringId)
                || package.Titles.All(t => t.TitleId != crown.TitleId)
                || package.Fiefs.Any(f => !FeudalInheritancePlanner.IsHeritableFief(f, parent)
                    || !allTitles.Any(t => scope.Contains(t.TitleId) && t.TitleType == FeudalTitleType.Barony
                        && t.CapitalSettlementId == f.Settlement.StringId)))
            { reason = "Crown estate changed before capture"; return false; }
            string founderId = BuildUniqueClanIdFromSeed(parent, crown.TitleId, heir);
            string successorId = "bc_partition_indep_" + FeudalTitleBehavior.SanitizeTitleIdSeed(crown.TitleId)
                + "_" + FeudalTitleBehavior.SanitizeTitleIdSeed(founderId);
            if (Kingdom.All.Any(k => k.StringId == successorId)
                || _pendingPartitions.Where(HasCrownPromotionJournal).SelectMany(r => r.CrownPromotions ?? Enumerable.Empty<CrownPartitionPromotionRecord>())
                    .Any(r => r != null && (r.FounderId == founderId || r.SuccessorId == successorId)))
            { reason = "reserved successor identity already exists"; return false; }
            var houses = realm.Clans.Where(c => c != null && !c.IsEliminated && !c.IsUnderMercenaryService
                && !c.IsBanditFaction && !NobleClanEligibilityHelper.IsNonPlayerMinorClan(c)).ToDictionary(c => c.StringId);
            var holdings = houses.ToDictionary(p => p.Key, p => p.Value.Settlements
                .Where(s => s?.Town != null && !FiefDeliberationBehavior.IsAwaitingAllocation(s)).Select(s => s.StringId).ToArray());
            holdings.Add(founderId, new string[0]);
            if (!CrownPartitionAllegiancePlan.TryCreate(crown.TitleId, parent.StringId, founderId,
                holdings, allTitles, out var allegiance, out reason)) return false;
            var captured = new CrownPartitionPromotionRecord
            {
                Parent = realm, RetainedHouse = parent, Heir = heir, CrownId = crown.TitleId,
                FounderId = founderId, SuccessorId = successorId, Residence = residence.Settlement,
                CapturedAt = CampaignTime.Now,
                EstateFiefIds = package.Fiefs.Select(f => f.Settlement.StringId).ToList(),
                EstateTitleIds = package.Titles.Select(t => t.TitleId).ToList(),
                PendingReason = "captured; promotion execution is not enabled"
            };
            foreach (string id in allegiance.MovingHouses)
                captured.Houses.Add(new CrownPartitionHouseRecord
                {
                    PrincipalTitleId = allegiance.PrincipalTitles[id],
                    Transfer = id == founderId ? new RealmUnionClanRecord() : RealmUnionSnapshotService.CaptureClan(houses[id])
                });
            var involvedHouses = new HashSet<string>(allegiance.MovingHouses) { parent.StringId };
            var affected = new HashSet<string>(allTitles.Where(t => t.IsActive && (scope.Contains(t.TitleId)
                || involvedHouses.Contains(t.DeJureHolderClanId) || involvedHouses.Contains(t.DeFactoHolderClanId)))
                .Select(t => t.TitleId), StringComparer.Ordinal);
            ExpandCrownPromotionDescendants(affected, allTitles);
            captured.Titles = allTitles.Where(t => t.IsActive && affected.Contains(t.TitleId))
                .Select(RealmUnionTitleRecord.Capture).ToList();
            if (pending.CrownPromotions == null) pending.CrownPromotions = new List<CrownPartitionPromotionRecord>();
            pending.CrownPromotions.Add(captured);
            journal = captured;
            reason = null;
            return true;
        }

        internal static void ExpandCrownPromotionDescendants(HashSet<string> affected, System.Collections.Generic.IEnumerable<FeudalTitleRecord> titles)
        {
            var children = titles.Where(t => t?.IsActive == true)
                .SelectMany(t => new[] { Tuple.Create(t.ParentTitleId, t.TitleId), Tuple.Create(t.DeFactoParentTitleId, t.TitleId) })
                .Where(p => !string.IsNullOrWhiteSpace(p.Item1)).ToLookup(p => p.Item1, p => p.Item2, StringComparer.Ordinal);
            var queue = new Queue<string>(affected);
            while (queue.Count > 0)
                foreach (var child in children[queue.Dequeue()])
                    if (affected.Add(child)) queue.Enqueue(child);
        }
    }
}
