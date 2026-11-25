using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.Core;
using C = BellumCivile.BellumCivileConstants;

namespace BellumCivile.Behaviors
{
    /// <summary>
    /// Staggered scheduler for private title, service and formation actions.
    /// Crown grants and public revocations are owned by the court term framework.
    /// </summary>
    public class FeudalPoliticalOptionsBehavior : CampaignBehaviorBase
    {

        private Dictionary<string, float> _nextClanEvaluationDays = new Dictionary<string, float>();
        private List<string> _dailyBatch = new List<string>();
        private float _batchDay = -1f;
        private float _nextScheduledEvaluationDay = float.MaxValue;
        private readonly Dictionary<string, Clan> _scheduledClansById = new Dictionary<string, Clan>(StringComparer.Ordinal);
        private readonly FeudalPoliticalTiming _timing = new FeudalPoliticalTiming();
        private FeudalTitleUsurpationBehavior _usurpationBehavior;
        private ClaimFeudBehavior _claimFeudBehavior;
        private FeudalServiceBehavior _serviceBehavior;

        public override void RegisterEvents()
        {
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
            CampaignEvents.HourlyTickEvent.AddNonSerializedListener(this, OnHourlyTick);
            CampaignEvents.OnClanDestroyedEvent.AddNonSerializedListener(this, OnClanDestroyed);
            CampaignEvents.OnClanChangedKingdomEvent.AddNonSerializedListener(this, OnClanChangedKingdom);
        }

