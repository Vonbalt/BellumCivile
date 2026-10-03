using System;
using System.Collections.Generic;
using BellumCivile;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.SaveSystem;
using TaleWorlds.SaveSystem.Definition;

internal static class CourtTradeSaveDefinitionTests
{
    private static int _registrations;
    private static bool OnlyTradeContainer(Type type)
    {
        if (type != typeof(Dictionary<string, double>)) return false;
        _registrations++;
        return true;
    }

    internal static void Run(Action<bool, string> check)
    {
        var context = new DefinitionContext();
        var initialize = AccessTools.Method(typeof(SaveableTypeDefiner), "Initialize");
        var basic = new SaveableBasicTypeDefiner();
        initialize.Invoke(basic, new object[] { context });
        AccessTools.Method(typeof(SaveableBasicTypeDefiner), "DefineBasicTypes").Invoke(basic, null);
        var type = typeof(Dictionary<string, double>);
        var lookup = AccessTools.Method(typeof(DefinitionContext), "GetContainerDefinition");
        check(lookup.Invoke(context, new object[] { type }) == null,
            "Native basic types alone do not register the court trade cooldown dictionary");
        check(AccessTools.Field(typeof(CourtAgendaBehavior), "_tradeRepeatUntil").FieldType == type,
            "Regression tests the actual persisted trade cooldown field type");
        var definer = new BellumCivileSaveDefiner();
        initialize.Invoke(definer, new object[] { context });
        var harmony = new Harmony("bellum.test.court_trade_save_definition");
        _registrations = 0;
        try
        {
            // Isolate this native registration from unrelated campaign type dependencies.
            harmony.Patch(AccessTools.Method(typeof(SaveableTypeDefiner), "ConstructContainerDefinition"),
                prefix: new HarmonyMethod(typeof(CourtTradeSaveDefinitionTests), nameof(OnlyTradeContainer)));
            AccessTools.Method(typeof(BellumCivileSaveDefiner), "DefineContainerDefinitions").Invoke(definer, null);
            check(_registrations == 1, "Bellum explicitly registers the persisted string/double container exactly once");
            check(lookup.Invoke(context, new object[] { type }) != null && !context.GotError,
                "Native save registry resolves the trade cooldown dictionary without definition errors");
        }
        finally { harmony.UnpatchAll(harmony.Id); }
    }
}
