using System;
using System.Collections.Generic;
using System.Linq;
using BellumCivile.Behaviors;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using C = BellumCivile.BellumCivileConstants;

namespace BellumCivile
{
    internal sealed class ProjectedRebellionSupport
    {
        public ProjectedRebellionSupport(Clan clan, Clan sponsor, float fullPower, float joinChance,
            float contributionCap, CivilWarSolidarityReason reason)
        {
            Clan = clan;
            Sponsor = sponsor;
            FullPower = fullPower;
            JoinChance = joinChance;
            ContributionCap = contributionCap;
            Reason = reason;
        }

        public Clan Clan { get; }
        public Clan Sponsor { get; }
        public float FullPower { get; }
        public float JoinChance { get; }
        public float ContributionCap { get; }
        public float CreditedPower => FullPower * ContributionCap * JoinChance;
        public CivilWarSolidarityReason Reason { get; }
    }

    internal sealed class ProjectedLoyalistContribution
    {
        public ProjectedLoyalistContribution(Clan clan, float fullPower, float rebelCreditedPower, float loyalistCommitment)
        {
            Clan = clan;
            FullPower = fullPower;
            RebelCreditedPower = rebelCreditedPower;
            LoyalistCommitment = loyalistCommitment;
        }

        public Clan Clan { get; }
        public float FullPower { get; }
        public float RebelCreditedPower { get; }
        public float LoyalistCommitment { get; }
        public float CreditedPower => (FullPower - RebelCreditedPower) * LoyalistCommitment;
    }

    internal sealed class RebellionPowerProjection
    {
        private readonly List<ProjectedRebellionSupport> _projectedSupporters;
        private readonly List<ProjectedLoyalistContribution> _loyalistContributions;
        private IReadOnlyList<ProjectedRebellionSupport> _sortedSupporters;
        private IReadOnlyList<ProjectedLoyalistContribution> _sortedLoyalists;

        public RebellionPowerProjection(
            Kingdom kingdom,
            List<Clan> committedRebels,
            List<ProjectedRebellionSupport> projectedSupporters,
            List<ProjectedLoyalistContribution> loyalistContributions,
            float pledgedPower)
        {
            Kingdom = kingdom;
            CommittedRebels = committedRebels ?? new List<Clan>();
            _projectedSupporters = projectedSupporters ?? new List<ProjectedRebellionSupport>();
            _loyalistContributions = loyalistContributions ?? new List<ProjectedLoyalistContribution>();
            PledgedPower = pledgedPower;
            ExpectedSupportPower = _projectedSupporters.Sum(support => support.CreditedPower);
            LoyalistPower = _loyalistContributions.Sum(entry => entry.CreditedPower);
        }

        public Kingdom Kingdom { get; }
        public IReadOnlyList<Clan> CommittedRebels { get; }
        public IReadOnlyList<ProjectedRebellionSupport> ProjectedSupporters =>
            _sortedSupporters ?? (_sortedSupporters = _projectedSupporters.OrderByDescending(support => support.CreditedPower).ToList());
        public IReadOnlyList<ProjectedLoyalistContribution> LoyalistContributions =>
            _sortedLoyalists ?? (_sortedLoyalists = _loyalistContributions.OrderByDescending(entry => entry.CreditedPower).ToList());
        public float PledgedPower { get; }
        public float ExpectedSupportPower { get; }
        public float FactionPower => PledgedPower + ExpectedSupportPower;
        public float LoyalistPower { get; }

        internal (float Faction, float Loyalist) GetConcessionPower()
        {
            float cappedSupport = Math.Min(ExpectedSupportPower, Math.Max(0f, PledgedPower));
            if (cappedSupport >= ExpectedSupportPower)
                return (FactionPower, LoyalistPower);

            // Discount the same share on both sides of the concession calculation.
            float restoreShare = 1f - cappedSupport / ExpectedSupportPower;
            float restoredLoyalists = _loyalistContributions.Sum(entry =>
                entry.RebelCreditedPower * entry.LoyalistCommitment * restoreShare);
            return (PledgedPower + cappedSupport, LoyalistPower + restoredLoyalists);
        }
    }

