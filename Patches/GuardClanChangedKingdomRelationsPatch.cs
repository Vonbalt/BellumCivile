using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CampaignBehaviors;

namespace BellumCivile.Patches
{
    /// <summary>
    /// Vanilla applies relation penalties when a clan leaves a kingdom, but assumes
    /// every involved clan still has a valid leader. Bellum preserves and relocates
    /// exiled clans more often than vanilla, so defend this volatile callback.
    /// </summary>
    [HarmonyPatch]
    public static class GuardClanChangedKingdomRelationsPatch
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(
                typeof(CharacterRelationCampaignBehavior),
                "OnClanChangedKingdom",
                new[]
                {
                    typeof(Clan),
                    typeof(Kingdom),
                    typeof(Kingdom),
                    typeof(ChangeKingdomAction.ChangeKingdomActionDetail),
                    typeof(bool)
                });
        }

        private static bool Prefix(
            Clan clan,
            Kingdom oldKingdom,
            Kingdom newKingdom,
            ChangeKingdomAction.ChangeKingdomActionDetail detail,
            bool showNotification)
        {
            if (detail != ChangeKingdomAction.ChangeKingdomActionDetail.LeaveWithRebellion &&
                detail != ChangeKingdomAction.ChangeKingdomActionDetail.LeaveKingdom)
            {
                return true;
            }

            ApplySafeLeaveRelationPenalties(clan, oldKingdom, detail);
            return false;
        }

        internal static void ApplySafeLeaveRelationPenalties(
            Clan clan,
            Kingdom oldKingdom,
            ChangeKingdomAction.ChangeKingdomActionDetail detail)
        {
            Hero leavingLeader = clan?.Leader;
            if (clan == null || oldKingdom == null || leavingLeader == null || leavingLeader.IsDead)
            {
                BellumCivileLogger.Log(
                    $"Skipped clan-leave relation penalties for invalid transition; clan={clan?.StringId ?? "null"} leader={leavingLeader?.StringId ?? "null"} oldKingdom={oldKingdom?.StringId ?? "null"} detail={detail}.");
                return;
            }

            int relationChange = detail == ChangeKingdomAction.ChangeKingdomActionDetail.LeaveWithRebellion ? -40 : -20;
            int applied = 0;
            int skipped = 0;

            foreach (Clan oldKingdomClan in new List<Clan>(oldKingdom.Clans))
            {
                Hero targetLeader = oldKingdomClan?.Leader;
                if (oldKingdomClan == null || oldKingdomClan.IsEliminated || targetLeader == null || targetLeader.IsDead || targetLeader == leavingLeader)
                {
                    skipped++;
                    continue;
                }

                try
                {
                    RelationMemoryService.ApplyChange(
                        leavingLeader,
                        targetLeader,
                        relationChange,
                        false,
                        detail == ChangeKingdomAction.ChangeKingdomActionDetail.LeaveWithRebellion
                            ? RelationMemorySources.DefectedFromMyCause
                            : RelationMemorySources.AbandonedMyCause,
                        detail == ChangeKingdomAction.ChangeKingdomActionDetail.LeaveWithRebellion ? 20f : 10f,
                        RelationMemoryScope.House,
                        oldKingdom.Name?.ToString());
                    applied++;
                }
                catch (NullReferenceException ex)
                {
                    skipped++;
                    BellumCivileLogger.Log(
                        $"Suppressed clan-leave relation crash; leavingClan={clan.StringId} targetClan={oldKingdomClan.StringId} oldKingdom={oldKingdom.StringId} error={ex.Message}.");
                }
            }

            if (skipped > 0)
            {
                BellumCivileLogger.Log(
                    $"Applied safe clan-leave relation penalties; clan={clan.StringId} oldKingdom={oldKingdom.StringId} detail={detail} applied={applied} skipped={skipped}.");
            }

        }
    }
}
