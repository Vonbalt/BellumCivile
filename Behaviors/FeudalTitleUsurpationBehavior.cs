using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.SaveSystem;

namespace BellumCivile.Behaviors
{
    /// <summary>
    /// Processes higher-title usurpations lazily. Ownership changes and claim records enqueue
    /// likely candidates, then a small number are evaluated per tick.
    /// </summary>
    public class FeudalTitleUsurpationBehavior : CampaignBehaviorBase
    {
        private const int MaxChecksPerDailyTick = 8;
        private const int MinGoldReserve = 25000;
        private const int UsurpationRelationPenalty = -30;

        private List<FeudalUsurpationCandidateRecord> _pendingCandidates = new List<FeudalUsurpationCandidateRecord>();
        private HashSet<string> _pendingKeys = new HashSet<string>();

        public override void RegisterEvents()
        {
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
            CampaignEvents.OnSettlementOwnerChangedEvent.AddNonSerializedListener(this, OnSettlementOwnerChanged);
        }

        public override void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData("BellumCivile_PendingFeudalUsurpations", ref _pendingCandidates);
            EnsureCollectionsInitialized();
            RebuildPendingKeyIndex();
        }

        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            EnsureCollectionsInitialized();
            RebuildPendingKeyIndex();
            SeedCandidatesFromClaims(maxClaims: 64);
        }

        private void OnSettlementOwnerChanged(
            Settlement settlement,
            bool openToClaim,
            Hero newOwner,
            Hero oldOwner,
            Hero capturerHero,
            ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail detail)
        {
            FeudalTitleBehavior titleBehavior = Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>();
            if (titleBehavior == null || !titleBehavior.TryGetBarony(settlement, out FeudalTitleRecord baronyTitle))
                return;

            EnqueueClaimantsForTitle(titleBehavior, baronyTitle);
            EnqueueClaimantsForTitleChain(titleBehavior, baronyTitle);
        }

        private void ProcessPendingCandidates(int maxChecks)
        {
            FeudalTitleBehavior titleBehavior = Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>();
            if (titleBehavior == null || _pendingCandidates.Count == 0)
                return;

            int checks = Math.Min(maxChecks, _pendingCandidates.Count);
            for (int i = 0; i < checks; i++)
            {
                FeudalUsurpationCandidateRecord candidate = _pendingCandidates[0];
                _pendingCandidates.RemoveAt(0);
                RemovePendingKey(candidate);

                if (candidate == null)
                    continue;

                Clan claimant = ResolveClan(candidate.ClaimantClanId);
                FeudalTitleRecord title = titleBehavior.GetTitle(candidate.TitleId);
                if (!CanEvaluateCandidate(claimant, title))
                    continue;

                if (!CanNpcUsurpTitle(titleBehavior, claimant, title, out FeudalTitleUsurpationAssessment assessment))
                {
                    BellumCivileDebug.Trace("titles", $"usurpation skipped: claimant={claimant.StringId}; title={title.TitleId}; reason={assessment.Reason}; control={assessment.ControlShare:0.00}", requestInGameDisplay: false);
                    continue;
                }

                Hero claimantLeader = claimant.Leader;
                Clan oldHolder = ResolveClan(title.DeJureHolderClanId);

                if (!titleBehavior.TryUsurpTitle(claimant, title, "automatic_npc_usurpation", out string failureReason))
                {
                    BellumCivileLogger.Log($"Feudal title usurpation failed after payment guard; claimant={claimant.StringId}; title={title.TitleId}; reason={failureReason}.");
                    continue;
                }

                if (assessment.GoldCost > 0)
                    GiveGoldAction.ApplyBetweenCharacters(claimantLeader, null, assessment.GoldCost, true);
                if (assessment.InfluenceCost > 0f
                    && !NpcInfluenceBudgetService.TrySpend(
                        claimant,
                        assessment.InfluenceCost,
                        NpcInfluenceExpenseKind.Discretionary,
                        "title_usurpation"))
                {
                    BellumCivileLogger.Log($"Feudal title usurpation completed without influence payment after a successful reserve guard; claimant={claimant.StringId}; title={title.TitleId}.");
                }

                ResolveNpcSovereignElevation(titleBehavior, claimant, title);
                ApplyUsurpationRelations(claimant, oldHolder, title);
                NotificationHelper.ShowFeudalTitleUsurped(claimant, oldHolder, title);
                EnqueueClaimantsForTitleChain(titleBehavior, title);
            }
        }

        public bool TryRunAutonomousUsurpationEvaluationForClan(FeudalTitleBehavior titleBehavior, Clan claimant, out string report)
        {
            report = null;
            EnsureCollectionsInitialized();

            if (titleBehavior == null)
            {
                report = "title behavior unavailable";
                return false;
            }

            if (!IsValidNpcClan(claimant))
            {
                report = "invalid npc clan";
                return false;
            }

            List<FeudalTitleRecord> candidates = titleBehavior.GetActiveClaimsByClan(claimant)
                .Select(claim => titleBehavior.GetTitle(claim.TargetTitleId))
                .Where(title => title != null && title.IsActive)
                .GroupBy(title => title.TitleId)
                .Select(group => group.First())
                .OrderByDescending(title => title.TitleType)
                .ThenBy(title => title.Name ?? string.Empty)
                .ToList();

            foreach (FeudalTitleRecord title in candidates)
            {
                if (!CanNpcUsurpTitle(titleBehavior, claimant, title, out FeudalTitleUsurpationAssessment assessment))
                {
                    report = $"no valid usurpation; last checked={title.TitleId}; reason={assessment.Reason}; control={assessment.ControlShare:0.00}";
                    continue;
                }

                Hero claimantLeader = claimant.Leader;
                Clan oldHolder = ResolveClan(title.DeJureHolderClanId);

                if (!titleBehavior.TryUsurpTitle(claimant, title, "automatic_npc_usurpation", out string failureReason))
                {
                    report = failureReason ?? "title behavior rejected usurpation";
                    BellumCivileLogger.Log($"Feudal title usurpation failed after payment guard; claimant={claimant.StringId}; title={title.TitleId}; reason={report}.");
                    continue;
                }

                if (assessment.GoldCost > 0)
                    GiveGoldAction.ApplyBetweenCharacters(claimantLeader, null, assessment.GoldCost, true);
                if (assessment.InfluenceCost > 0f
                    && !NpcInfluenceBudgetService.TrySpend(
                        claimant,
                        assessment.InfluenceCost,
                        NpcInfluenceExpenseKind.Discretionary,
                        "title_usurpation"))
                {
                    BellumCivileLogger.Log($"Feudal title usurpation completed without influence payment after a successful reserve guard; claimant={claimant.StringId}; title={title.TitleId}.");
                }

                ResolveNpcSovereignElevation(titleBehavior, claimant, title);
                ApplyUsurpationRelations(claimant, oldHolder, title);
                NotificationHelper.ShowFeudalTitleUsurped(claimant, oldHolder, title);
                EnqueueClaimantsForTitleChain(titleBehavior, title);
                report = $"usurped {title.TitleId}; control={assessment.ControlShare:0.00}; cost={assessment.GoldCost}g/{assessment.InfluenceCost:0}inf";
                BellumCivileDebug.Trace(
                    "titles",
                    $"AI usurped feudal title from political options pass; claimant={claimant.Name} ({claimant.StringId}); title={FeudalTitleDisplayHelper.FormatTitleName(title, claimant)}; old_holder={oldHolder?.Name}; control={assessment.ControlShare:0.00}; cost={assessment.GoldCost}g/{assessment.InfluenceCost:0}inf.",
                    requestInGameDisplay: true);
                return true;
            }

            if (string.IsNullOrWhiteSpace(report))
                report = "no active claim candidates";
            return false;
        }

        private bool CanNpcUsurpTitle(
            FeudalTitleBehavior titleBehavior,
            Clan claimant,
            FeudalTitleRecord title,
            out FeudalTitleUsurpationAssessment assessment)
        {
            if (claimant == Clan.PlayerClan)
            {
                assessment = new FeudalTitleUsurpationAssessment
                {
                    Reason = "player usurpation waits for explicit UI action"
                };
                return false;
            }

            if (!IsValidNpcClan(claimant))
            {
                assessment = new FeudalTitleUsurpationAssessment { Reason = "invalid npc clan" };
                return false;
            }

            assessment = FeudalTitleUsurpationAssessmentService.Evaluate(
                titleBehavior,
                claimant,
                title,
                checkResources: true,
                minimumGoldReserve: MinGoldReserve);
            if (!assessment.CanUsurp)
                return false;

            if (!NpcInfluenceBudgetService.CanAfford(
                claimant,
                assessment.InfluenceCost,
                NpcInfluenceExpenseKind.Discretionary))
            {
                NpcInfluenceBudgetService.RecordBlocked(
                    claimant,
                    assessment.InfluenceCost,
                    NpcInfluenceExpenseKind.Discretionary,
                    "title_usurpation");
                assessment.CanUsurp = false;
                assessment.Reason = "insufficient influence reserve";
                return false;
            }

            return true;
        }

        public static List<FeudalTitleRecord> GetControlUnits(FeudalTitleBehavior titleBehavior, FeudalTitleRecord title)
        {
            return FeudalTitleUsurpationAssessmentService.GetControlUnits(titleBehavior, title);
        }

        private void SeedCandidatesFromClaims(int maxClaims)
        {
            FeudalTitleBehavior titleBehavior = Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>();
            if (titleBehavior == null)
                return;

            foreach (FeudalClaimRecord claim in titleBehavior.GetActiveClaims().Take(maxClaims))
            {
                FeudalTitleRecord title = titleBehavior.GetTitle(claim.TargetTitleId);
                if (title == null)
                    continue;

                Enqueue(title.TitleId, claim.ClaimantClanId);
            }
        }

        private void EnqueueClaimantsForTitle(FeudalTitleBehavior titleBehavior, FeudalTitleRecord title)
        {
            if (titleBehavior == null || title == null)
                return;

            foreach (FeudalClaimRecord claim in titleBehavior.GetActiveClaimsByTitle(title))
            {
                Enqueue(title.TitleId, claim.ClaimantClanId);
            }

            if (!string.IsNullOrWhiteSpace(title.DeFactoHolderClanId))
                Enqueue(title.TitleId, title.DeFactoHolderClanId);
        }

        private void EnqueueClaimantsForTitleChain(FeudalTitleBehavior titleBehavior, FeudalTitleRecord changedTitle)
        {
            if (titleBehavior == null || changedTitle == null)
                return;

            string parentId = changedTitle.ParentTitleId;
            int guard = 0;
            while (!string.IsNullOrWhiteSpace(parentId) && guard++ < 12)
            {
                FeudalTitleRecord parent = titleBehavior.GetTitle(parentId);
                if (parent == null)
                    break;

                foreach (FeudalClaimRecord claim in titleBehavior.GetActiveClaimsByTitle(parent))
                {
                    Enqueue(parent.TitleId, claim.ClaimantClanId);
                }

                if (!string.IsNullOrWhiteSpace(parent.DeFactoHolderClanId))
                    Enqueue(parent.TitleId, parent.DeFactoHolderClanId);

                parentId = parent.ParentTitleId;
            }
        }

        private void Enqueue(string titleId, string claimantClanId)
        {
            if (string.IsNullOrWhiteSpace(titleId) || string.IsNullOrWhiteSpace(claimantClanId))
                return;

            string key = BuildKey(titleId, claimantClanId);
            if (!_pendingKeys.Add(key))
                return;

            _pendingCandidates.Add(new FeudalUsurpationCandidateRecord(titleId, claimantClanId));
            Campaign.Current?.GetCampaignBehavior<FeudalPoliticalOptionsBehavior>()
                ?.QueueClanForEvaluation(ResolveClan(claimantClanId), $"usurpation candidate queued: {titleId}", 1f);
        }

        private static bool CanEvaluateCandidate(Clan claimant, FeudalTitleRecord title)
        {
            return claimant != null
                && title != null
                && title.IsActive;
        }

        private static bool IsValidNpcClan(Clan clan)
        {
            return clan != null
                && clan != Clan.PlayerClan
                && !clan.IsEliminated
                && !clan.IsMinorFaction
                && !clan.IsClanTypeMercenary
                && !clan.IsUnderMercenaryService
                && !clan.IsBanditFaction
                && clan.Leader != null
                && !clan.Leader.IsDead;
        }

        private static void ApplyUsurpationRelations(Clan usurper, Clan oldHolder, FeudalTitleRecord title)
        {
            if (usurper?.Leader == null || oldHolder?.Leader == null || usurper == oldHolder)
                return;

            RelationMemoryService.ApplyChange(oldHolder.Leader, usurper.Leader, UsurpationRelationPenalty, false,
                RelationMemorySources.TitleUsurpation, 20f, RelationMemoryScope.House, FeudalTitleDisplayHelper.FormatTitleName(title, usurper));
            BellumCivileLogger.Log($"Feudal usurpation relation penalty; old_holder={oldHolder.StringId}; usurper={usurper.StringId}; title={title?.TitleId ?? "unknown"}.");
        }

        private static void ResolveNpcSovereignElevation(
            FeudalTitleBehavior titleBehavior,
            Clan claimant,
            FeudalTitleRecord title)
        {
            FeudalSovereignElevationPreview preview = titleBehavior?.GetSovereignElevationPreview(claimant, title);
            if (preview?.IsRequired != true)
                return;

            Kingdom formerKingdom = preview.ParentKingdom;
            // Autonomous usurpation uses the conservative settlement: NPC rulers relinquish
            // unrelated holdings instead of silently creating an extra war during a title tick.
            if (!titleBehavior.TryPromoteSovereignTitleToIndependentRealm(
                claimant,
                title,
                FeudalSovereignElevationChoice.PeacefulSeparation,
                "sovereign title usurpation",
                out Kingdom independentKingdom,
                out string failureReason))
            {
                BellumCivileLogger.Log(
                    $"NPC sovereign elevation failed after usurpation; claimant={claimant?.StringId ?? "null"}; title={title?.TitleId ?? "null"}; reason={failureReason ?? "unknown"}.");
                return;
            }

            NotificationHelper.ShowSovereignTitleElevated(
                claimant,
                formerKingdom,
                independentKingdom,
                title,
                retainedHoldings: false);
        }

        private void EnsureCollectionsInitialized()
        {
            if (_pendingCandidates == null)
                _pendingCandidates = new List<FeudalUsurpationCandidateRecord>();
            if (_pendingKeys == null)
                _pendingKeys = new HashSet<string>();
        }

        private void RebuildPendingKeyIndex()
        {
            _pendingKeys.Clear();
            foreach (FeudalUsurpationCandidateRecord candidate in _pendingCandidates.ToList())
            {
                if (candidate == null || string.IsNullOrWhiteSpace(candidate.TitleId) || string.IsNullOrWhiteSpace(candidate.ClaimantClanId))
                {
                    _pendingCandidates.Remove(candidate);
                    continue;
                }

                _pendingKeys.Add(BuildKey(candidate.TitleId, candidate.ClaimantClanId));
            }
        }

        private void RemovePendingKey(FeudalUsurpationCandidateRecord candidate)
        {
            if (candidate == null)
                return;

            _pendingKeys.Remove(BuildKey(candidate.TitleId, candidate.ClaimantClanId));
        }

        private static string BuildKey(string titleId, string claimantClanId)
        {
            return $"{titleId}|{claimantClanId}";
        }

        private static Clan ResolveClan(string clanId)
        {
            return string.IsNullOrWhiteSpace(clanId)
                ? null
                : Clan.All.FirstOrDefault(clan => clan != null && clan.StringId == clanId);
        }
    }
}
