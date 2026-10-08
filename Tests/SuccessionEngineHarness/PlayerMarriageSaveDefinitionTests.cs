using System;
using System.Collections.Generic;
using BellumCivile;
using HarmonyLib;
using TaleWorlds.SaveSystem;
using TaleWorlds.SaveSystem.Definition;

internal static class PlayerMarriageSaveDefinitionTests
{
    private static bool OnlyAgreement(Type type) => type == typeof(PlayerMarriageAgreement);
    private static bool OnlyList(Type type) => type == typeof(List<PlayerMarriageAgreement>);
    internal static void Run(Action<bool, string> check)
    {
        var context = new DefinitionContext();
        var initialize = AccessTools.Method(typeof(SaveableTypeDefiner), "Initialize");
        var basic = new SaveableBasicTypeDefiner();
        initialize.Invoke(basic, new object[] { context });
        AccessTools.Method(typeof(SaveableBasicTypeDefiner), "DefineBasicTypes").Invoke(basic, null);
        var definer = new BellumCivileSaveDefiner();
        initialize.Invoke(definer, new object[] { context });
        var harmony = new Harmony("bellum.test.player_marriage_save_definition");
        try
        {
            harmony.Patch(AccessTools.Method(typeof(SaveableTypeDefiner), "AddClassDefinition"),
                prefix: new HarmonyMethod(typeof(PlayerMarriageSaveDefinitionTests), nameof(OnlyAgreement)));
            harmony.Patch(AccessTools.Method(typeof(SaveableTypeDefiner), "ConstructContainerDefinition"),
                prefix: new HarmonyMethod(typeof(PlayerMarriageSaveDefinitionTests), nameof(OnlyList)));
            AccessTools.Method(typeof(BellumCivileSaveDefiner), "DefineClassTypes").Invoke(definer, null);
            AccessTools.Method(typeof(BellumCivileSaveDefiner), "DefineContainerDefinitions").Invoke(definer, null);
            check(AccessTools.Method(typeof(DefinitionContext), "GetClassDefinition", new[] { typeof(Type) })
                .Invoke(context, new object[] { typeof(PlayerMarriageAgreement) }) != null, "Native save registry resolves player agreement class");
            check(AccessTools.Method(typeof(DefinitionContext), "GetContainerDefinition")
                .Invoke(context, new object[] { typeof(List<PlayerMarriageAgreement>) }) != null && !context.GotError,
                "Native save registry resolves player agreement list without definition errors");
        }
        finally { harmony.UnpatchAll(harmony.Id); }
    }
}
