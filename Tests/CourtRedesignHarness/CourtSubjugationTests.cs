using System;
using System.Linq;
using System.Reflection;
using BellumCivile;
using TaleWorlds.CampaignSystem;

internal static class CourtSubjugationTests
{
    internal static void Run(Action<bool, string> check)
    {
        foreach (int cost in new[] { 0, 1, 100, 149, 150, 151 })
        foreach (int fiefs in new[] { 0, 1, 5 })
        foreach (float enemy in new[] { -1f, 0, 500, 999, 1000, 1500, float.NaN })
            check(CourtSubjugationRules.CanSelect(fiefs, cost, 1000, enemy)
                == (fiefs > 0 && cost > 0 && cost < 150 && enemy >= 0 && enemy < 1000),
                "Clientage discovery respects strict valuation and strength limits");
        var target = new Kingdom();
        var plan = new CourtSubjugationRecord { Target = target, ActivatedDay = 11 };
        check(plan.ActiveBonus(target, 20, 84) == 0, "No subjugation bonus before the session");
        plan.Activated = true;
        foreach (double now in new[] { 10d, 11, 20, 84, 85, double.NaN, double.PositiveInfinity })
            check(plan.ActiveBonus(target, now, 84) == (now >= 11 && now <= 84 ? 15 : 0), "Saved subjugation window controls support");
        check(plan.ActiveBonus(new Kingdom(), 20, 84) == 0 && plan.ActiveBonus(null, 20, 84) == 0, "Target identity is exact");
        foreach (double started in new[] { 9d, 10, 11, 84, 85, double.NaN })
        foreach (string client in new[] { "target", "other", null })
        foreach (string ruler in new[] { "realm", "other", null })
            check(CourtSubjugationRules.Fulfilled(client, ruler, "target", "realm", started, 10, 84)
                == (client == "target" && ruler == "realm" && started >= 10 && started <= 84),
                "Only dated clientage of the exact realm pair fulfills the objective");
        var fields = typeof(CourtSubjugationRecord).GetFields();
        var ids = fields.Select(f => f.GetCustomAttribute<TaleWorlds.SaveSystem.SaveableFieldAttribute>()?.Id).ToList();
        check(fields.Length == 4 && ids.All(i => i.HasValue) && ids.Distinct().Count() == 4, "Clientage record retains unique save IDs");
        var loaded = new CourtSubjugationRecord();
        foreach (var field in fields) field.SetValue(loaded, field.GetValue(plan));
        check(loaded.ActiveBonus(target, 20, 84) == 15, "Saved clientage plan roundtrips without reactivation");
        var agenda = new CourtAgendaRecord { State = CourtAgendaState.PursuingObjective, Subjugation = plan,
            ObjectiveData = new CourtObjectiveRecord { Kind = CourtSubjugationRules.Kind } };
        agenda.ObjectiveData.FreezeTerm(10, 84);
        check(CourtAgendaPresentation.Deadline(agenda)?.date.ToDays == 84, "Clientage uses original term deadline");
        check(CourtAgendaPresentation.Status(agenda.State, subjugationObjective: true).Contains("submission"), "Clientage has its own status");
        check(agenda.ObjectiveData.Finish(CourtObjectiveState.Succeeded, CourtObjectiveCredit.Sponsor, "clientage")
            && !agenda.ObjectiveData.Finish(CourtObjectiveState.Expired, CourtObjectiveCredit.None, "late"), "Expiry cannot overwrite clientage success");
        check(agenda.ObjectiveData.TryClaimResult() && !agenda.ObjectiveData.TryClaimResult(), "Clientage reward receipt is claimed once");
    }
}
