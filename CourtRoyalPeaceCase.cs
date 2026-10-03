using TaleWorlds.CampaignSystem;
using TaleWorlds.SaveSystem;

namespace BellumCivile
{
    public sealed class CourtRoyalPeaceCase
    {
        [SaveableField(1)] public string Id;
        [SaveableField(2)] public Kingdom Realm;
        [SaveableField(3)] public Kingdom Attacker;
        [SaveableField(4)] public Hero Ruler;
        [SaveableField(5)] public double Created;
        [SaveableField(6)] public double Expires;
        [SaveableField(7)] public string FeudId;
        [SaveableField(8)] public bool Assessed;
        [SaveableField(9)] public bool Closed;
        [SaveableField(10)] public CourtAgendaRecord Agenda;
        [SaveableField(11)] public bool Announced;
        [SaveableField(12)] public bool Started;
        [SaveableField(13)] public string Result;
        [SaveableField(14)] public string TitleId;
        [SaveableField(15)] public Clan Claimant;
        [SaveableField(16)] public Clan Holder;
        [SaveableField(17)] public double WarStarted;
        internal bool TryBegin()
        {
            if (Closed || Started) return false;
            Started = true;
            return true;
        }
    }
}
