using System;
using System.Collections.Generic;
using System.Linq;
using BellumCivile.Behaviors;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using C = BellumCivile.BellumCivileConstants;

namespace BellumCivile
{
    public static class ForeignPolicyVoteEvaluator
    {
        private sealed class CachedRealmStrength
        {
            public int CampaignDay;
            public float AverageStrength;
        }

        private static readonly Dictionary<string, CachedRealmStrength> RealmStrengthCache =
            new Dictionary<string, CachedRealmStrength>();

        public static bool TryEvaluateWar(
            Kingdom sourceKingdom,
            Kingdom targetKingdom,
            Clan proposerClan,
            Clan voterClan,
            out ForeignPolicyVoteBreakdown result)
        {
            result = CreateBreakdown(ForeignPolicyActionType.War, sourceKingdom, targetKingdom, proposerClan, voterClan);
            if (!IsValidVote(sourceKingdom, targetKingdom, voterClan) || sourceKingdom.IsAtWarWith(targetKingdom))
                return false;

            var diplomacyModel = Campaign.Current?.Models?.DiplomacyModel;
            if (diplomacyModel == null)
                return false;

            IReadOnlyList<ForeignPolicyClaimStake> stakes = ForeignPolicyEvaluationService.GetClaimStakes(sourceKingdom, targetKingdom);
            float effectiveScore;
            float threshold;
            try
            {
                TaleWorlds.Localization.TextObject reason;
                effectiveScore = diplomacyModel.GetScoreOfDeclaringWar(
                    sourceKingdom,
                    targetKingdom,
                    voterClan,
                    out reason,
                    includeReason: false);
                threshold = diplomacyModel.GetDecisionMakingThreshold(sourceKingdom);
            }
            catch
            {
                return false;
            }

            if (effectiveScore <= C.ForeignPolicyHardWarVetoScore)
            {
                result.StrategicScore = -C.ForeignPolicyVoteTotalCap;
                result.TotalScore = -C.ForeignPolicyVoteTotalCap;
                result.Reasons.Add("active diplomacy model hard veto");
                return true;
            }

            ForeignPolicyClaimScoreBreakdown claimScore = ForeignPolicyClaimScoringHelper.Calculate(
                stakes,
                sourceKingdom,
                voterClan,
                effectiveScore,
                threshold);
            float rawScore = effectiveScore - claimScore.ScoreBonus;
            result.StrategicScore = NormalizeStrategicScore(rawScore, threshold);
            result.LegalScore = CalculateWarLegalScore(stakes, sourceKingdom, voterClan, claimScore);
            result.CourtScore = CalculateCourtScore(
                ForeignPolicyActionType.War,
                sourceKingdom,
                targetKingdom,
                proposerClan,
                voterClan);
            result.RelationshipScore = CalculateRelationshipScore(
                wantsPeace: false,
                targetKingdom,
                proposerClan,
                voterClan);
            result.TraitScore = CalculateWarTraitScore(sourceKingdom, targetKingdom, voterClan, claimScore.RealmPressure);
            result.ReadinessScore = CalculateReadinessScore(sourceKingdom, voterClan, wantsPeace: false);

            result.TotalScore = ClampTotal(SumComponents(result));
            return true;
        }

