using System;
using TaleWorlds.CampaignSystem;

namespace BellumCivile
{
    internal readonly struct ClanPairKey : IEquatable<ClanPairKey>
    {
        private readonly Clan _firstClan;
        private readonly Clan _secondClan;

        public ClanPairKey(Clan firstClan, Clan secondClan)
        {
            if (CompareClans(firstClan, secondClan) <= 0)
            {
                _firstClan = firstClan;
                _secondClan = secondClan;
            }
            else
            {
                _firstClan = secondClan;
                _secondClan = firstClan;
            }
        }

        public bool Equals(ClanPairKey other)
        {
            return ReferenceEquals(_firstClan, other._firstClan)
                && ReferenceEquals(_secondClan, other._secondClan);
        }

        public override bool Equals(object obj)
        {
            return obj is ClanPairKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int firstHash = _firstClan != null ? _firstClan.GetHashCode() : 0;
                int secondHash = _secondClan != null ? _secondClan.GetHashCode() : 0;
                return (firstHash * 397) ^ secondHash;
            }
        }

        private static int CompareClans(Clan firstClan, Clan secondClan)
        {
            string first = firstClan?.StringId ?? string.Empty;
            string second = secondClan?.StringId ?? string.Empty;
            return string.CompareOrdinal(first, second);
        }
    }
}
