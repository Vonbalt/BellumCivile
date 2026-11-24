using System;
using System.Collections.Generic;
using BellumCivile;
using HarmonyLib;
using TaleWorlds.SaveSystem;
using TaleWorlds.SaveSystem.Definition;

internal static class PlayerMarriageSaveDefinitionTests
{
    private static readonly Type Pledge = typeof(PlayerMarriageAgreement).Assembly.GetType("BellumCivile.BetrothalRecord", true);
    private static readonly Type PledgeList = typeof(List<>).MakeGenericType(Pledge);
    private static bool OnlyAgreement(Type type) => type == typeof(PlayerMarriageAgreement) || type == Pledge || type == typeof(BetrothalOfferNotice);
    private static bool OnlyList(Type type) => type == typeof(List<PlayerMarriageAgreement>) || type == PledgeList;
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
            check(AccessTools.Method(typeof(DefinitionContext), "GetClassDefinition", new[] { typeof(Type) })
                .Invoke(context, new object[] { Pledge }) != null && AccessTools.Method(typeof(DefinitionContext), "GetContainerDefinition")
                .Invoke(context, new object[] { PledgeList }) != null && !context.GotError,
                "Native save registry resolves the separate betrothal records and list");
            check(AccessTools.Method(typeof(DefinitionContext), "GetClassDefinition", new[] { typeof(Type) })
                .Invoke(context, new object[] { typeof(BetrothalOfferNotice) }) != null && !context.GotError,
                "Native save registry resolves the separate betrothal map notice");
        }
        finally { harmony.UnpatchAll(harmony.Id); }
    }
}
