using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;
using BellumCivile;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Election;
using TaleWorlds.Localization;

internal static class InternalPeaceTests
{
    private static Kingdom _first, _second;
    private static InternalPeaceSettlementBehavior _behavior;
    private static float _day, _firstWill, _secondWill;
    private static bool _internal, _rival, _atWar, _termsAccepted, _revamp;
    private static WarScoreRecord _willContext;
    private static bool Current(ref InternalPeaceSettlementBehavior __result) { __result = _behavior; return false; }
    private static bool Identify(Kingdom __0, Kingdom __1, ref string __2, ref bool __3, ref bool __result)
    { __2 = "test_pair"; __3 = _rival; __result = _internal && __0 != null && __1 != null && __0 != __1; return false; }
    private static bool Resolve(string __0, ref Kingdom __result)
    { __result = __0 == "first" ? _first : __0 == "second" ? _second : null; return false; }
    private static bool AtWar(ref bool __result) { __result = _atWar; return false; }
    private static bool False(ref bool __result) { __result = false; return false; }
    private static bool True(ref bool __result) { __result = true; return false; }
    private static bool Day(ref float __result) { __result = _day; return false; }
    private static bool Revamp(ref bool __result) { __result = _revamp; return false; }
    private static bool NoPlayer(ref Clan __result) { __result = null; return false; }
    private static bool Terms(ref TextObject __3, ref bool __result)
    { __3 = new TextObject("test"); __result = _termsAccepted; return false; }
    private static bool Will(Kingdom __0, WarScoreRecord __1, ref float __result)
    { _willContext = __1; __result = __0 == _first ? _firstWill : _secondWill; return false; }
    private static T Blank<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    private static object Call(object instance, string name, params object[] args)
        => AccessTools.Method(instance.GetType(), name).Invoke(instance, args);
    private static Dictionary<string, T> Map<T>(string name)
        => (Dictionary<string, T>)AccessTools.Field(typeof(InternalPeaceSettlementBehavior), name).GetValue(_behavior);
    private static MakePeaceKingdomDecision Decision(bool apply = true)
    {
        var decision = Blank<MakePeaceKingdomDecision>();
        AccessTools.Field(typeof(KingdomDecision), "_kingdom").SetValue(decision, _first);
        AccessTools.Field(typeof(MakePeaceKingdomDecision), "FactionToMakePeaceWith").SetValue(decision, _second);
        AccessTools.Field(typeof(MakePeaceKingdomDecision), "_applyResults").SetValue(decision, apply);
        AccessTools.Field(typeof(MakePeaceKingdomDecision), "DailyTributeToBePaid").SetValue(decision, 900);
        return decision;
    }

