using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using BellumCivile;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;

internal static class CampaignRunRecoveryTests
{
    private sealed class Manager : ICampaignBehaviorManager
    {
        internal readonly List<CampaignBehaviorBase> Items = new List<CampaignBehaviorBase>();
        public T GetBehavior<T>() => Items.OfType<T>().FirstOrDefault();
        public IEnumerable<T> GetBehaviors<T>() => Items.OfType<T>();
        public void AddBehavior(CampaignBehaviorBase b) => Items.Add(b);
        public void RemoveBehavior<T>() where T : CampaignBehaviorBase => Items.RemoveAll(b => b is T);
        public void ClearBehaviors() => Items.Clear();
        public void InitializeCampaignBehaviors(IEnumerable<CampaignBehaviorBase> b) => Items.AddRange(b);
        public void LoadBehaviorData() { }
        public void RegisterEvents() { }
    }
    private static Hero _heir, _predecessor, _spouse, _head;
    private static Clan _source, _heirHouse, _ruler, _cadet;
    private static Kingdom _realm;
    private static int _plans, _finishes, _payments;
    private static double _day;
    private static bool _failPlan;
    private static T Blank<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    private static object Call(object owner, string name, params object[] args) => AccessTools.Method(owner.GetType(), name).Invoke(owner, args);
    private static bool Skip() => false;
    private static bool Yes(ref bool __result) { __result = true; return false; }
    private static bool No(ref bool __result) { __result = false; return false; }
    private static bool Now(ref CampaignTime __result) { __result = CampaignTime.Days((float)_day); return false; }
    private static bool House(Hero __instance, ref Clan __result) { __result = __instance == _heir ? _heirHouse : _source; return false; }
    private static bool Head(Clan __instance, ref Hero __result) { __result = __instance == _cadet ? _heir : _head; return false; }
    private static bool Spouse(Hero __instance, ref Hero __result) { __result = __instance == _heir ? _spouse : null; return false; }
    private static bool MainHero(ref Hero __result) { __result = null; return false; }
    private static bool Player(ref Clan __result) { __result = null; return false; }
    private static bool Realm(ref Kingdom __result) { __result = _realm; return false; }
    private static bool Ruler(ref Clan __result) { __result = _ruler; return false; }
    private static bool Age(ref float __result) { __result = 30; return false; }
    private static bool Majority(ref int __result) { __result = 18; return false; }
    private static bool Name(ref string __result) { __result = "Test branch"; return false; }
    private static bool Endowment(CrownAccessionRecord record)
    {
        if (_failPlan) throw new InvalidOperationException("interrupted endowment planning");
        _plans++; record.EndowmentGold = 100; return false;
    }
    private static bool Settle(CrownAccessionRecord record, ref bool __result)
    {
        record.Cadet = _cadet; record.CadetInitialized = record.EndowmentSettled = true;
        _heirHouse = _cadet; __result = record.Household.Contains(_heir.StringId); return false;
    }
    private static bool ChangeRuler(Kingdom kingdom, Clan clan) { _ruler = clan; return false; }
    private static bool Finish(CrownAccessionRecord record)
    { _finishes++; record.Completed = record.TitleTransferred = true; return false; }
    private static bool Pay(ref bool __result) { _payments++; __result = true; return false; }

