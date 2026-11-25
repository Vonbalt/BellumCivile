using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.BarterSystem.Barterables;

namespace BellumCivile
{
    internal sealed class PlayerMarriageQuote
    {
        internal PlayerMarriageAgreement Agreement;
        internal int Compensation;
        internal MarriageDowryBarterable Payment;
        internal IReadOnlyList<MarriageValuableBarterable> Valuables;
        internal bool HasNegotiatedPayment => Valuables != null;

        internal bool TryConfirmPayment(out int amount, out MarriageValuableBarterable[] goods)
        {
            amount = Payment?.IsOffered == true ? Payment.CurrentAmount : 0;
            goods = Array.Empty<MarriageValuableBarterable>();
            if (!HasNegotiatedPayment) return true;
            var payer = MarriagePayments.Wallet(Agreement.Destination);
            var payee = MarriagePayments.Wallet(Agreement.SendingHouse);
            if (payer == null || payee == null || amount > payer.Gold || amount < 0
                || amount > 0 && (Payment.OriginalOwner != payer || amount > Payment.MaxAmount)) return false;
            if (Agreement.Destination != Agreement.PlayerHouse)
                return amount > 0 && amount <= Compensation && MarriagePayments.HasRoom(payee, amount);

            goods = Valuables.Where(item => item.IsOffered).ToArray();
            long credit = amount, goldCredit = amount;
            foreach (var item in goods)
            {
                if (!item.HasStock()) return false;
                long value = (long)item.Credit * item.CurrentAmount;
                credit += value;
                if (item.ReceivingParty == null) goldCredit += value;
            }
            return credit >= Compensation && goldCredit <= int.MaxValue && MarriagePayments.HasRoom(payee, (int)goldCredit);
        }
    }

    internal static class PlayerMarriagePricing
    {
        private static readonly ConditionalWeakTable<MarriageBarterable, PlayerMarriageQuote> Quotes
            = new ConditionalWeakTable<MarriageBarterable, PlayerMarriageQuote>();

        internal static bool TryGet(MarriageBarterable item, out PlayerMarriageQuote quote)
        {
            quote = null;
            return item != null && Quotes.TryGetValue(item, out quote);
        }

        internal static void BindNegotiated(MarriageBarterable item, PlayerMarriageAgreement agreement, int amount,
            MarriageDowryBarterable payment, IReadOnlyList<MarriageValuableBarterable> valuables)
        {
            Quotes.Remove(item);
            Quotes.Add(item, new PlayerMarriageQuote { Agreement = agreement, Compensation = amount, Payment = payment, Valuables = valuables });
        }

        internal static int NegotiatedQuote(PlayerMarriageAgreement terms)
        {
            Hero receiving = terms.Destination == terms.PlayerHouse ? terms.Player : terms.Other;
            return MarriagePayments.Quote(terms.Departing, receiving, new MarriageHouseholdPolicy());
        }

        internal static void BindAgreement(MarriageBarterable item, PlayerMarriageAgreement agreement)
        {
            if (BellumCivileOptions.EnableBellumStrategicMarriageLogic)
                Quotes.Add(item, new PlayerMarriageQuote { Agreement = agreement });
        }

        internal static PlayerMarriageAgreement AgreementFor(MarriageBarterable item)
        {
            if (Quotes.TryGetValue(item, out var quote)) return quote.Agreement;
            Hero first = item.HeroBeingProposedTo, second = item.ProposingHero;
            if (first?.Clan == null || second?.Clan == null || first.Clan == second.Clan
                || TreatyMarriageClanContext.TryResolve(first, second, out _)) return null;
            Hero player = first.Clan == Clan.PlayerClan ? first : second.Clan == Clan.PlayerClan ? second : null;
            if (player == null) return null;
            Clan destination = Campaign.Current.Models.MarriageModel.GetClanAfterMarriage(first, second);
            return PlayerMarriageAgreement.Create(player, player == first ? second : first, destination, true);
        }
    }
}
