using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using BellumCivile.Behaviors;
using BellumCivile.UI;
using HarmonyLib;
using SandBox.View.Map;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.ViewModelCollection.Map;
using TaleWorlds.CampaignSystem.ViewModelCollection.Map.MarriageOfferPopup;
using TaleWorlds.CampaignSystem.ViewModelCollection.Map.MapNotificationTypes;
using TaleWorlds.Core;
using TaleWorlds.Core.ViewModelCollection.Generic;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.ScreenSystem;

namespace BellumCivile.Patches
{
    internal static class BetrothalOfferPopupPatch
    {
        internal enum ShowResult { Shown, Busy, Unavailable }

        private static readonly ConditionalWeakTable<MarriageOfferPopupVM, BetrothalRecord> Offers =
            new ConditionalWeakTable<MarriageOfferPopupVM, BetrothalRecord>();
        private static readonly ConditionalWeakTable<MarriageOfferPopupHeroVM, BetrothalRecord> Portraits =
            new ConditionalWeakTable<MarriageOfferPopupHeroVM, BetrothalRecord>();
        private static MethodInfo _openNativePopup;
        [ThreadStatic] private static Opening _opening;
        [ThreadStatic] private static BetrothalRecord _refreshing;

        private sealed class Opening
        {
            public BetrothalRecord Offer;
            public bool Bound;
        }

        internal static bool IsAvailable { get; private set; }
        internal static bool IsRefreshing => _refreshing != null;

        internal static void Initialize()
        {
            if (IsAvailable) return;
            var harmony = new Harmony("com.bellumcivile.betrothal_offer_popup");
            try
            {
                var native = typeof(MarriageOfferPopupVM);
                _openNativePopup = AccessTools.Method(typeof(MapScreen), "OnMarriageOfferedToPlayer", new[] { typeof(Hero), typeof(Hero) });
                if (_openNativePopup == null || AccessTools.Field(native, "_onClose")?.FieldType != typeof(Action))
                    throw new InvalidOperationException("The native marriage popup entry point does not match.");
                var ctor = AccessTools.GetDeclaredConstructors(typeof(MapNotificationVM))[0];
                harmony.Patch(ctor, postfix: PatchMethod(nameof(RegisterNotice)));
                harmony.Patch(AccessTools.Method(typeof(MapNotificationItemBaseVM), nameof(MapNotificationItemBaseVM.ExecuteRemove)),
                    prefix: PatchMethod(nameof(DismissNotice)));
                harmony.Patch(AccessTools.Method(native, nameof(MarriageOfferPopupVM.RefreshValues)),
                    prefix: PatchMethod(nameof(BeginRefresh)), postfix: PatchMethod(nameof(FillBetrothalTerms)),
                    finalizer: PatchMethod(nameof(EndRefresh)));
                harmony.Patch(AccessTools.Method(typeof(MarriageOfferCampaignBehavior), nameof(MarriageOfferCampaignBehavior.GetMarriageAcceptedConsequences)),
                    prefix: PatchMethod(nameof(SkipNativeConsequences)));
                harmony.Patch(AccessTools.Method(native, nameof(MarriageOfferPopupVM.ExecuteAcceptOffer)), prefix: PatchMethod(nameof(Accept)));
                harmony.Patch(AccessTools.Method(native, nameof(MarriageOfferPopupVM.ExecuteDeclineOffer)), prefix: PatchMethod(nameof(Decline)));
                IsAvailable = true;
            }
            catch (Exception ex)
            {
                harmony.UnpatchAll(harmony.Id);
                BellumCivileLogger.Log($"Betrothal offer screen unavailable; using the text offer: {ex}");
            }
        }

        private static HarmonyMethod PatchMethod(string name) => new HarmonyMethod(typeof(BetrothalOfferPopupPatch), name);

        private static void RegisterNotice(MapNotificationVM __instance) => __instance.RegisterMapNotificationType(
            typeof(BetrothalOfferNotice), typeof(BetrothalOfferNotificationItemVM));

        private static void DismissNotice(MapNotificationItemBaseVM __instance)
        {
            if (__instance.Data is BetrothalOfferNotice notice && notice.IsValid())
                BetrothalBehavior.Instance?.ResolveIncomingOffer(notice.Pledge, accept: false);
        }

        internal static ShowResult TryShow(BetrothalRecord offer)
        {
            if (_opening != null || !(ScreenManager.TopScreen is MapScreen map) || map.IsMarriageOfferPopupActive
                || map.IsHeirSelectionPopupActive || map.IsInArmyManagement || map.IsMapIncidentActive
                || map.IsOverlayContextMenuEnabled) return ShowResult.Busy;
            if (!IsAvailable) return ShowResult.Unavailable;

            var opening = new Opening { Offer = offer };
            _opening = opening;
            try
            {
                if (NeedsPortrait(offer.First) || NeedsPortrait(offer.Second))
                    UIResourceManager.LoadSpriteCategory("ui_encyclopedia");
                Hero groom = offer.First.IsFemale ? offer.Second : offer.First;
                Hero bride = groom == offer.First ? offer.Second : offer.First;
                // Call only the map view opener. Do not dispatch marriage events or populate the native offer slot.
                _openNativePopup.Invoke(map, new object[] { groom, bride });
                if (opening.Bound && map.IsMarriageOfferPopupActive) return ShowResult.Shown;
                ClosePopup(map);
                return ShowResult.Unavailable;
            }
            catch (Exception ex)
            {
                BellumCivileLogger.Log($"Could not open the betrothal offer screen: {ex}");
                ClosePopup(map);
                return ShowResult.Unavailable;
            }
            finally { _opening = null; }
        }

