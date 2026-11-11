using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using TaleWorlds.Localization;

namespace BellumCivile
{
    // Opt-in only; does not retain message contents or write per-event logs.
    internal static class NotificationPerformanceDiagnostics
    {
        private sealed class Counts
        {
            public long Shown;
            public long Filtered;
        }

        private const int MaxSources = 128;
        private static readonly object Sync = new object();
        private static readonly Dictionary<string, Counts> Sources = new Dictionary<string, Counts>();
        private static volatile bool _enabled;
        private static long _started, _ended, _shown, _filtered, _personal, _major, _ordinary, _formatTicks, _dispatchTicks;

        internal static bool Enabled => _enabled;

        internal static void Start()
        {
            lock (Sync)
            {
                Reset();
                _started = Stopwatch.GetTimestamp();
                _enabled = true;
            }
        }

        internal static string Stop()
        {
            lock (Sync)
            {
                if (_enabled) _ended = Stopwatch.GetTimestamp();
                _enabled = false;
                return Report();
            }
        }

        internal static string Snapshot()
        {
            lock (Sync) return Report();
        }

        private static void Reset()
        {
            Sources.Clear();
            _started = _ended = _shown = _filtered = _personal = _major = _ordinary = _formatTicks = _dispatchTicks = 0;
        }

        internal static string TextId(TextObject text)
        {
            string value = text?.Value;
            if (value != null && value.StartsWith("{=", StringComparison.Ordinal))
            {
                int end = value.IndexOf('}', 2);
                if (end > 2 && end <= 100) return value.Substring(2, end - 2);
            }
            return "unidentified-text";
        }

        internal static void Record(bool shown, bool personal, bool major, string source, long formatTicks, long dispatchTicks)
        {
            lock (Sync)
            {
                if (!_enabled) return;
                if (shown)
                {
                    _shown++;
                    if (personal) _personal++;
                    else if (major) _major++;
                    else _ordinary++;
                    _formatTicks += formatTicks;
                    _dispatchTicks += dispatchTicks;
                }
                else _filtered++;

                source = source ?? "unknown";
                if (!Sources.TryGetValue(source, out Counts counts))
                {
                    if (Sources.Count >= MaxSources) source = "other";
                    if (!Sources.TryGetValue(source, out counts)) Sources[source] = counts = new Counts();
                }
                if (shown) counts.Shown++; else counts.Filtered++;
            }
        }

        private static string Report()
        {
            long now = _enabled ? Stopwatch.GetTimestamp() : _ended;
            double seconds = _started == 0 ? 0 : (now - _started) / (double)Stopwatch.Frequency;
            double formatMs = _formatTicks * 1000d / Stopwatch.Frequency;
            double dispatchMs = _dispatchTicks * 1000d / Stopwatch.Frequency;
            string shownSources = string.Join(", ", Sources.Where(x => x.Value.Shown > 0).OrderByDescending(x => x.Value.Shown)
                .ThenBy(x => x.Key, StringComparer.Ordinal).Take(5).Select(x => $"{x.Key}={x.Value.Shown}"));
            string filteredSources = string.Join(", ", Sources.Where(x => x.Value.Filtered > 0).OrderByDescending(x => x.Value.Filtered)
                .ThenBy(x => x.Key, StringComparer.Ordinal).Take(5).Select(x => $"{x.Key}={x.Value.Filtered}"));
            return string.Format(CultureInfo.InvariantCulture,
                "Notification diagnostics ({0}, {1:F1}s): shown={2} [personal={3}, major={4}, other={5}], filtered={6}; render={7:F2}ms; synchronous dispatch={8:F2}ms; top shown: {9}; top filtered: {10}. Timings exclude message preparation and later native UI frames.",
                _enabled ? "running" : "stopped", seconds, _shown, _personal, _major, _ordinary, _filtered,
                formatMs, dispatchMs, string.IsNullOrEmpty(shownSources) ? "none" : shownSources,
                string.IsNullOrEmpty(filteredSources) ? "none" : filteredSources);
        }
    }
}
