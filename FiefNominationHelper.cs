using System;
using System.Collections.Generic;
using System.Linq;
using BellumCivile.Behaviors;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using C = BellumCivile.BellumCivileConstants;

namespace BellumCivile
{
    internal sealed class FiefNominationResult
    {
        public Clan Candidate { get; set; }
        public float Score { get; set; }
        public List<string> Reasons { get; } = new List<string>();
    }

    internal sealed class FiefLegalClaimScore
    {
        public float Score { get; set; }
        public float BaseScore { get; set; }
        public bool HasClaim => BaseScore > 0f;
        public bool IsParentTitleClaim { get; set; }
        public bool IsDeJureHolder { get; set; }
        public FeudalClaimStrength? Strength { get; set; }
    }

    internal static class FiefNominationHelper
    {
        private sealed class ReasonScore
        {
            public ReasonScore(string id, float weight)
            {
                Id = id;
                Weight = weight;
            }

            public string Id { get; }
            public float Weight { get; }
        }

        public static FiefNominationResult ChooseNominee(
            Clan voter,
            Kingdom kingdom,
            Settlement settlement,
            Clan capturerClan,
            Clan clanToExclude,
            FactionManagerBehavior factionManager)
        {
            if (!IsValidVoter(voter, kingdom) || settlement == null)
                return null;

            FiefNominationResult best = null;
            foreach (Clan candidate in GetEligibleCandidates(kingdom, settlement, clanToExclude))
            {
                FiefNominationResult result = ScoreCandidate(voter, candidate, kingdom, settlement, capturerClan, factionManager);
                if (result == null)
                    continue;

                if (best == null || result.Score > best.Score)
                    best = result;
            }

            return best;
        }

