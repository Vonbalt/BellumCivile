using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.SaveSystem;

namespace BellumCivile
{
    public sealed class ElectiveContestPreference
    {
        [SaveableField(1)] public Hero Candidate;
        [SaveableField(2)] public float PersonalChance;
        [SaveableField(3)] public float Roll;
        [SaveableField(4)] public Dictionary<Clan, float> LiegeChances = new Dictionary<Clan, float>();
    }

    public sealed class ElectiveContestPledge
    {
        [SaveableField(1)] public Clan House;
        [SaveableField(2)] public Hero Speaker;
        [SaveableField(3)] public double Power;
        [SaveableField(4)] public bool HasStronghold;
        [SaveableField(5)] public bool AwaitingPlayer;
        [SaveableField(6)] public bool PlayerChoice;
        // Ordered preferences also define the fallback if a chosen challenger withdraws.
        [SaveableField(7)] public List<ElectiveContestPreference> Preferences = new List<ElectiveContestPreference>();
        [SaveableField(8)] public Hero AssignedSide;
    }
}
