using System;
using System.Linq;
using System.Reflection;
using BellumCivile;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;

internal static class CourtClaimTests
{
    internal static void Run(Action<bool, string> check)
    {
        foreach (int days in new[] { -1, 0, 1, 3, 7, 30 })
        foreach (double acquired in new[] { -1d, 9, 10, 50, 84, 84.1, double.NaN })
        foreach (double now in new[] { 83d, 84, 84.1, 85, 87, 91, 114, 115, double.NaN })
        foreach (bool pending in new[] { false, true })
            check(CourtClaimRules.CanGrace(acquired, 10, 84, pending, now, days)
                == (pending && acquired >= 10 && acquired <= 84 && now > 84 && now <= 84 + Math.Max(1, days)),
                "Allocation grace requires dated acquisition, pending allocation and a fixed end");
        var plan = new CourtClaimRecord { Fief = new Settlement(), Beneficiary = new Clan(), Target = new Kingdom(),
            ActivatedDay = 20, AcquiredDay = 83, PendingAllocation = true, GraceDays = 3 };
        check(!plan.AllocationWindow(30, 84), "No assistance before session activation");
        plan.Activated = true;
        foreach (double now in new[] { 19d, 20, 84, 84.1, 87, double.NaN })
            check(plan.AllocationWindow(now, 84) == (now >= 20 && now <= 84), "Original allocation support expires at term end");
        plan.GraceActive = true;
        plan.GraceUntil = CourtClaimRules.GraceEnd(84, plan.GraceDays);
        foreach (double now in new[] { 20d, 83, 84, 85, 87, 87.1, double.NaN })
        {
            check(!plan.InTerm(now, 84), "Grace never revives war or treaty assistance");
            check(plan.AllocationWindow(now, 84) == (now >= 84 && now <= 87), "Saved grace only permits allocation during fixed window");
        }
        var fields = typeof(CourtClaimRecord).GetFields();
        var ids = fields.Select(f => f.GetCustomAttribute<TaleWorlds.SaveSystem.SaveableFieldAttribute>()?.Id).ToList();
        check(fields.Length == 13 && ids.All(i => i.HasValue) && ids.Distinct().Count() == 13, "Claim record has unique appended save fields");
        var loaded = new CourtClaimRecord();
        foreach (var field in fields) field.SetValue(loaded, field.GetValue(plan));
        check(loaded.Fief == plan.Fief && loaded.Beneficiary == plan.Beneficiary && loaded.AcquiredDay == 83
            && loaded.GraceUntil == 87 && loaded.AllocationWindow(85, 84) && !loaded.InTerm(85, 84), "Field roundtrip preserves claimant identity and grace without extending it");
        var agenda = new CourtAgendaRecord { State = CourtAgendaState.PursuingObjective, Claim = plan,
            ObjectiveData = new CourtObjectiveRecord { Kind = CourtClaimRules.Kind } };
        agenda.ObjectiveData.FreezeTerm(10, 84);
        check(CourtAgendaPresentation.Deadline(agenda)?.date.ToDays == 84, "Original agenda term never moves to grace deadline");
        check(CourtAgendaPresentation.Status(agenda.State, claimObjective: true).Contains("claim"), "Claim objective has its own status");
        check(agenda.ObjectiveData.Finish(CourtObjectiveState.Succeeded, CourtObjectiveCredit.Sponsor, "award")
            && !agenda.ObjectiveData.Finish(CourtObjectiveState.Expired, CourtObjectiveCredit.None, "late"), "Success cannot be overwritten by expiry");
        check(agenda.ObjectiveData.TryClaimResult() && !agenda.ObjectiveData.TryClaimResult(), "Claim mood result applies once");
    }
}
