using Bannerlord.UIExtenderEx.Attributes;
using Bannerlord.UIExtenderEx.Prefabs2;
using JetBrains.Annotations;
using System.Xml;

namespace BellumCivile.ViewModelMixin
{
    [PrefabExtension("EncyclopediaHeroPage", "descendant::EncyclopediaDivider[@Id='AlliesDivider']")]
    [UsedImplicitly]
    internal sealed class EncyclopediaHeroFeudalTitlesPrefabExtension : PrefabExtensionInsertPatch
    {
        public override InsertType Type => InsertType.Replace;

        private readonly XmlDocument _document;

        public EncyclopediaHeroFeudalTitlesPrefabExtension()
        {
            _document = new XmlDocument();
            _document.LoadXml(@"
                <ListPanel WidthSizePolicy='StretchToParent' HeightSizePolicy='CoverChildren' StackLayout.LayoutMethod='VerticalTopToBottom'>
                  <Children>
                    <ListPanel WidthSizePolicy='StretchToParent' HeightSizePolicy='CoverChildren' StackLayout.LayoutMethod='VerticalTopToBottom' IsVisible='@HasBellumTitles'>
                      <Children>
                        <EncyclopediaDivider Id='BellumTitlesDivider' MarginTop='30' Parameter.Title='@BellumTitlesText' Parameter.ItemList='..\BellumTitlesGridParent' GamepadNavigationIndex='0'/>

                        <Widget Id='BellumTitlesGridParent' DoNotAcceptEvents='true' WidthSizePolicy='StretchToParent' HeightSizePolicy='CoverChildren' MarginTop='10' MarginLeft='30'>
                          <Children>
                            <GridWidget DataSource='{BellumTitles}' WidthSizePolicy='StretchToParent' HeightSizePolicy='CoverChildren' DefaultCellWidth='325' DefaultCellHeight='30' ColumnCount='2' HorizontalAlignment='Left'>
                              <ItemTemplate>
                                <ListPanel WidthSizePolicy='CoverChildren' HeightSizePolicy='CoverChildren' MarginLeft='15' MarginTop='3'>
                                  <Children>
                                    <AutoHideRichTextWidget HeightSizePolicy='CoverChildren' WidthSizePolicy='CoverChildren' VerticalAlignment='Bottom' Brush='Encyclopedia.Stat.DefinitionText' Text='@Definition' MarginRight='5'/>
                                    <AutoHideRichTextWidget HeightSizePolicy='CoverChildren' WidthSizePolicy='CoverChildren' VerticalAlignment='Bottom' Brush='Encyclopedia.Stat.ValueText' Text='@Value' />
                                  </Children>
                                </ListPanel>
                              </ItemTemplate>
                            </GridWidget>
                          </Children>
                        </Widget>
                      </Children>
                    </ListPanel>

                    <ListPanel WidthSizePolicy='StretchToParent' HeightSizePolicy='CoverChildren' StackLayout.LayoutMethod='VerticalTopToBottom' IsVisible='@HasBellumClaims'>
                      <Children>
                        <EncyclopediaDivider Id='BellumClaimsDivider' MarginTop='20' Parameter.Title='@BellumClaimsText' Parameter.ItemList='..\BellumClaimsGridParent' GamepadNavigationIndex='0'/>

                        <Widget Id='BellumClaimsGridParent' DoNotAcceptEvents='true' WidthSizePolicy='StretchToParent' HeightSizePolicy='CoverChildren' MarginTop='10' MarginLeft='30'>
                          <Children>
                            <GridWidget DataSource='{BellumClaims}' WidthSizePolicy='StretchToParent' HeightSizePolicy='CoverChildren' DefaultCellWidth='325' DefaultCellHeight='30' ColumnCount='2' HorizontalAlignment='Left'>
                              <ItemTemplate>
                                <ListPanel WidthSizePolicy='CoverChildren' HeightSizePolicy='CoverChildren' MarginLeft='15' MarginTop='3'>
                                  <Children>
                                    <AutoHideRichTextWidget HeightSizePolicy='CoverChildren' WidthSizePolicy='CoverChildren' VerticalAlignment='Bottom' Brush='Encyclopedia.Stat.DefinitionText' Text='@Definition' MarginRight='5'/>
                                    <AutoHideRichTextWidget HeightSizePolicy='CoverChildren' WidthSizePolicy='CoverChildren' VerticalAlignment='Bottom' Brush='Encyclopedia.Stat.ValueText' Text='@Value' />
                                  </Children>
                                </ListPanel>
                              </ItemTemplate>
                            </GridWidget>
                          </Children>
                        </Widget>
                      </Children>
                    </ListPanel>

                    <EncyclopediaDivider Id='AlliesDivider' MarginTop='50' Parameter.Title='@AlliesText' Parameter.ItemList='..\..\AlliesGridParent' GamepadNavigationIndex='0'/>
                  </Children>
                </ListPanel>");
        }

        [PrefabExtensionXmlDocument]
        [UsedImplicitly]
        public XmlDocument GetPrefabExtension() => _document;
    }
}
