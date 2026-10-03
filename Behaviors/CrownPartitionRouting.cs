using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;

namespace BellumCivile.Behaviors
{
    public partial class PartitionSuccessionBehavior
    {
        private static bool HasSeparableCrownEstate(Clan source, FeudalTitleBehavior titles, FeudalTitleRecord primary)
        {
            if (titles == null || primary == null || source?.Kingdom?.RulingClan != source
                || SuccessionRealmRules.Classify(SuccessionLawHelper.GetLawsForKingdom(source.Kingdom).SuccessionLaw)
                    != RealmSuccessionSystem.Hereditary) return false;
            return FeudalCrownPartitionPlan.TryCreate(source.StringId, primary.TitleId, titles.GetAllTitles().ToList(),
                out var crowns, out _) && crowns.Count > 1;
        }

        private void ResumeCrownPartition(PendingPartitionSuccessionRecord pending)
        {
            string reason;
            try
            {
                if (TryRouteCrownPartition(pending, out reason)) return;
            }
            catch (Exception ex)
            {
                reason = "Crown routing interrupted: " + ex.Message;
                if (pending.CrownRoutingFailure != reason) BellumCivileLogger.Log($"Crown partition {pending.ParentClanId}: {ex}");
            }
            reason = reason ?? "Crown transition has not met its preparation conditions";
            if (pending.CrownRoutingFailure != reason)
                BellumCivileLogger.Log($"Crown partition deferred; house={pending.ParentClanId}; predecessor={pending.DeadLeaderId}; reason={reason}.");
            pending.CrownRoutingFailure = reason;
            pending.ReadyDate = CampaignTime.Now + CampaignTime.Days(1f);
        }

        private bool TryRouteCrownPartition(PendingPartitionSuccessionRecord pending, out string reason)
        {
            reason = "Crown estate is waiting for a stable hereditary ruling house";
            if (_pendingPartitions?.Contains(pending) != true) return false;
            var source = ResolveClan(pending.ParentClanId);
            var realm = ResolveKingdom(pending.KingdomId);
            var deceased = ResolveHero(pending.DeadLeaderId);
            var titles = Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>();
            if (titles == null || !CanPartitionClan(source) || realm?.RulingClan != source || source.Kingdom != realm
                || source.Leader?.IsAlive != true || source.Leader == deceased || deceased == null
                || RegencyBehavior.Instance?.TryGetRegency(source, out _) == true) return false;
            if (!CanPrepareCrownPartitionDiplomacy(realm, null, out reason)) return false;
            if (pending.CrownBatch == null)
            {
                if (pending.CrownPromotions?.Count > 0 || pending.EstateShares != null)
                { reason = "pre-existing partial estate requires reconciliation before automatic batch capture"; return false; }
                var heirs = SplitIds(pending.HeirIds).Select(ResolveHero)
                    .Where(h => IsStillEligibleSecondaryHeir(h, source, deceased)).Distinct().ToList();
                if (heirs.Count == 0)
                {
                    _pendingPartitions.Remove(pending);
                    reason = null;
                    return true;
                }
                var fiefs = SplitIds(pending.FiefIds).Select(ResolveTown).ToList();
                var estateTitles = SplitIds(pending.TitleIds).Select(titles.GetTitle).ToList();
                if (fiefs.Any(f => f?.Settlement == null || !FeudalInheritancePlanner.IsHeritableFief(f, source))
                    || estateTitles.Any(t => t?.IsActive != true || t.DeJureHolderClanId != source.StringId))
                { reason = "death estate ownership changed before Crown batch capture"; return false; }
                var plan = FeudalInheritancePlanner.BuildPlan(source, deceased, fiefs, heirs, estateTitles,
                    GetMainHeirReservedFiefs(), pending.PrimarySovereignTitleId);
                if (fiefs.Count == 0) plan.FailureReason = null;
                var allTitles = titles.GetAllTitles().ToList();
                if (!plan.IsValid || !FeudalInheritancePlanner.TryBuildCrownFirstTitlePackages(plan, titles, allTitles,
                    GetMainHeirReservedFiefs(), pending.PrimarySovereignTitleId, out reason)) return false;
                var assignedCrowns = plan.SecondaryPackages.Take(heirs.Count).Select((p, i) => new { Package = p, Heir = heirs[i] })
                    .Where(p => p.Package.RootTitle?.TitleType >= FeudalTitleType.Kingdom).ToList();
                if (assignedCrowns.Count == 0)
                { reason = "recorded Crown estate no longer has an assignable separating Crown"; return false; }
                var recipients = assignedCrowns.ToDictionary(p => p.Package.RootTitle.TitleId,
                    p => BuildUniqueClanIdFromSeed(source, p.Package.RootTitle.TitleId, p.Heir));
                var holdings = realm.Clans.Where(c => c != null && !c.IsEliminated && !c.IsUnderMercenaryService
                    && !c.IsBanditFaction && !NobleClanEligibilityHelper.IsNonPlayerMinorClan(c))
                    .ToDictionary(c => c.StringId, c => c.Settlements.Select(s => s.StringId).ToArray());
                if (!CrownPartitionBatchPlan.TryCreate(source.StringId, pending.PrimarySovereignTitleId,
                    recipients, holdings, allTitles, out var batchPlan, out reason)) return false;
                if (!TryRecordPartitionEstateShares(pending, plan, source.Leader, heirs, out reason)) return false;
                if (!TryRecordCrownPartitionBatch(pending, batchPlan, out reason))
                {
                    // No native mutation has begun; discard only this attempt's unpublished shares.
                    pending.EstateShares = null;
                    return false;
                }
            }
            if (pending.CrownPromotions == null || pending.CrownPromotions.Count == 0)
            {
                if (pending.EstateShares == null)
                { reason = "saved Crown batch lacks its complete estate-share manifest"; return false; }
                var assigned = pending.EstateShares.Where(s => s != null && !s.Primary
                    && pending.CrownBatch.Recipients.ContainsKey(s.RootTitleId ?? "")).ToDictionary(s => s.RootTitleId, s => s.Heir);
                if (!TryCaptureCrownBatchPromotions(pending, assigned, out reason)) return false;
            }
            return TryFinishCrownBatch(pending, out reason);
        }
    }
}
