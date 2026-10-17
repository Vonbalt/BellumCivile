using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using BellumCivile;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;

internal static class InternalInstabilityTests
{
    private static Clan _player;
    private static float _day, _mood;
    private static bool Player(ref Clan __result) { __result = _player; return false; }
    private static bool Now(ref CampaignTime __result) { __result = CampaignTime.Days(_day); return false; }
    private static bool Mood(ref float __result) { __result = _mood; return false; }
    private static bool Skip() => false;
    private static T Blank<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
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

    internal static void Run(Action<bool, string> check)
    {
        var assembly = typeof(CourtAgendaBehavior).Assembly;
        Type Type(string name) => assembly.GetType("BellumCivile." + name, true);
        var settingsType = Type("BellumCivileSettings");
        var options = Type("BellumCivileOptions");
        var rules = Type("InternalInstabilityRules");
        var cache = AccessTools.Field(options, "_settings");
        var oldSettings = cache.GetValue(null);
        var ticks = AccessTools.Field(typeof(CampaignTime), "TimeTicksPerDay");
        var oldTicks = ticks.GetValue(null);
        var harmony = new Harmony("bellum.test.internal_instability");
        void Patch(MethodBase method, string prefix) => harmony.Patch(method, prefix: new HarmonyMethod(typeof(InternalInstabilityTests), prefix));
        try
        {
            Patch(AccessTools.Method(Type("FeudalTitleStylePresetCatalog"), "CreateDropdown"), nameof(Skip));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "PlayerClan"), nameof(Player));
            Patch(AccessTools.PropertyGetter(typeof(CampaignTime), "Now"), nameof(Now));
            Patch(AccessTools.PropertyGetter(typeof(FactionObject), "Mood"), nameof(Mood));
            ticks.SetValue(null, 1000L);
            InternalInstabilityPresetTests.Run(settingsType, check);
            object settings = Activator.CreateInstance(settingsType, true);
            cache.SetValue(null, settings);
            void Set(string name, int value) => AccessTools.Property(settingsType, name).SetValue(settings, value);
            double Option(string name) => Convert.ToDouble(AccessTools.Property(options, name).GetValue(null));
            var specs = new[] {
                ("CourtCrisisMoodThreshold", -100, -25, -60), ("CourtChallengeChancePercent", 0, 100, 100),
                ("HeirChallengeLoyaltyThreshold", 0, 50, 25), ("ElectionChallengeChancePercent", 0, 200, 100),
                ("ClaimFeudStartThreshold", 25, 150, 65), ("ClaimedRevocationChancePercent", 0, 200, 100),
                ("TreasonResistanceChancePercent", 0, 200, 100), ("ClientLiberationWillingnessThreshold", 0, 100, 75)
            };
            foreach (var spec in specs)
            {
                var property = AccessTools.Property(settingsType, spec.Item1);
                check(Convert.ToInt32(property.GetValue(settings)) == spec.Item4 && Option(spec.Item1) == spec.Item4,
                    spec.Item1 + " retains the existing Bellum default");
                var slider = property.GetCustomAttributesData().Single(a => a.AttributeType.Name == "SettingPropertyIntegerAttribute");
                check((int)slider.ConstructorArguments[1].Value == spec.Item2 && (int)slider.ConstructorArguments[2].Value == spec.Item3,
                    spec.Item1 + " exposes the intended MCM bounds");
                string hint = (string)slider.NamedArguments.Single(a => a.MemberName == "HintText").TypedValue.Value;
                check(hint.Contains("Bellum default:") && hint.Contains("Increasing") && hint.ToLowerInvariant().Contains("decreasing"),
                    spec.Item1 + " documents default and both adjustment directions");
                Set(spec.Item1, -9999); check(Option(spec.Item1) == spec.Item2, spec.Item1 + " clamps below range");
                Set(spec.Item1, 9999); check(Option(spec.Item1) == spec.Item3, spec.Item1 + " clamps above range");
                Set(spec.Item1, spec.Item4);
            }
            Set("RebellionThreshold", 187);
            check(Option("RebelliousIntentThreshold") == 187, "Existing rebellion preference keeps its serialized property");
            Set("ClientLiberationWillingnessThreshold", 65);
            _player = Blank<Clan>();
            var npc = Blank<Clan>();
            var clientThreshold = AccessTools.Method(typeof(ClientKingdomBehavior), "LiberationWillingnessThreshold");
            check((float)clientThreshold.Invoke(null, new object[] { npc }) == 65
                && (float)clientThreshold.Invoke(null, new object[] { _player }) == 75 && Option("WarWillDeclareThreshold") == 75,
                "Client setting excludes player initiation and foreign wars");

