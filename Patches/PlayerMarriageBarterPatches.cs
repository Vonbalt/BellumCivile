using System.Collections.Generic;
using System.Linq;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.BarterSystem;
using TaleWorlds.CampaignSystem.BarterSystem.Barterables;
using TaleWorlds.CampaignSystem.ViewModelCollection.Barter;
using TaleWorlds.Core;
using TaleWorlds.Localization;

namespace BellumCivile.Patches
{
    [HarmonyPatch(typeof(BarterManager), nameof(BarterManager.BeginPlayerBarter))]
    internal static class PlayerMarriageDowryBarterPatch
    {
        private static bool Prefix(ref BarterData __0)
        {
            if (!BellumCivileOptions.EnableBellumStrategicMarriageLogic || __0.OffererHero != Hero.MainHero) return true;
            var wedding = __0.GetBarterables().OfType<MarriageBarterable>().FirstOrDefault(item => item.IsContextDependent && item.IsOffered);
            if (wedding == null) return true;
            var terms = PlayerMarriagePricing.AgreementFor(wedding);
            if (terms == null || __0.OtherHero?.Clan != terms.OtherHouse) return true;
            if (!PlayerMarriageAgreementBehavior.Ready(terms, out TextObject reason))
            { PlayerMarriageAgreementBehavior.Notify(reason); return false; }

            int quote = PlayerMarriagePricing.NegotiatedQuote(terms);
            var payer = MarriagePayments.Wallet(terms.Destination);
            var payee = MarriagePayments.Wallet(terms.SendingHouse);
            bool playerPays = terms.Destination == terms.PlayerHouse;
            if (payer == null || payee == null || !playerPays && payer.Gold <= 0)
            { PlayerMarriageAgreementBehavior.Notify(MarriagePayments.Unaffordable(terms.Destination, quote)); return false; }

            var gold = payer.Gold > 0 ? new MarriageDowryBarterable(payer, payee, terms.Destination, terms.SendingHouse, quote) : null;
            var barter = new BarterData(__0.OffererHero, __0.OtherHero, __0.OffererParty, __0.OtherParty,
                (item, _, __) => item == wedding || item == gold);
            barter.AddBarterable<OtherBarterGroup>(wedding, isContextDependent: true);
            if (gold != null) barter.AddBarterable<GoldBarterGroup>(gold, isContextDependent: true);
            if (barter.GetBarterables().Count != (gold == null ? 1 : 2)) return false;
            var valuables = playerPays ? AddValuables(__0, barter, payer, payee) : new List<MarriageValuableBarterable>();
            wedding.SetIsOffered(true);
            gold?.SetIsOffered(true);
            PlayerMarriagePricing.BindNegotiated(wedding, terms, quote, gold, valuables);
            MarriagePayments.TraceQuote("marriage barter quoted", terms.Player, terms.Other, terms.Destination,
                terms.SendingHouse, quote, quote);
            __0 = barter;
            return true;
        }

        private static List<MarriageValuableBarterable> AddValuables(BarterData original, BarterData barter, Hero payer, Hero payee)
        {
            var added = new List<MarriageValuableBarterable>();
            var roster = original.OffererParty?.ItemRoster;
            if (roster == null || barter.GetBarterGroup<ItemBarterGroup>() == null) return added;
            var nativeValues = new Dictionary<EquipmentElement, int>();
            foreach (var item in original.GetBarterables().OfType<ItemBarterable>())
                if (item.OriginalParty == original.OffererParty && item.OriginalOwner == original.OffererHero && item.ItemValue > 0)
                    nativeValues[item.ItemRosterElement.EquipmentElement] = item.ItemValue;
            for (int i = 0; i < roster.Count; i++)
            {
                var element = roster.GetElementCopyAtIndex(i);
                if (element.Amount <= 0 || element.EquipmentElement.Item == null) continue;
                int value = nativeValues.TryGetValue(element.EquipmentElement, out int native)
                    ? native : element.EquipmentElement.GetBaseValue();
                if (value <= 100) continue;
                var receivingParty = original.OtherParty == original.OffererParty ? null : original.OtherParty;
                var good = new MarriageValuableBarterable(payer, payee, original.OffererParty, receivingParty, element, value);
                barter.AddBarterable<ItemBarterGroup>(good);
                added.Add(good);
            }
            return added;
        }
    }

    [HarmonyPatch(typeof(BarterItemVM), MethodType.Constructor, new[] {
        typeof(Barterable), typeof(BarterItemVM.BarterTransferEventDelegate), typeof(System.Action), typeof(bool) })]
    internal static class MarriageDowryBarterItemPatch
    {
        // Native initializes all offered stacks to one, even when the barter opens with an agreed sum.
        private static void Prefix(Barterable __0, out int __state) =>
            __state = __0 is MarriageDowryBarterable ? __0.CurrentAmount : 0;

        private static void Postfix(BarterItemVM __instance, int __state)
        {
            if (__state > 0) __instance.CurrentOfferedAmount = __state;
        }
    }

