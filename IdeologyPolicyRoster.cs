using System.Collections.Generic;
using TaleWorlds.CampaignSystem;

namespace BellumCivile
{
    public static class IdeologyPolicyRoster
    {
        public static List<string> GetSupportedPolicies(FactionType type) => IdeologyPolicyAgendaConfig.Instance.GetSupportedPolicies(type);
        internal static List<string> GetDefaultSupportedPolicies(FactionType type) => GetSupportedPolicies(type);
        public static CourtPolicyStance GetStance(FactionType type, PolicyObject policy) =>
            IdeologyPolicyAgendaConfig.Instance.GetStance(type, policy?.StringId);
        // Stable interests above feed mood; current political consent must never feed it back.
        public static CourtPolicyStance GetEffectiveStance(FactionObject faction, PolicyObject policy) =>
            faction == null ? CourtPolicyStance.Neutral : GetEffectiveStance(faction.Type, policy, faction.Mood);
        public static CourtPolicyStance GetEffectiveStance(FactionType type, PolicyObject policy, float mood) =>
            GetEffectiveStanceForId(type, policy?.StringId, mood);
        internal static CourtPolicyStance GetEffectiveStanceForId(FactionType type, string id, float mood)
        {
            var config = IdeologyPolicyAgendaConfig.Instance;
            return CourtPolicyStanceRules.Resolve(config.GetStance(type, id), config.IsCrownPolicy(id), mood);
        }
        public static bool DoesFactionSupportPolicy(FactionType type, PolicyObject policy) => GetStance(type, policy) == CourtPolicyStance.Support;
        public static bool DoesFactionOpposePolicy(FactionType type, PolicyObject policy) => GetStance(type, policy) == CourtPolicyStance.Oppose;
        public static bool IsCrownPolicy(PolicyObject policy) => IdeologyPolicyAgendaConfig.Instance.IsCrownPolicy(policy?.StringId);
    }
}
