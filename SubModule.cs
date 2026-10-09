﻿﻿using HarmonyLib;
using Bannerlord.UIExtenderEx;
using System;
using System.IO;
using System.Reflection;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.ComponentInterfaces;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.GameState;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;
using TaleWorlds.Localization;
using TaleWorlds.Library;
using BellumCivile.Behaviors;

namespace BellumCivile
{
    /// <summary>
    /// Why did I do this file?
    /// To serve as the entry point for the mod, applying safe Harmony patches, registering campaign models and behaviors, and handling global input events for toggling the UI.
    /// </summary>
    public class SubModule : MBSubModuleBase
    {
        private UIExtender _uiExtender;
        private Harmony _harmony;
        private bool _diplomacyKingdomFactionsButtonHijackPatched;
        private bool _diplomacyKingdomListFilterPatched;
        private bool _encyclopediaHeroRelationThresholdPatched;
        private bool _hostageEncyclopediaHistoryPatched;
        private bool _encyclopediaClanSuccessionPatched;
        private bool _navalDlcPatchRegistered;
        private static bool _navalDlcAssemblyResolverRegistered;
        public static bool ShouldShowStandaloneFactionsTab => !ModIntegrationHelper.IsDiplomacyLoaded;
        internal static bool IsDiplomacyFactionsButtonIntegrationReady { get; private set; }

        protected override void OnSubModuleLoad()
        {
            base.OnSubModuleLoad();

            _harmony = new Harmony("com.bellumcivile.patch");
            try
            {
                // Install preference migration before any of our UI/patches can request settings.
                _harmony.CreateClassProcessor(typeof(Patches.RealmNameSettingsMigrationPatch)).Patch();
            }
            catch (Exception ex)
            {
                BellumCivileLogger.Log($"Failed to apply realm-name settings migration: {ex.Message}");
            }

            _uiExtender = UIExtender.Create("BellumCivile");
            _uiExtender.Register(Assembly.GetExecutingAssembly());
            TryRegisterBundledNavalDlcPatch();
            _uiExtender.Enable();

            foreach (Type type in Assembly.GetExecutingAssembly().GetTypes())
            {
                try
                {
                    if (type == typeof(Patches.RealmNameSettingsMigrationPatch) || ShouldSkipOptionalPatch(type))
                        continue;

                    if (type.GetCustomAttributes(typeof(HarmonyPatch), true).Length > 0)
                    {
                        _harmony.CreateClassProcessor(type).Patch();
                    }
                }
                catch (Exception ex)
                {
                    BellumCivileLogger.Log($"Failed to apply Harmony patch '{type.Name}': {ex.Message}");
                }
            }

        }

        protected override void OnBeforeInitialModuleScreenSetAsRoot()
        {
            base.OnBeforeInitialModuleScreenSetAsRoot();
            InitializeDiplomacyIntegration(finalAttempt: false);
            Patches.ArtemRealmNameCompatibility.TryApply(_harmony);
            Patches.PocBannerCompatibility.TryApply(_harmony);
        }

        private void TryRegisterBundledNavalDlcPatch()
        {
            if (_navalDlcPatchRegistered || _uiExtender == null)
                return;

            if (!TryFindNavalDlcViewModelAssemblyPath(out _))
                return;

            try
            {
                string ownDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                if (string.IsNullOrWhiteSpace(ownDirectory))
                    return;

                string patchPath = Path.Combine(ownDirectory, "BellumCivile.NavalDLCPatch.dll");
                if (!File.Exists(patchPath))
                {
                    BellumCivileLogger.Log("War Sails detected, but bundled BellumCivile.NavalDLCPatch.dll was not found beside BellumCivile.dll.");
                    return;
                }

                EnsureNavalDlcAssemblyResolverRegistered();
                Assembly patchAssembly = Assembly.LoadFrom(patchPath);
                Type bootstrapType = patchAssembly.GetType("BellumCivile.NavalDLCPatch.NavalDlcPatchBootstrap", throwOnError: false);
                MethodInfo enableMethod = bootstrapType?.GetMethod("EnableStandalone", BindingFlags.Public | BindingFlags.Static);
                bool registered = enableMethod != null
                    && enableMethod.Invoke(null, null) is bool value
                    && value;

                _navalDlcPatchRegistered = registered;
                if (registered)
                    BellumCivileLogger.Log("Registered bundled Bellum Civile War Sails UI patch.");
            }
            catch (Exception ex)
            {
                BellumCivileLogger.Log($"Failed to register bundled Bellum Civile War Sails UI patch: {ex.GetType().Name}:{ex.Message}");
            }
        }

