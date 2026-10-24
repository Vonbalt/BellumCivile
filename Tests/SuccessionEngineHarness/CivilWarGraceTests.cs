using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using BellumCivile;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

internal static class CivilWarGraceTests
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
        public bool IsLoading { get; set; }
        public bool IsSaving => !IsLoading;
        public bool SyncData<T>(string key, ref T value)
        {
            if (IsLoading) { if (!Values.TryGetValue(key, out var saved)) return false; value = (T)saved; }
            else Values[key] = value;
            return true;
        }
    }
    private static Campaign _campaign;
    private static Kingdom _parent;
    private static Clan _clan;
    private static double _day;
    private static readonly HashSet<Kingdom> Gone = new HashSet<Kingdom>();
    private static readonly List<Kingdom> Realms = new List<Kingdom>();
    private static MultiSelectionInquiryData _inquiry;
    private static InquiryData _confirmation;
    private static int _memories, _notices;
    private static bool _warActive;
    private static T Blank<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    private static bool Campaign(ref Campaign __result) { __result = _campaign; return false; }
    private static bool Now(ref CampaignTime __result) { __result = CampaignTime.Days((float)_day); return false; }
    private static bool Eliminated(Kingdom __instance, ref bool __result) { __result = Gone.Contains(__instance); return false; }
    private static bool Realm(ref Kingdom __result) { __result = _parent; return false; }
    private static bool No(ref bool __result) { __result = false; return false; }
    private static bool Active(ref bool __result) { __result = _warActive; return false; }
    private static bool Skip() => false;
    private static bool NoUnsafe(ref bool __result) { __result = false; return false; }
    private static bool Player(ref Clan __result) { __result = _clan; return false; }
    private static bool All(ref MBReadOnlyList<Kingdom> __result) { __result = new MBReadOnlyList<Kingdom>(Realms); return false; }
    private static bool Yes(ref bool __result) { __result = true; return false; }
    private static bool Two(ref int __result) { __result = 2; return false; }
    private static bool Influence(ref float __result) { __result = 1000; return false; }
    private static bool Name(ref TextObject __result) { __result = new TextObject("Test Realm"); return false; }
    private static bool NoPolicy(ref PolicyObject __result) { __result = null; return false; }
    private static bool Capture(MultiSelectionInquiryData __0) { _inquiry = __0; return false; }
    private static bool Confirm(InquiryData __0) { _confirmation = __0; return false; }
    private static bool Notice() { _notices++; return false; }
    private static bool Memory() { _memories++; return false; }
    private static bool Future(CampaignTime __instance, ref bool __result) { __result = __instance.ToDays > _day; return false; }
    private static bool Past(CampaignTime __instance, ref bool __result) { __result = __instance.ToDays < _day; return false; }
    private static bool Date(ref string __result) { __result = "Day 100"; return false; }
    private static IGrouping<string, T> Group<T>(object[] motions) => motions.Cast<T>().GroupBy(_ => "Ruler's authority").Single();
    private static object Call(object owner, string name, params object[] args) => AccessTools.Method(owner.GetType(), name).Invoke(owner, args);
    private static void Field(object owner, string name, object value) => AccessTools.Field(owner.GetType(), name).SetValue(owner, value);
    private static Kingdom NewRealm(string id) { var realm = Blank<Kingdom>(); realm.StringId = id; return realm; }
    private static FactionObject Faction(Kingdom realm, FactionType type = FactionType.Abdication, bool fought = false)
    {
        var faction = Blank<FactionObject>();
        Field(faction, "_name", "Test coalition"); Field(faction, "_parentKingdom", realm);
        Field(faction, "_members", new List<Clan> { _clan }); Field(faction, "_leader", _clan);
        Field(faction, "_type", type); Field(faction, "_rebellionCreationCompleted", fought);
        return faction;
    }

    internal static void Run(Action<bool, string> check)
    {
        var harmony = new Harmony("bellum.test.civil_war_grace");
        var options = typeof(FactionObject).Assembly.GetType("BellumCivile.BellumCivileOptions", true);
        var settingsCache = AccessTools.Field(options, "_settings");
        object oldSettings = settingsCache.GetValue(null);
        var ticks = AccessTools.Field(typeof(CampaignTime), "TimeTicksPerDay");
        object oldTicks = ticks.GetValue(null);
        var calendar = new[] { "DaysInWeek", "WeeksInSeason", "SeasonsInYear" }.Select(n => AccessTools.Field(typeof(CampaignTime), n)).ToArray();
        var oldCalendar = calendar.Select(f => f.GetValue(null)).ToArray();
        void Patch(MethodBase method, string prefix) => harmony.Patch(method, prefix: new HarmonyMethod(typeof(CivilWarGraceTests), prefix));
        _campaign = Blank<Campaign>(); _parent = NewRealm("empire_w");
        _clan = Blank<Clan>(); _clan.StringId = "rebels";
        var manager = new Manager(); _campaign.AddCampaignBehaviorManager(manager);
        FactionManagerBehavior behavior = null;
        List<CivilWarGraceRecord> records = null;
        List<FactionObject> active = null;
        var settingsType = typeof(FactionObject).Assembly.GetType("BellumCivile.BellumCivileSettings", true);
        object settings = null;
        void Years(int years) => AccessTools.Property(settingsType, "CivilWarGraceYears").SetValue(settings, years);
        void Setup(int years = 2)
        {
            Gone.Clear(); _day = 100; manager.Items.Clear();
            _warActive = false;
            Realms.Clear(); Realms.Add(_parent);
            behavior = new FactionManagerBehavior(); manager.Items.Add(behavior);
            records = (List<CivilWarGraceRecord>)AccessTools.Field(typeof(FactionManagerBehavior), "_civilWarGrace").GetValue(behavior);
            active = (List<FactionObject>)AccessTools.Field(typeof(FactionManagerBehavior), "_activeFactions").GetValue(behavior);
            Years(years);
        }
        void Queue(FactionObject faction, Kingdom shell, Kingdom result = null) => Call(behavior, "QueueCivilWarGrace", faction, shell, result);
        void Maintain() => Call(behavior, "MaintainCivilWarGrace");
        bool Protected(Kingdom realm) => (bool)Call(behavior, "HasCivilWarGrace", realm);
        bool Blocked(FactionObject faction) => (bool)Call(behavior, "IsCivilWarGraceBlocked", faction);
        bool CanIssue(FactionObject faction) => (bool)Call(faction, "CanTriggerUltimatum", new object[] { null });
        void Outcomes(FactionObject faction, params Kingdom[] realms) => Call(behavior, "AddCivilWarGraceRealms", faction, realms);
        try
        {
            Patch(AccessTools.PropertyGetter(typeof(Campaign), "Current"), nameof(Campaign));
            Patch(AccessTools.PropertyGetter(typeof(CampaignTime), "Now"), nameof(Now));
            Patch(AccessTools.PropertyGetter(typeof(CampaignTime), "IsFuture"), nameof(Future));
            Patch(AccessTools.PropertyGetter(typeof(CampaignTime), "IsPast"), nameof(Past));
            Patch(AccessTools.Method(typeof(CampaignTime), "ToString", Type.EmptyTypes), nameof(Date));
            Patch(AccessTools.PropertyGetter(typeof(Kingdom), "IsEliminated"), nameof(Eliminated));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "Kingdom"), nameof(Realm));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "PlayerClan"), nameof(Player));
            Patch(AccessTools.PropertyGetter(typeof(Kingdom), "All"), nameof(All));
            Patch(AccessTools.Method(typeof(FactionObject), "IsCivilWarActive"), nameof(Active));
            Patch(AccessTools.Method(typeof(FactionObject), "ShouldDelayUltimatumForUnsafeLeaderLocation"), nameof(NoUnsafe));
            Patch(AccessTools.Method(typeof(FactionObject).Assembly.GetType("BellumCivile.FeudalTitleStylePresetCatalog"), "CreateDropdown"), nameof(Skip));
            ticks.SetValue(null, 1000L);
            calendar[0].SetValue(null, 7); calendar[1].SetValue(null, 3); calendar[2].SetValue(null, 4);
            settings = Activator.CreateInstance(settingsType, true); settingsCache.SetValue(null, settings);
            double year = Math.Max(1, CampaignTime.DaysInYear);
            Setup();
            var shell = NewRealm("empire_w_rebels_rebels");
            var war = Faction(_parent, fought: true); active.Add(war);
            Queue(war, shell); Queue(war, shell);
            check(records.Count == 1 && records[0].DurationDays == 2 * year && !records[0].Settled,
                "One completed-war identity captures the configured two-year duration only once");
            check(Protected(_parent) && !Protected(shell), "Settlement immediately protects the parent, never the temporary war shell");
            _day += 20; Maintain();
            check(!records[0].Settled, "An active faction or surviving rebel shell cannot start consuming the grace period");
            active.Clear(); Gone.Add(shell);
            var accession = new CrownAccessionBehavior(); manager.Items.Add(accession);
            var crown = new CrownAccessionRecord { Realm = _parent };
            ((List<CrownAccessionRecord>)AccessTools.Field(typeof(CrownAccessionBehavior), "_accessions").GetValue(accession)).Add(crown);
            Maintain(); check(!records[0].Settled, "A pending accession keeps protection active without consuming the recovery years");
            crown.Completed = true; Maintain();
            double end = _day + 2 * year;
            check(records[0].Settled && records[0].UntilDay == end, "The full grace period starts after realm retirement and succession settle");
            _day += 1; Maintain(); Queue(war, shell);
            check(records.Count == 1 && records[0].UntilDay == end, "Hourly maintenance and repeated outcome callbacks cannot renew the same war");
            Years(5); check(records[0].UntilDay == end, "Changing MCM does not rewrite a recorded recovery period");
            var fresh = Faction(_parent);
            check(Blocked(fresh) && !CanIssue(fresh), "A fresh ordinary faction cannot issue an ultimatum during grace");
            fresh.DailyTick(); check(fresh.Discontent == 0, "Dormant conspiracies do not gain discontent during grace");
            behavior.RegisterNewFaction(fresh); check(!active.Contains(fresh), "New rebellion registration is blocked before membership or summary callbacks");
            var member = Blank<Clan>(); fresh.AddMember(member);
            check(!fresh.Members.Contains(member), "New supporters cannot join an uncommitted conspiracy during grace");
            fresh.Leader = Blank<Clan>(); check(Blocked(fresh), "Replacing the faction leader cannot bypass realm-wide protection");
            var bloc = Faction(_parent, FactionType.Nobility);
            check(!Blocked(bloc), "Ordinary court affiliation and non-rebellion business remain available");
            var ideology = new IdeologyBehavior();
            check(!(bool)Call(ideology, "CanSelectCourtChallenge", bloc, _parent)
                && !(bool)Call(ideology, "TryLaunchCourtChallenge", bloc, _parent),
                "Court challenge selection and direct coalition launch both respect the realm timer");
            check(!(bool)Call(ideology, "TriggerGrandCoalition", bloc, _parent, behavior, false),
                "The emergency/direct coalition backend cannot bypass protection");
            var court = new CourtAgendaBehavior(); manager.Items.Add(court);
            check(!(bool)Call(court, "MayStartAutomaticCourtChallenge", bloc, _parent), "Automatic ordinary and emergency court evaluation is blocked before rolling");
            ((List<CourtAgendaRecord>)AccessTools.Field(typeof(CourtAgendaBehavior), "_agendas").GetValue(court))
                .Add(new CourtAgendaRecord { Realm = _parent, Faction = bloc, State = CourtAgendaState.Crisis });
            check(!(bool)Call(court, "MayStartAutomaticCourtChallenge", bloc, _parent)
                && !(bool)Call(ideology, "TryLaunchCourtChallenge", bloc, _parent),
                "An announced but unissued court crisis cannot bypass pacification");
            var heirs = new SuccessionChallengeBehavior();
            check(Call(heirs, "TryBegin", _parent, Blank<Hero>(), 0d, false) == null
                && Call(heirs, "TryBegin", _parent, Blank<Hero>(), 0d, true) == null,
                "Both AI and player hereditary challenges stop at the shared eligibility gate");
            var contests = new ElectiveContestBehavior();
            var winner = Blank<Hero>();
            var ballot = new ElectiveContestRecord { Id = "postwar_election", Realm = _parent, ElectedWinner = winner };
            ((List<ElectiveContestRecord>)AccessTools.Field(typeof(ElectiveContestBehavior), "_contests").GetValue(contests)).Add(ballot);
            Call(contests, "CompleteAccession", new CrownAccessionRecord { ElectiveContestId = ballot.Id,
                Realm = _parent, Heir = winner, Completed = true, TitleTransferred = true });
            check(ballot.AccessionCompleted && ballot.Closed && !ballot.WarDispatchStarted,
                "An election completed during grace installs its winner without opening a new armed dispute");
            _day += 2 * year;
            check(!(bool)Call(ballot, "ShouldAdvance", _day), "A blocked election contest cannot revive an obsolete ballot when grace expires");
            _day -= 2 * year;
            var committed = Faction(_parent); Field(committed, "_ultimatumPending", true);
            check(!Blocked(committed), "A pending ultimatum is not cancelled by another civil war ending");
            committed = Faction(_parent); Call(committed, "BindSuccessionChallenge", "saved-challenge");
            check(!Blocked(committed), "Frozen hereditary and elective war startup remains recoverable");
            var resistance = Faction(_parent); Call(resistance, "MarkJudgmentResistance");
            behavior.RegisterNewFaction(resistance);
            check(active.Contains(resistance) && !Blocked(resistance) && CanIssue(resistance),
                "Deliberate treason judgments can still provoke a new resistance war");
            check(Call(behavior, "CivilWarGraceBlock", _parent).ToString().StartsWith("The realm is currently pacified."), "Player rejection uses the shared pacification message");
            _day = end - .25;
            check(Protected(_parent) && Call(behavior, "CivilWarGraceBlock", _parent).ToString().Contains("barred for 1 days."),
                "A fractional final day remains protected and rounds up for display");
            _day = end;
            check(!Protected(_parent) && !Blocked(fresh), "Protection expires exactly at its saved deadline without a daily-tick delay");
            check(Call(behavior, "CivilWarGraceBlock", _parent) == null, "Expired protection leaves no stale blocking tooltip");

            Setup(3); war = Faction(_parent, FactionType.Independence, true); Queue(war, shell);
            var countyA = NewRealm("empire_w_indep_a"); var countyB = NewRealm("empire_w_indep_b");
            Outcomes(war, countyA, countyB, countyA, shell, null); Gone.Add(shell); Maintain();
            check(records.Single().Realms.Count == 3 && Protected(_parent) && Protected(countyA) && Protected(countyB),
                "Independence protects the surviving parent and every permanent successor without duplicate targets");
            double savedEnd = records[0].UntilDay;
            var store = new Store(); Call(behavior, "SyncCivilWarGrace", store); store.IsLoading = true;
            var loaded = new FactionManagerBehavior(); Call(loaded, "SyncCivilWarGrace", store);
            check((bool)Call(loaded, "HasCivilWarGrace", countyB)
                && ((List<CivilWarGraceRecord>)AccessTools.Field(typeof(FactionManagerBehavior), "_civilWarGrace").GetValue(loaded)).Single().UntilDay == savedEnd,
                "Protection for multiple resulting realms survives a behavior save/load roundtrip");
            Call(new FactionManagerBehavior(), "SyncCivilWarGrace", new Store { IsLoading = true });
            check(true, "Older saves without the new key initialize safely without retroactive cooldowns");
            Setup(1); war = Faction(_parent, fought: true); Queue(war, shell);
            var restored = NewRealm("empire_w_restored"); Outcomes(war, restored); Gone.Add(_parent); Gone.Add(shell); Maintain();
            check(!Protected(_parent) && Protected(restored) && records.Single().DurationDays == year,
                "A restored successor receives the one-year configured recovery while the destroyed parent is ignored");
            Setup(0); war = Faction(_parent, fought: true); Queue(war, shell);
            check(!Protected(_parent), "Zero records no new blocking period even during settlement");
            Years(2); Queue(war, shell); check(records.Count == 1 && !Protected(_parent), "A recorded zero cannot become a new grace period on retry after changing settings");
            Gone.Add(shell); Maintain();
            check(records.Single().Settled && !Protected(_parent), "Zero-duration settlements finish without granting protection");
            Setup(); Queue(Faction(_parent), null); Queue(Faction(_parent, FactionType.Nobility, true), shell);
            check(records.Count == 0, "Peaceful ultimatums, preparations and ordinary court factions cannot generate postwar immunity");
            check(!Protected(null) && !Protected(NewRealm("foreign")), "Unrelated kingdoms and null references are unaffected");
            Setup(); war = Faction(_parent, fought: true); Queue(war, shell); Gone.Add(shell); Maintain();
            end = records[0].UntilDay; _day += 10;
            var second = Faction(_parent, fought: true); Queue(second, shell); Maintain();
            check(records.Count == 2 && records.Last().UntilDay > end && Protected(_parent),
                "A genuinely different completed civil war grants a new period without reusing the first war's receipt");

            Setup(0);
            var modded = NewRealm("rhodok"); var eliminated = NewRealm("eliminated");
            Realms.AddRange(new[] { modded, eliminated, shell }); Gone.Add(eliminated);
            Call(behavior, "ApplyInitialCivilWarGrace", new object[] { null });
            check(!Protected(_parent), "Loading an existing save cannot arm startup pacification");
            Call(behavior, "ArmInitialCivilWarGrace", new object[] { null });
            double initialEnd = _day + year;
            Call(behavior, "ApplyInitialCivilWarGrace", new object[] { null });
            check(Protected(_parent) && Protected(modded) && !Protected(eliminated) && !Protected(shell),
                "A new game protects every starting permanent realm, including modded realms, for one calendar year");
            check(Call(behavior, "CivilWarGraceBlock", modded).ToString() ==
                $"The realm is currently pacified. New rebellions against the crown are barred for {(int)year} days.",
                "Startup protection uses exactly the same countdown tooltip as postwar protection");
            check(records.Count == 0, "Startup protection neither invents a civil war nor depends on the postwar MCM slider");
            var laterRealm = NewRealm("later_realm"); Realms.Add(laterRealm); _day += 10;
            Call(behavior, "ArmInitialCivilWarGrace", new object[] { null }); Call(behavior, "ApplyInitialCivilWarGrace", new object[] { null });
            check(!Protected(laterRealm) && ((CampaignTime)AccessTools.Field(typeof(FactionManagerBehavior), "_initialCivilWarGraceUntil").GetValue(behavior)).ToDays == initialEnd,
                "Repeated startup callbacks neither renew grace nor grant it to later-created realms");
            store = new Store(); Call(behavior, "SyncCivilWarGrace", store); store.IsLoading = true;
            loaded = new FactionManagerBehavior(); Call(loaded, "SyncCivilWarGrace", store);
            Call(loaded, "ApplyInitialCivilWarGrace", new object[] { null });
            check((bool)Call(loaded, "HasCivilWarGrace", modded) && !(bool)Call(loaded, "HasCivilWarGrace", laterRealm),
                "Startup realm membership and frozen deadlines survive save/load without a new grant");
            Years(2); war = Faction(_parent, fought: true); Queue(war, shell); Gone.Add(shell); Maintain();
            double postwarEnd = records.Single().UntilDay;
            check(Call(behavior, "CivilWarGraceBlock", _parent).ToString().Contains($"for {(int)(2 * year)} days."),
                "Overlapping protection reports the later postwar deadline, never their sum");
            _day = initialEnd;
            check(!Protected(modded) && Protected(_parent), "Startup expires exactly while a longer postwar period remains active");
            _day = postwarEnd; check(!Protected(_parent), "Both protections expire without another campaign tick");
            var old = new FactionManagerBehavior(); Call(old, "SyncCivilWarGrace", new Store { IsLoading = true });
            Call(old, "ApplyInitialCivilWarGrace", new object[] { null });
            check(!(bool)Call(old, "HasCivilWarGrace", _parent), "Old saves with no startup keys never receive retroactive pacification");

            Setup(0); Call(behavior, "ArmInitialCivilWarGrace", new object[] { null });
            store = new Store(); Call(behavior, "SyncCivilWarGrace", store); store.IsLoading = true;
            loaded = new FactionManagerBehavior(); Call(loaded, "SyncCivilWarGrace", store); _day += 2;
            Call(loaded, "ApplyInitialCivilWarGrace", new object[] { null });
            check((bool)Call(loaded, "HasCivilWarGrace", _parent)
                && ((CampaignTime)AccessTools.Field(typeof(FactionManagerBehavior), "_initialCivilWarGraceUntil").GetValue(loaded)).ToDays == 100 + year,
                "A save made between new-game arming and publication recovers without shifting its original deadline");
            Setup(0); calendar[0].SetValue(null, 3); calendar[1].SetValue(null, 2);
            Call(behavior, "ArmInitialCivilWarGrace", new object[] { null }); Call(behavior, "ApplyInitialCivilWarGrace", new object[] { null });
            check(Call(behavior, "CivilWarGraceBlock", _parent).ToString().Contains("for 24 days."),
                "Accelerated campaigns receive their actual 24-day year, not a hardcoded 84-day delay");
            calendar[0].SetValue(null, 7); calendar[1].SetValue(null, 3);

            Setup(); war = Faction(_parent, fought: true); Queue(war, shell); Gone.Add(shell); Maintain();
            court = new CourtAgendaBehavior(); manager.Items.Add(court); bloc = Faction(_parent, FactionType.Nobility); bloc.Mood = -100;
            check(!(bool)Call(court, "ShouldStartCourtCrisis", bloc, _parent), "Player-led low mood cannot turn an ordinary agenda into a new crisis during grace");
            committed = Faction(_parent); Field(committed, "_ultimatumPending", true);
            check(Call(behavior, "CivilWarGraceJoinBlock", committed) != null && !Blocked(committed),
                "An issued ultimatum can finish but cannot recruit a new voluntary player pledge during grace");
            committed.Members.Clear(); committed.AddMember(_clan);
            check(!committed.Members.Contains(_clan), "The membership backend also rejects a new player pledge to an unstarted pending ultimatum");
            _warActive = true;
            check(Call(behavior, "CivilWarGraceJoinBlock", committed) == null, "Joining a war already under way is not confused with starting a new rebellion");
            _warActive = false;
            check(Call(behavior, "CivilWarGraceJoinBlock", bloc) == null && Call(behavior, "CivilWarGraceJoinBlock", resistance) == null,
                "Court affiliation and resistance to judgment retain their join exceptions");

            Patch(AccessTools.PropertyGetter(typeof(Clan), "Influence"), nameof(Influence));
            Patch(AccessTools.PropertyGetter(typeof(Kingdom), "Name"), nameof(Name));
            Patch(AccessTools.Method(typeof(CourtAgendaBehavior), "CanChooseTermBusiness"), nameof(Yes));
            Patch(AccessTools.Method(typeof(CourtAgendaBehavior), "Eligible"), nameof(Yes));
            Patch(AccessTools.Method(typeof(CourtAgendaBehavior), "MemberCount"), nameof(Two));
            Patch(AccessTools.Method(typeof(CourtAgendaBehavior), "ReviewInheritedFactionAgenda"), nameof(Skip));
            Patch(AccessTools.Method(typeof(CourtAgendaBehavior), "TryWithdrawChangedPolicyAgenda"), nameof(No));
            Patch(AccessTools.Method(typeof(CourtAgendaBehavior), "Policy"), nameof(NoPolicy));
            Patch(AccessTools.Method(typeof(CourtAgendaBehavior), "AgendaOwnerName"), nameof(Name));
            Patch(AccessTools.Method(typeof(CourtAgendaBehavior), "NotifyAgenda"), nameof(Skip));
            Patch(AccessTools.Method(typeof(CourtAgendaBehavior), "ApplyMemberMemory"), nameof(Memory));
            Patch(AccessTools.Method(typeof(MBInformationManager), "ShowMultiSelectionInquiry"), nameof(Capture));
            Patch(AccessTools.Method(typeof(InformationManager), "ShowInquiry"), nameof(Confirm));
            Patch(AccessTools.Method(typeof(FactionObject).Assembly.GetType("BellumCivile.BellumCivileNotifications"), "ShowPersonal", new[] { typeof(TextObject), typeof(Color) }), nameof(Notice));
            var agenda = new CourtAgendaRecord { Realm = _parent, Faction = bloc, Sponsor = _clan,
                State = CourtAgendaState.AwaitingPlayerDecision, SessionDate = CampaignTime.Days((float)_day + 10),
                ObjectiveData = new CourtObjectiveRecord { Kind = "policy" } };
            Call(court, "Advance", agenda, ideology);
            check(!agenda.CrisisInterventionPending && agenda.State == CourtAgendaState.AwaitingPlayerDecision,
                "The real daily agenda advancement cannot recreate a player crisis through its low-mood branch");
            agenda.CrisisInterventionPending = true; _memories = _notices = 0;
            Call(court, "ShowTermBusinessInquiry", agenda);
            var approve = _inquiry.InquiryElements.Single(e => (int)e.Identifier == 0);
            var decline = _inquiry.InquiryElements.Single(e => (int)e.Identifier == 1);
            string hint = Call(behavior, "CivilWarGraceBlock", _parent).ToString();
            check(!approve.IsEnabled && approve.Hint == hint, "A pending crisis popup disables Approve with the shared countdown tooltip");
            check(decline.IsEnabled && decline.Title == "Set the crisis aside" && decline.Hint == hint
                && (int)Call(court, "ReplacementCost", agenda, true) == 0, "A prohibited crisis can be set aside or substituted without influence cost");
            _inquiry.AffirmativeAction(new List<InquiryElement> { approve });
            check(agenda.State == CourtAgendaState.AwaitingPlayerDecision && agenda.CrisisInterventionPending && _notices == 1,
                "Forcing a disabled Approve callback cannot turn a prohibited crisis into a committed coalition");
            Call(court, "ShowTermBusinessInquiry", agenda); _inquiry.AffirmativeAction(new List<InquiryElement> { decline });
            check(!agenda.CrisisInterventionPending && agenda.State == CourtAgendaState.NotProposed && _memories == 0 && !agenda.OrdinaryCrisisRestrained,
                "Setting a prohibited crisis aside applies neither a relation penalty nor a permanent restraint");
            var motionType = typeof(CourtAgendaBehavior).GetNestedType("PlayerMotion", BindingFlags.NonPublic);
            object Motion(string kind)
            {
                var motion = Activator.CreateInstance(motionType, true);
                Field(motion, "Kind", kind); Field(motion, "Label", new TextObject("Debate the ruler's tyranny"));
                var candidateType = AccessTools.Field(motionType, "Candidate").FieldType;
                Field(motion, "Candidate", Activator.CreateInstance(candidateType, BindingFlags.Instance | BindingFlags.NonPublic,
                    null, new object[] { _parent.StringId, "debate", null }, null));
                return motion;
            }
            var debate = Motion("court_tyranny_debate");
            var choice = (InquiryElement)Call(court, "PlayerMotionChoice", agenda, debate, 0);
            check(!choice.IsEnabled && choice.Hint == hint, "The substitute tyranny motion remains visible but disabled with the grace tooltip");
            var group = AccessTools.Method(typeof(CivilWarGraceTests), "Group").MakeGenericMethod(motionType).Invoke(null, new object[] { new[] { debate } });
            var categoryChoice = (InquiryElement)Call(court, "PlayerMotionCategoryChoice", agenda, group);
            check(!categoryChoice.IsEnabled && categoryChoice.Hint == hint, "The tyranny-only motion category also explains pacification");
            check(((InquiryElement)Call(court, "PlayerMotionChoice", agenda, Motion("policy"), 0)).IsEnabled,
                "Ordinary substitute motions remain available during grace");
            Patch(AccessTools.Method(typeof(IdeologyBehavior), "CanConsiderCourtChallenge"), nameof(Yes));
            Patch(AccessTools.GetDeclaredConstructors(typeof(FactionObject).Assembly.GetType("BellumCivile.CourtTermContext")).Single(), nameof(Skip));
            manager.Items.Add(ideology);
            var available = (System.Collections.IList)Call(court, "AvailablePlayerMotions", agenda, "court_tyranny_debate");
            check(available.Count == 1 && !((InquiryElement)Call(court, "PlayerMotionChoice", agenda, available[0], 0)).IsEnabled,
                "An otherwise eligible tyranny debate is actually listed rather than hidden while pacified");
            check(((System.Collections.IList)Call(court, "AvailablePlayerMotions", agenda, "unavailable_kind")).Count == 0,
                "Tyranny debates cannot leak into policy or council nomination availability");
            Call(court, "ShowPlayerMotionConfirmation", agenda, debate, false, 0, null);
            check(_notices == 2, "A forced disabled motion cannot even open its approval preview");
            records.Clear(); agenda.State = CourtAgendaState.AwaitingPlayerDecision; agenda.PlayerSelectionConfirmed = false;
            int previewPrice = (int)Call(court, "ReplacementCost", agenda, false);
            Call(court, "ShowPlayerMotionConfirmation", agenda, debate, false, previewPrice, null);
            check(_confirmation != null, "An eligible debate can open its normal preview outside grace");
            Queue(Faction(_parent, fought: true), shell); _confirmation.AffirmativeAction();
            check(agenda.State == CourtAgendaState.AwaitingPlayerDecision && !agenda.PlayerSelectionConfirmed
                && agenda.SubstitutionInfluencePaid == 0 && _notices == 3,
                "Grace starting after a preview blocks stale confirmation before agenda mutation or influence payment");
            agenda.State = CourtAgendaState.Crisis; agenda.SessionDate = CampaignTime.Days((float)_day - 1);
            Call(court, "Advance", agenda, ideology);
            check(agenda.State == CourtAgendaState.Crisis, "A previously scheduled unissued crisis pauses without being mislabeled TooWeak");
            foreach (bool compact in new[] { false, true })
                check(Call(court, "DescribeAgendaEntry", _parent, bloc, compact, agenda).ToString().Contains(hint),
                    "Paused crisis status explains pacification in the " + (compact ? "compact" : "full") + " agenda display");
            bloc.Mood = -20; Call(court, "Advance", agenda, ideology);
            check(agenda.State == CourtAgendaState.Withdrawn, "Mood recovery can still withdraw a paused crisis before grace expires");
            bloc.Mood = -100; records.Clear();
            check((bool)Call(court, "ShouldStartCourtCrisis", bloc, _parent), "Player-led mood escalation becomes available again after protection ends");
            // Each field remains explicitly saveable, and existing faction IDs are only appended.
            var ids = typeof(FactionObject).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Select(f => f.GetCustomAttributesData().FirstOrDefault(a => a.AttributeType.Name == "SaveableFieldAttribute"))
                .Where(a => a != null).Select(a => Convert.ToInt32(a.ConstructorArguments[0].Value)).ToArray();
            check(ids.Length == 38 && ids.Distinct().Count() == 38 && ids.Max() == 38,
                "Judgment and committed-crisis markers append unique save fields without renumbering existing data");
        }
        finally
        {
            harmony.UnpatchAll(harmony.Id); settingsCache.SetValue(null, oldSettings); ticks.SetValue(null, oldTicks);
            for (int i = 0; i < calendar.Length; i++) calendar[i].SetValue(null, oldCalendar[i]);
            _campaign = null;
        }
    }
}
