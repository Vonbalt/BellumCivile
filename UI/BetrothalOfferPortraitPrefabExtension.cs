using System.Xml;
using Bannerlord.UIExtenderEx.Attributes;
using Bannerlord.UIExtenderEx.Prefabs2;
using JetBrains.Annotations;

namespace BellumCivile.ViewModelMixin
{
    [PrefabExtension("MarriageOfferPopup", "descendant::Widget[@Id='MarriageClipRect']/Children/Widget[@AlphaFactor='2']")]
    [UsedImplicitly]
    internal sealed class BetrothalOfferPortraitPrefabExtension : PrefabExtensionInsertPatch
    {
        public override InsertType Type => InsertType.Append;

        [PrefabExtensionXmlNode]
        public XmlDocument GetPrefabExtension()
        {
            var xml = new XmlDocument();
            xml.LoadXml(@"<ListPanel Id='BellumBetrothalPortraits' WidthSizePolicy='StretchToParent' HeightSizePolicy='StretchToParent'
                  StackLayout.LayoutMethod='HorizontalSpaced' DoNotAcceptEvents='true'>
              <Children>
                <Widget DataSource='{OffereeClanMember}' WidthSizePolicy='Fixed' HeightSizePolicy='StretchToParent' SuggestedWidth='400' DoNotAcceptEvents='true'>
                  <Children>
                    <Widget IsVisible='@BellumBetrothalChildPortrait' WidthSizePolicy='Fixed' HeightSizePolicy='Fixed' SuggestedWidth='176' SuggestedHeight='126'
                        HorizontalAlignment='Center' VerticalAlignment='Top' MarginTop='196' DoNotAcceptEvents='true' Sprite='Encyclopedia\portrait_baby'/>
                  </Children>
                </Widget>
                <Widget DataSource='{OffererClanMember}' WidthSizePolicy='Fixed' HeightSizePolicy='StretchToParent' SuggestedWidth='400' DoNotAcceptEvents='true'>
                  <Children>
                    <Widget IsVisible='@BellumBetrothalChildPortrait' WidthSizePolicy='Fixed' HeightSizePolicy='Fixed' SuggestedWidth='176' SuggestedHeight='126'
                        HorizontalAlignment='Center' VerticalAlignment='Top' MarginTop='196' DoNotAcceptEvents='true' Sprite='Encyclopedia\portrait_baby'/>
                  </Children>
                </Widget>
              </Children>
            </ListPanel>");
            return xml;
        }
    }
}