        private static void EnsureNavalDlcAssemblyResolverRegistered()
        {
            if (_navalDlcAssemblyResolverRegistered)
                return;

            AppDomain.CurrentDomain.AssemblyResolve += ResolveNavalDlcAssembly;
            _navalDlcAssemblyResolverRegistered = true;
        }

        private static Assembly ResolveNavalDlcAssembly(object sender, ResolveEventArgs args)
        {
            string assemblyName;
            try
            {
                assemblyName = new AssemblyName(args.Name).Name;
            }
            catch
            {
                return null;
            }

            if (!string.Equals(assemblyName, "NavalDLC.ViewModelCollection", StringComparison.OrdinalIgnoreCase))
                return null;

            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (string.Equals(assembly.GetName().Name, assemblyName, StringComparison.OrdinalIgnoreCase))
                    return assembly;
            }

            return TryFindNavalDlcViewModelAssemblyPath(out string path)
                ? Assembly.LoadFrom(path)
                : null;
        }

        private static bool TryFindNavalDlcViewModelAssemblyPath(out string path)
        {
            path = null;

            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                string assemblyName = assembly.GetName().Name ?? string.Empty;
                if (assemblyName.Equals("NavalDLC.ViewModelCollection", StringComparison.OrdinalIgnoreCase)
                    || assembly.GetType("NavalDLC.ViewModelCollection.Kingdom.NavalKingdomManagementVM", throwOnError: false) != null)
                {
                    path = assembly.Location;
                    return true;
                }
            }

            string ownDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            foreach (string modulesDirectory in GetPossibleModulesDirectories(ownDirectory))
            {
                string candidate = Path.Combine(modulesDirectory, "NavalDLC", "bin", "Win64_Shipping_Client", "NavalDLC.ViewModelCollection.dll");
                if (File.Exists(candidate))
                {
                    path = candidate;
                    return true;
                }
            }

