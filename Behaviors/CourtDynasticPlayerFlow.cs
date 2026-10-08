using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace BellumCivile.Behaviors
{
    public sealed partial class CourtAgendaBehavior
    {
        private bool PlayerDynasticAgenda(CourtAgendaRecord a) => _agendas.Contains(a) && DynasticUnfinished(a) && a.IsOngoingObjective
            && a.Dynastic.OurHouse == Clan.PlayerClan && DynasticOwnerValid(a) && DynasticWindow(a);

        internal bool CanReviewDynastic(FactionObject faction) => _agendas.Any(a => a.Faction == faction && PlayerDynasticAgenda(a)
            && (a.Dynastic.Response == CourtDynasticResponse.Deferred || a.Dynastic.Response == CourtDynasticResponse.AwaitingApproach));

        internal void RequestDynasticReview(FactionObject faction)
        {
            var a = _agendas.FirstOrDefault(x => x.Faction == faction && PlayerDynasticAgenda(x)
                && x.Dynastic.Response == CourtDynasticResponse.Deferred);
            if (a != null) a.Dynastic.Response = CourtDynasticResponse.AwaitingApproach;
        }

        private bool ProcessDynasticInquiry()
        {
            var a = _agendas.FirstOrDefault(x => PlayerDynasticAgenda(x)
                && (x.Dynastic.Response == CourtDynasticResponse.AwaitingApproach || x.Dynastic.Response == CourtDynasticResponse.ReplyReady));
            if (a == null) return false;
            var plan = a.Dynastic;
            var response = plan.Response;
            var service = Campaign.Current.GetCampaignBehavior<StrategicMarriageBehavior>();
            if (response == CourtDynasticResponse.ReplyReady && service?.CanSendCourtMarriageOffer != true) return false;
            if (response == CourtDynasticResponse.ReplyReady
                && (!BellumMarriageStrategyHelper.CourtMarriageParticipant(plan.First)
                    || !BellumMarriageStrategyHelper.CourtMarriageParticipant(plan.Second))) return false;
            _activePlayerInquiry = a;
            if (response == CourtDynasticResponse.AwaitingApproach)
            {
                var options = new[] {
                    new InquiryElement(0, new TextObject("{=BC_CourtDynasticSend}Send word proposing the match.").ToString(), null),
                    new InquiryElement(1, new TextObject("{=BC_CourtDynasticConsider}I will consider it.").ToString(), null),
                    new InquiryElement(2, new TextObject("{=BC_CourtDynasticDecline}I will not pursue this match.").ToString(), null) };
                MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                    new TextObject("{=BC_CourtDynasticTitle}A Bond Between Crowns").ToString(),
                    DynasticText(a, new TextObject("{=BC_CourtDynasticApproach}The lords of the {FACTION} urge you to strengthen ties with {TARGET} through a marriage between {FIRST} and {SECOND}. They believe this union would serve both houses and will lend their voices to an alliance with that realm during this term.\n\nThe couple would make their household with {HOUSE}. Shall we approach {RULER} with this proposal?")).ToString(),
                    options.ToList(), false, 1, 1, new TextObject("{=BC_CourtConfirmAgenda}Confirm").ToString(), null,
                    selected => {
                        if (_activePlayerInquiry != a) return;
                        _activePlayerInquiry = null;
                        if (!PlayerDynasticAgenda(a) || a.Dynastic != plan || plan.Response != response || selected?.Count != 1) return;
                        int choice = (int)selected[0].Identifier;
                        plan.Response = choice == 0 ? CourtDynasticResponse.Approaching : choice == 1 ? CourtDynasticResponse.Deferred : CourtDynasticResponse.Declined;
                        if (choice == 0) plan.ReplyDay = CampaignTime.Now.ToDays + 1;
                    }, null), true);
            }
            else
            {
                InformationManager.ShowInquiry(new InquiryData(new TextObject("{=BC_CourtDynasticReplyTitle}An Answer to Your Proposal").ToString(),
                    DynasticText(a, new TextObject("{=BC_CourtDynasticReply}{RULER} has answered your approach favorably and is willing to agree to the marriage of {FIRST} and {SECOND}. The couple would join {HOUSE}; no marriage payment is requested in this offer.\n\nYou may review the formal marriage offer before giving your final consent. An alliance between the realms remains a separate decision.")).ToString(),
                    true, true, new TextObject("{=BC_CourtDynasticReviewOffer}Review the marriage offer").ToString(),
                    new TextObject("{=BC_CourtDynasticDecline}I will not pursue this match.").ToString(), () => {
                        if (_activePlayerInquiry != a) return;
                        _activePlayerInquiry = null;
                        if (!PlayerDynasticAgenda(a) || a.Dynastic != plan || plan.Response != response) return;
                        if (!BellumMarriageStrategyHelper.CourtMarriageParticipant(plan.First)
                            || !BellumMarriageStrategyHelper.CourtMarriageParticipant(plan.Second)) return;
                        var match = BellumMarriageStrategyHelper.EvaluateCourtMarriage(plan.First, plan.Second, true, plan.Destination);
                        if (match == null)
                        {
                            plan.Response = CourtDynasticResponse.ForeignRefused;
                            ReportDynastic(a, new TextObject("{=BC_CourtDynasticRefused}{RULER} has declined the proposed match between {FIRST} and {SECOND}. The hoped-for union has not been agreed."));
                            return;
                        }
                        // Seal before the native offer publishes notifications or callbacks.
                        plan.Response = CourtDynasticResponse.OfferIssued;
                        if (service?.TrySendCourtMarriageOffer(match) != true) plan.Response = CourtDynasticResponse.ReplyReady;
                    }, () => {
                        if (_activePlayerInquiry != a) return;
                        _activePlayerInquiry = null;
                        if (PlayerDynasticAgenda(a) && a.Dynastic == plan && plan.Response == response) plan.Response = CourtDynasticResponse.Declined;
                    }), true);
            }
            return true;
        }

        private void OnCourtMarriageOfferCancelled(Hero first, Hero second)
        {
            foreach (var a in _agendas.Where(a => DynasticUnfinished(a) && a.Dynastic?.Matches(first, second) == true
                && a.Dynastic.Response == CourtDynasticResponse.OfferIssued))
            {
                a.Dynastic.Response = CourtDynasticResponse.Declined;
                a.Dynastic.MarriageOutcome = "native_offer_cancelled";
                BellumCivileLogger.Log($"Court royal marriage offer cancelled; realm={a.Realm.StringId}; target={a.Dynastic.Target.StringId}.");
            }
        }
    }
}