        public static FiefNominationResult ScoreCandidate(
            Clan voter,
            Clan candidate,
            Kingdom kingdom,
            Settlement settlement,
            Clan capturerClan,
            FactionManagerBehavior factionManager)
        {
            if (!IsValidVoter(voter, kingdom) || !IsValidCandidate(candidate, kingdom, null) || candidate.Leader == null)
                return null;

            Hero voterLeader = voter.Leader;
            int calculating = voterLeader.GetTraitLevel(DefaultTraits.Calculating);
            int honor = voterLeader.GetTraitLevel(DefaultTraits.Honor);
            int generosity = voterLeader.GetTraitLevel(DefaultTraits.Generosity);
            int mercy = voterLeader.GetTraitLevel(DefaultTraits.Mercy);

            FactionObject voterFaction = factionManager?.GetIdeologicalFaction(voter);
            FactionObject candidateFaction = factionManager?.GetIdeologicalFaction(candidate);
            Clan rulingClan = kingdom?.RulingClan;
            FeudalTitleType? candidateHighestTitle = FeudalPoliticalWeightHelper.GetHighestHeldTitleRank(candidate);

            float score = 0f;
            List<ReasonScore> reasons = new List<ReasonScore>();

            int desiredFiefs = FactionObject.CalculateDesiredFiefs(candidate);
            int fiefDelta = desiredFiefs - candidate.Fiefs.Count;
            if (fiefDelta > 0)
            {
                float value = CalculateNeedMerit(
                    voter,
                    candidate,
                    fiefDelta,
                    voterFaction,
                    candidateFaction,
                    generosity,
                    mercy,
                    calculating);
                score += value;
                if (candidate.Fiefs.Count > 0 && value > 0f)
                    reasons.Add(new ReasonScore("need", value));
            }
            else if (fiefDelta == 0)
            {
                score += CalculateCandidateFiefNeedScore(fiefDelta);
            }
            else
            {
                score += CalculateCandidateFiefNeedScore(fiefDelta);
            }

            if (candidate == voter)
            {
                float selfValue = 25f;
                if (generosity < 0) selfValue += C.FiefTyrantGreedBonus;
                if (fiefDelta > 0) selfValue += 25f;
                if (generosity > 0 && fiefDelta <= 0) selfValue -= 25f;
                score += selfValue;
                if (selfValue > 0f)
                    reasons.Add(new ReasonScore("self", selfValue));
            }

            if (capturerClan != null && candidate == capturerClan)
            {
                float value = C.FiefCandidateCapturerBonus;
                if (honor > 0) value += C.FiefCapturerHonorBonus;
                if (calculating > 0) value -= C.FiefCapturerCalcPenalty;
                score += value;
                if (value > 0f)
                    reasons.Add(new ReasonScore("capturer", value));
            }

            if (settlement?.Culture != null && candidate.Culture == settlement.Culture)
            {
                score += C.FiefCandidateCultureBonus;
                reasons.Add(new ReasonScore("culture", C.FiefCandidateCultureBonus));
            }

            FiefLegalClaimScore legalClaim = CalculateLegalClaimScore(voter, candidate, settlement, voterFaction);
            if (legalClaim.HasClaim)
            {
                score += legalClaim.Score;
                reasons.Add(new ReasonScore("legal_claim", legalClaim.Score));
            }

            float courtClaim = CourtAgendaBehavior.Current?.ClaimAllocationBonus(kingdom, settlement, candidate, voter) ?? 0;
            if (courtClaim != 0)
            {
                score += courtClaim;
                reasons.Add(new ReasonScore("court_claim", courtClaim));
            }

            if (voterFaction != null && candidateFaction != null)
            {
                if (voterFaction.Type == candidateFaction.Type)
                {
                    float value = C.FiefBlocBonus;
                    if (calculating > 0) value += C.FiefBlocCalculatingBonus;
                    score += value;
                    reasons.Add(new ReasonScore("same_faction", value));
                }
                else
                {
                    score -= C.FiefNonRivalPenalty;
                }
            }

            if (voterFaction != null)
            {
                if (voterFaction.Type == FactionType.Glory)
                {
                    if (candidate.CurrentTotalStrength <= C.FiefMilWeakStrengthThreshold)
                    {
                        float penalty = C.FiefMilWeakClanPenalty;
                        if (calculating > 0) penalty += C.FiefMilCalcPenalty;
                        score -= penalty;
                    }
                    else if (candidate.CurrentTotalStrength >= C.FiefMilStrongStrengthThreshold)
                    {
                        score += C.FiefMilStrongClanBonus;
                        reasons.Add(new ReasonScore("strong_house", C.FiefMilStrongClanBonus));
                    }
                }
                else if (voterFaction.Type == FactionType.Nobility)
                {
                    float titleRankScore = FeudalPoliticalWeightHelper.GetAristocraticFiefVoteScore(candidateHighestTitle);
                    score += titleRankScore;
                    if (titleRankScore > 0f)
                    {
                        reasons.Add(new ReasonScore("proper_rank", titleRankScore));
                    }

                    if (candidate != voter && MarriageAllianceHelper.HasMarriageAlliance(voter, candidate))
                    {
                        score += C.FiefAristMarriageAllianceBonus;
                        reasons.Add(new ReasonScore("marriage_alliance", C.FiefAristMarriageAllianceBonus));
                    }
                }
                else if (voterFaction.Type == FactionType.Liberty)
                {
                    float lowLandedScore = FeudalPoliticalWeightHelper.GetPopulistFiefVoteScore(candidateHighestTitle);
                    score += lowLandedScore;
                    if (lowLandedScore > 0f)
                    {
                        reasons.Add(new ReasonScore(
                            candidateHighestTitle.HasValue ? "low_house" : "landless",
                            lowLandedScore));
                    }

                    if (candidate.Fiefs.Count == 0)
                    {
                        score += C.FiefPopLandlessBonus;
                        reasons.Add(new ReasonScore("landless", C.FiefPopLandlessBonus));
                    }

                    if (candidate.Influence < C.FiefPopInfluenceLowThreshold)
                    {
                        score -= C.FiefPopLowInfluencePenalty;
                    }
                    else if (candidate.Influence > C.FiefPopInfluenceHighThreshold)
                    {
                        score += C.FiefPopHighInfluenceBonus;
                        reasons.Add(new ReasonScore("popular_support", C.FiefPopHighInfluenceBonus));
                    }
                }
            }

            if (candidate != voter && candidate.Leader != null)
            {
                float relation = voterLeader.GetRelation(candidate.Leader);
                float relationMerit = relation * C.FiefRelScale;
                if (calculating > 0) relationMerit *= C.FiefRelCalcMultiplier;
                else if (calculating < 0) relationMerit *= C.FiefRelHotheadMultiplier;
                if (voterFaction?.Type == FactionType.Nobility)
                    relationMerit *= FeudalPoliticalWeightHelper.IsHighLandedRank(candidateHighestTitle)
                        ? C.FiefAristHighTitleRelationMultiplier
                        : C.FiefAristRelationMultiplier;
                score += relationMerit;
                if (relationMerit >= 10f)
                    reasons.Add(new ReasonScore("friend", relationMerit));
            }

            if (voterFaction?.Type == FactionType.Royalists)
            {
                if (candidate == rulingClan)
                {
                    score += C.FiefRoyKingBonus;
                    if (fiefDelta == 0) score -= C.FiefRoyKingSatisfiedPenalty;
                    else if (fiefDelta < 0) score -= -fiefDelta * C.FiefRoyKingExcessPenalty;
                }
                else if (candidateFaction?.Type == FactionType.Royalists && rulingClan?.Leader != null)
                {
                    float loyaltyToCrown = candidate.Leader.GetRelation(rulingClan.Leader);
                    if (loyaltyToCrown > C.FiefRoyFriendRelThreshold)
                    {
                        float value = C.FiefRoyFriendBonus + loyaltyToCrown * 0.2f;
                        score += value;
                        reasons.Add(new ReasonScore("crown_loyalty", value));
                    }
                    else if (loyaltyToCrown < C.FiefRoyEnemyRelThreshold)
                    {
                        score -= C.FiefRoyEnemyPenalty;
                    }

                    if (MarriageAllianceHelper.HasMarriageAlliance(candidate, rulingClan))
                    {
                        score += C.FiefRoyRoyalMarriageAllianceBonus;
                        reasons.Add(new ReasonScore("royal_marriage", C.FiefRoyRoyalMarriageAllianceBonus));
                    }
                }
            }

            if (candidate.Fiefs.Count == 0)
            {
                bool isSameFaction = voterFaction != null && candidateFaction != null && voterFaction == candidateFaction;

                if (isSameFaction)
                {
                    if (generosity > 0)
                    {
                        score += C.FiefLandlessGenerousBonus;
                        reasons.Add(new ReasonScore("landless", C.FiefLandlessGenerousBonus));
                    }
                    if (mercy > 0)
                    {
                        score += C.FiefLandlessMercifulBonus;
                        reasons.Add(new ReasonScore("landless", C.FiefLandlessMercifulBonus));
                    }
                }

                if (generosity < 0) score -= C.FiefLandlessGreedyPenalty;
                if (calculating > 0) score -= C.FiefLandlessCalcPenalty;
            }

            if (rulingClan?.Leader != null && voter != rulingClan && candidate != voter)
            {
                bool isCandidateTheKing = candidate == rulingClan;
                bool isCandidateRoyalist = candidateFaction?.Type == FactionType.Royalists;
                float relationWithKing = voterLeader.GetRelation(rulingClan.Leader);

                if (relationWithKing <= C.FiefSpiteRelThreshold)
                {
                    if (isCandidateTheKing || isCandidateRoyalist)
                    {
                        float penalty = C.FiefSpiteBase;
                        if (voterFaction != null && voterFaction.Mood <= C.FiefSpiteMoodReq)
                            penalty += C.FiefSpiteRebelliousBonus;
                        if (calculating < 0)
                            penalty += C.FiefSpiteHotheadBonus;

                        score -= penalty;
                    }
                    else
                    {
                        score += C.FiefAntiEstablishmentRally;
                        reasons.Add(new ReasonScore("anti_crown", C.FiefAntiEstablishmentRally));
                    }
                }
            }

            if (rulingClan != null && voter == rulingClan)
            {
                if (candidate == rulingClan)
                {
                    if (generosity < 0)
                    {
                        score += C.FiefTyrantGreedBonus;
                        reasons.Add(new ReasonScore("self", C.FiefTyrantGreedBonus));
                    }
                    if (honor < 0)
                    {
                        score += C.FiefTyrantDishonorBonus;
                        reasons.Add(new ReasonScore("self", C.FiefTyrantDishonorBonus));
                    }
                    if (calculating > 0)
                    {
                        score += C.FiefTyrantCalcBonus;
                        reasons.Add(new ReasonScore("self", C.FiefTyrantCalcBonus));
                    }

                    if (generosity > 0) score -= C.FiefGoodKingGenerousPenalty;
                    if (honor > 0) score -= C.FiefGoodKingHonorPenalty;
                }
                else if (candidate.Fiefs.Count == 0 && (generosity > 0 || honor > 0))
                {
                    bool isSameFaction = voterFaction != null && candidateFaction != null && voterFaction == candidateFaction;
                    if (isSameFaction)
                    {
                        score += C.FiefGoodKingLandlessBonus;
                        reasons.Add(new ReasonScore("landless", C.FiefGoodKingLandlessBonus));
                    }
                }
            }

            if (voterFaction?.Leader != null && voterFaction.Leader != voter
                && candidate.Leader != null && voterFaction.Leader.Leader != null)
            {
                float leaderRelWithCandidate = voterFaction.Leader.Leader.GetRelation(candidate.Leader);
                float trustFactor = MathF.Max(0f, (float)voterLeader.GetRelation(voterFaction.Leader.Leader)) / 100f;
                float leaderPull = leaderRelWithCandidate * trustFactor * C.FactionLeaderPullStrength;
                score += leaderPull;

                if (leaderPull >= 10f)
                    reasons.Add(new ReasonScore("leader_pull", leaderPull));
            }

            score += candidate.Tier * C.FiefCandidateRenownTierScale;
            score += MathF.Min(
                C.FiefCandidateStrengthBonusCap,
                candidate.CurrentTotalStrength * C.FiefCandidateStrengthScale);
            score += MathF.Min(
                C.FiefCandidateInfluenceBonusCap,
                candidate.Influence * C.FiefCandidateInfluenceScale);

            FiefNominationResult final = new FiefNominationResult
            {
                Candidate = candidate,
                Score = score
            };

            foreach (string reason in reasons
                .Where(r => r.Weight > 0f)
                .OrderByDescending(r => r.Weight)
                .Select(r => r.Id)
                .Distinct()
                .Take(3))
            {
                final.Reasons.Add(reason);
            }

            if (final.Reasons.Count == 0)
                final.Reasons.Add("judgment");

            return final;
        }

