using System;
using System.Collections.Generic;
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
        var bannerPatch = typeof(KingdomPolicyButtonPatch).Assembly.GetType("BellumCivile.Patches.LordshipMapBannerPatch", true);
        check(!AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == "SandBox.ViewModelCollection"),
            "Startup probe begins before the optional Sandbox view-model assembly is loaded");
        AccessTools.Method(bannerPatch, "Initialize").Invoke(null, null);
        check(!(bool)AccessTools.Field(bannerPatch, "_installed").GetValue(null)
            && !(bool)AccessTools.Field(bannerPatch, "_unavailable").GetValue(null)
            && !AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == "SandBox.ViewModelCollection"),
            "Missing Sandbox UI defers banner registration without forcing assembly loading or permanently disabling it");
        var harmony = new Harmony("bellum.tests.campaign-ui-startup");
        Assembly.Load("TaleWorlds.CampaignSystem.ViewModelCollection");
        var patchTypes = typeof(KingdomPolicyButtonPatch).Assembly.GetTypes()
            .Where(t => t.GetCustomAttributes(typeof(HarmonyPatch), true).Length > 0)
            .Where(t => !(bool)AccessTools.Method(typeof(BellumCivile.SubModule), "ShouldSkipOptionalPatch")
                .Invoke(null, new object[] { t })).ToArray();
        foreach (string name in new[] { "LordshipVillageIncomePatch", "LordshipSurplusPatch" })
            check(!patchTypes.Any(t => t.Name == name), "Finance targets are not patched at module startup: " + name);
        Console.WriteLine("Applying " + patchTypes.Length + " early patches before game-text initialization");
        var failures = new List<string>();
        foreach (var type in patchTypes)
        {
            try
            {
                harmony.CreateClassProcessor(type).Patch();
            }
            catch (Exception ex)
            {
                failures.Add(type.FullName);
                Console.Error.WriteLine("PATCH FAILED: " + type.FullName + "\n" + ex);
            }
        }
        check(failures.Count == 0, "Every early Harmony patch binds: " + string.Join(", ", failures));
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
        var clanPage = AccessTools.TypeByName("TaleWorlds.CampaignSystem.ViewModelCollection.Encyclopedia.Pages.EncyclopediaClanPageVM");
        harmony.Patch(AccessTools.Method(clanPage, "RefreshValues"), transpiler: new HarmonyMethod(
            AccessTools.Method(mixinPatches, "ViewModel_Refresh_Transpiler")));
        check(AccessTools.Field(typeof(GameTexts), "_gameTextManager").GetValue(null) == null,
            "Clan household mixin registration does not initialize native game texts early");
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
        foreach (var name in new[] { "EncyclopediaHeroRelationThresholdPatch", "EncyclopediaClanSuccessionPatch" })
        {
            var type = typeof(KingdomPolicyButtonPatch).Assembly.GetType("BellumCivile.Patches." + name, true);
            harmony.CreateClassProcessor(type).Patch();
            check(true, "Deferred patch binds: " + name);
        }
        var helper = AccessTools.TypeByName("TaleWorlds.CampaignSystem.ViewModelCollection.CampaignUIHelper");
        RuntimeHelpers.RunClassConstructor(helper.TypeHandle);
        check(true, "Startup patches leave CampaignUIHelper usable after game-text initialization");
        var occupationDisplay = typeof(KingdomPolicyButtonPatch).Assembly.GetType("BellumCivile.Patches.HouseholdOccupationDisplayPatch", true);
        check(!patchTypes.Contains(occupationDisplay), "Retainer occupation display is not patched before native game texts exist");
        AccessTools.Method(occupationDisplay, "Initialize").Invoke(null, null);
        AccessTools.Method(occupationDisplay, "Initialize").Invoke(null, null);
        foreach (var target in new[] { AccessTools.Method(typeof(Helpers.HeroHelper), "GetCharacterTypeName"),
            AccessTools.Method(helper, "GetHeroOccupationName") })
            check(Harmony.GetPatchInfo(target).Postfixes.Count(p => p.PatchMethod.DeclaringType == occupationDisplay) == 1,
                "Deferred retainer display installs once for " + target.Name);
        var lordships = typeof(KingdomPolicyButtonPatch).Assembly.GetType("BellumCivile.Patches.LordshipAppointmentPatches", true);
        AccessTools.Method(lordships, "Initialize").Invoke(null, null);
        AccessTools.Method(lordships, "Initialize").Invoke(null, null);
        var governor = AccessTools.Method(typeof(TaleWorlds.CampaignSystem.Actions.ChangeGovernorAction), "Apply");
        check(Harmony.GetPatchInfo(governor).Prefixes.Count(p => p.PatchMethod.DeclaringType == lordships) == 1,
            "Deferred lordship appointment/UI hooks install successfully and only once");
        var armySelection = AccessTools.Method(typeof(TaleWorlds.CampaignSystem.GameComponents.DefaultArmyManagementCalculationModel), "CanLordCreateArmy");
        check(Harmony.GetPatchInfo(armySelection).Prefixes.Count(p => p.PatchMethod.DeclaringType == lordships) == 1
            && Harmony.GetPatchInfo(armySelection).Finalizers.Count(p => p.PatchMethod.DeclaringType == lordships) == 1,
            "Military army selection retains its scoped lordship check without restricting commercial work");
        var vassal = typeof(KingdomPolicyButtonPatch).Assembly.GetType("BellumCivile.Patches.VassalLordshipPatches", true);
        AccessTools.Method(vassal, "Initialize").Invoke(null, null);
        AccessTools.Method(vassal, "Initialize").Invoke(null, null);
        var oath = AccessTools.Method(typeof(TaleWorlds.CampaignSystem.CampaignBehaviors.LordConversationsCampaignBehavior),
            "conversation_player_is_accepted_as_a_vassal_on_consequence");
        check(Harmony.GetPatchInfo(oath).Postfixes.Count(p => p.PatchMethod.DeclaringType == vassal) == 1,
            "Deferred vassal grant and local village hooks install once after game texts exist");
        var notices = typeof(KingdomPolicyButtonPatch).Assembly.GetType("BellumCivile.Patches.TitleAcquisitionNotificationPatch", true);
        AccessTools.Method(notices, "Initialize").Invoke(null, null);
        AccessTools.Method(notices, "Initialize").Invoke(null, null);
        var map = AccessTools.TypeByName("TaleWorlds.CampaignSystem.ViewModelCollection.Map.MapNotificationVM");
        check(Harmony.GetPatchInfo(AccessTools.GetDeclaredConstructors(map)[0]).Postfixes.Count(p => p.PatchMethod.DeclaringType == notices) == 1,
            "Deferred title notification registration installs once after native game texts exist");
        Assembly.Load("SandBox.ViewModelCollection");
        AccessTools.Method(bannerPatch, "Initialize").Invoke(null, null);
        AccessTools.Method(bannerPatch, "Initialize").Invoke(null, null);
        var nameplate = Type.GetType("SandBox.ViewModelCollection.Nameplate.SettlementNameplateVM, SandBox.ViewModelCollection", true);
        check(Harmony.GetPatchInfo(AccessTools.Method(nameplate, "RefreshBindValues")).Transpilers.Count(p => p.PatchMethod.DeclaringType == bannerPatch) == 1,
            "Deferred lordship banner binding installs once after Sandbox UI is loaded normally");
        var overlay = AccessTools.TypeByName("TaleWorlds.CampaignSystem.ViewModelCollection.GameMenu.Overlay.SettlementMenuOverlayVM");
        foreach (string name in new[] { "UpdateSettlementOwnerBanner", "Refresh" })
            check(Harmony.GetPatchInfo(AccessTools.Method(overlay, name)).Postfixes.Count(p => p.PatchMethod.DeclaringType == bannerPatch) == 1,
                "The deferred village menu banner hook installs exactly once: " + name);
        var tick = PatchProcessor.GetOriginalInstructions(AccessTools.Method(typeof(BellumCivile.SubModule), "OnApplicationTick"));
        check(tick.Any(i => i.operand is MethodInfo m && m.DeclaringType == bannerPatch && m.Name == "Initialize"),
            "Deferred banner registration is retried from application frames, including while the campaign is paused");
        harmony.UnpatchAll(harmony.Id);
    }
}