    internal sealed class ClanSupportConnection
    {
        internal Clan Clan { get; }
        internal float? ServiceShare { get; }
        internal int? Friendship { get; }
        internal bool CloseKin { get; }
        internal (Hero First, Hero Second) Spouses { get; }
        internal MarriageTruceStatus MarriageTruce { get; }
        internal ProjectedRebellionSupport ExpectedSupport { get; }

        internal ClanSupportConnection(Clan clan, float? serviceShare, int? friendship, bool closeKin,
            (Hero First, Hero Second) spouses, MarriageTruceStatus marriageTruce, ProjectedRebellionSupport expectedSupport)
        {
            Clan = clan;
            ServiceShare = serviceShare;
            Friendship = friendship;
            CloseKin = closeKin;
            Spouses = spouses;
            MarriageTruce = marriageTruce;
            ExpectedSupport = expectedSupport;
        }
    }

    internal static class RebellionPowerHelper
    {
        public static float CalculateRebellionPowerThreshold(Hero leader)
        {
            float threshold = C.RebellionPowerThresholdBase;
            if (leader == null)
                return threshold;

            int calculating = leader.GetTraitLevel(DefaultTraits.Calculating);
            if (calculating <= -2) threshold -= C.TraitThresholdAdjLarge;
            else if (calculating == -1) threshold -= C.TraitThresholdAdjSmall;
            else if (calculating == 1) threshold += C.TraitThresholdAdjSmall;
            else if (calculating >= 2) threshold += C.TraitThresholdAdjLarge;

            int valor = leader.GetTraitLevel(DefaultTraits.Valor);
            if (valor >= 2) threshold -= C.TraitThresholdAdjLarge;
            else if (valor == 1) threshold -= C.TraitThresholdAdjSmall;
            else if (valor == -1) threshold += C.TraitThresholdAdjSmall;
            else if (valor <= -2) threshold += C.TraitThresholdAdjLarge;

            return TaleWorlds.Library.MathF.Max(C.TraitThresholdFloor, threshold);
        }

        public static float CalculateGrandCoalitionPowerThreshold(Hero leader, bool desperate)
        {
            float threshold = CalculateRebellionPowerThreshold(leader);
            if (desperate)
                threshold -= C.GrandCoalitionDesperateThresholdReduction;

            return TaleWorlds.Library.MathF.Max(
                desperate ? C.GrandCoalitionDesperateThresholdFloor : C.TraitThresholdFloor,
                threshold);
        }

        public static float CalculateFactionPower(IEnumerable<Clan> clans)
        {
            if (clans == null)
                return 0f;

            return clans
                .Where(c => c != null && !c.IsEliminated)
                .Sum(CalculateClanPower);
        }

