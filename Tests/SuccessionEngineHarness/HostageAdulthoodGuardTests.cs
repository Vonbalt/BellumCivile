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
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Library;

internal static class HostageAdulthoodGuardTests
{
    private sealed class Manager : ICampaignBehaviorManager
    {
        internal readonly List<CampaignBehaviorBase> Items = new List<CampaignBehaviorBase>();
        internal int Queries;
        internal Exception Failure;
        public T GetBehavior<T>()
        {
            Queries++;
            if (Failure != null) throw Failure;
            return Items.OfType<T>().FirstOrDefault();
        }
        public IEnumerable<T> GetBehaviors<T>() => Items.OfType<T>();
        public void AddBehavior(CampaignBehaviorBase b) => Items.Add(b);
        public void RemoveBehavior<T>() where T : CampaignBehaviorBase => Items.RemoveAll(b => b is T);
        public void ClearBehaviors() => Items.Clear();
        public void InitializeCampaignBehaviors(IEnumerable<CampaignBehaviorBase> b) => Items.AddRange(b);
        public void LoadBehaviorData() { }
        public void RegisterEvents() { }
    }

    private static int _activations, _teleports;
    private static Settlement _destination;
    private static readonly List<string> Logs = new List<string>();
    private static T Empty<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    private static void Set(object target, string field, object value) => AccessTools.Field(target.GetType(), field).SetValue(target, value);
    private static void Current(Campaign campaign) => AccessTools.PropertySetter(typeof(Campaign), "Current").Invoke(null, new object[] { campaign });
    private static bool Log(string message) { Logs.Add(message); return false; }
    private static bool Activate(Hero __instance, Hero.CharacterStates newState)
    { _activations++; Set(__instance, "_heroState", newState); return false; }
    private static bool Teleport(Settlement targetSettlement)
    { _teleports++; _destination = targetSettlement; return false; }
    private static Exception Failure(Action action)
    {
        try { action(); return null; }
        catch (TargetInvocationException ex) { return ex.InnerException; }
    }

