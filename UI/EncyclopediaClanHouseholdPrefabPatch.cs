using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Xml;
using Bannerlord.UIExtenderEx.Attributes;
using Bannerlord.UIExtenderEx.Prefabs;

namespace BellumCivile.UI
{
    [PrefabExtension("EncyclopediaClanPage", null)]
    internal sealed class EncyclopediaClanHouseholdPrefabPatch : CustomPatch<XmlDocument>
    {
        internal static bool Enabled { get; private set; }
        private static readonly ConditionalWeakTable<XmlDocument, List<XmlElement>> Hidden = new ConditionalWeakTable<XmlDocument, List<XmlElement>>();
        public override string Id => "BellumClanHousehold";

        public override void Apply(XmlDocument document)
        {
            var hidden = Hidden.GetValue(document, _ => new List<XmlElement>());
            foreach (var node in hidden)
                if (node.GetAttribute("IsHidden") == "true") node.RemoveAttribute("IsHidden");
            hidden.Clear();
            foreach (XmlNode old in document.SelectNodes("//*[@Id='BellumClanHousehold']")) old.ParentNode.RemoveChild(old);
            var grid = document.SelectSingleNode("//NavigatableGridWidget[@Id='MembersGrid' and @DataSource='{Members}']") as XmlElement;
            var divider = grid?.ParentNode?.SelectSingleNode("EncyclopediaDivider[@Id='MembersDivider' and @Parameter.ItemList='..\\MembersGrid']") as XmlElement;
            Enabled = EncyclopediaFamilyPrefabFinalizationPatch.Supported
                && grid != null && divider != null && !grid.HasAttribute("IsHidden") && !divider.HasAttribute("IsHidden")
                && !grid.HasAttribute("IsVisible") && !divider.HasAttribute("IsVisible");
            if (!Enabled) return;
            var section = document.CreateElement("ListPanel");
            section.SetAttribute("Id", "BellumClanHousehold");
            section.SetAttribute("WidthSizePolicy", "StretchToParent");
            section.SetAttribute("HeightSizePolicy", "CoverChildren");
            section.SetAttribute("StackLayout.LayoutMethod", "VerticalTopToBottom");
            var content = document.CreateElement("Children");
            section.AppendChild(content);
            foreach (string group in new[] { "Family", "Retainers" })
            {
                var row = (XmlElement)section.CloneNode(false);
                row.SetAttribute("Id", "BellumClan" + group + "Section");
                row.SetAttribute("IsVisible", "@HasBellumClan" + group);
                content.AppendChild(row);
                var children = document.CreateElement("Children");
                row.AppendChild(children);
                var heading = (XmlElement)divider.CloneNode(true);
                heading.SetAttribute("Id", "BellumClan" + group + "Divider");
                heading.SetAttribute("MarginTop", "30");
                heading.SetAttribute("Parameter.Title", "@BellumClan" + group + "Text");
                heading.SetAttribute("Parameter.ItemList", "..\\BellumClan" + group + "Grid");
                children.AppendChild(heading);
                var scope = document.CreateElement("NavigationScopeTargeter");
                scope.SetAttribute("ScopeID", "BellumClan" + group + "Scope");
                scope.SetAttribute("ScopeParent", "..\\BellumClan" + group + "Grid");
                scope.SetAttribute("ScopeMovements", "Horizontal");
                scope.SetAttribute("AlternateScopeMovements", "Vertical");
                scope.SetAttribute("AlternateMovementStepSize", "7");
                children.AppendChild(scope);
                var portraits = (XmlElement)grid.CloneNode(true);
                portraits.SetAttribute("Id", "BellumClan" + group + "Grid");
                portraits.SetAttribute("DataSource", "{BellumClan" + group + "}");
                children.AppendChild(portraits);
            }
            divider.ParentNode.InsertBefore(section, divider);
            foreach (XmlElement scope in grid.ParentNode.SelectNodes("NavigationScopeTargeter[@ScopeParent='..\\MembersGrid']")) hidden.Add(scope);
            hidden.Add(grid);
            hidden.Add(divider);
            foreach (var node in hidden) node.SetAttribute("IsHidden", "true");
        }
    }
}
