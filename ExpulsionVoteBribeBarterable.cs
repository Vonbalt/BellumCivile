using BellumCivile.Behaviors;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.BarterSystem.Barterables;
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
    /// To serve as a custom Barterable token for bribing a lord's vote during the expulsion
    /// deliberation window. The lord demands gold based on the strength of their conviction,
    /// relationship with the player, and character traits. On apply, it locks in a forced support
    /// score, scoped to the specific expulsion target, that overrides ExpulsionVoteAIPatch for
    /// that lord when the vote fires.
    /// </summary>
    public class ExpulsionVoteBribeBarterable : Barterable
    {
        private readonly Clan    _voterClan;
        private readonly Kingdom _kingdom;
        private readonly Clan    _targetClan;
        private readonly bool    _swayToExpel;
        private readonly float   _currentScore;

        public ExpulsionVoteBribeBarterable(
            Clan voterClan, Kingdom kingdom, Clan targetClan,
            bool swayToExpel, float currentScore, Hero proposer)
            : base(proposer, proposer.PartyBelongedTo?.Party)
        {
            _voterClan    = voterClan;
            _kingdom      = kingdom;
            _targetClan   = targetClan;
            _swayToExpel  = swayToExpel;
            _currentScore = currentScore;
        }

        public override string     StringID => "expulsion_vote_bribe_barterable";
public override TextObject Name     => new TextObject("{=BC_Barter_ExpulsionBribe}Influence Vote");

        public override ImageIdentifier GetVisualIdentifier() => null;
        public override void CheckBarterLink(Barterable linkedBarterable) { }

        // What does this complex formula do?
        // Returns the gold value each side must balance in the barter screen.
        // Base demand is tiered by conviction strength (|score| <= 10 = neutral, 10-100 = committed,
        // > 100 = deeply invested). Three further multipliers adjust the final price:
        //   Relationship  - a lord who likes you asks less; one who distrusts you asks more (+/-30%).
        //   Honor trait   - Honorable/Honest lords cost more; Devious/Deceitful cost less (+/-50/25%).
        //   Generosity    - Tightfisted lords demand more; Generous lords less (+/-20/10%).
        // The combined multiplier is clamped to [0.25, 2.0].
        public override int GetUnitValueForFaction(IFaction faction)
        {
            if (faction == Hero.MainHero.Clan)
                return O.ApplyBribeCostMultiplier(C.ExpulsionBribeCostNeutral);

            if (faction == _voterClan)
            {
                float abs      = MathF.Abs(_currentScore);
                float baseCost = abs > 100f ? C.ExpulsionBribeCostHigh
                               : abs >  10f ? C.ExpulsionBribeCostMid
                                            : C.ExpulsionBribeCostNeutral;

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
            int forcedScore = _swayToExpel ? C.ExpulsionBribeForcedSupportScore : -C.ExpulsionBribeForcedSupportScore;
            ExpulsionDeliberationBehavior.Current?.SetBribedVote(_kingdom, _targetClan, _voterClan, forcedScore);
        }
    }
}
