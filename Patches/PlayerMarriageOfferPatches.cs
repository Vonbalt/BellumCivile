using System;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace BellumCivile.Patches
{
    [HarmonyPatch(typeof(MarriageOfferCampaignBehavior), nameof(MarriageOfferCampaignBehavior.CreateMarriageOffer))]
    internal static class PlayerMarriageOfferCapturePatch
    {
        private static bool Prefix(Hero __0, Hero __1) => PlayerMarriageAgreementBehavior.Instance?.Capture(__0, __1) ?? true;
    }

    [HarmonyPatch(typeof(MarriageOfferCampaignBehavior), nameof(MarriageOfferCampaignBehavior.IsHeroEngaged))]
    internal static class PlayerMarriageOwnEngagementPatch
    {
        private static void Postfix(Hero hero, ref bool __result)
        { if (__result && PlayerMarriageAgreementBehavior.IsOwnReservation(hero)) __result = false; }
    }

    [HarmonyPatch(typeof(MarriageOfferCampaignBehavior), nameof(MarriageOfferCampaignBehavior.GetMarriageAcceptedConsequences))]
    internal static class PlayerMarriageOfferConsequencesPatch
    {
        private static void Postfix(ref MBBindingList<TextObject> __result)
        {
            if (BetrothalOfferPopupPatch.IsRefreshing) return;
            var record = PlayerMarriageAgreementBehavior.Instance?.Active;
            if (record == null) return;
            // Keep the native relation reward, replacing its ambiguous transfer line.
            if (__result.Count > 0) __result.RemoveAt(0);
            __result.Insert(0, record.FormText());
            __result.Insert(1, record.HouseholdText());
            __result.Insert(2, record.ChildrenText());
            if (record.Dowry > 0)
            {
                __result.Add(MarriagePayments.DowryText(record.Destination, record.SendingHouse, record.Dowry));
                if (!PlayerMarriageAgreementBehavior.CanFund(record))
                    __result.Add(MarriagePayments.Unaffordable(record.Destination, record.Dowry));
            }
            if (new MarriageHouseholdPolicy().CrownHeirs.Contains(record.Departing)) __result.Add(record.CrownWarning());
        }
    }

    [HarmonyPatch(typeof(MarriageOfferCampaignBehavior), nameof(MarriageOfferCampaignBehavior.OnMarriageOfferAcceptedOnPopUp))]
    internal static class PlayerMarriageOfferAcceptPatch
    {
        [ThreadStatic] private static PlayerMarriageAgreement _confirmed;
        private static bool Prefix(MarriageOfferCampaignBehavior __instance)
        {
            var behavior = PlayerMarriageAgreementBehavior.Instance;
            var record = behavior?.Active;
            if (record == null) return true;
            if (!PlayerMarriageAgreementBehavior.StillLegal(record, out TextObject reason))
            { PlayerMarriageAgreementBehavior.Notify(reason); __instance.OnMarriageOfferDeclinedOnPopUp(); return false; }
            if (!PlayerMarriageAgreementBehavior.CanFund(record))
            { PlayerMarriageAgreementBehavior.Notify(MarriagePayments.Unaffordable(record.Destination, record.Dowry)); return false; }
            if (_confirmed != record)
            {
                PlayerMarriageAgreementBehavior.ConfirmDeparture(record, () =>
                {
                    if (behavior.Active != record) return;
                    var previous = _confirmed;
                    try { _confirmed = record; __instance.OnMarriageOfferAcceptedOnPopUp(); }
                    finally { _confirmed = previous; }
                }, () => { if (behavior.Active == record) __instance.OnMarriageOfferDeclinedOnPopUp(); });
                return false;
            }
            record.Accepted = true;
            return true;
        }
    }

    [HarmonyPatch(typeof(MarriageOfferCampaignBehavior), "HourlyTick")]
    internal static class PlayerMarriageOfferHourlyPatch
    {
        private static void Prefix() => PlayerMarriageAgreementBehavior.Instance?.BeforeHourlyTick();
        private static void Postfix() => PlayerMarriageAgreementBehavior.Instance?.BeforeHourlyTick();
    }

    [HarmonyPatch(typeof(MarriageOfferCampaignBehavior), "MarryHeroesViaOffer")]
    internal static class PlayerMarriageOfferWeddingPatch
    {
        internal sealed class WeddingScope : IDisposable
        {
            private readonly NpcMarriageClanContext _household;
            private readonly PlayerMarriageValidationScope _validation;
            private readonly PlayerMarriageAgreement _record;
            private readonly MarriagePayments.Reservation _payment;
            internal WeddingScope(PlayerMarriageAgreement record, MarriagePayments.Reservation payment)
            {
                _record = record; _payment = payment;
                _household = new NpcMarriageClanContext(record.Player, record.Other, record.Destination);
                _validation = new PlayerMarriageValidationScope(record);
            }
            public void Dispose()
            {
                _validation.Dispose(); _household.Dispose(); _payment?.Dispose();
                if (_payment?.Settled == true) _record.DowrySettled = true;
            }
        }
        private static bool Prefix(Hero playerClanHero, Hero otherClanHero, out WeddingScope __state)
        {
            __state = null;
            var record = PlayerMarriageAgreementBehavior.Instance?.Find(playerClanHero, otherClanHero);
            if (record == null) return true;
            if (!PlayerMarriageAgreementBehavior.Ready(record, out TextObject reason))
            { PlayerMarriageAgreementBehavior.Notify(reason); return false; }
            var payment = record.DowrySettled ? null
                : MarriagePayments.Reserve(record.Player, record.Other, record.Destination, record.SendingHouse, record.Dowry);
            if (!record.DowrySettled && payment == null) return false;
            __state = new WeddingScope(record, payment);
            return true;
        }
        private static void Finalizer(WeddingScope __state) => __state?.Dispose();
    }
}
