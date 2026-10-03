using System;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.CampaignSystem;

namespace BellumCivile.Patches
{
    [HarmonyPatch]
    internal static class DiplomacyCadetFiefCompatibilityPatch
    {
        private static MethodInfo _target;

        private static bool Prepare()
        {
            var type = AccessTools.TypeByName("Diplomacy.CampaignBehaviors.MaintainInfluenceBehavior");
            _target = type == null ? null : AccessTools.Method(type, "ReduceCorruption", new[] { typeof(Clan) });
            return _target != null;
        }

        private static MethodBase TargetMethod() => _target;

        internal static bool ProtectLastFief(string clanId, int fiefCount) =>
            clanId?.StartsWith("bc_challenge_", StringComparison.Ordinal) == true && fiefCount <= 1;

        [HarmonyPrefix]
        private static bool Prefix(Clan __0)
        {
            // Stop the entire automatic sale before either its fief or gold barter applies.
            // Manual trading and Diplomacy's influence calculation are unaffected.
            return __0 == null || !ProtectLastFief(__0.StringId, __0.Fiefs.Count);
        }
    }
}
