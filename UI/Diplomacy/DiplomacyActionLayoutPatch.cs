using System.Xml;
using Bannerlord.UIExtenderEx.Attributes;
using Bannerlord.UIExtenderEx.Prefabs;

namespace BellumCivile.ViewModelMixin
{
    [PrefabExtension("DiplomacyPanel", null)]
    [PrefabExtension("DiplomacyPanelButtons", null)]
    internal sealed class DiplomacyActionLayoutPatch : CustomPatch<XmlDocument>
    {
        public override string Id => "BellumDiplomacyActionLayout";

        public override void Apply(XmlDocument document)
        {
            var externalButtons = document.SelectNodes("//ListPanel[@DataSource='{CurrentSelectedDiplomacyItem}' and (@IsVisible='@IsDirectActionVisible' or @IsVisible='@IsNonAggressionPactVisible')]");
            if (externalButtons != null)
            {
                foreach (XmlElement list in externalButtons)
                {
                    var parent = list.ParentNode as XmlElement;
                    if (parent == null || parent.Name != "Children"
                        || list.SelectSingleNode("ancestor::Widget[@Id='BellumDiplomacyExternalAction']") != null)
                        continue;
                    var wrapper = document.CreateElement("Widget");
                    wrapper.SetAttribute("Id", "BellumDiplomacyExternalAction");
                    wrapper.SetAttribute("WidthSizePolicy", "CoverChildren");
                    wrapper.SetAttribute("HeightSizePolicy", "CoverChildren");
                    wrapper.SetAttribute("IsVisible", "@BellumDiplomacyExternalButtonsVisible");
                    var children = document.CreateElement("Children");
                    wrapper.AppendChild(children);
                    parent.ReplaceChild(wrapper, list);
                    children.AppendChild(list);
                }
            }
            var actions = document.SelectNodes("//ListPanel[@DataSource='{Actions}']");
            if (actions == null) return;
            foreach (XmlElement list in actions)
            {
                var parent = list.ParentNode as XmlElement;
                if (parent?.Name != "Children" || list.SelectSingleNode("ancestor::Widget[@Id='BellumNativeDiplomacyActions']") != null
                    || parent.SelectSingleNode("./Widget[@Id='BellumNativeDiplomacyActions']") != null
                    || list.SelectSingleNode("ItemTemplate/ListPanel/Children/Widget[@Id='KingdomDiplomacyAction']") == null)
                    continue;

                var native = document.CreateElement("Widget");
                native.SetAttribute("Id", "BellumNativeDiplomacyActions");
                native.SetAttribute("WidthSizePolicy", "CoverChildren");
                native.SetAttribute("HeightSizePolicy", "CoverChildren");
                native.SetAttribute("IsVisible", "@BellumNativeDiplomacyActionsVisible");
                var nativeChildren = document.CreateElement("Children");
                native.AppendChild(nativeChildren);
                parent.ReplaceChild(native, list);
                nativeChildren.AppendChild(list);

                var button = document.CreateElement("ButtonWidget");
                button.SetAttribute("Id", "BellumDiplomacyActionButton");
                button.SetAttribute("WidthSizePolicy", "Fixed");
                button.SetAttribute("HeightSizePolicy", "Fixed");
                button.SetAttribute("SuggestedWidth", "227");
                button.SetAttribute("SuggestedHeight", "40");
                button.SetAttribute("HorizontalAlignment", "Center");
                button.SetAttribute("Brush", "ButtonBrush2");
                button.SetAttribute("UpdateChildrenStates", "true");
                button.SetAttribute("DoNotPassEventsToChildren", "true");
                button.SetAttribute("IsVisible", "@BellumCentralDiplomacyActionVisible");
                button.SetAttribute("Command.Click", "ExecuteBellumDiplomaticAction");
                var children = document.CreateElement("Children");
                var label = document.CreateElement("TextWidget");
                label.SetAttribute("WidthSizePolicy", "StretchToParent");
                label.SetAttribute("HeightSizePolicy", "StretchToParent");
                label.SetAttribute("Brush", "Kingdom.GeneralButtons.Text");
                label.SetAttribute("Text", "@BellumDiplomaticActionText");
                children.AppendChild(label);
                button.AppendChild(children);
                parent.AppendChild(button);
            }
        }
    }
}
