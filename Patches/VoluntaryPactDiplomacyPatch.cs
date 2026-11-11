using System;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Election;
using TaleWorlds.CampaignSystem.ViewModelCollection;
using TaleWorlds.CampaignSystem.ViewModelCollection.KingdomManagement.Diplomacy;
using TaleWorlds.Localization;

namespace BellumCivile.Patches
{
    [HarmonyPatch(typeof(KingdomDiplomacyVM), "OnSetPeaceItem")]
    internal static class VoluntaryPactDiplomacyPatch
    {
        private static void Postfix(KingdomDiplomacyVM __instance, KingdomTruceItemVM item,
            Action<KingdomDecision> ____forceDecision)
        {
            if (!BellumCivileOptions.EnableWarPeaceLogicRevamp || __instance?.Actions == null
                || !(item?.Faction1 is Kingdom source) || !(item.Faction2 is Kingdom target)
                || source == target || source.IsAllyWith(target)) return;
            var owner = Campaign.Current?.GetCampaignBehavior<VoluntaryPactProposalBehavior>();
            if (owner == null || Clan.PlayerClan == null || Clan.PlayerClan.Kingdom != source) return;
            bool enabled = owner.CanPropose(source, target, Clan.PlayerClan, out TextObject reason);
            if (enabled && !CampaignUIHelper.GetMapScreenActionIsEnabledWithReason(out var blocked))
            { enabled = false; reason = blocked; }
            __instance.Actions.Add(new KingdomDiplomacyProposalActionItemVM(
                new TextObject("{=BC_VoluntaryPact_Submit}Propose"),
                new TextObject("{=BC_VoluntaryPact_ActionDescription}Propose a mutual hostage pledge of peace."),
                PeacetimeHostagePactRules.ProposalInfluenceCost, enabled, reason,
                () => VoluntaryPactPlayerFlow.Open(owner, source, target, ____forceDecision)));
        }
    }
}
