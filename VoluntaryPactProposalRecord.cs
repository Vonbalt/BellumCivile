using System;
using TaleWorlds.CampaignSystem;
using TaleWorlds.SaveSystem;

namespace BellumCivile
{
    internal enum VoluntaryPactCloseReason { Invalid = 0, SourceRejected = 1, TargetRejected = 2, TargetDeclined = 3 }

    public enum VoluntaryPactProposalPhase
    {
        Draft = 0,
        SourceVote = 1,
        TargetChoice = 2,
        TargetVote = 3,
        Delivery = 4,
        Completed = 5,
        Cancelled = 6
    }

    // Saved separately from the active pact: votes and transfers can outlive a UI session.
    public sealed class VoluntaryPactProposalRecord
    {
        [SaveableField(1)] public string Id;
        [SaveableField(2)] public Kingdom Source;
        [SaveableField(3)] public Kingdom Target;
        [SaveableField(4)] public Clan SourceHouse;
        [SaveableField(5)] public Clan TargetHouse;
        [SaveableField(6)] public Clan Sponsor;
        [SaveableField(7)] public Hero SourceHostage;
        [SaveableField(8)] public Hero TargetHostage;
        [SaveableField(9)] public int DurationDays;
        [SaveableField(10)] public int BaselineDays;
        [SaveableField(11)] public int Compensation;
        [SaveableField(12)] public VoluntaryPactProposalPhase Phase;
        [SaveableField(13)] public Hero Payer;
        [SaveableField(14)] public Hero Payee;
        [SaveableField(15)] public bool GoldDebited;
        [SaveableField(16)] public bool GoldCredited;
        [SaveableField(17)] public bool TradeEventStarted;
        [SaveableField(18)] public string PactId;
        [SaveableField(19)] public double CreatedDay;
        [SaveableField(20)] public bool OutcomeReported;
        [SaveableField(21)] public bool HandoverStarted;
        [SaveableField(22)] public VoluntaryPactDecision SourceDecision;
        [SaveableField(23)] public VoluntaryPactDecision TargetDecision;
        [SaveableField(24)] public bool SourceFilingStarted;
        [SaveableField(25)] public bool TargetFilingStarted;
        [SaveableField(26)] public float SourceInfluenceBeforeFiling;
        [SaveableField(27)] public bool SourceFilingRefundStarted;
        [SaveableField(28)] public bool SourceBallotFiled;
        [SaveableField(29)] public bool TargetBallotFiled;
        [SaveableField(30)] internal int CloseReason;
        [SaveableField(31)] public bool RefusalCooldownRecorded;

        internal bool HasValidTerms(bool requireTargetHostage)
        {
            return !string.IsNullOrWhiteSpace(Id) && Source != null && Target != null && Source != Target
                && SourceHouse != null && TargetHouse != null && SourceHouse != TargetHouse && Sponsor != null
                && SourceHostage != null && (!requireTargetHostage || TargetHostage != null)
                && (TargetHostage == null || TargetHostage != SourceHostage)
                && BaselineDays > 0 && HostagePactRules.IsNegotiableDuration(DurationDays, BaselineDays)
                && Compensation >= 0 && !double.IsNaN(CreatedDay) && !double.IsInfinity(CreatedDay) && CreatedDay >= 0;
        }

        internal bool PaymentPartiesCurrent() => Compensation == 0 || (Payer != null && Payee != null
            && Source?.Leader == Payer && Target?.Leader == Payee);

        internal bool TrySubmit()
        {
            if (Phase != VoluntaryPactProposalPhase.Draft || !HasValidTerms(false)) return false;
            Phase = VoluntaryPactProposalPhase.SourceVote;
            return true;
        }

        internal bool TryFinishSourceVote(bool approved)
        {
            if (Phase != VoluntaryPactProposalPhase.SourceVote) return false;
            if (!approved) CloseReason = (int)VoluntaryPactCloseReason.SourceRejected;
            Phase = approved ? VoluntaryPactProposalPhase.TargetChoice : VoluntaryPactProposalPhase.Cancelled;
            return true;
        }

        internal bool TryChooseTargetHostage(Hero hostage)
        {
            if (Phase != VoluntaryPactProposalPhase.TargetChoice || hostage == null || hostage == SourceHostage) return false;
            TargetHostage = hostage;
            if (!HasValidTerms(true)) { TargetHostage = null; return false; }
            Phase = VoluntaryPactProposalPhase.TargetVote;
            return true;
        }

        internal bool TryFinishTargetVote(bool approved)
        {
            if (Phase != VoluntaryPactProposalPhase.TargetVote) return false;
            if (!approved) CloseReason = (int)VoluntaryPactCloseReason.TargetRejected;
            Phase = approved ? VoluntaryPactProposalPhase.Delivery : VoluntaryPactProposalPhase.Cancelled;
            return true;
        }

        internal bool TryCancel()
        {
            // An established pact or started payment must be resolved through delivery recovery.
            if (Phase == VoluntaryPactProposalPhase.Completed || Phase == VoluntaryPactProposalPhase.Cancelled
                || GoldDebited || !string.IsNullOrWhiteSpace(PactId)) return false;
            Phase = VoluntaryPactProposalPhase.Cancelled;
            return true;
        }

        internal bool TryCancelAfterUnpaidRecovery()
        {
            if (Phase != VoluntaryPactProposalPhase.Delivery || GoldDebited || GoldCredited) return false;
            PactId = null;
            Phase = VoluntaryPactProposalPhase.Cancelled;
            return true;
        }

        internal bool TryComplete()
        {
            if (Phase != VoluntaryPactProposalPhase.Delivery || string.IsNullOrWhiteSpace(PactId)
                || !GoldDebited || !GoldCredited || (Compensation > 0 && !TradeEventStarted)) return false;
            Phase = VoluntaryPactProposalPhase.Completed;
            return true;
        }
    }
}
