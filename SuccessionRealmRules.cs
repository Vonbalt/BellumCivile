namespace BellumCivile
{
    public enum RealmSuccessionSystem
    {
        Unknown,
        Hereditary,
        Elective
    }

    public static class SuccessionRealmRules
    {
        // Crown selection classification only; household inheritance keeps its existing rules.
        public static RealmSuccessionSystem Classify(HouseSuccessionLaw law)
        {
            switch (law)
            {
                case HouseSuccessionLaw.Primogeniture:
                case HouseSuccessionLaw.Ultimogeniture:
                case HouseSuccessionLaw.Kinship:
                case HouseSuccessionLaw.Seniority:
                    return RealmSuccessionSystem.Hereditary;
                case HouseSuccessionLaw.MilitaryAcclamation:
                case HouseSuccessionLaw.Tanistry:
                case HouseSuccessionLaw.ShuraCouncil:
                case HouseSuccessionLaw.ElectiveSeniority:
                    return RealmSuccessionSystem.Elective;
                default:
                    return RealmSuccessionSystem.Unknown;
            }
        }
    }
}
