using System;
using System.Xml;
using BellumCivile;
using HarmonyLib;

internal static class ClanMemberLocationHintTests
{
    internal static void Run(Action<bool, string> check)
    {
        var type = typeof(FeudalTitleRecord).Assembly.GetType(
            "BellumCivile.ViewModelMixin.ClanLordPrisonerRansomLocationHintPrefabExtension");
        var patch = Activator.CreateInstance(type, true);
        var document = (XmlDocument)AccessTools.Method(type, "GetPrefabExtension").Invoke(patch, null);
        var root = document.DocumentElement;
        check(root.Name == "Widget" && root.GetAttribute("DoNotAcceptEvents") == "true",
            "Clan member location wrapper passes clicks through to native selection button");
        check(!root.HasAttribute("IsVisible") && !root.HasAttribute("IsHidden"),
            "Location remains visible for both prisoners and free members");
        check(root.GetAttribute("MarginRight") == "10"
            && root.GetAttribute("WidthSizePolicy") == "StretchToParent"
            && root.GetAttribute("HeightSizePolicy") == "StretchToParent", "Location layout remains unchanged");
        check(root.GetAttribute("DoNotPassEventsToChildren") != "true", "Wrapper still permits child hint hover handling");
        var text = (XmlElement)root.SelectSingleNode("Children/TextWidget");
        check(text.GetAttribute("Text") == "@CurrentActionText" && text.GetAttribute("DoNotAcceptEvents") == "true",
            "Native location binding remains click-through");
        var hint = (XmlElement)root.SelectSingleNode("Children/HintWidget");
        check(hint.GetAttribute("DoNotAcceptEvents") == "true" && hint.GetAttribute("DataSource") == "{PrisonerRansomHint}",
            "Prisoner hint remains bound without intercepting clicks");
        check(hint.GetAttribute("Command.HoverBegin") == "ExecuteBeginHint"
            && hint.GetAttribute("Command.HoverEnd") == "ExecuteEndHint", "Prisoner hint hover lifecycle remains wired");
        check(AccessTools.Property(type, "Type").GetValue(patch).ToString() == "Replace", "Extension still replaces only the location label");
    }
}
