using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;
using BellumCivile;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;

internal static class IndependentDriftTests
{
    private static Kingdom _realm;
    private static FeudalTitleRecord _receivingCounty, _receivingDuchy;
    private static bool _fullControl;
    private static float _day;
    private static bool Unified(FeudalTitleRecord __0, ref Kingdom __1, ref Hero __2, ref string __3, ref bool __result)
    { __1 = _realm; __2 = null; __3 = null; __result = _fullControl || __0.TitleId == "first"; return false; }
    private static bool Permanent(ref bool __result) { __result = true; return false; }
    private static bool Parent(FeudalTitleRecord __1, ref FeudalTitleRecord __result)
    { __result = __1.TitleType == FeudalTitleType.Barony ? _receivingCounty : _receivingDuchy; return false; }
    private static bool Day(ref float __result) { __result = _day; return false; }
    private static bool Delta(float __0, ref float __result) { __result = __0 * .001f; return false; }
    private static bool Skip() => false;

    internal static void Run(Action<bool, string> check)
    {
        var h = new Harmony("bellum.test.independent_drift");
        var driftType = typeof(FeudalDeJureDriftBehavior);
        var titleType = typeof(FeudalTitleBehavior);
        void Patch(MethodBase method, string name) => h.Patch(method, prefix: new HarmonyMethod(typeof(IndependentDriftTests), name));
        _realm = (Kingdom)FormatterServices.GetUninitializedObject(typeof(Kingdom)); _realm.StringId = "new_realm";
        var titles = new FeudalTitleBehavior();
        var registry = (Dictionary<string, FeudalTitleRecord>)AccessTools.Field(titleType, "_titlesById").GetValue(titles);
        FeudalTitleRecord Add(string id, FeudalTitleType tier, string parent, string realm)
        {
            var t = new FeudalTitleRecord(id, id, tier, "owner", "owner", parent, "", realm, 0, 0);
            registry[id] = t; return t;
        }
        var oldDuchy = Add("old_duchy", FeudalTitleType.Duchy, "", "old_realm");
        var county = Add("county", FeudalTitleType.County, oldDuchy.TitleId, "old_realm");
        var first = Add("first", FeudalTitleType.Barony, county.TitleId, "old_realm");
        var second = Add("second", FeudalTitleType.Barony, county.TitleId, "");
        _receivingDuchy = Add("new_duchy", FeudalTitleType.Duchy, "", _realm.StringId);
        _receivingCounty = Add("new_county", FeudalTitleType.County, _receivingDuchy.TitleId, _realm.StringId);
        AccessTools.Method(titleType, "RebuildRuntimeIndexes").Invoke(titles, new object[] { true, true });
        var drift = new FeudalDeJureDriftBehavior();
        var records = (Dictionary<string, FeudalDeJureDriftRecord>)AccessTools.Field(driftType, "_driftByTitleId").GetValue(drift);
        void Evaluate(FeudalTitleRecord t) => AccessTools.Method(driftType, "EvaluateAndMaintainRecord").Invoke(drift, new object[] { titles, t });
        try
        {
            Patch(AccessTools.Method(driftType, "TryResolveUnifiedHolderKingdom"), nameof(Unified));
            Patch(AccessTools.Method(driftType, "IsPermanentKingdom"), nameof(Permanent));
            Patch(AccessTools.Method(driftType, "FindTargetParent"), nameof(Parent));
            Patch(AccessTools.PropertyGetter(driftType, "CurrentDay"), nameof(Day));
            Patch(AccessTools.PropertyGetter(titleType, "CurrentDay"), nameof(Day));
            Patch(AccessTools.Method(driftType, "CalculateProgressDelta"), nameof(Delta));
            Patch(AccessTools.Method(titleType, "QueueServiceReviewForTitleAndChildren"), nameof(Skip));
            _fullControl = false; _day = 0;
            Evaluate(first); Evaluate(county);
            check(records.ContainsKey(first.TitleId) && !records.ContainsKey(county.TitleId), "A single barony can drift before its whole county is controlled");
            _day = 100; Evaluate(first);
            var original = records[first.TitleId];
            check(Math.Abs(original.Progress - .1f) < .00001f, "First barony accumulates its own progress");
            _fullControl = true;
            Evaluate(county); Evaluate(second); Evaluate(first);
            check(ReferenceEquals(original, records[first.TitleId]) && original.Progress == .1f && original.IsActive,
                "Acquiring the county preserves the existing barony record and progress");
            check(records[county.TitleId].Progress == 0 && records[second.TitleId].Progress == 0,
                "Newly eligible county and second barony start their own clocks");
            _day = 110; Evaluate(county); Evaluate(second); Evaluate(first);
            check(Math.Abs(original.Progress - .11f) < .00001f && Math.Abs(records[county.TitleId].Progress - .01f) < .00001f,
                "Parent and children progress concurrently without synchronization");

            var completeCounty = records[county.TitleId]; completeCounty.SetProgress(1);
            check(titles.TryCompleteDeJureDrift(completeCounty, out _, out _, out _, out _), "Parent completion succeeds independently");
            check(county.AssociatedKingdomId == _realm.StringId && first.AssociatedKingdomId == "old_realm",
                "County completion does not integrate an unfinished child");
            check(second.AssociatedKingdomId == "old_realm", "Child with inherited origin keeps it when its parent completes");
            check(Math.Abs(original.Progress - .11f) < .00001f && Math.Abs(records[second.TitleId].Progress - .01f) < .00001f,
                "Parent completion does not modify either child's clock");
            _receivingCounty = county;
            var findParent = AccessTools.Method(driftType, "FindTargetParent");
            h.Unpatch(findParent, HarmonyPatchType.Prefix, h.Id);
            check(ReferenceEquals(findParent.Invoke(drift, new object[] { titles, first, county, _realm }), county),
                "A child may complete in place when its original parent has already integrated");
            Patch(findParent, nameof(Parent));
            _day = 120; Evaluate(first);
            check(original.TargetParentTitleId == county.TitleId && Math.Abs(original.Progress - .12f) < .00001f,
                "Receiving-parent change inside the same realm preserves accumulated progress");
            original.SetProgress(1);
            check(titles.TryCompleteDeJureDrift(original, out _, out _, out _, out _), "Child can finish under its now-integrated original county");
            check(first.AssociatedKingdomId == _realm.StringId && second.AssociatedKingdomId == "old_realm",
                "Finishing one barony leaves its sibling's integration untouched");

            var legacy = new FeudalDeJureDriftBehavior();
            county.SetAssociatedKingdom("old_realm"); county.SetParentTitle(oldDuchy.TitleId);
            first.SetAssociatedKingdom("old_realm");
            var legacyParent = new FeudalDeJureDriftRecord(county.TitleId, oldDuchy.TitleId, _receivingDuchy.TitleId, _realm.StringId, .3f, 10, 100);
            var personal = new FeudalDeJureDriftRecord(first.TitleId, county.TitleId, _receivingCounty.TitleId, _realm.StringId, .5f, 0, 100);
            var saved = (List<FeudalDeJureDriftRecord>)AccessTools.Field(driftType, "_drifts").GetValue(legacy);
            saved.Add(legacyParent); saved.Add(personal);
            AccessTools.Method(driftType, "RebuildDriftIndex").Invoke(legacy, null);
            AccessTools.Method(titleType, "RebuildRuntimeIndexes").Invoke(titles, new object[] { true, true });
            var migrate = AccessTools.Method(driftType, "InitializeIndependentTiming");
            migrate.Invoke(legacy, new object[] { titles });
            var migrated = legacy.GetDriftForTitle(second.TitleId);
            check(migrated != null && migrated.Progress == .3f && migrated.StartedDay == 10 && migrated.LastEvaluatedDay == 100,
                "Legacy shared clock seeds an eligible child with known progress and dates");
            check(legacy.GetDriftForTitle(first.TitleId).Progress == .5f, "Migration never overwrites surviving individual progress");
            int count = saved.Count; migrate.Invoke(legacy, new object[] { titles });
            check(saved.Count == count, "Legacy timing migration runs only once");
        }
        finally { h.UnpatchAll(h.Id); _realm = null; _receivingCounty = _receivingDuchy = null; }
    }
}
