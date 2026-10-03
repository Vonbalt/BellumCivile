using System;
using System.Collections.Generic;
using System.Linq;
using BellumCivile.Behaviors;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.Localization;

namespace BellumCivile
{
    internal static class CouncilIncidentAspects
    {
        public const string PatrolPartySize = "patrol_party_size";
        public const string PatrolFormationSpeed = "patrol_formation_speed";
        public const string RoadFailureControversy = "road_failure_controversy";
        public const string MilitiaGrowth = "militia_growth";
        public const string VeteranMilitiaChance = "veteran_militia_chance";
        public const string VillageProductionPenalty = "village_production_penalty";
        public const string ArmyCohesionRetention = "army_cohesion_retention";
        public const string ArmyFoodEfficiency = "army_food_efficiency";
        public const string LogisticsWeeklyControversy = "logistics_weekly_controversy";
        public const string NobleAppeasementStrength = "noble_appeasement_strength";
        public const string NobleAppeasementReach = "noble_appeasement_reach";
        public const string NobleAppeasementWeeklyControversy = "noble_appeasement_weekly_controversy";
        public const string ForeignRelationsStrength = "foreign_relations_strength";
        public const string ForeignRelationsReach = "foreign_relations_reach";
        public const string ForeignRelationsWeeklyControversy = "foreign_relations_weekly_controversy";
        public const string FabricationProgress = "fabrication_progress";
        public const string FabricationDiscoveryRisk = "fabrication_discovery_risk";
        public const string FabricationWeeklyControversy = "fabrication_weekly_controversy";
        public const string AuditServiceStrength = "audit_service_strength";
        public const string AuditReach = "audit_reach";
        public const string AuditWeeklyControversy = "audit_weekly_controversy";
        public const string InfrastructureConstructionStrength = "infrastructure_construction_strength";
        public const string InfrastructureDailyCost = "infrastructure_daily_cost";
        public const string InfrastructureEfficiency = "infrastructure_efficiency";
        public const string ProvisionsFoodYield = "provisions_food_yield";
        public const string ProvisionsDailyCost = "provisions_daily_cost";
        public const string ProvisionsDistributionEfficiency = "provisions_distribution_efficiency";
        public const string CounterEspionageDefense = "counter_espionage_defense";
        public const string CounterEspionageClaimDiscovery = "counter_espionage_claim_discovery";
        public const string CounterEspionageOffensePenalty = "counter_espionage_offense_penalty";
        public const string DissentConspiracySuppression = "dissent_conspiracy_suppression";
        public const string DissentClaimDiscovery = "dissent_claim_discovery";
        public const string DissentWeeklyControversy = "dissent_weekly_controversy";
        public const string RumorOffense = "rumor_offense";
        public const string RumorDefensePenalty = "rumor_defense_penalty";
        public const string RumorWeeklyControversy = "rumor_weekly_controversy";
        public const string AdvisorCounselCompetence = "advisor_counsel_competence";
        public const string AdvisorMediationSupport = "advisor_mediation_support";
        public const string AdvisorMediationControversy = "advisor_mediation_controversy";
        public const string AdvisorRepresentationMood = "advisor_representation_mood";
        public const string AdvisorRepresentationIntent = "advisor_representation_intent";
        public const string AdvisorRepresentationOverall = "advisor_representation_overall";
    }

    internal enum CouncilIncidentQuality
    {
        Failure = 0,
        Neutral = 1,
        Success = 2
    }

    internal sealed class CouncilIncidentContext
    {
        public CouncilIncidentContext(
            CouncilIncidentBehavior incidentBehavior,
            PrivyCouncilBehavior councilBehavior,
            Kingdom kingdom,
            PrivyCouncilOffice office,
            string assignmentId,
            Clan holder,
            float competence,
            CouncilIncidentQuality quality)
        {
            IncidentBehavior = incidentBehavior;
            CouncilBehavior = councilBehavior;
            Kingdom = kingdom;
            Office = office;
            AssignmentId = assignmentId ?? string.Empty;
            Holder = holder;
            Competence = Math.Max(0f, Math.Min(100f, competence));
            Quality = quality;
        }

        public CouncilIncidentBehavior IncidentBehavior { get; }
        public PrivyCouncilBehavior CouncilBehavior { get; }
        public Kingdom Kingdom { get; }
        public PrivyCouncilOffice Office { get; }
        public string AssignmentId { get; }
        public Clan Holder { get; }
        public float Competence { get; }
        public CouncilIncidentQuality Quality { get; }

        public int SuccessChance => (int)Math.Round(Competence, MidpointRounding.AwayFromZero);

        public int NeutralChance
        {
            get
            {
                float chance = (1f - Competence / 100f) * (Competence / 100f) * 100f;
                return (int)Math.Round(chance, MidpointRounding.AwayFromZero);
            }
        }

        public int FailureChance => Math.Max(0, 100 - SuccessChance - NeutralChance);

        public void ApplyAspectMultiplier(string aspectId, string sourceId, float multiplier, float durationDays)
        {
            IncidentBehavior?.SetAssignmentAspectModifier(
                Kingdom,
                Office,
                AssignmentId,
                aspectId,
                sourceId,
                multiplier,
                durationDays);
        }

        public void ChangeOfficeControversy(float amount, string reason)
        {
            PrivyCouncilOfficeRecord record = CouncilBehavior?.GetOfficeRecord(Kingdom, Office);
            record?.ChangeControversy(amount, reason, (float)CampaignTime.Now.ToDays);
        }

        public void ChangeHolderRelation(int amount)
        {
            Hero ruler = Kingdom?.RulingClan?.Leader;
            Hero councilor = Holder?.Leader;
            if (ruler == null || councilor == null || ruler == councilor || amount == 0)
                return;

            ChangeRelationAction.ApplyRelationChangeBetweenHeroes(
                ruler,
                councilor,
                amount,
                showQuickNotification: ruler == Hero.MainHero || councilor == Hero.MainHero);
        }

        public TextObject ApplyInstitutionName(TextObject text)
        {
            return CourtInstitutionDisplayHelper.ApplyOfficeName(text, Office, Kingdom);
        }

        public List<TextObject> ApplyInstitutionNames(List<TextObject> texts)
        {
            if (texts == null)
                return new List<TextObject>();

            foreach (TextObject text in texts)
                ApplyInstitutionName(text);
            return texts;
        }
    }

    internal sealed class CouncilIncidentOptionDefinition
    {
        private readonly Func<CouncilIncidentContext, List<TextObject>> _hintFactory;
        private readonly Func<CouncilIncidentContext, List<TextObject>> _effect;

        public CouncilIncidentOptionDefinition(
            string text,
            Func<CouncilIncidentContext, List<TextObject>> hintFactory,
            Func<CouncilIncidentContext, List<TextObject>> effect)
        {
            Text = new TextObject(text);
            _hintFactory = hintFactory;
            _effect = effect;
        }

        public TextObject Text { get; }

        public List<TextObject> GetHints(CouncilIncidentContext context)
        {
            return context?.ApplyInstitutionNames(
                _hintFactory?.Invoke(context) ?? new List<TextObject>())
                ?? new List<TextObject>();
        }

        public List<TextObject> Apply(CouncilIncidentContext context)
        {
            return context?.ApplyInstitutionNames(
                _effect?.Invoke(context) ?? new List<TextObject>())
                ?? new List<TextObject>();
        }
    }

    internal sealed class CouncilIncidentDefinition
    {
        private readonly Func<CouncilIncidentContext, TextObject> _titleFactory;
        private readonly Func<CouncilIncidentContext, TextObject> _descriptionFactory;
        private readonly Func<CouncilIncidentContext, List<CouncilIncidentOptionDefinition>> _optionFactory;

        public CouncilIncidentDefinition(
            string id,
            string assignmentId,
            PrivyCouncilOffice office,
            IncidentsCampaignBehaviour.IncidentType incidentType,
            float selectionWeight,
            Func<CouncilIncidentContext, TextObject> titleFactory,
            Func<CouncilIncidentContext, TextObject> descriptionFactory,
            Func<CouncilIncidentContext, List<CouncilIncidentOptionDefinition>> optionFactory)
        {
            Id = id ?? string.Empty;
            AssignmentId = assignmentId ?? string.Empty;
            Office = office;
            IncidentType = incidentType;
            SelectionWeight = Math.Max(0f, selectionWeight);
            _titleFactory = titleFactory;
            _descriptionFactory = descriptionFactory;
            _optionFactory = optionFactory;
        }

        public string Id { get; }
        public string AssignmentId { get; }
        public PrivyCouncilOffice Office { get; }
        public IncidentsCampaignBehaviour.IncidentType IncidentType { get; }
        public float SelectionWeight { get; }

        public TextObject GetTitle(CouncilIncidentContext context)
        {
            return context?.ApplyInstitutionName(
                _titleFactory?.Invoke(context) ?? TextObject.GetEmpty())
                ?? TextObject.GetEmpty();
        }

        public TextObject GetDescription(CouncilIncidentContext context)
        {
            return context?.ApplyInstitutionName(
                _descriptionFactory?.Invoke(context) ?? TextObject.GetEmpty())
                ?? TextObject.GetEmpty();
        }

        public List<CouncilIncidentOptionDefinition> GetOptions(CouncilIncidentContext context)
        {
            List<CouncilIncidentOptionDefinition> options =
                _optionFactory?.Invoke(context) ?? new List<CouncilIncidentOptionDefinition>();
            foreach (CouncilIncidentOptionDefinition option in options)
                context?.ApplyInstitutionName(option?.Text);
            return options;
        }
    }

    internal static class CouncilIncidentRegistry
    {
        private const string BolsterTownWatchEventId = "marshal_organize_patrols_bolster_watch";
        private const string RequisitionRemountsEventId = "marshal_organize_patrols_requisition_remounts";
        private const string BrigandsOnRoadEventId = "marshal_organize_patrols_brigands_on_road";
        private const string MusterRollsEventId = "marshal_train_militia_muster_rolls";
        private const string LiveSteelSparringEventId = "marshal_train_militia_live_steel_sparring";
        private const string HarvestExemptionEventId = "marshal_train_militia_harvest_exemption";
        private const string CampFollowersEventId = "marshal_oversee_logistics_camp_followers";
        private const string ForagingRightsEventId = "marshal_oversee_logistics_foraging_rights";
        private const string QuartermastersSkimEventId = "marshal_oversee_logistics_quartermasters_skim";
        private const string ChancellorBanquetEventId = "chancellor_appease_nobles_banquet";
        private const string ChancellorHuntingDisputeEventId = "chancellor_appease_nobles_hunting_dispute";
        private const string ChancellorVanityTitleEventId = "chancellor_appease_nobles_vanity_title";
        private const string ChancellorExtravagantGiftEventId = "chancellor_improve_foreign_relations_extravagant_gift";
        private const string ChancellorMatchmakerEventId = "chancellor_improve_foreign_relations_matchmaker";
        private const string ChancellorBorderApologyEventId = "chancellor_improve_foreign_relations_border_apology";
        private const string ChancellorForgedLineageEventId = "chancellor_fabricate_grievances_forged_lineage";
        private const string ChancellorLooseLippedInformantEventId = "chancellor_fabricate_grievances_loose_lipped_informant";
        private const string ChancellorBribingClericsEventId = "chancellor_fabricate_grievances_bribing_clerics";
        private const string SeneschalHiddenLedgersEventId = "seneschal_audit_vassals_hidden_ledgers";
        private const string SeneschalNobleExemptionEventId = "seneschal_audit_vassals_noble_exemption";
        private const string SeneschalRoyalAssessorsEventId = "seneschal_audit_vassals_royal_assessors";
        private const string SeneschalCorruptGuildmasterEventId = "seneschal_subsidize_infrastructure_corrupt_guildmaster";
        private const string SeneschalPeasantCorveeEventId = "seneschal_subsidize_infrastructure_peasant_corvee";
        private const string SeneschalMaterialShortagesEventId = "seneschal_subsidize_infrastructure_material_shortages";
        private const string SeneschalVerminGranariesEventId = "seneschal_stockpile_provisions_vermin_granaries";
        private const string SeneschalHoardingPanicEventId = "seneschal_stockpile_provisions_hoarding_panic";
        private const string SeneschalSpoiledMeatEventId = "seneschal_stockpile_provisions_spoiled_meat";
        private const string SpymasterDoubleAgentEventId = "spymaster_counter_espionage_double_agent";
        private const string SpymasterCourtParanoiaEventId = "spymaster_counter_espionage_court_paranoia";
        private const string SpymasterCompromisedGuardEventId = "spymaster_counter_espionage_compromised_guard";
        private const string SpymasterFabricatedTreasonEventId = "spymaster_uncover_dissent_fabricated_treason";
        private const string SpymasterAgentProvocateurEventId = "spymaster_uncover_dissent_agent_provocateur";
        private const string SpymasterUndergroundNetworkEventId = "spymaster_uncover_dissent_underground_network";
        private const string SpymasterViciousPamphletsEventId = "spymaster_sow_rumors_vicious_pamphlets";
        private const string SpymasterBribedServantEventId = "spymaster_sow_rumors_bribed_servant";
        private const string SpymasterCounterRumorEventId = "spymaster_sow_rumors_counter_rumor";
        private static readonly List<CouncilIncidentDefinition> Definitions =
            new List<CouncilIncidentDefinition>();

        private sealed class ChancellorIncidentTemplate
        {
            public string Id { get; set; }
            public string AssignmentId { get; set; }
            public string AspectId { get; set; }
            public bool IsDrawback { get; set; }
            public string Title { get; set; }
            public string Description { get; set; }
            public string EffectLabel { get; set; }
            public string Endorse { get; set; }
            public string Oppose { get; set; }
            public string Abstain { get; set; }
            public string EndorseSuccessResult { get; set; }
            public string EndorseNeutralResult { get; set; }
            public string EndorseFailureResult { get; set; }
            public string OpposeResult { get; set; }
            public string AbstainSuccessResult { get; set; }
            public string AbstainNeutralResult { get; set; }
            public string AbstainFailureResult { get; set; }
            public string SuccessReason { get; set; }
            public string FailureReason { get; set; }
            public string LimitedSuccessReason { get; set; }
            public string LimitedFailureReason { get; set; }
        }

        private sealed class SeneschalIncidentTemplate
        {
            public string Id { get; set; }
            public string AssignmentId { get; set; }
            public string AspectId { get; set; }
            public bool IsDrawback { get; set; }
            public string Title { get; set; }
            public string Description { get; set; }
            public string EffectLabel { get; set; }
            public string Endorse { get; set; }
            public string Oppose { get; set; }
            public string Abstain { get; set; }
            public string EndorseSuccessResult { get; set; }
            public string EndorseNeutralResult { get; set; }
            public string EndorseFailureResult { get; set; }
            public string OpposeResult { get; set; }
            public string AbstainSuccessResult { get; set; }
            public string AbstainNeutralResult { get; set; }
            public string AbstainFailureResult { get; set; }
            public string SuccessReason { get; set; }
            public string FailureReason { get; set; }
            public string LimitedSuccessReason { get; set; }
            public string LimitedFailureReason { get; set; }
        }

        private sealed class SpymasterIncidentTemplate
        {
            public string Id { get; set; }
            public string AssignmentId { get; set; }
            public string AspectId { get; set; }
            public bool IsDrawback { get; set; }
            public string Title { get; set; }
            public string Description { get; set; }
            public string EffectLabel { get; set; }
            public string Endorse { get; set; }
            public string Oppose { get; set; }
            public string Abstain { get; set; }
            public string EndorseSuccessResult { get; set; }
            public string EndorseNeutralResult { get; set; }
            public string EndorseFailureResult { get; set; }
            public string OpposeResult { get; set; }
            public string AbstainSuccessResult { get; set; }
            public string AbstainNeutralResult { get; set; }
            public string AbstainFailureResult { get; set; }
            public string SuccessReason { get; set; }
            public string FailureReason { get; set; }
            public string LimitedSuccessReason { get; set; }
            public string LimitedFailureReason { get; set; }
        }

        private sealed class AdvisorIncidentTemplate
        {
            public string EventSuffix { get; set; }
            public string AssignmentSuffix { get; set; }
            public string AspectId { get; set; }
            public bool IsDrawback { get; set; }
            public string Title { get; set; }
            public string Description { get; set; }
            public string EffectLabel { get; set; }
            public string Endorse { get; set; }
            public string Oppose { get; set; }
            public string Abstain { get; set; }
            public string EndorseSuccessResult { get; set; }
            public string EndorseNeutralResult { get; set; }
            public string EndorseFailureResult { get; set; }
            public string OpposeResult { get; set; }
            public string AbstainSuccessResult { get; set; }
            public string AbstainNeutralResult { get; set; }
            public string AbstainFailureResult { get; set; }
            public string SuccessReason { get; set; }
            public string FailureReason { get; set; }
            public string LimitedSuccessReason { get; set; }
            public string LimitedFailureReason { get; set; }
        }

        static CouncilIncidentRegistry()
        {
            Register(CreateBolsterTownWatchEvent());
            Register(CreateRequisitionRemountsEvent());
            Register(CreateBrigandsOnRoadEvent());
            Register(CreateMusterRollsEvent());
            Register(CreateLiveSteelSparringEvent());
            Register(CreateHarvestExemptionEvent());
            Register(CreateCampFollowersEvent());
            Register(CreateForagingRightsEvent());
            Register(CreateQuartermastersSkimEvent());
            Register(CreateChancellorBanquetEvent());
            Register(CreateChancellorHuntingDisputeEvent());
            Register(CreateChancellorVanityTitleEvent());
            Register(CreateChancellorExtravagantGiftEvent());
            Register(CreateChancellorMatchmakerEvent());
            Register(CreateChancellorBorderApologyEvent());
            Register(CreateChancellorForgedLineageEvent());
            Register(CreateChancellorLooseLippedInformantEvent());
            Register(CreateChancellorBribingClericsEvent());
            Register(CreateSeneschalHiddenLedgersEvent());
            Register(CreateSeneschalNobleExemptionEvent());
            Register(CreateSeneschalRoyalAssessorsEvent());
            Register(CreateSeneschalCorruptGuildmasterEvent());
            Register(CreateSeneschalPeasantCorveeEvent());
            Register(CreateSeneschalMaterialShortagesEvent());
            Register(CreateSeneschalVerminGranariesEvent());
            Register(CreateSeneschalHoardingPanicEvent());
            Register(CreateSeneschalSpoiledMeatEvent());
            Register(CreateSpymasterDoubleAgentEvent());
            Register(CreateSpymasterCourtParanoiaEvent());
            Register(CreateSpymasterCompromisedGuardEvent());
            Register(CreateSpymasterFabricatedTreasonEvent());
            Register(CreateSpymasterAgentProvocateurEvent());
            Register(CreateSpymasterUndergroundNetworkEvent());
            Register(CreateSpymasterViciousPamphletsEvent());
            Register(CreateSpymasterBribedServantEvent());
            Register(CreateSpymasterCounterRumorEvent());
            RegisterAdvisorIncidents(PrivyCouncilOffice.FirstAdvisor, "first_advisor");
            RegisterAdvisorIncidents(PrivyCouncilOffice.SecondAdvisor, "second_advisor");
        }

