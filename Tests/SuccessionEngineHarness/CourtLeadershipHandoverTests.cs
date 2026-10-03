using System;
using System.Runtime.Serialization;
using BellumCivile;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;

internal static class CourtLeadershipHandoverTests
{
    internal static void Run(Action<bool, string> check)
    {
        var player = (Clan)FormatterServices.GetUninitializedObject(typeof(Clan));
        var previous = (Clan)FormatterServices.GetUninitializedObject(typeof(Clan));
        var faction = (FactionObject)FormatterServices.GetUninitializedObject(typeof(FactionObject));
        CourtAgendaRecord Agenda(CourtAgendaState state) => new CourtAgendaRecord
        {
            Faction = faction, Sponsor = previous, State = state, PolicyId = "test_policy",
            ObjectiveData = new CourtObjectiveRecord { Kind = "policy" }, PlayerSelectionConfirmed = true,
            HasScheduleSnapshot = true, TermDays = 84, NominationWindowDays = 7
        };
        bool Transfer(CourtAgendaRecord agenda, bool late = false, bool crisis = false) =>
            (bool)AccessTools.Method(typeof(CourtAgendaBehavior), "TryHandOverUnstartedAgenda")
                .Invoke(null, new object[] { agenda, player, late, crisis });
        foreach (CourtAgendaState state in Enum.GetValues(typeof(CourtAgendaState)))
        {
            var agenda = Agenda(state);
            var objective = agenda.ObjectiveData;
            var session = agenda.SessionDate; var vote = agenda.VoteDate;
            bool expected = state == CourtAgendaState.Announced || state == CourtAgendaState.Crisis || state == CourtAgendaState.NotProposed;
            check(Transfer(agenda) == expected, "Leadership handover eligibility: " + state);
            if (!expected)
            {
                check(agenda.State == state && agenda.Sponsor == previous, "Filed/finished/ongoing decisions stay untouched: " + state);
                continue;
            }
            check(agenda.State == CourtAgendaState.AwaitingPlayerDecision && agenda.Sponsor == player && !agenda.PlayerSelectionConfirmed,
                "New leader receives approval/substitution/veto review");
            check(ReferenceEquals(objective, agenda.ObjectiveData) && agenda.PolicyId == "test_policy"
                && agenda.SessionDate.Equals(session) && agenda.VoteDate.Equals(vote) && agenda.HasScheduleSnapshot
                && agenda.TermDays == 84 && agenda.NominationWindowDays == 7, "Handover preserves proposed motion and frozen schedule");
            check(agenda.CrisisInterventionPending == (state == CourtAgendaState.Crisis), "Existing crisis requires explicit player review");
            check(!Transfer(agenda), "Leadership callback/tick cannot duplicate review");
            agenda.State = CourtAgendaState.Announced; agenda.PlayerSelectionConfirmed = true;
            check(!Transfer(agenda), "Confirmed player motion is not reopened every tick");
        }
        check(!Transfer(Agenda(CourtAgendaState.Announced), late: true), "Handover does not extend an elapsed session");
        var unhappy = Agenda(CourtAgendaState.Announced);
        check(Transfer(unhappy, crisis: true) && unhappy.CrisisInterventionPending, "New leader can review current faction crisis demand");
        foreach (string field in new[] { "Manual", "PaymentSettled", "ResultApplied", "EventApplied" })
        {
            var agenda = Agenda(CourtAgendaState.Announced);
            AccessTools.Field(typeof(CourtAgendaRecord), field).SetValue(agenda, true);
            check(!Transfer(agenda), "Handover protects " + field);
        }
        foreach (string field in new[] { "PaidInfluence", "SubstitutionInfluencePaid" })
        {
            var agenda = Agenda(CourtAgendaState.Announced);
            AccessTools.Field(typeof(CourtAgendaRecord), field).SetValue(agenda, 100);
            check(!Transfer(agenda), "Handover never changes ownership of paid business: " + field);
        }
        var crown = Agenda(CourtAgendaState.Announced); crown.Faction = null;
        check(!Transfer(crown), "Faction handover never reopens Crown business");
    }
}
