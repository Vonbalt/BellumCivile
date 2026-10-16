using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Xml;
using BellumCivile;
using HarmonyLib;
using TaleWorlds.Core.ViewModelCollection.Information;
using TaleWorlds.Library;

internal static class MapWidgetTooltipTests
{
    internal static void Run(Action<bool, string> check)
    {
        var document = new XmlDocument();
        document.Load("GUI/Prefabs/Map/BellumWarScoreMapWidget.xml");
        check(document.SelectSingleNode("/Prefab/Window/Widget[@DoNotAcceptEvents='true']") != null,
            "Map overlay does not intercept input across the whole screen");

        var ui = Assembly.Load("TaleWorlds.GauntletUI");
        var widgets = Assembly.Load("TaleWorlds.MountAndBlade.GauntletUI.Widgets");
        AccessTools.Method(ui.GetType("TaleWorlds.GauntletUI.WidgetInfo", true), "Refresh").Invoke(null, null);
        var contextType = ui.GetType("TaleWorlds.GauntletUI.UIContext", true);
        var context = FormatterServices.GetUninitializedObject(contextType);
        AccessTools.Property(contextType, "EventManager").SetValue(context,
            FormatterServices.GetUninitializedObject(ui.GetType("TaleWorlds.GauntletUI.EventManager", true)));

        foreach (var row in new[] {
            new[] { "Wars", "WarScore", "WarScoreMapWidgetItemVM", "_warScoreHint" },
            new[] { "Clients", "Liberty", "ClientStateMapWidgetItemVM", "_libertyHint" } })
        {
            var list = (XmlElement)document.SelectSingleNode("//ListPanel[@DataSource='{" + row[0] + "}']");
            var template = (XmlElement)list.SelectSingleNode("ItemTemplate/*");
            check(list.GetAttribute("DoNotAcceptEvents") == "true", row[0] + " list leaves empty map space interactive");
            check(template.GetAttribute("SuggestedWidth") == "82" && template.GetAttribute("SuggestedHeight") == "96",
                row[0] + " hover target retains the banner and score bounds");
            var type = ui.GetType("TaleWorlds.GauntletUI.BaseTypes." + template.Name)
                ?? widgets.GetType("TaleWorlds.MountAndBlade.GauntletUI.Widgets." + template.Name, true);
            var widget = Activator.CreateInstance(type, context);
            foreach (string name in new[] { "DoNotAcceptEvents", "DoNotPassEventsToChildren", "IsDisabled" })
                if (template.HasAttribute(name)) AccessTools.Property(type, name).SetValue(widget, bool.Parse(template.GetAttribute(name)));
            check(!(bool)AccessTools.Property(type, "DoNotAcceptEvents").GetValue(widget)
                && !(bool)AccessTools.Property(type, "IsDisabled").GetValue(widget)
                && (bool)AccessTools.Method(type, "OnPreviewMouseMove").Invoke(widget, null),
                row[0] + " native widget accepts its own mouse hover in the beta");
            check(template.SelectNodes(".//Children/*").Cast<XmlElement>()
                .All(child => child.GetAttribute("DoNotAcceptEvents") == "true"),
                row[0] + " decorative children cannot steal the hover target");

            string begin = "ExecuteBegin" + row[1] + "Hint", end = "ExecuteEnd" + row[1] + "Hint";
            check(template.GetAttribute("Command.HoverBegin") == begin && template.GetAttribute("Command.HoverEnd") == end,
                row[0] + " binds both hover enter and leave to its shared tooltip");
            var vmType = typeof(FeudalTitleRecord).Assembly.GetType("BellumCivile.UI.Map." + row[2], true);
            var vm = Activator.CreateInstance(vmType, row[0] == "Wars"
                ? new object[] { null, null, null, 0f } : new object[] { null });
            var hint = AccessTools.Field(vmType, row[3]).GetValue(vm);
            var expected = new List<TooltipProperty> { new TooltipProperty("Fixture", row[0], 0) };
            int builds = 0, shown = 0, hidden = 0;
            Func<List<TooltipProperty>> build = () => { builds++; return expected; };
            AccessTools.Method(hint.GetType(), "SetToolipCallback").Invoke(hint, new object[] { build });
            Action<Type, object[]> show = (tooltipType, args) => {
                check(tooltipType == typeof(List<TooltipProperty>) && ReferenceEquals(args.Single(), expected),
                    row[0] + " publishes the property tooltip through the native tooltip manager");
                shown++;
            };
            Action hide = () => hidden++;
            InformationManager.OnShowTooltip += show;
            InformationManager.OnHideTooltip += hide;
            try
            {
                AccessTools.Method(vmType, begin).Invoke(vm, null);
                AccessTools.Method(vmType, end).Invoke(vm, null);
                check(builds == 1 && shown == 1 && hidden == 1, row[0] + " opens and closes exactly once per hover");
                AccessTools.Method(vmType, begin).Invoke(vm, null);
                AccessTools.Method(vmType, "OnFinalize").Invoke(vm, null);
                check(builds == 2 && shown == 2 && hidden == 2, row[0] + " rebuilds on re-entry and clears its tooltip on removal");
            }
            finally
            {
                InformationManager.OnShowTooltip -= show;
                InformationManager.OnHideTooltip -= hide;
            }
        }
    }
}
