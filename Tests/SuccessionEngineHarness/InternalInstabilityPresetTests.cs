using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Xml;
using BellumCivile.Behaviors;
using HarmonyLib;
using MCM.Abstractions;
using MCM.Abstractions.Attributes;
using MCM.Abstractions.Attributes.v2;
using MCM.Common;
using TaleWorlds.Library;

internal static class InternalInstabilityPresetTests
{
    private static int _unavailable;
    private static bool Unavailable() { _unavailable++; return false; }
    private static bool SettingsGroups(object settings, ref object __result)
    {
        var discoverer = typeof(SettingsUtils).Assembly.GetType("MCM.Implementation.AttributeSettingsPropertyDiscoverer");
        var definitions = (IEnumerable<ISettingsPropertyDefinition>)AccessTools.Method(discoverer, "GetPropertiesInternal").Invoke(null, new[] { settings });
        __result = SettingsUtils.GetSettingsPropertyGroups('/', definitions);
        return false;
    }
    private sealed class OtherSettings
    {
        internal int Clicks;
        [SettingPropertyButton("Other button")]
        [SettingPropertyGroup("Other")]
        public Action ApplyInstabilityPreset { get => () => Clicks++; set { } }
    }

    internal static void Run(Type settingsType, Action<bool, string> check)
    {
        var assembly = typeof(CourtAgendaBehavior).Assembly;
        var presets = assembly.GetType("BellumCivile.InternalInstabilityPresets", true);
        var getValues = AccessTools.Method(presets, "GetValues");
        IReadOnlyDictionary<string, int> Values(int index) =>
            (IReadOnlyDictionary<string, int>)getValues.Invoke(null, new object[] { index });
        var names = new[] { "RebellionThreshold", "CourtCrisisMoodThreshold", "CourtChallengeChancePercent",
            "HeirChallengeLoyaltyThreshold", "ElectionChallengeChancePercent", "ClaimFeudStartThreshold",
            "ClaimedRevocationChancePercent", "TreasonResistanceChancePercent", "ClientLiberationWillingnessThreshold" };
        var expected = new[] { new[] { 100, -60, 100, 25, 100, 65, 100, 100, 75 },
            new[] { 125, -80, 50, 15, 50, 80, 50, 50, 85 }, new[] { 75, -40, 100, 35, 125, 55, 150, 150, 65 } };
        object settings = Activator.CreateInstance(settingsType, true);
        int Read(string name) => (int)settingsType.GetProperty(name).GetValue(settings);
        var selector = (Dropdown<string>)settingsType.GetProperty("InstabilityPreset").GetValue(settings);
        check(selector.SelectedIndex == 0 && selector.Count == 3, "Preset selector defaults to Standard with exactly three choices");
        for (int i = 0; i < 3; i++)
        {
            var values = Values(i);
            check(values.Count == 9, "Preset " + i + " contains only the nine instability settings");
            for (int j = 0; j < names.Length; j++)
            {
                var slider = settingsType.GetProperty(names[j]).GetCustomAttributes(false).OfType<SettingPropertyIntegerAttribute>().Single();
                check(values[names[j]] == expected[i][j] && values[names[j]] >= slider.MinValue && values[names[j]] <= slider.MaxValue,
                    "Preset " + i + " has the approved in-range value for " + names[j]);
                if (i == 0) check(Read(names[j]) == values[names[j]], "Standard matches constructor default for " + names[j]);
            }
        }
        check(Values(-1) == null && Values(3) == null, "Invalid preset selection never defaults silently to Standard");

        settingsType.GetProperty("PoliticalBribeCostMultiplier").SetValue(settings, 2.5f);
        settingsType.GetProperty("EnablePartitionSuccession").SetValue(settings, false);
        var unrelated = settingsType.GetProperties().Where(p => !names.Contains(p.Name) && p.Name != "InstabilityPreset"
            && p.GetCustomAttributes(false).Any(a => a is BaseSettingPropertyAttribute)).ToDictionary(p => p, p => p.GetValue(settings));
        settingsType.GetProperty("RebellionThreshold").SetValue(settings, 187);
        int[] original = names.Select(Read).ToArray();
        selector.SelectedIndex = 1;
        check(names.Select(Read).SequenceEqual(original), "Selecting a template does not apply it or reset existing sliders");

        // Exercise the real MCM UI setters and undo stack without opening a native screen.
        string game = (string)AccessTools.Field(typeof(Program), "_game").GetValue(null);
        var native = new XmlDocument(); native.Load(Path.Combine(game, "Modules", "Native", "SubModule.xml"));
        var version = new Version(native.SelectSingleNode("/Module/Version/@value").Value.TrimStart('v', 'e'));
        string uiFolder = Path.Combine(game, "Modules", "Bannerlord.MBOptionScreen", "bin", "Win64_Shipping_Client");
        var uiPath = Directory.GetFiles(uiFolder, "Bannerlord.MBOptionScreen.v*.dll")
            .Select(p => new { Path = p, Version = new Version(Path.GetFileNameWithoutExtension(p).Substring("Bannerlord.MBOptionScreen.v".Length)) })
            .Where(p => p.Version <= version).OrderByDescending(p => p.Version).First().Path;
        var ui = Assembly.LoadFrom(uiPath);
        Type Ui(string name) => ui.GetType(name, true);
        object Blank(Type type) => FormatterServices.GetUninitializedObject(type);
        void Field(object obj, string name, object value) => AccessTools.Field(obj.GetType(), "<" + name + ">k__BackingField").SetValue(obj, value);
        var vmType = Ui("MCM.UI.GUI.ViewModels.SettingsPropertyVM");
        var settingsVM = Blank(Ui("MCM.UI.GUI.ViewModels.SettingsVM"));
        var undo = Activator.CreateInstance(Ui("MCM.UI.Actions.UndoRedoStack"), true);
        Field(settingsVM, "URS", undo);
        var group = Blank(Ui("MCM.UI.GUI.ViewModels.SettingsPropertyGroupVM"));
        var controls = (IList)Activator.CreateInstance(AccessTools.Property(group.GetType(), "SettingProperties").PropertyType);
        Field(group, "SettingProperties", controls);
        object Control(object owner, string name)
        {
            var property = owner.GetType().GetProperty(name);
            var attrs = property.GetCustomAttributes(false);
            var definition = new SettingsPropertyDefinition(attrs.OfType<IPropertyDefinitionBase>(),
                attrs.OfType<IPropertyGroupDefinition>().Single(), new PropertyRef(property, owner), '/');
            SettingsUtils.CheckIsValid(definition, owner);
            var vm = Blank(vmType);
            Field(vm, "SettingsVM", settingsVM);
            Field(vm, "SettingPropertyDefinition", definition);
            AccessTools.Property(vmType, "Group").SetValue(vm, group);
            return vm;
        }
        var sliders = names.ToDictionary(n => n, n => Control(settings, n));
        foreach (var slider in sliders.Values) controls.Add(slider);
        var button = Control(settings, "ApplyInstabilityPreset"); controls.Add(button);
        var intValue = AccessTools.Property(vmType, "IntValue");
        var click = AccessTools.Method(vmType, "OnValueClick");
        var undoAll = AccessTools.Method(undo.GetType(), "UndoAll");
        var clear = AccessTools.Method(undo.GetType(), "ClearStack");
        bool Dirty() => (bool)AccessTools.Property(undo.GetType(), "ChangesMade").GetValue(undo);
        var notifications = new HashSet<string>();
        foreach (var slider in sliders)
            ((ViewModel)slider.Value).PropertyChanged += (sender, args) => notifications.Add(slider.Key + ":" + args.PropertyName);
        var harmony = new Harmony("bellum.test.instability_presets");
        try
        {
            harmony.Patch(AccessTools.Method(presets, "ShowUnavailable"), prefix: new HarmonyMethod(typeof(InternalInstabilityPresetTests), nameof(Unavailable)));
            harmony.Patch(AccessTools.Method(typeof(BaseSettingsExtensions), "GetUnsortedSettingPropertyGroups"),
                prefix: new HarmonyMethod(typeof(InternalInstabilityPresetTests), nameof(SettingsGroups)));
            var patch = assembly.GetType("BellumCivile.Patches.InternalInstabilityPresetButtonPatch", true);
            check(harmony.CreateClassProcessor(patch).Patch().Count == 1, "Instability Apply patch compiles against " + Path.GetFileName(uiPath));
            click.Invoke(button, null);
            check(names.Select(Read).SequenceEqual(expected[1]) && Dirty(), "Apply updates all sliders through MCM and marks settings dirty");
            check(names.All(n => notifications.Contains(n + ":IntValue") && notifications.Contains(n + ":NumericValue")),
                "Every changed slider and numeric label receives MCM UI notifications");
            check(unrelated.All(p => Equals(p.Key.GetValue(settings), p.Value)), "Applying Reduced preserves every unrelated annotated preference");
            intValue.SetValue(sliders[names[0]], 200);
            check(Read(names[0]) == 200, "Manual adjustment remains available after Apply");
            undoAll.Invoke(undo, null);
            check(names.Select(Read).SequenceEqual(original) && !Dirty(), "MCM Cancel restores pre-Apply values including later manual edits");

            intValue.SetValue(sliders[names[0]], 160);
            selector.SelectedIndex = 2; click.Invoke(button, null);
            check(names.Select(Read).SequenceEqual(expected[2]), "Increased preset uses its approved values");
            selector.SelectedIndex = 0; click.Invoke(button, null);
            check(names.Select(Read).SequenceEqual(expected[0]) && unrelated.All(p => Equals(p.Key.GetValue(settings), p.Value)),
                "Standard resets only instability, not the rest of Bellum");
            undoAll.Invoke(undo, null);
            check(names.Select(Read).SequenceEqual(original), "Cancel restores the opening values after multiple presets and earlier edits");

            selector.SelectedIndex = 1; click.Invoke(button, null); clear.Invoke(undo, null);
            check(!Dirty() && names.Select(Read).SequenceEqual(expected[1]), "Clearing the MCM transaction after saving retains the applied values");
            click.Invoke(button, null);
            check(!Dirty(), "Reapplying the same preset is idempotent");
            intValue.SetValue(sliders[names[0]], 211); clear.Invoke(undo, null);
            selector.SelectedIndex = 2;
            check(Read(names[0]) == 211, "Changing the template later preserves saved manual adjustments");
            check(unrelated.All(p => Equals(p.Key.GetValue(settings), p.Value)), "Other preferences remain intact after all preset operations");

            var format = Activator.CreateInstance(typeof(SettingsUtils).Assembly.GetType("MCM.Implementation.JsonSettingsFormat"), new object[] { null });
            string json = (string)AccessTools.Method(format.GetType(), "SaveJson").Invoke(format, new[] { settings });
            check(json.Contains("InstabilityPreset") && json.Contains("211") && !json.Contains("ApplyInstabilityPreset"),
                "MCM saves the selected template and custom slider values, never the button action");
            object Load(string text)
            {
                var loaded = Activator.CreateInstance(settingsType, true);
                AccessTools.Method(format.GetType(), "LoadFromJson").Invoke(format, new[] { loaded, text });
                return loaded;
            }
            var restored = Load(json);
            check(names.All(n => (int)settingsType.GetProperty(n).GetValue(restored) == Read(n))
                && ((Dropdown<string>)settingsType.GetProperty("InstabilityPreset").GetValue(restored)).SelectedIndex == 2,
                "Settings reload preserves manual values without automatically applying the saved Increased template");
            var legacy = Load("{\"RebellionThreshold\":187,\"ElectionChallengeChancePercent\":45}");
            check((int)settingsType.GetProperty("RebellionThreshold").GetValue(legacy) == 187
                && (int)settingsType.GetProperty("ElectionChallengeChancePercent").GetValue(legacy) == 45
                && ((Dropdown<string>)settingsType.GetProperty("InstabilityPreset").GetValue(legacy)).SelectedIndex == 0,
                "Older settings gain the Standard selector without resetting their existing preferences");
            var copy = AccessTools.Method(settingsType, "CopyAsNew").Invoke(settings, null);
            check(names.All(n => (int)settingsType.GetProperty(n).GetValue(copy) == Read(n))
                && Equals(settingsType.GetProperty("ApplyInstabilityPreset").GetValue(copy), settingsType.GetProperty("ApplyInstabilityPreset").GetValue(settings)),
                "MCM settings copies preserve sliders and do not capture a different settings instance in the button");

            int before = _unavailable;
            controls.Remove(sliders[names[0]]);
            var beforeFailure = names.Select(Read).ToArray();
            click.Invoke(button, null);
            check(_unavailable == before + 1 && names.Select(Read).SequenceEqual(beforeFailure) && !Dirty(),
                "Missing UI control rejects the entire preset before changing anything");
            controls.Add(sliders[names[0]]);
            settingsType.GetProperty("InstabilityPreset").SetValue(settings, null);
            click.Invoke(button, null);
            check(_unavailable == before + 2 && names.Select(Read).SequenceEqual(beforeFailure), "Missing selector fails without changing settings");
            var other = new OtherSettings(); click.Invoke(Control(other, "ApplyInstabilityPreset"), null);
            check(other.Clicks == 1, "Another mod's identically named button is unaffected");
        }
        finally { harmony.UnpatchAll(harmony.Id); }
    }
}
