using System;
using System.Collections.Generic;
using BellumCivile.Behaviors;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Localization;

namespace BellumCivile
{
    public sealed class PrivyCouncilAssignmentContext
    {
        public PrivyCouncilAssignmentContext(
            PrivyCouncilBehavior behavior,
            Kingdom kingdom,
            PrivyCouncilOffice office,
            Clan holder,
            float competence)
        {
            Behavior = behavior;
            Kingdom = kingdom;
            Office = office;
            Holder = holder;
            Competence = competence;
        }

        public PrivyCouncilBehavior Behavior { get; }
        public Kingdom Kingdom { get; }
        public PrivyCouncilOffice Office { get; }
        public Clan Holder { get; }
        public float Competence { get; }
    }

    public sealed class PrivyCouncilAssignmentDefinition
    {
        private readonly Func<PrivyCouncilAssignmentContext, float> _aiSuitability;
        private readonly Action<PrivyCouncilAssignmentContext> _dailyEffect;

        public PrivyCouncilAssignmentDefinition(
            string id,
            PrivyCouncilOffice office,
            TextObject name,
            TextObject description,
            Func<PrivyCouncilAssignmentContext, float> aiSuitability = null,
            Action<PrivyCouncilAssignmentContext> dailyEffect = null)
        {
            Id = id ?? string.Empty;
            Office = office;
            Name = name ?? TextObject.GetEmpty();
            Description = description ?? TextObject.GetEmpty();
            _aiSuitability = aiSuitability;
            _dailyEffect = dailyEffect;
        }

        public string Id { get; }
        public PrivyCouncilOffice Office { get; }
        public TextObject Name { get; }
        public TextObject Description { get; }

        public float EvaluateAiSuitability(PrivyCouncilAssignmentContext context)
        {
            return _aiSuitability?.Invoke(context) ?? 0f;
        }

        public void ApplyDailyEffect(PrivyCouncilAssignmentContext context)
        {
            _dailyEffect?.Invoke(context);
        }
    }

    public static class PrivyCouncilAssignmentRegistry
    {
        private static readonly List<PrivyCouncilAssignmentDefinition> Definitions =
            new List<PrivyCouncilAssignmentDefinition>();
        private static readonly Dictionary<string, PrivyCouncilAssignmentDefinition> DefinitionsById =
            new Dictionary<string, PrivyCouncilAssignmentDefinition>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<PrivyCouncilOffice, List<PrivyCouncilAssignmentDefinition>> DefinitionsByOffice =
            new Dictionary<PrivyCouncilOffice, List<PrivyCouncilAssignmentDefinition>>();
        private static readonly Dictionary<PrivyCouncilOffice, PrivyCouncilAssignmentDefinition> DefaultByOffice =
            new Dictionary<PrivyCouncilOffice, PrivyCouncilAssignmentDefinition>();

        static PrivyCouncilAssignmentRegistry()
        {
            RegisterBuiltInAssignments();
        }

        public static void Register(PrivyCouncilAssignmentDefinition definition)
        {
            if (definition == null || string.IsNullOrWhiteSpace(definition.Id))
                throw new ArgumentException("A council assignment requires a stable id.", nameof(definition));

            if (DefinitionsById.ContainsKey(definition.Id))
                throw new InvalidOperationException("A council assignment with id '" + definition.Id + "' is already registered.");

            Definitions.Add(definition);
            DefinitionsById.Add(definition.Id, definition);
            if (!DefinitionsByOffice.TryGetValue(definition.Office, out List<PrivyCouncilAssignmentDefinition> officeDefinitions))
            {
                officeDefinitions = new List<PrivyCouncilAssignmentDefinition>();
                DefinitionsByOffice.Add(definition.Office, officeDefinitions);
            }

            officeDefinitions.Add(definition);
            if (!DefaultByOffice.ContainsKey(definition.Office))
                DefaultByOffice.Add(definition.Office, definition);
        }

