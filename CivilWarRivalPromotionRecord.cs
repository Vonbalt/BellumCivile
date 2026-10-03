using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.SaveSystem;

namespace BellumCivile
{
    public sealed class CivilWarRivalPromotionRecord
    {
        [SaveableField(1)] public CivilWarConflictRecord Conflict;
        [SaveableField(2)] public CivilWarSideRecord Winner;
        [SaveableField(3)] public CivilWarSideRecord Survivor;
        [SaveableField(4)] public CivilWarPairRecord RivalPair;
        [SaveableField(5)] public CivilWarPairRecord CrownPair;
        [SaveableField(6)] public WarScoreRecord OldCrownScore;
        [SaveableField(7)] public CampaignTime WarStarted;
        [SaveableField(8)] public List<int> NativeCounts = new List<int>();
        [SaveableField(9)] public int Stage;
        [SaveableField(10)] public bool Completed;
        [SaveableField(11)] public string Failure;
        [SaveableField(12)] public CivilWarCollapseRecord CollapseOwner;

        internal bool Protects(Kingdom realm) => !Completed && realm != null
            && (realm == Conflict?.CrownRealm || realm == Winner?.Realm || realm == Survivor?.Realm);
    }
}
