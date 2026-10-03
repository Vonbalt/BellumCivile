using System.Collections.Generic;
using System.Linq;

namespace BellumCivile
{
    internal static class PolicyRefundRules
    {
        private static readonly HashSet<string> TechnicalReasons = new HashSet<string>
        {
            "policy_missing", "kingdom_missing", "proposer_missing", "abolish_flag_missing", "leader_flag_missing",
            "queue_rejected_after_payment", "is_allowed_exception", "add_decision_exception", "add_decision_failed",
            "initial_outcomes_exception", "no_initial_outcomes", "narrow_outcomes_exception", "no_narrowed_outcomes",
            "sponsor_resolution_exception", "queried_outcome_missing", "support_option_exception"
        };

        // Unknown or mixed political/technical failures are not automatically reimbursed.
        internal static bool IsTechnical(string reason) => reason != null && TechnicalReasons.Contains(reason);
        internal static bool IsTechnical(IEnumerable<string> reasons) => reasons != null && reasons.Any() && reasons.All(IsTechnical);
    }
}
