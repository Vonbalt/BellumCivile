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
using TaleWorlds.CampaignSystem.Election;
using TaleWorlds.Library;

internal static class CourtDynasticPursuitTests
{
    private static Kingdom _realm;
    private static Clan _sponsor;
    private static Clan _player;
    private static Hero _second;
    private static bool _offerReady, _offerSent;
    private static int _offers;
    private static bool _willing, _funded, _register, _throwAfter, _available, _legal, _valid, _married, _throwEvaluation;
    private static object _match;
    private static int _weddings;
    private static int _spent, _refunded, _submissions, _evaluations;
    private static double _day;
    private static MBList<KingdomDecision> _decisions;
    private static bool Yes(ref bool __result) { __result = true; return false; }
    private static bool Valid(ref bool __result) { __result = _valid; return false; }
    private static bool Legal(ref bool __result) { __result = _legal; return false; }
    private static bool Available(ref bool __result) { __result = _available; return false; }
    private static bool Evaluate(ref object __result)
    { _evaluations++; if (_throwEvaluation) throw new InvalidOperationException("test evaluation failure"); __result = _match; return false; }
    private static bool Married(ref bool __result) { __result = _married; return false; }
    private static bool Wedding() { _weddings++; _married = true; return false; }
    private static bool Skip() => false;
    private static bool Sponsor(ref Clan __result) { __result = _willing ? _sponsor : null; return false; }
    private static bool Player(ref Clan __result) { __result = _player; return false; }
    private static bool House(Hero __instance, ref Clan __result) { __result = __instance == _second && _player != null ? _player : _sponsor; return false; }
    private static bool OfferReady(ref bool __result) { __result = _offerReady; return false; }
    private static bool SendOffer(ref bool __result) { _offers++; __result = _offerSent; return false; }
    private static bool Realm(ref Kingdom __result) { __result = _realm; return false; }
    private static bool Now(ref CampaignTime __result) { __result = CampaignTime.Days((float)_day); return false; }
    private static bool Due(ref CampaignTime __result) { __result = CampaignTime.Days((float)(_day + 2)); return false; }
    private static bool Cost(ref int __result) { __result = 200; return false; }
    private static bool Spend(ref bool __result) { __result = _funded; if (_funded) _spent += 200; return false; }
    private static bool Refund() { _refunded += 200; return false; }
    private static bool Add(KingdomDecision decision)
    {
        _submissions++;
        if (_register) _decisions.Add(decision);
        if (_throwAfter) throw new InvalidOperationException("test callback after registration");
        return false;
    }
    private static bool Decisions(ref MBReadOnlyList<KingdomDecision> __result)
    { __result = new MBReadOnlyList<KingdomDecision>(_decisions); return false; }
    private static bool Allies(ref MBReadOnlyList<Kingdom> __result) { __result = new MBReadOnlyList<Kingdom>(new List<Kingdom>()); return false; }

