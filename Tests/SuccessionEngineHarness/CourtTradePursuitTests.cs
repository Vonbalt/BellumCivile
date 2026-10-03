using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using BellumCivile;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.Election;
using TaleWorlds.Library;

internal static class CourtTradePursuitTests
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
    private static Kingdom _realm;
    private static Clan _sponsor;
    private static bool _willing, _funded, _register;
    private static int _spent, _refunded, _submissions;
    private static List<KingdomDecision> _decisions;
    private static bool Yes(ref bool __result) { __result = true; return false; }
    private static bool Sponsor(ref Clan __result) { __result = _willing ? _sponsor : null; return false; }
    private static bool Realm(ref Kingdom __result) { __result = _realm; return false; }
    private static bool Now(ref CampaignTime __result) { __result = CampaignTime.Days(40); return false; }
    private static bool Due(ref CampaignTime __result) { __result = CampaignTime.Days(42); return false; }
    private static bool Cost(ref int __result) { __result = 200; return false; }
    private static bool Spend(ref bool __result) { __result = _funded; if (_funded) _spent += 200; return false; }
    private static bool Refund() { _refunded += 200; return false; }
    private static bool Add(KingdomDecision decision)
    { _submissions++; if (_register) _decisions.Add(decision); return false; }
    private static bool Decisions(ref MBReadOnlyList<KingdomDecision> __result)
    { __result = new MBReadOnlyList<KingdomDecision>(_decisions); return false; }

    internal static void Run(Action<bool, string> check)
    {
        T Blank<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
        var oldCampaign = Campaign.Current;
        var ticks = AccessTools.Field(typeof(CampaignTime), "TimeTicksPerDay"); var oldTicks = ticks.GetValue(null);
        var h = new Harmony("bellum.test.trade_pursuit");
        void Patch(MethodBase method, string prefix) => h.Patch(method, prefix: new HarmonyMethod(typeof(CourtTradePursuitTests), prefix));
        var calendar = new CourtAgendaBehavior(); var type = typeof(CourtAgendaBehavior);
        var campaign = Blank<Campaign>(); var manager = new Manager();
        manager.AddBehavior(calendar); manager.AddBehavior(Blank<KingdomDecisionProposalBehavior>());
        _realm = Blank<Kingdom>(); _realm.StringId = "trade_pursuit_home";
        var target = Blank<Kingdom>(); target.StringId = "trade_pursuit_target";
        _sponsor = Blank<Clan>(); _sponsor.StringId = "trade_sponsor";
        var source = type.Assembly.GetType("BellumCivile.CourtTradeObjectiveSource");
        var budget = type.Assembly.GetType("BellumCivile.NpcInfluenceBudgetService");
        CourtAgendaRecord Agenda(double deadline = 60)
        {
            _decisions = new List<KingdomDecision>(); _willing = _funded = _register = true;
            _spent = _refunded = _submissions = 0;
            var a = new CourtAgendaRecord { Realm = _realm, State = CourtAgendaState.PursuingObjective,
                Trade = new CourtTradeRecord { Target = target, Activated = true }, ObjectiveData = new CourtObjectiveRecord() };
            AccessTools.Method(typeof(CourtObjectiveRecord), "FreezeTerm").Invoke(a.ObjectiveData, new object[] { 30d, deadline });
            return a;
        }
        void Pursue(CourtAgendaRecord a, double day = 40) => AccessTools.Method(type, "PursueCourtTrade").Invoke(calendar, new object[] { a, day });
        try
        {
            ticks.SetValue(null, 1000L);
            AccessTools.PropertySetter(typeof(Campaign), "Current").Invoke(null, new object[] { campaign });
            campaign.AddCampaignBehaviorManager(manager);
            Patch(AccessTools.PropertyGetter(typeof(CampaignTime), "Now"), nameof(Now));
            Patch(AccessTools.Method(typeof(CampaignTime), "HoursFromNow"), nameof(Due));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "Kingdom"), nameof(Realm));
            Patch(AccessTools.PropertyGetter(typeof(Kingdom), "UnresolvedDecisions"), nameof(Decisions));
            Patch(AccessTools.Method(source, "Legal"), nameof(Yes));
            Patch(AccessTools.Method(source, "ProspectiveSponsor"), nameof(Sponsor));
            Patch(AccessTools.Method(typeof(KingdomDecisionProposalBehavior), "ConsiderTradeAgreement"), nameof(Yes));
            Patch(AccessTools.Method(typeof(TradeAgreementDecision), "IsAllowed"), nameof(Yes));
            Patch(AccessTools.Method(typeof(TradeAgreementDecision), "CanMakeDecision"), nameof(Yes));
            Patch(AccessTools.Method(typeof(KingdomDecision), "GetInfluenceCost", new[] { typeof(Clan) }), nameof(Cost));
            Patch(AccessTools.Method(budget, "TrySpend"), nameof(Spend));
            Patch(AccessTools.Method(budget, "Refund"), nameof(Refund));
            Patch(AccessTools.Method(typeof(IdeologyBehavior), "AddDecisionAsModAction"), nameof(Add));
            var a = Agenda(); Pursue(a); Pursue(a, 44);
            check(a.Trade.ProposalAttempted && _submissions == 1 && _spent == 200 && _refunded == 0,
                "Trade agenda files exactly one paid native vote");
            _decisions.Clear(); Pursue(a, 48);
            check(_submissions == 1, "A removed or defeated ballot does not restart agenda submissions");
            a = Agenda(); _register = false; Pursue(a);
            check(!a.Trade.ProposalAttempted && _spent == 200 && _refunded == 200,
                "Rejected registration refunds exactly its proposal fee");
            Pursue(a, 41); check(_submissions == 1, "Failed registration respects saved retry day");
            Pursue(a, 43); Pursue(a, 46); Pursue(a, 49);
            check(_submissions == 3 && _spent == _refunded, "Technical registration failures are bounded and conserve influence");
            a = Agenda(); _funded = false; Pursue(a);
            check(_spent == 0 && _submissions == 0 && !a.Trade.ProposalAttempted,
                "Unavailable budget waits without consuming the term's proposal");
            a = Agenda(); _willing = false; Pursue(a);
            check(_spent == 0 && _submissions == 0 && a.Trade.LastBlocker == "no_funded_willing_participant",
                "Absent sponsor records a specific blocker without forcing a vote");
            a = Agenda(); a.Trade.NextAttemptDay = 45; Pursue(a);
            check(_submissions == 0, "Restored future attempt date prevents replay");
            a = Agenda(41); Pursue(a);
            check(_spent == 0 && _submissions == 0 && a.Trade.LastBlocker == "insufficient_time_for_ballot",
                "No automatic vote is started beyond the fixed objective deadline");
            var fields = typeof(CourtTradeRecord).GetFields();
            check(fields.All(f => f.IsDefined(typeof(TaleWorlds.SaveSystem.SaveableFieldAttribute), false)),
                "Trade attempt, outcome, blocker and review timing are saved");
        }
        finally
        {
            h.UnpatchAll(h.Id); ticks.SetValue(null, oldTicks);
            AccessTools.PropertySetter(typeof(Campaign), "Current").Invoke(null, new object[] { oldCampaign });
        }
    }
}
