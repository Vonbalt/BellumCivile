using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.SaveSystem;

namespace BellumCivile
{
    public sealed class ElectivePreference
    {
        [SaveableField(1)] public Hero Candidate;
        [SaveableField(2)] public double Law;
        [SaveableField(3)] public double Politics;
        [SaveableField(4)] public double Score;
    }

    public sealed class ElectiveCommitment
    {
        [SaveableField(1)] public Clan Clan;
        [SaveableField(2)] public Hero Speaker;
        [SaveableField(3)] public Hero Nominee;
        [SaveableField(4)] public Hero Supported;
        [SaveableField(5)] public string Source;
        [SaveableField(6)] public CampaignTime Until;
        [SaveableField(7)] public double Weight;
        [SaveableField(8)] public List<ElectivePreference> Preferences = new List<ElectivePreference>();
    }

    public sealed class ElectiveSuccessionRecord
    {
        [SaveableField(1)] public Kingdom Realm;
        [SaveableField(2)] public Hero Sovereign;
        [SaveableField(3)] public HouseSuccessionLaw Law;
        [SaveableField(4)] public GenderSuccessionLaw Gender;
        [SaveableField(5)] public List<ElectiveCommitment> Votes = new List<ElectiveCommitment>();
        [SaveableField(6)] public List<Hero> Finalists = new List<Hero>();
        [SaveableField(7)] public bool Frozen;
        [SaveableField(8)] public bool PlayerConfirmed;
        [SaveableField(9)] public Hero Winner;
        [SaveableField(10)] public Hero Excluded;
        [SaveableField(11)] public bool Completed;
        [SaveableField(12)] public bool MandateInitialized;
        [SaveableField(13)] public Hero MandateSovereign;
        [SaveableField(14)] public CampaignTime MandateStart;
        [SaveableField(15)] public CampaignTime MandateEnd;
        [SaveableField(16)] public int MandateYears;
        [SaveableField(17)] public bool ElectionWarningSent;
        [SaveableField(18)] public int MandateNumber;
        [SaveableField(19)] public bool DepositionPending;
        [SaveableField(20)] public Hero DeposedRuler;
        [SaveableField(21)] public Clan InterimHouse;
        [SaveableField(22)] public CampaignTime DepositionElectionDate;
        [SaveableField(23)] public bool InterimPrepared;
        [SaveableField(24)] public bool DepositionAnnounced;
        [SaveableField(25)] public bool DepositionAwaitingJudgments;
        [SaveableField(26)] public bool ReformElectionPending;
        [SaveableField(27)] public CampaignTime ReformElectionDate;
        public CampaignTime ElectionDate => ReformElectionPending ? ReformElectionDate : MandateEnd;

        internal bool ReformMandate(int years, CampaignTime approved, int deliberationDays)
        {
            if (years != 0 && years != 1 && years != 5 && years != 10)
                throw new System.ArgumentOutOfRangeException(nameof(years));
            if (!MandateInitialized || Frozen || Completed || DepositionPending || ReformElectionPending || years == MandateYears)
                return false;
            if (MandateYears == 0 && years != 0) MandateStart = approved;
            MandateYears = years;
            MandateEnd = years == 0 ? CampaignTime.Never : MandateStart + CampaignTime.Years(years);
            ElectionWarningSent = false;
            if (years != 0 && MandateEnd.ToDays <= approved.ToDays)
            {
                // Keep the legal expiry distinct from the time allowed to deliberate.
                ReformElectionPending = true;
                ReformElectionDate = approved + CampaignTime.Days(System.Math.Max(1, deliberationDays));
            }
            return true;
        }
        internal bool ScheduleDeposition(Hero deposed, Clan caretaker, CampaignTime electionDate)
        {
            if (DepositionPending) return DeposedRuler == deposed && InterimHouse == caretaker;
            DepositionPending = true;
            ReformElectionPending = false;
            DepositionAwaitingJudgments = true;
            DeposedRuler = deposed;
            InterimHouse = caretaker;
            DepositionElectionDate = electionDate;
            InterimPrepared = DepositionAnnounced = false;
            Excluded = deposed;
            Completed = PlayerConfirmed = false;
            return true;
        }
        internal void StartMandate(Hero sovereign, int years, CampaignTime start)
        {
            if (years != 0 && years != 1 && years != 5 && years != 10)
                throw new System.ArgumentOutOfRangeException(nameof(years));
            MandateYears = years;
            MandateStart = start;
            ReformElectionPending = false;
            ReformElectionDate = CampaignTime.Never;
            MandateEnd = years == 0 ? CampaignTime.Never : start + CampaignTime.Years(years);
            MandateSovereign = sovereign;
            MandateInitialized = true;
            ElectionWarningSent = false;
            MandateNumber++;
        }
        public double TotalWeight => Votes.Sum(v => v.Weight);
        public double Support(Hero candidate) => Votes.Where(v => v.Supported == candidate).Sum(v => v.Weight);
    }
}
