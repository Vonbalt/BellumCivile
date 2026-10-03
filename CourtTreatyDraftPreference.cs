using System.Collections.Generic;
using System.Linq;

namespace BellumCivile
{
    // Disposable per-draft context. Never saved or retained across negotiation passes.
    internal sealed class CourtTreatyDraftPreference
    {
        internal TreatyTermType Type { get; }
        internal string From { get; }
        internal string To { get; }
        internal float ConsiderationBonus { get; }
        internal float PackageBonus { get; }
        internal string Reason { get; }
        internal string SettlementId { get; }

        internal CourtTreatyDraftPreference(TreatyTermType type, string from, string to,
            float considerationBonus, float packageBonus, string reason)
            : this(type, from, to, considerationBonus, packageBonus, reason, null) { }

        internal CourtTreatyDraftPreference(TreatyTermType type, string from, string to,
            float considerationBonus, float packageBonus, string reason, string settlementId)
        {
            Type = type; From = from; To = to;
            ConsiderationBonus = considerationBonus; PackageBonus = packageBonus; Reason = reason;
            SettlementId = settlementId;
        }

        internal bool Matches(TreatyTermRecord term) => term != null && !string.IsNullOrEmpty(From)
            && !string.IsNullOrEmpty(To) && term.Type == Type && term.FromKingdomId == From && term.ToKingdomId == To
            && (string.IsNullOrEmpty(SettlementId) || term.SettlementId == SettlementId);

        internal float Score(IEnumerable<TreatyTermRecord> terms) => terms?.Any(Matches) == true ? PackageBonus : 0;
    }
}
