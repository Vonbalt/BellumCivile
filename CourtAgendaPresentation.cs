using TaleWorlds.CampaignSystem;

namespace BellumCivile
{
    internal static class CourtAgendaPresentation
    {
        internal static (string text, CampaignTime date)? Deadline(CourtAgendaRecord agenda)
        {
            if (agenda == null) return null;
            if (agenda.ObjectiveData?.Kind == CourtRoyalPeaceRules.Kind && agenda.IsUnopened)
                return ("{=BC_RoyalPeaceDate}Peace to be imposed {DATE}", agenda.SessionDate);
            if (agenda.IsOngoingObjective && agenda.Protection != null)
                return agenda.Protection.Phase == CourtProtectionPhase.Accepted
                    ? ("{=BC_ProtectionRecoveryDate}Agreement being carried out by {DATE}", CampaignTime.Days((float)agenda.Protection.RecoveryUntil))
                    : ("{=BC_ProtectionReplyDate}Reply expected by {DATE}", CampaignTime.Days((float)agenda.Protection.ReplyDeadline));
            if (agenda.IsOngoingObjective && agenda.ObjectiveData?.HasTermSnapshot == true)
                return (agenda.Manual ? "{=BC_CrownObjectiveDeadline}Undertaking ends {DATE}" : "{=BC_CourtObjectiveDeadline}Term ends {DATE}", CampaignTime.Days((float)agenda.ObjectiveData.DeadlineDay));
            if (agenda.State == CourtAgendaState.AwaitingNomination)
                return ("{=BC_AgendaDeadlineNomination}Nomination closes {DATE}", agenda.NominationDeadline);
            if (agenda.State == CourtAgendaState.Crisis || (agenda.IsUnopened && agenda.CrisisInterventionPending))
                return ("{=BC_AgendaDeadlineCrisis}Challenge considered {DATE}", agenda.SessionDate);
            if (agenda.State == CourtAgendaState.AwaitingPlayerDecision)
                return ("{=BC_AgendaDeadlineSession}Court session: {DATE}", agenda.SessionDate);
            if (agenda.State != CourtAgendaState.Announced && agenda.State != CourtAgendaState.Deliberating)
                return null;
            if (agenda.ObjectiveData?.Kind == CourtClientGrantRules.Kind || agenda.ObjectiveData?.Kind == CourtActivityRules.Kind || agenda.ObjectiveData?.Kind == CourtAppeasementRules.Kind
                || agenda.ObjectiveData?.Kind == CourtPeaceRules.Kind || agenda.ObjectiveData?.Kind == CourtCampaignRules.Kind
                || agenda.ObjectiveData?.Kind == CourtSubjugationRules.Kind || agenda.ObjectiveData?.Kind == CourtClaimRules.Kind
                || agenda.ObjectiveData?.Kind == CourtDynasticRules.Kind || agenda.ObjectiveData?.Kind == CourtProtectionRules.Kind || agenda.ObjectiveData?.Kind == CourtTradeRules.Kind
                || agenda.ObjectiveData?.Kind == CourtLiberationRules.Kind || agenda.ObjectiveData?.Kind == CourtRallyRules.Kind || agenda.ObjectiveData?.Kind == CourtTitleGrantRules.Grant || agenda.ObjectiveData?.Kind == CourtTitleGrantRules.Petition)
                return ("{=BC_CourtActivityDate}Scheduled for {DATE}", agenda.SessionDate);
            if (agenda.ObjectiveData?.Kind == CourtExecutiveRules.Decree)
                return ("{=BC_AgendaDeadlineJudgment}Royal judgment: {DATE}", agenda.SessionDate);
            if (!agenda.HasScheduleSnapshot)
                return ("{=BC_AgendaDeadlineSession}Court session: {DATE}", agenda.SessionDate);
            return ("{=BC_AgendaDeadlineVote}Vote scheduled for {DATE}",
                agenda.AllocationStage ? agenda.AllocationVoteDate : agenda.VoteDate);
        }

        internal static string Color(CourtAgendaState? state) => state == CourtAgendaState.Passed || state == CourtAgendaState.Completed ? "#82E06AFF"
            : state == CourtAgendaState.Defeated ? "#FF6B6BFF" : "#F1D8A4FF";

        internal static string Status(CourtAgendaState? state, bool crisisPending = false, bool campaignObjective = false, bool subjugationObjective = false, bool claimObjective = false, bool dynasticObjective = false, bool protectionObjective = false, bool tradeObjective = false, bool titleObjective = false, bool rallyObjective = false, bool liberationObjective = false)
        {
            if (crisisPending) return "{=BC_AgendaShortDecision}awaiting decision";
            if (state == CourtAgendaState.PursuingObjective && liberationObjective) return "{=BC_CourtLiberationShort}preparing for liberation";
            if (state == CourtAgendaState.PursuingObjective && rallyObjective) return "{=BC_RallyShortStatus}seeking victory";
            if (state == CourtAgendaState.PursuingObjective && tradeObjective) return "{=BC_CourtTradeShortStatus}seeking a trade accord";
            if (state == CourtAgendaState.PursuingObjective && titleObjective) return "{=BC_TitleGrantShortStatus}awaiting a royal grant";
            switch (state)
            {
                case CourtAgendaState.PursuingObjective: return protectionObjective ? "{=BC_ProtectionShortStatus}seeking protection" : dynasticObjective ? "{=BC_AgendaShortDynastic}seeking a royal match" : claimObjective ? "{=BC_AgendaShortClaim}upholding a claim" : subjugationObjective ? "{=BC_AgendaShortClientage}seeking submission" : campaignObjective
                    ? "{=BC_AgendaShortCampaign}pressing for a campaign" : "{=BC_AgendaShortPursuing}pursuing settlement";
                case CourtAgendaState.Completed: return "{=BC_AgendaShortCompleted}completed";
                case CourtAgendaState.Decreed: return "{=BC_AgendaShortDecreed}decree issued";
                case CourtAgendaState.Passed: return "{=BC_AgendaShortPassed}passed";
                case CourtAgendaState.Defeated: return "{=BC_AgendaShortRejected}rejected";
                case CourtAgendaState.Deliberating: return "{=BC_AgendaShortDeliberating}deliberating";
                case CourtAgendaState.Voting: return "{=BC_AgendaShortVoting}voting";
                case CourtAgendaState.Announced: return "{=BC_AgendaShortScheduled}scheduled";
                case CourtAgendaState.AwaitingPlayerDecision: return "{=BC_AgendaShortDecision}awaiting decision";
                case CourtAgendaState.AwaitingNomination: return "{=BC_AgendaShortNomination}awaiting nomination";
                case CourtAgendaState.Cancelled: return "{=BC_AgendaShortCancelled}cancelled";
                case CourtAgendaState.FulfilledElsewhere: return "{=BC_AgendaShortFulfilled}fulfilled elsewhere";
                case CourtAgendaState.Crisis: return "{=BC_AgendaShortCrisis}crisis";
                case CourtAgendaState.Withdrawn: return "{=BC_AgendaShortWithdrawn}withdrawn";
                case CourtAgendaState.TooWeak: return "{=BC_AgendaShortWeak}insufficient backing";
                case CourtAgendaState.Ultimatum: return "{=BC_AgendaShortUltimatum}ultimatum issued";
                case CourtAgendaState.Blocked: return "{=BC_AgendaShortBlocked}blocked";
                case CourtAgendaState.NominationExpired: return "{=BC_AgendaShortExpired}nomination expired";
                default: return "{=BC_AgendaShortNextTerm}awaiting next term";
            }
        }
    }
}