        public static RebellionPowerProjection CalculateProjectedConflictPower(
            Kingdom parentKingdom,
            IEnumerable<Clan> committedRebels,
            Clan rebelLeader,
            FactionObject rebelFaction,
            bool includeProjectedSupport,
            Hero projectedSovereign = null,
            ISet<Clan> projectedLoyalists = null)
        {
            Clan rulingClan = projectedSovereign?.Clan ?? parentKingdom?.RulingClan;
            float Power(Clan clan) => projectedSovereign == null ? CalculateClanPower(clan)
                : System.Math.Max(0f, CalculateClanPower(clan));
            List<Clan> committed = (committedRebels ?? Enumerable.Empty<Clan>())
                .Where(clan => clan != null && !clan.IsEliminated)
                .Distinct()
                .ToList();

            if (rulingClan != null)
            {
                // Court membership describes the ruler's political sympathies, not
                // the side they would fight for. A grand coalition expels the ruler
                // before raising its banners, so its preview must do the same.
                committed.RemoveAll(clan => clan == rulingClan);
            }
            HashSet<Clan> committedSet = new HashSet<Clan>(committed);
            float pledgedPower = committed.Sum(Power);

            if (parentKingdom == null)
            {
                return new RebellionPowerProjection(
                    null,
                    committed,
                    new List<ProjectedRebellionSupport>(),
                    new List<ProjectedLoyalistContribution>(),
                    pledgedPower);
            }

            FactionManagerBehavior factionManager = Campaign.Current?.GetCampaignBehavior<FactionManagerBehavior>();
            IdeologyBehavior ideologyBehavior = Campaign.Current?.GetCampaignBehavior<IdeologyBehavior>();
            FeudalTitleBehavior titleBehavior = Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>();

            Clan[] sponsors = includeProjectedSupport && factionManager != null && ideologyBehavior != null
                ? committed.Where(clan => clan.Kingdom == parentKingdom && clan.Leader?.IsAlive == true)
                    .OrderByDescending(clan => clan == rebelLeader)
                    .ThenBy(clan => clan.StringId, StringComparer.Ordinal)
                    .ToArray()
                : Array.Empty<Clan>();
            FeudalServiceBehavior service = Campaign.Current?.GetCampaignBehavior<FeudalServiceBehavior>();
            var projectedSupporters = new List<ProjectedRebellionSupport>();
            var loyalistContributions = new List<ProjectedLoyalistContribution>();
            foreach (Clan clan in parentKingdom.Clans.Where(clan => clan != null && !clan.IsEliminated && !committedSet.Contains(clan)))
            {
                float fullPower = Power(clan);
                FactionObject ideology = factionManager?.GetIdeologicalFaction(clan);
                bool declaredLoyalist = clan == rulingClan || projectedLoyalists?.Contains(clan) == true;
                ProjectedRebellionSupport support = null;
                if (!declaredLoyalist && fullPower > 0f && sponsors.Length > 0
                    && CivilWarSolidarityHelper.IsEligibleProjectionCandidate(clan, parentKingdom, committedSet, rebelFaction, factionManager, rulingClan))
                {
                    support = ProjectDirectSupport(clan, fullPower, sponsors, parentKingdom, rulingClan,
                        factionManager, ideologyBehavior, GetDirectServiceShares(clan, titleBehavior, service), projectedSovereign);
                    if (support != null)
                        projectedSupporters.Add(support);
                }

                float commitment = declaredLoyalist ? 1f : CalculateLoyalistContributionMultiplier(parentKingdom, clan, ideology);
                if (projectedSovereign != null && clan == Clan.PlayerClan && !declaredLoyalist)
                    commitment = 0f;
                loyalistContributions.Add(new ProjectedLoyalistContribution(
                    clan, fullPower, support?.CreditedPower ?? 0f, commitment));
            }

            return new RebellionPowerProjection(
                parentKingdom,
                committed,
                projectedSupporters,
                loyalistContributions,
                pledgedPower);
        }

