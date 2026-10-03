using System.Collections.Generic;
using System.Linq;
using BellumCivile.Behaviors;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using C = BellumCivile.BellumCivileConstants;

namespace BellumCivile
{
    internal enum CivilWarPlayerCallReason
    {
        RebelFactionMember,
        CourtFactionMember,
        DirectVassal
    }

    internal sealed class CivilWarPlayerCallContext
    {
        public CivilWarPlayerCallContext(CivilWarPlayerCallReason reason, Clan sponsorClan)
        {
            Reason = reason;
            SponsorClan = sponsorClan;
        }

        public CivilWarPlayerCallReason Reason { get; }
        public Clan SponsorClan { get; }
    }

    internal enum CivilWarSolidarityReason
    {
        CrownLoyalty,
        RebelliousIntent,
        Friendship,
        MarriageAlliance,
        DynasticKin,
        DirectVassal
    }

    internal sealed class CivilWarSolidarityAssessment
    {
        public CivilWarSolidarityAssessment(
            Clan candidate,
            Clan sponsor,
            bool directVassal,
            float score,
            float joinChance,
            CivilWarSolidarityReason primaryReason)
        {
            Candidate = candidate;
            Sponsor = sponsor;
            DirectVassal = directVassal;
            Score = score;
            JoinChance = joinChance;
            PrimaryReason = primaryReason;
        }

        public Clan Candidate { get; }
        public Clan Sponsor { get; }
        public bool DirectVassal { get; }
        public float Score { get; }
        public float JoinChance { get; }
        public CivilWarSolidarityReason PrimaryReason { get; }
    }

    /// <summary>
    /// Reconciles personal rebellious intent with feudal obligations when a civil war
    /// actually begins. The hierarchy is evaluated in passes so subordinate chains can
    /// follow a rebel liege without requiring a separate daily scheduler.
    /// </summary>
    internal static class CivilWarSolidarityHelper
    {
        public static void RallyAiSupporters(FactionObject rebelFaction)
        {
            Kingdom kingdom = rebelFaction?.ParentKingdom;
            FactionManagerBehavior factionManager = Campaign.Current?.GetCampaignBehavior<FactionManagerBehavior>();
            IdeologyBehavior ideologyBehavior = Campaign.Current?.GetCampaignBehavior<IdeologyBehavior>();
            if (rebelFaction == null || kingdom == null || factionManager == null || ideologyBehavior == null)
                return;

            HashSet<Clan> resolvedVassals = new HashSet<Clan>();
            HashSet<Clan> evaluatedRebelliousOutsiders = new HashSet<Clan>();

            for (int pass = 0; pass < C.CivilWarSolidarityHierarchyPassLimit; pass++)
            {
                bool joinedThisPass = false;

                foreach (Clan candidate in kingdom.Clans.ToList())
                {
                    if (!IsEligibleCandidate(candidate, rebelFaction, factionManager) || resolvedVassals.Contains(candidate))
                        continue;

                    Clan sponsor = FindImmediateRebelLiege(candidate, rebelFaction.Members);
                    if (sponsor == null)
                        continue;

                    resolvedVassals.Add(candidate);
                    CivilWarSolidarityAssessment assessment = AssessSupport(
                        candidate,
                        sponsor,
                        kingdom,
                        factionManager,
                        ideologyBehavior,
                        directVassal: true);
                    if (RollToJoin(assessment.Score))
                    {
                        if (TryAddSupporter(candidate, sponsor, rebelFaction, factionManager, assessment.Score, assessment.PrimaryReason, directVassal: true))
                            joinedThisPass = true;
                    }
                    else
                    {
                        ApplyRefusalConsequences(candidate, sponsor, rebelFaction, assessment.Score);
                    }
                }

                if (joinedThisPass)
                    continue;

                foreach (Clan candidate in kingdom.Clans.ToList())
                {
                    if (!IsEligibleCandidate(candidate, rebelFaction, factionManager)
                        || evaluatedRebelliousOutsiders.Contains(candidate)
                        || FindImmediateRebelLiege(candidate, rebelFaction.Members) != null)
                    {
                        continue;
                    }

                    evaluatedRebelliousOutsiders.Add(candidate);
                    CivilWarSolidarityAssessment assessment = FindBestSupportAssessment(
                        candidate,
                        rebelFaction.Members,
                        kingdom,
                        factionManager,
                        ideologyBehavior,
                        directVassalOnly: false);
                    if (assessment == null)
                        continue;

                    if (RollToJoin(assessment.Score))
                    {
                        if (TryAddSupporter(candidate, assessment.Sponsor, rebelFaction, factionManager, assessment.Score, assessment.PrimaryReason, directVassal: false))
                            joinedThisPass = true;
                    }
                    else
                    {
                        ApplyOutsiderRefusalConsequences(candidate, assessment, rebelFaction);
                    }
                }

                if (!joinedThisPass)
                    break;
            }
        }

