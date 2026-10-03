using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.SaveSystem;

namespace BellumCivile
{
    public sealed class CourtMandateRecord
    {
        [SaveableField(1)] public string Id;
        [SaveableField(2)] public string OldLaw;
        [SaveableField(3)] public string NewLaw;
        [SaveableField(4)] public int Direction;
        [SaveableField(5)] public MandateReformDecision Decision;
        [SaveableField(6)] public List<CourtMandatePledge> Pledges = new List<CourtMandatePledge>();
    }
    public sealed class CourtMandatePledge
    {
        [SaveableField(1)] public Clan Clan;
        [SaveableField(2)] public Hero Speaker;
        [SaveableField(3)] public bool Reform;
        [SaveableField(4)] public bool Committed;
        [SaveableField(5)] public bool FailedPersuasion;
        [SaveableField(6)] public bool Bribed;
    }
}
