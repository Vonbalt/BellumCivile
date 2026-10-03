using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.Core;
using TaleWorlds.Library;
using C = BellumCivile.BellumCivileConstants;

namespace BellumCivile.Behaviors
{
    internal sealed class BellumMarriageMatch
    {
        public Hero Suitor { get; set; }
        public Hero Candidate { get; set; }
        public float Score { get; set; }
        public float SuitorAcceptance { get; set; }
        public float CandidateAcceptance { get; set; }
        public MarriageOutcome Outcome { get; set; }
        public bool HasDynasticContinuity { get; set; }
        public bool IsDomestic { get; set; }
        public bool HasRoyalPolitics { get; set; }
        public bool HasForeignPolitics { get; set; }
        public List<string> Reasons { get; } = new List<string>();
    }

    internal enum BellumMarriageRejectionReason
    {
        None,
        SameClan,
        SameHero,
        SameSex,
        AlreadyMarried,
        TooYoung,
        FemaleAboveMaxAge,
        PlayerClan,
        MinorMercenaryOrNeutralClan,
        EliminatedClan,
        InvalidOrInactiveHero,
        NoKingdom,
        KingdomAtWar,
        OppositeCivilWarSides,
        TemporarilyUnavailable,
        CannotMarry,
        VanillaRejected
    }

    internal sealed class BellumMarriageEvaluationStats
    {
        public int ParticipantChecks, EligibleParticipants, TemporaryParticipants;
        public int LegalPairs, AcceptedPairs, FirstHouseRefused, SecondHouseRefused;
        public float BestRejectedScore = float.NegativeInfinity;
        public BellumMarriageMatch ClosestRefusedMatch;
        public int KingdomsSkippedAtWar;
        public int RejectedAlreadyMarried;
        public int RejectedTooYoung;
        public int RejectedFemaleAboveMaxAge;
        public int RejectedPlayerClan;
        public int RejectedMinorMercenaryOrNeutralClan;
        public int RejectedEliminatedClan;
        public int RejectedInvalidOrInactiveHero;
        public int RejectedNoKingdom;
        public int RejectedKingdomAtWar;
        public int RejectedOppositeCivilWarSides;
        public int RejectedCannotMarry;
        public int RejectedVanillaModel;
        public int RejectedOther;

        public void AddRejection(BellumMarriageRejectionReason reason)
        {
            switch (reason)
            {
                case BellumMarriageRejectionReason.TemporarilyUnavailable:
                    TemporaryParticipants++;
                    break;
                case BellumMarriageRejectionReason.AlreadyMarried:
                    RejectedAlreadyMarried++;
                    break;
                case BellumMarriageRejectionReason.TooYoung:
                    RejectedTooYoung++;
                    break;
                case BellumMarriageRejectionReason.FemaleAboveMaxAge:
                    RejectedFemaleAboveMaxAge++;
                    break;
                case BellumMarriageRejectionReason.PlayerClan:
                    RejectedPlayerClan++;
                    break;
                case BellumMarriageRejectionReason.MinorMercenaryOrNeutralClan:
                    RejectedMinorMercenaryOrNeutralClan++;
                    break;
                case BellumMarriageRejectionReason.EliminatedClan:
                    RejectedEliminatedClan++;
                    break;
                case BellumMarriageRejectionReason.InvalidOrInactiveHero:
                    RejectedInvalidOrInactiveHero++;
                    break;
                case BellumMarriageRejectionReason.NoKingdom:
                    RejectedNoKingdom++;
                    break;
                case BellumMarriageRejectionReason.KingdomAtWar:
                    RejectedKingdomAtWar++;
                    break;
                case BellumMarriageRejectionReason.OppositeCivilWarSides:
                    RejectedOppositeCivilWarSides++;
                    break;
                case BellumMarriageRejectionReason.CannotMarry:
                    RejectedCannotMarry++;
                    break;
                case BellumMarriageRejectionReason.VanillaRejected:
                    RejectedVanillaModel++;
                    break;
                case BellumMarriageRejectionReason.SameClan:
                case BellumMarriageRejectionReason.SameHero:
                case BellumMarriageRejectionReason.SameSex:
                    RejectedOther++;
                    break;
            }
        }