        public static bool TryEvaluatePeace(
            Kingdom sourceKingdom,
            Kingdom targetKingdom,
            Clan proposerClan,
            Clan voterClan,
            int dailyTribute,
            int tributeDurationDays,
            out ForeignPolicyVoteBreakdown result)
        {
            result = CreateBreakdown(ForeignPolicyActionType.Peace, sourceKingdom, targetKingdom, proposerClan, voterClan);
            if (!IsValidVote(sourceKingdom, targetKingdom, voterClan) || !sourceKingdom.IsAtWarWith(targetKingdom))
                return false;

            var diplomacyModel = Campaign.Current?.Models?.DiplomacyModel;
            if (diplomacyModel == null)
                return false;

            if (!diplomacyModel.IsPeaceSuitable(sourceKingdom, targetKingdom))
            {
                result.StrategicScore = -C.ForeignPolicyVoteTotalCap;
                result.TotalScore = -C.ForeignPolicyVoteTotalCap;
                result.Reasons.Add("active diplomacy model considers peace unsuitable");
                return true;
            }

            try
            {
                TaleWorlds.Localization.TextObject reason;
                float clanPeaceScore = diplomacyModel.GetScoreOfDeclaringPeaceForClan(
                    sourceKingdom,
                    targetKingdom,
                    voterClan,
                    out reason,
                    includeReason: false);
                float threshold = diplomacyModel.GetDecisionMakingThreshold(sourceKingdom);
                result.StrategicScore = NormalizeStrategicScore(clanPeaceScore, threshold);
            }
            catch
            {
                return false;
            }

            IReadOnlyList<ForeignPolicyClaimStake> stakes = ForeignPolicyEvaluationService.GetClaimStakes(sourceKingdom, targetKingdom);
            ForeignPolicyBehavior behavior = Campaign.Current?.GetCampaignBehavior<ForeignPolicyBehavior>();

            ForeignPolicyObjectiveStatus objectives = behavior?.GetObjectiveStatus(sourceKingdom, targetKingdom)
                ?? new ForeignPolicyObjectiveStatus();
            result.ObjectiveScore = CalculateObjectiveScore(objectives, stakes, voterClan, result.Reasons);

            ForeignPolicyTributeAssessment tribute = ForeignPolicyTributeHelper.Assess(
                sourceKingdom,
                dailyTribute,
                tributeDurationDays);
            result.TributeScore = CalculateTributeScore(tribute);
            result.Reasons.Add($"terms {tribute.Classification.ToString().ToLowerInvariant()} ({tribute.BurdenRatio:P0} burden)");

            result.CourtScore = CalculateCourtScore(
                ForeignPolicyActionType.Peace,
                sourceKingdom,
                targetKingdom,
                proposerClan,
                voterClan);
            result.RelationshipScore = CalculateRelationshipScore(
                wantsPeace: true,
                targetKingdom,
                proposerClan,
                voterClan);
            result.TraitScore = CalculatePeaceTraitScore(voterClan, tribute);
            result.ReadinessScore = CalculateReadinessScore(sourceKingdom, voterClan, wantsPeace: true);

            int activeWars = CountActiveWars(sourceKingdom);
            if (activeWars > 1)
            {
                float multiWarPressure = Math.Min(16f, (activeWars - 1) * C.ForeignPolicyVotePeaceAdditionalWar);
                result.StrategicScore += multiWarPressure;
                result.Reasons.Add($"{activeWars} active wars");
            }

            result.TotalScore = ClampTotal(SumComponents(result));
            return true;
        }

        private static ForeignPolicyVoteBreakdown CreateBreakdown(
            ForeignPolicyActionType actionType,
            Kingdom sourceKingdom,
            Kingdom targetKingdom,
            Clan proposerClan,
            Clan voterClan)
        {
            return new ForeignPolicyVoteBreakdown
            {
                ActionType = actionType,
                SourceKingdom = sourceKingdom,
                TargetKingdom = targetKingdom,
                ProposerClan = proposerClan,
                VoterClan = voterClan
            };
        }

        private static bool IsValidVote(Kingdom sourceKingdom, Kingdom targetKingdom, Clan voterClan)
        {
            return sourceKingdom != null
                && targetKingdom != null
                && voterClan?.Leader != null
                && voterClan.Kingdom == sourceKingdom
                && !voterClan.IsEliminated
                && !voterClan.IsUnderMercenaryService
                && (!voterClan.IsMinorFaction || voterClan == Clan.PlayerClan);
        }

        private static float NormalizeStrategicScore(float score, float threshold)
        {
            float scale = Math.Max(C.ForeignPolicyMinimumScoreScale, Math.Abs(threshold));
            float normalized = (score - threshold) / scale * C.ForeignPolicyVoteStrategicScale;
            return Math.Max(-C.ForeignPolicyVoteStrategicCap, Math.Min(C.ForeignPolicyVoteStrategicCap, normalized));
        }

        private static float CalculateWarLegalScore(
            IReadOnlyList<ForeignPolicyClaimStake> stakes,
            Kingdom sourceKingdom,
            Clan voterClan,
            ForeignPolicyClaimScoreBreakdown claimScore)
        {
            float score = claimScore.RealmPressure * C.ForeignPolicyVoteRealmClaimScale
                + claimScore.PersonalPressure * C.ForeignPolicyVotePersonalClaimScale;
            float sympathy = 0f;
            foreach (ForeignPolicyClaimStake stake in stakes.Where(stake => stake.ClaimantClan != voterClan))
            {
                Clan claimant = stake.ClaimantClan;
                if (claimant?.Leader == null)
                    continue;

                int relation = voterClan.Leader.GetRelation(claimant.Leader);
                if (relation >= 60)
                    sympathy += stake.Pressure * C.ForeignPolicyVoteClaimantFriendScale;
                if (MarriageAllianceHelper.HasMarriageAlliance(voterClan, claimant))
                    sympathy += stake.Pressure * C.ForeignPolicyVoteClaimantAllianceScale;
            }

            return score + Math.Min(C.ForeignPolicyVoteClaimantSympathyCap, sympathy);
        }

