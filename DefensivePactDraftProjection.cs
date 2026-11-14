using System;
using System.Collections.Generic;

namespace BellumCivile
{
    internal sealed class DefensivePactDraftProjection
    {
        internal bool IsReliable { get; }
        internal IReadOnlyDictionary<string, DefensivePactTreatyThreat> Impacts { get; }

        internal DefensivePactDraftProjection(bool reliable, IReadOnlyDictionary<string, DefensivePactTreatyThreat> impacts)
        {
            IsReliable = reliable;
            Impacts = impacts;
        }

        internal static DefensivePactDraftProjection Build(TreatyProposalRecord proposal,
            IEnumerable<WarScoreFiefSnapshotRecord> snapshots, Func<string, string, bool?> sameSide,
            Func<TreatyTermRecord, DefensivePactLegalBasis> legalBasis)
        {
            var capture = DefensivePactTreatySnapshot.Capture(proposal, snapshots, sameSide, legalBasis);
            return capture?.Evaluate(null)
                ?? new DefensivePactDraftProjection(false, new Dictionary<string, DefensivePactTreatyThreat>());
        }
    }
}
