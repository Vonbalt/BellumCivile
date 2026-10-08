using System.Collections.Generic;
using System.Linq;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.BarterSystem;
using TaleWorlds.CampaignSystem.BarterSystem.Barterables;
using TaleWorlds.Localization;

namespace BellumCivile.Patches
{
    internal static class PlayerMarriageBarterGuard
    {
        internal static bool Validate(IEnumerable<Barterable> items)
        {
            int marriages = 0;
            foreach (var item in items?.OfType<MarriageBarterable>() ?? Enumerable.Empty<MarriageBarterable>())
            {
                if (!PlayerMarriagePricing.TryGet(item, out var quote)) continue;
                if (++marriages > 1)
                {
                    PlayerMarriageAgreementBehavior.Notify(new TextObject("{=BC_Marriage_OneAgreement}Only one marriage agreement can be concluded at a time."));
                    return false;
                }
                if (PlayerMarriageAgreementBehavior.Ready(quote.Agreement, out TextObject reason)) continue;
                PlayerMarriageAgreementBehavior.Notify(reason);
                return false;
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(BarterManager), nameof(BarterManager.ApplyAndFinalizePlayerBarter))]
    internal static class PlayerMarriageBarterCommitPatch
    {
        private static bool Prefix(BarterManager __instance, Hero offererHero, Hero otherHero, BarterData barterData)
        {
            if (PlayerMarriageBarterGuard.Validate(barterData.GetOfferedBarterables())) return true;
            __instance.CancelAndFinalizePlayerBarter(offererHero, otherHero, barterData);
            return false;
        }
    }

    [HarmonyPatch(typeof(BarterManager), "ApplyBarterOffer")]
    internal static class PlayerMarriageBarterAtomicPatch
    {
        private static bool Prefix(List<Barterable> barters) => PlayerMarriageBarterGuard.Validate(barters);
    }

    [HarmonyPatch(typeof(MarriageBarterable), nameof(MarriageBarterable.Apply))]
    internal static class PlayerMarriageBarterWeddingPatch
    {
        private static bool Prefix(MarriageBarterable __instance, out NpcMarriageClanContext __state)
        {
            __state = null;
            if (!PlayerMarriagePricing.TryGet(__instance, out var quote)) return true;
            if (!PlayerMarriageAgreementBehavior.Ready(quote.Agreement, out TextObject reason))
            { PlayerMarriageAgreementBehavior.Notify(reason); return false; }
            __state = new NpcMarriageClanContext(quote.Agreement.Player, quote.Agreement.Other, quote.Agreement.Destination);
            return true;
        }
        private static void Finalizer(NpcMarriageClanContext __state) => __state?.Dispose();
    }

    [HarmonyPatch(typeof(MarriageBarterable), nameof(MarriageBarterable.Name), MethodType.Getter)]
    internal static class PlayerMarriageBarterNamePatch
    {
        private static void Postfix(MarriageBarterable __instance, ref TextObject __result)
        {
            if (!PlayerMarriagePricing.TryGet(__instance, out var quote)) return;
            __result = new TextObject("{=BC_Marriage_BarterHousehold}{FORM}: {HERO} joins {HOUSE}")
                .SetTextVariable("FORM", quote.Agreement.FormText()).SetTextVariable("HERO", quote.Agreement.Departing.Name)
                .SetTextVariable("HOUSE", quote.Agreement.Destination.Name);
        }
    }
}
