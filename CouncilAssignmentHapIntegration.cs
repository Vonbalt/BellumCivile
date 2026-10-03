using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Localization;

namespace BellumCivile
{
    internal static class CouncilAssignmentHapIntegration
    {
        private const string HapVillageProductionTypeName = "HarvestAndProduction.Models.VillageProductionModel";
        private const string HapTownFoodEconomyTypeName = "HarvestAndProduction.Behaviors.TownFoodEconomyBehavior";
        private const string ProvisionRequisitionSourceId = "bellum_provisions";
        private static bool _initializationAttempted;
        private static Delegate _tooltipProvider;
        private static MethodInfo _getAvailableMarketFoodMethod;
        private static MethodInfo _tryRequisitionMarketFoodMethod;
        private static object _rawMaterialModeSkip;
        private static bool _productionApiActive;
        private static bool _externalRequisitionApiActive;
        private static bool _requisitionFailureLogged;

        public static bool IsProductionApiActive => _productionApiActive;
        public static bool IsExternalRequisitionApiActive => _externalRequisitionApiActive;

        public static void Initialize()
        {
            if (_initializationAttempted)
                return;

            _initializationAttempted = true;
            InitializeProductionApi();
            InitializeExternalRequisitionApi();
        }

        private static void InitializeProductionApi()
        {
            Type modelType = AccessTools.TypeByName(HapVillageProductionTypeName);
            if (modelType == null)
                return;

            try
            {
                Type entryType = modelType.GetNestedType("ExternalTooltipEntry", BindingFlags.Public);
                Type providerType = modelType.GetNestedType("ExternalTooltipProvider", BindingFlags.Public);
                MethodInfo registerMethod = AccessTools.DeclaredMethod(modelType, "RegisterTooltipProvider");
                FieldInfo labelField = entryType?.GetField("Label", BindingFlags.Instance | BindingFlags.Public);
                FieldInfo factorField = entryType?.GetField("Factor", BindingFlags.Instance | BindingFlags.Public);
                if (entryType == null || providerType == null || registerMethod == null || labelField == null || factorField == null)
                    throw new MissingMemberException("HAP production tooltip API does not match the documented contract.");

                Type listType = typeof(List<>).MakeGenericType(entryType);
                ParameterExpression village = Expression.Parameter(typeof(Village), "village");
                MethodInfo builder = AccessTools.DeclaredMethod(
                    typeof(CouncilAssignmentHapIntegration),
                    nameof(BuildTooltipEntries));
                MethodCallExpression call = Expression.Call(
                    builder,
                    village,
                    Expression.Constant(listType, typeof(Type)),
                    Expression.Constant(entryType, typeof(Type)),
                    Expression.Constant(labelField, typeof(FieldInfo)),
                    Expression.Constant(factorField, typeof(FieldInfo)));
                _tooltipProvider = Expression.Lambda(
                    providerType,
                    Expression.Convert(call, listType),
                    village).Compile();
                registerMethod.Invoke(null, new object[] { _tooltipProvider });
                _productionApiActive = true;
                BellumCivileLogger.Log("HAP council assignment production tooltip provider registered.");
            }
            catch (Exception ex)
            {
                _productionApiActive = false;
                BellumCivileLogger.Log($"HAP council assignment tooltip integration failed safely: {ex.GetBaseException().Message}");
            }
        }

