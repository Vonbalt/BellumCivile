using BellumCivile.Behaviors;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.BarterSystem.Barterables;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.Core.ImageIdentifiers;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using C = BellumCivile.BellumCivileConstants;
using O = BellumCivile.BellumCivileOptions;

namespace BellumCivile
{
    public sealed class CouncilAppointmentVoteBribeBarterable : Barterable, IInfluenceVotePledgeBarterable
    {
        private readonly Clan _voter;
        private readonly Kingdom _kingdom;
        private readonly PrivyCouncilOffice _office;
        private readonly Clan _candidate;
        private readonly float _resistance;
        private bool _secured;

        public CouncilAppointmentVoteBribeBarterable(
            Clan voter,
            Kingdom kingdom,
            PrivyCouncilOffice office,
            Clan candidate,
            float resistance,
            Hero proposer)
            : base(proposer, proposer?.PartyBelongedTo?.Party)
        {
            _voter = voter;
            _kingdom = kingdom;
            _office = office;
            _candidate = candidate;
            _resistance = resistance;
        }

        public override string StringID => "council_appointment_vote_bribe";
        public override TextObject Name => new TextObject("{=BC_CouncilDelib_BribeBarter}Influence Council Appointment");

        public override ImageIdentifier GetVisualIdentifier() => null;
        public override void CheckBarterLink(Barterable linkedBarterable) { }

        public override int GetUnitValueForFaction(IFaction faction)
        {
            if (faction == Hero.MainHero?.Clan)
                return O.ApplyBribeCostMultiplier(C.FiefBribeCostNeutral);

            if (faction != _voter)
                return 0;

            float baseCost = _resistance > 100f ? C.FiefBribeCostHigh
                : _resistance > 10f ? C.FiefBribeCostMid
                : C.FiefBribeCostNeutral;
            Hero target = _voter?.Leader;
            float multiplier = 1f;
            if (target != null)
            {
                multiplier -= target.GetRelation(Hero.MainHero) / 100f * 0.30f;
                multiplier += target.GetTraitLevel(DefaultTraits.Honor) * 0.25f;
                multiplier -= target.GetTraitLevel(DefaultTraits.Generosity) * 0.10f;
            }

            return -O.ApplyBribeCostMultiplier((int)(baseCost * MathF.Clamp(multiplier, 0.25f, 2f)));
        }

        bool IInfluenceVotePledgeBarterable.TrySecure(Hero offerer, Hero other)
        {
            var deliberation = CouncilAppointmentDeliberationBehavior.Current;
            if (_secured || !VotePledgeService.IsBarterParticipant(offerer, other, _voter, _kingdom)
                || deliberation?.HasPendingAppointment(_kingdom, _office) != true
                || CouncilAppointmentNominationHelper.ScoreCandidate(_voter, _candidate, _kingdom, _office,
                    Campaign.Current?.GetCampaignBehavior<PrivyCouncilBehavior>()) == null) return false;
            return _secured = VotePledgeService.TryCommit(_voter, _kingdom,
                VotePledgeService.CouncilKey(_kingdom, _office, _voter),
                () => deliberation.SetCommittedCandidateVote(_kingdom, _office, _voter, _candidate, "bribed"));
        }

        // The finalization prefix secures the promise before any payment items apply.
        public override void Apply() { }
    }
}
