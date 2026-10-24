using System;
using System.Collections.Generic;
using System.Linq;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace BellumCivile.Patches
{
    [HarmonyPatch(typeof(ClanVariablesCampaignBehavior), "OnGameLoadFinished")]
    internal static class ProtectedRealmLoadMigrationPatch
    {
        private static bool IsMigrationActive() => MBSaveLoad.IsUpdatingGameVersion
            && MBSaveLoad.LastLoadedGameVersion.IsOlderThan(ApplicationVersion.FromString("v1.3.14"));

        [HarmonyPrefix, HarmonyPriority(Priority.First)]
        private static void Prefix(out Dictionary<Kingdom, Clan> __state)
        {
            __state = null;
            if (Campaign.Current == null || !IsMigrationActive()) return;
            __state = Kingdom.All.Where(k => !k.IsEliminated
                && KingdomCreationSafetyHelper.IsValidFounder(k.RulingClan))
                .ToDictionary(k => k, k => k.RulingClan);
        }

        [HarmonyFinalizer]
        private static void Finalizer(Dictionary<Kingdom, Clan> __state)
        {
            if (__state == null || Campaign.Current == null) return;
            foreach (var previous in __state)
            {
                var realm = previous.Key;
                try
                {
                    if (realm.IsEliminated || realm.RulingClan != null
                        || !KingdomCreationSafetyHelper.IsValidFounder(previous.Value) || !IsProtected(realm)) continue;
                    // The native duplicate-ruler migration clears this property before
                    // requesting destruction. Keep the old ruler if that request was deferred.
                    realm.RulingClan = previous.Value;
                    BellumCivileLogger.Log($"Restored ruler after deferred load migration; realm={realm.StringId}; ruling_clan={previous.Value.StringId}.");
                }
                catch (Exception ex)
                {
                    BellumCivileLogger.Log($"Protected realm load migration recovery failed; realm={realm.StringId}; error={ex}");
                }
            }
        }

        private static bool IsProtected(Kingdom realm) => CivilWarConflictBehavior.IsRealmTransferPending(realm)
            || CrownAccessionBehavior.Instance?.IsRealmUnionProtected(realm) == true
            || Campaign.Current.GetCampaignBehavior<PartitionSuccessionBehavior>()?.IsCrownPromotionRealmProtected(realm) == true;
    }
}
