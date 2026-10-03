using System;
using System.Collections.Generic;
using System.Linq;
using BellumCivile.Behaviors;
using BellumCivile.ViewModelMixin;
using Helpers;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.ViewModelCollection.Map.HeirSelectionPopup;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace BellumCivile.Patches
{
    [HarmonyPatch]
    public static class PlayerRegencyDeathPreparationPatch
    {
        public static System.Reflection.MethodBase TargetMethod()
        {
            return AccessTools.Method(
                "SandBox.CampaignBehaviors.HeirSelectionCampaignBehavior:OnBeforeMainCharacterDied");
        }

        public static void Prefix(Hero victim)
        {
            try
            {
                RegencyBehavior.Instance?.TryPreparePlayerSuccession(victim);
            }
            catch (Exception ex)
            {
                BellumCivileLogger.Log(
                    $"Player regency preparation failed; victim={victim?.StringId ?? "none"}; error={ex.GetType().Name}:{ex.Message}.");
            }
        }
    }

    [HarmonyPatch(typeof(Clan), nameof(Clan.GetHeirApparents))]
    public static class PlayerRegencyCandidatePatch
    {
        public static void Postfix(Clan __instance, ref Dictionary<Hero, int> __result)
        {
            try
            {
                if (RegencyBehavior.Instance?.TryInjectPlayerSuccessionCandidates(
                        __instance,
                        out Dictionary<Hero, int> candidates) == true)
                {
                    __result = candidates;
                }
            }
            catch (Exception ex)
            {
                BellumCivileLogger.Log(
                    $"Player regency candidate injection failed; clan={__instance?.StringId ?? "none"}; error={ex.GetType().Name}:{ex.Message}.");
            }
        }
    }

    [HarmonyPatch]
    public static class PlayerHeirSelectionLawPatch
    {
        public static System.Reflection.MethodBase TargetMethod()
        {
            return AccessTools.Method("SandBox.CampaignBehaviors.HeirSelectionCampaignBehavior:OnHeirSelectionOver");
        }

        public static void Prefix(ref Hero selectedHeir)
        {
            if (RegencyBehavior.Instance?.TryCommitApprovedPlayerSuccession(selectedHeir) == true)
                return;

            if (RegencyBehavior.Instance?.ConsumeApprovedPlayerSuccessor(selectedHeir) == true)
                return;

            if (!BellumCivileOptions.EnforcePlayerSuccessionLaw)
                return;

            Hero deadHero = Hero.MainHero;
            if (deadHero == null || selectedHeir == null || Clan.PlayerClan == null)
                return;

            IEnumerable<Hero> candidates = Clan.PlayerClan.GetHeirApparents().Keys;
            if (!SuccessionLawHelper.TryResolveLegalPlayerHeir(
                    deadHero,
                    candidates,
                    out Hero legalHeir,
                    out int successionType,
                    out bool usedFallbackCandidatePool))
            {
                return;
            }

            TracePlayerSuccession(
                $"selection finalizing; selected={selectedHeir.StringId}; legal={legalHeir.StringId}; succession_type={successionType}; fallback_pool={usedFallbackCandidatePool}.");

            if (selectedHeir == legalHeir)
                return;

            Hero attemptedHeir = selectedHeir;
            selectedHeir = legalHeir;
            ShowLegalHeirEnforcedMessage(attemptedHeir, legalHeir);
        }

        private static void ShowLegalHeirEnforcedMessage(Hero attemptedHeir, Hero legalHeir)
        {
            TextObject text = new TextObject("{=BC_PlayerSuccession_LegalHeirEnforced}The succession law of your house recognizes {LEGAL_HEIR.NAME} as the lawful heir. {ATTEMPTED_HEIR.NAME} is passed over.");
            if (legalHeir?.CharacterObject != null)
                StringHelpers.SetCharacterProperties("LEGAL_HEIR", legalHeir.CharacterObject, text);
            if (attemptedHeir?.CharacterObject != null)
                StringHelpers.SetCharacterProperties("ATTEMPTED_HEIR", attemptedHeir.CharacterObject, text);

            BellumCivileNotifications.ShowPersonal(text, BellumNotificationColors.InheritanceWarning);
        }

        internal static void TracePlayerSuccession(string message)
        {
            BellumCivileDebug.Trace("player succession", message, requestInGameDisplay: true);
        }
    }

    [HarmonyPatch(typeof(HeirSelectionPopupVM), MethodType.Constructor, typeof(Dictionary<Hero, int>))]
    public static class HeirSelectionPopupDefaultLegalHeirPatch
    {
        public static void Postfix(HeirSelectionPopupVM __instance, Dictionary<Hero, int> heirApparents)
        {
            RegencyBehavior regency = RegencyBehavior.Instance;
            if (regency?.HasPendingPlayerSuccessionSelection == true && __instance != null)
            {
                regency.OnPlayerSuccessionSelectionScreenOpened();
                __instance.TitleText = regency.GetPlayerSuccessionSelectionTitle().ToString();
                __instance.ButtonOkLabel = regency.GetPlayerSuccessionSelectionButtonText().ToString();

                HeirSelectionPopupHeroVM selected = __instance.HeirApparents?
                    .FirstOrDefault(vm => vm?.Hero != null
                        && regency.CanSelectPendingPlayerSuccessor(vm.Hero));
                if (selected != null)
                {
                    foreach (HeirSelectionPopupHeroVM heirVm in __instance.HeirApparents)
                    {
                        if (heirVm != null)
                            heirVm.IsSelected = heirVm == selected;
                    }
                    __instance.CurrentSelectedHero = selected;
                }

                HeirSelectionPopupLegalHeirMixinRegistry.Refresh(__instance);
                return;
            }

            if (!BellumCivileOptions.EnforcePlayerSuccessionLaw
                || __instance == null || heirApparents == null || Hero.MainHero == null)
                return;

            if (!SuccessionLawHelper.TryResolveLegalPlayerHeir(
                    Hero.MainHero,
                    heirApparents.Keys,
                    out Hero legalHeir,
                    out int successionType,
                    out bool usedFallbackCandidatePool))
            {
                return;
            }

            HeirSelectionPopupHeroVM legalVm = __instance.HeirApparents?.FirstOrDefault(vm => vm?.Hero == legalHeir);
            if (legalVm == null)
                return;

            foreach (HeirSelectionPopupHeroVM heirVm in __instance.HeirApparents)
            {
                if (heirVm != null)
                    heirVm.IsSelected = heirVm == legalVm;
            }

            __instance.CurrentSelectedHero = legalVm;
            HeirSelectionPopupLegalHeirMixinRegistry.Refresh(__instance);
            PlayerHeirSelectionLawPatch.TracePlayerSuccession(
                $"popup defaulted to legal heir; legal={legalHeir.StringId}; succession_type={successionType}; fallback_pool={usedFallbackCandidatePool}.");
        }
    }

    [HarmonyPatch(typeof(HeirSelectionPopupVM), "ExecuteSelectHeir")]
    public static class HeirSelectionPopupLegalHeirConfirmationPatch
    {
        private static readonly System.Reflection.MethodInfo FinalizeMethod =
            AccessTools.Method(typeof(HeirSelectionPopupVM), "ExecuteFinalizeHeirSelection", new[] { typeof(Hero) });

        public static bool Prefix(HeirSelectionPopupVM __instance)
        {
            RegencyBehavior regency = RegencyBehavior.Instance;
            if (regency?.HasPendingPlayerSuccessionSelection == true)
            {
                Hero selectedRegent = __instance?.CurrentSelectedHero?.Hero;
                if (__instance == null
                    || selectedRegent == null
                    || FinalizeMethod == null
                    || !regency.CanSelectPendingPlayerSuccessor(selectedRegent))
                {
                    return false;
                }

                Hero successorToFinalize = selectedRegent;
                TextObject regencyDescription = regency.BuildPlayerSuccessionSelectionConfirmation(
                    successorToFinalize);
                InformationManager.ShowInquiry(
                    new InquiryData(
                        new TextObject("{=BC_PlayerRegency_ConfirmTitle}Regency").ToString(),
                        regencyDescription.ToString(),
                        true,
                        true,
                        new TextObject("{=BC_PlayerRegency_Confirm}Confirm").ToString(),
                        new TextObject("{=BC_PlayerSuccession_Cancel}Cancel").ToString(),
                        () =>
                        {
                            if (!regency.TryApprovePreparedPlayerSuccessor(successorToFinalize))
                                return;

                            try
                            {
                                FinalizeMethod.Invoke(__instance, new object[] { successorToFinalize });
                            }
                            catch (Exception ex)
                            {
                                regency.CancelPreparedPlayerSuccessorApproval(successorToFinalize);
                                BellumCivileLogger.Log(
                                    $"Could not finalize player regency selection; successor={successorToFinalize.StringId}; error={ex.GetType().Name}:{ex.Message}.");
                            }
                        },
                        null),
                    true);
                return false;
            }

            if (!BellumCivileOptions.EnforcePlayerSuccessionLaw)
                return true;

            Hero selectedHeir = __instance?.CurrentSelectedHero?.Hero;
            if (__instance == null || selectedHeir == null || FinalizeMethod == null)
                return true;

            if (!SuccessionLawHelper.TryResolveLegalPlayerHeir(
                    Hero.MainHero,
                    Clan.PlayerClan?.GetHeirApparents()?.Keys,
                    out Hero legalHeir,
                    out int successionType,
                    out bool usedFallbackCandidatePool)
                || legalHeir == null
                || successionType == 0)
            {
                return true;
            }

            SuccessionLawHelper.GetSuccessionTypeForClan(Clan.PlayerClan, out SuccessionConfig.SuccessionRuleScope scope);
            Hero heirToFinalize = legalHeir;
            TextObject title = new TextObject("{=BC_PlayerSuccession_ConfirmTitle}Succession");
            TextObject description = SuccessionLawHelper.BuildLegalPlayerHeirConfirmation(legalHeir, successionType, scope);

            PlayerHeirSelectionLawPatch.TracePlayerSuccession(
                $"showing legal heir confirmation; selected={selectedHeir.StringId}; legal={legalHeir.StringId}; succession_type={successionType}; fallback_pool={usedFallbackCandidatePool}.");

            InquiryData inquiry = new InquiryData(
                title.ToString(),
                description.ToString(),
                true,
                true,
                new TextObject("{=BC_PlayerSuccession_Acknowledge}Acknowledge").ToString(),
                new TextObject("{=BC_PlayerSuccession_Cancel}Cancel").ToString(),
                () => FinalizeMethod.Invoke(__instance, new object[] { heirToFinalize }),
                null);

            InformationManager.ShowInquiry(inquiry, true);
            return false;
        }
    }

    [HarmonyPatch(typeof(HeirSelectionPopupVM), "set_CurrentSelectedHero")]
    public static class HeirSelectionPopupLegalHeirSelectionChangedPatch
    {
        public static void Postfix(HeirSelectionPopupVM __instance)
        {
            HeirSelectionPopupLegalHeirMixinRegistry.Refresh(__instance);
        }
    }

    [HarmonyPatch(typeof(HeirSelectionPopupVM), "OnFinalize")]
    public static class HeirSelectionPopupLegalHeirFinalizePatch
    {
        public static void Postfix(HeirSelectionPopupVM __instance)
        {
            HeirSelectionPopupLegalHeirMixinRegistry.Unregister(__instance);
        }
    }
}
