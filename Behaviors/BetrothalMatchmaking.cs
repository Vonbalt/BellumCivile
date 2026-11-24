using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;

namespace BellumCivile.Behaviors
{
    internal sealed partial class BetrothalBehavior
    {
        internal static double BetrothalDelay(Hero first, Hero second) =>
            Math.Max(0d, Adulthood - Math.Min(first.Age, second.Age));

        internal static float BetrothalScore(Hero member, Hero counterpart, Clan destination, double delay,
            float? knownRisk = null, float politics = 0f)
        {
            var house = member.Clan;
            var other = counterpart.Clan;
            float risk = knownRisk ?? BellumMarriageStrategyHelper.PlayerMarriageHouseRisk(house);
            float score = 68f + house.GetRelationWithClan(other) * .35f + politics;
            if (house.Kingdom != null && house.Kingdom == other.Kingdom) score += 15;
            if (member.Culture == counterpart.Culture) score += 6;
            if (member.IsChild && counterpart.IsChild) score += 12;
            else score -= 10;
            if (destination == house) score += 20 * risk;
            score += Math.Min(15, Math.Max(0, other.Tier - house.Tier) * 5);
            return score - BetrothalRules.DelayCost(delay);
        }

        internal static bool AcceptsAdultWait(Clan house, float risk, float politics) => risk >= .55f || politics >= 20f
            || house.Leader?.GetTraitLevel(DefaultTraits.Generosity) < 0;
    }

    internal static partial class BellumMarriageStrategyHelper
    {
        internal sealed class BetrothalMatch
        {
            internal Hero First, Second;
            internal Clan OtherHouse;
            internal Clan Destination;
            internal float Score;
            internal bool BothMinors;
        }

        internal static List<BetrothalMatch> FindPlayerBetrothalTestOffers(FactionManagerBehavior factions,
            bool? matrilineal, Hero playerMember, out string diagnostics)
        {
            // Betrothals use the family roster, not the adult marriage candidate index.
            var context = new EvaluationContext(factions, true, new HashSet<Clan>());
            context.Realms[Clan.PlayerClan] = MarriagePoliticalRealm(Clan.PlayerClan);
            var policy = new MarriageHouseholdPolicy();
            policy.UseCrownHeirs(context.CrownHeirs);
            var matches = new List<BetrothalMatch>();
            var players = HouseholdMembership.Members(Clan.PlayerClan).Where(hero =>
                (playerMember == null || hero == playerMember) && BetrothalBehavior.Potential(hero)).ToList();
            int legalPairs = 0;
            float best = float.NegativeInfinity;
            int threshold = BellumCivileOptions.MarriageAcceptanceThreshold;
            if (players.Count != 0)
            {
                foreach (Clan house in Clan.All)
                {
                    if (!IsStrategicMarriageClanAllowed(house, allowPlayerClan: false)
                        || GetCandidateHouseRejectionReason(house, Clan.PlayerClan, factions, true) != BellumMarriageRejectionReason.None) continue;
                    context.Realms[house] = MarriagePoliticalRealm(house);
                    foreach (Hero other in HouseholdMembership.Members(house).Where(BetrothalBehavior.Potential))
                    foreach (Hero player in players)
                    {
                        if (!TryEvaluateBetrothal(other, player, policy, context, matrilineal,
                            out Clan destination, out float score, out bool bothMinors)) continue;
                        legalPairs++;
                        best = Math.Max(best, score);
                        if (score >= threshold) matches.Add(new BetrothalMatch { First = other, Second = player,
                            OtherHouse = Clan.PlayerClan, Destination = destination, Score = score, BothMinors = bothMinors });
                    }
                }
            }

            diagnostics = $"Eligible player family members: {players.Count}; legal pairs: {legalPairs}; "
                + $"best NPC score: {(legalPairs == 0 ? "none" : best.ToString("0.#"))}; required: {threshold}";
            return matches;
        }