        public static CivilWarPlayerCallContext BuildPlayerCallContext(
            FactionObject rebelFaction,
            bool playerWasRebelFactionMember,
            CivilWarPlayerCallContext explicitContext)
        {
            Clan playerClan = Clan.PlayerClan;
            Kingdom kingdom = rebelFaction?.ParentKingdom;
            if (playerClan?.Leader == null || kingdom == null || playerClan.Kingdom != kingdom
                || playerClan == kingdom.RulingClan || playerClan == rebelFaction.Leader
                || playerClan.IsUnderMercenaryService)
            {
                return null;
            }

            Clan immediateLiege = FindImmediateRebelLiege(playerClan, rebelFaction.Members);
            if (immediateLiege != null)
                return new CivilWarPlayerCallContext(CivilWarPlayerCallReason.DirectVassal, immediateLiege);

            if (explicitContext != null)
                return explicitContext;

            return playerWasRebelFactionMember
                ? new CivilWarPlayerCallContext(CivilWarPlayerCallReason.RebelFactionMember, rebelFaction.Leader)
                : null;
        }

        public static Clan FindImmediateRebelLiege(Clan possibleVassal, IEnumerable<Clan> rebelClans)
        {
            FeudalTitleBehavior titleBehavior = Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>();
            if (titleBehavior == null || possibleVassal == null)
                return null;

            return (rebelClans ?? Enumerable.Empty<Clan>())
                .Where(clan => clan != null && clan != possibleVassal)
                .FirstOrDefault(clan => IsImmediateVassalOf(titleBehavior, possibleVassal, clan));
        }

        public static bool IsImmediateVassalOf(FeudalTitleBehavior titleBehavior, Clan possibleVassal, Clan possibleLiege)
        {
            if (titleBehavior == null || possibleVassal == null || possibleLiege == null || possibleVassal == possibleLiege)
                return false;

            IEnumerable<FeudalTitleRecord> titles = titleBehavior.GetTitlesHeldByClan(possibleVassal, deJure: true)
                .Concat(titleBehavior.GetTitlesHeldByClan(possibleVassal, deJure: false))
                .Where(title => title != null && title.IsActive)
                .GroupBy(title => title.TitleId)
                .Select(group => group.First());

            foreach (FeudalTitleRecord title in titles)
            {
                FeudalTitleRecord deJureParent = titleBehavior.GetParentTitle(title, FeudalHierarchyMode.DeJure);
                FeudalTitleRecord deFactoParent = titleBehavior.GetParentTitle(title, FeudalHierarchyMode.DeFacto);
                if (ParentBelongsToClan(deJureParent, possibleLiege) || ParentBelongsToClan(deFactoParent, possibleLiege))
                    return true;
            }

            return false;
        }

        public static void JoinPlayerRebellion(FactionObject rebelFaction, CivilWarPlayerCallContext context)
        {
            Clan playerClan = Clan.PlayerClan;
            Kingdom parentKingdom = rebelFaction?.ParentKingdom;
            Kingdom rebelKingdom = rebelFaction?.GetRebelKingdom();
            FactionManagerBehavior factionManager = Campaign.Current?.GetCampaignBehavior<FactionManagerBehavior>();
            if (playerClan?.Leader == null || parentKingdom == null || rebelKingdom == null || factionManager == null)
                return;

            RemoveFromCompetingWarmingFaction(playerClan, rebelFaction, factionManager);
            rebelFaction.AddMember(playerClan);
            rebelFaction.MoveClanToKingdomPreservingCivilWarInfluence(playerClan, rebelKingdom, preserveCustomBanner: true, showNotification: false);
            Campaign.Current?.GetCampaignBehavior<ClaimFeudBehavior>()
                ?.CooldownFeudsForCivilWarParticipants(new[] { playerClan }, $"joined civil war led by {rebelFaction.Name}");

            Hero sponsor = context?.SponsorClan?.Leader ?? rebelFaction.Leader?.Leader;
            Hero ruler = parentKingdom.RulingClan?.Leader;
            if (sponsor != null && sponsor != playerClan.Leader)
                RelationMemoryService.ApplyChange(playerClan.Leader, sponsor, C.CoalitionRebelLeaderBonus, true,
                    RelationMemorySources.HonoredCallToArms, 10f, RelationMemoryScope.House, rebelFaction.Name?.ToString());
            if (ruler != null && ruler != playerClan.Leader)
                RelationMemoryService.ApplyChange(playerClan.Leader, ruler, C.CivilWarSolidarityJoinRulerRelation, false,
                    RelationMemorySources.DefectedFromMyCause, 20f, RelationMemoryScope.House, rebelFaction.Name?.ToString());
        }

