using System;
using System.Collections.Generic;
using BellumCivile.Behaviors;
using TaleWorlds.CampaignSystem;

namespace BellumCivile
{
    // Only surrounds synchronous AI candidate search. Never retain this across campaign ticks
    // or settlement execution: availability and prices must be checked again at that boundary.
    internal sealed class TreatyDraftReadScope : IDisposable
    {
        [ThreadStatic] private static TreatyDraftReadScope _current;
        private readonly TreatyDraftReadScope _previous;
        private readonly Dictionary<Tuple<Type, object, object, object>, object> _reads =
            new Dictionary<Tuple<Type, object, object, object>, object>();
        private bool _disposed;

        internal TreatyDraftReadScope()
        {
            _previous = _current;
            _current = this;
        }

        internal static T Read<T>(object first, object second, object third, Func<T> read)
        {
            if (_current == null) return read();
            var key = Tuple.Create(typeof(T), first, second, third);
            if (_current._reads.TryGetValue(key, out object cached)) return (T)cached;
            T value = read();
            _current._reads.Add(key, value);
            return value;
        }

        internal static Hero GetHeir(Kingdom kingdom)
        {
            return Read(kingdom, null, null,
                () => Campaign.Current?.GetCampaignBehavior<DynasticHeirBehavior>()?.GetDynasticHeir(kingdom));
        }

        public void Dispose()
        {
            if (_disposed) return;
            _current = _previous;
            _reads.Clear();
            _disposed = true;
        }
    }
}
