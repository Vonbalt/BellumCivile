using System.Xml;
using Bannerlord.UIExtenderEx.Attributes;
using Bannerlord.UIExtenderEx.Prefabs;

namespace BellumCivile.ViewModelMixin
{
    [PrefabExtension("DiplomacyPanel", null)]
    [PrefabExtension("DiplomacyPanelCustom", null)]
    internal sealed class KingdomDiplomacyThreatPrefabPatch : CustomPatch<XmlDocument>
    {
        public override string Id => "BellumDiplomacyThreat";

        public override void Apply(XmlDocument document)
        {
            var strategy = document.SelectSingleNode("//*[@Id='DropdownParent']/ancestor::Widget[@IsVisible='@IsAcceptableItemSelected'][1]");
            var layer = strategy?.ParentNode;
            if (layer?.Name != "Children") return;
            Add(document, layer, "First", "Left", "15");
            Add(document, layer, "Second", "Right", "225");
        }

        private static void Add(XmlDocument document, XmlNode layer, string side, string alignment, string margin)
        {
            string id = "Bellum" + side + "DiplomacyThreat";
            if (document.SelectSingleNode("//*[@Id='" + id + "']") != null) return;
            var indicator = document.CreateElement("Widget");
            indicator.SetAttribute("Id", id);
            indicator.SetAttribute("IsVisible", "@Bellum" + side + "ThreatVisible");
            indicator.SetAttribute("DoNotAcceptEvents", "true");
            indicator.SetAttribute("WidthSizePolicy", "Fixed");
            indicator.SetAttribute("HeightSizePolicy", "Fixed");
            indicator.SetAttribute("SuggestedWidth", "36");
            indicator.SetAttribute("SuggestedHeight", "36");
            indicator.SetAttribute("HorizontalAlignment", alignment);
            indicator.SetAttribute("VerticalAlignment", "Top");
            indicator.SetAttribute("Margin" + alignment, margin);
            indicator.SetAttribute("MarginTop", "122");
            var children = document.CreateElement("Children");
            var icon = document.CreateElement("Widget");
            icon.SetAttribute("DataSource", "{Bellum" + side + "ThreatHint}");
            icon.SetAttribute("WidthSizePolicy", "Fixed");
            icon.SetAttribute("HeightSizePolicy", "Fixed");
            icon.SetAttribute("SuggestedWidth", "29");
            icon.SetAttribute("SuggestedHeight", "27");
            icon.SetAttribute("HorizontalAlignment", "Center");
            icon.SetAttribute("VerticalAlignment", "Center");
            icon.SetAttribute("Sprite", @"SPGeneral\Clan\Status\Dead");
            icon.SetAttribute("Command.HoverBegin", "ExecuteBeginHint");
            icon.SetAttribute("Command.HoverEnd", "ExecuteEndHint");
            children.AppendChild(icon);
            indicator.AppendChild(children);
            layer.AppendChild(indicator);
        }
    }
}
