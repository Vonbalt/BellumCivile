using Bannerlord.UIExtenderEx.Attributes;
using Bannerlord.UIExtenderEx.Prefabs2;
using JetBrains.Annotations;
using System.Collections.Generic;
using System.Xml;

namespace BellumCivile.ViewModelMixin
{
    [PrefabExtension("EncyclopediaHeroPage", "descendant::GridWidget[@Id='StatsGrid']/ItemTemplate/ListPanel/Children/AutoHideRichTextWidget[@Text='@Value']")]
    [UsedImplicitly]
    internal sealed class EncyclopediaHeroRelationThresholdPrefabExtension : PrefabExtensionInsertPatch
    {
        public override InsertType Type => InsertType.Append;

        private readonly XmlDocument _document;

        public EncyclopediaHeroRelationThresholdPrefabExtension()
        {
            _document = new XmlDocument();
            _document.LoadXml(@"
                <HintWidget DataSource='{Hint}' WidthSizePolicy='StretchToParent' HeightSizePolicy='StretchToParent' Command.HoverBegin='ExecuteBeginHint' Command.HoverEnd='ExecuteEndHint' />");
        }

        [PrefabExtensionXmlDocument]
        [UsedImplicitly]
        public XmlDocument GetPrefabExtension() => _document;
    }

    [PrefabExtension("EncyclopediaHeroPage", "descendant::GridWidget[@Id='StatsGrid']/ItemTemplate/ListPanel/Children/AutoHideRichTextWidget[@Text='@Definition']")]
    [UsedImplicitly]
    internal sealed class EncyclopediaHeroRelationThresholdDefinitionEventsPatch : PrefabExtensionSetAttributePatch
    {
        public override List<Attribute> Attributes => new List<Attribute>
        {
            new Attribute("DoNotAcceptEvents", "true")
        };
    }

    [PrefabExtension("EncyclopediaHeroPage", "descendant::GridWidget[@Id='StatsGrid']/ItemTemplate/ListPanel/Children/AutoHideRichTextWidget[@Text='@Value']")]
    [UsedImplicitly]
    internal sealed class EncyclopediaHeroRelationThresholdValueEventsPatch : PrefabExtensionSetAttributePatch
    {
        public override List<Attribute> Attributes => new List<Attribute>
        {
            new Attribute("DoNotAcceptEvents", "true")
        };
    }
}