        public static void KeepPlayerLoyal(FactionObject rebelFaction, CivilWarPlayerCallContext context)
        {
            Clan playerClan = Clan.PlayerClan;
            Kingdom kingdom = rebelFaction?.ParentKingdom;
            if (playerClan?.Leader == null || kingdom == null)
                return;

            if (rebelFaction.Members.Contains(playerClan))
                rebelFaction.RemoveMember(playerClan);
            rebelFaction.RecordLoyaltyDeclaration(playerClan);

            Hero sponsor = context?.SponsorClan?.Leader ?? rebelFaction.Leader?.Leader;
            Hero ruler = kingdom.RulingClan?.Leader;
            int sponsorPenalty = context?.Reason == CivilWarPlayerCallReason.DirectVassal
                ? C.CivilWarSolidarityRefuseLiegeRelation
                : -C.CoalitionLoyalistRelPenalty;

            if (sponsor != null && sponsor != playerClan.Leader)
                RelationMemoryService.ApplyChange(playerClan.Leader, sponsor, sponsorPenalty, true,
                    RelationMemorySources.RefusedCallToArms, 10f, RelationMemoryScope.House, rebelFaction.Name?.ToString());
            if (ruler != null && ruler != playerClan.Leader)
                RelationMemoryService.ApplyChange(playerClan.Leader, ruler, C.CoalitionLoyalistKingBonus, true,
                    RelationMemorySources.HonoredCallToArms, 10f, RelationMemoryScope.House, rebelFaction.Name?.ToString());
        }

        internal static bool IsEligibleCandidate(Clan candidate, FactionObject rebelFaction, FactionManagerBehavior factionManager)
        {
            Kingdom kingdom = rebelFaction.ParentKingdom;
            if (candidate == null || candidate == Clan.PlayerClan || candidate == kingdom.RulingClan
                || candidate.Kingdom != kingdom || candidate.IsEliminated || candidate.IsMinorFaction
                || candidate.IsUnderMercenaryService || candidate.Leader == null || candidate.Leader.IsDead
                || rebelFaction.Members.Contains(candidate) || factionManager.IsClanPacified(candidate))
            {
                return false;
            }

            FactionObject existing = factionManager.GetRebelFaction(candidate);
            return existing == null || existing == rebelFaction || !existing.IsCivilWarActive();
        }

        internal static bool IsEligibleProjectionCandidate(
            Clan candidate,
            Kingdom kingdom,
            ISet<Clan> committedRebels,
            FactionObject rebelFaction,
            FactionManagerBehavior factionManager,
            Clan projectedRulingClan = null)
        {
            if (candidate == null || candidate == Clan.PlayerClan || kingdom == null || committedRebels == null
                || candidate == (projectedRulingClan ?? kingdom.RulingClan) || candidate.Kingdom != kingdom
                || candidate.IsEliminated || candidate.IsMinorFaction || candidate.IsUnderMercenaryService
                || candidate.Leader == null || candidate.Leader.IsDead || committedRebels.Contains(candidate)
                || factionManager?.IsClanPacified(candidate) == true)
            {
                return false;
            }

            FactionObject existing = factionManager?.GetRebelFaction(candidate);
            return existing == null || existing == rebelFaction || !existing.IsCivilWarActive();
        }

        internal static bool MeetsOutsiderIntentRequirement(
            Clan candidate,
            IdeologyBehavior ideologyBehavior,
            float? rebellionIntent = null)
        {
            return candidate != null
                && ideologyBehavior != null
                && (rebellionIntent ?? ideologyBehavior.CalculateRebellionScore(candidate))
                    >= C.CivilWarSolidarityRebelliousIntentThreshold;
        }

