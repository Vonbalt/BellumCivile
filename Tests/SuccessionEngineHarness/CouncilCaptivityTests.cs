using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Threading;
using BellumCivile;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Library;
using TaleWorlds.Localization;

internal static class CouncilCaptivityTests
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

    private sealed class Store : IDataStore
    {
        internal readonly Dictionary<string, object> Values = new Dictionary<string, object>();
        public bool IsSaving { get; set; } = true;
        public bool IsLoading => !IsSaving;
        public bool SyncData<T>(string key, ref T data)
        {
            if (IsSaving) Values[key] = data;
            else if (Values.TryGetValue(key, out var value)) data = (T)value;
            return true;
        }
    }

    private static readonly Dictionary<Clan, Hero> Heads = new Dictionary<Clan, Hero>();
    private static readonly HashSet<Hero> Captives = new HashSet<Hero>();
    private static Kingdom _realm, _enemy;
    private static Clan _crown, _holder, _challenger, _newCaptive, _player;
    private static float _day, _competence;
    private static bool _war;
    private static int _dismissals, _notices;
    private static Action _duringOfficeIndex;
    private static MobileParty _patrol;
    private static bool Skip() => false;
    private static bool Yes(ref bool __result) { __result = true; return false; }
    private static bool No(ref bool __result) { __result = false; return false; }
    private static bool Now(ref CampaignTime __result) { __result = CampaignTime.Days(_day); return false; }
    private static bool Realm(ref Kingdom __result) { __result = _realm; return false; }
    private static bool Eligible(Kingdom kingdom, ref bool __result) { __result = kingdom == _realm; return false; }
    private static bool Kingdoms(ref MBReadOnlyList<Kingdom> __result)
    { __result = new MBReadOnlyList<Kingdom>(new List<Kingdom> { _realm, _enemy }); return false; }
    private static bool Clans(ref MBReadOnlyList<Clan> __result)
    {
        _duringOfficeIndex?.Invoke();
        __result = new MBReadOnlyList<Clan>(Heads.Keys.ToList());
        return false;
    }
    private static bool Patrol(ref MobileParty __result) { __result = _patrol; return false; }
    private static bool NoPatrolHome(ref Settlement __result) { __result = null; return false; }
    private static bool PatrolFaction(ref IFaction __result) { __result = _realm; return false; }
    private static bool Crown(ref Clan __result) { __result = _crown; return false; }
    private static bool Player(ref Clan __result) { __result = _player; return false; }
    private static bool Leader(Clan __instance, ref Hero __result) { __result = Heads[__instance]; return false; }
    private static bool Resolve(string clanId, ref Clan __result)
    { __result = Heads.Keys.FirstOrDefault(c => c.StringId == clanId); return false; }
    private static bool Captive(Hero __instance, ref bool __result) { __result = Captives.Contains(__instance); return false; }
    private static bool War(ref bool __result) { __result = _war; return false; }
    private static bool Zero(ref float __result) { __result = 0; return false; }
    private static bool Relation(ref int __result) { __result = 0; return false; }
    private static bool Weight(ref float __result) { __result = 2; return false; }
    private static bool Competence(ref float __result) { __result = _competence; return false; }
    private static bool Name(ref TextObject __result) { __result = new TextObject("{=!}Test councillor"); return false; }
    private static bool Dismiss(ref int __result) { _dismissals++; __result = 25; return false; }
    private static bool Notice() { _notices++; return false; }
    private static bool Log(string message)
    {
        if (message.Contains("daily update failed")) throw new InvalidOperationException(message);
        return false;
    }

    internal static void Run(Action<bool, string> check)
    {
        T Blank<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
        Clan House(string id) { var c = Blank<Clan>(); c.StringId = id; Heads[c] = Blank<Hero>(); return c; }
        var old = Campaign.Current;
        var ticks = AccessTools.Field(typeof(CampaignTime), "TimeTicksPerDay");
        var oldTicks = ticks.GetValue(null);
        var h = new Harmony("bellum.test.council_captivity");
        void Patch(MethodBase m, string name) => h.Patch(m, prefix: new HarmonyMethod(typeof(CouncilCaptivityTests), name));
        var type = typeof(PrivyCouncilBehavior);
        var b = new PrivyCouncilBehavior();
        var manager = new Manager(); manager.AddBehavior(b);
        void Invalidate() => AccessTools.Method(type, "InvalidateRuntimeCache").Invoke(b, new object[] { true });
        PrivyCouncilOfficeRecord Seat(PrivyCouncilOffice office, float controversy = 20)
        {
            var seat = new PrivyCouncilOfficeRecord(_realm.StringId, office, 0);
            seat.Appoint(_holder.StringId, 1);
            seat.SetControversy(controversy, "test", 2);
            seat.SetAssignment(PrivyCouncilAssignmentRegistry.GetAssignments(office)[1].Id, 2);
            AccessTools.Field(type, "_officeRecords").SetValue(b, new List<PrivyCouncilOfficeRecord> { seat });
            Invalidate();
            return seat;
        }
        void Daily() { _day++; AccessTools.Method(type, "OnDailyTick").Invoke(b, null); }
        float Penalty(PrivyCouncilOffice office) => (float)AccessTools.Method(type, "GetCaptivitySupportPenalty")
            .Invoke(b, new object[] { _realm, office });
        try
        {
            ticks.SetValue(null, 1000L); Heads.Clear(); Captives.Clear(); _day = 10; _war = false; _competence = 60;
            _realm = Blank<Kingdom>(); _realm.StringId = "council_captivity";
            _enemy = Blank<Kingdom>(); _enemy.StringId = "enemy";
            _crown = House("crown"); _holder = House("holder"); _challenger = House("challenger");
            _newCaptive = House("new_captive"); _player = _challenger;
            var campaign = Blank<Campaign>();
            AccessTools.PropertySetter(typeof(Campaign), "Current").Invoke(null, new object[] { campaign });
            campaign.AddCampaignBehaviorManager(manager);
            Patch(AccessTools.PropertyGetter(typeof(CampaignTime), "Now"), nameof(Now));
            Patch(AccessTools.Method(typeof(CampaignTime), "HoursFromNow"), nameof(Now));
            Patch(AccessTools.PropertyGetter(typeof(Kingdom), "All"), nameof(Kingdoms));
            Patch(AccessTools.PropertyGetter(typeof(Kingdom), "Clans"), nameof(Clans));
            Patch(AccessTools.PropertyGetter(typeof(Kingdom), "RulingClan"), nameof(Crown));
            Patch(AccessTools.PropertyGetter(typeof(Kingdom), "IsEliminated"), nameof(No));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "All"), nameof(Clans));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "Kingdom"), nameof(Realm));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "Leader"), nameof(Leader));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "PlayerClan"), nameof(Player));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "Renown"), nameof(Zero));
            Patch(AccessTools.PropertyGetter(typeof(Hero), "Name"), nameof(Name));
            Patch(AccessTools.PropertyGetter(typeof(Hero), "IsPrisoner"), nameof(Captive));
            Patch(AccessTools.Method(typeof(Hero), "GetRelation"), nameof(Relation));
            Patch(AccessTools.Method(typeof(FactionManager), "IsAtWarAgainstFaction"), nameof(War));
            Patch(AccessTools.Method(typeof(NobleClanEligibilityHelper), "IsLiveNobleClan"), nameof(Yes));
            Patch(AccessTools.Method(type.Assembly.GetType("BellumCivile.MarriageAllianceHelper"), "HasMarriageAlliance"), nameof(No));
            Patch(AccessTools.Method(type, "IsEligiblePermanentRealm"), nameof(Eligible));
            Patch(AccessTools.Method(type, "EnsureCouncilForKingdom"), nameof(Skip));
            Patch(AccessTools.Method(type, "IsOfficeUnlocked"), nameof(Yes));
            Patch(AccessTools.Method(type, "ResolveClan"), nameof(Resolve));
            Patch(AccessTools.Method(type, "CalculateCompetence"), nameof(Competence));
            Patch(AccessTools.Method(type, "GetPoliticalWeight"), nameof(Weight));
            Patch(AccessTools.Method(type, "GetLocalizedOfficeName"), nameof(Name));
            Patch(AccessTools.Method(type, "ApplyDismissalAftermath"), nameof(Dismiss));
            Patch(AccessTools.Method(type, "QueuePlayerDisgracedDismissal"), nameof(Notice));
            foreach (var method in type.Assembly.GetType("BellumCivile.BellumCivileNotifications").GetMethods().Where(m => m.Name == "Show"))
                Patch(method, nameof(Skip));
            Patch(AccessTools.Method(typeof(BellumCivileLogger), "Log"), nameof(Log));

            foreach (PrivyCouncilOffice office in Enum.GetValues(typeof(PrivyCouncilOffice)))
            {
                Captives.Clear(); _war = false; _dismissals = _notices = 0;
                var seat = Seat(office);
                string assignment = seat.AssignmentId;
                float freeSupport = b.CalculateAppointmentSupport(_realm, _challenger, _holder, office);
                Captives.Add(Heads[_holder]); Captives.Add(Heads[_newCaptive]);
                var candidates = b.GetAppointmentCandidatesForVote(_realm, office);
                check(candidates.Contains(_holder) && candidates.Contains(_challenger) && !candidates.Contains(_newCaptive),
                    office + ": captive incumbent can be retained, but new captive nominees are excluded");
                check(Math.Abs(freeSupport - b.CalculateAppointmentSupport(_realm, _challenger, _holder, office) - 10) < .001,
                    office + ": capture reduces shared support score by ten");
                _war = true;
                check(Penalty(office) == (office == PrivyCouncilOffice.Marshal ? 20 : 10),
                    office + ": only wartime Marshal has the larger penalty");
                check(b.GetAssignmentEffectMultiplier(_realm, office, assignment) == 0
                    && b.GetAssignmentDrawbackMultiplier(_realm, office, assignment) == 0,
                    office + ": captivity suspends assignment effects and drawbacks");
                foreach (var player in new[] { _holder, _crown, _challenger })
                {
                    _player = player; Daily();
                    check(b.GetOfficeHolder(_realm, office) == _holder && seat.AppointedDay == 1 && _dismissals == 0,
                        office + ": player/NPC capture preserves tenure and creates no dismissal grievance");
                }
                check(seat.Controversy == 21.5f && seat.AssignmentId == assignment && seat.LastAssignmentChangedDay == 2,
                    office + ": captive ticks add 0.5 without recovery or resetting assignment");
                check(b.GetCouncilInfluenceGain(_holder) > 0,
                    office + ": captive holder retains council influence");
                check(b.TryAppointOffice(_realm, office, _holder, false) && seat.Controversy == 21.5f && seat.AppointedDay == 1,
                    office + ": retention does not reset tenure or controversy");
                Captives.Remove(Heads[_holder]); Invalidate();
                check(Penalty(office) == 0 && b.GetAssignmentEffectMultiplier(_realm, office, assignment) > 0,
                    office + ": release removes support penalty and resumes duties");
                Daily();
                check(seat.Controversy == 21.25f && seat.AssignmentId == assignment,
                    office + ": release resumes normal recovery with the same assignment");
                Captives.Add(Heads[_holder]); Daily();
                check(seat.Controversy == 21.75f, office + ": recapture resumes accumulation");
                seat.SetControversy(99.5f, "test", _day); _player = _holder; Daily();
                check(b.GetOfficeHolder(_realm, office) == null && seat.Controversy == 100 && _dismissals == 1 && _notices == 1,
                    office + ": reaches 100 and dismisses once, including advisors and player holders");
                Daily();
                check(_dismissals == 1 && !seat.IsCaptivityVacancy,
                    office + ": dismissal creates ordinary vacancy without repeated grievances");
            }

            Captives.Clear(); _player = _challenger;
            Seat(PrivyCouncilOffice.Seneschal);
            check(b.GetSeneschalTaxBonusRate(_realm) > 0, "Free seneschal supplies tax bonus");
            Captives.Add(Heads[_holder]);
            check(b.GetSeneschalTaxBonusRate(_realm) == 0, "Captured seneschal supplies no tax bonus");
            Seat(PrivyCouncilOffice.Chancellor); Captives.Clear();
            check(b.GetClaimFeudPressureMultiplier(_realm) < 1.5f, "Free chancellor moderates feud pressure");
            Captives.Add(Heads[_holder]);
            check(b.GetClaimFeudPressureMultiplier(_realm) == 1.5f, "Captured chancellor cannot moderate feud pressure");
            Seat(PrivyCouncilOffice.Spymaster); Captives.Clear();
            check(b.GetIntrigueProxy(_realm) == Heads[_holder], "Free spymaster serves as intrigue proxy");
            Captives.Add(Heads[_holder]);
            check(b.GetIntrigueProxy(_realm) == Heads[_crown] && b.GetIntrigueSkillModifier(_realm, true) == 0
                && b.GetIntrigueSkillModifier(_realm, false) == -50, "Captured spymaster is unavailable for intrigue duties");

            Captives.Clear();
            var reassurance = Seat(PrivyCouncilOffice.Chancellor, 20);
            reassurance.SetAssignment("chancellor_reassure_realms", 2);
            Invalidate();
            var assignmentUpkeep = AccessTools.Method(type, "ApplyAssignmentUpkeepControversy");
            var reassuranceDefinition = PrivyCouncilAssignmentRegistry.GetAssignment(reassurance.AssignmentId);
            check(b.GetAssignmentDescription(reassuranceDefinition, _realm).ToString().Contains("1 Chancellor controversy each week")
                && b.GetAssignmentStatusHint(_realm, PrivyCouncilOffice.Chancellor).ToString().Contains(
                    $"{.9f:0.#} Chancellor controversy each week"),
                "Reassurance description shows the base cost and its active tooltip shows the competence-adjusted cost");
            assignmentUpkeep.Invoke(b, new object[] { _realm });
            check(Math.Abs(reassurance.Controversy - 20.9f) < .001f,
                "Reassuring realms generates competence-adjusted weekly controversy once");
            _competence = 50;
            Invalidate();
            float beforeAverageUpkeep = reassurance.Controversy;
            assignmentUpkeep.Invoke(b, new object[] { _realm });
            check(Math.Abs(reassurance.Controversy - beforeAverageUpkeep - 1f) < .001f,
                "Reassuring realms generates one controversy at average competence");
            _competence = 60;
            Captives.Add(Heads[_holder]);
            Invalidate();
            float beforeCaptiveUpkeep = reassurance.Controversy;
            assignmentUpkeep.Invoke(b, new object[] { _realm });
            check(reassurance.Controversy == beforeCaptiveUpkeep,
                "A captive chancellor does not generate reassurance assignment upkeep");
            Captives.Clear();
            var options = type.Assembly.GetType("BellumCivile.BellumCivileOptions", true);
            var settingsType = type.Assembly.GetType("BellumCivile.BellumCivileSettings", true);
            var settingsField = AccessTools.Field(options, "_settings");
            var oldSettings = settingsField.GetValue(null);
            try
            {
                var settings = Activator.CreateInstance(settingsType, true);
                AccessTools.Property(settingsType, "EnableDefensivePacts").SetValue(settings, false);
                settingsField.SetValue(null, settings);
                assignmentUpkeep.Invoke(b, new object[] { _realm });
                check(reassurance.Controversy == beforeCaptiveUpkeep,
                    "Reassurance upkeep stops when defensive pacts are disabled");
            }
            finally { settingsField.SetValue(null, oldSettings); }
            Captives.Add(Heads[_holder]);

            // Exercise the native decision contract, then the behavior's save/load callback.
            var retained = Seat(PrivyCouncilOffice.Marshal, 35);
            var decision = new PrivyCouncilAppointmentDecision(_crown, PrivyCouncilOffice.Marshal,
                new[] { _holder, _challenger });
            var outcome = decision.DetermineInitialCandidates()
                .OfType<PrivyCouncilAppointmentDecision.PrivyCouncilAppointmentOutcome>()
                .Single(c => c.CandidateClan == _holder);
            decision.ApplyChosenOutcome(outcome);
            check(retained.AppointedDay == 1 && retained.Controversy == 35,
                "Native retention outcome preserves captive incumbent tenure and controversy");
            var oldHistory = (Dictionary<string, float>)AccessTools.Field(type, "_captivityDismissalUntilByClan").GetValue(b);
            oldHistory["legacy"] = _day + 100;
            var saved = new Store(); b.SyncData(saved); saved.IsSaving = false;
            b = new PrivyCouncilBehavior(); b.SyncData(saved);
            manager.ClearBehaviors(); manager.AddBehavior(b);
            check(b.GetOfficeHolder(_realm, PrivyCouncilOffice.Marshal) == _holder && Penalty(PrivyCouncilOffice.Marshal) == 20
                && ((Dictionary<string, float>)AccessTools.Field(type, "_captivityDismissalUntilByClan").GetValue(b)).ContainsKey("legacy"),
                "Save/load callback retains captive holder and historical dismissal entries");
            Daily();
            check(retained.Controversy == 35.5f, "Loaded captive resumes gradual controversy");

            Captives.Clear();
            var core = Seat(PrivyCouncilOffice.Marshal);
            var advisor = new PrivyCouncilOfficeRecord(_realm.StringId, PrivyCouncilOffice.FirstAdvisor, 0);
            advisor.Appoint(_challenger.StringId, 1);
            advisor.SetAssignment("first_advisor_counsel_crown", 2);
            AccessTools.Field(type, "_officeRecords").SetValue(b, new List<PrivyCouncilOfficeRecord> { core, advisor });
            Invalidate();
            check(b.GetEffectiveCoreOfficeCompetence(_realm, PrivyCouncilOffice.Marshal) > 60,
                "Available advisor contributes to cached core competence");
            Captives.Add(Heads[_challenger]);
            AccessTools.Method(type, "OnCouncillorCaptured").Invoke(b, new object[] { null, Heads[_challenger] });
            check(b.GetEffectiveCoreOfficeCompetence(_realm, PrivyCouncilOffice.Marshal) == 60,
                "Capture event removes cached advisor bonus on the same day");
            Captives.Clear();
            var release = AccessTools.Method(type, "OnCouncillorReleased");
            release.Invoke(b, new object[] { Heads[_challenger], null, null,
                Activator.CreateInstance(release.GetParameters()[3].ParameterType), false });
            check(b.GetEffectiveCoreOfficeCompetence(_realm, PrivyCouncilOffice.Marshal) > 60,
                "Release event restores advisor bonus on the same day");
            var disgraced = Seat(PrivyCouncilOffice.SecondAdvisor, 100);
            _dismissals = 0; Daily();
            check(string.IsNullOrEmpty(disgraced.HolderClanId) && _dismissals == 1,
                "Free advisor at 100 is dismissed before daily recovery");
            disgraced.SetControversy(0, "test", _day);
            _day += 10; Daily();
            check(disgraced.Controversy == 0, "Empty advisory office does not accrue core vacancy controversy");

            var legacy = new PrivyCouncilOfficeRecord(_realm.StringId, PrivyCouncilOffice.Marshal, 0);
            var marker = AccessTools.Field(typeof(PrivyCouncilOfficeRecord), "_captivityVacancy");
            check(marker.GetCustomAttributes(false).Any(a => a.GetType().Name == "SaveableFieldAttribute"),
                "Legacy captivity vacancy field remains save-compatible");
            marker.SetValue(legacy, true);
            check(legacy.IsCaptivityVacancy, "Old vacancy marker is still readable");
            legacy.Appoint(_holder.StringId, _day);
            check(!legacy.IsCaptivityVacancy, "New appointment clears legacy vacancy flag");
            var severity = AccessTools.Method(type, "DismissalSeverity");
            check((int)severity.Invoke(null, new object[] { 100f, false }) == 25
                && (int)severity.Invoke(null, new object[] { 100f, true }) == 13,
                "Normal dismissal severity and legacy captivity grievance semantics remain distinct");

            var patrolSeat = Seat(PrivyCouncilOffice.Marshal);
            patrolSeat.SetAssignment("marshal_organize_patrols", _day);
            RunAssignmentThreadChecks(check, b, Invalidate, Patch);
        }
        finally
        {
            _duringOfficeIndex = null; _patrol = null;
            h.UnpatchAll(h.Id); Heads.Clear(); Captives.Clear(); ticks.SetValue(null, oldTicks);
            AccessTools.PropertySetter(typeof(Campaign), "Current").Invoke(null, new object[] { old });
        }
    }

    private static void RunAssignmentThreadChecks(Action<bool, string> check, PrivyCouncilBehavior council,
        Action invalidate, Action<MethodBase, string> patch)
    {
        const PrivyCouncilOffice Office = PrivyCouncilOffice.Marshal;
        const string Assignment = "marshal_organize_patrols";
        const string Aspect = "patrol_party_size";
        var incidents = new CouncilIncidentBehavior();
        incidents.SetAssignmentAspectModifier(_realm, Office, Assignment, Aspect, "thread_test", 1.25f, 5f);

        _patrol = (MobileParty)FormatterServices.GetUninitializedObject(typeof(MobileParty));
        var party = (PartyBase)FormatterServices.GetUninitializedObject(typeof(PartyBase));
        patch(AccessTools.PropertyGetter(typeof(PartyBase), "MobileParty"), nameof(Patrol));
        patch(AccessTools.PropertyGetter(typeof(MobileParty), "IsPatrolParty"), nameof(Yes));
        patch(AccessTools.PropertyGetter(typeof(MobileParty), "HomeSettlement"), nameof(NoPatrolHome));
        patch(AccessTools.PropertyGetter(typeof(MobileParty), "MapFaction"), nameof(PatrolFaction));
        var patches = typeof(PrivyCouncilBehavior).Assembly.GetType("BellumCivile.CouncilAssignmentRuntimePatches", true);
        var councilField = AccessTools.Field(patches, "_councilBehavior");
        var incidentField = AccessTools.Field(patches, "_incidentBehavior");
        var oldCouncil = councilField.GetValue(null);
        var oldIncidents = incidentField.GetValue(null);
        var applyPatrol = AccessTools.Method(patches, "ApplyPatrolSizeAssignment");
        float PatrolSize()
        {
            var args = new object[] { party, new ExplainedNumber(100f) };
            applyPatrol.Invoke(null, args);
            return ((ExplainedNumber)args[1]).ResultNumber;
        }

        try
        {
            councilField.SetValue(null, council);
            incidentField.SetValue(null, incidents);
            invalidate();
            float size = 0, effect = 0, drawback = 0, aspect = 0;
            bool officeMissing = false;
            Exception failure = null;
            void ReadOnWorker()
            {
                var worker = new Thread(() =>
                {
                    try
                    {
                        size = PatrolSize();
                        effect = council.GetAssignmentEffectMultiplier(_realm, Office, Assignment, requireFunding: true);
                        drawback = council.GetAssignmentDrawbackMultiplier(_realm, Office, Assignment);
                        aspect = incidents.GetAssignmentAspectMultiplier(_realm, Office, Assignment, Aspect);
                        officeMissing = council.GetOfficeRecords(_realm).Count == 0 && council.GetOfficeRecord(_realm, Office) == null
                            && council.GetOfficeHolder(_realm, Office) == null;
                    }
                    catch (Exception ex) { failure = ex; }
                }) { IsBackground = true };
                worker.Start();
                if (!worker.Join(TimeSpan.FromSeconds(5)))
                    throw new TimeoutException("Worker council reads must not wait for the campaign thread");
                if (failure != null) throw new InvalidOperationException("Worker council query failed", failure);
            }

            bool overlapped = false;
            _duringOfficeIndex = () =>
            {
                _duringOfficeIndex = null;
                overlapped = true;
                ReadOnWorker();
            };
            float ownerSize = PatrolSize();
            check(overlapped && size == 100f && effect == 0f && drawback == 0f && aspect == 1f && officeMissing,
                "Overlapping worker reads leave patrol size and incidents neutral during a dirty council index rebuild");
            check(ownerSize > 110f && Math.Abs(incidents.GetAssignmentAspectMultiplier(_realm, Office, Assignment, Aspect) - 1.25f) < .001f
                && council.GetOfficeRecords(_realm).Count == 1,
                "Campaign-thread patrol size and incident bonus remain active without duplicate office records");

            ReadOnWorker();
            check(size == 100f && effect == 0f && drawback == 0f && aspect == 1f && officeMissing,
                "Worker reads also bypass live records and assignment state when the caches are already warm");
            check(Math.Abs(PatrolSize() - ownerSize) < .001f, "Worker reads leave the campaign thread's cached patrol bonus unchanged");
        }
        finally
        {
            _duringOfficeIndex = null;
            councilField.SetValue(null, oldCouncil);
            incidentField.SetValue(null, oldIncidents);
        }
    }
}
