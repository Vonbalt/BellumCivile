using Bannerlord.UIExtenderEx.Attributes;
using Bannerlord.UIExtenderEx.Prefabs2;
using JetBrains.Annotations;
using System.Collections.Generic;
using System.Xml;

namespace BellumCivile.ViewModelMixin
{
    [PrefabExtension("EncyclopediaClanPage", "descendant::GridWidget[@Id='Info']/ItemTemplate/ListPanel/Children/AutoHideRichTextWidget[@Text='@Value']")]
    [UsedImplicitly]
    internal sealed class EncyclopediaClanSuccessionHintPrefabExtension : PrefabExtensionInsertPatch
    {
        public override InsertType Type => InsertType.Append;

        private readonly XmlDocument _document;

        public EncyclopediaClanSuccessionHintPrefabExtension()
        {
            _document = new XmlDocument();
            _document.LoadXml(@"
                <HintWidget DataSource='{Hint}' WidthSizePolicy='StretchToParent' HeightSizePolicy='StretchToParent' Command.HoverBegin='ExecuteBeginHint' Command.HoverEnd='ExecuteEndHint' />");
        }

        [PrefabExtensionXmlDocument]
        [UsedImplicitly]
        public XmlDocument GetPrefabExtension() => _document;
    }

    [PrefabExtension("EncyclopediaClanPage", "descendant::GridWidget[@Id='Info']/ItemTemplate/ListPanel/Children/AutoHideRichTextWidget[@Text='@Definition']")]
    [UsedImplicitly]
    internal sealed class EncyclopediaClanSuccessionDefinitionEventsPatch : PrefabExtensionSetAttributePatch
    {
        public override List<Attribute> Attributes => new List<Attribute>
        {
            new Attribute("DoNotAcceptEvents", "true")
        };
    }

    [PrefabExtension("EncyclopediaClanPage", "descendant::GridWidget[@Id='Info']/ItemTemplate/ListPanel/Children/AutoHideRichTextWidget[@Text='@Value']")]
    [UsedImplicitly]
    internal sealed class EncyclopediaClanSuccessionValueEventsPatch : PrefabExtensionSetAttributePatch
    {
        public override List<Attribute> Attributes => new List<Attribute>
        {
            new Attribute("DoNotAcceptEvents", "true")
        };
    }
}