        internal static bool CanAnswerOutsiderCall(
            Clan candidate,
            Clan sponsor,
            Kingdom kingdom,
            IdeologyBehavior ideologyBehavior,
            float? rebellionIntent = null,
            Clan projectedRulingClan = null)
        {
            if (candidate?.Leader == null || sponsor?.Leader == null || kingdom == null)
                return false;

            if (MeetsOutsiderIntentRequirement(candidate, ideologyBehavior, rebellionIntent))
                return true;

            // A marriage into the ruling house is an obligation to the crown, not a
            // second invitation that can be counted by a rebel coalition.
            if (MarriageAllianceHelper.HasMarriageAlliance(candidate, projectedRulingClan ?? kingdom.RulingClan))
                return false;

            if (MarriageAllianceHelper.HasMarriageAlliance(candidate, sponsor))
                return true;

            if (AreCloseDynasticKin(candidate, sponsor))
                return true;

            return candidate.Leader.GetRelation(sponsor.Leader) >= C.TreasonSolidarityFriendRelationThreshold;
        }

        internal static CivilWarSolidarityAssessment AssessSupport(
            Clan candidate,
            Clan sponsor,
            Kingdom kingdom,
            FactionManagerBehavior factionManager,
            IdeologyBehavior ideologyBehavior,
            bool directVassal,
            float? rebellionIntent = null,
            Hero projectedSovereign = null,
            Hero personalSponsor = null)
        {
            if (candidate == null || sponsor == null || kingdom == null || factionManager == null || ideologyBehavior == null)
                return null;

            float score = CalculateSupportScore(
                candidate,
                sponsor,
                kingdom,
                factionManager,
                ideologyBehavior,
                directVassal,
                rebellionIntent,
                projectedSovereign, personalSponsor);
            return new CivilWarSolidarityAssessment(
                candidate,
                sponsor,
                directVassal,
                score,
                CalculateJoinChance(score),
                DeterminePrimaryReason(candidate, sponsor, factionManager, ideologyBehavior, directVassal));
        }

        internal static bool CanAnswerCrownCall(
            Clan candidate,
            Clan sponsor,
            Kingdom kingdom,
            FactionManagerBehavior factionManager,
            FactionObject royalistFaction)
        {
            if (candidate?.Leader == null || sponsor?.Leader == null || kingdom?.RulingClan?.Leader == null)
                return false;

            // The sponsor is significant: a tie to the ruler can draw support when
            // the ruler is the sponsor, while a tie to another committed Royalist
            // can propagate that member's own network. Treating any crown tie as a
            // tie to every sponsor distorts both the projected root and its strength.
            if (MarriageAllianceHelper.HasMarriageAlliance(candidate, sponsor))
                return true;

            if (candidate.Leader.GetRelation(sponsor.Leader) >= C.TreasonSolidarityFriendRelationThreshold)
                return true;

            // Court alignment can strengthen a genuine personal or feudal call below,
            // but belonging to the Royalists is not itself a call-to-arms obligation.
            return false;
        }

