using System;
using System.Collections.Generic;
using System.Reflection;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.ComponentInterfaces;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Localization;

namespace BellumCivile
{
    internal static class CouncilAssignmentRuntimePatches
    {
        private static readonly Harmony Harmony = new Harmony("com.bellumcivile.council.assignments");
        private static readonly HashSet<MethodBase> PatchedMethods = new HashSet<MethodBase>();
        private static readonly TextObject VillageProductionReason =
            new TextObject("{=BC_Council_AssignmentVillageProduction}Militia training levies");
        private static readonly TextObject ArmyLogisticsReason =
            new TextObject("{=BC_Council_AssignmentArmyLogistics}Council logistics");
        private static readonly TextObject PatrolSizeReason =
            new TextObject("{=BC_Council_AssignmentPatrolSize}Council patrol organization");
        private static PrivyCouncilBehavior _councilBehavior;
        private static CouncilIncidentBehavior _incidentBehavior;

        [ThreadStatic]
        private static bool _suppressPatrolSizeAssignment;

        internal static void BindCouncilBehavior(PrivyCouncilBehavior behavior)
        {
            _councilBehavior = behavior;
        }

        internal static void BindIncidentBehavior(CouncilIncidentBehavior behavior)
        {
            _incidentBehavior = behavior;
        }

        internal static PrivyCouncilBehavior GetCouncilBehavior()
        {
            if (_councilBehavior == null)
                _councilBehavior = Campaign.Current?.GetCampaignBehavior<PrivyCouncilBehavior>();
            return _councilBehavior;
        }

        internal static CouncilIncidentBehavior GetIncidentBehavior()
        {
            if (_incidentBehavior == null)
                _incidentBehavior = Campaign.Current?.GetCampaignBehavior<CouncilIncidentBehavior>();
            return _incidentBehavior;
        }

        public static void Initialize(
            VillageProductionCalculatorModel villageProductionModel,
            MobilePartyFoodConsumptionModel foodConsumptionModel,
            PartySizeLimitModel partySizeLimitModel)
        {
            _councilBehavior = null;
            _incidentBehavior = null;
            if (!CouncilAssignmentHapIntegration.IsProductionApiActive)
            {
                PatchActiveModelMethod(
                    villageProductionModel,
                    "CalculateDailyProductionAmount",
                    new[] { typeof(Village), typeof(ItemObject) },
                    nameof(ApplyVillageProductionAssignment));
            }
            else
            {
                BellumCivileLogger.Log("Council village production assignment is using HAP's registered provider.");
            }
            PatchActiveModelMethod(
                foodConsumptionModel,
                "CalculateDailyBaseFoodConsumptionf",
                new[] { typeof(MobileParty), typeof(bool) },
                nameof(ApplyArmyFoodAssignment));
            PatchActiveModelMethod(
                partySizeLimitModel,
                "GetPartyMemberSizeLimit",
                new[] { typeof(PartyBase), typeof(bool) },
                nameof(ApplyPatrolSizeAssignment));
            PatchActiveModelMethod(
                partySizeLimitModel,
                "FindAppropriateInitialRosterForMobileParty",
                new[] { typeof(MobileParty), typeof(PartyTemplateObject) },
                nameof(ApplyPatrolRosterAssignment));
        }

        private static void PatchActiveModelMethod(
            object model,
            string methodName,
            Type[] argumentTypes,
            string postfixName)
        {
            if (model == null)
                return;

            MethodInfo target = ResolveImplementation(model.GetType(), methodName, argumentTypes);
            if (target == null || PatchedMethods.Contains(target))
                return;

            try
            {
                MethodInfo postfix = AccessTools.DeclaredMethod(typeof(CouncilAssignmentRuntimePatches), postfixName);
                Harmony.Patch(target, postfix: new HarmonyMethod(postfix));
                PatchedMethods.Add(target);
                BellumCivileLogger.Log($"Council assignment runtime patch applied; model={target.DeclaringType?.FullName}; method={target.Name}.");
            }
            catch (Exception ex)
            {
                BellumCivileLogger.Log($"Council assignment runtime patch failed; model={model.GetType().FullName}; method={methodName}; error={ex.Message}");
            }
        }

        private static MethodInfo ResolveImplementation(Type type, string methodName, Type[] argumentTypes)
        {
            while (type != null)
            {
                MethodInfo method = type.GetMethod(
                    methodName,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
                    null,
                    argumentTypes,
                    null);
                if (method != null)
                    return method;

                type = type.BaseType;
            }

            return null;
        }

        private static void ApplyVillageProductionAssignment(Village village, ref ExplainedNumber __result)
        {
            float penalty = CouncilAssignmentHapIntegration.GetVillageProductionPenalty(village);
            if (penalty > 0f)
            {
                __result.AddFactor(
                    -penalty,
                    VillageProductionReason);
            }
        }

