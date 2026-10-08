using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using C = BellumCivile.BellumCivileConstants;

namespace BellumCivile.Behaviors
{
    internal static partial class BellumMarriageStrategyHelper
    {
        // One context per scheduled house search; never reuse campaign objects after load.
        private sealed partial class EvaluationContext
        {
            public readonly Dictionary<Clan, List<Hero>> Participants = new Dictionary<Clan, List<Hero>>();
            public readonly Dictionary<Clan, Kingdom> Realms = new Dictionary<Clan, Kingdom>();
            private readonly Dictionary<Clan, HouseHealth> _health = new Dictionary<Clan, HouseHealth>();
            public readonly HashSet<Hero> CrownHeirs = new HashSet<Hero>();
            private readonly MarriageHouseholdPolicy _households = new MarriageHouseholdPolicy();
            private readonly Dictionary<Hero, List<Hero>> _parentalEstates = new Dictionary<Hero, List<Hero>>();
            private readonly Dictionary<Hero, List<Hero>> _parentHeirs = new Dictionary<Hero, List<Hero>>();
            private readonly Dictionary<Hero, bool> _bloodMembers = new Dictionary<Hero, bool>();
            public readonly Dictionary<Clan, Dictionary<Clan, float>> Politics = new Dictionary<Clan, Dictionary<Clan, float>>();
            private readonly FeudalTitleBehavior _titles;
            private readonly int _day;
            private MarriageTitleClaimCache _claims;
            private readonly Dictionary<Clan, Dictionary<Hero, ClaimConsideration[]>> _claimValues = new Dictionary<Clan, Dictionary<Hero, ClaimConsideration[]>>();
            public readonly FactionManagerBehavior Factions;

            private sealed class ClaimConsideration
            {
                public float Value;
                public readonly List<string> Reasons = new List<string>();
            }

            public EvaluationContext(FactionManagerBehavior factions, bool includePlayer, HashSet<Clan> houses = null, BellumMarriageEvaluationStats stats = null, bool allowTemporary = false)
            {
                Factions = factions;
                _titles = Campaign.Current.GetCampaignBehavior<FeudalTitleBehavior>();
                _day = (int)CampaignTime.Now.ToDays;
                // An heir may belong to either candidate house while inheriting a third realm.
                CrownHeirs.UnionWith(_households.CrownHeirs);
                foreach (Kingdom realm in Kingdom.All)
                {
                    if (realm == null || realm.IsEliminated || houses != null && !houses.Any(c => c.Kingdom == realm)) continue;
                    foreach (Clan clan in realm.Clans)
                    {
                        if (houses != null && !houses.Contains(clan) || !IsStrategicMarriageClanAllowed(clan, includePlayer)) continue;
                        Realms[clan] = MarriagePoliticalRealm(clan);
                        Participants[clan] = new List<Hero>();
                        foreach (var hero in clan.Heroes)
                        {
                            if (stats != null) stats.ParticipantChecks++;
                            var rejection = GetOfferParticipantRejectionReason(hero, includePlayer, allowTemporary);
                            if (rejection != BellumMarriageRejectionReason.None) { stats?.AddRejection(rejection); continue; }
                            Participants[clan].Add(hero);
                            if (stats != null) stats.EligibleParticipants++;
                        }
                    }
                }
            }

            public Clan HouseholdDestination(Hero first, Hero second)
            {
                Clan ordinary = Campaign.Current.Models.MarriageModel.GetClanAfterMarriage(first, second);
                return _households.TryChoose(first, second, ordinary, out Clan destination) ? destination : null;
            }

            public IEnumerable<Clan> HouseholdDestinations(Hero first, Hero second)
            {
                Clan preferred = HouseholdDestination(first, second);
                if (first.Clan != Clan.PlayerClan && second.Clan != Clan.PlayerClan)
                {
                    if (preferred != null) yield return preferred;
                    yield break;
                }
                // Share the same heir/health snapshot while considering both player-offer arrangements.
                if (preferred != first.Clan && preferred != second.Clan) preferred = first.Clan;
                yield return preferred;
                yield return preferred == first.Clan ? second.Clan : first.Clan;
            }

            public bool CanChooseHousehold(Hero first, Hero second, Clan destination, out TextObject reason)
            {
                reason = null;
                if (first.Clan == Clan.PlayerClan || second.Clan == Clan.PlayerClan)
                    return _households.CanOfferToPlayer(first, second, destination, out reason);
                return destination != null && HouseholdDestination(first, second) == destination;
            }

