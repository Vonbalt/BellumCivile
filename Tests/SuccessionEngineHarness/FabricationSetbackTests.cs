using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using BellumCivile;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.SaveSystem;
using TaleWorlds.CampaignSystem;

internal static class FabricationSetbackTests
{
    private sealed class Store : IDataStore
    {
        internal readonly Dictionary<string, object> Data = new Dictionary<string, object>();
        public bool IsLoading { get; set; }
        public bool IsSaving => !IsLoading;
        public bool SyncData<T>(string key, ref T value)
        {
            if (IsSaving) { Data[key] = value; return true; }
            if (!Data.TryGetValue(key, out object saved)) return false;
            value = (T)saved; return true;
        }
    }
    private static float _day;
    private static bool Day(ref float __result) { __result = _day; return false; }
    private static bool Year(ref int __result) { __result = 84; return false; }
    private static FeudalClaimFabricationRecord Record(int cost = 50000) => new FeudalClaimFabricationRecord(
        "test", "hero", "clan", "title", FeudalClaimFabricationTrack.DeFacto, 0, .01f, 0, cost, 50);

    // Round-trip precisely the annotated payload; native disk serialization is tested in-game.
    private static FeudalClaimFabricationRecord Reload(FeudalClaimFabricationRecord source)
    {
        var target = (FeudalClaimFabricationRecord)FormatterServices.GetUninitializedObject(typeof(FeudalClaimFabricationRecord));
        foreach (var field in AccessTools.GetDeclaredFields(typeof(FeudalClaimFabricationRecord))
            .Where(f => f.IsDefined(typeof(SaveableFieldAttribute), false))) field.SetValue(target, field.GetValue(source));
        return target;
    }

