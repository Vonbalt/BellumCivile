using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.SaveSystem;

namespace BellumCivile
{
    public sealed class AsylumVisitRecord
    {
        public const int Waiting = 0, Accepted = 1, Refused = 2, Cancelled = 3, Expired = 4;

        [SaveableField(1)] public Clan Clan;
        [SaveableField(2)] public Hero Guest;
        [SaveableField(3)] public Hero Ruler;
        [SaveableField(4)] public Kingdom Realm;
        [SaveableField(5)] public Settlement Settlement;
        [SaveableField(6)] public MobileParty Party;
        [SaveableField(7)] public CampaignTime Deadline;
        [SaveableField(8)] public bool Notified;
        [SaveableField(9)] public CampaignTime PartyHoldUntil;
        [SaveableField(10)] public int State;
        [SaveableField(11)] public bool RelationApplied;
        [SaveableField(12)] public Settlement PromisedVillage;
        [SaveableField(13)] public bool GrantSettled;
        [SaveableField(14)] public bool GrantAwarded;
        [SaveableField(15)] public bool PartyHeld;
    }
}
