using System;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameComponents;

namespace BellumCivile.Patches
{
    /// <summary>
    /// Shields vanilla barter diplomacy from transient leaderless kingdoms during join-kingdom scoring.
    /// </summary>
    [HarmonyPatch(typeof(DefaultDiplomacyModel), "GetScoreOfClanToJoinKingdom")]
    public static class GuardClanJoinKingdomScorePatch
    {
        public static bool Prefix(Clan clan, Kingdom kingdom, ref float __result)
        {
            if (clan == null || clan.IsEliminated || clan.Leader == null || clan.Leader.IsDead || kingdom == null || kingdom.IsEliminated)
            {
                __result = 0f;
                BellumCivileLogger.Log($"Skipped vanilla clan-join score for invalid input; clan={clan?.StringId ?? "null"} clan_leader={clan?.Leader?.StringId ?? "null"} kingdom={kingdom?.StringId ?? "null"}.");
                return false;
            }

            FactionManagerBehavior factionManager = Campaign.Current?.GetCampaignBehavior<FactionManagerBehavior>();
            if (factionManager == null)
                return true;

            if (factionManager.TryPrepareKingdomForDiplomacyScore(kingdom, "vanilla clan-join score"))
                return true;

            __result = 0f;
            BellumCivileLogger.Log($"Skipped vanilla clan-join score for {clan.StringId} into {kingdom.StringId}; kingdom has no valid Bellum-repairable ruling clan.");
            return false;
        }

        public static Exception Finalizer(Exception __exception, Clan clan, Kingdom kingdom, ref float __result)
        {
            if (__exception == null)
                return null;

            if (!(__exception is NullReferenceException))
                return __exception;

            __result = 0f;
            BellumCivileLogger.Log($"Suppressed null clan-join score crash; clan={clan?.StringId ?? "null"} clan_leader={clan?.Leader?.StringId ?? "null"} kingdom={kingdom?.StringId ?? "null"} rulingClan={kingdom?.RulingClan?.StringId ?? "null"}.");
            return null;
        }
    }
}