        public static void Register(CouncilIncidentDefinition definition)
        {
            if (definition == null || string.IsNullOrWhiteSpace(definition.Id))
                throw new ArgumentException("A council incident requires a stable id.", nameof(definition));

            if (Definitions.Any(existing => string.Equals(existing.Id, definition.Id, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("A council incident with id '" + definition.Id + "' is already registered.");

            Definitions.Add(definition);
        }

        public static CouncilIncidentDefinition GetDefinition(string id)
        {
            if (string.IsNullOrEmpty(id))
                return null;

            return Definitions.FirstOrDefault(definition =>
                string.Equals(definition.Id, id, StringComparison.OrdinalIgnoreCase));
        }

        public static IReadOnlyList<CouncilIncidentDefinition> GetEligibleDefinitions(
            PrivyCouncilOffice office,
            string assignmentId)
        {
            return Definitions.Where(definition => definition.Office == office
                && string.Equals(definition.AssignmentId, assignmentId, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        private static CouncilIncidentDefinition CreateBolsterTownWatchEvent()
        {
            return new CouncilIncidentDefinition(
                BolsterTownWatchEventId,
                "marshal_organize_patrols",
                PrivyCouncilOffice.Marshal,
                IncidentsCampaignBehaviour.IncidentType.FiefManagement,
                1f,
                context => new TextObject("{=BC_CouncilIncident_BolsterWatch_Title}Bolstering the Town Watch"),
                context =>
                {
                    TextObject description = new TextObject("{=BC_CouncilIncident_BolsterWatch_Description}{MARSHAL_NAME}, your marshal, has proposed sending recruiters into the countryside to entice young villagers into the local watches with promises of adventure and a signing bonus. The marshal argues that fresh recruits would extend the reach of the realm's patrols and make the roads safer.");
                    description.SetTextVariable("MARSHAL_NAME", context?.Holder?.Leader?.Name ?? context?.Holder?.Name ?? TextObject.GetEmpty());
                    return description;
                },
                BuildBolsterTownWatchOptions);
        }

        private static List<CouncilIncidentOptionDefinition> BuildBolsterTownWatchOptions(
            CouncilIncidentContext context)
        {
            return new List<CouncilIncidentOptionDefinition>
            {
                new CouncilIncidentOptionDefinition(
                    "{=BC_CouncilIncident_BolsterWatch_Endorse}Endorse the recruitment campaign and accept the risks.",
                    BuildEndorseHints,
                    ApplyEndorse),
                new CouncilIncidentOptionDefinition(
                    "{=BC_CouncilIncident_BolsterWatch_Oppose}Veto the proposal and retain the present patrol organization.",
                    BuildOpposeHints,
                    ApplyOppose),
                new CouncilIncidentOptionDefinition(
                    "{=BC_CouncilIncident_BolsterWatch_Abstain}Permit a limited trial without committing the Crown fully.",
                    BuildAbstainHints,
                    ApplyAbstain)
            };
        }

        private static List<TextObject> BuildEndorseHints(CouncilIncidentContext context)
        {
            return new List<TextObject>
            {
                ChanceText("{=BC_CouncilIncident_BolsterWatch_EndorseSuccess}{CHANCE}% chance: double the patrol-size effect for {DAYS} days and reduce Marshal controversy by 5.", context.SuccessChance, BellumCivileConstants.CouncilIncidentEndorsedEffectDays),
                ChanceText("{=BC_CouncilIncident_BolsterWatch_EndorseNeutral}{CHANCE}% chance: strengthen the patrol-size effect by 50% for {DAYS} days.", context.NeutralChance, BellumCivileConstants.CouncilIncidentEndorsedEffectDays),
                ChanceText("{=BC_CouncilIncident_BolsterWatch_EndorseFailure}{CHANCE}% chance: halve the patrol-size effect for {DAYS} days and increase Marshal controversy by 10.", context.FailureChance, BellumCivileConstants.CouncilIncidentEndorsedEffectDays),
                new TextObject("{=BC_CouncilIncident_BolsterWatch_EndorseRelation}Gain 5 relation with your marshal.")
            };
        }

        private static List<TextObject> BuildOpposeHints(CouncilIncidentContext context)
        {
            return new List<TextObject>
            {
                new TextObject("{=BC_CouncilIncident_BolsterWatch_OpposeHint}The assignment remains unchanged. Lose 10 relation with your marshal.")
            };
        }

        private static List<TextObject> BuildAbstainHints(CouncilIncidentContext context)
        {
            return new List<TextObject>
            {
                ChanceText("{=BC_CouncilIncident_BolsterWatch_AbstainSuccess}{CHANCE}% chance: strengthen the patrol-size effect by 50% for {DAYS} days and reduce Marshal controversy by 2.", context.SuccessChance, BellumCivileConstants.CouncilIncidentLimitedTrialEffectDays),
                ChanceText("{=BC_CouncilIncident_BolsterWatch_AbstainNeutral}{CHANCE}% chance: the assignment continues unchanged.", context.NeutralChance),
                ChanceText("{=BC_CouncilIncident_BolsterWatch_AbstainFailure}{CHANCE}% chance: weaken the patrol-size effect by 25% for {DAYS} days and increase Marshal controversy by 5.", context.FailureChance, BellumCivileConstants.CouncilIncidentLimitedTrialEffectDays)
            };
        }

        private static List<TextObject> ApplyEndorse(CouncilIncidentContext context)
        {
            context.ChangeHolderRelation(5);
            switch (context.Quality)
            {
                case CouncilIncidentQuality.Success:
                    context.ApplyAspectMultiplier(
                        CouncilIncidentAspects.PatrolPartySize,
                        BolsterTownWatchEventId,
                        2f,
                        BellumCivileConstants.CouncilIncidentEndorsedEffectDays);
                    context.ChangeOfficeControversy(-5f, "successful patrol recruitment");
                    return Result("{=BC_CouncilIncident_BolsterWatch_ResultEndorseSuccess}The recruitment campaign succeeds. Reinforced patrols make the roads safer, and the marshal's standing improves.");
                case CouncilIncidentQuality.Neutral:
                    context.ApplyAspectMultiplier(
                        CouncilIncidentAspects.PatrolPartySize,
                        BolsterTownWatchEventId,
                        1.5f,
                        BellumCivileConstants.CouncilIncidentEndorsedEffectDays);
                    return Result("{=BC_CouncilIncident_BolsterWatch_ResultEndorseNeutral}The recruiters find fewer volunteers than hoped, but patrols are modestly reinforced for a time.");
                default:
                    context.ApplyAspectMultiplier(
                        CouncilIncidentAspects.PatrolPartySize,
                        BolsterTownWatchEventId,
                        0.5f,
                        BellumCivileConstants.CouncilIncidentEndorsedEffectDays);
                    context.ChangeOfficeControversy(10f, "failed patrol recruitment");
                    return Result("{=BC_CouncilIncident_BolsterWatch_ResultEndorseFailure}The campaign backfires. Some watchmen desert and others turn to banditry, compromising established patrol routes.");
            }
        }

        private static List<TextObject> ApplyOppose(CouncilIncidentContext context)
        {
            context.ChangeHolderRelation(-10);
            return Result("{=BC_CouncilIncident_BolsterWatch_ResultOppose}You veto the recruitment campaign. The marshal obeys, but resents the Crown's lack of confidence.");
        }

        private static List<TextObject> ApplyAbstain(CouncilIncidentContext context)
        {
            switch (context.Quality)
            {
                case CouncilIncidentQuality.Success:
                    context.ApplyAspectMultiplier(
                        CouncilIncidentAspects.PatrolPartySize,
                        BolsterTownWatchEventId,
                        1.5f,
                        BellumCivileConstants.CouncilIncidentLimitedTrialEffectDays);
                    context.ChangeOfficeControversy(-2f, "successful limited patrol recruitment");
                    return Result("{=BC_CouncilIncident_BolsterWatch_ResultAbstainSuccess}The limited trial succeeds and gives the patrols a useful influx of recruits.");
                case CouncilIncidentQuality.Neutral:
                    return Result("{=BC_CouncilIncident_BolsterWatch_ResultAbstainNeutral}The limited trial produces little of note, and patrol organization continues as before.");
                default:
                    context.ApplyAspectMultiplier(
                        CouncilIncidentAspects.PatrolPartySize,
                        BolsterTownWatchEventId,
                        0.75f,
                        BellumCivileConstants.CouncilIncidentLimitedTrialEffectDays);
                    context.ChangeOfficeControversy(5f, "failed limited patrol recruitment");
                    return Result("{=BC_CouncilIncident_BolsterWatch_ResultAbstainFailure}Even the limited trial causes desertions and weakens patrol coverage for a time.");
            }
        }

        private static CouncilIncidentDefinition CreateRequisitionRemountsEvent()
        {
            return new CouncilIncidentDefinition(
                RequisitionRemountsEventId,
                "marshal_organize_patrols",
                PrivyCouncilOffice.Marshal,
                IncidentsCampaignBehaviour.IncidentType.FiefManagement,
                1f,
                context => new TextObject("{=BC_CouncilIncident_Remounts_Title}Requisitioning Remounts"),
                context =>
                {
                    TextObject description = new TextObject("{=BC_CouncilIncident_Remounts_Description}{MARSHAL_NAME}, your marshal, reports that patrol musters are being delayed by a shortage of suitable horses. The marshal proposes requisitioning remounts from village breeders, caravan yards, and noble estates so newly raised patrols can take to the roads sooner. Such measures may improve readiness, but hurried seizures could provoke resistance and leave the realm with exhausted or unsuitable mounts.");
                    description.SetTextVariable("MARSHAL_NAME", context?.Holder?.Leader?.Name ?? context?.Holder?.Name ?? TextObject.GetEmpty());
                    return description;
                },
                BuildRequisitionRemountsOptions);
        }

        private static List<CouncilIncidentOptionDefinition> BuildRequisitionRemountsOptions(
            CouncilIncidentContext context)
        {
            return new List<CouncilIncidentOptionDefinition>
            {
                new CouncilIncidentOptionDefinition(
                    "{=BC_CouncilIncident_Remounts_Endorse}Grant warrants for a general requisition.",
                    BuildRequisitionRemountsEndorseHints,
                    ApplyRequisitionRemountsEndorse),
                new CouncilIncidentOptionDefinition(
                    "{=BC_CouncilIncident_Remounts_Oppose}Refuse to disturb the realm's breeders and estates.",
                    BuildRequisitionRemountsOpposeHints,
                    ApplyRequisitionRemountsOppose),
                new CouncilIncidentOptionDefinition(
                    "{=BC_CouncilIncident_Remounts_Abstain}Permit limited requisitions in the most troubled districts.",
                    BuildRequisitionRemountsAbstainHints,
                    ApplyRequisitionRemountsAbstain)
            };
        }

        private static List<TextObject> BuildRequisitionRemountsEndorseHints(CouncilIncidentContext context)
        {
            return new List<TextObject>
            {
                ChanceText("{=BC_CouncilIncident_Remounts_EndorseSuccess}{CHANCE}% chance: double the patrol formation-speed effect for {DAYS} days and reduce Marshal controversy by 5.", context.SuccessChance, BellumCivileConstants.CouncilIncidentEndorsedEffectDays),
                ChanceText("{=BC_CouncilIncident_Remounts_EndorseNeutral}{CHANCE}% chance: strengthen the patrol formation-speed effect by 50% for {DAYS} days.", context.NeutralChance, BellumCivileConstants.CouncilIncidentEndorsedEffectDays),
                ChanceText("{=BC_CouncilIncident_Remounts_EndorseFailure}{CHANCE}% chance: halve the patrol formation-speed effect for {DAYS} days and increase Marshal controversy by 10.", context.FailureChance, BellumCivileConstants.CouncilIncidentEndorsedEffectDays),
                new TextObject("{=BC_CouncilIncident_Remounts_EndorseRelation}Gain 5 relation with your marshal.")
            };
        }

        private static List<TextObject> BuildRequisitionRemountsOpposeHints(CouncilIncidentContext context)
        {
            return new List<TextObject>
            {
                new TextObject("{=BC_CouncilIncident_Remounts_OpposeHint}The assignment remains unchanged. Lose 10 relation with your marshal.")
            };
        }

        private static List<TextObject> BuildRequisitionRemountsAbstainHints(CouncilIncidentContext context)
        {
            return new List<TextObject>
            {
                ChanceText("{=BC_CouncilIncident_Remounts_AbstainSuccess}{CHANCE}% chance: strengthen the patrol formation-speed effect by 50% for {DAYS} days and reduce Marshal controversy by 2.", context.SuccessChance, BellumCivileConstants.CouncilIncidentLimitedTrialEffectDays),
                ChanceText("{=BC_CouncilIncident_Remounts_AbstainNeutral}{CHANCE}% chance: the assignment continues unchanged.", context.NeutralChance),
                ChanceText("{=BC_CouncilIncident_Remounts_AbstainFailure}{CHANCE}% chance: weaken the patrol formation-speed effect by 25% for {DAYS} days and increase Marshal controversy by 5.", context.FailureChance, BellumCivileConstants.CouncilIncidentLimitedTrialEffectDays)
            };
        }

        private static List<TextObject> ApplyRequisitionRemountsEndorse(CouncilIncidentContext context)
        {
            context.ChangeHolderRelation(5);
            switch (context.Quality)
            {
                case CouncilIncidentQuality.Success:
                    context.ApplyAspectMultiplier(
                        CouncilIncidentAspects.PatrolFormationSpeed,
                        RequisitionRemountsEventId,
                        2f,
                        BellumCivileConstants.CouncilIncidentEndorsedEffectDays);
                    context.ChangeOfficeControversy(-5f, "successful remount requisition");
                    return Result("{=BC_CouncilIncident_Remounts_ResultEndorseSuccess}Remount stations are established across the realm, and newly raised patrols take to the roads with remarkable speed.");
                case CouncilIncidentQuality.Neutral:
                    context.ApplyAspectMultiplier(
                        CouncilIncidentAspects.PatrolFormationSpeed,
                        RequisitionRemountsEventId,
                        1.5f,
                        BellumCivileConstants.CouncilIncidentEndorsedEffectDays);
                    return Result("{=BC_CouncilIncident_Remounts_ResultEndorseNeutral}Enough horses are gathered to improve patrol readiness, though several districts resist the requisition.");
                default:
                    context.ApplyAspectMultiplier(
                        CouncilIncidentAspects.PatrolFormationSpeed,
                        RequisitionRemountsEventId,
                        0.5f,
                        BellumCivileConstants.CouncilIncidentEndorsedEffectDays);
                    context.ChangeOfficeControversy(10f, "failed remount requisition");
                    return Result("{=BC_CouncilIncident_Remounts_ResultEndorseFailure}Unsuitable horses, disputed seizures, and resistance from breeders delay the patrol musters rather than hasten them.");
            }
        }

        private static List<TextObject> ApplyRequisitionRemountsOppose(CouncilIncidentContext context)
        {
            context.ChangeHolderRelation(-10);
            return Result("{=BC_CouncilIncident_Remounts_ResultOppose}You forbid the requisitions. The realm's breeders are spared, but the marshal resents the Crown's refusal.");
        }

        private static List<TextObject> ApplyRequisitionRemountsAbstain(CouncilIncidentContext context)
        {
            switch (context.Quality)
            {
                case CouncilIncidentQuality.Success:
                    context.ApplyAspectMultiplier(
                        CouncilIncidentAspects.PatrolFormationSpeed,
                        RequisitionRemountsEventId,
                        1.5f,
                        BellumCivileConstants.CouncilIncidentLimitedTrialEffectDays);
                    context.ChangeOfficeControversy(-2f, "successful limited remount requisition");
                    return Result("{=BC_CouncilIncident_Remounts_ResultAbstainSuccess}The limited requisition supplies the worst affected districts with enough remounts to hasten their patrol musters.");
                case CouncilIncidentQuality.Neutral:
                    return Result("{=BC_CouncilIncident_Remounts_ResultAbstainNeutral}The limited requisition produces little of note, and patrols continue to muster at their former pace.");
                default:
                    context.ApplyAspectMultiplier(
                        CouncilIncidentAspects.PatrolFormationSpeed,
                        RequisitionRemountsEventId,
                        0.75f,
                        BellumCivileConstants.CouncilIncidentLimitedTrialEffectDays);
                    context.ChangeOfficeControversy(5f, "failed limited remount requisition");
                    return Result("{=BC_CouncilIncident_Remounts_ResultAbstainFailure}Even the limited requisition provokes disputes and leaves the affected patrols struggling with poor mounts.");
            }
        }

        private static CouncilIncidentDefinition CreateBrigandsOnRoadEvent()
        {
            return new CouncilIncidentDefinition(
                BrigandsOnRoadEventId,
                "marshal_organize_patrols",
                PrivyCouncilOffice.Marshal,
                IncidentsCampaignBehaviour.IncidentType.FiefManagement,
                1f,
                context => new TextObject("{=BC_CouncilIncident_Brigands_Title}Brigands on the Road"),
                context =>
                {
                    TextObject description = new TextObject("{=BC_CouncilIncident_Brigands_Description}Reports of brigandage have multiplied along the realm's roads. Merchants accuse patrol captains of idleness, while village reeves complain that armed bands disperse before soldiers arrive. {MARSHAL_NAME}, your marshal, requests authority to organize a coordinated sweep using local guides and expanded powers of search. Success may restore confidence in the patrols, but failure would turn every fresh attack into a scandal.");
                    description.SetTextVariable("MARSHAL_NAME", context?.Holder?.Leader?.Name ?? context?.Holder?.Name ?? TextObject.GetEmpty());
                    return description;
                },
                BuildBrigandsOnRoadOptions);
        }

        private static List<CouncilIncidentOptionDefinition> BuildBrigandsOnRoadOptions(
            CouncilIncidentContext context)
        {
            return new List<CouncilIncidentOptionDefinition>
            {
                new CouncilIncidentOptionDefinition(
                    "{=BC_CouncilIncident_Brigands_Endorse}Authorize a general sweep against the brigands.",
                    BuildBrigandsOnRoadEndorseHints,
                    ApplyBrigandsOnRoadEndorse),
                new CouncilIncidentOptionDefinition(
                    "{=BC_CouncilIncident_Brigands_Oppose}Order the existing patrol captains to answer for the roads.",
                    BuildBrigandsOnRoadOpposeHints,
                    ApplyBrigandsOnRoadOppose),
                new CouncilIncidentOptionDefinition(
                    "{=BC_CouncilIncident_Brigands_Abstain}Permit limited sweeps along the worst affected roads.",
                    BuildBrigandsOnRoadAbstainHints,
                    ApplyBrigandsOnRoadAbstain)
            };
        }

        private static List<TextObject> BuildBrigandsOnRoadEndorseHints(CouncilIncidentContext context)
        {
            return new List<TextObject>
            {
                ChanceText("{=BC_CouncilIncident_Brigands_EndorseSuccess}{CHANCE}% chance: reduce the assignment's additional road-failure controversy by 75% for {DAYS} days and reduce Marshal controversy by 5.", context.SuccessChance, BellumCivileConstants.CouncilIncidentEndorsedEffectDays),
                ChanceText("{=BC_CouncilIncident_Brigands_EndorseNeutral}{CHANCE}% chance: reduce the assignment's additional road-failure controversy by 50% for {DAYS} days.", context.NeutralChance, BellumCivileConstants.CouncilIncidentEndorsedEffectDays),
                ChanceText("{=BC_CouncilIncident_Brigands_EndorseFailure}{CHANCE}% chance: double the assignment's additional road-failure controversy for {DAYS} days and increase Marshal controversy by 10.", context.FailureChance, BellumCivileConstants.CouncilIncidentEndorsedEffectDays),
                new TextObject("{=BC_CouncilIncident_Brigands_EndorseRelation}Gain 5 relation with your marshal.")
            };
        }

        private static List<TextObject> BuildBrigandsOnRoadOpposeHints(CouncilIncidentContext context)
        {
            return new List<TextObject>
            {
                new TextObject("{=BC_CouncilIncident_Brigands_OpposeHint}The assignment remains unchanged. Lose 10 relation with your marshal.")
            };
        }

        private static List<TextObject> BuildBrigandsOnRoadAbstainHints(CouncilIncidentContext context)
        {
            return new List<TextObject>
            {
                ChanceText("{=BC_CouncilIncident_Brigands_AbstainSuccess}{CHANCE}% chance: reduce the assignment's additional road-failure controversy by 50% for {DAYS} days and reduce Marshal controversy by 2.", context.SuccessChance, BellumCivileConstants.CouncilIncidentLimitedTrialEffectDays),
                ChanceText("{=BC_CouncilIncident_Brigands_AbstainNeutral}{CHANCE}% chance: the assignment continues unchanged.", context.NeutralChance),
                ChanceText("{=BC_CouncilIncident_Brigands_AbstainFailure}{CHANCE}% chance: increase the assignment's additional road-failure controversy by 50% for {DAYS} days and increase Marshal controversy by 5.", context.FailureChance, BellumCivileConstants.CouncilIncidentLimitedTrialEffectDays)
            };
        }

        private static List<TextObject> ApplyBrigandsOnRoadEndorse(CouncilIncidentContext context)
        {
            context.ChangeHolderRelation(5);
            switch (context.Quality)
            {
                case CouncilIncidentQuality.Success:
                    context.ApplyAspectMultiplier(
                        CouncilIncidentAspects.RoadFailureControversy,
                        BrigandsOnRoadEventId,
                        0.25f,
                        BellumCivileConstants.CouncilIncidentEndorsedEffectDays);
                    context.ChangeOfficeControversy(-5f, "successful suppression of road brigands");
                    return Result("{=BC_CouncilIncident_Brigands_ResultEndorseSuccess}Guided patrols break up several hideouts, reopen the roads, and silence the marshal's loudest critics.");
                case CouncilIncidentQuality.Neutral:
                    context.ApplyAspectMultiplier(
                        CouncilIncidentAspects.RoadFailureControversy,
                        BrigandsOnRoadEventId,
                        0.5f,
                        BellumCivileConstants.CouncilIncidentEndorsedEffectDays);
                    return Result("{=BC_CouncilIncident_Brigands_ResultEndorseNeutral}Some bands are scattered, but others simply move into neighboring districts. Scrutiny of the patrols eases for a time.");
                default:
                    context.ApplyAspectMultiplier(
                        CouncilIncidentAspects.RoadFailureControversy,
                        BrigandsOnRoadEventId,
                        2f,
                        BellumCivileConstants.CouncilIncidentEndorsedEffectDays);
                    context.ChangeOfficeControversy(10f, "failed suppression of road brigands");
                    return Result("{=BC_CouncilIncident_Brigands_ResultEndorseFailure}The patrols harass innocent villagers while the brigands escape, turning every fresh attack into a deeper scandal.");
            }
        }

        private static List<TextObject> ApplyBrigandsOnRoadOppose(CouncilIncidentContext context)
        {
            context.ChangeHolderRelation(-10);
            return Result("{=BC_CouncilIncident_Brigands_ResultOppose}You refuse the marshal's request and order the existing patrol captains to secure the roads. The marshal takes the rebuke personally.");
        }

        private static List<TextObject> ApplyBrigandsOnRoadAbstain(CouncilIncidentContext context)
        {
            switch (context.Quality)
            {
                case CouncilIncidentQuality.Success:
                    context.ApplyAspectMultiplier(
                        CouncilIncidentAspects.RoadFailureControversy,
                        BrigandsOnRoadEventId,
                        0.5f,
                        BellumCivileConstants.CouncilIncidentLimitedTrialEffectDays);
                    context.ChangeOfficeControversy(-2f, "successful limited sweep against road brigands");
                    return Result("{=BC_CouncilIncident_Brigands_ResultAbstainSuccess}The targeted sweeps break the worst bands and restore confidence along the most troubled roads.");
                case CouncilIncidentQuality.Neutral:
                    return Result("{=BC_CouncilIncident_Brigands_ResultAbstainNeutral}The limited sweeps displace a few brigands but leave the wider security of the roads unchanged.");
                default:
                    context.ApplyAspectMultiplier(
                        CouncilIncidentAspects.RoadFailureControversy,
                        BrigandsOnRoadEventId,
                        1.5f,
                        BellumCivileConstants.CouncilIncidentLimitedTrialEffectDays);
                    context.ChangeOfficeControversy(5f, "failed limited sweep against road brigands");
                    return Result("{=BC_CouncilIncident_Brigands_ResultAbstainFailure}The limited columns are misdirected and embarrassed, while accusations against the patrol service multiply.");
            }
        }

        private static CouncilIncidentDefinition CreateMusterRollsEvent()
        {
            return new CouncilIncidentDefinition(
                MusterRollsEventId,
                "marshal_train_militia",
                PrivyCouncilOffice.Marshal,
                IncidentsCampaignBehaviour.IncidentType.FiefManagement,
                1f,
                context => new TextObject("{=BC_CouncilIncident_MusterRolls_Title}The Muster Rolls"),
                context =>
                {
                    TextObject description = new TextObject("{=BC_CouncilIncident_MusterRolls_Description}{MARSHAL_NAME}, your marshal, has presented a plan to revise the realm's muster rolls. Every able household would be counted, village headmen made responsible for maintaining their quotas, and regular muster days established throughout the countryside. Accurate rolls could greatly strengthen the local watches, though corrupt officials and disputed exemptions may undermine the effort.");
                    description.SetTextVariable("MARSHAL_NAME", context?.Holder?.Leader?.Name ?? context?.Holder?.Name ?? TextObject.GetEmpty());
                    return description;
                },
                BuildMusterRollsOptions);
        }

        private static List<CouncilIncidentOptionDefinition> BuildMusterRollsOptions(
            CouncilIncidentContext context)
        {
            return new List<CouncilIncidentOptionDefinition>
            {
                new CouncilIncidentOptionDefinition(
                    "{=BC_CouncilIncident_MusterRolls_Endorse}Order a realm-wide census and levy review.",
                    BuildMusterRollsEndorseHints,
                    ApplyMusterRollsEndorse),
                new CouncilIncidentOptionDefinition(
                    "{=BC_CouncilIncident_MusterRolls_Oppose}Leave the muster rolls in the hands of local authorities.",
                    BuildMusterRollsOpposeHints,
                    ApplyMusterRollsOppose),
                new CouncilIncidentOptionDefinition(
                    "{=BC_CouncilIncident_MusterRolls_Abstain}Test the revised rolls in selected frontier districts.",
                    BuildMusterRollsAbstainHints,
                    ApplyMusterRollsAbstain)
            };
        }

        private static List<TextObject> BuildMusterRollsEndorseHints(CouncilIncidentContext context)
        {
            return new List<TextObject>
            {
                ChanceText("{=BC_CouncilIncident_MusterRolls_EndorseSuccess}{CHANCE}% chance: double the militia-growth effect for {DAYS} days and reduce Marshal controversy by 5.", context.SuccessChance, BellumCivileConstants.CouncilIncidentEndorsedEffectDays),
                ChanceText("{=BC_CouncilIncident_MusterRolls_EndorseNeutral}{CHANCE}% chance: strengthen the militia-growth effect by 50% for {DAYS} days.", context.NeutralChance, BellumCivileConstants.CouncilIncidentEndorsedEffectDays),
                ChanceText("{=BC_CouncilIncident_MusterRolls_EndorseFailure}{CHANCE}% chance: halve the militia-growth effect for {DAYS} days and increase Marshal controversy by 10.", context.FailureChance, BellumCivileConstants.CouncilIncidentEndorsedEffectDays),
                new TextObject("{=BC_CouncilIncident_MusterRolls_EndorseRelation}Gain 5 relation with your marshal.")
            };
        }

        private static List<TextObject> BuildMusterRollsOpposeHints(CouncilIncidentContext context)
        {
            return new List<TextObject>
            {
                new TextObject("{=BC_CouncilIncident_MusterRolls_OpposeHint}The assignment remains unchanged. Lose 10 relation with your marshal.")
            };
        }

        private static List<TextObject> BuildMusterRollsAbstainHints(CouncilIncidentContext context)
        {
            return new List<TextObject>
            {
                ChanceText("{=BC_CouncilIncident_MusterRolls_AbstainSuccess}{CHANCE}% chance: strengthen the militia-growth effect by 50% for {DAYS} days and reduce Marshal controversy by 2.", context.SuccessChance, BellumCivileConstants.CouncilIncidentLimitedTrialEffectDays),
                ChanceText("{=BC_CouncilIncident_MusterRolls_AbstainNeutral}{CHANCE}% chance: the assignment continues unchanged.", context.NeutralChance),
                ChanceText("{=BC_CouncilIncident_MusterRolls_AbstainFailure}{CHANCE}% chance: weaken the militia-growth effect by 25% for {DAYS} days and increase Marshal controversy by 5.", context.FailureChance, BellumCivileConstants.CouncilIncidentLimitedTrialEffectDays)
            };
        }

        private static List<TextObject> ApplyMusterRollsEndorse(CouncilIncidentContext context)
        {
            context.ChangeHolderRelation(5);
            switch (context.Quality)
            {
                case CouncilIncidentQuality.Success:
                    context.ApplyAspectMultiplier(
                        CouncilIncidentAspects.MilitiaGrowth,
                        MusterRollsEventId,
                        2f,
                        BellumCivileConstants.CouncilIncidentEndorsedEffectDays);
                    context.ChangeOfficeControversy(-5f, "successful revision of the muster rolls");
                    return Result("{=BC_CouncilIncident_MusterRolls_ResultEndorseSuccess}The revised rolls expose neglected households and bring a strong influx of recruits to village musters.");
                case CouncilIncidentQuality.Neutral:
                    context.ApplyAspectMultiplier(
                        CouncilIncidentAspects.MilitiaGrowth,
                        MusterRollsEventId,
                        1.5f,
                        BellumCivileConstants.CouncilIncidentEndorsedEffectDays);
                    return Result("{=BC_CouncilIncident_MusterRolls_ResultEndorseNeutral}Implementation varies by district, but the new rolls modestly strengthen militia recruitment.");
                default:
                    context.ApplyAspectMultiplier(
                        CouncilIncidentAspects.MilitiaGrowth,
                        MusterRollsEventId,
                        0.5f,
                        BellumCivileConstants.CouncilIncidentEndorsedEffectDays);
                    context.ChangeOfficeControversy(10f, "failed revision of the muster rolls");
                    return Result("{=BC_CouncilIncident_MusterRolls_ResultEndorseFailure}Headmen falsify records, exemptions are sold, and resentment disrupts militia recruitment throughout the realm.");
            }
        }

        private static List<TextObject> ApplyMusterRollsOppose(CouncilIncidentContext context)
        {
            context.ChangeHolderRelation(-10);
            return Result("{=BC_CouncilIncident_MusterRolls_ResultOppose}You leave the muster rolls under local authority. The marshal obeys, but considers the old rolls unreliable.");
        }

        private static List<TextObject> ApplyMusterRollsAbstain(CouncilIncidentContext context)
        {
            switch (context.Quality)
            {
                case CouncilIncidentQuality.Success:
                    context.ApplyAspectMultiplier(
                        CouncilIncidentAspects.MilitiaGrowth,
                        MusterRollsEventId,
                        1.5f,
                        BellumCivileConstants.CouncilIncidentLimitedTrialEffectDays);
                    context.ChangeOfficeControversy(-2f, "successful trial of revised muster rolls");
                    return Result("{=BC_CouncilIncident_MusterRolls_ResultAbstainSuccess}The frontier trial uncovers neglected households and provides the local watches with fresh recruits.");
                case CouncilIncidentQuality.Neutral:
                    return Result("{=BC_CouncilIncident_MusterRolls_ResultAbstainNeutral}The trial reveals little of consequence, and militia recruitment continues as before.");
                default:
                    context.ApplyAspectMultiplier(
                        CouncilIncidentAspects.MilitiaGrowth,
                        MusterRollsEventId,
                        0.75f,
                        BellumCivileConstants.CouncilIncidentLimitedTrialEffectDays);
                    context.ChangeOfficeControversy(5f, "failed trial of revised muster rolls");
                    return Result("{=BC_CouncilIncident_MusterRolls_ResultAbstainFailure}The frontier rolls provoke disputes over exemptions and briefly impede militia recruitment.");
            }
        }

        private static CouncilIncidentDefinition CreateLiveSteelSparringEvent()
        {
            return new CouncilIncidentDefinition(
                LiveSteelSparringEventId,
                "marshal_train_militia",
                PrivyCouncilOffice.Marshal,
                IncidentsCampaignBehaviour.IncidentType.FiefManagement,
                1f,
                context => new TextObject("{=BC_CouncilIncident_LiveSteel_Title}Live-Steel Sparring"),
                context =>
                {
                    TextObject description = new TextObject("{=BC_CouncilIncident_LiveSteel_Description}{MARSHAL_NAME}, your marshal, argues that villagers trained only with blunted staves and wooden shields will falter when they first meet real steel. The marshal proposes placing selected militia companies under seasoned soldiers and allowing them to drill with sharpened weapons. Those who endure the exercises may become formidable veterans, but an ill-managed program could produce injuries, fear, and desertion.");
                    description.SetTextVariable("MARSHAL_NAME", context?.Holder?.Leader?.Name ?? context?.Holder?.Name ?? TextObject.GetEmpty());
                    return description;
                },
                BuildLiveSteelSparringOptions);
        }

        private static List<CouncilIncidentOptionDefinition> BuildLiveSteelSparringOptions(
            CouncilIncidentContext context)
        {
            return new List<CouncilIncidentOptionDefinition>
            {
                new CouncilIncidentOptionDefinition(
                    "{=BC_CouncilIncident_LiveSteel_Endorse}Authorize live-steel exercises throughout the realm.",
                    BuildLiveSteelSparringEndorseHints,
                    ApplyLiveSteelSparringEndorse),
                new CouncilIncidentOptionDefinition(
                    "{=BC_CouncilIncident_LiveSteel_Oppose}Forbid the reckless use of sharpened weapons.",
                    BuildLiveSteelSparringOpposeHints,
                    ApplyLiveSteelSparringOppose),
                new CouncilIncidentOptionDefinition(
                    "{=BC_CouncilIncident_LiveSteel_Abstain}Permit supervised trials in selected strongholds.",
                    BuildLiveSteelSparringAbstainHints,
                    ApplyLiveSteelSparringAbstain)
            };
        }

        private static List<TextObject> BuildLiveSteelSparringEndorseHints(CouncilIncidentContext context)
        {
            return new List<TextObject>
            {
                ChanceText("{=BC_CouncilIncident_LiveSteel_EndorseSuccess}{CHANCE}% chance: double the veteran militia effect for {DAYS} days and reduce Marshal controversy by 5.", context.SuccessChance, BellumCivileConstants.CouncilIncidentEndorsedEffectDays),
                ChanceText("{=BC_CouncilIncident_LiveSteel_EndorseNeutral}{CHANCE}% chance: strengthen the veteran militia effect by 50% for {DAYS} days.", context.NeutralChance, BellumCivileConstants.CouncilIncidentEndorsedEffectDays),
                ChanceText("{=BC_CouncilIncident_LiveSteel_EndorseFailure}{CHANCE}% chance: halve the veteran militia effect for {DAYS} days and increase Marshal controversy by 10.", context.FailureChance, BellumCivileConstants.CouncilIncidentEndorsedEffectDays),
                new TextObject("{=BC_CouncilIncident_LiveSteel_EndorseRelation}Gain 5 relation with your marshal.")
            };
        }

        private static List<TextObject> BuildLiveSteelSparringOpposeHints(CouncilIncidentContext context)
        {
            return new List<TextObject>
            {
                new TextObject("{=BC_CouncilIncident_LiveSteel_OpposeHint}The assignment remains unchanged. Lose 10 relation with your marshal.")
            };
        }

        private static List<TextObject> BuildLiveSteelSparringAbstainHints(CouncilIncidentContext context)
        {
            return new List<TextObject>
            {
                ChanceText("{=BC_CouncilIncident_LiveSteel_AbstainSuccess}{CHANCE}% chance: strengthen the veteran militia effect by 50% for {DAYS} days and reduce Marshal controversy by 2.", context.SuccessChance, BellumCivileConstants.CouncilIncidentLimitedTrialEffectDays),
                ChanceText("{=BC_CouncilIncident_LiveSteel_AbstainNeutral}{CHANCE}% chance: the assignment continues unchanged.", context.NeutralChance),
                ChanceText("{=BC_CouncilIncident_LiveSteel_AbstainFailure}{CHANCE}% chance: weaken the veteran militia effect by 25% for {DAYS} days and increase Marshal controversy by 5.", context.FailureChance, BellumCivileConstants.CouncilIncidentLimitedTrialEffectDays)
            };
        }

        private static List<TextObject> ApplyLiveSteelSparringEndorse(CouncilIncidentContext context)
        {
            context.ChangeHolderRelation(5);
            switch (context.Quality)
            {
                case CouncilIncidentQuality.Success:
                    context.ApplyAspectMultiplier(
                        CouncilIncidentAspects.VeteranMilitiaChance,
                        LiveSteelSparringEventId,
                        2f,
                        BellumCivileConstants.CouncilIncidentEndorsedEffectDays);
                    context.ChangeOfficeControversy(-5f, "successful live-steel militia exercises");
                    return Result("{=BC_CouncilIncident_LiveSteel_ResultEndorseSuccess}Seasoned soldiers impose firm discipline, producing an exceptional generation of militia veterans.");
                case CouncilIncidentQuality.Neutral:
                    context.ApplyAspectMultiplier(
                        CouncilIncidentAspects.VeteranMilitiaChance,
                        LiveSteelSparringEventId,
                        1.5f,
                        BellumCivileConstants.CouncilIncidentEndorsedEffectDays);
                    return Result("{=BC_CouncilIncident_LiveSteel_ResultEndorseNeutral}The exercises improve battlefield readiness, though injuries limit how widely the program can be applied.");
                default:
                    context.ApplyAspectMultiplier(
                        CouncilIncidentAspects.VeteranMilitiaChance,
                        LiveSteelSparringEventId,
                        0.5f,
                        BellumCivileConstants.CouncilIncidentEndorsedEffectDays);
                    context.ChangeOfficeControversy(10f, "failed live-steel militia exercises");
                    return Result("{=BC_CouncilIncident_LiveSteel_ResultEndorseFailure}Reckless instructors cause bloodshed and frighten recruits away from advanced training.");
            }
        }

        private static List<TextObject> ApplyLiveSteelSparringOppose(CouncilIncidentContext context)
        {
            context.ChangeHolderRelation(-10);
            return Result("{=BC_CouncilIncident_LiveSteel_ResultOppose}You forbid the use of sharpened weapons in militia drills. The marshal obeys, but calls the decision timid.");
        }

        private static List<TextObject> ApplyLiveSteelSparringAbstain(CouncilIncidentContext context)
        {
            switch (context.Quality)
            {
                case CouncilIncidentQuality.Success:
                    context.ApplyAspectMultiplier(
                        CouncilIncidentAspects.VeteranMilitiaChance,
                        LiveSteelSparringEventId,
                        1.5f,
                        BellumCivileConstants.CouncilIncidentLimitedTrialEffectDays);
                    context.ChangeOfficeControversy(-2f, "successful supervised live-steel exercises");
                    return Result("{=BC_CouncilIncident_LiveSteel_ResultAbstainSuccess}Careful supervision makes the limited exercises both safe and effective.");
                case CouncilIncidentQuality.Neutral:
                    return Result("{=BC_CouncilIncident_LiveSteel_ResultAbstainNeutral}The trials produce a handful of capable veterans but change little across the wider realm.");
                default:
                    context.ApplyAspectMultiplier(
                        CouncilIncidentAspects.VeteranMilitiaChance,
                        LiveSteelSparringEventId,
                        0.75f,
                        BellumCivileConstants.CouncilIncidentLimitedTrialEffectDays);
                    context.ChangeOfficeControversy(5f, "failed supervised live-steel exercises");
                    return Result("{=BC_CouncilIncident_LiveSteel_ResultAbstainFailure}Poor supervision causes injuries and discourages militia from seeking advanced training.");
            }
        }

        private static CouncilIncidentDefinition CreateHarvestExemptionEvent()
        {
            return new CouncilIncidentDefinition(
                HarvestExemptionEventId,
                "marshal_train_militia",
                PrivyCouncilOffice.Marshal,
                IncidentsCampaignBehaviour.IncidentType.FiefManagement,
                1f,
                context => new TextObject("{=BC_CouncilIncident_HarvestExemption_Title}The Harvest Exemption"),
                context =>
                {
                    TextObject description = new TextObject("{=BC_CouncilIncident_HarvestExemption_Description}Village reeves warn that seasonal musters are drawing laborers away from the fields when they are needed most. {MARSHAL_NAME}, your marshal, proposes rotating harvest exemptions so farmers may complete their work without abandoning militia service entirely. Careful scheduling could preserve both harvest and watch, but confused exemptions may encourage idleness and evasion.");
                    description.SetTextVariable("MARSHAL_NAME", context?.Holder?.Leader?.Name ?? context?.Holder?.Name ?? TextObject.GetEmpty());
                    return description;
                },
                BuildHarvestExemptionOptions);
        }

        private static List<CouncilIncidentOptionDefinition> BuildHarvestExemptionOptions(
            CouncilIncidentContext context)
        {
            return new List<CouncilIncidentOptionDefinition>
            {
                new CouncilIncidentOptionDefinition(
                    "{=BC_CouncilIncident_HarvestExemption_Endorse}Grant carefully scheduled harvest exemptions.",
                    BuildHarvestExemptionEndorseHints,
                    ApplyHarvestExemptionEndorse),
                new CouncilIncidentOptionDefinition(
                    "{=BC_CouncilIncident_HarvestExemption_Oppose}Declare that the watch must take precedence over the harvest.",
                    BuildHarvestExemptionOpposeHints,
                    ApplyHarvestExemptionOppose),
                new CouncilIncidentOptionDefinition(
                    "{=BC_CouncilIncident_HarvestExemption_Abstain}Permit exemptions only in the hardest-pressed villages.",
                    BuildHarvestExemptionAbstainHints,
                    ApplyHarvestExemptionAbstain)
            };
        }

        private static List<TextObject> BuildHarvestExemptionEndorseHints(CouncilIncidentContext context)
        {
            return new List<TextObject>
            {
                ChanceText("{=BC_CouncilIncident_HarvestExemption_EndorseSuccess}{CHANCE}% chance: reduce the assignment's village-production penalty by 75% for {DAYS} days and reduce Marshal controversy by 5.", context.SuccessChance, BellumCivileConstants.CouncilIncidentEndorsedEffectDays),
                ChanceText("{=BC_CouncilIncident_HarvestExemption_EndorseNeutral}{CHANCE}% chance: reduce the assignment's village-production penalty by 50% for {DAYS} days.", context.NeutralChance, BellumCivileConstants.CouncilIncidentEndorsedEffectDays),
                ChanceText("{=BC_CouncilIncident_HarvestExemption_EndorseFailure}{CHANCE}% chance: double the assignment's village-production penalty for {DAYS} days and increase Marshal controversy by 10.", context.FailureChance, BellumCivileConstants.CouncilIncidentEndorsedEffectDays),
                new TextObject("{=BC_CouncilIncident_HarvestExemption_EndorseRelation}Gain 5 relation with your marshal.")
            };
        }

        private static List<TextObject> BuildHarvestExemptionOpposeHints(CouncilIncidentContext context)
        {
            return new List<TextObject>
            {
                new TextObject("{=BC_CouncilIncident_HarvestExemption_OpposeHint}The assignment remains unchanged. Lose 10 relation with your marshal.")
            };
        }

        private static List<TextObject> BuildHarvestExemptionAbstainHints(CouncilIncidentContext context)
        {
            return new List<TextObject>
            {
                ChanceText("{=BC_CouncilIncident_HarvestExemption_AbstainSuccess}{CHANCE}% chance: reduce the assignment's village-production penalty by 50% for {DAYS} days and reduce Marshal controversy by 2.", context.SuccessChance, BellumCivileConstants.CouncilIncidentLimitedTrialEffectDays),
                ChanceText("{=BC_CouncilIncident_HarvestExemption_AbstainNeutral}{CHANCE}% chance: the assignment continues unchanged.", context.NeutralChance),
                ChanceText("{=BC_CouncilIncident_HarvestExemption_AbstainFailure}{CHANCE}% chance: increase the assignment's village-production penalty by 50% for {DAYS} days and increase Marshal controversy by 5.", context.FailureChance, BellumCivileConstants.CouncilIncidentLimitedTrialEffectDays)
            };
        }

        private static List<TextObject> ApplyHarvestExemptionEndorse(CouncilIncidentContext context)
        {
            context.ChangeHolderRelation(5);
            switch (context.Quality)
            {
                case CouncilIncidentQuality.Success:
                    context.ApplyAspectMultiplier(
                        CouncilIncidentAspects.VillageProductionPenalty,
                        HarvestExemptionEventId,
                        0.25f,
                        BellumCivileConstants.CouncilIncidentEndorsedEffectDays);
                    context.ChangeOfficeControversy(-5f, "successful harvest exemptions");
                    return Result("{=BC_CouncilIncident_HarvestExemption_ResultEndorseSuccess}Carefully rotated exemptions preserve the harvest without allowing the village watches to fall into neglect.");
                case CouncilIncidentQuality.Neutral:
                    context.ApplyAspectMultiplier(
                        CouncilIncidentAspects.VillageProductionPenalty,
                        HarvestExemptionEventId,
                        0.5f,
                        BellumCivileConstants.CouncilIncidentEndorsedEffectDays);
                    return Result("{=BC_CouncilIncident_HarvestExemption_ResultEndorseNeutral}The exemptions ease the burden on the fields, though militia duties still disrupt parts of the harvest.");
                default:
                    context.ApplyAspectMultiplier(
                        CouncilIncidentAspects.VillageProductionPenalty,
                        HarvestExemptionEventId,
                        2f,
                        BellumCivileConstants.CouncilIncidentEndorsedEffectDays);
                    context.ChangeOfficeControversy(10f, "failed harvest exemptions");
                    return Result("{=BC_CouncilIncident_HarvestExemption_ResultEndorseFailure}Confused orders, fraudulent exemptions, and delayed musters leave both the fields and village watches in disorder.");
            }
        }

        private static List<TextObject> ApplyHarvestExemptionOppose(CouncilIncidentContext context)
        {
            context.ChangeHolderRelation(-10);
            return Result("{=BC_CouncilIncident_HarvestExemption_ResultOppose}You order the village musters to continue without exemption. The marshal complies, but resents the rejection of the proposed schedule.");
        }

        private static List<TextObject> ApplyHarvestExemptionAbstain(CouncilIncidentContext context)
        {
            switch (context.Quality)
            {
                case CouncilIncidentQuality.Success:
                    context.ApplyAspectMultiplier(
                        CouncilIncidentAspects.VillageProductionPenalty,
                        HarvestExemptionEventId,
                        0.5f,
                        BellumCivileConstants.CouncilIncidentLimitedTrialEffectDays);
                    context.ChangeOfficeControversy(-2f, "successful limited harvest exemptions");
                    return Result("{=BC_CouncilIncident_HarvestExemption_ResultAbstainSuccess}Targeted exemptions relieve the hardest-pressed villages without seriously weakening their watches.");
                case CouncilIncidentQuality.Neutral:
                    return Result("{=BC_CouncilIncident_HarvestExemption_ResultAbstainNeutral}The limited exemptions provide little relief, but cause no wider disruption.");
                default:
                    context.ApplyAspectMultiplier(
                        CouncilIncidentAspects.VillageProductionPenalty,
                        HarvestExemptionEventId,
                        1.5f,
                        BellumCivileConstants.CouncilIncidentLimitedTrialEffectDays);
                    context.ChangeOfficeControversy(5f, "failed limited harvest exemptions");
                    return Result("{=BC_CouncilIncident_HarvestExemption_ResultAbstainFailure}Poorly chosen exemptions disrupt the harvest while leaving other villages short of militia hands.");
            }
        }

        private static CouncilIncidentDefinition CreateCampFollowersEvent()
        {
            return new CouncilIncidentDefinition(
                CampFollowersEventId,
                "marshal_oversee_logistics",
                PrivyCouncilOffice.Marshal,
                IncidentsCampaignBehaviour.IncidentType.FiefManagement,
                1f,
                context => new TextObject("{=BC_CouncilIncident_CampFollowers_Title}Camp Followers"),
                context =>
                {
                    TextObject description = new TextObject("{=BC_CouncilIncident_CampFollowers_Description}{MARSHAL_NAME}, your marshal, proposes formally licensing the camp followers who accompany the realm's armies: sutlers, smiths, healers, laundresses, and servants. Properly organized, their services could keep soldiers supplied, healthy, and close to their banners during long campaigns. Left undisciplined, however, the enlarged camp train may clog the roads and encourage disorder.");
                    description.SetTextVariable("MARSHAL_NAME", context?.Holder?.Leader?.Name ?? context?.Holder?.Name ?? TextObject.GetEmpty());
                    return description;
                },
                BuildCampFollowersOptions);
        }

        private static List<CouncilIncidentOptionDefinition> BuildCampFollowersOptions(
            CouncilIncidentContext context)
        {
            return new List<CouncilIncidentOptionDefinition>
            {
                new CouncilIncidentOptionDefinition(
                    "{=BC_CouncilIncident_CampFollowers_Endorse}Establish a regulated camp train for every royal army.",
                    BuildCampFollowersEndorseHints,
                    ApplyCampFollowersEndorse),
                new CouncilIncidentOptionDefinition(
                    "{=BC_CouncilIncident_CampFollowers_Oppose}Forbid the army from burdening itself with an enlarged camp train.",
                    BuildCampFollowersOpposeHints,
                    ApplyCampFollowersOppose),
                new CouncilIncidentOptionDefinition(
                    "{=BC_CouncilIncident_CampFollowers_Abstain}License camp followers only for the largest armies.",
                    BuildCampFollowersAbstainHints,
                    ApplyCampFollowersAbstain)
            };
        }

        private static List<TextObject> BuildCampFollowersEndorseHints(CouncilIncidentContext context)
        {
            return new List<TextObject>
            {
                ChanceText("{=BC_CouncilIncident_CampFollowers_EndorseSuccess}{CHANCE}% chance: double the army cohesion-preservation effect for {DAYS} days and reduce Marshal controversy by 5.", context.SuccessChance, BellumCivileConstants.CouncilIncidentEndorsedEffectDays),
                ChanceText("{=BC_CouncilIncident_CampFollowers_EndorseNeutral}{CHANCE}% chance: strengthen the army cohesion-preservation effect by 50% for {DAYS} days.", context.NeutralChance, BellumCivileConstants.CouncilIncidentEndorsedEffectDays),
                ChanceText("{=BC_CouncilIncident_CampFollowers_EndorseFailure}{CHANCE}% chance: halve the army cohesion-preservation effect for {DAYS} days and increase Marshal controversy by 10.", context.FailureChance, BellumCivileConstants.CouncilIncidentEndorsedEffectDays),
                new TextObject("{=BC_CouncilIncident_CampFollowers_EndorseRelation}Gain 5 relation with your marshal.")
            };
        }

        private static List<TextObject> BuildCampFollowersOpposeHints(CouncilIncidentContext context)
        {
            return new List<TextObject>
            {
                new TextObject("{=BC_CouncilIncident_CampFollowers_OpposeHint}The assignment remains unchanged. Lose 10 relation with your marshal.")
            };
        }

        private static List<TextObject> BuildCampFollowersAbstainHints(CouncilIncidentContext context)
        {
            return new List<TextObject>
            {
                ChanceText("{=BC_CouncilIncident_CampFollowers_AbstainSuccess}{CHANCE}% chance: strengthen the army cohesion-preservation effect by 50% for {DAYS} days and reduce Marshal controversy by 2.", context.SuccessChance, BellumCivileConstants.CouncilIncidentLimitedTrialEffectDays),
                ChanceText("{=BC_CouncilIncident_CampFollowers_AbstainNeutral}{CHANCE}% chance: the assignment continues unchanged.", context.NeutralChance),
                ChanceText("{=BC_CouncilIncident_CampFollowers_AbstainFailure}{CHANCE}% chance: weaken the army cohesion-preservation effect by 25% for {DAYS} days and increase Marshal controversy by 5.", context.FailureChance, BellumCivileConstants.CouncilIncidentLimitedTrialEffectDays)
            };
        }

        private static List<TextObject> ApplyCampFollowersEndorse(CouncilIncidentContext context)
        {
            context.ChangeHolderRelation(5);
            switch (context.Quality)
            {
                case CouncilIncidentQuality.Success:
                    context.ApplyAspectMultiplier(
                        CouncilIncidentAspects.ArmyCohesionRetention,
                        CampFollowersEventId,
                        2f,
                        BellumCivileConstants.CouncilIncidentEndorsedEffectDays);
                    context.ChangeOfficeControversy(-5f, "successful regulation of camp followers");
                    return Result("{=BC_CouncilIncident_CampFollowers_ResultEndorseSuccess}Licensed followers keep equipment repaired, wounds treated, and stragglers close to their banners.");
                case CouncilIncidentQuality.Neutral:
                    context.ApplyAspectMultiplier(
                        CouncilIncidentAspects.ArmyCohesionRetention,
                        CampFollowersEventId,
                        1.5f,
                        BellumCivileConstants.CouncilIncidentEndorsedEffectDays);
                    return Result("{=BC_CouncilIncident_CampFollowers_ResultEndorseNeutral}The regulated camp train provides useful services, though its size remains cumbersome on the march.");
                default:
                    context.ApplyAspectMultiplier(
                        CouncilIncidentAspects.ArmyCohesionRetention,
                        CampFollowersEventId,
                        0.5f,
                        BellumCivileConstants.CouncilIncidentEndorsedEffectDays);
                    context.ChangeOfficeControversy(10f, "failed regulation of camp followers");
                    return Result("{=BC_CouncilIncident_CampFollowers_ResultEndorseFailure}Crowded roads, disorder, and quarrels within the camp cause soldiers to stray from their formations.");
            }
        }

        private static List<TextObject> ApplyCampFollowersOppose(CouncilIncidentContext context)
        {
            context.ChangeHolderRelation(-10);
            return Result("{=BC_CouncilIncident_CampFollowers_ResultOppose}You refuse to enlarge the army's camp train. The marshal obeys, but considers the decision short-sighted.");
        }

        private static List<TextObject> ApplyCampFollowersAbstain(CouncilIncidentContext context)
        {
            switch (context.Quality)
            {
                case CouncilIncidentQuality.Success:
                    context.ApplyAspectMultiplier(
                        CouncilIncidentAspects.ArmyCohesionRetention,
                        CampFollowersEventId,
                        1.5f,
                        BellumCivileConstants.CouncilIncidentLimitedTrialEffectDays);
                    context.ChangeOfficeControversy(-2f, "successful limited regulation of camp followers");
                    return Result("{=BC_CouncilIncident_CampFollowers_ResultAbstainSuccess}The licensed followers serve the largest armies well and help keep their soldiers together.");
                case CouncilIncidentQuality.Neutral:
                    return Result("{=BC_CouncilIncident_CampFollowers_ResultAbstainNeutral}The limited licenses provide some useful services but change little in the wider army.");
                default:
                    context.ApplyAspectMultiplier(
                        CouncilIncidentAspects.ArmyCohesionRetention,
                        CampFollowersEventId,
                        0.75f,
                        BellumCivileConstants.CouncilIncidentLimitedTrialEffectDays);
                    context.ChangeOfficeControversy(5f, "failed limited regulation of camp followers");
                    return Result("{=BC_CouncilIncident_CampFollowers_ResultAbstainFailure}Even the restricted camp trains become disordered and encourage straggling for a time.");
            }
        }

        private static CouncilIncidentDefinition CreateForagingRightsEvent()
        {
            return new CouncilIncidentDefinition(
                ForagingRightsEventId,
                "marshal_oversee_logistics",
                PrivyCouncilOffice.Marshal,
                IncidentsCampaignBehaviour.IncidentType.FiefManagement,
                1f,
                context => new TextObject("{=BC_CouncilIncident_ForagingRights_Title}Foraging Rights"),
                context =>
                {
                    TextObject description = new TextObject("{=BC_CouncilIncident_ForagingRights_Description}{MARSHAL_NAME}, your marshal, requests that soldiers marching under an army banner be granted limited rights to forage from the surrounding countryside. Supplementing issued rations with locally gathered provisions could greatly extend the army's stores. Poor discipline, however, may turn sanctioned foraging into theft and leave both villages and supply officers uncertain about what is owed.");
                    description.SetTextVariable("MARSHAL_NAME", context?.Holder?.Leader?.Name ?? context?.Holder?.Name ?? TextObject.GetEmpty());
                    return description;
                },
                BuildForagingRightsOptions);
        }

        private static List<CouncilIncidentOptionDefinition> BuildForagingRightsOptions(
            CouncilIncidentContext context)
        {
            return new List<CouncilIncidentOptionDefinition>
            {
                new CouncilIncidentOptionDefinition(
                    "{=BC_CouncilIncident_ForagingRights_Endorse}Grant regulated foraging rights to the armies.",
                    BuildForagingRightsEndorseHints,
                    ApplyForagingRightsEndorse),
                new CouncilIncidentOptionDefinition(
                    "{=BC_CouncilIncident_ForagingRights_Oppose}Require every army to rely upon properly purchased provisions.",
                    BuildForagingRightsOpposeHints,
                    ApplyForagingRightsOppose),
                new CouncilIncidentOptionDefinition(
                    "{=BC_CouncilIncident_ForagingRights_Abstain}Permit limited foraging in sparsely settled or hostile territory.",
                    BuildForagingRightsAbstainHints,
                    ApplyForagingRightsAbstain)
            };
        }

        private static List<TextObject> BuildForagingRightsEndorseHints(CouncilIncidentContext context)
        {
            return new List<TextObject>
            {
                ChanceText("{=BC_CouncilIncident_ForagingRights_EndorseSuccess}{CHANCE}% chance: double the army food-efficiency effect for {DAYS} days and reduce Marshal controversy by 5.", context.SuccessChance, BellumCivileConstants.CouncilIncidentEndorsedEffectDays),
                ChanceText("{=BC_CouncilIncident_ForagingRights_EndorseNeutral}{CHANCE}% chance: strengthen the army food-efficiency effect by 50% for {DAYS} days.", context.NeutralChance, BellumCivileConstants.CouncilIncidentEndorsedEffectDays),
                ChanceText("{=BC_CouncilIncident_ForagingRights_EndorseFailure}{CHANCE}% chance: halve the army food-efficiency effect for {DAYS} days and increase Marshal controversy by 10.", context.FailureChance, BellumCivileConstants.CouncilIncidentEndorsedEffectDays),
                new TextObject("{=BC_CouncilIncident_ForagingRights_EndorseRelation}Gain 5 relation with your marshal.")
            };
        }

        private static List<TextObject> BuildForagingRightsOpposeHints(CouncilIncidentContext context)
        {
            return new List<TextObject>
            {
                new TextObject("{=BC_CouncilIncident_ForagingRights_OpposeHint}The assignment remains unchanged. Lose 10 relation with your marshal.")
            };
        }

        private static List<TextObject> BuildForagingRightsAbstainHints(CouncilIncidentContext context)
        {
            return new List<TextObject>
            {
                ChanceText("{=BC_CouncilIncident_ForagingRights_AbstainSuccess}{CHANCE}% chance: strengthen the army food-efficiency effect by 50% for {DAYS} days and reduce Marshal controversy by 2.", context.SuccessChance, BellumCivileConstants.CouncilIncidentLimitedTrialEffectDays),
                ChanceText("{=BC_CouncilIncident_ForagingRights_AbstainNeutral}{CHANCE}% chance: the assignment continues unchanged.", context.NeutralChance),
                ChanceText("{=BC_CouncilIncident_ForagingRights_AbstainFailure}{CHANCE}% chance: weaken the army food-efficiency effect by 25% for {DAYS} days and increase Marshal controversy by 5.", context.FailureChance, BellumCivileConstants.CouncilIncidentLimitedTrialEffectDays)
            };
        }

        private static List<TextObject> ApplyForagingRightsEndorse(CouncilIncidentContext context)
        {
            context.ChangeHolderRelation(5);
            switch (context.Quality)
            {
                case CouncilIncidentQuality.Success:
                    context.ApplyAspectMultiplier(
                        CouncilIncidentAspects.ArmyFoodEfficiency,
                        ForagingRightsEventId,
                        2f,
                        BellumCivileConstants.CouncilIncidentEndorsedEffectDays);
                    context.ChangeOfficeControversy(-5f, "successful regulation of army foraging");
                    return Result("{=BC_CouncilIncident_ForagingRights_ResultEndorseSuccess}Organized foraging supplements army stores without seriously disrupting the surrounding countryside.");
                case CouncilIncidentQuality.Neutral:
                    context.ApplyAspectMultiplier(
                        CouncilIncidentAspects.ArmyFoodEfficiency,
                        ForagingRightsEventId,
                        1.5f,
                        BellumCivileConstants.CouncilIncidentEndorsedEffectDays);
                    return Result("{=BC_CouncilIncident_ForagingRights_ResultEndorseNeutral}Soldiers find some provisions, though uneven enforcement limits the benefit to the armies.");
                default:
                    context.ApplyAspectMultiplier(
                        CouncilIncidentAspects.ArmyFoodEfficiency,
                        ForagingRightsEventId,
                        0.5f,
                        BellumCivileConstants.CouncilIncidentEndorsedEffectDays);
                    context.ChangeOfficeControversy(10f, "failed regulation of army foraging");
                    return Result("{=BC_CouncilIncident_ForagingRights_ResultEndorseFailure}Undisciplined troops strip nearby lands while supply officers lose control of distribution.");
            }
        }

        private static List<TextObject> ApplyForagingRightsOppose(CouncilIncidentContext context)
        {
            context.ChangeHolderRelation(-10);
            return Result("{=BC_CouncilIncident_ForagingRights_ResultOppose}You require the armies to rely upon purchased provisions. The marshal complies, but resents the restriction.");
        }

        private static List<TextObject> ApplyForagingRightsAbstain(CouncilIncidentContext context)
        {
            switch (context.Quality)
            {
                case CouncilIncidentQuality.Success:
                    context.ApplyAspectMultiplier(
                        CouncilIncidentAspects.ArmyFoodEfficiency,
                        ForagingRightsEventId,
                        1.5f,
                        BellumCivileConstants.CouncilIncidentLimitedTrialEffectDays);
                    context.ChangeOfficeControversy(-2f, "successful limited army foraging");
                    return Result("{=BC_CouncilIncident_ForagingRights_ResultAbstainSuccess}Limited foraging provides useful supplies without provoking serious complaints.");
                case CouncilIncidentQuality.Neutral:
                    return Result("{=BC_CouncilIncident_ForagingRights_ResultAbstainNeutral}The restricted foraging parties find little and leave army supply largely unchanged.");
                default:
                    context.ApplyAspectMultiplier(
                        CouncilIncidentAspects.ArmyFoodEfficiency,
                        ForagingRightsEventId,
                        0.75f,
                        BellumCivileConstants.CouncilIncidentLimitedTrialEffectDays);
                    context.ChangeOfficeControversy(5f, "failed limited army foraging");
                    return Result("{=BC_CouncilIncident_ForagingRights_ResultAbstainFailure}Even the restricted foragers become undisciplined, disrupting the countryside and army supply alike.");
            }
        }

        private static CouncilIncidentDefinition CreateQuartermastersSkimEvent()
        {
            return new CouncilIncidentDefinition(
                QuartermastersSkimEventId,
                "marshal_oversee_logistics",
                PrivyCouncilOffice.Marshal,
                IncidentsCampaignBehaviour.IncidentType.FiefManagement,
                1f,
                context => new TextObject("{=BC_CouncilIncident_QuartermasterSkim_Title}The Quartermaster's Skim"),
                context =>
                {
                    TextObject description = new TextObject("{=BC_CouncilIncident_QuartermasterSkim_Description}Rumors have reached the court that army quartermasters are shaving measures from grain allotments, selling equipment from royal stores, and falsifying supply ledgers. {MARSHAL_NAME}, your marshal, proposes a formal audit of the logistical service. A successful inquiry could restore confidence, but exposing disorder without correcting it would make every shortage another accusation against the marshal.");
                    description.SetTextVariable("MARSHAL_NAME", context?.Holder?.Leader?.Name ?? context?.Holder?.Name ?? TextObject.GetEmpty());
                    return description;
                },
                BuildQuartermastersSkimOptions);
        }

        private static List<CouncilIncidentOptionDefinition> BuildQuartermastersSkimOptions(
            CouncilIncidentContext context)
        {
            return new List<CouncilIncidentOptionDefinition>
            {
                new CouncilIncidentOptionDefinition(
                    "{=BC_CouncilIncident_QuartermasterSkim_Endorse}Authorize a complete audit and exemplary punishments.",
                    BuildQuartermastersSkimEndorseHints,
                    ApplyQuartermastersSkimEndorse),
                new CouncilIncidentOptionDefinition(
                    "{=BC_CouncilIncident_QuartermasterSkim_Oppose}Dismiss the accusations and preserve the existing supply administration.",
                    BuildQuartermastersSkimOpposeHints,
                    ApplyQuartermastersSkimOppose),
                new CouncilIncidentOptionDefinition(
                    "{=BC_CouncilIncident_QuartermasterSkim_Abstain}Permit a limited inspection of the largest supply depots.",
                    BuildQuartermastersSkimAbstainHints,
                    ApplyQuartermastersSkimAbstain)
            };
        }

        private static List<TextObject> BuildQuartermastersSkimEndorseHints(CouncilIncidentContext context)
        {
            return new List<TextObject>
            {
                ChanceText("{=BC_CouncilIncident_QuartermasterSkim_EndorseSuccess}{CHANCE}% chance: reduce the assignment's weekly controversy by 75% for {DAYS} days and reduce Marshal controversy by 5.", context.SuccessChance, BellumCivileConstants.CouncilIncidentEndorsedEffectDays),
                ChanceText("{=BC_CouncilIncident_QuartermasterSkim_EndorseNeutral}{CHANCE}% chance: reduce the assignment's weekly controversy by 50% for {DAYS} days.", context.NeutralChance, BellumCivileConstants.CouncilIncidentEndorsedEffectDays),
                ChanceText("{=BC_CouncilIncident_QuartermasterSkim_EndorseFailure}{CHANCE}% chance: double the assignment's weekly controversy for {DAYS} days and increase Marshal controversy by 10.", context.FailureChance, BellumCivileConstants.CouncilIncidentEndorsedEffectDays),
                new TextObject("{=BC_CouncilIncident_QuartermasterSkim_EndorseRelation}Gain 5 relation with your marshal.")
            };
        }

        private static List<TextObject> BuildQuartermastersSkimOpposeHints(CouncilIncidentContext context)
        {
            return new List<TextObject>
            {
                new TextObject("{=BC_CouncilIncident_QuartermasterSkim_OpposeHint}The assignment remains unchanged. Lose 10 relation with your marshal.")
            };
        }

        private static List<TextObject> BuildQuartermastersSkimAbstainHints(CouncilIncidentContext context)
        {
            return new List<TextObject>
            {
                ChanceText("{=BC_CouncilIncident_QuartermasterSkim_AbstainSuccess}{CHANCE}% chance: reduce the assignment's weekly controversy by 50% for {DAYS} days and reduce Marshal controversy by 2.", context.SuccessChance, BellumCivileConstants.CouncilIncidentLimitedTrialEffectDays),
                ChanceText("{=BC_CouncilIncident_QuartermasterSkim_AbstainNeutral}{CHANCE}% chance: the assignment continues unchanged.", context.NeutralChance),
                ChanceText("{=BC_CouncilIncident_QuartermasterSkim_AbstainFailure}{CHANCE}% chance: increase the assignment's weekly controversy by 50% for {DAYS} days and increase Marshal controversy by 5.", context.FailureChance, BellumCivileConstants.CouncilIncidentLimitedTrialEffectDays)
            };
        }

        private static List<TextObject> ApplyQuartermastersSkimEndorse(CouncilIncidentContext context)
        {
            context.ChangeHolderRelation(5);
            switch (context.Quality)
            {
                case CouncilIncidentQuality.Success:
                    context.ApplyAspectMultiplier(
                        CouncilIncidentAspects.LogisticsWeeklyControversy,
                        QuartermastersSkimEventId,
                        0.25f,
                        BellumCivileConstants.CouncilIncidentEndorsedEffectDays);
                    context.ChangeOfficeControversy(-5f, "successful audit of army quartermasters");
                    return Result("{=BC_CouncilIncident_QuartermasterSkim_ResultEndorseSuccess}The audit exposes corrupt quartermasters, restores missing stores, and strengthens confidence in the marshal's administration.");
                case CouncilIncidentQuality.Neutral:
                    context.ApplyAspectMultiplier(
                        CouncilIncidentAspects.LogisticsWeeklyControversy,
                        QuartermastersSkimEventId,
                        0.5f,
                        BellumCivileConstants.CouncilIncidentEndorsedEffectDays);
                    return Result("{=BC_CouncilIncident_QuartermasterSkim_ResultEndorseNeutral}Several irregularities are corrected, though suspicion continues to follow the army's supply service.");
                default:
                    context.ApplyAspectMultiplier(
                        CouncilIncidentAspects.LogisticsWeeklyControversy,
                        QuartermastersSkimEventId,
                        2f,
                        BellumCivileConstants.CouncilIncidentEndorsedEffectDays);
                    context.ChangeOfficeControversy(10f, "failed audit of army quartermasters");
                    return Result("{=BC_CouncilIncident_QuartermasterSkim_ResultEndorseFailure}The audit reveals widespread corruption but punishes no one of consequence, making every shortage another scandal.");
            }
        }

        private static List<TextObject> ApplyQuartermastersSkimOppose(CouncilIncidentContext context)
        {
            context.ChangeHolderRelation(-10);
            return Result("{=BC_CouncilIncident_QuartermasterSkim_ResultOppose}You dismiss the accusations and preserve the existing supply administration. The marshal resents the Crown's lack of confidence in the proposed inquiry.");
        }

        private static List<TextObject> ApplyQuartermastersSkimAbstain(CouncilIncidentContext context)
        {
            switch (context.Quality)
            {
                case CouncilIncidentQuality.Success:
                    context.ApplyAspectMultiplier(
                        CouncilIncidentAspects.LogisticsWeeklyControversy,
                        QuartermastersSkimEventId,
                        0.5f,
                        BellumCivileConstants.CouncilIncidentLimitedTrialEffectDays);
                    context.ChangeOfficeControversy(-2f, "successful limited audit of army quartermasters");
                    return Result("{=BC_CouncilIncident_QuartermasterSkim_ResultAbstainSuccess}The limited inspection corrects several abuses and quiets the strongest accusations against the supply service.");
                case CouncilIncidentQuality.Neutral:
                    return Result("{=BC_CouncilIncident_QuartermasterSkim_ResultAbstainNeutral}The inspectors find minor irregularities but nothing sufficient to alter the marshal's standing.");
                default:
                    context.ApplyAspectMultiplier(
                        CouncilIncidentAspects.LogisticsWeeklyControversy,
                        QuartermastersSkimEventId,
                        1.5f,
                        BellumCivileConstants.CouncilIncidentLimitedTrialEffectDays);
                    context.ChangeOfficeControversy(5f, "failed limited audit of army quartermasters");
                    return Result("{=BC_CouncilIncident_QuartermasterSkim_ResultAbstainFailure}The inspectors expose enough disorder to provoke outrage, but not enough to identify those responsible.");
            }
        }

        private static CouncilIncidentDefinition CreateSpymasterDoubleAgentEvent()
        {
            return CreateSpymasterIncident(new SpymasterIncidentTemplate
            {
                Id = SpymasterDoubleAgentEventId,
                AssignmentId = "spymaster_counter_espionage",
                AspectId = CouncilIncidentAspects.CounterEspionageClaimDiscovery,
                Title = "{=BC_CouncilIncident_DoubleAgent_Title}The Double Agent",
                Description = "{=BC_CouncilIncident_DoubleAgent_Description}{SPYMASTER_NAME}, your spymaster, reports that a foreign agent caught purchasing seals and genealogical records has offered to betray his handlers in exchange for protection. Returned with convincing information, he might expose every attempt to manufacture claims against the realm. If his former masters suspect the deception, they may instead use him to feed your court precisely what they wish it to believe.",
                EffectLabel = "{=BC_CouncilIncident_DoubleAgent_Effect}hostile claim discovery",
                Endorse = "{=BC_CouncilIncident_DoubleAgent_Endorse}Return the agent with false records and secret instructions.",
                Oppose = "{=BC_CouncilIncident_DoubleAgent_Oppose}Imprison him and dismantle his known contacts.",
                Abstain = "{=BC_CouncilIncident_DoubleAgent_Abstain}Use him only to arrange one controlled exchange.",
                EndorseSuccessResult = "{=BC_CouncilIncident_DoubleAgent_ResultEndorseSuccess}The agent regains his handlers' confidence and identifies the scribes, intermediaries, and foreign patrons supporting hostile claims against the realm.",
                EndorseNeutralResult = "{=BC_CouncilIncident_DoubleAgent_ResultEndorseNeutral}The agent provides several useful names, though his former masters keep their most important plans beyond his reach.",
                EndorseFailureResult = "{=BC_CouncilIncident_DoubleAgent_ResultEndorseFailure}The foreign network recognizes the deception and uses the agent to deliver convincing false trails while its genuine fabrications proceed unseen.",
                OpposeResult = "{=BC_CouncilIncident_DoubleAgent_ResultOppose}You order the agent imprisoned. The spymaster closes the operation and accepts the information already recovered.",
                AbstainSuccessResult = "{=BC_CouncilIncident_DoubleAgent_ResultAbstainSuccess}The controlled exchange exposes a small but valuable chain of hostile intermediaries.",
                AbstainNeutralResult = "{=BC_CouncilIncident_DoubleAgent_ResultAbstainNeutral}The exchange confirms existing suspicions without revealing the wider network.",
                AbstainFailureResult = "{=BC_CouncilIncident_DoubleAgent_ResultAbstainFailure}The agent's handlers detect the controlled exchange and sever contacts before they can be identified.",
                SuccessReason = "a successful double-agent operation",
                FailureReason = "a compromised double-agent operation",
                LimitedSuccessReason = "a useful controlled exchange with a foreign agent",
                LimitedFailureReason = "a failed controlled exchange with a foreign agent"
            });
        }

        private static CouncilIncidentDefinition CreateSpymasterCourtParanoiaEvent()
        {
            return CreateSpymasterIncident(new SpymasterIncidentTemplate
            {
                Id = SpymasterCourtParanoiaEventId,
                AssignmentId = "spymaster_counter_espionage",
                AspectId = CouncilIncidentAspects.CounterEspionageOffensePenalty,
                IsDrawback = true,
                Title = "{=BC_CouncilIncident_CourtParanoia_Title}Paranoia in the Court",
                Description = "{=BC_CouncilIncident_CourtParanoia_Description}{SPYMASTER_NAME}, your spymaster, warns that the search for foreign agents has made officials afraid to speak freely, exchange correspondence, or authorize covert work abroad. A system of sealed warrants and compartmentalized reports might restore confidence without relaxing security. If mishandled, it will merely add another layer of suspicion.",
                EffectLabel = "{=BC_CouncilIncident_CourtParanoia_Effect}the assignment's offensive intrigue penalty",
                Endorse = "{=BC_CouncilIncident_CourtParanoia_Endorse}Establish sealed channels for authorized agents.",
                Oppose = "{=BC_CouncilIncident_CourtParanoia_Oppose}Accept caution as the price of security.",
                Abstain = "{=BC_CouncilIncident_CourtParanoia_Abstain}Protect only the most trusted foreign operatives.",
                EndorseSuccessResult = "{=BC_CouncilIncident_CourtParanoia_ResultEndorseSuccess}Sealed warrants distinguish authorized agents from genuine suspects, allowing foreign operations to resume without weakening the internal watch.",
                EndorseNeutralResult = "{=BC_CouncilIncident_CourtParanoia_ResultEndorseNeutral}The new channels restore some confidence, though officials remain cautious about covert correspondence.",
                EndorseFailureResult = "{=BC_CouncilIncident_CourtParanoia_ResultEndorseFailure}Contradictory warrants and secret compartments deepen the confusion. Agents abroad are recalled while courtiers accuse one another of concealed authority.",
                OpposeResult = "{=BC_CouncilIncident_CourtParanoia_ResultOppose}You retain the present precautions. The spymaster leaves foreign operations constrained by the court's suspicion.",
                AbstainSuccessResult = "{=BC_CouncilIncident_CourtParanoia_ResultAbstainSuccess}A narrow circle of trusted operatives resumes useful work abroad without attracting scrutiny at home.",
                AbstainNeutralResult = "{=BC_CouncilIncident_CourtParanoia_ResultAbstainNeutral}The protected channels function, but their limited reach changes little beyond a few missions.",
                AbstainFailureResult = "{=BC_CouncilIncident_CourtParanoia_ResultAbstainFailure}Preferential protection makes the chosen operatives appear more suspicious and further restricts their movements.",
                SuccessReason = "a successful reform of covert warrants",
                FailureReason = "deepening paranoia within the court",
                LimitedSuccessReason = "protected channels for trusted operatives",
                LimitedFailureReason = "compromised channels for trusted operatives"
            });
        }

        private static CouncilIncidentDefinition CreateSpymasterCompromisedGuardEvent()
        {
            return CreateSpymasterIncident(new SpymasterIncidentTemplate
            {
                Id = SpymasterCompromisedGuardEventId,
                AssignmentId = "spymaster_counter_espionage",
                AspectId = CouncilIncidentAspects.CounterEspionageDefense,
                Title = "{=BC_CouncilIncident_CompromisedGuard_Title}The Compromised Guard",
                Description = "{=BC_CouncilIncident_CompromisedGuard_Description}{SPYMASTER_NAME}, your spymaster, presents evidence that several palace guards have accepted gifts from foreign merchants and allowed unrecorded visitors into restricted halls. Replacing the watch and rotating its officers could close dangerous gaps, though a clumsy purge may leave the palace guarded by strangers who know neither its passages nor its people.",
                EffectLabel = "{=BC_CouncilIncident_CompromisedGuard_Effect}the assignment's defensive intrigue bonus",
                Endorse = "{=BC_CouncilIncident_CompromisedGuard_Endorse}Replace the compromised watch immediately.",
                Oppose = "{=BC_CouncilIncident_CompromisedGuard_Oppose}Watch the suspected guards rather than expose the investigation.",
                Abstain = "{=BC_CouncilIncident_CompromisedGuard_Abstain}Quietly remove only those against whom proof is strongest.",
                EndorseSuccessResult = "{=BC_CouncilIncident_CompromisedGuard_ResultEndorseSuccess}The compromised guards are replaced in a single night and their contacts identified, closing several paths into the royal household.",
                EndorseNeutralResult = "{=BC_CouncilIncident_CompromisedGuard_ResultEndorseNeutral}The most dangerous guards are removed, though the new watch requires time to master the palace and its routines.",
                EndorseFailureResult = "{=BC_CouncilIncident_CompromisedGuard_ResultEndorseFailure}The purge removes loyal veterans alongside the guilty. Confused replacements leave the palace more vulnerable than before.",
                OpposeResult = "{=BC_CouncilIncident_CompromisedGuard_ResultOppose}You keep the suspected guards under observation. The spymaster abandons the immediate purge and preserves the present defenses.",
                AbstainSuccessResult = "{=BC_CouncilIncident_CompromisedGuard_ResultAbstainSuccess}The proven collaborators disappear quietly and their replacements strengthen the most vulnerable posts.",
                AbstainNeutralResult = "{=BC_CouncilIncident_CompromisedGuard_ResultAbstainNeutral}Several guards are replaced without incident, but the wider compromise remains uncertain.",
                AbstainFailureResult = "{=BC_CouncilIncident_CompromisedGuard_ResultAbstainFailure}The remaining collaborators recognize the pattern and exploit gaps created by the partial rotation.",
                SuccessReason = "a successful purge of compromised guards",
                FailureReason = "a disastrous purge of the palace guard",
                LimitedSuccessReason = "a discreet removal of compromised guards",
                LimitedFailureReason = "a failed partial rotation of the palace guard"
            });
        }

        private static CouncilIncidentDefinition CreateSpymasterFabricatedTreasonEvent()
        {
            return CreateSpymasterIncident(new SpymasterIncidentTemplate
            {
                Id = SpymasterFabricatedTreasonEventId,
                AssignmentId = "spymaster_uncover_dissent",
                AspectId = CouncilIncidentAspects.DissentWeeklyControversy,
                IsDrawback = true,
                Title = "{=BC_CouncilIncident_FabricatedTreason_Title}The Fabricated Treason",
                Description = "{=BC_CouncilIncident_FabricatedTreason_Description}{SPYMASTER_NAME}, your spymaster, proposes planting false correspondence among suspected conspirators, hoping that frightened accomplices will expose themselves while attempting to destroy it. A convincing deception could vindicate the office and silence its critics. Discovery would make every accusation of treason appear manufactured.",
                EffectLabel = "{=BC_CouncilIncident_FabricatedTreason_Effect}the assignment's weekly controversy",
                Endorse = "{=BC_CouncilIncident_FabricatedTreason_Endorse}Plant the correspondence and prepare the arrests.",
                Oppose = "{=BC_CouncilIncident_FabricatedTreason_Oppose}Forbid the manufacture of treasonous evidence.",
                Abstain = "{=BC_CouncilIncident_FabricatedTreason_Abstain}Introduce one carefully limited false message.",
                EndorseSuccessResult = "{=BC_CouncilIncident_FabricatedTreason_ResultEndorseSuccess}The false letters provoke genuine conspirators into revealing their contacts. The arrests appear thoroughly justified and criticism of the office subsides.",
                EndorseNeutralResult = "{=BC_CouncilIncident_FabricatedTreason_ResultEndorseNeutral}Several suspects betray their anxiety, though the planted evidence produces more suspicion than proof.",
                EndorseFailureResult = "{=BC_CouncilIncident_FabricatedTreason_ResultEndorseFailure}A copied seal exposes the letters as false. Every previous accusation is reconsidered, and the spymaster's methods become a scandal throughout the realm.",
                OpposeResult = "{=BC_CouncilIncident_FabricatedTreason_ResultOppose}You forbid the deception. The spymaster destroys the false letters and continues the investigation by ordinary means.",
                AbstainSuccessResult = "{=BC_CouncilIncident_FabricatedTreason_ResultAbstainSuccess}The single message unsettles a genuine accomplice, who quietly reveals enough to justify closer scrutiny.",
                AbstainNeutralResult = "{=BC_CouncilIncident_FabricatedTreason_ResultAbstainNeutral}The message circulates without producing either a confession or a public scandal.",
                AbstainFailureResult = "{=BC_CouncilIncident_FabricatedTreason_ResultAbstainFailure}The false message is recognized and preserved as evidence that the office invents the threats it claims to uncover.",
                SuccessReason = "a successful deception against suspected conspirators",
                FailureReason = "an exposed fabrication of treason",
                LimitedSuccessReason = "a useful false message among conspirators",
                LimitedFailureReason = "an exposed false message among conspirators"
            });
        }

        private static CouncilIncidentDefinition CreateSpymasterAgentProvocateurEvent()
        {
            return CreateSpymasterIncident(new SpymasterIncidentTemplate
            {
                Id = SpymasterAgentProvocateurEventId,
                AssignmentId = "spymaster_uncover_dissent",
                AspectId = CouncilIncidentAspects.DissentClaimDiscovery,
                Title = "{=BC_CouncilIncident_AgentProvocateur_Title}The Agent Provocateur",
                Description = "{=BC_CouncilIncident_AgentProvocateur_Description}{SPYMASTER_NAME}, your spymaster, has prepared an agent to pose as a dishonest archivist willing to sell seals, pedigrees, and disputed charters. Foreign claimants and their intermediaries may reveal themselves while seeking his services, though one careless promise could supply them with the very evidence they came to purchase.",
                EffectLabel = "{=BC_CouncilIncident_AgentProvocateur_Effect}hostile claim discovery",
                Endorse = "{=BC_CouncilIncident_AgentProvocateur_Endorse}Establish the false archive and invite every interested buyer.",
                Oppose = "{=BC_CouncilIncident_AgentProvocateur_Oppose}Close the operation before it assists a genuine claimant.",
                Abstain = "{=BC_CouncilIncident_AgentProvocateur_Abstain}Approach only agents already under suspicion.",
                EndorseSuccessResult = "{=BC_CouncilIncident_AgentProvocateur_ResultEndorseSuccess}Claimants and foreign intermediaries flock to the false archive, exposing networks that had previously remained beyond suspicion.",
                EndorseNeutralResult = "{=BC_CouncilIncident_AgentProvocateur_ResultEndorseNeutral}The operation identifies several minor intermediaries but attracts none of the principal architects of hostile claims.",
                EndorseFailureResult = "{=BC_CouncilIncident_AgentProvocateur_ResultEndorseFailure}The agent sells convincing material to a genuine claimant before the trap can close, strengthening the very fabrication he was meant to expose.",
                OpposeResult = "{=BC_CouncilIncident_AgentProvocateur_ResultOppose}You close the false archive. The spymaster withdraws the agent before any claimant can approach him.",
                AbstainSuccessResult = "{=BC_CouncilIncident_AgentProvocateur_ResultAbstainSuccess}A carefully chosen approach exposes useful contacts without releasing valuable records.",
                AbstainNeutralResult = "{=BC_CouncilIncident_AgentProvocateur_ResultAbstainNeutral}The suspected agents show interest but reveal little beyond what the office already knew.",
                AbstainFailureResult = "{=BC_CouncilIncident_AgentProvocateur_ResultAbstainFailure}The targets recognize the archivist as bait and warn the wider network to adopt greater caution.",
                SuccessReason = "a successful operation against hostile claimants",
                FailureReason = "a failed operation against hostile claimants",
                LimitedSuccessReason = "a useful approach to hostile claim agents",
                LimitedFailureReason = "an exposed approach to hostile claim agents"
            });
        }

        private static CouncilIncidentDefinition CreateSpymasterUndergroundNetworkEvent()
        {
            return CreateSpymasterIncident(new SpymasterIncidentTemplate
            {
                Id = SpymasterUndergroundNetworkEventId,
                AssignmentId = "spymaster_uncover_dissent",
                AspectId = CouncilIncidentAspects.DissentConspiracySuppression,
                Title = "{=BC_CouncilIncident_UndergroundNetwork_Title}The Underground Network",
                Description = "{=BC_CouncilIncident_UndergroundNetwork_Description}{SPYMASTER_NAME}, your spymaster, reports that covert meetings, hidden messengers, and sympathetic safe houses form an underground network linking several discontented houses. Rather than strike immediately, the office proposes mapping the entire structure and quietly disrupting its communications. Waiting may expose the whole conspiracy, or allow it time to mature beyond control.",
                EffectLabel = "{=BC_CouncilIncident_UndergroundNetwork_Effect}the assignment's suppression of covert conspiracies",
                Endorse = "{=BC_CouncilIncident_UndergroundNetwork_Endorse}Infiltrate the network before dismantling it.",
                Oppose = "{=BC_CouncilIncident_UndergroundNetwork_Oppose}Arrest the known conspirators immediately.",
                Abstain = "{=BC_CouncilIncident_UndergroundNetwork_Abstain}Disrupt its messengers while continuing limited surveillance.",
                EndorseSuccessResult = "{=BC_CouncilIncident_UndergroundNetwork_ResultEndorseSuccess}Patient infiltration reveals the network's couriers and safe houses. Quiet disruptions leave its conspiracies isolated and slow to advance.",
                EndorseNeutralResult = "{=BC_CouncilIncident_UndergroundNetwork_ResultEndorseNeutral}The office maps much of the network and impedes its communications, though several important organizers remain hidden.",
                EndorseFailureResult = "{=BC_CouncilIncident_UndergroundNetwork_ResultEndorseFailure}The infiltrator is exposed and the underground disperses into smaller cells that communicate more cautiously and organize beyond the office's sight.",
                OpposeResult = "{=BC_CouncilIncident_UndergroundNetwork_ResultOppose}You order immediate arrests. The spymaster abandons the wider infiltration and preserves the assignment's existing methods.",
                AbstainSuccessResult = "{=BC_CouncilIncident_UndergroundNetwork_ResultAbstainSuccess}Intercepted couriers delay meetings and leave several conspiratorial cells unable to coordinate.",
                AbstainNeutralResult = "{=BC_CouncilIncident_UndergroundNetwork_ResultAbstainNeutral}The disrupted messages inconvenience the network without seriously impeding its growth.",
                AbstainFailureResult = "{=BC_CouncilIncident_UndergroundNetwork_ResultAbstainFailure}The missing couriers warn the underground that it is watched, driving its members toward safer and more effective methods.",
                SuccessReason = "a successful infiltration of an underground network",
                FailureReason = "a failed infiltration of an underground network",
                LimitedSuccessReason = "a successful disruption of conspiratorial couriers",
                LimitedFailureReason = "a failed disruption of conspiratorial couriers"
            });
        }

        private static CouncilIncidentDefinition CreateSpymasterViciousPamphletsEvent()
        {
            return CreateSpymasterIncident(new SpymasterIncidentTemplate
            {
                Id = SpymasterViciousPamphletsEventId,
                AssignmentId = "spymaster_sow_rumors",
                AspectId = CouncilIncidentAspects.RumorOffense,
                Title = "{=BC_CouncilIncident_ViciousPamphlets_Title}The Vicious Pamphlets",
                Description = "{=BC_CouncilIncident_ViciousPamphlets_Description}{SPYMASTER_NAME}, your spymaster, presents a collection of vicious pamphlets accusing a rival court of cowardice, corruption, secret betrayals, and several improprieties too entertaining to require proof. Distributed through merchants and taverns, they may poison the rival ruler's reputation. If traced back to your court, the crude attack could inspire sympathy for its intended victim.",
                EffectLabel = "{=BC_CouncilIncident_ViciousPamphlets_Effect}the assignment's offensive intrigue bonus",
                Endorse = "{=BC_CouncilIncident_ViciousPamphlets_Endorse}Distribute the pamphlets throughout the rival realm.",
                Oppose = "{=BC_CouncilIncident_ViciousPamphlets_Oppose}Burn them and conduct intrigue with greater dignity.",
                Abstain = "{=BC_CouncilIncident_ViciousPamphlets_Abstain}Circulate only the most plausible accusations.",
                EndorseSuccessResult = "{=BC_CouncilIncident_ViciousPamphlets_ResultEndorseSuccess}The pamphlets are copied, embellished, and repeated at every market. Rival courtiers waste their energy denying stories the public prefers to believe.",
                EndorseNeutralResult = "{=BC_CouncilIncident_ViciousPamphlets_ResultEndorseNeutral}Several accusations gain currency and make the rival court more vulnerable to further manipulation.",
                EndorseFailureResult = "{=BC_CouncilIncident_ViciousPamphlets_ResultEndorseFailure}The pamphlets are exposed as clumsy foreign inventions. Their intended victim appears dignified, while your agents lose credibility abroad.",
                OpposeResult = "{=BC_CouncilIncident_ViciousPamphlets_ResultOppose}You order the pamphlets burned. The spymaster abandons the campaign and continues with less conspicuous rumors.",
                AbstainSuccessResult = "{=BC_CouncilIncident_ViciousPamphlets_ResultAbstainSuccess}The restrained accusations appear credible enough to spread through the rival court without revealing their source.",
                AbstainNeutralResult = "{=BC_CouncilIncident_ViciousPamphlets_ResultAbstainNeutral}The selected rumors earn brief attention but little lasting influence.",
                AbstainFailureResult = "{=BC_CouncilIncident_ViciousPamphlets_ResultAbstainFailure}Even the plausible accusations are traced to paid distributors, making later rumors easier to dismiss.",
                SuccessReason = "a successful campaign of hostile pamphlets",
                FailureReason = "an exposed campaign of hostile pamphlets",
                LimitedSuccessReason = "a credible campaign of selected rumors",
                LimitedFailureReason = "an exposed campaign of selected rumors"
            });
        }

        private static CouncilIncidentDefinition CreateSpymasterBribedServantEvent()
        {
            return CreateSpymasterIncident(new SpymasterIncidentTemplate
            {
                Id = SpymasterBribedServantEventId,
                AssignmentId = "spymaster_sow_rumors",
                AspectId = CouncilIncidentAspects.RumorWeeklyControversy,
                IsDrawback = true,
                Title = "{=BC_CouncilIncident_BribedServant_Title}The Bribed Servant",
                Description = "{=BC_CouncilIncident_BribedServant_Description}{SPYMASTER_NAME}, your spymaster, reports that a servant close to a rival ruler is prepared to repeat carefully chosen stories, misplace correspondence, and encourage suspicion among the household. The requested payment is considerable but discreet. Exposure would reveal not only the servant's treachery, but the hand that purchased it.",
                EffectLabel = "{=BC_CouncilIncident_BribedServant_Effect}the assignment's weekly controversy",
                Endorse = "{=BC_CouncilIncident_BribedServant_Endorse}Pay the servant and provide a full set of instructions.",
                Oppose = "{=BC_CouncilIncident_BribedServant_Oppose}Refuse to build policy upon a servant's betrayal.",
                Abstain = "{=BC_CouncilIncident_BribedServant_Abstain}Purchase only occasional information through intermediaries.",
                EndorseSuccessResult = "{=BC_CouncilIncident_BribedServant_ResultEndorseSuccess}The servant works discreetly, rival suspicions turn inward, and no trace of the payments reaches your court.",
                EndorseNeutralResult = "{=BC_CouncilIncident_BribedServant_ResultEndorseNeutral}The servant spreads several useful stories while keeping the arrangement concealed behind layers of intermediaries.",
                EndorseFailureResult = "{=BC_CouncilIncident_BribedServant_ResultEndorseFailure}The servant is caught with payment and instructions in hand. Copies circulate widely, exposing the spymaster's methods to ridicule and outrage.",
                OpposeResult = "{=BC_CouncilIncident_BribedServant_ResultOppose}You reject the arrangement. The spymaster dismisses the intermediary and leaves the servant unbought.",
                AbstainSuccessResult = "{=BC_CouncilIncident_BribedServant_ResultAbstainSuccess}Occasional payments secure useful whispers without revealing a regular relationship.",
                AbstainNeutralResult = "{=BC_CouncilIncident_BribedServant_ResultAbstainNeutral}The servant supplies minor gossip and keeps the limited arrangement private.",
                AbstainFailureResult = "{=BC_CouncilIncident_BribedServant_ResultAbstainFailure}An intermediary boasts of his access, exposing the attempted bribery before it yields anything useful.",
                SuccessReason = "a discreet arrangement with a rival servant",
                FailureReason = "an exposed attempt to bribe a rival servant",
                LimitedSuccessReason = "a discreet exchange with a rival servant",
                LimitedFailureReason = "a compromised exchange with a rival servant"
            });
        }

        private static CouncilIncidentDefinition CreateSpymasterCounterRumorEvent()
        {
            return CreateSpymasterIncident(new SpymasterIncidentTemplate
            {
                Id = SpymasterCounterRumorEventId,
                AssignmentId = "spymaster_sow_rumors",
                AspectId = CouncilIncidentAspects.RumorDefensePenalty,
                IsDrawback = true,
                Title = "{=BC_CouncilIncident_CounterRumor_Title}The Counter-Rumor",
                Description = "{=BC_CouncilIncident_CounterRumor_Description}{SPYMASTER_NAME}, your spymaster, warns that a rival court has answered your whispering campaign with stories of its own, accusing your household of weakness, scandal, and divided loyalties. A coordinated rebuttal may protect the realm without abandoning operations abroad. A panicked response could lend every accusation greater credibility.",
                EffectLabel = "{=BC_CouncilIncident_CounterRumor_Effect}the assignment's defensive intrigue penalty",
                Endorse = "{=BC_CouncilIncident_CounterRumor_Endorse}Identify the source and crush the counter-rumor publicly.",
                Oppose = "{=BC_CouncilIncident_CounterRumor_Oppose}Ignore foreign slander and maintain the present campaign.",
                Abstain = "{=BC_CouncilIncident_CounterRumor_Abstain}Answer only the accusations gaining serious attention.",
                EndorseSuccessResult = "{=BC_CouncilIncident_CounterRumor_ResultEndorseSuccess}The source is exposed as a paid foreign agent and the accusations collapse, freeing the court from much of their disruptive effect.",
                EndorseNeutralResult = "{=BC_CouncilIncident_CounterRumor_ResultEndorseNeutral}The rebuttal contains the most damaging stories, though lesser accusations continue circulating.",
                EndorseFailureResult = "{=BC_CouncilIncident_CounterRumor_ResultEndorseFailure}The public denial repeats every accusation to a wider audience. Rival agents exploit the panic and find your court easier to manipulate.",
                OpposeResult = "{=BC_CouncilIncident_CounterRumor_ResultOppose}You refuse to answer the foreign stories. The spymaster maintains the existing campaign and accepts its defensive cost.",
                AbstainSuccessResult = "{=BC_CouncilIncident_CounterRumor_ResultAbstainSuccess}A restrained answer discredits the most dangerous accusation without amplifying the rest.",
                AbstainNeutralResult = "{=BC_CouncilIncident_CounterRumor_ResultAbstainNeutral}The selected rebuttals reassure loyal courtiers but leave the broader rumor campaign unchanged.",
                AbstainFailureResult = "{=BC_CouncilIncident_CounterRumor_ResultAbstainFailure}Selective denials make every unanswered accusation appear implicitly confirmed.",
                SuccessReason = "a successful rebuttal of hostile rumors",
                FailureReason = "a disastrous rebuttal of hostile rumors",
                LimitedSuccessReason = "a measured rebuttal of hostile rumors",
                LimitedFailureReason = "a failed selective rebuttal of hostile rumors"
            });
        }

        private static void RegisterAdvisorIncidents(PrivyCouncilOffice office, string assignmentPrefix)
        {
            foreach (AdvisorIncidentTemplate template in CreateAdvisorIncidentTemplates())
                Register(CreateAdvisorIncident(office, assignmentPrefix, template));
        }

        private static IReadOnlyList<AdvisorIncidentTemplate> CreateAdvisorIncidentTemplates()
        {
            return new[]
            {
                new AdvisorIncidentTemplate
                {
                    EventSuffix = "rival_schools",
                    AssignmentSuffix = "counsel_crown",
                    AspectId = CouncilIncidentAspects.AdvisorCounselCompetence,
                    Title = "{=BC_CouncilIncident_RivalSchools_Title}Rival Schools of Counsel",
                    Description = "{=BC_CouncilIncident_RivalSchools_Description}{ADVISOR_NAME}, your advisor, warns that the marshal, chancellor, seneschal, and spymaster keep their records according to different customs and often offer incompatible answers to the same question. A common method of reports and sealed memoranda might bring order to their counsel, though proud officers may resist abandoning familiar practice.",
                    EffectLabel = "{=BC_CouncilIncident_RivalSchools_Effect}the advisor's contribution to core-office competence",
                    Endorse = "{=BC_CouncilIncident_RivalSchools_Endorse}Order the council to adopt a common method.",
                    Oppose = "{=BC_CouncilIncident_RivalSchools_Oppose}Allow every office to preserve its customs.",
                    Abstain = "{=BC_CouncilIncident_RivalSchools_Abstain}Test the method in two offices first.",
                    EndorseSuccessResult = "{=BC_CouncilIncident_RivalSchools_ResultEndorseSuccess}The common reports expose contradictions early and allow every officer to build upon the work of the others.",
                    EndorseNeutralResult = "{=BC_CouncilIncident_RivalSchools_ResultEndorseNeutral}The new forms improve communication, though several offices retain their old habits alongside them.",
                    EndorseFailureResult = "{=BC_CouncilIncident_RivalSchools_ResultEndorseFailure}The officers bury one another beneath unfamiliar records, and useful counsel is delayed by arguments over form and precedence.",
                    OpposeResult = "{=BC_CouncilIncident_RivalSchools_ResultOppose}You preserve the council's separate customs. The advisor abandons the proposed reforms.",
                    AbstainSuccessResult = "{=BC_CouncilIncident_RivalSchools_ResultAbstainSuccess}The limited trial demonstrates how shared reports can reconcile two offices without disturbing the whole council.",
                    AbstainNeutralResult = "{=BC_CouncilIncident_RivalSchools_ResultAbstainNeutral}The two offices exchange clearer reports, but the wider council remains unchanged.",
                    AbstainFailureResult = "{=BC_CouncilIncident_RivalSchools_ResultAbstainFailure}Even the limited trial produces duplicated records and fresh disputes over authority.",
                    SuccessReason = "successful council reporting reforms",
                    FailureReason = "failed council reporting reforms",
                    LimitedSuccessReason = "a successful trial of shared council reports",
                    LimitedFailureReason = "a failed trial of shared council reports"
                },
                new AdvisorIncidentTemplate
                {
                    EventSuffix = "ancient_tome",
                    AssignmentSuffix = "counsel_crown",
                    AspectId = CouncilIncidentAspects.AdvisorCounselCompetence,
                    Title = "{=BC_CouncilIncident_AncientTome_Title}The Ancient Tome",
                    Description = "{=BC_CouncilIncident_AncientTome_Description}{ADVISOR_NAME}, your advisor, has obtained an ancient treatise on the governance of royal households. Its passages promise remedies for corruption, delay, and confused authority, though some of its wisdom belongs to courts and customs long since vanished.",
                    EffectLabel = "{=BC_CouncilIncident_AncientTome_Effect}the advisor's contribution to core-office competence",
                    Endorse = "{=BC_CouncilIncident_AncientTome_Endorse}Commission copies and apply its lessons throughout the council.",
                    Oppose = "{=BC_CouncilIncident_AncientTome_Oppose}Return the relic to the library.",
                    Abstain = "{=BC_CouncilIncident_AncientTome_Abstain}Test only its most practical chapters.",
                    EndorseSuccessResult = "{=BC_CouncilIncident_AncientTome_ResultEndorseSuccess}Beneath its archaic language, the council finds principles that clarify authority and sharpen every office's work.",
                    EndorseNeutralResult = "{=BC_CouncilIncident_AncientTome_ResultEndorseNeutral}Several chapters prove useful, while the rest inspire more admiration than practical reform.",
                    EndorseFailureResult = "{=BC_CouncilIncident_AncientTome_ResultEndorseFailure}Obsolete procedures are imposed upon a living court, leaving its officers less certain of their duties than before.",
                    OpposeResult = "{=BC_CouncilIncident_AncientTome_ResultOppose}You return the tome to the library and leave the council's present methods undisturbed.",
                    AbstainSuccessResult = "{=BC_CouncilIncident_AncientTome_ResultAbstainSuccess}The chosen chapters offer concise remedies that improve the council without binding it to obsolete custom.",
                    AbstainNeutralResult = "{=BC_CouncilIncident_AncientTome_ResultAbstainNeutral}The selected passages provoke discussion but little lasting change.",
                    AbstainFailureResult = "{=BC_CouncilIncident_AncientTome_ResultAbstainFailure}The trial revives antiquated distinctions that confuse the officers asked to observe them.",
                    SuccessReason = "useful lessons drawn from an ancient treatise",
                    FailureReason = "misguided reforms drawn from an ancient treatise",
                    LimitedSuccessReason = "a successful trial of ancient council practices",
                    LimitedFailureReason = "a failed trial of ancient council practices"
                },
                new AdvisorIncidentTemplate
                {
                    EventSuffix = "sycophant",
                    AssignmentSuffix = "counsel_crown",
                    AspectId = CouncilIncidentAspects.AdvisorCounselCompetence,
                    Title = "{=BC_CouncilIncident_Sycophant_Title}The Sycophant",
                    Description = "{=BC_CouncilIncident_Sycophant_Description}{ADVISOR_NAME}, your advisor, has identified a courtier whose agreeable counsel has begun shaping reports before they reach the throne. Exposing the flatterer may restore honesty to the council, but an indiscriminate inquiry could make every officer afraid to speak plainly.",
                    EffectLabel = "{=BC_CouncilIncident_Sycophant_Effect}the advisor's contribution to core-office competence",
                    Endorse = "{=BC_CouncilIncident_Sycophant_Endorse}Expose the flatterer and demand candid counsel.",
                    Oppose = "{=BC_CouncilIncident_Sycophant_Oppose}Leave the courtier undisturbed.",
                    Abstain = "{=BC_CouncilIncident_Sycophant_Abstain}Quietly compare his reports against the originals.",
                    EndorseSuccessResult = "{=BC_CouncilIncident_Sycophant_ResultEndorseSuccess}Altered reports are uncovered, and the council learns that unwelcome truths will reach the throne without punishment.",
                    EndorseNeutralResult = "{=BC_CouncilIncident_Sycophant_ResultEndorseNeutral}The flatterer's influence is curtailed, though caution still colors the advice offered before the Crown.",
                    EndorseFailureResult = "{=BC_CouncilIncident_Sycophant_ResultEndorseFailure}The inquiry mistakes prudence for deceit and leaves honest officers terrified that disagreement will be called treachery.",
                    OpposeResult = "{=BC_CouncilIncident_Sycophant_ResultOppose}You decline the inquiry. The advisor withdraws the accusation and counsel continues as before.",
                    AbstainSuccessResult = "{=BC_CouncilIncident_Sycophant_ResultAbstainSuccess}A quiet comparison reveals where uncomfortable findings were softened before reaching the throne.",
                    AbstainNeutralResult = "{=BC_CouncilIncident_Sycophant_ResultAbstainNeutral}The reviewed reports reveal embellishment but no organized effort to mislead the Crown.",
                    AbstainFailureResult = "{=BC_CouncilIncident_Sycophant_ResultAbstainFailure}Rumors of the secret review spread, encouraging officers to make their reports even more guarded.",
                    SuccessReason = "the exposure of a court sycophant",
                    FailureReason = "a destructive inquiry into court sycophancy",
                    LimitedSuccessReason = "a careful review of altered council reports",
                    LimitedFailureReason = "an exposed review of council reports"
                },
                new AdvisorIncidentTemplate
                {
                    EventSuffix = "bitter_rivalry",
                    AssignmentSuffix = "mediate_council",
                    AspectId = CouncilIncidentAspects.AdvisorMediationSupport,
                    Title = "{=BC_CouncilIncident_BitterRivalry_Title}The Bitter Rivalry",
                    Description = "{=BC_CouncilIncident_BitterRivalry_Description}{ADVISOR_NAME}, your advisor, reports that two officers have ceased speaking except through hostile clerks. Their quarrel now colors every proposal brought before the council, and each regards compromise as a victory for the other.",
                    EffectLabel = "{=BC_CouncilIncident_BitterRivalry_Effect}the advisor's contribution to core-councillor support",
                    Endorse = "{=BC_CouncilIncident_BitterRivalry_Endorse}Compel both officers to submit to arbitration.",
                    Oppose = "{=BC_CouncilIncident_BitterRivalry_Oppose}Let the rivalry burn itself out.",
                    Abstain = "{=BC_CouncilIncident_BitterRivalry_Abstain}Broker a temporary working accord.",
                    EndorseSuccessResult = "{=BC_CouncilIncident_BitterRivalry_ResultEndorseSuccess}The arbitration gives both officers honorable concessions and restores confidence in the council's ability to govern itself.",
                    EndorseNeutralResult = "{=BC_CouncilIncident_BitterRivalry_ResultEndorseNeutral}The rivals resume formal cooperation, though neither abandons the grievance beneath it.",
                    EndorseFailureResult = "{=BC_CouncilIncident_BitterRivalry_ResultEndorseFailure}Public arbitration gives each rival a larger audience for old accusations and makes the entire council appear divided.",
                    OpposeResult = "{=BC_CouncilIncident_BitterRivalry_ResultOppose}You leave the rivals to their quarrel. The advisor abandons the proposed arbitration.",
                    AbstainSuccessResult = "{=BC_CouncilIncident_BitterRivalry_ResultAbstainSuccess}A narrow accord restores cooperation on urgent matters without reopening every grievance.",
                    AbstainNeutralResult = "{=BC_CouncilIncident_BitterRivalry_ResultAbstainNeutral}The rivals observe the accord in public while their households continue the dispute.",
                    AbstainFailureResult = "{=BC_CouncilIncident_BitterRivalry_ResultAbstainFailure}The temporary accord collapses at its first test and further discredits attempts at mediation.",
                    SuccessReason = "the successful arbitration of a council rivalry",
                    FailureReason = "a failed arbitration between rival councillors",
                    LimitedSuccessReason = "a successful working accord between rival councillors",
                    LimitedFailureReason = "a collapsed accord between rival councillors"
                },
                new AdvisorIncidentTemplate
                {
                    EventSuffix = "bribed_mediator",
                    AssignmentSuffix = "mediate_council",
                    AspectId = CouncilIncidentAspects.AdvisorMediationSupport,
                    Title = "{=BC_CouncilIncident_BribedMediator_Title}The Bribed Mediator",
                    Description = "{=BC_CouncilIncident_BribedMediator_Description}{ADVISOR_NAME}, your advisor, has learned that a clerk entrusted with carrying compromises between council offices accepted gifts from one of the disputing parties. A public inquiry may restore confidence, or convince every councillor that mediation merely conceals another form of influence.",
                    EffectLabel = "{=BC_CouncilIncident_BribedMediator_Effect}the advisor's contribution to core-councillor support",
                    Endorse = "{=BC_CouncilIncident_BribedMediator_Endorse}Expose the bribery and review every compromise.",
                    Oppose = "{=BC_CouncilIncident_BribedMediator_Oppose}Suppress the evidence to preserve appearances.",
                    Abstain = "{=BC_CouncilIncident_BribedMediator_Abstain}Investigate the clerk privately.",
                    EndorseSuccessResult = "{=BC_CouncilIncident_BribedMediator_ResultEndorseSuccess}The inquiry isolates the corruption to one clerk, reverses his dishonest bargains, and restores faith in impartial mediation.",
                    EndorseNeutralResult = "{=BC_CouncilIncident_BribedMediator_ResultEndorseNeutral}The clerk is removed and several bargains reviewed, though suspicion lingers around earlier compromises.",
                    EndorseFailureResult = "{=BC_CouncilIncident_BribedMediator_ResultEndorseFailure}The inquiry reveals gifts and favors throughout the mediation staff, leaving every settlement open to accusations of corruption.",
                    OpposeResult = "{=BC_CouncilIncident_BribedMediator_ResultOppose}You suppress the accusation. The clerk remains in place and the council's confidence changes little.",
                    AbstainSuccessResult = "{=BC_CouncilIncident_BribedMediator_ResultAbstainSuccess}The private inquiry secures proof, removes the clerk quietly, and reassures the officers most directly affected.",
                    AbstainNeutralResult = "{=BC_CouncilIncident_BribedMediator_ResultAbstainNeutral}The clerk is watched closely but the inquiry proves neither his innocence nor his guilt.",
                    AbstainFailureResult = "{=BC_CouncilIncident_BribedMediator_ResultAbstainFailure}The clerk discovers the inquiry and destroys his records, making every prior compromise appear suspect.",
                    SuccessReason = "the exposure of a bribed council mediator",
                    FailureReason = "a widening scandal among council mediators",
                    LimitedSuccessReason = "a discreet inquiry into a bribed mediator",
                    LimitedFailureReason = "a compromised inquiry into a bribed mediator"
                },
                new AdvisorIncidentTemplate
                {
                    EventSuffix = "council_deadlock",
                    AssignmentSuffix = "mediate_council",
                    AspectId = CouncilIncidentAspects.AdvisorMediationControversy,
                    Title = "{=BC_CouncilIncident_CouncilDeadlock_Title}Council Deadlock",
                    Description = "{=BC_CouncilIncident_CouncilDeadlock_Description}{ADVISOR_NAME}, your advisor, warns that disputes over precedence and responsibility have brought the council to a standstill. Every delayed matter adds another grievance, but imposing a settlement may only conceal the quarrel beneath reluctant obedience.",
                    EffectLabel = "{=BC_CouncilIncident_CouncilDeadlock_Effect}the advisor's contribution to reducing core-office controversy",
                    Endorse = "{=BC_CouncilIncident_CouncilDeadlock_Endorse}Impose a settlement and reopen council business.",
                    Oppose = "{=BC_CouncilIncident_CouncilDeadlock_Oppose}Require the officers to resolve it themselves.",
                    Abstain = "{=BC_CouncilIncident_CouncilDeadlock_Abstain}Settle only the most urgent dispute.",
                    EndorseSuccessResult = "{=BC_CouncilIncident_CouncilDeadlock_ResultEndorseSuccess}A carefully balanced settlement restores the council's work and deprives its officers of their loudest grievances.",
                    EndorseNeutralResult = "{=BC_CouncilIncident_CouncilDeadlock_ResultEndorseNeutral}Business resumes under an uneasy compromise, easing some disputes while leaving others unresolved.",
                    EndorseFailureResult = "{=BC_CouncilIncident_CouncilDeadlock_ResultEndorseFailure}The imposed settlement pleases no one and turns procedural complaints into lasting accusations against the council.",
                    OpposeResult = "{=BC_CouncilIncident_CouncilDeadlock_ResultOppose}You refuse to impose a settlement. The advisor leaves the officers to resolve their own deadlock.",
                    AbstainSuccessResult = "{=BC_CouncilIncident_CouncilDeadlock_ResultAbstainSuccess}Resolving the urgent dispute creates enough goodwill for the council to address several lesser grievances.",
                    AbstainNeutralResult = "{=BC_CouncilIncident_CouncilDeadlock_ResultAbstainNeutral}The immediate matter is settled, but the wider procedural quarrel continues.",
                    AbstainFailureResult = "{=BC_CouncilIncident_CouncilDeadlock_ResultAbstainFailure}The limited settlement is read as favoritism and adds another grievance to the deadlock.",
                    SuccessReason = "a successful settlement of council deadlock",
                    FailureReason = "a failed settlement imposed upon the council",
                    LimitedSuccessReason = "a successful limited council settlement",
                    LimitedFailureReason = "a failed limited council settlement"
                },
                new AdvisorIncidentTemplate
                {
                    EventSuffix = "faction_demands",
                    AssignmentSuffix = "represent_court",
                    AspectId = CouncilIncidentAspects.AdvisorRepresentationMood,
                    Title = "{=BC_CouncilIncident_FactionDemands_Title}The Faction Demands",
                    Description = "{=BC_CouncilIncident_FactionDemands_Description}{ADVISOR_NAME}, your advisor, brings a formal list of demands from the {FACTION_NAME}. Receiving them respectfully may demonstrate that their voice carries weight at court, though even a hearing may be mistaken for a promise.",
                    EffectLabel = "{=BC_CouncilIncident_FactionDemands_Effect}the represented faction's mood baseline",
                    Endorse = "{=BC_CouncilIncident_FactionDemands_Endorse}Receive the demands and authorize negotiations.",
                    Oppose = "{=BC_CouncilIncident_FactionDemands_Oppose}Refuse to entertain factional pressure.",
                    Abstain = "{=BC_CouncilIncident_FactionDemands_Abstain}Hear a limited delegation without making commitments.",
                    EndorseSuccessResult = "{=BC_CouncilIncident_FactionDemands_ResultEndorseSuccess}The hearing separates practical grievances from ceremonial demands, leaving the faction convinced that its interests receive an honest audience.",
                    EndorseNeutralResult = "{=BC_CouncilIncident_FactionDemands_ResultEndorseNeutral}The faction welcomes the hearing, though few demands advance beyond polite consideration.",
                    EndorseFailureResult = "{=BC_CouncilIncident_FactionDemands_ResultEndorseFailure}Competing promises and public refusals leave the faction feeling manipulated rather than represented.",
                    OpposeResult = "{=BC_CouncilIncident_FactionDemands_ResultOppose}You refuse the petition. The advisor returns the demands without negotiation.",
                    AbstainSuccessResult = "{=BC_CouncilIncident_FactionDemands_ResultAbstainSuccess}The small delegation receives clear answers and carries a measure of reassurance back to the faction.",
                    AbstainNeutralResult = "{=BC_CouncilIncident_FactionDemands_ResultAbstainNeutral}The delegation is heard courteously but leaves with little more than acknowledgement.",
                    AbstainFailureResult = "{=BC_CouncilIncident_FactionDemands_ResultAbstainFailure}Limiting the delegation is taken as an insult, and its members return to the faction more aggrieved than before.",
                    SuccessReason = "a successful hearing of faction demands",
                    FailureReason = "a disastrous hearing of faction demands",
                    LimitedSuccessReason = "a successful limited faction hearing",
                    LimitedFailureReason = "a failed limited faction hearing"
                },
                new AdvisorIncidentTemplate
                {
                    EventSuffix = "compromised_representative",
                    AssignmentSuffix = "represent_court",
                    AspectId = CouncilIncidentAspects.AdvisorRepresentationIntent,
                    Title = "{=BC_CouncilIncident_CompromisedRepresentative_Title}The Compromised Representative",
                    Description = "{=BC_CouncilIncident_CompromisedRepresentative_Description}{ADVISOR_NAME}, your advisor, reports that an intermediary trusted by the {FACTION_NAME} has been selling private correspondence and distorting messages carried between the faction and the Crown. Rebuilding these channels may restore confidence, but exposing the betrayal could deepen existing suspicion.",
                    EffectLabel = "{=BC_CouncilIncident_CompromisedRepresentative_Effect}the assignment's reduction of faction rebellious intent",
                    Endorse = "{=BC_CouncilIncident_CompromisedRepresentative_Endorse}Expose the intermediary and rebuild the channels.",
                    Oppose = "{=BC_CouncilIncident_CompromisedRepresentative_Oppose}Conceal the scandal and preserve appearances.",
                    Abstain = "{=BC_CouncilIncident_CompromisedRepresentative_Abstain}Quietly replace only those directly implicated.",
                    EndorseSuccessResult = "{=BC_CouncilIncident_CompromisedRepresentative_ResultEndorseSuccess}The traitor is exposed with convincing proof, and new messengers restore confidence between the faction and the Crown.",
                    EndorseNeutralResult = "{=BC_CouncilIncident_CompromisedRepresentative_ResultEndorseNeutral}The compromised channel is replaced, though suspicion continues to burden private negotiations.",
                    EndorseFailureResult = "{=BC_CouncilIncident_CompromisedRepresentative_ResultEndorseFailure}The accusation cannot be proved, and the faction concludes that the Crown is purging messengers who carry unwelcome truths.",
                    OpposeResult = "{=BC_CouncilIncident_CompromisedRepresentative_ResultOppose}You conceal the scandal. The advisor continues using the existing channels despite their uncertainty.",
                    AbstainSuccessResult = "{=BC_CouncilIncident_CompromisedRepresentative_ResultAbstainSuccess}The guilty intermediaries are replaced without spectacle, restoring confidence among those who knew of the breach.",
                    AbstainNeutralResult = "{=BC_CouncilIncident_CompromisedRepresentative_ResultAbstainNeutral}Several messengers are changed, but doubts about the remaining correspondence persist.",
                    AbstainFailureResult = "{=BC_CouncilIncident_CompromisedRepresentative_ResultAbstainFailure}The quiet removals inspire rumors of a wider purge and make faction leaders more suspicious of royal messages.",
                    SuccessReason = "the restoration of compromised faction channels",
                    FailureReason = "a failed purge of faction intermediaries",
                    LimitedSuccessReason = "a discreet restoration of faction channels",
                    LimitedFailureReason = "a suspicious replacement of faction intermediaries"
                },
                new AdvisorIncidentTemplate
                {
                    EventSuffix = "public_petition",
                    AssignmentSuffix = "represent_court",
                    AspectId = CouncilIncidentAspects.AdvisorRepresentationOverall,
                    Title = "{=BC_CouncilIncident_PublicPetition_Title}The Public Petition",
                    Description = "{=BC_CouncilIncident_PublicPetition_Description}{ADVISOR_NAME}, your advisor, proposes inviting a delegation of the {FACTION_NAME} to present its grievances openly before the Crown. Such a hearing could turn private resentment into orderly petition, or transform the council chamber into a stage for factional defiance.",
                    EffectLabel = "{=BC_CouncilIncident_PublicPetition_Effect}the advisor's overall representation of the faction",
                    Endorse = "{=BC_CouncilIncident_PublicPetition_Endorse}Summon the delegation before the full court.",
                    Oppose = "{=BC_CouncilIncident_PublicPetition_Oppose}Reject the spectacle.",
                    Abstain = "{=BC_CouncilIncident_PublicPetition_Abstain}Receive a small deputation in private.",
                    EndorseSuccessResult = "{=BC_CouncilIncident_PublicPetition_ResultEndorseSuccess}The hearing gives grievance an orderly voice, and measured royal answers convince the faction that petition serves it better than defiance.",
                    EndorseNeutralResult = "{=BC_CouncilIncident_PublicPetition_ResultEndorseNeutral}The delegation is heard with dignity, producing goodwill without resolving the faction's deeper concerns.",
                    EndorseFailureResult = "{=BC_CouncilIncident_PublicPetition_ResultEndorseFailure}The hearing descends into accusation and applause, turning private grievances into a public demonstration of factional strength.",
                    OpposeResult = "{=BC_CouncilIncident_PublicPetition_ResultOppose}You reject the proposed hearing. The advisor dismisses the delegation and representation continues through private channels.",
                    AbstainSuccessResult = "{=BC_CouncilIncident_PublicPetition_ResultAbstainSuccess}The private deputation airs its strongest grievances without turning the meeting into a contest of public authority.",
                    AbstainNeutralResult = "{=BC_CouncilIncident_PublicPetition_ResultAbstainNeutral}The deputation appreciates the audience but carries few concrete assurances back to the faction.",
                    AbstainFailureResult = "{=BC_CouncilIncident_PublicPetition_ResultAbstainFailure}Excluding the wider delegation makes the private meeting appear secretive and unrepresentative.",
                    SuccessReason = "a successful public faction petition",
                    FailureReason = "a disorderly public faction petition",
                    LimitedSuccessReason = "a successful private faction deputation",
                    LimitedFailureReason = "a failed private faction deputation"
                }
            };
        }

        private static CouncilIncidentDefinition CreateSeneschalHiddenLedgersEvent()
        {
            return CreateSeneschalIncident(new SeneschalIncidentTemplate
            {
                Id = SeneschalHiddenLedgersEventId,
                AssignmentId = "seneschal_audit_vassals",
                AspectId = CouncilIncidentAspects.AuditServiceStrength,
                Title = "{=BC_CouncilIncident_HiddenLedgers_Title}The Hidden Ledgers",
                Description = "{=BC_CouncilIncident_HiddenLedgers_Description}{SENESCHAL_NAME}, your seneschal, presents private ledgers seized from the household of a powerful vassal. Their figures bear little resemblance to the obligations declared before the Crown. A comprehensive examination might uncover years of concealed service, though a failed investigation would teach every noble house exactly which records must disappear.",
                EffectLabel = "{=BC_CouncilIncident_HiddenLedgers_Effect}the assignment's feudal service income bonus",
                Endorse = "{=BC_CouncilIncident_HiddenLedgers_Endorse}Seize the accounts and examine every household entry.",
                Oppose = "{=BC_CouncilIncident_HiddenLedgers_Oppose}Return the records and avoid provoking the peerage.",
                Abstain = "{=BC_CouncilIncident_HiddenLedgers_Abstain}Quietly compare selected entries through trusted clerks.",
                EndorseSuccessResult = "{=BC_CouncilIncident_HiddenLedgers_ResultEndorseSuccess}The concealed accounts reveal systematic underpayment. Revised assessments bring a marked increase in service reaching the Crown.",
                EndorseNeutralResult = "{=BC_CouncilIncident_HiddenLedgers_ResultEndorseNeutral}The accounts expose enough irregularities to strengthen collection, though the grand conspiracy promised by the clerks never emerges.",
                EndorseFailureResult = "{=BC_CouncilIncident_HiddenLedgers_ResultEndorseFailure}The seized ledgers prove incomplete and the accused household rallies the peerage against an audit denounced as arbitrary confiscation.",
                OpposeResult = "{=BC_CouncilIncident_HiddenLedgers_ResultOppose}You order the records returned. The seneschal obeys, leaving their discrepancies unresolved.",
                AbstainSuccessResult = "{=BC_CouncilIncident_HiddenLedgers_ResultAbstainSuccess}A discreet comparison identifies several concealed obligations without alerting the wider nobility.",
                AbstainNeutralResult = "{=BC_CouncilIncident_HiddenLedgers_ResultAbstainNeutral}The selected entries raise suspicions but provide little that can be enforced.",
                AbstainFailureResult = "{=BC_CouncilIncident_HiddenLedgers_ResultAbstainFailure}Word of the secret examination reaches the household, whose clerks remove the most useful records before the audit can proceed.",
                SuccessReason = "a successful examination of concealed service ledgers",
                FailureReason = "a failed seizure of noble service ledgers",
                LimitedSuccessReason = "a discreet examination of concealed service ledgers",
                LimitedFailureReason = "an exposed examination of noble service ledgers"
            });
        }

        private static CouncilIncidentDefinition CreateSeneschalNobleExemptionEvent()
        {
            return CreateSeneschalIncident(new SeneschalIncidentTemplate
            {
                Id = SeneschalNobleExemptionEventId,
                AssignmentId = "seneschal_audit_vassals",
                AspectId = CouncilIncidentAspects.AuditWeeklyControversy,
                IsDrawback = true,
                Title = "{=BC_CouncilIncident_NobleExemption_Title}The Noble Exemption",
                Description = "{=BC_CouncilIncident_NobleExemption_Description}{SENESCHAL_NAME}, your seneschal, reports that a powerful noble house has petitioned for temporary exemption from several disputed obligations, claiming hardship and ancient privilege. Granting the request may calm resistance to the wider audit, but other vassals will watch closely for weakness or favoritism.",
                EffectLabel = "{=BC_CouncilIncident_NobleExemption_Effect}the assignment's weekly controversy",
                Endorse = "{=BC_CouncilIncident_NobleExemption_Endorse}Grant the exemption and present it as a singular act of royal grace.",
                Oppose = "{=BC_CouncilIncident_NobleExemption_Oppose}Insist that every house meet its lawful obligations.",
                Abstain = "{=BC_CouncilIncident_NobleExemption_Abstain}Waive only the most contested portion of the obligation.",
                EndorseSuccessResult = "{=BC_CouncilIncident_NobleExemption_ResultEndorseSuccess}The exemption is accepted as measured clemency. Resistance to the wider audit briefly subsides throughout the peerage.",
                EndorseNeutralResult = "{=BC_CouncilIncident_NobleExemption_ResultEndorseNeutral}The favored house is satisfied and the remaining nobility confines its resentment to private complaint.",
                EndorseFailureResult = "{=BC_CouncilIncident_NobleExemption_ResultEndorseFailure}Every magnate discovers an ancestral privilege of equal dignity. The exemption turns a single petition into a realm-wide challenge to the audit.",
                OpposeResult = "{=BC_CouncilIncident_NobleExemption_ResultOppose}You reject the petition. The seneschal enforces the assessment, though the opportunity to quiet resistance is lost.",
                AbstainSuccessResult = "{=BC_CouncilIncident_NobleExemption_ResultAbstainSuccess}The narrow concession saves the house's dignity without inspiring a flood of rival petitions.",
                AbstainNeutralResult = "{=BC_CouncilIncident_NobleExemption_ResultAbstainNeutral}The compromise is accepted reluctantly and changes little beyond the immediate dispute.",
                AbstainFailureResult = "{=BC_CouncilIncident_NobleExemption_ResultAbstainFailure}The partial waiver pleases no one. The petitioner calls it insulting, while rival houses still denounce favoritism.",
                SuccessReason = "a well-judged noble exemption",
                FailureReason = "a divisive noble exemption",
                LimitedSuccessReason = "a narrow exemption from disputed service",
                LimitedFailureReason = "a failed compromise over disputed service"
            });
        }

        private static CouncilIncidentDefinition CreateSeneschalRoyalAssessorsEvent()
        {
            return CreateSeneschalIncident(new SeneschalIncidentTemplate
            {
                Id = SeneschalRoyalAssessorsEventId,
                AssignmentId = "seneschal_audit_vassals",
                AspectId = CouncilIncidentAspects.AuditReach,
                Title = "{=BC_CouncilIncident_RoyalAssessors_Title}The Royal Assessors",
                Description = "{=BC_CouncilIncident_RoyalAssessors_Description}{SENESCHAL_NAME}, your seneschal, proposes dispatching royal assessors through distant estates to compare local rolls, rents, and customary obligations against the records held at court. Honest commissioners could extend the audit far beyond the capital, while corrupt or overzealous ones may leave the countryside united chiefly by hatred of the Crown.",
                EffectLabel = "{=BC_CouncilIncident_RoyalAssessors_Effect}the reach of the vassal audit",
                Endorse = "{=BC_CouncilIncident_RoyalAssessors_Endorse}Dispatch a full royal commission throughout the realm.",
                Oppose = "{=BC_CouncilIncident_RoyalAssessors_Oppose}Leave distant obligations to their local officials.",
                Abstain = "{=BC_CouncilIncident_RoyalAssessors_Abstain}Send a smaller commission to selected estates.",
                EndorseSuccessResult = "{=BC_CouncilIncident_RoyalAssessors_ResultEndorseSuccess}The assessors reconcile scattered rolls with unusual discipline, bringing distant obligations within the effective reach of the exchequer.",
                EndorseNeutralResult = "{=BC_CouncilIncident_RoyalAssessors_ResultEndorseNeutral}The commission reaches the principal estates and improves collection, though many remote accounts remain uncertain.",
                EndorseFailureResult = "{=BC_CouncilIncident_RoyalAssessors_ResultEndorseFailure}Bribery, threats, and contradictory assessments discredit the commission. Distant vassals become harder to audit than before.",
                OpposeResult = "{=BC_CouncilIncident_RoyalAssessors_ResultOppose}You refuse the travelling commission. The seneschal withdraws the assessors and leaves provincial accounts beyond close scrutiny.",
                AbstainSuccessResult = "{=BC_CouncilIncident_RoyalAssessors_ResultAbstainSuccess}The limited commission uncovers useful discrepancies in several carefully chosen estates.",
                AbstainNeutralResult = "{=BC_CouncilIncident_RoyalAssessors_ResultAbstainNeutral}The assessors improve a handful of rolls without materially extending the audit's reach.",
                AbstainFailureResult = "{=BC_CouncilIncident_RoyalAssessors_ResultAbstainFailure}Even the small commission attracts local obstruction and returns with less reliable accounts than it carried out.",
                SuccessReason = "a disciplined royal assessment commission",
                FailureReason = "a discredited royal assessment commission",
                LimitedSuccessReason = "a useful provincial assessment",
                LimitedFailureReason = "a failed provincial assessment"
            });
        }

        private static CouncilIncidentDefinition CreateSeneschalCorruptGuildmasterEvent()
        {
            return CreateSeneschalIncident(new SeneschalIncidentTemplate
            {
                Id = SeneschalCorruptGuildmasterEventId,
                AssignmentId = "seneschal_subsidize_infrastructure",
                AspectId = CouncilIncidentAspects.InfrastructureDailyCost,
                IsDrawback = true,
                Title = "{=BC_CouncilIncident_CorruptGuildmaster_Title}The Corrupt Guildmaster",
                Description = "{=BC_CouncilIncident_CorruptGuildmaster_Description}{SENESCHAL_NAME}, your seneschal, presents evidence that a guildmaster entrusted with royal contracts has inflated invoices, substituted inferior materials, and divided the difference among cooperative officials. Purging the arrangement could greatly reduce expenditure, but an accusation made without sufficient proof may paralyze the realm's workshops.",
                EffectLabel = "{=BC_CouncilIncident_CorruptGuildmaster_Effect}the assignment's daily administration cost",
                Endorse = "{=BC_CouncilIncident_CorruptGuildmaster_Endorse}Arrest the guildmaster and audit every associated contract.",
                Oppose = "{=BC_CouncilIncident_CorruptGuildmaster_Oppose}Preserve the contracts rather than disrupt ongoing works.",
                Abstain = "{=BC_CouncilIncident_CorruptGuildmaster_Abstain}Replace the most suspicious contracts discreetly.",
                EndorseSuccessResult = "{=BC_CouncilIncident_CorruptGuildmaster_ResultEndorseSuccess}Confessions and duplicate invoices expose the entire scheme. Honest contracts sharply reduce the Crown's expenditure.",
                EndorseNeutralResult = "{=BC_CouncilIncident_CorruptGuildmaster_ResultEndorseNeutral}The worst abuses are removed, though several expensive contracts survive the inquiry.",
                EndorseFailureResult = "{=BC_CouncilIncident_CorruptGuildmaster_ResultEndorseFailure}The evidence collapses under scrutiny. Guilds suspend deliveries, demand compensation, and charge dearly for every interrupted contract.",
                OpposeResult = "{=BC_CouncilIncident_CorruptGuildmaster_ResultOppose}You decline to disturb the guild contracts. The seneschal obeys, and the existing expense continues unchanged.",
                AbstainSuccessResult = "{=BC_CouncilIncident_CorruptGuildmaster_ResultAbstainSuccess}Quiet replacements remove several costly intermediaries without alarming the wider guilds.",
                AbstainNeutralResult = "{=BC_CouncilIncident_CorruptGuildmaster_ResultAbstainNeutral}A few contracts improve, but the overall cost of the program remains much as before.",
                AbstainFailureResult = "{=BC_CouncilIncident_CorruptGuildmaster_ResultAbstainFailure}The guildmaster discovers the quiet inquiry and raises prices to cover the risk of further interference.",
                SuccessReason = "the exposure of corrupt infrastructure contracts",
                FailureReason = "a failed purge of royal contractors",
                LimitedSuccessReason = "a discreet reform of royal contracts",
                LimitedFailureReason = "an exposed inquiry into royal contracts"
            });
        }

        private static CouncilIncidentDefinition CreateSeneschalPeasantCorveeEvent()
        {
            return CreateSeneschalIncident(new SeneschalIncidentTemplate
            {
                Id = SeneschalPeasantCorveeEventId,
                AssignmentId = "seneschal_subsidize_infrastructure",
                AspectId = CouncilIncidentAspects.InfrastructureConstructionStrength,
                Title = "{=BC_CouncilIncident_PeasantCorvee_Title}Peasant Corvee",
                Description = "{=BC_CouncilIncident_PeasantCorvee_Description}{SENESCHAL_NAME}, your seneschal, proposes invoking customary labor obligations to place additional hands upon the realm's roads, walls, and public works. Properly organized, the levy could hasten construction considerably. Poorly managed, it would empty fields at the wrong season and send exhausted laborers home before either task was finished.",
                EffectLabel = "{=BC_CouncilIncident_PeasantCorvee_Effect}the assignment's construction bonus",
                Endorse = "{=BC_CouncilIncident_PeasantCorvee_Endorse}Call the full labor obligation across the realm.",
                Oppose = "{=BC_CouncilIncident_PeasantCorvee_Oppose}Forbid compulsory labor on the royal works.",
                Abstain = "{=BC_CouncilIncident_PeasantCorvee_Abstain}Permit a limited levy near the largest projects.",
                EndorseSuccessResult = "{=BC_CouncilIncident_PeasantCorvee_ResultEndorseSuccess}The levy is carefully rotated between villages. Fresh crews accelerate construction without abandoning the fields.",
                EndorseNeutralResult = "{=BC_CouncilIncident_PeasantCorvee_ResultEndorseNeutral}The additional labor advances several projects, though confusion and uneven attendance blunt its full promise.",
                EndorseFailureResult = "{=BC_CouncilIncident_PeasantCorvee_ResultEndorseFailure}Officials summon laborers during essential farm work, provoke desertions, and leave construction slower beneath the weight of disorder.",
                OpposeResult = "{=BC_CouncilIncident_PeasantCorvee_ResultOppose}You forbid the levy. The seneschal withdraws the order and the construction program proceeds at its normal pace.",
                AbstainSuccessResult = "{=BC_CouncilIncident_PeasantCorvee_ResultAbstainSuccess}A limited, well-timed levy supplies useful labor to the most important works.",
                AbstainNeutralResult = "{=BC_CouncilIncident_PeasantCorvee_ResultAbstainNeutral}The local levy adds hands but achieves little beyond the ordinary construction schedule.",
                AbstainFailureResult = "{=BC_CouncilIncident_PeasantCorvee_ResultAbstainFailure}Even the limited levy disrupts nearby villages and leaves workers resentful and unproductive.",
                SuccessReason = "a well-organized labor levy",
                FailureReason = "a ruinous labor levy",
                LimitedSuccessReason = "a successful local labor levy",
                LimitedFailureReason = "a failed local labor levy"
            });
        }

        private static CouncilIncidentDefinition CreateSeneschalMaterialShortagesEvent()
        {
            return CreateSeneschalIncident(new SeneschalIncidentTemplate
            {
                Id = SeneschalMaterialShortagesEventId,
                AssignmentId = "seneschal_subsidize_infrastructure",
                AspectId = CouncilIncidentAspects.InfrastructureEfficiency,
                Title = "{=BC_CouncilIncident_MaterialShortages_Title}Material Shortages",
                Description = "{=BC_CouncilIncident_MaterialShortages_Description}{SENESCHAL_NAME}, your seneschal, warns that shortages of seasoned timber, dressed stone, and workable iron are delaying projects across the realm. A centralized program of salvage, substitution, and prioritized deliveries may keep construction moving, but a mistaken allocation could consume more coin while leaving works unfinished.",
                EffectLabel = "{=BC_CouncilIncident_MaterialShortages_Effect}the efficiency of infrastructure subsidies",
                Endorse = "{=BC_CouncilIncident_MaterialShortages_Endorse}Centralize procurement and authorize substitute materials.",
                Oppose = "{=BC_CouncilIncident_MaterialShortages_Oppose}Let each settlement secure its own materials.",
                Abstain = "{=BC_CouncilIncident_MaterialShortages_Abstain}Prioritize deliveries only for the most important projects.",
                EndorseSuccessResult = "{=BC_CouncilIncident_MaterialShortages_ResultEndorseSuccess}Salvage, careful substitutions, and coordinated transport keep works supplied while reducing waste throughout the program.",
                EndorseNeutralResult = "{=BC_CouncilIncident_MaterialShortages_ResultEndorseNeutral}Central procurement keeps the principal works moving, though savings prove less dramatic than promised.",
                EndorseFailureResult = "{=BC_CouncilIncident_MaterialShortages_ResultEndorseFailure}Unsuitable materials arrive at the wrong sites. Projects stall while the Crown pays to replace ruined work and duplicate deliveries.",
                OpposeResult = "{=BC_CouncilIncident_MaterialShortages_ResultOppose}You leave procurement in local hands. The seneschal abandons the central plan and accepts the existing pace and expense.",
                AbstainSuccessResult = "{=BC_CouncilIncident_MaterialShortages_ResultAbstainSuccess}Priority deliveries sustain the most valuable works and reduce waste in their supply lines.",
                AbstainNeutralResult = "{=BC_CouncilIncident_MaterialShortages_ResultAbstainNeutral}The chosen projects receive enough material to continue, but the realm-wide program changes little.",
                AbstainFailureResult = "{=BC_CouncilIncident_MaterialShortages_ResultAbstainFailure}Concentrating deliveries deprives secondary projects without adequately supplying the favored works.",
                SuccessReason = "an efficient response to material shortages",
                FailureReason = "a wasteful response to material shortages",
                LimitedSuccessReason = "a successful priority supply program",
                LimitedFailureReason = "a failed priority supply program"
            });
        }

        private static CouncilIncidentDefinition CreateSeneschalVerminGranariesEvent()
        {
            return CreateSeneschalIncident(new SeneschalIncidentTemplate
            {
                Id = SeneschalVerminGranariesEventId,
                AssignmentId = "seneschal_stockpile_provisions",
                AspectId = CouncilIncidentAspects.ProvisionsFoodYield,
                Title = "{=BC_CouncilIncident_VerminGranaries_Title}Vermin in the Granaries",
                Description = "{=BC_CouncilIncident_VerminGranaries_Description}{SENESCHAL_NAME}, your seneschal, reports that vermin have spread through several royal granaries, consuming grain and fouling stores intended for the coming season. A coordinated cleansing and reconstruction of the storehouses may preserve far more food, but careless measures could ruin what remains.",
                EffectLabel = "{=BC_CouncilIncident_VerminGranaries_Effect}the assignment's daily food provision",
                Endorse = "{=BC_CouncilIncident_VerminGranaries_Endorse}Empty, cleanse, and repair every affected granary.",
                Oppose = "{=BC_CouncilIncident_VerminGranaries_Oppose}Leave the infestation to local stewards.",
                Abstain = "{=BC_CouncilIncident_VerminGranaries_Abstain}Treat only the largest royal storehouses.",
                EndorseSuccessResult = "{=BC_CouncilIncident_VerminGranaries_ResultEndorseSuccess}The stores are cleaned, raised from damp floors, and sealed. Far more of each delivery now reaches the granaries intact.",
                EndorseNeutralResult = "{=BC_CouncilIncident_VerminGranaries_ResultEndorseNeutral}The worst infestations are contained and losses fall, though several provincial stores remain vulnerable.",
                EndorseFailureResult = "{=BC_CouncilIncident_VerminGranaries_ResultEndorseFailure}Hasty cleansing contaminates sound grain and scatters the infestation into neighboring stores, sharply reducing usable provisions.",
                OpposeResult = "{=BC_CouncilIncident_VerminGranaries_ResultOppose}You leave the granaries under local management. The seneschal withdraws the proposed intervention.",
                AbstainSuccessResult = "{=BC_CouncilIncident_VerminGranaries_ResultAbstainSuccess}The principal stores are successfully cleansed and preserve a greater share of incoming food.",
                AbstainNeutralResult = "{=BC_CouncilIncident_VerminGranaries_ResultAbstainNeutral}The limited treatment contains visible losses but leaves the wider stockpile little changed.",
                AbstainFailureResult = "{=BC_CouncilIncident_VerminGranaries_ResultAbstainFailure}The infestation spreads from untreated stores back into the granaries selected for cleansing.",
                SuccessReason = "a successful cleansing of royal granaries",
                FailureReason = "a disastrous cleansing of royal granaries",
                LimitedSuccessReason = "a successful treatment of major granaries",
                LimitedFailureReason = "a failed treatment of major granaries"
            });
        }

        private static CouncilIncidentDefinition CreateSeneschalHoardingPanicEvent()
        {
            return CreateSeneschalIncident(new SeneschalIncidentTemplate
            {
                Id = SeneschalHoardingPanicEventId,
                AssignmentId = "seneschal_stockpile_provisions",
                AspectId = CouncilIncidentAspects.ProvisionsDailyCost,
                IsDrawback = true,
                Title = "{=BC_CouncilIncident_HoardingPanic_Title}Hoarding Panic",
                Description = "{=BC_CouncilIncident_HoardingPanic_Description}{SENESCHAL_NAME}, your seneschal, reports that merchants have begun withholding grain after learning of the Crown's purchases, expecting prices to rise further. Compulsory sales and fixed prices may break the panic, though heavy-handed intervention could drive commerce into hidden markets.",
                EffectLabel = "{=BC_CouncilIncident_HoardingPanic_Effect}the assignment's daily procurement cost",
                Endorse = "{=BC_CouncilIncident_HoardingPanic_Endorse}Impose compulsory sales at a declared royal price.",
                Oppose = "{=BC_CouncilIncident_HoardingPanic_Oppose}Continue purchasing provisions at the market rate.",
                Abstain = "{=BC_CouncilIncident_HoardingPanic_Abstain}Negotiate limited contracts with trusted merchants.",
                EndorseSuccessResult = "{=BC_CouncilIncident_HoardingPanic_ResultEndorseSuccess}The decree releases hoarded grain without emptying the markets. Procurement costs fall as confidence returns.",
                EndorseNeutralResult = "{=BC_CouncilIncident_HoardingPanic_ResultEndorseNeutral}Enough merchants comply to restrain prices, though private hoards and quiet resentment remain.",
                EndorseFailureResult = "{=BC_CouncilIncident_HoardingPanic_ResultEndorseFailure}Grain disappears into hidden warehouses and smugglers demand ruinous prices. Feeding the stockpiles becomes markedly more expensive.",
                OpposeResult = "{=BC_CouncilIncident_HoardingPanic_ResultOppose}You refuse to interfere with the markets. The seneschal continues buying at prevailing prices.",
                AbstainSuccessResult = "{=BC_CouncilIncident_HoardingPanic_ResultAbstainSuccess}Trusted merchants accept stable contracts and restrain the worst price increases.",
                AbstainNeutralResult = "{=BC_CouncilIncident_HoardingPanic_ResultAbstainNeutral}The private contracts secure provisions but do little to alter their overall cost.",
                AbstainFailureResult = "{=BC_CouncilIncident_HoardingPanic_ResultAbstainFailure}The favored merchants resell their contracts through intermediaries, adding another layer of expense.",
                SuccessReason = "a successful response to grain hoarding",
                FailureReason = "a disastrous intervention in the grain market",
                LimitedSuccessReason = "successful contracts with grain merchants",
                LimitedFailureReason = "failed contracts with grain merchants"
            });
        }

        private static CouncilIncidentDefinition CreateSeneschalSpoiledMeatEvent()
        {
            return CreateSeneschalIncident(new SeneschalIncidentTemplate
            {
                Id = SeneschalSpoiledMeatEventId,
                AssignmentId = "seneschal_stockpile_provisions",
                AspectId = CouncilIncidentAspects.ProvisionsDistributionEfficiency,
                Title = "{=BC_CouncilIncident_SpoiledMeat_Title}Spoiled Meat",
                Description = "{=BC_CouncilIncident_SpoiledMeat_Description}{SENESCHAL_NAME}, your seneschal, presents reports that shipments of salted meat have arrived rancid at several strongholds after negligent packing and long delays on the road. Reorganizing inspection and transport may prevent further waste, but rejecting entire shipments could leave garrisons with less food than if nothing were done.",
                EffectLabel = "{=BC_CouncilIncident_SpoiledMeat_Effect}the efficiency of provision distribution",
                Endorse = "{=BC_CouncilIncident_SpoiledMeat_Endorse}Inspect every shipment and rebuild the distribution chain.",
                Oppose = "{=BC_CouncilIncident_SpoiledMeat_Oppose}Accept the losses rather than disrupt deliveries.",
                Abstain = "{=BC_CouncilIncident_SpoiledMeat_Abstain}Inspect only provisions bound for vulnerable strongholds.",
                EndorseSuccessResult = "{=BC_CouncilIncident_SpoiledMeat_ResultEndorseSuccess}New inspections identify careless suppliers and reorganized routes deliver sound provisions with far less waste.",
                EndorseNeutralResult = "{=BC_CouncilIncident_SpoiledMeat_ResultEndorseNeutral}The most obvious failures are corrected, improving distribution without fully eliminating spoilage.",
                EndorseFailureResult = "{=BC_CouncilIncident_SpoiledMeat_ResultEndorseFailure}Inspectors condemn usable stores, wagons wait for replacement cargoes, and strongholds receive less food despite the Crown paying the full expense.",
                OpposeResult = "{=BC_CouncilIncident_SpoiledMeat_ResultOppose}You preserve the existing distribution system. The seneschal records the losses and makes no wider intervention.",
                AbstainSuccessResult = "{=BC_CouncilIncident_SpoiledMeat_ResultAbstainSuccess}Targeted inspections prevent spoiled shipments from reaching the strongholds least able to endure them.",
                AbstainNeutralResult = "{=BC_CouncilIncident_SpoiledMeat_ResultAbstainNeutral}The limited inspections catch several bad shipments but leave the broader distribution network unchanged.",
                AbstainFailureResult = "{=BC_CouncilIncident_SpoiledMeat_ResultAbstainFailure}Delays caused by selective inspection spoil additional cargoes before they reach their destinations.",
                SuccessReason = "a successful reform of provision distribution",
                FailureReason = "a failed reform of provision distribution",
                LimitedSuccessReason = "successful inspections of vulnerable supply routes",
                LimitedFailureReason = "failed inspections of vulnerable supply routes"
            });
        }

        private static CouncilIncidentDefinition CreateChancellorForgedLineageEvent()
        {
            return CreateChancellorIncident(new ChancellorIncidentTemplate
            {
                Id = ChancellorForgedLineageEventId,
                AssignmentId = "chancellor_fabricate_grievances",
                AspectId = CouncilIncidentAspects.FabricationProgress,
                Title = "{=BC_CouncilIncident_ForgedLineage_Title}The Forged Lineage",
                Description = "{=BC_CouncilIncident_ForgedLineage_Description}{CHANCELLOR_NAME}, your chancellor, presents a genealogist who claims to have reconstructed a forgotten branch of the ruling house from a worm-eaten register, three ambiguous seals, and the testimony of a conveniently ancient widow. The lineage could lend ancestral weight to the Crown's present claims, but if its contradictions are noticed before the story takes root, the entire fabrication may become harder to defend.",
                EffectLabel = "{=BC_CouncilIncident_ForgedLineage_Effect}the assignment's claim-fabrication progress bonus",
                Endorse = "{=BC_CouncilIncident_ForgedLineage_Endorse}Publish the reconstructed lineage and reward its learned author.",
                Oppose = "{=BC_CouncilIncident_ForgedLineage_Oppose}Burn the genealogy before scholarship becomes perjury.",
                Abstain = "{=BC_CouncilIncident_ForgedLineage_Abstain}Circulate the lineage quietly among sympathetic jurists.",
                EndorseSuccessResult = "{=BC_CouncilIncident_ForgedLineage_ResultEndorseSuccess}The genealogy is copied, cited, and embellished faster than its critics can answer. What began as conjecture soon acquires the comfortable weight of inherited truth.",
                EndorseNeutralResult = "{=BC_CouncilIncident_ForgedLineage_ResultEndorseNeutral}The lineage convinces no serious scholar, yet supplies enough plausible names and dates to accelerate the Chancellor's case.",
                EndorseFailureResult = "{=BC_CouncilIncident_ForgedLineage_ResultEndorseFailure}Rival genealogists expose impossible marriages and ancestors dead before their supposed children were born. The chancery must rebuild its case around the wreckage.",
                OpposeResult = "{=BC_CouncilIncident_ForgedLineage_ResultOppose}You order the genealogy destroyed. The chancellor obeys, lamenting the Crown's sudden devotion to historical precision.",
                AbstainSuccessResult = "{=BC_CouncilIncident_ForgedLineage_ResultAbstainSuccess}Sympathetic jurists begin citing the lineage as a possibility, giving the claim useful momentum without public commitment.",
                AbstainNeutralResult = "{=BC_CouncilIncident_ForgedLineage_ResultAbstainNeutral}The genealogy circulates quietly but attracts little notice beyond those already inclined to believe it.",
                AbstainFailureResult = "{=BC_CouncilIncident_ForgedLineage_ResultAbstainFailure}A private copy reaches a hostile scholar, forcing the Chancellor to spend valuable time explaining its contradictions.",
                SuccessReason = "a persuasive reconstruction of the royal lineage",
                FailureReason = "an exposed fabrication of the royal lineage",
                LimitedSuccessReason = "a useful private genealogy",
                LimitedFailureReason = "a compromised private genealogy"
            });
        }

        private static CouncilIncidentDefinition CreateChancellorLooseLippedInformantEvent()
        {
            return CreateChancellorIncident(new ChancellorIncidentTemplate
            {
                Id = ChancellorLooseLippedInformantEventId,
                AssignmentId = "chancellor_fabricate_grievances",
                AspectId = CouncilIncidentAspects.FabricationDiscoveryRisk,
                IsDrawback = true,
                Title = "{=BC_CouncilIncident_LooseLippedInformant_Title}The Loose-Lipped Informant",
                Description = "{=BC_CouncilIncident_LooseLippedInformant_Description}{CHANCELLOR_NAME}, your chancellor, reports that a minor clerk employed to gather seals, precedents, and household gossip has been seen drinking beyond his means and boasting that he knows which title the Crown will claim next. Authority is requested to remove the informant and reconstruct the compromised network around quieter agents. A public disappearance may silence one tongue while confirming every rumor it had spread.",
                EffectLabel = "{=BC_CouncilIncident_LooseLippedInformant_Effect}the assignment's additional discovery risk",
                Endorse = "{=BC_CouncilIncident_LooseLippedInformant_Endorse}Remove the informant and rebuild the compromised network at once.",
                Oppose = "{=BC_CouncilIncident_LooseLippedInformant_Oppose}Ignore tavern boasts rather than dignify them with royal attention.",
                Abstain = "{=BC_CouncilIncident_LooseLippedInformant_Abstain}Feed the informant false details and quietly isolate his contacts.",
                EndorseSuccessResult = "{=BC_CouncilIncident_LooseLippedInformant_ResultEndorseSuccess}The clerk vanishes into an obscure provincial office, his contacts are identified, and the chancery resumes its work behind a convincing trail of false rumors.",
                EndorseNeutralResult = "{=BC_CouncilIncident_LooseLippedInformant_ResultEndorseNeutral}The compromised agents are replaced, though fragments of the clerk's story continue circulating among suspicious courts.",
                EndorseFailureResult = "{=BC_CouncilIncident_LooseLippedInformant_ResultEndorseFailure}The clerk flees before he can be contained and carries documents proving that his tavern boasts were more than drunken invention.",
                OpposeResult = "{=BC_CouncilIncident_LooseLippedInformant_ResultOppose}You decline to act against the clerk. The chancellor withdraws the request and leaves the network to endure whatever attention follows.",
                AbstainSuccessResult = "{=BC_CouncilIncident_LooseLippedInformant_ResultAbstainSuccess}The informant enthusiastically repeats the false details, drawing suspicion away from the Chancellor's actual work.",
                AbstainNeutralResult = "{=BC_CouncilIncident_LooseLippedInformant_ResultAbstainNeutral}The clerk's stories become too contradictory to trust, but not too absurd to keep foreign agents interested.",
                AbstainFailureResult = "{=BC_CouncilIncident_LooseLippedInformant_ResultAbstainFailure}The informant recognizes the deception and sells both the true and false accounts, making the chancery's intentions easier to uncover.",
                SuccessReason = "the containment of a compromised informant",
                FailureReason = "the escape of a compromised informant",
                LimitedSuccessReason = "a successful deception of a loose-lipped informant",
                LimitedFailureReason = "a failed deception of a loose-lipped informant"
            });
        }

        private static CouncilIncidentDefinition CreateChancellorBribingClericsEvent()
        {
            return CreateChancellorIncident(new ChancellorIncidentTemplate
            {
                Id = ChancellorBribingClericsEventId,
                AssignmentId = "chancellor_fabricate_grievances",
                AspectId = CouncilIncidentAspects.FabricationWeeklyControversy,
                IsDrawback = true,
                Title = "{=BC_CouncilIncident_BribingClerics_Title}Bribing the Clerks",
                Description = "{=BC_CouncilIncident_BribingClerics_Description}{CHANCELLOR_NAME}, your chancellor, reports that the strongest charters opposing the Crown's claim lie in archives maintained by provincial clerks who jealously guard access to their registers. Generous fees, private stipends for cooperative record-keepers, and advancement for those who remember the past correctly are proposed. If the payments become public, the claim may survive while the reputation of {CHANCELLOR_NAME} does not.",
                EffectLabel = "{=BC_CouncilIncident_BribingClerics_Effect}the assignment's weekly controversy",
                Endorse = "{=BC_CouncilIncident_BribingClerics_Endorse}Fund the payments and place the archives in cooperative hands.",
                Oppose = "{=BC_CouncilIncident_BribingClerics_Oppose}Leave the clerks and their inconvenient records untouched.",
                Abstain = "{=BC_CouncilIncident_BribingClerics_Abstain}Approach only a few discreet record-keepers through intermediaries.",
                EndorseSuccessResult = "{=BC_CouncilIncident_BribingClerics_ResultEndorseSuccess}The payments pass as ordinary fees, the record-keepers become remarkably helpful, and no one publicly connects the two developments.",
                EndorseNeutralResult = "{=BC_CouncilIncident_BribingClerics_ResultEndorseNeutral}Several useful documents become difficult to locate. The arrangement inspires quiet suspicion, but little that can be proved.",
                EndorseFailureResult = "{=BC_CouncilIncident_BribingClerics_ResultEndorseFailure}An offended clerk publishes the offered sums alongside copies of the disputed charters. The Chancellor's methods become a scandal throughout the realm.",
                OpposeResult = "{=BC_CouncilIncident_BribingClerics_ResultOppose}You forbid payments to the record-keepers. The chancellor obeys, though the hostile records remain precisely where your rivals expect to find them.",
                AbstainSuccessResult = "{=BC_CouncilIncident_BribingClerics_ResultAbstainSuccess}A few discreet record-keepers rearrange the relevant shelves and discourage curious visitors without attracting wider notice.",
                AbstainNeutralResult = "{=BC_CouncilIncident_BribingClerics_ResultAbstainNeutral}The intermediaries secure limited cooperation, but the archives remain largely intact and accessible.",
                AbstainFailureResult = "{=BC_CouncilIncident_BribingClerics_ResultAbstainFailure}One intermediary boasts of his access, provoking rumors of corruption without obtaining any useful control over the records.",
                SuccessReason = "a discreet arrangement with cooperative record-keepers",
                FailureReason = "an exposed attempt to bribe archival clerks",
                LimitedSuccessReason = "a discreet approach to archival clerks",
                LimitedFailureReason = "a compromised approach to archival clerks"
            });
        }

        private static CouncilIncidentDefinition CreateChancellorExtravagantGiftEvent()
        {
            return CreateChancellorIncident(new ChancellorIncidentTemplate
            {
                Id = ChancellorExtravagantGiftEventId,
                AssignmentId = "chancellor_improve_foreign_relations",
                AspectId = CouncilIncidentAspects.ForeignRelationsStrength,
                Title = "{=BC_CouncilIncident_ExtravagantGift_Title}The Extravagant Gift",
                Description = "{=BC_CouncilIncident_ExtravagantGift_Description}{CHANCELLOR_NAME}, your chancellor, brings reports from a neighboring court of a ruler whose opinion of you has hardened into open disdain. A conspicuous embassy is proposed, bearing worked silver, rare cloth, and horses chosen from the royal stables. A gift judged generous may reopen correspondence; one judged desperate, vulgar, or insufficiently grand will purchase only laughter at your expense.",
                EffectLabel = "{=BC_CouncilIncident_ExtravagantGift_Effect}weekly relation improvement with neighboring rulers",
                Endorse = "{=BC_CouncilIncident_ExtravagantGift_Endorse}Send an embassy laden with gifts worthy of a sovereign.",
                Oppose = "{=BC_CouncilIncident_ExtravagantGift_Oppose}Keep the treasury closed and let foreign rulers master their own tempers.",
                Abstain = "{=BC_CouncilIncident_ExtravagantGift_Abstain}Send a courteous but restrained diplomatic gift.",
                EndorseSuccessResult = "{=BC_CouncilIncident_ExtravagantGift_ResultEndorseSuccess}The embassy enters the neighboring court in magnificent procession. The gifts are praised, old correspondence resumes, and foreign audiences become markedly warmer.",
                EndorseNeutralResult = "{=BC_CouncilIncident_ExtravagantGift_ResultEndorseNeutral}The gifts are accepted with correct ceremony. They do not inspire affection, but they soften the coldness surrounding further negotiations.",
                EndorseFailureResult = "{=BC_CouncilIncident_ExtravagantGift_ResultEndorseFailure}The embassy's display is mocked as clumsy extravagance intended to conceal weakness. Foreign courtiers enjoy the gifts while respecting you less for sending them.",
                OpposeResult = "{=BC_CouncilIncident_ExtravagantGift_ResultOppose}You reject the proposed embassy. The chancellor obeys, but warns that injured foreign pride can prove dearer than silver.",
                AbstainSuccessResult = "{=BC_CouncilIncident_ExtravagantGift_ResultAbstainSuccess}The restrained gift is praised for its taste and sincerity, making diplomatic correspondence easier for a time.",
                AbstainNeutralResult = "{=BC_CouncilIncident_ExtravagantGift_ResultAbstainNeutral}The modest embassy is received politely and departs without altering the wider relationship.",
                AbstainFailureResult = "{=BC_CouncilIncident_ExtravagantGift_ResultAbstainFailure}The restrained gift is interpreted as a deliberate slight, chilling the very correspondence it was meant to restore.",
                SuccessReason = "a celebrated diplomatic embassy",
                FailureReason = "a humiliated diplomatic embassy",
                LimitedSuccessReason = "a well-judged diplomatic gift",
                LimitedFailureReason = "an insulting diplomatic gift"
            });
        }

        private static CouncilIncidentDefinition CreateChancellorMatchmakerEvent()
        {
            return CreateChancellorIncident(new ChancellorIncidentTemplate
            {
                Id = ChancellorMatchmakerEventId,
                AssignmentId = "chancellor_improve_foreign_relations",
                AspectId = CouncilIncidentAspects.ForeignRelationsReach,
                Title = "{=BC_CouncilIncident_Matchmaker_Title}The Matchmaker",
                Description = "{=BC_CouncilIncident_Matchmaker_Description}{CHANCELLOR_NAME}, your chancellor, proposes sponsoring a season of hunts, dances, and carefully supervised correspondence between the unmarried nobility of your realm and neighboring courts. No binding marriage is promised, but the expectation of future matches may place several foreign rulers in a more receptive mood. Poorly chosen introductions could instead produce insult, scandal, and competing expectations.",
                EffectLabel = "{=BC_CouncilIncident_Matchmaker_Effect}the diplomatic reach of the chancery",
                Endorse = "{=BC_CouncilIncident_Matchmaker_Endorse}Sponsor the introductions and let the Chancellor arrange the season.",
                Oppose = "{=BC_CouncilIncident_Matchmaker_Oppose}Keep dynastic courtship beyond the Chancellor's reach.",
                Abstain = "{=BC_CouncilIncident_Matchmaker_Abstain}Permit only a small exchange of letters and portraits.",
                EndorseSuccessResult = "{=BC_CouncilIncident_Matchmaker_ResultEndorseSuccess}The season produces promising attachments and abundant flattering correspondence. Several neighboring courts begin treating your chancery as a welcome intermediary.",
                EndorseNeutralResult = "{=BC_CouncilIncident_Matchmaker_ResultEndorseNeutral}No celebrated match emerges, but the introductions create enough goodwill to broaden the Chancellor's foreign contacts.",
                EndorseFailureResult = "{=BC_CouncilIncident_Matchmaker_ResultEndorseFailure}Competing expectations collapse into insult. Portraits are returned, letters become evidence, and foreign courts grow wary of every chancery introduction.",
                OpposeResult = "{=BC_CouncilIncident_Matchmaker_ResultOppose}You forbid the Chancellor from meddling in courtship. The office withdraws from the matter, displeased at losing a useful instrument of diplomacy.",
                AbstainSuccessResult = "{=BC_CouncilIncident_Matchmaker_ResultAbstainSuccess}The discreet exchange creates promising correspondence without binding any house to a formal match.",
                AbstainNeutralResult = "{=BC_CouncilIncident_Matchmaker_ResultAbstainNeutral}A few portraits and courteous letters cross the borders, but foreign relations remain largely unchanged.",
                AbstainFailureResult = "{=BC_CouncilIncident_Matchmaker_ResultAbstainFailure}A private letter reaches the wrong hands, making neighboring courts suspicious of the Chancellor's intentions.",
                SuccessReason = "a successful season of diplomatic courtship",
                FailureReason = "a scandalous season of diplomatic courtship",
                LimitedSuccessReason = "a discreet exchange of diplomatic introductions",
                LimitedFailureReason = "a compromised exchange of diplomatic introductions"
            });
        }

        private static CouncilIncidentDefinition CreateChancellorBorderApologyEvent()
        {
            return CreateChancellorIncident(new ChancellorIncidentTemplate
            {
                Id = ChancellorBorderApologyEventId,
                AssignmentId = "chancellor_improve_foreign_relations",
                AspectId = CouncilIncidentAspects.ForeignRelationsWeeklyControversy,
                IsDrawback = true,
                Title = "{=BC_CouncilIncident_BorderApology_Title}The Border Apology",
                Description = "{=BC_CouncilIncident_BorderApology_Description}{CHANCELLOR_NAME}, your chancellor, reports that retainers from your realm have crossed a disputed boundary, seized livestock, and exchanged blows with men serving a neighboring lord. A formal apology is recommended before the quarrel grows into a matter of sovereign honor. If phrased with care, the apology may demonstrate strength under discipline. If it sounds like submission, {CHANCELLOR_NAME} will be condemned at home without satisfying anyone abroad.",
                EffectLabel = "{=BC_CouncilIncident_BorderApology_Effect}the assignment's weekly controversy",
                Endorse = "{=BC_CouncilIncident_BorderApology_Endorse}Issue a formal apology and compensate the injured border lord.",
                Oppose = "{=BC_CouncilIncident_BorderApology_Oppose}Deny wrongdoing and refuse to answer foreign complaints.",
                Abstain = "{=BC_CouncilIncident_BorderApology_Abstain}Permit a local apology without admitting fault on behalf of the Crown.",
                EndorseSuccessResult = "{=BC_CouncilIncident_BorderApology_ResultEndorseSuccess}The apology is received as the measured act of a confident sovereign. The border quiets, and criticism of the Chancellor's foreign policy briefly subsides.",
                EndorseNeutralResult = "{=BC_CouncilIncident_BorderApology_ResultEndorseNeutral}Compensation is accepted and the immediate quarrel ends, though neither court abandons its account of the disputed boundary.",
                EndorseFailureResult = "{=BC_CouncilIncident_BorderApology_ResultEndorseFailure}The neighboring court publishes the apology as an admission of weakness. Your own nobles denounce the Chancellor for surrendering honor without securing peace.",
                OpposeResult = "{=BC_CouncilIncident_BorderApology_ResultOppose}You refuse to apologize for the border incident. The chancellor complies, but regards the decision as an invitation to a larger quarrel.",
                AbstainSuccessResult = "{=BC_CouncilIncident_BorderApology_ResultAbstainSuccess}A local settlement compensates the injured parties without compromising the dignity of either Crown.",
                AbstainNeutralResult = "{=BC_CouncilIncident_BorderApology_ResultAbstainNeutral}The local apology quiets the retainers but leaves the underlying diplomatic grievance untouched.",
                AbstainFailureResult = "{=BC_CouncilIncident_BorderApology_ResultAbstainFailure}The carefully limited apology satisfies no one and is attacked at court as both cowardly and insincere.",
                SuccessReason = "a dignified settlement of a border incident",
                FailureReason = "a humiliating apology for a border incident",
                LimitedSuccessReason = "a successful local border settlement",
                LimitedFailureReason = "a failed local border settlement"
            });
        }

        private static CouncilIncidentDefinition CreateChancellorBanquetEvent()
        {
            return CreateChancellorIncident(new ChancellorIncidentTemplate
            {
                Id = ChancellorBanquetEventId,
                AssignmentId = "chancellor_appease_nobles",
                AspectId = CouncilIncidentAspects.NobleAppeasementStrength,
                Title = "{=BC_CouncilIncident_Banquet_Title}The Banquet",
                Description = "{=BC_CouncilIncident_Banquet_Description}{CHANCELLOR_NAME}, your chancellor, warns that old grievances are souring the atmosphere at court and proposes a royal banquet for the realm's most estranged loyal vassals. Carefully judged honors, generous hospitality, and a seating plan negotiated in advance might restore warmth between Crown and peerage. One careless precedence, however, could turn a feast of reconciliation into a public inventory of every slight the nobility believes it has suffered.",
                EffectLabel = "{=BC_CouncilIncident_Banquet_Effect}weekly relation improvement with loyal vassals",
                Endorse = "{=BC_CouncilIncident_Banquet_Endorse}Open the royal cellars and receive the nobility in splendor.",
                Oppose = "{=BC_CouncilIncident_Banquet_Oppose}Refuse to purchase loyalty with spectacle and wine.",
                Abstain = "{=BC_CouncilIncident_Banquet_Abstain}Host a restrained supper for the most aggrieved houses.",
                EndorseSuccessResult = "{=BC_CouncilIncident_Banquet_ResultEndorseSuccess}The banquet is remembered as a triumph of courtesy. Old enemies share cups, wounded pride is soothed, and the Chancellor's invitations begin opening doors throughout the peerage.",
                EndorseNeutralResult = "{=BC_CouncilIncident_Banquet_ResultEndorseNeutral}The tables are full and the speeches polite. Few grievances are forgotten, but several estranged lords leave court less hostile than they arrived.",
                EndorseFailureResult = "{=BC_CouncilIncident_Banquet_ResultEndorseFailure}A dispute over precedence poisons the evening. Every misplaced chair and withheld toast becomes fresh evidence of royal contempt.",
                OpposeResult = "{=BC_CouncilIncident_Banquet_ResultOppose}You refuse the proposed banquet. The chancellor obeys, but warns that injured pride rarely grows cheaper with time.",
                AbstainSuccessResult = "{=BC_CouncilIncident_Banquet_ResultAbstainSuccess}The smaller supper allows grievances to be heard without an audience, and several strained relationships begin to mend.",
                AbstainNeutralResult = "{=BC_CouncilIncident_Banquet_ResultAbstainNeutral}The restrained gathering remains civil but cautious, changing little beyond the mood of a few conversations.",
                AbstainFailureResult = "{=BC_CouncilIncident_Banquet_ResultAbstainFailure}Even the private supper founders on questions of rank and favor, leaving its guests more suspicious than before.",
                SuccessReason = "a successful banquet of reconciliation",
                FailureReason = "a disastrous banquet of reconciliation",
                LimitedSuccessReason = "a successful private supper with estranged vassals",
                LimitedFailureReason = "a failed private supper with estranged vassals"
            });
        }

        private static CouncilIncidentDefinition CreateChancellorHuntingDisputeEvent()
        {
            return CreateChancellorIncident(new ChancellorIncidentTemplate
            {
                Id = ChancellorHuntingDisputeEventId,
                AssignmentId = "chancellor_appease_nobles",
                AspectId = CouncilIncidentAspects.NobleAppeasementReach,
                Title = "{=BC_CouncilIncident_HuntingDispute_Title}The Hunting Dispute",
                Description = "{=BC_CouncilIncident_HuntingDispute_Description}{CHANCELLOR_NAME}, your chancellor, reports that a prized stag has been brought down in woodland claimed by two noble houses, where armed retainers now guard the disputed carcass as though it were a captured banner. A royal arbitration could settle both the hunting rights and the older quarrels gathered around them. A clumsy judgment would merely give two aggrieved families a common grievance against the Crown.",
                EffectLabel = "{=BC_CouncilIncident_HuntingDispute_Effect}the reach of noble appeasement",
                Endorse = "{=BC_CouncilIncident_HuntingDispute_Endorse}Summon both houses and arbitrate the dispute before the full court.",
                Oppose = "{=BC_CouncilIncident_HuntingDispute_Oppose}Leave the nobles to settle their own hunting rights.",
                Abstain = "{=BC_CouncilIncident_HuntingDispute_Abstain}Send commissioners to negotiate a narrow compromise.",
                EndorseSuccessResult = "{=BC_CouncilIncident_HuntingDispute_ResultEndorseSuccess}The judgment balances custom, dignity, and practical access so deftly that both houses claim to have prevailed. The chancery gains new credibility as a mediator among the peerage.",
                EndorseNeutralResult = "{=BC_CouncilIncident_HuntingDispute_ResultEndorseNeutral}Neither house is satisfied, yet both accept the boundary and return their retainers home. The compromise gives the Chancellor room to address more than one grievance at a time.",
                EndorseFailureResult = "{=BC_CouncilIncident_HuntingDispute_ResultEndorseFailure}The ruling offends both claimants. Their retainers withdraw, but the dispute now joins a longer catalogue of insults attributed to the Crown.",
                OpposeResult = "{=BC_CouncilIncident_HuntingDispute_ResultOppose}You decline to intervene in the hunting dispute. The chancellor fears that private quarrels left unattended will eventually arrive at court bearing drawn swords.",
                AbstainSuccessResult = "{=BC_CouncilIncident_HuntingDispute_ResultAbstainSuccess}The commissioners establish shared rights and compensation, allowing the chancery to calm both houses without a public contest of honor.",
                AbstainNeutralResult = "{=BC_CouncilIncident_HuntingDispute_ResultAbstainNeutral}The commissioners secure a temporary boundary, though neither family considers the deeper dispute resolved.",
                AbstainFailureResult = "{=BC_CouncilIncident_HuntingDispute_ResultAbstainFailure}The commissioners are dismissed as meddling clerks, and both houses emerge less receptive to chancery mediation.",
                SuccessReason = "a masterful arbitration of noble hunting rights",
                FailureReason = "a failed arbitration of noble hunting rights",
                LimitedSuccessReason = "a successful compromise over noble hunting rights",
                LimitedFailureReason = "a failed compromise over noble hunting rights"
            });
        }

        private static CouncilIncidentDefinition CreateChancellorVanityTitleEvent()
        {
            return CreateChancellorIncident(new ChancellorIncidentTemplate
            {
                Id = ChancellorVanityTitleEventId,
                AssignmentId = "chancellor_appease_nobles",
                AspectId = CouncilIncidentAspects.NobleAppeasementWeeklyControversy,
                IsDrawback = true,
                Title = "{=BC_CouncilIncident_VanityTitle_Title}A Vanity Title",
                Description = "{=BC_CouncilIncident_VanityTitle_Description}{CHANCELLOR_NAME}, your chancellor, proposes placating a particularly difficult magnate with a splendid ceremonial dignity: an honor rich in robes, precedence, and flattering forms of address, but carrying no land or lawful authority. Properly presented, it may satisfy wounded pride at little cost. If the peerage recognizes it as an empty invention, both recipient and {CHANCELLOR_NAME} may become objects of ridicule.",
                EffectLabel = "{=BC_CouncilIncident_VanityTitle_Effect}the assignment's weekly controversy",
                Endorse = "{=BC_CouncilIncident_VanityTitle_Endorse}Create the ceremonial dignity and present it with royal solemnity.",
                Oppose = "{=BC_CouncilIncident_VanityTitle_Oppose}Refuse to cheapen noble rank with invented honors.",
                Abstain = "{=BC_CouncilIncident_VanityTitle_Abstain}Offer a private commendation without creating a public dignity.",
                EndorseSuccessResult = "{=BC_CouncilIncident_VanityTitle_ResultEndorseSuccess}The new dignity is received as a mark of exceptional royal confidence. Its recipient is delighted, while the rest of the peerage treats the honor with sufficient seriousness.",
                EndorseNeutralResult = "{=BC_CouncilIncident_VanityTitle_ResultEndorseNeutral}The ceremonial title pleases its recipient and draws only muted amusement from court. For a time, the Chancellor's appeasement attracts less resentment.",
                EndorseFailureResult = "{=BC_CouncilIncident_VanityTitle_ResultEndorseFailure}The court immediately brands the dignity an empty bauble. Its recipient feels mocked, rival nobles feel slighted, and the Chancellor is blamed for debasing honor itself.",
                OpposeResult = "{=BC_CouncilIncident_VanityTitle_ResultOppose}You reject the invented dignity. The chancellor accepts the rebuke, though the estranged magnate remains entirely unappeased.",
                AbstainSuccessResult = "{=BC_CouncilIncident_VanityTitle_ResultAbstainSuccess}A private commendation flatters the intended noble without provoking jealousy among the wider peerage.",
                AbstainNeutralResult = "{=BC_CouncilIncident_VanityTitle_ResultAbstainNeutral}The private honor is received politely and soon forgotten, leaving chancery policy unchanged.",
                AbstainFailureResult = "{=BC_CouncilIncident_VanityTitle_ResultAbstainFailure}Word of the private commendation spreads in distorted form, provoking whispers of secret favorites and purchased loyalty.",
                SuccessReason = "a well-received ceremonial dignity",
                FailureReason = "a ridiculed ceremonial dignity",
                LimitedSuccessReason = "a tactful private commendation",
                LimitedFailureReason = "a divisive private commendation"
            });
        }

        private static CouncilIncidentDefinition CreateChancellorIncident(ChancellorIncidentTemplate template)
        {
            return new CouncilIncidentDefinition(
                template.Id,
                template.AssignmentId,
                PrivyCouncilOffice.Chancellor,
                IncidentsCampaignBehaviour.IncidentType.FiefManagement,
                1f,
                context => new TextObject(template.Title),
                context =>
                {
                    TextObject description = new TextObject(template.Description);
                    description.SetTextVariable(
                        "CHANCELLOR_NAME",
                        context?.Holder?.Leader?.Name ?? context?.Holder?.Name ?? TextObject.GetEmpty());
                    return description;
                },
                context => BuildChancellorIncidentOptions(template));
        }

        private static List<CouncilIncidentOptionDefinition> BuildChancellorIncidentOptions(
            ChancellorIncidentTemplate template)
        {
            return new List<CouncilIncidentOptionDefinition>
            {
                new CouncilIncidentOptionDefinition(
                    template.Endorse,
                    context => BuildChancellorEndorseHints(context, template),
                    context => ApplyChancellorEndorse(context, template)),
                new CouncilIncidentOptionDefinition(
                    template.Oppose,
                    BuildChancellorOpposeHints,
                    context => ApplyChancellorOppose(context, template)),
                new CouncilIncidentOptionDefinition(
                    template.Abstain,
                    context => BuildChancellorAbstainHints(context, template),
                    context => ApplyChancellorAbstain(context, template))
            };
        }

        private static List<TextObject> BuildChancellorEndorseHints(
            CouncilIncidentContext context,
            ChancellorIncidentTemplate template)
        {
            string success = template.IsDrawback
                ? "{=BC_CouncilIncident_Chancellor_DrawbackEndorseSuccess}{CHANCE}% chance: reduce {EFFECT} by 75% for {DAYS} days and reduce Chancellor controversy by 5."
                : "{=BC_CouncilIncident_Chancellor_BenefitEndorseSuccess}{CHANCE}% chance: double {EFFECT} for {DAYS} days and reduce Chancellor controversy by 5.";
            string neutral = template.IsDrawback
                ? "{=BC_CouncilIncident_Chancellor_DrawbackEndorseNeutral}{CHANCE}% chance: reduce {EFFECT} by 50% for {DAYS} days."
                : "{=BC_CouncilIncident_Chancellor_BenefitEndorseNeutral}{CHANCE}% chance: strengthen {EFFECT} by 50% for {DAYS} days.";
            string failure = template.IsDrawback
                ? "{=BC_CouncilIncident_Chancellor_DrawbackEndorseFailure}{CHANCE}% chance: double {EFFECT} for {DAYS} days and increase Chancellor controversy by 10."
                : "{=BC_CouncilIncident_Chancellor_BenefitEndorseFailure}{CHANCE}% chance: halve {EFFECT} for {DAYS} days and increase Chancellor controversy by 10.";

            return new List<TextObject>
            {
                ChanceEffectText(success, context.SuccessChance, BellumCivileConstants.CouncilIncidentEndorsedEffectDays, template.EffectLabel),
                ChanceEffectText(neutral, context.NeutralChance, BellumCivileConstants.CouncilIncidentEndorsedEffectDays, template.EffectLabel),
                ChanceEffectText(failure, context.FailureChance, BellumCivileConstants.CouncilIncidentEndorsedEffectDays, template.EffectLabel),
                new TextObject("{=BC_CouncilIncident_Chancellor_EndorseRelation}Gain 5 relation with your chancellor.")
            };
        }

        private static List<TextObject> BuildChancellorOpposeHints(CouncilIncidentContext context)
        {
            return new List<TextObject>
            {
                new TextObject("{=BC_CouncilIncident_Chancellor_OpposeHint}The assignment remains unchanged. Lose 10 relation with your chancellor.")
            };
        }

        private static List<TextObject> BuildChancellorAbstainHints(
            CouncilIncidentContext context,
            ChancellorIncidentTemplate template)
        {
            string success = template.IsDrawback
                ? "{=BC_CouncilIncident_Chancellor_DrawbackAbstainSuccess}{CHANCE}% chance: reduce {EFFECT} by 50% for {DAYS} days and reduce Chancellor controversy by 2."
                : "{=BC_CouncilIncident_Chancellor_BenefitAbstainSuccess}{CHANCE}% chance: strengthen {EFFECT} by 50% for {DAYS} days and reduce Chancellor controversy by 2.";
            string failure = template.IsDrawback
                ? "{=BC_CouncilIncident_Chancellor_DrawbackAbstainFailure}{CHANCE}% chance: increase {EFFECT} by 50% for {DAYS} days and increase Chancellor controversy by 5."
                : "{=BC_CouncilIncident_Chancellor_BenefitAbstainFailure}{CHANCE}% chance: weaken {EFFECT} by 25% for {DAYS} days and increase Chancellor controversy by 5.";

            return new List<TextObject>
            {
                ChanceEffectText(success, context.SuccessChance, BellumCivileConstants.CouncilIncidentLimitedTrialEffectDays, template.EffectLabel),
                ChanceEffectText("{=BC_CouncilIncident_Chancellor_AbstainNeutral}{CHANCE}% chance: the assignment continues unchanged.", context.NeutralChance, 0, template.EffectLabel),
                ChanceEffectText(failure, context.FailureChance, BellumCivileConstants.CouncilIncidentLimitedTrialEffectDays, template.EffectLabel)
            };
        }

        private static List<TextObject> ApplyChancellorEndorse(
            CouncilIncidentContext context,
            ChancellorIncidentTemplate template)
        {
            context.ChangeHolderRelation(5);
            switch (context.Quality)
            {
                case CouncilIncidentQuality.Success:
                    ApplyChancellorAspect(context, template, template.IsDrawback ? 0.25f : 2f,
                        BellumCivileConstants.CouncilIncidentEndorsedEffectDays);
                    context.ChangeOfficeControversy(-5f, template.SuccessReason);
                    return Result(template.EndorseSuccessResult);
                case CouncilIncidentQuality.Neutral:
                    ApplyChancellorAspect(context, template, template.IsDrawback ? 0.5f : 1.5f,
                        BellumCivileConstants.CouncilIncidentEndorsedEffectDays);
                    return Result(template.EndorseNeutralResult);
                default:
                    ApplyChancellorAspect(context, template, template.IsDrawback ? 2f : 0.5f,
                        BellumCivileConstants.CouncilIncidentEndorsedEffectDays);
                    context.ChangeOfficeControversy(10f, template.FailureReason);
                    return Result(template.EndorseFailureResult);
            }
        }

        private static List<TextObject> ApplyChancellorOppose(
            CouncilIncidentContext context,
            ChancellorIncidentTemplate template)
        {
            context.ChangeHolderRelation(-10);
            return Result(template.OpposeResult);
        }

        private static List<TextObject> ApplyChancellorAbstain(
            CouncilIncidentContext context,
            ChancellorIncidentTemplate template)
        {
            switch (context.Quality)
            {
                case CouncilIncidentQuality.Success:
                    ApplyChancellorAspect(context, template, 0.5f + (template.IsDrawback ? 0f : 1f),
                        BellumCivileConstants.CouncilIncidentLimitedTrialEffectDays);
                    context.ChangeOfficeControversy(-2f, template.LimitedSuccessReason);
                    return Result(template.AbstainSuccessResult);
                case CouncilIncidentQuality.Neutral:
                    return Result(template.AbstainNeutralResult);
                default:
                    ApplyChancellorAspect(context, template, template.IsDrawback ? 1.5f : 0.75f,
                        BellumCivileConstants.CouncilIncidentLimitedTrialEffectDays);
                    context.ChangeOfficeControversy(5f, template.LimitedFailureReason);
                    return Result(template.AbstainFailureResult);
            }
        }

        private static void ApplyChancellorAspect(
            CouncilIncidentContext context,
            ChancellorIncidentTemplate template,
            float multiplier,
            float durationDays)
        {
            context.ApplyAspectMultiplier(template.AspectId, template.Id, multiplier, durationDays);
        }

        private static CouncilIncidentDefinition CreateSeneschalIncident(SeneschalIncidentTemplate template)
        {
            return new CouncilIncidentDefinition(
                template.Id,
                template.AssignmentId,
                PrivyCouncilOffice.Seneschal,
                IncidentsCampaignBehaviour.IncidentType.FiefManagement,
                1f,
                context => new TextObject(template.Title),
                context =>
                {
                    TextObject description = new TextObject(template.Description);
                    description.SetTextVariable(
                        "SENESCHAL_NAME",
                        context?.Holder?.Leader?.Name ?? context?.Holder?.Name ?? TextObject.GetEmpty());
                    return description;
                },
                context => BuildSeneschalIncidentOptions(template));
        }

        private static List<CouncilIncidentOptionDefinition> BuildSeneschalIncidentOptions(
            SeneschalIncidentTemplate template)
        {
            return new List<CouncilIncidentOptionDefinition>
            {
                new CouncilIncidentOptionDefinition(
                    template.Endorse,
                    context => BuildSeneschalEndorseHints(context, template),
                    context => ApplySeneschalEndorse(context, template)),
                new CouncilIncidentOptionDefinition(
                    template.Oppose,
                    BuildSeneschalOpposeHints,
                    context => ApplySeneschalOppose(context, template)),
                new CouncilIncidentOptionDefinition(
                    template.Abstain,
                    context => BuildSeneschalAbstainHints(context, template),
                    context => ApplySeneschalAbstain(context, template))
            };
        }

        private static List<TextObject> BuildSeneschalEndorseHints(
            CouncilIncidentContext context,
            SeneschalIncidentTemplate template)
        {
            string success = template.IsDrawback
                ? "{=BC_CouncilIncident_Seneschal_DrawbackEndorseSuccess}{CHANCE}% chance: reduce {EFFECT} by 75% for {DAYS} days and reduce Seneschal controversy by 5."
                : "{=BC_CouncilIncident_Seneschal_BenefitEndorseSuccess}{CHANCE}% chance: double {EFFECT} for {DAYS} days and reduce Seneschal controversy by 5.";
            string neutral = template.IsDrawback
                ? "{=BC_CouncilIncident_Seneschal_DrawbackEndorseNeutral}{CHANCE}% chance: reduce {EFFECT} by 50% for {DAYS} days."
                : "{=BC_CouncilIncident_Seneschal_BenefitEndorseNeutral}{CHANCE}% chance: strengthen {EFFECT} by 50% for {DAYS} days.";
            string failure = template.IsDrawback
                ? "{=BC_CouncilIncident_Seneschal_DrawbackEndorseFailure}{CHANCE}% chance: double {EFFECT} for {DAYS} days and increase Seneschal controversy by 10."
                : "{=BC_CouncilIncident_Seneschal_BenefitEndorseFailure}{CHANCE}% chance: halve {EFFECT} for {DAYS} days and increase Seneschal controversy by 10.";

            return new List<TextObject>
            {
                ChanceEffectText(success, context.SuccessChance, BellumCivileConstants.CouncilIncidentEndorsedEffectDays, template.EffectLabel),
                ChanceEffectText(neutral, context.NeutralChance, BellumCivileConstants.CouncilIncidentEndorsedEffectDays, template.EffectLabel),
                ChanceEffectText(failure, context.FailureChance, BellumCivileConstants.CouncilIncidentEndorsedEffectDays, template.EffectLabel),
                new TextObject("{=BC_CouncilIncident_Seneschal_EndorseRelation}Gain 5 relation with your seneschal.")
            };
        }

        private static List<TextObject> BuildSeneschalOpposeHints(CouncilIncidentContext context)
        {
            return new List<TextObject>
            {
                new TextObject("{=BC_CouncilIncident_Seneschal_OpposeHint}The assignment remains unchanged. Lose 10 relation with your seneschal.")
            };
        }

        private static List<TextObject> BuildSeneschalAbstainHints(
            CouncilIncidentContext context,
            SeneschalIncidentTemplate template)
        {
            string success = template.IsDrawback
                ? "{=BC_CouncilIncident_Seneschal_DrawbackAbstainSuccess}{CHANCE}% chance: reduce {EFFECT} by 50% for {DAYS} days and reduce Seneschal controversy by 2."
                : "{=BC_CouncilIncident_Seneschal_BenefitAbstainSuccess}{CHANCE}% chance: strengthen {EFFECT} by 50% for {DAYS} days and reduce Seneschal controversy by 2.";
            string failure = template.IsDrawback
                ? "{=BC_CouncilIncident_Seneschal_DrawbackAbstainFailure}{CHANCE}% chance: increase {EFFECT} by 50% for {DAYS} days and increase Seneschal controversy by 5."
                : "{=BC_CouncilIncident_Seneschal_BenefitAbstainFailure}{CHANCE}% chance: weaken {EFFECT} by 25% for {DAYS} days and increase Seneschal controversy by 5.";

            return new List<TextObject>
            {
                ChanceEffectText(success, context.SuccessChance, BellumCivileConstants.CouncilIncidentLimitedTrialEffectDays, template.EffectLabel),
                ChanceEffectText("{=BC_CouncilIncident_Seneschal_AbstainNeutral}{CHANCE}% chance: the assignment continues unchanged.", context.NeutralChance, 0, template.EffectLabel),
                ChanceEffectText(failure, context.FailureChance, BellumCivileConstants.CouncilIncidentLimitedTrialEffectDays, template.EffectLabel)
            };
        }

        private static List<TextObject> ApplySeneschalEndorse(
            CouncilIncidentContext context,
            SeneschalIncidentTemplate template)
        {
            context.ChangeHolderRelation(5);
            switch (context.Quality)
            {
                case CouncilIncidentQuality.Success:
                    ApplySeneschalAspect(context, template, template.IsDrawback ? 0.25f : 2f,
                        BellumCivileConstants.CouncilIncidentEndorsedEffectDays);
                    context.ChangeOfficeControversy(-5f, template.SuccessReason);
                    return Result(template.EndorseSuccessResult);
                case CouncilIncidentQuality.Neutral:
                    ApplySeneschalAspect(context, template, template.IsDrawback ? 0.5f : 1.5f,
                        BellumCivileConstants.CouncilIncidentEndorsedEffectDays);
                    return Result(template.EndorseNeutralResult);
                default:
                    ApplySeneschalAspect(context, template, template.IsDrawback ? 2f : 0.5f,
                        BellumCivileConstants.CouncilIncidentEndorsedEffectDays);
                    context.ChangeOfficeControversy(10f, template.FailureReason);
                    return Result(template.EndorseFailureResult);
            }
        }

        private static List<TextObject> ApplySeneschalOppose(
            CouncilIncidentContext context,
            SeneschalIncidentTemplate template)
        {
            context.ChangeHolderRelation(-10);
            return Result(template.OpposeResult);
        }

        private static List<TextObject> ApplySeneschalAbstain(
            CouncilIncidentContext context,
            SeneschalIncidentTemplate template)
        {
            switch (context.Quality)
            {
                case CouncilIncidentQuality.Success:
                    ApplySeneschalAspect(context, template, template.IsDrawback ? 0.5f : 1.5f,
                        BellumCivileConstants.CouncilIncidentLimitedTrialEffectDays);
                    context.ChangeOfficeControversy(-2f, template.LimitedSuccessReason);
                    return Result(template.AbstainSuccessResult);
                case CouncilIncidentQuality.Neutral:
                    return Result(template.AbstainNeutralResult);
                default:
                    ApplySeneschalAspect(context, template, template.IsDrawback ? 1.5f : 0.75f,
                        BellumCivileConstants.CouncilIncidentLimitedTrialEffectDays);
                    context.ChangeOfficeControversy(5f, template.LimitedFailureReason);
                    return Result(template.AbstainFailureResult);
            }
        }

        private static void ApplySeneschalAspect(
            CouncilIncidentContext context,
            SeneschalIncidentTemplate template,
            float multiplier,
            float durationDays)
        {
            context.ApplyAspectMultiplier(template.AspectId, template.Id, multiplier, durationDays);
        }

        private static CouncilIncidentDefinition CreateSpymasterIncident(SpymasterIncidentTemplate template)
        {
            return new CouncilIncidentDefinition(
                template.Id,
                template.AssignmentId,
                PrivyCouncilOffice.Spymaster,
                IncidentsCampaignBehaviour.IncidentType.FiefManagement,
                1f,
                context => new TextObject(template.Title),
                context =>
                {
                    TextObject description = new TextObject(template.Description);
                    description.SetTextVariable(
                        "SPYMASTER_NAME",
                        context?.Holder?.Leader?.Name ?? context?.Holder?.Name ?? TextObject.GetEmpty());
                    return description;
                },
                context => BuildSpymasterIncidentOptions(template));
        }

        private static List<CouncilIncidentOptionDefinition> BuildSpymasterIncidentOptions(
            SpymasterIncidentTemplate template)
        {
            return new List<CouncilIncidentOptionDefinition>
            {
                new CouncilIncidentOptionDefinition(
                    template.Endorse,
                    context => BuildSpymasterEndorseHints(context, template),
                    context => ApplySpymasterEndorse(context, template)),
                new CouncilIncidentOptionDefinition(
                    template.Oppose,
                    BuildSpymasterOpposeHints,
                    context => ApplySpymasterOppose(context, template)),
                new CouncilIncidentOptionDefinition(
                    template.Abstain,
                    context => BuildSpymasterAbstainHints(context, template),
                    context => ApplySpymasterAbstain(context, template))
            };
        }

        private static List<TextObject> BuildSpymasterEndorseHints(
            CouncilIncidentContext context,
            SpymasterIncidentTemplate template)
        {
            string success = template.IsDrawback
                ? "{=BC_CouncilIncident_Spymaster_DrawbackEndorseSuccess}{CHANCE}% chance: reduce {EFFECT} by 75% for {DAYS} days and reduce Spymaster controversy by 5."
                : "{=BC_CouncilIncident_Spymaster_BenefitEndorseSuccess}{CHANCE}% chance: double {EFFECT} for {DAYS} days and reduce Spymaster controversy by 5.";
            string neutral = template.IsDrawback
                ? "{=BC_CouncilIncident_Spymaster_DrawbackEndorseNeutral}{CHANCE}% chance: reduce {EFFECT} by 50% for {DAYS} days."
                : "{=BC_CouncilIncident_Spymaster_BenefitEndorseNeutral}{CHANCE}% chance: strengthen {EFFECT} by 50% for {DAYS} days.";
            string failure = template.IsDrawback
                ? "{=BC_CouncilIncident_Spymaster_DrawbackEndorseFailure}{CHANCE}% chance: double {EFFECT} for {DAYS} days and increase Spymaster controversy by 10."
                : "{=BC_CouncilIncident_Spymaster_BenefitEndorseFailure}{CHANCE}% chance: halve {EFFECT} for {DAYS} days and increase Spymaster controversy by 10.";

            return new List<TextObject>
            {
                ChanceEffectText(success, context.SuccessChance, BellumCivileConstants.CouncilIncidentEndorsedEffectDays, template.EffectLabel),
                ChanceEffectText(neutral, context.NeutralChance, BellumCivileConstants.CouncilIncidentEndorsedEffectDays, template.EffectLabel),
                ChanceEffectText(failure, context.FailureChance, BellumCivileConstants.CouncilIncidentEndorsedEffectDays, template.EffectLabel),
                new TextObject("{=BC_CouncilIncident_Spymaster_EndorseRelation}Gain 5 relation with your spymaster.")
            };
        }

        private static List<TextObject> BuildSpymasterOpposeHints(CouncilIncidentContext context)
        {
            return new List<TextObject>
            {
                new TextObject("{=BC_CouncilIncident_Spymaster_OpposeHint}The assignment remains unchanged. Lose 10 relation with your spymaster.")
            };
        }

        private static List<TextObject> BuildSpymasterAbstainHints(
            CouncilIncidentContext context,
            SpymasterIncidentTemplate template)
        {
            string success = template.IsDrawback
                ? "{=BC_CouncilIncident_Spymaster_DrawbackAbstainSuccess}{CHANCE}% chance: reduce {EFFECT} by 50% for {DAYS} days and reduce Spymaster controversy by 2."
                : "{=BC_CouncilIncident_Spymaster_BenefitAbstainSuccess}{CHANCE}% chance: strengthen {EFFECT} by 50% for {DAYS} days and reduce Spymaster controversy by 2.";
            string failure = template.IsDrawback
                ? "{=BC_CouncilIncident_Spymaster_DrawbackAbstainFailure}{CHANCE}% chance: increase {EFFECT} by 50% for {DAYS} days and increase Spymaster controversy by 5."
                : "{=BC_CouncilIncident_Spymaster_BenefitAbstainFailure}{CHANCE}% chance: weaken {EFFECT} by 25% for {DAYS} days and increase Spymaster controversy by 5.";

            return new List<TextObject>
            {
                ChanceEffectText(success, context.SuccessChance, BellumCivileConstants.CouncilIncidentLimitedTrialEffectDays, template.EffectLabel),
                ChanceEffectText("{=BC_CouncilIncident_Spymaster_AbstainNeutral}{CHANCE}% chance: the assignment continues unchanged.", context.NeutralChance, 0, template.EffectLabel),
                ChanceEffectText(failure, context.FailureChance, BellumCivileConstants.CouncilIncidentLimitedTrialEffectDays, template.EffectLabel)
            };
        }

        private static List<TextObject> ApplySpymasterEndorse(
            CouncilIncidentContext context,
            SpymasterIncidentTemplate template)
        {
            context.ChangeHolderRelation(5);
            switch (context.Quality)
            {
                case CouncilIncidentQuality.Success:
                    ApplySpymasterAspect(context, template, template.IsDrawback ? 0.25f : 2f,
                        BellumCivileConstants.CouncilIncidentEndorsedEffectDays);
                    context.ChangeOfficeControversy(-5f, template.SuccessReason);
                    return Result(template.EndorseSuccessResult);
                case CouncilIncidentQuality.Neutral:
                    ApplySpymasterAspect(context, template, template.IsDrawback ? 0.5f : 1.5f,
                        BellumCivileConstants.CouncilIncidentEndorsedEffectDays);
                    return Result(template.EndorseNeutralResult);
                default:
                    ApplySpymasterAspect(context, template, template.IsDrawback ? 2f : 0.5f,
                        BellumCivileConstants.CouncilIncidentEndorsedEffectDays);
                    context.ChangeOfficeControversy(10f, template.FailureReason);
                    return Result(template.EndorseFailureResult);
            }
        }

        private static List<TextObject> ApplySpymasterOppose(
            CouncilIncidentContext context,
            SpymasterIncidentTemplate template)
        {
            context.ChangeHolderRelation(-10);
            return Result(template.OpposeResult);
        }

        private static List<TextObject> ApplySpymasterAbstain(
            CouncilIncidentContext context,
            SpymasterIncidentTemplate template)
        {
            switch (context.Quality)
            {
                case CouncilIncidentQuality.Success:
                    ApplySpymasterAspect(context, template, template.IsDrawback ? 0.5f : 1.5f,
                        BellumCivileConstants.CouncilIncidentLimitedTrialEffectDays);
                    context.ChangeOfficeControversy(-2f, template.LimitedSuccessReason);
                    return Result(template.AbstainSuccessResult);
                case CouncilIncidentQuality.Neutral:
                    return Result(template.AbstainNeutralResult);
                default:
                    ApplySpymasterAspect(context, template, template.IsDrawback ? 1.5f : 0.75f,
                        BellumCivileConstants.CouncilIncidentLimitedTrialEffectDays);
                    context.ChangeOfficeControversy(5f, template.LimitedFailureReason);
                    return Result(template.AbstainFailureResult);
            }
        }

        private static void ApplySpymasterAspect(
            CouncilIncidentContext context,
            SpymasterIncidentTemplate template,
            float multiplier,
            float durationDays)
        {
            context.ApplyAspectMultiplier(template.AspectId, template.Id, multiplier, durationDays);
        }

        private static CouncilIncidentDefinition CreateAdvisorIncident(
            PrivyCouncilOffice office,
            string assignmentPrefix,
            AdvisorIncidentTemplate template)
        {
            string assignmentId = assignmentPrefix + "_" + template.AssignmentSuffix;
            string eventId = assignmentId + "_" + template.EventSuffix;
            return new CouncilIncidentDefinition(
                eventId,
                assignmentId,
                office,
                IncidentsCampaignBehaviour.IncidentType.FiefManagement,
                1f,
                context => new TextObject(template.Title),
                context =>
                {
                    TextObject description = new TextObject(template.Description);
                    description.SetTextVariable(
                        "ADVISOR_NAME",
                        context?.Holder?.Leader?.Name ?? context?.Holder?.Name ?? TextObject.GetEmpty());
                    FactionObject faction = Campaign.Current?
                        .GetCampaignBehavior<FactionManagerBehavior>()?
                        .GetIdeologicalFaction(context?.Holder);
                    description.SetTextVariable(
                        "FACTION_NAME",
                        faction?.Name ?? new TextObject("{=BC_CouncilIncident_AdvisorCourtAllies}advisor's allies at court").ToString());
                    return description;
                },
                context => BuildAdvisorIncidentOptions(template, eventId));
        }

        private static List<CouncilIncidentOptionDefinition> BuildAdvisorIncidentOptions(
            AdvisorIncidentTemplate template,
            string eventId)
        {
            return new List<CouncilIncidentOptionDefinition>
            {
                new CouncilIncidentOptionDefinition(
                    template.Endorse,
                    context => BuildAdvisorEndorseHints(context, template),
                    context => ApplyAdvisorEndorse(context, template, eventId)),
                new CouncilIncidentOptionDefinition(
                    template.Oppose,
                    BuildAdvisorOpposeHints,
                    context => ApplyAdvisorOppose(context, template)),
                new CouncilIncidentOptionDefinition(
                    template.Abstain,
                    context => BuildAdvisorAbstainHints(context, template),
                    context => ApplyAdvisorAbstain(context, template, eventId))
            };
        }

        private static List<TextObject> BuildAdvisorEndorseHints(
            CouncilIncidentContext context,
            AdvisorIncidentTemplate template)
        {
            string success = template.IsDrawback
                ? "{=BC_CouncilIncident_Advisor_DrawbackEndorseSuccess}{CHANCE}% chance: reduce {EFFECT} by 75% for {DAYS} days and reduce Advisor controversy by 5."
                : "{=BC_CouncilIncident_Advisor_BenefitEndorseSuccess}{CHANCE}% chance: double {EFFECT} for {DAYS} days and reduce Advisor controversy by 5.";
            string neutral = template.IsDrawback
                ? "{=BC_CouncilIncident_Advisor_DrawbackEndorseNeutral}{CHANCE}% chance: reduce {EFFECT} by 50% for {DAYS} days."
                : "{=BC_CouncilIncident_Advisor_BenefitEndorseNeutral}{CHANCE}% chance: strengthen {EFFECT} by 50% for {DAYS} days.";
            string failure = template.IsDrawback
                ? "{=BC_CouncilIncident_Advisor_DrawbackEndorseFailure}{CHANCE}% chance: double {EFFECT} for {DAYS} days and increase Advisor controversy by 10."
                : "{=BC_CouncilIncident_Advisor_BenefitEndorseFailure}{CHANCE}% chance: halve {EFFECT} for {DAYS} days and increase Advisor controversy by 10.";

            return new List<TextObject>
            {
                ChanceEffectText(success, context.SuccessChance, BellumCivileConstants.CouncilIncidentEndorsedEffectDays, template.EffectLabel),
                ChanceEffectText(neutral, context.NeutralChance, BellumCivileConstants.CouncilIncidentEndorsedEffectDays, template.EffectLabel),
                ChanceEffectText(failure, context.FailureChance, BellumCivileConstants.CouncilIncidentEndorsedEffectDays, template.EffectLabel),
                new TextObject("{=BC_CouncilIncident_Advisor_EndorseRelation}Gain 5 relation with your advisor.")
            };
        }

        private static List<TextObject> BuildAdvisorOpposeHints(CouncilIncidentContext context)
        {
            return new List<TextObject>
            {
                new TextObject("{=BC_CouncilIncident_Advisor_OpposeHint}The assignment remains unchanged. Lose 10 relation with your advisor.")
            };
        }

        private static List<TextObject> BuildAdvisorAbstainHints(
            CouncilIncidentContext context,
            AdvisorIncidentTemplate template)
        {
            string success = template.IsDrawback
                ? "{=BC_CouncilIncident_Advisor_DrawbackAbstainSuccess}{CHANCE}% chance: reduce {EFFECT} by 50% for {DAYS} days and reduce Advisor controversy by 2."
                : "{=BC_CouncilIncident_Advisor_BenefitAbstainSuccess}{CHANCE}% chance: strengthen {EFFECT} by 50% for {DAYS} days and reduce Advisor controversy by 2.";
            string failure = template.IsDrawback
                ? "{=BC_CouncilIncident_Advisor_DrawbackAbstainFailure}{CHANCE}% chance: increase {EFFECT} by 50% for {DAYS} days and increase Advisor controversy by 5."
                : "{=BC_CouncilIncident_Advisor_BenefitAbstainFailure}{CHANCE}% chance: weaken {EFFECT} by 25% for {DAYS} days and increase Advisor controversy by 5.";

            return new List<TextObject>
            {
                ChanceEffectText(success, context.SuccessChance, BellumCivileConstants.CouncilIncidentLimitedTrialEffectDays, template.EffectLabel),
                ChanceEffectText("{=BC_CouncilIncident_Advisor_AbstainNeutral}{CHANCE}% chance: the assignment continues unchanged.", context.NeutralChance, 0, template.EffectLabel),
                ChanceEffectText(failure, context.FailureChance, BellumCivileConstants.CouncilIncidentLimitedTrialEffectDays, template.EffectLabel)
            };
        }

        private static List<TextObject> ApplyAdvisorEndorse(
            CouncilIncidentContext context,
            AdvisorIncidentTemplate template,
            string eventId)
        {
            context.ChangeHolderRelation(5);
            switch (context.Quality)
            {
                case CouncilIncidentQuality.Success:
                    ApplyAdvisorAspect(context, template, eventId, template.IsDrawback ? 0.25f : 2f,
                        BellumCivileConstants.CouncilIncidentEndorsedEffectDays);
                    context.ChangeOfficeControversy(-5f, template.SuccessReason);
                    return Result(template.EndorseSuccessResult);
                case CouncilIncidentQuality.Neutral:
                    ApplyAdvisorAspect(context, template, eventId, template.IsDrawback ? 0.5f : 1.5f,
                        BellumCivileConstants.CouncilIncidentEndorsedEffectDays);
                    return Result(template.EndorseNeutralResult);
                default:
                    ApplyAdvisorAspect(context, template, eventId, template.IsDrawback ? 2f : 0.5f,
                        BellumCivileConstants.CouncilIncidentEndorsedEffectDays);
                    context.ChangeOfficeControversy(10f, template.FailureReason);
                    return Result(template.EndorseFailureResult);
            }
        }

        private static List<TextObject> ApplyAdvisorOppose(
            CouncilIncidentContext context,
            AdvisorIncidentTemplate template)
        {
            context.ChangeHolderRelation(-10);
            return Result(template.OpposeResult);
        }

        private static List<TextObject> ApplyAdvisorAbstain(
            CouncilIncidentContext context,
            AdvisorIncidentTemplate template,
            string eventId)
        {
            switch (context.Quality)
            {
                case CouncilIncidentQuality.Success:
                    ApplyAdvisorAspect(context, template, eventId, template.IsDrawback ? 0.5f : 1.5f,
                        BellumCivileConstants.CouncilIncidentLimitedTrialEffectDays);
                    context.ChangeOfficeControversy(-2f, template.LimitedSuccessReason);
                    return Result(template.AbstainSuccessResult);
                case CouncilIncidentQuality.Neutral:
                    return Result(template.AbstainNeutralResult);
                default:
                    ApplyAdvisorAspect(context, template, eventId, template.IsDrawback ? 1.5f : 0.75f,
                        BellumCivileConstants.CouncilIncidentLimitedTrialEffectDays);
                    context.ChangeOfficeControversy(5f, template.LimitedFailureReason);
                    return Result(template.AbstainFailureResult);
            }
        }

        private static void ApplyAdvisorAspect(
            CouncilIncidentContext context,
            AdvisorIncidentTemplate template,
            string eventId,
            float multiplier,
            float durationDays)
        {
            context.ApplyAspectMultiplier(template.AspectId, eventId, multiplier, durationDays);
        }

        private static TextObject ChanceEffectText(
            string text,
            int chance,
            int durationDays,
            string effectLabel)
        {
            TextObject result = ChanceText(text, chance, durationDays);
            result.SetTextVariable("EFFECT", new TextObject(effectLabel));
            return result;
        }

        private static TextObject ChanceText(string text, int chance, int durationDays = 0)
        {
            TextObject result = new TextObject(text);
            result.SetTextVariable("CHANCE", chance);
            if (durationDays > 0)
                result.SetTextVariable("DAYS", durationDays);
            return result;
        }

        private static List<TextObject> Result(string text)
        {
            return new List<TextObject> { new TextObject(text) };
        }
    }
}
