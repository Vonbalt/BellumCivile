using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.SaveSystem;

namespace BellumCivile
{
    public class TreatyProposalRecord
    {
        [SaveableField(1)] private string _proposalId;
        [SaveableField(2)] private string _warKey;
        [SaveableField(3)] private string _winnerKingdomId;
        [SaveableField(4)] private string _loserKingdomId;
        [SaveableField(5)] private float _createdDay;
        [SaveableField(6)] private int _warScoreBudget;
        [SaveableField(7)] private bool _isForced;
        [SaveableField(8)] private TreatyProposalState _state;
        [SaveableField(9)] private List<TreatyTermRecord> _terms;
        [SaveableField(10)] private float _winnerSupport;
        [SaveableField(11)] private float _loserSupport;
        [SaveableField(12)] private int _winnerOverrideCost;
        [SaveableField(13)] private int _loserOverrideCost;
        [SaveableField(14)] private string _resolutionNote;
        [SaveableField(15)] private int _draftRevision;
        [SaveableField(16)] private int _playerVoteStance;
        [SaveableField(17)] private int _playerInfluenceCommitment;
        [SaveableField(18)] private bool _playerInfluenceSpent;
        [SaveableField(19)] private bool _playerVoteSubmitted;
        [SaveableField(20)] private string _drafterKingdomId;
        [SaveableField(21)] private bool _councilConsequencesApplied;
        [SaveableField(22)] private float _resolvedDay;
        [SaveableField(23)] private bool _hasResolutionDate;

        internal bool TryMarkCouncilConsequences()
        {
            if (_councilConsequencesApplied || (_state != TreatyProposalState.Applied && _state != TreatyProposalState.Rejected))
                return false;
            _councilConsequencesApplied = true;
            return true;
        }

        public string ProposalId => _proposalId;
        public string WarKey => _warKey;
        public string WinnerKingdomId => _winnerKingdomId;
        public string LoserKingdomId => _loserKingdomId;
        public float CreatedDay => _createdDay;
        public float ResolvedDay => _hasResolutionDate ? _resolvedDay : -1f;
        public int WarScoreBudget => _warScoreBudget;
        public bool IsForced => _isForced;
        public TreatyProposalState State => _state;
        public IReadOnlyList<TreatyTermRecord> Terms => _terms;
        internal bool IsWhitePeaceOrEmpty => _terms == null
            || _terms.All(term => term == null)
            || _terms.Any(term => term?.Type == TreatyTermType.WhitePeace);
        public int UsedWarScore => TreatyWarScoreAccounting.Calculate(
            _terms,
            _winnerKingdomId,
            _warScoreBudget).UsedWarScore;
        public int BudgetOverrun => Math.Max(0, UsedWarScore - _warScoreBudget);
        public bool IsOverBudget => BudgetOverrun > 0;
        public float WinnerSupport => _winnerSupport;
        public float LoserSupport => _loserSupport;
        public int WinnerOverrideCost => _winnerOverrideCost;
        public int LoserOverrideCost => _loserOverrideCost;
        public string ResolutionNote => _resolutionNote;
        public int DraftRevision => _draftRevision;
        public TreatyCouncilVoteStance PlayerVoteStance => (TreatyCouncilVoteStance)_playerVoteStance;
        public int PlayerInfluenceCommitment => _playerInfluenceCommitment;
        public bool PlayerInfluenceSpent => _playerInfluenceSpent;
        public bool PlayerVoteSubmitted => _playerVoteSubmitted;
        public string DrafterKingdomId => _drafterKingdomId;

        public TreatyProposalRecord(string warKey, string winnerKingdomId, string loserKingdomId, string drafterKingdomId, float createdDay, int warScoreBudget, bool isForced)
        {
            _proposalId = Guid.NewGuid().ToString("N");
            _warKey = warKey ?? string.Empty;
            _winnerKingdomId = winnerKingdomId ?? string.Empty;
            _loserKingdomId = loserKingdomId ?? string.Empty;
            _drafterKingdomId = drafterKingdomId ?? winnerKingdomId ?? string.Empty;
            _createdDay = createdDay;
            _warScoreBudget = Math.Max(0, warScoreBudget);
            _isForced = isForced;
            _state = TreatyProposalState.ParleyPending;
            _terms = new List<TreatyTermRecord>();
            _resolutionNote = string.Empty;
            _playerVoteStance = (int)TreatyCouncilVoteStance.Abstain;
        }

        public void AddTerm(TreatyTermRecord term)
        {
            if (term != null)
                _terms.Add(term);
        }

        public void ReplaceTerms(IEnumerable<TreatyTermRecord> terms)
        {
            _terms = new List<TreatyTermRecord>(terms?.Where(term => term != null) ?? Enumerable.Empty<TreatyTermRecord>());
            _draftRevision++;
        }

        public void SetPlayerVote(TreatyCouncilVoteStance stance, int influenceCommitment)
        {
            _playerVoteStance = (int)stance;
            _playerInfluenceCommitment = stance == TreatyCouncilVoteStance.Abstain
                ? 0
                : Math.Max(0, influenceCommitment);
            _playerInfluenceSpent = false;
            _playerVoteSubmitted = true;
        }

        public void MarkPlayerInfluenceSpent()
        {
            _playerInfluenceSpent = true;
        }

        public void SetRatification(float winnerSupport, float loserSupport, int winnerOverrideCost, int loserOverrideCost)
        {
            _winnerSupport = winnerSupport;
            _loserSupport = loserSupport;
            _winnerOverrideCost = Math.Max(0, winnerOverrideCost);
            _loserOverrideCost = Math.Max(0, loserOverrideCost);
        }

        public void SetState(TreatyProposalState state, string note = "") => SetState(state, note, -1f);

        public void SetState(TreatyProposalState state, string note, float resolvedDay)
        {
            if (state == TreatyProposalState.Applied || state == TreatyProposalState.Rejected
                || state == TreatyProposalState.Cancelled)
            {
                if (resolvedDay >= 0 && (!_hasResolutionDate || _state != state))
                {
                    _resolvedDay = resolvedDay;
                    _hasResolutionDate = true;
                }
            }
            _state = state;
            _resolutionNote = note ?? string.Empty;
        }
    }
}