            public float PoliticalValue(Clan house, Clan other)
            {
                if (!Politics.TryGetValue(house, out var values)) Politics[house] = values = new Dictionary<Clan, float>();
                if (!values.TryGetValue(other, out float value))
                {
                    value = CalculatePoliticalValue(house, other);
                    values[other] = value;
                }
                return value;
            }

            public float HouseholdDepartureCost(Hero member, MarriageOutcome outcome, bool foreignRoyal)
            {
                var house = member.Clan;
                if (outcome.Destination == house) return 0;
                var health = Health(house);
                if (!foreignRoyal)
                    return MarriageMatchmakingRules.DepartureCost(true, health.HasFertileCouple,
                        health.Prospects.Count - (health.Prospects.Contains(member) ? 1 : 0), false, health.Risk);
                if (health.BloodProspects == null)
                    health.BloodProspects = new HashSet<Hero>(health.Prospects.Where(IsBloodMember));
                return MarriageMatchmakingRules.DepartureCost(true, health.HasFertileCouple,
                    health.BloodProspects.Count - (health.BloodProspects.Contains(member) ? 1 : 0), true, health.Risk);
            }

            public HouseHealth Health(Clan house)
            {
                if (!_health.TryGetValue(house, out var health)) _health[house] = health = AssessHouseHealth(house);
                return health;
            }

            public bool FertileHousehold(Clan house) => Health(house).HasFertileCouple;

            public float IncomingClaimValue(Clan house, Hero carrier, List<string> reasons, bool canFoundLine)
            {
                if (!_claimValues.TryGetValue(house, out var values))
                    _claimValues[house] = values = new Dictionary<Hero, ClaimConsideration[]>();
                if (!values.TryGetValue(carrier, out var variants)) values[carrier] = variants = new ClaimConsideration[2];
                int variant = canFoundLine ? 1 : 0;
                var claim = variants[variant];
                if (claim == null)
                {
                    // Index all active titles/claims only if a legally eligible pair reaches scoring.
                    if (_claims == null) _claims = MarriageTitleClaimCache.Build(_titles, _day);
                    claim = new ClaimConsideration();
                    claim.Value = CalculateDirectionalTitleClaimMarriageValue(house, house.Leader, carrier.Clan,
                        carrier, _claims, claim.Reasons, MarriageHealthRules.StrategyScale(Health(house).Risk), canFoundLine);
                    variants[variant] = claim;
                }
                reasons.AddRange(claim.Reasons);
                return claim.Value;
            }

            public List<Hero> ParentalEstateProspects(Hero member)
            {
                if (_parentalEstates.TryGetValue(member, out var result)) return result;
                result = new List<Hero>();
                foreach (Hero parent in new[] { member.Father, member.Mother }.Where(h => h != null).Distinct())
                {
                    if (!parent.IsAlive || parent.Clan?.Leader != parent
                        || CrownAccessionBehavior.Instance?.HasInheritanceAdvance(parent, member) == true) continue;
                    if (!_parentHeirs.TryGetValue(parent, out var heirs))
                        _parentHeirs[parent] = heirs = PartitionSuccessionBehavior.OrderEstateHeirs(
                            parent.Clan.Heroes, parent, SuccessionLawHelper.GetLawsForClan(parent.Clan));
                    if (heirs.Contains(member)) result.Add(parent);
                }
                _parentalEstates[member] = result;
                return result;
            }

            public bool IsBloodMember(Hero member)
            {
                if (!_bloodMembers.TryGetValue(member, out bool value))
                    _bloodMembers[member] = value = member == member.Clan.Leader
                        || SuccessionLawHelper.IsBloodRelative(member, member.Clan.Leader);
                return value;
            }
        }

