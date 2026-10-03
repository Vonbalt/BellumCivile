using HarmonyLib;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors.BarterBehaviors;

namespace BellumCivile.Patches
{
    /// <summary>
    /// Blocks vanilla's AI-only daily "stop serving this kingdom" barter for noble clans.
    /// Bellum routes noble departures through rebellions, expulsion, exile, or explicit player persuasion.
    /// </summary>
    [HarmonyPatch(typeof(DiplomaticBartersBehavior), "ConsiderClanLeaveKingdom")]
    public static class BlockVanillaClanLeaveKingdomPatch
    {
        private const int RoutineTraceIntervalDays = 30;
        private static readonly Dictionary<string, int> NextRoutineTraceDayByClanId = new Dictionary<string, int>();

        public static bool Prefix(Clan clan)
        {
            if (clan == null || clan.IsEliminated || clan.Leader == null || clan.Leader.IsDead)
            {
                BellumCivileLogger.Log(
                    $"Blocked invalid vanilla clan-leave consideration; clan={clan?.StringId ?? "null"} leader={clan?.Leader?.StringId ?? "null"}.");
                return false;
            }

            if (clan == Clan.PlayerClan)
            {
                if (clan.IsUnderMercenaryService || clan.IsClanTypeMercenary)
                    return true;

                TraceRoutineBlock(clan,
                    $"Blocked vanilla daily player-clan leave barter; kingdom={clan.Kingdom?.StringId ?? "null"} fiefs={clan.Fiefs?.Count ?? 0}.");
                return false;
            }

            if (clan.IsMinorFaction || clan.IsUnderMercenaryService || clan.IsClanTypeMercenary)
                return true;

            Kingdom kingdom = clan.Kingdom;
            if (kingdom == null || kingdom.IsEliminated || kingdom.RulingClan == clan)
                return false;

            TraceRoutineBlock(clan,
                $"Blocked vanilla AI clan-leave barter; clan={clan.StringId} kingdom={kingdom.StringId} fiefs={clan.Fiefs?.Count ?? 0}.");
            return false;
        }

        private static void TraceRoutineBlock(Clan clan, string message)
        {
            if (!BellumCivileDebug.ShowInGameMessages || clan == null || string.IsNullOrWhiteSpace(clan.StringId))
                return;

            int today = Campaign.Current == null ? 0 : (int)CampaignTime.Now.ToDays;
            if (NextRoutineTraceDayByClanId.TryGetValue(clan.StringId, out int nextDay) && today < nextDay)
                return;

            NextRoutineTraceDayByClanId[clan.StringId] = today + RoutineTraceIntervalDays;
            BellumCivileDebug.Trace("clan departure", message);
        }
    }
}