        public void Merge(BellumMarriageEvaluationStats other)
        {
            if (other == null) return;
            ParticipantChecks += other.ParticipantChecks;
            EligibleParticipants += other.EligibleParticipants;
            TemporaryParticipants += other.TemporaryParticipants;
            LegalPairs += other.LegalPairs;
            AcceptedPairs += other.AcceptedPairs;
            FirstHouseRefused += other.FirstHouseRefused;
            SecondHouseRefused += other.SecondHouseRefused;
            BestRejectedScore = MathF.Max(BestRejectedScore, other.BestRejectedScore);
            KingdomsSkippedAtWar += other.KingdomsSkippedAtWar;
            RejectedAlreadyMarried += other.RejectedAlreadyMarried;
            RejectedTooYoung += other.RejectedTooYoung;
            RejectedFemaleAboveMaxAge += other.RejectedFemaleAboveMaxAge;
            RejectedPlayerClan += other.RejectedPlayerClan;
            RejectedMinorMercenaryOrNeutralClan += other.RejectedMinorMercenaryOrNeutralClan;
            RejectedEliminatedClan += other.RejectedEliminatedClan;
            RejectedInvalidOrInactiveHero += other.RejectedInvalidOrInactiveHero;
            RejectedNoKingdom += other.RejectedNoKingdom;
            RejectedKingdomAtWar += other.RejectedKingdomAtWar;
            RejectedOppositeCivilWarSides += other.RejectedOppositeCivilWarSides;
            RejectedCannotMarry += other.RejectedCannotMarry;
            RejectedVanillaModel += other.RejectedVanillaModel;
            RejectedOther += other.RejectedOther;
        }
    }

    internal static partial class BellumMarriageStrategyHelper
    {

        public static bool IsStrategicMarriageInitiator(Hero hero)
        {
            return GetOfferParticipantRejectionReason(hero, false) == BellumMarriageRejectionReason.None;
        }

        public static float CalculateDynasticNeed(Clan clan)
        {
            if (clan == null || clan.IsEliminated || clan.IsClanTypeMercenary || clan.IsMinorFaction || clan.IsBanditFaction)
                return 0f;

            List<Hero> adultNobles = clan.Heroes
                .Where(h => h != null && h.IsAlive && h.IsLord && !h.IsChild)
                .ToList();

            int childCount = clan.Heroes.Count(h => h != null && h.IsAlive && h.IsChild);
            float score = 0f;

            if (!HasHouseholdFertileCouple(clan))
                score += C.MarriageNeedNoFertileCouple;

            if (adultNobles.Count <= 1)
                score += C.MarriageNeedOneAdultNoble;
            else if (adultNobles.Count <= 2)
                score += C.MarriageNeedFewAdultNobles;

            Hero leader = clan.Leader;
            if (leader != null)
            {
                if (leader.Age >= 65f)
                    score += C.MarriageNeedVeryOldLeader;
                else if (leader.Age >= 55f)
                    score += C.MarriageNeedOldLeader;
            }

            if (childCount == 0)
                score += C.MarriageNeedNoChildren;

            score += clan.Tier * C.MarriageNeedTierScale;
            return MathF.Max(0f, score);
        }

        public static float CalculateAnnualMarriageChance(Hero hero)
        {
            if (!IsStrategicMarriageInitiator(hero))
                return 0f;

            float dynasticNeed = CalculateDynasticNeed(hero.Clan);
            float chance = C.MarriageStrategyBaseAnnualChance + (dynasticNeed * C.MarriageStrategyDynasticNeedChanceScale);
            return MathF.Clamp(chance, 0f, C.MarriageStrategyMaximumAnnualChance);
        }

        public static BellumMarriageMatch FindBestMatch(Hero suitor, FactionManagerBehavior factionManager, DynasticHeirBehavior heirBehavior)
        {
            BellumMarriageMatch best = FindBestCandidate(suitor, factionManager, heirBehavior);
            return best != null && best.Score >= C.MarriageStrategyMinimumScore ? best : null;
        }

        public static BellumMarriageMatch FindBestCandidate(Hero suitor, FactionManagerBehavior factionManager, DynasticHeirBehavior heirBehavior, BellumMarriageEvaluationStats stats = null, bool includePlayerClan = false)
        {
            if (!IsStrategicMarriageInitiator(suitor))
                return null;

            Kingdom suitorKingdom = suitor.Clan.Kingdom;
            BellumMarriageMatch best = null;
            var context = new EvaluationContext(factionManager, includePlayerClan);

            foreach (Kingdom kingdom in Kingdom.All)
            {
                if (kingdom == null || kingdom.IsEliminated)
                    continue;

                if (suitorKingdom != null && kingdom.IsAtWarWith(suitorKingdom))
                {
                    stats?.AddRejection(BellumMarriageRejectionReason.KingdomAtWar);
                    if (stats != null)
                        stats.KingdomsSkippedAtWar++;
                    continue;
                }

                foreach (Clan clan in kingdom.Clans)
                {
                    BellumMarriageRejectionReason clanRejection = GetCandidateClanRejectionReason(suitor, clan, factionManager, includePlayerClan);
                    if (clanRejection != BellumMarriageRejectionReason.None)
                    {
                        stats?.AddRejection(clanRejection);
                        continue;
                    }

                    foreach (Hero candidate in clan.Heroes)
                    {
                        BellumMarriageRejectionReason heroRejection = GetCandidateRejectionReason(suitor, candidate);
                        if (heroRejection != BellumMarriageRejectionReason.None)
                        {
                            stats?.AddRejection(heroRejection);
                            continue;
                        }

                        BellumMarriageMatch match = EvaluateOutcome(suitor, candidate, context);
                        if (match == null)
                            continue;

                        if (best == null || match.Score > best.Score)
                            best = match;
                    }
                }
            }

            return best;
        }

