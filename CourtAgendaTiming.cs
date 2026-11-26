using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;

namespace BellumCivile
{
    internal enum CourtMaintenanceStep
    {
        ExistingBusiness,
        PeaceObjectives,
        LiberationObjectives,
        CampaignObjectives,
        SubjugationObjectives,
        ClaimObjectives,
        DynasticObjectives,
        TradeObjectives,
        TitleGrants,
        ClientGrants,
        Rallies,
        ProtectionOffers,
        CrownReconciliation,
        RoyalPeaceCases,
        RealmRoster,
        MandateCheck,
        MandateRenewal,
        HereditaryLoyalty,
        DecreeCases,
        CouncilPriority,
        EmergencyChallenges,
        AgendaAdvance,
        PruneState,
        ExecutiveResults,
        SyncObjectiveRecords
    }

    internal sealed class CourtAgendaTiming
    {
        private static readonly string[] MaintenanceStepNames = Enum.GetNames(typeof(CourtMaintenanceStep));
        private static readonly long SlowMaintenanceTicks = Stopwatch.Frequency / 20; // 50 ms

        private struct Sample
        {
            internal int Count;
            internal long Total;
            internal long Peak;

            internal void Add(long ticks)
            {
                Count++;
                Total += ticks;
                Peak = Math.Max(Peak, ticks);
            }

            public override string ToString() => $"count={Count},total_ms={Ms(Total)},peak_ms={Ms(Peak)}";
        }

        private struct MaintenanceSample
        {
            internal Sample Timing;
            internal string PeakRealm;

            internal void Add(long ticks, string realmId)
            {
                if (Timing.Count == 0 || ticks > Timing.Peak) PeakRealm = realmId;
                Timing.Add(ticks);
            }

            internal void Merge(MaintenanceSample other)
            {
                if (other.Timing.Count == 0) return;
                if (Timing.Count == 0 || other.Timing.Peak > Timing.Peak)
                {
                    Timing.Peak = other.Timing.Peak;
                    PeakRealm = other.PeakRealm;
                }
                Timing.Count += other.Timing.Count;
                Timing.Total += other.Timing.Total;
            }

            public override string ToString() =>
                $"calls={Timing.Count},total_ms={Ms(Timing.Total)},peak_call_ms={Ms(Timing.Peak)}"
                + (PeakRealm == null ? string.Empty : $",peak_realm={PeakRealm}");
        }

        private readonly List<(string RealmId, long Total, long Selection)> _realms = new List<(string, long, long)>();
        private readonly MaintenanceSample[] _dailySteps = new MaintenanceSample[MaintenanceStepNames.Length];
        private readonly MaintenanceSample[] _yearlySteps = new MaintenanceSample[MaintenanceStepNames.Length];
        private Sample _daily, _maintenance, _preparation, _realm, _selection;
        private string _peakRealm, _peakSelectionRealm;
        private int? _year;
        private long _preparationTicks, _selectionTicks;

        internal void BeginPass(int year)
        {
            if (year != _year)
            {
                if (_year.HasValue && _daily.Count > 0)
                {
                    BellumCivileLogger.LogYearlyReport("court timing", $"year={_year}; daily:{_daily}; maintenance:{_maintenance}; "
                        + $"preparation:{_preparation}; realm:{_realm},peak_realm={_peakRealm ?? "none"}; "
                        + $"selection:{_selection},peak_realm={_peakSelectionRealm ?? "none"}");
                    if (_yearlySteps.Any(s => s.Timing.Count > 0))
                        BellumCivileLogger.LogYearlyReport("court maintenance steps", $"year={_year}; steps={DescribeSteps(_yearlySteps)}");
                }
                _year = year;
                _daily = _maintenance = _preparation = _realm = _selection = default;
                _peakRealm = _peakSelectionRealm = null;
                Array.Clear(_yearlySteps, 0, _yearlySteps.Length);
            }
            _realms.Clear();
            _preparationTicks = _selectionTicks = 0;
        }

        internal void BeginDay(int year)
        {
            BeginPass(year);
            Array.Clear(_dailySteps, 0, _dailySteps.Length);
        }

        internal void RecordMaintenanceStep(CourtMaintenanceStep step, long ticks, string realmId) =>
            _dailySteps[(int)step].Add(ticks, realmId);

        internal void RecordPreparation(long ticks) => _preparationTicks = ticks;
        internal void RecordSelection(long ticks) => _selectionTicks += ticks;

        internal void RecordRealm(string realmId, long ticks)
        {
            if (_realm.Count == 0 || ticks > _realm.Peak) _peakRealm = realmId;
            _realm.Add(ticks);
            if (_selectionTicks > 0)
            {
                if (_selection.Count == 0 || _selectionTicks > _selection.Peak) _peakSelectionRealm = realmId;
                _selection.Add(_selectionTicks);
            }
            _realms.Add((realmId, ticks, _selectionTicks));
            _selectionTicks = 0;
        }

        internal void EndDay(double day, long ticks)
        {
            long maintenance = Math.Max(0, ticks - _preparationTicks);
            _daily.Add(ticks);
            _maintenance.Add(maintenance);
            if (_preparationTicks > 0) _preparation.Add(_preparationTicks);
            for (int i = 0; i < _dailySteps.Length; i++) _yearlySteps[i].Merge(_dailySteps[i]);
            if (maintenance >= SlowMaintenanceTicks)
            {
                long unattributed = Math.Max(0, maintenance - _dailySteps.Sum(s => s.Timing.Total));
                BellumCivileLogger.Log($"Court maintenance timing; day={day.ToString("0.###", CultureInfo.InvariantCulture)}; "
                    + $"maintenance_ms={Ms(maintenance)}; unattributed_ms={Ms(unattributed)}; steps={DescribeSteps(_dailySteps)}");
            }
            if (_realms.Count > 0)
                ReportPreparation(day, _preparationTicks, $"daily_ms={Ms(ticks)}; maintenance_ms={Ms(maintenance)}; ");
        }

        internal void EndHour(double day, long ticks)
        {
            _preparation.Add(ticks);
            if (_realms.Count > 0) ReportPreparation(day, ticks, "source=hourly; ");
        }

        private void ReportPreparation(double day, long ticks, string tickInfo)
        {
            string realms = string.Join(" | ", _realms.Select(r => $"{r.RealmId}:term_ms={Ms(r.Total)},selection_ms={Ms(r.Selection)}"));
            BellumCivileLogger.Log($"Court timing; day={day.ToString("0.###", CultureInfo.InvariantCulture)}; {tickInfo}"
                + $"preparation_ms={Ms(ticks)}; "
                + $"queue_ms={Ms(Math.Max(0, ticks - _realms.Sum(r => r.Total)))}; prepared={_realms.Count}; {realms}");
        }

        private static string DescribeSteps(MaintenanceSample[] steps) => string.Join(" | ", Enumerable.Range(0, steps.Length)
            .Where(i => steps[i].Timing.Count > 0)
            .OrderByDescending(i => steps[i].Timing.Total)
            .Select(i => $"{MaintenanceStepNames[i]}:{steps[i]}"));

        private static string Ms(long ticks) => (ticks * 1000d / Stopwatch.Frequency).ToString("0.###", CultureInfo.InvariantCulture);
    }
}
