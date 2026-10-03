using System.Collections.Generic;
using System.Linq;

namespace BellumCivile
{
    public sealed class WarWillMotiveBreakdown
    {
        public float WarWill { get; }
        public WarWillVoteStance Stance { get; }
        public IReadOnlyList<WarWillMotiveLine> Motives { get; }
        public WarWillMotiveLine PrimaryMotive => Motives.OrderByDescending(line => System.Math.Abs(line.Amount)).FirstOrDefault();

        public WarWillMotiveBreakdown(float warWill, WarWillVoteStance stance, IEnumerable<WarWillMotiveLine> motives)
        {
            WarWill = warWill;
            Stance = stance;
            Motives = motives?.ToList() ?? new List<WarWillMotiveLine>();
        }
    }
}
