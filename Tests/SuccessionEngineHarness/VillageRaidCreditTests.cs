using System;
using System.Collections.Generic;
using BellumCivile;
using HarmonyLib;

internal static class VillageRaidCreditTests
{
    internal static void Run(Action<bool, string> check)
    {
        var method = AccessTools.Method(typeof(WarScoreRecord), "TryCreditVillageRaid");
        bool Raid(WarScoreRecord w, string village, string side) => (bool)method.Invoke(w, new object[] { village, side, 1f, 15f });
        foreach (var kind in new[] { WarScoreConflictType.ForeignWar, WarScoreConflictType.CivilWar, WarScoreConflictType.ClaimFeud })
        {
            var war = new WarScoreRecord("a:b", "a", "b", 0, null, kind);
            check(Raid(war, "village", "a") && war.RaidScore == 1, kind + " first raid gives one point");
            check(!Raid(war, "village", "a") && war.RaidScore == 1, kind + " repeated/recovered village gives no further points");
            check(Raid(war, "village", "b") && war.RaidScore == 0, kind + " opposing side has its own village credit");
            check(!Raid(war, "village", "b") && !Raid(war, "other", "outsider"), kind + " duplicates and unrelated sides cannot score");
            var restored = new WarScoreRecord("a:b", "a", "b", 0, null, kind);
            foreach (string field in new[] { "_attackerRaidedVillages", "_defenderRaidedVillages" })
                AccessTools.Field(typeof(WarScoreRecord), field).SetValue(restored,
                    new List<string>((List<string>)AccessTools.Field(typeof(WarScoreRecord), field).GetValue(war)));
            check(!Raid(restored, "village", "a") && !Raid(restored, "village", "b"), kind + " restored history retains both side credits");
            check(Raid(new WarScoreRecord("a:b", "a", "b", 100, null, kind), "village", "a"), kind + " new conflict resets raid credits");
            for (int i = 0; i < 20; i++) Raid(war, "v" + i, "a");
            check(war.RaidScore == 15, kind + " raid component retains cap");
            Raid(war, "counter", "b");
            check(!Raid(war, "v19", "a") && war.RaidScore == 14, kind + " capped raid cannot be reused after counter-raiding");
        }
        var old = new WarScoreRecord("a:b", "a", "b", 0, null);
        AccessTools.Field(typeof(WarScoreRecord), "_attackerRaidedVillages").SetValue(old, null);
        AccessTools.Field(typeof(WarScoreRecord), "_defenderRaidedVillages").SetValue(old, null);
        old.AddEvent(new WarScoreEventRecord(WarScoreEventType.VillageRaided, 1, 0, 15, "a", "b", "capped_village"));
        check(!Raid(old, "capped_village", "a"), "Legacy event history recovers even zero-delta capped raids");
        check(Raid(old, "capped_village", "b"), "Legacy history does not consume opposite side's credit");
        var civil = new WarScoreRecord("a:b", "a", "b", 0, null, WarScoreConflictType.CivilWar);
        Raid(civil, "v", "b");
        AccessTools.Method(typeof(WarScoreRecord), "TryRetargetCivilWarDefender").Invoke(civil, new object[] { "b", "successor" });
        check(!Raid(civil, "v", "successor"), "Successor Crown inherits defender's spent raid credits");
        var prefix = (string)AccessTools.Field(typeof(WarScoreRecord).Assembly.GetType("BellumCivile.CivilWarPairRecord"), "RivalryPrefix").GetValue(null);
        var rival = new WarScoreRecord("a:b", "a", "b", 0, null, WarScoreConflictType.CivilWar, prefix + "test");
        Raid(rival, "winner_v", "a"); Raid(rival, "survivor_v", "b");
        check((bool)AccessTools.Method(typeof(WarScoreRecord), "TryPromoteRivalry").Invoke(rival, new object[] { "a", "b", "crown" }), "Rivalry can promote to Crown conflict");
        check(!Raid(rival, "winner_v", "crown") && !Raid(rival, "survivor_v", "b"), "Reversed rivalry perspective preserves the correct side's raid history");
    }
}
