using System;
using System.Linq;
using System.Reflection;
using BellumCivile;
using TaleWorlds.CampaignSystem;

internal static class CourtDynasticTests
{
    internal static void Run(Action<bool, string> check)
    {
        var first = new Hero(); var second = new Hero();
        var plan = new CourtDynasticRecord { First = first, Second = second, ActivatedDay = 20,
            Response = CourtDynasticResponse.Deferred, ReplyDay = 25, NpcAttempted = true,
            NextMarriageAttemptDay = 43, MarriageTechnicalAttempts = 2, MarriageOutcome = "player_offer_issued",
            MarriageBlocker = "temporary", AllianceAttempted = true, NextAllianceAttemptDay = 46,
            AllianceTechnicalAttempts = 1, AllianceOutcome = "ballot_rejected", AllianceBlocker = "native_opposed" };
        check(plan.Matches(first, second) && plan.Matches(second, first) && !plan.Matches(first, null)
            && !plan.Matches(first, new Hero()), "Marriage receipt requires the exact named pair in either order");
        check(!plan.InTerm(30, 84), "No marriage assistance before activation");
        plan.Activated = true;
        foreach (double day in new[] { 19d, 20, 83, 84, 84.1, double.NaN })
            check(plan.InTerm(day, 84) == (day >= 20 && day <= 84), "Marriage assistance has a fixed term boundary");
        foreach (bool player in new[] { false, true })
        foreach (bool aligned in new[] { false, true })
        foreach (bool active in new[] { false, true })
            check(CourtDynasticRules.AcceptanceBonus(player, aligned, active) == (!player && aligned && active ? 15 : 0),
                "Marriage consideration belongs only to an aligned NPC Crown during the objective");
        for (int score = -100; score <= 100; score++)
        {
            check(CourtDynasticRules.Support(score, true) == score + 15 && CourtDynasticRules.Support(score, false) == score,
                "Alliance preference is additive, never a guaranteed positive vote");
            check((score + CourtDynasticRules.AcceptanceBonus(false, true, true) >= 95) == (score >= 80),
                "Aligned marriage consideration shifts 95 acceptance threshold to 80");
        }
        foreach (CourtDynasticResponse response in Enum.GetValues(typeof(CourtDynasticResponse)))
            check(CourtDynasticRules.MayConsider(response) == (response != CourtDynasticResponse.Declined
                && response != CourtDynasticResponse.ForeignRefused && response != CourtDynasticResponse.OfferIssued),
                "Declined and issued offers cannot generate repeated approaches");
        var fields = typeof(CourtDynasticRecord).GetFields();
        var ids = fields.Select(f => f.GetCustomAttribute<TaleWorlds.SaveSystem.SaveableFieldAttribute>()?.Id).ToList();
        check(fields.Length == 22 && ids.All(i => i.HasValue) && ids.Distinct().Count() == 22, "Dynastic save fields are unique");
        var loaded = new CourtDynasticRecord();
        foreach (var field in fields) field.SetValue(loaded, field.GetValue(plan));
        check(loaded.Matches(first, second) && loaded.Response == CourtDynasticResponse.Deferred && loaded.NpcAttempted
            && loaded.ReplyDay == 25 && loaded.MarriageDay == -1 && loaded.InTerm(84, 84), "Saved fields preserve identity, deferral and court attempt receipt");
        check(loaded.NextMarriageAttemptDay == 43 && loaded.MarriageTechnicalAttempts == 2 && loaded.MarriageOutcome == "player_offer_issued"
            && loaded.MarriageBlocker == "temporary" && loaded.AllianceAttempted && loaded.NextAllianceAttemptDay == 46
            && loaded.AllianceTechnicalAttempts == 1 && loaded.AllianceOutcome == "ballot_rejected" && loaded.AllianceBlocker == "native_opposed",
            "Saved field copy preserves bounded pursuit receipts and diagnostics");
        var agenda = new CourtAgendaRecord { State = CourtAgendaState.PursuingObjective, Dynastic = plan,
            ObjectiveData = new CourtObjectiveRecord { Kind = CourtDynasticRules.Kind } };
        agenda.ObjectiveData.FreezeTerm(10, 84);
        check(CourtAgendaPresentation.Deadline(agenda)?.date.ToDays == 84, "Royal marriage uses original term deadline");
        check(CourtAgendaPresentation.Status(agenda.State, dynasticObjective: true).Contains("royal match"), "Royal marriage has an explicit agenda status");
    }
}
