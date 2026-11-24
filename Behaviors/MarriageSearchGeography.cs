using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;

namespace BellumCivile.Behaviors
{
    internal enum DistantMarriageSearch { None, RoyalHouses, AnyHouse }

    internal static class MarriageSearchScope
    {
        internal const int DistantHouseLimit = 8;
        internal const int DistantPairLimit = 48;

        internal static DistantMarriageSearch DistantPass(bool household, bool royal, bool hasLocalMatch,
            float dynasticRisk, int localLegalHouses)
        {
            if (!hasLocalMatch && (localLegalHouses < 2 || !household && dynasticRisk >= .5f))
                return DistantMarriageSearch.AnyHouse;
            return royal && !household ? DistantMarriageSearch.RoyalHouses : DistantMarriageSearch.None;
        }

        internal static int DistantStart(string houseId, int day, int count) => count <= 0 ? 0
            : (int)((MarriageOutcome.AnnualOffset(houseId, count) + (long)Math.Max(0, day)) % count);
    }

    internal sealed class MarriageRealmGeography
    {
        private readonly HashSet<(Kingdom, Kingdom)> _neighbors = new HashSet<(Kingdom, Kingdom)>();
        private bool _available;

        internal MarriageRealmGeography(bool available) => _available = available;

        internal bool IsLocal(Kingdom first, Kingdom second) => !_available || first == null || second == null
            || first == second || _neighbors.Contains((first, second));

        internal void Add(Kingdom first, Kingdom second)
        {
            if (first == null || second == null || first == second) return;
            _neighbors.Add((first, second));
            _neighbors.Add((second, first));
        }

        internal static MarriageRealmGeography Capture(Func<Clan, Kingdom> realmFor)
        {
            var model = Campaign.Current?.Models?.MapDistanceModel;
            var geography = new MarriageRealmGeography(false);
            if (model == null) return geography;

            try
            {
                // Read the engine's cached fortification graph once per market, never pathfind for pairs.
                bool hasNavigationData = false;
                foreach (var settlement in Settlement.All)
                {
                    var town = settlement?.Town;
                    if (town == null || (!settlement.IsTown && !settlement.IsCastle)) continue;
                    var realm = realmFor(settlement.OwnerClan);
                    if (realm == null) continue;
                    var neighbors = model.GetNeighborsOfFortification(town, MobileParty.NavigationType.All);
                    if (neighbors == null) return geography;
                    hasNavigationData |= neighbors.Count > 0;
                    foreach (var neighbor in neighbors)
                        geography.Add(realm, realmFor(neighbor?.OwnerClan));
                }
                geography._available = hasNavigationData;
            }
            catch (Exception ex)
            {
                // Unknown geography must not be mistaken for no reachable matches.
                BellumCivileDebug.TraceIfEnabled("marriage", $"Marriage neighborhood unavailable: {ex.Message}");
            }
            return geography;
        }
    }
}
