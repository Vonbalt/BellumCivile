using System;
using System.Globalization;
using System.Reflection;
using System.Runtime.Serialization;
using System.Xml;
using BellumCivile;
using HarmonyLib;
using TaleWorlds.Core.ViewModelCollection.Information;
using TaleWorlds.Library;

internal static class EncyclopediaInheritanceLayoutTests
{
    internal static void Run(Action<bool, string> check)
    {
        var type = typeof(FeudalTitleRecord).Assembly.GetType(
            "BellumCivile.ViewModelMixin.EncyclopediaHeroFeudalTitlesPrefabExtension", true);
        var patch = Activator.CreateInstance(type, true);
        var document = (XmlDocument)AccessTools.Method(type, "GetPrefabExtension").Invoke(patch, null);
        var titles = (XmlElement)document.SelectSingleNode("//GridWidget[@DataSource='{BellumTitles}']");
        var inheritance = (XmlElement)document.SelectSingleNode("//GridWidget[@DataSource='{BellumInheritance}']");
        check(inheritance != null, "Inheritance uses a grid rather than a single-column list");
        check(inheritance.GetAttribute("ColumnCount") == "2"
            && inheritance.GetAttribute("ColumnCount") == titles.GetAttribute("ColumnCount"),
            "Inheritance matches the two-column Titles layout");
        check(inheritance.GetAttribute("DefaultCellWidth") == titles.GetAttribute("DefaultCellWidth")
            && inheritance.GetAttribute("WidthSizePolicy") == "StretchToParent",
            "Inheritance columns share the available page width like Titles");
        check(inheritance.GetAttribute("UseDynamicCellHeight") == "true"
            && inheritance.GetAttribute("HeightSizePolicy") == "CoverChildren",
            "Inheritance rows grow only when title names wrap");
        check(inheritance.GetAttribute("DefaultCellHeight") == titles.GetAttribute("DefaultCellHeight"),
            "Inheritance uses the compact Titles row height");

        var item = (XmlElement)inheritance.SelectSingleNode("ItemTemplate/Widget");
        check(item.GetAttribute("HeightSizePolicy") == "CoverChildren",
            "Each inheritance cell covers only its title text");
        check(item.GetAttribute("MarginRight") == "15" && item.GetAttribute("MarginBottom") == "5",
            "Inheritance retains column spacing with a compact row gap");
        check(item.GetAttribute("WidthSizePolicy") == "Fixed"
            && float.Parse(item.GetAttribute("SuggestedWidth"), CultureInfo.InvariantCulture)
                + float.Parse(item.GetAttribute("MarginLeft"), CultureInfo.InvariantCulture)
                + float.Parse(item.GetAttribute("MarginRight"), CultureInfo.InvariantCulture)
                <= float.Parse(inheritance.GetAttribute("DefaultCellWidth"), CultureInfo.InvariantCulture),
            "Inheritance cells have a stable measurement width that fits the column");
        check(item.GetAttribute("VerticalAlignment") == "Top",
            "Short inheritance entries remain aligned with the top of their row");
        CheckNativePremeasurement(item, check);
        var text = (XmlElement)item.SelectSingleNode("Children/TextWidget[@Text='@Definition']");
        check(text != null && text.GetAttribute("WidthSizePolicy") == "StretchToParent"
            && text.GetAttribute("HeightSizePolicy") == "CoverChildren",
            "Inheritance title text wraps within its column");
        check(inheritance.SelectNodes(".//TextWidget").Count == 1 && inheritance.SelectSingleNode(".//*[@Text='@Value']") == null,
            "Provenance no longer occupies a second visible text row");
        var hint = (XmlElement)text.SelectSingleNode("Children/HintWidget[@DataSource='{Hint}']");
        check(hint != null && hint.GetAttribute("WidthSizePolicy") == "StretchToParent"
            && hint.GetAttribute("HeightSizePolicy") == "StretchToParent"
            && hint.GetAttribute("Command.HoverBegin") == "ExecuteBeginHint"
            && hint.GetAttribute("Command.HoverEnd") == "ExecuteEndHint",
            "Hovering the title opens its native hint and leaving closes it");
        CheckNativeHoverRouting(text, hint, check);

        check(document.SelectSingleNode("//*[@Text='@BellumInheritanceCaveat']") == null,
            "Hero page no longer repeats the successor tooltip caveat");
        var divider = (XmlElement)document.SelectSingleNode("//*[@Id='BellumInheritanceDivider']");
        check(divider.GetAttribute("Parameter.ItemList") == "..\\BellumInheritanceParent"
            && inheritance.ParentNode.ParentNode.Attributes["Id"].Value == "BellumInheritanceParent",
            "Inheritance section collapse still targets the full list");
        EstateInheritanceTooltipTests.Run(check);
    }

