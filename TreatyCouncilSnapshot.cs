using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;

namespace BellumCivile
{
    /// <summary>
    /// Runtime-only parley context. A treaty is negotiated against the political situation present
    /// when the session opens, rather than repeatedly sampling War Will and influence while UI controls change.
    /// </summary>
    internal sealed class TreatyCouncilSnapshot
    {
        private readonly Dictionary<string, TreatyCouncilClanSnapshot> _clans;

        private TreatyCouncilSnapshot(IEnumerable<TreatyCouncilClanSnapshot> clans)
        {
            _clans = (clans ?? Enumerable.Empty<TreatyCouncilClanSnapshot>())
                .Where(snapshot => snapshot != null && !string.IsNullOrWhiteSpace(snapshot.ClanId))
                .ToDictionary(snapshot => snapshot.ClanId, snapshot => snapshot);
        }

        public static TreatyCouncilSnapshot Capture(IEnumerable<Kingdom> kingdoms, Func<Clan, float> warWillProvider)
        {
            List<TreatyCouncilClanSnapshot> clans = new List<TreatyCouncilClanSnapshot>();
            foreach (Clan clan in (kingdoms ?? Enumerable.Empty<Kingdom>())
                .Where(kingdom => kingdom != null)
                .SelectMany(kingdom => kingdom.Clans)
                .Where(clan => clan != null && !clan.IsEliminated && clan.Leader != null && !clan.IsUnderMercenaryService)
                .Distinct())
            {
                clans.Add(new TreatyCouncilClanSnapshot(
                    clan.StringId,
                    warWillProvider?.Invoke(clan) ?? 50f,
                    clan.CurrentTotalStrength,
                    clan.Influence));
            }
            return new TreatyCouncilSnapshot(clans);
        }

        public float GetEnthusiasm(Clan clan, float fallback)
        {
            return TryGet(clan, out TreatyCouncilClanSnapshot snapshot) ? snapshot.Enthusiasm : fallback;
        }

        public float GetStrength(Clan clan, float fallback)
        {
            return TryGet(clan, out TreatyCouncilClanSnapshot snapshot) ? snapshot.Strength : fallback;
        }

        public float GetInfluence(Clan clan, float fallback)
        {
            return TryGet(clan, out TreatyCouncilClanSnapshot snapshot) ? snapshot.Influence : fallback;
        }

        private bool TryGet(Clan clan, out TreatyCouncilClanSnapshot snapshot)
        {
            snapshot = null;
            return clan != null && !string.IsNullOrWhiteSpace(clan.StringId) && _clans.TryGetValue(clan.StringId, out snapshot);
        }
    }

    internal sealed class TreatyCouncilClanSnapshot
    {
        public string ClanId { get; }
        public float Enthusiasm { get; }
        public float Strength { get; }
        public float Influence { get; }

        public TreatyCouncilClanSnapshot(string clanId, float enthusiasm, float strength, float influence)
        {
            ClanId = clanId ?? string.Empty;
            Enthusiasm = Math.Max(0f, Math.Min(100f, enthusiasm));
            Strength = Math.Max(0f, strength);
            Influence = Math.Max(0f, influence);
        }
    }
}
