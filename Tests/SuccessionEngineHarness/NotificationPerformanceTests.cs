using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;
using BellumCivile;
using HarmonyLib;
using MCM.Abstractions;
using MCM.Common;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

internal static class NotificationPerformanceTests
{
    private sealed class TrackedText : TextObject
    {
        internal int Renders;
        internal TrackedText() : base("{=BC_Test_Notice}Test notice") { }
        public override string ToString() { Renders++; return "Test notice"; }
    }

    private static Clan _player;
    private static readonly List<string> Messages = new List<string>();
    private static int _randomCalls;
    private static T Blank<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    private static bool Player(ref Clan __result) { __result = _player; return false; }
    private static bool Display(InformationMessage message) { Messages.Add(message.Information); return false; }
    private static bool Random(ref int __result) { _randomCalls++; __result = 1; return false; }

    internal static void Run(Action<bool, string> check)
    {
        Assembly mod = typeof(FactionObject).Assembly;
        Type notifications = mod.GetType("BellumCivile.BellumCivileNotifications", true);
        Type diagnostics = mod.GetType("BellumCivile.NotificationPerformanceDiagnostics", true);
        Type options = mod.GetType("BellumCivile.BellumCivileOptions", true);
        Type settingsType = mod.GetType("BellumCivile.BellumCivileSettings", true);
        FieldInfo settingsField = AccessTools.Field(options, "_settings");
        object oldSettings = settingsField.GetValue(null);
        object settings = Activator.CreateInstance(settingsType, true);
        object dropdown = AccessTools.Property(settingsType, "BellumCivileNotifications").GetValue(settings);
        PropertyInfo index = AccessTools.Property(dropdown.GetType(), "SelectedIndex");
        var show = AccessTools.Method(notifications, "Show", new[] { typeof(TextObject), typeof(Color), typeof(Kingdom),
            typeof(Kingdom), typeof(Clan), typeof(Clan), typeof(bool), typeof(bool) });
        var captured = AccessTools.Method(notifications, "ShowCapturedOutcome");
        var start = AccessTools.Method(diagnostics, "Start");
        var snapshot = AccessTools.Method(diagnostics, "Snapshot");
        var stop = AccessTools.Method(diagnostics, "Stop");
        var harmony = new Harmony("bellum.tests.notification_performance");
        void SetMode(int mode) => index.SetValue(dropdown, mode);
        void Send(TrackedText text, Kingdom realm = null, Clan clan = null, bool personal = false, bool major = false) =>
            show.Invoke(null, new object[] { text, Colors.White, realm, null, clan, null, personal, major });
        try
        {
            settingsField.SetValue(null, settings);
            var format = Activator.CreateInstance(typeof(SettingsUtils).Assembly.GetType("MCM.Implementation.JsonSettingsFormat"), new object[] { null });
            SetMode(1);
            string savedSettings = (string)AccessTools.Method(format.GetType(), "SaveJson").Invoke(format, new[] { settings });
            var restoredSettings = Activator.CreateInstance(settingsType, true);
            AccessTools.Method(format.GetType(), "LoadFromJson").Invoke(format, new[] { restoredSettings,
                savedSettings.Replace("Local & Major Events", "Kingdom Only") });
            object restoredDropdown = AccessTools.Property(settingsType, "BellumCivileNotifications").GetValue(restoredSettings);
            check((int)index.GetValue(restoredDropdown) == 1, "Existing local notification selection survives the label change");
            harmony.Patch(AccessTools.PropertyGetter(typeof(Clan), "PlayerClan"), prefix: new HarmonyMethod(typeof(NotificationPerformanceTests), nameof(Player)));
            harmony.Patch(AccessTools.Method(typeof(InformationManager), "DisplayMessage", new[] { typeof(InformationMessage) }), prefix: new HarmonyMethod(typeof(NotificationPerformanceTests), nameof(Display)));
            harmony.Patch(AccessTools.Method(typeof(MBRandom), "RandomInt", new[] { typeof(int) }), prefix: new HarmonyMethod(typeof(NotificationPerformanceTests), nameof(Random)));
            _player = Blank<Clan>();
            var local = Blank<Kingdom>(); var remote = Blank<Kingdom>(); var other = Blank<Clan>();
            AccessTools.Field(typeof(Clan), "_kingdom").SetValue(_player, local);
            Messages.Clear(); _randomCalls = 0;
            check(!(bool)AccessTools.Property(diagnostics, "Enabled").GetValue(null), "Diagnostics are disabled by default");
            start.Invoke(null, null);
            SetMode(0);
            var disabled = new TrackedText(); Send(disabled, local);
            check(disabled.Renders == 0 && Messages.Count == 0, "Disabled mode filters before rendering");
            var personal = new TrackedText(); Send(personal, personal: true);
            check(personal.Renders == 1 && Messages.Count == 1, "Personal messages still display when notifications are disabled");
            var claimant = Blank<Clan>(); var holder = Blank<Clan>(); var hero = Blank<Hero>(); var title = Blank<FeudalTitleRecord>();
            NotificationHelper.ShowFeudalClaimFabricationCompleted(claimant, holder, hero, title);
            check(_randomCalls == 1 && Messages.Count == 1, "Filtered claim fabrication preserves its random draw without formatting");
            NotificationHelper.ShowClaimFeudStarted(claimant, holder, title);
            check(Messages.Count == 1, "Filtered feud exits before touching notification title data");
            SetMode(1);
            var foreign = new TrackedText(); Send(foreign, remote, other);
            check(foreign.Renders == 0 && Messages.Count == 1, "Local mode filters ordinary foreign notices before rendering");
            var own = new TrackedText(); Send(own, local);
            var clan = new TrackedText(); Send(clan, clan: _player);
            var major = new TrackedText(); Send(major, remote, major: true);
            check(own.Renders == 1 && clan.Renders == 1 && major.Renders == 1 && Messages.Count == 4,
                "Local mode retains realm, player clan and worldwide major notices");
            SetMode(2);
            var global = new TrackedText(); Send(global, remote);
            check(global.Renders == 1 && Messages.Count == 5, "Global mode retains ordinary foreign notices");
            SetMode(0);
            captured.Invoke(null, new object[] { "Previously allowed outcome", Colors.White, true });
            captured.Invoke(null, new object[] { "Previously filtered outcome", Colors.White, false });
            check(Messages.Count == 6 && Messages[5] == "Previously allowed outcome", "Captured outcome preserves its original approval after settings change");
            string report = (string)snapshot.Invoke(null, null);
            check(report.Contains("shown=6") && report.Contains("filtered=5") && report.Contains("personal=1") && report.Contains("major=1"),
                "Diagnostics count shown and filtered notices once, including early exits and captured outcomes");
            check(report.Contains("prepare:ShowClaimFeudStarted") && report.Contains("BC_Test_Notice") && !report.Contains("Test notice"),
                "Diagnostic categories identify sources without storing message contents");
            string stopped = (string)stop.Invoke(null, null);
            Send(new TrackedText(), local);
            check(stopped.Contains("stopped") && ((string)snapshot.Invoke(null, null)).Contains("filtered=5"),
                "Stopping freezes diagnostic counters");
            start.Invoke(null, null);
            check(((string)snapshot.Invoke(null, null)).Contains("shown=0"), "Starting a new sample clears old counters");
        }
        finally
        {
            stop.Invoke(null, null);
            harmony.UnpatchAll(harmony.Id);
            settingsField.SetValue(null, oldSettings);
            _player = null; Messages.Clear();
        }
    }
}
