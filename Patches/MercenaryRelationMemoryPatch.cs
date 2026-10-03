using System;
using System.Collections.Generic;
using System.Reflection;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;

namespace BellumCivile.Patches
{
    [HarmonyPatch]
    internal static class MercenaryRelationMemoryPatch
    {
        private const string PayDebtMethod = "conversation_player_want_to_fire_mercenary_with_paying_debt_on_consequence";
        private const string WithholdWagesMethod = "conversation_player_want_to_fire_mercenary_without_paying_debt_on_consequence";
        private const string DismissMethod = "conversation_player_want_to_fire_mercenary_on_consequence";

        [HarmonyTargetMethods]
        private static IEnumerable<MethodBase> TargetMethods()
        {
            Type behaviorType = typeof(LordConversationsCampaignBehavior);
            foreach (string methodName in new[] { PayDebtMethod, WithholdWagesMethod, DismissMethod })
            {
                MethodInfo method = AccessTools.Method(behaviorType, methodName);
                if (method != null)
                    yield return method;
            }
        }

        private static void Prefix(MethodBase __originalMethod, out IDisposable __state)
        {
            __state = null;
            if (__originalMethod == null || __originalMethod.Name == PayDebtMethod)
                return;

            Clan mercenaryClan = Hero.OneToOneConversationHero?.Clan;
            Kingdom employerKingdom = Clan.PlayerClan?.Kingdom;
            if (mercenaryClan == null || employerKingdom == null)
                return;

            string sourceId = __originalMethod.Name == WithholdWagesMethod
                ? RelationMemorySources.MercenaryWagesWithheld
                : RelationMemorySources.MercenaryCompanyDismissed;
            float durationYears = __originalMethod.Name == WithholdWagesMethod ? 8f : 3f;
            __state = RelationMemoryService.Begin(
                sourceId,
                durationYears,
                RelationMemoryScope.House,
                MercenaryRelationMemoryBehavior.GetRealmContext(employerKingdom));
        }

        private static void Postfix(MethodBase __originalMethod, IDisposable __state)
        {
            try
            {
                if (__originalMethod?.Name == PayDebtMethod)
                    MercenaryRelationMemoryBehavior.Instance?.MarkPlayerDebtPaid(Hero.OneToOneConversationHero?.Clan);
            }
            finally
            {
                __state?.Dispose();
            }
        }

        private static Exception Finalizer(Exception __exception, IDisposable __state)
        {
            __state?.Dispose();
            return __exception;
        }
    }
}
