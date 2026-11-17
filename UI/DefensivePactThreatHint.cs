using System;
using System.Globalization;
using TaleWorlds.Localization;

namespace BellumCivile.UI
{
    internal static class DefensivePactThreatHint
    {
        internal static TextObject Current(TextObject realm, double threat)
        {
            string summary = new TextObject("{=BC_DefensivePact_CurrentThreat}{REALM}: current threat is {THREAT}.")
                .SetTextVariable("REALM", realm ?? TextObject.GetEmpty())
                .SetTextVariable("THREAT", Math.Round(threat, 2, MidpointRounding.AwayFromZero).ToString("0.##", CultureInfo.CurrentCulture))
                .ToString();
            return Build(summary);
        }

        internal static TextObject Build(string forecast)
        {
            return new TextObject("{=BC_DefensivePact_ThreatIndicatorHint}{FORECAST}\n\nHigher threat makes defensive pacts against this realm more likely. Eligible realms may form or join at these thresholds:\n{NEIGHBOR}: neighboring realms\n{TWO}: up to two borders away\n{THREE}: up to three borders away\n{ANYWHERE}: any distance\n\nJoining also requires peace with other kingdoms, fewer than 25 towns and castles, strength below 80% of the target's, and negative relations between the rulers. Alliance, a hostage pact or clientage with the target, existing defensive pact membership, and cooldowns may prevent joining.")
                .SetTextVariable("FORECAST", forecast ?? string.Empty)
                .SetTextVariable("NEIGHBOR", DefensivePactEligibilityRules.JoiningThreshold(1))
                .SetTextVariable("TWO", DefensivePactEligibilityRules.JoiningThreshold(2))
                .SetTextVariable("THREE", DefensivePactEligibilityRules.JoiningThreshold(3))
                .SetTextVariable("ANYWHERE", DefensivePactEligibilityRules.JoiningThreshold(0));
        }
    }
}