        private static float CalculateNeedMerit(
            Clan voter,
            Clan candidate,
            int candidateDeficit,
            FactionObject voterFaction,
            FactionObject candidateFaction,
            int voterGenerosity,
            int voterMercy,
            int voterCalculating)
        {
            if (candidateDeficit <= 0)
                return 0f;

            int voterDeficit = voter != null
                ? Math.Max(0, FactionObject.CalculateDesiredFiefs(voter) - voter.Fiefs.Count)
                : 0;

            bool strongNeed = candidateDeficit >= 2;
            float value = MathF.Min(
                C.FiefCandidateNeedBonusCap,
                candidateDeficit * C.FiefCandidateNeedPerMissingFief);

            if (candidate != voter && voterDeficit > 0)
                value *= C.FiefCandidateNeedyVoterMultiplier;

            bool isSameFaction = voterFaction != null
                && candidateFaction != null
                && voterFaction.Type == candidateFaction.Type;

            if (voterFaction?.Type == FactionType.Liberty)
                value += strongNeed
                    ? C.FiefCandidatePopulistNeedBonusTwoPlus
                    : C.FiefCandidatePopulistNeedBonusOne;

            if (isSameFaction)
                value += strongNeed
                    ? C.FiefCandidateSameFactionNeedBonusTwoPlus
                    : C.FiefCandidateSameFactionNeedBonusOne;

            if (voterGenerosity > 0)
                value += voterGenerosity * (strongNeed
                    ? C.FiefCandidateGenerousNeedBonusTwoPlus
                    : C.FiefCandidateGenerousNeedBonusOne);
            else if (voterGenerosity < 0)
                value -= -voterGenerosity * (strongNeed
                    ? C.FiefCandidateGreedyNeedPenaltyTwoPlus
                    : C.FiefCandidateGreedyNeedPenaltyOne);

            if (voterMercy > 0)
                value += voterMercy * (strongNeed
                    ? C.FiefCandidateMercifulNeedBonusTwoPlus
                    : C.FiefCandidateMercifulNeedBonusOne);

            if (voterCalculating > 0)
                value -= voterCalculating * (strongNeed
                    ? C.FiefCandidateCalculatingNeedPenaltyTwoPlus
                    : C.FiefCandidateCalculatingNeedPenaltyOne);

            return MathF.Max(0f, value);
        }

