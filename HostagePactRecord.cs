using System;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.SaveSystem;

namespace BellumCivile
{
    public enum HostagePactPhase { Preparing = 0, Active = 1, Resolving = 2, Ended = 3 }
    public enum HostagePactEndReason
    {
        None = 0, Expired = 1, HouseReplaced = 2, Rescued = 3, HostageDied = 4,
        War = 5, RealmLost = 6, RevampDisabled = 7, InvalidCustody = 8,
        EarlyRelease = 9, Betrayal = 10
    }
    public enum HostageCustodyOutcome { Pending = 0, Release = 1, Retain = 2, Execute = 3 }

    public sealed class TreatyHostageRecord
    {
        [SaveableField(1)] public Hero Hero;
        [SaveableField(2)] public Clan SupplyingHouse;
        [SaveableField(3)] public Clan ReceivingHouse;
        [SaveableField(4)] public Settlement Holding;
        [SaveableField(5)] public Settlement PreviousHome;
        [SaveableField(6)] public int PreviousHeroState;
        [SaveableField(7)] public int Tier;
        [SaveableField(8)] public int NegotiatedCost;
        [SaveableField(9)] public HostageCustodyOutcome Outcome;
        [SaveableField(10)] public bool CustodyEstablished;
        [SaveableField(11)] public bool ActionStarted;
        [SaveableField(12)] public bool ActionCompleted;
        [SaveableField(13)] public bool RelationsApplied;
        [SaveableField(14)] public bool Reported;
        [SaveableField(15)] public double CaptivityStartDay;
        [SaveableField(16)] public Hero DispositionRuler;
        [SaveableField(17)] public bool ExecutionSucceeded;
        [SaveableField(18)] public bool ClemencyChosen;
        [SaveableField(19)] public bool ReleaseSucceeded;

        internal bool TryChoose(HostageCustodyOutcome outcome)
        {
            if (Outcome != HostageCustodyOutcome.Pending || outcome < HostageCustodyOutcome.Release
                || outcome > HostageCustodyOutcome.Execute) return false;
            Outcome = outcome;
            return true;
        }
    }

    public sealed class HostagePactRecord
    {
        [SaveableField(1)] public string Id;
        [SaveableField(2)] public Kingdom FirstRealm;
        [SaveableField(3)] public Kingdom SecondRealm;
        [SaveableField(4)] public Clan FirstHouse;
        [SaveableField(5)] public Clan SecondHouse;
        [SaveableField(6)] public double SignedDay;
        [SaveableField(7)] public double EndDay;
        [SaveableField(8)] public HostagePactPhase Phase;
        [SaveableField(9)] public HostagePactEndReason EndReason;
        [SaveableField(10)] public TreatyHostageRecord FirstHostage;
        [SaveableField(11)] public TreatyHostageRecord SecondHostage;
        [SaveableField(12)] public Kingdom VoluntaryAggressor;
        [SaveableField(13)] public bool BreachMemoryApplied;
        [SaveableField(14)] public bool Reported;
        [SaveableField(15)] public Hero FirstSignatory;
        [SaveableField(16)] public Hero SecondSignatory;
        [SaveableField(17)] public string TreatyProposalId;
        [SaveableField(18)] public bool TreatySettlementStarted;
        [SaveableField(19)] public bool TreatySettlementCompleted;
        [SaveableField(20)] public bool SigningDayRecorded;
        [SaveableField(21)] public double TreatySigningDay;
        [SaveableField(22)] public Hero TerminatingHostage;
        [SaveableField(23)] public Hero TerminatingRuler;
        [SaveableField(24)] public bool TerminationTraitApplied;
        [SaveableField(25)] public bool ReleaseGratitudeApplied;
        [SaveableField(26)] public bool ExecutionNeedsHonorPenalty;

        internal bool TryActivate(double day)
        {
            if (Phase != HostagePactPhase.Preparing || !Finite(day) || day < 0
                || string.IsNullOrWhiteSpace(Id) || FirstRealm == null || SecondRealm == null
                || FirstRealm == SecondRealm || FirstHouse == null || SecondHouse == null
                || FirstHouse == SecondHouse || (FirstHostage == null && SecondHostage == null)
                || !ValidHostage(FirstHostage, FirstHouse, SecondHouse)
                || !ValidHostage(SecondHostage, SecondHouse, FirstHouse)
                || (FirstHostage != null && SecondHostage != null && FirstHostage.Hero == SecondHostage.Hero))
                return false;
            SignedDay = day;
            EndDay = day + HostagePactRules.DurationDays;
            FirstSignatory = FirstRealm.Leader;
            SecondSignatory = SecondRealm.Leader;
            Phase = HostagePactPhase.Active;
            return true;
        }

        internal bool IsDue(double day) => Phase == HostagePactPhase.Active && Finite(day) && day >= EndDay;

        internal bool TryAbortPreparation()
        {
            if (Phase != HostagePactPhase.Preparing) return false;
            EndReason = HostagePactEndReason.InvalidCustody;
            Phase = HostagePactPhase.Resolving;
            FirstHostage?.TryChoose(HostageCustodyOutcome.Release);
            SecondHostage?.TryChoose(HostageCustodyOutcome.Release);
            return true;
        }

        internal bool TryBeginResolution(HostagePactEndReason reason, Kingdom voluntaryAggressor = null)
        {
            if (Phase != HostagePactPhase.Active || reason <= HostagePactEndReason.None
                || reason > HostagePactEndReason.Betrayal
                || (reason == HostagePactEndReason.Betrayal && voluntaryAggressor == null)
                || (voluntaryAggressor != null && ((reason != HostagePactEndReason.War && reason != HostagePactEndReason.Betrayal)
                    || (voluntaryAggressor != FirstRealm && voluntaryAggressor != SecondRealm)))) return false;
            // Commit before any release/death callback, so reciprocal hostages cannot
            // reinterpret a breach disposition as natural-death invalidation.
            EndReason = reason;
            VoluntaryAggressor = voluntaryAggressor;
            Phase = HostagePactPhase.Resolving;
            if (reason != HostagePactEndReason.War && reason != HostagePactEndReason.Betrayal)
            {
                FirstHostage?.TryChoose(HostageCustodyOutcome.Release);
                SecondHostage?.TryChoose(HostageCustodyOutcome.Release);
            }
            return true;
        }

        internal bool TryComplete()
        {
            if (Phase != HostagePactPhase.Resolving
                || !Completed(FirstHostage) || !Completed(SecondHostage)) return false;
            Phase = HostagePactPhase.Ended;
            return true;
        }

        internal bool Protects(Hero hero) => hero != null && Phase != HostagePactPhase.Ended
            && (Protects(FirstHostage, hero) || Protects(SecondHostage, hero));

        private static bool Protects(TreatyHostageRecord record, Hero hero)
            => record?.Hero == hero && record.CustodyEstablished && !record.ActionCompleted;
        private static bool Completed(TreatyHostageRecord record) => record == null
            || (record.Outcome != HostageCustodyOutcome.Pending && record.ActionCompleted);
        private static bool ValidHostage(TreatyHostageRecord record, Clan supplier, Clan receiver)
            => record == null || (record.Hero != null && record.SupplyingHouse == supplier
                && record.ReceivingHouse == receiver && record.Holding != null && record.CustodyEstablished
                && !record.ActionStarted && !record.ActionCompleted && record.Outcome == HostageCustodyOutcome.Pending
                && record.Tier >= 1 && record.Tier <= 4
                && record.NegotiatedCost == HostagePactRules.GetTreatyCost(record.Tier));
        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
