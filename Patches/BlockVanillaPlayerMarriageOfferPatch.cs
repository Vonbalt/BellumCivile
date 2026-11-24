using System.Reflection;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;

namespace BellumCivile.Patches
{
    /// <summary>
    /// When Bellum's strategic marriage system is enabled, block vanilla's automatic
    /// player-clan marriage offers so Bellum can send strategic offers through the
    /// same vanilla popup.
    /// </summary>
    [HarmonyPatch]
    internal static class BlockVanillaPlayerMarriageOfferPatch
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(MarriageOfferCampaignBehavior), "DailyTickClan", new[] { typeof(Clan) });
        }

        private static bool Prefix() => !BellumCivileOptions.EnableBellumStrategicMarriageLogic;
    }
}
