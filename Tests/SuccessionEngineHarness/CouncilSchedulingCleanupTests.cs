using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;
using BellumCivile;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;

internal static class CouncilSchedulingCleanupTests
{
    private static CourtAgendaBehavior _calendar;
    private static bool _valid, _throw;
    private static int _recorded;
    private static string _withdrawnMotion, _withdrawnReason;
    private static bool Withdraw(string motionId, string reason)
    { _withdrawnMotion = motionId; _withdrawnReason = reason; return false; }
    private static bool Current(ref CourtAgendaBehavior __result) { __result = _calendar; return false; }
    private static bool Resolve(out Kingdom kingdom, out PrivyCouncilOffice office, ref bool __result)
    {
        kingdom = null; office = PrivyCouncilOffice.Marshal; __result = _valid; return false;
    }
    private static bool Record()
    {
        if (_throw) throw new InvalidOperationException("injected receipt failure");
        _recorded++; return false;
    }
    internal static void Run(Action<bool, string> check)
    {
        var type = typeof(CouncilAppointmentDeliberationBehavior);
        var h = new Harmony("bellum.test.council_scheduler_cleanup");
        void Patch(MethodBase target, string prefix) => h.Patch(target, prefix: new HarmonyMethod(typeof(CouncilSchedulingCleanupTests), prefix));
        var behavior = new CouncilAppointmentDeliberationBehavior();
        var receipts = (List<string>)AccessTools.Field(type, "_pendingOfficeSettlements").GetValue(behavior);
        var active = (Dictionary<string, bool>)AccessTools.Field(type, "_activeDecisionKeys").GetValue(behavior);
        var dates = (Dictionary<string, CampaignTime>)AccessTools.Field(type, "_pendingVoteDate").GetValue(behavior);
        void Complete(string key) => AccessTools.Method(type, "CompleteAppointmentLifecycle").Invoke(behavior, new object[] { key });
        void Reconcile() => AccessTools.Method(type, "ReconcileOfficeSettlements").Invoke(behavior, null);
        try
        {
            Patch(AccessTools.PropertyGetter(typeof(CourtAgendaBehavior), "Current"), nameof(Current));
            Patch(AccessTools.Method(type, "TryResolvePendingKey"), nameof(Resolve));
            Patch(AccessTools.Method(typeof(CourtAgendaBehavior), "RecordCouncilOfficeSettled"), nameof(Record));
            Patch(AccessTools.Method(typeof(CourtAgendaBehavior), "WithdrawCouncilProceeding"), nameof(Withdraw));
            _calendar = null; _recorded = 0; _valid = true; _throw = false;
            check(!behavior.TryProposePlayerAppointment(null, PrivyCouncilOffice.Marshal, out var reason) && reason != null,
                "Player appointment fails closed without calendar");
            check(!behavior.TryStartFactionAppointment(null, PrivyCouncilOffice.Marshal, null, null, out reason),
                "Legacy faction entry cannot schedule without calendar");
            check(!behavior.TryStartImmediateAppointment(null, PrivyCouncilOffice.Marshal, null, null, "fixture"),
                "Immediate entry cannot bypass appointment calendar");
            check(dates.Count == 0 && active.Count == 0, "Rejected entries create no pending or active ballots");
            dates["home|0"] = default(CampaignTime); active["home|0"] = true;
            Complete("home|0"); Complete("home|0");
            check(receipts.Count == 1 && _recorded == 0, "Missing calendar retains one saved completion receipt");
            check(dates.Count == 0 && active.Count == 0, "Completed ballot leaves no stale queue or active marker");
            _calendar = (CourtAgendaBehavior)FormatterServices.GetUninitializedObject(typeof(CourtAgendaBehavior));
            Reconcile(); Reconcile();
            check(_recorded == 1 && receipts.Count == 0, "Deferred completion records term lock once after calendar returns");
            _valid = false; Complete("malformed");
            check(_recorded == 1 && receipts.Count == 0, "Invalid completion key creates no lock and is retired");
            _valid = true; _throw = true;
            try { Complete("home|1"); } catch (TargetInvocationException) { }
            check(receipts.Count == 1, "Interrupted term-lock delivery retains its saved receipt");
            _throw = false; Reconcile();
            check(_recorded == 2 && receipts.Count == 0, "Receipt can recover after interrupted delivery");
            var motions = (Dictionary<string, string>)AccessTools.Field(type, "_pendingCourtAgendaIds").GetValue(behavior);
            dates["home|2"] = default(CampaignTime); motions["home|2"] = "paid-motion";
            AccessTools.Method(type, "WithdrawPendingAppointment").Invoke(behavior,
                new object[] { "home|2", "council_nominee_ineligible" });
            check(_withdrawnMotion == "paid-motion" && _withdrawnReason == "council_nominee_ineligible",
                "Scheduler preserves withdrawal identity and political reason");
            check(!dates.ContainsKey("home|2") && !motions.ContainsKey("home|2") && _recorded == 2,
                "Withdrawal releases pending state without stamping completed-office cooldown");
            foreach (string reasonCode in new[] { "council_nominee_ineligible", "council_sponsor_changed",
                "council_holder_changed", "council_candidate_wait_expired", "queue_rejected_after_payment", "kingdom_missing" })
            {
                var agenda = new CourtAgendaRecord { State = CourtAgendaState.Deliberating, PaidInfluence = 100 };
                int expected = reasonCode == "queue_rejected_after_payment" || reasonCode == "kingdom_missing" ? 100 : 0;
                check(agenda.SettleCancellation(reasonCode) == expected && agenda.CancellationReason == reasonCode,
                    "Council cancellation applies explicit refund policy: " + reasonCode);
                check(agenda.SettleCancellation(reasonCode) == 0, "Council cancellation settles payment once: " + reasonCode);
            }
            check(AccessTools.Field(typeof(CourtAgendaRecord), "PaymentExpenseCode").IsDefined(
                typeof(TaleWorlds.SaveSystem.SaveableFieldAttribute), false), "Payment expense classification survives saves");
        }
        finally { h.UnpatchAll(h.Id); _calendar = null; }
    }
}
