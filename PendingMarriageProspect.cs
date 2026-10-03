using TaleWorlds.CampaignSystem;
using TaleWorlds.SaveSystem;

namespace BellumCivile
{
    public sealed class PendingMarriageProspect
    {
        [SaveableField(1)] public Clan Sponsor;
        [SaveableField(2)] public Hero First;
        [SaveableField(3)] public Hero Second;
        [SaveableField(4)] public Clan FirstHouse;
        [SaveableField(5)] public Clan SecondHouse;
        [SaveableField(6)] public Clan Destination;
        [SaveableField(7)] public int ExpiresDay;
        [SaveableField(8)] public int NextAttemptDay;

        internal bool IdentityValid => First?.IsAlive == true && Second?.IsAlive == true
            && Sponsor?.IsEliminated == false && FirstHouse?.IsEliminated == false && SecondHouse?.IsEliminated == false
            && First.Clan == FirstHouse && Second.Clan == SecondHouse && First.Spouse == null && Second.Spouse == null;
    }
}