        internal static CivilWarSolidarityAssessment AssessCrownSupport(
            Clan candidate,
            Clan sponsor,
            Kingdom kingdom,
            FactionManagerBehavior factionManager,
            IdeologyBehavior ideologyBehavior,
            FactionObject royalistFaction,
            bool directVassal,
            float? rebellionIntent = null)
        {
            if (candidate == null || sponsor == null || kingdom?.RulingClan?.Leader == null
                || factionManager == null || ideologyBehavior == null)
            {
                return null;
            }

            Clan rulingClan = kingdom.RulingClan;
            Hero ruler = rulingClan.Leader;
            float score = directVassal ? C.TreasonSolidarityDirectVassalSupport : 0f;

            bool sponsorMarriage = MarriageAllianceHelper.HasMarriageAlliance(candidate, sponsor);
            bool crownMarriage = MarriageAllianceHelper.HasMarriageAlliance(candidate, rulingClan);
            if (sponsorMarriage)
                score += C.TreasonSolidarityMarriageSupport;
            if (crownMarriage && sponsor != rulingClan)
                score += C.TreasonSolidarityMarriageSupport;

            int sponsorRelation = candidate.Leader.GetRelation(sponsor.Leader);
            if (sponsorRelation >= C.TreasonSolidarityFriendRelationThreshold)
            {
                score += C.TreasonSolidarityFriendSupportBase
                    + ((sponsorRelation - C.TreasonSolidarityFriendRelationThreshold) * C.TreasonSolidarityFriendSupportScale);
            }

            int rulerRelation = candidate.Leader.GetRelation(ruler);
            if (sponsor != rulingClan && rulerRelation >= C.TreasonSolidarityFriendRelationThreshold)
            {
                score += C.TreasonSolidarityFriendSupportBase
                    + ((rulerRelation - C.TreasonSolidarityFriendRelationThreshold) * C.TreasonSolidarityFriendSupportScale);
            }

            FactionObject candidateIdeology = factionManager.GetIdeologicalFaction(candidate);
            if (candidateIdeology == royalistFaction)
                score += C.TreasonSolidaritySameFactionSupport;
            float rebelliousIntent = rebellionIntent ?? ideologyBehavior.CalculateRebellionScore(candidate);
            score -= TaleWorlds.Library.MathF.Min(
                C.CivilWarSolidarityMaxIntentContribution,
                TaleWorlds.Library.MathF.Max(0f, rebelliousIntent) * C.CivilWarSolidarityIntentContributionScale);

            score += TaleWorlds.Library.MathF.Clamp(
                rulerRelation * C.CivilWarSolidarityRelationComparisonScale,
                -C.CivilWarSolidarityMaxRelationComparison,
                C.CivilWarSolidarityMaxRelationComparison);

            CivilWarSolidarityReason reason;
            if (directVassal)
                reason = CivilWarSolidarityReason.DirectVassal;
            else if (sponsorMarriage || crownMarriage)
                reason = CivilWarSolidarityReason.MarriageAlliance;
            else if (sponsorRelation >= C.TreasonSolidarityFriendRelationThreshold
                     || rulerRelation >= C.TreasonSolidarityFriendRelationThreshold)
                reason = CivilWarSolidarityReason.Friendship;
            else
                reason = CivilWarSolidarityReason.CrownLoyalty;

            return new CivilWarSolidarityAssessment(
                candidate,
                sponsor,
                directVassal,
                score,
                CalculateJoinChance(score),
                reason);
        }

        internal static CivilWarSolidarityAssessment FindBestSupportAssessment(
            Clan candidate,
            IEnumerable<Clan> sponsors,
            Kingdom kingdom,
            FactionManagerBehavior factionManager,
            IdeologyBehavior ideologyBehavior,
            bool directVassalOnly)
        {
            FeudalTitleBehavior titleBehavior = Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>();
            CivilWarSolidarityAssessment best = null;
            float rebellionIntent = ideologyBehavior?.CalculateRebellionScore(candidate) ?? 0f;

            foreach (Clan sponsor in sponsors ?? Enumerable.Empty<Clan>())
            {
                if (sponsor == null || sponsor == candidate || sponsor.IsEliminated)
                    continue;

                bool directVassal = IsImmediateVassalOf(titleBehavior, candidate, sponsor);
                if (directVassalOnly && !directVassal)
                    continue;
                if (!directVassal
                    && !CanAnswerOutsiderCall(candidate, sponsor, kingdom, ideologyBehavior, rebellionIntent))
                    continue;

                CivilWarSolidarityAssessment assessment = AssessSupport(
                    candidate,
                    sponsor,
                    kingdom,
                    factionManager,
                    ideologyBehavior,
                    directVassal,
                    rebellionIntent);
                if (assessment != null && (best == null || assessment.Score > best.Score))
                    best = assessment;
            }

            return best;
        }