    internal static void Run(Action<bool, string> check)
    {
        var types = new[] { FeudalTitleType.Barony, FeudalTitleType.County, FeudalTitleType.Duchy, FeudalTitleType.Kingdom, FeudalTitleType.Empire };
        for (int i = 0; i < types.Length; i++)
        {
            int cost = 50000 + 25000 * i;
            check(FeudalClaimFabricationBehavior.GetFabricationGoldCost(types[i]) == cost, "Revised fabrication gold cost");
            check(FeudalClaimFabricationBehavior.GetFabricationInfluenceCost(types[i]) == cost / 1000, "Revised fabrication influence cost");
            var r = Record(cost);
            r.BeginSetback(1);
            check(r.SetbackGoldCost == cost / 4, "Salvage quotes one quarter of original paid gold");
        }
        foreach (int stage in new[] { 1, 2 })
        foreach (bool paid in new[] { false, true })
        {
            var r = Record();
            r.MarkMilestonePassed(.33f);
            if (stage == 2) r.MarkMilestonePassed(.66f);
            r.BeginSetback(stage);
            r = Reload(r);
            float threshold = stage == 1 ? .33f : .66f;
            r.AddProgress(1f);
            check(r.AwaitingSetback && r.PendingSetbackStage == stage && r.Progress == threshold, "Saved pending setback freezes progress");
            check(r.ResolveSetback(paid), "First decision resolves the saved setback");
            check(!r.ResolveSetback(paid), "Repeated decision cannot charge or roll back again");
            r = Reload(r);
            check(r.Progress == (paid ? threshold : stage == 1 ? 0f : .33f), "Payment preserves threshold; delay repeats only affected stage");
            check(r.PaidGoldCost == 50000 && r.PaidInfluenceCost == 50, "Salvage does not inflate technical refund basis or influence expense");
            check(r.HasPassed33 && (stage == 1 || r.HasPassed66), "Failed but resolved checkpoint survives rollback and reload");
            check(r.ReworkStage == (paid ? 0 : stage), "Rework stage is saved for display");
            r.AddProgress(.33f);
            check(r.ReworkStage == 0 && r.HasPassed33 && (stage == 1 || r.HasPassed66), "Repeating work clears display marker without re-enabling check");
        }
        var both = Record();
        both.BeginSetback(1); both.ResolveSetback(false); both.AddProgress(.66f);
        both.BeginSetback(2); both.ResolveSetback(false);
        both = Reload(both); both.AddProgress(.67f);
        check(both.HasPassed33 && both.HasPassed66 && !both.HasPassed100, "Two early setbacks never bypass the final check");
        both.MarkStageFailed(3); both.SetActive(false);
        check(!both.ResolveSetback(true) && both.DidStageFail(3), "Final exposure remains terminal");
        var legacy = Record(); legacy.MarkMilestonePassed(.33f); legacy = Reload(legacy);
        check(legacy.HasPassed33 && !legacy.AwaitingSetback && legacy.ReworkStage == 0, "Legacy progress needs no pending-state migration");
        var zero = Record(0); zero.BeginSetback(1);
        check(zero.SetbackGoldCost == 0, "Free debug fabrication quotes no salvage fee");

        var choose = AccessTools.Method(typeof(FeudalClaimFabricationBehavior), "ChooseNpcSetback");
        int Choice(bool desired, int gold, float roll) => (int)choose.Invoke(null, new object[] { desired, gold, 12500, roll });
        check(Choice(false, 1000000, 0) == 2, "NPC abandons strategically unwanted target even when rich");
        check(Choice(true, 37500, .99f) == 0, "NPC pays when exact reserve remains");
        check(Choice(true, 37499, .749f) == 1, "NPC repeats work below reserve with 75 percent chance");
        check(Choice(true, 37499, .75f) == 2, "NPC abandonment boundary is 25 percent");
        var risk = AccessTools.Method(typeof(FeudalClaimFabricationBehavior), "CalculateFabricationDiscoveryChance");
        float Risk(int stage) => (float)risk.Invoke(null, new object[] { null, null, stage, 0, 0 });
        check(Risk(1) == Risk(2) && Risk(2) == Risk(3), "All three stages use identical difficulty");
        var key = AccessTools.Method(typeof(FeudalClaimFabricationBehavior), "RetryKey");
        check(!Equals(key.Invoke(null, new object[] { "ab", "c" }), key.Invoke(null, new object[] { "a", "bc" })), "House-title cooldown keys cannot collide through concatenation");
        var harmony = new Harmony("bellum.test.fabrication_cooldowns");
        try
        {
            harmony.Patch(AccessTools.PropertyGetter(typeof(FeudalClaimFabricationBehavior), "CurrentDay"), prefix: new HarmonyMethod(typeof(FabricationSetbackTests), nameof(Day)));
            harmony.Patch(AccessTools.Method(typeof(FeudalClaimFabricationBehavior), "GetCampaignDaysInYear"), prefix: new HarmonyMethod(typeof(FabricationSetbackTests), nameof(Year)));
            var set = AccessTools.Method(typeof(FeudalClaimFabricationBehavior), "SetRetryCooldown");
            var active = AccessTools.Method(typeof(FeudalClaimFabricationBehavior), "IsOnCooldown");
            var clan = (Clan)FormatterServices.GetUninitializedObject(typeof(Clan)); clan.StringId = "clan";
            var title = new FeudalTitleRecord("title", "Title", FeudalTitleType.Barony, "other", "other", "", "", "", 0, 0);
            foreach (int years in new[] { 1, 2 })
            {
                _day = 100;
                var behavior = new FeudalClaimFabricationBehavior();
                set.Invoke(behavior, new object[] { Record(), years });
                var store = new Store(); behavior.SyncData(store);
                var loaded = new FeudalClaimFabricationBehavior(); store.IsLoading = true; loaded.SyncData(store);
                check((bool)active.Invoke(loaded, new object[] { clan, title }), "Cooldown survives behavior save/load");
                clan.StringId = "another";
                check(!(bool)active.Invoke(loaded, new object[] { clan, title }), "Cooldown is house-specific");
                clan.StringId = "clan";
                _day = 100 + 84 * years - 1;
                check((bool)active.Invoke(loaded, new object[] { clan, title }), "Cooldown lasts the agreed full campaign years");
                _day++;
                check(!(bool)active.Invoke(loaded, new object[] { clan, title }), "Cooldown expires on its exact due day");
            }
            var oldSave = new Store { IsLoading = true };
            var migrated = new FeudalClaimFabricationBehavior(); migrated.SyncData(oldSave);
            check(!(bool)active.Invoke(migrated, new object[] { clan, title }), "Older saves initialize an empty cooldown collection");
        }
        finally { harmony.UnpatchAll(harmony.Id); }
    }
}
