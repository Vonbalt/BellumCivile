using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace BellumCivile.Behaviors
{
    public sealed partial class CourtAgendaBehavior
    {
        private bool PlayerTitlePetition(CourtAgendaRecord a) => TitleAnnounced(a) && a.IsOngoingObjective && a.Faction != null
            && a.TitleGrant.Grantor == Clan.PlayerClan && GrantIdentity(a.TitleGrant) && GrantOwner(a)
            && CampaignTime.Now.ToDays <= a.ObjectiveData.DeadlineDay;
        internal bool CanReviewTitlePetition(FactionObject faction) => _agendas.Any(a => a.Faction == faction && PlayerTitlePetition(a)
            && (a.TitleGrant.Response == CourtTitleResponse.Deferred || a.TitleGrant.Response == CourtTitleResponse.Refused));
        internal void RequestTitlePetitionReview(FactionObject faction)
        {
            var a = _agendas.FirstOrDefault(x => x.Faction == faction && PlayerTitlePetition(x)
                && (x.TitleGrant.Response == CourtTitleResponse.Deferred || x.TitleGrant.Response == CourtTitleResponse.Refused));
            if (a != null) a.TitleGrant.Response = CourtTitleResponse.AwaitingPlayer;
        }
        private bool ProcessTitlePetitionInquiry()
        {
            var a = _agendas.FirstOrDefault(x => PlayerTitlePetition(x) && x.TitleGrant.Response == CourtTitleResponse.AwaitingPlayer);
            if (a == null) return false;
            var p = a.TitleGrant;
            if (!GrantUnchanged(p)) return false;
            bool afford = NpcInfluenceBudgetService.CanAfford(p.Grantor, FeudalTitlePlayerActionService.GrantInfluenceCost, NpcInfluenceExpenseKind.Discretionary);
            string rights = new TextObject(p.Legal && p.Practical ? "{=BC_TitleGrantBothRights}Legal and practical rights"
                : p.Legal ? "{=BC_TitleGrantLegalRights}Legal rights only" : "{=BC_TitleGrantPracticalRights}Practical rights only").ToString();
            var body = TitleText(p, new TextObject("{=BC_TitleGrantInquiry}The nobility asks you to bestow {TITLE} upon {RECIPIENT}, recognizing that house's claim. Will you grant their petition?\n\nRights: {RIGHTS}\nCost: 100 influence\nPersonal goodwill: +{RELATION}\nRecipient faction approval: +{APPROVAL}\n\nThe house remains your vassal. No subordinate settlement changes owner. A grant fulfills the petition without a second approval reward. You may reconsider before {DATE}; leaving the petition unfulfilled brings -10 approval."))
                .SetTextVariable("RIGHTS", rights).SetTextVariable("RELATION", p.RelationGain).SetTextVariable("APPROVAL", p.RelationGain * .5f);
            var choices = new System.Collections.Generic.List<InquiryElement>
            {
                new InquiryElement("grant", new TextObject("{=BC_TitleGrantAccept}Grant the title").ToString(), null, afford,
                    afford ? "" : new TextObject("{=BC_TitleGrantNoInfluence}You need 100 influence to grant this title.").ToString()),
                new InquiryElement("refuse", new TextObject("{=BC_TitleGrantRefuse}Refuse the petition").ToString(), null),
                new InquiryElement("defer", new TextObject("{=BC_TitleGrantDefer}Consider it later").ToString(), null)
            };
            _activePlayerInquiry = a;
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(new TextObject("{=BC_TitleGrantInquiryTitle}A Petition for Royal Favor").ToString(),
                body.ToString(), choices, false, 1, 1, new TextObject("{=BC_TitleGrantConfirm}Confirm response").ToString(), null, selected =>
                {
                    if (_activePlayerInquiry != a) return;
                    _activePlayerInquiry = null;
                    if (!_agendas.Contains(a) || !PlayerTitlePetition(a) || a.TitleGrant != p || p.Response != CourtTitleResponse.AwaitingPlayer || !GrantUnchanged(p)) return;
                    if (selected == null || selected.Count != 1) return;
                    string answer = selected[0]?.Identifier as string;
                    if (answer == "grant")
                    {
                        if (!NpcInfluenceBudgetService.CanAfford(p.Grantor, FeudalTitlePlayerActionService.GrantInfluenceCost, NpcInfluenceExpenseKind.Discretionary)) return;
                        AcceptTitleGrant(a);
                    }
                    else p.Response = answer == "refuse" ? CourtTitleResponse.Refused : CourtTitleResponse.Deferred;
                }, null), true);
            return true;
        }
    }
}
