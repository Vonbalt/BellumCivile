using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;

namespace BellumCivile
{
    internal sealed class CourtAgendaTiming
    {
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

        private readonly List<(string RealmId, long Total, long Selection)> _realms = new List<(string, long, long)>();
        private Sample _daily, _maintenance, _preparation, _realm, _selection;
        private string _peakRealm, _peakSelectionRealm;
        private int? _year;
        private long _preparationTicks, _selectionTicks;

        internal void BeginDay(int year)
        {
            if (year != _year)
            {
                if (_year.HasValue && _daily.Count > 0)
                    BellumCivileLogger.LogYearlyReport("court timing", $"year={_year}; daily:{_daily}; maintenance:{_maintenance}; "
                        + $"preparation:{_preparation}; realm:{_realm},peak_realm={_peakRealm ?? "none"}; "
                        + $"selection:{_selection},peak_realm={_peakSelectionRealm ?? "none"}");
                _year = year;
                _daily = _maintenance = _preparation = _realm = _selection = default;
                _peakRealm = _peakSelectionRealm = null;
            }
            _realms.Clear();
            _preparationTicks = _selectionTicks = 0;
        }

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
            if (_realms.Count == 0) return;

            string realms = string.Join(" | ", _realms.Select(r => $"{r.RealmId}:term_ms={Ms(r.Total)},selection_ms={Ms(r.Selection)}"));
            BellumCivileLogger.Log($"Court timing; day={day.ToString("0.###", CultureInfo.InvariantCulture)}; daily_ms={Ms(ticks)}; "
                + $"maintenance_ms={Ms(maintenance)}; preparation_ms={Ms(_preparationTicks)}; "
                + $"queue_ms={Ms(Math.Max(0, _preparationTicks - _realms.Sum(r => r.Total)))}; prepared={_realms.Count}; {realms}");
        }

        private static string Ms(long ticks) => (ticks * 1000d / Stopwatch.Frequency).ToString("0.###", CultureInfo.InvariantCulture);
    }
}
