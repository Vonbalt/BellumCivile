using System.Collections.Generic;
using System.Reflection;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;

namespace BellumCivile.Patches
{
    [HarmonyPatch(typeof(EndCaptivityAction), "ApplyInternal")]
    internal static class HostageReleaseGuardPatch
    {
        private static bool Prefix(Hero prisoner, EndCaptivityDetail detail)
            => detail == EndCaptivityDetail.Death || !HostageCustodyGuard.BlocksOrdinaryAction(prisoner);
    }
    [HarmonyPatch(typeof(TransferPrisonerAction), "ApplyInternal")]
    internal static class HostageTransferGuardPatch
    {
        private static bool Prefix(CharacterObject prisonerTroop)
            => !HostageCustodyGuard.BlocksOrdinaryAction(prisonerTroop?.HeroObject);
    }
    [HarmonyPatch(typeof(SellPrisonersAction), "ApplyInternal")]
    internal static class HostageSaleGuardPatch
    {
        private static bool Prefix(ref TroopRoster prisoners)
        {
            var filtered = HostageCustodyGuard.WithoutHostages(prisoners);
            if (ReferenceEquals(filtered, prisoners)) return true;
            prisoners = filtered;
            return prisoners.Count > 0;
        }
    }
    [HarmonyPatch]
    internal static class HostageRansomOfferGuardPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(RansomOfferCampaignBehavior), "ConsiderRansomPrisoner");
            yield return AccessTools.Method(typeof(RansomOfferCampaignBehavior), "OnRansomOffered");
        }
        private static bool Prefix(Hero __0) => !HostageCustodyGuard.IsProtected(__0);
    }
    [HarmonyPatch(typeof(RansomOfferCampaignBehavior), "AcceptRansomOffer")]
    internal static class HostageRansomPaymentGuardPatch
    {
        private static bool Prefix(RansomOfferCampaignBehavior __instance, Hero ____currentRansomHero)
        {
            if (!HostageCustodyGuard.IsProtected(____currentRansomHero)) return true;
            CampaignEventDispatcher.Instance.OnRansomOfferCancelled(____currentRansomHero);
            __instance.SetCurrentRansomHero(null);
            return false;
        }
    }
    [HarmonyPatch(typeof(PartyScreenLogic), nameof(PartyScreenLogic.IsTroopTransferable))]
    internal static class HostagePartyTransferGuardPatch
    {
        private static void Postfix(CharacterObject character, ref bool __result)
        {
            if (HostageCustodyGuard.IsProtected(character?.HeroObject)) __result = false;
        }
    }
    [HarmonyPatch(typeof(PartyScreenLogic), nameof(PartyScreenLogic.DoneLogic))]
    internal static class HostagePartyCommitGuardPatch
    {
        private static bool Prefix(PartyScreenLogic __instance, ref bool __result)
        {
            if (HostageCustodyGuard.PreservesCustody(__instance.LeftOwnerParty?.PrisonRoster, __instance.PrisonerRosters[0])
                && HostageCustodyGuard.PreservesCustody(__instance.RightOwnerParty?.PrisonRoster, __instance.PrisonerRosters[1])) return true;
            __result = false;
            return false;
        }
    }
    [HarmonyPatch(typeof(HeroSpawnCampaignBehavior), "OnHeroComesOfAge")]
    internal static class HostageAdulthoodRelocationGuardPatch
    {
        private static bool Prefix(Hero hero)
            => Campaign.Current?.GetCampaignBehavior<HostagePactBehavior>()?.IsHostageOriginCaptive(hero) != true;
    }
}
