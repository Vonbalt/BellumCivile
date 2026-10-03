using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;

namespace BellumCivile
{
    internal static class CrownReignClock
    {
        internal static CampaignTime Update(IDictionary<string, string> rulers, IDictionary<string, CampaignTime> starts,
            string realm, string ruler, CampaignTime now, CampaignTime campaignStart, bool newRealm = false)
        {
            bool known = rulers.TryGetValue(realm, out string previous);
            if (!starts.ContainsKey(realm) || (known && previous != ruler))
                starts[realm] = known || newRealm ? now : campaignStart;
            rulers[realm] = ruler;
            return starts[realm];
        }

        internal static int Months(double elapsedDays, int daysInYear) =>
            (int)Math.Floor(Math.Max(0, elapsedDays) * 12 / Math.Max(1, daysInYear) + 1e-9);
    }
}
