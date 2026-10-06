using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TaleWorlds.CampaignSystem;
using C = BellumCivile.BellumCivileConstants;

namespace BellumCivile.Behaviors
{
    public sealed class InfluenceBudgetTelemetryBehavior : CampaignBehaviorBase
    {
        private int _summaryYearIndex = -1;
        private readonly Dictionary<string, InfluenceSourceTelemetry> _sources =
            new Dictionary<string, InfluenceSourceTelemetry>(StringComparer.OrdinalIgnoreCase);

        public override void RegisterEvents()
        {
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
        }

        public override void SyncData(IDataStore dataStore)
        {
        }

        internal void RecordSpend(
            Clan clan,
            string source,
            NpcInfluenceExpenseKind kind,
            float requested,
            float applied,
            bool blocked,
            float protectedReserve)
        {
            if (!IsTrackedNpcClan(clan))
                return;

            FlushSummaryIfYearChanged();
            InfluenceSourceTelemetry telemetry = GetSource(source, kind);
            telemetry.Requested += Math.Max(0f, requested);
            telemetry.Applied += Math.Max(0f, applied);
            if (applied > 0f)
                telemetry.Actions++;
            if (blocked)
                telemetry.Blocked++;
            else if (applied + 0.001f < requested)
                telemetry.Downgraded++;
            telemetry.HighestReserve = Math.Max(telemetry.HighestReserve, Math.Max(0f, protectedReserve));
            telemetry.Observe(clan.Influence);
        }

        internal void RecordBlocked(
            Clan clan,
            string source,
            NpcInfluenceExpenseKind kind,
            float requested,
            float protectedReserve)
        {
            if (!IsTrackedNpcClan(clan))
                return;

            FlushSummaryIfYearChanged();
            InfluenceSourceTelemetry telemetry = GetSource(source, kind);
            telemetry.Requested += Math.Max(0f, requested);
            telemetry.Blocked++;
            telemetry.HighestReserve = Math.Max(telemetry.HighestReserve, Math.Max(0f, protectedReserve));
            telemetry.Observe(clan.Influence);
        }

        internal void RecordLoss(Clan clan, string source, float requested, float applied, bool hostile)
        {
            if (!IsTrackedNpcClan(clan))
                return;

            FlushSummaryIfYearChanged();
            InfluenceSourceTelemetry telemetry = GetSource(source, NpcInfluenceExpenseKind.InvoluntaryLoss);
            telemetry.Requested += Math.Max(0f, requested);
            telemetry.Applied += Math.Max(0f, applied);
            telemetry.Prevented += Math.Max(0f, requested - applied);
            telemetry.Actions++;
            if (hostile)
                telemetry.HostileEvents++;
            telemetry.Observe(clan.Influence);
        }

        internal void RecordRefund(
            Clan clan,
            string source,
            NpcInfluenceExpenseKind kind,
            float amount)
        {
            if (!IsTrackedNpcClan(clan))
                return;

            FlushSummaryIfYearChanged();
            InfluenceSourceTelemetry telemetry = GetSource(source, kind);
            telemetry.Applied = Math.Max(0f, telemetry.Applied - Math.Max(0f, amount));
            telemetry.Refunded += Math.Max(0f, amount);
            telemetry.Observe(clan.Influence);
        }

        private void OnDailyTick()
        {
            FlushSummaryIfYearChanged();
        }

        private void FlushSummaryIfYearChanged()
        {
            int daysPerYear = GetCampaignDaysInYear();
            int currentYearIndex = (int)(CampaignTime.Now.ToDays / daysPerYear);
            if (_summaryYearIndex < 0)
            {
                _summaryYearIndex = currentYearIndex;
                return;
            }

            if (currentYearIndex == _summaryYearIndex)
                return;

            BellumCivileDebug.TraceYearlyReport(
                "influence",
                BuildReport(_summaryYearIndex, daysPerYear));
            _sources.Clear();
            _summaryYearIndex = currentYearIndex;
        }