    internal static void Run(Action<bool, string> check)
    {
        var h = new Harmony("bellum.test.internal_peace");
        void Patch(MethodBase m, string name) => h.Patch(m, prefix: new HarmonyMethod(typeof(InternalPeaceTests), name));
        var type = typeof(InternalPeaceSettlementBehavior);
        var scores = new WarScoreBehavior();
        _first = Blank<Kingdom>(); _first.StringId = "first";
        _second = Blank<Kingdom>(); _second.StringId = "second";
        _internal = true; _atWar = true; _rival = false; _revamp = false;
        _behavior = new InternalPeaceSettlementBehavior();
        try
        {
            Patch(AccessTools.PropertyGetter(typeof(Kingdom), "IsEliminated"), nameof(False));
            Patch(AccessTools.Method(typeof(Kingdom), "IsAtWarWith"), nameof(AtWar));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "PlayerClan"), nameof(NoPlayer));
            Patch(AccessTools.Method(typeof(WarScoreBehavior), "ResolveKingdom"), nameof(Resolve));
            Patch(AccessTools.Method(typeof(WarScoreBehavior), "ShouldProcessWar"), nameof(True));
            Patch(AccessTools.Method(typeof(WarScoreBehavior), "CalculatePowerWeightedWarWill"), nameof(Will));
            Patch(AccessTools.PropertyGetter(typeof(WarScoreBehavior), "CurrentDay"), nameof(Day));
            Patch(AccessTools.Method(typeof(CivilWarConflictBehavior), "IsScoreTransferPending"), nameof(False));
            Patch(AccessTools.Method(typeof(WarPeaceRevampBehavior), "IsRevampEnabled"), nameof(Revamp));
            var war = new WarScoreRecord("pair", "first", "second", 0, null, WarScoreConflictType.CivilWar);
            bool Accept(string winner) => (bool)Call(scores, "CanNegotiateInternalOutcome", war, winner, null);
            void Score(float n) => AccessTools.Field(typeof(WarScoreRecord), "_score").SetValue(war, n);
            var options = type.Assembly.GetType("BellumCivile.BellumCivileOptions", true);
            _day = 1;
            _firstWill = _secondWill = 0;
            check(Accept(""), "Fully exhausted sides may negotiate white peace without a fixed minimum age");
            check(_willContext == war, "Negotiated exhaustion includes the current conflict's court bonuses");
            _secondWill = 9; check(!Accept(""), "Early reluctance can outweigh low enthusiasm");
            _day = (int)AccessTools.Property(options, "WarDurationReluctanceDays").GetValue(null);
            check(Accept(""), "Prolonged exhaustion supports white peace");
            _secondWill = 10.1f; check(!Accept(""), "Both sides must meet the exhaustion ceiling"); _secondWill = 0;
            Score(10); check(Accept(""), "White peace includes exactly ten leverage");
            Score(10.01f); check(!Accept(""), "White peace excludes leverage above ten");
            _day = 99; Score(49.99f); check(!Accept("first"), "Pre-fallback surrender requires fifty leverage");
            Score(50); check(!Accept("") && Accept("first"), "Fifty leverage permits exhausted defeat, not white peace");
            _secondWill = 10; check(!Accept("first"), "Ordinary concession requires enthusiasm strictly below ten");
            _secondWill = 0;
            _day = 100; Score(20); check(Accept("first"), "Day-hundred fallback closes the exhausted middle-score gap");
            _secondWill = .01f; check(!Accept("first"), "Low-score fallback requires complete exhaustion"); _secondWill = 0;
            check(!Accept("second"), "The losing side cannot demand submission with reversed leverage");
            _secondWill = 100; check(!Accept("first"), "Nonterminal leverage does not force an unexhausted loser to submit");
            Score(100); check(Accept("first"), "Terminal leverage allows submission without exhaustion");
            Score(-100); check(Accept("second") && !Accept("first"), "Terminal submission respects score orientation");
            check(!Accept("missing"), "An unknown winner cannot be accepted");
            war.BeginResolution(); check(!Accept("second"), "Already resolving conflicts cannot negotiate again"); war.CancelResolution();
            _atWar = false; check(!Accept("second"), "Settled hostility invalidates a pending negotiation"); _atWar = true;
            AccessTools.Field(typeof(WarScoreRecord), "_conflictType").SetValue(war, WarScoreConflictType.ForeignWar);
            check(!Accept("second"), "Restricted internal terms never apply to foreign wars");
            AccessTools.Field(typeof(WarScoreRecord), "_conflictType").SetValue(war, WarScoreConflictType.CivilWar);
            AccessTools.Field(typeof(WarScoreRecord), "_contextId").SetValue(war,
                (string)AccessTools.Field(typeof(CivilWarPairRecord), "RivalryPrefix").GetRawConstantValue() + "test");
            Score(0); _firstWill = _secondWill = 0;
            check(!Accept(""), "Rival claimants cannot negotiate a separate white peace");