        internal static BellumMarriageMatch FindBestHouseMatch(Clan house, FactionManagerBehavior factions,
            bool includePlayer, BellumMarriageEvaluationStats stats)
        {
            var context = new EvaluationContext(factions, includePlayer, stats: stats, allowTemporary: true);
            if (!context.Participants.TryGetValue(house, out var members)) return null;
            BellumMarriageMatch best = null;
            foreach (var entry in context.Participants)
            {
                if (entry.Key == house) continue;
                var candidatesBySex = entry.Value.ToLookup(h => h.IsFemale);
                foreach (Hero member in members)
                {
                    if (MarriageReserved(member)) continue;
                    var rejected = GetCandidateClanRejectionReason(member, entry.Key, factions, includePlayer);
                    if (rejected != BellumMarriageRejectionReason.None) { stats?.AddRejection(rejected); continue; }
                    foreach (Hero candidate in candidatesBySex[!member.IsFemale])
                    {
                        if (MarriageReserved(candidate)) continue;
                        rejected = GetPairRejectionReason(member, candidate, factions, includePlayer, allowTemporary: true);
                        if (rejected != BellumMarriageRejectionReason.None) { stats?.AddRejection(rejected); continue; }
                        var match = EvaluateOutcome(member, candidate, context, false);
                        if (match == null) continue;
                        if (stats != null) stats.LegalPairs++;
                        bool firstAccepts = member.Clan == Clan.PlayerClan || match.SuitorAcceptance >= C.MarriageStrategyMinimumScore;
                        bool secondAccepts = candidate.Clan == Clan.PlayerClan || match.CandidateAcceptance >= C.MarriageStrategyMinimumScore;
                        if (!firstAccepts || !secondAccepts)
                        {
                            if (stats != null)
                            {
                                if (!firstAccepts) stats.FirstHouseRefused++;
                                if (!secondAccepts) stats.SecondHouseRefused++;
                                if (match.Score > stats.BestRejectedScore)
                                { stats.BestRejectedScore = match.Score; stats.ClosestRefusedMatch = match; }
                            }
                            continue;
                        }
                        if (stats != null) stats.AcceptedPairs++;
                        if (best == null || match.Score > best.Score
                            || match.Score == best.Score && match.SuitorAcceptance + match.CandidateAcceptance
                                > best.SuitorAcceptance + best.CandidateAcceptance) best = match;
                    }
                }
            }
            return best;
        }

        private static bool MarriageReserved(Hero hero) => StrategicMarriageBehavior.HasMarriageOfferFor(hero)
            || Campaign.Current?.GetCampaignBehavior<StrategicMarriageBehavior>()?.HasPendingProspect(hero) == true
            || CourtAgendaBehavior.Current?.HasDynasticReservation(hero, hero) == true;

        // Console-only search: retain normal acceptance and household rules, skipping only scheduling.
        internal static List<BellumMarriageMatch> FindPlayerClanTestOffers(FactionManagerBehavior factions,
            bool? matrilineal, out string diagnostics, Hero playerMember = null)
        {
            var matches = new List<BellumMarriageMatch>();
            var stats = new BellumMarriageEvaluationStats();
            var context = new EvaluationContext(factions, true, stats: stats);
            if (!context.Participants.TryGetValue(Clan.PlayerClan, out var members))
            {
                diagnostics = "player clan is not eligible for strategic offers (it must belong to an active kingdom)";
                return matches;
            }
            var availableMembers = members.Where(h => (playerMember == null || h == playerMember) && !MarriageReserved(h)).ToList();
            int pairs = 0, householdRejected = 0, acceptanceRejected = 0, maternal = 0, paternal = 0;
            var householdReasons = new HashSet<string>();
            BellumMarriageMatch closestRefused = null;
            foreach (var entry in context.Participants)
            {
                if (entry.Key == Clan.PlayerClan
                    || !Campaign.Current.Models.MarriageModel.ShouldNpcMarriageBetweenClansBeAllowed(Clan.PlayerClan, entry.Key)) continue;
                foreach (Hero other in entry.Value)
                {
                    if (MarriageReserved(other)) continue;
                    foreach (Hero player in availableMembers)
                    {
                        if (GetPairRejectionReason(other, player, factions, true) != BellumMarriageRejectionReason.None) continue;
                        pairs++;
                        foreach (Clan destination in context.HouseholdDestinations(other, player))
                        {
                            bool isMatrilineal = destination == (other.IsFemale ? other.Clan : player.Clan);
                            bool requested = !matrilineal.HasValue || matrilineal.Value == isMatrilineal;
                            if (!context.CanChooseHousehold(other, player, destination, out TextObject reason))
                            {
                                householdRejected++;
                                if (requested && reason != null) householdReasons.Add(reason.ToString());
                                continue;
                            }
                            var match = EvaluateHousehold(other, player, context, destination, false);
                            if (match.Score < C.MarriageStrategyMinimumScore)
                            {
                                acceptanceRejected++;
                                if (requested && (closestRefused == null || match.Score > closestRefused.Score)) closestRefused = match;
                                continue;
                            }
                            if (isMatrilineal) maternal++; else paternal++;
                            if (requested) matches.Add(match);
                        }
                    }
                }
            }
            diagnostics = $"available_player_members={availableMembers.Count}; suitable_pairs={pairs}; "
                + $"household_rejected={householdRejected}; acceptance_rejected={acceptanceRejected}; "
                + $"eligible_matrilineal={maternal}; eligible_patrilineal={paternal}; "
                + $"participants_too_young={stats.RejectedTooYoung}; participants_unavailable={stats.TemporaryParticipants}";
            if (playerMember != null && !members.Contains(playerMember))
                diagnostics += $"; selected_member_rejected={GetOfferParticipantRejectionReason(playerMember, true)}";
            else if (playerMember != null && availableMembers.Count == 0)
                diagnostics += "; selected_member_rejected=reserved";
            if (householdReasons.Count > 0) diagnostics += "; household_reasons=" + string.Join(" | ", householdReasons);
            if (closestRefused != null)
                diagnostics += $"; closest_refused={closestRefused.Suitor.StringId}/{closestRefused.Candidate.StringId}; "
                    + $"destination={closestRefused.Outcome.Destination.StringId}; npc_score={closestRefused.Score:0.##}/{C.MarriageStrategyMinimumScore:0}; "
                    + "reasons=" + string.Join(", ", closestRefused.Reasons);
            return matches;
        }

