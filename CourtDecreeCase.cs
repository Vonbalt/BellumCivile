using TaleWorlds.CampaignSystem;
using TaleWorlds.SaveSystem;

namespace BellumCivile
{
    public sealed class CourtDecreeCase
    {
        [SaveableField(1)] public string Id;
        [SaveableField(2)] public Kingdom Realm;
        [SaveableField(3)] public Clan Accused;
        [SaveableField(4)] public Hero Ruler;
        [SaveableField(5)] public bool Assigned;
        [SaveableField(6)] public bool Closed;
        [SaveableField(7)] public bool Announced;

        internal bool TrySeal()
        {
            if (Closed) return false;
            Closed = true;
            return true;
        }
    }
}
