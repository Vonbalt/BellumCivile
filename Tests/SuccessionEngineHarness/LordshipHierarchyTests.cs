using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Serialization;
using BellumCivile;
using BellumCivile.Behaviors;
using BellumCivile.UI.VanillaTabs.Kingdoms.Hierarchy;
using HarmonyLib;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core.ViewModelCollection.Information;

internal static class LordshipHierarchyTests
{
    private static Type _widget;
    private static object _context;
    private static object New(Type type) => Activator.CreateInstance(type, new[] { _context });
    private static object Get(object instance, string property) => AccessTools.Property(instance.GetType(), property).GetValue(instance);
    private static void Call(object instance, string method, params object[] args) => AccessTools.Method(instance.GetType(), method).Invoke(instance, args);
    private static int Count(object instance) => (int)Get(instance, "ChildCount");
    private static object Child(object instance, int index) => AccessTools.Method(_widget, "GetChild").Invoke(instance, new object[] { index });
    private static void Position(object instance, float x)
    {
        var property = AccessTools.Property(_widget, "LocalPosition");
        property.SetValue(instance, Activator.CreateInstance(property.PropertyType, new object[] { x, 0f }));
    }
    private static bool SkipLayout() => false;

    private static Type CompiledPrefabType(Type widget)
    {
        // UIExtenderEx emits prefab subclasses in a separate assembly.
        var name = new AssemblyName("Bellum.Tests.CompiledHierarchyPrefab");
        var assembly = AppDomain.CurrentDomain.DefineDynamicAssembly(name, AssemblyBuilderAccess.Run);
        var type = assembly.DefineDynamicModule(name.Name).DefineType(
            "BellumHierarchyTitleTreeNode__InheritedPrefab", TypeAttributes.Public, widget);
        var constructor = widget.GetConstructors().Single();
        var parameters = constructor.GetParameters().Select(p => p.ParameterType).ToArray();
        var il = type.DefineConstructor(MethodAttributes.Public, CallingConventions.Standard, parameters).GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, constructor);
        il.Emit(OpCodes.Ret);
        return type.CreateType();
    }

    // Keep real widget collections and native add callbacks, without requiring rendered sprites.
    private static bool CreateLine(object __instance, ref object __result)
    {
        __result = New(_widget);
        Call(Get(__instance, "LineContainer"), "AddChild", __result);
        return false;
    }

    internal static void Run(Action<bool, string> check, Settlement village)
    {
        var parent = (HierarchyTitleNodeVM)FormatterServices.GetUninitializedObject(typeof(HierarchyTitleNodeVM));
        AccessTools.Field(typeof(HierarchyTitleNodeVM), "<Name>k__BackingField").SetValue(parent, "Baronatus Test");
        var barony = new FeudalTitleRecord("test_barony", "Test", FeudalTitleType.Barony, "house", "house", "", "", "", 0, 0);
        AccessTools.Field(typeof(HierarchyTitleNodeVM), "<Title>k__BackingField").SetValue(parent, barony);
        var node = new HierarchyLordshipNodeVM(village, parent, null, null);
        check(node.Tier == "Lordship" && !node.IsTitleNode && node.ActionMenuHeight == 84,
            "Personal lordships expose their own tier and compact two-action menu");
        var tooltip = AccessTools.Field(typeof(HierarchyTitleNodeVM), "_tooltip").GetValue(node);
        List<object> Rows() => ((IEnumerable)((Delegate)AccessTools.Field(typeof(BasicTooltipViewModel), "_tooltipProperties")
            .GetValue(tooltip)).DynamicInvoke()).Cast<object>().ToList();
        string Value(List<object> properties, string label) => (string)Get(properties.Single(p => (string)Get(p, "DefinitionLabel") == label), "ValueLabel");
        var rows = Rows();
        check(rows.Any(row => (string)Get(row, "DefinitionLabel") == "Tier" && (string)Get(row, "ValueLabel") == "Lordship"),
            "Lordship tooltip labels the tier Lordship rather than the parent barony name");
        check((string)Get(rows[0], "DefinitionLabel") == LordshipBehavior.Instance.GetGrant(village).Holder.Name.ToString(),
            "Lordship tooltip uses the personal holder's name as its heading");
        check(rows.Where(p => !string.IsNullOrEmpty((string)Get(p, "ValueLabel")))
                .Select(p => (string)Get(p, "DefinitionLabel")).SequenceEqual(new[] { "Tier", "Tenure", "De jure liege" }),
            "Lordship tooltip has only the requested tier, tenure and de jure liege rows in title-tooltip order");
        check(Value(rows, "Tenure") == "De facto and de jure" && Value(rows, "De jure liege") == parent.Name,
            "Full barony ownership supplies the lordship's tenure and its actual parent as liege");
        barony.SetDeJureHolder("other_house");
        check(Value(Rows(), "Tenure") == "de facto only",
            "A lordship immediately reflects its parent barony's de facto-only tenure without treating the grant as a legal claim");
        barony.SetDeJureHolder("house");
        check(Value(Rows(), "Tenure") == "De facto and de jure",
            "Legalizing the parent barony updates lordship tenure on the next hover without rebuilding its node");

        var type = typeof(HierarchyTitleNodeVM).Assembly.GetType("BellumCivile.UI.VanillaTabs.Kingdoms.Hierarchy.HierarchyTitleTreeNodeWidget", true);
        check(type.IsVisible && !type.IsSealed,
            "Hierarchy widget is public and inheritable for UIExtenderEx prefab compilation");
        var compiledType = CompiledPrefabType(type);
        check(compiledType.BaseType == type && compiledType.Assembly != type.Assembly,
            "Generated prefab can subclass the hierarchy widget from a separate assembly");
        var native = type.BaseType;
        _widget = native.Assembly.GetReferencedAssemblies().Select(System.Reflection.Assembly.Load)
            .Select(a => a.GetType("TaleWorlds.GauntletUI.BaseTypes.Widget")).First(t => t != null);
        var list = _widget.Assembly.GetType("TaleWorlds.GauntletUI.BaseTypes.ListPanel", true);
        var brush = _widget.Assembly.GetType("TaleWorlds.GauntletUI.BaseTypes.BrushWidget", true);
        _context = FormatterServices.GetUninitializedObject(_widget.Assembly.GetType("TaleWorlds.GauntletUI.UIContext", true));
        foreach (string scale in new[] { "Scale", "CustomScale", "InverseScale", "CustomInverseScale" })
            AccessTools.Property(_context.GetType(), scale).SetValue(_context, 1f);
        var info = _widget.Assembly.GetType("TaleWorlds.GauntletUI.WidgetInfo", true);
        var infoField = AccessTools.Field(info, "_widgetInfos");
        object previousInfo = infoField.GetValue(null);
        var infos = (IDictionary)Activator.CreateInstance(typeof(Dictionary<,>).MakeGenericType(typeof(Type), info));
        foreach (var t in new[] { _widget, type, compiledType, list }) infos.Add(t, Activator.CreateInstance(info, new object[] { t }));
        infoField.SetValue(null, infos);
        var harmony = new Harmony("bellum.tests.lordship.hierarchy");
        try
        {
            foreach (string method in new[] { "SetMeasureDirty", "SetLayoutDirty" })
                harmony.Patch(AccessTools.Method(_widget, method), prefix: new HarmonyMethod(typeof(LordshipHierarchyTests), nameof(SkipLayout)));
            harmony.Patch(AccessTools.Method(brush, "RegisterUpdateBrushes"), prefix: new HarmonyMethod(typeof(LordshipHierarchyTests), nameof(SkipLayout)));
            harmony.Patch(AccessTools.Method(native, "CreateLineWidget"), prefix: new HarmonyMethod(typeof(LordshipHierarchyTests), nameof(CreateLine)));
            object Tree(Type widgetType, out object children, out object lines)
            {
                object tree = New(widgetType); children = New(list); lines = New(_widget);
                AccessTools.Property(type, "LineContainer").SetValue(tree, lines);
                AccessTools.Property(type, "ChildContainer").SetValue(tree, children);
                return tree;
            }
            object first = Tree(type, out var firstChildren, out var firstLines);
            object second = Tree(compiledType, out var secondChildren, out var secondLines);
            check(type.IsInstanceOfType(second) && second.GetType() == compiledType,
                "Generated prefab constructor preserves the hierarchy widget and its inherited containers");
            for (int cycle = 0; cycle < 10; cycle++)
            {
                Call(firstChildren, "AddChild", New(_widget)); Call(firstChildren, "AddChild", New(_widget));
                check(Count(firstLines) == 2, "Native connector creation stays intact on expansion: " + cycle);
                Call(firstChildren, "RemoveAllChildren");
                check(Count(firstLines) == 2, "Regression fixture reproduces native orphaned connectors: " + cycle);
                Call(first, "OnLateUpdate", 0f);
                check(Count(firstLines) == 0, "Collapsing the first barony removes its connectors: " + cycle);
                Call(secondChildren, "AddChild", New(_widget)); Call(secondChildren, "AddChild", New(_widget));
                Call(secondChildren, "AddChild", New(_widget)); Call(second, "OnLateUpdate", 0f);
                check(Count(firstLines) == 0 && Count(secondLines) == 3,
                    "Selecting a different barony does not accumulate old connectors: " + cycle);
                Position(Child(secondChildren, 0), 20f); Call(second, "OnLateUpdate", 0f);
                check((float)Get(Child(secondLines, 0), "ScaledSuggestedWidth") == 20f,
                    "Connectors follow the native child-center geometry: " + cycle);
                Position(Child(secondChildren, 0), 80f); Call(second, "OnLateUpdate", 0f);
                check((float)Get(Child(secondLines, 0), "ScaledSuggestedWidth") == 80f,
                    "Layout shifts update ancestor connectors without changing their child count: " + cycle);
                Call(secondChildren, "RemoveChild", AccessTools.Method(_widget, "GetChild").Invoke(secondChildren, new object[] { 0 }));
                Call(second, "OnLateUpdate", 0f);
                check(Count(secondLines) == 2, "Partial child removal rebuilds the remaining connectors: " + cycle);
                object unchanged = AccessTools.Method(_widget, "GetChild").Invoke(secondLines, new object[] { 0 });
                Call(second, "OnLateUpdate", 0f);
                check(ReferenceEquals(unchanged, AccessTools.Method(_widget, "GetChild").Invoke(secondLines, new object[] { 0 })),
                    "An unchanged branch does not rebuild its lines each frame: " + cycle);
                Call(secondChildren, "RemoveChild", AccessTools.Method(_widget, "GetChild").Invoke(secondChildren, new object[] { 0 }));
                Call(second, "OnLateUpdate", 0f);
                check(Count(secondLines) == 1 && (string)Get(AccessTools.Method(_widget, "GetChild").Invoke(secondLines, new object[] { 0 }), "CurrentState") == "Straight",
                    "Shrinking to one village restores the native straight connector: " + cycle);
                Call(secondChildren, "RemoveAllChildren"); Call(second, "OnLateUpdate", 0f);
                check(Count(secondLines) == 0, "Deselecting the second barony leaves no lines: " + cycle);
            }
        }
        finally { harmony.UnpatchAll(harmony.Id); infoField.SetValue(null, previousInfo); }
    }
}