    private static void CheckNativeHoverRouting(XmlElement text, XmlElement hint, Action<bool, string> check)
    {
        var ui = Assembly.Load("TaleWorlds.GauntletUI");
        var widgets = Assembly.Load("TaleWorlds.MountAndBlade.GauntletUI.Widgets");
        AccessTools.Method(ui.GetType("TaleWorlds.GauntletUI.WidgetInfo", true), "Refresh").Invoke(null, null);
        var contextType = ui.GetType("TaleWorlds.GauntletUI.UIContext", true);
        var context = FormatterServices.GetUninitializedObject(contextType);
        AccessTools.Property(contextType, "EventManager").SetValue(context,
            FormatterServices.GetUninitializedObject(ui.GetType("TaleWorlds.GauntletUI.EventManager", true)));
        var textType = ui.GetType("TaleWorlds.GauntletUI.BaseTypes.TextWidget", true);
        var widgetType = ui.GetType("TaleWorlds.GauntletUI.BaseTypes.Widget", true);
        check(AccessTools.Method(textType, "OnPreviewMouseMove").DeclaringType == widgetType,
            "Title text inherits the native Widget hover preview used by the renderless fixture");
        // Exercise the inherited input behavior without requiring TextWidget font and brush resources.
        var title = Activator.CreateInstance(widgetType, context);
        if (text.HasAttribute("DoNotAcceptEvents"))
            AccessTools.Property(widgetType, "DoNotAcceptEvents").SetValue(title, bool.Parse(text.GetAttribute("DoNotAcceptEvents")));
        var hintType = widgets.GetType("TaleWorlds.MountAndBlade.GauntletUI.Widgets.HintWidget", true);
        var relay = Activator.CreateInstance(hintType, context);
        AccessTools.Property(hintType, "ParentWidget").SetValue(relay, title);
        AccessTools.Method(hintType, "OnConnectedToRoot").Invoke(relay, null);

        const string expected = "Crown estate - Expected from Rex Derthert of Dey Meroc";
        var vm = new BasicTooltipViewModel(() => expected);
        int shown = 0, hidden = 0;
        Action<Type, object[]> show = (type, args) => {
            check(type == typeof(string) && args.Length == 1 && (string)args[0] == expected,
                "Inheritance hover publishes its source through the native hint manager");
            shown++;
        };
        Action hide = () => hidden++;
        Action<object, string, object[]> command = (_, name, args) => {
            string method = hint.GetAttribute("Command." + name);
            if (!string.IsNullOrEmpty(method)) AccessTools.Method(vm.GetType(), method).Invoke(vm, null);
        };
        var eventInfo = hintType.GetEvent("EventFire");
        var handler = Delegate.CreateDelegate(eventInfo.EventHandlerType, command.Target, command.Method);
        eventInfo.AddEventHandler(relay, handler);
        InformationManager.OnShowTooltip += show;
        InformationManager.OnHideTooltip += hide;
        try
        {
            bool acceptsHover = !(bool)AccessTools.Property(widgetType, "DoNotAcceptEvents").GetValue(title)
                && !(bool)AccessTools.Property(widgetType, "IsDisabled").GetValue(title)
                && (bool)AccessTools.Method(widgetType, "OnPreviewMouseMove").Invoke(title, null);
            check(acceptsHover, "The inheritance title accepts hover so the beta's passive HintWidget receives parent events");
            var fire = AccessTools.Method(widgetType, "EventFired");
            for (int i = 0; i < 2; i++)
            {
                fire.Invoke(title, new object[] { "HoverBegin", new object[] { true } });
                fire.Invoke(title, new object[] { "HoverEnd", new object[] { true } });
            }
            check(shown == 2 && hidden == 2, "Native inheritance hints open and close exactly once on repeated title hovers");
            AccessTools.Method(hintType, "OnDisconnectedFromRoot").Invoke(relay, null);
            fire.Invoke(title, new object[] { "HoverBegin", new object[] { true } });
            check(shown == 2, "Removing an inheritance hint disconnects its parent hover listener");
        }
        finally
        {
            InformationManager.OnShowTooltip -= show;
            InformationManager.OnHideTooltip -= hide;
            eventInfo.RemoveEventHandler(relay, handler);
        }
    }

