using System.Reflection;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;

namespace BellumCivile.Patches
{
    /// <summary>
    /// Lets Bellum take exclusive control of NPC-to-NPC marriage pacing when the
    /// player enables the MCM testing / overhaul option. Player courtship and
    /// player-arranged marriage dialogues remain untouched.
    /// </summary>
    [HarmonyPatch]
    internal static class BlockVanillaNpcMarriagePatch
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(RomanceCampaignBehavior), "CheckNpcMarriages", new[] { typeof(Clan) });
        }

        private static bool Prefix(out NativeNpcMarriageHouseholdScope __state)
        {
            __state = null;
            if (!BellumCivileOptions.EnableBellumStrategicMarriageLogic) return true;
            if (BellumCivileOptions.UseBellumStrategicNpcMarriagesOnly) return false;
            __state = new NativeNpcMarriageHouseholdScope();
            return true;
        }

        private static void Finalizer(NativeNpcMarriageHouseholdScope __state) => __state?.Dispose();
    }
}
