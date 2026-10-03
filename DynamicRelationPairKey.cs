using System;
using TaleWorlds.CampaignSystem;

namespace BellumCivile
{
    internal readonly struct DynamicRelationPairKey : IEquatable<DynamicRelationPairKey>
    {
        private readonly Hero _firstHero;
        private readonly Hero _secondHero;

        public DynamicRelationPairKey(Hero firstHero, Hero secondHero)
        {
            if (CompareHeroes(firstHero, secondHero) <= 0)
            {
                _firstHero = firstHero;
                _secondHero = secondHero;
            }
            else
            {
                _firstHero = secondHero;
                _secondHero = firstHero;
            }
        }

        public Hero FirstHero => _firstHero;
        public Hero SecondHero => _secondHero;

        public bool Equals(DynamicRelationPairKey other)
        {
            return ReferenceEquals(_firstHero, other._firstHero)
                && ReferenceEquals(_secondHero, other._secondHero);
        }

        public override bool Equals(object obj)
        {
            return obj is DynamicRelationPairKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int firstHash = _firstHero != null ? _firstHero.GetHashCode() : 0;
                int secondHash = _secondHero != null ? _secondHero.GetHashCode() : 0;
                return (firstHash * 397) ^ secondHash;
            }
        }

        public string ToSerializedKey()
        {
            string first = _firstHero?.StringId ?? string.Empty;
            string second = _secondHero?.StringId ?? string.Empty;
            return string.CompareOrdinal(first, second) <= 0 ? $"{first}|{second}" : $"{second}|{first}";
        }

        private static int CompareHeroes(Hero firstHero, Hero secondHero)
        {
            string first = firstHero?.StringId ?? string.Empty;
            string second = secondHero?.StringId ?? string.Empty;
            return string.CompareOrdinal(first, second);
        }
    }
}
