using BellumCivile.Behaviors;
using System;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.BarterSystem.Barterables;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.Core;
using TaleWorlds.Core.ImageIdentifiers;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace BellumCivile
{
    /// <summary>
    /// Why did I do this file?
    /// To serve as a custom Barterable token to facilitate recruiting AI lords into a rebellion, quantifying the immense risk of treason into a massive negative barter value that must be balanced by gold and land.
    /// </summary>
    public class JoinFactionBarterable : Barterable
    {
        private FactionObject _faction;
        private Hero _targetHero;
        private Hero _proposer;

        public JoinFactionBarterable(FactionObject faction, Hero targetHero, Hero proposer) : base(proposer, proposer.PartyBelongedTo?.Party)
        {
            _faction = faction;
            _targetHero = targetHero;
            _proposer = proposer;
        }

        public override string StringID => "join_rebellion_barterable";
        public override TextObject Name => new TextObject("{=BC_Barter_JoinRebellion}Join the Rebellion");

        public override ImageIdentifier GetVisualIdentifier()
        {
            return null;
        }

        public override void CheckBarterLink(Barterable linkedBarterable) { }

        public override int GetUnitValueForFaction(IFaction faction)
        {
            if (faction == _proposer.Clan)
                return 100000; 

            // What does this complex formula do?
            // Calculates the base financial and land cost to convince an AI lord to commit treason, weighing their relationship with the King, tier, personality traits, and returning it as a massive negative value to force the player to overpay in the barter screen.
            if (faction == _targetHero.Clan)
            {
                Hero ruler = _targetHero.Clan.Kingdom?.RulingClan?.Leader;
                float relationWithRuler = ruler != null ? _targetHero.GetRelation(ruler) : 0f;
                float relationWithPlayer = _targetHero.GetRelation(_proposer);

                float cost = 150000f;
                cost += (_targetHero.Clan.Tier * 50000f);
                cost += (relationWithRuler * 2000f);
                cost -= (relationWithPlayer * 1000f);

                int honor = _targetHero.GetTraitLevel(DefaultTraits.Honor);
                int calculating = _targetHero.GetTraitLevel(DefaultTraits.Calculating);

                int proposerHonor = _proposer.GetTraitLevel(DefaultTraits.Honor);
                if (proposerHonor >= 1) cost -= 50000f;
                if (proposerHonor <= -1) cost += 50000f;

                if (honor > 0) cost += 100000f;
                if (calculating > 0) cost += 50000f;
                
                if (_targetHero.Clan.Fiefs.Count == 0) cost += 100000f; 

                return -(int)MathF.Clamp(cost, 50000f, 1500000f);
            }

            return 0;
        }

        public override void Apply()
        {
            _faction.AddMember(_targetHero.Clan);

            var manager = Campaign.Current.GetCampaignBehavior<FactionManagerBehavior>();
            manager?.ApplyPacifiedCooldown(_targetHero.Clan, BellumCivileConstants.RebelFactionBribedJoinCommitmentDays);

            NotificationHelper.ShowClanJoinedFaction(_targetHero.Clan, _faction);
        }
    }
}