        public override void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData("BellumCivile_FeudalPoliticalOptionNextClanEvaluationDays", ref _nextClanEvaluationDays);
            dataStore.SyncData("BellumCivile_FeudalPoliticalOptionDailyBatch", ref _dailyBatch);
            dataStore.SyncData("BellumCivile_FeudalPoliticalOptionBatchDay", ref _batchDay);
            EnsureCollectionsInitialized();
        }

        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            EnsureCollectionsInitialized();
            CachePoliticalBehaviors();
            ScheduleMissingClanEvaluations();
            RecalculateNextScheduledEvaluationDay();
        }

        private void OnDailyTick()
        {
            EnsureCollectionsInitialized();

            float currentDay = CurrentDay;
            _timing.FlushIfYearChanged((int)(currentDay / GetCampaignDaysInYear()));
            if (_dailyBatch.Count > 0 || Math.Floor(_batchDay) == Math.Floor(currentDay)
                || currentDay < _nextScheduledEvaluationDay)
                return;

            _dailyBatch = _nextClanEvaluationDays
                .Where(pair => pair.Value <= currentDay)
                .OrderBy(pair => pair.Value)
                .ThenBy(pair => pair.Key)
                .Take(C.ClaimFeudMaxClanEvaluationsPerDailyTick)
                .Select(pair => pair.Key)
                .ToList();
            _batchDay = currentDay;
            _timing.RecordBatch(_dailyBatch.Count);
        }

        private void OnHourlyTick()
        {
            if (_dailyBatch.Count == 0) return;
            FeudalTitleBehavior titleBehavior = FeudalTitleBehavior.Instance ?? Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>();
            if (titleBehavior == null) return;

            string clanId = _dailyBatch[0];
            _dailyBatch.RemoveAt(0);
            if (_nextClanEvaluationDays.TryGetValue(clanId, out float dueDay) && dueDay <= _batchDay)
            {
                Clan clan = ResolveScheduledClan(clanId);
                if (!IsValidPoliticalActionClan(clan))
                {
                    _nextClanEvaluationDays.Remove(clanId);
                }
                else
                {
                    long started = Stopwatch.GetTimestamp();
                    TryRunPoliticalOptionsForClan(titleBehavior, clan, out string report);
                    _timing.Record(FeudalPoliticalTiming.Stage.Total, started);
                    if (BellumCivileDebug.ShowInGameMessages)
                        BellumCivileDebug.Trace("titles",
                            $"Feudal political options evaluated; clan={clan.Name} ({clan.StringId}); report={report ?? "no report"}.");
                    // Preserve the normal cadence from the day this job would have run.
                    ScheduleNextEvaluation(clan, initialSchedule: false, scheduledFromDay: _batchDay);
                }
            }

            if (_dailyBatch.Count == 0) RecalculateNextScheduledEvaluationDay();
        }

        public bool TryForceEvaluateClan(Clan clan, out string report)
        {
            EnsureCollectionsInitialized();
            FeudalTitleBehavior titleBehavior = FeudalTitleBehavior.Instance ?? Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>();
            if (titleBehavior == null)
            {
                report = "Error: FeudalTitleBehavior unavailable.";
                return false;
            }

            if (!IsValidPoliticalActionClan(clan))
            {
                report = "Error: clan is not eligible for feudal political options.";
                return false;
            }

            long started = Stopwatch.GetTimestamp();
            bool acted = TryRunPoliticalOptionsForClan(titleBehavior, clan, out report);
            _timing.Record(FeudalPoliticalTiming.Stage.Total, started);
            report = $"{(acted ? "Action selected" : "No action selected")}: {report}";
            return true;
        }

        internal void RecordFabricationSearch(long startedAt) =>
            _timing.Record(FeudalPoliticalTiming.Stage.FabricationSearch, startedAt);

        public void QueueClanForEvaluation(Clan clan, string reason, float delayDays = 1f)
        {
            EnsureCollectionsInitialized();
            if (!IsValidPoliticalActionClan(clan) || string.IsNullOrWhiteSpace(clan.StringId))
                return;

            _scheduledClansById[clan.StringId] = clan;
            float dueDay = CurrentDay + Math.Max(1f, delayDays);
            if (!_nextClanEvaluationDays.TryGetValue(clan.StringId, out float existingDueDay) || existingDueDay > dueDay)
            {
                _nextClanEvaluationDays[clan.StringId] = dueDay;
                if (dueDay < _nextScheduledEvaluationDay)
                    _nextScheduledEvaluationDay = dueDay;
                if (BellumCivileDebug.ShowInGameMessages)
                    BellumCivileDebug.Trace("titles",
                        $"Queued feudal political options evaluation; clan={clan.Name} ({clan.StringId}); due_day={dueDay:0}; reason={reason ?? "unknown"}.");
            }
        }

        private bool TryRunPoliticalOptionsForClan(FeudalTitleBehavior titleBehavior, Clan clan, out string report)
        {
            report = null;
            CachePoliticalBehaviors();

            if (_usurpationBehavior != null)
            {
                long started = Stopwatch.GetTimestamp();
                bool acted = _usurpationBehavior.TryRunAutonomousUsurpationEvaluationForClan(titleBehavior, clan, out string detail);
                _timing.Record(FeudalPoliticalTiming.Stage.Usurpation, started);
                if (acted) { report = $"usurpation: {detail}"; return true; }
            }

            if (_claimFeudBehavior != null && (C.ClaimFeudAiEnabled || C.FeudalClaimFabricationAiEnabled))
            {
                long started = Stopwatch.GetTimestamp();
                bool acted = _claimFeudBehavior.TryRunAutonomousLandedAmbitionEvaluationForClan(titleBehavior, clan, out string detail);
                _timing.Record(FeudalPoliticalTiming.Stage.Landed, started);
                if (acted) { report = $"landed ambition: {detail}"; return true; }
            }

            if (_serviceBehavior != null)
            {
                long started = Stopwatch.GetTimestamp();
                bool acted = _serviceBehavior.TryRunAutonomousServiceReviewForClan(titleBehavior, clan, out string detail);
                _timing.Record(FeudalPoliticalTiming.Stage.Service, started);
                if (acted) { report = $"service review: {detail}"; return true; }
            }

            long formationStart = Stopwatch.GetTimestamp();
            bool formed = TryRunAutonomousFormationEvaluationForClan(titleBehavior, clan, out string formationReport);
            _timing.Record(FeudalPoliticalTiming.Stage.Formation, formationStart);
            if (formed)
            {
                report = $"title formation: {formationReport}";
                return true;
            }


            report = "no eligible action";
            return false;
        }

        private static bool TryRunAutonomousFormationEvaluationForClan(FeudalTitleBehavior titleBehavior, Clan clan, out string report)
        {
            report = null;

            if (titleBehavior == null)
            {
                report = "title behavior unavailable";
                return false;
            }

            if (clan == Clan.PlayerClan)
            {
                report = "player formations use explicit hierarchy action";
                return false;
            }

            if (!IsValidPoliticalActionClan(clan))
            {
                report = "invalid political action clan";
                return false;
            }

            FeudalTitleFormationCandidate bestCandidate = null;
            foreach (FeudalTitleType targetType in new[]
            {
                FeudalTitleType.Empire,
                FeudalTitleType.Kingdom,
                FeudalTitleType.Duchy,
                FeudalTitleType.County
            })
            {
                if (!titleBehavior.TryGetBestFormableTitle(clan, targetType, out FeudalTitleFormationCandidate candidate, out _))
                    continue;

                if (!CanAffordFormation(clan, candidate, out string budgetReason))
                {
                    report = budgetReason;
                    continue;
                }

                bestCandidate = candidate;
                break;
            }

            if (bestCandidate == null)
            {
                if (string.IsNullOrWhiteSpace(report))
                    report = "no affordable formable title";
                return false;
            }

            if (!titleBehavior.TryFormTitle(clan, bestCandidate, chargeCost: true, out FeudalTitleRecord formedTitle, out string reason))
            {
                report = reason ?? "title behavior rejected formation";
                return false;
            }

            report = $"formed {formedTitle?.TitleId ?? bestCandidate.Name}; type={bestCandidate.TargetType}; children={bestCandidate.ChildTitleIds.Count}; cost={bestCandidate.GoldCost}g; influence_reward={bestCandidate.InfluenceReward:0}";
            BellumCivileDebug.Trace(
                "titles",
                $"AI formed higher feudal title from political options pass; clan={clan.Name} ({clan.StringId}); title={FeudalTitleDisplayHelper.FormatTitleName(formedTitle, clan)}; type={bestCandidate.TargetType}; children={bestCandidate.ChildTitleIds.Count}; cost={bestCandidate.GoldCost}g; influence_reward={bestCandidate.InfluenceReward:0}.",
                requestInGameDisplay: true);
            return true;
        }

        private static bool CanAffordFormation(Clan clan, FeudalTitleFormationCandidate candidate, out string reason)
        {
            reason = null;
            if (clan?.Leader == null || candidate == null)
            {
                reason = "missing clan leader or formation candidate";
                return false;
            }

            int reserve = Math.Max(25000, candidate.GoldCost / 4);
            if (clan.Leader.Gold < candidate.GoldCost + reserve)
            {
                reason = $"insufficient formation gold reserve; candidate={candidate.TargetType}; cost={candidate.GoldCost}; reserve={reserve}";
                return false;
            }

            return true;
        }


        private static bool IsValidPoliticalActionClan(Clan clan)
        {
            return clan != null
                && !clan.IsEliminated
                && !clan.IsUnderMercenaryService
                && (!clan.IsMinorFaction || clan == Clan.PlayerClan)
                && clan.Kingdom != null
                && !clan.Kingdom.IsEliminated
                && !BellumKingdomVisibilityHelper.IsTemporaryBellumKingdom(clan.Kingdom)
                && clan.Leader != null
                && !clan.Leader.IsDead;
        }

        private void ScheduleMissingClanEvaluations()
        {
            _scheduledClansById.Clear();
            foreach (Clan clan in Clan.All)
            {
                if (clan == null || string.IsNullOrWhiteSpace(clan.StringId)) continue;
                if (!_scheduledClansById.ContainsKey(clan.StringId))
                    _scheduledClansById.Add(clan.StringId, clan);
                if (IsValidPoliticalActionClan(clan) && !_nextClanEvaluationDays.ContainsKey(clan.StringId))
                    ScheduleNextEvaluation(clan, initialSchedule: true);
            }
        }

        private Clan ResolveScheduledClan(string clanId)
        {
            if (_scheduledClansById.TryGetValue(clanId, out Clan clan)) return clan;
            clan = Clan.All.FirstOrDefault(candidate => candidate != null && candidate.StringId == clanId);
            if (clan != null) _scheduledClansById[clanId] = clan;
            return clan;
        }

        private void ScheduleNextEvaluation(Clan clan, bool initialSchedule, float? scheduledFromDay = null)
        {
            if (clan == null || string.IsNullOrWhiteSpace(clan.StringId))
                return;

            int daysPerYear = GetCampaignDaysInYear();
            float minDays = Math.Max(1f, daysPerYear * C.ClaimFeudEvaluationMinYears);
            float randomDays = Math.Max(1f, daysPerYear * C.ClaimFeudEvaluationRandomYears);
            float delay = initialSchedule
                ? GetStableClanDayOffset(clan, Math.Max(1, (int)Math.Ceiling(minDays + randomDays)))
                : minDays + MBRandom.RandomFloat * randomDays;

            float dueDay = (scheduledFromDay ?? CurrentDay) + Math.Max(1f, delay);
            _nextClanEvaluationDays[clan.StringId] = dueDay;
            if (dueDay < _nextScheduledEvaluationDay)
                _nextScheduledEvaluationDay = dueDay;
        }

        private void OnClanDestroyed(Clan clan)
        {
            if (clan == null || string.IsNullOrWhiteSpace(clan.StringId))
                return;

            _nextClanEvaluationDays.Remove(clan.StringId);
            _scheduledClansById.Remove(clan.StringId);
            RecalculateNextScheduledEvaluationDay();
        }

        private void OnClanChangedKingdom(
            Clan clan,
            Kingdom oldKingdom,
            Kingdom newKingdom,
            ChangeKingdomAction.ChangeKingdomActionDetail detail,
            bool showNotification = true)
        {
            if (clan == null || string.IsNullOrWhiteSpace(clan.StringId))
                return;

            if (!IsValidPoliticalActionClan(clan))
            {
                _nextClanEvaluationDays.Remove(clan.StringId);
                _scheduledClansById.Remove(clan.StringId);
            }
            else
            {
                _scheduledClansById[clan.StringId] = clan;
                ScheduleNextEvaluation(clan, initialSchedule: true);
            }

            RecalculateNextScheduledEvaluationDay();
        }

        private void CachePoliticalBehaviors()
        {
            Campaign campaign = Campaign.Current;
            if (campaign == null)
                return;

            if (_usurpationBehavior == null)
                _usurpationBehavior = campaign.GetCampaignBehavior<FeudalTitleUsurpationBehavior>();
            if (_claimFeudBehavior == null)
                _claimFeudBehavior = campaign.GetCampaignBehavior<ClaimFeudBehavior>();
            if (_serviceBehavior == null)
                _serviceBehavior = FeudalServiceBehavior.Instance ?? campaign.GetCampaignBehavior<FeudalServiceBehavior>();
        }

        private void RecalculateNextScheduledEvaluationDay()
        {
            _nextScheduledEvaluationDay = _nextClanEvaluationDays.Count == 0
                ? float.MaxValue
                : _nextClanEvaluationDays.Values.Min();
        }


        private static int GetStableClanDayOffset(Clan clan, int interval)
        {
            if (clan == null || interval <= 1)
                return 1;

            unchecked
            {
                int hash = 17;
                string id = clan.StringId ?? clan.Name?.ToString() ?? string.Empty;
                foreach (char c in id)
                    hash = hash * 31 + c;

                return Math.Abs(hash) % interval + 1;
            }
        }

        private static int GetCampaignDaysInYear()
        {
            return Math.Max(1, CampaignTime.DaysInYear > 0 ? CampaignTime.DaysInYear : C.MarriageStrategyDaysPerYear);
        }

        private void EnsureCollectionsInitialized()
        {
            if (_nextClanEvaluationDays == null)
                _nextClanEvaluationDays = new Dictionary<string, float>();
            if (_dailyBatch == null)
                _dailyBatch = new List<string>();
        }

        private static float CurrentDay => (float)CampaignTime.Now.ToDays;

    }
}
