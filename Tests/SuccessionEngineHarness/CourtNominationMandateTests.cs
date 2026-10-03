using System;
using System.Linq;
using System.Reflection;
using BellumCivile;
using BellumCivile.Behaviors;
using HarmonyLib;

internal static class CourtNominationMandateTests
{
    internal static void Run(Action<bool, string> check)
    {
        var rules = typeof(CourtAgendaRecord).Assembly.GetType("BellumCivile.CourtAgendaRules");
        var authorize = AccessTools.Method(rules, "NominationAuthorized");
        bool Open(CourtAgendaState state, string stored, string requested, double day = 10, double deadline = 20)
            => (bool)authorize.Invoke(null, new object[] { state, stored, requested, day, deadline });
        foreach (CourtAgendaState state in Enum.GetValues(typeof(CourtAgendaState)))
        foreach (string kind in new[] { "policy", "council_appointment" })
        {
            check(Open(state, kind, kind) == (state == CourtAgendaState.AwaitingNomination), "Only live nomination mandates authorize menu actions: " + state + "/" + kind);
            check(!Open(state, kind, kind == "policy" ? "council_appointment" : "policy"), "Mandates cannot be spent across categories");
        }
        check(Open(CourtAgendaState.AwaitingNomination, null, "policy"), "Legacy awaiting-policy nominations remain readable");
        check(!Open(CourtAgendaState.AwaitingNomination, null, "council_appointment"), "Legacy policy mandate does not grant council authority");
        foreach (double day in new[] { 20d, 21d, double.NaN, double.PositiveInfinity })
            check(!Open(CourtAgendaState.AwaitingNomination, "policy", "policy", day), "Expired/invalid clock cannot authorize filing");
        check(!Open(CourtAgendaState.AwaitingNomination, "unrecognized", "unrecognized"), "Unknown saved mandate kind grants no powers");
        check(Open(CourtAgendaState.AwaitingNomination, "policy", "policy", 19.99), "Mandate remains available until the precise deadline");
        var closes = AccessTools.Method(rules, "NominationCloses");
        check((double)closes.Invoke(null, new object[] { 10d, 50d, 7 }) == 17, "Nomination window uses the frozen term allowance");
        check((double)closes.Invoke(null, new object[] { 10d, 14d, 7 }) == 13, "Nomination deadline remains before the old term session");
        var field = typeof(CourtAgendaRecord).GetField("NominationKind");
        var saved = CustomAttributeData.GetCustomAttributes(field).Single(a => a.AttributeType.Name == "SaveableFieldAttribute");
        check(Convert.ToInt32(saved.ConstructorArguments[0].Value) == 46, "Typed nomination survives saves using a new field ID");
        var ids = typeof(CourtAgendaRecord).GetFields().SelectMany(f => CustomAttributeData.GetCustomAttributes(f))
            .Where(a => a.AttributeType.Name == "SaveableFieldAttribute").Select(a => Convert.ToInt32(a.ConstructorArguments[0].Value)).ToList();
        check(ids.Count == ids.Distinct().Count(), "Agenda save-field IDs remain unique");

        var behavior = typeof(CourtAgendaBehavior);
        string[] Calls(string method) => PatchProcessor.GetCurrentInstructions(AccessTools.Method(behavior, method))
            .Select(i => i.operand as MethodInfo).Where(m => m != null).Select(m => m.Name).ToArray();
        check(Calls("TryNominatePolicy").Contains("FileMandateMotion"), "Policy menu routes through immediate transactional mandate filing");
        check(Calls("TryNominateCouncilAppointment").Contains("TryCouncilMandateNomination")
            && Calls("TryNominateCouncilAppointment").Contains("TryPlayerCouncilBusiness"), "Council menu separates faction mandates from royal prerogatives");
        var filing = Calls("FileMandateMotion");
        check(filing.Contains("IsMandateOpen") && filing.Contains("QueuePolicyVote") && filing.Contains("AdvanceCouncil"),
            "Both filing routes revalidate authority and use existing deliberation queues");
        check(!filing.Contains("ReplacementCost"), "Filing does not charge the substitution fee a second time");
        var update = Calls("UpdatePlayerAgenda");
        check(Array.IndexOf(update, "ValidPlayerAgenda") < Array.IndexOf(update, "ApplyMemberMemory"),
            "Loss of authority is checked before expiry penalties");
        var mandate = new CourtAgendaRecord { State = CourtAgendaState.AwaitingNomination, NominationKind = "council_appointment" };
        check(mandate.IsUnopened && !mandate.IsFiled, "Active mandate does not impersonate a filed ballot");
        mandate.State = CourtAgendaState.NominationExpired;
        check(!mandate.IsUnopened && !Open(mandate.State, mandate.NominationKind, mandate.NominationKind),
            "Expired mandate cannot be advanced or spent again");
    }
}