        public static BellumMarriageMatch FindBestCandidateIncludingPlayerClan(Hero suitor, FactionManagerBehavior factionManager, DynasticHeirBehavior heirBehavior, BellumMarriageEvaluationStats stats = null)
        {
            BellumMarriageMatch best = FindBestCandidate(suitor, factionManager, heirBehavior, stats, includePlayerClan: true);
            BellumMarriageMatch bestPlayerClanOffer = FindBestPlayerClanOffer(suitor?.Clan, factionManager, heirBehavior, stats);
            if (bestPlayerClanOffer != null && (best == null || bestPlayerClanOffer.Score > best.Score))
                best = bestPlayerClanOffer;

            return best;
        }

        public static BellumMarriageMatch FindBestPlayerClanOffer(Clan offeringClan, FactionManagerBehavior factionManager, DynasticHeirBehavior heirBehavior, BellumMarriageEvaluationStats stats = null)
        {
            if (!IsStrategicMarriageClanAllowed(offeringClan, allowPlayerClan: false) || Clan.PlayerClan == null)
                return null;

            BellumMarriageMatch best = null;
            var context = new EvaluationContext(factionManager, true);
            foreach (Hero suitor in offeringClan.Heroes)
            {
                if (GetOfferParticipantRejectionReason(suitor, allowPlayerClan: false) != BellumMarriageRejectionReason.None)
                    continue;

                foreach (Hero playerClanHero in Clan.PlayerClan.Heroes)
                {
                    BellumMarriageRejectionReason playerRejection = GetOfferParticipantRejectionReason(playerClanHero, allowPlayerClan: true);
                    if (playerRejection != BellumMarriageRejectionReason.None)
                    {
                        stats?.AddRejection(playerRejection);
                        continue;
                    }

                    BellumMarriageRejectionReason pairRejection = GetPairRejectionReason(suitor, playerClanHero, factionManager, allowPlayerClan: true);
                    if (pairRejection != BellumMarriageRejectionReason.None)
                    {
                        stats?.AddRejection(pairRejection);
                        continue;
                    }

                    BellumMarriageMatch match = EvaluateOutcome(suitor, playerClanHero, context);
                    if (match == null)
                        continue;

                    if (best == null || match.Score > best.Score)
                        best = match;
                }
            }

            return best;
        }

        public static string BuildPlayerClanOfferDiagnostics(FactionManagerBehavior factionManager)
        {
            if (Clan.PlayerClan == null)
                return "player clan missing";

            BellumMarriageEvaluationStats stats = new BellumMarriageEvaluationStats();
            int offeringClans = 0;
            int validOfferingParticipants = 0;
            int validPlayerParticipants = 0;
            int pairChecks = 0;
            int suitablePairs = 0;

            foreach (Clan clan in Clan.All)
            {
                if (clan == null || clan == Clan.PlayerClan)
                    continue;

                if (!IsStrategicMarriageClanAllowed(clan, allowPlayerClan: false))
                    continue;

                offeringClans++;
                foreach (Hero suitor in clan.Heroes)
                {
                    BellumMarriageRejectionReason suitorRejection = GetOfferParticipantRejectionReason(suitor, allowPlayerClan: false);
                    if (suitorRejection != BellumMarriageRejectionReason.None)
                    {
                        stats.AddRejection(suitorRejection);
                        continue;
                    }

                    validOfferingParticipants++;
                    foreach (Hero playerClanHero in Clan.PlayerClan.Heroes)
                    {
                        BellumMarriageRejectionReason playerRejection = GetOfferParticipantRejectionReason(playerClanHero, allowPlayerClan: true);
                        if (playerRejection != BellumMarriageRejectionReason.None)
                        {
                            stats.AddRejection(playerRejection);
                            continue;
                        }

                        validPlayerParticipants++;
                        pairChecks++;

                        BellumMarriageRejectionReason pairRejection = GetPairRejectionReason(suitor, playerClanHero, factionManager, allowPlayerClan: true);
                        if (pairRejection != BellumMarriageRejectionReason.None)
                        {
                            stats.AddRejection(pairRejection);
                            continue;
                        }

                        suitablePairs++;
                    }
                }
            }

            return $"offering_clans={offeringClans}; valid_offering_heroes={validOfferingParticipants}; valid_player_heroes_seen={validPlayerParticipants}; pair_checks={pairChecks}; suitable_pairs={suitablePairs}; rejections: already_married={stats.RejectedAlreadyMarried}, too_young={stats.RejectedTooYoung}, female_above_max={stats.RejectedFemaleAboveMaxAge}, player_clan={stats.RejectedPlayerClan}, minor_or_neutral={stats.RejectedMinorMercenaryOrNeutralClan}, eliminated={stats.RejectedEliminatedClan}, invalid={stats.RejectedInvalidOrInactiveHero}, no_kingdom={stats.RejectedNoKingdom}, war={stats.RejectedKingdomAtWar}, civil_war={stats.RejectedOppositeCivilWarSides}, cannot_marry={stats.RejectedCannotMarry}, vanilla_model={stats.RejectedVanillaModel}, other={stats.RejectedOther}.";
        }

