using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Election;
using TaleWorlds.Core;
using TaleWorlds.Localization;

namespace BellumCivile
{
    internal sealed class ForeignPolicyCouncilEvaluation
    {
        public PoliticalInfluenceVoteTally Tally { get; }
        public IReadOnlyList<ForeignPolicyCouncilMember> Members { get; }

        public ForeignPolicyCouncilEvaluation(PoliticalInfluenceVoteTally tally, IEnumerable<ForeignPolicyCouncilMember> members)
        {
            Tally = tally;
            Members = members?.ToList() ?? new List<ForeignPolicyCouncilMember>();
        }
    }

    internal sealed class ForeignPolicyCouncilMember
    {
        public Clan Clan { get; }
        public float Utility { get; }
        public TreatyCouncilVoteStance Stance { get; }
        public int Commitment { get; }

        public ForeignPolicyCouncilMember(Clan clan, float utility, TreatyCouncilVoteStance stance, int commitment)
        {
            Clan = clan;
            Utility = utility;
            Stance = stance;
            Commitment = commitment;
        }
    }

    internal static class WarDeclarationCouncilService
    {
        private static readonly PropertyInfo TotalSupportPointsProperty = AccessTools.Property(
            typeof(DecisionOutcome),
            nameof(DecisionOutcome.TotalSupportPoints));
        private static readonly PropertyInfo WinChanceProperty = AccessTools.Property(
            typeof(DecisionOutcome),
            nameof(DecisionOutcome.WinChance));

        public static ForeignPolicyCouncilEvaluation EvaluatePreliminary(DeclareWarDecision decision)
        {
            List<ForeignPolicyCouncilMember> members = new List<ForeignPolicyCouncilMember>();
            int yay = 0;
            int nay = 0;
            int capacity = 0;
            Kingdom kingdom = decision?.Kingdom;
            WarPeaceRevampBehavior behavior = Campaign.Current?.GetCampaignBehavior<WarPeaceRevampBehavior>();

            foreach (Clan clan in GetEligibleClans(kingdom))
            {
                bool playerRuler = clan == Clan.PlayerClan && kingdom?.RulingClan == Clan.PlayerClan;
                if (!playerRuler)
                    capacity += PoliticalInfluenceVoteHelper.GetVotingCapacity(clan);

                if (clan == Clan.PlayerClan || behavior == null
                    || !behavior.TryEvaluateWarSupport(decision, clan, true, out float utility))
                    continue;

                int commitment = PoliticalInfluenceVoteHelper.GetCommitment(utility, clan, out TreatyCouncilVoteStance stance);
                members.Add(new ForeignPolicyCouncilMember(clan, utility, stance, commitment));
                if (stance == TreatyCouncilVoteStance.Yay)
                    yay += commitment;
                else if (stance == TreatyCouncilVoteStance.Nay)
                    nay += commitment;
            }

            return new ForeignPolicyCouncilEvaluation(
                PoliticalInfluenceVoteHelper.Calculate(yay, nay, capacity),
                members);
        }

        public static ForeignPolicyCouncilEvaluation EvaluatePeacePreliminary(MakePeaceKingdomDecision decision)
        {
            List<ForeignPolicyCouncilMember> members = new List<ForeignPolicyCouncilMember>();
            int yay = 0;
            int nay = 0;
            int capacity = 0;
            Kingdom kingdom = decision?.Kingdom;
            WarPeaceRevampBehavior behavior = Campaign.Current?.GetCampaignBehavior<WarPeaceRevampBehavior>();

            foreach (Clan clan in GetEligibleClans(kingdom))
            {
                bool playerRuler = clan == Clan.PlayerClan && kingdom?.RulingClan == Clan.PlayerClan;
                if (!playerRuler)
                    capacity += PoliticalInfluenceVoteHelper.GetVotingCapacity(clan);

                if (clan == Clan.PlayerClan || behavior == null
                    || !behavior.TryEvaluatePeaceSupport(decision, clan, true, out float utility))
                    continue;

                int commitment = PoliticalInfluenceVoteHelper.GetCommitment(utility, clan, out TreatyCouncilVoteStance stance);
                members.Add(new ForeignPolicyCouncilMember(clan, utility, stance, commitment));
                if (stance == TreatyCouncilVoteStance.Yay)
                    yay += commitment;
                else if (stance == TreatyCouncilVoteStance.Nay)
                    nay += commitment;
            }

            return new ForeignPolicyCouncilEvaluation(
                PoliticalInfluenceVoteHelper.Calculate(yay, nay, capacity),
                members);
        }

