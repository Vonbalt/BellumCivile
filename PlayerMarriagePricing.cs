using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using BellumCivile.Behaviors;
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
        [ThreadStatic] internal static bool ReadingNative;

        internal static int Calculate(int nativeValue, int tierGap, float risk, bool npcLeaves,
            bool crownHeir, bool clanHeir, bool continuation)
        {
            double ordinary = Math.Max(0d, -(double)nativeValue) * (1 + .5 * Math.Min(4, Math.Max(0, tierGap)));
            double vulnerability = Math.Max(0, Math.Min(1, risk));
            double price = npcLeaves
                ? ordinary + (crownHeir ? 100000 : clanHeir ? 50000 : 0) + (continuation ? 50000 * vulnerability : 0)
                : ordinary * (continuation ? 1 - .5 * vulnerability : 1);
            return (int)Math.Min(int.MaxValue, Math.Ceiling(price));
        }

        internal static int CalculateHousehold(int nativeValue, float staffRisk, bool npcLeaves)
        {
            double ordinary = Math.Max(0d, -(double)nativeValue);
            double risk = Math.Max(0, Math.Min(1, staffRisk));
            return (int)Math.Min(int.MaxValue, Math.Ceiling(ordinary * (npcLeaves ? 1 + .5 * risk : 1 - .25 * risk)));
        }

        internal static int AdjustStatus(int compensation, bool playerFamily, bool otherFamily)
        {
            double price = Math.Max(0, compensation);
            if (!playerFamily && otherFamily) price += Math.Max(2500, Math.Ceiling(price * .25));
            else if (playerFamily && !otherFamily) price *= .75;
            return (int)Math.Min(int.MaxValue, Math.Ceiling(price));
        }

        internal static bool TryGet(MarriageBarterable item, out PlayerMarriageQuote quote)
        {
            quote = null;
            if (ReadingNative || item == null) return false;
            if (Quotes.TryGetValue(item, out quote)) return true;
            Hero first = item.HeroBeingProposedTo, second = item.ProposingHero;
            if (first?.Clan == null || second?.Clan == null || first.Clan == second.Clan) return false;
            Hero player = first.Clan == Clan.PlayerClan ? first : second.Clan == Clan.PlayerClan ? second : null;
            if (player == null || TreatyMarriageClanContext.TryResolve(first, second, out _)) return false;
            Hero other = player == first ? second : first;
            Clan destination = Campaign.Current.Models.MarriageModel.GetClanAfterMarriage(first, second);
            return Bind(item, PlayerMarriageAgreement.Create(player, other, destination, true), out quote);
        }

        internal static bool Bind(MarriageBarterable item, PlayerMarriageAgreement agreement, out PlayerMarriageQuote quote)
        {
            if (Quotes.TryGetValue(item, out quote)) return true;
            // Capture once for barter redraws; legality is checked again before any payment.
            var policy = new MarriageHouseholdPolicy();
            bool npcLeaves = agreement.Destination == agreement.PlayerHouse;
            Hero bride = agreement.Player.IsFemale ? agreement.Player : agreement.Other;
            bool family = HouseholdMembership.IsFamily(agreement.Other);
            bool playerFamily = HouseholdMembership.IsFamily(agreement.Player);
            bool dynasticPair = family && playerFamily;
            bool continuation = npcLeaves ? policy.IsContinuation(agreement.Other)
                : HouseholdMembership.IsBloodRelative(agreement.Other, MarriageHouseholdPolicy.LegalHead(agreement.OtherHouse))
                    && bride.Age >= SuccessionLawHelper.GetAgeOfMajority() && bride.Age <= 45;
            int native;
            bool previous = ReadingNative;
            try
            {
                ReadingNative = true;
                using (new NpcMarriageClanContext(agreement.Player, agreement.Other, agreement.Destination))
                    native = new MarriageBarterable(item.OriginalOwner, null, agreement.Player, agreement.Other)
                        .GetUnitValueForFaction(agreement.OtherHouse);
            }
            finally { ReadingNative = previous; }
            int compensation = family ? Calculate(native,
                dynasticPair ? agreement.OtherHouse.Tier - agreement.PlayerHouse.Tier : 0,
                BellumMarriageStrategyHelper.PlayerMarriageHouseRisk(agreement.OtherHouse), npcLeaves,
                policy.CrownHeirs.Contains(agreement.Other), policy.MustRemain(agreement.Other), continuation)
                : CalculateHousehold(native, BellumMarriageStrategyHelper.PlayerMarriageStaffRisk(agreement.Other, agreement.Player, agreement.Destination), npcLeaves);
            quote = new PlayerMarriageQuote { Agreement = agreement, Compensation = AdjustStatus(compensation, playerFamily, family) };
            Quotes.Add(item, quote);
            return true;
        }

        internal static void BindNegotiated(MarriageBarterable item, PlayerMarriageAgreement agreement, int amount,
            MarriageDowryBarterable payment, IReadOnlyList<MarriageValuableBarterable> valuables)
        {
            Quotes.Remove(item);
            Quotes.Add(item, new PlayerMarriageQuote { Agreement = agreement, Compensation = amount, Payment = payment, Valuables = valuables });
        }

        internal static int NegotiatedQuote(PlayerMarriageAgreement terms)
        {
            var policy = new MarriageHouseholdPolicy();
            return MarriagePayments.Quote(terms.Departing, policy.CrownHeirs.Contains(terms.Departing), policy.MustRemain(terms.Departing));
        }

        internal static void BindAgreement(MarriageBarterable item, PlayerMarriageAgreement agreement)
        {
            if (BellumCivileOptions.EnableBellumStrategicMarriageLogic)
                Quotes.Add(item, new PlayerMarriageQuote { Agreement = agreement });
            else Bind(item, agreement, out _);
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
