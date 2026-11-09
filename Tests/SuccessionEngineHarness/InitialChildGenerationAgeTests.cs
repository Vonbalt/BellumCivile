using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Serialization;
using BellumCivile;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.Core;

internal static class InitialChildGenerationAgeTests
{
    private static bool _custom;
    private static int _majority, _minimum, _maximum, _rolls;
    private static bool Enabled(ref bool __result) { __result = _custom; return false; }
    private static bool Majority(ref int __result) { __result = _majority; return false; }
    private static bool Roll(int __0, int __1, ref int __result)
    { _minimum = __0; _maximum = __1; _rolls++; __result = __1 - 1; return false; }

    private static T Blank<T>() { return (T)FormatterServices.GetUninitializedObject(typeof(T)); }
    private static List<CodeInstruction> Rewrite(MethodInfo transpiler, IEnumerable<CodeInstruction> code)
    { return ((IEnumerable<CodeInstruction>)transpiler.Invoke(null, new object[] { code })).ToList(); }
    private static bool Rejects(MethodInfo transpiler, IEnumerable<CodeInstruction> code)
    {
        try { Rewrite(transpiler, code); return false; }
        catch (TargetInvocationException ex) { return ex.InnerException is InvalidOperationException; }
    }

    internal static void Run(Action<bool, string> check)
    {
        Type patch = typeof(BellumAgeModel).Assembly.GetType("BellumCivile.Patches.InitialChildGenerationAgePatch", true);
        MethodInfo transpiler = AccessTools.Method(patch, "Transpiler");
        MethodInfo ceiling = AccessTools.Method(patch, "MaximumAge");
        MethodInfo target = AccessTools.Method(typeof(InitialChildGenerationCampaignBehavior), "OnNewGameCreatedPartialFollowUp");
        MethodInfo random = AccessTools.Method(typeof(MBRandom), "RandomInt", new[] { typeof(int), typeof(int) });
        MethodInfo create = AccessTools.Method(typeof(HeroCreator), "CreateChild");
        var original = PatchProcessor.GetOriginalInstructions(target).ToList();
        var rewritten = Rewrite(transpiler, original.Select(c => new CodeInstruction(c)));
        int changed = Enumerable.Range(0, original.Count).Single(i => original[i].opcode != rewritten[i].opcode
            || !Equals(original[i].operand, rewritten[i].operand));
        check(original[changed].LoadsConstant(18) && rewritten[changed].Calls(ceiling)
            && original[changed - 1].LoadsConstant(2) && original[changed + 1].Calls(random),
            "Only the exclusive ceiling of the native starting-child age roll changes");
        check(original.Count == rewritten.Count && rewritten.Count(c => c.Calls(create)) == 1,
            "Native child creation, family selection, equipment and population logic remain in place");
        check(Rejects(transpiler, new CodeInstruction[0]), "An absent native age roll is rejected");
        var altered = original.Select(c => new CodeInstruction(c)).ToList();
        altered[changed].operand = (sbyte)19;
        check(Rejects(transpiler, altered), "An unfamiliar age ceiling is not silently overwritten");
        altered = original.Select(c => new CodeInstruction(c)).ToList();
        altered.AddRange(new[] { new CodeInstruction(OpCodes.Ldc_I4_2), new CodeInstruction(OpCodes.Ldc_I4_S, (sbyte)18),
            new CodeInstruction(OpCodes.Call, random) });
        check(Rejects(transpiler, altered), "Ambiguous matching age rolls are rejected");
        altered = original.Select(c => new CodeInstruction(c)).ToList();
        altered.RemoveAll(c => c.Calls(create));
        check(Rejects(transpiler, altered), "A changed child-creation path is rejected");
        var dynamic = new DynamicMethod("child_age_labels", typeof(void), Type.EmptyTypes);
        Label label = dynamic.GetILGenerator().DefineLabel();
        altered = original.Select(c => new CodeInstruction(c)).ToList();
        altered[changed].labels.Add(label);
        altered[changed].blocks.Add(new ExceptionBlock(ExceptionBlockType.BeginExceptionBlock));
        rewritten = Rewrite(transpiler, altered);
        check(rewritten[changed].labels.Contains(label) && rewritten[changed].blocks.Count == 1,
            "The rewritten ceiling retains native labels and exception metadata");

        var h = new Harmony("bellum.tests.initial_child_ages");
        Campaign previous = Campaign.Current;
        try
        {
            Type options = typeof(BellumAgeModel).Assembly.GetType("BellumCivile.BellumCivileOptions", true);
            h.Patch(AccessTools.PropertyGetter(options, "EnableCustomAdulthoodAge"),
                prefix: new HarmonyMethod(typeof(InitialChildGenerationAgeTests), "Enabled"));
            h.Patch(AccessTools.PropertyGetter(options, "AdulthoodAge"),
                prefix: new HarmonyMethod(typeof(InitialChildGenerationAgeTests), "Majority"));
            h.Patch(random, prefix: new HarmonyMethod(typeof(InitialChildGenerationAgeTests), "Roll"));
            h.CreateClassProcessor(patch).Patch();
            check(PatchProcessor.GetCurrentInstructions(target).Count(c => c.Calls(ceiling)) == 1,
                "Harmony installs exactly one ceiling hook in the actual game method");
            var campaign = Blank<Campaign>();
            var models = Blank<GameModels>();
            AccessTools.PropertySetter(typeof(Campaign), "Current").Invoke(null, new object[] { campaign });
            AccessTools.Field(typeof(Campaign), "_gameModels").SetValue(campaign, models);
            AccessTools.PropertySetter(typeof(GameModels), "AgeModel").Invoke(models, new object[] { new BellumAgeModel() });
            var sample = new DynamicMethod("starting_child_age_roll", typeof(int), Type.EmptyTypes, patch, true);
            ILGenerator il = sample.GetILGenerator();
            il.Emit(OpCodes.Ldc_I4_2); il.Emit(OpCodes.Call, ceiling); il.Emit(OpCodes.Call, random); il.Emit(OpCodes.Ret);
            var ageRoll = (Func<int>)sample.CreateDelegate(typeof(Func<int>));
            _custom = true;
            for (_majority = 16; _majority <= 21; _majority++)
            {
                _rolls = 0;
                int age = ageRoll();
                check(age == _majority - 1 && _minimum == 2 && _maximum == _majority && _rolls == 1,
                    "Starting children stay below custom adulthood " + _majority + " with one unchanged random draw");
            }
            _majority = 16; _custom = false;
            check(ageRoll() == 17 && _maximum == 18, "Disabling custom adulthood preserves the vanilla 2-17 range");
            _custom = true; _majority = 21;
            AccessTools.PropertySetter(typeof(GameModels), "AgeModel").Invoke(models, new object[] { new DefaultAgeModel() });
            check(ageRoll() == 17 && _maximum == 18, "Another active age model takes precedence over Bellum's configured value");
            AccessTools.PropertySetter(typeof(Campaign), "Current").Invoke(null, new object[] { null });
            _majority = 16;
            check((int)ceiling.Invoke(null, null) == 16, "Missing campaign context falls back to the configured adulthood age");
            check(Harmony.GetAllPatchedMethods().Where(m => Harmony.GetPatchInfo(m).Owners.Contains(h.Id))
                .Count(m => m.DeclaringType == typeof(InitialChildGenerationCampaignBehavior)) == 1,
                "Only the new-game child-generation handler is patched");
        }
        finally
        {
            h.UnpatchAll(h.Id);
            AccessTools.PropertySetter(typeof(Campaign), "Current").Invoke(null, new object[] { previous });
        }
    }
}
