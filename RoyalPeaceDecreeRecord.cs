using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.SaveSystem;

namespace BellumCivile
{
    public sealed class RoyalPeaceDecreeRecord
    {
        [SaveableField(1)] public string Id;
        [SaveableField(2)] public Kingdom Realm;
        [SaveableField(3)] public Clan Enforcer;
        [SaveableField(4)] public List<string> FeudIds = new List<string>();
        [SaveableField(5)] public List<string> SettledFeudIds = new List<string>();
        [SaveableField(6)] public List<string> InvolvedClanIds = new List<string>();
        [SaveableField(7)] public List<string> PenalizedClanIds = new List<string>();
        [SaveableField(8)] public int InfluenceCost;
        [SaveableField(9)] public bool Wartime;
        [SaveableField(10)] public bool Complete;
        [SaveableField(11)] public string StartedFeudId;
        [SaveableField(12)] public bool Interrupted;
        [SaveableField(13)] public bool NoticePublished;
        [SaveableField(14)] public bool PlayerInvolved;
        [SaveableField(15)] public float UntilDay;
        [SaveableField(16)] public List<string> AffectedTitles = new List<string>();
    }
}
