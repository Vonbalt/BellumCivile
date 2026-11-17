using System;
using System.Collections.Generic;
using System.Linq;

namespace BellumCivile
{
    // Capture before treaty effects; later evaluations never consult mutable ownership or claims.
    internal sealed class DefensivePactTreatySnapshot
    {
        private readonly string _winnerId;
        private readonly string _loserId;
        private readonly List<TreatyTermRecord> _terms;
        private readonly Dictionary<TreatyTermRecord, DefensivePactTerritorialResult> _territory;
        private readonly Dictionary<TreatyTermRecord, DefensivePactLegalBasis> _basis;

        internal bool Matches(TreatyProposalRecord proposal) => proposal != null
            && proposal.WinnerKingdomId == _winnerId && proposal.LoserKingdomId == _loserId
            && _terms.SequenceEqual((proposal.Terms ?? Array.Empty<TreatyTermRecord>()).Where(t => t != null));

        private DefensivePactTreatySnapshot(string winnerId, string loserId, List<TreatyTermRecord> terms,
            Dictionary<TreatyTermRecord, DefensivePactTerritorialResult> territory,
            Dictionary<TreatyTermRecord, DefensivePactLegalBasis> basis)
        { _winnerId = winnerId; _loserId = loserId; _terms = terms; _territory = territory; _basis = basis; }

        internal static DefensivePactTreatySnapshot Capture(TreatyProposalRecord proposal,
            IEnumerable<WarScoreFiefSnapshotRecord> snapshots, Func<string, string, bool?> sameSide,
            Func<TreatyTermRecord, DefensivePactLegalBasis> legalBasis)
        {
            if (proposal == null || snapshots == null || sameSide == null || legalBasis == null
                || string.IsNullOrWhiteSpace(proposal.WinnerKingdomId) || string.IsNullOrWhiteSpace(proposal.LoserKingdomId)
                || proposal.WinnerKingdomId == proposal.LoserKingdomId) return null;
            var terms = (proposal.Terms ?? Array.Empty<TreatyTermRecord>()).Where(t => t != null).ToList();
            var territory = new Dictionary<TreatyTermRecord, DefensivePactTerritorialResult>();
            var basis = new Dictionary<TreatyTermRecord, DefensivePactLegalBasis>();
            if (!terms.Any(t => t.Type == TreatyTermType.WhitePeace))
            {
                foreach (var term in terms.Where(IsFief))
                {
                    var result = DefensivePactTerritorialFacts.Classify(term, snapshots, sameSide);
                    territory[term] = result;
                    if (result == DefensivePactTerritorialResult.Concession) basis[term] = legalBasis(term);
                }
            }
            return new DefensivePactTreatySnapshot(proposal.WinnerKingdomId, proposal.LoserKingdomId, terms, territory, basis);
        }

        // Null means hypothetical draft; an empty delivery set means nothing was delivered.
        internal DefensivePactDraftProjection Evaluate(ISet<TreatyTermRecord> delivered)
            => EvaluateScaled(delivered, 1d);

        internal DefensivePactDraftProjection EvaluateScaled(ISet<TreatyTermRecord> delivered, double gainMultiplier)
        {
            if (!DefensivePactThreatRules.Finite(gainMultiplier) || gainMultiplier < 0d || gainMultiplier > 2d)
                throw new ArgumentOutOfRangeException(nameof(gainMultiplier));
            var empty = new Dictionary<string, DefensivePactTreatyThreat>();
            if (_terms.Any(t => t.Type == TreatyTermType.WhitePeace)) return new DefensivePactDraftProjection(true, empty);
            bool Effective(TreatyTermRecord term) => delivered == null || delivered.Contains(term);
            bool Principals(TreatyTermRecord term) => term.FromKingdomId == _winnerId && term.ToKingdomId == _loserId
                || term.FromKingdomId == _loserId && term.ToKingdomId == _winnerId;
            foreach (var term in _terms.Where(t => Effective(t) && IsRelevant(t)))
            {
                if (!Principals(term) || term.WarScoreCost <= 0) return new DefensivePactDraftProjection(false, empty);
                if (!IsFief(term)) continue;
                if (!_territory.TryGetValue(term, out var territory) || territory == DefensivePactTerritorialResult.Unknown)
                    return new DefensivePactDraftProjection(false, empty);
                if (territory == DefensivePactTerritorialResult.Concession
                    && (!_basis.TryGetValue(term, out var basis) || basis == DefensivePactLegalBasis.Unknown))
                    return new DefensivePactDraftProjection(false, empty);
            }
            var impacts = DefensivePactTreatyCalculator.Calculate(_terms,
                term => Effective(term) && Principals(term),
                term => _territory.TryGetValue(term, out var result) && result == DefensivePactTerritorialResult.Concession
                    && _basis.TryGetValue(term, out var basis) && basis != DefensivePactLegalBasis.Unknown,
                term => _basis.TryGetValue(term, out var basis) && basis == DefensivePactLegalBasis.Established);
            foreach (var impact in impacts.Values) impact.SetGainMultiplier(gainMultiplier);
            return new DefensivePactDraftProjection(true, impacts);
        }

        internal DefensivePactTreatyJournal CreateJournal(string proposalId, double settlementDay)
        {
            var facts = new List<DefensivePactTreatyTermFact>();
            foreach (var term in _terms)
            {
                _territory.TryGetValue(term, out var territory);
                _basis.TryGetValue(term, out var basis);
                facts.Add(new DefensivePactTreatyTermFact(term, territory, basis));
            }
            return new DefensivePactTreatyJournal(proposalId, _winnerId, _loserId, facts, settlementDay);
        }

        internal static DefensivePactTreatySnapshot FromJournal(string winnerId, string loserId,
            IEnumerable<DefensivePactTreatyTermFact> facts)
        {
            if (facts == null || string.IsNullOrWhiteSpace(winnerId) || string.IsNullOrWhiteSpace(loserId)
                || winnerId == loserId) return null;
            var entries = facts.ToList();
            if (entries.Any(fact => fact?.Term == null) || entries.Select(fact => fact.Term).Distinct().Count() != entries.Count)
                return null;
            var territory = new Dictionary<TreatyTermRecord, DefensivePactTerritorialResult>();
            var basis = new Dictionary<TreatyTermRecord, DefensivePactLegalBasis>();
            foreach (var fact in entries.Where(fact => IsFief(fact.Term)))
            {
                territory.Add(fact.Term, fact.Territory);
                basis.Add(fact.Term, fact.Basis);
            }
            return new DefensivePactTreatySnapshot(winnerId, loserId, entries.Select(fact => fact.Term).ToList(), territory, basis);
        }

        private static bool IsFief(TreatyTermRecord term) => term.Type == TreatyTermType.TransferFief
            || term.Type == TreatyTermType.RecognizeClientOccupation;
        internal static bool IsRelevant(TreatyTermRecord term) => term != null && (IsFief(term)
            || (!term.WasVoluntaryOffering && (term.Type == TreatyTermType.ForceVassalization
                || term.Type == TreatyTermType.MakeClientKingdom || term.Type == TreatyTermType.ArrangeRoyalMarriage)));
    }
}
