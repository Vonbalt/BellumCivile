using Bannerlord.UIExtenderEx.Attributes;
using Bannerlord.UIExtenderEx.Prefabs2;
using JetBrains.Annotations;
using System.Xml;

namespace BellumCivile.ViewModelMixin
{
    [PrefabExtension("TruceTuple", "descendant::ButtonWidget/Children/ListPanel")]
    [UsedImplicitly]
    internal sealed class KingdomTruceClientSuzerainPrefabExtension : PrefabExtensionInsertPatch
    {
        public override InsertType Type => InsertType.Append;

        private readonly XmlDocument _document;

        public KingdomTruceClientSuzerainPrefabExtension()
        {
            _document = new XmlDocument();
            _document.LoadXml(@"
                <Widget DoNotAcceptEvents='true'
                    WidthSizePolicy='StretchToParent' HeightSizePolicy='StretchToParent'
                    IsVisible='@HasClientSuzerain'>
                  <Children>
                    <MaskedTextureWidget DataSource='{ClientSuzerainVisual}'
                        DoNotAcceptEvents='true'
                        RenderLate='true'
                        WidthSizePolicy='Fixed' HeightSizePolicy='Fixed'
                        SuggestedWidth='34' SuggestedHeight='43'
                        VerticalAlignment='Center' HorizontalAlignment='Right'
                        MarginRight='55' Brush='Flat.Tuple.Banner.Small'
                        AdditionalArgs='@AdditionalArgs' ImageId='@Id'
                        TextureProviderName='@TextureProviderName'
                        IsDisabled='true' />
                    <MaskedTextureWidget DataSource='{ClientSuzerainFarVisual}'
                        DoNotAcceptEvents='true'
                        RenderLate='true'
                        WidthSizePolicy='Fixed' HeightSizePolicy='Fixed'
                        SuggestedWidth='34' SuggestedHeight='43'
                        VerticalAlignment='Center' HorizontalAlignment='Right'
                        MarginRight='100' Brush='Flat.Tuple.Banner.Small'
                        AdditionalArgs='@AdditionalArgs' ImageId='@Id'
                        TextureProviderName='@TextureProviderName'
                        IsDisabled='true' />
                  </Children>
                </Widget>");
        }

        [PrefabExtensionXmlDocument]
        [UsedImplicitly]
        public XmlDocument GetPrefabExtension() => _document;
    }

    [PrefabExtension("WarTuple", "descendant::ButtonWidget/Children/ListPanel")]
    [UsedImplicitly]
    internal sealed class KingdomWarClientSuzerainPrefabExtension : PrefabExtensionInsertPatch
    {
        public override InsertType Type => InsertType.Append;

        private readonly XmlDocument _document;

        public KingdomWarClientSuzerainPrefabExtension()
        {
            _document = new XmlDocument();
            _document.LoadXml(@"
                <Widget DoNotAcceptEvents='true'
                    WidthSizePolicy='StretchToParent' HeightSizePolicy='StretchToParent'
                    IsVisible='@HasClientSuzerain'>
                  <Children>
                    <MaskedTextureWidget DataSource='{ClientSuzerainVisual}'
                        DoNotAcceptEvents='true'
                        RenderLate='true'
                        WidthSizePolicy='Fixed' HeightSizePolicy='Fixed'
                        SuggestedWidth='34' SuggestedHeight='43'
                        VerticalAlignment='Center' HorizontalAlignment='Right'
                        MarginRight='55' Brush='Flat.Tuple.Banner.Small'
                        AdditionalArgs='@AdditionalArgs' ImageId='@Id'
                        TextureProviderName='@TextureProviderName'
                        IsDisabled='true' />
                  </Children>
                </Widget>");
        }

        [PrefabExtensionXmlDocument]
        [UsedImplicitly]
        public XmlDocument GetPrefabExtension() => _document;
    }
}
