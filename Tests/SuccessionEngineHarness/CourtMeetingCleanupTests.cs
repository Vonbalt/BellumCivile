using System;
using System.Runtime.Serialization;
using BellumCivile;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;

internal static class CourtMeetingCleanupTests
{
    private static CourtAgendaBehavior _calendar;
    private static bool _open;
    private static CourtAgendaRecord _agenda;
    private static bool Current(ref CourtAgendaBehavior __result) { __result = _calendar; return false; }
    private static bool Open(ref bool __result) { __result = _open; return false; }
    private static bool Agenda(ref CourtAgendaRecord __result) { __result = _agenda; return false; }
    internal static void Run(Action<bool, string> check)
    {
        T Blank<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
        var h = new Harmony("bellum.test.meeting_cleanup");
        var type = typeof(CourtAgendaBehavior);
        try
        {
            h.Patch(AccessTools.PropertyGetter(type, "Current"), prefix: new HarmonyMethod(typeof(CourtMeetingCleanupTests), nameof(Current)));
            h.Patch(AccessTools.Method(type, "IsNominationOpen"), prefix: new HarmonyMethod(typeof(CourtMeetingCleanupTests), nameof(Open)));
            h.Patch(AccessTools.Method(type, "GetPlayerTermAgenda"), prefix: new HarmonyMethod(typeof(CourtMeetingCleanupTests), nameof(Agenda)));
            var policy = new PolicyDeliberationBehavior(); var realm = Blank<Kingdom>();
            _calendar = null; _open = true;
            check(!policy.HasActivePlayerPolicyMandate(realm), "No calendar means no legacy nomination permission");
            _calendar = Blank<CourtAgendaBehavior>();
            check(!policy.HasActivePlayerPolicyMandate(null), "Null realm cannot gain nomination permission");
            check(policy.HasActivePlayerPolicyMandate(realm), "Current open nomination grants permission");
            _open = false;
            check(!policy.HasActivePlayerPolicyMandate(realm), "Closing nomination removes permission immediately");
            _open = true; _agenda = null;
            check(policy.GetPlayerPolicyMandateAlignment(realm, Blank<PolicyObject>(), false) == PlayerPolicyMandateAlignment.Invalid,
                "Missing agenda cannot inherit old faction permission");
            _agenda = new CourtAgendaRecord { Faction = null };
            check(policy.GetPlayerPolicyMandateAlignment(realm, Blank<PolicyObject>(), false) == PlayerPolicyMandateAlignment.Invalid,
                "Crown is not treated as a faction nomination when queried for alignment");
            var ideology = new IdeologyBehavior();
            foreach (bool calendarPresent in new[] { false, true })
            {
                _calendar = calendarPresent ? Blank<CourtAgendaBehavior>() : null;
                check(ideology.ForceFactionMeeting(null, FactionType.Nobility).Contains("retired"),
                    "Retired meeting command never revives old scheduling, calendar=" + calendarPresent);
            }
        }
        finally { h.UnpatchAll(h.Id); _calendar = null; _agenda = null; }
    }
}
