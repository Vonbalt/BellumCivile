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
        // One score context per search; the scheduler shares only lookup snapshots.
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
            private readonly Dictionary<Hero, int> _dowries = new Dictionary<Hero, int>();
            public readonly Dictionary<Clan, Dictionary<Clan, float>> Politics = new Dictionary<Clan, Dictionary<Clan, float>>();
            private readonly FeudalTitleBehavior _titles;
            private readonly int _day;
            private MarriageTitleClaimCache _claims;
            private readonly Dictionary<Clan, Dictionary<Hero, ClaimConsideration[]>> _claimValues = new Dictionary<Clan, Dictionary<Hero, ClaimConsideration[]>>();
            public readonly FactionManagerBehavior Factions;
            public readonly BellumMarriageEvaluationStats FundingDiagnostics;

            private sealed class ClaimConsideration
            {
                public float Value;
                public readonly List<string> Reasons = new List<string>();
            }

            public EvaluationContext(FactionManagerBehavior factions, bool includePlayer, HashSet<Clan> houses = null, BellumMarriageEvaluationStats stats = null, bool allowTemporary = false, ScheduledMarriageMarket market = null)
            {
                Factions = factions;
                _titles = Campaign.Current.GetCampaignBehavior<FeudalTitleBehavior>();
                _day = (int)CampaignTime.Now.ToDays;
                _market = market;
                // An heir may belong to either candidate house while inheriting a third realm.
                if (market != null) _households.UseCrownHeirs(market.CrownHeirs);
                CrownHeirs.UnionWith(_households.CrownHeirs);
                if (market != null)
                {
                    if (BellumCivileDebug.ShowInGameMessages) FundingDiagnostics = stats;
                    Participants = market.Participants;
                    Realms = market.Realms;
                    return;
                }
                CollectParticipants(includePlayer, houses, stats, allowTemporary, Participants, Realms);
            }

            private readonly ScheduledMarriageMarket _market;

            internal static void CollectParticipants(bool includePlayer, HashSet<Clan> houses,
                BellumMarriageEvaluationStats stats, bool allowTemporary,
                Dictionary<Clan, List<Hero>> participants, Dictionary<Clan, Kingdom> realms)
            {
                foreach (Kingdom realm in Kingdom.All)
                {
                    if (realm == null || realm.IsEliminated || houses != null && !houses.Any(c => c.Kingdom == realm)) continue;
                    foreach (Clan clan in realm.Clans)
                    {
                        if (houses != null && !houses.Contains(clan) || !IsStrategicMarriageClanAllowed(clan, includePlayer)) continue;
                        realms[clan] = MarriagePoliticalRealm(clan);
                        participants[clan] = new List<Hero>();
                        foreach (var hero in HouseholdMembership.Members(clan))
                        {
                            if (stats != null) stats.ParticipantChecks++;
                            var rejection = GetOfferParticipantRejectionReason(hero, includePlayer, allowTemporary);
                            if (rejection != BellumMarriageRejectionReason.None) { stats?.AddRejection(rejection); continue; }
                            participants[clan].Add(hero);
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
                if (health.BloodProspects == null)
                    health.BloodProspects = new HashSet<Hero>(health.Prospects.Where(IsBloodMember));
                float dynasty = !IsBloodMember(member) ? 0 : MarriageMatchmakingRules.DepartureCost(true, health.HasFertileCouple,
                    health.BloodProspects.Count - (health.BloodProspects.Contains(member) ? 1 : 0), foreignRoyal, health.Risk);
                return System.Math.Max(dynasty, health.StaffDepartureCost(member));
            }

            public HouseHealth Health(Clan house)
            {
                if (!_health.TryGetValue(house, out var health)) _health[house] = health = AssessHouseHealth(house);
                return health;
            }

            public bool FertileHousehold(Clan house) => Health(house).HasFertileCouple;
            public int DowryFor(Hero hero)
            {
                if (!_dowries.TryGetValue(hero, out int dowry))
                    _dowries[hero] = dowry = MarriagePayments.Quote(hero, CrownHeirs.Contains(hero),
                        HouseholdMembership.IsFamily(hero) && _households.MustRemain(hero));
                return dowry;
            }

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
                    if (_claims == null) _claims = _market?.GetClaims(_titles, _day) ?? MarriageTitleClaimCache.Build(_titles, _day);
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
                    _bloodMembers[member] = value = HouseholdMembership.IsBloodRelative(member,
                        MarriageHouseholdPolicy.LegalHead(member.Clan));
                return value;
            }
        }

        internal sealed class ScheduledMarriageMarket
        {
            internal readonly Dictionary<Clan, List<Hero>> Participants = new Dictionary<Clan, List<Hero>>();
            internal readonly Dictionary<Clan, Kingdom> Realms = new Dictionary<Clan, Kingdom>();
            internal readonly BellumMarriageEvaluationStats Stats = new BellumMarriageEvaluationStats();
            private readonly Dictionary<Clan, ILookup<bool, Hero>> _bySex = new Dictionary<Clan, ILookup<bool, Hero>>();
            private MarriageTitleClaimCache _claims;
            private int _revision = -1;
            private int _claimDay = -1;
            private MarriageHouseholdPolicy _crownPolicy;
            private int _crownHour = -1;
            private MarriageRealmGeography _geography;
            private readonly Dictionary<Clan, Kingdom> _otherOwners = new Dictionary<Clan, Kingdom>();
            private readonly Dictionary<Clan, List<Hero>> _pledgeCandidates = new Dictionary<Clan, List<Hero>>();

            internal ScheduledMarriageMarket()
            {
                EvaluationContext.CollectParticipants(true, null, Stats, true, Participants, Realms);
                foreach (var entry in Participants) _bySex.Add(entry.Key, entry.Value.ToLookup(hero => hero.IsFemale));
            }

            internal ILookup<bool, Hero> BySex(Clan house) => _bySex[house];

            internal List<Hero> PledgeCandidates(Clan house)
            {
                if (!_pledgeCandidates.TryGetValue(house, out var candidates))
                    _pledgeCandidates[house] = candidates = HouseholdMembership.Members(house).Where(BetrothalBehavior.Potential).ToList();
                return candidates;
            }

            internal bool IsLocal(Clan house, Clan candidate)
            {
                var ownRealm = Realms[house];
                var otherRealm = Realms[candidate];
                if (ownRealm == otherRealm) return true;
                if (_geography == null) _geography = MarriageRealmGeography.Capture(RealmForOwner);
                return _geography.IsLocal(ownRealm, otherRealm);
            }

            private Kingdom RealmForOwner(Clan owner)
            {
                if (owner == null) return null;
                if (Realms.TryGetValue(owner, out var realm)) return realm;
                if (!_otherOwners.TryGetValue(owner, out realm)) _otherOwners[owner] = realm = MarriagePoliticalRealm(owner);
                return realm;
            }

            internal HashSet<Hero> CrownHeirs
            {
                get
                {
                    int hour = CampaignTime.Now.GetHourOfDay;
                    if (_crownPolicy == null || _crownHour != hour)
                    {
                        _crownPolicy = new MarriageHouseholdPolicy();
                        _crownHour = hour;
                    }
                    return _crownPolicy.CrownHeirs;
                }
            }

            internal void InvalidateAfterMatch() => _crownPolicy = null;

            internal MarriageTitleClaimCache GetClaims(FeudalTitleBehavior titles, int day)
            {
                int revision = titles?.RuntimeRevision ?? 0;
                if (_claims == null || revision != _revision || day != _claimDay)
                {
                    _claims = MarriageTitleClaimCache.Build(titles, day);
                    _revision = revision;
                    _claimDay = day;
                }
                return _claims;
            }
        }

        internal sealed class ScheduledMarriageSearch
        {
            private IEnumerator<bool> _steps;
            internal BellumMarriageMatch Best;
            internal BetrothalMatch Betrothal;
            internal readonly BellumMarriageEvaluationStats Stats;
            internal bool Complete => _steps == null;

            internal ScheduledMarriageSearch(Clan house, bool household, bool includePlayer, FactionManagerBehavior factions,
                ScheduledMarriageMarket market, int day)
            {
                Stats = new BellumMarriageEvaluationStats();
                var context = new EvaluationContext(factions, includePlayer, stats: Stats, allowTemporary: true, market: market);
                Stats.MarketHasTemporaryParticipants = market?.Stats.TemporaryParticipants > 0;
                _steps = SearchOpportunity(house, household, includePlayer, context, market, this, day).GetEnumerator();
            }

            internal int Advance(int budget)
            {
                int spent = 0;
                while (_steps != null && spent < budget)
                {
                    if (!_steps.MoveNext())
                    {
                        _steps.Dispose();
                        _steps = null;
                        break;
                    }
                    spent++;
                }
                return spent;
            }
        }

        private static IEnumerable<bool> SearchOpportunity(Clan house, bool household, bool includePlayer,
            EvaluationContext context, ScheduledMarriageMarket market, ScheduledMarriageSearch search, int day)
        {
            foreach (var step in SearchAdultOpportunity(house, household, includePlayer, context, market, search, day)) yield return step;
            if (!household && search.Best == null && BetrothalBehavior.Instance != null && BetrothalBehavior.CanArrange
                && context.Participants.ContainsKey(house))
                foreach (var step in SearchBetrothals(house, includePlayer, context, market, search, day)) yield return step;
        }

        private static IEnumerable<bool> SearchAdultOpportunity(Clan house, bool household, bool includePlayer,
            EvaluationContext context, ScheduledMarriageMarket market, ScheduledMarriageSearch search, int day)
        {
            var stats = search.Stats;
            if (!context.Participants.TryGetValue(house, out var members)) yield break;
            var initiators = members.Where(member => member.Clan == house && HouseholdMembership.IsFamily(member) != household).ToList();
            if (initiators.Count == 0) yield break;
            int localLegalHouses = 0;
            int threshold = BellumCivileOptions.MarriageAcceptanceThreshold;
            var distant = new List<Clan>();
            foreach (var entry in context.Participants)
            {
                yield return true; // Count rejected houses and pairs against the same work budget.
                if (entry.Key == house || entry.Value.Count == 0 || !includePlayer && entry.Key == Clan.PlayerClan) continue;
                if (!market.IsLocal(house, entry.Key))
                {
                    distant.Add(entry.Key);
                    continue;
                }
                int previousLegalPairs = stats.LegalPairs;
                foreach (var step in SearchMarriageHouse(house, entry.Key, initiators, household, includePlayer,
                    threshold, context, market, search, distantPass: false)) yield return step;
                if (stats.LegalPairs > previousLegalPairs) localLegalHouses++;
            }

            if (distant.Count == 0) yield break;
            bool royal = context.Realms[house]?.RulingClan == house;
            float risk = !household && search.Best == null ? context.Health(house).Risk : 0;
            var pass = MarriageSearchScope.DistantPass(household, royal, search.Best != null, risk, localLegalHouses);
            if (pass == DistantMarriageSearch.None) yield break;

            stats.DistantPasses++;
            // Rotate a stable list so a capped pass does not always favor the same distant houses.
            distant.Sort((a, b) => System.StringComparer.Ordinal.Compare(a.StringId, b.StringId));
            int start = MarriageSearchScope.DistantStart(house.StringId, day, distant.Count);
            bool seeksGroom = initiators.Any(member => member.IsFemale);
            bool seeksBride = initiators.Any(member => !member.IsFemale);
            int searchedHouses = 0;
            for (int i = 0; i < distant.Count && searchedHouses < MarriageSearchScope.DistantHouseLimit
                && stats.DistantPairChecks < MarriageSearchScope.DistantPairLimit; i++)
            {
                yield return true;
                Clan candidate = distant[(start + i) % distant.Count];
                if (pass == DistantMarriageSearch.RoyalHouses && context.Realms[candidate]?.RulingClan != candidate) continue;
                var bySex = market.BySex(candidate);
                if (!(seeksGroom && bySex.Contains(false)) && !(seeksBride && bySex.Contains(true))) continue;
                if (GetCandidateHouseRejectionReason(house, candidate, context.Factions, includePlayer)
                    != BellumMarriageRejectionReason.None) continue;
                searchedHouses++;
                foreach (var step in SearchMarriageHouse(house, candidate, initiators, household, includePlayer,
                    threshold, context, market, search, distantPass: true)) yield return step;
            }
        }

        private static IEnumerable<bool> SearchMarriageHouse(Clan house, Clan target, List<Hero> initiators,
            bool household, bool includePlayer, int threshold, EvaluationContext context, ScheduledMarriageMarket market,
            ScheduledMarriageSearch search, bool distantPass)
        {
            var stats = search.Stats;
            var candidatesBySex = market.BySex(target);
            foreach (Hero member in initiators)
            {
                yield return true;
                if (member.Clan != house || MarriageReserved(member)) continue;
                var rejected = GetCandidateHouseRejectionReason(house, target, context.Factions, includePlayer);
                if (rejected != BellumMarriageRejectionReason.None) { stats.AddRejection(rejected); continue; }
                foreach (Hero candidate in candidatesBySex[!member.IsFemale])
                {
                    if (distantPass && stats.DistantPairChecks >= MarriageSearchScope.DistantPairLimit) yield break;
                    yield return true;
                    if (distantPass) stats.DistantPairChecks++;
                    if (candidate.Clan != target || member.Clan != house || MarriageReserved(candidate) || MarriageReserved(member)) continue;
                    rejected = GetPairRejectionReason(member, candidate, context.Factions, includePlayer, allowTemporary: true);
                    if (rejected != BellumMarriageRejectionReason.None) { stats.AddRejection(rejected); continue; }
                    var match = EvaluateOutcome(member, candidate, context, false);
                    if (match == null) continue;
                    match.HouseholdOpportunity = household;
                    stats.LegalPairs++;
                    if (match.FundingBlocked) { stats.UnaffordablePairs++; continue; }
                    bool firstAccepts = member.Clan == Clan.PlayerClan || match.SuitorAcceptance >= threshold;
                    bool secondAccepts = candidate.Clan == Clan.PlayerClan || match.CandidateAcceptance >= threshold;
                    if (!firstAccepts || !secondAccepts)
                    {
                        if (!firstAccepts) stats.FirstHouseRefused++;
                        if (!secondAccepts) stats.SecondHouseRefused++;
                        if (match.Score > stats.BestRejectedScore)
                        { stats.BestRejectedScore = match.Score; stats.ClosestRefusedMatch = match; }
                        continue;
                    }
                    stats.AcceptedPairs++;
                    BellumMarriageMatch best = search.Best;
                    if (best == null || match.Score > best.Score
                        || match.Score == best.Score && match.SuitorAcceptance + match.CandidateAcceptance
                            > best.SuitorAcceptance + best.CandidateAcceptance) search.Best = match;
                }
            }
        }

        private static bool MarriageReserved(Hero hero) => StrategicMarriageBehavior.HasMarriageOfferFor(hero)
            || BetrothalBehavior.IsReserved(hero)
            || Campaign.Current?.GetCampaignBehavior<StrategicMarriageBehavior>()?.HasPendingProspect(hero) == true
            || CourtAgendaBehavior.Current?.HasDynasticReservation(hero, hero) == true;

        internal static BellumMarriageMatch RevalidateScheduledMarriage(BellumMarriageMatch match, bool household, bool includePlayer)
        {
            var first = match.Suitor;
            var second = match.Candidate;
            if (first.Clan != match.Outcome.FirstHouse || second.Clan != match.Outcome.SecondHouse
                || !IsStrategicMarriageClanAllowed(first.Clan, includePlayer) || !IsStrategicMarriageClanAllowed(second.Clan, includePlayer)
                || HouseholdMembership.IsFamily(first) == household || MarriageReserved(first) || MarriageReserved(second)) return null;
            var current = ReevaluateProspect(first, second, match.Outcome.Destination);
            if (current != null) current.HouseholdOpportunity = household;
            return current;
        }

        // Console-only search: retain normal acceptance and household rules, skipping only scheduling.
        internal static List<BellumMarriageMatch> FindPlayerClanTestOffers(FactionManagerBehavior factions,
            bool? matrilineal, out string diagnostics, Hero playerMember = null)
        {
            var matches = new List<BellumMarriageMatch>();
            int threshold = BellumCivileOptions.MarriageAcceptanceThreshold;
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
                            if (match.FundingBlocked || match.Score < threshold)
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
                    + $"destination={closestRefused.Outcome.Destination.StringId}; npc_score={closestRefused.Score:0.##}/{threshold}; "
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
                if (match != null && (best == null || best.FundingBlocked && !match.FundingBlocked
                    || best.FundingBlocked == match.FundingBlocked && match.Score > best.Score)) best = match;
            }
            return best;
        }

        private static BellumMarriageMatch EvaluateHousehold(Hero first, Hero second, EvaluationContext context,
            Clan destination, bool requireAcceptance)
        {
            if (destination == null || destination.IsEliminated) return null;
            if (!MarriageParticipantPolicy.SameMarriageStanding(first, second)) return null;
            var outcome = new MarriageOutcome(first, second, destination,
                context.CrownHeirs.Contains(first), context.CrownHeirs.Contains(second));
            outcome.FirstParentalEstateProspects = context.ParentalEstateProspects(first);
            outcome.SecondParentalEstateProspects = context.ParentalEstateProspects(second);
            var match = new BellumMarriageMatch { Suitor = first, Candidate = second, Outcome = outcome };
            bool firstFamily = HouseholdMembership.IsFamily(first), secondFamily = HouseholdMembership.IsFamily(second);
            match.PairClass = firstFamily && secondFamily ? "family" : "retainers";
            match.HasDynasticContinuity = outcome.HasReproductiveOpportunity
                && (destination == first.Clan && context.IsBloodMember(first) && !context.FertileHousehold(first.Clan)
                    || destination == second.Clan && context.IsBloodMember(second) && !context.FertileHousehold(second.Clan));
            match.IsDomestic = context.Realms[first.Clan] == context.Realms[second.Clan];
            bool politics = MarriageParticipantPolicy.IsDynasticPair(first, second)
                && (context.PoliticalValue(first.Clan, second.Clan) > 0f || context.PoliticalValue(second.Clan, first.Clan) > 0f);
            match.HasRoyalPolitics = match.IsDomestic && politics
                && (context.Realms[first.Clan]?.RulingClan == first.Clan || context.Realms[second.Clan]?.RulingClan == second.Clan);
            match.HasForeignPolitics = !match.IsDomestic && politics;
            match.SuitorAcceptance = EvaluateHouse(first, second, outcome, context, match.Reasons);
            match.CandidateAcceptance = EvaluateHouse(second, first, outcome, context, match.Reasons);
            bool firstPlayer = first.Clan == Clan.PlayerClan, secondPlayer = second.Clan == Clan.PlayerClan;
            if (requireAcceptance && !MarriageOutcome.BothAccept(match.SuitorAcceptance, match.CandidateAcceptance,
                firstPlayer, secondPlayer, BellumCivileOptions.MarriageAcceptanceThreshold)) return null;
            match.Score = MarriageOutcome.Rank(match.SuitorAcceptance, match.CandidateAcceptance, firstPlayer, secondPlayer);
            Hero leaving = destination == first.Clan ? second : first;
            match.Dowry = context.DowryFor(leaving);
            match.FundingBlocked = !MarriagePayments.CanTransfer(destination, leaving.Clan, match.Dowry);
            if (match.FundingBlocked)
            {
                context.FundingDiagnostics?.ConsiderFundingBlock(first, second, destination, leaving.Clan, match.Score,
                    match.Dowry, match.Dowry, deposit: false);
                if (requireAcceptance) return null;
                match.Reasons.Add("receiving house cannot fund dowry");
            }
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
            var health = context.Health(house);
            bool family = HouseholdMembership.IsFamily(member), spouseFamily = HouseholdMembership.IsFamily(spouse);
            bool dynasticPair = family && spouseFamily;
            float risk = family ? health.Risk : health.ManpowerRisk;
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
            float status = dynasticPair ? System.Math.Max(0, house.Tier - other.Tier) * C.MarriageScoreRankGapPenalty : 0;
            status *= preference;
            score -= status;
            if (!family)
            {
                float ordinary = MarriageHealthRules.HouseholdMarriageValue(spouseFamily);
                score += ordinary;
                reasons.Add($"{house.StringId}: household marriage +{ordinary:0}");
            }
            float staff = outcome.Destination == house ? health.StaffIncomingValue(member, spouse) : 0;
            score += staff;
            if (staff > 0) reasons.Add($"{house.StringId}: incoming service +{staff:0.##}");

            bool bloodMember = context.IsBloodMember(member);
            float kinship = MarriageHealthRules.OutgoingKinshipValue(risk, outcome.HasReproductiveOpportunity,
                bloodMember, outcome.Destination != house);
            score += kinship;
            if (kinship > 0) reasons.Add($"{house.StringId}: outgoing bloodline alliance +{kinship:0}");
            if (outcome.HasReproductiveOpportunity)
            {
                bool bloodline = bloodMember || HouseholdMembership.IsBloodRelative(spouse, MarriageHouseholdPolicy.LegalHead(house));
                float continuity = family
                    ? MarriageHealthRules.Continuity(bloodline ? risk : 0, true, outcome.NewbornHouse == house, bloodMember) : 0;
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
                dynasticPair && realm != otherRealm && realm?.RulingClan == house && otherRealm?.RulingClan == other);
            if (departure > 0)
            {
                score -= departure;
                reasons.Add($"{house.StringId}: household/reserve departure -{departure:0.##}");
            }
            float politics = dynasticPair ? context.PoliticalValue(house, other) * strategy : 0;
            score += politics;
            if (politics != 0f) reasons.Add($"{house.StringId}: political ties {politics:+0;-0}");
            if (outcome.Destination == house)
                score += context.IncomingClaimValue(house, spouse, reasons, outcome.HasReproductiveOpportunity);
            float prestige = dynasticPair ? MarriageHealthRules.Prestige(house.Tier, other.Tier) * strategy : 0;
            score += prestige;
            if (prestige > 0) reasons.Add($"{house.StringId}: prestigious house connection +{prestige:0}");
            float court = dynasticPair ? CourtAgendaBehavior.Current?.DynasticMarriageBonus(member, spouse) ?? 0 : 0;
            if (court != 0) { score += court; reasons.Add($"{house.StringId}: Crown-backed royal marriage +{court:0}"); }
            reasons.Add($"{house.StringId}: class={(family ? "family" : "retainer")}; dynasty risk={health.Risk:0.00}; manpower risk={health.ManpowerRisk:0.00}; status -{status:0}; future estates remain personal and conditional");
            return score;
        }
    }
}
