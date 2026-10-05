using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.SceneInformationPopupTypes;
using TaleWorlds.Localization;

namespace BellumCivile.Patches
{
    internal static class ArtemRealmNameCompatibility
    {
        private const string ViewTypeName = "ArtemsBetterUIVisuals.BetterUIVisualsKingdomLabelsView";
        private const string LabelTypeName = "ArtemsBetterUIVisuals.BetterUIVisualsKingdomLabelVM";
        private static readonly ConditionalWeakTable<object, ViewLabels> Views = new ConditionalWeakTable<object, ViewLabels>();
        private static readonly MethodInfo FormalName = AccessTools.Method(typeof(CampaignSceneNotificationHelper), "GetFormalNameForKingdom");
        private static readonly MethodInfo NameGetter = AccessTools.PropertyGetter(typeof(Kingdom), "Name");
        private static bool _attempted;
        private static PropertyInfo _labelName;
        private static ConstructorInfo _labelConstructor;
        [ThreadStatic] private static Capture _capture;

        private sealed class Label
        {
            public object ViewModel;
            public Kingdom Kingdom;
            public bool Alternate;
            public string LastName;
        }

        private sealed class ViewLabels
        {
            public readonly List<Label> Labels = new List<Label>();
            public float UntilRefresh;
            public bool Failed;
        }

        private sealed class Capture
        {
            public object View;
            public Kingdom Kingdom;
            public bool Alternate;
        }

        internal static void TryApply(Harmony harmony)
        {
            if (_attempted || harmony == null) return;
            Type view = AccessTools.TypeByName(ViewTypeName);
            Type label = AccessTools.TypeByName(LabelTypeName);
            if (view == null || label == null) return;
            _attempted = true;
            try
            {
                MethodInfo rebuild = AccessTools.DeclaredMethod(view, "RebuildLabels");
                MethodInfo update = AccessTools.DeclaredMethod(view, "OnMapScreenUpdate");
                _labelName = label.GetProperty("KingdomName");
                _labelConstructor = label.GetConstructor(new[] { typeof(string) });
                if (rebuild == null || update == null || _labelConstructor == null || _labelName?.CanWrite != true)
                    throw new InvalidOperationException("Unsupported kingdom-label API.");
                // Validate all anchors before touching the optional mod. No guessed
                // ordering of kingdoms, settlement lists, or private label fields.
                if (!SupportsInstructions(PatchProcessor.GetOriginalInstructions(rebuild)))
                    throw new InvalidOperationException("Unsupported kingdom-label construction path.");
                harmony.Patch(rebuild,
                    prefix: Method(nameof(RebuildPrefix)),
                    transpiler: Method(nameof(RebuildTranspiler)),
                    finalizer: Method(nameof(RebuildFinalizer)));
                harmony.Patch(update, postfix: Method(nameof(UpdatePostfix)));
                BellumCivileLogger.Log("Enabled Artem's Better UI Visuals realm-name compatibility.");
            }
            catch (Exception ex)
            {
                BellumCivileLogger.Log($"Artem realm-name adapter unavailable; native formal-name integration remains active: {ex.GetType().Name}: {ex.Message}");
            }
        }

        private static HarmonyMethod Method(string name) => new HarmonyMethod(typeof(ArtemRealmNameCompatibility), name);

        private static bool SupportsInstructions(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.ToList();
            return code.Count(i => i.Calls(FormalName)) == 1
                && code.Count(i => i.Calls(NameGetter)) == 1
                && code.Count(i => i.opcode == OpCodes.Newobj && Equals(i.operand, _labelConstructor)) == 1;
        }

        private static IEnumerable<CodeInstruction> RebuildTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.ToList();
            if (!SupportsInstructions(code))
                throw new InvalidOperationException("Kingdom-label IL changed before patching.");
            foreach (CodeInstruction instruction in code)
            {
                if (instruction.Calls(FormalName) || instruction.Calls(NameGetter))
                {
                    bool formal = instruction.Calls(FormalName);
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = AccessTools.Method(typeof(ArtemRealmNameCompatibility),
                        formal ? nameof(ReadFormalName) : nameof(ReadName));
                }
                yield return instruction;
                if (instruction.opcode == OpCodes.Newobj && Equals(instruction.operand, _labelConstructor))
                {
                    yield return new CodeInstruction(OpCodes.Dup);
                    yield return new CodeInstruction(OpCodes.Ldarg_0);
                    yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(ArtemRealmNameCompatibility), nameof(RegisterLabel)));
                }
            }
        }

        private static void RebuildPrefix(object __instance, out Capture __state)
        {
            __state = _capture;
            _capture = new Capture { View = __instance };
            Views.GetOrCreateValue(__instance).Labels.Clear();
        }

        private static Exception RebuildFinalizer(Exception __exception, Capture __state)
        {
            _capture = __state;
            return __exception;
        }

        private static TextObject ReadFormalName(Kingdom kingdom)
        {
            TextObject result = CampaignSceneNotificationHelper.GetFormalNameForKingdom(kingdom);
            CaptureRealm(kingdom, false);
            return result;
        }

        private static TextObject ReadName(Kingdom kingdom)
        {
            TextObject result = kingdom.Name;
            CaptureRealm(kingdom, true);
            return result;
        }

        private static void CaptureRealm(Kingdom kingdom, bool alternate)
        {
            if (_capture == null) return;
            _capture.Kingdom = kingdom;
            _capture.Alternate = alternate;
        }

        private static void RegisterLabel(object viewModel, object view)
        {
            if (_capture?.View != view || _capture.Kingdom == null) return;
            ViewLabels state = Views.GetOrCreateValue(view);
            var label = new Label { ViewModel = viewModel, Kingdom = _capture.Kingdom, Alternate = _capture.Alternate };
            state.Labels.Add(label);
            _capture.Kingdom = null;
            RefreshLabel(state, label);
        }

        private static void UpdatePostfix(object __instance, float dt)
        {
            if (!Views.TryGetValue(__instance, out ViewLabels state) || state.Failed) return;
            state.UntilRefresh -= dt;
            if (state.UntilRefresh > 0f) return;
            state.UntilRefresh = 1f;
            foreach (Label label in state.Labels) RefreshLabel(state, label);
        }

        private static void RefreshLabel(ViewLabels state, Label label)
        {
            if (state.Failed) return;
            try
            {
                string name = ResolveLabelName(label.Kingdom, label.Alternate);
                if (name == label.LastName) return;
                _labelName.SetValue(label.ViewModel, name);
                label.LastName = name;
            }
            catch (Exception ex)
            {
                state.Failed = true;
                BellumCivileLogger.Log($"Artem realm-name refresh stopped for this view: {ex.GetType().Name}: {ex.Message}");
            }
        }

        internal static string ResolveLabelName(Kingdom kingdom, bool alternate)
        {
            if (DynamicKingdomTitleNameHelper.TryResolveDisplayText(kingdom, KingdomDisplayNameField.Name, out TextObject text))
                return text.ToString();
            // In Native mode (or without a Bellum name), preserve Artem's own choice.
            return alternate
                ? new TextObject("{=ABUV16}Kingdom of {KINGDOM_NAME}").SetTextVariable("KINGDOM_NAME", kingdom.Name).ToString()
                : CampaignSceneNotificationHelper.GetFormalNameForKingdom(kingdom).ToString();
        }
    }
}
