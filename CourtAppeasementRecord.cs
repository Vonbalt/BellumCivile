using TaleWorlds.CampaignSystem;
using TaleWorlds.SaveSystem;

namespace BellumCivile
{
    public sealed class CourtAppeasementRecord
    {
        [SaveableField(1)] public FactionObject Target;
        [SaveableField(2)] public Kingdom Realm;
        [SaveableField(3)] public Clan RulingClan;
        [SaveableField(4)] public Hero Ruler;
        [SaveableField(5)] public int QuotedCost;
        [SaveableField(6)] public float DurationDays;
        [SaveableField(7)] public CampaignTime TermEnd;
        [SaveableField(8)] public CampaignTime Until;
        [SaveableField(9)] public bool Attempted;
        [SaveableField(10)] public bool Applied;
        [SaveableField(11)] public bool Ended;

        internal bool SameRuler => Realm != null && !Realm.IsEliminated && Ruler != null && !Ruler.IsDead
            && Realm.RulingClan == RulingClan && RulingClan?.Leader == Ruler;
        internal bool Active => Applied && !Ended && SameRuler && Target?.ParentKingdom == Realm
            && Target.IsIdeology && Until.IsFuture;
        internal bool TryBegin()
        {
            if (Attempted) return false;
            Attempted = true;
            return true;
        }
    }
}
