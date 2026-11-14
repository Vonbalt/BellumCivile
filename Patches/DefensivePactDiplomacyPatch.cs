using System;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Election;
using TaleWorlds.CampaignSystem.ViewModelCollection;
using TaleWorlds.CampaignSystem.ViewModelCollection.KingdomManagement.Diplomacy;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace BellumCivile.Patches
{
    [HarmonyPatch(typeof(KingdomDiplomacyVM), "OnSetPeaceItem")]
    internal static class DefensivePactDiplomacyPatch
    {
        private static void Postfix(KingdomDiplomacyVM __instance, KingdomTruceItemVM item,
            Action<KingdomDecision> ____forceDecision)
        {
            if (!BellumCivileOptions.EnableWarPeaceLogicRevamp || __instance?.Actions == null
                || !(item?.Faction1 is Kingdom source) || !(item.Faction2 is Kingdom target)
                || source == target || Clan.PlayerClan?.Kingdom != source) return;
            var owner = Campaign.Current?.GetCampaignBehavior<DefensivePactBehavior>();
            if (owner == null) return;
            var pending = owner.GetPendingVote(source, target);
            if (pending != null)
            {
                bool canResolve = CampaignUIHelper.GetMapScreenActionIsEnabledWithReason(out var disabledReason);
                __instance.Actions.Add(new KingdomDiplomacyProposalActionItemVM(
                    new TextObject("{=BC_DefensivePact_ResolveButton}Resolve"),
                    new TextObject("{=BC_DefensivePact_ResolveAction}Resolve defensive pact decision"),
                    0, canResolve, disabledReason, () =>
                    {
                        if (owner.GetPendingVote(source, target) == pending) ____forceDecision?.Invoke(pending);
                    }));
                return;
            }
            bool leaving = owner.IsMemberAgainst(source.StringId, target.StringId);
            bool enabled = leaving ? owner.CanProposeDeparture(Clan.PlayerClan, target, out var leaveReason)
                : owner.CanProposeJoin(Clan.PlayerClan, target, out leaveReason);
            var reason = leaveReason;
            if (enabled && !CampaignUIHelper.GetMapScreenActionIsEnabledWithReason(out var blocked))
            { enabled = false; reason = blocked; }
            bool joining = owner.GetMembersAgainst(target.StringId).Count > 0;
            var description = new TextObject(leaving ? "{=BC_DefensivePact_LeaveAction}Leave defensive pact"
                : joining ? "{=BC_DefensivePact_JoinAction}Join defensive pact"
                : "{=BC_DefensivePact_FormAction}Form defensive pact");
            if (DefensivePactVotePreview.TryEvaluate(owner, Clan.PlayerClan, target, leaving, out var support))
                description = new TextObject(leaving ? "{=BC_DefensivePact_LeaveActionSupport}Leave defensive pact ({SUPPORT})"
                    : joining ? "{=BC_DefensivePact_JoinActionSupport}Join defensive pact ({SUPPORT})"
                    : "{=BC_DefensivePact_FormActionSupport}Form defensive pact ({SUPPORT})")
                    .SetTextVariable("SUPPORT", VoluntaryPactPreview.SupportText(support));
            else
                description = new TextObject(leaving ? "{=BC_DefensivePact_LeaveSupportUnavailable}Leave defensive pact (support unavailable)"
                    : joining ? "{=BC_DefensivePact_JoinSupportUnavailable}Join defensive pact (support unavailable)"
                    : "{=BC_DefensivePact_FormSupportUnavailable}Form defensive pact (support unavailable)");
            __instance.Actions.Add(new KingdomDiplomacyProposalActionItemVM(
                new TextObject("{=BC_DefensivePact_ProposeButton}Propose"),
                description,
                DefensivePactVoteRules.InfluenceCost, enabled, reason, () =>
                {
                    bool allowed = leaving ? owner.CanProposeDeparture(Clan.PlayerClan, target, out var failure)
                        : owner.CanProposeJoin(Clan.PlayerClan, target, out failure);
                    if (allowed && (leaving ? owner.TryFileDepartureVote(Clan.PlayerClan, target, ____forceDecision)
                        : owner.TryFileJoinVote(Clan.PlayerClan, target, ____forceDecision))) return;
                    InformationManager.ShowInquiry(new InquiryData(
                        new TextObject("{=BC_DefensivePact_UnavailableTitle}Defensive Pact Unavailable").ToString(),
                        (failure ?? new TextObject("{=BC_DefensivePact_ProposeUnavailable}A defensive pact cannot be proposed under the current circumstances.")).ToString(),
                        true, false, new TextObject("{=BC_DefensivePact_Done}Done").ToString(), "", null, null));
                }));
        }
    }
}
