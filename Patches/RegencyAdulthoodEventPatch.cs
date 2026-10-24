using System;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;

namespace BellumCivile.Patches
{
    [HarmonyPatch(typeof(CampaignEventDispatcher), nameof(CampaignEventDispatcher.OnHeroComesOfAge))]
    internal static class RegencyAdulthoodEventPatch
    {
        [HarmonyPrefix, HarmonyPriority(Priority.First)]
        private static void Prefix(Hero hero, out Tuple<RegencyBehavior, RegencyRecord> __state)
        {
            __state = null;
            if (hero == null) return;
            var behavior = Campaign.Current?.GetCampaignBehavior<RegencyBehavior>();
            var record = behavior?.BeginWardAdulthoodEvent(hero);
            __state = record == null ? null : Tuple.Create(behavior, record);
        }

        [HarmonyFinalizer]
        private static void Finalizer(Hero hero, Tuple<RegencyBehavior, RegencyRecord> __state, Exception __exception)
        {
            if (__state != null) __state.Item1.FinishWardAdulthoodEvent(hero, __state.Item2, __exception);
        }
    }
}
