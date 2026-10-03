using BellumCivile.Behaviors;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.BarterSystem.Barterables;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.Core;
using TaleWorlds.Core.ImageIdentifiers;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using C = BellumCivile.BellumCivileConstants;
using O = BellumCivile.BellumCivileOptions;

namespace BellumCivile
{
    /// <summary>
    /// Why did I do this file?
    /// To serve as a custom Barterable token for bribing a lord's fief vote during the capture
    /// deliberation window. The lord demands gold based on how far the requested candidate is from
    /// their natural nomination. On apply, it locks in a candidate preference stored for that lord
    /// and settlement, which FiefVoteAIPatch then applies when the SettlementClaimantDecision fires.
    /// </summary>
    public class FiefVoteBribeBarterable : Barterable
    {
        private readonly Clan       _voterClan;
        private readonly Kingdom    _kingdom;
        private readonly Settlement _settlement;
        private readonly Clan       _preferredCandidate;
        private readonly float      _resistanceScore;

        public FiefVoteBribeBarterable(
            Clan voterClan, Kingdom kingdom, Settlement settlement,
            Clan preferredCandidate, float resistanceScore, Hero proposer)
            : base(proposer, proposer.PartyBelongedTo?.Party)
        {
            _voterClan       = voterClan;
            _kingdom         = kingdom;
            _settlement      = settlement;
            _preferredCandidate = preferredCandidate;
            _resistanceScore  = resistanceScore;
        }

        public override string     StringID => "fief_vote_bribe_barterable";
public override TextObject Name     => new TextObject("{=BC_Barter_FiefVoteBribe}Influence Fief Vote");

        public override ImageIdentifier GetVisualIdentifier() => null;
        public override void CheckBarterLink(Barterable linkedBarterable) { }

        // What does this complex formula do?
        // Returns the gold value each side must balance in the barter screen.
        // Base demand is tiered by ideological friction (aligned faction = neutral cost,
        // different non-rival faction = mid cost, rival faction = high cost).
        // Three further multipliers adjust the final price:
        //   Relationship  - a lord who likes you asks less; one who distrusts you asks more (+/-30%).
        //   Honor trait   - Honorable/Honest lords cost more; Devious/Deceitful cost less (+/-50/25%).
        //   Generosity    - Tightfisted lords demand more; Generous lords less (+/-20/10%).
        // The combined multiplier is clamped to [0.25, 2.0].
        public override int GetUnitValueForFaction(IFaction faction)
        {
            if (faction == Hero.MainHero.Clan)
                return O.ApplyBribeCostMultiplier(C.FiefBribeCostNeutral);

            if (faction == _voterClan)
            {
                float baseCost = _resistanceScore > 100f ? C.FiefBribeCostHigh
                               : _resistanceScore >  10f ? C.FiefBribeCostMid
                                                         : C.FiefBribeCostNeutral;

                float mult = 1f;

                Hero target = _voterClan.Leader;
                if (target != null)
                {
                    int relation = CharacterRelationManager.GetHeroRelation(Hero.MainHero, target);
                    mult -= relation / 100f * 0.30f;
                    mult += target.GetTraitLevel(DefaultTraits.Honor)      *  0.25f;
                    mult -= target.GetTraitLevel(DefaultTraits.Generosity) *  0.10f;
                }

                mult = MathF.Clamp(mult, 0.25f, 2.0f);
                return -O.ApplyBribeCostMultiplier((int)(baseCost * mult));
            }

            return 0;
        }

        public override void Apply()
        {
            FiefDeliberationBehavior.Current?.SetBribedCandidateVote(_kingdom, _settlement, _voterClan, _preferredCandidate);
        }
    }
}
