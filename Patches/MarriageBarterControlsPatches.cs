using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using TaleWorlds.CampaignSystem.BarterSystem;
using TaleWorlds.CampaignSystem.BarterSystem.Barterables;
using TaleWorlds.CampaignSystem.ViewModelCollection.Barter;

namespace BellumCivile.Patches
{
    [HarmonyPatch(typeof(BarterVM), nameof(BarterVM.ExecuteReset))]
    internal static class MarriageBarterResetPatch
    {
        private static void Prefix(BarterData ____barterData)
        {
            // Native rebuilds the offer from its fixed rows but preserves their edited amounts.
            foreach (var dowry in ____barterData.GetBarterables().OfType<MarriageDowryBarterable>())
                dowry.CurrentAmount = dowry.OpeningAmount;
        }
    }

    [HarmonyPatch(typeof(BarterVM), nameof(BarterVM.ExecuteAutoBalance))]
    internal static class MarriageBarterAutoOfferPatch
    {
        private static readonly Action<BarterVM> RefreshOffer =
            AccessTools.MethodDelegate<Action<BarterVM>>(AccessTools.Method(typeof(BarterVM), "SendOffer"));

        private static bool Prefix(BarterVM __instance, BarterData ____barterData)
        {
            foreach (var item in ____barterData.GetBarterables())
            {
                if (item is MarriageDowryBarterable deposit && deposit.IsDeposit)
                {
                    var row = FindPaymentRow(__instance, deposit);
                    if (row != null) row.CurrentOfferedAmount = deposit.OpeningAmount;
                    return false;
                }
                if (item is MarriageBarterable wedding && PlayerMarriagePricing.TryGet(wedding, out var quote)
                    && quote.HasNegotiatedPayment)
                {
                    AutoOffer(__instance, quote);
                    return false;
                }
            }
            return true;
        }

        private static BarterItemVM FindPaymentRow(BarterVM vm, MarriageDowryBarterable payment) =>
            vm.LeftOfferList.Concat(vm.RightOfferList).FirstOrDefault(row => row.Barterable == payment);

        private static void AutoOffer(BarterVM vm, PlayerMarriageQuote quote)
        {
            if (quote.TryConfirmPayment(out _, out _)) return;
            var payer = MarriagePayments.Wallet(quote.Agreement.Destination);
            var payee = MarriagePayments.Wallet(quote.Agreement.SendingHouse);
            if (payer == null || payee == null) return;

            var payment = quote.Payment;
            var goldRow = payment == null ? null : FindPaymentRow(vm, payment);
            if (payment != null && (goldRow == null || payment.OriginalOwner != payer)) return;

            long cashRoom = (long)int.MaxValue - payee.Gold;
            int cashLimit = payment == null ? 0 : Math.Min(payment.MaxAmount, payer.Gold);
            if (quote.Agreement.Destination != quote.Agreement.PlayerHouse)
            {
                int bid = (int)Math.Min(cashRoom, Math.Min(cashLimit, quote.Compensation));
                if (bid > 0) goldRow.CurrentOfferedAmount = bid;
                return;
            }

            long due = quote.Compensation;
            foreach (var good in quote.Valuables.Where(item => item.IsOffered))
            {
                if (!good.HasStock()) return;
                long value = (long)good.Credit * good.CurrentAmount;
                due -= value;
                if (good.ReceivingParty == null) cashRoom -= value;
            }
            int cash = (int)Math.Max(0, Math.Min(Math.Max(0, due), Math.Min(cashLimit, cashRoom)));
            if (cashRoom < 0 || payment != null && cash == 0) return;
            due -= cash;
            cashRoom -= cash;

            var additions = new List<(BarterItemVM Row, int Amount)>();
            foreach (var row in vm.RightItemList)
            {
                if (due <= 0) break;
                if (!(row.Barterable is MarriageValuableBarterable good) || !quote.Valuables.Contains(good)
                    || good.Credit <= 0 || !good.IsOffered && !row.IsItemTransferrable) continue;
                int offered = good.IsOffered ? good.CurrentAmount : 0;
                long available = good.AvailableUnits() - offered;
                if (good.ReceivingParty == null) available = Math.Min(available, cashRoom / good.Credit);
                int count = (int)Math.Min(available, (due + good.Credit - 1) / good.Credit);
                if (count <= 0) continue;
                additions.Add((row, offered + count));
                long value = (long)count * good.Credit;
                due -= value;
                if (good.ReceivingParty == null) cashRoom -= value;
            }
            if (due > 0) return;

            // Update the native rows only after finding a complete offer. No payment is made here.
            if (goldRow != null) goldRow.CurrentOfferedAmount = cash;
            foreach (var (row, amount) in additions)
            {
                if (!row.Barterable.IsOffered)
                {
                    row.Barterable.SetIsOffered(true);
                    vm.OnTransferItem(row.Barterable, isTransferrable: true);
                }
                row.CurrentOfferedAmount = amount;
            }
            RefreshOffer(vm);
        }
    }
}
