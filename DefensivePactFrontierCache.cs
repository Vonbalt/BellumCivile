using System;
using System.Collections.Generic;
using System.Linq;

namespace BellumCivile
{
    // Pair queries may be expensive; callers budget Step across campaign days.
    internal sealed class DefensivePactFrontierCache
    {
        private readonly string[] _realms;
        private readonly Dictionary<string, int> _indexes;
        private readonly sbyte[,] _edges;
        private readonly int[,] _revisions;
        private readonly Queue<int> _pending = new Queue<int>();
        private readonly HashSet<int> _queued = new HashSet<int>();
        private int _lastPriorityCode = -1;

        internal DefensivePactFrontierCache(IEnumerable<string> realmIds)
        {
            if (realmIds == null) throw new ArgumentNullException(nameof(realmIds));
            _realms = realmIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal)
                .OrderBy(id => id, StringComparer.Ordinal).ToArray();
            _indexes = _realms.Select((id, index) => new { id, index })
                .ToDictionary(entry => entry.id, entry => entry.index, StringComparer.Ordinal);
            _edges = new sbyte[_realms.Length, _realms.Length];
            _revisions = new int[_realms.Length, _realms.Length];
            for (int i = 0; i < _realms.Length; i++)
                for (int j = i + 1; j < _realms.Length; j++) Enqueue(i, j);
        }

        internal int PendingCount => _pending.Count;

        internal void Prioritize(string firstId, string secondId)
        {
            if (!_indexes.TryGetValue(firstId ?? string.Empty, out int first)
                || !_indexes.TryGetValue(secondId ?? string.Empty, out int second) || first == second || _pending.Count == 0) return;
            var urgent = new Queue<int>();
            var other = new Queue<int>();
            int direct = Math.Min(first, second) * _realms.Length + Math.Max(first, second);
            if (_lastPriorityCode == direct) return;
            _lastPriorityCode = direct;
            bool directPending = false;
            while (_pending.Count > 0)
            {
                int code = _pending.Dequeue();
                if (code == direct) { directPending = true; continue; }
                int a = code / _realms.Length, b = code % _realms.Length;
                if (a == first || b == first || a == second || b == second) urgent.Enqueue(code);
                else other.Enqueue(code);
            }
            if (directPending) _pending.Enqueue(direct);
            while (urgent.Count > 0) _pending.Enqueue(urgent.Dequeue());
            while (other.Count > 0) _pending.Enqueue(other.Dequeue());
        }

        internal void Invalidate(string realmId)
        {
            if (string.IsNullOrWhiteSpace(realmId) || !_indexes.TryGetValue(realmId, out int index)) return;
            _lastPriorityCode = -1;
            for (int other = 0; other < _realms.Length; other++)
            {
                if (index == other) continue;
                int first = Math.Min(index, other), second = Math.Max(index, other);
                _edges[first, second] = _edges[second, first] = 0;
                _revisions[first, second]++;
                Enqueue(first, second);
            }
        }

        internal int Step(int budget, Func<string, string, bool?> areNeighbors)
        {
            if (budget < 0) throw new ArgumentOutOfRangeException(nameof(budget));
            if (areNeighbors == null) throw new ArgumentNullException(nameof(areNeighbors));
            int attempts = Math.Min(budget, _pending.Count);
            for (int n = 0; n < attempts; n++)
            {
                int code = _pending.Dequeue();
                _queued.Remove(code);
                int first = code / _realms.Length, second = code % _realms.Length;
                int revision = _revisions[first, second];
                bool? neighbors;
                try { neighbors = areNeighbors(_realms[first], _realms[second]); }
                catch { Enqueue(first, second); throw; }
                if (_revisions[first, second] != revision || !neighbors.HasValue)
                {
                    Enqueue(first, second);
                    continue;
                }
                _edges[first, second] = _edges[second, first] = neighbors.Value ? (sbyte)1 : (sbyte)-1;
            }
            return attempts;
        }

        internal bool HasKnownDirectFrontier(string firstId, string secondId) =>
            !string.IsNullOrWhiteSpace(firstId) && !string.IsNullOrWhiteSpace(secondId)
            && _indexes.TryGetValue(firstId, out int first) && _indexes.TryGetValue(secondId, out int second)
            && _edges[first, second] > 0;

        // Unknown edges are considered both absent and present; only agree-on-both results are returned.
        internal bool TryGetDistance(string firstId, string secondId, out int distance)
        {
            distance = 0;
            if (string.IsNullOrWhiteSpace(firstId) || string.IsNullOrWhiteSpace(secondId)
                || !_indexes.TryGetValue(firstId, out int first) || !_indexes.TryGetValue(secondId, out int second)
                || first == second) return false;
            if (_edges[first, second] > 0) { distance = 1; return true; }
            int known = Distance(first, second, false);
            int possible = Distance(first, second, true);
            if (known != possible) return false;
            distance = known;
            return true;
        }

        private int Distance(int first, int target, bool includeUnknown)
        {
            var visited = new bool[_realms.Length];
            var queue = new Queue<(int Realm, int Depth)>();
            visited[first] = true;
            queue.Enqueue((first, 0));
            while (queue.Count > 0)
            {
                var item = queue.Dequeue();
                if (item.Depth >= 3) continue;
                for (int next = 0; next < _realms.Length; next++)
                {
                    if (next == item.Realm || visited[next] || _edges[item.Realm, next] < 0
                        || (!includeUnknown && _edges[item.Realm, next] == 0)) continue;
                    if (next == target) return item.Depth + 1;
                    visited[next] = true;
                    queue.Enqueue((next, item.Depth + 1));
                }
            }
            return 0;
        }

        private void Enqueue(int first, int second)
        {
            int code = first * _realms.Length + second;
            if (_queued.Add(code)) _pending.Enqueue(code);
        }
    }
}
