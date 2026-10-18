using System;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace BellumCivile.Behaviors
{
    public sealed partial class CourtAgendaBehavior
    {
        private InquiryElement RoyalPeaceAction(Kingdom realm)
        {
            var preview = RoyalExecutor?.GetRealmPreview(realm, Clan.PlayerClan);
            bool pending = HasPendingRoyalPeace(realm);
            var hint = pending ? new TextObject("{=BC_RoyalPeaceAlreadyAnnounced}A Royal Peace decree has already been announced.")
                : preview?.Hint ?? new TextObject("{=BC_RoyalPeace_ErrUnavailable}The realm's peace cannot be enforced right now.");
            return new InquiryElement(CourtRoyalPeaceRules.Kind, new TextObject("{=BC_RoyalPeace_Button}Enforce Peace").ToString(),
                null, !pending && preview?.IsEnabled == true, hint.ToString());
        }

        public void ShowPlayerRoyalPeace(Kingdom realm, Action refresh)
        {
            if (!CanUseCrownActions(realm) || HasPendingRoyalPeace(realm)) return;
            var preview = RoyalExecutor?.GetRealmPreview(realm, Clan.PlayerClan);
            if (preview?.IsEnabled != true) return;
            var ruler = realm.Leader;
            var description = new TextObject("{=BC_RealmPeaceConfirm}{DETAIL}\n\nThe decree covers every private dispute in the realm, not just a selected title. It will take effect on {DATE}, subject to safe settlement. No council vote is held. If the price increases or the realm's wartime status changes, it will be cancelled without payment; a new decree must be confirmed.")
                .SetTextVariable("DETAIL", preview.Hint)
                .SetTextVariable("DATE", CampaignTime.Days((float)CourtRoyalPeaceRules.ExecutionDay(CampaignTime.Now.ToDays,
                    BellumCivileOptions.PoliticalDeliberationDays)).ToString());
            InformationManager.ShowInquiry(new InquiryData(new TextObject("{=BC_RoyalPeace_ConfirmTitle}Enforce Peace").ToString(),
                description.ToString(), true, true, new TextObject("{=BC_RoyalPeaceAnnounce}Announce Decree").ToString(),
                new TextObject("{=BC_UI_Cancel}Cancel").ToString(), () => {
                    if (!CanUseCrownActions(realm) || realm.Leader != ruler) return;
                    var current = RoyalExecutor?.GetRealmPreview(realm, Clan.PlayerClan);
                    string report = null;
                    if (current?.IsEnabled != true
                        || !CourtRoyalPeaceRules.QuoteStillValid(preview.Wartime, preview.InfluenceCost, current.Wartime, current.InfluenceCost)
                        || !TryScheduleRoyalPeace(realm, Clan.PlayerClan, out report))
                        BellumCivileNotifications.ShowPersonal(new TextObject("{=BC_CrownActionChanged}This undertaking can no longer proceed. Its conditions or cost have changed."), BellumNotificationColors.Warning);
                    refresh?.Invoke();
                }, null), true);
        }
    }
}
