using System;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Localization;

namespace BellumCivile
{
    internal static class MarriagePayments
    {
        internal static int Quote(Hero departing, Hero receiving, MarriageHouseholdPolicy policy)
        {
            Clan sending = departing.Clan;
            int tier = ClampTier(sending.Tier);
            if (!HouseholdMembership.IsFamily(departing))
                return 4000 + tier * 500;

            int ordinary = 20000 + tier * 5000;
            if (HouseholdMembership.IsFamily(receiving))
                ordinary = ApplyPrestigePremium(ordinary, tier, receiving.Clan.Tier,
                    policy.DowryPeerage(sending), policy.DowryPeerage(receiving.Clan));

            return ordinary + (policy.IsCrownHeirForDowry(departing) ? 45000 : policy.MustRemain(departing) ? 20000 : 0);
        }

        private static int ClampTier(int tier) => Math.Min(6, Math.Max(0, tier));

        internal static int ApplyPrestigePremium(int ordinary, int sendingTier, int receivingTier,
            FeudalTitleType? sendingRank, FeudalTitleType? receivingRank)
        {
            int renown = 25 * Math.Max(0, ClampTier(sendingTier) - ClampTier(receivingTier));
            int peerage = 35 * Math.Max(0, (sendingRank.HasValue ? (int)sendingRank.Value : -1)
                - (receivingRank.HasValue ? (int)receivingRank.Value : -1));
            // Count the larger gap in full and the smaller at half weight; cap the base at 3x.
            int halfPercent = Math.Min(400, 2 * Math.Max(renown, peerage) + Math.Min(renown, peerage));
            return (ordinary * (200 + halfPercent) + 199) / 200;
        }

        internal static int Deposit(int dowry) => dowry <= 0 ? 0 : (int)((dowry + 3L) / 4);
        internal static int RenunciationPremium(int deposit) => (int)((deposit + 1L) / 2);
        internal static Hero Wallet(Clan clan) => clan?.Leader?.IsAlive == true ? clan.Leader : null;
        internal static bool CanPay(Clan clan, int amount) => amount == 0 || (Wallet(clan)?.Gold ?? -1) >= amount;
        internal static bool CanTransfer(Clan payer, Clan payee, int amount) => amount == 0
            || payer != payee && CanPay(payer, amount) && HasRoom(Wallet(payee), amount);
        internal static bool HasRoom(Hero hero, int amount) => hero != null && hero.Gold <= int.MaxValue - amount;

        internal static void Move(Hero from, Hero to, int amount)
        {
            if (amount <= 0) return;
            // Escrow debits and credits are not standalone native trades.
            from?.ChangeHeroGold(-amount);
            to?.ChangeHeroGold(amount);
        }

        internal static bool IsWedding(Hero first, Hero second, Clan destination) =>
            first.Spouse == second && second.Spouse == first && first.Clan == destination && second.Clan == destination;

        internal static bool CanFundWedding(Clan payer, Clan payee, int balance, int held = 0) =>
            (balance == 0 && held == 0) || payer != payee && CanPay(payer, balance) && HasRoom(Wallet(payee), balance + held);

        internal static Reservation Reserve(Hero first, Hero second, Clan destination, Clan sendingHouse, int balance, int held = 0)
        {
            if (!CanFundWedding(destination, sendingHouse, balance, held)) return null;
            return new Reservation(first, second, destination, sendingHouse, balance, held);
        }

        internal static void TraceQuote(string stage, Hero first, Hero second, Clan payer, Clan payee, int dowry, int dueNow)
        {
            if (!BellumCivileDebug.ShowInGameMessages || dowry <= 0) return;
            int? available = Wallet(payer)?.Gold;
            BellumCivileDebug.Trace("marriage",
                $"{stage}; first={first?.StringId}; second={second?.StringId}; payer={payer?.StringId}; payee={payee?.StringId}; "
                + $"dowry={dowry}; due_now={dueNow}; payer_gold={available?.ToString() ?? "unavailable"}.", requestInGameDisplay: true);
        }

        internal sealed class Reservation : IDisposable
        {
            private readonly Hero _first, _second, _payer, _payee;
            private readonly Clan _destination;
            private readonly Clan _sendingHouse;
            private readonly int _balance, _held;
            private bool _closed;
            internal bool Settled { get; private set; }

            internal Reservation(Hero first, Hero second, Clan destination, Clan sendingHouse, int balance, int held)
            {
                _first = first; _second = second; _destination = destination;
                _sendingHouse = sendingHouse;
                _payer = Wallet(destination); _payee = Wallet(sendingHouse); _balance = balance; _held = held;
                Move(_payer, null, _balance);
            }

            public void Dispose()
            {
                if (_closed) return;
                _closed = true;
                Settled = IsWedding(_first, _second, _destination);
                bool trace = _balance + _held > 0 && BellumCivileDebug.ShowInGameMessages;
                var recipient = Settled ? _payee : _payer;
                int before = trace ? (recipient?.Gold ?? 0) : 0;
                Move(null, recipient, Settled ? _balance + _held : _balance);
                if (!trace) return;
                int credited = (recipient?.Gold ?? 0) - before;
                BellumCivileDebug.Trace("marriage",
                    $"wedding payment {(Settled ? "settled" : "rolled back")}; first={_first.StringId}; second={_second.StringId}; "
                    + $"payer={_destination?.StringId}; payee={_sendingHouse?.StringId}; agreed={_balance + _held}; "
                    + $"transferred={(Settled ? credited : 0)}; refunded={(Settled ? 0 : credited)}; escrow={_held}; wedding_balance={_balance}.",
                    requestInGameDisplay: true);
            }
        }

        internal static TextObject Unaffordable(Clan payer, int amount) =>
            new TextObject("{=BC_Marriage_DowryUnavailable}{HOUSE} cannot fund the payment of {GOLD} denars.")
                .SetTextVariable("HOUSE", payer?.Name ?? new TextObject(string.Empty)).SetTextVariable("GOLD", amount);

        internal static TextObject DowryText(Clan payer, Clan payee, int amount) =>
            new TextObject("{=BC_Marriage_DowryTerms}{PAYER} will pay {PAYEE} {GOLD} denars at the wedding.")
                .SetTextVariable("PAYER", payer.Name).SetTextVariable("PAYEE", payee.Name).SetTextVariable("GOLD", amount);
    }
}