        internal static List<ClanSupportConnection> GetClanSupportNetwork(Clan sponsor, out bool canForecast)
        {
            canForecast = false;
            var entries = new List<ClanSupportConnection>();
            Kingdom realm = sponsor?.Kingdom;
            if (Campaign.Current == null || realm == null || realm.IsEliminated
                || !CourtAgendaBehavior.Eligible(sponsor, realm) || sponsor.Leader?.IsAlive != true)
                return entries;

            var manager = Campaign.Current.GetCampaignBehavior<FactionManagerBehavior>();
            var ideology = Campaign.Current.GetCampaignBehavior<IdeologyBehavior>();
            var titles = Campaign.Current.GetCampaignBehavior<FeudalTitleBehavior>();
            var service = Campaign.Current.GetCampaignBehavior<FeudalServiceBehavior>();
            Clan ruler = realm.RulingClan;
            canForecast = ruler != null && ruler != sponsor && ruler.Leader?.IsAlive == true && manager != null && ideology != null
                && !BellumKingdomVisibilityHelper.IsTemporaryBellumKingdom(realm);
            var sponsors = new[] { sponsor };
            var committed = canForecast ? new HashSet<Clan>(sponsors) : null;

            foreach (Clan clan in realm.Clans.Distinct())
            {
                if (clan == sponsor || !CourtAgendaBehavior.Eligible(clan, realm) || clan.Leader?.IsAlive != true) continue;
                var shares = GetDirectServiceShares(clan, titles, service);
                float? share = !string.IsNullOrEmpty(sponsor.StringId) && shares != null && shares.TryGetValue(sponsor.StringId, out float rate)
                    ? (float?)rate : null;
                int relation = clan.Leader.GetRelation(sponsor.Leader);
                int? friendship = relation >= C.TreasonSolidarityFriendRelationThreshold ? (int?)relation : null;
                var spouses = MarriageAllianceHelper.GetMarriageAllianceSpouses(clan, sponsor);
                bool kin = CivilWarSolidarityHelper.AreCloseDynasticKin(clan, sponsor);
                if (!share.HasValue && !friendship.HasValue && spouses.First == null && !kin) continue;

                ProjectedRebellionSupport expected = null;
                if (canForecast && CivilWarSolidarityHelper.IsEligibleProjectionCandidate(clan, realm, committed, null, manager, ruler))
                {
                    float power = CalculateClanPower(clan);
                    if (power > 0f)
                        expected = ProjectDirectSupport(clan, power, sponsors, realm, ruler, manager, ideology, shares, null);
                }
                var truce = spouses.First == null ? default : MarriageAllianceHelper.GetMarriageTruce(clan, sponsor);
                entries.Add(new ClanSupportConnection(clan, share, friendship, kin, spouses, truce, expected));
            }
            return entries;
        }

        private static Dictionary<string, float> GetDirectServiceShares(Clan candidate, FeudalTitleBehavior titles, FeudalServiceBehavior service)
        {
            // Average multiple contracts with the same liege rather than stacking them.
            return titles?.GetTitlesHeldByClan(candidate, deJure: false)
                .Select(title => new { Title = title, Parent = titles.GetParentTitle(title, FeudalHierarchyMode.DeFacto) })
                .Where(pair => pair.Parent?.IsActive == true && !string.IsNullOrEmpty(pair.Parent.DeFactoHolderClanId))
                .GroupBy(pair => pair.Parent.DeFactoHolderClanId)
                .ToDictionary(group => group.Key, group => group.Average(pair =>
                    service?.GetTaxShare(pair.Title, pair.Parent) ?? C.FeudalServiceCustomaryTenureTaxShare));
        }

        private static ProjectedRebellionSupport ProjectDirectSupport(
            Clan candidate, float fullPower, IEnumerable<Clan> sponsors, Kingdom kingdom, Clan rulingClan,
            FactionManagerBehavior manager, IdeologyBehavior ideology, Dictionary<string, float> serviceByLiege, Hero projectedSovereign)
        {
            bool tiedToCrown = MarriageAllianceHelper.HasMarriageAlliance(candidate, rulingClan);
            float? intent = null;
            ProjectedRebellionSupport best = null;
            foreach (Clan sponsor in sponsors)
            {
                float cap = 0f;
                bool directVassal = !string.IsNullOrEmpty(sponsor.StringId) && serviceByLiege != null
                    && serviceByLiege.TryGetValue(sponsor.StringId, out cap);
                if (!directVassal)
                {
                    if (tiedToCrown || !CivilWarSolidarityHelper.HasPersonalSolidarityTie(candidate, sponsor))
                        continue;
                    cap = C.RebellionProjectedPersonalSupportCap;
                }

                if (cap <= 0f)
                    continue;

                intent = intent ?? ideology.CalculateRebellionScore(candidate);
                CivilWarSolidarityAssessment assessment = CivilWarSolidarityHelper.AssessSupport(
                    candidate, sponsor, kingdom, manager, ideology, directVassal, intent, projectedSovereign);
                if (assessment.JoinChance <= 0f)
                    continue;

                var support = new ProjectedRebellionSupport(candidate, sponsor, fullPower,
                    assessment.JoinChance, cap, assessment.PrimaryReason);
                if (best == null || support.CreditedPower > best.CreditedPower)
                    best = support;
            }
            return best;
        }

