using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.SaveSystem;

namespace BellumCivile
{
    public sealed class CivilWarGraceRecord
    {
        [SaveableField(1)] public FactionObject Faction;
        [SaveableField(2)] public Kingdom RebelRealm;
        [SaveableField(3)] public List<Kingdom> Realms = new List<Kingdom>();
        [SaveableField(4)] public double DurationDays;
        [SaveableField(5)] public double UntilDay;
        [SaveableField(6)] public bool Settled;
    }
}