        private static bool AreKingdomsAllied(Kingdom firstKingdom, Kingdom secondKingdom)
        {
            if (firstKingdom == null || secondKingdom == null || firstKingdom == secondKingdom)
                return false;

            return firstKingdom.AlliedKingdoms.Contains(secondKingdom)
                || secondKingdom.AlliedKingdoms.Contains(firstKingdom);
        }

        private static float CalculateRoyalPoliticalValue(Clan royalClan, Clan targetClan, Kingdom kingdom, FactionManagerBehavior factionManager, List<string> reasons)
        {
            if (royalClan == null || targetClan == null || kingdom == null || kingdom.RulingClan != royalClan || targetClan == royalClan)
                return 0f;

            if (MarriagePoliticalRealm(targetClan) != kingdom)
                return 0f;

            if (MarriageAllianceHelper.HasMarriageAlliance(targetClan, royalClan))
                return 0f;

            float score = C.MarriageScoreRulerPacifyBase;
            reasons.Add("royal pacification");

            FactionObject rebelFaction = factionManager?.GetRebelFaction(targetClan);
            if (rebelFaction != null)
            {
                if (rebelFaction.Leader == targetClan)
                {
                    score += C.MarriageScoreRebelFactionLeader;
                    reasons.Add("rebel leader");
                }
                else
                {
                    score += C.MarriageScoreRebelFactionMember;
                    reasons.Add("rebel supporter");
                }

                score += rebelFaction.Discontent * C.MarriageScoreRebelDiscontentScale;
            }

            FactionObject ideology = factionManager?.GetIdeologicalFaction(targetClan);
            if (ideology != null)
            {
                var favored = Campaign.Current.GetCampaignBehavior<CourtAgendaBehavior>()?.GetFavoredBloc(kingdom);
                score += favored == ideology.Type ? 20f : 10f;
                score += MathF.Clamp(-ideology.Mood, 0f, 100f) * 0.15f;
            }

            score += MathF.Clamp(RebellionIntentCalculator.Assess(targetClan).Total, 0f, 100f) * 0.2f;
            score += MathF.Min(50f, Campaign.Current.Models.DiplomacyModel.GetClanStrength(targetClan) * C.MarriageScoreTargetPowerScale);
            score += MathF.Clamp(targetClan.Influence * C.MarriageScoreTargetInfluenceScale, 0f, 40f);
            return score;
        }

