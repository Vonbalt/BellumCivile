using System.Runtime.CompilerServices;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Election;

namespace BellumCivile.Patches
{
    // A mandatory wartime succession cannot be cancelled merely because its proposer dislikes the ballot.
    [HarmonyPatch(typeof(KingdomDecision), nameof(KingdomDecision.ShouldBeCancelled))]
    internal static class RebelSuccessionCancellationDiagnosticPatch
    {
        private static readonly ConditionalWeakTable<KingdomDecision, object> Reported = new ConditionalWeakTable<KingdomDecision, object>();

        [HarmonyPostfix]
        private static void Postfix(KingdomDecision __instance, ref bool __result)
        {
            if (CivilWarResolutionBehavior.Current?.KeepCoalitionBallot(__instance) == true)
            {
                __result = false;
                return;
            }
            if (!__result || !(__instance is KingSelectionKingdomDecision) || Reported.TryGetValue(__instance, out _)) return;
            if (RebelSuccessionDecisionDiagnostic.Log(__instance, "succession cancellation"))
                Reported.Add(__instance, new object());
        }
    }

    [HarmonyPatch(typeof(Kingdom), nameof(Kingdom.AddDecision))]
    internal static class RebelSuccessionAddedDiagnosticPatch
    {
        [HarmonyPostfix]
        private static void Postfix(KingdomDecision __0) => RebelSuccessionDecisionDiagnostic.Log(__0, "succession added");
    }

    [HarmonyPatch(typeof(Kingdom), nameof(Kingdom.RemoveDecision))]
    internal static class RebelSuccessionRemovedDiagnosticPatch
    {
        [HarmonyPrefix]
        private static void Prefix(KingdomDecision __0) => RebelSuccessionDecisionDiagnostic.Log(__0, "succession removal");
    }

    internal static class RebelSuccessionDecisionDiagnostic
    {
        internal static bool Log(KingdomDecision decision, string stage)
        {
            if (!(decision is KingSelectionKingdomDecision)) return false;
            var realm = decision.Kingdom;
            var faction = Campaign.Current?.GetCampaignBehavior<FactionManagerBehavior>()?.GetFactionByRebelKingdom(realm);
            if (faction == null) return false;
            CivilWarTransitionDiagnostics.Log(stage, faction, realm,
                $"proposer={decision.ProposerClan?.StringId}; proposer_realm={decision.ProposerClan?.Kingdom?.StringId}; "
                + $"proposer_influence={decision.ProposerClan?.Influence}; queued={realm.UnresolvedDecisions.Contains(decision)}");
            return true;
        }
    }
}
