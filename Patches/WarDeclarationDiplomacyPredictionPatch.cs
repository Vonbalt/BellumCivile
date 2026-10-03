using System.Reflection;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Election;
using TaleWorlds.CampaignSystem.ViewModelCollection.KingdomManagement.Diplomacy;
using TaleWorlds.Core;
using TaleWorlds.Localization;

namespace BellumCivile.Patches
{
    [HarmonyPatch]
    internal static class WarDeclarationSupportLabelPatch
    {
        private static MethodBase TargetMethod()
        {
            return typeof(KingdomDiplomacyVM).GetMethod(
                "CalculateWarSupport",
                BindingFlags.Instance | BindingFlags.NonPublic);
        }

        [HarmonyPrefix]
        private static bool Prefix(IFaction faction, ref TextObject __result)
        {
            if (!WarPeaceRevampBehavior.IsRevampEnabled() || Clan.PlayerClan == null || faction == null)
                return true;

            DeclareWarDecision decision = new DeclareWarDecision(Clan.PlayerClan, faction);
            ForeignPolicyCouncilEvaluation evaluation = WarDeclarationCouncilService.EvaluatePreliminary(decision);
            __result = WarDeclarationCouncilService.GetSupportLabel(evaluation.Tally);
            return false;
        }
    }

}