        private static float CalculateDirectionalTitleClaimMarriageValue(
            Clan seekerClan,
            Hero seekerHero,
            Clan claimClan,
            Hero claimHero,
            MarriageTitleClaimCache cache,
            List<string> reasons,
            float strategyScale = 1f,
            bool canFoundLine = false)
        {
            if (seekerClan == null || claimClan == null || seekerClan == claimClan)
                return 0f;

            var realmId = MarriagePoliticalRealm(seekerClan)?.StringId;
            float ambition = CalculateTitleAmbitionMultiplier(seekerHero ?? seekerClan.Leader);
            float bestScore = 0f;
            FeudalClaimStrength bestStrength = FeudalClaimStrength.Weak;
            bool bestLocal = false;
            bool bestRealm = false;
            string bestSource = null;
            FeudalTitleRecord bestTitle = null;

            float Value(FeudalTitleRecord title, FeudalClaimStrength strength, bool local, bool realm)
            {
                float value = (strength == FeudalClaimStrength.Strong ? C.MarriageScoreStrongTitleClaim : C.MarriageScoreWeakTitleClaim)
                    + C.MarriageScorePersonalTitleClaimCarrier;
                if (local) value += C.MarriageScoreLocalTitleClaim;
                else if (realm) value += C.MarriageScoreRealmTitleClaim;
                if (title.TitleType > FeudalTitleType.Barony) value += (int)title.TitleType * C.MarriageScoreHigherTitleClaimTierScale;
                // Remote legal rights are real, but less useful than rights near the receiving house.
                return MathF.Min(C.MarriageScoreTitleClaimMax, value * ambition) * (local || realm ? 1f : .25f) * strategyScale;
            }

            void Consider(FeudalTitleRecord title, FeudalClaimStrength strength, string source, float certainty = 1f)
            {
                if (title.DeJureHolderClanId == seekerClan.StringId || title.DeFactoHolderClanId == seekerClan.StringId)
                    return;
                var existing = cache.ExistingStrength(seekerClan.StringId, title.TitleId);
                if (existing.HasValue && existing.Value >= strength) return;
                bool local = cache.IsLocalTo(title, seekerClan);
                bool realm = !string.IsNullOrWhiteSpace(title.AssociatedKingdomId)
                    && title.AssociatedKingdomId == realmId;
                float claimScore = Value(title, strength, local, realm);
                if (existing.HasValue) claimScore -= Value(title, existing.Value, local, realm);
                claimScore *= certainty;
                if (claimScore > bestScore)
                {
                    bestScore = claimScore;
                    bestStrength = strength;
                    bestSource = source;
                    bestLocal = local;
                    bestRealm = realm;
                    bestTitle = title;
                }
            }

            bool receivingLine = canFoundLine || claimHero?.Children.Any(h => h.Clan == seekerClan
                && FeudalTitleBehavior.IsEligibleClaimHeir(h)) == true;
            if (receivingLine && cache.ClaimsByClan.TryGetValue(claimClan.StringId, out var claims))
                foreach (var claim in claims)
                {
                    bool personalCarrier = claimHero != null && claim.CarrierHeroId == claimHero.StringId;
                    if (!personalCarrier) continue;
                    var inherited = FeudalTitleBehavior.GetInheritedClaimStrength(claim);
                    if (inherited.HasValue && cache.TitlesById.TryGetValue(claim.TargetTitleId, out var title))
                        Consider(title, inherited.Value, "conditional inherited claim", .25f);
                }

            // Reuse the actual wedding's blood-relative eligibility; never register claims here.
            if (claimHero?.Clan == claimClan && cache.CanConveyBirthright(claimHero, claimClan))
                foreach (var title in cache.GetHeldTitles(claimClan))
                    if (title.DeJureHolderClanId == claimClan.StringId)
                        Consider(title, FeudalClaimStrength.Strong, "marriage birthright forecast");

            if (bestScore >= 1f)
            {
                string scope = bestLocal ? "local" : bestRealm ? "realm" : "distant";
                reasons?.Add($"{bestSource}: {bestStrength.ToString().ToLowerInvariant()} {scope} title claim: {bestTitle?.Name ?? "unknown"} +{bestScore:0}");
            }

            return bestScore;
        }

        private static float CalculateTitleAmbitionMultiplier(Hero hero)
        {
            if (hero == null)
                return 1f;

            float multiplier = 1f;

            int calculating = hero.GetTraitLevel(DefaultTraits.Calculating);
            if (calculating > 0)
                multiplier += calculating * 0.15f;
            else if (calculating < 0)
                multiplier += calculating * 0.05f;

            int honor = hero.GetTraitLevel(DefaultTraits.Honor);
            if (honor < 0)
                multiplier += -honor * 0.10f;
            else if (honor > 0)
                multiplier -= honor * 0.05f;

            int valor = hero.GetTraitLevel(DefaultTraits.Valor);
            if (valor > 0)
                multiplier += valor * 0.05f;

            int generosity = hero.GetTraitLevel(DefaultTraits.Generosity);
            if (generosity > 0)
                multiplier -= generosity * 0.05f;

            return MathF.Clamp(multiplier, 0.75f, 1.6f);
        }