    internal static void Run(Action<bool, string> check)
    {
        T Blank<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
        var ticks = AccessTools.Field(typeof(CampaignTime), "TimeTicksPerDay"); var oldTicks = ticks.GetValue(null);
        var h = new Harmony("bellum.test.dynastic_pursuit");
        void Patch(MethodBase method, string prefix) => h.Patch(method, prefix: new HarmonyMethod(typeof(CourtDynasticPursuitTests), prefix));
        var calendar = new CourtAgendaBehavior(); var type = typeof(CourtAgendaBehavior);
        _realm = Blank<Kingdom>(); _realm.StringId = "dynastic_pursuit_home";
        var target = Blank<Kingdom>(); target.StringId = "dynastic_pursuit_target";
        _sponsor = Blank<Clan>(); _sponsor.StringId = "dynastic_sponsor";
        var budget = type.Assembly.GetType("BellumCivile.NpcInfluenceBudgetService");
        var helper = type.Assembly.GetType("BellumCivile.Behaviors.BellumMarriageStrategyHelper");
        var agendas = (List<CourtAgendaRecord>)AccessTools.Field(type, "_agendas").GetValue(calendar);
        CourtAgendaRecord Agenda(double deadline = 60)
        {
            _day = 40;
            _decisions = new MBList<KingdomDecision>(); _willing = _funded = _register = _available = _legal = _valid = true;
            foreach (var realm in new[] { _realm, target })
            {
                AccessTools.Field(typeof(Kingdom), "_alliedKingdoms").SetValue(realm, new MBList<Kingdom>());
                AccessTools.Field(typeof(Kingdom), "_unresolvedDecisions").SetValue(realm, _decisions);
            }
            _throwAfter = false; _spent = _refunded = _submissions = _evaluations = 0;
            _married = true; _throwEvaluation = false; _weddings = 0;
            _player = null; _offers = 0; _offerReady = _offerSent = true;
            var a = new CourtAgendaRecord { Realm = _realm, State = CourtAgendaState.Completed,
                Dynastic = new CourtDynasticRecord { Target = target, OurHouse = _sponsor, First = Blank<Hero>(), Second = Blank<Hero>(), Activated = true, ActivatedDay = 30 },
                ObjectiveData = new CourtObjectiveRecord { Kind = "court_royal_marriage" } };
            _second = a.Dynastic.Second;
            AccessTools.Method(typeof(CourtObjectiveRecord), "FreezeTerm").Invoke(a.ObjectiveData, new object[] { 30d, deadline });
            var matchType = type.Assembly.GetType("BellumCivile.Behaviors.BellumMarriageMatch");
            _match = Activator.CreateInstance(matchType, true);
            matchType.GetProperty("Suitor").SetValue(_match, a.Dynastic.First);
            matchType.GetProperty("Candidate").SetValue(_match, a.Dynastic.Second);
            var outcomeType = type.Assembly.GetType("BellumCivile.Behaviors.MarriageOutcome");
            var outcome = FormatterServices.GetUninitializedObject(outcomeType);
            AccessTools.Field(outcomeType, "<Destination>k__BackingField").SetValue(outcome, _sponsor);
            matchType.GetProperty("Outcome").SetValue(_match, outcome);
            a.Dynastic.Destination = _sponsor;
            agendas.Clear(); agendas.Add(a);
            return a;
        }
        void Pursue(CourtAgendaRecord a, double day = 40)
        { _day = day; AccessTools.Method(type, "PursueDynasticAlliance").Invoke(calendar, new object[] { a, day }); }
        bool Marry() => (bool)AccessTools.Method(type, "PursueDynasticMarriage").Invoke(calendar, new object[] { _sponsor, new StrategicMarriageBehavior() });
        try
        {
            ticks.SetValue(null, 1000L);
            Patch(AccessTools.PropertyGetter(typeof(CampaignTime), "Now"), nameof(Now));
            Patch(AccessTools.Method(typeof(CampaignTime), "HoursFromNow"), nameof(Due));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "Kingdom"), nameof(Realm));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "PlayerClan"), nameof(Player));
            Patch(AccessTools.PropertyGetter(typeof(Hero), "Clan"), nameof(House));
            Patch(AccessTools.PropertyGetter(typeof(Kingdom), "UnresolvedDecisions"), nameof(Decisions));
            Patch(AccessTools.PropertyGetter(typeof(Kingdom), "AlliedKingdoms"), nameof(Allies));
            Patch(AccessTools.Method(type, "DynasticAllianceSponsor"), nameof(Sponsor));
            Patch(AccessTools.Method(type, "DynasticOwnerValid"), nameof(Valid));
            Patch(AccessTools.Method(type, "DynasticMarried"), nameof(Married));
            Patch(AccessTools.Method(type, "DynasticIdentity"), nameof(Yes));
            Patch(AccessTools.Method(helper, "CourtMarriageParticipant"), nameof(Available));
            Patch(AccessTools.Method(helper, "EvaluateCourtMarriage"), nameof(Evaluate));
            Patch(AccessTools.Method(helper, "CourtMarriageExecutionReady"), nameof(Yes));
            Patch(AccessTools.Method(typeof(MarriageAction), "Apply"), nameof(Wedding));
            Patch(AccessTools.Method(type, "OnCourtMarriageCompleted"), nameof(Skip));
            Patch(AccessTools.Method(type, "ReportDynastic"), nameof(Skip));
            Patch(AccessTools.Method(typeof(StrategicMarriageBehavior), "RecordCourtMarriageOpportunity"), nameof(Skip));
            Patch(AccessTools.PropertyGetter(typeof(StrategicMarriageBehavior), "CanSendCourtMarriageOffer"), nameof(OfferReady));
            Patch(AccessTools.Method(typeof(StrategicMarriageBehavior), "TrySendCourtMarriageOffer"), nameof(SendOffer));
            Patch(AccessTools.Method(typeof(StartAllianceDecision), "IsAllowed"), nameof(Yes));
            Patch(AccessTools.Method(typeof(StartAllianceDecision), "CanMakeDecision"), nameof(Legal));
            Patch(AccessTools.Method(typeof(KingdomDecision), "GetInfluenceCost", new[] { typeof(Clan) }), nameof(Cost));
            Patch(AccessTools.Method(budget, "TrySpend"), nameof(Spend));
            Patch(AccessTools.Method(budget, "Refund"), nameof(Refund));
            Patch(AccessTools.Method(typeof(IdeologyBehavior), "AddDecisionAsModAction"), nameof(Add));
            var a = Agenda(); Pursue(a); Pursue(a, 44);
            check(a.Dynastic.AllianceAttempted && _submissions == 1 && _spent == 200 && _refunded == 0,
                "Dynastic follow-up files exactly one paid native alliance vote");
            _decisions.Clear(); Pursue(a, 48);
            check(_submissions == 1, "Rejected or removed alliance ballot cannot be resubmitted by the same motion");
            a = Agenda(); _register = false; Pursue(a);
            check(!a.Dynastic.AllianceAttempted && _spent == 200 && _refunded == 200,
                "Unregistered alliance proposal refunds its full fee");
            Pursue(a, 41); check(_submissions == 1, "Alliance registration retry respects saved retry date");
            Pursue(a, 43); Pursue(a, 46); Pursue(a, 49);
            check(_submissions == 3 && _spent == _refunded, "Alliance technical attempts are bounded and conserve influence");
            a = Agenda(); _throwAfter = true; Pursue(a); Pursue(a, 44);
            check(a.Dynastic.AllianceAttempted && _submissions == 1 && _spent == 200 && _refunded == 0,
                "A throwing subscriber after alliance registration cannot cause a refund or duplicate vote");
            a = Agenda(); _funded = false; Pursue(a);
            check(_spent == 0 && _submissions == 0 && !a.Dynastic.AllianceAttempted,
                "Insufficient alliance budget does not consume the term's attempt");
            a = Agenda(); _willing = false; Pursue(a);
            check(_spent == 0 && _submissions == 0 && a.Dynastic.AllianceBlocker == "no_funded_willing_participant",
                "No willing sponsor means no forced alliance vote");
            a = Agenda(); _legal = false; Pursue(a);
            check(_spent == 0 && !a.Dynastic.AllianceAttempted && a.Dynastic.AllianceBlocker == "native_or_foreign_acceptance_unavailable",
                "Native or foreign refusal blocks alliance payment without inventing consent");
            a = Agenda(41); Pursue(a);
            check(_submissions == 0 && a.Dynastic.AllianceBlocker == "insufficient_time_for_ballot",
                "Alliance ballot must finish within original term");
            a = Agenda(); a.State = CourtAgendaState.PursuingObjective; Pursue(a);
            check(_submissions == 0, "Alliance pursuit requires an already completed marriage objective");
            a = Agenda(); _valid = false; Pursue(a);
            check(_submissions == 0, "Changed royal house stops post-wedding alliance pursuit");
            a = Agenda(); _decisions.Add(new StartAllianceDecision(_sponsor, target)); Pursue(a);
            check(_submissions == 0 && a.Dynastic.AllianceOutcome == "existing_ballot", "Existing matching alliance ballot consumes follow-up without duplicate spending");
            a = Agenda(); a.State = CourtAgendaState.PursuingObjective; _available = false;
            check(Marry() && !a.Dynastic.NpcAttempted && _evaluations == 0, "Temporary captivity or engagement does not consume court marriage consideration");
            _available = true; _day = 41; Marry();
            check(_evaluations == 0, "Court marriage temporary blocker has a saved three-day retry interval");
            _day = 43; Marry();
            check(_evaluations == 1 && a.Dynastic.NpcAttempted && a.Dynastic.MarriageOutcome == "house_refused",
                "An unaligned NPC house still considers the match, and genuine refusal is recorded once");
            _day = 46; check(!Marry() && _evaluations == 1, "A genuinely refused court marriage is not reconsidered every day");
            a = Agenda(); a.State = CourtAgendaState.PursuingObjective; _match = null; Marry();
            check(!a.Dynastic.NpcAttempted && a.Dynastic.MarriageBlocker == "native_couple_unavailable",
                "Unavailable native match is not mislabeled as a house refusing the marriage");
            a = Agenda(); a.State = CourtAgendaState.PursuingObjective; _throwEvaluation = true; _married = false;
            Marry(); _day = 43; Marry(); _day = 46; Marry(); _day = 49; Marry();
            check(_evaluations == 3 && a.Dynastic.MarriageTechnicalAttempts == 3,
                "Repeated evaluation exceptions stop after three technical attempts");
            a = Agenda(); a.State = CourtAgendaState.PursuingObjective; _married = false;
            _match.GetType().GetProperty("SuitorAcceptance").SetValue(_match, 100f);
            _match.GetType().GetProperty("CandidateAcceptance").SetValue(_match, 100f);
            Marry(); _day = 44; Marry();
            check(_weddings == 1 && a.Dynastic.NpcAttempted && a.Dynastic.MarriageOutcome == "married",
                "Naturally willing unaligned NPC houses complete exactly one wedding without an annual random roll");
            a = Agenda(); a.State = CourtAgendaState.PursuingObjective; _married = false;
            _match.GetType().GetProperty("SuitorAcceptance").SetValue(_match, 100f);
            _match.GetType().GetProperty("CandidateAcceptance").SetValue(_match, 100f);
            a.Dynastic.Destination = Blank<Clan>();
            Marry();
            check(_weddings == 0 && a.State == CourtAgendaState.Cancelled && a.ResultApplied,
                "Court pursuit cancels changed household terms before any wedding rather than rewriting the agreement");
            a = Agenda(); a.State = CourtAgendaState.PursuingObjective; _married = false; _player = Blank<Clan>(); _offerReady = false;
            _match.GetType().GetProperty("SuitorAcceptance").SetValue(_match, 100f);
            Marry();
            check(!a.Dynastic.NpcAttempted && _evaluations == 0 && _offers == 0,
                "Foreign player offer cooldown preserves the court marriage attempt");
            _offerReady = true; _day = 43; Marry(); _day = 46; Marry();
            check(_offers == 1 && _weddings == 0 && a.Dynastic.NpcAttempted && a.Dynastic.Response == CourtDynasticResponse.OfferIssued,
                "A foreign player's house receives one consent-based offer, never an automatic wedding");
            a = Agenda(); a.State = CourtAgendaState.PursuingObjective; _married = false; _player = Blank<Clan>(); _offerSent = false;
            _match.GetType().GetProperty("SuitorAcceptance").SetValue(_match, 100f);
            Marry();
            check(!a.Dynastic.NpcAttempted && a.Dynastic.Response == CourtDynasticResponse.AwaitingApproach && _weddings == 0,
                "Unregistered player marriage offer restores the approach instead of sealing a nonexistent offer");
            check(typeof(CourtDynasticRecord).GetFields().All(f => f.IsDefined(typeof(TaleWorlds.SaveSystem.SaveableFieldAttribute), false)),
                "Dynastic attempt, outcome, blocker and retry timing are saved");
        }
        finally { h.UnpatchAll(h.Id); ticks.SetValue(null, oldTicks); }
    }
}
