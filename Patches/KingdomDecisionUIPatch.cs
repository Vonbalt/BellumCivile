using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Election;
using TaleWorlds.CampaignSystem.ViewModelCollection.KingdomManagement.Decisions;
using TaleWorlds.CampaignSystem.ViewModelCollection.KingdomManagement.Decisions.ItemTypes;
using TaleWorlds.Core;
using TaleWorlds.Core.ViewModelCollection.Information;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace BellumCivile.Patches
{
    /// <summary>
    /// Why did I do this file?
    /// To make kingdom voting UI easier to read by appending role labels to king-selection candidates
    /// and showing each supporter's ideological alignment in vote tooltips.
    /// </summary>
    public static class KingdomDecisionUIPatch
    {
        [HarmonyPatch(typeof(PolicyDecisionItemVM), "InitValues")]
        public static class PolicyDecisionItemVMInitValuesPatch
        {
            [HarmonyPostfix]
            public static void Postfix(PolicyDecisionItemVM __instance)
            {
                KingdomPolicyDecision decision = __instance?.PolicyDecision;
                PolicyObject policy = decision?.Policy;
                if (policy == null)
                    return;

                string factionLabel = GetPolicyAgendaFactionLabel(decision.Kingdom, policy);
                if (string.IsNullOrWhiteSpace(factionLabel))
                    return;

                TextObject title = new TextObject("{=BC_KingdomDecision_PolicyAgendaTitle}{POLICY_NAME} ({FACTION_NAME})");
                title.SetTextVariable("POLICY_NAME", policy.Name);
                title.SetTextVariable("FACTION_NAME", factionLabel);
                __instance.TitleText = title.ToString();
            }
        }

        [HarmonyPatch(typeof(DecisionOptionVM), "RefreshValues")]
        public static class DecisionOptionVMRefreshValuesPatch
        {
            [HarmonyPostfix]
            public static void Postfix(DecisionOptionVM __instance)
            {
                if (__instance?.Option == null) return;

                if (__instance.Decision is PrivyCouncilAppointmentDecision appointment
                    && __instance.Option is PrivyCouncilAppointmentDecision.PrivyCouncilAppointmentOutcome candidate)
                {
                    var competence = CouncilCompetencePresentation.Candidate(candidate.CandidateClan?.Leader, appointment.Office);
                    __instance.Description = competence.ToString();
                    __instance.OptionHint = new HintViewModel(new TextObject("{=BC_Council_CandidateCompetenceHint}{DESCRIPTION}\n\n{COMPETENCE}\n{REQUIREMENTS}\nPersonal competence before council-wide bonuses. Skills count up to 300 and attributes up to 10.")
                        .SetTextVariable("DESCRIPTION", candidate.GetDecisionDescription())
                        .SetTextVariable("COMPETENCE", competence)
                        .SetTextVariable("REQUIREMENTS", CouncilCompetencePresentation.Requirements(appointment.Office)));
                }

                if (__instance.Decision is KingSelectionKingdomDecision kingDecision)
                {
                    if (!KingSelectionAIPatch.ActiveElections.TryGetValue(kingDecision.Kingdom, out SuccessionElectionProfile election)) return;

                    Clan candidateClan = KingSelectionAIPatch.ResolveCandidateClan(__instance.Option);
                    string slotLabel = GetKingSelectionSlotLabel(kingDecision.Kingdom, candidateClan, election);
                    if (string.IsNullOrEmpty(slotLabel)) return;

                    string baseName = __instance.Option.GetDecisionTitle().ToString();
                    __instance.Name = baseName + " (" + slotLabel + ")";
                    return;
                }

                string factionLabel = GetDecisionOptionFactionLabel(__instance.Decision, __instance.Option);
                if (string.IsNullOrEmpty(factionLabel)) return;

                __instance.Name = __instance.Option.GetDecisionTitle().ToString() + " (" + factionLabel + ")";
            }
        }

        [HarmonyPatch(typeof(DecisionOptionVM), "ExecuteShowSupporterTooltip")]
        public static class DecisionOptionVMExecuteShowSupporterTooltipPatch
        {
            [HarmonyPrefix]
            public static bool Prefix(DecisionOptionVM __instance)
            {
                DecisionOutcome option = __instance?.Option;
                if (option == null || option.SupporterList == null || option.SupporterList.Count <= 0)
                    return false;

                List<TooltipProperty> properties = new List<TooltipProperty>();
                foreach (Supporter supporter in option.SupporterList)
                {
                    if (supporter == null || supporter.SupportWeight <= Supporter.SupportWeights.StayNeutral || supporter.IsPlayer)
                        continue;

                    int influenceCost = __instance.Decision.GetInfluenceCost(option, supporter.Clan, supporter.SupportWeight);
                    GameTexts.SetVariable("AMOUNT", influenceCost);
                    GameTexts.SetVariable("INFLUENCE_ICON", "{=!}<img src=\"General\\Icons\\Influence@2x\" extend=\"7\">");

                    string alignment = GetClanAlignmentLabel(supporter.Clan);
                    string leftText = supporter.Name?.ToString() + " (" + alignment + ")";
                    string rightText = GameTexts.FindText("str_amount_with_influence_icon").ToString();
                    properties.Add(new TooltipProperty(leftText, rightText, 0));
                }

                if (properties.Count > 0)
                    InformationManager.ShowTooltip(typeof(List<TooltipProperty>), new object[] { properties });

                return false;
            }
        }

        private static string GetKingSelectionSlotLabel(Kingdom kingdom, Clan candidateClan, SuccessionElectionProfile election)
        {
            if (kingdom == null || candidateClan == null || election == null)
                return string.Empty;

            if (candidateClan == election.ProtectedCandidateClan)
            {
                if (election.IsAbdication)
                    return new TextObject("{=BC_KingSelRole_ChosenSuccessor}Chosen Successor").ToString();

                if (election.ProtectedCandidateIsRightfulSovereign)
                    return new TextObject("{=BC_KingSelRole_RightfulSovereign}Rightful Sovereign").ToString();

                if (election.LegitimateDynasticClan != null && candidateClan == election.LegitimateDynasticClan)
                    return new TextObject("{=BC_KingSelRole_RightfulHeir}Rightful Heir").ToString();

                return new TextObject("{=BC_KingSelRole_Claimant}Claimant").ToString();
            }

            return GetClanFactionDisplayLabel(candidateClan);
        }

        private static string GetClanAlignmentLabel(Clan clan)
        {
            return GetClanFactionDisplayLabel(clan);
        }

        private static string GetPolicyAgendaFactionLabel(Kingdom kingdom, PolicyObject policy)
        {
            if (policy == null)
                return string.Empty;

            FactionManagerBehavior manager = Campaign.Current?.GetCampaignBehavior<FactionManagerBehavior>();
            FactionObject liveFaction = manager?.GetFactionsInKingdom(kingdom)
                .FirstOrDefault(faction => faction != null
                    && faction.IsIdeology && IdeologyPolicyRoster.GetEffectiveStance(faction, policy) == CourtPolicyStance.Support);
            if (liveFaction != null)
                return liveFaction.GetDisplayName().ToString();

            if (IdeologyPolicyRoster.IsCrownPolicy(policy)) return string.Empty;

            foreach (FactionType factionType in new[]
            {
                FactionType.Nobility,
                FactionType.Glory,
                FactionType.Liberty
            })
            {
                if (!IdeologyPolicyRoster.DoesFactionSupportPolicy(factionType, policy))
                    continue;

                return CourtInstitutionDisplayHelper
                    .GetCourtFactionName(factionType, kingdom)
                    .ToString();
            }

            return string.Empty;
        }

        private static string GetDecisionOptionFactionLabel(KingdomDecision decision, DecisionOutcome option)
        {
            if (decision == null || option == null)
                return string.Empty;

            if (decision is PrivyCouncilAppointmentDecision
                && option is PrivyCouncilAppointmentDecision.PrivyCouncilAppointmentOutcome appointmentOutcome)
            {
                return GetClanFactionDisplayLabel(appointmentOutcome.CandidateClan);
            }

            if (decision is SettlementClaimantDecision)
            {
                Clan candidateClan = FiefVoteAIPatch.GetCandidateClan(option);
                return GetClanFactionDisplayLabel(candidateClan);
            }

            if (decision is KingdomPolicyDecision policyDecision)
            {
                bool? supportsProposal = TryGetBoolValue(option,
                    "ShouldDecisionBeEnforced",
                    "ShouldBeEnacted",
                    "ShouldDecisionBeEnacted",
                    "IsEnacted",
                    "IsDecisionEnacted",
                    "bIsEnacted");

                if (!supportsProposal.HasValue)
                    return string.Empty;

                return supportsProposal.Value
                    ? GetClanFactionDisplayLabel(policyDecision.ProposerClan)
                    : new TextObject("{=BC_DecisionLabel_Opposition}Opposition").ToString();
            }

            if (decision is ExpelClanFromKingdomDecision expelDecision)
            {
                bool? expel = TryGetBoolValue(option,
                    "ShouldBeExpelled",
                    "Expel",
                    "IsExpelled",
                    "bShouldBeExpelled");

                if (!expel.HasValue)
                    return string.Empty;

                string targetFaction = GetClanFactionDisplayLabel(expelDecision.ClanToExpel);
                return expel.Value
                    ? FormatAgainstLabel(targetFaction)
                    : targetFaction;
            }

            if (decision is SettlementClaimantPreliminaryDecision preliminaryDecision)
            {
                bool? shouldChangeOwner = TryGetBoolValue(option,
                    "ShouldSettlementOwnerChange",
                    "ShouldOwnerChange",
                    "ShouldSettlementChangeOwner");

                if (!shouldChangeOwner.HasValue)
                    return string.Empty;

                Clan ownerClan = ResolvePreliminaryOwnerClan(preliminaryDecision);
                string ownerFaction = GetClanFactionDisplayLabel(ownerClan);
                return shouldChangeOwner.Value
                    ? FormatAgainstLabel(ownerFaction)
                    : ownerFaction;
            }

            return string.Empty;
        }

        private static string FormatAgainstLabel(string targetLabel)
        {
            TextObject text = new TextObject("{=BC_DecisionLabel_Against}Against {FACTION_NAME}");
            text.SetTextVariable("FACTION_NAME", targetLabel);
            return text.ToString();
        }

        private static Clan ResolvePreliminaryOwnerClan(SettlementClaimantPreliminaryDecision decision)
        {
            if (decision?.Settlement?.OwnerClan != null)
                return decision.Settlement.OwnerClan;

            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            FieldInfo ownerField = typeof(SettlementClaimantPreliminaryDecision).GetField("_ownerClan", flags)
                                ?? typeof(SettlementClaimantPreliminaryDecision).GetField("OwnerClan", flags);
            return ownerField?.GetValue(decision) as Clan;
        }

        private static bool? TryGetBoolValue(DecisionOutcome option, params string[] memberNames)
        {
            if (option == null || memberNames == null)
                return null;

            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            System.Type searchType = option.GetType();

            while (searchType != null && searchType != typeof(object))
            {
                foreach (string name in memberNames)
                {
                    FieldInfo field = searchType.GetField(name, flags);
                    if (field != null && field.FieldType == typeof(bool))
                        return (bool)field.GetValue(option);

                    PropertyInfo property = searchType.GetProperty(name, flags);
                    if (property != null && property.PropertyType == typeof(bool))
                        return (bool)property.GetValue(option);
                }

                searchType = searchType.BaseType;
            }

            return null;
        }

        private static string GetClanFactionDisplayLabel(Clan clan)
        {
            if (clan == null)
                return new TextObject("{=BC_KingVoteAlign_NonAligned}Non-Aligned").ToString();

            FactionManagerBehavior factionManager = Campaign.Current?.GetCampaignBehavior<FactionManagerBehavior>();
            FactionObject ideology = factionManager?.GetIdeologicalFaction(clan);
            if (ideology == null)
                return new TextObject("{=BC_KingVoteAlign_NonAligned}Non-Aligned").ToString();

            return ideology.GetDisplayName().ToString();
        }
    }
}
