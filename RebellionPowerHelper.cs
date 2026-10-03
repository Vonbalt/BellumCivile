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
        public ProjectedRebellionSupport(
            Clan clan,
            Clan sponsor,
            Clan rootSponsor,
            float fullPower,
            float joinChance,
            float creditedPower,
            float solidarityScore,
            CivilWarSolidarityReason reason,
            float baselineCommitment = 0f,
            float solidarityChance = 0f)
        {
            Clan = clan;
            Sponsor = sponsor;
            RootSponsor = rootSponsor;
            FullPower = fullPower;
            JoinChance = joinChance;
            CreditedPower = creditedPower;
            SolidarityScore = solidarityScore;
            Reason = reason;
            BaselineCommitment = baselineCommitment;
            SolidarityChance = solidarityChance;
        }

        public Clan Clan { get; }
        public Clan Sponsor { get; }
        public Clan RootSponsor { get; }
        public float FullPower { get; }
        public float JoinChance { get; }
        public float CreditedPower { get; }
        public float SolidarityScore { get; }
        public CivilWarSolidarityReason Reason { get; }
        public float BaselineCommitment { get; }
        public float SolidarityChance { get; }
    }

    internal sealed class ProjectedLoyalistContribution
    {
        public ProjectedLoyalistContribution(
            Clan clan,
            float fullPower,
            float rebelJoinChance,
            float loyalistCommitment,
            float creditedPower)
        {
            Clan = clan;
            FullPower = fullPower;
            RebelJoinChance = rebelJoinChance;
            LoyalistCommitment = loyalistCommitment;
            CreditedPower = creditedPower;
        }

        public Clan Clan { get; }
        public float FullPower { get; }
        public float RebelJoinChance { get; }
        public float LoyalistCommitment { get; }
        public float CreditedPower { get; }
    }

    internal sealed class RebellionPowerProjection
    {
        public RebellionPowerProjection(
            Kingdom kingdom,
            List<Clan> committedRebels,
            List<ProjectedRebellionSupport> projectedSupporters,
            List<ProjectedLoyalistContribution> loyalistContributions,
            float factionPower,
            float loyalistPower)
        {
            Kingdom = kingdom;
            CommittedRebels = committedRebels ?? new List<Clan>();
            ProjectedSupporters = projectedSupporters ?? new List<ProjectedRebellionSupport>();
            LoyalistContributions = loyalistContributions ?? new List<ProjectedLoyalistContribution>();
            FactionPower = factionPower;
            LoyalistPower = loyalistPower;
        }

        public Kingdom Kingdom { get; }
        public IReadOnlyList<Clan> CommittedRebels { get; }
        public IReadOnlyList<ProjectedRebellionSupport> ProjectedSupporters { get; }
        public IReadOnlyList<ProjectedLoyalistContribution> LoyalistContributions { get; }
        public float FactionPower { get; }
        public float LoyalistPower { get; }

        public float GetProjectedSupportPower(Clan rootSponsor)
        {
            return ProjectedSupporters
                .Where(support => support.RootSponsor == rootSponsor)
                .Sum(support => support.CreditedPower);
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

            if (parentKingdom?.RulingClan != null)
            {
                // Court membership describes the ruler's political sympathies, not
                // the side they would fight for. A grand coalition expels the ruler
                // before raising its banners, so its preview must do the same.
                committed.RemoveAll(clan => clan == rulingClan);
            }
            HashSet<Clan> committedSet = new HashSet<Clan>(committed);
            float factionPower = committed.Sum(Power);

            if (parentKingdom == null)
            {
                return new RebellionPowerProjection(
                    null,
                    committed,
                    new List<ProjectedRebellionSupport>(),
                    new List<ProjectedLoyalistContribution>(),
                    factionPower,
                    0f);
            }

            FactionManagerBehavior factionManager = Campaign.Current?.GetCampaignBehavior<FactionManagerBehavior>();
            IdeologyBehavior ideologyBehavior = Campaign.Current?.GetCampaignBehavior<IdeologyBehavior>();
            FeudalTitleBehavior titleBehavior = Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>();

            Dictionary<Clan, float> rebelChances = committed.ToDictionary(clan => clan, clan => 1f);
            Dictionary<Clan, Clan> rootSponsors = committed.ToDictionary(clan => clan, clan => clan);
            Dictionary<Clan, CivilWarSolidarityAssessment> assessments = new Dictionary<Clan, CivilWarSolidarityAssessment>();

            List<Clan> candidates = includeProjectedSupport && factionManager != null && ideologyBehavior != null
                ? parentKingdom.Clans
                    .Where(clan => projectedLoyalists?.Contains(clan) != true)
                    .Where(clan => CivilWarSolidarityHelper.IsEligibleProjectionCandidate(
                        clan,
                        parentKingdom,
                        committedSet,
                        rebelFaction,
                        factionManager,
                        rulingClan))
                    .ToList()
                : new List<Clan>();
            Dictionary<Clan, float> rebellionIntentByCandidate = candidates
                .ToDictionary(clan => clan, clan => ideologyBehavior.CalculateRebellionScore(clan));

            for (int pass = 0; pass < C.CivilWarSolidarityHierarchyPassLimit && candidates.Count > 0; pass++)
            {
                List<KeyValuePair<Clan, float>> availableSponsors = rebelChances
                    .Where(pair => pair.Value > 0f)
                    .ToList();
                Dictionary<Clan, CivilWarSolidarityAssessment> passAssessments = new Dictionary<Clan, CivilWarSolidarityAssessment>();
                Dictionary<Clan, float> passChances = new Dictionary<Clan, float>();
                bool improved = false;

                foreach (Clan candidate in candidates)
                {
                    float currentChance = rebelChances.TryGetValue(candidate, out float existingChance) ? existingChance : 0f;
                    CivilWarSolidarityAssessment bestAssessment = null;
                    float bestChance = currentChance;

                    foreach (KeyValuePair<Clan, float> sponsorEntry in availableSponsors)
                    {
                        Clan sponsor = sponsorEntry.Key;
                        bool directVassal = CivilWarSolidarityHelper.IsImmediateVassalOf(titleBehavior, candidate, sponsor);
                        if (!directVassal
                            && !CivilWarSolidarityHelper.CanAnswerOutsiderCall(
                                candidate,
                                sponsor,
                                parentKingdom,
                                ideologyBehavior,
                                rebellionIntentByCandidate[candidate],
                                rulingClan))
                            continue;

                        CivilWarSolidarityAssessment assessment = CivilWarSolidarityHelper.AssessSupport(
                            candidate,
                            sponsor,
                            parentKingdom,
                            factionManager,
                            ideologyBehavior,
                            directVassal,
                            rebellionIntentByCandidate[candidate],
                            projectedSovereign);
                        if (assessment == null || assessment.JoinChance <= 0f)
                            continue;

                        float effectiveChance = sponsorEntry.Value * assessment.JoinChance;
                        if (effectiveChance > bestChance + 0.001f)
                        {
                            bestChance = effectiveChance;
                            bestAssessment = assessment;
                        }
                    }

                    if (bestAssessment == null)
                        continue;

                    passAssessments[candidate] = bestAssessment;
                    passChances[candidate] = bestChance;
                    improved = true;
                }

                foreach (KeyValuePair<Clan, float> update in passChances)
                {
                    Clan candidate = update.Key;
                    CivilWarSolidarityAssessment assessment = passAssessments[candidate];
                    rebelChances[candidate] = update.Value;
                    assessments[candidate] = assessment;
                    rootSponsors[candidate] = rootSponsors.TryGetValue(assessment.Sponsor, out Clan root)
                        ? root
                        : assessment.Sponsor;
                }

                if (!improved)
                    break;
            }

            List<ProjectedRebellionSupport> projectedSupporters = assessments
                .Where(pair => rebelChances.TryGetValue(pair.Key, out float chance) && chance > 0f)
                .Select(pair =>
                {
                    Clan clan = pair.Key;
                    CivilWarSolidarityAssessment assessment = pair.Value;
                    float fullPower = Power(clan);
                    float chance = rebelChances[clan];
                    return new ProjectedRebellionSupport(
                        clan,
                        assessment.Sponsor,
                        rootSponsors.TryGetValue(clan, out Clan root) ? root : assessment.Sponsor,
                        fullPower,
                        chance,
                        fullPower * chance,
                        assessment.Score,
                        assessment.PrimaryReason);
                })
                .OrderByDescending(support => support.CreditedPower)
                .ToList();

            factionPower += projectedSupporters.Sum(support => support.CreditedPower);

            List<ProjectedLoyalistContribution> loyalistContributions = new List<ProjectedLoyalistContribution>();
            float loyalistPower = 0f;
            foreach (Clan clan in parentKingdom.Clans.Where(clan => clan != null && !clan.IsEliminated && !committedSet.Contains(clan)))
            {
                float fullPower = Power(clan);
                float rebelChance = rebelChances.TryGetValue(clan, out float chance) ? chance : 0f;
                FactionObject ideology = factionManager?.GetIdeologicalFaction(clan);
                bool declaredLoyalist = clan == rulingClan || projectedLoyalists?.Contains(clan) == true;
                float commitment = declaredLoyalist ? 1f : CalculateLoyalistContributionMultiplier(parentKingdom, clan, ideology);
                if (projectedSovereign != null && clan == Clan.PlayerClan && !declaredLoyalist)
                    commitment = 0f;
                float creditedPower = fullPower * (1f - rebelChance) * commitment;
                loyalistPower += creditedPower;
                loyalistContributions.Add(new ProjectedLoyalistContribution(
                    clan,
                    fullPower,
                    rebelChance,
                    commitment,
                    creditedPower));
            }

            return new RebellionPowerProjection(
                parentKingdom,
                committed,
                projectedSupporters,
                loyalistContributions.OrderByDescending(entry => entry.CreditedPower).ToList(),
                factionPower,
                loyalistPower);
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
