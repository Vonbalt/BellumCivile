using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.CampaignSystem;

namespace BellumCivile.Patches
{
    [HarmonyPatch]
    internal static class RealmLeadershipDiagnosticPatch
    {
        internal static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.PropertySetter(typeof(Kingdom), nameof(Kingdom.RulingClan));
            yield return AccessTools.Method(typeof(Clan), nameof(Clan.SetLeader));
            yield return AccessTools.PropertySetter(typeof(Clan), nameof(Clan.Kingdom));
            yield return AccessTools.Method(typeof(Hero), nameof(Hero.ChangeState));
            yield return AccessTools.Method(typeof(TaleWorlds.CampaignSystem.Actions.DestroyKingdomAction), "ApplyInternal");
        }

        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static void Prefix(object __instance, object __0, out RealmLeadershipDiagnostics.Snapshot[] __state)
            => __state = RealmLeadershipDiagnostics.Capture(__instance ?? __0);

        [HarmonyFinalizer]
        private static void Finalizer(RealmLeadershipDiagnostics.Snapshot[] __state, MethodBase __originalMethod, Exception __exception)
            => RealmLeadershipDiagnostics.Complete(__state, __originalMethod, __exception);
    }
}