        public static float CalculateCandidateFiefNeedScore(int fiefDelta)
        {
            if (fiefDelta > 0)
                return MathF.Min(
                    C.FiefCandidateNeedBonusCap,
                    fiefDelta * C.FiefCandidateNeedPerMissingFief);
            if (fiefDelta == 0)
                return -C.FiefCandidateSatisfiedPenalty;

            return fiefDelta * C.FiefCandidateExcessPenaltyPerFief;
        }

        public static FiefLegalClaimScore CalculateLegalClaimScore(
            Clan voter,
            Clan candidate,
            Settlement settlement,
            FactionObject voterFaction = null)
        {
            FiefLegalClaimScore result = new FiefLegalClaimScore();
            if (candidate == null || settlement == null)
                return result;

            FeudalTitleBehavior titleBehavior = Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>();
            if (titleBehavior == null || !titleBehavior.TryGetBarony(settlement, out FeudalTitleRecord barony) || barony == null)
                return result;

            result.BaseScore = GetDirectClaimBaseScore(titleBehavior, candidate, barony, result);
            if (result.BaseScore <= 0f)
                result.BaseScore = GetParentClaimBaseScore(titleBehavior, candidate, barony, result);

            if (result.BaseScore <= 0f)
                return result;

            float multiplier = GetClaimIdeologyMultiplier(voterFaction);
            result.Score = result.BaseScore * multiplier;

            if (voter != null && voter != candidate && voter.Leader != null && candidate.Leader != null)
            {
                int relation = voter.Leader.GetRelation(candidate.Leader);
                if (relation >= C.FiefClaimFriendRelationThreshold)
                    result.Score += MathF.Min(
                        C.FiefClaimFriendSympathyCap,
                        result.BaseScore * C.FiefClaimFriendSympathyMultiplier);
            }

            return result;
        }

