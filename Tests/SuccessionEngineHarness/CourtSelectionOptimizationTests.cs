using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using BellumCivile;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Election;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Library;

internal static class CourtSelectionOptimizationTests
{
    private static Kingdom _realm;
    private static Clan _proposer, _opponent;
    private static Hero _leader;
    private static DecisionOutcome _pass;
    private static int _reserveReads;
    private static float _reserve;

    private static T Blank<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    private static bool Realm(ref Kingdom __result) { __result = _realm; return false; }
    private static bool Proposer(ref Clan __result) { __result = _proposer; return false; }
    private static bool Leader(ref Hero __result) { __result = _leader; return false; }
    private static bool Player(ref Clan __result) { __result = null; return false; }
    private static bool Clans(ref MBReadOnlyList<Clan> __result)
    { __result = new MBReadOnlyList<Clan>(new[] { _proposer, _opponent }); return false; }
    private static bool Fiefs(ref MBReadOnlyList<Town> __result)
    { __result = new MBReadOnlyList<Town>(Array.Empty<Town>()); return false; }
    private static bool Active(ref IList<PolicyObject> __result) { __result = new List<PolicyObject>(); return false; }
    private static bool Policies(ref MBReadOnlyList<PolicyObject> __result)
    { __result = new MBReadOnlyList<PolicyObject>(Array.Empty<PolicyObject>()); return false; }
    private static bool Influence(ref float __result) { __result = 300; return false; }
    private static bool CanAfford(ref bool __result) { __result = true; return false; }
    private static bool Free(ref bool __result) { __result = false; return false; }
    private static bool Reserve(ref float __result) { _reserveReads++; __result = _reserve; return false; }
    private static bool Relation(ref int __result) { __result = 50; return false; }
    private static bool Cost(Supporter.SupportWeights supportWeight, ref int __result)
    { __result = ((int)supportWeight - 1) * 20; return false; }
    private static bool Support(Clan clan, DecisionOutcome possibleOutcome, ref float __result)
    { __result = (clan == _proposer) == (possibleOutcome == _pass) ? 80 : -30; return false; }

    internal static void Run(Action<bool, string> check)
    {
        var mod = typeof(CourtAgendaBehavior).Assembly;
        var contextType = mod.GetType("BellumCivile.CourtTermContext", true);
        var forecastType = mod.GetType("BellumCivile.CourtPolicyForecast", true);
        var budgetType = mod.GetType("BellumCivile.NpcInfluenceBudgetService", true);
        var harmony = new Harmony("bellum.test.court_selection_optimization");
        void Patch(MethodBase target, string name) =>
            harmony.Patch(target, prefix: new HarmonyMethod(typeof(CourtSelectionOptimizationTests), name));
        void Getter(Type type, string name, string handler) => Patch(AccessTools.PropertyGetter(type, name), handler);
        try
        {
            _realm = Blank<Kingdom>(); _realm.StringId = "forecast_realm";
            _proposer = Blank<Clan>(); _proposer.StringId = "forecast_for";
            _opponent = Blank<Clan>(); _opponent.StringId = "forecast_against";
            _leader = Blank<Hero>();
            _reserve = 20; _reserveReads = 0;
            _pass = new KingdomPolicyDecision.PolicyDecisionOutcome(true);
            var fail = new KingdomPolicyDecision.PolicyDecisionOutcome(false);
            Getter(typeof(KingdomDecision), "Kingdom", nameof(Realm));
            Getter(typeof(KingdomDecision), "ProposerClan", nameof(Proposer));
            Getter(typeof(Clan), "Kingdom", nameof(Realm));
            Getter(typeof(Clan), "PlayerClan", nameof(Player));
            Getter(typeof(Clan), "Leader", nameof(Leader));
            Getter(typeof(Clan), "Influence", nameof(Influence));
            foreach (var name in new[] { "IsMinorFaction", "IsUnderMercenaryService", "IsEliminated" })
                Getter(typeof(Clan), name, nameof(Free));
            Getter(typeof(Kingdom), "Clans", nameof(Clans));
            Getter(typeof(Kingdom), "Fiefs", nameof(Fiefs));
            Getter(typeof(Kingdom), "ActivePolicies", nameof(Active));
            Getter(typeof(PolicyObject), "All", nameof(Policies));
            Patch(AccessTools.Method(typeof(Hero), "GetRelation", new[] { typeof(Hero) }), nameof(Relation));
            Patch(AccessTools.Method(typeof(KingdomDecision), "GetInfluenceCostOfSupport"), nameof(Cost));
            Patch(AccessTools.Method(typeof(KingdomPolicyDecision), "DetermineSupport"), nameof(Support));
            Patch(AccessTools.Method(budgetType, "GetRoleReserve"), nameof(Reserve));
            Patch(AccessTools.Method(budgetType, "CanAfford"), nameof(CanAfford));

            object Context() => Activator.CreateInstance(contextType, BindingFlags.Instance | BindingFlags.NonPublic,
                null, new object[] { _realm, new Func<PolicyObject, bool>(_ => true), null }, null);
            var live = AccessTools.Method(forecastType, "Calculate", new[] {
                typeof(KingdomDecision), typeof(int), typeof(DecisionOutcome), typeof(DecisionOutcome),
                typeof(IEnumerable<Clan>), typeof(Func<Clan, float>) });
            var cached = AccessTools.Method(contextType, "Forecast", new[] {
                typeof(KingdomDecision), typeof(int), typeof(DecisionOutcome), typeof(DecisionOutcome) });
            var decision = Blank<KingdomPolicyDecision>();
            object Live(int fee) => live.Invoke(null, new object[] { decision, fee, _pass, fail, new[] { _proposer, _opponent }, null });
            object Cached(object context, int fee) => cached.Invoke(context, new object[] { decision, fee, _pass, fail });
            bool Matches(object expected, object actual) => forecastType.GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
                .All(f => Equals(f.GetValue(expected), f.GetValue(actual)));
            var first = Live(50); var second = Live(100);
            check(_reserveReads == 4 && (int)AccessTools.Field(forecastType, "For").GetValue(first) > 0,
                "Two live forecasts read the current reserve for every voter and produce actual vote totals");
            var pass = Context();
            _reserveReads = 0;
            check(Matches(first, Cached(pass, 50)) && Matches(second, Cached(pass, 100)) && _reserveReads == 2,
                "Sharing voter reserves halves reserve reads across two forecasts without changing any forecast result");

            _reserve = 500;
            var changed = Live(50);
            check(!Matches(first, changed) && Matches(changed, Cached(Context(), 50)),
                "A later realm pass reads fresh reserves rather than reusing a stale forecast");
        }
        finally { harmony.UnpatchAll(harmony.Id); }
    }
}
