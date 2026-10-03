using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using BellumCivile;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;

internal static class CourtHistoryPresentationTests
{
    private sealed class Store : IDataStore
    {
        internal readonly Dictionary<string, object> Data = new Dictionary<string, object>();
        public bool IsLoading { get; set; }
        public bool IsSaving => !IsLoading;
        public bool SyncData<T>(string key, ref T data)
        {
            if (IsSaving) { Data[key] = data; return true; }
            if (!Data.TryGetValue(key, out var value)) return false;
            data = (T)value; return true;
        }
    }
    private static float _day;
    private static bool Now(ref CampaignTime __result) { __result = CampaignTime.Days(_day); return false; }
    private static bool NoShock() => false;
    private static bool Future(CampaignTime __instance, ref bool __result) { __result = __instance.ToDays > _day; return false; }
    private static bool Past(CampaignTime __instance, ref bool __result) { __result = __instance.ToDays <= _day; return false; }

    internal static void Run(Action<bool, string> check)
    {
        T Blank<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
        var realm = Blank<Kingdom>(); realm.StringId = "history";
        var faction = Blank<FactionObject>();
        AccessTools.Field(typeof(FactionObject), "_parentKingdom").SetValue(faction, realm);
        AccessTools.Field(typeof(FactionObject), "_type").SetValue(faction, FactionType.Nobility);
        var behavior = new CourtAgendaBehavior();
        var h = new Harmony("bellum.test.court_history");
        var ticks = AccessTools.Field(typeof(CampaignTime), "TimeTicksPerDay");
        var oldTicks = ticks.GetValue(null);
        var type = typeof(CourtAgendaBehavior);
        bool? Approval(CourtAgendaRecord a) => (bool?)AccessTools.Method(type, "ReactionApproval").Invoke(null, new object[] { a });
        void Record(CourtAgendaRecord a, float shock) => AccessTools.Method(type, "RecordResultHistory").Invoke(behavior, new object[] { a, shock });
        CourtAgendaRecord Objective(CourtObjectiveState state) => new CourtAgendaRecord
        {
            Realm = realm, Faction = faction, ResultApplied = true, State = CourtAgendaState.Completed,
            ObjectiveData = new CourtObjectiveRecord { Kind = "test", State = state }
        };
        try
        {
            ticks.SetValue(null, 1000L);
            h.Patch(AccessTools.PropertyGetter(typeof(CampaignTime), "Now"), prefix: new HarmonyMethod(typeof(CourtHistoryPresentationTests), nameof(Now)));
            h.Patch(AccessTools.PropertyGetter(typeof(CampaignTime), "IsFuture"), prefix: new HarmonyMethod(typeof(CourtHistoryPresentationTests), nameof(Future)));
            h.Patch(AccessTools.PropertyGetter(typeof(CampaignTime), "IsPast"), prefix: new HarmonyMethod(typeof(CourtHistoryPresentationTests), nameof(Past)));
            h.Patch(AccessTools.Method(typeof(IdeologyEventShockBehavior), "ApplyMoodShock"), prefix: new HarmonyMethod(typeof(CourtHistoryPresentationTests), nameof(NoShock)));
            _day = 100;
            var passed = new CourtAgendaRecord { Realm = realm, Faction = faction, ResultApplied = true, State = CourtAgendaState.Passed };
            var failed = new CourtAgendaRecord { Realm = realm, Faction = faction, ResultApplied = true, State = CourtAgendaState.Defeated };
            check(Approval(passed) == true && Approval(failed) == false, "Policy history distinguishes passage and defeat");
            var success = Objective(CourtObjectiveState.Succeeded);
            var expiry = Objective(CourtObjectiveState.Expired);
            var defeat = Objective(CourtObjectiveState.Failed);
            var cancelled = Objective(CourtObjectiveState.Cancelled);
            check(Approval(success) == true, "Completed objective is approval, not defeated vote");
            check(Approval(expiry) == false && Approval(defeat) == false, "Failed and expired objectives are reprisals");
            check(Approval(cancelled) == null, "Neutral cancellation has no like or dislike");
            Record(success, 20);
            Record(expiry, -20);
            Record(cancelled, 0);
            check(Math.Abs(success.ResultVisibleUntil.ToDays - 120) < .001 && Math.Abs(expiry.ResultVisibleUntil.ToDays - 120) < .001,
                $"Both +20 and -20 remain visible for twenty days (now={CampaignTime.Now.ToDays}, success={success.ResultVisibleUntil.ToDays}, expiry={expiry.ResultVisibleUntil.ToDays})");
            check(behavior.RecentResults(faction).Count() == 2, "Zero reaction adds no history");
            Record(success, 20);
            check(behavior.RecentResults(faction).Count() == 2, "One result reference cannot create duplicate rows");
            _day = 105;
            Record(success, 20);
            check(Math.Abs(success.ResultVisibleUntil.ToDays - 120) < .001, "Repeated recording cannot extend the original expiry");
            _day = 100;
            var active = (List<CourtAgendaRecord>)AccessTools.Field(type, "_agendas").GetValue(behavior);
            active.Add(success); active.Clear(); active.Add(new CourtAgendaRecord { Realm = realm, Faction = faction });
            check(behavior.RecentResults(faction).Count() == 2, "Replacing the term agenda preserves unexpired reactions");
            var store = new Store();
            var sync = AccessTools.Method(type, "SyncReactionHistory");
            sync.Invoke(behavior, new object[] { store });
            check(store.Data.ContainsKey("BC_CourtRecentResults"), "History has a separate save key");
            var restored = new CourtAgendaBehavior(); store.IsLoading = true;
            sync.Invoke(restored, new object[] { store });
            check(restored.RecentResults(faction).Count() == 2 && restored.RecentResults(faction).First().ResultVisibleUntil == success.ResultVisibleUntil,
                "Save adapter restores records with their original expiry, without replaying reactions");
            var legacy = new CourtAgendaBehavior();
            var legacyRows = (List<CourtAgendaRecord>)AccessTools.Field(type, "_agendas").GetValue(legacy);
            legacyRows.Add(success);
            cancelled.ResultVisibleUntil = CampaignTime.Days(120); legacyRows.Add(cancelled);
            legacyRows.Add(Objective(CourtObjectiveState.Succeeded));
            sync.Invoke(legacy, new object[] { new Store { IsLoading = true } });
            check(legacy.RecentResults(faction).Count() == 1,
                "Legacy import preserves valid windows, excluding neutral cancellations and unrecorded reactions");
            _day = 119.5f;
            check(behavior.RecentResults(faction).Count() == 2, "Twenty-day history survives through day nineteen");
            _day = 120;
            check(!behavior.RecentResults(faction).Any(), "History expires at its exact end, without a daily-tick delay");
            _day = 100;
            Record(defeat, -2.5f);
            check(Math.Abs(defeat.ResultVisibleUntil.ToDays - 102.5) < .001, "Fractional reactions retain fractional-day duration");
            var crown = Objective(CourtObjectiveState.Succeeded); crown.Faction = null;
            Record(crown, 20);
            check(behavior.RecentResults(faction).Count() == 3, "Crown-only completion does not invent a faction reaction");

            var shocks = new IdeologyEventShockBehavior();
            var material = AccessTools.Method(typeof(IdeologyEventShockBehavior), "RecordMaterialEvent");
            material.Invoke(shocks, new object[] { realm, "Conquest", 10f });
            check(shocks.HasRecentMaterialEvent(realm, "Conquest"), "Conquest receives a history entry");
            check(!shocks.HasRecentVillageRaid(realm), "A raid window alone does not imply an applied raid shock");
            material.Invoke(shocks, new object[] { realm, "VillageRaids", -5f });
            check(shocks.HasRecentVillageRaid(realm), "Applied raid reaction is visible");
            _day = 105;
            check(!shocks.HasRecentVillageRaid(realm), "Raid history expires five days after the actual shock");
            check(shocks.HasRecentMaterialEvent(realm, "Conquest"), "Independent material events keep their own duration");
            _day = 110;
            check(!shocks.HasRecentMaterialEvent(realm, "Conquest"), "Ten-point conquest history expires after ten days");
            _day = 100;
            shocks.RecordCouncilAppointmentReaction(faction, CouncilAppointmentReaction.MemberAppointed, 10);
            _day = 101;
            shocks.RecordCouncilAppointmentReaction(faction, CouncilAppointmentReaction.CandidatePassedOver, -5);
            check(shocks.HasRecentCouncilAppointment(faction) && shocks.HasRecentCouncilSnub(faction),
                "Later council reaction does not erase an unexpired independent reaction");
            _day = 107;
            check(shocks.HasRecentCouncilAppointment(faction) && !shocks.HasRecentCouncilSnub(faction),
                "Council reactions expire by their own magnitudes");
        }
        finally { h.UnpatchAll(h.Id); ticks.SetValue(null, oldTicks); }
    }
}