    internal static class PlayerMarriageBarterGuard
    {
        internal static bool Validate(IEnumerable<Barterable> items)
        {
            int marriages = 0;
            var offered = items as IList<Barterable> ?? items?.ToList();
            if (offered == null) return true;
            if (!BetrothalBehavior.ValidatePledgeBarter(offered)) return false;
            foreach (var item in offered.OfType<MarriageBarterable>())
            {
                if (!PlayerMarriagePricing.TryGet(item, out var quote)) continue;
                if (++marriages > 1)
                {
                    PlayerMarriageAgreementBehavior.Notify(new TextObject("{=BC_Marriage_OneAgreement}Only one marriage agreement can be concluded at a time."));
                    return false;
                }
                if (quote.HasNegotiatedPayment
                    && (!quote.TryConfirmPayment(out int gold, out var goods)
                        || gold > 0 && !offered.Contains(quote.Payment)
                        || goods.Any(good => !offered.Contains(good))
                        || offered.Count != 1 + (gold > 0 ? 1 : 0) + goods.Length))
                {
                    PlayerMarriageAgreementBehavior.Notify(MarriagePayments.Unaffordable(quote.Agreement.Destination, quote.Compensation));
                    return false;
                }
                if (PlayerMarriageAgreementBehavior.Ready(quote.Agreement, out TextObject reason)) continue;
                PlayerMarriageAgreementBehavior.Notify(reason);
                return false;
            }
            return marriages > 0 || !offered.Any(item => item is MarriageValuableBarterable
                || item is MarriageDowryBarterable gold && !gold.IsDeposit);
        }
    }

    [HarmonyPatch(typeof(BarterManager), nameof(BarterManager.ApplyAndFinalizePlayerBarter))]
    internal static class PlayerMarriageBarterCommitPatch
    {
        private static bool Prefix(BarterManager __instance, Hero offererHero, Hero otherHero, BarterData barterData)
        {
            if (PlayerMarriageBarterGuard.Validate(barterData.GetOfferedBarterables())) return true;
            __instance.CancelAndFinalizePlayerBarter(offererHero, otherHero, barterData);
            return false;
        }
    }

    [HarmonyPatch(typeof(BarterManager), "ApplyBarterOffer")]
    internal static class PlayerMarriageBarterAtomicPatch
    {
        private static bool Prefix(List<Barterable> barters) => PlayerMarriageBarterGuard.Validate(barters);
    }

    [HarmonyPatch(typeof(MarriageBarterable), nameof(MarriageBarterable.Apply))]
    internal static class PlayerMarriageBarterWeddingPatch
    {
        internal sealed class WeddingScope : System.IDisposable
        {
            internal readonly NpcMarriageClanContext Household;
            internal readonly MarriagePayments.Reservation Payment;
            private readonly PlayerMarriageValidationScope _validation;
            private readonly (MarriageValuableBarterable Item, int Amount)[] _goods;
            private readonly Hero _payee;
            internal WeddingScope(PlayerMarriageAgreement terms, MarriagePayments.Reservation payment, MarriageValuableBarterable[] goods)
            {
                Household = new NpcMarriageClanContext(terms.Player, terms.Other, terms.Destination);
                _validation = new PlayerMarriageValidationScope(terms);
                Payment = payment;
                _payee = MarriagePayments.Wallet(terms.SendingHouse);
                _goods = goods.Select(item => (item, item.CurrentAmount)).ToArray();
                foreach (var good in _goods)
                    good.Item.OriginalParty.ItemRoster.AddToCounts(good.Item.ItemRosterElement.EquipmentElement, -good.Amount);
            }

            public void Dispose()
            {
                _validation.Dispose();
                Household.Dispose();
                Payment?.Dispose();
                foreach (var good in _goods)
                {
                    var item = good.Item;
                    if (Payment?.Settled != true)
                        item.OriginalParty.ItemRoster.AddToCounts(item.ItemRosterElement.EquipmentElement, good.Amount);
                    else if (item.ReceivingParty != null)
                        item.ReceivingParty.ItemRoster.AddToCounts(item.ItemRosterElement.EquipmentElement, good.Amount);
                    else
                        MarriagePayments.Move(null, _payee, item.Credit * good.Amount);
                }
                if (_goods.Length != 0 && BellumCivileDebug.ShowInGameMessages)
                    BellumCivileDebug.Trace("marriage",
                        $"wedding valuables {(Payment?.Settled == true ? "settled" : "returned")}; "
                        + $"payee={_payee?.StringId}; stacks={_goods.Length}; barter_credit={_goods.Sum(g => (long)g.Item.Credit * g.Amount)}.",
                        requestInGameDisplay: true);
            }
        }

        private static bool Prefix(MarriageBarterable __instance, out WeddingScope __state)
        {
            __state = null;
            if (!PlayerMarriagePricing.TryGet(__instance, out var quote)) return true;
            if (!PlayerMarriageAgreementBehavior.Ready(quote.Agreement, out TextObject reason))
            { PlayerMarriageAgreementBehavior.Notify(reason); return false; }
            if (!quote.TryConfirmPayment(out int amount, out var goods)) return false;
            var terms = quote.Agreement;
            var reservation = !quote.HasNegotiatedPayment ? null
                : MarriagePayments.Reserve(terms.Player, terms.Other, terms.Destination, terms.SendingHouse, amount);
            if (quote.HasNegotiatedPayment && reservation == null) return false;
            __state = new WeddingScope(terms, reservation, goods);
            return true;
        }
        private static void Finalizer(WeddingScope __state) => __state?.Dispose();
    }

    [HarmonyPatch(typeof(MarriageBarterable), nameof(MarriageBarterable.Name), MethodType.Getter)]
    internal static class PlayerMarriageBarterNamePatch
    {
        private static void Postfix(MarriageBarterable __instance, ref TextObject __result)
        {
            if (!PlayerMarriagePricing.TryGet(__instance, out var quote)) return;
            __result = new TextObject("{=BC_Marriage_BarterHousehold}{FORM}: {HERO} joins {HOUSE}")
                .SetTextVariable("FORM", quote.Agreement.FormText()).SetTextVariable("HERO", quote.Agreement.Departing.Name)
                .SetTextVariable("HOUSE", quote.Agreement.Destination.Name);
        }
    }
}
