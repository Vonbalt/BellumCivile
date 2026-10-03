using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using BellumCivile;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;

internal static class PeaceReconsiderationTests
{
    private static Kingdom[] _enemies;
    private static Kingdom _ordinary;
    private static bool Enemies(ref IEnumerable<Kingdom> __result) { __result = _enemies; return false; }
    private static bool Protected(Kingdom second, ref bool __result) { __result = second != _ordinary; return false; }
    private static bool No(ref bool __result) { __result = false; return false; }
    private static bool Zero(ref int __result) { __result = 0; return false; }

    internal static void Run(Action<bool, string> check)
    {
        object Call(WarScoreRecord w, string name, params object[] args) => AccessTools.Method(typeof(WarScoreRecord), name).Invoke(w, args);
        bool Ready(WarScoreRecord w, float day) => (bool)Call(w, "CanReconsiderPeace", day);
        bool Reaction(WarScoreRecord w, string voter, params TreatyTermRecord[] terms) =>
            (bool)Call(w, "TryRecordRefusalReaction", "ruler", voter, terms);
        WarScoreRecord NewWar() => new WarScoreRecord("a:b", "a", "b", 0, null);
        var war = NewWar();
        check(Ready(war, 0), "Legacy/default wars have no artificial retry lock");
        Call(war, "RecordPeaceRejection", 40f);
        check(!Ready(war, 40) && !Ready(war, 46.99f) && Ready(war, 47), "Automatic retry opens at exactly seven days");
        check(!war.ParleyPending, "Cooldown expiry never creates a proposal");
        var deferred = NewWar();
        var rememberedTerms = new[] { new TreatyTermRecord(TreatyTermType.WhitePeace, 0) };
        check(Reaction(deferred, "existing", rememberedTerms), "Existing refusal receipt predates feasibility deferral");
        Call(deferred, "RecordPeaceDeferral", 40f);
        check(!Ready(deferred, 46.99f) && Ready(deferred, 47), "Feasibility deferral prevents repeated clan searches for seven days");
        check(!Reaction(deferred, "existing", rememberedTerms) && !deferred.ParleyPending,
            "Feasibility deferral preserves grievances without opening a parley");
        var deferredRestored = NewWar();
        foreach (var field in new[] { "_peaceFeasibilityRetryDay", "_peaceReconsiderationScore", "_refusalReactions" })
            AccessTools.Field(typeof(WarScoreRecord), field).SetValue(deferredRestored, AccessTools.Field(typeof(WarScoreRecord), field).GetValue(deferred));
        check(!Ready(deferredRestored, 46) && Ready(deferredRestored, 47), "Feasibility backoff survives field restoration");
        Call(deferred, "RecordPeaceDeferral", 50f);
        deferred.AddEvent(new WarScoreEventRecord(WarScoreEventType.MajorBattleWon, 51, 0, deferred.Score, "a", "b"));
        check(Ready(deferred, 51), "Material battle reopens deferred feasibility");
        var terms = new[] { new TreatyTermRecord(TreatyTermType.WhitePeace, 0) };
        check(Reaction(war, "voter", terms) && !Reaction(war, "voter", terms), "Unchanged rejection penalizes each member only once");
        check(Reaction(war, "other", terms), "Different members retain their own grievance receipts");
        Call(war, "RecordPeaceRejection", 48f);
        check(!Reaction(war, "voter", terms), "Later rejection does not erase earlier grievance receipts");
        var restored = NewWar();
        foreach (var field in new[] { "_peaceRetryDay", "_peaceReconsiderationScore", "_refusalReactions" })
            AccessTools.Field(typeof(WarScoreRecord), field).SetValue(restored, AccessTools.Field(typeof(WarScoreRecord), field).GetValue(war));
        check(!Ready(restored, 54) && Ready(restored, 55) && !Reaction(restored, "voter", terms), "Retry window and reaction receipts survive saved-field restoration");
        foreach (var type in new[] { WarScoreEventType.TownCaptured, WarScoreEventType.CastleCaptured,
            WarScoreEventType.CoreFiefRetaken, WarScoreEventType.MajorBattleWon, WarScoreEventType.RulerCaptured })
        {
            Call(war, "RecordPeaceRejection", 60f);
            war.AddEvent(new WarScoreEventRecord(type, 61, 0, war.Score, "a", "b"));
            check(Ready(war, 61) && Reaction(war, "voter", terms), type + " permits early reconsideration and a new grievance");
        }
        Call(war, "RecordPeaceRejection", 70f);
        war.AddEvent(new WarScoreEventRecord(WarScoreEventType.VillageRaided, 71, 1, war.Score, "a", "b"));
        check(!Ready(war, 71), "A minor raid does not bypass the retry window");
        war.AddBattleScore(19, 100);
        check(!Ready(war, 71), "Small cumulative score change retains the window");
        war.AddBattleScore(1, 100);
        check(Ready(war, 71), "Twenty-point change permits early reconsideration");
        Call(war, "RecordPeaceRejection", 80f);
        check(war.BeginParley(false, 81), "Explicit player parley is not blocked by automatic retry policy");
        war.EndParley();
        check(war.BeginParley(true, 81), "Forced settlements remain available during retry window");
        war = NewWar();
        var firstTerm = new TreatyTermRecord(TreatyTermType.WhitePeace, 0, goldAmount: 2000);
        var minorRevision = new TreatyTermRecord(TreatyTermType.WhitePeace, 0, goldAmount: 2001);
        check(Reaction(war, "voter", firstTerm) && !Reaction(war, "voter", minorRevision), "Cosmetic financial changes do not manufacture new refusal penalties");
        var otherTerm = new TreatyTermRecord(TreatyTermType.WhitePeace, 0, settlementId: "different_terms");
        check(Reaction(war, "voter", otherTerm), "Materially different terms permit a new reaction");
        check(Reaction(war, "voter", firstTerm, otherTerm) && !Reaction(war, "voter", otherTerm, firstTerm), "Reordering terms cannot repeat the grievance");
        var savedIds = typeof(WarScoreRecord).GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            .SelectMany(f => f.GetCustomAttributesData().Where(a => a.AttributeType.Name == "SaveableFieldAttribute"))
            .Select(a => a.ConstructorArguments[0].Value).ToList();
        check(savedIds.Count == savedIds.Distinct().Count(), "War score retry fields use unique save IDs");

        var h = new Harmony("bellum.test.storyline_enthusiasm");
        try
        {
            Kingdom Blank() => (Kingdom)FormatterServices.GetUninitializedObject(typeof(Kingdom));
            var realm = Blank(); _ordinary = Blank(); _enemies = new[] { Blank(), Blank(), Blank(), _ordinary };
            var type = typeof(WarPeaceRevampBehavior);
            h.Patch(AccessTools.Method(type, "GetActiveDirectWarKingdoms"), prefix: new HarmonyMethod(typeof(PeaceReconsiderationTests), nameof(Enemies)));
            h.Patch(AccessTools.Method(type, "IsTemporaryWarKingdom"), prefix: new HarmonyMethod(typeof(PeaceReconsiderationTests), nameof(No)));
            h.Patch(AccessTools.Method(type, "CountActiveRealmFeuds"), prefix: new HarmonyMethod(typeof(PeaceReconsiderationTests), nameof(Zero)));
            h.Patch(AccessTools.Method(type.Assembly.GetType("BellumCivile.StorylineWarProtectionHelper"), "IsPeaceBlocked"),
                prefix: new HarmonyMethod(typeof(PeaceReconsiderationTests), nameof(Protected)));
            object Load(bool enthusiasm) => AccessTools.Method(type, "GetConflictLoad").Invoke(null, new object[] { realm, enthusiasm });
            int Count(object load) => (int)AccessTools.Property(load.GetType(), "ForeignWarCount").GetValue(load);
            check(Count(Load(false)) == 4 && Count(Load(true)) == 1, "Conspiracy fronts count strategically but not for passive enthusiasm strain");
            _enemies = _enemies.Take(3).ToArray();
            check(Count(Load(true)) == 0 && Count(Load(false)) == 3, "Only storyline wars allow peacetime recovery without hiding military commitments");
            _enemies = new[] { _ordinary };
            check(Count(Load(true)) == 1 && Count(Load(false)) == 1, "Ordinary war keeps normal fatigue and strategic load");
        }
        finally { h.UnpatchAll(h.Id); _enemies = null; _ordinary = null; }
    }
}