        internal static BellumMarriageMatch ReevaluateProspect(Hero first, Hero second, Clan destination = null)
        {
            if (!MarriageProspectEligible(first) || !MarriageProspectEligible(second)) return null;
            var factions = Campaign.Current.GetCampaignBehavior<FactionManagerBehavior>();
            if (GetPairRejectionReason(first, second, factions, true) != BellumMarriageRejectionReason.None) return null;
            var context = new EvaluationContext(factions, true, new HashSet<Clan> { first.Clan, second.Clan });
            return destination == null ? EvaluateOutcome(first, second, context, true)
                : context.CanChooseHousehold(first, second, destination, out _)
                    ? EvaluateHousehold(first, second, context, destination, true) : null;
        }

        private static BellumMarriageMatch EvaluateOutcome(Hero first, Hero second, EvaluationContext context, bool requireAcceptance = true)
        {
            if (first.Clan != Clan.PlayerClan && second.Clan != Clan.PlayerClan)
                return EvaluateHousehold(first, second, context, context.HouseholdDestination(first, second), requireAcceptance);
            BellumMarriageMatch best = null;
            foreach (Clan destination in context.HouseholdDestinations(first, second))
            {
                if (!context.CanChooseHousehold(first, second, destination, out _)) continue;
                var match = EvaluateHousehold(first, second, context, destination, requireAcceptance);
                if (match != null && (best == null || match.Score > best.Score)) best = match;
            }
            return best;
        }

        private static BellumMarriageMatch EvaluateHousehold(Hero first, Hero second, EvaluationContext context,
            Clan destination, bool requireAcceptance)
        {
            if (destination == null || destination.IsEliminated) return null;
            var outcome = new MarriageOutcome(first, second, destination,
                context.CrownHeirs.Contains(first), context.CrownHeirs.Contains(second));
            outcome.FirstParentalEstateProspects = context.ParentalEstateProspects(first);
            outcome.SecondParentalEstateProspects = context.ParentalEstateProspects(second);
            var match = new BellumMarriageMatch { Suitor = first, Candidate = second, Outcome = outcome };
            match.HasDynasticContinuity = outcome.HasReproductiveOpportunity
                && (!context.FertileHousehold(first.Clan) || !context.FertileHousehold(second.Clan));
            match.IsDomestic = context.Realms[first.Clan] == context.Realms[second.Clan];
            bool politics = context.PoliticalValue(first.Clan, second.Clan) > 0f || context.PoliticalValue(second.Clan, first.Clan) > 0f;
            match.HasRoyalPolitics = match.IsDomestic && politics
                && (context.Realms[first.Clan]?.RulingClan == first.Clan || context.Realms[second.Clan]?.RulingClan == second.Clan);
            match.HasForeignPolitics = !match.IsDomestic && politics;
            match.SuitorAcceptance = EvaluateHouse(first, second, outcome, context, match.Reasons);
            match.CandidateAcceptance = EvaluateHouse(second, first, outcome, context, match.Reasons);
            bool firstPlayer = first.Clan == Clan.PlayerClan, secondPlayer = second.Clan == Clan.PlayerClan;
            if (requireAcceptance && !MarriageOutcome.BothAccept(match.SuitorAcceptance, match.CandidateAcceptance,
                firstPlayer, secondPlayer, C.MarriageStrategyMinimumScore)) return null;
            match.Score = MarriageOutcome.Rank(match.SuitorAcceptance, match.CandidateAcceptance, firstPlayer, secondPlayer);
            if (match.IsDomestic) match.Reasons.Add("same realm");
            if (match.HasRoyalPolitics) match.Reasons.Add("royal pacification");
            if (match.HasForeignPolitics) match.Reasons.Add("foreign royal alliance");
            Hero bride = first.IsFemale ? first : second;
            if (destination == bride.Clan) match.Reasons.Add("matrilineal household: husband joins bride's house");
            match.Reasons.Add($"acceptance: {first.Clan.StringId}={match.SuitorAcceptance:0}; {second.Clan.StringId}={match.CandidateAcceptance:0}; household={destination.StringId}");
            return match;
        }

