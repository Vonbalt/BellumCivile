using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Bannerlord.UIExtenderEx;
using BellumCivile.UI.VanillaTabs.Kingdoms.Factions;
using HarmonyLib;
using TaleWorlds.CampaignSystem.ViewModelCollection.KingdomManagement;

namespace BellumCivile.Patches
{
    [HarmonyPatch(typeof(KingdomManagementVM), "OnFrameTick")]
    internal static class KingdomManagementTabDiagnosticsPatch
    {
        private sealed class DiagnosticState
        {
            public int FramesObserved { get; set; }
            public bool Completed { get; set; }
        }

        private static readonly ConditionalWeakTable<KingdomManagementVM, DiagnosticState> ViewModelStates =
            new ConditionalWeakTable<KingdomManagementVM, DiagnosticState>();

        private static void Postfix(KingdomManagementVM __instance)
        {
            if (!BellumCivileDebug.ShowInGameMessages || __instance == null)
                return;

            DiagnosticState state = ViewModelStates.GetValue(__instance, _ => new DiagnosticState());
            if (state.Completed || ++state.FramesObserved < 2)
                return;

            state.Completed = true;

            try
            {
                if (!BellumFactionsKingdomTabState.TryDiagnose(__instance, out IReadOnlyList<string> failures))
                {
                    Version uiExtenderVersion = typeof(UIExtender).Assembly.GetName().Version;
                    string diplomacy = ModIntegrationHelper.IsDiplomacyLoaded ? "yes" : "no";
                    BellumCivileDebug.Trace(
                        "kingdom tabs",
                        $"[KTAB-VM] Hierarchy and Factions failed: Bellum's KingdomManagement mixin was not attached, so injected labels and commands have no data source. VM={__instance.GetType().FullName}; UIExtenderEx={uiExtenderVersion}; Diplomacy={diplomacy}. Check dependency versions, load order, and other KingdomManagement UI extensions.",
                        requestInGameDisplay: true);
                    return;
                }

                foreach (string failure in failures)
                {
                    if (!string.IsNullOrWhiteSpace(failure))
                        BellumCivileDebug.Trace("kingdom tabs", failure, requestInGameDisplay: true);
                }
            }
            catch (Exception ex)
            {
                BellumCivileDebug.Trace(
                    "kingdom tabs",
                    $"[KTAB-CHECK] Tab diagnostics failed: {ex.GetType().Name}: {ex.Message}",
                    requestInGameDisplay: true);
            }
        }
    }
}
