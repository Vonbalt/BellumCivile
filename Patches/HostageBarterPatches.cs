using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.BarterSystem;
using TaleWorlds.CampaignSystem.BarterSystem.Barterables;

namespace BellumCivile.Patches
{
    internal static class HostageBarterGuard
    {
        private static readonly FieldInfo ReleaseHero = AccessTools.Field(typeof(SetPrisonerFreeBarterable), "_prisonerCharacter");
        private static readonly FieldInfo TransferHero = AccessTools.Field(typeof(TransferPrisonerBarterable), "_prisonerCharacter");
        internal static bool IsProtected(Barterable item)
        {
            Hero hero = item is SetPrisonerFreeBarterable ? ReleaseHero.GetValue(item) as Hero
                : item is TransferPrisonerBarterable ? TransferHero.GetValue(item) as Hero : null;
            return HostageCustodyGuard.IsProtected(hero);
        }
        internal static bool HasProtected(IEnumerable<Barterable> items) => items?.Any(IsProtected) == true;
    }

    [HarmonyPatch(typeof(CampaignEventDispatcher), nameof(CampaignEventDispatcher.OnBarterablesRequested))]
    internal static class HostageBarterListPatch
    {
        private static void Postfix(BarterData __0) => __0?.GetBarterables().RemoveAll(HostageBarterGuard.IsProtected);
    }

    [HarmonyPatch(typeof(BarterManager), nameof(BarterManager.ApplyAndFinalizePlayerBarter))]
    internal static class HostagePlayerBarterCommitPatch
    {
        private static bool Prefix(BarterManager __instance, Hero offererHero, Hero otherHero, BarterData barterData)
        {
            if (!HostageBarterGuard.HasProtected(barterData?.GetOfferedBarterables())) return true;
            __instance.CancelAndFinalizePlayerBarter(offererHero, otherHero, barterData);
            return false;
        }
    }

    [HarmonyPatch(typeof(BarterManager), nameof(BarterManager.ExecuteAIBarter))]
    internal static class HostageAiBarterCommitPatch
    {
        private static bool Prefix(BarterData barterData)
            => !HostageBarterGuard.HasProtected(barterData?.GetOfferedBarterables());
    }

    [HarmonyPatch(typeof(BarterManager), "ApplyBarterOffer")]
    internal static class HostageBarterAtomicGuardPatch
    {
        private static bool Prefix(List<Barterable> barters) => !HostageBarterGuard.HasProtected(barters);
    }
}
