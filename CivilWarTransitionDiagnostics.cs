using System;
using System.Diagnostics;
using System.Linq;
using BellumCivile.Behaviors;
using TaleWorlds.CampaignSystem;

namespace BellumCivile
{
    internal static class CivilWarTransitionDiagnostics
    {
        internal static void Log(string stage, FactionObject faction, Kingdom shell, string detail = null)
        {
            try
            {
                var parent = faction?.ParentKingdom;
                string callers = string.Join(" > ", new StackTrace(1, false).GetFrames()
                    .Take(7).Select(frame => frame.GetMethod())
                    .Select(method => method.DeclaringType?.FullName + "." + method.Name));
                BellumCivileLogger.Log($"Civil-war transition; stage={stage}; type={faction?.Type}; "
                    + $"parent={parent?.StringId}; parent_ruler={parent?.Leader?.StringId}; "
                    + $"shell={shell?.StringId}; shell_ruler={shell?.Leader?.StringId}; "
                    + $"faction_house={faction?.Leader?.StringId}; faction_head={faction?.Leader?.Leader?.StringId}; "
                    + $"parent_fiefs={parent?.Fiefs.Count}; shell_fiefs={shell?.Fiefs.Count}; "
                    + $"shell_eliminated={shell?.IsEliminated}; peace_suppressed={CivilWarResolutionBehavior.IsPeaceHandlingSuppressed}; "
                    + $"detail={detail}; callers={callers}.");
            }
            catch (Exception ex)
            {
                BellumCivileLogger.Log($"Civil-war transition diagnostic failed; stage={stage}; error={ex.Message}.");
            }
        }
    }
}
