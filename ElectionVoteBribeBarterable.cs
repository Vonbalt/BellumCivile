using BellumCivile.Behaviors;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.BarterSystem.Barterables;
using TaleWorlds.Core.ImageIdentifiers;
using TaleWorlds.Localization;

namespace BellumCivile
{
    public sealed class ElectionVoteBribeBarterable : Barterable
    {
        private readonly Kingdom _realm;
        private readonly Hero _speaker, _candidate, _proposer;
        private readonly CampaignTime _until;
        private readonly int _price;
        private readonly double _gap;
        private readonly ElectiveSuccessionRecord _ballot;
        private readonly int _mandateNumber;
        private bool _secured;

        internal ElectionVoteBribeBarterable(Kingdom realm, Hero speaker, Hero candidate, Hero proposer,
            CampaignTime until, int price, double gap) : base(proposer, proposer?.PartyBelongedTo?.Party)
        {
            _realm = realm; _speaker = speaker; _candidate = candidate; _proposer = proposer;
            _until = until; _price = price; _gap = gap;
            _ballot = ElectiveSuccessionBehavior.Instance?.Get(realm);
            _mandateNumber = _ballot?.MandateNumber ?? -1;
        }
        public override string StringID => "bc_election_vote_bribe";
        public override TextObject Name => new TextObject("{=BC_EL_BarterItem}Pledge electoral support until the court reconsiders");
        public override ImageIdentifier GetVisualIdentifier() => null;
        public override void CheckBarterLink(Barterable linkedBarterable) { }
        public override int GetUnitValueForFaction(IFaction faction) => faction == _speaker.Clan ? -_price
            : faction == _proposer.Clan ? _price : 0;

        internal bool TrySecure(Hero offerer, Hero other)
        {
            if (_secured || offerer != _proposer || other != _speaker || _proposer != Hero.MainHero
                || Hero.OneToOneConversationHero != _speaker || _proposer.Clan.Kingdom != _realm
                || _proposer.Clan.IsUnderMercenaryService || _speaker.IsPrisoner
                || ElectionLobbyingBehavior.BribeOpenness(_speaker, _gap) < BellumCivileConstants.FiefBribeOpennessThreshold
                || ElectiveSuccessionBehavior.Instance?.CanCommitPromise(_realm, _speaker, _candidate, out var until) != true
                || until != _until || _ballot == null
                || ElectiveSuccessionBehavior.Instance.Get(_realm) != _ballot
                || _ballot.MandateNumber != _mandateNumber) return false;
            _secured = ElectiveSuccessionBehavior.Instance.TryCommitPromise(_realm, _speaker, _candidate, true);
            if (_secured && ElectionLobbyingBehavior.Current != null) ElectionLobbyingBehavior.Current.BribeCommitted = true;
            return _secured;
        }

        // The finalization prefix secures the pledge before native code applies any payment items.
        public override void Apply() { }
    }
}
