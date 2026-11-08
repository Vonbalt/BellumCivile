using System;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;

namespace BellumCivile.Patches
{
    internal static class LordshipFinancePatches
    {
        private static bool _villagePatched, _surplusPatched;

        internal static void Initialize()
        {
            // Some runtimes run the native finance type initializer while Harmony installs a patch.
            if (Game.Current?.GameTextManager == null) return;
            Install(typeof(LordshipVillageIncomePatch), ref _villagePatched);
            Install(typeof(LordshipSurplusPatch), ref _surplusPatched);
        }

        private static void Install(Type type, ref bool installed)
        {
            if (installed) return;
            var harmony = new Harmony("com.bellumcivile.lordship.finance." + type.Name);
            try
            {
                harmony.CreateClassProcessor(type).Patch();
                installed = true;
            }
            catch (Exception ex)
            {
                harmony.UnpatchAll(harmony.Id);
                BellumCivileLogger.Log($"Failed to apply deferred lordship finance patch '{type.Name}': {ex}");
            }
        }
    }

    [HarmonyPatch(typeof(DefaultClanFinanceModel), nameof(DefaultClanFinanceModel.CalculateVillageIncome))]
    internal static class LordshipVillageIncomePatch
    {
        private static void Postfix(Clan clan, Village village, int __result) => LordshipIncome.Observe(clan, village, __result);
    }

    [HarmonyPatch(typeof(DefaultClanFinanceModel), "AddIncomeFromParty")]
    internal static class LordshipSurplusPatch
    {
        private static bool Prefix(MobileParty party, Clan clan, ref int __result)
        {
            if (LordshipBehavior.Instance?.Ready != true || party?.IsLordParty != true || party.LeaderHero == null || party.LeaderHero == clan?.Leader
                || !LordshipBehavior.Instance.HasServiceSupport(party.LeaderHero)) return true;
            __result = 0;
            return false;
        }
    }
}
