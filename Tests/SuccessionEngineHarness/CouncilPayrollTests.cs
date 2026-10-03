using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using BellumCivile;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Localization;

internal static class CouncilPayrollTests
{
    private static Kingdom _realm;
    private static Clan _crown, _first, _second;
    private static Hero _hero;
    private static int _gold;
    private static float _day;
    private static bool _self, _vacant;
    private static List<PrivyCouncilOfficeRecord> _seats;
    private static bool Now(ref CampaignTime __result) { __result = CampaignTime.Days(_day); return false; }
    private static bool Yes(ref bool __result) { __result = true; return false; }
    private static bool No(ref bool __result) { __result = false; return false; }
    private static bool Realm(ref Kingdom __result) { __result = _realm; return false; }
    private static bool Crown(ref Clan __result) { __result = _crown; return false; }
    private static bool Leader(ref Hero __result) { __result = _hero; return false; }
    private static bool Gold(Clan __instance, ref int __result) { __result = __instance == _crown ? _gold : 0; return false; }
    private static bool Seats(ref IReadOnlyList<PrivyCouncilOfficeRecord> __result) { __result = _seats; return false; }
    private static bool Holder(PrivyCouncilOffice office, ref Clan __result)
    { __result = _vacant ? null : _self ? _crown : office == PrivyCouncilOffice.Marshal ? _first : _second; return false; }
    private static bool Competence(ref float __result) { __result = 65; return false; }
    private static bool Label(CouncilSalaryCredit credit, ref TextObject __result)
    { __result = new TextObject("{=!}Council salary: " + credit.Office); return false; }
    internal static void Run(Action<bool, string> check)
    {
        T Blank<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
        var h = new Harmony("bellum.test.council_payroll");
        void Patch(MethodBase m, string name) => h.Patch(m, prefix: new HarmonyMethod(typeof(CouncilPayrollTests), name));
        var type = typeof(PrivyCouncilBehavior);
        var ticks = AccessTools.Field(typeof(CampaignTime), "TimeTicksPerDay"); var oldTicks = ticks.GetValue(null);
        try
        {
            ticks.SetValue(null, 1000L);
            _realm = Blank<Kingdom>(); _realm.StringId = "realm";
            _crown = Blank<Clan>(); _first = Blank<Clan>(); _second = Blank<Clan>(); _hero = Blank<Hero>();
            _day = 10; _gold = 1000; _self = _vacant = false;
            _seats = new List<PrivyCouncilOfficeRecord> {
                new PrivyCouncilOfficeRecord("realm", PrivyCouncilOffice.Marshal, 0),
                new PrivyCouncilOfficeRecord("realm", PrivyCouncilOffice.Chancellor, 0) };
            Patch(AccessTools.PropertyGetter(typeof(CampaignTime), "Now"), nameof(Now));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "Kingdom"), nameof(Realm));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "Leader"), nameof(Leader));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "Gold"), nameof(Gold));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "IsEliminated"), nameof(No));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "PlayerClan"), nameof(Crown));
            Patch(AccessTools.PropertyGetter(typeof(Kingdom), "RulingClan"), nameof(Crown));
            Patch(AccessTools.Method(type, "IsEligiblePermanentRealm"), nameof(Yes));
            Patch(AccessTools.Method(type, "GetOfficeRecords"), nameof(Seats));
            Patch(AccessTools.Method(type, "GetOfficeHolder"), nameof(Holder));
            Patch(AccessTools.Method(type, "CalculateCompetence"), nameof(Competence));
            Patch(AccessTools.Method(type, "SalaryFinanceLabel"), nameof(Label));
            var b = new PrivyCouncilBehavior();
            float Finance(string method, Clan clan, bool apply, float initial = 0)
            {
                object[] args = { clan, new ExplainedNumber(initial, true), apply };
                AccessTools.Method(type, method).Invoke(b, args);
                return ((ExplainedNumber)args[1]).ResultNumber;
            }
            List<CouncilSalaryCredit> Credits() => (List<CouncilSalaryCredit>)AccessTools.Field(type, "_salaryCredits").GetValue(b);
            check(b.GetDailySalary(_realm, PrivyCouncilOffice.Marshal) == 625, "Salary keeps competence-scaled nominal rate");
            check(Finance("AddCouncilSalaryExpenses", _crown, false) == -1000 && Credits().Count == 0,
                "Payroll preview is cash-capped and read-only");
            check(Finance("AddCouncilSalaryIncome", _first, false) == 625 && Credits().Count == 0,
                "Recipient preview includes expected salary without funding it");
            check(Finance("AddCouncilSalaryIncome", _first, true) == 0, "Recipient cannot collect money before Crown funds it");
            check(Finance("AddCouncilSalaryExpenses", _crown, true) == -1000 && Credits().Sum(c => c.Amount) == 1000,
                "Crown expense exactly funds matching salary credits");
            check(Credits()[0].Amount == 625 && Credits()[1].Amount == 375, "Limited treasury pays partial later salary without creating gold");
            check(Finance("AddCouncilSalaryExpenses", _crown, true) == 0 && Credits().Sum(c => c.Amount) == 1000,
                "Repeated settlement cannot fund payroll twice in one day");
            check(Finance("AddCouncilSalaryIncome", _second, true) == 375 && Finance("AddCouncilSalaryIncome", _first, true) == 625,
                "Recipients collect conserved credits in either order");
            check(Finance("AddCouncilSalaryIncome", _first, true) == 0 && Credits().Count == 0,
                "Collected wages cannot be paid twice");
            _day = 11;
            check(Finance("AddCouncilSalaryExpenses", _crown, true, -900) == -1000 && Credits().Sum(c => c.Amount) == 100,
                "Other expenses leave only remaining cash for payroll");
            var restored = new PrivyCouncilBehavior();
            AccessTools.Field(type, "_salaryCredits").SetValue(restored, Credits().Select(c => new CouncilSalaryCredit
                { Realm = c.Realm, Recipient = c.Recipient, Office = c.Office, Amount = c.Amount }).ToList());
            AccessTools.Field(type, "_payrollDay").SetValue(restored, new Dictionary<string, float>(
                (Dictionary<string, float>)AccessTools.Field(type, "_payrollDay").GetValue(b)));
            b = restored; _vacant = true;
            check(Finance("AddCouncilSalaryExpenses", _crown, true) == 0 && Finance("AddCouncilSalaryIncome", _first, true) == 100,
                "Restored receipts prevent duplicate Crown expenses and retain wages earned before dismissal");
            _vacant = false; _self = true; _day = 12;
            check(b.GetDailySalary(_realm, PrivyCouncilOffice.Marshal) == 0 && Finance("AddCouncilSalaryExpenses", _crown, false) == 0,
                "Self-held council offices show no salary and incur no payroll");
            _self = false; _gold = 0;
            check(Finance("AddCouncilSalaryExpenses", _crown, true) == 0 && Credits().Count == 0,
                "Empty treasury creates no wages or unfunded arrears");
            check(AccessTools.Method(type, "PayDailyCouncilSalary") == null, "Old direct-transfer payment path is removed");
        }
        finally { h.UnpatchAll(h.Id); ticks.SetValue(null, oldTicks); }
    }
}
