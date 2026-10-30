using System.Xml;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Bannerlord.UIExtenderEx.Attributes;
using Bannerlord.UIExtenderEx.Prefabs;
using BellumCivile.UI;

namespace BellumCivile.ViewModelMixin
{
    [PrefabExtension("EncyclopediaHeroPage", null)]
    internal sealed class EncyclopediaHeroFamilyPrefabPatch : CustomPatch<XmlDocument>
    {
        private static readonly ConditionalWeakTable<XmlDocument, List<XmlElement>> HiddenNativeNodes
            = new ConditionalWeakTable<XmlDocument, List<XmlElement>>();

        public override string Id => "BellumHeroFamily";

        public override void Apply(XmlDocument document)
        {
            var hidden = HiddenNativeNodes.GetValue(document, _ => new List<XmlElement>());
            foreach (var node in hidden)
                if (node.GetAttribute("IsHidden") == "true") node.RemoveAttribute("IsHidden");
            hidden.Clear();
            EncyclopediaFamilyLayoutCompatibility.Refresh(document);
            if (!EncyclopediaFamilyLayoutCompatibility.Enabled) return;
            var grid = document.SelectSingleNode("//NavigatableGridWidget[@Id='FamilyGrid' and @DataSource='{Family}']");
            var divider = grid?.ParentNode?.SelectSingleNode("EncyclopediaDivider[@Id='FamilyDivider']");
            // Preserve native anchors, data sources and sibling paths for other prefab extensions.
            foreach (XmlElement node in grid.ParentNode.SelectNodes("NavigationScopeTargeter[@ScopeParent='..\\FamilyGrid']"))
                hidden.Add(node);
            hidden.Add((XmlElement)divider);
            hidden.Add((XmlElement)grid);
            // The grid is list-bound, so it cannot resolve a visibility property on the page VM.
            foreach (var node in hidden) node.SetAttribute("IsHidden", "true");
        }

        internal static void InsertSection(XmlDocument document)
        {
            XmlNode allies = document.SelectSingleNode("//EncyclopediaDivider[@Id='AlliesDivider']");
            if (allies == null || document.SelectSingleNode("//*[@Id='BellumFamilySection']") != null) return;
            var section = Element(document, "ListPanel", "Id", "BellumFamilySection", "DataSource", "{BellumFamily}",
                "WidthSizePolicy", "StretchToParent", "HeightSizePolicy", "CoverChildren", "StackLayout.LayoutMethod", "VerticalTopToBottom", "IsVisible", "@IsVisible");
            var content = Children(section);
            content.AppendChild(Element(document, "EncyclopediaDivider", "Id", "BellumFamilyDivider", "MarginTop", "30",
                "Parameter.Title", "@Header", "Parameter.ItemList", "..\\BellumFamilyBody", "GamepadNavigationIndex", "0"));
            var body = Element(document, "ListPanel", "Id", "BellumFamilyBody", "WidthSizePolicy", "StretchToParent",
                "HeightSizePolicy", "CoverChildren", "MarginLeft", "20", "MarginTop", "10", "StackLayout.LayoutMethod", "VerticalTopToBottom");
            content.AppendChild(body);
            var rows = Children(body);
            var ancestors = Row(document, "Ancestors", "@HasAncestors");
            rows.AppendChild(ancestors);
            Group(ancestors, "Parents", "200", "2", "@HasParents", marginRight: "@ParentsGap");
            Group(ancestors, "Grandparents", "400", "4", "@HasGrandparents");
            var household = Row(document, "Household", "@HasHousehold");
            rows.AppendChild(household);
            Group(household, "Spouses", "100", "1", "@HasSpouses", marginRight: "@SpousesGap");
            Group(household, "Children", "@ChildrenWidth", "6", "@HasChildren", sharedWhen: "@HasSpouses");
            var relatives = Row(document, "SiblingsAndGrandchildren", "@HasDescendantsOrSiblings");
            rows.AppendChild(relatives);
            Group(relatives, "Siblings", "@SiblingsWidth", "3", "@HasSiblings", sharedWhen: "@HasGrandchildren", marginRight: "@SiblingsGap");
            Group(relatives, "Grandchildren", "@GrandchildrenWidth", "4", "@HasGrandchildren", sharedWhen: "@HasSiblings");
            allies.ParentNode.InsertBefore(section, allies);
        }

        private static XmlElement Row(XmlDocument document, string id, string visible)
        {
            var row = Element(document, "ListPanel", "Id", "BellumFamily" + id, "WidthSizePolicy", "StretchToParent",
                "HeightSizePolicy", "CoverChildren", "MarginBottom", "12", "StackLayout.LayoutMethod", "HorizontalLeftToRight");
            if (visible != null) row.SetAttribute("IsVisible", visible);
            Children(row);
            return row;
        }

