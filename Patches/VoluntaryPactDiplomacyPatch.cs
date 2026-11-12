using System;
using System.Linq;
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
            var description = new TextObject("{=BC_VoluntaryPact_ActionDescription}Propose non-aggression pact.");
            if (enabled)
            {
                var hostage = TreatyHostageEligibility.GetCandidates(source)
                    .Where(c => c.Hero != null && c.Hero != Hero.MainHero)
                    .OrderByDescending(c => c.Tier).ThenBy(c => c.Hero.StringId, StringComparer.Ordinal)
                    .FirstOrDefault()?.Hero;
                int days = BellumCivileOptions.HostagePactMinimumDurationDays;
                var preview = VoluntaryPactPreview.Create(source, target, Clan.PlayerClan, hostage, days, 0);
                if (VoluntaryPactPreview.TryEvaluate(preview, false, out var support, out _))
                    description = new TextObject("{=BC_VoluntaryPact_ActionSupport}Propose non-aggression pact ({SUPPORT})")
                        .SetTextVariable("SUPPORT", VoluntaryPactPreview.SupportText(support));
            }
            __instance.Actions.Add(new KingdomDiplomacyProposalActionItemVM(
                new TextObject("{=BC_VoluntaryPact_Submit}Propose"),
                description,
                PeacetimeHostagePactRules.ProposalInfluenceCost, enabled, reason,
                () => VoluntaryPactPlayerFlow.Open(owner, source, target, ____forceDecision)));
        }
    }
}
