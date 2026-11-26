using System.Diagnostics;
using System.Globalization;
using System.Linq;

namespace BellumCivile
{
    internal sealed class CourtCoalitionTiming
    {
        // RulerForecast through Concession are subphases of a synchronous ultimatum.
        internal enum Phase { Assembly, CoalitionForecast, Registration, Ultimatum, RulerForecast, Rally, WarCreation, Concession }
        private static readonly string[] Labels =
            { "assembly", "coalition_forecast", "registration", "ultimatum", "ruler_forecast", "rally", "war_creation", "concession" };
        private readonly long?[] _ticks = new long?[Labels.Length];
        private readonly long _started = Stopwatch.GetTimestamp();
        private readonly double _day;
        private readonly string _realmId, _leaderId;
        private readonly FactionType _bloc;
        private readonly bool _playerResponse;

        internal int AssembledHouses { get; set; }
        internal UltimatumResolution Result { get; set; }
        internal string BlockedReason { get; set; }

        internal CourtCoalitionTiming(double day, string realmId, FactionType bloc, string leaderId, bool playerResponse = false)
        {
            _day = day;
            _realmId = realmId;
            _bloc = bloc;
            _leaderId = leaderId;
            _playerResponse = playerResponse;
        }

        internal CourtCoalitionTiming ForPlayerResponse(double day) =>
            new CourtCoalitionTiming(day, _realmId, _bloc, _leaderId, playerResponse: true) { AssembledHouses = AssembledHouses };

        internal void Finish(Phase phase, long started) => Record(phase, Stopwatch.GetTimestamp() - started);

        internal void Record(Phase phase, long ticks) => _ticks[(int)phase] = ticks;

        internal void Report()
        {
            string line = Describe(Stopwatch.GetTimestamp() - _started);
            if (line != null) BellumCivileLogger.Log(line);
        }

        internal string Describe(long ticks)
        {
            // Issued ultimatums are rare; omit cheap attempts that never issued one.
            if (Result == UltimatumResolution.Failed && ticks < Stopwatch.Frequency / 100) return null;
            string phases = string.Join("; ", Enumerable.Range(0, Labels.Length).Where(i => _ticks[i].HasValue)
                .Select(i => $"{Labels[i]}_ms={Ms(_ticks[i].Value)}"));
            string blocked = BlockedReason == null ? "" : $"; blocked={BlockedReason}";
            return $"Court coalition timing; day={_day.ToString("0.###", CultureInfo.InvariantCulture)}; realm={_realmId}; "
                + $"bloc={_bloc}; leader={_leaderId}; source={(_playerResponse ? "player_response" : "launch")}; "
                + $"result={Result}{blocked}; assembled_houses={AssembledHouses}; total_ms={Ms(ticks)}"
                + (phases.Length == 0 ? "" : $"; {phases}");
        }

        private static string Ms(long ticks) => (ticks * 1000d / Stopwatch.Frequency).ToString("0.###", CultureInfo.InvariantCulture);
    }
}