            return false;
        }

        private static System.Collections.Generic.IEnumerable<string> GetPossibleModulesDirectories(string startDirectory)
        {
            if (string.IsNullOrWhiteSpace(startDirectory))
                yield break;

            DirectoryInfo current = new DirectoryInfo(startDirectory);
            while (current != null)
            {
                if (current.Name.Equals("Modules", StringComparison.OrdinalIgnoreCase))
                {
                    yield return current.FullName;
                    yield break;
                }

                if (current.Parent != null && current.Parent.Name.Equals("Modules", StringComparison.OrdinalIgnoreCase))
                {
                    yield return current.Parent.FullName;
                    yield break;
                }

                current = current.Parent;
            }
        }

        private static bool ShouldSkipOptionalPatch(Type type)
        {
            if (type == typeof(Patches.DiplomacyKingdomFactionsButtonHijackPatch))
                return true;

            if (type == typeof(Patches.EncyclopediaHeroRelationThresholdPatch))
                return true;

            if (type == typeof(Patches.HostageEncyclopediaHistoryPatch))
                return true;

            if (type == typeof(Patches.EncyclopediaClanSuccessionPatch))
                return true;

            if (type == typeof(Patches.DiplomacyKingdomListFilterPatch))
                return true;

            return false;
        }

        protected override void OnGameStart(Game game, IGameStarter gameStarterObject)
        {
            base.OnGameStart(game, gameStarterObject);

            if (game.GameType is Campaign)
            {
                DynamicKingdomTitleNameHelper.BeginCampaign();

                if (!_diplomacyKingdomFactionsButtonHijackPatched || !_diplomacyKingdomListFilterPatched)
                    InitializeDiplomacyIntegration(finalAttempt: true);
                Patches.ArtemRealmNameCompatibility.TryApply(_harmony);
                Patches.PocBannerCompatibility.TryApply(_harmony);

                CampaignGameStarter campaignStarter = (CampaignGameStarter)gameStarterObject;

                DiplomacyModel baseDiplomacyModel = campaignStarter.GetModel<DiplomacyModel>() ?? new DefaultDiplomacyModel();
                campaignStarter.AddModel(new CivilWarDiplomacyModel(baseDiplomacyModel));

                SettlementLoyaltyModel baseSettlementLoyaltyModel = campaignStarter.GetModel<SettlementLoyaltyModel>() ?? new DefaultSettlementLoyaltyModel();
                campaignStarter.AddModel(new BellumFeudalSettlementLoyaltyModel(baseSettlementLoyaltyModel));

                SettlementPatrolModel baseSettlementPatrolModel = campaignStarter.GetModel<SettlementPatrolModel>() ?? new DefaultSettlementPatrolModel();
                campaignStarter.AddModel(new BellumCouncilSettlementPatrolModel(baseSettlementPatrolModel));

                SettlementMilitiaModel baseSettlementMilitiaModel = campaignStarter.GetModel<SettlementMilitiaModel>() ?? new DefaultSettlementMilitiaModel();
                campaignStarter.AddModel(new BellumCouncilSettlementMilitiaModel(baseSettlementMilitiaModel));

                BuildingConstructionModel baseBuildingConstructionModel = campaignStarter.GetModel<BuildingConstructionModel>() ?? new DefaultBuildingConstructionModel();
                campaignStarter.AddModel(new BellumCouncilBuildingConstructionModel(baseBuildingConstructionModel));

                SettlementFoodModel baseSettlementFoodModel = campaignStarter.GetModel<SettlementFoodModel>() ?? new DefaultSettlementFoodModel();
                campaignStarter.AddModel(new BellumCouncilSettlementFoodModel(baseSettlementFoodModel));

                ClanFinanceModel baseClanFinanceModel = campaignStarter.GetModel<ClanFinanceModel>() ?? new DefaultClanFinanceModel();
                campaignStarter.AddModel(new BellumFeudalClanFinanceModel(baseClanFinanceModel));

                ClanPoliticsModel baseClanPoliticsModel = campaignStarter.GetModel<ClanPoliticsModel>() ?? new DefaultClanPoliticsModel();
                campaignStarter.AddModel(new BellumClanPoliticsModel(baseClanPoliticsModel));

                campaignStarter.AddBehavior(new FactionManagerBehavior());
                campaignStarter.AddBehavior(new InfluenceBudgetTelemetryBehavior());
                campaignStarter.AddBehavior(new CivilWarInterventionBehavior());
                campaignStarter.AddBehavior(new CivilWarResolutionBehavior());
                campaignStarter.AddBehavior(new InternalPeaceSettlementBehavior());
                campaignStarter.AddBehavior(new ConflictOutcomeBehavior());
                campaignStarter.AddBehavior(new CivilWarConflictBehavior());
                campaignStarter.AddBehavior(new RebellionSummaryBehavior());
                campaignStarter.AddBehavior(new ConflictCallResponseBehavior());
                campaignStarter.AddBehavior(new ExiledClanRecoveryBehavior());
                campaignStarter.AddBehavior(new AppeasementBehavior());
                campaignStarter.AddBehavior(new PolicyDeliberationBehavior());
                campaignStarter.AddBehavior(new ExpulsionDeliberationBehavior());
                campaignStarter.AddBehavior(new FiefDeliberationBehavior());
                campaignStarter.AddBehavior(new CouncilAppointmentDeliberationBehavior());
                campaignStarter.AddBehavior(new PrivyCouncilBehavior());
                campaignStarter.AddBehavior(new CouncilIncidentBehavior());
                campaignStarter.AddBehavior(new ControversyBehavior());
                campaignStarter.AddBehavior(new MarriageAllianceBehavior());
                campaignStarter.AddBehavior(new DynamicRelationBehavior());
                campaignStarter.AddBehavior(new MercenaryRelationMemoryBehavior());
                campaignStarter.AddBehavior(new CourtPoliticalPositionBehavior());
                campaignStarter.AddBehavior(new IdeologyBehavior());
                campaignStarter.AddBehavior(new CourtAgendaBehavior());
                campaignStarter.AddBehavior(new IdeologyEventShockBehavior());
                campaignStarter.AddBehavior(new ComradesInArmsBehavior());
                campaignStarter.AddBehavior(new ArmySummonsConversationBehavior());
                campaignStarter.AddBehavior(new ProxyWarBehavior());
                campaignStarter.AddBehavior(new FeudalTitleBehavior());
                campaignStarter.AddBehavior(new FeudalServiceBehavior());
                campaignStarter.AddBehavior(new FeudalDeJureDriftBehavior());
                campaignStarter.AddBehavior(new FeudalTitleUsurpationBehavior());
                campaignStarter.AddBehavior(new FeudalClaimFabricationBehavior());
                campaignStarter.AddBehavior(new CrownAuthorityBehavior());
                campaignStarter.AddBehavior(new RealmPeaceEnforcementBehavior());
                campaignStarter.AddBehavior(new ClaimFeudBehavior());
                campaignStarter.AddBehavior(new FeudalPoliticalOptionsBehavior());
                campaignStarter.AddBehavior(new ClaimFeudWarBehavior());
                campaignStarter.AddBehavior(new RetainedTreatyPrisonerBehavior());
                campaignStarter.AddBehavior(new HostagePactBehavior());
                campaignStarter.AddBehavior(new HouseholdRansomBehavior());
                campaignStarter.AddBehavior(new CompanionSubinfeudationBehavior());
                campaignStarter.AddBehavior(new ClientKingdomBehavior());
                campaignStarter.AddBehavior(new ForeignTreatyBehavior());
                campaignStarter.AddBehavior(new WarScoreBehavior());
                campaignStarter.AddBehavior(new WarScoreMapWidgetBehavior());
                campaignStarter.AddBehavior(new WarPeaceRevampBehavior());
                campaignStarter.AddBehavior(new WarPeaceYearlySummaryBehavior());
                campaignStarter.AddBehavior(new ForeignPolicyBehavior());
                campaignStarter.AddBehavior(new DiplomacyCompatibilityBehavior());
                campaignStarter.AddBehavior(new RegencyBehavior());
                campaignStarter.AddBehavior(new CrownAccessionBehavior());
                campaignStarter.AddBehavior(new ElectiveSuccessionBehavior());
                campaignStarter.AddBehavior(new ElectionLobbyingBehavior());
                campaignStarter.AddBehavior(new ElectiveContestBehavior());
                campaignStarter.AddBehavior(new HereditaryLoyaltyBehavior());
                campaignStarter.AddBehavior(new SuccessionChallengeBehavior());
                campaignStarter.AddBehavior(new RealmLawBehavior());
                campaignStarter.AddBehavior(new SuccessionLawBehavior());
                campaignStarter.AddBehavior(new DynasticClaimBehavior());
                campaignStarter.AddBehavior(new DynasticHeirBehavior());
                campaignStarter.AddBehavior(new PartitionSuccessionBehavior());
                campaignStarter.AddBehavior(new CadetHouseholdBehavior());
                campaignStarter.AddBehavior(new DynamicMercenaryBandBehavior());
                campaignStarter.AddBehavior(new SuccessionYearlySummaryBehavior());
                campaignStarter.AddBehavior(new StrategicMarriageBehavior());
                campaignStarter.AddBehavior(new PlayerMarriageAgreementBehavior());
                campaignStarter.AddBehavior(new SpecificMarriageProposalBehavior());
                campaignStarter.AddModel(new DynamicArmyManagementModel());
                if (BellumCivileOptions.EnableCustomAdulthoodAge)
                    campaignStarter.AddModel(new BellumAgeModel());
                campaignStarter.AddModel(new DynamicSuccessionModel());
                campaignStarter.AddModel(new BellumMarriageModel());

                CouncilAssignmentHapIntegration.Initialize();
                CouncilAssignmentRuntimePatches.Initialize(
                    campaignStarter.GetModel<VillageProductionCalculatorModel>(),
                    campaignStarter.GetModel<MobilePartyFoodConsumptionModel>(),
                    campaignStarter.GetModel<PartySizeLimitModel>());

                TryPatchEncyclopediaHeroRelationThreshold();
                TryPatchHostageEncyclopediaHistory();
                TryPatchEncyclopediaClanSuccession();
            }
        }

        private void TryPatchEncyclopediaHeroRelationThreshold()
        {
            if (_encyclopediaHeroRelationThresholdPatched || _harmony == null)
                return;

            try
            {
                _harmony.CreateClassProcessor(typeof(Patches.EncyclopediaHeroRelationThresholdPatch)).Patch();
                _encyclopediaHeroRelationThresholdPatched = true;
            }
            catch (Exception ex)
            {
                BellumCivileLogger.Log($"Failed to apply encyclopedia relation threshold patch: {ex.GetType().Name}:{ex.Message}");
            }
        }

        private void TryPatchHostageEncyclopediaHistory()
        {
            if (_hostageEncyclopediaHistoryPatched || _harmony == null)
                return;

            try
            {
                // Patching Refresh can initialize CampaignUIHelper. Game texts
                // must be ready, as with the existing encyclopedia patches.
                _harmony.CreateClassProcessor(typeof(Patches.HostageEncyclopediaHistoryPatch)).Patch();
                _hostageEncyclopediaHistoryPatched = true;
            }
            catch (Exception ex)
            {
                BellumCivileLogger.Log($"Failed to apply hostage encyclopedia history patch: {ex}");
            }
        }

        private void TryPatchEncyclopediaClanSuccession()
        {
            if (_encyclopediaClanSuccessionPatched || _harmony == null)
                return;

            try
            {
                _harmony.CreateClassProcessor(typeof(Patches.EncyclopediaClanSuccessionPatch)).Patch();
                _encyclopediaClanSuccessionPatched = true;
            }
            catch (Exception ex)
            {
                BellumCivileLogger.Log($"Failed to apply encyclopedia clan succession patch: {ex.GetType().Name}:{ex.Message}");
            }
        }

        private void InitializeDiplomacyIntegration(bool finalAttempt)
        {
            if (_harmony == null)
                return;

            if (!ModIntegrationHelper.RefreshDiplomacyDetection())
                return;

            TryPatchDiplomacyKingdomListFilter(finalAttempt);
            TryPatchDiplomacyKingdomFactionsButton(finalAttempt);
        }

        private void TryPatchDiplomacyKingdomListFilter(bool finalAttempt)
        {
            if (_diplomacyKingdomListFilterPatched || _harmony == null)
                return;

            try
            {
                _harmony.CreateClassProcessor(typeof(Patches.DiplomacyKingdomListFilterPatch)).Patch();
                _diplomacyKingdomListFilterPatched = true;
            }
            catch (Exception ex)
            {
                if (finalAttempt)
                {
                    BellumCivileLogger.Log(
                        $"Diplomacy was detected, but its kingdom-list compatibility patch could not be applied: {ex.GetType().Name}:{ex.Message}");
                }
            }
        }

        private void TryPatchDiplomacyKingdomFactionsButton(bool finalAttempt)
        {
            if (_diplomacyKingdomFactionsButtonHijackPatched || _harmony == null)
                return;

            MethodInfo prefix = typeof(Patches.DiplomacyKingdomFactionsButtonHijackPatch).GetMethod(
                nameof(Patches.DiplomacyKingdomFactionsButtonHijackPatch.Prefix),
                BindingFlags.Public | BindingFlags.Static);

            if (prefix == null)
                return;

            bool patchedAny = false;
            bool patchFailed = false;
            try
            {
                foreach (MethodBase target in Patches.DiplomacyKingdomFactionsButtonHijackPatch.GetTargetMethods())
                {
                    if (target == null)
                        continue;

                    _harmony.Patch(target, prefix: new HarmonyMethod(prefix));
                    patchedAny = true;
                }
            }
            catch (Exception ex)
            {
                patchFailed = true;
                if (finalAttempt)
                {
                    BellumCivileLogger.Log(
                        $"Diplomacy was detected, but its factions-button compatibility patch could not be applied: {ex.GetType().Name}:{ex.Message}");
                }
            }

            if (patchedAny)
            {
                _diplomacyKingdomFactionsButtonHijackPatched = true;
                IsDiplomacyFactionsButtonIntegrationReady = true;
            }
            else if (finalAttempt && !patchFailed)
                BellumCivileLogger.Log("Diplomacy was detected, but no supported kingdom factions-button target was found; compatibility probing has stopped.");
        }

    }
}
