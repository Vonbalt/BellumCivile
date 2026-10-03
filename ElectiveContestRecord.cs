using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.SaveSystem;

namespace BellumCivile
{
    public enum ElectiveContestDecision { Pending, Unavailable, Accept, Contest, AwaitingPlayer }

    public sealed class ElectiveContestVote
    {
        [SaveableField(1)] public Clan House;
        [SaveableField(2)] public Hero Speaker;
        [SaveableField(3)] public Hero Supported;
        [SaveableField(4)] public double Weight;
    }

    public sealed class ElectiveContestCandidate
    {
        [SaveableField(1)] public Hero Candidate;
        [SaveableField(2)] public Clan House;
        [SaveableField(3)] public double Support;
        [SaveableField(4)] public bool HasAssessment;
        [SaveableField(5)] public double Acceptance;
        [SaveableField(6)] public double Baseline;
        [SaveableField(7)] public double Honor;
        [SaveableField(8)] public double Mercy;
        [SaveableField(9)] public double PersonalityCap;
        [SaveableField(10)] public double Relations;
        [SaveableField(11)] public double Claim;
        [SaveableField(12)] public double Margin;
        [SaveableField(13)] public double Military;
        [SaveableField(14)] public double Backing;
        [SaveableField(15)] public double Loyalists;
        [SaveableField(16)] public double Threshold;
        [SaveableField(17)] public ElectiveContestDecision Decision;
        [SaveableField(18)] public double Roll = -1;
        [SaveableField(19)] public bool Withdrawn;
        [SaveableField(20)] public double PledgedPower;
        [SaveableField(21)] public bool UltimatumConfirmed;
        [SaveableField(22)] public FactionObject WarFaction;
        [SaveableField(23)] public Kingdom WarShell;
        [SaveableField(24)] public bool WarStarted;

        internal void Decide(bool available, bool player, double roll)
        {
            if (Decision != ElectiveContestDecision.Pending) return;
            if (!available) { Decision = ElectiveContestDecision.Unavailable; return; }
            if (player) { Decision = ElectiveContestDecision.AwaitingPlayer; return; }
            if (!HasAssessment) { Decision = ElectiveContestDecision.Unavailable; return; }
            if (!SuccessionChallengeRules.Finite(roll) || roll < 0 || roll >= 1)
                throw new ArgumentOutOfRangeException(nameof(roll));
            Roll = roll;
            Decision = roll * 100 < 100 - Acceptance ? ElectiveContestDecision.Contest : ElectiveContestDecision.Accept;
        }

        internal void AnswerPlayer(bool contest)
        {
            if (Decision == ElectiveContestDecision.AwaitingPlayer)
                Decision = contest ? ElectiveContestDecision.Contest : ElectiveContestDecision.Accept;
        }
    }

    public sealed class ElectiveContestRecord
    {
        [SaveableField(1)] public string Id;
        [SaveableField(2)] public Kingdom Realm;
        [SaveableField(3)] public Hero ElectedWinner;
        [SaveableField(4)] public Clan ElectedHouse;
        [SaveableField(5)] public Hero Predecessor;
        [SaveableField(6)] public HouseSuccessionLaw Law;
        [SaveableField(7)] public GenderSuccessionLaw Gender;
        [SaveableField(8)] public List<ElectiveContestVote> Votes = new List<ElectiveContestVote>();
        [SaveableField(9)] public List<ElectiveContestCandidate> Candidates = new List<ElectiveContestCandidate>();
        [SaveableField(10)] public double CapturedDay;
        [SaveableField(11)] public bool AccessionCompleted;
        [SaveableField(12)] public double EligibleDay;
        [SaveableField(13)] public bool Closed;
        [SaveableField(14)] public string ClosedReason;
        [SaveableField(15)] public int MandateNumber;
        [SaveableField(16)] public List<ElectiveContestPledge> Pledges = new List<ElectiveContestPledge>();
        [SaveableField(17)] public bool PledgesCaptured;
        [SaveableField(18)] public bool PledgesResolved;
        [SaveableField(19)] public bool UltimataPrepared;
        [SaveableField(20)] public bool RulerAnswered;
        [SaveableField(21)] public Hero SurrenderTo;
        [SaveableField(22)] public double RulerRoll = -1;
        [SaveableField(23)] public bool CrownTransferStarted;
        [SaveableField(24)] public bool CrownTransferred;
        [SaveableField(25)] public bool MandateStarted;
        [SaveableField(26)] public bool WarDispatchStarted;
        [SaveableField(27)] public bool WarDispatchCompleted;
        [SaveableField(28)] public string DispatchFailure;
        [SaveableField(29)] public bool ManualTest;
        [SaveableField(30)] public string TestStatus;
        [SaveableField(31)] public CivilWarPairRecord Rivalry;
        [SaveableField(32)] public bool StartupAborted;
        [SaveableField(33)] public bool ForcedThreeWayTest;
        [SaveableField(34)] public bool AutomaticChallengeEnabled;

        internal bool BypassTestPowerGate => ManualTest && ForcedThreeWayTest;

        internal bool RevalidateParticipants(Func<ElectiveContestCandidate, bool> available,
            Func<Clan, Hero> currentSpeaker, Clan playerHouse)
        {
            if (Closed || WarDispatchStarted || CrownTransferStarted || WarDispatchCompleted) return false;
            bool changed = false;
            foreach (var candidate in Candidates.Where(c => c.Candidate != ElectedWinner
                && (c.Decision == ElectiveContestDecision.Pending || c.Decision == ElectiveContestDecision.AwaitingPlayer
                    || c.Decision == ElectiveContestDecision.Contest && !c.Withdrawn)))
            {
                if (available(candidate)) continue;
                candidate.Decision = ElectiveContestDecision.Unavailable;
                candidate.UltimatumConfirmed = false;
                changed = true;
            }
            foreach (var pledge in Pledges.ToList())
            {
                Hero speaker = currentSpeaker(pledge.House);
                if (speaker == pledge.Speaker && speaker != null) continue;
                changed = true;
                if (speaker == null) { Pledges.Remove(pledge); continue; }
                // Personal promises do not bind a successor. No fresh AI recruitment roll.
                pledge.Speaker = speaker;
                pledge.Preferences.Clear();
                pledge.PlayerChoice = pledge.House == playerHouse;
                pledge.AwaitingPlayer = pledge.PlayerChoice;
                pledge.AssignedSide = ElectedWinner;
            }
            if (!changed) return false;
            PledgesResolved = false;
            UltimataPrepared = false;
            RulerAnswered = false;
            SurrenderTo = null;
            return true;
        }

        internal bool ShouldAdvance(double day) => (ManualTest || AutomaticChallengeEnabled)
            && AccessionCompleted && !Closed && !WarDispatchCompleted && day >= EligibleDay;

        internal void Arm(double day)
        {
            if (AccessionCompleted || Closed) return;
            EligibleDay = Math.Floor(day) + 1;
            AccessionCompleted = true;
        }
    }
}
