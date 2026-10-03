using System;
using System.Linq;
using System.Reflection;
using BellumCivile;
using TaleWorlds.CampaignSystem;

internal static class CourtPeaceTests
{
    internal static void Run(Action<bool, string> check)
    {
        var member = new Clan();
        var war = new WarScoreRecord();
        var plan = new CourtPeaceRecord { War = war, ActivatedDay = 11 };
        plan.Members.Add(member);
        check(plan.BonusFor(war, member, 20, 84) == 0, "No peace bonus before activation");
        plan.Activated = true;
        check(plan.ActiveBonus(war, 20, 84) == 15, "Eligible Crown uses active peace magnitude without membership");
        check(plan.ActiveBonus(war, 85, 84) == 0 && plan.ActiveBonus(new WarScoreRecord(), 20, 84) == 0,
            "Crown peace bonus cannot bypass expiry or war identity");
        foreach (double day in new[] { 10d, 11, 20, 84, 84.001, double.NaN, double.PositiveInfinity })
            check(plan.BonusFor(war, member, day, 84) == (day >= 11 && day <= 84 ? 15 : 0), "Peace bonus respects the saved window: " + day);
        check(plan.BonusFor(new WarScoreRecord(), member, 20, 84) == 0, "Later war against same realm cannot inherit the bonus");
        check(plan.BonusFor(null, member, 20, 84) == 0, "No missing-war bonus");
        check(plan.BonusFor(war, new Clan(), 20, 84) == 0, "New faction member is not in the session snapshot");
        check(plan.BonusFor(war, null, 20, 84) == 0, "No missing-voter bonus");
        var loaded = new CourtPeaceRecord();
        foreach (var field in typeof(CourtPeaceRecord).GetFields()) field.SetValue(loaded, field.GetValue(plan));
        check(loaded.BonusFor(war, member, 20, 84) == 15, "Saved fields reconstruct active bonus without reactivation");
        loaded.Members.Remove(member);
        check(loaded.BonusFor(war, member, 20, 84) == 0, "Pruned member loses bonus");
        var ids = typeof(CourtPeaceRecord).GetFields().Select(f => f.GetCustomAttribute<TaleWorlds.SaveSystem.SaveableFieldAttribute>()?.Id).ToList();
        check(ids.Count == 5 && ids.All(i => i.HasValue) && ids.Distinct().Count() == 5, "Peace record saves all fields with unique IDs");
        foreach (double term in new[] { 6d, 21, 24, 84, 168, 336 })
        {
            int nomination = CourtAgendaRules.NominationDays(term);
            double session = CourtPeaceRules.Session(100, nomination, 100, 100 + term - 1);
            check(session == 100 + nomination + 1 && session < 100 + term, "Early peace session retains nomination and time to act");
            double late = CourtPeaceRules.Session(100, nomination, 100 + term - 3, 100 + term - 1);
            check(late >= 100 + term - 2 && late <= 100 + term - 1, "Late substitution never schedules a retroactive session");
        }
        var agenda = new CourtAgendaRecord { State = CourtAgendaState.PursuingObjective,
            ObjectiveData = new CourtObjectiveRecord { Kind = CourtPeaceRules.Kind } };
        agenda.ObjectiveData.FreezeTerm(0, 84);
        check(agenda.IsOngoingObjective && !agenda.IsFiled && !agenda.IsUnopened, "Ongoing objective is not a queued ballot");
        check(CourtAgendaPresentation.Deadline(agenda)?.date.ToDays == 84, "Active peace agenda displays saved term deadline");
        check(CourtAgendaPresentation.Status(agenda.State).Contains("pursuing settlement"), "Active objective has its own label");
        check(agenda.ObjectiveData.Finish(CourtObjectiveState.Succeeded, CourtObjectiveCredit.Sponsor, "peace"), "Peace concludes once");
        check(!agenda.ObjectiveData.Finish(CourtObjectiveState.Expired, CourtObjectiveCredit.None, "late"), "Expiry cannot replace peace success");
        check(agenda.ObjectiveData.TryClaimResult() && !agenda.ObjectiveData.TryClaimResult(), "Peace mood reward receipt is one-shot");
        check((int)CourtAgendaState.Completed == 17 && (int)CourtAgendaState.PursuingObjective == 18, "New agenda state preserves previous saved enum values");
    }
}