            Patch(AccessTools.PropertyGetter(type, "Current"), nameof(Current));
            Patch(AccessTools.Method(type, "TryIdentify"), nameof(Identify));
            Patch(AccessTools.Method(type, "FindRealm"), nameof(Resolve));
            Patch(AccessTools.Method(type, "CanAcceptTerms"), nameof(Terms));
            foreach (string patch in new[] { "InternalPeaceVoteOutcomePatch", "InternalPeaceResultTextPatch", "InternalPeaceSupportTextPatch", "InternalPeaceCourierPatch",
                "InternalPeaceRivalVotePatch", "InternalPeaceDescriptionPatch", "InternalPeaceStaleRivalVotePatch", "InternalPeaceRivalInsertionPatch" })
                h.CreateClassProcessor(type.Assembly.GetType("BellumCivile.Patches." + patch, true)).Patch();
            var yes = new MakePeaceKingdomDecision.MakePeaceDecisionOutcome(true, _first, _second);
            var no = new MakePeaceKingdomDecision.MakePeaceDecisionOutcome(false, _first, _second);
            Decision(false).ApplyChosenOutcome(yes);
            check(Map<string>("_first").Count == 0, "Native preview decisions do not queue a settlement");
            var rejected = Decision(); rejected.ApplyChosenOutcome(no);
            check(Map<string>("_first").Count == 0, "A rejected own-side vote leaves the conflict unchanged");
            var decision = Decision(); decision.ApplyChosenOutcome(yes);
            check(Map<string>("_first").Count == 1 && Map<string>("_winner")["test_pair"] == "",
                "Revamp-off native approval queues white peace instead of calling native peace or tribute");
            check(decision.GetSupportTitle().ToString().Contains("No tribute"), "Internal vote text does not display obsolete native tribute");
            check(decision.OnShowDecision(), "Internal council outcomes bypass native tribute courier redirection");
            decision.ApplyChosenOutcome(yes);
            check(decision.GetChosenOutcomeText(yes, KingdomDecision.SupportStatus.Majority).ToString().Contains("agreed"),
                "Repeated outcome callbacks retain the accepted receipt instead of overwriting it as failed");
            _internal = false;
            check(decision.GetChosenOutcomeText(yes, KingdomDecision.SupportStatus.Majority).ToString().Contains("agreed"),
                "Cached result text survives loss of the realm registry without dereferencing a dead ruler");
            _internal = true;
            check(!(bool)Call(_behavior, "QueueSettlement", _second, _first, _first, false, false), "Duplicate inverse settlement cannot overwrite accepted terms");
            Call(_behavior, "Remove", "test_pair");
            _rival = true;
            check(!(bool)Call(_behavior, "QueueWhitePeace", _first, _second, false), "Registry fallback also refuses rival white peace with revamp disabled");
            _rival = false;
            var offer = Decision(); var unrelated = Decision();
            Map<KingdomDecision>("_offerDecisions")["test_pair"] = offer;
            Map<string>("_offeredWinner")["test_pair"] = "first";
            check((bool)Call(_behavior, "HasOffer", offer) && !(bool)Call(_behavior, "HasOffer", unrelated),
                "Only the exact saved decision owns its restricted terms");
            _termsAccepted = false;
            offer.ApplyChosenOutcome(yes);
            check(Map<string>("_first").Count == 0 && Map<KingdomDecision>("_offerDecisions").Count == 0,
                "Opponent acceptance is revalidated after council voting, including a revamp toggle");
            var next = Decision(); Map<KingdomDecision>("_offerDecisions")["test_pair"] = next;
            Map<string>("_offeredWinner")["test_pair"] = "second"; _termsAccepted = true;
            next.ApplyChosenOutcome(yes);
            check(Map<string>("_winner")["test_pair"] == "second", "Approved concession preserves the named winner, not white peace");
            var store = new Store(); _behavior.SyncData(store);
            var reloaded = new InternalPeaceSettlementBehavior(); store.IsLoading = true; reloaded.SyncData(store);
            _behavior = reloaded;
            check(Map<string>("_winner")["test_pair"] == "second", "Deferred outcome survives campaign SyncData round trip");
        }
        finally { h.UnpatchAll(h.Id); _behavior = null; _first = _second = null; }
    }

    private sealed class Store : IDataStore
    {
        private readonly Dictionary<string, object> _data = new Dictionary<string, object>();
        public bool IsLoading { get; set; }
        public bool IsSaving => !IsLoading;
        public bool SyncData<T>(string key, ref T data)
        { if (IsLoading) { if (_data.TryGetValue(key, out object value)) data = (T)value; } else _data[key] = data; return true; }
    }
}
