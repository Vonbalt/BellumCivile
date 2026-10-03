using System.Collections.Generic;
using System.Linq;
using BellumCivile.Behaviors;
using TaleWorlds.CampaignSystem;

namespace BellumCivile
{
    internal static class RebelPolicyHelper
    {
        public static void ApplyIndependencePolicyProfile(FactionObject faction, Kingdom kingdom)
        {
            if (faction?.Type != FactionType.Independence || kingdom == null) return;

            List<PolicyObject> desiredPolicies = BuildIndependencePolicySet(faction);
            ReplacePolicies(kingdom, desiredPolicies);
        }

        public static void CopyPolicies(Kingdom sourceKingdom, Kingdom destinationKingdom)
        {
            if (sourceKingdom == null || destinationKingdom == null) return;
            ReplacePolicies(destinationKingdom, sourceKingdom.ActivePolicies.ToList());
        }

        internal static void InitializePartitionPolicies(CrownPartitionPromotionRecord journal)
        {
            if (journal?.GovernmentStarted != true || journal.GovernmentReturned || journal.Completed
                || journal.Successor == null || journal.Successor == journal.Parent || journal.InheritedPolicies == null)
                throw new System.InvalidOperationException("Invalid Crown partition policy initialization context.");
            var policies = journal.InheritedPolicies.Select(id => PolicyObject.All.SingleOrDefault(p => p.StringId == id)).ToList();
            if (policies.Any(p => p == null) || policies.Distinct().Count() != policies.Count)
                throw new System.InvalidOperationException("An inherited policy is missing or duplicated.");
            ReplacePolicies(journal.Successor, policies);
        }

        private static List<PolicyObject> BuildIndependencePolicySet(FactionObject faction)
        {
            List<PolicyObject> parentPolicies = faction.ParentKingdom?.ActivePolicies.ToList() ?? new List<PolicyObject>();
            int desiredPolicyCount = parentPolicies.Count;

            if (desiredPolicyCount == 0) return parentPolicies;

            FactionManagerBehavior factionManager = Campaign.Current?.GetCampaignBehavior<FactionManagerBehavior>();
            FactionObject leaderIdeology = factionManager?.GetIdeologicalFaction(faction.Leader);
            if (leaderIdeology == null || !leaderIdeology.IsIdeology) return parentPolicies;

            List<string> supportedPolicyIds = IdeologyPolicyRoster.GetSupportedPolicies(leaderIdeology.Type);
            if (supportedPolicyIds.Count == 0) return parentPolicies;

            Dictionary<string, PolicyObject> policyLookup = PolicyObject.All
                .Where(policy => policy != null && !string.IsNullOrEmpty(policy.StringId))
                .GroupBy(policy => policy.StringId)
                .ToDictionary(group => group.Key, group => group.First());

            List<PolicyObject> selectedPolicies = new List<PolicyObject>();

            foreach (string policyId in supportedPolicyIds)
            {
                if (selectedPolicies.Count >= desiredPolicyCount) break;
                if (!policyLookup.TryGetValue(policyId, out PolicyObject policy)) continue;
                if (!selectedPolicies.Contains(policy)) selectedPolicies.Add(policy);
            }

            foreach (PolicyObject parentPolicy in parentPolicies)
            {
                if (selectedPolicies.Count >= desiredPolicyCount) break;
                if (parentPolicy == null || selectedPolicies.Contains(parentPolicy)) continue;
                selectedPolicies.Add(parentPolicy);
            }

            return selectedPolicies;
        }

        private static void ReplacePolicies(Kingdom kingdom, List<PolicyObject> desiredPolicies)
        {
            if (kingdom == null) return;

            List<PolicyObject> currentPolicies = kingdom.ActivePolicies.ToList();
            List<PolicyObject> targetPolicies = desiredPolicies?.Where(policy => policy != null).Distinct().ToList()
                                            ?? new List<PolicyObject>();

            foreach (PolicyObject policy in currentPolicies.Where(policy => !targetPolicies.Contains(policy)).ToList())
                kingdom.RemovePolicy(policy);

            foreach (PolicyObject policy in targetPolicies)
                kingdom.AddPolicy(policy);
        }
    }
}
