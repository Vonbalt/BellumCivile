using Bannerlord.UIExtenderEx.Attributes;
using Bannerlord.UIExtenderEx.Prefabs2;
using JetBrains.Annotations;
using System.Xml;

namespace BellumCivile.ViewModelMixin
{
    [PrefabExtension("PolicyActiveTuple", "descendant::ButtonWidget")]
    [UsedImplicitly]
    internal sealed class ActivePolicyFactionAgendaPrefabExtension : PrefabExtensionInsertPatch
    {
        public override InsertType Type => InsertType.Replace;

        private readonly XmlDocument _document;

        public ActivePolicyFactionAgendaPrefabExtension()
        {
            _document = PolicyFactionAgendaPrefabFactory.Create(
                "!Kingdom.Policy.Active.Tuple.Height",
                "!Kingdom.Policy.Active.Tuple.Width",
                "Kingdom.Policy.Active.Tuple");
        }

        [PrefabExtensionXmlDocument]
        [UsedImplicitly]
        public XmlDocument GetPrefabExtension() => _document;
    }

    [PrefabExtension("PolicyOtherTuple", "descendant::ButtonWidget")]
    [UsedImplicitly]
    internal sealed class OtherPolicyFactionAgendaPrefabExtension : PrefabExtensionInsertPatch
    {
        public override InsertType Type => InsertType.Replace;

        private readonly XmlDocument _document;

        public OtherPolicyFactionAgendaPrefabExtension()
        {
            _document = PolicyFactionAgendaPrefabFactory.Create(
                "!Kingdom.Policy.Other.Tuple.Height",
                "!Kingdom.Policy.Other.Tuple.Width",
                "Kingdom.Policy.Other.Tuple");
        }

        [PrefabExtensionXmlDocument]
        [UsedImplicitly]
        public XmlDocument GetPrefabExtension() => _document;
    }

    internal static class PolicyFactionAgendaPrefabFactory
    {
        internal static XmlDocument Create(string height, string width, string brush)
        {
            var document = new XmlDocument();
            document.LoadXml($@"
                <ButtonWidget DoNotPassEventsToChildren='true'
                    HeightSizePolicy='Fixed' WidthSizePolicy='Fixed'
                    SuggestedHeight='{height}' SuggestedWidth='{width}'
                    Brush='{brush}' Command.Click='OnSelect'
                    Command.HoverBegin='ExecuteBeginPolicyStanceHint' Command.HoverEnd='ExecuteEndPolicyStanceHint'
                    IsSelected='@IsSelected'>
                  <Children>
                    <Widget DoNotAcceptEvents='true' IsDisabled='true'
                        WidthSizePolicy='StretchToParent' HeightSizePolicy='StretchToParent'
                        Sprite='BlankWhiteSquare_9' Color='#315F9BFF' AlphaFactor='0.52'
                        IsVisible='@IsPlayerFactionAgendaPolicy' />
                    <Widget DoNotAcceptEvents='true' IsDisabled='true'
                        WidthSizePolicy='StretchToParent' HeightSizePolicy='StretchToParent'
                        Sprite='BlankWhiteSquare_9' Color='#9B3131FF' AlphaFactor='0.52'
                        IsVisible='@IsPlayerFactionOpposedPolicy' />
                    <TextWidget WidthSizePolicy='StretchToParent' HeightSizePolicy='StretchToParent'
                        Brush='Kingdom.PoliciesItem.Text' Text='@Name' />
                  </Children>
                </ButtonWidget>");
            return document;
        }
    }
}