        private static void ApplyArmyFoodAssignment(MobileParty party, ref ExplainedNumber __result)
        {
            Kingdom kingdom = party?.Army?.Kingdom;
            if (kingdom == null)
                return;

            float efficiency = GetCouncilBehavior()?.GetAssignmentEffectMultiplier(
                    kingdom,
                    PrivyCouncilOffice.Marshal,
                    "marshal_oversee_logistics") ?? 0f;
            if (efficiency > 0f)
            {
                float incidentMultiplier = GetIncidentBehavior()?.GetAssignmentAspectMultiplier(
                        kingdom,
                        PrivyCouncilOffice.Marshal,
                        "marshal_oversee_logistics",
                        CouncilIncidentAspects.ArmyFoodEfficiency) ?? 1f;
                efficiency *= incidentMultiplier;
                __result.AddFactor(
                    -0.15f * efficiency,
                    ArmyLogisticsReason);
            }
        }

        private static void ApplyPatrolSizeAssignment(PartyBase party, ref ExplainedNumber __result)
        {
            if (_suppressPatrolSizeAssignment)
                return;

            MobileParty mobileParty = party?.MobileParty;
            if (mobileParty?.IsPatrolParty != true)
                return;

            Kingdom kingdom = mobileParty.HomeSettlement?.OwnerClan?.Kingdom
                ?? mobileParty.MapFaction as Kingdom;
            float efficiency = GetCouncilBehavior()?.GetAssignmentEffectMultiplier(
                    kingdom,
                    PrivyCouncilOffice.Marshal,
                    "marshal_organize_patrols") ?? 0f;
            if (efficiency > 0f)
            {
                float incidentMultiplier = GetIncidentBehavior()?.GetAssignmentAspectMultiplier(
                        kingdom,
                        PrivyCouncilOffice.Marshal,
                        "marshal_organize_patrols",
                        CouncilIncidentAspects.PatrolPartySize) ?? 1f;
                efficiency *= incidentMultiplier;
            }
            if (efficiency > 0f)
            {
                __result.AddFactor(
                    0.10f * efficiency,
                    PatrolSizeReason);
            }
        }

        private static void ApplyPatrolRosterAssignment(
            MobileParty party,
            PartyTemplateObject partyTemplate,
            ref TroopRoster __result)
        {
            if (__result == null
                || !TryGetPatrolAssignmentLimits(party, out int baseLimit, out int targetLimit))
            {
                return;
            }

            int assignmentSlots = targetLimit - baseLimit;
            int desiredCount = Math.Min(targetLimit, __result.TotalManCount + assignmentSlots);
            AddPatrolTemplateTroops(__result, partyTemplate, desiredCount - __result.TotalManCount);
        }

        internal static void ReinforcePatrolAssignmentSlots(MobileParty party)
        {
            if (!TryGetPatrolAssignmentLimits(party, out int baseLimit, out int targetLimit))
                return;

            party.MemberRoster.UpdateVersion();
            int currentCount = party.MemberRoster.TotalManCount;
            if (currentCount < baseLimit || currentCount >= targetLimit)
                return;

            PartyTemplateObject template = Campaign.Current?.Models?.SettlementPatrolModel?
                .GetPartyTemplateForPatrolParty(party.HomeSettlement, party.PatrolPartyComponent.IsNaval);
            if (AddPatrolTemplateTroops(party.MemberRoster, template, targetLimit - currentCount) > 0)
                party.PatrolPartyComponent.SortRoster();
        }

        private static bool TryGetPatrolAssignmentLimits(
            MobileParty party,
            out int baseLimit,
            out int targetLimit)
        {
            baseLimit = 0;
            targetLimit = 0;
            if (party?.IsPatrolParty != true || party.Party == null || party.HomeSettlement == null)
                return false;

            PartySizeLimitModel model = Campaign.Current?.Models?.PartySizeLimitModel;
            if (model == null)
                return false;

            try
            {
                _suppressPatrolSizeAssignment = true;
                baseLimit = Math.Max(0, (int)model.GetPartyMemberSizeLimit(party.Party).ResultNumber);
            }
            finally
            {
                _suppressPatrolSizeAssignment = false;
            }

            targetLimit = Math.Max(0, (int)model.GetPartyMemberSizeLimit(party.Party).ResultNumber);
            return baseLimit > 0 && targetLimit > baseLimit;
        }

        private static int AddPatrolTemplateTroops(
            TroopRoster roster,
            PartyTemplateObject template,
            int requestedCount)
        {
            if (roster == null || template?.Stacks == null || requestedCount <= 0)
                return 0;

            List<CharacterObject> weightedTroops = new List<CharacterObject>();
            foreach (PartyTemplateStack stack in template.Stacks)
            {
                if (stack.Character == null)
                    continue;

                int weight = Math.Max(1, stack.MaxValue);
                for (int i = 0; i < weight; i++)
                    weightedTroops.Add(stack.Character);
            }

            if (weightedTroops.Count == 0)
                return 0;

            for (int i = 0; i < requestedCount; i++)
            {
                CharacterObject recruit = weightedTroops[MBRandom.RandomInt(weightedTroops.Count)];
                roster.AddToCounts(recruit, 1);
            }

            return requestedCount;
        }
    }
}