        private string BuildReport(int yearIndex, int daysPerYear)
        {
            List<Clan> clans = Kingdom.All
                .Where(kingdom => kingdom != null
                    && !kingdom.IsEliminated
                    && !BellumKingdomVisibilityHelper.IsTemporaryBellumKingdom(kingdom))
                .SelectMany(kingdom => kingdom.Clans)
                .Where(IsTrackedNpcClan)
                .Distinct()
                .ToList();

            float totalApplied = _sources.Values.Sum(source => source.Applied);
            float totalRequested = _sources.Values.Sum(source => source.Requested);
            float prevented = _sources.Values.Sum(source => source.Prevented);
            float refunded = _sources.Values.Sum(source => source.Refunded);
            int actions = _sources.Values.Sum(source => source.Actions);
            int blocked = _sources.Values.Sum(source => source.Blocked);
            int downgraded = _sources.Values.Sum(source => source.Downgraded);
            int hostile = _sources.Values.Sum(source => source.HostileEvents);

            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"yearly NPC influence summary: campaign year {yearIndex + 1} ({daysPerYear} days/year)");
            sb.AppendLine($"budget_activity: actions={actions}; requested={totalRequested:0.0}; applied={totalApplied:0.0}; refunded={refunded:0.0}; reserve_blocks={blocked}; downgraded={downgraded}; prevented_losses={prevented:0.0}; hostile_loss_events={hostile}");
            sb.AppendLine($"end_state: clans={clans.Count}; average={AverageInfluence(clans):0.0}; minimum={MinimumInfluence(clans):0.0}; negative={clans.Count(clan => clan.Influence < 0f)}; at_or_below_100={clans.Count(clan => clan.Influence <= 100f)}; below_clan_reserve={clans.Count(clan => clan.Influence < C.NpcInfluenceClanReserve)}; below_role_reserve={clans.Count(clan => clan.Influence < NpcInfluenceBudgetService.GetRoleReserve(clan))}; rulers_below_ruler_reserve={clans.Count(clan => clan.Kingdom?.RulingClan == clan && clan.Influence < NpcInfluenceBudgetService.GetRoleReserve(clan))}");

            foreach (IGrouping<NpcInfluenceExpenseKind, InfluenceSourceTelemetry> kind in _sources.Values
                .GroupBy(source => source.Kind)
                .OrderBy(group => group.Key))
            {
                sb.AppendLine($"kind: id={kind.Key}; actions={kind.Sum(source => source.Actions)}; requested={kind.Sum(source => source.Requested):0.0}; applied={kind.Sum(source => source.Applied):0.0}; blocked={kind.Sum(source => source.Blocked)}; downgraded={kind.Sum(source => source.Downgraded)}; prevented={kind.Sum(source => source.Prevented):0.0}");
            }

            foreach (InfluenceSourceTelemetry source in _sources.Values
                .OrderByDescending(value => value.Applied)
                .ThenBy(value => value.Source, StringComparer.OrdinalIgnoreCase))
            {
                sb.AppendLine($"source: id={source.Source}; kind={source.Kind}; actions={source.Actions}; requested={source.Requested:0.0}; applied={source.Applied:0.0}; refunded={source.Refunded:0.0}; blocked={source.Blocked}; downgraded={source.Downgraded}; prevented={source.Prevented:0.0}; hostile={source.HostileEvents}; lowest_after={FormatMinimum(source.LowestObserved)}; highest_reserve={source.HighestReserve:0.0}");
            }

            return sb.ToString().TrimEnd();
        }

        private InfluenceSourceTelemetry GetSource(string source, NpcInfluenceExpenseKind kind)
        {
            string normalizedSource = string.IsNullOrWhiteSpace(source) ? "unknown" : source.Trim();
            string key = kind + "|" + normalizedSource;
            if (!_sources.TryGetValue(key, out InfluenceSourceTelemetry telemetry))
            {
                telemetry = new InfluenceSourceTelemetry(normalizedSource, kind);
                _sources[key] = telemetry;
            }

            return telemetry;
        }

        private static bool IsTrackedNpcClan(Clan clan)
        {
            return clan != null
                && clan != Clan.PlayerClan
                && !clan.IsEliminated
                && !clan.IsBanditFaction
                && clan.Leader != null;
        }

        private static float AverageInfluence(List<Clan> clans)
        {
            return clans.Count == 0 ? 0f : clans.Average(clan => clan.Influence);
        }

        private static float MinimumInfluence(List<Clan> clans)
        {
            return clans.Count == 0 ? 0f : clans.Min(clan => clan.Influence);
        }

        private static string FormatMinimum(float value)
        {
            return value == float.MaxValue ? "n/a" : value.ToString("0.0");
        }

        private static int GetCampaignDaysInYear()
        {
            return Math.Max(1, CampaignTime.DaysInYear > 0 ? CampaignTime.DaysInYear : C.MarriageStrategyDaysPerYear);
        }

        private sealed class InfluenceSourceTelemetry
        {
            public string Source { get; }
            public NpcInfluenceExpenseKind Kind { get; }
            public int Actions { get; set; }
            public int Blocked { get; set; }
            public int Downgraded { get; set; }
            public int HostileEvents { get; set; }
            public float Requested { get; set; }
            public float Applied { get; set; }
            public float Prevented { get; set; }
            public float Refunded { get; set; }
            public float HighestReserve { get; set; }
            public float LowestObserved { get; private set; } = float.MaxValue;

            public InfluenceSourceTelemetry(string source, NpcInfluenceExpenseKind kind)
            {
                Source = source;
                Kind = kind;
            }

            public void Observe(float influence)
            {
                LowestObserved = Math.Min(LowestObserved, influence);
            }
        }
    }
}
