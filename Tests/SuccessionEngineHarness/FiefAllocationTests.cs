using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using BellumCivile;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Election;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Library;

internal static class FiefAllocationTests
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
        internal readonly Dictionary<string, object> Data = new Dictionary<string, object>();
        public bool IsLoading { get; set; }
        public bool IsSaving => !IsLoading;
        public bool SyncData<T>(string key, ref T data)
        {
            if (IsSaving) { Data[key] = data is IDictionary ? Activator.CreateInstance(data.GetType(), data) : (object)data; return true; }
            if (!Data.TryGetValue(key, out var value)) return false;
            data = (T)value; return true;
        }
    }

    private static readonly Assembly Mod = typeof(FiefDeliberationBehavior).Assembly;
    private static Kingdom _realm, _foreign, _proposerRealm, _mapRealm;
    private static Clan _player, _crown, _candidate, _excluded;
    private static Hero _leader, _capturer;
    private static Settlement _fief;
    private static Town _town;
    private static PerkObject _perk;
    private static bool _allowed, _eliminated, _invalidSponsor;
    private static bool _unassigned { get => _town.IsOwnerUnassigned; set => _town.IsOwnerUnassigned = value; }
    private static float _influence, _day;
    private static MBList<KingdomDecision> _decisions;
    private static readonly List<string> Logs = new List<string>();
    private static T Blank<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    private static void Set(object obj, string field, object value) => AccessTools.Field(obj.GetType(), field).SetValue(obj, value);
    private static Dictionary<string, T> Data<T>(FiefDeliberationBehavior b, string field) =>
        (Dictionary<string, T>)AccessTools.Field(b.GetType(), field).GetValue(b);
    private static object Call(FiefDeliberationBehavior b, string method, params object[] args) =>
        AccessTools.Method(b.GetType(), method).Invoke(b, args);
    private static bool Player(ref Clan __result) { __result = _player; return false; }
    private static bool Crown(ref Clan __result) { __result = _crown; return false; }
    private static bool Realm(Clan __instance, ref Kingdom __result)
    { __result = __instance == _crown ? _proposerRealm : _realm; return false; }
    private static bool MapRealm(ref IFaction __result) { __result = _mapRealm; return false; }
    private static bool Head(ref Hero __result) { __result = _leader; return false; }
    private static bool All(ref MBReadOnlyList<Kingdom> __result)
    { __result = new MBReadOnlyList<Kingdom>(new List<Kingdom> { _realm }); return false; }
    private static bool Clans(ref MBReadOnlyList<Clan> __result)
    { __result = new MBReadOnlyList<Clan>(new List<Clan> { _crown, _candidate, _excluded }); return false; }
    private static bool Settlements(ref MBReadOnlyList<Settlement> __result)
    { __result = new MBReadOnlyList<Settlement>(new List<Settlement> { _fief }); return false; }
    private static bool FindSettlement(ref Settlement __result) { __result = _fief; return false; }
    private static bool FindHero(ref Hero __result) { __result = _capturer; return false; }
    private static bool Decisions(ref MBReadOnlyList<KingdomDecision> __result)
    { __result = new MBReadOnlyList<KingdomDecision>(_decisions); return false; }
    private static bool Add(KingdomDecision decision) { _decisions.Add(decision); return false; }
    private static bool No(ref bool __result) { __result = false; return false; }
    private static bool Allowed(ref bool __result) { __result = _allowed; return false; }
    private static bool Eliminated(ref bool __result) { __result = _eliminated; return false; }
    private static bool ClanEliminated(Clan __instance, ref bool __result)
    { __result = _invalidSponsor && __instance == _candidate; return false; }
    private static bool Balance(ref float __result) { __result = _influence; return false; }
    private static bool Perk(ref PerkObject __result) { __result = _perk; return false; }
    private static bool Zero(ref int __result) { __result = 0; return false; }
    private static bool Merit(ref float __result) { __result = 100; return false; }
    private static bool Support(DecisionOutcome possibleOutcome, ref float __result)
    { __result = ((SettlementClaimantDecision.ClanAsDecisionOutcome)possibleOutcome).Clan == _crown ? 500 : 0; return false; }
    private static bool Candidates(ref IEnumerable<DecisionOutcome> __result)
    { __result = new DecisionOutcome[] { new SettlementClaimantDecision.ClanAsDecisionOutcome(_crown),
        new SettlementClaimantDecision.ClanAsDecisionOutcome(_candidate) }; return false; }
    private static bool Log(string message) { Logs.Add(message); return false; }

    internal static void Run(Action<bool, string> check)
    {
        var h = new Harmony("bellum.tests.fief_allocation");
        var old = Campaign.Current;
        var dayTicks = AccessTools.Field(typeof(CampaignTime), "TimeTicksPerDay");
        var hourTicks = AccessTools.Field(typeof(CampaignTime), "TimeTicksPerHour");
        var oldDayTicks = dayTicks.GetValue(null);
        var oldHourTicks = hourTicks.GetValue(null);
        void Patch(MethodBase m, string method)
        {
            if (m == null) throw new Exception("Missing target for fixture: " + method);
            h.Patch(m, prefix: new HarmonyMethod(typeof(FiefAllocationTests), method));
        }
        Clan NewClan(string id) { var c = Blank<Clan>(); c.StringId = id; return c; }
        _realm = Blank<Kingdom>(); _realm.StringId = "allocation";
        _foreign = Blank<Kingdom>(); _foreign.StringId = "foreign";
        _proposerRealm = _mapRealm = _realm;
        _crown = NewClan("crown"); _candidate = NewClan("candidate"); _excluded = NewClan("excluded");
        _player = _candidate;
        _leader = Blank<Hero>(); _capturer = Blank<Hero>(); _capturer.StringId = "capturer";
        _fief = Blank<Settlement>(); _fief.StringId = "town_S1"; _town = Blank<Town>(); _perk = Blank<PerkObject>();
        Set(_fief, "Town", _town);
        _allowed = _unassigned = true; _invalidSponsor = _eliminated = false;
        _influence = 136; _day = 110; _decisions = new MBList<KingdomDecision>(); Logs.Clear();
        Set(_realm, "_unresolvedDecisions", _decisions);
        var manager = new Manager();
        var behavior = new FiefDeliberationBehavior(); manager.Items.Add(behavior);
        var campaign = Blank<Campaign>();
        string key = "allocation|town_S1";
        SettlementClaimantDecision Decision()
        {
            var d = Blank<SettlementClaimantDecision>(); Set(d, "_kingdom", _realm);
            Set(d, "<ProposerClan>k__BackingField", _crown); Set(d, "Settlement", _fief);
            Set(d, "ClanToExclude", _excluded); Set(d, "_capturerHero", _capturer);
            Set(d, "<TriggerTime>k__BackingField", CampaignTime.Days(107)); return d;
        }
        void Seed(FiefDeliberationBehavior b)
        {
            Data<CampaignTime>(b, "_pendingFiefDate")[key] = CampaignTime.Days(105);
            Data<string>(b, "_pendingFiefProposer")[key] = _crown.StringId;
            Data<string>(b, "_pendingFiefCapturer")[key] = _capturer.StringId;
            Data<string>(b, "_pendingFiefParticipants")[key] = "crown|candidate";
            Data<string>(b, "_pendingFiefExclude")[key] = _excluded.StringId;
            Data<float>(b, "_pendingFiefCreatedDay")[key] = 100;
            Data<int>(b, "_pendingFiefRetryCount")[key] = 0;
            b.SetBribedCandidateVote(_realm, _fief, _candidate, _candidate);
        }
        try
        {
            AccessTools.PropertySetter(typeof(Campaign), "Current").Invoke(null, new object[] { campaign });
            campaign.AddCampaignBehaviorManager(manager);
            dayTicks.SetValue(null, 24000L);
            hourTicks.SetValue(null, 1000L);
            var tracker = Activator.CreateInstance(typeof(Campaign).Assembly.GetType("TaleWorlds.CampaignSystem.MapTimeTracker"),
                BindingFlags.Instance | BindingFlags.NonPublic, null, new object[] { CampaignTime.Days(_day) }, null);
            Set(campaign, "<MapTimeTracker>k__BackingField", tracker);
            Patch(AccessTools.PropertyGetter(typeof(Clan), "PlayerClan"), nameof(Player));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "Kingdom"), nameof(Realm));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "Leader"), nameof(Head));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "Influence"), nameof(Balance));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "All"), nameof(Clans));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "IsEliminated"), nameof(ClanEliminated));
            Patch(AccessTools.PropertyGetter(typeof(Kingdom), "RulingClan"), nameof(Crown));
            Patch(AccessTools.PropertyGetter(typeof(Kingdom), "All"), nameof(All));
            Patch(AccessTools.PropertyGetter(typeof(Kingdom), "IsEliminated"), nameof(Eliminated));
            Patch(AccessTools.PropertyGetter(typeof(Kingdom), "UnresolvedDecisions"), nameof(Decisions));
            Patch(AccessTools.PropertyGetter(typeof(Settlement), "MapFaction"), nameof(MapRealm));
            Patch(AccessTools.PropertyGetter(typeof(Settlement), "OwnerClan"), nameof(Crown));
            Patch(AccessTools.PropertyGetter(typeof(Settlement), "All"), nameof(Settlements));
            Patch(AccessTools.Method(typeof(Settlement), "Find"), nameof(FindSettlement));
            Patch(AccessTools.Method(typeof(Hero), "FindFirst"), nameof(FindHero));
            Patch(AccessTools.Method(typeof(Hero), "GetPerkValue"), nameof(No));
            Patch(AccessTools.Method(typeof(Hero), "GetRelation", new[] { typeof(Hero) }), nameof(Zero));
            Patch(AccessTools.PropertyGetter(typeof(DefaultPerks.Charm), "FlexibleEthics"), nameof(Perk));
            Patch(AccessTools.Method(typeof(FactionManager), "IsAtWarAgainstFaction"), nameof(No));
            Patch(AccessTools.Method(typeof(SettlementClaimantDecision), "IsAllowed"), nameof(Allowed));
            Patch(AccessTools.Method(typeof(SettlementClaimantDecision), "CalculateMeritOfOutcome"), nameof(Merit));
            Patch(AccessTools.Method(typeof(SettlementClaimantDecision), "DetermineInitialCandidates"), nameof(Candidates));
            Patch(AccessTools.Method(typeof(SettlementClaimantDecision), "DetermineSupport"), nameof(Support));
            Patch(AccessTools.Method(Mod.GetType("BellumCivile.BellumCivileLogger"), "Log", new[] { typeof(string) }), nameof(Log));
            Patch(AccessTools.Method(Mod.GetType("BellumCivile.ModIntegrationHelper"), "ShouldBypassFiefDeliberation"), nameof(No));
            Patch(AccessTools.Method(typeof(IdeologyBehavior), "AddDecisionAsModAction"), nameof(Add));
            h.CreateClassProcessor(Mod.GetType("BellumCivile.Patches.FiefAllocationRemovalDiagnosticsPatch")).Patch();

            var decision = Decision();
            check(!decision.ShouldBeCancelled(), "Vanilla funds proposer support at 136 influence");
            h.CreateClassProcessor(Mod.GetType("BellumCivile.Patches.NpcKingdomDecisionSupportBudgetPatch")).Patch();
            check(decision.ShouldBeCancelled(), "Reproduces cancellation when Bellum reserve makes proposer abstain");
            h.CreateClassProcessor(Mod.GetType("BellumCivile.Patches.PreserveUnassignedFiefVotePatch")).Patch();
            check(!decision.ShouldBeCancelled(), "Unassigned allocation survives proposer abstention");
            var outcomes = decision.DetermineInitialCandidates().ToMBList(); decision.DetermineSponsors(outcomes);
            check(decision.DetermineSupportOption(new Supporter(_crown), outcomes, out var weight, true) == null
                && weight == Supporter.SupportWeights.StayNeutral && _influence == 136,
                "Preservation neither funds a free vote nor changes reserves");
            decision.ShouldBeCancelled();
            check(Logs.Count(s => s.StartsWith("Preserved unassigned")) == 1, "Repeated hourly abstention logs only once per ballot and reason");
            _allowed = false; check(decision.ShouldBeCancelled(), "Permission failure still cancels"); _allowed = true;
            _mapRealm = _foreign; check(decision.ShouldBeCancelled(), "Loss of the fief still cancels"); _mapRealm = _realm;
            _proposerRealm = _foreign; check(decision.ShouldBeCancelled(), "Proposer leaving the realm still cancels"); _proposerRealm = _realm;
            _eliminated = true; check(decision.ShouldBeCancelled(), "Eliminated realm still cancels"); _eliminated = false;
            _invalidSponsor = true; check(decision.ShouldBeCancelled(), "Eliminated sponsor still cancels"); _invalidSponsor = false;
            _unassigned = false; check(decision.ShouldBeCancelled(), "Assigned-fief motions retain native cancellation rules"); _unassigned = true;
            _influence = 10; check(!decision.ShouldBeCancelled(), "Native very-low-influence exemption remains intact"); _influence = 136;
            _player = _crown;
            check(decision.TriggerTime.IsPast && decision.NeedsPlayerResolution && !decision.ShouldBeCancelled(),
                "After 48 hours a player ruler still must resolve the ballot, without a new deliberation");
            _player = _candidate;
            check(decision.TriggerTime.IsPast && !decision.NeedsPlayerResolution && decision.IsPlayerParticipant,
                "After 48 hours a player vassal can be bypassed by native automatic resolution");

            Seed(behavior); Call(behavior, "RememberOpenedAllocation", decision);
            _decisions.Add(decision); Call(behavior, "FinishFiefHandoff", key);
            check(!behavior.HasPendingFiefVote(_realm) && Data<CampaignTime>(behavior, "_openedFiefVoteDates").ContainsKey(key),
                "Handoff keeps recovery receipt without exposing a second pending deliberation");
            check(behavior.QueueBlockedClaimantDecision(Decision()) && !behavior.HasPendingFiefVote(_realm),
                "Live ballot suppresses duplicate native allocation requests");
            AccessTools.Method(typeof(Kingdom), "RemoveDecision").Invoke(_realm, new object[] { decision });
            check(_decisions.Count == 0 && Logs.Any(s => s.StartsWith("Removing unassigned fief ballot")
                && s.Contains("trigger_elapsed_days=") && s.Contains("ruler_in_siege=False")),
                "Removal diagnostics observe an overdue ballot without suppressing native removal");
            check(behavior.QueueBlockedClaimantDecision(Decision()) && behavior.HasPendingFiefVoteForSettlement(_realm, _fief),
                "Missing ballot resumes through the real native-interception queue");
            check(Data<CampaignTime>(behavior, "_pendingFiefDate")[key].ToDays == 105
                && Data<int>(behavior, "_pendingFiefRetryCount")[key] == 1,
                "Interrupted ballot retains its expired original due date and records a retry");
            check(Data<string>(behavior, "_pendingFiefCapturer")[key] == "capturer"
                && Data<string>(behavior, "_pendingFiefParticipants")[key] == "crown|candidate"
                && Data<string>(behavior, "_pendingFiefExclude")[key] == "excluded",
                "Recovery retains capturer, siege participants, and excluded owner");
            check(behavior.GetBribedCandidateVote(_realm, _fief, _candidate) == "candidate"
                && Data<bool>(behavior, "_fiefCommittedNominationKeys")[key + "|candidate"],
                "Recovery retains bought votes and committed nominations");
            behavior.QueueBlockedClaimantDecision(Decision());
            check(Data<int>(behavior, "_pendingFiefRetryCount")[key] == 1, "Duplicate requests cannot consume multiple retries");
            Call(behavior, "OnDailyTick");
            check(_decisions.Count == 1 && !behavior.HasPendingFiefVote(_realm), "Recovered ballot opens on the next eligible daily tick");
            var reopened = (SettlementClaimantDecision)_decisions.Single();
            check(ReferenceEquals(AccessTools.Field(reopened.GetType(), "_capturerHero").GetValue(reopened), _capturer)
                && reopened.ClanToExclude == _excluded, "Actual reopened decision receives original capture and exclusion context");
            check(!Logs.Any(s => s.StartsWith("Queued fief deliberation")), "Recovery does not repeat the five-day announcement path");

            var store = new Store(); behavior.SyncData(store); store.IsLoading = true;
            var restored = new FiefDeliberationBehavior(); restored.SyncData(store);
            manager.Items.Clear(); manager.Items.Add(restored); _decisions.Clear();
            check(restored.QueueBlockedClaimantDecision(Decision())
                && Data<CampaignTime>(restored, "_pendingFiefDate")[key].ToDays == 105
                && restored.GetBribedCandidateVote(_realm, _fief, _candidate) == "candidate",
                "Save roundtrip retains original allocation deadline and pledges after handoff");
            Call(restored, "FinishFiefHandoff", key);
            Data<int>(restored, "_pendingFiefRetryCount")[key] = 7;
            check(!restored.QueueBlockedClaimantDecision(Decision()) && !restored.HasPendingFiefVote(_realm),
                "Exhausted recovery lets native allocation proceed without a fresh Bellum delay");
            check(!restored.QueueBlockedClaimantDecision(Decision())
                && Logs.Count(s => s.StartsWith("Fief allocation recovery exhausted")) == 1,
                "Exhausted receipt survives repeated requests without log spam");
            _unassigned = false; Call(restored, "ReconcileOpenedAllocations");
            check(Data<CampaignTime>(restored, "_openedFiefVoteDates").Count == 0
                && restored.GetBribedCandidateVote(_realm, _fief, _candidate) == null,
                "Completed award clears receipt and promises even when the owner stays in the same clan");
            _unassigned = true;
            var legacy = new FiefDeliberationBehavior(); legacy.SyncData(new Store { IsLoading = true });
            Call(legacy, "RememberOpenedAllocation", decision);
            check(legacy.QueueBlockedClaimantDecision(Decision()) && Data<CampaignTime>(legacy, "_pendingFiefDate")[key].IsPast
                && Data<string>(legacy, "_pendingFiefCapturer")[key] == "capturer",
                "Older save with an already-live ballot recovers immediately and preserves its native capturer");
            Seed(legacy); Call(legacy, "ClearOpenedAllocations", _fief);
            check(Data<CampaignTime>(legacy, "_openedFiefVoteDates").Count == 0
                && !legacy.HasPendingFiefVote(_realm) && legacy.GetBribedCandidateVote(_realm, _fief, _candidate) == null,
                "A new capture can clear the previous allocation instead of inheriting stale promises");
            Seed(legacy); Call(legacy, "RememberOpenedAllocation", decision); Call(legacy, "FinishFiefHandoff", key);
            Data<float>(legacy, "_pendingFiefCreatedDay")[key] = 1;
            check(!legacy.QueueBlockedClaimantDecision(Decision()) && !legacy.HasPendingFiefVote(_realm),
                "Maximum allocation age also falls back without restarting deliberation");
            _mapRealm = _foreign; Call(legacy, "ReconcileOpenedAllocations");
            check(Data<CampaignTime>(legacy, "_openedFiefVoteDates").Count == 0
                && legacy.GetBribedCandidateVote(_realm, _fief, _candidate) == null,
                "Conquest by another realm discards both recovery receipt and old promises");
        }
        finally
        {
            h.UnpatchAll(h.Id);
            dayTicks.SetValue(null, oldDayTicks);
            hourTicks.SetValue(null, oldHourTicks);
            AccessTools.PropertySetter(typeof(Campaign), "Current").Invoke(null, new object[] { old });
        }
    }
}