        public static IReadOnlyList<PrivyCouncilAssignmentDefinition> GetAssignments(PrivyCouncilOffice office)
        {
            if (DefinitionsByOffice.TryGetValue(office, out List<PrivyCouncilAssignmentDefinition> definitions))
                return definitions;
            return Array.Empty<PrivyCouncilAssignmentDefinition>();
        }

        public static PrivyCouncilAssignmentDefinition GetAssignment(string id)
        {
            if (string.IsNullOrEmpty(id))
                return null;

            DefinitionsById.TryGetValue(id, out PrivyCouncilAssignmentDefinition definition);
            return definition;
        }

        public static PrivyCouncilAssignmentDefinition GetDefaultAssignment(PrivyCouncilOffice office)
        {
            DefaultByOffice.TryGetValue(office, out PrivyCouncilAssignmentDefinition definition);
            return definition;
        }

        private static void RegisterBuiltInAssignments()
        {
            RegisterNoneAssignment(PrivyCouncilOffice.Marshal, "marshal");
            RegisterAssignment("marshal_organize_patrols", PrivyCouncilOffice.Marshal,
                "{=BC_Council_AssignmentOrganizePatrols}Organize Patrols",
                "{=BC_Council_AssignmentOrganizePatrolsHint}At average competence: patrol parties are 10% larger and form 15% faster. Failures to protect caravans and villages create 25% more {OFFICE} controversy.");
            RegisterAssignment("marshal_train_militia", PrivyCouncilOffice.Marshal,
                "{=BC_Council_AssignmentTrainMilitia}Train the Militia",
                "{=BC_Council_AssignmentTrainMilitiaHint}At average competence: +10% militia growth and +10% veteran militia chance across the realm, at the cost of 10% village production.");
            RegisterAssignment("marshal_oversee_logistics", PrivyCouncilOffice.Marshal,
                "{=BC_Council_AssignmentOverseeLogistics}Oversee Logistics",
                "{=BC_Council_AssignmentOverseeLogisticsHint}At average competence: armies lose 15% less cohesion and parties serving in armies consume 15% less food. Creates 1 {OFFICE} controversy each week.");

            RegisterNoneAssignment(PrivyCouncilOffice.Chancellor, "chancellor");
            RegisterAssignment("chancellor_appease_nobles", PrivyCouncilOffice.Chancellor,
                "{=BC_Council_AssignmentAppeaseNobles}Appease the Nobility",
                "{=BC_Council_AssignmentAppeaseNoblesHint}At average competence: +1 weekly relation between the ruler and the most estranged loyal vassal, up to 25 relation. Creates 1 {OFFICE} controversy each week.");
            RegisterAssignment("chancellor_improve_foreign_relations", PrivyCouncilOffice.Chancellor,
                "{=BC_Council_AssignmentImproveForeignRelations}Improve Foreign Relations",
                "{=BC_Council_AssignmentImproveForeignRelationsHint}At average competence: +1 weekly relation with the most estranged neighboring ruler not at war with the realm, up to 25 relation. Creates 1 {OFFICE} controversy each week.");
            RegisterAssignment("chancellor_fabricate_grievances", PrivyCouncilOffice.Chancellor,
                "{=BC_Council_AssignmentFabricateGrievances}Fabricate Grievances",
                "{=BC_Council_AssignmentFabricateGrievancesHint}At average competence: +25% ruling-clan claim fabrication progress, but +5 percentage points to its discovery chance. Creates 1 {OFFICE} controversy each week.");

            RegisterNoneAssignment(PrivyCouncilOffice.Seneschal, "seneschal");
            RegisterAssignment("seneschal_audit_vassals", PrivyCouncilOffice.Seneschal,
                "{=BC_Council_AssignmentAuditVassals}Audit Vassals",
                "{=BC_Council_AssignmentAuditVassalsHint}At average competence: +10% feudal service income reaching the crown. Creates 1 {OFFICE} controversy each week.");
            RegisterAssignment("seneschal_subsidize_infrastructure", PrivyCouncilOffice.Seneschal,
                "{=BC_Council_AssignmentSubsidizeInfrastructure}Subsidize Infrastructure",
                "{=BC_Council_AssignmentSubsidizeInfrastructureHint}At average competence: +10% construction across the realm. The ruler pays 50 denars per city or castle each day. No benefit is applied unless the full daily cost can be paid.");
            RegisterAssignment("seneschal_stockpile_provisions", PrivyCouncilOffice.Seneschal,
                "{=BC_Council_AssignmentStockpileProvisions}Stockpile Provisions",
                "{=BC_Council_AssignmentStockpileProvisionsHint}At average competence: up to 2 daily food per stronghold at a cost of 30 denars per stronghold each day. No benefit is applied unless the full daily cost can be paid.");

            RegisterNoneAssignment(PrivyCouncilOffice.Spymaster, "spymaster");
            RegisterAssignment("spymaster_counter_espionage", PrivyCouncilOffice.Spymaster,
                "{=BC_Council_AssignmentCounterEspionage}Counter-Espionage",
                "{=BC_Council_AssignmentCounterEspionageHint}At average competence: +15 defensive intrigue, +5 percentage points to hostile claim discovery, and -5 offensive intrigue.");
            RegisterAssignment("spymaster_uncover_dissent", PrivyCouncilOffice.Spymaster,
                "{=BC_Council_AssignmentUncoverDissent}Uncover Dissent",
                "{=BC_Council_AssignmentUncoverDissentHint}At average competence: covert rebel conspiracies build 15% slower and hostile claim discovery gains 10 percentage points. Creates 1 {OFFICE} controversy each week.");
            RegisterAssignment("spymaster_sow_rumors", PrivyCouncilOffice.Spymaster,
                "{=BC_Council_AssignmentSowRumors}Sow Rumors",
                "{=BC_Council_AssignmentSowRumorsHint}At average competence: +15 offensive intrigue against rival courts and -5 defensive intrigue. Creates 1 {OFFICE} controversy each week.");

            RegisterAdvisorAssignments(PrivyCouncilOffice.FirstAdvisor, "first_advisor");
            RegisterAdvisorAssignments(PrivyCouncilOffice.SecondAdvisor, "second_advisor");
        }

