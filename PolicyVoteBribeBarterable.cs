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
    /// To serve as a custom Barterable token for bribing a lord's vote during the policy deliberation
    /// window. The lord demands a sum of gold determined by their conviction level, relationship with
    /// the player, and character traits (Honor and Generosity). The player must match the demand through
    /// the standard barter screen. On apply, it locks in a forced support score, scoped to the specific
    /// policy being voted on, that overrides PolicyVoteAIPatch.DetermineSupport for that lord.
    /// </summary>
    public class PolicyVoteBribeBarterable : Barterable
    {
        private readonly Clan         _targetClan;
        private readonly Kingdom      _kingdom;
        private readonly PolicyObject _policy;
        private readonly bool         _swayToSupport;
        private readonly float        _currentScore;   // lord's raw DetermineSupport score at dialogue time

        public PolicyVoteBribeBarterable(Clan targetClan, Kingdom kingdom, PolicyObject policy, bool swayToSupport, float currentScore, Hero proposer)
            : base(proposer, proposer.PartyBelongedTo?.Party)
        {
            _targetClan    = targetClan;
            _kingdom       = kingdom;
            _policy        = policy;
            _swayToSupport = swayToSupport;
            _currentScore  = currentScore;
        }

        public override string StringID => "policy_vote_bribe_barterable";
public override TextObject Name  => new TextObject("{=BC_Barter_PolicyBribe}Influence Vote");

        public override ImageIdentifier GetVisualIdentifier() => null;
        public override void CheckBarterLink(Barterable linkedBarterable) { }

        // What does this complex formula do?
        // Returns the gold value that each side must balance in the barter screen.
        // Base demand is tiered by conviction strength (neutral < committed < deeply opposed/supportive).
        // Three further multipliers adjust the final price:
        //   Relationship  - a lord who likes you asks for less; one who distrusts you asks for more (+/-30%).
        //   Honor trait   - Honorable/Honest lords cost significantly more; Devious/Deceitful cost less (+/-50/25%).
        //   Generosity    - Tightfisted/Closefisted lords demand more gold; Generous/Munificent less (+/-20/10%).
        // The combined multiplier is clamped to [0.25, 2.0] of the base tier cost.
        public override int GetUnitValueForFaction(IFaction faction)
        {
            if (faction == Hero.MainHero.Clan)
                return O.ApplyBribeCostMultiplier(C.PolicyBribeCostNeutral);

            if (faction == _targetClan)
            {
                float abs      = MathF.Abs(_currentScore);
                float baseCost = abs > 100f ? C.PolicyBribeCostHigh
                               : abs >  10f ? C.PolicyBribeCostMid
                                            : C.PolicyBribeCostNeutral;

                float mult = 1f;

                Hero target = _targetClan.Leader;
                if (target != null)
                {
                    int relation = CharacterRelationManager.GetHeroRelation(Hero.MainHero, target);
                    mult -= relation / 100f * 0.30f;

                    mult += target.GetTraitLevel(DefaultTraits.Honor) * 0.25f;

                    mult -= target.GetTraitLevel(DefaultTraits.Generosity) * 0.10f;
                }

                mult = MathF.Clamp(mult, 0.25f, 2.0f);
                return -O.ApplyBribeCostMultiplier((int)(baseCost * mult));
            }

            return 0;
        }

        public override void Apply()
        {
            int forcedScore = _swayToSupport ? C.PolicyBribeForcedSupportScore : -C.PolicyBribeForcedSupportScore;
            PolicyDeliberationBehavior.Current?.SetBribedVote(_kingdom, _policy, _targetClan, forcedScore);
        }
    }
}
