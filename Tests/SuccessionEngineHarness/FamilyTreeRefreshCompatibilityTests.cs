using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Serialization;
using BellumCivile;
using HarmonyLib;

internal static class FamilyTreeRefreshCompatibilityTests
{
    private static int _bodyCalls, _otherPrefixes, _familyPostfixes;
    private static readonly string[] Labels = { "NameText", "InfoText", "ClanText", "AlliesText", "EnemiesText", "SkillsText" };

    // Only the native refresh body is substituted: the actual third-party prefix and Harmony dispatch run unchanged.
    private static IEnumerable<CodeInstruction> FixtureBody(IEnumerable<CodeInstruction> instructions) => new[] {
        new CodeInstruction(OpCodes.Ldarg_0),
        new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(FamilyTreeRefreshCompatibilityTests), nameof(FillPage))),
        new CodeInstruction(OpCodes.Ret) };
    private static void FillPage(object page)
    {
        _bodyCalls++;
        foreach (string label in Labels) AccessTools.Property(page.GetType(), label).SetValue(page, label + " initialized");
    }
    private static void OtherPrefix() => _otherPrefixes++;
    private static void FamilyPostfix() => _familyPostfixes++;
    private static void PrefixModifier() { }

    internal static void Run(Action<bool, string> check, string originalDll, string workingDll)
    {
        var core = typeof(FeudalTitleRecord).Assembly;
        var adapter = core.GetType("BellumCivile.Patches.FamilyTreeRefreshCompatibility", true);
        var apply = AccessTools.Method(adapter, "TryApply");
        check(PatchProcessor.GetOriginalInstructions(AccessTools.Method(typeof(BellumCivile.SubModule), "OnBeforeInitialModuleScreenSetAsRoot"))
            .Any(i => i.Calls(apply)), "The adapter is invoked after module loading rather than during hero-page construction");
        var supported = AccessTools.Method(adapter, "IsKnownPrefix");
        var original = Assembly.LoadFrom(originalDll);
        var patchType = original.GetType("Bannerlord.FamilyTree.Patches.EncyclopediaHeroPageVMPatch3", true);
        var prefix = AccessTools.DeclaredMethod(patchType, "Prefix");
        check((bool)supported.Invoke(null, new object[] { prefix }), "The exact original Family Tree refresh blocker is recognized");
        var toggle = AccessTools.DeclaredField(patchType, "toggle");
        var instructions = PatchProcessor.GetOriginalInstructions(prefix).ToArray();
        instructions[5] = new CodeInstruction(OpCodes.Ldc_I4_1);
        check(!(bool)AccessTools.Method(adapter, "MatchesToggleBody").Invoke(null, new object[] { toggle, instructions }),
            "A changed toggle implementation is not treated as the known broken prefix");
        check(!(bool)supported.Invoke(null, new object[] { AccessTools.Method(typeof(FamilyTreeRefreshCompatibilityTests), nameof(OtherPrefix)) }),
            "Unrelated refresh prefixes are never selected for removal");

        var pageType = AccessTools.TypeByName("TaleWorlds.CampaignSystem.ViewModelCollection.Encyclopedia.Pages.EncyclopediaHeroPageVM");
        var refresh = AccessTools.DeclaredMethod(pageType, "RefreshValues");
        var family = new Harmony("bellum.tests.original_family_tree");
        var other = new Harmony("bellum.tests.other_refresh_patches");
        var bridge = new Harmony("bellum.tests.family_refresh_bridge");
        bool HasPrefix(MethodInfo method) => Harmony.GetPatchInfo(refresh)?.Prefixes.Any(p => p.PatchMethod == method) == true;
        try
        {
            other.Patch(refresh, prefix: new HarmonyMethod(typeof(FamilyTreeRefreshCompatibilityTests), nameof(OtherPrefix)),
                transpiler: new HarmonyMethod(typeof(FamilyTreeRefreshCompatibilityTests), nameof(FixtureBody)));
            family.Patch(refresh, postfix: new HarmonyMethod(typeof(FamilyTreeRefreshCompatibilityTests), nameof(FamilyPostfix)));
            apply.Invoke(null, new object[] { bridge });
            check(Harmony.GetPatchInfo(refresh).Postfixes.Count == 1,
                "Running compatibility before Family Tree installs its prefix leaves existing patches alone");

            family.Patch(refresh, prefix: new HarmonyMethod(prefix));
            toggle.SetValue(null, false);
            var blank = FormatterServices.GetUninitializedObject(pageType);
            _bodyCalls = _otherPrefixes = _familyPostfixes = 0;
            refresh.Invoke(blank, null);
            check(_bodyCalls == 0 && Labels.All(label => AccessTools.Property(pageType, label).GetValue(blank) == null),
                "The actual Family Tree prefix reproduces the blank first hero page before compatibility");

            other.Patch(prefix, prefix: new HarmonyMethod(typeof(FamilyTreeRefreshCompatibilityTests), nameof(PrefixModifier)));
            apply.Invoke(null, new object[] { bridge });
            check(HasPrefix(prefix), "A prefix already modified by another compatibility patch is left intact");
            other.Unpatch(prefix, AccessTools.Method(typeof(FamilyTreeRefreshCompatibilityTests), nameof(PrefixModifier)));
            apply.Invoke(null, new object[] { bridge });
            check(!HasPrefix(prefix), "Only the exact blocking prefix registration is removed");
            check(Harmony.GetPatchInfo(refresh).Prefixes.Any(p => p.owner == other.Id)
                && Harmony.GetPatchInfo(refresh).Postfixes.Any(p => p.owner == family.Id)
                && Harmony.GetPatchInfo(refresh).Transpilers.Any(p => p.owner == other.Id),
                "Other owners, Family Tree postfixes and existing refresh transpilers remain installed");

            _bodyCalls = _otherPrefixes = _familyPostfixes = 0;
            foreach (int number in new[] { 1, 2, 3 })
            {
                var page = FormatterServices.GetUninitializedObject(pageType);
                refresh.Invoke(page, null);
                check(_bodyCalls == number && Labels.All(label => (string)AccessTools.Property(pageType, label).GetValue(page) == label + " initialized"),
                    "Each hero page receives exactly one native refresh and initialized labels after compatibility: " + number);
            }
            check(_otherPrefixes == 3 && _familyPostfixes == 3, "Remaining prefixes and postfixes continue running on every page");
            apply.Invoke(null, new object[] { bridge });
            check(!HasPrefix(prefix) && Harmony.GetPatchInfo(refresh).Prefixes.Count == 1,
                "Repeated compatibility setup does not alter other registrations");

            var future = FuturePrefix(prefix.GetParameters()[0].ParameterType);
            family.Patch(refresh, prefix: new HarmonyMethod(future));
            apply.Invoke(null, new object[] { bridge });
            check(HasPrefix(future), "A different Family Tree build with the same type name is left untouched");

            var working = Assembly.LoadFrom(workingDll);
            check(working.GetType("Bannerlord.FamilyTree.Patches.EncyclopediaHeroPageVMPatch3") == null,
                "The working workshop fork does not contain the targeted prefix");
        }
        finally
        {
            family.UnpatchAll(family.Id);
            other.UnpatchAll(other.Id);
            bridge.UnpatchAll(bridge.Id);
        }
    }

    private static MethodInfo FuturePrefix(Type argument)
    {
        var name = new AssemblyName("Bannerlord.FamilyTree.1.4.8") { Version = new Version(1, 4, 8, 1) };
        var assembly = AppDomain.CurrentDomain.DefineDynamicAssembly(name, AssemblyBuilderAccess.Run);
        var type = assembly.DefineDynamicModule(name.Name).DefineType(
            "Bannerlord.FamilyTree.Patches.EncyclopediaHeroPageVMPatch3", TypeAttributes.Public);
        var method = type.DefineMethod("Prefix", MethodAttributes.Public | MethodAttributes.Static, typeof(bool), new[] { argument });
        method.DefineParameter(1, ParameterAttributes.None, "__instance");
        var il = method.GetILGenerator(); il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Ret);
        return type.CreateType().GetMethod("Prefix");
    }
}
