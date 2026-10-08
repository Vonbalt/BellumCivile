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
using TaleWorlds.CampaignSystem.GameComponents;

internal static class CourtDynasticEngineTests
{
    private static double _day;
    private static bool _married, _valid, _support, _identity;
    private static bool Now(ref CampaignTime __result) { __result = CampaignTime.Days((float)_day); return false; }
    private static bool Days(ref double __result) { __result = _day; return false; }
    private static bool Married(ref bool __result) { __result = _married; return false; }
    private static bool Valid(ref bool __result) { __result = _valid; return false; }
    private static bool Identity(ref bool __result) { __result = _identity; return false; }
    private static bool Support(ref bool __result) { __result = _support; return false; }
    private static bool Skip() => false;

    internal static void Run(Action<bool, string> check)
    {
        T Blank<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
        var type = typeof(CourtAgendaBehavior);
        var realm = Blank<Kingdom>(); realm.StringId = "dynastic_home";
        var target = Blank<Kingdom>(); target.StringId = "dynastic_foreign";
        var first = Blank<Hero>(); var second = Blank<Hero>(); var voter = Blank<Clan>();
        var maintain = AccessTools.Method(type, "MaintainDynasticObjectives");
        var receipt = AccessTools.Method(type, "OnCourtMarriageCompleted");
        var bonus = AccessTools.Method(type, "DynasticAllianceBonus");
        var cancel = AccessTools.Method(type, "OnCourtMarriageOfferCancelled");
        CourtAgendaRecord Add(CourtAgendaBehavior b)
        {
            _day = 40; _married = false; _valid = _identity = _support = true;
            var a = new CourtAgendaRecord { Realm = realm, Faction = Blank<FactionObject>(), State = CourtAgendaState.PursuingObjective,
                Dynastic = new CourtDynasticRecord { Target = target, First = first, Second = second, Activated = true,
                    ActivatedDay = 20, Response = CourtDynasticResponse.Deferred },
                ObjectiveData = new CourtObjectiveRecord { Kind = "court_royal_marriage" } };
            AccessTools.Method(typeof(CourtObjectiveRecord), "FreezeTerm").Invoke(a.ObjectiveData, new object[] { 10d, 84d });
            ((List<CourtAgendaRecord>)AccessTools.Field(type, "_agendas").GetValue(b)).Add(a);
            return a;
        }
        float Bonus(CourtAgendaBehavior b, Kingdom other = null) => (float)bonus.Invoke(b, new object[] { realm, other ?? target, voter });
        void Tick(CourtAgendaBehavior b) => maintain.Invoke(b, null);
        var harmony = new Harmony("bellum.test.court_dynastic");
        void Patch(MethodBase method, string prefix) => harmony.Patch(method, prefix: new HarmonyMethod(typeof(CourtDynasticEngineTests), prefix));
        try
        {
            Patch(AccessTools.PropertyGetter(typeof(CampaignTime), "Now"), nameof(Now));
            Patch(AccessTools.PropertyGetter(typeof(CampaignTime), "ToDays"), nameof(Days));
            Patch(AccessTools.Method(type, "DynasticOwnerValid"), nameof(Valid));
            Patch(AccessTools.Method(type, "DynasticIdentity"), nameof(Identity));
            Patch(AccessTools.Method(type.Assembly.GetType("BellumCivile.MarriageHouseholdPolicy"), "Matches"), nameof(Identity));
            Patch(AccessTools.Method(type, "DynasticMarried"), nameof(Married));
            Patch(AccessTools.Method(type, "ReceivesPoliticalSupport"), nameof(Support));
            Patch(AccessTools.Method(type, "ReportDynastic"), nameof(Skip));
            Patch(AccessTools.Method(type, "PursueDynasticAlliance"), nameof(Skip));

            var b = new CourtAgendaBehavior(); var a = Add(b);
            check(Bonus(b) == 15 && Bonus(b, Blank<Kingdom>()) == 0, "Royal marriage preference is restricted to the named foreign realm");
            _support = false;
            check(Bonus(b) == 0, "Royal marriage does not compel an unaligned or player voter");
            _support = true;
            receipt.Invoke(b, new object[] { first, Blank<Hero>() });
            check(!a.ResultApplied, "Unrelated wedding cannot fulfill the court marriage objective");
            receipt.Invoke(b, new object[] { first, second });
            check(!a.ResultApplied, "Named receipt is insufficient until actual wedding and household are verified");
            _married = true;
            receipt.Invoke(b, new object[] { second, first });
            check(a.State == CourtAgendaState.Completed && a.Faction.Mood == 10, "Named wedding alone fulfills the objective, without a formal alliance");
            check(Bonus(b) == 15, "Alliance preference survives successful wedding through the original term");
            Tick(b); receipt.Invoke(b, new object[] { first, second });
            check(a.Faction.Mood == 10, "Post-action receipt and maintenance cannot duplicate marriage approval");
            _day = 85;
            check(Bonus(b) == 0, "Completed wedding does not extend alliance preference past term end");

            b = new CourtAgendaBehavior(); a = Add(b); a.Dynastic.Response = CourtDynasticResponse.Declined; _day = 85;
            Tick(b); Tick(b);
            check(a.ObjectiveData.State == CourtObjectiveState.Expired && a.Faction.Mood == -10,
                "Declining the proposal produces one ordinary expiry consequence, not immediate repeated punishment");
            b = new CourtAgendaBehavior(); a = Add(b); _identity = false;
            Tick(b);
            check(a.ObjectiveData.State == CourtObjectiveState.Cancelled && a.Faction.Mood == 0,
                "Changed spouse or household cancels the objective without blame");
            b = new CourtAgendaBehavior(); a = Add(b); _valid = false;
            Tick(b);
            check(a.ObjectiveData.State == CourtObjectiveState.Cancelled && a.Faction.Mood == 0,
                "A different sovereign house cancels the old marriage initiative");
            b = new CourtAgendaBehavior(); a = Add(b); _day = 85; _married = true;
            Tick(b);
            check(a.ObjectiveData.State == CourtObjectiveState.Expired,
                "Late observation cannot invent an in-term wedding or an alliance grace period");
            b = new CourtAgendaBehavior(); a = Add(b); a.Dynastic.Response = CourtDynasticResponse.OfferIssued;
            cancel.Invoke(b, new object[] { second, first });
            check(a.Dynastic.Response == CourtDynasticResponse.Declined && !a.ResultApplied,
                "Native offer cancellation closes reminders but leaves the term outcome pending");
            _married = true;
            receipt.Invoke(b, new object[] { first, second });
            check(a.State == CourtAgendaState.Completed,
                "Native pre-wedding offer cleanup cannot suppress the post-wedding success receipt");
            b = new CourtAgendaBehavior(); a = Add(b); a.Faction.Mood = 98; _married = true;
            receipt.Invoke(b, new object[] { first, second });
            check(a.Faction.Mood == 100, "Marriage approval respects the faction mood cap");

            var assembly = type.Assembly;
            foreach (string name in new[] { "CourtDynasticMarriageReceiptPatch", "CourtDynasticAllianceSupportPatch", "CourtDynasticMarriageRecognitionPatch" })
            {
                var patchType = assembly.GetType("BellumCivile.Patches." + name);
                var patched = harmony.CreateClassProcessor(patchType).Patch();
                check(patched?.Count > 0, "Installed native method accepts the royal-marriage Harmony patch: " + name);
            }
            check(AccessTools.Method(typeof(MarriageAction), "Apply").GetParameters().Select(p => p.Name)
                .SequenceEqual(new[] { "firstHero", "secondHero", "showNotification" }), "Marriage completion patch binds native final-action arguments");
        }
        finally { harmony.UnpatchAll(harmony.Id); }
    }
}
