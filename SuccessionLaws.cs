using TaleWorlds.SaveSystem;
using System.Collections.Generic;

namespace BellumCivile
{
    public enum GenderSuccessionLaw
    {
        MalePreference = 0,
        FemalePreference = 1,
        Equal = 2,
        MaleOnly = 3,
        FemaleOnly = 4
    }

    public enum HouseSuccessionLaw
    {
        Primogeniture = 0,
        Ultimogeniture = 1,
        ElectiveSeniority = 2,
        MilitaryAcclamation = 3,
        Tanistry = 4,
        Seniority = 5,
        ShuraCouncil = 6,
        Kinship = 7
    }

    public struct SuccessionLawSet
    {
        public SuccessionLawSet(GenderSuccessionLaw genderLaw, HouseSuccessionLaw successionLaw)
        {
            GenderLaw = genderLaw;
            SuccessionLaw = successionLaw;
        }

        public GenderSuccessionLaw GenderLaw { get; }
        public HouseSuccessionLaw SuccessionLaw { get; }
    }

    public sealed class RealmLawSelectionRecord
    {
        [SaveableField(1)]
        public string RealmId;

        [SaveableField(2)]
        public Dictionary<string, string> SelectedLaws = new Dictionary<string, string>();
    }
}
