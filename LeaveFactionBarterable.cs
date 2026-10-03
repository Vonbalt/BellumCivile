using BellumCivile.Behaviors;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.BarterSystem.Barterables;
using TaleWorlds.Core;
using TaleWorlds.Core.ImageIdentifiers;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace BellumCivile
{
    /// <summary>
    /// Why did I do this file?
    /// To serve as a custom Barterable token for appeasing an active rebellion, quantifying the cost of pardoning a traitor into a negative barter value that scales dynamically with faction discontent and personal rebellion scores.
    /// </summary>
    public class LeaveFactionBarterable : Barterable
    {
        private readonly FactionObject _faction;
        private readonly Hero _targetVassal;
        private readonly Hero _liege;

        public LeaveFactionBarterable(FactionObject faction, Hero targetVassal, Hero liege)
            : base(liege, liege.PartyBelongedTo?.Party)
        {
            _faction = faction;
            _targetVassal = targetVassal;
            _liege = liege;
        }

        public override string StringID => "leave_faction_barterable";
        public override TextObject Name => new TextObject("{=BC_Barter_LeaveFaction}Abandon Faction & Swear Loyalty");

        public override int GetUnitValueForFaction(IFaction factionToEvaluate)
        {
            if (factionToEvaluate == _targetVassal.Clan)
            {
                // What does this complex formula do?
                // Calculates the cost to bribe a lord into abandoning a rebellion by combining base bribery costs, faction-level discontent multipliers, and the vassal's individual treason logic score.
                float baseCost = 25000f;

                float discontentCost = _faction.Discontent * 500f;

                var ideologyBehavior = Campaign.Current.GetCampaignBehavior<IdeologyBehavior>();
                float personalScore = ideologyBehavior?.CalculateRebellionScore(_targetVassal.Clan) ?? 0f;
                float personalCost = personalScore * 1000f;

                float totalCost = baseCost + discontentCost + personalCost;

                float leaderMultiplier = (_faction.Leader == _targetVassal.Clan) ? 3.0f : 1.0f;
                totalCost *= leaderMultiplier;
                return -(int)totalCost;
            }
            if (factionToEvaluate == _liege.Clan) return 100000;

            return 0;
        }

        public override void CheckBarterLink(Barterable linkedBarterable) { }

        public override ImageIdentifier GetVisualIdentifier()
        {
            return null;
        }

        public override void Apply()
        {
            var manager = Campaign.Current.GetCampaignBehavior<FactionManagerBehavior>();

            if (_faction.Leader == _targetVassal.Clan)
            {
                manager?.RemoveFaction(_faction);
                NotificationHelper.ShowFactionDisbanded(_faction, new TextObject("{=BC_Disband_BoughtLeader}the leader was bought off by the crown").ToString());
            }
            else
            {
                _faction.RemoveMember(_targetVassal.Clan);
                NotificationHelper.ShowClanLeftFaction(_targetVassal.Clan, _faction, new TextObject("{=BC_Left_BoughtMember}they were bought off by the crown").ToString());
            }

            manager?.ApplyPacifiedCooldown(_targetVassal.Clan, 30);
        }
    }
}
