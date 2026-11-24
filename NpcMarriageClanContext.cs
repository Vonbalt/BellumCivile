using System;
using TaleWorlds.CampaignSystem;

namespace BellumCivile
{
    // Pin the accepted household through native wedding callbacks, which run before clan transfer.
    internal sealed class NpcMarriageClanContext : IDisposable
    {
        [ThreadStatic] private static NpcMarriageClanContext _current;
        private readonly NpcMarriageClanContext _previous;
        private readonly Hero _first, _second;
        private readonly Clan _destination;

        internal NpcMarriageClanContext(Hero first, Hero second, Clan destination)
        {
            _first = first; _second = second; _destination = destination;
            _previous = _current; _current = this;
        }

        internal static bool TryResolve(Hero first, Hero second, out Clan destination)
        {
            var current = _current;
            destination = current != null && (current._first == first && current._second == second
                || current._first == second && current._second == first) ? current._destination : null;
            return destination != null;
        }

        public void Dispose() { _current = _previous; }
    }
}