        private static float GetDirectClaimBaseScore(
            FeudalTitleBehavior titleBehavior,
            Clan candidate,
            FeudalTitleRecord barony,
            FiefLegalClaimScore result)
        {
            if (titleBehavior == null || candidate == null || barony == null)
                return 0f;

            if (barony.DeJureHolderClanId == candidate.StringId)
            {
                result.IsDeJureHolder = true;
                result.Strength = FeudalClaimStrength.Strong;
                return C.FiefClaimDeJureBonus;
            }

            if (titleBehavior.HasActiveClaim(candidate, barony, FeudalClaimStrength.Strong))
            {
                result.Strength = FeudalClaimStrength.Strong;
                return C.FiefClaimStrongBonus;
            }

            if (titleBehavior.HasActiveClaim(candidate, barony, FeudalClaimStrength.Weak))
            {
                result.Strength = FeudalClaimStrength.Weak;
                return C.FiefClaimWeakBonus;
            }

            return 0f;
        }

        private static float GetParentClaimBaseScore(
            FeudalTitleBehavior titleBehavior,
            Clan candidate,
            FeudalTitleRecord barony,
            FiefLegalClaimScore result)
        {
            FeudalTitleRecord parent = titleBehavior.GetTitle(barony?.ParentTitleId);
            while (parent != null)
            {
                float parentBase = GetDirectClaimBaseScore(titleBehavior, candidate, parent, result);
                if (parentBase > 0f)
                {
                    result.IsParentTitleClaim = true;
                    return parentBase * C.FiefClaimParentTitleMultiplier;
                }

                parent = titleBehavior.GetTitle(parent.ParentTitleId);
            }

            return 0f;
        }

