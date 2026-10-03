using BellumCivile.Behaviors;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.BarterSystem.Barterables;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.Core.ImageIdentifiers;
using TaleWorlds.Localization;

namespace BellumCivile
{
    public sealed class MandateVoteBribeBarterable : Barterable
    {
        private readonly string _id;
        private readonly Hero _speaker, _player;
        private readonly bool _reform;
        private readonly int _price;
        private bool _secured;
        internal MandateVoteBribeBarterable(string id, Hero speaker, bool reform) : base(Hero.MainHero, Hero.MainHero.PartyBelongedTo?.Party)
        {
            _id = id; _speaker = speaker; _player = Hero.MainHero; _reform = reform;
            _price = BellumCivileOptions.ApplyBribeCostMultiplier(CourtMandateRules.Price(
                CourtAgendaBehavior.Current.MandateResistance(id, speaker, reform), _player.GetRelation(speaker),
                speaker.GetTraitLevel(DefaultTraits.Honor), speaker.GetTraitLevel(DefaultTraits.Generosity)));
        }
        public override string StringID => "bc_mandate_vote_bribe";
        public override TextObject Name => new TextObject("{=BC_MandateBarterItem}Pledge support in the mandate reform vote");
        public override ImageIdentifier GetVisualIdentifier() => null;
        public override void CheckBarterLink(Barterable other) { }
        public override int GetUnitValueForFaction(IFaction faction) => faction == _speaker.Clan ? -_price : faction == _player.Clan ? _price : 0;
        internal bool TrySecure(Hero player, Hero other)
        {
            var court = CourtAgendaBehavior.Current;
            if (_secured || player != _player || player != Hero.MainHero || other != _speaker || Hero.OneToOneConversationHero != _speaker
                || court?.CanLobbyMandate(_id, _speaker) != true || court.MandateBribeOpenness(_id, _speaker, _reform) < 35) return false;
            return _secured = court.CommitMandate(_id, _speaker, _reform, true);
        }
        // The finalization prefix secures the promise before native payment items apply.
        public override void Apply() { }
    }
}
