using System;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameComponents;

namespace BellumCivile.Patches
{
    /// <summary>
    /// Shields vanilla barter diplomacy from transient leaderless kingdoms during kingdom-recruitment scoring.
    /// </summary>
    [HarmonyPatch(typeof(DefaultDiplomacyModel), "GetScoreOfKingdomToGetClan")]
    public static class GuardKingdomGetClanScorePatch
    {
        public static bool Prefix(Kingdom kingdom, Clan clan, ref float __result)
        {
            if (clan == null || clan.IsEliminated || clan.Leader == null || clan.Leader.IsDead || kingdom == null || kingdom.IsEliminated)
            {
                __result = 0f;
                BellumCivileLogger.Log($"Skipped vanilla kingdom-get-clan score for invalid input; kingdom={kingdom?.StringId ?? "null"} clan={clan?.StringId ?? "null"} clan_leader={clan?.Leader?.StringId ?? "null"}.");
                return false;
            }

            FactionManagerBehavior factionManager = Campaign.Current?.GetCampaignBehavior<FactionManagerBehavior>();
            if (factionManager == null)
                return true;

            if (factionManager.TryPrepareKingdomForDiplomacyScore(kingdom, "vanilla kingdom-get-clan score"))
                return true;

            __result = 0f;
            BellumCivileLogger.Log($"Skipped vanilla kingdom-get-clan score for {kingdom.StringId} recruiting {clan.StringId}; kingdom has no valid Bellum-repairable ruling clan.");
            return false;
        }

        public static Exception Finalizer(Exception __exception, Kingdom kingdom, Clan clan, ref float __result)
        {
            if (__exception == null)
                return null;

            if (!(__exception is NullReferenceException))
                return __exception;

            __result = 0f;
            BellumCivileLogger.Log($"Suppressed null kingdom-get-clan score crash; kingdom={kingdom?.StringId ?? "null"} rulingClan={kingdom?.RulingClan?.StringId ?? "null"} clan={clan?.StringId ?? "null"} clan_leader={clan?.Leader?.StringId ?? "null"}.");
            return null;
        }
    }
}
