using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.BarterSystem.Barterables;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;

namespace BellumCivile
{
    internal sealed class MarriageValuableBarterable : ItemBarterable
    {
        internal PartyBase ReceivingParty { get; }
        private readonly Clan _receivingHouse;
        private int? _credit;

        internal MarriageValuableBarterable(Hero payer, Hero payee, PartyBase payerParty, PartyBase payeeParty,
            ItemRosterElement element, int value) : base(payer, payee, payerParty, payeeParty, element, value)
        {
            ReceivingParty = payeeParty;
            _receivingHouse = payee.Clan;
        }

        internal int Credit => _credit ?? (_credit = System.Math.Max(0, base.GetUnitValueForFaction(_receivingHouse))).Value;
        public override int GetUnitValueForFaction(IFaction faction) => faction == _receivingHouse ? Credit : 0;
        public override void Apply() { } // The wedding settles these goods, not the individual native barter row.

        internal bool HasStock()
        {
            var roster = OriginalParty.ItemRoster;
            int index = roster.FindIndexOfElement(ItemRosterElement.EquipmentElement);
            return CurrentAmount > 0 && CurrentAmount <= MaxAmount
                && index >= 0 && roster.GetElementNumber(index) >= CurrentAmount;
        }
    }
}
