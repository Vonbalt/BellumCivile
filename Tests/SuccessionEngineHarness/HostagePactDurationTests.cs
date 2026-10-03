using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using BellumCivile;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;

internal static class HostagePactDurationTests
{
    private static bool SkipPresetDiscovery() => false;
    private static Clan _firstHouse, _secondHouse;
    private static Kingdom _firstRealm;
    private static bool RulingHouse(Kingdom __instance, ref Clan __result)
    { __result = __instance == _firstRealm ? _firstHouse : _secondHouse; return false; }
    private static T Empty<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));

    internal static void Run(Action<bool, string> check)
    {
        var assembly = typeof(HostagePactRecord).Assembly;
        var settingsType = assembly.GetType("BellumCivile.BellumCivileSettings", true);
        var optionsType = assembly.GetType("BellumCivile.BellumCivileOptions", true);
        var setting = AccessTools.Property(settingsType, "HostagePactDurationDays");
        var option = AccessTools.Property(optionsType, "HostagePactDurationDays");
        var settingsCache = AccessTools.Field(optionsType, "_settings");
        object previousSettings = settingsCache.GetValue(null);
        var service = assembly.GetType("BellumCivile.TreatyHostageTerms", true);
        var harmony = new Harmony("bellum.tests.hostage.duration");
        try
        {
            harmony.Patch(AccessTools.Method(assembly.GetType("BellumCivile.FeudalTitleStylePresetCatalog"), "CreateDropdown"),
                prefix: new HarmonyMethod(typeof(HostagePactDurationTests), nameof(SkipPresetDiscovery)));
            harmony.Patch(AccessTools.PropertyGetter(typeof(Kingdom), "RulingClan"),
                prefix: new HarmonyMethod(typeof(HostagePactDurationTests), nameof(RulingHouse)));
            object settings = Activator.CreateInstance(settingsType, true);
            settingsCache.SetValue(null, settings);
            check((int)setting.GetValue(settings) == 100 && (int)option.GetValue(null) == 100,
                "Hostage MCM and runtime defaults remain 100 days");
            var slider = setting.GetCustomAttributesData().Single(a => a.AttributeType.Name == "SettingPropertyIntegerAttribute");
            check(Convert.ToInt32(slider.ConstructorArguments[1].Value) == 0
                && Convert.ToInt32(slider.ConstructorArguments[2].Value) == 1000
                && slider.NamedArguments.Any(a => a.MemberName == "RequireRestart" && !(bool)a.TypedValue.Value),
                "Hostage slider covers 0 to 1000 days without requiring a restart");
            foreach (int days in new[] { -1, 0, 1, 100, 250, 1000, 1001 })
            {
                setting.SetValue(settings, days);
                check((int)option.GetValue(null) == Math.Max(0, Math.Min(1000, days)),
                    "Hostage runtime duration clamps setting " + days);
            }
            var secondRealm = Empty<Kingdom>(); secondRealm.StringId = "second_realm";
            _firstRealm = Empty<Kingdom>(); _firstRealm.StringId = "first_realm";
            _firstHouse = Empty<Clan>(); _firstHouse.StringId = "first_house";
            _secondHouse = Empty<Clan>(); _secondHouse.StringId = "second_house";
            var hero = Empty<Hero>(); hero.StringId = "pledged_heir";
            var candidateType = assembly.GetType("BellumCivile.TreatyHostageCandidate");
            var candidate = Activator.CreateInstance(candidateType, BindingFlags.Instance | BindingFlags.NonPublic,
                null, new object[] { hero, 1 }, null);
            TreatyTermRecord Draft(int? days = null) => (TreatyTermRecord)AccessTools.Method(service, "Create")
                .Invoke(null, new object[] { _firstRealm, secondRealm, candidate, false, days });
            int DraftDuration(params TreatyTermRecord[] terms) => (int)AccessTools.Method(service, "DurationForDraft")
                .Invoke(null, new object[] { terms });
            setting.SetValue(settings, 250);
            var clause = Draft();
            check(clause.DurationDays == 250 && DraftDuration() == 250, "New clauses use the configured duration");
            setting.SetValue(settings, 1000);
            check(clause.DurationDays == 250 && DraftDuration(clause) == 250 && Draft().DurationDays == 1000,
                "Changing MCM affects future agreements but not a drafted pledge");
            check(Draft(DraftDuration(clause)).DurationDays == 250, "Reciprocal clause copies the negotiated duration");
            var original = new TreatyTermRecord(TreatyTermType.HostagePeace, 30, durationDays: 100);
            setting.SetValue(settings, 0);
            check(DraftDuration(original) == 100 && DraftDuration(clause) == 250,
                "Disabling new pacts preserves old and pending treaty durations");
            var available = (IEnumerable)AccessTools.Method(service, "Available")
                .Invoke(null, new object[] { _firstRealm, secondRealm, null });
            check(!available.Cast<object>().Any(), "Zero duration excludes new hostage choices before campaign scans");

            HostagePactRecord Pact(int days, bool recorded = true) => new HostagePactRecord {
                Id = "duration-test", FirstRealm = _firstRealm, SecondRealm = secondRealm,
                FirstHouse = _firstHouse, SecondHouse = _secondHouse,
                AgreedDurationDays = days, DurationRecorded = recorded,
                FirstHostage = new TreatyHostageRecord { Hero = hero, SupplyingHouse = _firstHouse,
                    ReceivingHouse = _secondHouse, Holding = Empty<Settlement>(), Tier = 1,
                    NegotiatedCost = 30, CustodyEstablished = true }
            };
            bool Activate(HostagePactRecord pact, double day) => (bool)AccessTools.Method(typeof(HostagePactRecord), "TryActivate")
                .Invoke(pact, new object[] { day });
            bool Expire(HostagePactRecord pact, double day, bool daily) => (bool)AccessTools.Method(typeof(HostagePactRecord), "CanExpire")
                .Invoke(pact, new object[] { day, daily });
            foreach (int days in new[] { 1, 100, 250, 1000 })
            {
                var pact = Pact(days);
                check(Activate(pact, 10) && pact.EndDay == 10 + days,
                    "Activation freezes the duration snapshot even when MCM is zero: " + days);
                check(!Expire(pact, pact.EndDay - .001, true) && Expire(pact, pact.EndDay, false),
                    "Positive duration expires at its saved boundary: " + days);
                setting.SetValue(settings, 500);
                check(!Activate(pact, 20) && pact.EndDay == 10 + days,
                    "Later setting changes and activation retries never extend a signed pact: " + days);
                setting.SetValue(settings, 0);
            }
            var legacy = Pact(0, false);
            check(Activate(legacy, 10) && legacy.EndDay == 110, "Missing duration snapshot migrates to the original 100-day term");
            var zero = Pact(0);
            check(Activate(zero, 10) && zero.EndDay == 10 && !Expire(zero, 10, false)
                && !Expire(zero, 11, false) && Expire(zero, 10, true),
                "Zero-day pact awaits the next daily tick, not hourly or handover callbacks");
            foreach (int invalid in new[] { -1, 1001 })
                check(!Activate(Pact(invalid), 10), "Invalid saved duration cannot activate pact: " + invalid);
            var recovered = Pact(250); recovered.TreatySettlementStarted = recovered.TreatySettlementCompleted = true;
            recovered.SigningDayRecorded = true; recovered.TreatySigningDay = 10;
            check(Activate(recovered, recovered.TreatySigningDay) && recovered.EndDay == 260,
                "Interrupted delivery activation uses the saved duration and original signing day");
        }
        finally
        {
            harmony.UnpatchAll(harmony.Id);
            settingsCache.SetValue(null, previousSettings);
            _firstRealm = null; _firstHouse = _secondHouse = null;
        }
    }
}