        private static bool TryEvaluateBetrothal(Hero first, Hero second, MarriageHouseholdPolicy policy,
            EvaluationContext context, bool? matrilineal, out Clan destination, out float best, out bool minors)
        {
            destination = null;
            best = float.NegativeInfinity;
            minors = false;
            if (!BetrothalBehavior.CompatiblePair(first, second)) return false;
            Clan house = first.Clan, other = second.Clan;
            double delay = BetrothalBehavior.BetrothalDelay(first, second);
            minors = first.IsChild && second.IsChild;
            bool playerResponds = other == Clan.PlayerClan;
            float firstRisk = context.Health(house).Risk, secondRisk = playerResponds ? 0 : context.Health(other).Risk;
            float firstPolitics = context.PoliticalValue(house, other), secondPolitics = playerResponds ? 0 : context.PoliticalValue(other, house);
            if (!minors && (delay > 3d || !first.IsChild && !BetrothalBehavior.AcceptsAdultWait(house, firstRisk, firstPolitics)
                || !second.IsChild && !playerResponds && !BetrothalBehavior.AcceptsAdultWait(other, secondRisk, secondPolitics))) return false;
            Clan maternalHouse = first.IsFemale ? house : other;
            foreach (Clan possible in new[] { house, other })
            {
                if (matrilineal.HasValue && matrilineal.Value != (possible == maternalHouse)
                    || !BetrothalBehavior.CanSetDestination(first, second, possible, policy)) continue;
                if (!MarriagePayments.CanPay(possible, MarriagePayments.Deposit(BetrothalBehavior.BetrothalDowry(first, second, possible)))) continue;
                float firstScore = BetrothalBehavior.BetrothalScore(first, second, possible, delay, firstRisk, firstPolitics);
                float secondScore = playerResponds ? firstScore
                    : BetrothalBehavior.BetrothalScore(second, first, possible, delay, secondRisk, secondPolitics);
                float score = Math.Min(firstScore, secondScore);
                if (destination == null || score > best) { destination = possible; best = score; }
            }
            return destination != null;
        }

        private static IEnumerable<bool> SearchBetrothals(Clan house, bool includePlayer, EvaluationContext context,
            ScheduledMarriageMarket market, ScheduledMarriageSearch search, int day)
        {
            var initiators = market.PledgeCandidates(house);
            if (initiators.Count == 0) yield break;
            var policy = new MarriageHouseholdPolicy();
            policy.UseCrownHeirs(context.CrownHeirs);
            var distant = new List<Clan>();
            int localLegalHouses = 0;
            foreach (var other in context.Participants.Keys)
            {
                yield return true;
                if (other == house || !includePlayer && other == Clan.PlayerClan) continue;
                if (!market.IsLocal(house, other)) { distant.Add(other); continue; }
                int before = search.Stats.BetrothalLegalPairs;
                foreach (var step in SearchPledgeHouse(house, other, initiators, policy, context, market, search, false)) yield return step;
                if (search.Stats.BetrothalLegalPairs > before) localLegalHouses++;
            }

            var pass = MarriageSearchScope.DistantPass(false, context.Realms[house]?.RulingClan == house,
                search.Betrothal != null, context.Health(house).Risk, localLegalHouses);
            if (pass == DistantMarriageSearch.None || distant.Count == 0) yield break;
            distant.Sort((a, b) => StringComparer.Ordinal.Compare(a.StringId, b.StringId));
            int start = MarriageSearchScope.DistantStart(house.StringId, day, distant.Count);
            int houses = 0;
            for (int i = 0; i < distant.Count && houses < MarriageSearchScope.DistantHouseLimit
                && search.Stats.DistantBetrothalPairs < MarriageSearchScope.DistantPairLimit; i++)
            {
                yield return true;
                var target = distant[(start + i) % distant.Count];
                if (pass == DistantMarriageSearch.RoyalHouses && context.Realms[target]?.RulingClan != target) continue;
                if (GetCandidateHouseRejectionReason(house, target, context.Factions, includePlayer) != BellumMarriageRejectionReason.None
                    || market.PledgeCandidates(target).Count == 0) continue;
                houses++;
                foreach (var step in SearchPledgeHouse(house, target, initiators, policy, context, market, search, true)) yield return step;
            }
        }

        private static IEnumerable<bool> SearchPledgeHouse(Clan house, Clan other, List<Hero> initiators,
            MarriageHouseholdPolicy policy, EvaluationContext context, ScheduledMarriageMarket market, ScheduledMarriageSearch search, bool distant)
        {
            if (GetCandidateHouseRejectionReason(house, other, context.Factions, true) != BellumMarriageRejectionReason.None) yield break;
            int threshold = BellumCivileOptions.MarriageAcceptanceThreshold;
            foreach (var first in initiators)
            {
                yield return true;
                if (first.Clan != house || !BetrothalBehavior.Potential(first)) continue;
                foreach (var second in market.PledgeCandidates(other))
                {
                    if (distant && search.Stats.DistantBetrothalPairs >= MarriageSearchScope.DistantPairLimit) yield break;
                    yield return true;
                    if (distant) search.Stats.DistantBetrothalPairs++;
                    if (second.Clan != other || !BetrothalBehavior.Potential(second)) continue;
                    if (!TryEvaluateBetrothal(first, second, policy, context, null,
                        out Clan destination, out float score, out bool minors)) continue;
                    search.Stats.BetrothalLegalPairs++;
                    var best = search.Betrothal;
                    if (score >= threshold && (best == null || minors && !best.BothMinors || minors == best.BothMinors && score > best.Score))
                        search.Betrothal = new BetrothalMatch { First = first, Second = second, OtherHouse = other,
                            Destination = destination, BothMinors = minors, Score = score };
                }
            }
        }
    }
}
