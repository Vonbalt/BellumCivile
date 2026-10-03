using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace BellumCivile.Behaviors
{
    public sealed partial class CourtAgendaBehavior
    {
        private List<ClaimFeudRecord> PlayerRoyalPeaceFeuds(Kingdom realm) =>
            Campaign.Current?.GetCampaignBehavior<ClaimFeudBehavior>()?.GetActiveFeuds()
                .Where(f => RealmPeaceEnforcementBehavior.ResolveKingdomForFeud(f) == realm).ToList()
                ?? new List<ClaimFeudRecord>();

        private InquiryElement RoyalPeaceAction(Kingdom realm)
        {
            var behavior = Campaign.Current?.GetCampaignBehavior<RealmPeaceEnforcementBehavior>();
            var previews = PlayerRoyalPeaceFeuds(realm).Select(f => behavior?.GetClaimFeudPreview(f, Clan.PlayerClan)).ToList();
            bool enabled = previews.Any(p => p?.IsEnabled == true);
            var hint = enabled ? new TextObject("{=BC_CrownPeaceHint}Choose a feud to end by royal decree. The existing influence cost, resentment and Tyrant's Debt apply immediately; no council vote is held.")
                : previews.FirstOrDefault(p => p != null)?.Hint
                    ?? new TextObject("{=BC_CrownPeaceNoFeuds}There are no active feuds in this realm to suppress.");
            return new InquiryElement(CourtRoyalPeaceRules.Kind, new TextObject("{=BC_RoyalPeace_Button}Enforce Peace").ToString(), null, enabled, hint.ToString());
        }

        private static TextObject RoyalPeaceFeudLabel(ClaimFeudRecord feud)
        {
            var claimant = Clan.All.FirstOrDefault(c => c.StringId == feud.ClaimantClanId);
            var holder = Clan.All.FirstOrDefault(c => c.StringId == feud.HolderClanId);
            var title = FeudalTitleBehavior.Instance?.GetTitle(feud.TargetTitleId);
            return new TextObject("{=BC_CrownPeaceFeud}{CLAIMANT} against {HOLDER}: {TITLE}")
                .SetTextVariable("CLAIMANT", claimant?.Name ?? new TextObject("?"))
                .SetTextVariable("HOLDER", holder?.Name ?? new TextObject("?"))
                .SetTextVariable("TITLE", title == null ? new TextObject("{=BC_RoyalPeaceDispute}the disputed title")
                    : new TextObject("{=!}" + FeudalTitleDisplayHelper.FormatTitleName(title, claimant ?? holder)));
        }

        private void ShowPlayerRoyalPeace(Kingdom realm, Action refresh)
        {
            if (!CanUseCrownActions(realm)) return;
            var behavior = Campaign.Current?.GetCampaignBehavior<RealmPeaceEnforcementBehavior>();
            var choices = PlayerRoyalPeaceFeuds(realm).Select(f => {
                var preview = behavior?.GetClaimFeudPreview(f, Clan.PlayerClan);
                return new InquiryElement(f, RoyalPeaceFeudLabel(f).ToString(), null, preview?.IsEnabled == true,
                    preview?.Hint.ToString() ?? new TextObject("{=BC_RoyalPeace_ErrUnavailable}The realm's peace cannot be enforced right now.").ToString());
            }).ToList();
            choices.Add(new InquiryElement("back", new TextObject("{=BC_CourtPickerBack}Back").ToString(), null));
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                new TextObject("{=BC_RoyalPeace_Button}Enforce Peace").ToString(),
                new TextObject("{=BC_CrownPeaceChoose}Which feuding houses will you command to lay aside their dispute?").ToString(), choices,
                true, 1, 1, new TextObject("{=BC_CourtPickerContinue}Continue").ToString(),
                new TextObject("{=BC_UI_Cancel}Cancel").ToString(), selected => {
                    if (selected?.Count != 1 || !CanUseCrownActions(realm)) return;
                    if (selected[0].Identifier is ClaimFeudRecord feud) ConfirmPlayerRoyalPeace(realm, feud, refresh);
                    else ShowCrownActions(realm, refresh);
                }, null), true);
        }

        private void ConfirmPlayerRoyalPeace(Kingdom realm, ClaimFeudRecord feud, Action refresh)
        {
            var behavior = Campaign.Current?.GetCampaignBehavior<RealmPeaceEnforcementBehavior>();
            var preview = behavior?.GetClaimFeudPreview(feud, Clan.PlayerClan);
            if (!CanUseCrownActions(realm) || preview?.IsEnabled != true || preview.Kingdom != realm) return;
            var description = new TextObject("{=BC_RoyalPeace_ConfirmDesc}Enforcing the {PEACE_NAME} will cost {COST} influence, anger involved clans, and reduce the crown's influence gains for one year. Proceed?")
                .SetTextVariable("PEACE_NAME", new TextObject("{=!}" + FeudalTitleDisplayHelper.FormatRulerPeaceName(realm)))
                .SetTextVariable("COST", preview.InfluenceCost);
            InformationManager.ShowInquiry(new InquiryData(new TextObject("{=BC_RoyalPeace_ConfirmTitle}Enforce Peace").ToString(),
                RoyalPeaceFeudLabel(feud) + "\n\n" + description, true, true,
                new TextObject("{=BC_RoyalPeace_Button}Enforce Peace").ToString(), new TextObject("{=BC_CourtPickerBack}Back").ToString(),
                () => {
                    if (!CanUseCrownActions(realm)) return;
                    string report = null;
                    var current = Campaign.Current?.GetCampaignBehavior<RealmPeaceEnforcementBehavior>();
                    if (current == null || RealmPeaceEnforcementBehavior.ResolveKingdomForFeud(feud) != realm
                        || !current.TryEnforceClaimFeudPeace(feud, Clan.PlayerClan, out report))
                        BellumCivileNotifications.ShowPersonal(new TextObject("{=BC_RoyalPeace_Failed}The realm's peace could not be enforced: {REASON}")
                            .SetTextVariable("REASON", report ?? "?"), BellumNotificationColors.Danger);
                    refresh?.Invoke();
                }, () => ShowPlayerRoyalPeace(realm, refresh)), true);
        }
    }
}
