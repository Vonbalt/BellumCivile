using TaleWorlds.CampaignSystem;
using TaleWorlds.SaveSystem;

namespace BellumCivile
{
    public enum CourtAgendaState
    {
        Announced, Deliberating, Voting, Passed, Defeated, NotProposed,
        Cancelled, FulfilledElsewhere, Crisis, Withdrawn, TooWeak, Ultimatum,
        AwaitingPlayerDecision, AwaitingNomination, Blocked, NominationExpired, Decreed, Completed, PursuingObjective
    }

    public sealed class CourtAgendaRecord
    {
        [SaveableField(1)] public Kingdom Realm;
        [SaveableField(2)] public FactionObject Faction;
        [SaveableField(3)] public Clan Sponsor;
        [SaveableField(4)] public string PolicyId;
        [SaveableField(5)] public bool Abolish;
        [SaveableField(6)] public CourtAgendaState State;
        [SaveableField(7)] public CampaignTime SessionDate;
        [SaveableField(8)] public int PaidInfluence;
        [SaveableField(9)] public bool PaymentSettled;
        [SaveableField(10)] public bool Manual;
        [SaveableField(11)] public bool ResultApplied;
        [SaveableField(12)] public bool EventApplied;
        [SaveableField(13)] public CampaignTime ResultVisibleUntil;
        [SaveableField(14)] public CampaignTime NominationDeadline;
        [SaveableField(15)] public int NominationWindowDays;
        [SaveableField(16)] public bool NominationReminderSent;
        [SaveableField(17)] public bool CrisisInterventionPending;
        [SaveableField(18)] public bool OrdinaryCrisisRestrained;
        [SaveableField(19)] public bool HasScheduleSnapshot;
        [SaveableField(20)] public float TermDays;
        [SaveableField(21)] public CampaignTime VoteDate;
        [SaveableField(22)] public string CancellationReason;
        [SaveableField(23)] public CourtObjectiveRecord ObjectiveData;
        [SaveableField(24)] public Clan OriginalHolder;
        [SaveableField(25)] public bool AllocationStage;
        [SaveableField(26)] public CampaignTime AllocationVoteDate;
        [SaveableField(27)] public int ActionAttempts;
        [SaveableField(28)] public string DecreeCaseId;
        [SaveableField(29)] public Clan PreferredCouncilCandidate;
        [SaveableField(30)] public string CouncilMotionId;
        [SaveableField(31)] public CourtActivityRecord Activity;
        [SaveableField(32)] public bool PlayerSelectionConfirmed;
        [SaveableField(33)] public int SubstitutionInfluencePaid;
        [SaveableField(34)] public CourtAppeasementRecord Appeasement;
        [SaveableField(35)] public CourtPeaceRecord Peace;
        [SaveableField(36)] public CourtCampaignRecord Campaign;
        [SaveableField(37)] public CourtSubjugationRecord Subjugation;
        [SaveableField(38)] public CourtClaimRecord Claim;
        [SaveableField(39)] public CourtDynasticRecord Dynastic;
        [SaveableField(40)] public CourtProtectionRecord Protection;
        [SaveableField(41)] public CourtTradeRecord Trade;
        [SaveableField(42)] public CourtTitleGrantRecord TitleGrant;
        [SaveableField(43)] public CourtRallyRecord Rally;
        [SaveableField(44)] public CourtMandateRecord Mandate;
        [SaveableField(45)] public CourtLiberationRecord Liberation;
        [SaveableField(46)] public string NominationKind;
        [SaveableField(47)] public CourtClientGrantRecord ClientGrant;
        [SaveableField(48)] public bool HasPolicyStanceSnapshot;
        [SaveableField(49)] public int SelectedPolicyStance;
        // Zero denotes legacy records; new payments store expense kind plus one.
        [SaveableField(50)] public int PaymentExpenseCode;
        [SaveableField(51)] internal string IntegrationId = System.Guid.NewGuid().ToString("N");

        internal void CapturePolicyStance()
        {
            if (HasPolicyStanceSnapshot || Faction == null || string.IsNullOrEmpty(PolicyId) || !IsPolicy) return;
            var config = IdeologyPolicyAgendaConfig.Instance;
            SelectedPolicyStance = (int)CourtPolicyStanceRules.Resolve(config.GetStance(Faction.Type, PolicyId),
                config.IsCrownPolicy(PolicyId), Faction.Mood);
            HasPolicyStanceSnapshot = true;
        }

        internal bool HasLostPolicyMandate(CourtPolicyStance current) =>
            Faction != null && IdeologyPolicyAgendaConfig.Instance.IsCrownPolicy(PolicyId)
            && CourtPolicyStanceRules.LostMandate(HasPolicyStanceSnapshot,
                (CourtPolicyStance)SelectedPolicyStance, current, Abolish);

        public bool IsPolicy => ObjectiveData == null || ObjectiveData.Kind == "policy";

        public CourtObjectiveRecord GetObjective() => CourtPolicyObjectiveBridge.Refresh(this);

        public int SettleCancellation(string reason)
        {
            if (PaymentSettled || ResultApplied) return 0;
            int refund = IsFiled && PolicyRefundRules.IsTechnical(reason) ? System.Math.Max(0, PaidInfluence) : 0;
            State = CourtAgendaState.Cancelled;
            CrisisInterventionPending = false;
            PaymentSettled = true;
            CancellationReason = reason;
            return refund;
        }

        public void FreezeSchedule(float termDays, int deliberationDays, CampaignTime? existingVoteDate = null)
        {
            if (HasScheduleSnapshot) return;
            TermDays = termDays;
            VoteDate = existingVoteDate ?? SessionDate + CampaignTime.Days(deliberationDays);
            HasScheduleSnapshot = true;
        }

        public bool IsFiled => State == CourtAgendaState.Deliberating || State == CourtAgendaState.Voting;
        public bool IsOngoingObjective => State == CourtAgendaState.PursuingObjective;
        public bool IsUnopened => State == CourtAgendaState.Announced || State == CourtAgendaState.Crisis
            || State == CourtAgendaState.AwaitingPlayerDecision || State == CourtAgendaState.AwaitingNomination;
    }
}
