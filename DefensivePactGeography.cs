using System;
using System.Collections.Generic;

namespace BellumCivile
{
    // Traverses a supplied frontier cache only; the caller controls cache invalidation and rebuild cost.
    internal static class DefensivePactGeography
    {
        internal static IReadOnlyDictionary<string, int> ReachWithinThree(
            string targetId, Func<string, IEnumerable<string>> neighbours)
        {
            if (string.IsNullOrWhiteSpace(targetId)) throw new ArgumentException(nameof(targetId));
            if (neighbours == null) throw new ArgumentNullException(nameof(neighbours));
            var distances = new Dictionary<string, int>(StringComparer.Ordinal) { [targetId] = 0 };
            var queue = new Queue<string>();
            queue.Enqueue(targetId);
            while (queue.Count > 0)
            {
                string current = queue.Dequeue();
                int nextDistance = distances[current] + 1;
                if (nextDistance > 3) continue;
                foreach (string next in neighbours(current) ?? Array.Empty<string>())
                {
                    if (string.IsNullOrWhiteSpace(next) || distances.ContainsKey(next)) continue;
                    distances[next] = nextDistance;
                    queue.Enqueue(next);
                }
            }
            distances.Remove(targetId);
            return distances;
        }
    }
}