        private static float CalculateSupportScore(
            Clan candidate,
            Clan sponsor,
            Kingdom kingdom,
            FactionManagerBehavior factionManager,
            IdeologyBehavior ideologyBehavior,
            bool directVassal,
            float? rebellionIntent,
            Hero projectedSovereign = null,
            Hero personalSponsor = null)
        {
            float score = directVassal ? C.TreasonSolidarityDirectVassalSupport : 0f;

            if (personalSponsor != null ? personalSponsor.Spouse?.Clan == candidate : MarriageAllianceHelper.HasMarriageAlliance(candidate, sponsor))
                score += C.TreasonSolidarityMarriageSupport;

            if (personalSponsor != null ? AreImmediateBloodKin(candidate.Leader, personalSponsor) : AreCloseDynasticKin(candidate, sponsor))
                score += C.CivilWarSolidarityDynasticKinSupport;

            int sponsorRelation = personalSponsor != null && candidate.Leader != null
                ? CharacterRelationManager.GetHeroRelation(candidate.Leader, personalSponsor)
                : candidate.Leader?.GetRelation(sponsor?.Leader) ?? 0;
            if (sponsorRelation >= C.TreasonSolidarityFriendRelationThreshold)
            {
                score += C.TreasonSolidarityFriendSupportBase
                    + ((sponsorRelation - C.TreasonSolidarityFriendRelationThreshold) * C.TreasonSolidarityFriendSupportScale);
            }

            FactionObject candidateIdeology = factionManager.GetIdeologicalFaction(candidate);
            FactionObject sponsorIdeology = personalSponsor == null ? factionManager.GetIdeologicalFaction(sponsor) : null;
            if (candidateIdeology != null)
            {
                if (candidateIdeology == sponsorIdeology)
                    score += C.TreasonSolidaritySameFactionSupport;

                if (candidateIdeology.Mood <= C.GrandCoalitionJoinMoodThreshold)
                    score += C.TreasonSolidarityRebelliousFactionSupport;
                else if (candidateIdeology.Mood <= C.MoodThresholdUnhappy)
                    score += C.TreasonSolidarityAngryFactionSupport;

            }

            float rebelliousIntent = rebellionIntent ?? ideologyBehavior.CalculateRebellionScore(candidate);
            score += MathF.Min(
                C.CivilWarSolidarityMaxIntentContribution,
                MathF.Max(0f, rebelliousIntent) * C.CivilWarSolidarityIntentContributionScale);

            Hero ruler = projectedSovereign ?? kingdom.RulingClan?.Leader;
            int rulerRelation = projectedSovereign != null && candidate.Leader != null
                ? CharacterRelationManager.GetHeroRelation(candidate.Leader, projectedSovereign)
                : candidate.Leader?.GetRelation(ruler) ?? 0;
            float relationComparison = (sponsorRelation - rulerRelation) * C.CivilWarSolidarityRelationComparisonScale;
            score += MathF.Clamp(
                relationComparison,
                -C.CivilWarSolidarityMaxRelationComparison,
                C.CivilWarSolidarityMaxRelationComparison);

            if (MarriageAllianceHelper.HasMarriageAlliance(candidate, projectedSovereign?.Clan ?? kingdom.RulingClan))
                score -= C.CivilWarSolidarityCrownMarriagePenalty;
            if (rulerRelation >= C.TreasonSolidarityRulerLoyalRelationThreshold)
                score -= C.CivilWarSolidarityStrongRulerRelationPenalty;

            return score;
        }

        private static CivilWarSolidarityReason DeterminePrimaryReason(
            Clan candidate,
            Clan sponsor,
            FactionManagerBehavior factionManager,
            IdeologyBehavior ideologyBehavior,
            bool directVassal)
        {
            if (directVassal)
                return CivilWarSolidarityReason.DirectVassal;

            if (MarriageAllianceHelper.HasMarriageAlliance(candidate, sponsor))
                return CivilWarSolidarityReason.MarriageAlliance;

            if (AreCloseDynasticKin(candidate, sponsor))
                return CivilWarSolidarityReason.DynasticKin;

            int sponsorRelation = candidate?.Leader?.GetRelation(sponsor?.Leader) ?? 0;
            if (sponsorRelation >= C.TreasonSolidarityFriendRelationThreshold)
                return CivilWarSolidarityReason.Friendship;

            return CivilWarSolidarityReason.RebelliousIntent;
        }

        internal static float CalculateJoinChance(float score)
        {
            if (score >= C.TreasonSolidarityJoinThreshold)
                return 1f;
            if (score < C.TreasonSolidarityRollThreshold)
                return 0f;

            return MathF.Clamp(score / 100f, 0f, 1f);
        }

        private static bool RollToJoin(float score)
        {
            float chance = CalculateJoinChance(score);
            return chance >= 1f || (chance > 0f && MBRandom.RandomFloat <= chance);
        }

