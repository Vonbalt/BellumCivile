using Bannerlord.UIExtenderEx.Attributes;
using Bannerlord.UIExtenderEx.Prefabs2;
using JetBrains.Annotations;
using System.Xml;

namespace BellumCivile.ViewModelMixin
{
    [PrefabExtension("KingdomManagement", "descendant::ButtonWidget[@Id='FiefsTabButton']")]
    [UsedImplicitly]
    internal sealed class KingdomManagementPrefabExtension : PrefabExtensionInsertPatch
    {
        public override InsertType Type => InsertType.Append;

        private readonly XmlDocument _document;

        public KingdomManagementPrefabExtension()
        {
            _document = new XmlDocument();
            _document.LoadXml(@"
                <ButtonWidget Id='BellumFactionsTabButton' IsSelected='@BellumFactionsSelected' DoNotPassEventsToChildren='true' WidthSizePolicy='Fixed' HeightSizePolicy='Fixed' SuggestedWidth='!Header.Tab.Center.Width.Scaled' SuggestedHeight='!Header.Tab.Center.Height.Scaled' VerticalAlignment='Center' PositionYOffset='2' Brush='Header.Tab.Center' Command.Click='ExecuteShowFactions' UpdateChildrenStates='true' IsVisible='@ShowBellumCivileFactionsTab'>
                  <Children>
                    <TextWidget DataSource='{..}' WidthSizePolicy='CoverChildren' HeightSizePolicy='CoverChildren' HorizontalAlignment='Center' VerticalAlignment='Center' Brush='Clan.TabControl.Text' Text='@FactionsLabel' />
                  </Children>
                </ButtonWidget>");
        }

        [PrefabExtensionXmlDocument]
        [UsedImplicitly]
        public XmlDocument GetPrefabExtension() => _document;
    }

    [PrefabExtension("KingdomManagement", "descendant::Constant[@Name='Header.Tab.Center.Width.Scaled']")]
    [UsedImplicitly]
    internal sealed class KingdomManagementScalingPatch : PrefabExtensionSetAttributePatch
    {
        public override System.Collections.Generic.List<Attribute> Attributes => new System.Collections.Generic.List<Attribute>()
        {
            new Attribute("MultiplyResult", "0.52")
        };
    }

    [PrefabExtension("KingdomManagement", "descendant::ButtonWidget[@Id='FiefsTabButton']")]
    [UsedImplicitly]
    internal sealed class KingdomManagementHierarchyButtonExtension : PrefabExtensionInsertPatch
    {
        public override InsertType Type => InsertType.Append;

        private readonly XmlDocument _document;

        public KingdomManagementHierarchyButtonExtension()
        {
            _document = new XmlDocument();
            _document.LoadXml(@"
                <ButtonWidget Id='BellumHierarchyTabButton' IsSelected='@BellumHierarchySelected' DoNotPassEventsToChildren='true' WidthSizePolicy='Fixed' HeightSizePolicy='Fixed' SuggestedWidth='!Header.Tab.Center.Width.Scaled' SuggestedHeight='!Header.Tab.Center.Height.Scaled' VerticalAlignment='Center' PositionYOffset='2' Brush='Header.Tab.Center' Command.Click='ExecuteShowHierarchy' UpdateChildrenStates='true'>
                  <Children>
                    <TextWidget DataSource='{..}' WidthSizePolicy='CoverChildren' HeightSizePolicy='CoverChildren' HorizontalAlignment='Center' VerticalAlignment='Center' Brush='Clan.TabControl.Text' Text='@HierarchyLabel' />
                  </Children>
                </ButtonWidget>");
        }

        [PrefabExtensionXmlDocument]
        [UsedImplicitly]
        public XmlDocument GetPrefabExtension() => _document;
    }

    [PrefabExtension("KingdomManagement", "descendant::DiplomacyPanel[@Id='DiplomacyPanel']")]
    [UsedImplicitly]
    internal sealed class KingdomManagementBellumFactionsPanelExtension : PrefabExtensionInsertPatch
    {
        public override InsertType Type => InsertType.Append;

        private readonly XmlDocument _document;

        public KingdomManagementBellumFactionsPanelExtension()
        {
            _document = new XmlDocument();
            _document.LoadXml("<BellumFactionsPanel />");
        }

        [PrefabExtensionXmlDocument]
        [UsedImplicitly]
        public XmlDocument GetPrefabExtension() => _document;
    }

    [PrefabExtension("KingdomManagement", "descendant::DiplomacyPanel[@Id='DiplomacyPanel']")]
    [UsedImplicitly]
    internal sealed class KingdomManagementBellumHierarchyPanelExtension : PrefabExtensionInsertPatch
    {
        public override InsertType Type => InsertType.Append;

        private readonly XmlDocument _document;

        public KingdomManagementBellumHierarchyPanelExtension()
        {
            _document = new XmlDocument();
            _document.LoadXml("<BellumHierarchyPanel />");
        }

        [PrefabExtensionXmlDocument]
        [UsedImplicitly]
        public XmlDocument GetPrefabExtension() => _document;
    }

}
