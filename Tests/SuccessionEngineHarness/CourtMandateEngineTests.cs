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

internal static class CourtMandateEngineTests
{
    private static CourtAgendaBehavior _court;
    private static RealmLawBehavior _laws;
    private static RealmLawSelectionRecord _record;
    private static Kingdom _realm;
    private static Hero _leader;
    private static Clan _clan;
    private static bool _valid, _future, _allowed;
    private static int _resolved, _cancelled;
    private static bool Current(ref CourtAgendaBehavior __result) { __result = _court; return false; }
    private static bool Laws(ref RealmLawBehavior __result) { __result = _laws; return false; }
    private static bool Record(ref RealmLawSelectionRecord __result) { __result = _record; return false; }
    private static bool Valid(ref bool __result) { __result = _valid; return false; }
    private static bool Allowed(ref bool __result) { __result = _allowed; return false; }
    private static bool Future(ref bool __result) { __result = _future; return false; }
    private static bool Yes(ref bool __result) { __result = true; return false; }
    private static bool No(ref bool __result) { __result = false; return false; }
    private static bool Leader(ref Hero __result) { __result = _leader; return false; }
    private static bool Main(ref Hero __result) { __result = null; return false; }
    private static bool Clan(ref Clan __result) { __result = _clan; return false; }
    private static bool Realm(ref Kingdom __result) { __result = _realm; return false; }
    private static bool Resolve() { _resolved++; return false; }
    private static bool Cancel() { _cancelled++; return false; }
    internal static void Run(Action<bool,string> check)
    {
        T Blank<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
        var type = typeof(CourtAgendaBehavior);
        var h = new Harmony("bellum.test.court_mandate");
        void Patch(MethodBase method, string name) => h.Patch(method, prefix: new HarmonyMethod(typeof(CourtMandateEngineTests), name));
        void Field(object target, string name, object value) => AccessTools.Field(target.GetType(), name).SetValue(target, value);
        object Call(string name, params object[] args) => AccessTools.Method(type, name).Invoke(_court, args);
        var registry = RealmLawRegistry.Instance;
        string old = registry.ForTerm(5).Id, next = registry.ForTerm(10).Id;
        MandateReformDecision Decision()
        {
            var d = Blank<MandateReformDecision>();
            Field(d, "OldLaw", old); Field(d, "NewLaw", next); Field(d, "MotionId", "one"); Field(d, "Direction", 1);
            return d;
        }
        try
        {
            _court = new CourtAgendaBehavior(); _laws = new RealmLawBehavior(); _realm = Blank<Kingdom>();
            _clan = Blank<Clan>(); _leader = Blank<Hero>(); _valid = _future = _allowed = true;
            Patch(AccessTools.PropertyGetter(type, "Current"), nameof(Current));
            Patch(AccessTools.PropertyGetter(type.Assembly.GetType("BellumCivile.CourtMandateObjectiveSource"), "Laws"), nameof(Laws));
            Patch(AccessTools.Method(typeof(RealmLawBehavior), "GetOrInitialize"), nameof(Record));
            Patch(AccessTools.Method(type, "MandateRealmReady"), nameof(Valid));
            Patch(AccessTools.Method(type, "MandateLawUnchanged"), nameof(Valid));
            Patch(AccessTools.Method(type, "Eligible"), nameof(Yes));
            Patch(AccessTools.Method(type, "ValidateMandateDecision"), nameof(Allowed));
            Patch(AccessTools.Method(type, "ConcludeMandate"), nameof(Resolve));
            Patch(AccessTools.Method(type, "CancelMandateDecision"), nameof(Cancel));
            Patch(AccessTools.PropertyGetter(typeof(CampaignTime), "IsFuture"), nameof(Future));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "Leader"), nameof(Leader));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "PlayerClan"), nameof(Clan));
            Patch(AccessTools.PropertyGetter(typeof(Hero), "Clan"), nameof(Clan));
            Patch(AccessTools.PropertyGetter(typeof(Hero), "MainHero"), nameof(Main));
            Patch(AccessTools.PropertyGetter(typeof(Hero), "IsDead"), nameof(No));
            Patch(AccessTools.PropertyGetter(typeof(Hero), "IsPrisoner"), nameof(No));
            Patch(AccessTools.PropertyGetter(typeof(KingdomDecision), "Kingdom"), nameof(Realm));
            Patch(AccessTools.Method(type.Assembly.GetType("BellumCivile.Behaviors.DeliberationDialogueHelper"), "IsConversationInArmy"), nameof(No));
            var a = new CourtAgendaRecord { Realm = _realm, State = CourtAgendaState.Deliberating,
                ObjectiveData = new CourtObjectiveRecord { Kind = "mandate_reform" },
                Mandate = new CourtMandateRecord { Id = "one", OldLaw = old, NewLaw = next, Direction = 1, Decision = Decision() } };
            ((List<CourtAgendaRecord>)AccessTools.Field(type, "_agendas").GetValue(_court)).Add(a);
            check((bool)Call("CommitMandate", "one", _leader, true, false, true), "Failed persuasion recorded on named ballot");
            check(!(bool)Call("CommitMandate", "one", _leader, true, false, false), "Failed persuasion blocks repeat attempt");
            check((bool)Call("CommitMandate", "one", _leader, false, true, false), "Eligible bribery can follow failed persuasion");
            check(!(bool)Call("CommitMandate", "one", _leader, true, true, false), "Already committed vote cannot be repurchased");
            check(a.Mandate.Decision.DetermineSupport(_clan, new MandateReformDecision.ReformOutcome(false, old)) == 10000,
                "Native decision consumes promised retention outcome");
            a.State = CourtAgendaState.Voting; _future = false;
            check(Call("MandatePledge", "one", _clan) != null, "Filing handoff preserves saved promise");
            check(!(bool)Call("CommitMandate", "one", _leader, true, true, false), "New lobbying closed after deliberation");
            check(Call("MandatePledge", "two", _clan) == null, "Promise cannot cross ballot identity");
            _valid = false; check(Call("MandatePledge", "one", _clan) == null, "Invalid political/law context disables promise"); _valid = true;
            var original = _leader; _leader = Blank<Hero>();
            check(Call("MandatePledge", "one", _clan) == null, "Replacement clan leader is not bound"); _leader = original;
            Call("ClearMandatePledges", _clan, null);
            check(a.Mandate.Pledges.Count == 0, "Departure permanently clears commitments even if clan returns");

            _record = new RealmLawSelectionRecord { RealmId = "test" };
            foreach (string group in registry.Groups) _record.SelectedLaws[group] = registry.DefaultFor(group);
            _record.SelectedLaws[RealmLawRegistry.TermGroup] = old;
            var decision = Decision();
            _allowed = false;
            decision.ApplyChosenOutcome(new MandateReformDecision.ReformOutcome(true, next));
            check(_cancelled == 1 && _record.SelectedLaws[RealmLawRegistry.TermGroup] == old, "Invalid ballot cannot enact a law");
            _allowed = true; decision = Decision();
            decision.ApplyChosenOutcome(new MandateReformDecision.ReformOutcome(true, next));
            check(_resolved == 1 && _record.SelectedLaws[RealmLawRegistry.TermGroup] == next, "Authorized ballot replaces term law through guarded store");
            decision.ApplyChosenOutcome(new MandateReformDecision.ReformOutcome(false, old));
            check(_resolved == 1 && _record.SelectedLaws[RealmLawRegistry.TermGroup] == next, "Repeated callback cannot undo law or repeat result");
            var copy = Decision();
            foreach (var field in typeof(MandateReformDecision).GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                field.SetValue(copy, field.GetValue(decision));
            copy.ApplyChosenOutcome(new MandateReformDecision.ReformOutcome(true, next));
            check(_resolved == 1, "Restored decision receipt does not replay result");
            var stale = Decision(); stale.ApplyChosenOutcome(new MandateReformDecision.ReformOutcome(true, next));
            check(_cancelled == 2 && _resolved == 1, "Changed expected law cannot be committed again");
            _record.SelectedLaws[RealmLawRegistry.TermGroup] = old;
            var retained = Decision(); retained.ApplyChosenOutcome(new MandateReformDecision.ReformOutcome(false, old));
            check(_resolved == 2 && _record.SelectedLaws[RealmLawRegistry.TermGroup] == old, "Retention resolves without rewriting law");
            foreach (var recordType in new[] { typeof(CourtMandateRecord), typeof(CourtMandatePledge), typeof(MandateReformDecision), typeof(MandateReformDecision.ReformOutcome) })
            {
                var fields = recordType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                check(fields.All(f => f.GetCustomAttributesData().Any(x => x.AttributeType.Name == "SaveableFieldAttribute")), "Mandate runtime receipt fields persist: " + recordType.Name);
            }
        }
        finally { h.UnpatchAll(h.Id); }
    }
}