        private static bool TryAddSupporter(
            Clan supporter,
            Clan sponsor,
            FactionObject rebelFaction,
            FactionManagerBehavior factionManager,
            float score,
            CivilWarSolidarityReason reason,
            bool directVassal)
        {
            FactionObject existing = factionManager.GetRebelFaction(supporter);
            if (existing != null && existing != rebelFaction)
            {
                if (existing.IsCivilWarActive())
                    return false;

                if (existing.Leader == supporter)
                    factionManager.RemoveFaction(existing);
                else
                    existing.RemoveMember(supporter);
            }

            rebelFaction.AddMember(supporter);
            Hero ruler = rebelFaction.ParentKingdom?.RulingClan?.Leader;
            if (supporter.Leader != null && sponsor?.Leader != null)
                RelationMemoryService.ApplyChange(supporter.Leader, sponsor.Leader, C.CivilWarSolidarityJoinSponsorRelation, false,
                    RelationMemorySources.HonoredCallToArms, 10f, RelationMemoryScope.House, rebelFaction.Name?.ToString());
            if (supporter.Leader != null && ruler != null)
                RelationMemoryService.ApplyChange(supporter.Leader, ruler, C.CivilWarSolidarityJoinRulerRelation, false,
                    RelationMemorySources.DefectedFromMyCause, 20f, RelationMemoryScope.House, rebelFaction.Name?.ToString());

            TextObject text = directVassal
                ? new TextObject("{=BC_CivilWar_VassalJoined}Bound by fealty, {VASSAL_NAME} of the {VASSAL_CLAN} has answered {LIEGE_NAME}'s summons and raised their banners against the crown.")
                : new TextObject("{=BC_CivilWar_RebelliousLordJoined}Sensing the realm fracture, {VASSAL_NAME} of the {VASSAL_CLAN} has cast their lot in with {LIEGE_NAME}'s rebellion.");
            text.SetTextVariable("VASSAL_NAME", supporter.Leader?.Name ?? supporter.Name);
            text.SetTextVariable("VASSAL_CLAN", supporter.Name);
            text.SetTextVariable("LIEGE_NAME", sponsor?.Leader?.Name ?? sponsor?.Name ?? new TextObject("?"));
            BellumCivileNotifications.Show(
                text,
                BellumNotificationColors.Rebellion,
                primaryKingdom: rebelFaction.ParentKingdom,
                primaryClan: supporter,
                secondaryClan: sponsor);
            Campaign.Current?.GetCampaignBehavior<ConflictCallResponseBehavior>()
                ?.QueueCivilWarResponse(rebelFaction, supporter, sponsor, reason, accepted: true);

            BellumCivileLogger.Log(
                $"Civil-war solidarity supporter joined: faction={rebelFaction.Name}; supporter={supporter.StringId}; sponsor={sponsor?.StringId ?? "null"}; direct_vassal={directVassal}; score={score:0.0}.");
            return true;
        }

        private static void ApplyRefusalConsequences(Clan vassal, Clan liege, FactionObject rebelFaction, float score)
        {
            Kingdom kingdom = rebelFaction?.ParentKingdom;
            Hero ruler = kingdom?.RulingClan?.Leader;
            if (vassal?.Leader != null && liege?.Leader != null)
                RelationMemoryService.ApplyChange(vassal.Leader, liege.Leader, C.CivilWarSolidarityRefuseLiegeRelation, false,
                    RelationMemorySources.RefusedCallToArms, 10f, RelationMemoryScope.House, rebelFaction.Name?.ToString());
            if (vassal?.Leader != null && ruler != null)
                RelationMemoryService.ApplyChange(vassal.Leader, ruler, C.CivilWarSolidarityRefuseRulerRelation, false,
                    RelationMemorySources.HonoredCallToArms, 10f, RelationMemoryScope.House, rebelFaction.Name?.ToString());

            TextObject text = new TextObject("{=BC_CivilWar_VassalRefused}{VASSAL_NAME} of the {VASSAL_CLAN} has refused {LIEGE_NAME}'s summons and declared their loyalty to the crown.");
            text.SetTextVariable("VASSAL_NAME", vassal?.Leader?.Name ?? vassal?.Name ?? new TextObject("?"));
            text.SetTextVariable("VASSAL_CLAN", vassal?.Name ?? new TextObject("?"));
            text.SetTextVariable("LIEGE_NAME", liege?.Leader?.Name ?? liege?.Name ?? new TextObject("?"));
            BellumCivileNotifications.Show(
                text,
                BellumNotificationColors.Warning,
                primaryKingdom: kingdom,
                primaryClan: vassal,
                secondaryClan: liege);
            Campaign.Current?.GetCampaignBehavior<ConflictCallResponseBehavior>()
                ?.QueueCivilWarResponse(rebelFaction, vassal, liege, CivilWarSolidarityReason.DirectVassal, accepted: false);

            BellumCivileLogger.Log(
                $"Civil-war vassal refused summons: kingdom={kingdom?.StringId ?? "null"}; vassal={vassal?.StringId ?? "null"}; liege={liege?.StringId ?? "null"}; score={score:0.0}.");
        }

