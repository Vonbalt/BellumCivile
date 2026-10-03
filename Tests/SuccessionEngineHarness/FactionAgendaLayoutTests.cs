using System;
using System.IO;
using System.Xml;

internal static class FactionAgendaLayoutTests
{
    internal static void Run(Action<bool, string> check)
    {
        const string relative = "GUI/Prefabs/KingdomManagement/Factions/BellumFactionsPanel.xml";
        string path = null;
        foreach (string start in new[] { Environment.CurrentDirectory, AppDomain.CurrentDomain.BaseDirectory })
        {
            for (var directory = new DirectoryInfo(start); directory != null; directory = directory.Parent)
            {
                string candidate = Path.Combine(directory.FullName, relative);
                if (File.Exists(candidate)) { path = candidate; break; }
            }
            if (path != null) break;
        }
        check(path != null, "Faction layout prefab found for regression checks");
        var document = new XmlDocument(); document.Load(path);
        var row = (XmlElement)document.SelectSingleNode("//*[@Id='BellumFactionAgendaHint']");
        check(row != null && row.Name == "HintWidget", "Agenda row itself is the hover target");
        check(row.GetAttribute("HeightSizePolicy") == "CoverChildren", "Agenda height follows its actual text");
        check(row.GetAttribute("Command.HoverBegin") == "ExecuteBeginActivityHint"
            && row.GetAttribute("Command.HoverEnd") == "ExecuteEndActivityHint", "Agenda hint commands remain wired");
        check(!row.HasAttribute("IsVisible"), "No-motion agenda remains visible even without a hint");
        var text = (XmlElement)row.SelectSingleNode("Children/TextWidget[@Text='@RivalsText']");
        check(text != null && text.GetAttribute("HeightSizePolicy") == "CoverChildren"
            && text.GetAttribute("WidthSizePolicy") == "StretchToParent", "Dated and wrapped agenda text retains dynamic sizing");
        check(text.GetAttribute("DoNotAcceptEvents") == "true", "Agenda text leaves hover handling to its parent");
        check(row.SelectNodes(".//*[@HeightSizePolicy='StretchToParent']").Count == 0,
            "Agenda cannot expand to panel height through a stretching tooltip child");
        check(row.ParentNode.SelectSingleNode("ButtonWidget[@Command.Click='ExecuteReviewMarriage']") != null
            && row.ParentNode.SelectSingleNode("ButtonWidget[@Command.Click='ExecuteReviewTitlePetition']") != null,
            "Motion review actions remain separate siblings below the agenda");
        var crownButton = (XmlElement)document.SelectSingleNode("//ButtonWidget[@Command.Click='ExecuteCrownActions']");
        check(crownButton != null && crownButton.GetAttribute("IsEnabled") == "@CanUseCrownActions"
            && !crownButton.HasAttribute("IsVisible"), "Crown action button stays visible but disabled for non-rulers");
        check(crownButton.ParentNode.SelectSingleNode("HintWidget[@DataSource='{CrownActionsHint}']") != null,
            "Disabled Crown action button retains an independent explanation hint");
    }
}
