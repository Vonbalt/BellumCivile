using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BellumCivile;
using HarmonyLib;
using TaleWorlds.SaveSystem;
using TaleWorlds.SaveSystem.Definition;

internal static class IntegrationSaveDefinitionTests
{
    private static readonly Type[] Records = { typeof(BellumIntegration).Assembly.GetType("BellumCivile.PartitionCompletionRecord"),
        typeof(BellumIntegration).Assembly.GetType("BellumCivile.PartitionRecipientRecord") };
    private static bool OnlyRecords(Type type) => Records.Contains(type);
    private static bool OnlyLists(Type type) => Records.Any(r => type == typeof(List<>).MakeGenericType(r));
    internal static void Run(Action<bool, string> check)
    {
        var context = new DefinitionContext();
        var initialize = AccessTools.Method(typeof(SaveableTypeDefiner), "Initialize");
        var basic = new SaveableBasicTypeDefiner(); initialize.Invoke(basic, new object[] { context });
        AccessTools.Method(typeof(SaveableBasicTypeDefiner), "DefineBasicTypes").Invoke(basic, null);
        var definer = new BellumCivileSaveDefiner(); initialize.Invoke(definer, new object[] { context });
        var harmony = new Harmony("bellum.tests.integration.save_definitions");
        try
        {
            harmony.Patch(AccessTools.Method(typeof(SaveableTypeDefiner), "AddClassDefinition"),
                prefix: new HarmonyMethod(typeof(IntegrationSaveDefinitionTests), nameof(OnlyRecords)));
            harmony.Patch(AccessTools.Method(typeof(SaveableTypeDefiner), "ConstructContainerDefinition"),
                prefix: new HarmonyMethod(typeof(IntegrationSaveDefinitionTests), nameof(OnlyLists)));
            AccessTools.Method(typeof(BellumCivileSaveDefiner), "DefineClassTypes").Invoke(definer, null);
            AccessTools.Method(typeof(BellumCivileSaveDefiner), "DefineContainerDefinitions").Invoke(definer, null);
            foreach (var type in Records)
            {
                check(AccessTools.Method(typeof(DefinitionContext), "GetClassDefinition", new[] { typeof(Type) })
                    .Invoke(context, new object[] { type }) != null
                    && AccessTools.Method(typeof(DefinitionContext), "GetContainerDefinition")
                    .Invoke(context, new object[] { typeof(List<>).MakeGenericType(type) }) != null,
                    "Native save registry resolves integration record and list: " + type.Name);
                var fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                check(fields.All(f => f.GetCustomAttributesData().Count(a => a.AttributeType == typeof(SaveableFieldAttribute)) == 1),
                    "Every persisted notification field has a save ID: " + type.Name);
            }
            check(!context.GotError, "Integration save definitions introduce no registry errors");
        }
        finally { harmony.UnpatchAll(harmony.Id); }
    }
}
