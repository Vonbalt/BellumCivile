using System;
using C = BellumCivile.BellumCivileConstants;

namespace BellumCivile
{
    internal struct CourtPoliticalPositionScore
    {
        public float LawfulTenure, RealmStanding, SubordinateHouses, LegalTerritory;
        public float MilitaryStrength, FrontierExposure, LandShortage, PoliticalExclusion;

        public float Traditionalists => LawfulTenure + RealmStanding;
        public float Aristocrats => SubordinateHouses + LegalTerritory;
        public float Militarists => MilitaryStrength + FrontierExposure;
        public float Populists => LandShortage + PoliticalExclusion;

        internal static float Share(float part, float whole)
        {
            if (whole <= 0f || float.IsNaN(part) || float.IsNaN(whole))
                return 0f;
            return C.IdeologyPoliticalComponentCap * Math.Max(0f, Math.Min(1f, part / whole));
        }

        internal static CourtPoliticalPositionScore Calculate(
            int fiefs, int lawfulFiefs, float standingYears, int subordinateHouses,
            int legalBaronies, int missingLegalBaronies, float strength, float realmMeanStrength,
            int frontierFiefs, int tier, bool isRuler, bool councilKnown, bool holdsOffice)
        {
            int desiredFiefs = Math.Max(1, tier);
            return new CourtPoliticalPositionScore
            {
                LawfulTenure = Share(lawfulFiefs, fiefs),
                RealmStanding = Share(standingYears, C.IdeologyEstablishedRealmYears),
                SubordinateHouses = subordinateHouses <= 0 ? 0f
                    : Math.Min(C.IdeologyPoliticalComponentCap, C.IdeologyFirstSubordinatePull
                        + (subordinateHouses - 1) * C.IdeologyAdditionalSubordinatePull),
                LegalTerritory = Share(missingLegalBaronies, legalBaronies),
                MilitaryStrength = Share(strength,
                    C.IdeologyMilitaryMeanMultiplier * Math.Max(C.IdeologyMilitaryReferenceFloor, realmMeanStrength)),
                FrontierExposure = Share(frontierFiefs, fiefs),
                LandShortage = Share(desiredFiefs - fiefs, desiredFiefs),
                PoliticalExclusion = isRuler || !councilKnown || holdsOffice ? 0f
                    : C.IdeologyPoliticalComponentCap / (1f + Math.Max(0, fiefs) + Math.Max(0, subordinateHouses))
            };
        }

        internal void Add(CourtPoliticalPositionScore other)
        {
            LawfulTenure += other.LawfulTenure;
            RealmStanding += other.RealmStanding;
            SubordinateHouses += other.SubordinateHouses;
            LegalTerritory += other.LegalTerritory;
            MilitaryStrength += other.MilitaryStrength;
            FrontierExposure += other.FrontierExposure;
            LandShortage += other.LandShortage;
            PoliticalExclusion += other.PoliticalExclusion;
        }
    }
}