        public static PoliticalInfluenceVoteTally CalculateFromOutcomes(
            KingdomDecision decision,
            IEnumerable<DecisionOutcome> outcomes,
            DecisionOutcome hypotheticalOutcome = null,
            int hypotheticalCommitment = 0)
        {
            int yay = 0;
            int nay = 0;
            foreach (DecisionOutcome outcome in outcomes ?? Enumerable.Empty<DecisionOutcome>())
            {
                bool supportsWar = IsWarOutcome(outcome);
                int influence = outcome?.SupporterList?.Sum(supporter => GetCommitment(supporter.SupportWeight)) ?? 0;
                if (outcome == hypotheticalOutcome)
                    influence += Math.Max(0, hypotheticalCommitment);
                if (supportsWar)
                    yay += influence;
                else
                    nay += influence;
            }

            int capacity = GetEligibleClans(decision?.Kingdom)
                .Where(clan => !(clan == Clan.PlayerClan && decision?.Kingdom?.RulingClan == Clan.PlayerClan))
                .Sum(PoliticalInfluenceVoteHelper.GetVotingCapacity);
            return PoliticalInfluenceVoteHelper.Calculate(yay, nay, capacity);
        }

        public static void ApplyOfficialSupport(IEnumerable<DecisionOutcome> outcomes, PoliticalInfluenceVoteTally tally)
        {
            List<DecisionOutcome> list = outcomes?.Where(outcome => outcome != null).ToList() ?? new List<DecisionOutcome>();
            DecisionOutcome yes = list.FirstOrDefault(IsWarOutcome);
            DecisionOutcome no = list.FirstOrDefault(outcome => !IsWarOutcome(outcome));
            if (yes == null || no == null)
                return;

            float yesPoints = tally.YayInfluence;
            float noPoints = tally.NayInfluence;
            if (!tally.IsRatified)
                noPoints = Math.Max(noPoints, yesPoints + BellumCivileConstants.TreatyCouncilMinimumVoteStep);

            float total = Math.Max(0.001f, yesPoints + noPoints);
            SetOutcomeSupport(yes, yesPoints, yesPoints / total);
            SetOutcomeSupport(no, noPoints, noPoints / total);
        }

        public static bool IsWarOutcome(DecisionOutcome outcome)
        {
            return outcome is DeclareWarDecision.DeclareWarDecisionOutcome warOutcome
                && warOutcome.ShouldWarBeDeclared;
        }

        public static int GetCommitment(Supporter.SupportWeights weight)
        {
            switch (weight)
            {
                case Supporter.SupportWeights.SlightlyFavor:
                    return BellumCivileConstants.TreatyCouncilMinimumVoteStep;
                case Supporter.SupportWeights.StronglyFavor:
                    return BellumCivileConstants.TreatyCouncilMildCommitment;
                case Supporter.SupportWeights.FullyPush:
                    return BellumCivileConstants.TreatyCouncilStrongCommitment;
                default:
                    return 0;
            }
        }

        private static void SetOutcomeSupport(DecisionOutcome outcome, float supportPoints, float winChance)
        {
            TotalSupportPointsProperty?.SetValue(outcome, supportPoints, null);
            WinChanceProperty?.SetValue(outcome, winChance, null);
        }

        public static Supporter.SupportWeights GetWeight(int commitment)
        {
            if (commitment >= BellumCivileConstants.TreatyCouncilStrongCommitment)
                return Supporter.SupportWeights.FullyPush;
            if (commitment >= BellumCivileConstants.TreatyCouncilMildCommitment)
                return Supporter.SupportWeights.StronglyFavor;
            return commitment >= BellumCivileConstants.TreatyCouncilMinimumVoteStep
                ? Supporter.SupportWeights.SlightlyFavor
                : Supporter.SupportWeights.StayNeutral;
        }

        public static TextObject GetSupportLabel(PoliticalInfluenceVoteTally tally)
        {
            KingdomElection.ElectionOutcomeSupport support = KingdomElection.ElectionOutcomeSupport.LowSupport;
            if (tally?.IsRatified == true)
            {
                float yayShare = tally.Participation <= 0 ? 0f : tally.YayInfluence / (float)tally.Participation;
                support = yayShare > 0.75f
                    ? KingdomElection.ElectionOutcomeSupport.StrongSupport
                    : KingdomElection.ElectionOutcomeSupport.GoodSupport;
            }

            return GameTexts.FindText("str_decision_outcome_support_status", support.ToString());
        }

        private static IEnumerable<Clan> GetEligibleClans(Kingdom kingdom)
        {
            return kingdom?.Clans?.Where(clan => clan != null
                && !clan.IsEliminated
                && clan.Leader != null
                && !clan.IsUnderMercenaryService) ?? Enumerable.Empty<Clan>();
        }
    }
}
