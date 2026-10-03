using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;

namespace BellumCivile.Behaviors
{
    // Forecast only: no family, claim, estate or Crown mutations during proposal search.
    internal sealed class MarriageOutcome
    {
        public Hero First { get; }
        public Hero Second { get; }
        public Clan FirstHouse { get; }
        public Clan SecondHouse { get; }
        public Clan Destination { get; }
        public Clan NewbornHouse => Destination;
        public bool HasReproductiveOpportunity { get; }
        public bool FirstIsCrownHeir { get; }
        public bool SecondIsCrownHeir { get; }
        public IReadOnlyList<Hero> FirstParentalEstateProspects { get; internal set; } = new Hero[0];
        public IReadOnlyList<Hero> SecondParentalEstateProspects { get; internal set; } = new Hero[0];
        public bool ExistingChildrenStay => true;
        public bool CrownRightsRemainPersonal => true;
        public bool RequiresLaterAccessionHouse(Hero hero) =>
            (hero == First && FirstIsCrownHeir || hero == Second && SecondIsCrownHeir)
            && hero != Destination?.Leader;

        public MarriageOutcome(Hero first, Hero second, Clan destination, bool firstHeir, bool secondHeir)
        {
            First = first;
            Second = second;
            FirstHouse = first.Clan;
            SecondHouse = second.Clan;
            Destination = destination;
            FirstIsCrownHeir = firstHeir;
            SecondIsCrownHeir = secondHeir;
            Hero mother = first.IsFemale ? first : second;
            HasReproductiveOpportunity = mother.Age >= SuccessionLawHelper.GetAgeOfMajority()
                && mother.Age <= 45f;
        }

        public bool StillMatches() => First.Clan == FirstHouse && Second.Clan == SecondHouse
            && First.Spouse == null && Second.Spouse == null
            && Campaign.Current.Models.MarriageModel.GetClanAfterMarriage(First, Second) == Destination;

        internal static bool BothAccept(float first, float second, bool firstPlayer, bool secondPlayer, float floor) =>
            (firstPlayer || first >= floor) && (secondPlayer || second >= floor);

        internal static float Rank(float first, float second, bool firstPlayer, bool secondPlayer) =>
            firstPlayer ? second : secondPlayer ? first : Math.Min(first, second);

        internal static int AnnualOffset(string id, int days)
        {
            uint hash = 2166136261;
            foreach (char character in id ?? string.Empty) hash = unchecked((hash ^ character) * 16777619);
            return (int)(hash % (uint)Math.Max(1, days));
        }
    }
}
