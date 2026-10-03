using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.SaveSystem;

namespace BellumCivile
{
    public sealed class CourtActivityRecord
    {
        [SaveableField(1)] public string EventId;
        [SaveableField(2)] public Hero Ruler;
        [SaveableField(3)] public Clan RulingClan;
        [SaveableField(4)] public bool Positive;
        [SaveableField(5)] public List<CourtActivityTarget> Targets = new List<CourtActivityTarget>();
        [SaveableField(6)] public bool Finished;
    }

    public sealed class CourtActivityTarget
    {
        [SaveableField(1)] public Settlement Settlement;
        [SaveableField(2)] public Hero Hero;
        [SaveableField(3)] public Army Army;
        [SaveableField(4)] public CharacterObject Troop;
        [SaveableField(5)] public bool Started;
        [SaveableField(6)] public bool Completed;
        [SaveableField(7)] public bool Changed;
        [SaveableField(8)] public string Report;
        [SaveableField(9)] public string Name;

        // Seal BEFORE native actions: an interrupted recipient is never paid twice.
        internal bool TryBegin()
        {
            if (Started || Completed) return false;
            Started = true;
            return true;
        }
    }
}
