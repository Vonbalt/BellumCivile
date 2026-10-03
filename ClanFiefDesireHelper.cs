using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.Library;
using C = BellumCivile.BellumCivileConstants;

namespace BellumCivile
{
    internal static class ClanFiefDesireHelper
    {
        public const float RebelliousPressurePerMissingFief = 10f;
        public const float LandlessDesperationBonus = 10f;

        public static int CalculateDesiredFiefs(Clan clan)
        {
            if (clan == null)
                return 1;

            int desired = clan.Tier;
            Hero leader = clan.Leader;
            if (leader != null)
            {
                int greed = leader.GetTraitLevel(DefaultTraits.Generosity);
                int honor = leader.GetTraitLevel(DefaultTraits.Honor);

                if (greed < 0) desired += 1;
                else if (greed > 0) desired -= 1;

                if (honor < 0) desired += 1;
                else if (honor > 0) desired -= 1;
            }

            return desired < 1 ? 1 : desired;
        }

        public static int CalculateMissingFiefs(Clan clan)
        {
            if (clan == null)
                return 0;

            return CalculateDesiredFiefs(clan) - clan.Fiefs.Count;
        }

        public static float CalculateRebelliousFiefDesirePressure(Clan clan)
        {
            return CalculateFiefDesirePressure(clan, includeLandlessDesperation: true);
        }

        public static float CalculateRulerHoardingPressure(Clan assessingClan)
        {
            Kingdom kingdom = assessingClan?.Kingdom;
            Clan rulingClan = kingdom?.RulingClan;
            if (rulingClan == null || assessingClan == rulingClan)
                return 0f;

            int crownSurplus = MathF.Max(
                0,
                rulingClan.Fiefs.Count - C.RebelliousRulerReservedFiefs);
            if (crownSurplus == 0)
                return 0f;

            int realmUnmetDemand = 0;
            foreach (Clan vassal in kingdom.Clans)
            {
                if (!IsEligibleVassalForLandDemand(vassal, rulingClan))
                    continue;

                realmUnmetDemand += MathF.Max(0, CalculateMissingFiefs(vassal));
            }

            int actionableHoarding = MathF.Min(crownSurplus, realmUnmetDemand);
            if (actionableHoarding == 0)
                return 0f;

            float pressure = MathF.Min(
                C.RebelliousRulerHoardingIntentCap,
                actionableHoarding * C.RebelliousRulerHoardingIntentPerFief);
            if (CalculateMissingFiefs(assessingClan) <= 0)
                pressure *= C.RebelliousRulerHoardingSatisfiedMultiplier;

            return pressure;
        }

        private static float CalculateFiefDesirePressure(Clan clan, bool includeLandlessDesperation)
        {
            if (clan == null)
                return 0f;

            int missingFiefs = CalculateMissingFiefs(clan);
            if (missingFiefs <= 0)
                return 0f;

            float pressure = missingFiefs * RebelliousPressurePerMissingFief;
            if (includeLandlessDesperation && clan.Fiefs.Count == 0)
                pressure += LandlessDesperationBonus;

            return pressure;
        }

        private static bool IsEligibleVassalForLandDemand(Clan clan, Clan rulingClan)
        {
            return clan != null
                && clan != rulingClan
                && !clan.IsEliminated
                && !clan.IsUnderMercenaryService
                && (!clan.IsMinorFaction || clan == Clan.PlayerClan)
                && clan.Leader != null
                && clan.Leader.IsAlive;
        }
    }
}