        private static float CalculateCourtScore(
            ForeignPolicyActionType actionType,
            Kingdom sourceKingdom,
            Kingdom targetKingdom,
            Clan proposerClan,
            Clan voterClan)
        {
            FactionManagerBehavior factionManager = Campaign.Current?.GetCampaignBehavior<FactionManagerBehavior>();
            FactionObject voterFaction = factionManager?.GetIdeologicalFaction(voterClan);
            FactionObject proposerFaction = factionManager?.GetIdeologicalFaction(proposerClan);
            float score = 0f;

            if (voterFaction != null && proposerFaction != null)
            {
                if (voterFaction.Type == proposerFaction.Type)
                    score += C.ForeignPolicyVoteSameSponsorFaction;
            }

            if (voterFaction != null)
            {
                ForeignPolicyEvaluationService evaluator = new ForeignPolicyEvaluationService();
                ForeignPolicyEvaluation factionEvaluation = actionType == ForeignPolicyActionType.War
                    ? evaluator.EvaluateWarTarget(sourceKingdom, targetKingdom, voterFaction)
                    : evaluator.EvaluatePeaceTarget(sourceKingdom, targetKingdom, voterFaction);
                score += factionEvaluation.FactionAdjustment * C.ForeignPolicyVoteFactionViewpointScale;
            }

            return score;
        }

        private static float CalculateRelationshipScore(
            bool wantsPeace,
            Kingdom targetKingdom,
            Clan proposerClan,
            Clan voterClan)
        {
            float score = 0f;
            Hero targetRuler = targetKingdom?.Leader;
            if (targetRuler != null)
            {
                int relation = voterClan.Leader.GetRelation(targetRuler);
                float relationEffect = relation >= 0
                    ? relation * C.ForeignPolicyVoteTargetRelationPositiveScale
                    : relation * C.ForeignPolicyVoteTargetRelationNegativeScale;
                score += wantsPeace ? relationEffect : -relationEffect;
            }

            if (proposerClan?.Leader != null && proposerClan != voterClan)
                score += voterClan.Leader.GetRelation(proposerClan.Leader) * C.ForeignPolicyVoteProposerRelationScale;

            if (targetKingdom?.RulingClan != null
                && MarriageAllianceHelper.HasMarriageAlliance(voterClan, targetKingdom.RulingClan))
            {
                score += wantsPeace
                    ? C.ForeignPolicyVoteForeignMarriageAlliance
                    : -C.ForeignPolicyVoteForeignMarriageAlliance;
            }

            return score;
        }

        private static float CalculateWarTraitScore(
            Kingdom sourceKingdom,
            Kingdom targetKingdom,
            Clan voterClan,
            float realmClaimPressure)
        {
            Hero leader = voterClan.Leader;
            int valor = leader.GetTraitLevel(DefaultTraits.Valor);
            int mercy = leader.GetTraitLevel(DefaultTraits.Mercy);
            int honor = leader.GetTraitLevel(DefaultTraits.Honor);
            int calculating = leader.GetTraitLevel(DefaultTraits.Calculating);
            float score = valor * C.ForeignPolicyVoteWarValor
                - mercy * C.ForeignPolicyVoteWarMercy
                + (realmClaimPressure > 0f ? honor * 3f : -honor * 3f);

            if (CountActiveWars(sourceKingdom) == 0 && CountActiveWars(targetKingdom) > 0)
                score += calculating * 4f;
            return score;
        }

        private static float CalculatePeaceTraitScore(Clan voterClan, ForeignPolicyTributeAssessment tribute)
        {
            Hero leader = voterClan.Leader;
            int valor = leader.GetTraitLevel(DefaultTraits.Valor);
            int mercy = leader.GetTraitLevel(DefaultTraits.Mercy);
            int honor = leader.GetTraitLevel(DefaultTraits.Honor);
            int calculating = leader.GetTraitLevel(DefaultTraits.Calculating);
            float score = mercy * C.ForeignPolicyVotePeaceMercy
                - valor * C.ForeignPolicyVotePeaceValor;

            if (tribute.Classification == ForeignPolicyTributeClass.Humiliating
                || tribute.Classification == ForeignPolicyTributeClass.Ruinous)
            {
                score -= honor * 4f;
                score -= calculating * 4f;
            }
            else if (tribute.Classification == ForeignPolicyTributeClass.Favorable
                || tribute.Classification == ForeignPolicyTributeClass.None)
            {
                score += honor * 2f;
                if (tribute.Classification == ForeignPolicyTributeClass.Favorable)
                    score += calculating * 3f;
            }

            return score;
        }

