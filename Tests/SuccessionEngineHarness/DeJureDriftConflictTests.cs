using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;
using BellumCivile;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;

internal static class DeJureDriftConflictTests
{
    private static Kingdom _realm;
    private static Clan _clan;
    private static Hero _leader;
    private static float _day;
    private static bool _eligible;
    private static FeudalDeJureDriftEligibility _eligibility;
    private static bool Realm(ref Kingdom __result) { __result = _realm; return false; }
    private static bool Ruler(ref Clan __result) { __result = _clan; return false; }
    private static bool Leader(ref Hero __result) { __result = _leader; return false; }
    private static bool Day(ref float __result) { __result = _day; return false; }
    private static bool Eliminated(ref bool __result) { __result = false; return false; }
    private static bool Eligible(ref FeudalDeJureDriftEligibility __2, ref string __3, ref bool __result)
    { __2 = _eligibility; __3 = "test control change"; __result = _eligible; return false; }
    private static bool Delta(ref float __result) { __result = .01f; return false; }

    internal static void Run(Action<bool, string> check)
    {
        T Blank<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
        Kingdom Kingdom(string id) { var k = Blank<Kingdom>(); k.StringId = id; return k; }
        var home = Kingdom("drift_home");
        var rebels = Kingdom("drift_home_rebels_test");
        var feud = Kingdom("bc_feud_test");
        var foreign = Kingdom("drift_foreign");
        var independent = Kingdom("drift_home_indep_test");
        _clan = Blank<Clan>(); _clan.StringId = "drift_owner";
        _leader = Blank<Hero>();
        var behavior = new FeudalDeJureDriftBehavior();
        var type = typeof(FeudalDeJureDriftBehavior);
        var clans = (Dictionary<string, Clan>)AccessTools.Field(type, "_clansById").GetValue(behavior);
        clans[_clan.StringId] = _clan;
        var kingdoms = (Dictionary<string, Kingdom>)AccessTools.Field(type, "_kingdomsById").GetValue(behavior);
        kingdoms[home.StringId] = home;
        var title = new FeudalTitleRecord("drifting", "Drifting", FeudalTitleType.Barony,
            _clan.StringId, _clan.StringId, "old_parent", "", foreign.StringId, 0, 0);
        var parent = new FeudalTitleRecord("old_parent", "Old", FeudalTitleType.County, "", "", "", "", foreign.StringId, 0, 0);
        var target = new FeudalTitleRecord("target", "Target", FeudalTitleType.County, "", "", "", "", home.StringId, 0, 0);
        var record = new FeudalDeJureDriftRecord(title.TitleId, parent.TitleId, target.TitleId, home.StringId, .45f, 0, 10);
        ((Dictionary<string, FeudalDeJureDriftRecord>)AccessTools.Field(type, "_driftByTitleId").GetValue(behavior))[title.TitleId] = record;
        ((List<FeudalDeJureDriftRecord>)AccessTools.Field(type, "_drifts").GetValue(behavior)).Add(record);
        bool Reverse() => (bool)AccessTools.Method(type, "ShouldReverseDrift").Invoke(behavior, new object[] { title, record });
        void Evaluate() => AccessTools.Method(type, "EvaluateAndMaintainRecord").Invoke(behavior, new object[] { new FeudalTitleBehavior(), title });
        var h = new Harmony("bellum.test.drift_conflict");
        void Patch(MethodBase m, string name) => h.Patch(m, prefix: new HarmonyMethod(typeof(DeJureDriftConflictTests), name));
        try
        {
            Patch(AccessTools.PropertyGetter(typeof(Clan), "Kingdom"), nameof(Realm));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "Leader"), nameof(Leader));
            Patch(AccessTools.PropertyGetter(typeof(Kingdom), "RulingClan"), nameof(Ruler));
            Patch(AccessTools.PropertyGetter(typeof(Kingdom), "IsEliminated"), nameof(Eliminated));
            _realm = rebels;
            check(!Reverse(), "Civil-war shell does not reverse legal integration");
            _realm = feud;
            check(!Reverse(), "Private-feud shell does not reverse legal integration");
            _realm = home;
            check(!Reverse(), "Original permanent realm retains its integration");
            _realm = foreign;
            check(Reverse(), "Genuine foreign control still reverses integration");
            _realm = independent;
            check(Reverse(), "A permanent independent successor is not exempt as a temporary shell");

            Patch(AccessTools.PropertyGetter(type, "CurrentDay"), nameof(Day));
            Patch(AccessTools.Method(type, "TryEvaluateEligibility", new[] { typeof(FeudalTitleBehavior), typeof(FeudalTitleRecord),
                typeof(FeudalDeJureDriftEligibility).MakeByRefType(), typeof(string).MakeByRefType() }), nameof(Eligible));
            _eligible = false; _eligibility = null;
            foreach (var temporary in new[] { rebels, feud })
            {
                _realm = temporary;
                for (int i = 0; i < 20; i++) { _day += 100; Evaluate(); }
                check(record.Progress == .45f && record.IsActive && record.State == FeudalDeJureDriftState.Paused,
                    "Repeated temporary-realm evaluations preserve progress and the original record: " + temporary.StringId);
                check(record.LastEvaluatedDay == _day && record.TargetKingdomId == home.StringId && record.TargetParentTitleId == target.TitleId,
                    "Pause updates evaluation date without retargeting the legal destination: " + temporary.StringId);
            }
            _realm = home; _eligible = true; _eligibility = new FeudalDeJureDriftEligibility(title, parent, target, home, _leader);
            Patch(AccessTools.Method(type, "CalculateProgressDelta"), nameof(Delta));
            _day += 7; Evaluate();
            check(Math.Abs(record.Progress - .46f) < .00001f && record.State == FeudalDeJureDriftState.Advancing,
                "Reunification resumes the same record instead of starting again from zero");
        }
        finally { h.UnpatchAll(h.Id); _realm = null; _clan = null; _leader = null; _eligibility = null; }
    }
}
