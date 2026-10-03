using System.Linq;
using System.Xml;
using Bannerlord.UIExtenderEx.Attributes;
using Bannerlord.UIExtenderEx.Prefabs;

namespace BellumCivile.ViewModelMixin
{
    // Patch the two layouts independently: Diplomacy can replace the native panel
    // before or after our native patch, without either path creating duplicates.
    [PrefabExtension("DiplomacyPanel", null)]
    [PrefabExtension("DiplomacyPanelCustom", null)]
    internal sealed class KingdomDiplomacyOverviewPrefabPatch : CustomPatch<XmlDocument>
    {
        public override string Id => "BellumDiplomacyOverview";

        public override void Apply(XmlDocument document)
        {
            var content = document.SelectSingleNode("//ListPanel[@IsVisible='@IsAcceptableItemSelected']/Children");
            if (content == null || content.SelectSingleNode("BellumDiplomacyOverview") != null) return;

            var diplomacyScroll = content.SelectSingleNode("Widget[@Id='ScrollContainer']") as XmlElement;
            if (diplomacyScroll != null)
            {
                var buttons = content.SelectSingleNode("Widget[Children/ButtonWidget[@Id='OverviewButton']]");
                if (buttons == null || diplomacyScroll.SelectSingleNode(".//OverviewTab") == null) return;
                content.ReplaceChild(document.CreateElement("BellumDiplomacyOverviewButtons"), buttons);
                content.InsertBefore(document.CreateElement("BellumDiplomacyOverview"), diplomacyScroll);
                // StatsTab already owns its scrollable stat bars and war log. Avoid
                // nesting it in the old fixed-height overview scroll container.
                content.ReplaceChild(document.CreateElement("StatsTab"), diplomacyScroll);
            }
            else
            {
                var stats = content.SelectNodes("Widget[@IsVisible='@IsDisplayingWarLogs' or @IsHidden='@IsDisplayingWarLogs']")
                    .Cast<XmlElement>().ToList();
                if (stats.Count != 2) return;
                var wrapper = document.CreateElement("Widget");
                wrapper.SetAttribute("WidthSizePolicy", "StretchToParent");
                wrapper.SetAttribute("HeightSizePolicy", "StretchToParent");
                wrapper.SetAttribute("IsVisible", "@BellumStatsSelected");
                wrapper.SetAttribute("MarginBottom", "110");
                content.InsertBefore(document.CreateElement("BellumDiplomacyOverviewButtons"), stats[0]);
                content.InsertBefore(document.CreateElement("BellumDiplomacyOverview"), stats[0]);
                content.InsertBefore(wrapper, stats[0]);
                var children = document.CreateElement("Children");
                wrapper.AppendChild(children);
                foreach (var stat in stats)
                {
                    stat.SetAttribute("MarginBottom", "0");
                    children.AppendChild(stat);
                }
            }
            foreach (XmlElement controls in document.SelectNodes("//*[@Id='StatTypes']"))
                controls.SetAttribute("IsVisible", "@BellumStatsControlsVisible");
        }
    }

    [PrefabExtension("StatsTab", null)]
    internal sealed class DiplomacyOverviewStatsVisibilityPatch : CustomPatch<XmlDocument>
    {
        public override string Id => "BellumDiplomacyStatsVisibility";
        public override void Apply(XmlDocument document)
        {
            var root = document.SelectSingleNode("/Prefab/Window/Widget[@IsVisible='@ShowStats']") as XmlElement;
            if (root == null) return;
            root.SetAttribute("IsVisible", "@BellumStatsSelected");
            root.SetAttribute("HeightSizePolicy", "StretchToParent");
            root.SetAttribute("MarginBottom", "110");
            root.SetAttribute("MarginTop", "0");
            foreach (XmlElement section in root.SelectNodes("Children/Widget[@IsVisible='@IsDisplayingWarLogs' or @IsHidden='@IsDisplayingWarLogs']"))
                section.SetAttribute("MarginBottom", "0");
        }
    }
}
