using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.SaveSystem;

namespace BellumCivile
{
    internal sealed class DefensivePactTreatyTermFact
    {
        [SaveableField(1)] private TreatyTermRecord _term;
        [SaveableField(2)] private int _territory;
        [SaveableField(3)] private int _basis;
        [SaveableField(4)] private bool _delivered;

        internal TreatyTermRecord Term => _term;
        internal DefensivePactTerritorialResult Territory => Enum.IsDefined(typeof(DefensivePactTerritorialResult), _territory)
            ? (DefensivePactTerritorialResult)_territory : DefensivePactTerritorialResult.Unknown;
        internal DefensivePactLegalBasis Basis => Enum.IsDefined(typeof(DefensivePactLegalBasis), _basis)
            ? (DefensivePactLegalBasis)_basis : DefensivePactLegalBasis.Unknown;
        internal bool Delivered => _delivered;

        internal DefensivePactTreatyTermFact(TreatyTermRecord term, DefensivePactTerritorialResult territory,
            DefensivePactLegalBasis basis)
        {
            _term = term ?? throw new ArgumentNullException(nameof(term));
            _territory = (int)territory;
            _basis = (int)basis;
        }

        internal void MarkDelivered() => _delivered = true;
    }

    // Prepared before settlement; only a confirmed applied proposal may be sealed and evaluated.
    internal sealed class DefensivePactTreatyJournal
    {
        [SaveableField(1)] private string _proposalId;
        [SaveableField(2)] private string _winnerId;
        [SaveableField(3)] private string _loserId;
        [SaveableField(4)] private List<DefensivePactTreatyTermFact> _facts;
        [SaveableField(5)] private bool _sealed;
        [SaveableField(6)] private double _settlementDay;
        [SaveableField(7)] private bool _invalid;
        [SaveableField(8)] private double _winnerWeeklyDecay;
        [SaveableField(9)] private double _loserWeeklyDecay;
        [SaveableField(10)] private bool _ratesCaptured;
        [SaveableField(11)] private double _gainMultiplier;
        [SaveableField(12)] private bool _gainMultiplierCaptured;

        internal string ProposalId => _proposalId;
        internal bool IsSealed => _sealed;
        internal double SettlementDay => _settlementDay;
        internal double GainMultiplier => _gainMultiplierCaptured ? _gainMultiplier : 1d;

        internal DefensivePactTreatyJournal(string proposalId, string winnerId, string loserId,
            List<DefensivePactTreatyTermFact> facts, double settlementDay)
        {
            if (string.IsNullOrWhiteSpace(proposalId)) throw new ArgumentException(nameof(proposalId));
            if (string.IsNullOrWhiteSpace(winnerId) || string.IsNullOrWhiteSpace(loserId) || winnerId == loserId)
                throw new ArgumentException("Invalid treaty principals");
            if (!DefensivePactThreatRules.Finite(settlementDay) || settlementDay < 0)
                throw new ArgumentOutOfRangeException(nameof(settlementDay));
            _proposalId = proposalId;
            _winnerId = winnerId;
            _loserId = loserId;
            _facts = facts ?? throw new ArgumentNullException(nameof(facts));
            _settlementDay = settlementDay;
        }

        internal bool RecordDelivery(TreatyTermRecord term)
        {
            if (_sealed || _invalid) return false;
            if (term == null || _facts == null) { _invalid = true; return false; }
            var fact = _facts.FirstOrDefault(entry => entry?.Term == term);
            if (fact == null)
            {
                var matches = _facts.Where(entry => SameTerm(entry?.Term, term)).Take(2).ToList();
                if (matches.Count != 1) { _invalid = true; return false; }
                fact = matches[0];
            }
            if (fact == null || fact.Delivered) return false;
            fact.MarkDelivered();
            return true;
        }

        internal void Invalidate() { if (!_sealed) _invalid = true; }

        internal bool CaptureGainMultiplier(double multiplier)
        {
            if (_sealed || _invalid || _gainMultiplierCaptured || !DefensivePactThreatRules.Finite(multiplier)
                || multiplier < 0d || multiplier > 2d) return false;
            _gainMultiplier = multiplier;
            _gainMultiplierCaptured = true;
            return true;
        }

        internal bool CaptureRates(double winnerRate, double loserRate)
        {
            if (_sealed || _invalid || _ratesCaptured || !DefensivePactThreatRules.Finite(winnerRate)
                || winnerRate < 0 || !DefensivePactThreatRules.Finite(loserRate) || loserRate < 0) return false;
            _winnerWeeklyDecay = winnerRate;
            _loserWeeklyDecay = loserRate;
            _ratesCaptured = true;
            return true;
        }

        internal bool TryGetRate(string realmId, out double rate)
        {
            rate = 0;
            if (!_ratesCaptured) return false;
            if (realmId == _winnerId) rate = _winnerWeeklyDecay;
            else if (realmId == _loserId) rate = _loserWeeklyDecay;
            else return false;
            return DefensivePactThreatRules.Finite(rate) && rate >= 0;
        }

        internal bool Seal(TreatyProposalRecord proposal)
        {
            if (_sealed || _invalid || !_ratesCaptured || proposal == null || proposal.ProposalId != _proposalId
                || proposal.WinnerKingdomId != _winnerId || proposal.LoserKingdomId != _loserId
                || proposal.State != TreatyProposalState.Applied || !DefensivePactThreatRules.Finite(proposal.ResolvedDay)
                || proposal.ResolvedDay < 0 || (float)_settlementDay != proposal.ResolvedDay || _facts == null
                || !_facts.Select(f => f?.Term).SequenceEqual(
                    (proposal.Terms ?? Array.Empty<TreatyTermRecord>()).Where(t => t != null), TermComparer.Instance)) return false;
            _sealed = true;
            return true;
        }

        internal DefensivePactDraftProjection EvaluateDelivered()
        {
            if (!_sealed || _facts == null) return null;
            var snapshot = DefensivePactTreatySnapshot.FromJournal(_winnerId, _loserId, _facts);
            return snapshot?.EvaluateScaled(new HashSet<TreatyTermRecord>(_facts.Where(f => f.Delivered).Select(f => f.Term)), GainMultiplier);
        }

        private static bool SameTerm(TreatyTermRecord a, TreatyTermRecord b) => a != null && b != null
            && a.Type == b.Type && a.WarScoreCost == b.WarScoreCost && a.SettlementId == b.SettlementId
            && a.FromKingdomId == b.FromKingdomId && a.ToKingdomId == b.ToKingdomId
            && a.GoldAmount == b.GoldAmount && a.DailyGold == b.DailyGold && a.DurationDays == b.DurationDays
            && a.WasOccupiedAtDrafting == b.WasOccupiedAtDrafting && a.HeroId == b.HeroId && a.ClanId == b.ClanId
            && a.TitleId == b.TitleId && a.SecondaryHeroId == b.SecondaryHeroId && a.ThirdKingdomId == b.ThirdKingdomId
            && a.WasVoluntaryOffering == b.WasVoluntaryOffering && a.HostageTier == b.HostageTier
            && a.HostageReceivingClanId == b.HostageReceivingClanId && a.HostageDurationPriced == b.HostageDurationPriced
            && a.HostageDurationBaselineDays == b.HostageDurationBaselineDays;

        private sealed class TermComparer : IEqualityComparer<TreatyTermRecord>
        {
            internal static readonly TermComparer Instance = new TermComparer();
            public bool Equals(TreatyTermRecord x, TreatyTermRecord y) => SameTerm(x, y);
            public int GetHashCode(TreatyTermRecord term) => term == null ? 0 : (int)term.Type;
        }
    }
}
