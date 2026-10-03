using System.Collections.Generic;

namespace BellumCivile
{
    internal static class EnactedPolicyStances
    {
        internal static Dictionary<string, CourtPolicyStance> Build(IdeologyPolicyAgendaConfig config,
            IEnumerable<string> enactedIds, FactionType? alignment, bool crown)
        {
            var result = new Dictionary<string, CourtPolicyStance>();
            foreach (string id in enactedIds)
            {
                if (string.IsNullOrEmpty(id)) continue;
                CourtPolicyStance stance = crown && config.IsCrownPolicy(id) ? CourtPolicyStance.Support
                    : alignment.HasValue ? config.GetStance(alignment.Value, id) : CourtPolicyStance.Neutral;
                if (stance != CourtPolicyStance.Neutral) result[id] = stance;
            }
            return result;
        }
    }
}
