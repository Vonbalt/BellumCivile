using System.Xml;
using Bannerlord.UIExtenderEx.Attributes;
using Bannerlord.UIExtenderEx.Prefabs2;
using JetBrains.Annotations;

namespace BellumCivile.ViewModelMixin
{
    [PrefabExtension("ClanLordTuple", "descendant::TextWidget[@Text='@CurrentActionText']")]
    [UsedImplicitly]
    internal sealed class ClanLordPrisonerRansomLocationHintPrefabExtension
        : PrefabExtensionInsertPatch
    {
        private readonly XmlDocument _document;

        public ClanLordPrisonerRansomLocationHintPrefabExtension()
        {
            _document = new XmlDocument();
            _document.LoadXml(@"
                <Widget WidthSizePolicy='StretchToParent'
                    HeightSizePolicy='StretchToParent'
                    DoNotAcceptEvents='true'
                    MarginRight='10'>
                  <Children>
                    <TextWidget DoNotPassEventsToChildren='true'
                        WidthSizePolicy='StretchToParent'
                        HeightSizePolicy='StretchToParent'
                        Brush='Clan.Tuple.Location.Text'
                        Text='@CurrentActionText'
                        DoNotAcceptEvents='true' />
                    <HintWidget DataSource='{PrisonerRansomHint}'
                        WidthSizePolicy='StretchToParent'
                        HeightSizePolicy='StretchToParent'
                        DoNotAcceptEvents='true'
                        Command.HoverBegin='ExecuteBeginHint'
                        Command.HoverEnd='ExecuteEndHint' />
                  </Children>
                </Widget>");
        }

        public override InsertType Type => InsertType.Replace;

        [PrefabExtensionXmlDocument]
        [UsedImplicitly]
        public XmlDocument GetPrefabExtension() => _document;
    }
}