        private static void RegisterAdvisorAssignments(PrivyCouncilOffice office, string idPrefix)
        {
            RegisterNoneAssignment(office, idPrefix);
            RegisterAssignment(idPrefix + "_counsel_crown", office,
                "{=BC_Council_AssignmentCounselCrown}Counsel the Crown",
                "{=BC_Council_AssignmentCounselCrownHint}At average competence: +5 effective competence to all four core offices. Two advisors may contribute, capped at +8 total.");
            RegisterAssignment(idPrefix + "_mediate_council", office,
                "{=BC_Council_AssignmentMediateCouncil}Mediate the Council",
                "{=BC_Council_AssignmentMediateCouncilHint}At average competence: +5 support for core councillors and -1 weekly controversy from the most controversial core office.");
            RegisterAssignment(idPrefix + "_represent_court", office,
                "{=BC_Council_AssignmentRepresentCourt}Represent Court Interests",
                "{=BC_Council_AssignmentRepresentCourtHint}At average competence: +5 mood baseline and -5 rebellious intent for the advisor's court faction. Duplicate representation does not stack.");
        }

        private static void RegisterNoneAssignment(PrivyCouncilOffice office, string idPrefix)
        {
            RegisterAssignment(idPrefix + "_none", office,
                "{=BC_Council_AssignmentNone}None",
                "{=BC_Council_AssignmentNoneHint}This councillor has no assignment. The ruler loses 1 relation with them each week while they remain sidelined.");
        }

        private static void RegisterAssignment(
            string id,
            PrivyCouncilOffice office,
            string name,
            string description)
        {
            Register(new PrivyCouncilAssignmentDefinition(
                id,
                office,
                new TextObject(name),
                new TextObject(description),
                context => context?.Behavior?.EvaluateBuiltInAssignment(id, context) ?? 0f,
                context => context?.Behavior?.ApplyBuiltInAssignmentDailyEffect(id, context)));
        }
    }
}