        private static void ClosePopup(MapScreen map)
        {
            try { map.CloseMarriageOfferPopup(); }
            catch (Exception ex) { BellumCivileLogger.Log($"Could not close the betrothal offer screen: {ex}"); }
        }

        private static void BeginRefresh(MarriageOfferPopupVM __instance, out BetrothalRecord __state)
        {
            __state = _refreshing;
            if (!Offers.TryGetValue(__instance, out var offer) && _opening != null)
            {
                var first = __instance.OffereeClanMember?.Hero;
                var second = __instance.OffererClanMember?.Hero;
                if (first != second && _opening.Offer.Includes(first) && _opening.Offer.Includes(second))
                {
                    offer = _opening.Offer;
                    Offers.Add(__instance, offer);
                    Portraits.Add(__instance.OffereeClanMember, offer);
                    Portraits.Add(__instance.OffererClanMember, offer);
                    _opening.Bound = true;
                }
            }
            _refreshing = offer;
        }

        private static void EndRefresh(BetrothalRecord __state) => _refreshing = __state;

        private static bool SkipNativeConsequences(ref MBBindingList<TextObject> __result)
        {
            if (_refreshing == null) return true;
            __result = new MBBindingList<TextObject>();
            return false;
        }

        private static void FillBetrothalTerms(MarriageOfferPopupVM __instance)
        {
            if (!Offers.TryGetValue(__instance, out var offer)) return;
            __instance.TitleText = BetrothalOfferNotice.OfferTitle(offer).ToString();
            __instance.ButtonOkLabel = new TextObject("{=BC_Betrothal_AcceptOffer}Give my word").ToString();
            __instance.ButtonCancelLabel = new TextObject("{=BC_Betrothal_DeclineOffer}Decline").ToString();
            __instance.ConsequencesList.Clear();
            bool matrilineal = offer.Destination == (offer.First.IsFemale ? offer.FirstHouse : offer.SecondHouse);
            void Add(TextObject text) => __instance.ConsequencesList.Add(new BindingListStringItem("- " + text));
            Add(new TextObject(matrilineal ? "{=BC_Marriage_Matrilineal}Matrilineal marriage" : "{=BC_Marriage_Patrilineal}Patrilineal marriage"));
            Add(new TextObject("{=BC_Betrothal_OfferTransfer}At the wedding, {HERO} will join {HOUSE}.")
                .SetTextVariable("HERO", offer.Departing.Name).SetTextVariable("HOUSE", offer.Destination.Name));
            Add(new TextObject("{=BC_Marriage_FutureChildren}Future children will belong to {HOUSE}.")
                .SetTextVariable("HOUSE", offer.Destination.Name));
            Add(new TextObject("{=BC_Betrothal_OfferExpectedDate}Expected wedding: {DATE}.")
                .SetTextVariable("DATE", offer.ExpectedDate(BetrothalBehavior.Adulthood).ToString()));
            if (offer.Dowry > 0)
            {
                Add(BetrothalBehavior.BetrothalDepositText(offer));
                if (!BetrothalBehavior.CanFundDeposit(offer)) Add(MarriagePayments.Unaffordable(offer.Destination, offer.Deposit));
            }
            Add(new TextObject("{=BC_Betrothal_OfferReservation}Accepting reserves the match. No marriage or claim transfer takes place yet."));
        }

        private static bool Accept(MarriageOfferPopupVM __instance, Action ____onClose) =>
            Resolve(__instance, ____onClose, accept: true);

        private static bool Decline(MarriageOfferPopupVM __instance, Action ____onClose) =>
            Resolve(__instance, ____onClose, accept: false);

        private static bool Resolve(MarriageOfferPopupVM popup, Action close, bool accept)
        {
            if (!Offers.TryGetValue(popup, out var offer)) return true;
            if (accept && !BetrothalBehavior.CanFundDeposit(offer)) return false;
            try { BetrothalBehavior.Instance?.ResolveIncomingOffer(offer, accept); }
            catch (Exception ex) { BellumCivileLogger.Log($"Could not resolve the betrothal offer: {ex}"); }
            finally { close?.Invoke(); }
            return false;
        }

        internal static bool ShowsChildPortrait(MarriageOfferPopupHeroVM vm) => vm != null
            && Portraits.TryGetValue(vm, out _) && NeedsPortrait(vm.Hero);

        internal static bool CanAccept(MarriageOfferPopupVM vm)
        {
            if (Offers.TryGetValue(vm, out var offer)) return BetrothalBehavior.CanFundDeposit(offer);
            var marriage = PlayerMarriageAgreementBehavior.Instance?.Active;
            return marriage == null || PlayerMarriageAgreementBehavior.CanFund(marriage);
        }

        private static bool NeedsPortrait(Hero hero) => hero != null
            && FaceGen.GetMaturityTypeWithAge(hero.Age) <= BodyMeshMaturityType.Child;
    }
}