        private static float GetClaimIdeologyMultiplier(FactionObject voterFaction)
        {
            if (voterFaction == null)
                return C.FiefClaimNeutralMultiplier;

            switch (voterFaction.Type)
            {
                case FactionType.Nobility:
                    return C.FiefClaimAristocratMultiplier;
                case FactionType.Royalists:
                    return C.FiefClaimRoyalistMultiplier;
                case FactionType.Glory:
                    return C.FiefClaimMilitaristMultiplier;
                case FactionType.Liberty:
                    return C.FiefClaimPopulistMultiplier;
                default:
                    return C.FiefClaimNeutralMultiplier;
            }
        }

        public static IEnumerable<Clan> GetValidCandidates(Kingdom kingdom, Clan clanToExclude)
        {
            if (kingdom == null)
                yield break;

            foreach (Clan clan in Clan.All)
            {
                if (IsValidCandidate(clan, kingdom, clanToExclude))
                    yield return clan;
            }
        }

        public static IReadOnlyList<Clan> GetEligibleCandidates(
            Kingdom kingdom,
            Settlement settlement,
            Clan clanToExclude)
        {
            List<Clan> valid = GetValidCandidates(kingdom, clanToExclude).ToList();
            if (valid.Count == 0 || settlement == null)
                return valid;

            List<Clan> ordinary = valid
                .Where(clan => IsWithinOrdinaryOwnershipCeiling(clan) || IsDirectDeJureHolder(clan, settlement))
                .ToList();

            // Do not deadlock a severely concentrated realm that has no normally eligible house.
            return ordinary.Count > 0 ? ordinary : valid;
        }

        public static bool IsWithinOrdinaryOwnershipCeiling(Clan clan)
        {
            if (clan == null)
                return false;

            int desired = FactionObject.CalculateDesiredFiefs(clan);
            return clan.Fiefs.Count <= desired + C.FiefCandidateOrdinaryExcessAllowance;
        }

        public static bool IsDirectDeJureHolder(Clan clan, Settlement settlement)
        {
            if (clan == null || settlement == null)
                return false;

            FeudalTitleBehavior titleBehavior = Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>();
            return titleBehavior != null
                && titleBehavior.TryGetBarony(settlement, out FeudalTitleRecord barony)
                && barony != null
                && barony.DeJureHolderClanId == clan.StringId;
        }

        public static bool IsValidVoter(Clan clan, Kingdom kingdom)
        {
            if (clan == null || clan.Leader == null || kingdom == null)
                return false;

            if (clan.Kingdom != kingdom || clan.IsEliminated || (clan.IsMinorFaction && clan != Clan.PlayerClan) || clan.IsUnderMercenaryService)
                return false;

            return true;
        }

