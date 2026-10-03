using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using Agreement = TaleWorlds.CampaignSystem.CampaignBehaviors.TradeAgreementsCampaignBehavior.TradeAgreement;

namespace BellumCivile
{
    internal static class RealmUnionTradeAdapter
    {
        internal static bool TryPrepare(IReadOnlyList<Agreement> original, Kingdom source, Kingdom destination,
            Func<Kingdom, bool> conflicts, out List<Agreement> replacement, out string reason)
        {
            replacement = null;
            reason = "trade inheritance requires distinct realms and a complete agreement snapshot";
            if (original == null || source == null || destination == null || source == destination || conflicts == null) return false;
            var result = original.ToList();
            var seen = new HashSet<Kingdom>();
            try
            {
                foreach (var inherited in original.Where(a => a.Kingdom1 == source || a.Kingdom2 == source))
                {
                    Kingdom partner = inherited.Kingdom1 == source ? inherited.Kingdom2 : inherited.Kingdom1;
                    if (partner == null || partner == source || partner == destination || !seen.Add(partner) || conflicts(partner))
                    { reason = "trade partner is duplicated, internal to the union or diplomatically incompatible"; return false; }
                    var matching = result.Where(a => Pair(a, destination, partner)).ToList();
                    if (matching.Count > 1) { reason = "destination has duplicate trade agreements"; return false; }
                    var merged = new Agreement(destination, partner, inherited.EndTime);
                    merged.Kingdom1GoldGained = inherited.Kingdom1 == source ? inherited.Kingdom1GoldGained : inherited.Kingdom2GoldGained;
                    merged.Kingdom2GoldGained = inherited.Kingdom1 == source ? inherited.Kingdom2GoldGained : inherited.Kingdom1GoldGained;
                    merged.Kingdom1GoldGainedTotal = inherited.Kingdom1 == source ? inherited.Kingdom1GoldGainedTotal : inherited.Kingdom2GoldGainedTotal;
                    merged.Kingdom2GoldGainedTotal = inherited.Kingdom1 == source ? inherited.Kingdom2GoldGainedTotal : inherited.Kingdom1GoldGainedTotal;
                    if (matching.Count == 1)
                    {
                        var existing = matching[0];
                        if (existing.EndTime > merged.EndTime)
                        {
                            var longer = new Agreement(destination, partner, existing.EndTime);
                            longer.Kingdom1GoldGained = merged.Kingdom1GoldGained;
                            longer.Kingdom2GoldGained = merged.Kingdom2GoldGained;
                            longer.Kingdom1GoldGainedTotal = merged.Kingdom1GoldGainedTotal;
                            longer.Kingdom2GoldGainedTotal = merged.Kingdom2GoldGainedTotal;
                            merged = longer;
                        }
                        checked
                        {
                            merged.Kingdom1GoldGained += existing.Kingdom1 == destination ? existing.Kingdom1GoldGained : existing.Kingdom2GoldGained;
                            merged.Kingdom2GoldGained += existing.Kingdom1 == destination ? existing.Kingdom2GoldGained : existing.Kingdom1GoldGained;
                            merged.Kingdom1GoldGainedTotal += existing.Kingdom1 == destination ? existing.Kingdom1GoldGainedTotal : existing.Kingdom2GoldGainedTotal;
                            merged.Kingdom2GoldGainedTotal += existing.Kingdom1 == destination ? existing.Kingdom2GoldGainedTotal : existing.Kingdom1GoldGainedTotal;
                        }
                    }
                    result.RemoveAll(a => Pair(a, source, partner) || Pair(a, destination, partner));
                    result.Add(merged);
                }
            }
            catch (OverflowException)
            { reason = "combined trade income exceeds the native counter range"; return false; }
            replacement = result;
            reason = null;
            return true;
        }

        private static bool Pair(Agreement agreement, Kingdom first, Kingdom second) =>
            agreement.Kingdom1 == first && agreement.Kingdom2 == second || agreement.Kingdom1 == second && agreement.Kingdom2 == first;

        internal static bool CanApply(ITradeAgreementsCampaignBehavior behavior, Kingdom source, Kingdom destination,
            Func<Kingdom, bool> conflicts, out string reason)
        {
            reason = "unsupported trade behavior or native agreement storage";
            if (behavior?.GetType() != typeof(TradeAgreementsCampaignBehavior)) return false;
            var field = AccessTools.Field(typeof(TradeAgreementsCampaignBehavior), "_tradeAgreements");
            return field?.FieldType == typeof(List<Agreement>) && field.GetValue(behavior) is List<Agreement> original
                && TryPrepare(original, source, destination, conflicts, out _, out reason);
        }

        // Native signing APIs reset income and emit new-deal reactions. Swap prepared
        // records instead; their native save definition stays unchanged.
        internal static bool TryApply(ITradeAgreementsCampaignBehavior behavior, Kingdom source, Kingdom destination,
            Func<Kingdom, bool> conflicts, Action beforeWrite, Action returned, out string reason)
        {
            reason = "unsupported trade behavior or native agreement storage";
            if (behavior?.GetType() != typeof(TradeAgreementsCampaignBehavior) || beforeWrite == null || returned == null) return false;
            var field = AccessTools.Field(typeof(TradeAgreementsCampaignBehavior), "_tradeAgreements");
            if (field?.FieldType != typeof(List<Agreement>) || !(field.GetValue(behavior) is List<Agreement> original)) return false;
            if (!TryPrepare(original, source, destination, conflicts, out var replacement, out reason)) return false;
            beforeWrite();
            field.SetValue(behavior, replacement);
            returned();
            return true;
        }
    }
}
