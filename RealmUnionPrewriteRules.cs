using System;
using System.Linq;

namespace BellumCivile
{
    internal static class RealmUnionPrewriteRules
    {
        internal static bool SameClan(RealmUnionClanRecord captured, RealmUnionClanRecord current)
        {
            if (captured?.Clan == null || current?.Clan != captured.Clan
                || captured.Holdings == null || current.Holdings == null
                || captured.Holdings.Any(string.IsNullOrWhiteSpace) || current.Holdings.Any(string.IsNullOrWhiteSpace)
                || captured.Holdings.Distinct(StringComparer.Ordinal).Count() != captured.Holdings.Count
                || current.Holdings.Distinct(StringComparer.Ordinal).Count() != current.Holdings.Count) return false;
            return captured.EndMercenaryContract == current.EndMercenaryContract
                && !float.IsNaN(captured.Influence) && !float.IsInfinity(captured.Influence)
                && captured.Influence == current.Influence && captured.Debt == current.Debt
                && captured.Color == current.Color && captured.Color2 == current.Color2
                && (captured.OriginalBanner?.Serialize() ?? "") == (current.OriginalBanner?.Serialize() ?? "")
                && captured.Holdings.OrderBy(id => id, StringComparer.Ordinal)
                    .SequenceEqual(current.Holdings.OrderBy(id => id, StringComparer.Ordinal));
        }
    }
}
