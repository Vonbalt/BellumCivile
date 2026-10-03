using System.Collections.Generic;

namespace BellumCivile
{
    internal static class CourtFactionRoster
    {
        internal static readonly IReadOnlyList<FactionType> Types = new[]
        {
            FactionType.Nobility, FactionType.Glory, FactionType.Liberty
        };
    }
}