        public static bool IsValidCandidate(Clan clan, Kingdom kingdom, Clan clanToExclude)
        {
            if (clan == null || clan.Leader == null || kingdom == null)
                return false;

            if (clan == clanToExclude || clan.Kingdom != kingdom || clan.IsEliminated || (clan.IsMinorFaction && clan != Clan.PlayerClan) || clan.IsUnderMercenaryService)
                return false;

            return true;
        }

        public static string BuildReasonText(IEnumerable<string> reasonIds, Clan candidate, Clan speakerClan = null)
        {
            bool isSelf = speakerClan != null && candidate == speakerClan;
            List<string> orderedReasonIds = (reasonIds ?? Enumerable.Empty<string>())
                .Where(id => !string.IsNullOrEmpty(id))
                .Distinct()
                .ToList();

            if (isSelf)
            {
                List<string> nonFactionReasons = orderedReasonIds
                    .Where(id => id != "same_faction")
                    .ToList();

                if (nonFactionReasons.Count >= 2)
                    orderedReasonIds = nonFactionReasons;
                else if (orderedReasonIds.Contains("same_faction"))
                    orderedReasonIds = nonFactionReasons.Concat(new[] { "same_faction" }).ToList();
            }

            List<string> clauses = orderedReasonIds
                .Take(3)
                .Select(id => BuildReasonClause(id, candidate, isSelf))
                .Where(text => !string.IsNullOrEmpty(text))
                .ToList();

            if (clauses.Count == 0)
                clauses.Add(BuildReasonClause("judgment", candidate, isSelf));

            if (clauses.Count == 1)
                return clauses[0] + ".";

            if (clauses.Count == 2)
            {
                TextObject joinTwo = new TextObject("{=BC_Fief_Delib_ReasonJoinTwo}{REASON_1}, and {REASON_2}.");
                joinTwo.SetTextVariable("REASON_1", clauses[0]);
                joinTwo.SetTextVariable("REASON_2", clauses[1]);
                return joinTwo.ToString();
            }

            TextObject joinThree = new TextObject("{=BC_Fief_Delib_ReasonJoinThree}{REASON_1}, {REASON_2}, and {REASON_3}.");
            joinThree.SetTextVariable("REASON_1", clauses[0]);
            joinThree.SetTextVariable("REASON_2", clauses[1]);
            joinThree.SetTextVariable("REASON_3", clauses[2]);
            return joinThree.ToString();
        }

