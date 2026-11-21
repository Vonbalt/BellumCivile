using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using BellumCivile;
using BellumCivile.Behaviors;
using BellumCivile.UI;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Election;
using TaleWorlds.Core;
using TaleWorlds.Core.ViewModelCollection.Information;
using TaleWorlds.Library;
using TaleWorlds.Localization;

internal static class CrownRebellionSafetyTests
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
        public bool SyncData<T>(string key, ref T data)
        {
            if (IsLoading) { if (!Values.TryGetValue(key, out var value)) return false; data = (T)value; }
            else Values[key] = data;
            return true;
        }
    }
    private static readonly List<Kingdom> Kingdoms = new List<Kingdom>();
    private static readonly Dictionary<Kingdom, Clan> Rulers = new Dictionary<Kingdom, Clan>();
    private static readonly Dictionary<Clan, Kingdom> Realms = new Dictionary<Clan, Kingdom>();
    private static readonly Dictionary<Clan, Hero> Heads = new Dictionary<Clan, Hero>();
    private static readonly HashSet<Kingdom> Gone = new HashSet<Kingdom>();
    private static readonly List<Clan> Moves = new List<Clan>();
    private static readonly HashSet<(Hero, Hero)> Friendships = new HashSet<(Hero, Hero)>();
    private static readonly HashSet<(Clan, Clan)> Marriages = new HashSet<(Clan, Clan)>();
    private static IReadOnlyList<PrivyCouncilOfficeRecord> _offices;
    private static Clan _player, _eventOldHouse;
    private static Hero _dynasticCandidate;
    private static bool _pending, _transfer, _war;
    private static int _ultimata, _scans, _traces;
    private static Action _opportunity, _onMove;
    private static Action<Kingdom> _onMandate;
    private static T Blank<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    private static void Set(object owner, string field, object value) => AccessTools.Field(owner.GetType(), field).SetValue(owner, value);
    private static object Call(object owner, string method, params object[] args) => AccessTools.Method(owner.GetType(), method).Invoke(owner, args);
    private static bool Skip() => false;
    private static bool No(ref bool __result) { __result = false; return false; }
    private static bool Now(ref CampaignTime __result) { __result = CampaignTime.Zero; return false; }
    private static bool Yes(ref bool __result) { __result = true; return false; }
    private static bool Pending(ref bool __result) { __result = _pending; return false; }
    private static bool Transfer(ref bool __result) { __result = _transfer; return false; }
    private static bool War(ref bool __result) { __result = _war; return false; }
    private static bool Ruler(Kingdom __instance, ref Clan __result) { Rulers.TryGetValue(__instance, out __result); return false; }
    private static bool SetRuler(Kingdom __instance, Clan value) { Rulers[__instance] = value; return false; }
    private static bool Realm(Clan __instance, ref Kingdom __result) { Realms.TryGetValue(__instance, out __result); return false; }
    private static bool Head(Clan __instance, ref Hero __result) { Heads.TryGetValue(__instance, out __result); return false; }
    private static bool HeroClan(Hero __instance, ref Clan __result) { __result = Heads.FirstOrDefault(p => p.Value == __instance).Key; return false; }
    private static bool Player(ref Clan __result) { __result = _player; return false; }
    private static bool Eliminated(Kingdom __instance, ref bool __result) { __result = Gone.Contains(__instance); return false; }
    private static bool All(ref MBReadOnlyList<Kingdom> __result) { __result = new MBReadOnlyList<Kingdom>(Kingdoms); return false; }
    private static bool Clans(Kingdom __instance, ref MBReadOnlyList<Clan> __result)
    { __result = new MBReadOnlyList<Clan>(Realms.Where(p => p.Value == __instance).Select(p => p.Key).ToList()); return false; }
    private static bool Decisions(ref MBReadOnlyList<KingdomDecision> __result)
    { __result = new MBReadOnlyList<KingdomDecision>(new List<KingdomDecision>()); return false; }
    private static bool Power(ref float __result) { __result = 100; return false; }
    private static bool StrongIntent(ref float __result) { __result = 100; return false; }
    private static bool Relation(Hero __instance, Hero __0, ref int __result)
    { __result = Friendships.Contains((__instance, __0)) ? 80 : 0; return false; }
    private static bool Marriage(Clan clan, Clan alliedClan, ref bool __result)
    { __result = Marriages.Contains((clan, alliedClan)); return false; }
    private static bool Trait(ref int __result) { __result = 0; return false; }
    private static bool Opportunity(Action onUltimatum) { _opportunity = onUltimatum; return false; }
    private static bool Ultimatum(ref bool __result) { _ultimata++; __result = true; return false; }
    private static bool Move(Clan clan, Kingdom targetKingdom)
    { Moves.Add(clan); Realms[clan] = targetKingdom; _onMove?.Invoke(); return false; }
    private static void Event(Kingdom kingdom, Clan oldRulingClan) { _eventOldHouse = oldRulingClan; }
    private static bool Offices(ref IReadOnlyList<PrivyCouncilOfficeRecord> __result) { __result = _offices; return false; }
    private static bool Tracked(out Hero hero, ref bool __result) { hero = _dynasticCandidate; __result = true; return false; }
    private static bool Candidate(ref Hero __result) { __result = _dynasticCandidate; return false; }
    private static bool Trace() { _traces++; return false; }
    private static bool Mandate(Kingdom realm) { _onMandate?.Invoke(realm); return false; }
    private static void Scan() { _scans++; }

    internal static void Run(Action<bool, string> check)
    {
        var previous = Campaign.Current;
        var h = new Harmony("bellum.tests.crown_rebellion_safety");
        void Patch(MethodBase method, string name) => h.Patch(method, prefix: new HarmonyMethod(typeof(CrownRebellionSafetyTests), name));
        var mod = typeof(FactionObject).Assembly;
        var campaign = Blank<Campaign>();
        var behaviors = new Manager();
        var manager = new FactionManagerBehavior();
        var court = new CourtAgendaBehavior();
        var ideology = new IdeologyBehavior();
        var accession = new CrownAccessionBehavior();
        var elective = new ElectiveSuccessionBehavior();
        behaviors.Items.AddRange(new CampaignBehaviorBase[] { manager, court, ideology, accession, elective });
        campaign.AddCampaignBehaviorManager(behaviors);
        List<FactionObject> Factions() => (List<FactionObject>)AccessTools.Field(typeof(FactionManagerBehavior), "_activeFactions").GetValue(manager);
        Dictionary<string, string> Queue() => (Dictionary<string, string>)AccessTools.Field(typeof(FactionManagerBehavior), "_pendingSuccessionOpportunities").GetValue(manager);
        Clan House(string id, Kingdom realm)
        {
            var c = Blank<Clan>(); c.StringId = id; Realms[c] = realm;
            var head = Blank<Hero>(); head.StringId = id + "_head"; Heads[c] = head; return c;
        }
        var realm = Blank<Kingdom>(); realm.StringId = "empire"; Kingdoms.Add(realm);
        var oldHouse = House("old", realm); var newHouse = House("new", realm); var founder = House("founder", realm);
        _player = House("player", realm); Rulers[realm] = oldHouse;
        FactionObject Faction(Clan leader, FactionType type = FactionType.InstallRuler)
        {
            var f = Blank<FactionObject>(); Set(f, "_name", leader.StringId); Set(f, "_leader", leader);
            Set(f, "_parentKingdom", realm); Set(f, "_members", new List<Clan> { leader }); Set(f, "_type", type);
            Factions().Add(f); return f;
        }
        bool CanStart(FactionObject f) => (bool)Call(f, "CanTriggerUltimatum", new object[] { null });
        bool Complete(FactionObject f) => (bool)Call(f, "TryCompletePartialRebellionCreation", new object[] { null });
        try
        {
            AccessTools.PropertySetter(typeof(Campaign), "Current").Invoke(null, new object[] { campaign });
            var events = new CampaignEvents();
            AccessTools.PropertySetter(typeof(Campaign), "CampaignEvents").Invoke(campaign, new object[] { events });
            var dispatcher = Activator.CreateInstance(typeof(CampaignEventDispatcher), BindingFlags.NonPublic | BindingFlags.Instance,
                null, new object[] { new CampaignEventReceiver[] { events } }, null);
            AccessTools.PropertySetter(typeof(Campaign), "CampaignEventDispatcher").Invoke(campaign, new[] { dispatcher });
            Patch(AccessTools.PropertyGetter(typeof(Kingdom), "All"), nameof(All));
            Patch(AccessTools.PropertyGetter(typeof(CampaignTime), "Now"), nameof(Now));
            Patch(AccessTools.PropertyGetter(typeof(Kingdom), "RulingClan"), nameof(Ruler));
            Patch(AccessTools.PropertySetter(typeof(Kingdom), "RulingClan"), nameof(SetRuler));
            Patch(AccessTools.PropertyGetter(typeof(Kingdom), "IsEliminated"), nameof(Eliminated));
            Patch(AccessTools.PropertyGetter(typeof(Kingdom), "Clans"), nameof(Clans));
            Patch(AccessTools.PropertyGetter(typeof(Kingdom), "UnresolvedDecisions"), nameof(Decisions));
            Patch(AccessTools.Method(typeof(Kingdom), "IsAtWarWith"), nameof(War));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "Kingdom"), nameof(Realm));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "Leader"), nameof(Head));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "PlayerClan"), nameof(Player));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "IsEliminated"), nameof(No));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "IsUnderMercenaryService"), nameof(No));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "IsMinorFaction"), nameof(No));
            Patch(AccessTools.PropertyGetter(typeof(Hero), "Clan"), nameof(HeroClan));
            Patch(AccessTools.PropertyGetter(typeof(Hero), "IsDead"), nameof(No));
            Patch(AccessTools.Method(typeof(Hero), "GetTraitLevel"), nameof(Trait));
            Patch(AccessTools.PropertyGetter(typeof(DefaultTraits), "Calculating"), nameof(Skip));
            Patch(AccessTools.PropertyGetter(typeof(DefaultTraits), "Valor"), nameof(Skip));
            Patch(AccessTools.Method(typeof(BellumCivileLogger), "Log"), nameof(Skip));
            Patch(AccessTools.Method(typeof(FactionManagerBehavior), "ReportRulingClanChangedEvent"), nameof(Skip));
            Patch(AccessTools.Method(typeof(FactionManagerBehavior), "IsCivilWarGraceBlocked"), nameof(No));
            Patch(AccessTools.Method(typeof(CourtAgendaBehavior), "ReconcileCrown"), nameof(Skip));
            Patch(AccessTools.Method(typeof(CivilWarConflictBehavior), "IsRealmTransferPending"), nameof(Transfer));
            Patch(AccessTools.Method(typeof(CrownAccessionBehavior), "IsPending"), nameof(Pending));
            Patch(AccessTools.Method(typeof(ElectiveSuccessionBehavior), "PendingDeposition"), nameof(Skip));
            Patch(AccessTools.Method(mod.GetType("BellumCivile.BellumKingdomVisibilityHelper"), "IsTemporaryBellumKingdom"), nameof(No));
            foreach (string name in new[] { "ShowFactionDisbanded", "ShowClanLeftFaction" })
                Patch(AccessTools.Method(typeof(NotificationHelper), name), nameof(Skip));
            Patch(AccessTools.Method(typeof(NotificationHelper), "ShowPlayerSuccessionOpportunity"), nameof(Opportunity));
            Patch(AccessTools.Method(typeof(FactionObject), "CalculateFactionPower"), nameof(Power));
            Patch(AccessTools.Method(typeof(FactionObject), "CalculateLoyalistPower"), nameof(Power));
            Patch(AccessTools.Method(typeof(FactionObject), "ShouldDelayUltimatumForUnsafeLeaderLocation"), nameof(No));
            manager.RegisterEvents();
            CampaignEvents.RulingClanChanged.AddNonSerializedListener(new object(), Event);

            var warming = Faction(founder); warming.Members.AddRange(new[] { oldHouse, newHouse });
            var crownedLeader = Faction(newHouse);
            var active = Faction(newHouse); Set(active, "_rebellionCreationCompleted", true);
            var frozen = Faction(newHouse); Set(frozen, "_successionChallengeId", "saved");
            var partial = Faction(newHouse); Set(partial, "_rebellionCreationStarted", true);
            var courtParty = Faction(newHouse, FactionType.Nobility);
            ChangeRulingClanAction.Apply(realm, newHouse);
            check(_eventOldHouse == oldHouse, "Real native event passes the displaced ruling clan");
            check(!warming.Members.Contains(newHouse) && warming.Members.Contains(oldHouse), "Incoming ruler leaves warm coalition; former ruler is retained");
            check(!Factions().Contains(crownedLeader), "Incoming ruler's warm claimant faction is disbanded");
            check(Factions().Contains(active) && Factions().Contains(frozen) && Factions().Contains(partial), "Existing wars and saved startup receipts survive accession reconciliation");
            check(Factions().Contains(courtParty), "Court faction ownership is left to its existing membership maintenance");
            check(Queue().TryGetValue(realm.StringId, out var queued) && queued == newHouse.StringId && _opportunity == null,
                "Ruler event queues opportunities without launching a synchronous rebellion");
            check(!CanStart(crownedLeader), "Current ruling clan cannot issue an ultimatum against itself");
            warming.AddMember(newHouse);
            check(!warming.Members.Contains(newHouse), "Ruling clan cannot be added to another armed faction");
            check(CanStart(warming), "Healthy landless crown can still face a legitimate ultimatum");
            _pending = true; check(!CanStart(warming), "New ultimatums wait for accession completion"); _pending = false;
            _transfer = true; check(!CanStart(warming), "New ultimatums wait for realm transfer completion"); _transfer = false;
            Rulers[realm] = null; check(!CanStart(warming), "Leaderless parent cannot be challenged");
            Rulers[realm] = newHouse; Gone.Add(realm); check(!CanStart(warming), "Eliminated parent cannot be challenged"); Gone.Clear();
            Realms[newHouse] = null; check(!CanStart(warming), "A ruler no longer belonging to the parent cannot be challenged"); Realms[newHouse] = realm;
            var royal = new PrivyCouncilOfficeRecord(realm.StringId, PrivyCouncilOffice.Marshal, 0); royal.Initialize(newHouse.StringId, 0);
            var displaced = new PrivyCouncilOfficeRecord(realm.StringId, PrivyCouncilOffice.Chancellor, 0); displaced.Initialize(oldHouse.StringId, 0);
            _offices = new[] { royal, displaced };
            Patch(AccessTools.Method(typeof(PrivyCouncilBehavior), "IsEligiblePermanentRealm"), nameof(Yes));
            Patch(AccessTools.Method(typeof(PrivyCouncilBehavior), "EnsureCouncilForKingdom"), nameof(Skip));
            Patch(AccessTools.Method(typeof(PrivyCouncilBehavior), "GetOfficeRecords"), nameof(Offices));
            Call(new PrivyCouncilBehavior(), "OnRulingClanChanged", realm, oldHouse);
            check(string.IsNullOrEmpty(royal.HolderClanId) && displaced.HolderClanId == oldHouse.StringId,
                "Council vacates the incoming ruler's office, not the displaced house's office");
            Patch(AccessTools.Method(typeof(DynasticHeirBehavior), "TryGetTrackedDynasticHeir"), nameof(Tracked));
            Patch(AccessTools.Method(typeof(DynasticHeirBehavior), "ResolveDynasticSuccessionCandidate"), nameof(Candidate));
            Patch(AccessTools.Method(typeof(DynasticHeirBehavior), "RecalculateHeirForKingdom"), nameof(Skip));
            Patch(AccessTools.Method(typeof(DynasticHeirBehavior), "TraceDynasticSuccession"), nameof(Trace));
            _dynasticCandidate = Heads[newHouse];
            Call(new DynasticHeirBehavior(), "OnRulingClanChanged", realm, oldHouse);
            check(_traces == 0, "Dynastic callback recognizes the installed heir in the actual ruling house");
            _dynasticCandidate = Heads[oldHouse];
            Call(new DynasticHeirBehavior(), "OnRulingClanChanged", realm, oldHouse);
            check(_traces == 1, "Displaced dynastic line is recognized against the actual incoming ruler");

            Factions().Clear(); warming = Faction(founder); _player = founder;
            Patch(AccessTools.Method(typeof(FactionObject), "TriggerUltimatum", Type.EmptyTypes), nameof(Ultimatum));
            _pending = true; Call(manager, "ProcessSuccessionOpportunities");
            check(Queue().Count == 1 && _opportunity == null, "Pending accession retains its deferred opportunity");
            var store = new Store(); manager.SyncData(store); store.IsLoading = true;
            manager = new FactionManagerBehavior(); manager.SyncData(store);
            behaviors.Items.RemoveAll(b => b is FactionManagerBehavior); behaviors.Items.Add(manager);
            check(Queue().Count == 1, "Deferred succession opportunity survives save/load");
            _pending = false; Call(manager, "ProcessSuccessionOpportunities");
            check(Queue().Count == 0 && _opportunity != null && _ultimata == 0, "Ready succession offers player one deferred opportunity");
            _opportunity(); check(_ultimata == 1, "Current player opportunity may issue demands");
            Rulers[realm] = oldHouse; _opportunity(); check(_ultimata == 1, "Stale player inquiry cannot rebel against a different ruler");
            Rulers[realm] = newHouse; Factions().Clear(); _opportunity(); check(_ultimata == 1, "Removed faction cannot act through an old player inquiry");
            _opportunity = null; Call(manager, "ProcessSuccessionOpportunities"); check(_opportunity == null, "Consumed succession opportunity is not offered repeatedly");
            Queue()[realm.StringId] = oldHouse.StringId; Call(manager, "ProcessSuccessionOpportunities");
            check(Queue().Count == 0, "Superseded ruler receipt is discarded instead of replayed");
            var rawCrowned = Faction(newHouse); warming = Faction(founder); warming.Members.Add(newHouse);
            Call(manager, "ReconcileWarmingCrownFactions", realm, false);
            check(!Factions().Contains(rawCrowned) && !warming.Members.Contains(newHouse), "Daily pre-escalation reconciliation also handles direct ruler assignments");

            // Recover a partially moved rebellion with the monarch incorrectly in its saved roster.
            _player = null; _war = false;
            var shell = Blank<Kingdom>(); shell.StringId = "empire_rebels_founder"; Kingdoms.Add(shell); Rulers[shell] = founder;
            Set(warming, "_rebellionCreationStarted", true); warming.SetRebelKingdom(shell); warming.Members.Add(newHouse);
            Patch(AccessTools.Method(mod.GetType("BellumCivile.KingdomCreationSafetyHelper"), "PrepareRuler"), nameof(Yes));
            foreach (string name in new[] { "CaptureCivilWarStartFiefCounts", "CaptureCivilWarStartInfluence" })
                Patch(AccessTools.Method(typeof(FactionObject), name, new[] { typeof(IEnumerable<Clan>) }), nameof(Skip));
            Patch(AccessTools.Method(typeof(FactionManagerBehavior), "ValidateTransitionRulerOnce"), nameof(Skip));
            Patch(AccessTools.Method(typeof(FactionObject), "MoveClanToKingdomPreservingCivilWarInfluence"), nameof(Move));
            Patch(AccessTools.Method(typeof(DeclareWarAction), "ApplyByDefault"), nameof(Skip));
            _war = true;
            check(Complete(warming) && Moves.SequenceEqual(new[] { founder }) && Realms[newHouse] == realm,
                "Partial startup completes without transferring the parent's ruling clan");
            Moves.Clear(); Realms[founder] = realm;
            var adopter = Faction(founder); adopter.Members.Add(newHouse); Factions().Remove(warming);
            check((bool)Call(adopter, "TryAdoptExistingRebelKingdom", shell, null)
                && Moves.SequenceEqual(new[] { founder }) && Realms[newHouse] == realm,
                "Legacy shell adoption also leaves the parent crown at home");
            Moves.Clear(); Realms[founder] = realm;
            var interrupted = Faction(founder); interrupted.SetRebelKingdom(shell); Set(interrupted, "_rebellionCreationStarted", true);
            var next = House("next", realm); interrupted.Members.Add(next);
            _onMove = () => Rulers[realm] = next;
            check(!Complete(interrupted) && Moves.SequenceEqual(new[] { founder }) && Realms[next] == realm,
                "Ruler changes during a transfer stop startup before another crowned clan can move");
            check(!(bool)AccessTools.PropertyGetter(typeof(FactionObject), "HasStartedCivilWar").Invoke(interrupted, null),
                "Interrupted startup retains its receipt without claiming completion");
            _onMove = null; Rulers[realm] = newHouse;

            Rulers[realm] = null;
            Call(court, "ScanDecreeCases", realm, ideology);
            check(!(bool)Call(court, "AssignPriorityDecree", new CourtAgendaRecord { Realm = realm }), "Missing ruler safely blocks decree scanning and assignment");
            var decree = new CourtAgendaRecord { Realm = realm, State = CourtAgendaState.Announced,
                ObjectiveData = new CourtObjectiveRecord { Kind = "treason_decree" } };
            Call(court, "AdvanceExecutive", decree, ideology);
            check(decree.State == CourtAgendaState.Announced, "Unavailable ruler pauses a recorded decree instead of invalid execution");
            Rulers[realm] = newHouse; _pending = true; Call(court, "ScanDecreeCases", realm, ideology);
            check(!(bool)Call(court, "AssignPriorityDecree", new CourtAgendaRecord { Realm = realm }), "Pending accession cannot assign a decree"); _pending = false;
            var capture = new object[] { realm, null, null };
            check((bool)Call(court, "TryGetCourtRuler", capture), "Court accepts a healthy landless ruling clan");
            var expected = Heads[newHouse];
            Rulers[realm] = oldHouse;
            check(!(bool)Call(court, "CourtRulerUnchanged", realm, newHouse, expected), "Court detects a different ruling house");
            Rulers[realm] = newHouse; Heads[newHouse] = Blank<Hero>();
            check(!(bool)Call(court, "CourtRulerUnchanged", realm, newHouse, expected), "Court detects succession within the same house"); Heads[newHouse] = expected;

            foreach (string name in new[] { "MigratePlayerCrownBusiness", "FreezeExistingSchedules", "MaintainPeaceObjectives", "MaintainLiberationObjectives",
                "MaintainCampaignObjectives", "MaintainSubjugationObjectives", "MaintainClaimObjectives", "MaintainDynasticObjectives", "MaintainTradeObjectives",
                "MaintainTitleGrants", "MaintainClientGrants", "MaintainRallies", "MaintainProtectionOffers", "ReconcileCrowns", "MaintainRoyalPeaceCases",
                "PruneChallengeWindows", "SettleClosedExecutiveObjectives" }) Patch(AccessTools.Method(typeof(CourtAgendaBehavior), name), nameof(Skip));
            Patch(AccessTools.Method(typeof(ElectiveSuccessionBehavior), "CheckMandate"), nameof(Mandate));
            h.Patch(AccessTools.Method(typeof(CourtAgendaBehavior), "ScanDecreeCases"), postfix: new HarmonyMethod(typeof(CrownRebellionSafetyTests), nameof(Scan)));
            Kingdoms.Remove(shell); _scans = 0;
            _onMandate = k => Rulers[k] = null;
            Call(court, "Tick");
            check(_scans == 0, "Court tick revalidates after mandate callback makes the realm leaderless");
            Rulers[realm] = newHouse; _onMandate = k => Rulers[k] = oldHouse;
            Call(court, "Tick"); check(_scans == 0, "Court tick does not process a newly changed ruler using its old snapshot");

            Rulers[realm] = newHouse;
            Realms[founder] = realm;
            Factions().Clear();
            Set(realm, "_rulingClan", newHouse);
            Set(newHouse, "_leader", Heads[newHouse]);
            var resistance = (FactionObject)Call(ideology, "CreateExpelledLordRebelFaction", founder, realm, manager);
            check(resistance != null && resistance.Type == FactionType.Abdication,
                "New treason resistance demands abdication rather than independence");
            check(resistance.AbdicationMonarch == Heads[newHouse],
                "Treason resistance targets the incumbent ruler");
            check(resistance != null && resistance.Leader == founder && Factions().Contains(resistance)
                && (bool)AccessTools.PropertyGetter(typeof(FactionObject), "IsJudgmentResistance").Invoke(resistance, null),
                "Treason resistance is registered under the accused clan and keeps judgment resistance rules");
            var separatists = Faction(oldHouse, FactionType.Independence);
            check(ReferenceEquals(Call(ideology, "CreateExpelledLordRebelFaction", oldHouse, realm, manager), separatists)
                && separatists.Type == FactionType.Independence && Factions().Count == 2,
                "An already-led rebellion keeps its demands instead of creating a second faction");

            TestPowerForecast(check, behaviors, Patch, House);
        }
        finally
        {
            h.UnpatchAll(h.Id);
            AccessTools.PropertySetter(typeof(Campaign), "Current").Invoke(null, new object[] { previous });
            Kingdoms.Clear(); Rulers.Clear(); Realms.Clear(); Heads.Clear(); Gone.Clear(); Moves.Clear();
            Friendships.Clear(); Marriages.Clear();
        }
    }

    private static void TestPowerForecast(Action<bool, string> check, Manager behaviors,
        Action<MethodBase, string> patch, Func<string, Kingdom, Clan> house)
    {
        var mod = typeof(FactionObject).Assembly;
        patch(AccessTools.PropertyGetter(typeof(Hero), "IsAlive"), nameof(Yes));
        patch(AccessTools.Method(typeof(Hero), "GetRelation", new[] { typeof(Hero) }), nameof(Relation));
        patch(AccessTools.Method(mod.GetType("BellumCivile.MarriageAllianceHelper"), "HasMarriageAlliance"), nameof(Marriage));
        patch(AccessTools.Method(typeof(IdeologyBehavior), "CalculateRebellionScore"), nameof(StrongIntent));

        var realm = Blank<Kingdom>();
        realm.StringId = "power_test";
        Clan MakeHouse(string id, string name, float power)
        {
            var clan = house(id, realm);
            Set(clan, "<Name>k__BackingField", new TextObject(name));
            Set(clan, "<CurrentTotalStrength>k__BackingField", power);
            return clan;
        }
        var crown = Rulers[realm] = MakeHouse("crown", "Crown", 500);
        var founder = MakeHouse("baron", "Founding House", 300);
        var vassal = MakeHouse("vassal", "Supporting House", 1000);
        var friend = MakeHouse("friend", "Friendly House", 800);
        MakeHouse("spectator", "Restless House", 100);
        var titles = new FeudalTitleBehavior();
        var service = new FeudalServiceBehavior();
        behaviors.Items.Add(titles);
        behaviors.Items.Add(service);
        var root = new FeudalTitleRecord("power_root", "Root", FeudalTitleType.County, founder.StringId,
            founder.StringId, null, null, realm.StringId, 0, 0);
        var child = new FeudalTitleRecord("power_child", "Child", FeudalTitleType.Barony, vassal.StringId,
            vassal.StringId, root.TitleId, null, realm.StringId, 0, 0);
        var titleRecords = new Dictionary<string, FeudalTitleRecord> { [root.TitleId] = root, [child.TitleId] = child };
        Set(titles, "_titlesById", titleRecords);
        void Index() => Call(titles, "RebuildRuntimeIndexes", true, false);
        Index();
        string key = child.TitleId + "|" + root.TitleId;
        var contract = new FeudalServiceRecord(key, child.TitleId, root.TitleId, FeudalServiceLevel.CustomaryTenure, 0, null, null);
        var contracts = new Dictionary<string, FeudalServiceRecord> { [key] = contract };
        Set(service, "_contractsByTitlePair", contracts);
        Friendships.Add((Heads[friend], Heads[vassal]));

        var calculate = AccessTools.Method(mod.GetType("BellumCivile.RebellionPowerHelper"), "CalculateProjectedConflictPower");
        object Forecast(Clan[] members, bool support = true, ISet<Clan> loyal = null) =>
            calculate.Invoke(null, new object[] { realm, members, founder, null, support, null, loyal });
        object Get(object owner, string property) => AccessTools.PropertyGetter(owner.GetType(), property).Invoke(owner, null);
        float Number(object owner, string property) => (float)Get(owner, property);
        object[] Supporters(object projection) => ((IEnumerable)Get(projection, "ProjectedSupporters")).Cast<object>().ToArray();
        bool Near(float value, float expected) => Math.Abs(value - expected) < .01f;
        var baseline = Forecast(new[] { founder, founder, crown });
        var supporter = Supporters(baseline).Single();
        check(Get(supporter, "Clan") == (object)vassal && Get(supporter, "Sponsor") == (object)founder,
            "Forecast is one hop: neither a friend of projected support nor an untied rebellious house is counted");
        check(Near(Number(baseline, "PledgedPower"), 300) && Near(Number(baseline, "ExpectedSupportPower"), 250)
            && Near(Number(baseline, "LoyalistPower"), 2150),
            "The ruler stays loyal, pledges deduplicate, and the Crown loses only the 250 service credit");
        var uncappedConcession = ((float Faction, float Loyalist))Call(baseline, "GetConcessionPower");
        check(Near(uncappedConcession.Faction, 550) && Near(uncappedConcession.Loyalist, 2150),
            "Concession uses the ordinary balance when expected support is below pledged power");
        check(Supporters(Forecast(new[] { founder }, loyal: new HashSet<Clan> { vassal })).Length == 0,
            "An explicitly loyal house is excluded from projected support");
        Marriages.Add((vassal, crown));
        var dividedVassal = Supporters(Forecast(new[] { founder })).Single();
        check(Near(Number(dividedVassal, "JoinChance"), .5f) && Near(Number(dividedVassal, "CreditedPower"), 125),
            "A divided vassal's willingness discounts service power instead of replacing the service cap");
        Marriages.Remove((vassal, crown));

        Friendships.Add((Heads[vassal], Heads[founder]));
        foreach (var test in new[] { (FeudalServiceLevel.Exemption, 0f), (FeudalServiceLevel.Lessened, 100f),
            (FeudalServiceLevel.CustomaryTenure, 250f), (FeudalServiceLevel.Elevated, 400f), (FeudalServiceLevel.Extortion, 500f) })
        {
            contract.Update(test.Item1, 0, null, null);
            check(Near(Number(Forecast(new[] { founder }), "ExpectedSupportPower"), test.Item2),
                "Direct service uses " + test.Item1 + " without stacking friendship on top");
        }
        contract.Update(FeudalServiceLevel.CustomaryTenure, 0, null, null);
        Friendships.Remove((Heads[vassal], Heads[founder]));
        child.SetDeFactoParentTitle(null);
        Index();
        check(Supporters(Forecast(new[] { founder })).Length == 0, "A de jure relationship alone does not create a service contract");
        child.SetDeFactoParentTitle(root.TitleId);
        Index();
        var secondChild = new FeudalTitleRecord("child_two", "Child Two", FeudalTitleType.Barony, vassal.StringId,
            vassal.StringId, root.TitleId, null, realm.StringId, 0, 0);
        titleRecords.Add(secondChild.TitleId, secondChild);
        var secondKey = secondChild.TitleId + "|" + root.TitleId;
        contracts[secondKey] = new FeudalServiceRecord(secondKey, secondChild.TitleId, root.TitleId, FeudalServiceLevel.Elevated, 0, null, null);
        Index();
        check(Near(Number(Forecast(new[] { founder }), "ExpectedSupportPower"), 325),
            "Two titles held under the same liege average their service caps instead of stacking");
        titleRecords.Remove(secondChild.TitleId);
        Index();

        Marriages.Add((friend, vassal));
        Friendships.Remove((Heads[friend], Heads[vassal]));
        var marriageSupport = Supporters(Forecast(new[] { founder, vassal })).Single();
        check(Near(Number(marriageSupport, "CreditedPower"), 400) && Get(marriageSupport, "Reason").ToString() == "MarriageAlliance",
            "A marriage alliance with a pledged clan supplies at most half power without needing friendship");
        Friendships.Add((Heads[friend], Heads[founder]));
        var direct = Forecast(new[] { founder, vassal });
        var friendSupport = Supporters(direct).Single();
        check(Get(friendSupport, "Clan") == (object)friend && Near(Number(friendSupport, "CreditedPower"), 400)
            && Near(Number(direct, "PledgedPower"), 1300),
            "A committed supporter provides full power and unlocks its own ties; competing routes do not stack");
        check(Near(Number(Forecast(new[] { founder, vassal }, support: false), "FactionPower"), 1300),
            "Active war can use actual pledged power without projected support");
        Marriages.Add((friend, crown));
        check(Supporters(Forecast(new[] { founder, vassal })).Length == 0,
            "High intent does not bypass a marriage tie to the Crown for the personal support forecast");
        Marriages.Remove((friend, crown));

        var connected = Forecast(new[] { founder });
        var concession = ((float Faction, float Loyalist))Call(connected, "GetConcessionPower");
        check(Near(Number(connected, "FactionPower"), 950) && Near(Number(connected, "LoyalistPower"), 1750)
            && Near(concession.Faction, 600) && Near(concession.Loyalist, 2100),
            "AI concession alone caps the 650 support at 300 pledged and restores the corresponding Crown power");

        var coalition = Blank<FactionObject>();
        Set(coalition, "_name", "Power Coalition");
        Set(coalition, "_parentKingdom", realm);
        Set(coalition, "_members", new List<Clan> { founder });
        Set(coalition, "_leader", founder);
        Set(coalition, "_type", FactionType.Abdication);
        Set(coalition, "_isGrandCoalition", true);
        Set(coalition, "_ultimatumPending", true);
        var manager = behaviors.GetBehavior<FactionManagerBehavior>();
        ((List<FactionObject>)AccessTools.Field(manager.GetType(), "_activeFactions").GetValue(manager)).Add(coalition);
        object CachedForecast() => Call(coalition, "CalculatePowerProjection", false);
        check(Near(Number(CachedForecast(), "FactionPower"), 950), "Grand Coalition uses the new support calculation");
        contract.Update(FeudalServiceLevel.Exemption, 0, null, null);
        service.SyncData(new Store());
        check(Near(Number(CachedForecast(), "FactionPower"), 700), "A service change invalidates the same-day faction forecast");
        contract.Update(FeudalServiceLevel.CustomaryTenure, 0, null, null);
        service.SyncData(new Store());
        var crownView = AccessTools.Method(mod.GetType("BellumCivile.CrownPowerProjection"), "Calculate").Invoke(null, new[] { realm });
        var totals = Get(crownView, "Total");
        check(Near((float)(double)Get(totals, "Rebels"), Number(connected, "FactionPower"))
            && Near((float)(double)Get(totals, "Loyalists"), Number(connected, "LoyalistPower")),
            "The Crown forecast consumes the same capped credits as the faction forecast");

        for (int i = 0; i < 7; i++)
        {
            var ally = MakeHouse("ally_" + i, "Additional House " + i, 50);
            Friendships.Add((Heads[ally], Heads[founder]));
        }
        var tooltip = AccessTools.Method(typeof(FactionsWindowVM), "BuildProjectedFactionPowerTooltip")
            .Invoke(null, new[] { Forecast(new[] { founder }) });
        var rows = ((Func<List<TooltipProperty>>)AccessTools.Field(tooltip.GetType(), "_tooltipProperties").GetValue(tooltip))();
        check(rows.Any(row => row.DefinitionLabel.Trim() == "Pledged power")
            && rows.Any(row => row.DefinitionLabel.Trim() == "Expected support")
            && rows.Any(row => row.ValueLabel.Contains("not pledged")), "The power tooltip separates pledged and expected support");
        check(rows.Any(row => row.DefinitionLabel.Trim() == "Supporting House" && row.ValueLabel.Contains("25% cap, 100% chance"))
            && rows.Any(row => row.ValueLabel.Contains("vassal of Founding House"))
            && rows.Any(row => row.DefinitionLabel.Trim() == "Other houses (4)"),
            "The tooltip shows the cap, willingness, real sponsor and contributor overflow");
    }
}
