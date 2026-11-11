using System;
using HarmonyLib;
using TaleWorlds.CampaignSystem.Election;
using TaleWorlds.CampaignSystem.ViewModelCollection.KingdomManagement.Decisions;
using TaleWorlds.CampaignSystem.ViewModelCollection.KingdomManagement.Decisions.ItemTypes;

namespace BellumCivile.Patches
{
    [HarmonyPatch(typeof(KingdomDecisionsVM), "GetDecisionItemBasedOnType")]
    internal static class VoluntaryPactDecisionUIPatch
    {
        private static bool Prefix(KingdomDecisionsVM __instance, KingdomDecision decision, ref DecisionItemBaseVM __result)
        {
            if (!(decision is VoluntaryPactDecision)) return true;
            var done = AccessTools.MethodDelegate<Action>(AccessTools.Method(typeof(KingdomDecisionsVM), "OnDecisionOver"), __instance);
            __result = new DecisionItemBaseVM(decision, done);
            return false;
        }
    }
}
