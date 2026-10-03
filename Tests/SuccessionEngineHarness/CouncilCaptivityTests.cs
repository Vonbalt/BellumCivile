using System;
using System.Linq;
using BellumCivile;
using BellumCivile.Behaviors;
using HarmonyLib;

internal static class CouncilCaptivityTests
{
    internal static void Run(Action<bool, string> check)
    {
        var severity = AccessTools.Method(typeof(PrivyCouncilBehavior), "DismissalSeverity");
        int Severity(float value, bool captive) => (int)severity.Invoke(null, new object[] { value, captive });
        for (int value = 0; value <= 100; value++)
        {
            int normal = Math.Min(25, Math.Max(1, (int)Math.Round(value / 4f)));
            check(Severity(value, false) == normal, "Normal dismissal severity retained: " + value);
            check(Severity(value, true) == (normal + 1) / 2, "Captivity grievance halves existing severity, rounding up: " + value);
        }
        check(Severity(0, true) == 1 && Severity(100, true) == 13, "Captivity grievance range is 1 to 13");
        var eligible = AccessTools.Method(typeof(PrivyCouncilBehavior), "ShouldRelieveCaptive");
        foreach (bool npc in new[] { false, true })
        foreach (bool captive in new[] { false, true })
            check((bool)eligible.Invoke(null, new object[] { npc, captive }) == (npc && captive),
                "Only NPC rulers relieve currently captive holders: " + npc + "/" + captive);

        foreach (PrivyCouncilOffice office in Enum.GetValues(typeof(PrivyCouncilOffice)))
        {
            var record = new PrivyCouncilOfficeRecord("realm", office, 0);
            record.Appoint("former", 1);
            record.SetControversy(80, "test", 2);
            AccessTools.Method(typeof(PrivyCouncilOfficeRecord), "VacateForCaptivity").Invoke(record, new object[] { 3f });
            check(record.HolderClanId == "" && record.IsCaptivityVacancy && record.Controversy == 80,
                "Captivity vacates without prematurely resetting controversy: " + office);
            record.Appoint("replacement", 4);
            check(!record.IsCaptivityVacancy && record.Controversy == 0 && record.LastReason == "" && record.LastChange == 0,
                "Replacement clears urgency and predecessor controversy: " + office);
            record.Vacate(5);
            check(!record.IsCaptivityVacancy, "Later ordinary vacancy does not inherit captivity priority: " + office);
        }
        var marker = AccessTools.Field(typeof(PrivyCouncilOfficeRecord), "_captivityVacancy");
        check(marker.GetCustomAttributes(false).Any(a => a.GetType().Name == "SaveableFieldAttribute"),
            "Urgent captivity vacancy is saved");
        var replace = AccessTools.Method(typeof(CourtAgendaBehavior), "CanReplaceForCaptivity");
        bool CanReplace(CourtAgendaRecord a) => (bool)replace.Invoke(null, new object[] { a });
        check(CanReplace(null), "Urgent appointment can create a missing Crown agenda");
        foreach (var state in new[] { CourtAgendaState.Announced, CourtAgendaState.NotProposed, CourtAgendaState.Passed,
            CourtAgendaState.Defeated, CourtAgendaState.Cancelled })
            check(CanReplace(new CourtAgendaRecord { State = state }), "Urgent appointment replaces available business: " + state);
        foreach (var state in new[] { CourtAgendaState.Deliberating, CourtAgendaState.Voting, CourtAgendaState.PursuingObjective, CourtAgendaState.Crisis })
            check(!CanReplace(new CourtAgendaRecord { State = state }), "Urgent appointment respects active proceedings: " + state);
        check(!CanReplace(new CourtAgendaRecord { State = CourtAgendaState.Announced, PaidInfluence = 100 }),
            "Paid business is not silently discarded");
        check(!CanReplace(new CourtAgendaRecord { State = CourtAgendaState.Announced, CrisisInterventionPending = true }),
            "Existing crisis intervention is preserved");
        var marshal = new PrivyCouncilOfficeRecord("realm", PrivyCouncilOffice.Marshal, 0);
        var seneschal = new PrivyCouncilOfficeRecord("realm", PrivyCouncilOffice.Seneschal, 0);
        var spymaster = new PrivyCouncilOfficeRecord("realm", PrivyCouncilOffice.Spymaster, 0);
        var ordinary = new PrivyCouncilOfficeRecord("realm", PrivyCouncilOffice.Chancellor, 0);
        foreach (var pair in new[] { (marshal, 30f), (seneschal, 10f), (spymaster, 20f) })
            AccessTools.Method(typeof(PrivyCouncilOfficeRecord), "VacateForCaptivity").Invoke(pair.Item1, new object[] { pair.Item2 });
        var order = AccessTools.Method(typeof(CourtAgendaBehavior), "OrderCaptivityVacancies");
        PrivyCouncilOfficeRecord[] Ordered() => ((System.Collections.Generic.List<PrivyCouncilOfficeRecord>)order.Invoke(null,
            new object[] { new[] { spymaster, ordinary, seneschal, marshal } })).ToArray();
        check(Ordered().SequenceEqual(new[] { marshal, seneschal, spymaster }), "Marshal is first, remaining captivity vacancies ordered by age; ordinary vacancies excluded");
        marshal.Appoint("new_marshal", 31);
        check(Ordered().SequenceEqual(new[] { seneschal, spymaster }), "After marshal appointment, oldest remaining captivity vacancy becomes next");
        seneschal.Appoint("new_seneschal", 32);
        check(Ordered().SequenceEqual(new[] { spymaster }), "Sequential appointments retain the final pending vacancy");
        spymaster.Appoint("new_spymaster", 33);
        check(Ordered().Length == 0, "Queue drains only when every captivity vacancy has been filled");
    }
}