        private static string BuildReasonClause(string reasonId, Clan candidate, bool isSelf)
        {
            TextObject text;
            switch (reasonId)
            {
                case "self":
                    text = isSelf
                        ? new TextObject("{=BC_Fief_Delib_ReasonSelf}my house seeks this prize for itself")
                        : new TextObject("{=BC_Fief_Delib_ReasonSelfOther}their house presses its own interest");
                    break;
                case "court_claim":
                    text = new TextObject("{=BC_Fief_Delib_ReasonCourtClaim}I have pledged my support to this house's claim");
                    break;
                case "legal_claim":
                    text = isSelf
                        ? new TextObject("{=BC_Fief_Delib_ReasonLegalClaimSelf}my house has a lawful claim to these lands")
                        : new TextObject("{=BC_Fief_Delib_ReasonLegalClaim}their house has a lawful claim to these lands");
                    break;
                case "capturer":
                    text = isSelf
                        ? new TextObject("{=BC_Fief_Delib_ReasonCapturerSelf}I captured it by right of arms")
                        : new TextObject("{=BC_Fief_Delib_ReasonCapturer}{CANDIDATE_NAME} captured it by right of arms");
                    break;
                case "landless":
                    text = isSelf
                        ? new TextObject("{=BC_Fief_Delib_ReasonLandlessSelf}my house has no lands of its own")
                        : new TextObject("{=BC_Fief_Delib_ReasonLandless}their house has no lands of its own");
                    break;
                case "need":
                    text = isSelf
                        ? new TextObject("{=BC_Fief_Delib_ReasonNeedSelf}my clan lacks the lands expected of its station")
                        : new TextObject("{=BC_Fief_Delib_ReasonNeed}their clan lacks the lands expected of its station");
                    break;
                case "same_faction":
                    text = isSelf
                        ? new TextObject("{=BC_Fief_Delib_ReasonFactionSelf}my faction in court stands behind my claim")
                        : new TextObject("{=BC_Fief_Delib_ReasonFaction}they stand with my faction in court");
                    break;
                case "culture":
                    text = isSelf
                        ? new TextObject("{=BC_Fief_Delib_ReasonCultureSelf}my people know these lands")
                        : new TextObject("{=BC_Fief_Delib_ReasonCulture}their people know these lands");
                    break;
                case "friend":
                    text = isSelf
                        ? new TextObject("{=BC_Fief_Delib_ReasonFriendSelf}I trust my own claim")
                        : new TextObject("{=BC_Fief_Delib_ReasonFriend}I trust them personally");
                    break;
                case "crown_loyalty":
                    text = isSelf
                        ? new TextObject("{=BC_Fief_Delib_ReasonCrownSelf}my loyalty to the crown is well known")
                        : new TextObject("{=BC_Fief_Delib_ReasonCrown}their loyalty to the crown is well known");
                    break;
                case "strong_house":
                    text = isSelf
                        ? new TextObject("{=BC_Fief_Delib_ReasonStrongSelf}I have the strength to defend it")
                        : new TextObject("{=BC_Fief_Delib_ReasonStrong}they have the strength to defend it");
                    break;
                case "proper_rank":
                    text = isSelf
                        ? new TextObject("{=BC_Fief_Delib_ReasonRankSelf}my house has the standing to govern it")
                        : new TextObject("{=BC_Fief_Delib_ReasonRank}their house has the standing to govern it");
                    break;
                case "low_house":
                    text = isSelf
                        ? new TextObject("{=BC_Fief_Delib_ReasonLowHouseSelf}my house deserves a chance to rise")
                        : new TextObject("{=BC_Fief_Delib_ReasonLowHouse}a lesser house deserves a chance to rise");
                    break;
                case "popular_support":
                    text = isSelf
                        ? new TextObject("{=BC_Fief_Delib_ReasonPopularSupportSelf}my house has broad support in the realm")
                        : new TextObject("{=BC_Fief_Delib_ReasonPopularSupport}their house has broad support in the realm");
                    break;
                case "marriage_alliance":
                    text = isSelf
                        ? new TextObject("{=BC_Fief_Delib_ReasonMarriageAllianceSelf}my house is bound to mine by marriage")
                        : new TextObject("{=BC_Fief_Delib_ReasonMarriageAlliance}their house is bound to mine by marriage");
                    break;
                case "royal_marriage":
                    text = isSelf
                        ? new TextObject("{=BC_Fief_Delib_ReasonRoyalMarriageSelf}my house is bound to the royal family")
                        : new TextObject("{=BC_Fief_Delib_ReasonRoyalMarriage}their house is bound to the royal family");
                    break;
                case "anti_crown":
                    text = new TextObject("{=BC_Fief_Delib_ReasonAntiCrown}the crown's favorites should not take every prize");
                    break;
                case "leader_pull":
                    text = isSelf
                        ? new TextObject("{=BC_Fief_Delib_ReasonLeaderPullSelf}my faction leader trusts my claim")
                        : new TextObject("{=BC_Fief_Delib_ReasonLeaderPull}my faction leader trusts their claim");
                    break;
                default:
                    text = isSelf
                        ? new TextObject("{=BC_Fief_Delib_ReasonJudgmentSelf}my claim is the strongest in this matter")
                        : new TextObject("{=BC_Fief_Delib_ReasonJudgment}their claim seems strongest to me");
                    break;
            }

            text.SetTextVariable("CANDIDATE_NAME", candidate?.Leader?.Name ?? candidate?.Name ?? new TextObject(""));
            return text.ToString();
        }
    }
}