    private static void CheckNativePremeasurement(XmlElement template, Action<bool, string> check)
    {
        // Exercise native sizing without a renderer: GridLayout measures dynamic cells with Vector2.Zero.
        var assembly = Assembly.Load("TaleWorlds.GauntletUI");
        AccessTools.Method(assembly.GetType("TaleWorlds.GauntletUI.WidgetInfo", true), "Refresh").Invoke(null, null);
        var contextType = assembly.GetType("TaleWorlds.GauntletUI.UIContext", true);
        var context = FormatterServices.GetUninitializedObject(contextType);
        var events = FormatterServices.GetUninitializedObject(assembly.GetType("TaleWorlds.GauntletUI.EventManager", true));
        AccessTools.Property(contextType, "EventManager").SetValue(context, events);
        var cellType = assembly.GetType("TaleWorlds.GauntletUI.BaseTypes.Widget", true);
        var cell = Activator.CreateInstance(cellType, context);
        foreach (string name in new[] { "WidthSizePolicy", "HeightSizePolicy", "SuggestedWidth", "MarginLeft", "MarginRight" })
        {
            var property = AccessTools.Property(cellType, name);
            property.SetValue(cell, property.PropertyType.IsEnum
                ? Enum.Parse(property.PropertyType, template.GetAttribute(name))
                : Convert.ChangeType(template.GetAttribute(name), property.PropertyType, CultureInfo.InvariantCulture));
        }
        var measure = AccessTools.Method(cellType, "Measure");
        var vectorType = measure.GetParameters()[0].ParameterType;
        object zeroWidth = Activator.CreateInstance(vectorType);
        foreach (float scale in new[] { 0.75f, 1f, 1.5f })
        {
            AccessTools.Property(contextType, "CustomScale").SetValue(context, scale);
            AccessTools.Property(contextType, "Scale").SetValue(context, scale);
            measure.Invoke(cell, new[] { zeroWidth });
            var measured = AccessTools.Property(cellType, "MeasuredSize").GetValue(cell);
            float width = (float)vectorType.GetField("X").GetValue(measured);
            float expected = float.Parse(template.GetAttribute("SuggestedWidth"), CultureInfo.InvariantCulture) * scale;
            check(width > 0f && Math.Abs(width - expected) < 0.01f,
                "Native zero-width premeasurement preserves the inheritance text width at scale " + scale);
        }

        var widthPolicy = AccessTools.Property(cellType, "WidthSizePolicy");
        widthPolicy.SetValue(cell, Enum.Parse(widthPolicy.PropertyType, "StretchToParent"));
        measure.Invoke(cell, new[] { zeroWidth });
        var oldMeasurement = AccessTools.Property(cellType, "MeasuredSize").GetValue(cell);
        check((float)vectorType.GetField("X").GetValue(oldMeasurement) <= 0f,
            "Regression control reproduces the old stretch-width cell's invalid premeasurement");
    }
}
