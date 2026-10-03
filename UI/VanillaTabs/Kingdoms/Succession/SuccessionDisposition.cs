using System;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace BellumCivile.UI.VanillaTabs.Kingdoms.Succession
{
    internal static class SuccessionDisposition
    {
        internal static int Band(double score) => score < 25 ? 0 : score < 50 ? 1 : score < 75 ? 2 : 3;
        internal static string ColorHex(double? score) => !score.HasValue ? "#F1D8A4FF"
            : Band(score.Value) == 0 ? "#FF0000FF" : Band(score.Value) == 1 ? "#FF8C00FF"
            : Band(score.Value) == 2 ? "#F1D8A4FF" : "#82E06AFF";
        internal static Color Color(double? score) => TaleWorlds.Library.Color.ConvertStringToColor(ColorHex(score));
        internal static string Value(double score)
        {
            string[] states = { "{=BC_Loyalty_Disloyal}Disloyal", "{=BC_Loyalty_Wavering}Wavering",
                "{=BC_Loyalty_Loyal}Loyal", "{=BC_Loyalty_Steadfast}Steadfast" };
            // Keep the displayed percentage in its actual band near thresholds.
            double displayed = Math.Floor(Math.Max(0, Math.Min(100, score)) * 100) / 100;
            return new TextObject("{=BC_Loyalty_DispositionValue}{STATE} ({VALUE}%)")
                .SetTextVariable("STATE", new TextObject(states[Band(score)]))
                .SetTextVariable("VALUE", displayed.ToString("0.##")).ToString();
        }
    }
}