    internal static void Run(Action<bool, string> check)
    {
        var harmony = new Harmony("bellum.tests.hostage_adulthood");
        var oldCampaign = Campaign.Current;
        var ticks = AccessTools.Field(typeof(CampaignTime), "TimeTicksPerDay");
        var oldTicks = ticks.GetValue(null);
        var patch = typeof(HostagePactBehavior).Assembly.GetType("BellumCivile.Patches.HostageAdulthoodRelocationGuardPatch", true);
        var prefix = AccessTools.Method(patch, "Prefix");
        bool Allows(Hero hero) => (bool)prefix.Invoke(null, new object[] { hero });
        var native = AccessTools.Method(typeof(HeroSpawnCampaignBehavior), "OnHeroComesOfAge");
        var spawn = Empty<HeroSpawnCampaignBehavior>();
        void ComeOfAge(Hero hero) => native.Invoke(spawn, new object[] { hero });
        var manager = new Manager();
        var campaign = Empty<Campaign>(); campaign.AddCampaignBehaviorManager(manager);
        var hero = Empty<Hero>();
        var home = Empty<Settlement>();
        Set(hero, "_children", new MBList<Hero>());
        Set(hero, "_homeSettlement", home);
        _activations = _teleports = 0; _destination = null; Logs.Clear();
        try
        {
            ticks.SetValue(null, 24000L);
            harmony.Patch(AccessTools.Method(typeof(BellumCivileLogger), nameof(BellumCivileLogger.Log)),
                prefix: new HarmonyMethod(typeof(HostageAdulthoodGuardTests), nameof(Log)));
            harmony.Patch(AccessTools.Method(typeof(Hero), nameof(Hero.ChangeState)),
                prefix: new HarmonyMethod(typeof(HostageAdulthoodGuardTests), nameof(Activate)));
            harmony.Patch(AccessTools.Method(typeof(TeleportHeroAction), nameof(TeleportHeroAction.ApplyImmediateTeleportToSettlement)),
                prefix: new HarmonyMethod(typeof(HostageAdulthoodGuardTests), nameof(Teleport)));
            check(harmony.CreateClassProcessor(patch).Patch().Count == 1,
                "Adulthood relocation guard binds to the installed native handler");

            Current(campaign);
            check(!Allows(null) && manager.Queries == 0, "Null hero is rejected before any campaign behavior lookup");
            check(Logs.Count == 1 && Logs[0].Contains("null hero") && Logs[0].Contains("OnHeroComesOfAge"),
                "Null-hero diagnostic identifies the affected native relocation handler");
            Logs.Clear(); ComeOfAge(null);
            check(Logs.Count == 1 && _activations == 0 && _teleports == 0 && manager.Queries == 0,
                "Null event safely skips this native handler without activation or teleportation");
            Logs.Clear();

            foreach (var state in new[] { Hero.CharacterStates.NotSpawned, Hero.CharacterStates.Active,
                Hero.CharacterStates.Disabled, Hero.CharacterStates.Traveling })
            {
                Set(hero, "_heroState", state);
                check(Allows(hero) && manager.Queries == 0, "Non-prisoner bypasses hostage lookup: " + state);
            }
            Current(Empty<Campaign>());
            Set(hero, "_heroState", Hero.CharacterStates.NotSpawned);
            check(Allows(hero), "Non-prisoner does not depend on campaign behavior manager initialization");
            Current(campaign);
            ComeOfAge(hero);
            check(hero.IsActive && _activations == 1 && _teleports == 1 && _destination == home
                && manager.Queries == 0 && Logs.Count == 0,
                "Ordinary adulthood preserves native activation and home relocation without new diagnostics");

            Set(hero, "_heroState", Hero.CharacterStates.Prisoner);
            Current(null);
            check(Allows(hero), "Missing campaign preserves existing prisoner guard behavior");
            Current(campaign);
            check(Allows(hero), "Missing hostage behavior preserves ordinary prisoner handling");
            var behavior = new HostagePactBehavior(); manager.Items.Add(behavior);
            var pacts = Enumerable.Repeat<HostagePactRecord>(null, 950).ToList();
            pacts.Add(new HostagePactRecord());
            pacts.Add(new HostagePactRecord { FirstHostage = new TreatyHostageRecord() });
            Set(behavior, "_pacts", pacts);
            check(Allows(hero), "Null pact entries, absent slots and missing hostage references remain tolerated");
            hero.CaptivityStartTime = CampaignTime.Days(10);
            var slot = new TreatyHostageRecord { Hero = hero, CustodyEstablished = true, CaptivityStartDay = 10 };
            var pact = new HostagePactRecord { Phase = HostagePactPhase.Active, FirstHostage = slot };
            pacts.Add(pact);
            check(!Allows(hero), "Active hostage remains protected by the original custody predicate");
            _activations = _teleports = 0; ComeOfAge(hero);
            check(hero.IsPrisoner && _activations == 0 && _teleports == 0,
                "Coming-of-age handler cannot activate or relocate a current hostage");
            pact.Phase = HostagePactPhase.Resolving;
            check(!Allows(hero), "Pending hostage disposition still blocks relocation");
            pact.Phase = HostagePactPhase.Ended; slot.ActionCompleted = true; slot.Outcome = HostageCustodyOutcome.Retain;
            check(!Allows(hero), "Retained former hostage remains protected during the original captivity");
            slot.Outcome = HostageCustodyOutcome.Release;
            check(Allows(hero), "Completed release no longer claims custody");
            slot.Outcome = HostageCustodyOutcome.Retain; hero.CaptivityStartTime = CampaignTime.Days(20);
            check(Allows(hero), "Later unrelated captivity is not treated as the original hostage pact");
            var error = new InvalidOperationException("behavior lookup failed"); manager.Failure = error;
            check(ReferenceEquals(Failure(() => Allows(hero)), error), "Unrelated custody lookup exceptions are not swallowed");
            manager.Failure = null;

            // Preserve visibility of unrelated native failures; this is not a general save-repair patch.
            manager.Queries = 0;
            Set(hero, "_heroState", Hero.CharacterStates.NotSpawned);
            Set(hero, "_homeSettlement", null);
            Set(hero, "_children", new MBList<Hero> { null });
            Exception broken = Failure(() => ComeOfAge(hero));
            check(broken is NullReferenceException && broken.StackTrace.Contains("UpdateHomeSettlement")
                && manager.Queries == 0 && Logs.Count == 0,
                "Broken native family data still reports its real failure instead of being silently suppressed");
        }
        finally
        {
            harmony.UnpatchAll(harmony.Id);
            Current(oldCampaign); ticks.SetValue(null, oldTicks);
            Logs.Clear(); _destination = null;
        }
    }
}