        private static void InitializeExternalRequisitionApi()
        {
            Type economyType = AccessTools.TypeByName(HapTownFoodEconomyTypeName);
            if (economyType == null)
                return;

            try
            {
                Type rawMaterialModeType = economyType.GetNestedType("RawMaterialMode", BindingFlags.Public);
                if (rawMaterialModeType == null || !rawMaterialModeType.IsEnum)
                    throw new MissingMemberException("HAP requisition raw-material mode does not match the documented contract.");

                _rawMaterialModeSkip = Enum.Parse(rawMaterialModeType, "Skip");
                _getAvailableMarketFoodMethod = economyType.GetMethod(
                    "GetAvailableMarketFood",
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    new[] { typeof(Town), rawMaterialModeType },
                    null);
                _tryRequisitionMarketFoodMethod = economyType.GetMethod(
                    "TryRequisitionMarketFood",
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    new[]
                    {
                        typeof(Town),
                        typeof(int),
                        typeof(string),
                        typeof(int).MakeByRefType(),
                        rawMaterialModeType
                    },
                    null);
                if (_getAvailableMarketFoodMethod == null || _tryRequisitionMarketFoodMethod == null)
                    throw new MissingMemberException("HAP external requisition API does not match the documented contract.");

                _externalRequisitionApiActive = true;
                BellumCivileLogger.Log("HAP council assignment external requisition integration enabled.");
            }
            catch (Exception ex)
            {
                _externalRequisitionApiActive = false;
                BellumCivileLogger.Log($"HAP council assignment requisition integration failed safely: {ex.GetBaseException().Message}");
            }
        }

        public static bool TryRequisitionProvisions(Town town, float requisitionPercent, out int transferredFood)
        {
            transferredFood = 0;
            if (!_externalRequisitionApiActive || town == null || requisitionPercent <= 0f)
                return false;

            try
            {
                int availableFood = (int)_getAvailableMarketFoodMethod.Invoke(
                    null,
                    new[] { (object)town, _rawMaterialModeSkip });
                if (availableFood <= 0)
                    return false;

                float boundedPercent = Math.Min(100f, Math.Max(0f, requisitionPercent));
                int desiredFood = (int)Math.Floor(availableFood * boundedPercent / 100f);
                if (desiredFood <= 0)
                    return false;

                object[] arguments =
                {
                    town,
                    Math.Min(desiredFood, availableFood),
                    ProvisionRequisitionSourceId,
                    0,
                    _rawMaterialModeSkip
                };
                bool transferred = (bool)_tryRequisitionMarketFoodMethod.Invoke(null, arguments);
                transferredFood = (int)arguments[3];
                return transferred && transferredFood > 0;
            }
            catch (Exception ex)
            {
                if (!_requisitionFailureLogged)
                {
                    _requisitionFailureLogged = true;
                    BellumCivileLogger.Log($"HAP provision requisition failed safely: {ex.GetBaseException().Message}");
                }

                return false;
            }
        }

        public static float GetVillageProductionPenalty(Village village)
        {
            Kingdom kingdom = village?.Settlement?.OwnerClan?.Kingdom;
            if (kingdom == null)
                return 0f;

            float drawbackMultiplier = CouncilAssignmentRuntimePatches.GetCouncilBehavior()?
                .GetAssignmentDrawbackMultiplier(
                    kingdom,
                    PrivyCouncilOffice.Marshal,
                    "marshal_train_militia") ?? 0f;
            if (drawbackMultiplier <= 0f)
                return 0f;

            float incidentMultiplier = CouncilAssignmentRuntimePatches.GetIncidentBehavior()?
                .GetAssignmentAspectMultiplier(
                    kingdom,
                    PrivyCouncilOffice.Marshal,
                    "marshal_train_militia",
                    CouncilIncidentAspects.VillageProductionPenalty) ?? 1f;
            return 0.10f * drawbackMultiplier * incidentMultiplier;
        }

        private static object BuildTooltipEntries(
            Village village,
            Type listType,
            Type entryType,
            FieldInfo labelField,
            FieldInfo factorField)
        {
            IList entries = (IList)Activator.CreateInstance(listType);
            float penalty = GetVillageProductionPenalty(village);
            if (penalty <= 0f)
                return entries;

            object entry = Activator.CreateInstance(entryType);
            labelField.SetValue(
                entry,
                new TextObject("{=BC_Council_AssignmentVillageProduction}Militia training levies").ToString());
            factorField.SetValue(entry, -penalty);
            entries.Add(entry);
            return entries;
        }
    }
}
