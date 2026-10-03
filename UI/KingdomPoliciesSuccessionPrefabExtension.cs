using System.Collections.Generic;
using System.Xml;
using Bannerlord.UIExtenderEx.Attributes;
using Bannerlord.UIExtenderEx.Prefabs2;
using JetBrains.Annotations;

namespace BellumCivile.ViewModelMixin
{
    [PrefabExtension("PoliciesPanel", "descendant::Widget[@Id='ActivePoliciesParentWidget']")]
    [UsedImplicitly]
    internal sealed class KingdomPoliciesActiveContainerExtension : PrefabExtensionInsertPatch
    {
        public override InsertType Type => InsertType.ReplaceKeepChildren;

        private readonly XmlDocument _document;

        public KingdomPoliciesActiveContainerExtension()
        {
            _document = new XmlDocument();
            _document.LoadXml(@"
                <ListPanel Id='ActivePoliciesParentWidget'
                    WidthSizePolicy='CoverChildren' HeightSizePolicy='CoverChildren'
                    StackLayout.LayoutMethod='VerticalTopToBottom' />");
        }

        [PrefabExtensionXmlDocument]
        [UsedImplicitly]
        public XmlDocument GetPrefabExtension() => _document;
    }

    [PrefabExtension("PoliciesPanel", "descendant::NavigatableListPanel[@Id='ActivePoliciesList']")]
    [UsedImplicitly]
    internal sealed class KingdomPoliciesSuccessionEntryExtension : PrefabExtensionInsertPatch
    {
        public override InsertType Type => InsertType.Prepend;

        private readonly XmlDocument _document;

        public KingdomPoliciesSuccessionEntryExtension()
        {
            _document = new XmlDocument();
            _document.LoadXml(@"
                <ButtonWidget Id='BellumSuccessionLawsPolicyEntry'
                    DoNotPassEventsToChildren='true'
                    WidthSizePolicy='Fixed' HeightSizePolicy='Fixed'
                    SuggestedWidth='585' SuggestedHeight='87'
                    Brush='Kingdom.Policy.Active.Tuple'
                    Command.Click='ExecuteShowSuccessionLaws'
                    IsSelected='@BellumSuccessionSelected'
                    UpdateChildrenStates='true'>
                  <Children>
                    <TextWidget DoNotAcceptEvents='true'
                        WidthSizePolicy='StretchToParent' HeightSizePolicy='StretchToParent'
                        Brush='Kingdom.PoliciesItem.Text' Text='@SuccessionLawsEntryText' />
                  </Children>
                </ButtonWidget>");
        }

        [PrefabExtensionXmlDocument]
        [UsedImplicitly]
        public XmlDocument GetPrefabExtension() => _document;
    }

    [PrefabExtension("PoliciesPanel", "descendant::TextWidget[@Text='@NoItemSelectedText']")]
    [UsedImplicitly]
    internal sealed class KingdomPoliciesSuccessionPanelExtension : PrefabExtensionInsertPatch
    {
        public override InsertType Type => InsertType.Append;

        private readonly XmlDocument _document;

        public KingdomPoliciesSuccessionPanelExtension()
        {
            _document = new XmlDocument();
            _document.LoadXml("<BellumSuccessionPolicyPanel />");
        }

        [PrefabExtensionXmlDocument]
        [UsedImplicitly]
        public XmlDocument GetPrefabExtension() => _document;
    }

    [PrefabExtension("PoliciesPanel", "descendant::TextWidget[@Text='@NoItemSelectedText']")]
    [UsedImplicitly]
    internal sealed class KingdomPoliciesNoSelectionVisibilityExtension : PrefabExtensionSetAttributePatch
    {
        public override List<Attribute> Attributes => new List<Attribute>
        {
            new Attribute("IsHidden", "@HideNoPolicySelectionText")
        };
    }
}
