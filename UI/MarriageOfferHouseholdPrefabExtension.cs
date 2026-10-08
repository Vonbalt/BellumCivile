using System.Xml;
using Bannerlord.UIExtenderEx.Attributes;
using Bannerlord.UIExtenderEx.Prefabs2;
using JetBrains.Annotations;

namespace BellumCivile.ViewModelMixin
{
    // Keep vanilla's 250-wide gap between the skill panels; scroll longer terms inside it.
    [PrefabExtension("MarriageOfferPopup", "descendant::ListPanel[@DataSource='{ConsequencesList}']")]
    [UsedImplicitly]
    internal sealed class MarriageOfferHouseholdPrefabExtension : PrefabExtensionInsertPatch
    {
        public override InsertType Type => InsertType.Replace;
        [PrefabExtensionXmlDocument]
        public XmlDocument GetPrefabExtension()
        {
            var xml = new XmlDocument();
            xml.LoadXml(@"<ScrollablePanel WidthSizePolicy='Fixed' HeightSizePolicy='Fixed' SuggestedWidth='250' SuggestedHeight='180'
                HorizontalAlignment='Center' MarginTop='6' ClipContents='true' AutoHideScrollBars='true'
                InnerPanel='Clip\Terms' ClipRect='Clip' VerticalScrollbar='Scrollbar'>
              <Children>
                <Widget Id='Clip' WidthSizePolicy='StretchToParent' HeightSizePolicy='StretchToParent' MarginRight='12' ClipContents='true'>
                  <Children>
                    <ListPanel Id='Terms' DataSource='{ConsequencesList}' WidthSizePolicy='StretchToParent' HeightSizePolicy='CoverChildren'
                        StackLayout.LayoutMethod='VerticalTopToBottom'>
                      <ItemTemplate>
                        <RichTextWidget WidthSizePolicy='StretchToParent' HeightSizePolicy='CoverChildren' MarginBottom='6'
                            Brush='Clan.MarriagePopup.Paragraph.Text' Brush.FontSize='18' Brush.TextHorizontalAlignment='Left' Text='@Item'/>
                      </ItemTemplate>
                    </ListPanel>
                  </Children>
                </Widget>
                <ScrollbarWidget Id='Scrollbar' WidthSizePolicy='Fixed' HeightSizePolicy='StretchToParent' SuggestedWidth='8'
                    HorizontalAlignment='Right' MinValue='0' MaxValue='100' AlignmentAxis='Vertical' Handle='Handle'>
                  <Children><Widget Id='Handle' WidthSizePolicy='StretchToParent' HeightSizePolicy='Fixed' SuggestedHeight='40'
                      Sprite='BlankWhiteSquare_9' Color='#B9A47AFF'/></Children>
                </ScrollbarWidget>
              </Children>
            </ScrollablePanel>");
            return xml;
        }
    }
}
