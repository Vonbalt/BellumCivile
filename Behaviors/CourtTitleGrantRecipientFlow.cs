using System;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace BellumCivile.Behaviors
{
    public sealed partial class CourtAgendaBehavior
    {
        private const int TitleOfferRefusalRelation = -10;
        private const int TitleOfferRefusalYears = 2;

        private static bool SameTitleOffer(CourtTitleGrantRecord first, CourtTitleGrantRecord second) => first != null && second != null
            && first.Realm == second.Realm && first.TitleId == second.TitleId && first.Recipient == second.Recipient
            && first.Grantor == second.Grantor && first.Ruler == second.Ruler && first.Beneficiary == second.Beneficiary
            && first.OldLegal == second.OldLegal && first.OldPractical == second.OldPractical
            && first.Legal == second.Legal && first.Practical == second.Practical;

        private static bool TitleOfferNeedsConsent(CourtTitleGrantRecord p) => p?.Recipient != null && p.Recipient == Clan.PlayerClan
            && !p.RecipientAccepted && !p.RecipientRefused && !p.PaymentAttempted && !p.TransferAttempted && !p.TransferComplete;

        private bool ValidPlayerTitleOffer(CourtAgendaRecord a, CourtTitleGrantRecord p) => a != null && p != null
            && _agendas.Contains(a) && a.TitleGrant == p && TitleAnnounced(a) && a.IsOngoingObjective
            && p.Response == CourtTitleResponse.Accepted && _titleDeliveries.Contains(p) && TitleOfferNeedsConsent(p)
            && GrantIdentity(p) && GrantOwner(a) && GrantUnchanged(p) && !GrantDelivered(p)
            && CampaignTime.Now.ToDays <= p.Deadline && CampaignTime.Now.ToDays <= a.ObjectiveData.DeadlineDay;

        private static TextObject TitleOfferText(CourtTitleGrantRecord p)
        {
            var rights = new TextObject(p.Legal && p.Practical
                ? "{=BC_TitleGrantOfferBothRights}The grant conveys both lawful title and practical authority."
                : p.Legal ? "{=BC_TitleGrantOfferLegalRights}The grant conveys lawful title only; practical authority remains unchanged."
                : "{=BC_TitleGrantOfferPracticalRights}The grant conveys practical authority only; lawful ownership remains unchanged.");
            return TitleText(p, new TextObject("{=BC_TitleGrantOfferBody}An envoy from {RULER} brings word that the Crown wishes to bestow {TITLE} upon your household, honoring your house's claim.\n\n{RIGHTS}\nYour household will remain a vassal of {REALM}. This grant does not include its subordinate towns or castles.\n\nRefusing this honor will offend {RULER}. (Relations: {PENALTY})"))
                .SetTextVariable("RIGHTS", rights).SetTextVariable("PENALTY", TitleOfferRefusalRelation);
        }

        private bool ProcessTitleGrantOfferInquiry()
        {
            var a = _agendas.FirstOrDefault(x => ValidPlayerTitleOffer(x, x.TitleGrant)
                && NpcInfluenceBudgetService.CanAfford(x.TitleGrant.Grantor, FeudalTitlePlayerActionService.GrantInfluenceCost, NpcInfluenceExpenseKind.Discretionary));
            if (a == null) return false;
            var p = a.TitleGrant;
            _activePlayerInquiry = a;
            InformationManager.ShowInquiry(new InquiryData(new TextObject("{=BC_TitleGrantOfferTitle}A Royal Honor").ToString(),
                TitleOfferText(p).ToString(), true, true,
                new TextObject("{=BC_TitleGrantOfferAccept}Accept the honor").ToString(),
                new TextObject("{=BC_TitleGrantOfferRefuse}Refuse").ToString(),
                () => AnswerTitleGrantOffer(a, p, true), () => AnswerTitleGrantOffer(a, p, false)), true);
            return true;
        }

        private void AnswerTitleGrantOffer(CourtAgendaRecord a, CourtTitleGrantRecord p, bool accept)
        {
            if (_activePlayerInquiry != a) return;
            _activePlayerInquiry = null;
            if (!ValidPlayerTitleOffer(a, p))
            {
                BellumCivileNotifications.ShowPersonal(new TextObject("{=BC_TitleGrantOfferUnavailable}The Crown's offer is no longer available. No response has been delivered."), BellumNotificationColors.Warning);
                MaintainTitleGrants();
                return;
            }
            if (accept)
            {
                p.RecipientAccepted = true;
                MaintainTitleGrants();
                return;
            }

            // Withdraw every matching authorization before applying the personal aftermath.
            // A recipient's refusal is not the Crown neglecting a nobility petition.
            p.RecipientRefused = true;
            foreach (var pending in _titleDeliveries.Where(x => SameTitleOffer(x, p)
                && !x.PaymentAttempted && !x.TransferAttempted && !x.TransferComplete).ToList())
            {
                pending.RecipientRefused = true;
                pending.Response = CourtTitleResponse.Refused;
            }
            foreach (var agenda in _agendas.Where(x => TitleOpen(x) && SameTitleOffer(x.TitleGrant, p)
                && !x.TitleGrant.PaymentAttempted && !x.TitleGrant.TransferAttempted && !x.TitleGrant.TransferComplete).ToList())
            {
                agenda.TitleGrant.RecipientRefused = true;
                agenda.TitleGrant.Response = CourtTitleResponse.Refused;
                FinishTitleAgenda(agenda, CourtObjectiveState.Cancelled, "recipient_refused");
            }
            try
            {
                if (!p.RefusalRelationApplied)
                {
                    p.RefusalRelationApplied = true;
                    RelationMemoryService.ApplyChange(p.Ruler, p.Beneficiary, TitleOfferRefusalRelation, true,
                        RelationMemorySources.RefusedCourtTitleGrant, TitleOfferRefusalYears, RelationMemoryScope.Personal, TitleGrantName(p.TitleId).ToString());
                }
                if (!p.Reported)
                {
                    p.Reported = true;
                    BellumCivileNotifications.ShowPersonal(TitleText(p, new TextObject("{=BC_TitleGrantOfferDeclined}You have declined {RULER}'s offer of {TITLE}. The grant has been withdrawn.")), BellumNotificationColors.Politics);
                }
            }
            catch (Exception ex)
            {
                BellumCivileLogger.Log("Title offer refusal aftermath interrupted; title=" + p.TitleId + "; " + ex);
            }
        }
    }
}