        private sealed class MarriageTitleClaimCache
        {
            public int Day { get; private set; }
            public Dictionary<string, FeudalTitleRecord> TitlesById { get; private set; } = new Dictionary<string, FeudalTitleRecord>();
            public Dictionary<string, List<FeudalClaimRecord>> ClaimsByClan { get; private set; } = new Dictionary<string, List<FeudalClaimRecord>>();
            private Dictionary<string, List<FeudalTitleRecord>> _heldTitlesByClan = new Dictionary<string, List<FeudalTitleRecord>>();
            private readonly Dictionary<string, Dictionary<string, FeudalClaimStrength>> _existingStrengths = new Dictionary<string, Dictionary<string, FeudalClaimStrength>>();
            private readonly Dictionary<Hero, bool> _birthrightEligible = new Dictionary<Hero, bool>();
            private readonly Dictionary<Clan, TitleScope> _scopes = new Dictionary<Clan, TitleScope>();
            private static readonly List<FeudalTitleRecord> EmptyTitles = new List<FeudalTitleRecord>();

            private sealed class TitleScope
            {
                public readonly HashSet<string> Titles = new HashSet<string>();
                public readonly HashSet<string> Parents = new HashSet<string>();
            }

            public bool IsLocalTo(FeudalTitleRecord title, Clan clan)
            {
                if (!_scopes.TryGetValue(clan, out var scope))
                {
                    _scopes[clan] = scope = new TitleScope();
                    foreach (var held in GetHeldTitles(clan))
                    {
                        scope.Titles.Add(held.TitleId);
                        if (!string.IsNullOrWhiteSpace(held.ParentTitleId)) scope.Parents.Add(held.ParentTitleId);
                    }
                }
                return scope.Parents.Contains(title.TitleId) || (!string.IsNullOrWhiteSpace(title.ParentTitleId)
                    && (scope.Titles.Contains(title.ParentTitleId) || scope.Parents.Contains(title.ParentTitleId)));
            }

            public bool CanConveyBirthright(Hero hero, Clan birthClan)
            {
                if (!_birthrightEligible.TryGetValue(hero, out bool eligible))
                    _birthrightEligible[hero] = eligible = FeudalTitleBehavior.IsCloseBloodClaimantOfClan(hero, birthClan);
                return eligible;
            }

            public FeudalClaimStrength? ExistingStrength(string clanId, string titleId) =>
                _existingStrengths.TryGetValue(clanId, out var titles) && titles.TryGetValue(titleId, out var strength)
                    ? strength : (FeudalClaimStrength?)null;

            public bool HasClaims => ClaimsByClan.Count > 0;

            public static MarriageTitleClaimCache Build(FeudalTitleBehavior titleBehavior, int currentDay)
            {
                MarriageTitleClaimCache cache = new MarriageTitleClaimCache { Day = currentDay };
                if (titleBehavior == null)
                    return cache;

                foreach (FeudalTitleRecord title in titleBehavior.GetAllTitles())
                {
                    if (title == null || !title.IsActive || string.IsNullOrWhiteSpace(title.TitleId))
                        continue;

                    cache.TitlesById[title.TitleId] = title;
                    cache.AddHeldTitle(title.DeJureHolderClanId, title);
                    if (title.DeFactoHolderClanId != title.DeJureHolderClanId)
                        cache.AddHeldTitle(title.DeFactoHolderClanId, title);
                }

                foreach (FeudalClaimRecord claim in titleBehavior.GetActiveClaims())
                {
                    if (claim == null || !claim.IsActive
                        || string.IsNullOrWhiteSpace(claim.ClaimantClanId)
                        || string.IsNullOrWhiteSpace(claim.TargetTitleId)
                        || !cache.TitlesById.ContainsKey(claim.TargetTitleId)
                        || (claim.ExpiresDay >= 0f && claim.ExpiresDay <= currentDay))
                    {
                        continue;
                    }

                    if (!cache.ClaimsByClan.TryGetValue(claim.ClaimantClanId, out List<FeudalClaimRecord> claims))
                    {
                        claims = new List<FeudalClaimRecord>();
                        cache.ClaimsByClan[claim.ClaimantClanId] = claims;
                    }

                    claims.Add(claim);
                    if (!cache._existingStrengths.TryGetValue(claim.ClaimantClanId, out var strengths))
                        cache._existingStrengths[claim.ClaimantClanId] = strengths = new Dictionary<string, FeudalClaimStrength>();
                    if (!strengths.TryGetValue(claim.TargetTitleId, out var current) || claim.Strength > current)
                        strengths[claim.TargetTitleId] = claim.Strength;
                }

                return cache;
            }

            public List<FeudalTitleRecord> GetHeldTitles(Clan clan)
            {
                if (clan == null || string.IsNullOrWhiteSpace(clan.StringId))
                    return EmptyTitles;

                return _heldTitlesByClan.TryGetValue(clan.StringId, out List<FeudalTitleRecord> titles)
                    ? titles
                    : EmptyTitles;
            }

