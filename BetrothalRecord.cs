using System;
using TaleWorlds.CampaignSystem;
using TaleWorlds.SaveSystem;

namespace BellumCivile
{
    internal sealed class BetrothalRecord
    {
        [SaveableField(1)] internal Hero First;
        [SaveableField(2)] internal Hero Second;
        [SaveableField(3)] internal Clan FirstHouse;
        [SaveableField(4)] internal Clan SecondHouse;
        [SaveableField(5)] internal Clan Destination;
        [SaveableField(6)] internal bool Accepted;
        [SaveableField(7)] internal bool PlayerConsentedToWedding;
        [SaveableField(8)] internal int OfferExpiresDay;
        [SaveableField(9)] internal int NextReviewDay;
        [SaveableField(10)] internal int FirstDelayedDay = -1;
        [SaveableField(11)] internal int HostilityStartedDay = -1;
        [SaveableField(12)] internal bool OfferNoticePublished;

        internal bool Involves(Clan house) => house != null && (FirstHouse == house || SecondHouse == house);
        internal bool Includes(Hero hero) => hero != null && (First == hero || Second == hero);
        internal Hero PartnerOf(Hero hero) => hero == First ? Second : hero == Second ? First : null;
        internal Hero Departing => Destination == FirstHouse ? Second : First;
        internal bool IsPlayerPledge => Involves(Clan.PlayerClan);
        internal Clan OtherPlayerHouse => FirstHouse == Clan.PlayerClan ? SecondHouse : FirstHouse;

        internal CampaignTime ExpectedDate(int adulthood)
        {
            var age = CampaignTime.Years(adulthood);
            var first = First.BirthDay + age;
            var second = Second.BirthDay + age;
            return first.ToDays >= second.ToDays ? first : second;
        }
    }

    internal static class BetrothalRules
    {
        internal const int HostilityThreshold = -60;
        internal const int HostilityDays = 30;
        internal const int WeddingRetryDays = 3;
        internal const int WeddingGraceDays = 30;
        internal const int BrokenPledgeRelation = -30;
        internal const int BrokenPledgeYears = 10;
        internal const int PledgedHousesOpinion = 5;

        internal static int TrackHostility(int since, int day, float relation) => relation > HostilityThreshold
            ? -1 : since < 0 ? day : since;

        internal static bool MayEndForHostility(int since, int day) => since >= 0 && day - since >= HostilityDays;
        internal static bool DelayExpired(int since, int day) => since >= 0 && day - since >= WeddingGraceDays;
        internal static float DelayCost(double years) => Math.Min(35f, 5f * (float)Math.Max(0, years));
    }
}
