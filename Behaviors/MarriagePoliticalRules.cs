using System;

namespace BellumCivile.Behaviors
{
    internal static class MarriagePoliticalRules
    {
        internal static float DomesticPartner(float ownStrength, float otherStrength, float influence, bool royalAccess)
        {
            ownStrength = Math.Max(0, ownStrength);
            otherStrength = Math.Max(0, otherStrength);
            float power = otherStrength / Math.Max(1, ownStrength + otherStrength);
            return Math.Min(85, 10 + 35 * power + Math.Min(15, Math.Max(0, influence) * .02f)
                + (royalAccess ? 25 : 0));
        }

        internal static float ForeignHousePartner(float ownStrength, float otherStrength, float influence,
            bool royalAccess)
        {
            ownStrength = Math.Max(0, ownStrength);
            otherStrength = Math.Max(0, otherStrength);
            float power = otherStrength / Math.Max(1, ownStrength + otherStrength);
            return Math.Min(80, 40 + 35 * power + Math.Min(10, Math.Max(0, influence) * .02f)
                + (royalAccess ? 10 : 0));
        }

        internal static float ForeignPartner(float ownStrength, float otherStrength, float strongestEnemy,
            bool commonEnemy, bool allied, bool canAddAlliance, bool noAllies, bool sameCulture, float relation)
        {
            float score = 55 + (sameCulture ? 10 : 0) + Math.Max(-20, Math.Min(20, relation * .2f));
            if (commonEnemy) score += 22.5f;
            if (!allied && canAddAlliance) score += 15;
            if (!allied && canAddAlliance && noAllies) score += 20;
            // A strong prospective friend is not itself evidence of a foreign threat.
            if ((allied || canAddAlliance) && strongestEnemy > Math.Max(1, ownStrength) * 1.15f)
                score += 35 * Math.Min(1, Math.Max(0, otherStrength) / Math.Max(1, strongestEnemy));
            return Math.Max(0, Math.Min(100, score));
        }
    }
}
