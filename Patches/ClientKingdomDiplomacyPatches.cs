using System;
using System.Reflection;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CampaignBehaviors;

namespace BellumCivile.Patches
{
    [HarmonyPatch(typeof(FactionManager), nameof(FactionManager.DeclareWar))]
    internal static class ClientLiberationWarCommitPatch
    {
        // Runs even if a later native operation throws. A veto/no-op never commits
        // unless the hostile stance really exists; retain the original exception.
        [HarmonyFinalizer]
        private static Exception Finalizer(IFaction __0, IFaction __1, Exception __exception)
        {
            if (ClientKingdomBehavior.Instance?.IsApplyingStartingClientage == true) return __exception;
            try { CourtAgendaBehavior.Current?.RecordCourtLiberationWar(__0 as Kingdom, __1 as Kingdom); }
            catch (Exception ex) { BellumCivileLogger.Log("Court liberation war receipt failed: " + ex); }
            try
            {
                var clients = ClientKingdomBehavior.Instance;
                clients?.ConfirmLiberationWar(__0 as Kingdom, __1 as Kingdom);
                clients?.ConfirmLiberationWar(__1 as Kingdom, __0 as Kingdom);
            }
            catch (Exception ex)
            {
                BellumCivileLogger.Log("Client liberation post-stance cleanup failed: " + ex);
            }
            return __exception;
        }
    }

    [HarmonyPatch]
    internal static class ClientKingdomDeclareWarPatch
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(DeclareWarAction), "ApplyInternal");
        }

        [HarmonyPrefix]
        private static bool Prefix(IFaction __0, IFaction __1)
        {
            Kingdom initiator = __0 as Kingdom;
            Kingdom target = __1 as Kingdom;
            ClientKingdomBehavior clients = ClientKingdomBehavior.Instance;
            if (clients == null || clients.CanDeclareWar(initiator, target, out string reason))
                return true;

            BellumCivileLogger.Log($"Blocked client diplomacy war declaration; initiator={initiator?.StringId}; target={target?.StringId}; reason={reason}.");
            return false;
        }
    }

    [HarmonyPatch]
    internal static class ClientKingdomMakePeacePatch
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(MakePeaceAction), "ApplyInternal");
        }

        [HarmonyPrefix]
        private static bool Prefix(IFaction __0, IFaction __1)
        {
            Kingdom first = __0 as Kingdom;
            Kingdom second = __1 as Kingdom;
            ClientKingdomBehavior clients = ClientKingdomBehavior.Instance;
            if (clients == null || clients.CanMakePeace(first, second, out string reason))
                return true;

            BellumCivileLogger.Log($"Blocked independent client peace; first={first?.StringId}; second={second?.StringId}; reason={reason}.");
            return false;
        }
    }

    [HarmonyPatch(typeof(AllianceCampaignBehavior), "OnWarDeclared")]
    internal static class StartingClientageAllianceWarPatch
    {
        [HarmonyPrefix]
        private static bool Prefix()
        {
            // Startup sets the entire bloc's stances before creating its alliances.
            // These historical wars must not enqueue fresh call-to-war votes or penalties.
            return ClientKingdomBehavior.Instance?.IsApplyingStartingClientage != true;
        }
    }

    [HarmonyPatch(typeof(AllianceCampaignBehavior), "StartAlliance")]
    internal static class ClientKingdomStartAlliancePatch
    {
        [HarmonyPrefix]
        private static bool Prefix(Kingdom __0, Kingdom __1)
        {
            return ClientKingdomBehavior.Instance?.CanStartAlliance(__0, __1) ?? true;
        }
    }

    [HarmonyPatch(typeof(AllianceCampaignBehavior), "EndAlliance")]
    internal static class ClientKingdomEndAlliancePatch
    {
        [HarmonyPrefix]
        private static bool Prefix(Kingdom __0, Kingdom __1)
        {
            return ClientKingdomBehavior.Instance?.CanEndAlliance(__0, __1) ?? true;
        }
    }

    [HarmonyPatch(typeof(AllianceCampaignBehavior), "AddAllianceDecision")]
    internal static class AllianceDecisionLifecycleGuardPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(Kingdom __0, Kingdom __1)
        {
            if (HasValidDecisionRuler(__0) && HasValidDecisionRuler(__1) && __0 != __1)
                return true;

            BellumCivileLogger.Log(
                $"Skipped invalid vanilla alliance renewal decision; proposer_realm={DescribeRealm(__0)}; " +
                $"offered_realm={DescribeRealm(__1)}.");
            return false;
        }

        private static bool HasValidDecisionRuler(Kingdom kingdom)
        {
            Clan ruler = kingdom?.RulingClan;
            return kingdom != null
                && !kingdom.IsEliminated
                && ruler != null
                && !ruler.IsEliminated
                && ruler.Kingdom == kingdom
                && ruler.Leader != null;
        }

        private static string DescribeRealm(Kingdom kingdom)
        {
            if (kingdom == null)
                return "null";

            return $"{kingdom.StringId}(eliminated={kingdom.IsEliminated}," +
                $"ruler={kingdom.RulingClan?.StringId ?? "null"}," +
                $"ruler_leader={kingdom.RulingClan?.Leader?.StringId ?? "null"})";
        }
    }

    [HarmonyPatch(typeof(TradeAgreementsCampaignBehavior), "MakeTradeAgreement")]
    internal static class ClientKingdomMakeTradeAgreementPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(Kingdom __0, Kingdom __1)
        {
            return ClientKingdomBehavior.Instance?.CanMakeTradeAgreement(__0, __1) ?? true;
        }
    }

    [HarmonyPatch(typeof(TradeAgreementsCampaignBehavior), "EndTradeAgreement")]
    internal static class ClientKingdomEndTradeAgreementPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(Kingdom __0, Kingdom __1)
        {
            return ClientKingdomBehavior.Instance?.CanEndTradeAgreement(__0, __1) ?? true;
        }
    }
}
