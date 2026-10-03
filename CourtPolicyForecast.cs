using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Election;

namespace BellumCivile
{
    internal sealed class CourtPolicyForecast
    {
        internal int For, Against, SupportingHouses;
        internal bool SponsorSupports, Affordable;
        internal float SponsorPreference;
        internal bool Viable => CourtAgendaRules.Qualifies(For, Against, SupportingHouses, Affordable, SponsorSupports, SponsorPreference);

        private sealed class Vote
        {
            internal Clan Clan;
            internal float Yes, No, Balance, Reserve;
            internal int Points;
            internal bool Pass => Yes > No;
        }

        internal static CourtPolicyForecast Calculate(KingdomPolicyDecision decision, int filingCost, IEnumerable<Clan> voters = null)
            => Calculate(decision, filingCost, new KingdomPolicyDecision.PolicyDecisionOutcome(true),
                new KingdomPolicyDecision.PolicyDecisionOutcome(false), voters);

        internal static CourtPolicyForecast Calculate(KingdomDecision decision, int filingCost,
            DecisionOutcome yes, DecisionOutcome no, IEnumerable<Clan> voters = null)
        {
            var forecast = new CourtPolicyForecast();
            Clan proposer = decision.ProposerClan;
            if (proposer?.Leader == null || decision.Kingdom == null) return forecast;
            forecast.Affordable = proposer == Clan.PlayerClan ? proposer.Influence >= filingCost
                : NpcInfluenceBudgetService.CanAfford(proposer, filingCost, NpcInfluenceExpenseKind.Discretionary);
            var votes = new List<Vote>();
            foreach (Clan clan in voters ?? decision.Kingdom.Clans)
            {
                if (clan.IsEliminated || (clan.IsMinorFaction && clan != Clan.PlayerClan) || clan.IsUnderMercenaryService || clan.Leader == null) continue;
                votes.Add(new Vote { Clan = clan, Yes = decision.DetermineSupport(clan, yes), No = decision.DetermineSupport(clan, no),
                    Balance = clan.Influence - (clan == proposer ? filingCost : 0), Reserve = NpcInfluenceBudgetService.GetRoleReserve(clan) });
            }
            float initialYes = votes.Sum(v => Clamp(v.Yes, 0, 100));
            float initialNo = votes.Sum(v => Clamp(v.No, 0, 100));
            float total = initialYes + initialNo;
            float likelihoodYes = total > 0 ? initialYes / total : 0;
            float likelihoodNo = total > 0 ? initialNo / total : 0;
            Clan opposition = null;
            int strongest = 0;
            foreach (Vote vote in votes)
            {
                vote.Points = Commitment(decision, vote, vote.Pass ? likelihoodYes : likelihoodNo, null);
                if (!vote.Pass && vote.Points > strongest) { opposition = vote.Clan; strongest = vote.Points; }
            }
            foreach (Vote vote in votes)
            {
                vote.Points = Commitment(decision, vote, vote.Pass ? likelihoodYes : likelihoodNo, vote.Pass ? proposer : opposition);
                if (vote.Pass && vote.Points > 0) { forecast.For += vote.Points; forecast.SupportingHouses++; }
                else forecast.Against += vote.Points;
                if (vote.Clan == proposer)
                {
                    forecast.SponsorPreference = vote.Yes;
                    forecast.SponsorSupports = vote.Pass && vote.Points > 0;
                }
            }
            return forecast;
        }

        private static int Commitment(KingdomDecision decision, Vote vote, float likelihood, Clan sponsor)
        {
            float best = Math.Max(vote.Yes, vote.No);
            float willingness = best - Math.Min(0, Math.Min(vote.Yes, vote.No)) * 0.5f;
            if (vote.Balance < willingness * 2) willingness = Math.Min(willingness * 0.5f, vote.Balance * 0.7f);
            else if (vote.Balance > willingness * 10) willingness *= 1.5f;
            if (likelihood > 0.65f) willingness *= 1.6f * (1.2f - likelihood);
            if (sponsor?.Leader != null)
            {
                int distance = (int)(100 - Clamp(vote.Clan.Leader.GetRelation(sponsor.Leader), -100, 100));
                willingness *= 0.2f + 1.6f * (1 - distance / 200f);
            }
            int points = 0;
            for (int p = 1; p <= 3; p++)
            {
                int cost = decision.GetInfluenceCostOfSupport(vote.Clan, (Supporter.SupportWeights)(p + 1));
                if (willingness > cost && vote.Balance - cost >= vote.Reserve) points = p;
            }
            return points;
        }
        private static float Clamp(float n, float min, float max) => Math.Max(min, Math.Min(max, n));
    }
}
