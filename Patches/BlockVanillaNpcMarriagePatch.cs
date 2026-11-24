using System.Reflection;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;

namespace BellumCivile.Patches
{
    /// <summary>
    /// Switches only the automatic NPC roll. Player negotiations remain available.
    /// </summary>
    [HarmonyPatch]
    internal static class BlockVanillaNpcMarriagePatch
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(RomanceCampaignBehavior), "CheckNpcMarriages", new[] { typeof(Clan) });
        }

        private static bool Prefix() => !BellumCivileOptions.EnableBellumStrategicMarriageLogic;
    }
}
