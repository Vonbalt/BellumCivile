using System;
using System.Linq;
using System.Reflection;
using BellumCivile;
using TaleWorlds.CampaignSystem;

internal static class CourtCampaignTests
{
    internal static void Run(Action<bool, string> check)
    {
        foreach (bool player in new[] { false, true })
        foreach (bool ruler in new[] { false, true })
        foreach (bool aligned in new[] { false, true })
        foreach (bool currentMember in new[] { false, true })
        foreach (bool sessionMember in new[] { false, true })
            check(CourtAgendaRules.ReceivesPoliticalSupport(player, ruler, aligned, currentMember, sessionMember)
                == (!player && ((ruler && aligned) || (!ruler && currentMember && sessionMember))),
                "Political support respects Crown alignment, session membership and player agency");
        var target = new Kingdom();
        var member = new Clan();
        var plan = new CourtCampaignRecord { Target = target, ActivatedDay = 11 };
        plan.Members.Add(member);
        check(plan.BonusFor(target, member, 20, 84) == 0, "Campaign has no bonus before activation");
        plan.Activated = true;
        check(plan.ActiveBonus(target, 20, 84) == 15, "Eligible Crown uses the same active campaign magnitude without membership");
        check(plan.ActiveBonus(target, 85, 84) == 0 && plan.ActiveBonus(new Kingdom(), 20, 84) == 0,
            "Crown campaign bonus cannot bypass expiry or target identity");
        foreach (double now in new[] { 0d, 10, 11, 20, 84, 84.001, double.NaN, double.PositiveInfinity })
            check(plan.BonusFor(target, member, now, 84) == (now >= 11 && now <= 84 ? 15 : 0), "Campaign bonus respects saved dates");
        check(plan.BonusFor(new Kingdom(), member, 20, 84) == 0, "Campaign bonus is target-specific");
        check(plan.BonusFor(null, member, 20, 84) == 0, "Missing target cannot receive campaign bonus");
        check(plan.BonusFor(target, new Clan(), 20, 84) == 0, "New members do not inherit the session snapshot");
        var fields = typeof(CourtCampaignRecord).GetFields();
        var ids = fields.Select(f => f.GetCustomAttribute<TaleWorlds.SaveSystem.SaveableFieldAttribute>()?.Id).ToList();
        check(ids.Count == 4 && ids.All(i => i.HasValue) && ids.Distinct().Count() == 4, "Campaign record saves all fields with unique IDs");
        var loaded = new CourtCampaignRecord();
        foreach (var field in fields) field.SetValue(loaded, field.GetValue(plan));
        check(loaded.BonusFor(target, member, 20, 84) == 15, "Campaign record field roundtrip preserves bonus");
        loaded.Members.Remove(member);
        check(loaded.BonusFor(target, member, 20, 84) == 0, "Pruned participant no longer receives campaign bonus");
        foreach (bool initiated in new[] { false, true })
        foreach (bool authorized in new[] { false, true })
        foreach (double now in new[] { 0d, 10, 11, 83, 84, 85 })
        {
            var actual = CourtCampaignRules.DeclarationResult(initiated, authorized, now, 10, 84);
            var expected = now > 84 ? CourtObjectiveState.Expired : now < 10 || !initiated || !authorized
                ? CourtObjectiveState.Cancelled : CourtObjectiveState.Succeeded;
            check(actual == expected, "Only an authorized offensive within the term fulfills the campaign");
        }
        check(CourtCampaignRules.DeclarationResult(false, true, 40, 10, 84) == CourtObjectiveState.Cancelled,
            "Enemy-first declaration cancels without a failure outcome");
        check(CourtCampaignRules.DeclarationResult(true, false, 40, 10, 84) == CourtObjectiveState.Cancelled,
            "Client propagation or allied entry does not fulfill an offensive");
        var agenda = new CourtAgendaRecord { State = CourtAgendaState.PursuingObjective,
            ObjectiveData = new CourtObjectiveRecord { Kind = CourtCampaignRules.Kind }, Campaign = plan };
        agenda.ObjectiveData.FreezeTerm(10, 84);
        check(CourtAgendaPresentation.Deadline(agenda)?.date.ToDays == 84, "Campaign displays the saved term boundary");
        check(CourtAgendaPresentation.Status(agenda.State, campaignObjective: true).Contains("campaign"), "Campaign status is not labelled a peace settlement");
        check(!agenda.IsFiled && agenda.IsOngoingObjective, "Campaign assistance is not a queued ballot");
        check(agenda.ObjectiveData.Finish(CourtObjectiveState.Succeeded, CourtObjectiveCredit.Sponsor, "declaration")
            && !agenda.ObjectiveData.Finish(CourtObjectiveState.Expired, CourtObjectiveCredit.None, "late"), "Campaign result cannot be overwritten");
        check(agenda.ObjectiveData.TryClaimResult() && !agenda.ObjectiveData.TryClaimResult(), "Campaign mood result is claimed once");
        check(CourtCampaignRules.SupportBonus == CourtPeaceRules.AcceptanceBonus, "Initial campaign and peace utility magnitudes match");
    }
}
