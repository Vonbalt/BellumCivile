using System;
using System.Reflection;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using BellumCivile.Patches;
using HarmonyLib;
using TaleWorlds.Core;

internal static class CampaignUiStartupProbe
{
    // Run in a fresh process: failed CLR type initialization cannot be retried.
    internal static void Run(Action<bool, string> check)
    {
        check(AccessTools.Field(typeof(GameTexts), "_gameTextManager").GetValue(null) == null,
            "Startup probe begins before native game texts are initialized");
        var harmony = new Harmony("bellum.tests.campaign-ui-startup");
        Assembly.Load("TaleWorlds.CampaignSystem.ViewModelCollection");
        var patchTypes = typeof(KingdomPolicyButtonPatch).Assembly.GetTypes()
            .Where(t => t.GetCustomAttributes(typeof(HarmonyPatch), true).Length > 0)
            .Where(t => !(bool)AccessTools.Method(typeof(BellumCivile.SubModule), "ShouldSkipOptionalPatch")
                .Invoke(null, new object[] { t })).ToArray();
        Console.WriteLine("Applying " + patchTypes.Length + " early patches before game-text initialization");
        foreach (var type in patchTypes)
        {
            harmony.CreateClassProcessor(type).Patch();
        }
        var hostagePatch = typeof(KingdomPolicyButtonPatch).Assembly.GetType("BellumCivile.Patches.HostageEncyclopediaHistoryPatch");
        check(!patchTypes.Contains(hostagePatch), "Hostage encyclopedia patch is deferred beyond module load");
        var familyMixin = typeof(KingdomPolicyButtonPatch).Assembly.GetType("BellumCivile.ViewModelMixin.EncyclopediaHeroFamilyMixin", true);
        var familyAttribute = familyMixin.GetCustomAttributes(false).Single(a => a.GetType().Name == "ViewModelMixinAttribute");
        string familyRefresh = (string)AccessTools.Property(familyAttribute.GetType(), "RefreshMethodName").GetValue(familyAttribute);
        var page = AccessTools.TypeByName("TaleWorlds.CampaignSystem.ViewModelCollection.Encyclopedia.Pages.EncyclopediaHeroPageVM");
        var uiExtender = Assembly.Load("Bannerlord.UIExtenderEx");
        var mixinPatches = uiExtender.GetType("Bannerlord.UIExtenderEx.Patches.ViewModelWithMixinPatch", true);
        harmony.Patch(AccessTools.Method(page, familyRefresh), transpiler: new HarmonyMethod(
            AccessTools.Method(mixinPatches, "ViewModel_Refresh_Transpiler")));
        check(AccessTools.Field(typeof(GameTexts), "_gameTextManager").GetValue(null) == null,
            "Family mixin registers through the real UIExtender refresh transpiler before game texts exist");
        AccessTools.Method(typeof(KingdomPolicyButtonPatch).Assembly.GetType("BellumCivile.Patches.FamilyTreeRefreshCompatibility", true), "TryApply")
            .Invoke(null, new object[] { harmony });
        check(AccessTools.Field(typeof(GameTexts), "_gameTextManager").GetValue(null) == null,
            "Optional Family Tree refresh inspection does not initialize native game texts");
        GameTexts.Initialize(new GameTextManager());
        var module = (BellumCivile.SubModule)FormatterServices.GetUninitializedObject(typeof(BellumCivile.SubModule));
        AccessTools.Field(typeof(BellumCivile.SubModule), "_harmony").SetValue(module, harmony);
        var applyHistory = AccessTools.Method(typeof(BellumCivile.SubModule), "TryPatchHostageEncyclopediaHistory");
        applyHistory.Invoke(module, null);
        applyHistory.Invoke(module, null);
        check((bool)AccessTools.Field(typeof(BellumCivile.SubModule), "_hostageEncyclopediaHistoryPatched").GetValue(module),
            "Deferred hostage history patch installs successfully");
        var refresh = AccessTools.Method(AccessTools.TypeByName(
            "TaleWorlds.CampaignSystem.ViewModelCollection.Encyclopedia.Pages.EncyclopediaHeroPageVM"), "Refresh");
        check(Harmony.GetPatchInfo(refresh).Postfixes.Count(p => p.PatchMethod.DeclaringType == hostagePatch) == 1,
            "Deferred hostage history patch installs only once across repeated game starts");
        var helper = AccessTools.TypeByName("TaleWorlds.CampaignSystem.ViewModelCollection.CampaignUIHelper");
        RuntimeHelpers.RunClassConstructor(helper.TypeHandle);
        check(true, "Startup patches leave CampaignUIHelper usable after game-text initialization");
        harmony.UnpatchAll(harmony.Id);
    }
}
