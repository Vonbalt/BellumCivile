using System;
using System.Runtime.Serialization;
using BellumCivile;
using BellumCivile.Behaviors;
using HarmonyLib;

internal static class CourtMotionNavigationTests
{
    private static bool _valid;
    private static int _proposal, _picker;
    private static string _category;
    private static bool Valid(ref bool __result) { __result = _valid; return false; }
    private static bool Proposal() { _proposal++; return false; }
    private static bool Picker(string category) { _picker++; _category = category; return false; }

    internal static void Run(Action<bool, string> check)
    {
        var type = typeof(CourtAgendaBehavior);
        var harmony = new Harmony("bellum.test.court_motion_navigation");
        try
        {
            foreach (var pair in new[] { new[] { "CanChooseTermBusiness", nameof(Valid) },
                new[] { "ShowTermBusinessInquiry", nameof(Proposal) }, new[] { "ShowTermMotionPicker", nameof(Picker) } })
                harmony.Patch(AccessTools.Method(type, pair[0]), prefix: new HarmonyMethod(typeof(CourtMotionNavigationTests), pair[1]));
            var calendar = (CourtAgendaBehavior)FormatterServices.GetUninitializedObject(type);
            var agenda = new CourtAgendaRecord { State = CourtAgendaState.AwaitingPlayerDecision,
                ObjectiveData = new CourtObjectiveRecord { Kind = "policy" }, PolicyId = "original", SubstitutionInfluencePaid = 0 };
            var identity = agenda.ObjectiveData;
            var active = AccessTools.Field(type, "_activePlayerInquiry");
            void Back(string category, bool proposal = false) => AccessTools.Method(type, "ReturnFromMotionPicker").Invoke(calendar,
                new object[] { agenda, identity, CourtAgendaState.AwaitingPlayerDecision, false, category, proposal });
            _valid = true;
            foreach (string category in new[] { null, "Foreign affairs", "Faction activities" })
            {
                active.SetValue(calendar, agenda); _picker = _proposal = 0;
                Back(category);
                check(_picker == 1 && _proposal == 0 && _category == category, "Preview Back preserves originating motion category");
                check(agenda.ObjectiveData == identity && agenda.PolicyId == "original" && !agenda.PlayerSelectionConfirmed
                    && agenda.SubstitutionInfluencePaid == 0 && agenda.State == CourtAgendaState.AwaitingPlayerDecision,
                    "Back does not spend influence or change the selected agenda");
            }
            active.SetValue(calendar, agenda); _picker = _proposal = 0; Back(null, true);
            check(_proposal == 1 && _picker == 0, "Top-level Back returns to original faction proposal");
            foreach (string invalid in new[] { "authority", "identity", "state", "crisis", "active" })
            {
                _valid = invalid != "authority"; agenda.ObjectiveData = invalid == "identity" ? new CourtObjectiveRecord() : identity;
                agenda.State = invalid == "state" ? CourtAgendaState.Deliberating : CourtAgendaState.AwaitingPlayerDecision;
                agenda.CrisisInterventionPending = invalid == "crisis";
                active.SetValue(calendar, invalid == "active" ? new CourtAgendaRecord() : agenda);
                _picker = _proposal = 0; Back(null);
                check(_picker == 0 && _proposal == 0, "Stale Back callback cannot reopen changed agenda: " + invalid);
            }
        }
        finally { harmony.UnpatchAll(harmony.Id); }
    }
}
