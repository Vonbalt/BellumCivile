using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using MCM.Common;

namespace BellumCivile.Patches
{
    [HarmonyPatch]
    internal static class InternalInstabilityPresetButtonPatch
    {
        private static MethodInfo _target;
        private static PropertyInfo _reference, _group, _properties, _intValue;

        private static bool Prepare()
        {
            // MCM's UI assembly is game-versioned; its public core library does not expose these VMs.
            var type = AccessTools.TypeByName("MCM.UI.GUI.ViewModels.SettingsPropertyVM");
            if (type == null) return false;
            _target = AccessTools.Method(type, "OnValueClick", Type.EmptyTypes);
            _reference = AccessTools.Property(type, "PropertyReference");
            _group = AccessTools.Property(type, "Group");
            _properties = _group == null ? null : AccessTools.Property(_group.PropertyType, "SettingProperties");
            _intValue = AccessTools.Property(type, "IntValue");
            return _target != null && _reference != null && _properties != null && _intValue?.CanWrite == true;
        }

        private static MethodBase TargetMethod() => _target;

        private static bool Prefix(object __instance)
        {
            if (!(_reference.GetValue(__instance) is PropertyRef button)
                || !(button.Instance is BellumCivileSettings settings)
                || button.PropertyInfo.Name != nameof(BellumCivileSettings.ApplyInstabilityPreset))
                return true;

            try
            {
                var values = InternalInstabilityPresets.GetValues(settings.InstabilityPreset?.SelectedIndex ?? -1);
                var group = _group.GetValue(__instance);
                if (values == null || group == null || !(_properties.GetValue(group) is IEnumerable controls))
                    return true;

                var sliders = new Dictionary<string, object>();
                foreach (var control in controls)
                {
                    if (_reference.GetValue(control) is PropertyRef reference
                        && ReferenceEquals(reference.Instance, settings)
                        && reference.Type == typeof(int) && values.ContainsKey(reference.PropertyInfo.Name))
                        sliders.Add(reference.PropertyInfo.Name, control);
                }

                // Validate the entire group before editing. Never fall back to untracked direct assignments.
                if (sliders.Count != values.Count)
                    return true;
                foreach (var value in values)
                    _intValue.SetValue(sliders[value.Key], value.Value);
                return false;
            }
            catch (Exception ex)
            {
                BellumCivileLogger.Log($"Internal instability preset UI failure: {ex}");
                return true;
            }
        }
    }
}
