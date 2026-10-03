using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using BellumCivile;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;

internal static class CrownActionCooldownTests
{
    private static CampaignTime _now;
    private static bool Now(ref CampaignTime __result) { __result = _now; return false; }
    internal static void Run(Action<bool, string> check)
    {
        var h = new Harmony("bellum.test.crown_cooldowns");
        var ticks = AccessTools.Field(typeof(CampaignTime), "TimeTicksPerDay");
        var oldTicks = ticks.GetValue(null);
        try
        {
            ticks.SetValue(null, 1000L);
            h.Patch(AccessTools.PropertyGetter(typeof(CampaignTime), "Now"), prefix: new HarmonyMethod(typeof(CrownActionCooldownTests), nameof(Now)));
            _now = CampaignTime.Days(100);
            var court = new CourtAgendaBehavior();
            var realm = (Kingdom)FormatterServices.GetUninitializedObject(typeof(Kingdom)); realm.StringId = "test_crown";
            object Call(string name, params object[] args) => AccessTools.Method(typeof(CourtAgendaBehavior), name).Invoke(court, args);
            string Kind(string rules) => (string)AccessTools.Field(typeof(CourtAgendaRecord).Assembly.GetType("BellumCivile." + rules), "Kind").GetValue(null);
            var appease = Kind("CourtAppeasementRules");
            var liberation = Kind("CourtLiberationRules");
            var protection = Kind("CourtProtectionRules");
            var agenda = new CourtAgendaRecord { Realm = realm, ObjectiveData = new CourtObjectiveRecord { Kind = appease } };
            agenda.FreezeSchedule(84, 7);
            check(!(bool)Call("CrownActionCoolingDown", realm, appease), "Unused Crown action has no cooldown");
            Call("StartCrownActionCooldown", agenda);
            check(((CampaignTime)Call("CrownActionUntil", realm, appease)).ToDays == 184, "Cooldown uses full frozen term from activation");
            check(!(bool)Call("CrownActionCoolingDown", realm, liberation), "Appeasement does not lock liberation or other Crown business");
            agenda.ResultApplied = true; agenda.Sponsor = null;
            agenda.ObjectiveData.TargetId = "another_faction";
            check((bool)Call("CrownActionCoolingDown", realm, appease), "Ruler/target changes and completed agendas do not clear realm cooldown");
            _now = CampaignTime.Days(183);
            check((bool)Call("CrownActionCoolingDown", realm, appease), "Cooldown survives intervening annual term boundary");
            _now = CampaignTime.Days(184);
            check(!(bool)Call("CrownActionCoolingDown", realm, appease), "Cooldown expires exactly at its deadline");
            _now = CampaignTime.Days(100);
            var agendas = (List<CourtAgendaRecord>)AccessTools.Field(typeof(CourtAgendaBehavior), "_agendas").GetValue(court);
            agenda.Appeasement = new CourtAppeasementRecord { Applied = true, Until = CampaignTime.Days(170) };
            agendas.Add(agenda);
            Call("RecoverCrownActionCooldowns");
            check(((CampaignTime)Call("CrownActionUntil", realm, appease)).ToDays == 184, "Legacy receipt recovery never shortens an existing cooldown");
            var offers = (List<CourtProtectionRecord>)AccessTools.Field(typeof(CourtAgendaBehavior), "_protectionOffers").GetValue(court);
            offers.Add(new CourtProtectionRecord { Client = realm, Phase = CourtProtectionPhase.Cancelled, TermEnd = 200 });
            Call("RecoverCrownActionCooldowns");
            check(!(bool)Call("HasProtectionOffer", realm), "Cancelled unsent protection appeal has no cooldown");
            offers[0].ReplyDay = 101; offers[0].Phase = CourtProtectionPhase.Declined;
            Call("RecoverCrownActionCooldowns");
            check((bool)Call("CrownActionCoolingDown", realm, protection), "Declined sent appeal retains cooldown and migrates from its receipt");
            check((int)AccessTools.Field(typeof(CourtAgendaBehavior), "CrownInitiativeCost").GetValue(null) == 100,
                "Liberation and protection share 100 influence initiative cost");
        }
        finally { h.UnpatchAll(h.Id); ticks.SetValue(null, oldTicks); }
    }
}