    internal static void Run(Action<bool, string> check)
    {
        var previous = Campaign.Current;
        var h = new Harmony("bellum.tests.campaign_run_recovery");
        void Patch(MethodBase target, string prefix) => h.Patch(target, prefix: new HarmonyMethod(typeof(CampaignRunRecoveryTests), prefix));
        var assembly = typeof(CrownAccessionBehavior).Assembly;
        var crown = new CrownAccessionBehavior();
        var court = new CourtAgendaBehavior();
        var deliberation = new CouncilAppointmentDeliberationBehavior();
        var manager = new Manager();
        manager.Items.AddRange(new CampaignBehaviorBase[] { crown, court, deliberation, new PartitionSuccessionBehavior() });
        var campaign = Blank<Campaign>(); campaign.AddCampaignBehaviorManager(manager);
        _realm = Blank<Kingdom>(); _realm.StringId = "empire_s";
        _source = Blank<Clan>(); _source.StringId = "old_house";
        _cadet = Blank<Clan>(); _cadet.StringId = "saved_cadet";
        _heir = Blank<Hero>(); _heir.StringId = "former_regent";
        _predecessor = Blank<Hero>(); _predecessor.StringId = "ward";
        _spouse = Blank<Hero>(); _spouse.StringId = "spouse";
        _heirHouse = _ruler = _source; _head = _heir; _day = 100;
        var ticks = AccessTools.Field(typeof(CampaignTime), "TimeTicksPerDay"); var oldTicks = ticks.GetValue(null);
        CrownAccessionRecord Pending(bool old = false) => new CrownAccessionRecord
        {
            Realm = _realm, PreviousHouse = _source, SettlementRulingHouse = _source, Predecessor = _predecessor,
            Heir = _heir, HeirHouse = _source, ForcedAbdication = true, ForcedCauseId = "original_coup",
            CadetId = "saved_cadet", CreatesCadet = true, ForcedPrepared = old,
            Household = old ? new List<string> { _spouse.StringId } : new List<string>()
        };
        bool Prepare(CrownAccessionRecord record) => (bool)Call(crown, "PrepareForcedAbdication", record);
        CourtAgendaRecord Agenda(bool manual = false, double deadline = 140)
        {
            var a = new CourtAgendaRecord { Realm = _realm, Sponsor = _source, State = CourtAgendaState.Announced,
                Manual = manual, SessionDate = CampaignTime.Days(20), VoteDate = CampaignTime.Days(25), HasScheduleSnapshot = true,
                TermDays = 120, ObjectiveData = new CourtObjectiveRecord { Kind = "council_appointment", TargetId = "Chancellor" } };
            AccessTools.Method(typeof(CourtObjectiveRecord), "FreezeTerm").Invoke(a.ObjectiveData, new object[] { 10d, deadline });
            return a;
        }
        try
        {
            AccessTools.PropertySetter(typeof(Campaign), "Current").Invoke(null, new object[] { campaign }); ticks.SetValue(null, 24000L);
            Patch(AccessTools.PropertyGetter(typeof(CampaignTime), "Now"), nameof(Now));
            Patch(AccessTools.PropertyGetter(typeof(Hero), "Clan"), nameof(House));
            Patch(AccessTools.PropertyGetter(typeof(Hero), "Spouse"), nameof(Spouse));
            Patch(AccessTools.PropertyGetter(typeof(Hero), "IsAlive"), nameof(Yes));
            Patch(AccessTools.PropertyGetter(typeof(Hero), "IsDead"), nameof(No));
            Patch(AccessTools.PropertyGetter(typeof(Hero), "IsDisabled"), nameof(No));
            Patch(AccessTools.PropertyGetter(typeof(Hero), "MainHero"), nameof(MainHero));
            Patch(AccessTools.PropertyGetter(typeof(Hero), "Age"), nameof(Age));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "PlayerClan"), nameof(Player));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "Leader"), nameof(Head));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "Kingdom"), nameof(Realm));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "IsEliminated"), nameof(No));
            Patch(AccessTools.PropertyGetter(typeof(Kingdom), "RulingClan"), nameof(Ruler));
            Patch(AccessTools.PropertyGetter(typeof(Kingdom), "IsEliminated"), nameof(No));
            Patch(AccessTools.Method(typeof(BellumCivileLogger), "Log"), nameof(Skip));
            Patch(AccessTools.Method(assembly.GetType("BellumCivile.SuccessionLawHelper"), "GetAgeOfMajority"), nameof(Majority));
            Patch(AccessTools.Method(assembly.GetType("BellumCivile.RegencyPolicy"), "CanReceiveCrown"), nameof(Yes));
            Patch(AccessTools.Method(typeof(CrownAccessionBehavior), "FillAbdicationEndowment"), nameof(Endowment));
            Patch(AccessTools.Method(typeof(PartitionSuccessionBehavior), "PreviewAbdicationCadetName"), nameof(Name));

            var record = Pending();
            check(!Prepare(record) && !record.ForcedPrepared && _plans == 0,
                "An acting caretaker cannot freeze a same-house abdication plan without its founder");
            _head = _predecessor;
            check(Prepare(record) && record.ForcedPrepared && record.Household.Contains(_heir.StringId) && _plans == 1,
                "Once the lawful house head resumes, the same successor receives a complete household plan");
            check(record.ForcedCauseId == "original_coup" && record.CadetId == "saved_cadet" && record.Heir == _heir,
                "Preparation retains the original heir, Crown cause and saved branch identity");
            Prepare(record); check(_plans == 1, "A valid frozen plan is not recalculated on hourly retries");
            record = Pending(true); _head = _heir;
            check(!Prepare(record) && !record.Household.Contains(_heir.StringId), "Malformed old plan waits without extracting the minor's acting caretaker");
            _head = _predecessor;
            check(Prepare(record) && record.Household.Contains(_heir.StringId) && _plans == 2,
                "Older uncommitted plan missing its founder is rebuilt after the regency transition");
            foreach (string receipt in new[] { "Cadet", "CadetInitialized", "CadetAnnounced", "EndowmentSettled", "DeliveredFiefs",
                "DeliveredTitles", "GoldDebited", "GoldCredited", "GoldRecipient", "DeliveredGold", "TitleTransferred", "OutcomeApplied",
                "ForeignMovingClan", "ForeignMoveStarted", "ForeignMoveCompleted", "ForeignInfluenceRestored", "Announced", "MandateStarted", "IncomingHousePrepared" })
            {
                record = Pending(true); var field = typeof(CrownAccessionRecord).GetField(receipt);
                object value = field.FieldType == typeof(bool) ? (object)true : field.FieldType == typeof(Clan) ? _cadet
                    : field.FieldType == typeof(Hero) ? _heir : field.FieldType == typeof(int) ? (object)1 : new List<string> { "delivered" };
                field.SetValue(record, value); int count = _plans;
                check(!Prepare(record) && _plans == count && Equals(field.GetValue(record), value) && !record.Household.Contains(_heir.StringId),
                    "Malformed-plan repair cannot overwrite committed settlement: " + receipt);
            }
            record = Pending(true); record.Union = new RealmUnionRecord();
            check(!Prepare(record), "Malformed-plan repair does not take over a realm union journal");
            record = Pending(true); record.Household = null;
            check(Prepare(record) && record.Household.Contains(_heir.StringId), "An uncommitted null legacy household is repairable too");
            record = Pending(true); record.Household.Add(_heir.StringId); record.CadetInitialized = true;
            int before = _plans;
            check(Prepare(record) && _plans == before, "Valid plans retain the ordinary partially committed recovery path");
            record = Pending(true); _failPlan = true;
            bool interrupted = false;
            try { Prepare(record); }
            catch (TargetInvocationException ex) when (ex.InnerException is InvalidOperationException) { interrupted = true; }
            check(interrupted && !record.ForcedPrepared && record.Cadet == null,
                "An interrupted legacy rebuild cannot leave an apparently frozen partial endowment");
            _failPlan = false;
            check(Prepare(record) && record.ForcedPrepared && _plans == before + 1,
                "The same untouched plan is prepared again after an interrupted rebuild");

            // Real hourly coordinator, with only native estate/title endpoints replaced.
            record = Pending(true); var records = (List<CrownAccessionRecord>)AccessTools.Field(typeof(CrownAccessionBehavior), "_accessions").GetValue(crown);
            records.Add(record);
            Patch(AccessTools.Method(typeof(CrownAccessionBehavior), "ReconcileRegencyAvailability"), nameof(Yes));
            Patch(AccessTools.Method(typeof(CrownAccessionBehavior), "SettleCrownCadet"), nameof(Settle));
            Patch(AccessTools.Method(assembly.GetType("BellumCivile.HereditaryRealmSuccession"), "CanConsiderClan"), nameof(Yes));
            Patch(AccessTools.Method(typeof(PartitionSuccessionBehavior), "SettleCrownDeathEstate"), nameof(Yes));
            Patch(AccessTools.Method(typeof(CrownAccessionBehavior), "PrepareForeignCrownClan"), nameof(Yes));
            Patch(AccessTools.Method(typeof(ChangeRulingClanAction), "Apply"), nameof(ChangeRuler));
            Patch(AccessTools.Method(typeof(CrownAccessionBehavior), "Finish"), nameof(Finish));
            Call(crown, "ResumePending"); Call(crown, "ResumePending");
            check(record.Completed && record.TitleTransferred && _finishes == 1 && !crown.IsPending(_realm),
                "Hourly recovery completes the old forced accession once and releases the pending Crown gate");

            Patch(AccessTools.Method(typeof(CourtAgendaBehavior), "TryPay"), nameof(Pay));
            var agenda = Agenda(deadline: 50); Call(court, "AdvanceCouncil", agenda);
            check(agenda.State == CourtAgendaState.Cancelled && agenda.PaidInfluence == 0 && _payments == 0,
                "Expired unfiled council term is retired before payment or nomination evaluation");
            agenda = Agenda();
            check((bool)Call(court, "PrepareCouncilFilingSchedule", agenda) && agenda.SessionDate.ToDays == 100 && agenda.VoteDate.ToDays == 105,
                "A still-valid delayed council filing receives its original five-day deliberation window");
            check(agenda.TermDays == 120 && agenda.ObjectiveData.DeadlineDay == 140 && agenda.HasScheduleSnapshot,
                "Refreshing an unfiled vote does not rewrite the term snapshot or deadline");
            var future = Agenda(); future.SessionDate = CampaignTime.Days(110); future.VoteDate = CampaignTime.Days(115);
            check((bool)Call(court, "PrepareCouncilFilingSchedule", future) && future.SessionDate.ToDays == 110 && future.VoteDate.ToDays == 115,
                "Ordinary upcoming council sessions keep their scheduled dates");
            var due = Agenda(); due.SessionDate = CampaignTime.Days(95); due.VoteDate = CampaignTime.Days(100);
            check((bool)Call(court, "PrepareCouncilFilingSchedule", due) && due.VoteDate.ToDays == 100,
                "A ballot due exactly now remains valid without extending its window");
            _day = 101; Call(court, "PrepareCouncilFilingSchedule", agenda);
            check(agenda.VoteDate.ToDays == 105, "A future ballot date is not extended repeatedly");
            agenda.State = CourtAgendaState.Deliberating; _day = 200;
            check((bool)Call(court, "PrepareCouncilFilingSchedule", agenda) && agenda.VoteDate.ToDays == 105,
                "Paid/filed proceedings retain their original vote and bounded recovery dates");
            agenda = Agenda(manual: true, deadline: 50);
            check((bool)Call(court, "PrepareCouncilFilingSchedule", agenda) && agenda.VoteDate.ToDays == 205,
                "Explicit player Crown business is not expired as an unanswered annual NPC motion");
            Patch(AccessTools.Method(typeof(CourtAgendaBehavior), "ValidateCouncilProceeding"), nameof(Yes));
            var arguments = new object[] { _realm, PrivyCouncilOffice.Chancellor, _source, null, CampaignTime.Days(25), "stale", null };
            check(!(bool)Call(deliberation, "TryQueueAgendaAppointment", arguments)
                && ((Dictionary<string, CampaignTime>)AccessTools.Field(typeof(CouncilAppointmentDeliberationBehavior), "_pendingVoteDate").GetValue(deliberation)).Count == 0,
                "Queue entry independently rejects a stale scheduled vote before writing lifecycle state");
        }
        finally
        {
            h.UnpatchAll(h.Id); ticks.SetValue(null, oldTicks);
            AccessTools.PropertySetter(typeof(Campaign), "Current").Invoke(null, new object[] { previous });
        }
    }
}
