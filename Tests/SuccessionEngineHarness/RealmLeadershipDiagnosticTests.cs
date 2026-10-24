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
using TaleWorlds.Library;

internal static class RealmLeadershipDiagnosticTests
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
    private static Campaign _campaign;
    private static readonly List<Kingdom> Realms = new List<Kingdom>();
    private static readonly HashSet<Kingdom> Eliminated = new HashSet<Kingdom>();
    private static readonly List<string> Lines = new List<string>();
    private static int _flushes, _debugReads;
    private static double _day;
    private static bool Current(ref Campaign __result) { __result = _campaign; return false; }
    private static bool All(ref MBReadOnlyList<Kingdom> __result) { __result = new MBReadOnlyList<Kingdom>(Realms); return false; }
    private static bool Gone(Kingdom __instance, ref bool __result) { __result = Eliminated.Contains(__instance); return false; }
    private static bool Now(ref CampaignTime __result) { __result = CampaignTime.Days((float)_day); return false; }
    private static bool Log(string message) { Lines.Add(message); return false; }
    private static bool Flush() { _flushes++; return false; }
    private static bool DebugOff(ref bool __result) { _debugReads++; __result = false; return false; }
    private static bool Skip() => false;
    private static void ThrowAfterMutation(Kingdom __instance)
    {
        AccessTools.Field(typeof(Kingdom), "_rulingClan").SetValue(__instance, null);
        throw new InvalidOperationException("simulated third-party failure");
    }
    private static T Blank<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));

    internal static void Run(Action<bool, string> check)
    {
        var assembly = typeof(FactionObject).Assembly;
        var diagnostic = assembly.GetType("BellumCivile.RealmLeadershipDiagnostics", true);
        var patch = assembly.GetType("BellumCivile.Patches.RealmLeadershipDiagnosticPatch", true);
        var harmony = new Harmony("bellum.tests.realm_leadership_diagnostic");
        var external = new Harmony("bellum.tests.realm_leadership_external");
        void Patch(MethodBase target, string name) => harmony.Patch(target,
            prefix: new HarmonyMethod(typeof(RealmLeadershipDiagnosticTests), name));
        void Call(string name, params object[] args) => AccessTools.Method(diagnostic, name).Invoke(null, args);
        int Incidents() => Lines.Count(s => s.Contains("stage=became_leaderless") || s.Contains("stage=first_observed_leaderless"));
        void Clear() { Lines.Clear(); _flushes = 0; }
        var rulerSetter = AccessTools.PropertySetter(typeof(Kingdom), "RulingClan");
        var ticks = AccessTools.Field(typeof(CampaignTime), "TimeTicksPerDay");
        object oldTicks = ticks.GetValue(null);
        Manager behaviorManager = null;
        Campaign NewCampaign()
        {
            var campaign = Blank<Campaign>(); behaviorManager = new Manager();
            campaign.AddCampaignBehaviorManager(behaviorManager); return campaign;
        }
        Kingdom Realm(string id, Clan ruler)
        {
            var realm = Blank<Kingdom>(); realm.StringId = id;
            AccessTools.Field(typeof(Kingdom), "_rulingClan").SetValue(realm, ruler);
            Realms.Add(realm); return realm;
        }
        Clan House(string id)
        {
            var clan = Blank<Clan>(); clan.StringId = id;
            var head = Blank<Hero>(); head.StringId = id + "_head";
            AccessTools.Field(typeof(Clan), "_leader").SetValue(clan, head); return clan;
        }
        try
        {
            Realms.Clear(); Eliminated.Clear(); Clear(); _debugReads = 0; _day = 10;
            _campaign = NewCampaign(); ticks.SetValue(null, 1000L);
            Patch(AccessTools.PropertyGetter(typeof(Campaign), "Current"), nameof(Current));
            Patch(AccessTools.PropertyGetter(typeof(Kingdom), "All"), nameof(All));
            Patch(AccessTools.PropertyGetter(typeof(Kingdom), "IsEliminated"), nameof(Gone));
            Patch(AccessTools.PropertyGetter(typeof(CampaignTime), "Now"), nameof(Now));
            Patch(AccessTools.Method(typeof(BellumCivileLogger), "Log"), nameof(Log));
            Patch(AccessTools.Method(typeof(BellumCivileLogger), "Flush"), nameof(Flush));
            Patch(AccessTools.PropertyGetter(assembly.GetType("BellumCivile.BellumCivileDebug"), "ShowInGameMessages"), nameof(DebugOff));
            Patch(AccessTools.PropertySetter(typeof(Hero), "Clan"), nameof(Skip));
            harmony.CreateClassProcessor(patch).Patch();
            var house = House("original_house"); var realm = Realm("vlandia_restored", house);
            rulerSetter.Invoke(realm, new object[] { null });
            Call("OnHourlyTick");
            check(realm.RulingClan == null && Lines.Count == 0 && _flushes == 0,
                "Startup mutations and early ticks remain silent while native campaign data is incomplete");
            rulerSetter.Invoke(realm, new object[] { house });
            Call("OnGameLoaded", new object[] { null });
            check(Lines.Count == 0 && _flushes == 0, "Healthy realms generate no leadership diagnostics");
            rulerSetter.Invoke(realm, new object[] { null });
            check(realm.RulingClan == null && Incidents() == 1, "The actual ruler setter logs loss without repairing or blocking it");
            check(Lines.Any(s => s.Contains("previous_ruling_clan=original_house") && s.Contains("previous_leader=original_house_head")
                && s.Contains("causal_stack_available=True") && s.Contains("reason=no_ruling_clan")),
                "A native assignment records the previous house, previous leader and causal origin");
            check(Lines.Any(s => s.Contains("realm: unavailable=")) && Lines.Any(s => s.Contains("patch_owners:")
                && s.Contains(harmony.Id)) && Lines.Any(s => s.Contains("callers:")) && _flushes == 1,
                "Unavailable auxiliary state cannot prevent stack, patch-owner evidence or immediate flushing");
            int count = Lines.Count;
            Call("OnHourlyTick"); Call("OnHourlyTick");
            rulerSetter.Invoke(realm, new object[] { null });
            check(Lines.Count == count && _flushes == 1 && _debugReads == 0,
                "Repeated observations do not spam logs and never consult debug-message settings");
            _day = 11; rulerSetter.Invoke(realm, new object[] { house });
            check(Lines.Any(s => s.Contains("stage=recovered") && s.Contains("elapsed_days=" + 1d.ToString("F3"))) && _flushes == 2,
                "Recovery closes and times the incident without any diagnostic repair");
            Clear(); AccessTools.Method(typeof(Clan), "SetLeader").Invoke(house, new object[] { null });
            check(house.Leader == null && realm.RulingClan == house && Incidents() == 1
                && Lines.Any(s => s.Contains("reason=ruling_house_has_no_leader") && s.Contains("Clan.SetLeader")),
                "The actual clan-leader method detects leadership loss without changing the ruling house");
            var next = Blank<Hero>(); next.StringId = "next_head";
            AccessTools.Method(typeof(Clan), "SetLeader").Invoke(house, new object[] { next });
            check(Lines.Any(s => s.Contains("stage=recovered") && s.Contains("leader=next_head")),
                "A new head of the same ruling house resolves the recorded incident");
            Clear(); AccessTools.Field(typeof(Kingdom), "_rulingClan").SetValue(realm, null);
            Call("OnHourlyTick");
            check(Incidents() == 1 && Lines.Any(s => s.Contains("source=hourly_audit")
                && s.Contains("causal_stack_available=False") && s.Contains("previous_leader=next_head")),
                "An hourly audit catches raw-field changes without falsely attributing its detection stack as the cause");
            Eliminated.Add(realm); Call("OnHourlyTick");
            check(Lines.Any(s => s.Contains("stage=retired")), "Elimination closes an open leaderless incident");
            Clear(); rulerSetter.Invoke(realm, new object[] { null }); Call("OnHourlyTick");
            check(Lines.Count == 0, "Already eliminated realms do not generate new leaderless reports");
            var loaded = Realm("already_orphaned", null); Clear(); Call("OnGameLoaded", new object[] { null });
            check(Incidents() == 1 && Lines.Any(s => s.Contains("stage=first_observed_leaderless")
                && s.Contains("realm=already_orphaned") && s.Contains("previous_leader=unknown")),
                "An orphan loaded from a save is distinguished from a witnessed loss");
            _campaign = NewCampaign(); Clear();
            rulerSetter.Invoke(loaded, new object[] { null }); Call("OnHourlyTick");
            check(Lines.Count == 0, "A new session does not inherit readiness from the previous campaign");
            Call("OnSessionLaunched", new object[] { null });
            check(Incidents() == 1 && Lines.Any(s => s.Contains("realm=already_orphaned")),
                "Session readiness immediately reports a genuinely orphaned realm, with independent deduplication");
            var broken = Realm("native_failure", House("failure_house")); Clear();
            external.Patch(rulerSetter, prefix: new HarmonyMethod(typeof(RealmLeadershipDiagnosticTests), nameof(ThrowAfterMutation))
                { priority = Priority.Last });
            bool threw = false;
            try { rulerSetter.Invoke(broken, new object[] { null }); }
            catch (TargetInvocationException ex) { threw = ex.InnerException is InvalidOperationException && ex.InnerException.Message == "simulated third-party failure"; }
            check(threw && broken.RulingClan == null && Incidents() == 1
                && Lines.Any(s => s.Contains("exception=System.InvalidOperationException")),
                "A finalizer records partial third-party mutations and preserves the original exception");
            external.UnpatchAll(external.Id);
            var deferred = Realm("deferred_restored", House("deferred_house"));
            rulerSetter.Invoke(deferred, new object[] { null }); Clear();
            var destroy = AccessTools.Method(typeof(DestroyKingdomAction), "ApplyInternal");
            external.Patch(destroy, prefix: new HarmonyMethod(typeof(RealmLeadershipDiagnosticTests), nameof(Skip)));
            destroy.Invoke(null, new object[] { deferred, false });
            destroy.Invoke(null, new object[] { deferred, false });
            check(!deferred.IsEliminated && Lines.Count(s => s.Contains("stage=destruction_not_completed")) == 1
                && Lines.Any(s => s.Contains(external.Id)) && _flushes == 1,
                "Deferred destruction is logged once per incident with patch owners, without overriding another mod's guard");
            var resolution = new CivilWarResolutionBehavior();
            behaviorManager.Items.Add(resolution);
            var collapses = (List<CivilWarCollapseRecord>)AccessTools.Field(typeof(CivilWarResolutionBehavior), "_collapses").GetValue(resolution);
            collapses.Add(new CivilWarCollapseRecord { Parent = deferred, SuccessorId = "saved_successor", Stage = 2, Failure = "awaiting transfer" });
            var describe = AccessTools.Method(diagnostic, "DescribeRecovery").Invoke(null, new object[] { deferred }).ToString();
            check(describe.Contains("successor=saved_successor,stage=2,completed=False,failure=awaiting transfer") && !collapses[0].Completed,
                "Pending restored-realm stages are read directly without advancing their recovery journal");
            var migrantHouse = House("migrant_house");
            var formerRealm = Realm("former_restored", migrantHouse);
            var currentRealm = Realm("current_restored", migrantHouse);
            AccessTools.Field(typeof(Clan), "_kingdom").SetValue(migrantHouse, currentRealm);
            Clear(); AccessTools.Method(typeof(Clan), "SetLeader").Invoke(migrantHouse, new object[] { null });
            check(Incidents() == 2 && Lines.Any(s => s.Contains("realm=former_restored;"))
                && Lines.Any(s => s.Contains("realm=current_restored;")),
                "A house leadership change checks former realm references as well as its current kingdom");
            var mortalHouse = House("mortal_house"); var mortal = mortalHouse.Leader;
            Realm("mortal_restored", mortalHouse);
            AccessTools.Field(typeof(Hero), "_clan").SetValue(mortal, mortalHouse);
            Clear();
            try { AccessTools.Method(typeof(Hero), "ChangeState").Invoke(mortal, new object[] { Hero.CharacterStates.Dead }); }
            catch (TargetInvocationException ex) when (ex.InnerException is NullReferenceException) { }
            check(mortal.IsDead && Incidents() == 1 && Lines.Any(s => s.Contains("reason=ruler_dead") && s.Contains("Hero.ChangeState")),
                "The actual hero-state method reports a dead ruler even if later native work fails");
            var unseen = Realm("unseen_orphan", null); Clear();
            rulerSetter.Invoke(unseen, new object[] { null });
            check(Incidents() == 1 && Lines.Any(s => s.Contains("stage=first_observed_leaderless") && s.Contains("causal_stack_available=False")),
                "Encountering an already leaderless realm inside a native method does not mislabel its stack as the original cause");
            var newborn = Realm("newly_constructed", null); Clear();
            rulerSetter.Invoke(newborn, new object[] { House("founder") });
            check(Lines.Count == 0, "Seeding a newly constructed realm's valid founder does not report its initial empty slot as a failure");
            _campaign = null; rulerSetter.Invoke(newborn, new object[] { null });
            check(newborn.RulingClan == null && Lines.Count == 0, "Observation is inert outside a campaign and still cannot block the native setter");
            var targets = ((IEnumerable<MethodBase>)AccessTools.Method(patch, "TargetMethods").Invoke(null, null)).ToArray();
            check(targets.Length == 5 && targets.All(m => m != null)
                && AccessTools.Method(patch, "Prefix").ReturnType == typeof(void)
                && AccessTools.Method(patch, "Finalizer").ReturnType == typeof(void),
                "All five native observation targets exist and hooks cannot skip execution or replace exceptions");
        }
        finally
        {
            external.UnpatchAll(external.Id); harmony.UnpatchAll(harmony.Id);
            ticks.SetValue(null, oldTicks); _campaign = null;
            Realms.Clear(); Eliminated.Clear(); Lines.Clear();
        }
    }
}
