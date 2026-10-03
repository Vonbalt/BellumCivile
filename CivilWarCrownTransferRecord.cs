using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.SaveSystem;

namespace BellumCivile
{
    public sealed class CivilWarPairTransferRecord
    {
        [SaveableField(1)] public CivilWarSideRecord Side;
        [SaveableField(2)] public CivilWarPairRecord Pair;
        [SaveableField(3)] public CampaignTime WarStarted;
        // Per side: troop casualties, ship casualties, sieges, town sieges, raids. Rebel first.
        [SaveableField(4)] public List<int> NativeCounts = new List<int>();
        [SaveableField(5)] public int Stage;
    }

    public sealed class CivilWarCrownTransferRecord
    {
        [SaveableField(1)] public CivilWarConflictRecord Conflict;
        [SaveableField(2)] public Kingdom Previous;
        [SaveableField(3)] public Kingdom Successor;
        [SaveableField(4)] public List<CivilWarPairTransferRecord> Pairs = new List<CivilWarPairTransferRecord>();
        [SaveableField(5)] public bool Started;
        [SaveableField(6)] public bool Completed;
        [SaveableField(7)] public string Failure;

        internal bool Protects(Kingdom realm) => !Completed && realm != null
            && (Previous == realm || Successor == realm || Pairs.Exists(p => p.Side?.Realm == realm));

        internal bool ProtectsFaction(FactionObject faction) => !Completed && faction != null
            && (Protects(faction.ParentKingdom) || Pairs.Exists(p => p.Side?.Faction == faction));
    }

    internal static class CivilWarTransferRules
    {
        internal const int CompleteStage = 4;
        internal static bool Advance(CivilWarPairTransferRecord pair, Func<int, bool> apply)
        {
            if (pair == null || apply == null || pair.Stage < 0 || pair.Stage > CompleteStage) return false;
            while (pair.Stage < CompleteStage)
            {
                if (!apply(pair.Stage)) return false;
                pair.Stage++;
            }
            return true;
        }
    }
}
