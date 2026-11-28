using System;
using System.Collections.Generic;
using System.Globalization;
using TaleWorlds.Core.ViewModelCollection.Information;
using TaleWorlds.Localization;

namespace BellumCivile.UI
{
    internal static class DefensivePactThreatHint
    {
        private static double Round(double threat) => Math.Round(threat, 2, MidpointRounding.AwayFromZero);

        internal static string FormatValue(double threat) => Round(threat).ToString("0.##", CultureInfo.CurrentCulture);

        internal static string FormatChange(double before, double after)
        {
            double change = Round(Round(after) - Round(before));
            return (change < 0 ? "-" : "+") + FormatValue(Math.Abs(change));
        }

        internal static List<TooltipProperty> Build(TextObject realm, double current, double? afterTreaty = null)
        {
            var rows = new List<TooltipProperty>();
            void Add(string label, string value) => rows.Add(new TooltipProperty(new TextObject(label).ToString(), value, 0));
            void Band(int threshold, string description) => rows.Add(new TooltipProperty(threshold.ToString(), new TextObject(description).ToString(), 0));

            rows.Add(Title(realm ?? new TextObject("{=BC_DefensivePact_ThreatTitle}Threat")));
            rows.Add(Separator());
            Add("{=BC_DefensivePact_TooltipCurrent}Current threat", FormatValue(current));
            if (afterTreaty.HasValue)
            {
                Add("{=BC_DefensivePact_TooltipChange}Treaty change", FormatChange(current, afterTreaty.Value));
                Add("{=BC_DefensivePact_TooltipProjected}After treaty", FormatValue(afterTreaty.Value));
            }

            rows.Add(Separator());
            rows.Add(Title(new TextObject("{=BC_DefensivePact_TooltipBands}Pact Thresholds")));
            Band(DefensivePactEligibilityRules.EarlyNeighborThreshold, "{=BC_DefensivePact_TooltipEarlyBand}Small or same-culture direct neighbors");
            Band(DefensivePactEligibilityRules.NeighborThreshold, "{=BC_DefensivePact_TooltipNeighborBand}Other direct neighbors");
            Band(DefensivePactEligibilityRules.SecondFrontierThreshold, "{=BC_DefensivePact_TooltipSecondBand}Up to two borders away");
            Band(DefensivePactEligibilityRules.ThirdFrontierThreshold, "{=BC_DefensivePact_TooltipThirdBand}Up to three borders away");
            Band(DefensivePactEligibilityRules.AnyDistanceThreshold, "{=BC_DefensivePact_TooltipAnyBand}Any distance");
            rows.Add(Note(new TextObject("{=BC_DefensivePact_TooltipSmallRealm}Small realms hold 1-{MAX} towns or castles in total.")
                .SetTextVariable("MAX", DefensivePactEligibilityRules.EarlyNeighborMaxFiefs)));

            rows.Add(Separator());
            rows.Add(Title(new TextObject("{=BC_DefensivePact_TooltipRequirements}Joining Requirements")));
            Add("{=BC_DefensivePact_TooltipPeace}At peace", new TextObject("{=BC_DefensivePact_TooltipOtherRealms}With other kingdoms").ToString());
            Add("{=BC_DefensivePact_TooltipSize}Realm size", new TextObject("{=BC_DefensivePact_TooltipSizeLimit}Fewer than 25 towns or castles").ToString());
            Add("{=BC_DefensivePact_TooltipStrength}Relative strength", new TextObject("{=BC_DefensivePact_TooltipStrengthLimit}Below 80% of the target's").ToString());
            Add("{=BC_DefensivePact_TooltipRelations}Ruler relations", new TextObject("{=BC_DefensivePact_TooltipNegative}Negative").ToString());
            rows.Add(Note(new TextObject("{=BC_DefensivePact_TooltipProtectedTies}An alliance, hostage pact, or clientage with the target blocks joining.")));
            rows.Add(Note(new TextObject("{=BC_DefensivePact_TooltipOtherBlocks}Existing pact membership and cooldowns also block joining.")));
            return rows;
        }

        internal static List<TooltipProperty> Unavailable() => new List<TooltipProperty>
        {
            Title(new TextObject("{=BC_DefensivePact_ThreatTitle}Threat")), Separator(),
            Note(new TextObject("{=BC_DefensivePact_ForecastUnavailable}Treaty threat forecast unavailable."))
        };

        private static TooltipProperty Title(TextObject text) =>
            new TooltipProperty(text.ToString(), string.Empty, 0, false, TooltipProperty.TooltipPropertyFlags.Title);

        private static TooltipProperty Separator() =>
            new TooltipProperty(string.Empty, string.Empty, 0, false, TooltipProperty.TooltipPropertyFlags.DefaultSeperator);

        private static TooltipProperty Note(TextObject text) =>
            new TooltipProperty(string.Empty, text.ToString(), 0, false, TooltipProperty.TooltipPropertyFlags.MultiLine);
    }
}