            private void AddHeldTitle(string clanId, FeudalTitleRecord title)
            {
                if (string.IsNullOrWhiteSpace(clanId) || title == null)
                    return;

                if (!_heldTitlesByClan.TryGetValue(clanId, out List<FeudalTitleRecord> titles))
                {
                    titles = new List<FeudalTitleRecord>();
                    _heldTitlesByClan[clanId] = titles;
                }

                titles.Add(title);
            }
        }

        private static BellumMarriageRejectionReason GetCandidateClanRejectionReason(Hero suitor, Clan clan, FactionManagerBehavior factionManager, bool allowPlayerClan)
        {
            if (suitor?.Clan == null || clan == null)
                return BellumMarriageRejectionReason.InvalidOrInactiveHero;

            if (clan == suitor.Clan)
                return BellumMarriageRejectionReason.SameClan;

            BellumMarriageRejectionReason clanRejection = GetClanRejectionReason(clan, allowPlayerClan);
            if (clanRejection != BellumMarriageRejectionReason.None)
                return clanRejection;

            if (suitor.Clan.IsAtWarWith(clan))
                return BellumMarriageRejectionReason.KingdomAtWar;

            if (AreClansOnOppositeActiveCivilWarSides(suitor.Clan, clan, factionManager))
                return BellumMarriageRejectionReason.OppositeCivilWarSides;

            return BellumMarriageRejectionReason.None;
        }

        private static BellumMarriageRejectionReason GetCandidateRejectionReason(Hero suitor, Hero candidate)
        {
            if (suitor == null || candidate?.Clan == null)
                return BellumMarriageRejectionReason.InvalidOrInactiveHero;

            if (candidate == suitor)
                return BellumMarriageRejectionReason.SameHero;

            if (candidate.IsFemale == suitor.IsFemale)
                return BellumMarriageRejectionReason.SameSex;

            if (!candidate.IsLord || !candidate.IsAlive || !candidate.IsActive || candidate.IsChild)
                return BellumMarriageRejectionReason.InvalidOrInactiveHero;

            if (candidate.Spouse != null)
                return BellumMarriageRejectionReason.AlreadyMarried;

            BellumMarriageRejectionReason ageRejection = GetAgeRejectionReason(candidate);
            if (ageRejection != BellumMarriageRejectionReason.None)
                return ageRejection;

            if (!candidate.CanMarry())
                return BellumMarriageRejectionReason.CannotMarry;

            if (!Campaign.Current.Models.MarriageModel.IsCoupleSuitableForMarriage(suitor, candidate))
                return BellumMarriageRejectionReason.VanillaRejected;

            return BellumMarriageRejectionReason.None;
        }

        private static BellumMarriageRejectionReason GetOfferParticipantRejectionReason(Hero hero, bool allowPlayerClan, bool allowTemporary = false)
        {
            if (hero?.Clan == null)
                return BellumMarriageRejectionReason.InvalidOrInactiveHero;

            if (CrownAccessionBehavior.Instance?.IsHouseholdReservedForMarriage(hero) == true)
                return BellumMarriageRejectionReason.TemporarilyUnavailable;

            BellumMarriageRejectionReason clanRejection = GetClanRejectionReason(hero.Clan, allowPlayerClan);
            if (clanRejection != BellumMarriageRejectionReason.None)
                return clanRejection;

            if (!hero.IsLord || !hero.IsAlive || (!hero.IsActive && !(allowTemporary && (hero.IsFugitive || hero.IsPrisoner))) || hero.IsChild)
                return BellumMarriageRejectionReason.InvalidOrInactiveHero;

            if (hero.Spouse != null)
                return BellumMarriageRejectionReason.AlreadyMarried;

            BellumMarriageRejectionReason ageRejection = GetAgeRejectionReason(hero);
            if (ageRejection != BellumMarriageRejectionReason.None)
                return ageRejection;

            if (!allowTemporary && !MarriageParticipantReady(hero))
                return BellumMarriageRejectionReason.TemporarilyUnavailable;

            using (allowTemporary ? new MarriageProspectEvaluation() : null)
                if (!hero.CanMarry()) return BellumMarriageRejectionReason.CannotMarry;

            return BellumMarriageRejectionReason.None;
        }

        internal static bool MarriageParticipantReady(Hero hero) => hero?.IsAlive == true && hero.IsActive
            && !hero.IsPrisoner && !hero.IsDisabled && !hero.IsTraveling
            && hero.PartyBelongedTo?.MapEvent == null && hero.PartyBelongedTo?.SiegeEvent == null
            && hero.PartyBelongedTo?.Army == null;

        internal static bool MarriageProspectEligible(Hero hero) =>
            GetOfferParticipantRejectionReason(hero, true, allowTemporary: true) == BellumMarriageRejectionReason.None;

