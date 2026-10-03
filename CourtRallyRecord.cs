using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.SaveSystem;

namespace BellumCivile
{
    public sealed class CourtRallyRecord
    {
        [SaveableField(1)] public Kingdom Target;
        [SaveableField(2)] public WarScoreRecord War;
        [SaveableField(3)] public string Attacker;
        [SaveableField(4)] public string Defender;
        [SaveableField(5)] public List<Clan> Members = new List<Clan>();
        [SaveableField(6)] public bool Activated;
        [SaveableField(7)] public double Started;
        [SaveableField(8)] public double Expires;
        [SaveableField(9)] public bool OutcomeRecorded;
        [SaveableField(10)] public double OutcomeDay;
        [SaveableField(11)] public int Outcome;
        [SaveableField(12)] public string OutcomeReason;
        [SaveableField(13)] public bool SettlementPending;
        [SaveableField(14)] public double PendingDay;
        [SaveableField(15)] public bool Defeat;
        [SaveableField(16)] public string WarKey;
        [SaveableField(17)] public float WarStarted;
    }
    internal static class CourtRallyRules
    {
        internal const string Kind = "court_rally_victory";
        internal static float Effective(float baseline, bool eligible) => eligible ? Math.Min(100, baseline + 15) : baseline;
        // 1 victory, -1 defeat/inconclusive peace, 0 unverified, -2 unresolved term.
        internal static int Classify(bool verified, bool white, int recognized, float net) => !verified ? 0
            : white ? -1 : recognized != 0 ? Math.Sign(recognized) : net >= 10 ? 1 : -1;
        internal static int Shock(int result) => result == 1 ? 10 : result == -1 ? -10 : result == -2 ? -5 : 0;
        internal static double End(double start, double deadline, double year) => Math.Min(deadline, start + year / 4);
    }
}