        private static void ApplyOutsiderRefusalConsequences(
            Clan candidate,
            CivilWarSolidarityAssessment assessment,
            FactionObject rebelFaction)
        {
            if (candidate?.Leader == null || assessment?.Sponsor?.Leader == null)
                return;

            int relationLoss = assessment.PrimaryReason == CivilWarSolidarityReason.MarriageAlliance
                ? C.CivilWarSolidarityRefuseMarriageRelation
                : assessment.PrimaryReason == CivilWarSolidarityReason.DynasticKin
                    ? C.CivilWarSolidarityRefuseKinRelation
                : assessment.PrimaryReason == CivilWarSolidarityReason.Friendship
                    ? C.CivilWarSolidarityRefuseFriendRelation
                    : 0;
            if (relationLoss != 0)
            {
                RelationMemoryService.ApplyChange(
                    candidate.Leader,
                    assessment.Sponsor.Leader,
                    relationLoss,
                    false,
                    RelationMemorySources.RefusedCallToArms,
                    10f,
                    RelationMemoryScope.House,
                    rebelFaction.Name?.ToString());
            }

            TextObject text = new TextObject("{=BC_CivilWar_AllyRefused}{VASSAL_NAME} of the {VASSAL_CLAN} has declined {LIEGE_NAME}'s call to arms and will remain apart from the rebellion.");
            text.SetTextVariable("VASSAL_NAME", candidate.Leader.Name);
            text.SetTextVariable("VASSAL_CLAN", candidate.Name);
            text.SetTextVariable("LIEGE_NAME", assessment.Sponsor.Leader.Name);
            BellumCivileNotifications.Show(
                text,
                BellumNotificationColors.Warning,
                primaryKingdom: rebelFaction?.ParentKingdom,
                primaryClan: candidate,
                secondaryClan: assessment.Sponsor);
            Campaign.Current?.GetCampaignBehavior<ConflictCallResponseBehavior>()
                ?.QueueCivilWarResponse(rebelFaction, candidate, assessment.Sponsor, assessment.PrimaryReason, accepted: false);
            BellumCivileLogger.Log(
                $"Civil-war ally refused solidarity call: candidate={candidate.StringId}; sponsor={assessment.Sponsor.StringId}; reason={assessment.PrimaryReason}; relation={relationLoss}; score={assessment.Score:0.0}.");
        }

        private static void RemoveFromCompetingWarmingFaction(Clan clan, FactionObject targetFaction, FactionManagerBehavior factionManager)
        {
            FactionObject existing = factionManager.GetRebelFaction(clan);
            if (existing == null || existing == targetFaction || existing.IsCivilWarActive())
                return;

            if (existing.Leader == clan)
                factionManager.RemoveFaction(existing);
            else
                existing.RemoveMember(clan);
        }

        private static bool ParentBelongsToClan(FeudalTitleRecord parent, Clan clan)
        {
            return parent != null
                && (parent.DeJureHolderClanId == clan.StringId || parent.DeFactoHolderClanId == clan.StringId);
        }

        internal static bool AreCloseDynasticKin(Clan first, Clan second)
        {
            Hero firstLeader = first?.Leader;
            Hero secondLeader = second?.Leader;
            if (firstLeader == null || secondLeader == null)
                return false;

            return (firstLeader.Father != null && firstLeader.Father == secondLeader.Father)
                || (firstLeader.Mother != null && firstLeader.Mother == secondLeader.Mother)
                || firstLeader.Father == secondLeader
                || firstLeader.Mother == secondLeader
                || secondLeader.Father == firstLeader
                || secondLeader.Mother == firstLeader
                || firstLeader.Father?.Clan == second
                || firstLeader.Mother?.Clan == second
                || secondLeader.Father?.Clan == first
                || secondLeader.Mother?.Clan == first;
        }

        internal static bool AreImmediateBloodKin(Hero first, Hero second) => first != null && second != null && first != second
            && ((first.Father != null && first.Father == second.Father) || (first.Mother != null && first.Mother == second.Mother)
                || first.Father == second || first.Mother == second || second.Father == first || second.Mother == first);
    }
}
