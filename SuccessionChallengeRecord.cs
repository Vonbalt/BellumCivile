using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.SaveSystem;

namespace BellumCivile
{
    public enum SuccessionChallengePhase { Gathering, AwaitingResponse, LandSettlement, CrownSettlement, WarRequired, Settled, Withdrawn, Cancelled, ActiveWar, ResolvingWar }
    public enum SuccessionChallengeDemand { Crown, InheritanceFirst }
    public enum SuccessionChallengeOutcome { None, Victory, Defeat, WhitePeace }

    public sealed class SuccessionChallengeRecord
    {
        [SaveableField(1)] public string Id;
        [SaveableField(2)] public Kingdom Realm;
        [SaveableField(3)] public Hero Sovereign;
        [SaveableField(4)] public Hero Challenger;
        [SaveableField(5)] public Clan OriginalHouse;
        [SaveableField(6)] public SuccessionChallengePhase Phase;
        [SaveableField(7)] public SuccessionChallengeDemand Demand;
        [SaveableField(8)] public double InitialLoyalty;
        [SaveableField(9)] public double DemandRoll;
        [SaveableField(10)] public double ResponseRoll = -1;
        [SaveableField(11)] public double AcceptanceChance;
        [SaveableField(12)] public List<Clan> Backers = new List<Clan>();
        [SaveableField(13)] public List<Clan> Loyalists = new List<Clan>();
        [SaveableField(14)] public double BackingPower;
        [SaveableField(15)] public double LoyalistPower;
        [SaveableField(16)] public double RequiredRatio;
        [SaveableField(17)] public CrownAccessionRecord Estate;
        [SaveableField(18)] public bool FallbackGrant;
        [SaveableField(19)] public double StartedDay;
        [SaveableField(20)] public double PersonalBlockedUntil;
        [SaveableField(21)] public double RealmBlockedUntil;
        [SaveableField(22)] public double RewardUntil;
        [SaveableField(23)] public bool LoyaltyEnded;
        [SaveableField(24)] public bool RewardRecorded;
        [SaveableField(25)] public string Failure;
        [SaveableField(26)] public List<SuccessionPledgeRecord> Pledges = new List<SuccessionPledgeRecord>();
        [SaveableField(27)] public bool PledgesCaptured;
        [SaveableField(28)] public bool ReportPending;
        [SaveableField(29)] public CrownAccessionRecord CrownEstate;
        [SaveableField(30)] public bool CrownTransferStarted;
        [SaveableField(31)] public bool CrownTitleTransferred;
        [SaveableField(32)] public bool CrownAnnounced;
        [SaveableField(33)] public double CreditedHeirPartyPower;
        [SaveableField(34)] public bool WarDispatchStarted;
        [SaveableField(35)] public FactionObject WarFaction;
        [SaveableField(36)] public Kingdom WarShell;
        [SaveableField(37)] public List<string> SeizureIntents = new List<string>();
        [SaveableField(38)] public List<string> SeizedFiefs = new List<string>();
        [SaveableField(39)] public Dictionary<string, string> PrewarLegalOwners = new Dictionary<string, string>();
        [SaveableField(40)] public Dictionary<string, string> PrewarLegalParents = new Dictionary<string, string>();
        [SaveableField(41)] public List<string> ReconciledFiefs = new List<string>();
        [SaveableField(42)] public SuccessionChallengeOutcome WarOutcome;
        [SaveableField(43)] public Kingdom OutcomeRealm;
        [SaveableField(44)] public bool EstateResolved;
        [SaveableField(45)] public bool ResolutionReturned;
        [SaveableField(46)] public double OutcomeDay;
        [SaveableField(47)] public Hero SubmissionRuler;
        [SaveableField(48)] public double SubmissionUntil;
        [SaveableField(49)] public List<Clan> OutcomeLosers = new List<Clan>();
        [SaveableField(50)] public bool TribunalQueued;
        [SaveableField(51)] public bool OutcomeRewardsApplied;
        [SaveableField(52)] public Clan OutcomeVictor;
        [SaveableField(53)] public List<Clan> RewardedClans = new List<Clan>();
        [SaveableField(54)] public bool RestoredRealmRequested;
        [SaveableField(55)] public bool RestoredRealmInitialized;
        [SaveableField(56)] public bool RestoredRealmReady;
        [SaveableField(57)] public bool ManualTest;
        [SaveableField(58)] public bool ConcessionAnnounced;
        [SaveableField(59)] public CrownAccessionRecord WarEstate;
        [SaveableField(60)] public bool WarCrownTransferred;