            var scale = AccessTools.Method(rules, "ScaleChance");
            double Chance(double p, int percent) => (double)scale.Invoke(null, new object[] { p, percent });
            foreach (int percent in new[] { 0, 50, 100, 125, 150, 200 })
                foreach (double p in new[] { 0d, .1, .3, .5, .9, 1d })
                    check(Math.Abs(Chance(p, percent) - Math.Min(1, p * percent / 100d)) < 1e-12,
                        $"Chance scaling p={p}, percent={percent}");
            check(Chance(double.NaN, 100) == 0 && Chance(-.2, 150) == 0 && Chance(1.3, 50) == .5,
                "Invalid probabilities are safe and base probability is capped before scaling");
            var heir = AccessTools.Method(Type("SuccessionChallengeRules"), "CanInitiateAtThreshold");
            foreach (int threshold in new[] { 0, 15, 25, 35, 50 })
                foreach (double loyalty in new[] { 0d, 14, 15, 24, 25, 34, 35, 50, double.NaN })
                    check((bool)heir.Invoke(null, new object[] { loyalty, true, true, false, threshold }) == (loyalty < threshold),
                        $"Heir threshold {threshold}, loyalty {loyalty}");
            check(!(bool)heir.Invoke(null, new object[] { 0d, true, true, true, 50 })
                && !(bool)heir.Invoke(null, new object[] { 0d, false, true, false, 50 }), "Heir safety and cooldown gates survive higher thresholds");

            var decide = AccessTools.Method(typeof(ElectiveContestCandidate), "Decide");
            var legacy = new ElectiveContestCandidate { HasAssessment = true, Acceptance = 50 };
            Set("ElectionChallengeChancePercent", 0);
            decide.Invoke(legacy, new object[] { true, false, .49 });
            check(legacy.Decision == ElectiveContestDecision.Contest, "Legacy pending candidates retain their original chance without new save fields");
            var disabled = new ElectiveContestCandidate { HasAssessment = true, Acceptance = 0, HasChallengeChanceSnapshot = true, ChallengeChancePercent = 0 };
            decide.Invoke(disabled, new object[] { true, false, 0d });
            check(disabled.Decision == ElectiveContestDecision.Accept, "Zero election chance does not contest even on RNG zero");
            disabled.ChallengeChancePercent = 200;
            decide.Invoke(disabled, new object[] { true, false, 0d });
            check(disabled.Decision == ElectiveContestDecision.Accept, "Resolved candidate cannot reroll");
            var player = new ElectiveContestCandidate { HasAssessment = true, Acceptance = 0, HasChallengeChanceSnapshot = true };
            decide.Invoke(player, new object[] { true, true, 0d });
            check(player.Decision == ElectiveContestDecision.AwaitingPlayer, "Zero election chance leaves player choice intact");
            var increased = new ElectiveContestCandidate { HasAssessment = true, Acceptance = 50, HasChallengeChanceSnapshot = true, ChallengeChancePercent = 125 };
            decide.Invoke(increased, new object[] { true, false, .6 });
            check(increased.Decision == ElectiveContestDecision.Contest, "Saved 125 percent election chance overrides later MCM zero");
            var required = AccessTools.Property(typeof(CourtLiberationRecord), "RequiredWillingness");
            check((float)required.GetValue(new CourtLiberationRecord()) == 75
                && (float)required.GetValue(new CourtLiberationRecord { HasWillingnessSnapshot = true, WillingnessThreshold = 65 }) == 65,
                "Legacy and new liberation preparations retain their own thresholds");