        private static float CalculateReadinessScore(Kingdom kingdom, Clan voterClan, bool wantsPeace)
        {
            float averageStrength = GetAverageVotingClanStrength(kingdom, voterClan.CurrentTotalStrength);
            float warReadiness = 0f;
            if (voterClan.CurrentTotalStrength >= averageStrength * 1.25f)
                warReadiness += C.ForeignPolicyVoteStrongClanReadiness;
            else if (voterClan.CurrentTotalStrength <= averageStrength * 0.60f)
                warReadiness -= C.ForeignPolicyVoteWeakClanReadiness;
            if (voterClan.Leader.IsPrisoner)
                warReadiness -= C.ForeignPolicyVotePrisonerReadiness;
            if (voterClan.Leader.Gold < 10000)
                warReadiness -= C.ForeignPolicyVotePoorClanReadiness;

            return wantsPeace ? -warReadiness : warReadiness;
        }

        private static float GetAverageVotingClanStrength(Kingdom kingdom, float fallback)
        {
            int currentDay = Campaign.Current == null ? 0 : (int)CampaignTime.Now.ToDays;
            string key = kingdom?.StringId ?? "null";
            if (RealmStrengthCache.TryGetValue(key, out CachedRealmStrength cached)
                && cached.CampaignDay == currentDay)
            {
                return cached.AverageStrength;
            }

            List<Clan> votingClans = kingdom?.Clans
                .Where(clan => clan != null
                    && !clan.IsEliminated
                    && !clan.IsUnderMercenaryService
                    && (!clan.IsMinorFaction || clan == Clan.PlayerClan))
                .ToList() ?? new List<Clan>();
            float averageStrength = votingClans.Count > 0
                ? votingClans.Average(clan => clan.CurrentTotalStrength)
                : fallback;
            RealmStrengthCache[key] = new CachedRealmStrength
            {
                CampaignDay = currentDay,
                AverageStrength = averageStrength
            };
            return averageStrength;
        }

        private static float CalculateObjectiveScore(
            ForeignPolicyObjectiveStatus objectives,
            IReadOnlyList<ForeignPolicyClaimStake> stakes,
            Clan voterClan,
            List<string> reasons)
        {
            if (objectives == null || !objectives.HasObjectives)
                return 0f;
            if (objectives.AllObjectivesAchieved)
            {
                reasons.Add("declared objectives achieved");
                return C.ForeignPolicyVoteObjectivesAchieved;
            }

            float score = -C.ForeignPolicyVoteObjectivesOutstanding;
            bool personalObjectiveOutstanding = stakes.Any(stake => stake.ClaimantClan == voterClan
                && objectives.OutstandingTitleIds.Contains(stake.Title?.TitleId));
            if (personalObjectiveOutstanding)
            {
                score -= C.ForeignPolicyVotePersonalObjectiveOutstanding;
                reasons.Add("personal claimed objective remains outstanding");
            }
            else
            {
                reasons.Add("declared objectives remain outstanding");
            }

            return score;
        }

        private static float CalculateTributeScore(ForeignPolicyTributeAssessment tribute)
        {
            switch (tribute?.Classification ?? ForeignPolicyTributeClass.None)
            {
                case ForeignPolicyTributeClass.Favorable: return 20f;
                case ForeignPolicyTributeClass.None: return 5f;
                case ForeignPolicyTributeClass.Affordable: return 0f;
                case ForeignPolicyTributeClass.Costly: return -10f;
                case ForeignPolicyTributeClass.Humiliating: return -25f;
                case ForeignPolicyTributeClass.Ruinous: return -45f;
                default: return 0f;
            }
        }

        private static float SumComponents(ForeignPolicyVoteBreakdown result)
        {
            return result.StrategicScore
                + result.LegalScore
                + result.CourtScore
                + result.RelationshipScore
                + result.TraitScore
                + result.ReadinessScore
                + result.ObjectiveScore
                + result.TributeScore;
        }

        private static float ClampTotal(float score)
        {
            return Math.Max(-C.ForeignPolicyVoteTotalCap, Math.Min(C.ForeignPolicyVoteTotalCap, score));
        }

        private static int CountActiveWars(Kingdom kingdom)
        {
            return Kingdom.All.Count(other => other != null
                && other != kingdom
                && !other.IsEliminated
                && kingdom.IsAtWarWith(other));
        }
    }
}