        private static void Group(XmlElement row, string name, string width, string columns, string visible,
            string sharedWhen = null, string marginRight = "0")
        {
            var document = row.OwnerDocument;
            var group = Element(document, "ListPanel", "Id", "BellumFamily" + name, "WidthSizePolicy", "Fixed", "SuggestedWidth", width,
                "HeightSizePolicy", "CoverChildren", "MarginRight", marginRight, "VerticalAlignment", "Top", "StackLayout.LayoutMethod", "VerticalTopToBottom");
            if (visible != null) group.SetAttribute("IsVisible", visible);
            row.SelectSingleNode("Children").AppendChild(group);
            var content = Children(group);
            var heading = Element(document, "Widget", "WidthSizePolicy", "StretchToParent", "HeightSizePolicy", "Fixed", "SuggestedHeight", "30");
            content.AppendChild(heading);
            var text = Element(document, "TextWidget", "DoNotAcceptEvents", "true", "WidthSizePolicy", "StretchToParent",
                "HeightSizePolicy", "StretchToParent", "Text", "@" + name + "Text", "Brush", "Encyclopedia.SubPage.Info.Text");
            Children(heading).AppendChild(text);
            if (sharedWhen == null)
                Grid(content, name, columns, false);
            else
            {
                // A list-bound grid cannot read column counts from the enclosing family VM.
                // Keep the mode on its parent and use literal counts for native and compiled prefabs alike.
                foreach (bool expanded in new[] { false, true })
                {
                    var mode = Element(document, "Widget", "WidthSizePolicy", "StretchToParent", "HeightSizePolicy", "CoverChildren",
                        expanded ? "IsHidden" : "IsVisible", sharedWhen);
                    content.AppendChild(mode);
                    Grid(Children(mode), name, expanded ? "7" : columns, expanded);
                }
            }
        }

        private static void Grid(XmlNode content, string name, string columns, bool expanded)
        {
            var document = content.OwnerDocument;
            string id = name + (expanded ? "Expanded" : string.Empty) + "Grid";
            content.AppendChild(Element(document, "NavigationScopeTargeter", "ScopeID", "BellumFamily" + id + "Scope",
                "ScopeParent", "..\\" + id, "ScopeMovements", "Horizontal", "AlternateScopeMovements", "Vertical",
                "AlternateMovementStepSize", columns));
            var grid = Element(document, "NavigatableGridWidget", "Id", id, "DataSource", "{" + name + "}",
                "WidthSizePolicy", "CoverChildren", "HeightSizePolicy", "CoverChildren", "DefaultCellWidth", "100", "DefaultCellHeight", "140",
                "ColumnCount", columns, "MarginTop", "5", "AutoScrollTopOffset", "35", "AutoScrollBottomOffset", "35");
            content.AppendChild(grid);
            var template = Element(document, "ItemTemplate");
            grid.AppendChild(template);
            var cell = Element(document, "ListPanel", "WidthSizePolicy", "Fixed", "SuggestedWidth", "100", "HeightSizePolicy", "Fixed",
                "SuggestedHeight", "140", "StackLayout.LayoutMethod", "VerticalTopToBottom");
            template.AppendChild(cell);
            var cellContent = Children(cell);
            cellContent.AppendChild(Element(document, "TextWidget", "DoNotAcceptEvents", "true", "WidthSizePolicy", "StretchToParent",
                "HeightSizePolicy", "Fixed", "SuggestedHeight", "36", "Brush", "Encyclopedia.SubPage.Element.Name.Text", "Text", "@Role"));
            var portrait = Element(document, "Widget", "WidthSizePolicy", "Fixed", "SuggestedWidth", "100", "HeightSizePolicy", "Fixed", "SuggestedHeight", "100");
            cellContent.AppendChild(portrait);
            Children(portrait).AppendChild(Element(document, "EncyclopediaSubPageElement", "IsVisible", "@IsKnown", "VerticalAlignment", "Top", "GamepadNavigationIndex", "0"));
        }

        private static XmlElement Children(XmlElement parent)
        {
            var children = parent.OwnerDocument.CreateElement("Children");
            parent.AppendChild(children);
            return children;
        }

        private static XmlElement Element(XmlDocument document, string tag, params string[] attributes)
        {
            var element = document.CreateElement(tag);
            for (int i = 0; i < attributes.Length; i += 2) element.SetAttribute(attributes[i], attributes[i + 1]);
            return element;
        }
    }
}