            var ends = new Dictionary<string, double>(); var allowed = new Dictionary<string, bool>();
            int rolls = 0; double sample = .75;
            Func<double> next = () => { rolls++; return sample; };
            var window = AccessTools.Method(rules, "CourtWindowAllows");
            bool Allows(string key, double now, double end, int percent) =>
                (bool)window.Invoke(null, new object[] { key, now, end, percent, ends, allowed, next });
            check(!Allows("realm|1", 10, 84, 50) && rolls == 1, "Court failure is sampled once");
            sample = 0;
            check(!Allows("realm|1", 17, 84, 100) && rolls == 1, "Emergency retry and changed setting cannot reroll failed term");
            check(Allows("realm|1", 84, 168, 50) && rolls == 2, "Next actual court term grants one fresh opportunity");
            check(Allows("realm|2", 84, 168, 100) && rolls == 2, "Default chance consumes no additional random numbers");
            check(!Allows("realm|3", 84, 168, 0) && rolls == 2, "Zero court chance never draws or succeeds");
            check(Allows("realm|1", 85, 168, 0), "Approved window remains frozen through settings change");

            var realm = Blank<Kingdom>(); realm.StringId = "instability_test";
            var faction = Blank<FactionObject>(); faction.Leader = npc;
            AccessTools.Field(typeof(FactionObject), "_type").SetValue(faction, FactionType.Nobility);
            var court = new CourtAgendaBehavior();
            ((Dictionary<string, CampaignTime>)AccessTools.Field(typeof(CourtAgendaBehavior), "_nextTerms").GetValue(court))[realm.StringId] = CampaignTime.Days(168);
            var crisis = AccessTools.Method(typeof(CourtAgendaBehavior), "ShouldStartCourtCrisis");
            var emergency = AccessTools.Method(typeof(CourtAgendaBehavior), "MayStartAutomaticCourtChallenge");
            _day = 90; _mood = -70;
            Set("CourtCrisisMoodThreshold", -80); Set("CourtChallengeChancePercent", 0);
            check(!(bool)crisis.Invoke(court, new object[] { faction, realm }), "Reduced court threshold keeps moderate anger out of crisis");
            _mood = -100;
            check(!(bool)crisis.Invoke(court, new object[] { faction, realm }) && !(bool)emergency.Invoke(court, new object[] { faction, realm }),
                "Production ordinary and emergency gates both respect zero");
            var store = new Store(); var sync = AccessTools.Method(typeof(CourtAgendaBehavior), "SyncInstabilityData");
            sync.Invoke(court, new object[] { store }); store.IsLoading = true;
            var loaded = new CourtAgendaBehavior(); sync.Invoke(loaded, new object[] { store });
            check(((Dictionary<string, bool>)AccessTools.Field(typeof(CourtAgendaBehavior), "_challengeWindowAllowed").GetValue(loaded)).Values.Single() == false,
                "Court decision survives behavior save-data roundtrip");
            sync.Invoke(new CourtAgendaBehavior(), new object[] { new Store { IsLoading = true } });
            check(true, "Older saves without instability keys initialize empty decision windows");
            ((List<CourtAgendaRecord>)AccessTools.Field(typeof(CourtAgendaBehavior), "_agendas").GetValue(court))
                .Add(new CourtAgendaRecord { Realm = realm, Faction = faction, State = CourtAgendaState.Crisis });
            check((bool)emergency.Invoke(court, new object[] { faction, realm }), "Already-announced crisis survives new zero setting");
            faction.Leader = _player; _mood = -65;
            check((bool)crisis.Invoke(court, new object[] { faction, realm }), "Player court choices keep original mood requirement despite AI settings");
        }
        finally
        {
            harmony.UnpatchAll(harmony.Id);
            cache.SetValue(null, oldSettings);
            ticks.SetValue(null, oldTicks);
        }
    }
}
