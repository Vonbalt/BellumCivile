using System.Xml;

namespace BellumCivile.UI
{
    internal static class EncyclopediaClanSupportPrefab
    {
        internal static bool Enabled { get; private set; }

        internal static void Apply(XmlDocument document)
        {
            Enabled = false;
            foreach (XmlNode old in document.SelectNodes("//*[@Id='BellumClanSupportSection']"))
                old.ParentNode.RemoveChild(old);
            if (!EncyclopediaFamilyPrefabFinalizationPatch.Supported) return;

            var household = SingleAnchor(document, "//Children/ListPanel[@Id='BellumClanHousehold']");
            var settlements = household == null ? SingleAnchor(document,
                "//Children[ NavigatableGridWidget[@Id='SettlementsGrid' and @DataSource='{Settlements}'] ]/EncyclopediaDivider[@Id='SettlementsDivider' and @Parameter.ItemList='..\\SettlementsGrid']") : null;
            if (household == null && settlements == null) return;

            // Gauntlet treats every child node as a widget; parse without retaining indentation.
            var section = new XmlDocument { PreserveWhitespace = false };
            section.LoadXml(@"
                <ListPanel Id='BellumClanSupportSection' IsVisible='@HasBellumClanSupport' WidthSizePolicy='StretchToParent' HeightSizePolicy='CoverChildren' StackLayout.LayoutMethod='VerticalTopToBottom'>
                    <Children>
                        <EncyclopediaDivider Id='BellumClanSupportDivider' MarginTop='30' Parameter.Title='@BellumClanSupportTitle' Parameter.ItemList='..\BellumClanSupportGrid' GamepadNavigationIndex='0'/>
                        <NavigationScopeTargeter ScopeID='BellumClanSupportScope' ScopeParent='..\BellumClanSupportGrid' ScopeMovements='Horizontal' AlternateScopeMovements='Vertical' AlternateMovementStepSize='7'/>
                        <NavigatableGridWidget Id='BellumClanSupportGrid' DataSource='{BellumClanSupport}' WidthSizePolicy='StretchToParent' HeightSizePolicy='CoverChildren' DefaultCellWidth='100' DefaultCellHeight='100' HorizontalAlignment='Left' ColumnCount='7' MarginTop='10' MarginLeft='15' AutoScrollTopOffset='35' AutoScrollBottomOffset='35'>
                            <ItemTemplate><EncyclopediaClanSubPageElement/></ItemTemplate>
                        </NavigatableGridWidget>
                    </Children>
                </ListPanel>");
            var panel = document.ImportNode(section.DocumentElement, true);
            if (household != null) household.ParentNode.InsertAfter(panel, household);
            else settlements.ParentNode.InsertBefore(panel, settlements);
            Enabled = true;
        }

        private static XmlNode SingleAnchor(XmlDocument document, string xpath)
        {
            var anchors = document.SelectNodes(xpath);
            return anchors.Count == 1 ? anchors[0] : null;
        }
    }
}