        public static float CalculateLoyalistPower(Kingdom parentKingdom, IEnumerable<Clan> rebelMembers)
        {
            if (parentKingdom == null)
                return 0f;

            HashSet<Clan> rebels = rebelMembers != null
                ? new HashSet<Clan>(rebelMembers.Where(c => c != null))
                : new HashSet<Clan>();

            float totalPower = 0f;
            FactionManagerBehavior factionManager = Campaign.Current?.GetCampaignBehavior<FactionManagerBehavior>();

            foreach (Clan clan in parentKingdom.Clans.Where(c => c != null && !c.IsEliminated && !rebels.Contains(c)))
            {
                float baseStrength = CalculateClanPower(clan);

                if (clan == parentKingdom.RulingClan || clan.IsUnderMercenaryService || clan.IsMinorFaction)
                {
                    totalPower += baseStrength;
                    continue;
                }

                FactionObject ideology = factionManager?.GetIdeologicalFaction(clan);
                if (ideology == null)
                {
                    totalPower += baseStrength;
                    continue;
                }

                totalPower += baseStrength * CalculateLoyalistContributionMultiplier(parentKingdom, clan, ideology);
            }

            return totalPower;
        }

        public static float CalculateLoyalistContributionMultiplier(Kingdom parentKingdom, Clan clan, FactionObject ideology)
        {
            if (parentKingdom == null || clan == null)
                return 0f;

            if (clan == parentKingdom.RulingClan || clan.IsUnderMercenaryService || clan.IsMinorFaction || ideology == null)
                return 1f;

            float mood = ideology.Mood;

            if (mood >= C.ArmyMoodContent + 1f)
                return 1f;

            if (mood >= C.MoodThresholdHappy + 1f)
                return 0.75f;

            if (mood >= C.MoodThresholdUnhappy)
                return 0.50f;

            if (mood >= C.GrandCoalitionJoinMoodThreshold)
                return 0.25f;

            return 0f;
        }

        public static float CalculateClanPower(Clan clan)
        {
            if (clan == null)
                return 0f;

            return GetClanMilitaryStrength(clan) + clan.Influence;
        }

        public static float GetClanMilitaryStrength(Clan clan)
        {
            if (clan == null)
                return 0f;

            float strength = clan.CurrentTotalStrength;
            if (IsValidStrength(strength) && strength > 0f)
                return strength;

            return RebuildClanMilitaryStrength(clan);
        }

        internal static float CalculateLiveClanPower(Clan clan) => clan == null ? 0f
            : System.Math.Max(0f, RebuildClanMilitaryStrength(clan) + clan.Influence);

        private static float RebuildClanMilitaryStrength(Clan clan)
        {
            if (clan == null) return 0f;

            // DefaultDiplomacyModel.GetClanStrength is not a military-strength
            // fallback: it includes hero wealth, skills, influence and heavily
            // weighted party strength. Rebuild the same physical force total used
            // by Clan.UpdateCurrentStrength so temporary zero caches cannot create
            // enormous wealth-driven faction power spikes.
            float reconstructedStrength = 0f;

            foreach (var warParty in clan.WarPartyComponents)
            {
                float partyStrength = warParty?.MobileParty?.Party?.EstimatedStrength ?? 0f;
                if (IsValidStrength(partyStrength) && partyStrength > 0f)
                    reconstructedStrength += partyStrength;
            }

            foreach (var fief in clan.Fiefs)
            {
                float garrisonStrength = fief?.GarrisonParty?.Party?.EstimatedStrength ?? 0f;
                if (IsValidStrength(garrisonStrength) && garrisonStrength > 0f)
                    reconstructedStrength += garrisonStrength;
            }

            return IsValidStrength(reconstructedStrength) && reconstructedStrength > 0f
                ? reconstructedStrength
                : 0f;
        }

        private static bool IsValidStrength(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