        private static float EvaluateHouse(Hero member, Hero spouse, MarriageOutcome outcome,
            EvaluationContext context, List<string> reasons)
        {
            Clan house = member.Clan, other = spouse.Clan;
            Kingdom realm = context.Realms[house], otherRealm = context.Realms[other];
            float risk = context.Health(house).Risk;
            float preference = MarriageHealthRules.PreferenceScale(risk);
            float strategy = MarriageHealthRules.StrategyScale(risk);
            float score = realm == otherRealm ? C.MarriageScoreSameKingdom
                : -MarriageHealthRules.ForeignDistanceCost(risk, outcome.Destination != house);
            if (AreKingdomsAllied(realm, otherRealm)) score += C.MarriageScoreAlliedKingdom;
            if (member.Culture == spouse.Culture) score += C.MarriageScoreSameCulture;
            score += house.GetRelationWithClan(other) * C.MarriageScoreClanRelationScale;
            score += member.GetRelation(spouse) * C.MarriageScorePersonalRelationScale;
            score -= MathF.Abs(member.Age - spouse.Age) * C.MarriageScoreAgePenalty * preference;
            // Only the house marrying down pays a status cost, once, with its own desperation.
            float status = System.Math.Max(0, house.Tier - other.Tier) * C.MarriageScoreRankGapPenalty;
            status *= preference;
            score -= status;

            bool bloodMember = context.IsBloodMember(member);
            float kinship = MarriageHealthRules.OutgoingKinshipValue(risk, outcome.HasReproductiveOpportunity,
                bloodMember, outcome.Destination != house);
            score += kinship;
            if (kinship > 0) reasons.Add($"{house.StringId}: outgoing bloodline alliance +{kinship:0}");
            if (outcome.HasReproductiveOpportunity)
            {
                float continuity = MarriageHealthRules.Continuity(risk, true, outcome.NewbornHouse == house, bloodMember);
                score += continuity;
                reasons.Add($"{house.StringId}: household/bloodline continuity +{continuity:0}");
                if (context.CrownHeirs.Contains(member))
                {
                    float heirValue = member.Children.Any(h => h.IsAlive)
                        ? C.MarriageHeirWithChildrenValue : C.MarriageHeirWithoutChildrenValue;
                    score += heirValue;
                    reasons.Add($"{house.StringId}: lawful heir continuity +{heirValue:0}; Crown rights retained");
                }
            }
            float departure = context.HouseholdDepartureCost(member, outcome,
                realm != otherRealm && realm?.RulingClan == house && otherRealm?.RulingClan == other);
            if (departure > 0)
            {
                score -= departure;
                reasons.Add($"{house.StringId}: household/reserve departure -{departure:0.##}");
            }
            float politics = context.PoliticalValue(house, other) * strategy;
            score += politics;
            if (politics != 0f) reasons.Add($"{house.StringId}: political ties {politics:+0;-0}");
            if (outcome.Destination == house)
                score += context.IncomingClaimValue(house, spouse, reasons, outcome.HasReproductiveOpportunity);
            float prestige = MarriageHealthRules.Prestige(house.Tier, other.Tier) * strategy;
            score += prestige;
            if (prestige > 0) reasons.Add($"{house.StringId}: prestigious house connection +{prestige:0}");
            float court = CourtAgendaBehavior.Current?.DynasticMarriageBonus(member, spouse) ?? 0;
            if (court != 0) { score += court; reasons.Add($"{house.StringId}: Crown-backed royal marriage +{court:0}"); }
            reasons.Add($"{house.StringId}: household risk {risk:0.00}; status -{status:0}; future estates remain personal and conditional");
            return score;
        }
    }
}