        private static BellumMarriageRejectionReason GetPairRejectionReason(Hero suitor, Hero candidate, FactionManagerBehavior factionManager, bool allowPlayerClan, bool allowTemporary = false)
        {
            if (suitor?.Clan == null || candidate?.Clan == null)
                return BellumMarriageRejectionReason.InvalidOrInactiveHero;

            if (suitor == candidate)
                return BellumMarriageRejectionReason.SameHero;

            if (suitor.Clan == candidate.Clan)
                return BellumMarriageRejectionReason.SameClan;

            if (candidate.Clan == Clan.PlayerClan && !allowPlayerClan)
                return BellumMarriageRejectionReason.PlayerClan;

            if (suitor.IsFemale == candidate.IsFemale)
                return BellumMarriageRejectionReason.SameSex;

            if (suitor.Clan.IsAtWarWith(candidate.Clan))
                return BellumMarriageRejectionReason.KingdomAtWar;

            if (AreClansOnOppositeActiveCivilWarSides(suitor.Clan, candidate.Clan, factionManager))
                return BellumMarriageRejectionReason.OppositeCivilWarSides;

            using (allowTemporary ? new MarriageProspectEvaluation() : null)
                if (!Campaign.Current.Models.MarriageModel.IsCoupleSuitableForMarriage(suitor, candidate))
                    return BellumMarriageRejectionReason.VanillaRejected;

            return BellumMarriageRejectionReason.None;
        }

        private static bool IsStrategicMarriageClanAllowed(Clan clan, bool allowPlayerClan)
        {
            return GetClanRejectionReason(clan, allowPlayerClan) == BellumMarriageRejectionReason.None;
        }

        private static BellumMarriageRejectionReason GetClanRejectionReason(Clan clan, bool allowPlayerClan)
        {
            if (clan == null)
                return BellumMarriageRejectionReason.NoKingdom;

            if (clan == Clan.PlayerClan && !allowPlayerClan)
                return BellumMarriageRejectionReason.PlayerClan;

            if (clan.IsEliminated)
                return BellumMarriageRejectionReason.EliminatedClan;

            bool allowedPlayerClan = clan == Clan.PlayerClan && allowPlayerClan;
            if (!allowedPlayerClan && (clan.IsClanTypeMercenary || clan.IsMinorFaction || clan.IsBanditFaction || clan.StringId == "neutral"))
                return BellumMarriageRejectionReason.MinorMercenaryOrNeutralClan;

            if (clan.Kingdom == null || clan.Kingdom.IsEliminated)
                return BellumMarriageRejectionReason.NoKingdom;

            return BellumMarriageRejectionReason.None;
        }

        private static bool IsWithinStrategicMarriageAge(Hero hero)
        {
            return GetAgeRejectionReason(hero) == BellumMarriageRejectionReason.None;
        }

        private static BellumMarriageRejectionReason GetAgeRejectionReason(Hero hero)
        {
            if (hero == null)
                return BellumMarriageRejectionReason.InvalidOrInactiveHero;

            if (hero.IsFemale)
            {
                if (hero.Age < BellumCivileOptions.MarriageFemaleMinimumAge)
                    return BellumMarriageRejectionReason.TooYoung;

                if (hero.Age > BellumCivileOptions.MarriageFemaleMaximumAge)
                    return BellumMarriageRejectionReason.FemaleAboveMaxAge;

                return BellumMarriageRejectionReason.None;
            }

            if (hero.Age < BellumCivileOptions.MarriageMaleMinimumAge)
                return BellumMarriageRejectionReason.TooYoung;

            return BellumMarriageRejectionReason.None;
        }

        private static bool AreClansOnOppositeActiveCivilWarSides(Clan firstClan, Clan secondClan, FactionManagerBehavior factionManager)
        {
            if (firstClan == null || secondClan == null || factionManager == null)
                return false;

            bool firstIsRebel = factionManager.IsClanOnActiveCivilWarRebelSide(firstClan, out FactionObject firstFaction, out Kingdom firstRebelKingdom);
            bool secondIsRebel = factionManager.IsClanOnActiveCivilWarRebelSide(secondClan, out FactionObject secondFaction, out Kingdom secondRebelKingdom);

            if (firstIsRebel && secondIsRebel)
                return firstFaction?.ParentKingdom != null && firstFaction.ParentKingdom == secondFaction?.ParentKingdom
                    && firstRebelKingdom != secondRebelKingdom;

            if (firstIsRebel)
                return secondClan.Kingdom == firstFaction?.ParentKingdom;

            if (secondIsRebel)
                return firstClan.Kingdom == secondFaction?.ParentKingdom;

            return false;
        }
    }
}
