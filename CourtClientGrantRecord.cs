using System;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.SaveSystem;

namespace BellumCivile
{
    public sealed class CourtClientGrantRecord
    {
        [SaveableField(1)] public Kingdom Client;
        [SaveableField(2)] public Clan Grantor;
        [SaveableField(3)] public Clan Recipient;
        [SaveableField(4)] public Hero Ruler;
        [SaveableField(5)] public Hero Beneficiary;
        [SaveableField(6)] public Settlement Fief;
        [SaveableField(7)] public string TitleId;
        [SaveableField(8)] public string OldLegal;
        [SaveableField(9)] public bool TransferAttempted;
        [SaveableField(10)] public bool RelationApplied;
        [SaveableField(11)] public bool Paid;
        [SaveableField(12)] public int Attempts;
    }

    internal static class CourtClientGrantRules
    {
        internal const string Kind = "court_client_land_grant";
        internal const int Cost = 100, Gratitude = 25, GratitudeCap = 50;
        internal static double Admission(int generosity) => Math.Max(0, Math.Min(.05, .01 + .02 * generosity));
        internal static bool NpcEligible(int holdings, bool peace, bool border, bool peripheral,
            bool lowProsperity, int relation, int generosity) => holdings >= 6 && peace && border && peripheral
            && lowProsperity && relation >= 0 && generosity >= 0;
        internal static double Weight(int generosity, int relation) => .05 + .025 * Math.Max(0, generosity)
            + .0005 * Math.Max(0, Math.Min(100, relation));
        internal static int Reward(int existing) => Math.Min(Gratitude, Math.Max(0, GratitudeCap - existing));
    }
}
