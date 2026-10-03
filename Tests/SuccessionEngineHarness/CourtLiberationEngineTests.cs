using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;
using BellumCivile;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;

internal static class CourtLiberationEngineTests
{
    private static double _day;
    private static bool _identity, _war;
    private static bool Now(ref CampaignTime __result) { __result = CampaignTime.Days((float)_day); return false; }
    private static bool Identity(ref bool __result) { __result = _identity; return false; }
    private static bool War(ref bool __result) { __result = _war; return false; }
    private static bool Skip() => false;
    internal static void Run(Action<bool, string> check)
    {
        T Blank<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
        var type = typeof(CourtAgendaBehavior);
        var h = new Harmony("bellum.test.court_liberation");
        void Patch(MethodBase method, string prefix) => h.Patch(method, prefix: new HarmonyMethod(typeof(CourtLiberationEngineTests), prefix));
        var ticks = AccessTools.Field(typeof(CampaignTime), "TimeTicksPerDay");
        var oldTicks = ticks.GetValue(null);
        CourtAgendaBehavior b = null;
        List<CourtAgendaRecord> rows = null;
        CourtAgendaRecord Reset()
        {
            b = new CourtAgendaBehavior();
            rows = (List<CourtAgendaRecord>)AccessTools.Field(type, "_agendas").GetValue(b);
            _day = 30; _identity = true; _war = false;
            var a = new CourtAgendaRecord { Realm = Blank<Kingdom>(), State = CourtAgendaState.PursuingObjective,
                ObjectiveData = new CourtObjectiveRecord { Kind = "crown_prepare_liberation" },
                Liberation = new CourtLiberationRecord { Clientage = new ClientKingdomRecord("home", "overlord", 0, false, 0),
                    Suzerain = Blank<Kingdom>(), Activated = true, ActivatedDay = 20 } };
            AccessTools.Method(typeof(CourtObjectiveRecord), "FreezeTerm").Invoke(a.ObjectiveData, new object[] { 10d, 84d });
            AccessTools.Method(typeof(CourtObjectiveRecord), "Activate").Invoke(a.ObjectiveData, null);
            rows.Add(a); return a;
        }
        float Bonus(CourtAgendaRecord a, ClientKingdomRecord record = null) => (float)AccessTools.Method(type, "LiberationDesireBonus")
            .Invoke(b, new object[] { a.Realm, record ?? a.Liberation.Clientage });
        void Receipt(Kingdom first, Kingdom second) => AccessTools.Method(type, "RecordCourtLiberationWar").Invoke(b, new object[] { first, second });
        try
        {
            ticks.SetValue(null, 1000L);
            Patch(AccessTools.PropertyGetter(typeof(CampaignTime), "Now"), nameof(Now));
            Patch(AccessTools.Method(type, "SameLiberationClientage"), nameof(Identity));
            Patch(AccessTools.Method(type, "LiberationCrownValid"), nameof(Identity));
            Patch(AccessTools.Method(type, "LiberationNotice"), nameof(Skip));
            Patch(AccessTools.Method(typeof(Kingdom), "IsAtWarWith"), nameof(War));
            var a = Reset();
            check(Bonus(a) == 20, "Active preparations add exactly twenty desire");
            rows.Add(a); check(Bonus(a) == 20, "Duplicate agenda cannot stack preparation bonus");
            check(Bonus(a, new ClientKingdomRecord("home", "overlord", 0, false, 0)) == 0, "A new clientage between the same realms cannot inherit the bonus");
            _identity = false; check(Bonus(a) == 0, "Changed Crown or clientage removes bonus immediately");
            _identity = true; _day = 84; check(Bonus(a) == 0, "Exact frozen deadline removes bonus before maintenance");
            _day = 19; check(Bonus(a) == 0, "No bonus before activation date");
            _day = 30; a.Liberation.Activated = false; check(Bonus(a) == 0, "Announcing preparations does not activate them");
            a = Reset(); a.ResultApplied = true; check(Bonus(a) == 0, "Settled preparations have no residual effect");
            a = Reset(); Receipt(a.Realm, a.Liberation.Suzerain);
            check(!a.ResultApplied && !a.Liberation.WarConfirmed, "A declaration veto cannot complete preparations");
            _war = true; Receipt(a.Realm, Blank<Kingdom>());
            check(!a.ResultApplied, "Unrelated foreign war cannot complete liberation objective");
            Receipt(a.Realm, a.Liberation.Suzerain);
            check(a.ResultApplied && a.State == CourtAgendaState.Completed && a.Liberation.Initiated, "Confirmed liberation completes the objective, not a victory award");
            Receipt(a.Realm, a.Liberation.Suzerain);
            check(a.Liberation.WarDay == 30 && Bonus(a) == 0, "Duplicate confirmation is idempotent and bonus ends");
            a = Reset(); _war = true; Receipt(a.Liberation.Suzerain, a.Realm);
            check(a.ResultApplied && a.State == CourtAgendaState.Cancelled && !a.Liberation.Initiated, "Suzerain attack cancels without credit for launching liberation");
            a = Reset(); _war = true; _day = 84; Receipt(a.Realm, a.Liberation.Suzerain);
            check(!a.Liberation.WarConfirmed, "Post-deadline war cannot retroactively complete preparations");
            a = Reset(); a.Liberation.WarConfirmed = a.Liberation.Initiated = true; _identity = false; _day = 90;
            AccessTools.Method(type, "MaintainLiberationObjectives").Invoke(b, null);
            check(a.State == CourtAgendaState.Completed, "Saved confirmed receipt survives later ruler replacement and expiry");
        }
        finally { h.UnpatchAll(h.Id); ticks.SetValue(null, oldTicks); }
    }
}
