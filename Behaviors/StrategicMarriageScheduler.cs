using System;
using System.Collections.Generic;
using System.Diagnostics;
using TaleWorlds.CampaignSystem;

namespace BellumCivile.Behaviors
{
    public partial class StrategicMarriageBehavior
    {
        private const int MarriageWorkPerHour = 768;
        private const int MarriageJobsPerHour = 3;

        private sealed class ScheduledOpportunity
        {
            public Clan House;
            public bool Household;
            public Hero Initiator;
            public float DynasticNeed;
            public int Year;
            public BellumMarriageStrategyHelper.ScheduledMarriageSearch Search;
        }

        private Dictionary<string, int> _queuedDynastySearches = new Dictionary<string, int>();
        private Dictionary<string, int> _queuedHouseholdSearches = new Dictionary<string, int>();
        private readonly Queue<ScheduledOpportunity> _scheduledMarriages = new Queue<ScheduledOpportunity>();
        private readonly HashSet<(Clan, bool)> _scheduledMarriageKeys = new HashSet<(Clan, bool)>();
        private BellumMarriageStrategyHelper.ScheduledMarriageMarket _marriageMarket;
        private int _marriageMarketDay = -1;

        private Dictionary<string, int> ScheduledMarriageReceipts(bool household) =>
            household ? _queuedHouseholdSearches : _queuedDynastySearches;

        private void SyncScheduledMarriageSearches(IDataStore store)
        {
            store.SyncData("BellumCivile_QueuedDynasticMarriageSearches", ref _queuedDynastySearches);
            store.SyncData("BellumCivile_QueuedHouseholdMarriageSearches", ref _queuedHouseholdSearches);
            _queuedDynastySearches = _queuedDynastySearches ?? new Dictionary<string, int>();
            _queuedHouseholdSearches = _queuedHouseholdSearches ?? new Dictionary<string, int>();
            if (!store.IsLoading) return;
            _scheduledMarriages.Clear();
            _scheduledMarriageKeys.Clear();
            _marriageMarket = null;
            _marriageMarketDay = -1;
        }

        private static bool CanScheduleClan(Clan clan) => clan != null && clan != Clan.PlayerClan && !clan.IsEliminated
            && clan.Kingdom != null && !clan.IsMinorFaction && !clan.IsClanTypeMercenary && !clan.IsBanditFaction
            && !string.IsNullOrEmpty(clan.StringId) && clan.StringId != "neutral";

        private void ScheduleMarriageSearch(Clan clan, bool household, Hero initiator, float dynasticNeed, int year)
        {
            if (!_scheduledMarriageKeys.Add((clan, household))) return;
            ScheduledMarriageReceipts(household)[clan.StringId] = year;
            _scheduledMarriages.Enqueue(new ScheduledOpportunity
            {
                House = clan, Household = household, Initiator = initiator, DynasticNeed = dynasticNeed, Year = year
            });
            _yearlySummary.PeakSearchQueue = Math.Max(_yearlySummary.PeakSearchQueue, _scheduledMarriages.Count);
        }

        private void ProcessScheduledMarriageSearches()
        {
            if (!BellumCivileOptions.EnableBellumStrategicMarriageLogic || _scheduledMarriages.Count == 0) return;
            long start = Stopwatch.GetTimestamp();
            int remaining = MarriageWorkPerHour;
            int year = CurrentDay / GetCampaignDaysInYear();
            for (int jobs = 0; jobs < MarriageJobsPerHour && remaining > 0 && _scheduledMarriages.Count > 0; jobs++)
            {
                var job = _scheduledMarriages.Peek();
                var receipts = ScheduledMarriageReceipts(job.Household);
                bool current = receipts.TryGetValue(job.House.StringId, out int queuedYear) && queuedYear == job.Year;
                if (!current || !CanScheduleClan(job.House) || queuedYear < year - 1)
                {
                    DequeueMarriageSearch(job);
                    if (current) CloseMarriageOpportunity(job.House, job.Household);
                    continue;
                }

                BellumMarriageMatch best;
                BellumMarriageEvaluationStats stats;
                bool complete;
                try
                {
                    remaining -= AdvanceScheduledMarriageSearch(job, remaining, out best, out stats, out complete);
                }
                catch (Exception ex)
                {
                    DequeueMarriageSearch(job);
                    CloseMarriageOpportunity(job.House, job.Household);
                    BellumCivileLogger.Log($"Scheduled marriage search failed; house={job.House.StringId}; household={job.Household}; error={ex}");
                    break;
                }
                if (!complete) break;

                DequeueMarriageSearch(job);
                receipts.Remove(job.House.StringId);
                if (best == null && TryCompleteBetrothal(job, stats)) continue;
                FinishMarriageOpportunity(job.House, job.Household, job.Initiator, job.DynasticNeed, best, stats);
                if (best != null) _marriageMarket?.InvalidateAfterMatch();
            }
            double elapsed = (Stopwatch.GetTimestamp() - start) * 1000d / Stopwatch.Frequency;
            _yearlySummary.RecordScheduledSearchTick(MarriageWorkPerHour - remaining, elapsed);
        }

        private bool TryCompleteBetrothal(ScheduledOpportunity job, BellumMarriageEvaluationStats stats)
        {
            var match = job.Search?.Betrothal;
            if (job.Household || match == null || match.First.Clan != job.House || match.Second.Clan != match.OtherHouse
                || match.Score < BellumCivileOptions.MarriageAcceptanceThreshold) return false;
            bool toPlayer = match.Second.Clan == Clan.PlayerClan;
            if (toPlayer && !CanSendPlayerOffer()
                || BetrothalBehavior.Instance?.Create(match.First, match.Second, match.Destination, toPlayer) != true) return false;
            if (toPlayer) _lastPlayerOfferDay = CurrentDay;
            CloseMarriageOpportunity(job.House, false);
            _yearlySummary.Merge(stats);
            _yearlySummary.Searches++;
            _yearlySummary.BetrothalOffersOrPledges++;
            return true;
        }

        private void DequeueMarriageSearch(ScheduledOpportunity job)
        {
            _scheduledMarriages.Dequeue();
            _scheduledMarriageKeys.Remove((job.House, job.Household));
        }

        private int AdvanceScheduledMarriageSearch(ScheduledOpportunity job, int budget, out BellumMarriageMatch best,
            out BellumMarriageEvaluationStats stats, out bool complete)
        {
            if (job.Search == null)
            {
                if (_marriageMarket == null || _marriageMarketDay != CurrentDay)
                {
                    _marriageMarket = new BellumMarriageStrategyHelper.ScheduledMarriageMarket();
                    _marriageMarketDay = CurrentDay;
                    _yearlySummary.MarriageMarketBuilds++;
                    _yearlySummary.Merge(_marriageMarket.Stats);
                }
                var factions = Campaign.Current.GetCampaignBehavior<FactionManagerBehavior>();
                job.Search = new BellumMarriageStrategyHelper.ScheduledMarriageSearch(job.House, job.Household,
                    CanSendPlayerOffer(), factions, _marriageMarket, CurrentDay);
            }
            int used = job.Search.Advance(budget);
            best = job.Search.Best;
            stats = job.Search.Stats;
            complete = job.Search.Complete;
            return used;
        }
    }
}
