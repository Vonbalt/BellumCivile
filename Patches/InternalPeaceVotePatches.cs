using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Election;
using TaleWorlds.Localization;
using System.Collections.Generic;
using System.Reflection;
using TaleWorlds.CampaignSystem.ViewModelCollection.KingdomManagement.Decisions;

namespace BellumCivile.Patches
{
    internal static class InternalPeaceVoteText
    {
        internal static bool IsInternal(MakePeaceKingdomDecision decision, out bool rival)
        {
            rival = false;
            return decision != null && (InternalPeaceSettlementBehavior.TryIdentify(decision.Kingdom,
                decision.FactionToMakePeaceWith as Kingdom, out _, out rival)
                || InternalPeaceSettlementBehavior.Current?.KnowsOffer(decision) == true);
        }

        internal static TextObject Terms() => new TextObject("{=BC_InternalPeace_Terms}End the dispute without enforcing either side's demands. The departing houses will rejoin the realm, and no tribunal will be held. No tribute will be paid.");
    }

    [HarmonyPatch(typeof(MakePeaceKingdomDecision), nameof(MakePeaceKingdomDecision.OnShowDecision))]
    internal static class InternalPeaceCourierPatch
    {
        private static bool Prefix(MakePeaceKingdomDecision __instance, ref bool __result)
        {
            if (!InternalPeaceVoteText.IsInternal(__instance, out _)) return true;
            // Finish the proposing council's actual vote. The adapter obtains the opposing
            // player's consent afterward, without a tribute-bearing native courier offer.
            __result = true;
            return false;
        }
    }

    [HarmonyPatch(typeof(MakePeaceKingdomDecision), nameof(MakePeaceKingdomDecision.GetSupportTitle))]
    internal static class InternalPeaceSupportTextPatch
    {
        private static bool Prefix(MakePeaceKingdomDecision __instance, ref TextObject __result)
        {
            if (!InternalPeaceVoteText.IsInternal(__instance, out _)) return true;
            __result = InternalPeaceSettlementBehavior.Current?.TermsFor(__instance) ?? InternalPeaceVoteText.Terms(); return false;
        }
    }

    [HarmonyPatch(typeof(MakePeaceKingdomDecision), nameof(MakePeaceKingdomDecision.GetChosenOutcomeText))]
    internal static class InternalPeaceResultTextPatch
    {
        private static bool Prefix(MakePeaceKingdomDecision __instance, DecisionOutcome chosenOutcome, ref TextObject __result)
        {
            var result = InternalPeaceSettlementBehavior.Current?.ResultFor(__instance);
            if (result != null) { __result = result; return false; }
            if (!InternalPeaceVoteText.IsInternal(__instance, out _)) return true;
            bool peace = (chosenOutcome as MakePeaceKingdomDecision.MakePeaceDecisionOutcome)?.ShouldPeaceBeDeclared == true;
            __result = peace
                ? new TextObject("{=BC_InternalPeace_VotePassed}The council has agreed to end the dispute without judgment. The houses will return to the realm when these deliberations conclude.")
                : new TextObject("{=BC_InternalPeace_VoteRejected}The council has chosen to continue the conflict. No settlement has been agreed.");
            return false;
        }
    }

    [HarmonyPatch(typeof(MakePeaceKingdomDecision), nameof(MakePeaceKingdomDecision.IsAllowed))]
    internal static class InternalPeaceRivalVotePatch
    {
        private static void Postfix(MakePeaceKingdomDecision __instance, ref bool __result)
        {
            if (InternalPeaceVoteText.IsInternal(__instance, out bool rival) && rival
                && InternalPeaceSettlementBehavior.Current?.HasOffer(__instance) != true) __result = false;
        }
    }

    [HarmonyPatch(typeof(MakePeaceKingdomDecision), nameof(MakePeaceKingdomDecision.ApplyChosenOutcome))]
    internal static class InternalPeaceVoteOutcomePatch
    {
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(MakePeaceKingdomDecision __instance, DecisionOutcome chosenOutcome, bool ____applyResults)
        {
            if (!InternalPeaceVoteText.IsInternal(__instance, out _)) return true;
            if (____applyResults) InternalPeaceSettlementBehavior.Current?.CompleteVote(__instance,
                (chosenOutcome as MakePeaceKingdomDecision.MakePeaceDecisionOutcome)?.ShouldPeaceBeDeclared == true);
            return false;
        }
    }

    [HarmonyPatch]
    internal static class InternalPeaceDescriptionPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(MakePeaceKingdomDecision), nameof(MakePeaceKingdomDecision.GetSupportDescription));
            yield return AccessTools.Method(typeof(MakePeaceKingdomDecision), nameof(MakePeaceKingdomDecision.GetChooseDescription));
        }
        private static bool Prefix(MakePeaceKingdomDecision __instance, ref TextObject __result)
        {
            if (!InternalPeaceVoteText.IsInternal(__instance, out _)) return true;
            __result = InternalPeaceSettlementBehavior.Current?.TermsFor(__instance) ?? InternalPeaceVoteText.Terms();
            return false;
        }
    }

    [HarmonyPatch]
    internal static class InternalPeaceStaleRivalVotePatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(KingdomDecisionsVM), nameof(KingdomDecisionsVM.HandleDecision));
            yield return AccessTools.Method(typeof(KingdomDecisionsVM), nameof(KingdomDecisionsVM.RefreshWith));
        }
        private static bool Prefix(KingdomDecision __0)
        {
            var peace = __0 as MakePeaceKingdomDecision;
            if (!InternalPeaceVoteText.IsInternal(peace, out bool rival) || !rival
                || InternalPeaceSettlementBehavior.Current?.HasOffer(peace) == true) return true;
            peace.Kingdom.RemoveDecision(peace);
            BellumCivileLogger.Log("Retired unsupported standalone white peace between rival claimants.");
            return false;
        }
    }

    [HarmonyPatch(typeof(Kingdom), nameof(Kingdom.AddDecision))]
    internal static class InternalPeaceRivalInsertionPatch
    {
        private static bool Prefix(KingdomDecision kingdomDecision)
        {
            var peace = kingdomDecision as MakePeaceKingdomDecision;
            return !InternalPeaceVoteText.IsInternal(peace, out bool rival) || !rival
                || InternalPeaceSettlementBehavior.Current?.HasOffer(peace) == true;
        }
    }
}
