using System.Collections.Generic;
using System.Linq;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.BarterSystem;
using TaleWorlds.CampaignSystem.BarterSystem.Barterables;
using TaleWorlds.CampaignSystem.ViewModelCollection.Barter;
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

            var policy = new MarriageHouseholdPolicy();
            int quote = MarriagePayments.Quote(terms.Departing, policy.CrownHeirs.Contains(terms.Departing), policy.MustRemain(terms.Departing));
            if (!MarriagePayments.CanTransfer(terms.Destination, terms.SendingHouse, quote))
            { PlayerMarriageAgreementBehavior.Notify(MarriagePayments.Unaffordable(terms.Destination, quote)); return false; }

            var gold = new MarriageDowryBarterable(MarriagePayments.Wallet(terms.Destination),
                MarriagePayments.Wallet(terms.SendingHouse), terms.Destination, terms.SendingHouse, quote);
            var barter = new BarterData(__0.OffererHero, __0.OtherHero, __0.OffererParty, __0.OtherParty,
                (item, _, __) => item == wedding || item == gold);
            barter.AddBarterable<OtherBarterGroup>(wedding, isContextDependent: true);
            barter.AddBarterable<GoldBarterGroup>(gold, isContextDependent: true);
            if (barter.GetBarterables().Count != 2) return false;
            wedding.SetIsOffered(true);
            gold.SetIsOffered(true);
            PlayerMarriagePricing.BindNegotiated(wedding, terms, quote, gold);
            __0 = barter;
            return true;
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
                if (quote.Payment != null && (!offered.Contains(quote.Payment) || offered.Count != 2 || !quote.TryConfirmPayment(out _)))
                {
                    PlayerMarriageAgreementBehavior.Notify(MarriagePayments.Unaffordable(quote.Agreement.Destination, quote.Compensation));
                    return false;
                }
                if (PlayerMarriageAgreementBehavior.Ready(quote.Agreement, out TextObject reason)) continue;
                PlayerMarriageAgreementBehavior.Notify(reason);
                return false;
            }
            return marriages > 0 || !offered.OfType<MarriageDowryBarterable>().Any(item => !item.IsDeposit);
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
            internal WeddingScope(PlayerMarriageAgreement terms, MarriagePayments.Reservation payment)
            { Household = new NpcMarriageClanContext(terms.Player, terms.Other, terms.Destination); Payment = payment; }
            public void Dispose() { Household.Dispose(); Payment?.Dispose(); }
        }

        private static bool Prefix(MarriageBarterable __instance, out WeddingScope __state)
        {
            __state = null;
            if (!PlayerMarriagePricing.TryGet(__instance, out var quote)) return true;
            if (!PlayerMarriageAgreementBehavior.Ready(quote.Agreement, out TextObject reason))
            { PlayerMarriageAgreementBehavior.Notify(reason); return false; }
            if (!quote.TryConfirmPayment(out int amount)) return false;
            var terms = quote.Agreement;
            var reservation = quote.Payment == null ? null
                : MarriagePayments.Reserve(terms.Player, terms.Other, terms.Destination, terms.SendingHouse, amount);
            if (quote.Payment != null && reservation == null) return false;
            __state = new WeddingScope(terms, reservation);
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
