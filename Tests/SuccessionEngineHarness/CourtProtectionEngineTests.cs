using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;
using BellumCivile;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;

internal static class CourtProtectionEngineTests
{
    private static Kingdom _first;
    private static bool _loaded, _missing, _pact, _cooldown, _throws;
    private static bool Strength(Kingdom __instance, ref float __result) { __result = __instance == _first ? 100 : 200; return false; }
    private static bool Loaded(ref bool __result) { __result = _loaded; return false; }
    private static bool TypeLookup(ref Type __result) { __result = _missing ? null : typeof(FakeDiplomacyQueries); return false; }
    private static class FakeDiplomacyQueries
    {
        public static bool HasNonAggressionPact(Kingdom first, Kingdom second, out object agreement)
        { agreement = null; if (_throws) throw new InvalidOperationException(); return _pact; }
        public static bool HasDeclareWarCooldown(IFaction first, IFaction second, out float elapsed)
        { elapsed = 10; if (_throws) throw new InvalidOperationException(); return _cooldown; }
    }

    internal static void Run(Action<bool, string> check)
    {
        var mod = typeof(CourtAgendaRecord).Assembly;
        var service = mod.GetType("BellumCivile.CourtProtectionAssessmentService");
        var integration = mod.GetType("BellumCivile.ModIntegrationHelper");
        _first = (Kingdom)FormatterServices.GetUninitializedObject(typeof(Kingdom));
        var second = (Kingdom)FormatterServices.GetUninitializedObject(typeof(Kingdom));
        var harmony = new Harmony("bellum.test.court_protection");
        void Patch(MethodBase method, string prefix) => harmony.Patch(method, prefix: new HarmonyMethod(typeof(CourtProtectionEngineTests), prefix));
        bool Query(string name, out bool value)
        {
            var args = new object[] { _first, second, false };
            bool result = (bool)AccessTools.Method(integration, name).Invoke(null, args);
            value = (bool)args[2]; return result;
        }
        try
        {
            Patch(AccessTools.PropertyGetter(typeof(Kingdom), "CurrentTotalStrength"), nameof(Strength));
            double sum = (double)AccessTools.Method(service, "Strength").Invoke(null,
                new object[] { new[] { _first, second, _first, second } });
            check(sum == 300, "Protection military aggregation counts each realm once, even with duplicate membership");
            var behavior = new WarPeaceRevampBehavior();
            var clan = (Clan)FormatterServices.GetUninitializedObject(typeof(Clan)); clan.StringId = "protection_test";
            var saved = (Dictionary<string, float>)AccessTools.Field(typeof(WarPeaceRevampBehavior), "_warWillByClanId").GetValue(behavior);
            int count = saved.Count;
            var peek = AccessTools.Method(typeof(WarPeaceRevampBehavior), "PeekWarWill");
            peek.Invoke(behavior, new object[] { clan });
            check(saved.Count == count, "Protection preview does not initialize saved war-will values");
            saved[clan.StringId] = 17;
            check((float)peek.Invoke(behavior, new object[] { clan }) == 17 && saved.Count == count + 1,
                "Read-only war-will accessor returns an existing value unchanged");
            Patch(AccessTools.PropertyGetter(integration, "IsDiplomacyLoaded"), nameof(Loaded));
            Patch(AccessTools.Method(integration, "FindLoadedType"), nameof(TypeLookup));
            foreach (bool loaded in new[] { false, true })
            foreach (bool missing in new[] { false, true })
            foreach (bool pact in new[] { false, true })
            foreach (bool cooldown in new[] { false, true })
            foreach (bool throws in new[] { false, true })
            {
                _loaded = loaded; _missing = missing; _pact = pact; _cooldown = cooldown; _throws = throws;
                bool expected = !loaded || !missing && !throws;
                bool napOk = Query("TryReadDiplomacyNonAggressionPact", out bool actualPact);
                bool cooldownOk = Query("TryReadDiplomacyWarCooldown", out bool actualCooldown);
                check(napOk == expected && actualPact == (expected && loaded && pact),
                    "NAP preview distinguishes absent mod, valid query, missing API and backend failure");
                check(cooldownOk == expected && actualCooldown == (expected && loaded && cooldown),
                    "Truce preview fails closed when a loaded compatibility API cannot be read");
            }
        }
        finally { harmony.UnpatchAll(harmony.Id); }
    }
}
