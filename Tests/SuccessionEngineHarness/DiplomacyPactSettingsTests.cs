using System;
using System.Collections.Generic;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.Localization;

internal static class DiplomacyPactSettingsTests
{
    private static readonly Dictionary<string, int> Settings = new Dictionary<string, int>();
    public sealed class FakeSettings
    {
        public int? NonAggressionPactDuration => Settings.TryGetValue(nameof(NonAggressionPactDuration), out int value) ? value : (int?)null;
        public int? NonAggressionPactTendency => Settings.TryGetValue(nameof(NonAggressionPactTendency), out int value) ? value : (int?)null;
    }
    private static bool Read(ref object __result) { __result = new FakeSettings(); return false; }

    internal static void Run(Action<bool, string> check)
    {
        var helper = typeof(DiplomacyCompatibilityBehavior).Assembly.GetType("BellumCivile.ModIntegrationHelper");
        var reader = AccessTools.Method(helper, "TryGetMcmSettingsInstance");
        var scanned = AccessTools.Field(helper, "_diplomacyScanComplete");
        var loaded = AccessTools.Field(helper, "_isDiplomacyLoaded");
        var settingsType = AccessTools.Field(helper, "_diplomacySettingsType");
        object oldScanned = scanned.GetValue(null), oldLoaded = loaded.GetValue(null), oldType = settingsType.GetValue(null);
        var collect = AccessTools.Method(typeof(DiplomacyCompatibilityBehavior), "AddHostagePactGuidance");
        var harmony = new Harmony("bellum.tests.diplomacy.pactsettings");
        try
        {
            scanned.SetValue(null, true); loaded.SetValue(null, true); settingsType.SetValue(null, typeof(FakeSettings));
            harmony.Patch(reader, prefix: new HarmonyMethod(typeof(DiplomacyPactSettingsTests), nameof(Read)));
            List<TextObject> Warnings(int? duration, int? tendency)
            {
                Settings.Clear();
                if (duration.HasValue) Settings["NonAggressionPactDuration"] = duration.Value;
                if (tendency.HasValue) Settings["NonAggressionPactTendency"] = tendency.Value;
                var issues = new List<TextObject>();
                collect.Invoke(null, new object[] { issues });
                return issues;
            }
            check(Warnings(84, 0).Count == 2, "Default Diplomacy pact settings produce two actionable warnings");
            check(Warnings(0, -100).Count == 0, "Corrected pact sliders silence pact compatibility warnings");
            var durationOnly = Warnings(84, -100);
            check(durationOnly.Count == 1 && durationOnly[0].ToString() == "Non-Aggression Pact Duration in Days: set to 0.",
                "Only incorrect pact duration is reported");
            var tendencyOnly = Warnings(0, 0);
            check(tendencyOnly.Count == 1 && tendencyOnly[0].ToString() == "Non-Aggression Pact Tendency: set to -100.",
                "Only incorrect pact tendency is reported");
            check(Warnings(null, null).Count == 0, "Unavailable optional settings do not cause an unconditional warning");
            check(Warnings(null, 50).Count == 1, "Available incorrect slider is checked independently");
            Warnings(20, -10);
            check(Settings["NonAggressionPactDuration"] == 20 && Settings["NonAggressionPactTendency"] == -10,
                "Compatibility warning never rewrites Diplomacy sliders");
        }
        finally
        {
            harmony.UnpatchAll(harmony.Id); Settings.Clear();
            scanned.SetValue(null, oldScanned); loaded.SetValue(null, oldLoaded); settingsType.SetValue(null, oldType);
        }
    }
}
