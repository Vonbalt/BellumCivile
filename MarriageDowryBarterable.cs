using System;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.BarterSystem.Barterables;
using TaleWorlds.CampaignSystem.Party;

namespace BellumCivile
{
    // Native barter renders and edits this amount; the wedding transaction settles it.
    internal sealed class MarriageDowryBarterable : GoldBarterable
    {
        private readonly Clan _payer, _payee;
        internal int Minimum { get; }
        internal bool IsDeposit { get; }

        internal MarriageDowryBarterable(Hero payer, Hero payee, Clan payerClan, Clan payeeClan, int quote, bool isDeposit = false)
            : base(payer, payee, payer.PartyBelongedTo?.Party, payee.PartyBelongedTo?.Party,
                payerClan == Clan.PlayerClan && !isDeposit ? payer.Gold : Math.Min(quote, payer.Gold))
        {
            _payer = payerClan; _payee = payeeClan;
            IsDeposit = isDeposit;
            Minimum = payerClan == Clan.PlayerClan || isDeposit ? quote : 1;
            CurrentAmount = Math.Min(quote, MaxAmount);
        }

        public override int GetUnitValueForFaction(IFaction faction) => faction == _payer ? -1 : faction == _payee ? 1 : 0;
        public override void Apply() { }
    }
}