        internal bool HasWarCrownReceipt => WarCrownTransferred || RestoredRealmReady
            || WarOutcome == SuccessionChallengeOutcome.Victory && EstateResolved;

        // Existing wars resume their saved transaction; new wars always have a separate snapshot.
        internal CrownAccessionRecord MilitaryEstate => WarEstate ?? CrownEstate;

        // Realm remains the origin of the dispute and its estate receipts.
        internal Kingdom WarRealm => WarFaction?.ParentKingdom ?? Realm;

        public bool IsOpen => Phase != SuccessionChallengePhase.Settled && Phase != SuccessionChallengePhase.Withdrawn
            && Phase != SuccessionChallengePhase.Cancelled;
        public bool HasAdvance => DeliveredAdvance(CrownEstate);
        public bool HasHouseholdGrant => DeliveredAdvance(Estate) || DeliveredAdvance(WarEstate);
        private static bool DeliveredAdvance(CrownAccessionRecord estate) => estate?.EndowmentSettled == true &&
            (estate.DeliveredFiefs.Count > 0 || estate.DeliveredTitles.Count > 0 || estate.DeliveredGold > 0);
        internal double LoyaltyAt(Hero sovereign, double day) => RewardRecorded && !LoyaltyEnded
            && Sovereign == sovereign && day < RewardUntil ? 25 : 0;
        internal double SubmissionAt(Hero sovereign, double day) => SubmissionRuler == sovereign && ResolutionReturned
            && WarOutcome == SuccessionChallengeOutcome.Defeat
                ? SuccessionChallengeRules.SubmissionBonus(OutcomeDay, SubmissionUntil, day) : 0;
    }

    internal static class SuccessionChallengeRules
    {
        internal static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        internal static bool CanInitiate(double loyalty, bool available, bool lawful, bool blocked) =>
            available && lawful && !blocked && Finite(loyalty) && loyalty >= 0 && loyalty < 25;
        internal static double InheritanceChance(double loyalty) => Math.Max(50, Math.Min(75, 50 + loyalty));
        internal static SuccessionChallengeDemand ChooseDemand(double loyalty, bool dependent, double roll)
        {
            if (!Finite(loyalty) || !Finite(roll) || roll < 0 || roll >= 1) throw new ArgumentOutOfRangeException(nameof(roll));
            return dependent && roll * 100 < InheritanceChance(loyalty)
                ? SuccessionChallengeDemand.InheritanceFirst : SuccessionChallengeDemand.Crown;
        }
        internal static bool CanConcede(int ownedFiefs, int grantedFiefs) => grantedFiefs > 0 && ownedFiefs > grantedFiefs;
        internal static bool HasBacking(double backing, double loyalists, double threshold) =>
            Finite(backing) && Finite(loyalists) && Finite(threshold) && backing > 0 && loyalists >= 0
            && threshold > 0 && backing >= loyalists * threshold;
        internal static bool CanProceed(double backing, double opposition, double threshold, bool player, bool stronghold) =>
            stronghold && Finite(backing) && Finite(opposition) && backing > 0 && opposition >= 0
            && (player || HasBacking(backing, opposition, threshold));
        internal static double TransferablePower(double party, double royalMilitary, double loyalists) =>
            Finite(party) && Finite(royalMilitary) && Finite(loyalists)
                ? Math.Max(0, Math.Min(party, Math.Min(royalMilitary, loyalists))) : 0;
        internal static double SubmissionBonus(double start, double end, double now) =>
            Finite(start) && Finite(end) && Finite(now) && end > start
                ? 25 * Math.Max(0, Math.Min(1, (end - now) / (end - start))) : 0;
        internal static int PersonalPauseYears(SuccessionChallengeOutcome outcome) =>
            outcome == SuccessionChallengeOutcome.Defeat ? 5 : 2;
    }
}
