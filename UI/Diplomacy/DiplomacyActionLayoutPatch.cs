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
            var actions = document.SelectNodes("//ListPanel[@DataSource='{Actions}']/ItemTemplate/ListPanel");
            if (actions == null) return;
            foreach (XmlElement column in actions)
            {
                var explanation = column.SelectSingleNode("Children/TextWidget[@Text='@Explanation']") as XmlElement;
                if (explanation == null || column.SelectSingleNode("Children/Widget[@Id='KingdomDiplomacyAction']") == null)
                    continue;
                column.SetAttribute("MarginLeft", "3");
                column.SetAttribute("MarginRight", "3");
                explanation.SetAttribute("SuggestedWidth", "270");
            }
        }
    }
}
