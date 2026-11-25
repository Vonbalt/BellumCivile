using System;
using System.Diagnostics;
using System.Globalization;
using System.Linq;

namespace BellumCivile
{
    internal sealed class FeudalPoliticalTiming
    {
        internal enum Stage { Total, Usurpation, Landed, FabricationSearch, Service, Formation }
        private static readonly string[] Labels = { "clan", "usurpation", "landed", "fabrication_search", "service", "formation" };
        private readonly int[] _counts = new int[Labels.Length];
        private readonly long[] _ticks = new long[Labels.Length];
        private readonly long[] _peaks = new long[Labels.Length];
        private int _year = -1;
        private int _jobsQueued;
        private int _batchPeak;

        internal void Record(Stage stage, long startedAt)
        {
            int index = (int)stage;
            long elapsed = Stopwatch.GetTimestamp() - startedAt;
            _counts[index]++;
            _ticks[index] += elapsed;
            _peaks[index] = Math.Max(_peaks[index], elapsed);
        }

        internal void RecordBatch(int count)
        {
            _jobsQueued += count;
            _batchPeak = Math.Max(_batchPeak, count);
        }

        internal void FlushIfYearChanged(int year)
        {
            if (year == _year) return;
            if (_year >= 0 && (_jobsQueued > 0 || _counts[0] > 0))
            {
                string stages = string.Join("; ", Labels.Select((name, i) =>
                    $"{name}:count={_counts[i]},total_ms={Milliseconds(_ticks[i])},peak_ms={Milliseconds(_peaks[i])}"));
                BellumCivileLogger.LogYearlyReport("feudal politics",
                    $"year={_year}; jobs_queued={_jobsQueued}; peak_daily_batch={_batchPeak}; {stages}");
            }
            _year = year;
            _jobsQueued = _batchPeak = 0;
            Array.Clear(_counts, 0, _counts.Length);
            Array.Clear(_ticks, 0, _ticks.Length);
            Array.Clear(_peaks, 0, _peaks.Length);
        }

        private static string Milliseconds(long ticks) =>
            (ticks * 1000d / Stopwatch.Frequency).ToString("0.###", CultureInfo.InvariantCulture);
    }
}
