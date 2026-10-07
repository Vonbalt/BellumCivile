using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.SaveSystem;
using BellumCivile.UI.Parley;
using C = BellumCivile.BellumCivileConstants;

namespace BellumCivile.Behaviors
{
    /// <summary>
    /// Resolves foreign NPC wars through a small shared treaty model. The player-facing parley
    /// will use these records and term executors instead of maintaining a separate rule set.
    /// </summary>
    public sealed class ForeignTreatyBehavior : CampaignBehaviorBase
    {
        private List<TreatyProposalRecord> _proposals = new List<TreatyProposalRecord>();
        private List<ActiveTreatyTributeRecord> _activeTributes = new List<ActiveTreatyTributeRecord>();
        private List<PendingTreatyRebelResolutionRecord> _pendingRebelResolutions = new List<PendingTreatyRebelResolutionRecord>();
        private readonly TreatyRatificationService _ratification = new TreatyRatificationService();
        private readonly Dictionary<string, TreatyCouncilSnapshot> _councilSnapshots = new Dictionary<string, TreatyCouncilSnapshot>();
        private string _forcedPlayerParleyInquiryProposalId;
        private string _pendingPlayerParleyOpenProposalId;

        internal bool HasPendingRealmUnionSettlement(string realmId)
        {
            if (string.IsNullOrWhiteSpace(realmId)) return true;
            return _proposals?.Any(p => p != null
                && (p.WinnerKingdomId == realmId || p.LoserKingdomId == realmId)
                && (p.State == TreatyProposalState.ParleyPending || p.State == TreatyProposalState.Accepted)) == true
                || _pendingRebelResolutions?.Any(p => p != null && (p.ParentKingdomId == realmId
                    || p.RebelKingdomId == realmId || p.BeneficiaryKingdomId == realmId)) == true;
        }

        internal IReadOnlyList<ActiveTreatyTributeRecord> GetRealmUnionTributes(string realmId) =>
            (_activeTributes ?? new List<ActiveTreatyTributeRecord>()).Where(t => t != null && t.RemainingDays > 0
                && (t.PayerKingdomId == realmId || t.RecipientKingdomId == realmId)).ToList();

        internal bool TryInheritRealmUnionLegacyTributes(string sourceId, string destinationId,
            Func<string, bool> conflicts, Action beforeWrite, Action returned, out string reason)
        {
            reason = "legacy tribute inheritance callbacks are unavailable";
            if (beforeWrite == null || returned == null) return false;
            if (!RealmUnionLegacyTributeAdapter.TryPrepare(_activeTributes, sourceId, destinationId, conflicts,
                out var replacement, out reason)) return false;
            beforeWrite();
            _activeTributes = replacement;
            returned();
            return true;
        }

        private sealed class TreatyPrisonerReleaseCount
        {
            public Kingdom ReleasingRealm { get; }
            public Kingdom ReceivingRealm { get; }
            public int Count { get; set; }

            public TreatyPrisonerReleaseCount(Kingdom releasingRealm, Kingdom receivingRealm)
            {
                ReleasingRealm = releasingRealm;
                ReceivingRealm = receivingRealm;
            }
        }

        private sealed class TreatyPrisonerReleaseTally
        {
            private readonly List<TreatyPrisonerReleaseCount> _entries = new List<TreatyPrisonerReleaseCount>();

            public IReadOnlyList<TreatyPrisonerReleaseCount> Entries => _entries;

            public void Record(Kingdom releasingRealm, Kingdom receivingRealm)
            {
                if (releasingRealm == null || receivingRealm == null)
                    return;

                TreatyPrisonerReleaseCount entry = _entries.FirstOrDefault(candidate =>
                    candidate.ReleasingRealm == releasingRealm && candidate.ReceivingRealm == receivingRealm);
                if (entry == null)
                {
                    entry = new TreatyPrisonerReleaseCount(releasingRealm, receivingRealm);
                    _entries.Add(entry);
                }
                entry.Count++;
            }
        }

        public override void RegisterEvents()
        {
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
            CampaignEvents.HourlyTickEvent.AddNonSerializedListener(this, OnHourlyTick);
            CampaignEvents.TickEvent.AddNonSerializedListener(this, OnTick);
        }

        public override void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData("BellumCivile_ForeignTreaty_Proposals", ref _proposals);
            dataStore.SyncData("BellumCivile_ForeignTreaty_Tributes", ref _activeTributes);
            dataStore.SyncData("BellumCivile_ForeignTreaty_RebelResolutions", ref _pendingRebelResolutions);
            EnsureCollectionsInitialized();
        }

        public IReadOnlyList<TreatyProposalRecord> GetProposals()
        {
            EnsureCollectionsInitialized();
            return _proposals.ToList();
        }

        public TreatyCouncilEvaluation EvaluateCouncil(WarScoreRecord war, TreatyProposalRecord proposal, Kingdom kingdom, bool winnerSide)
        {
            return _ratification.EvaluateCouncil(war, proposal, kingdom, winnerSide, GetOrCreateCouncilSnapshot(war, proposal));
        }

        public bool WouldCouncilAccept(Kingdom kingdom, TreatyCouncilEvaluation council, bool forcedAcceptance, bool playerRulerAuthorizesOverride, out bool usesOverride)
        {
            usesOverride = false;
            if (council?.IsBudgetBlocked == true)
                return false;
            if (forcedAcceptance)
                return true;
            if (council?.IsSoleRulerDecision == true)
            {
                // A one-clan realm has no vassal quorum to satisfy. Its NPC sovereign follows the
                // treaty assessment directly, while a player sovereign retains the final decision.
                return kingdom?.RulingClan == Clan.PlayerClan
                    ? playerRulerAuthorizesOverride
                    : council.SoleRulerAccepts;
            }
            if (council?.Participation == 0)
            {
                Clan ruler = kingdom?.RulingClan;
                if (ruler == null)
                    return false;
                TreatyCouncilMemberEvaluation rulerAssessment = council.Members.FirstOrDefault(member => member?.Clan == ruler);
                if (rulerAssessment == null)
                    return false;
                return ruler == Clan.PlayerClan
                    ? playerRulerAuthorizesOverride
                    : rulerAssessment.Utility >= 10f;
            }
            if (council?.IsRatified == true)
            {
                if (!ShouldRulerRejectRatifiedTreaty(kingdom, council))
                    return true;

                usesOverride = true;
                return false;
            }
            if (!ShouldRulerOverride(kingdom, council, playerRulerAuthorizesOverride))
                return false;

            usesOverride = true;
            return true;
        }

        public bool TrySetPlayerVote(string proposalId, TreatyCouncilVoteStance stance, int influenceCommitment, out string report)
        {
            report = "player treaty vote could not be recorded";
            TreatyProposalRecord proposal = _proposals.FirstOrDefault(candidate => candidate != null && candidate.ProposalId == proposalId);
            Kingdom playerKingdom = WarPeaceRevampBehavior.GetPlayerPoliticalKingdom();
            if (proposal == null || proposal.State != TreatyProposalState.ParleyPending || playerKingdom == null)
                return false;
            if (playerKingdom.RulingClan == Clan.PlayerClan)
            {
                report = "the ruling clan drafts the treaty rather than casting a vassal vote";
                return false;
            }
            if (proposal.WinnerKingdomId != playerKingdom.StringId && proposal.LoserKingdomId != playerKingdom.StringId)
            {
                report = "the player is not a party to this parley";
                return false;
            }

            int[] validCommitments = { 0, 25, 75, 150 };
            if (stance == TreatyCouncilVoteStance.Abstain)
                influenceCommitment = 0;
            if (!validCommitments.Contains(influenceCommitment))
            {
                report = "invalid influence commitment";
                return false;
            }
            // Abstaining is always free. In particular, a clan with negative influence must
            // still be able to submit a neutral response and leave the parley normally.
            if (influenceCommitment > 0 && influenceCommitment > Clan.PlayerClan.Influence)
            {
                report = "not enough influence for that commitment";
                return false;
            }

            proposal.SetPlayerVote(stance, influenceCommitment);
            BellumCivileLogger.Log($"Player treaty vote updated; proposal={proposal.ProposalId}; stance={stance}; influence={influenceCommitment}.");
            report = "player treaty vote updated";
            return true;
        }

        public bool TryRejectPlayerParley(string proposalId, out string report)
        {
            report = "the treaty could not be rejected";
            TreatyProposalRecord proposal = FindPendingPlayerProposal(proposalId);
            Kingdom playerKingdom = WarPeaceRevampBehavior.GetPlayerPoliticalKingdom();
            if (proposal == null || playerKingdom?.RulingClan != Clan.PlayerClan)
            {
                report = "only the ruler may reject the treaty";
                return false;
            }

            WarScoreRecord war = Campaign.Current?.GetCampaignBehavior<WarScoreBehavior>()?.GetActiveWar(proposal.WarKey);
            if (TryCancelStorylineProtectedProposal(war, proposal, out report))
                return true;

            if (proposal.IsForced)
            {
                report = "terminal war score compels both realms to conclude the treaty";
                return false;
            }

            Kingdom winner = ResolveKingdom(proposal.WinnerKingdomId);
            Kingdom loser = ResolveKingdom(proposal.LoserKingdomId);
            bool playerWins = playerKingdom == winner;
            Kingdom playerSide = playerWins ? winner : loser;
            TreatyCouncilEvaluation rejectedCouncil = null;
            if (war != null && playerSide != null)
            {
                TreatyCouncilEvaluation council = EvaluateCouncil(war, proposal, playerSide, playerWins);
                int rejectionOverrideCost = council.IsRatified ? council.RejectionOverrideCost : 0;
                if (rejectionOverrideCost > Clan.PlayerClan.Influence)
                {
                    report = $"rejecting the council's ratified terms requires {rejectionOverrideCost} influence";
                    return false;
                }

                rejectedCouncil = council;
                SpendRulerOverride(playerSide, rejectionOverrideCost);
            }
            RejectParley(war, proposal, "player ruler refused the treaty", playerKingdom);
            ApplyFinalCouncilConsequences(proposal, playerSide?.RulingClan, rejectedCouncil, false, true);
            report = "treaty rejected";
            return true;
        }

        public bool TrySignPlayerParley(string proposalId, out string report)
        {
            report = "the treaty could not be signed";
            TreatyProposalRecord proposal = FindPendingPlayerProposal(proposalId);
            WarScoreRecord war = Campaign.Current?.GetCampaignBehavior<WarScoreBehavior>()?.GetActiveWar(proposal?.WarKey);
            Kingdom playerKingdom = WarPeaceRevampBehavior.GetPlayerPoliticalKingdom();
            if (proposal == null)
                return false;

            if (!TryPreparePendingProposalDraft(war, proposal, out report))
                return proposal.State == TreatyProposalState.Cancelled;

            Kingdom winner = ResolveKingdom(proposal.WinnerKingdomId);
            Kingdom loser = ResolveKingdom(proposal.LoserKingdomId);
            if (playerKingdom != winner && playerKingdom != loser)
                return false;

            bool playerIsRuler = playerKingdom?.RulingClan == Clan.PlayerClan;
            if (!playerIsRuler)
            {
                if (!proposal.PlayerVoteSubmitted)
                {
                    report = "choose a position before signing the treaty";
                    return false;
                }

                if (proposal.PlayerVoteStance != TreatyCouncilVoteStance.Abstain && proposal.PlayerInfluenceCommitment <= 0)
                {
                    report = "a YAY or NAY vote requires an influence commitment";
                    return false;
                }
            }

            // The parley may remain open while campaign state continues to change. Normalize once
            // more at the final ratification boundary so councils assess only terms that can still
            // be performed, rather than spending influence on a stale spouse, prisoner, or title.
            if (!TryPreparePendingProposalDraft(war, proposal, out report))
                return proposal.State == TreatyProposalState.Cancelled;

            winner = ResolveKingdom(proposal.WinnerKingdomId);
            loser = ResolveKingdom(proposal.LoserKingdomId);

            if (!TryValidateCouncilControlledWhitePeace(war, proposal, out report))
                return proposal.State == TreatyProposalState.Cancelled;

            if (!HasSettlementBudget(proposal, out report))
                return false;

            TreatyCouncilEvaluation winnerCouncil = EvaluateCouncil(war, proposal, winner, winnerSide: true);
            TreatyCouncilEvaluation loserCouncil = EvaluateCouncil(war, proposal, loser, winnerSide: false);
            proposal.SetRatification(winnerCouncil.Support, loserCouncil.Support, winnerCouncil.OverrideCost, loserCouncil.OverrideCost);
            bool playerRulesWinner = playerIsRuler && playerKingdom == winner;
            bool playerRulesLoser = playerIsRuler && playerKingdom == loser;
            bool winnerAccepts = WouldCouncilAccept(winner, winnerCouncil, forcedAcceptance: proposal.IsForced, playerRulerAuthorizesOverride: playerRulesWinner, out bool winnerUsesOverride);
            bool loserAccepts = WouldCouncilAccept(loser, loserCouncil, forcedAcceptance: proposal.IsForced, playerRulerAuthorizesOverride: playerRulesLoser, out bool loserUsesOverride);

            Clan winnerRuler = winner?.RulingClan;
            Clan loserRuler = loser?.RulingClan;
            SpendCouncilCommitments(proposal, winnerCouncil, loserCouncil);
            if (winnerUsesOverride)
                SpendRulerOverride(winner, GetRulerOverrideCost(winnerCouncil, winnerAccepts));
            if (loserUsesOverride)
                SpendRulerOverride(loser, GetRulerOverrideCost(loserCouncil, loserAccepts));
            if (!winnerAccepts || !loserAccepts)
            {
                RejectParley(
                    war,
                    proposal,
                    "one or both councils refused the terms",
                    winnerAccepts ? null : winner,
                    loserAccepts ? null : loser);
                report = "the councils rejected the treaty and the war continues";
                ApplyFinalCouncilConsequences(proposal, winnerRuler, winnerCouncil, winnerAccepts, winnerUsesOverride,
                    loserRuler, loserCouncil, loserAccepts, loserUsesOverride);
                return true;
            }

            if (!ApplyTreaty(war, proposal, winner, loser))
            {
                report = proposal.ResolutionNote;
                return proposal.State == TreatyProposalState.Cancelled;
            }

            report = "treaty signed";
            ApplyFinalCouncilConsequences(proposal, winnerRuler, winnerCouncil, winnerAccepts, winnerUsesOverride,
                loserRuler, loserCouncil, loserAccepts, loserUsesOverride);
            return true;
        }

        private TreatyProposalRecord FindPendingPlayerProposal(string proposalId)
        {
            Kingdom playerKingdom = WarPeaceRevampBehavior.GetPlayerPoliticalKingdom();
            if (playerKingdom == null)
                return null;

            return _proposals.FirstOrDefault(proposal => proposal != null
                && proposal.ProposalId == proposalId
                && proposal.State == TreatyProposalState.ParleyPending
                && (proposal.WinnerKingdomId == playerKingdom.StringId || proposal.LoserKingdomId == playerKingdom.StringId));
        }

        public TreatyProposalRecord GetPendingProposalForPlayer()
        {
            EnsureCollectionsInitialized();
            Kingdom playerKingdom = WarPeaceRevampBehavior.GetPlayerPoliticalKingdom();
            if (playerKingdom == null)
                return null;

            return _proposals.FirstOrDefault(proposal => proposal != null
                && proposal.State == TreatyProposalState.ParleyPending
                && (proposal.WinnerKingdomId == playerKingdom.StringId || proposal.LoserKingdomId == playerKingdom.StringId));
        }

        public TreatyProposalRecord GetPendingProposalForPlayer(WarScoreRecord war)
        {
            Kingdom playerKingdom = WarPeaceRevampBehavior.GetPlayerPoliticalKingdom();
            if (war == null || playerKingdom == null)
                return null;

            EnsureCollectionsInitialized();
            return _proposals.FirstOrDefault(proposal => proposal != null
                && proposal.State == TreatyProposalState.ParleyPending
                && proposal.WarKey == war.WarKey
                && (proposal.WinnerKingdomId == playerKingdom.StringId || proposal.LoserKingdomId == playerKingdom.StringId));
        }

        /// <summary>
        /// Enters the player-facing treaty flow for a tracked foreign war. Both the diplomacy-tab action
        /// and a vanilla peace-vote notification use this path, so neither can create a parallel vanilla vote.
        /// </summary>
        public bool TryOpenPlayerParley(WarScoreRecord war, bool forced, bool preferWhitePeace, string reason, out string report)
        {
            return TryOpenPlayerParley(war, forced, preferWhitePeace, reason, WarPeaceRevampBehavior.GetPlayerPoliticalKingdom(), null, out report);
        }

        public bool TryOpenPlayerParley(WarScoreRecord war, bool forced, bool preferWhitePeace, string reason, Action onClosed, out string report)
        {
            return TryOpenPlayerParley(war, forced, preferWhitePeace, reason, WarPeaceRevampBehavior.GetPlayerPoliticalKingdom(), onClosed, out report);
        }

        public bool TryOpenPlayerParley(WarScoreRecord war, bool forced, bool preferWhitePeace, string reason, Kingdom drafter, Action onClosed, out string report)
        {
            report = "foreign parley could not be opened";
            if (war == null || !WarPeaceRevampBehavior.IsPlayerPoliticalParticipant(
                    ResolveKingdom(war.AttackerKingdomId),
                    ResolveKingdom(war.DefenderKingdomId)))
                return false;

            TreatyProposalRecord proposal = GetPendingProposalForPlayer(war);
            if (proposal == null)
            {
                if (!TryQueuePlayerParley(war, forced, preferWhitePeace, reason, drafter, out report))
                    return false;

                proposal = GetPendingProposalForPlayer(war);
            }

            if (proposal == null)
            {
                report = "parley record was not created";
                return false;
            }

            EnsureProposalHasDraft(war, proposal);
            if (!TryPreparePendingProposalDraft(war, proposal, out report))
                return false;

            if (PeaceParleyInterface.Instance.IsShown)
            {
                report = "parley already open";
                return true;
            }

            if (!PeaceParleyInterface.Instance.Show(proposal, onClosed))
            {
                report = "the parley is already open or no campaign screen is available";
                return false;
            }

            report = "opened Bellum peace parley";
            return true;
        }

        private void EnsureProposalHasDraft(WarScoreRecord war, TreatyProposalRecord proposal)
        {
            if (war == null || proposal == null || proposal.Terms.Count > 0)
                return;

            Kingdom winner = ResolveKingdom(proposal.WinnerKingdomId);
            Kingdom loser = ResolveKingdom(proposal.LoserKingdomId);
            Kingdom drafter = ResolveKingdom(proposal.DrafterKingdomId) ?? winner;
            if (winner != null && loser != null)
                DraftAiTerms(war, proposal, winner, loser, drafter);
            if (proposal.Terms.Count == 0)
                proposal.AddTerm(new TreatyTermRecord(TreatyTermType.WhitePeace, 0));
        }

        private bool TryPreparePendingProposalDraft(
            WarScoreRecord war,
            TreatyProposalRecord proposal,
            out string report)
        {
            report = "the pending treaty proposal is no longer valid";
            if (proposal != null)
                Campaign.Current?.GetCampaignBehavior<HostagePactBehavior>()?.RecoverTreatyDelivery(proposal.ProposalId);
            if (proposal == null || proposal.State != TreatyProposalState.ParleyPending)
                return false;

            Kingdom winner = ResolveKingdom(proposal.WinnerKingdomId);
            Kingdom loser = ResolveKingdom(proposal.LoserKingdomId);
            if (TryCancelStorylineProtectedProposal(war, proposal, out report))
                return false;

            if (war == null || !war.IsActive || !IsTreatyRealmAvailable(winner) || !IsTreatyRealmAvailable(loser))
            {
                CancelInvalidProposal(war, proposal, "war or treaty realm no longer active");
                return false;
            }

            if (!proposal.IsForced && Math.Abs(war.Score) >= C.TreatyParleyAdvantageFlipTolerance)
            {
                string currentWinnerId = war.Score >= 0f
                    ? war.AttackerKingdomId
                    : war.DefenderKingdomId;
                if (!string.Equals(currentWinnerId, proposal.WinnerKingdomId, StringComparison.Ordinal))
                {
                    report = "the balance of the war changed before the treaty could be ratified";
                    CancelInvalidProposal(war, proposal, report);
                    return false;
                }
            }

            EnsureProposalHasDraft(war, proposal);
            if (!TreatyDraftService.TryValidateAndNormalize(
                war,
                proposal,
                winner,
                loser,
                proposal.Terms,
                out List<TreatyTermRecord> repairedDraft,
                out string validationReport,
                repairStateDrift: true,
                allowOverBudget: true))
            {
                repairedDraft = new List<TreatyTermRecord>
                {
                    new TreatyTermRecord(TreatyTermType.WhitePeace, 0)
                };
                BellumCivileLogger.Log(
                    $"Pending treaty draft could not be repaired and was replaced by white peace; proposal={proposal.ProposalId}; war={proposal.WarKey}; reason={validationReport}.");
                validationReport = "invalid pending terms replaced by white peace";
            }
            else if (validationReport == "stale treaty terms repaired")
            {
                BellumCivileLogger.Log(
                    $"Pending treaty draft repaired after campaign state changed; proposal={proposal.ProposalId}; war={proposal.WarKey}; terms={proposal.Terms.Count}->{repairedDraft.Count}.");
            }

            proposal.ReplaceTerms(repairedDraft);
            _councilSnapshots.Remove(proposal.ProposalId);
            report = validationReport;
            return true;
        }

        private bool TryValidateCouncilControlledWhitePeace(
            WarScoreRecord war,
            TreatyProposalRecord proposal,
            out string report)
        {
            report = string.Empty;
            if (!proposal.IsWhitePeaceOrEmpty)
                return true;

            // Forced terminal settlements may have no executable terms. Do not strand a total
            // victory by treating that fallback as a voluntary mutual-exhaustion white peace.
            if (proposal.IsForced)
                return true;

            // Native peace offers can enter through TryQueuePlayerParley even with an NPC
            // proposer. Use final ruling authority, not entry-point or drafter provenance.
            // Player rulers intentionally bypass this automatic gate; normal councils still decide.
            Kingdom playerKingdom = WarPeaceRevampBehavior.GetPlayerPoliticalKingdom();
            if (Clan.PlayerClan != null && playerKingdom?.RulingClan == Clan.PlayerClan
                && (playerKingdom.StringId == proposal.WinnerKingdomId
                    || playerKingdom.StringId == proposal.LoserKingdomId))
                return true;

            WarScoreBehavior scores = Campaign.Current?.GetCampaignBehavior<WarScoreBehavior>();
            if (scores?.IsMutualWhitePeaceEligible(war, out _, out _) == true)
                return true;

            report = "the council-controlled white peace no longer meets mutual peace eligibility";
            CancelInvalidProposal(war, proposal, report);
            return false;
        }

        private void CancelInvalidProposal(WarScoreRecord war, TreatyProposalRecord proposal, string reason)
        {
            if (proposal == null)
                return;

            proposal.SetState(TreatyProposalState.Cancelled, reason, Campaign.Current == null ? -1f : (float)CampaignTime.Now.ToDays);
            war?.EndParley();
            _councilSnapshots.Remove(proposal.ProposalId);
            BellumCivileLogger.Log(
                $"Pending treaty proposal cancelled after campaign state changed; proposal={proposal.ProposalId}; war={proposal.WarKey}; reason={reason}.");
        }

        private bool TryCancelStorylineProtectedProposal(
            WarScoreRecord war,
            TreatyProposalRecord proposal,
            out string report)
        {
            report = null;
            if (war == null || war.ConflictType != WarScoreConflictType.ForeignWar)
                return false;

            Kingdom attacker = ResolveKingdom(war.AttackerKingdomId);
            Kingdom defender = ResolveKingdom(war.DefenderKingdomId);
            if (!StorylineWarProtectionHelper.IsPeaceBlocked(attacker, defender))
                return false;

            report = "the active campaign quest prevents peace in this war";
            CancelInvalidProposal(war, proposal, report);
            if (proposal != null && _pendingPlayerParleyOpenProposalId == proposal.ProposalId)
                _pendingPlayerParleyOpenProposalId = null;
            if (proposal != null && _forcedPlayerParleyInquiryProposalId == proposal.ProposalId)
                _forcedPlayerParleyInquiryProposalId = null;
            return true;
        }

        private static bool IsTreatyRealmAvailable(Kingdom kingdom)
        {
            return kingdom != null && !kingdom.IsEliminated && kingdom.RulingClan?.Leader != null;
        }

        public bool TryQueuePlayerParley(WarScoreRecord war, bool forced, bool preferWhitePeace, string reason, out string report)
        {
            return TryQueuePlayerParley(war, forced, preferWhitePeace, reason, WarPeaceRevampBehavior.GetPlayerPoliticalKingdom(), out report);
        }

        public bool TryQueuePlayerParley(WarScoreRecord war, bool forced, bool preferWhitePeace, string reason, Kingdom drafter, out string report)
        {
            return TryQueueParley(war, forced, preferWhitePeace, reason, requireAiOnly: false, automaticRequest: false, drafter, out report);
        }

        public bool TryQueueDeferredPlayerParleyOpen(
            WarScoreRecord war,
            bool forced,
            bool preferWhitePeace,
            string reason,
            Kingdom drafter,
            out string report)
        {
            if (!TryQueuePlayerParley(war, forced, preferWhitePeace, reason, drafter, out report))
                return false;

            TreatyProposalRecord proposal = GetPendingProposalForPlayer(war);
            if (proposal == null)
            {
                report = "parley record was not created";
                return false;
            }

            _pendingPlayerParleyOpenProposalId = proposal.ProposalId;
            report = $"{report}; deferred until the current inquiry closes";
            return true;
        }

        public string BuildDebugReport()
        {
            EnsureCollectionsInitialized();
            if (_proposals.Count == 0 && _activeTributes.Count == 0)
                return "No Bellum foreign treaty proposals or active tributes.";

            List<string> lines = new List<string>();
            foreach (TreatyProposalRecord proposal in _proposals.OrderByDescending(proposal => proposal.CreatedDay).Take(12))
            {
                Kingdom winner = ResolveKingdom(proposal.WinnerKingdomId);
                Kingdom loser = ResolveKingdom(proposal.LoserKingdomId);
                string terms = string.Join(", ", proposal.Terms.Select(term => term.Type + "(" + term.WarScoreCost + " WS)"));
                lines.Add($"proposal={proposal.ProposalId}; war={proposal.WarKey}; {winner?.Name?.ToString() ?? proposal.WinnerKingdomId} -> {loser?.Name?.ToString() ?? proposal.LoserKingdomId}; state={proposal.State}; forced={proposal.IsForced}; score={proposal.UsedWarScore}/{proposal.WarScoreBudget}; winner_support={proposal.WinnerSupport:0.0}; loser_support={proposal.LoserSupport:0.0}; terms=[{terms}]; note={proposal.ResolutionNote}");
            }

            foreach (ActiveTreatyTributeRecord tribute in _activeTributes)
                lines.Add($"tribute: {tribute.PayerKingdomId} -> {tribute.RecipientKingdomId}; daily={tribute.DailyGold}; remaining_days={tribute.RemainingDays}");
            return string.Join(Environment.NewLine, lines);
        }

        public bool TryQueueAiParley(WarScoreRecord war, bool forced, bool preferWhitePeace, string reason, Kingdom drafter, out string report)
        {
            if (!forced && war != null && !war.CanReconsiderPeace((float)CampaignTime.Now.ToDays))
            {
                report = "automatic peace negotiations are awaiting reconsideration";
                return false;
            }
            Kingdom attacker = ResolveKingdom(war?.AttackerKingdomId);
            Kingdom defender = ResolveKingdom(war?.DefenderKingdomId);
            bool playerInvolved = WarPeaceRevampBehavior.IsPlayerPoliticalParticipant(attacker, defender);
            return TryQueueParley(war, forced, preferWhitePeace, reason, requireAiOnly: !playerInvolved, automaticRequest: true, drafter, out report);
        }

        public bool TryReplacePlayerDraft(string proposalId, IEnumerable<TreatyTermRecord> terms, out string report)
        {
            report = "treaty draft could not be changed";
            TreatyProposalRecord proposal = _proposals.FirstOrDefault(candidate => candidate != null && candidate.ProposalId == proposalId);
            if (proposal == null || proposal.State != TreatyProposalState.ParleyPending)
            {
                report = "no pending treaty proposal";
                return false;
            }

            Kingdom playerKingdom = WarPeaceRevampBehavior.GetPlayerPoliticalKingdom();
            if (playerKingdom == null
                || (proposal.WinnerKingdomId != playerKingdom.StringId && proposal.LoserKingdomId != playerKingdom.StringId)
                || playerKingdom.RulingClan != Clan.PlayerClan)
            {
                report = "only a ruler participating in the parley may draft these terms";
                return false;
            }

            WarScoreRecord war = Campaign.Current?.GetCampaignBehavior<WarScoreBehavior>()?.GetActiveWar(proposal.WarKey);
            Kingdom winner = ResolveKingdom(proposal.WinnerKingdomId);
            Kingdom loser = ResolveKingdom(proposal.LoserKingdomId);
            if (!TreatyDraftService.TryValidateAndNormalize(
                war,
                proposal,
                winner,
                loser,
                terms,
                out List<TreatyTermRecord> draft,
                out report,
                allowOverBudget: true))
                return false;

            proposal.ReplaceTerms(draft);
            if (war != null && winner != null && loser != null)
            {
                TreatyCouncilEvaluation winnerCouncil = EvaluateCouncil(war, proposal, winner, winnerSide: true);
                TreatyCouncilEvaluation loserCouncil = EvaluateCouncil(war, proposal, loser, winnerSide: false);
                proposal.SetRatification(winnerCouncil.Support, loserCouncil.Support, winnerCouncil.OverrideCost, loserCouncil.OverrideCost);
            }

            report = "treaty draft updated";
            return true;
        }

        private bool TryQueueParley(WarScoreRecord war, bool forced, bool preferWhitePeace, string reason, bool requireAiOnly, bool automaticRequest, Kingdom drafter, out string report)
        {
            report = "foreign parley could not be queued";
            if (!WarPeaceRevampBehavior.IsRevampEnabled() || war == null || !war.IsActive || war.ConflictType != WarScoreConflictType.ForeignWar)
                return false;

            Kingdom attacker = ResolveKingdom(war.AttackerKingdomId);
            Kingdom defender = ResolveKingdom(war.DefenderKingdomId);
            if (StorylineWarProtectionHelper.IsPeaceBlocked(attacker, defender))
            {
                report = "the active campaign quest prevents peace in this war";
                return false;
            }

            if (ClientKingdomBehavior.Instance?.IsAuxiliaryClientWar(attacker, defender) == true)
            {
                report = "client auxiliary wars are settled by the suzerain's principal treaty";
                return false;
            }
            bool playerInvolved = WarPeaceRevampBehavior.IsPlayerPoliticalParticipant(attacker, defender);
            if (requireAiOnly && !IsAiOnlyWar(attacker, defender))
            {
                report = "player involved; waiting for player-facing parley";
                return false;
            }
            if (!requireAiOnly && !playerInvolved)
            {
                report = "the player is not a party to this war";
                return false;
            }

            TreatyProposalRecord existing = _proposals.FirstOrDefault(candidate => candidate != null
                && candidate.WarKey == war.WarKey
                && candidate.State == TreatyProposalState.ParleyPending);
            if (existing != null)
            {
                report = "parley already pending";
                return true;
            }

            Kingdom winner = war.Score >= 0f ? attacker : defender;
            Kingdom loser = winner == attacker ? defender : attacker;
            if (drafter != winner && drafter != loser)
                drafter = winner;
            TreatyProposalRecord proposal = new TreatyProposalRecord(
                war.WarKey,
                winner.StringId,
                loser.StringId,
                drafter.StringId,
                (float)CampaignTime.Now.ToDays,
                (int)Math.Floor(Math.Abs(war.Score)),
                forced);
            if (preferWhitePeace)
                proposal.AddTerm(new TreatyTermRecord(TreatyTermType.WhitePeace, 0));
            else
                DraftAiTerms(war, proposal, winner, loser, drafter);

            // Reject automatic empty/white fallbacks before registration can expose a vassal
            // parley. This call-local distinction does not change a reopened proposal's origin.
            if (automaticRequest && !TryValidateCouncilControlledWhitePeace(war, proposal, out report))
            {
                if (!forced) war.RecordPeaceDeferral((float)CampaignTime.Now.ToDays);
                _councilSnapshots.Remove(proposal.ProposalId);
                return false;
            }

            if (automaticRequest && !CanSubmitAutomaticDraft(war, proposal, winner, loser))
            {
                report = "no mutually acceptable automatic treaty is available yet";
                return false;
            }
            if (!war.BeginParley(forced, (float)CampaignTime.Now.ToDays))
            {
                _councilSnapshots.Remove(proposal.ProposalId);
                report = "war could not enter parley state";
                return false;
            }

            _proposals.Add(proposal);
            _councilSnapshots.Remove(proposal.ProposalId);
            BellumCivileLogger.Log($"Foreign treaty parley queued; war={war.WarKey}; winner={winner.StringId}; loser={loser.StringId}; forced={forced}; white_peace={preferWhitePeace}; reason={reason}.");
            if (forced && playerInvolved)
                TryShowForcedPlayerParleyInquiry(war, proposal);
            report = $"parley pending: {winner.Name} versus {loser.Name}";
            return true;
        }

        private bool CanSubmitAutomaticDraft(WarScoreRecord war, TreatyProposalRecord proposal, Kingdom winner, Kingdom loser)
        {
            if (proposal.IsForced) return true;
            try
            {
                var winnerCouncil = EvaluateCouncil(war, proposal, winner, winnerSide: true);
                var loserCouncil = EvaluateCouncil(war, proposal, loser, winnerSide: false);
                // A human ruler's answer is unknown until presented with the offer.
                bool Accepts(Kingdom realm, TreatyCouncilEvaluation council) =>
                    realm?.RulingClan != null && realm.RulingClan == Clan.PlayerClan
                        ? council != null && !council.IsBudgetBlocked
                        : WouldCouncilAccept(realm, council, false, false, out _);
                if (Accepts(winner, winnerCouncil) && Accepts(loser, loserCouncil)) return true;
                float day = (float)CampaignTime.Now.ToDays;
                war.RecordPeaceDeferral(day);
                string Obstacles(TreatyCouncilEvaluation council) => string.Join(",", (council?.Members ?? Array.Empty<TreatyCouncilMemberEvaluation>())
                    .SelectMany(m => m.Reasons).Where(r => r.Amount < 0).GroupBy(r => r.Label)
                    .OrderBy(g => g.Sum(r => r.Amount)).Take(3).Select(g => g.Key));
                BellumCivileLogger.Log($"Automatic treaty deferred before submission; war={war.WarKey}; started={war.StartedDay:0.0}; day={day:0.0}; retry_day={day + WarScoreRecord.PeaceRetryDays:0.0}; score={war.Score:0.0}; used={proposal.UsedWarScore}/{proposal.WarScoreBudget}; winner_support={winnerCouncil?.Support:0.0}; loser_support={loserCouncil?.Support:0.0}; winner_obstacles={Obstacles(winnerCouncil)}; loser_obstacles={Obstacles(loserCouncil)}; reason=no_mutually_acceptable_draft.");
                return false;
            }
            finally
            {
                _councilSnapshots.Remove(proposal.ProposalId);
            }
        }

        private bool TryShowForcedPlayerParleyInquiry(WarScoreRecord war, TreatyProposalRecord proposal)
        {
            Kingdom playerKingdom = WarPeaceRevampBehavior.GetPlayerPoliticalKingdom();
            Kingdom winner = ResolveKingdom(proposal?.WinnerKingdomId);
            Kingdom loser = ResolveKingdom(proposal?.LoserKingdomId);
            if (war == null
                || proposal == null
                || proposal.State != TreatyProposalState.ParleyPending
                || !proposal.IsForced
                || playerKingdom == null
                || (playerKingdom != winner && playerKingdom != loser)
                || _forcedPlayerParleyInquiryProposalId == proposal.ProposalId
                || InformationManager.IsAnyInquiryActive()
                || PeaceParleyInterface.Instance.IsShown)
            {
                return false;
            }

            Kingdom enemyRealm = playerKingdom == winner ? loser : winner;
            Hero ruler = playerKingdom.RulingClan?.Leader;
            string rulerName = ruler?.Name?.ToString() ?? playerKingdom.Name?.ToString() ?? string.Empty;
            if (ruler != null)
                FeudalTitleDisplayHelper.TryFormatHeroName(ruler, rulerName, out rulerName);

            bool playerIsRuler = playerKingdom.RulingClan == Clan.PlayerClan;
            bool playerWasDefeated = playerKingdom == loser;
            TextObject body;
            if (playerIsRuler)
            {
                body = playerWasDefeated
                    ? new TextObject("{=BC_Parley_ForcedSurrenderDefeatedRulerBody}An envoy from {ENEMY_REALM} has arrived bearing terms for your realm's unconditional surrender. With further resistance impossible, you summon your vassals to witness the peace treaty.")
                    : new TextObject("{=BC_Parley_ForcedSurrenderVictoriousRulerBody}An envoy from {ENEMY_REALM} has arrived to offer unconditional surrender. You summon your vassals to witness the peace treaty.");
            }
            else
            {
                body = playerWasDefeated
                    ? new TextObject("{=BC_Parley_ForcedSurrenderDefeatedBody}An envoy from {ENEMY_REALM} has arrived bearing terms for your realm's unconditional surrender. {RULER_NAME} has summoned all vassals to witness the peace treaty.")
                    : new TextObject("{=BC_Parley_ForcedSurrenderVictoriousBody}An envoy from {ENEMY_REALM} has arrived to offer unconditional surrender. {RULER_NAME} has summoned all vassals to witness the peace treaty.");
            }
            body.SetTextVariable("ENEMY_REALM", enemyRealm?.Name ?? new TextObject("?"));
            body.SetTextVariable("RULER_NAME", rulerName);

            _forcedPlayerParleyInquiryProposalId = proposal.ProposalId;
            SoundEvent.PlaySound2D("event:/ui/reign/vote");
            InformationManager.ShowInquiry(new InquiryData(
                new TextObject("{=BC_Parley_ForcedSurrenderTitle}Unconditional Surrender").ToString(),
                body.ToString(),
                true,
                false,
                new TextObject("{=BC_Parley_ReviewTerms}Review Terms").ToString(),
                string.Empty,
                () =>
                {
                    // Inquiry callbacks run before their Gauntlet layer is always fully dismissed. Opening
                    // another overlay here can therefore fail even though the treaty remains valid. Let the
                    // next safe campaign UI tick perform the handoff instead.
                    _pendingPlayerParleyOpenProposalId = proposal.ProposalId;
                    BellumCivileLogger.Log($"Forced player parley queued after envoy inquiry; war={proposal.WarKey}; proposal={proposal.ProposalId}.");
                },
                null), true, false);
            return true;
        }

        private void OnTick(float dt)
        {
            if (string.IsNullOrWhiteSpace(_pendingPlayerParleyOpenProposalId)
                || InformationManager.IsAnyInquiryActive()
                || PeaceParleyInterface.Instance.IsShown)
            {
                return;
            }

            EnsureCollectionsInitialized();
            TreatyProposalRecord proposal = _proposals.FirstOrDefault(candidate => candidate != null
                && candidate.ProposalId == _pendingPlayerParleyOpenProposalId);
            if (proposal == null || proposal.State != TreatyProposalState.ParleyPending)
            {
                _pendingPlayerParleyOpenProposalId = null;
                _forcedPlayerParleyInquiryProposalId = null;
                return;
            }

            WarScoreRecord war = Campaign.Current?.GetCampaignBehavior<WarScoreBehavior>()?
                .GetActiveWars()
                .FirstOrDefault(candidate => candidate?.WarKey == proposal.WarKey);
            if (war == null || !war.IsActive)
            {
                _pendingPlayerParleyOpenProposalId = null;
                _forcedPlayerParleyInquiryProposalId = null;
                return;
            }

            Kingdom drafter = ResolveKingdom(proposal.DrafterKingdomId);
            bool preferWhitePeace = proposal.Terms.Any(term => term?.Type == TreatyTermType.WhitePeace);
            if (!TryOpenPlayerParley(
                    war,
                    proposal.IsForced,
                    preferWhitePeace,
                    "deferred peace-offer handoff",
                    drafter,
                    null,
                    out string report))
            {
                // A campaign screen may still be transitioning. Keep the request queued and retry on a
                // later tick; the normal treaty response timer remains the final fallback.
                return;
            }

            _pendingPlayerParleyOpenProposalId = null;
            _forcedPlayerParleyInquiryProposalId = null;
            BellumCivileLogger.Log($"Player parley opened after deferred inquiry handoff; war={proposal.WarKey}; forced={proposal.IsForced}; report={report}.");
        }

        private void OnDailyTick()
        {
            if (!WarPeaceRevampBehavior.IsRevampEnabled())
                return;

            EnsureCollectionsInitialized();
            ProcessTributes();

            WarScoreBehavior warScore = Campaign.Current?.GetCampaignBehavior<WarScoreBehavior>();
            if (warScore == null)
                return;

            float today = (float)CampaignTime.Now.ToDays;
            foreach (TreatyProposalRecord proposal in _proposals
                .Where(proposal => proposal != null
                    && proposal.State == TreatyProposalState.ParleyPending
                    && proposal.CreatedDay < today)
                .ToList())
            {
                WarScoreRecord war = warScore.GetActiveWars().FirstOrDefault(candidate => candidate.WarKey == proposal.WarKey);
                Kingdom winner = ResolveKingdom(proposal.WinnerKingdomId);
                Kingdom loser = ResolveKingdom(proposal.LoserKingdomId);
                if (TryCancelStorylineProtectedProposal(war, proposal, out _))
                    continue;

                if (IsAiOnlyWar(winner, loser))
                {
                    ResolvePendingParley(war, proposal);
                    continue;
                }

                Kingdom playerKingdom = WarPeaceRevampBehavior.GetPlayerPoliticalKingdom();
                if (playerKingdom != winner && playerKingdom != loser)
                    continue;

                if (proposal.IsForced)
                {
                    bool inquiryShown = TryShowForcedPlayerParleyInquiry(war, proposal);
                    BellumCivileLogger.Log($"Forced player parley envoy check; war={proposal.WarKey}; shown={inquiryShown}.");
                    continue;
                }

                if (today - proposal.CreatedDay < C.TreatyPlayerParleyResponseDays)
                    continue;

                if (playerKingdom?.RulingClan == Clan.PlayerClan)
                {
                    bool preferWhitePeace = proposal.Terms.Any(term => term?.Type == TreatyTermType.WhitePeace);
                    TryOpenPlayerParley(war, proposal.IsForced, preferWhitePeace, "ruler parley response window elapsed", out string report);
                    BellumCivileLogger.Log($"Player ruler parley reminder fired; war={proposal.WarKey}; report={report}.");
                }
                else
                {
                    BellumCivileLogger.Log($"Auto-resolving unattended player-vassal parley; war={proposal.WarKey}; response_days={C.TreatyPlayerParleyResponseDays}.");
                    ResolvePendingParley(war, proposal, allowPlayerVassal: true);
                }
            }
        }

        private void ResolvePendingParley(WarScoreRecord war, TreatyProposalRecord proposal, bool allowPlayerVassal = false)
        {
            if (!TryPreparePendingProposalDraft(war, proposal, out _))
                return;

            Kingdom winner = ResolveKingdom(proposal.WinnerKingdomId);
            Kingdom loser = ResolveKingdom(proposal.LoserKingdomId);
            Kingdom playerKingdom = WarPeaceRevampBehavior.GetPlayerPoliticalKingdom();
            bool playerVassalInvolved = playerKingdom != null
                && playerKingdom.RulingClan != Clan.PlayerClan
                && (playerKingdom == winner || playerKingdom == loser);
            if (!IsAiOnlyWar(winner, loser) && !(allowPlayerVassal && playerVassalInvolved))
            {
                war.EndParley();
                proposal.SetState(TreatyProposalState.Cancelled, "player entered the conflict", (float)CampaignTime.Now.ToDays);
                _councilSnapshots.Remove(proposal.ProposalId);
                return;
            }

            // AI parleys also spend at least a campaign day pending. Revalidate immediately before
            // the final council tally so any dynamic term removed here is reflected in both votes.
            if (!TryPreparePendingProposalDraft(war, proposal, out _))
                return;

            winner = ResolveKingdom(proposal.WinnerKingdomId);
            loser = ResolveKingdom(proposal.LoserKingdomId);

            if (!TryValidateCouncilControlledWhitePeace(war, proposal, out _))
                return;

            if (!HasSettlementBudget(proposal, out string budgetReport))
            {
                CancelInvalidProposal(war, proposal, budgetReport);
                return;
            }

            EvaluateRatification(
                war,
                proposal,
                winner,
                loser,
                out TreatyCouncilEvaluation winnerCouncil,
                out TreatyCouncilEvaluation loserCouncil,
                out bool winnerAccepts,
                out bool loserAccepts,
                out bool winnerUsesOverride,
                out bool loserUsesOverride);
            Clan winnerRuler = winner?.RulingClan;
            Clan loserRuler = loser?.RulingClan;
            SpendCouncilCommitments(proposal, winnerCouncil, loserCouncil);
            if (winnerUsesOverride)
                SpendRulerOverride(winner, GetRulerOverrideCost(winnerCouncil, winnerAccepts));
            if (loserUsesOverride)
                SpendRulerOverride(loser, GetRulerOverrideCost(loserCouncil, loserAccepts));
            if (!winnerAccepts || !loserAccepts)
            {
                RejectParley(
                    war,
                    proposal,
                    "council refused the terms",
                    winnerAccepts ? null : winner,
                    loserAccepts ? null : loser);
                ApplyFinalCouncilConsequences(proposal, winnerRuler, winnerCouncil, winnerAccepts, winnerUsesOverride,
                    loserRuler, loserCouncil, loserAccepts, loserUsesOverride);
                return;
            }

            if (ApplyTreaty(war, proposal, winner, loser))
                ApplyFinalCouncilConsequences(proposal, winnerRuler, winnerCouncil, winnerAccepts, winnerUsesOverride,
                    loserRuler, loserCouncil, loserAccepts, loserUsesOverride);
        }

        private void RejectParley(
            WarScoreRecord war,
            TreatyProposalRecord proposal,
            string reason,
            Kingdom firstRejectingRealm = null,
            Kingdom secondRejectingRealm = null)
        {
            if (proposal == null)
                return;

            proposal.SetState(TreatyProposalState.Rejected, reason, (float)CampaignTime.Now.ToDays);
            if (!proposal.IsForced) war?.RecordPeaceRejection((float)CampaignTime.Now.ToDays);
            war?.EndParley();
            _councilSnapshots.Remove(proposal.ProposalId);
            ShowTreatyRejectedMessage(proposal, firstRejectingRealm, secondRejectingRealm);
            BellumCivileLogger.Log(
                $"Foreign treaty rejected; proposal={proposal.ProposalId}; war={proposal.WarKey}; drafter={proposal.DrafterKingdomId}; rejecting_realms={firstRejectingRealm?.StringId ?? "none"},{secondRejectingRealm?.StringId ?? "none"}; reason={reason}.");
        }

        private void DraftAiTerms(WarScoreRecord war, TreatyProposalRecord proposal, Kingdom winner, Kingdom loser, Kingdom drafter)
        {
            try
            {
                if (TryDraftStrategicAiTerms(war, proposal, winner, loser, drafter))
                    return;
            }
            catch (Exception ex)
            {
                BellumCivileLogger.Log($"Strategic AI treaty drafting failed; war={war?.WarKey}; error={ex}");
            }

            DraftAiTermsLegacy(war, proposal, winner, loser, drafter);
        }

        private bool TryDraftStrategicAiTerms(
            WarScoreRecord war,
            TreatyProposalRecord proposal,
            Kingdom winner,
            Kingdom loser,
            Kingdom drafter)
        {
            using (new TreatyDraftReadScope())
                return TryDraftStrategicAiTermsCore(war, proposal, winner, loser, drafter);
        }

        private bool TryDraftStrategicAiTermsCore(
            WarScoreRecord war, TreatyProposalRecord proposal, Kingdom winner, Kingdom loser, Kingdom drafter)
        {
            if (war == null || proposal == null || winner == null || loser == null)
                return false;

            drafter = drafter == loser ? loser : winner;
            var courtPreference = drafter == winner
                ? CourtAgendaBehavior.Current?.CrownTreatyPreference(drafter, loser, war) : null;
            int budget = Math.Max(0, proposal.WarScoreBudget);
            int preferredSpend = TreatyAiDraftService.CalculateDesiredSpend(proposal, winner, loser, drafter,war);
            int step = TreatyAiDraftService.GetCouncilAdjustmentStep(budget);
            List<int> spendTargets = BuildAiSpendTargets(preferredSpend, budget, step);
            TreatyAiDraftResult bestDraft = null;
            float bestScore = float.MinValue;
            TreatyCouncilEvaluation bestWinnerCouncil = null;
            TreatyCouncilEvaluation bestLoserCouncil = null;
            bool bestWinnerAccepts = false, bestLoserAccepts = false;
            bool bestWinnerOverride = false, bestLoserOverride = false;
            float bestObjectivePreference = 0f;
            bool bestDraftRatifiedByBoth = false;
            bool bestIsObjectiveAlternative = false;

            foreach (int spendTarget in spendTargets)
            {
                TreatyAiDraftResult reciprocalCandidate = TreatyAiDraftService.BuildDraft(
                    war,
                    proposal,
                    winner,
                    loser,
                    drafter,
                    spendTarget,
                    allowReciprocalPrisonerOfferings: true);
                List<TreatyAiDraftResult> candidates = new List<TreatyAiDraftResult>
                {
                    reciprocalCandidate
                };
                bool containsReciprocalPrisonerOffering = reciprocalCandidate.Terms.Any(term => term != null
                    && term.Type == TreatyTermType.ReleasePrisoner
                    && term.FromKingdomId == winner.StringId
                    && term.ToKingdomId == loser.StringId);
                if (containsReciprocalPrisonerOffering)
                {
                    candidates.Add(TreatyAiDraftService.BuildDraft(
                        war,
                        proposal,
                        winner,
                        loser,
                        drafter,
                        spendTarget,
                        allowReciprocalPrisonerOfferings: false));
                }

                var objectiveDraft = TreatyAiDraftService.BuildObjectiveDraft(war, proposal, winner, loser, drafter, spendTarget, courtPreference);
                if (objectiveDraft != null) candidates.Add(objectiveDraft);

                foreach (TreatyAiDraftResult candidate in candidates)
                {
                    if (!TreatyDraftService.TryValidateAndNormalize(
                        war,
                        proposal,
                        winner,
                        loser,
                        candidate.Terms,
                        out List<TreatyTermRecord> normalized,
                        out string validationReport))
                    {
                        BellumCivileLogger.Log(
                            $"AI treaty draft candidate rejected; war={war.WarKey}; posture={candidate.Posture}; target={spendTarget}; reason={validationReport}.");
                        continue;
                    }

                    proposal.ReplaceTerms(normalized);
                    TreatyCouncilEvaluation winnerCouncil = EvaluateCouncil(war, proposal, winner, winnerSide: true);
                    TreatyCouncilEvaluation loserCouncil = EvaluateCouncil(war, proposal, loser, winnerSide: false);
                    bool winnerAccepts = WouldCouncilAccept(
                        winner,
                        winnerCouncil,
                        forcedAcceptance: false,
                        playerRulerAuthorizesOverride: false,
                        out bool winnerOverride);
                    bool loserAccepts = WouldCouncilAccept(
                        loser,
                        loserCouncil,
                        forcedAcceptance: proposal.IsForced,
                        playerRulerAuthorizesOverride: false,
                        out bool loserOverride);

                    var normalizedCandidate = new TreatyAiDraftResult(normalized, candidate.Posture, candidate.TargetSpend,
                        candidate.Summary, candidate.IsObjectiveAlternative);
                    float councilScore = ScoreAiDraftCandidate(
                        normalizedCandidate,
                        preferredSpend,
                        winner.StringId,
                        budget,
                        winnerCouncil,
                        loserCouncil,
                        winnerAccepts,
                        loserAccepts,
                        winnerOverride,
                        loserOverride,
                        proposal.IsForced);
                    float objectivePreference = courtPreference?.Score(normalized) ?? 0;
                    councilScore += objectivePreference;
                    if (councilScore > bestScore || (councilScore == bestScore && bestIsObjectiveAlternative && !candidate.IsObjectiveAlternative))
                    {
                        bestScore = councilScore;
                        bestDraft = normalizedCandidate;
                        bestWinnerCouncil = winnerCouncil;
                        bestLoserCouncil = loserCouncil;
                        bestWinnerAccepts = winnerAccepts;
                        bestLoserAccepts = loserAccepts;
                        bestWinnerOverride = winnerOverride;
                        bestLoserOverride = loserOverride;
                        bestObjectivePreference = objectivePreference;
                        bestDraftRatifiedByBoth = winnerAccepts && loserAccepts;
                        bestIsObjectiveAlternative = candidate.IsObjectiveAlternative;
                    }
                }
            }

            if (bestDraft == null)
                return false;

            proposal.ReplaceTerms(bestDraft.Terms);
            proposal.SetRatification(
                bestWinnerCouncil.Support,
                bestLoserCouncil.Support,
                bestWinnerCouncil.OverrideCost,
                bestLoserCouncil.OverrideCost);
            string bestCouncilReport =
                $"winner={FormatCouncilResult(winner, bestWinnerCouncil, bestWinnerAccepts, bestWinnerOverride)}; "
                + $"loser={FormatCouncilResult(loser, bestLoserCouncil, bestLoserAccepts, bestLoserOverride)}; court_preference={bestObjectivePreference:0.0}";
            BellumCivileLogger.Log(
                $"AI treaty draft selected{(bestDraftRatifiedByBoth ? string.Empty : " as best available compromise")}; war={war.WarKey}; drafter={drafter.StringId}; "
                + $"posture={bestDraft.Posture}; target={bestDraft.TargetSpend}; used={proposal.UsedWarScore}/{budget}; "
                + $"score={bestScore:0.0}; {bestCouncilReport}; motives={bestDraft.Summary}.");
            return true;
        }

        private static List<int> BuildAiSpendTargets(int preferredSpend, int budget, int step)
        {
            budget = Math.Max(0, budget);
            preferredSpend = Math.Max(0, Math.Min(budget, preferredSpend));
            step = Math.Max(1, step);
            List<int> targets = new List<int>();

            void Add(int value)
            {
                value = Math.Max(0, Math.Min(budget, value));
                if (!targets.Contains(value))
                    targets.Add(value);
            }

            Add(preferredSpend);
            for (int distance = step; distance <= budget; distance += step)
            {
                Add(preferredSpend - distance);
                Add(preferredSpend + distance);
            }
            Add(budget);
            Add(0);
            return targets;
        }

        private static float ScoreAiDraftCandidate(
            TreatyAiDraftResult draft,
            int preferredSpend,
            string winnerKingdomId,
            int warScoreBudget,
            TreatyCouncilEvaluation winnerCouncil,
            TreatyCouncilEvaluation loserCouncil,
            bool winnerAccepts,
            bool loserAccepts,
            bool winnerOverride,
            bool loserOverride,
            bool forced)
        {
            int used = TreatyWarScoreAccounting.Calculate(
                draft?.Terms,
                winnerKingdomId,
                warScoreBudget).UsedWarScore;
            float score = 0f;
            score += winnerAccepts ? 10000f : 0f;
            score += loserAccepts ? 10000f : 0f;
            score += Math.Max(-200f, Math.Min(200f, winnerCouncil?.Support ?? 0f));
            if (!forced)
                score += Math.Max(-200f, Math.Min(200f, loserCouncil?.Support ?? 0f));
            score += used * 2f;
            score -= Math.Abs(used - preferredSpend) * 6f;
            score -= winnerOverride ? 50f + GetRulerOverrideCost(winnerCouncil, winnerAccepts) * 0.25f : 0f;
            score -= loserOverride ? 50f + GetRulerOverrideCost(loserCouncil, loserAccepts) * 0.25f : 0f;
            return score;
        }

        private static string FormatCouncilResult(
            Kingdom kingdom,
            TreatyCouncilEvaluation council,
            bool accepts,
            bool usesOverride)
        {
            if (council == null)
                return "unavailable";
            string result = accepts ? "accept" : "reject";
            if (council.IsSoleRulerDecision)
                return $"{result} by sole ruler assessment ({council.Support:+0.0;-0.0;0.0}); quorum=waived";
            if (council.Participation == 0)
            {
                TreatyCouncilMemberEvaluation ruler = council.Members.FirstOrDefault(member => member?.Clan == kingdom?.RulingClan);
                string cause = council.Members.Count == 0 ? "no eligible clans"
                    : ruler == null ? "ruler unavailable"
                    : council.VotingCapacity == 0 ? "no spendable votes"
                    : "council abstained";
                string utility = ruler == null ? "unavailable" : ruler.Utility.ToString("+0.0;-0.0;0.0");
                return $"{result} by ruler assessment ({utility}); votes=0/0; cause={cause}";
            }
            if (usesOverride)
                result += $" by ruler override ({GetRulerOverrideCost(council, accepts)})";
            return $"{result}, yay={council.YayInfluence}, nay={council.NayInfluence}, quorum={council.Participation}/{council.Quorum}";
        }

        private void DraftAiTermsLegacy(WarScoreRecord war, TreatyProposalRecord proposal, Kingdom winner, Kingdom loser, Kingdom drafter)
        {
            int budget = proposal.WarScoreBudget;
            int targetSpend = Math.Max(1, (int)Math.Ceiling(budget * GetDesiredSpendShare(drafter ?? winner)));
            int spent = 0;
            List<TreatyTermRecord> draft = new List<TreatyTermRecord>();

            if (TreatyRealmTransitionService.TryGetForceVassalizationCandidate(winner, loser,
                out TreatyForceVassalizationCandidate forceCandidate, out _)
                && forceCandidate.WarScoreCost <= budget)
            {
                draft.Add(new TreatyTermRecord(TreatyTermType.ForceVassalization, forceCandidate.WarScoreCost,
                    fromKingdomId: loser.StringId,
                    toKingdomId: winner.StringId,
                    titleId: forceCandidate.DefeatedSovereignTitle.TitleId));
                CommitSuggestedDraft(war, proposal, winner, loser, draft);
                return;
            }

            if (TreatyDraftService.TryGetClientKingdomCandidate(winner, loser,
                out TreatyClientKingdomCandidate clientCandidate, out _)
                && clientCandidate.WarScoreCost <= budget)
            {
                draft.Add(new TreatyTermRecord(
                    TreatyTermType.MakeClientKingdom,
                    clientCandidate.WarScoreCost,
                    fromKingdomId: loser.StringId,
                    toKingdomId: winner.StringId));
                CommitSuggestedDraft(war, proposal, winner, loser, draft);
                return;
            }

            foreach (TreatyVassalReleaseCandidate release in TreatyRealmTransitionService.GetReleaseCandidates(loser))
            {
                if (spent >= targetSpend || spent + release.WarScoreCost > budget)
                    break;
                draft.Add(new TreatyTermRecord(TreatyTermType.ReleaseVassal, release.WarScoreCost,
                    fromKingdomId: loser.StringId,
                    toKingdomId: winner.StringId,
                    clanId: release.LeaderClan.StringId,
                    titleId: release.RootTitle.TitleId));
                spent += release.WarScoreCost;
            }

            if (spent < targetSpend
                && spent + C.TreatyRoyalMarriageCost <= budget
                && !MarriageAllianceHelper.HasMarriageAlliance(winner.RulingClan, loser.RulingClan))
            {
                Hero winnerRuler = winner.RulingClan?.Leader;
                float dynasticNeed = BellumMarriageStrategyHelper.CalculateDynasticNeed(winner.RulingClan);
                bool dynasticSettlement = dynasticNeed >= 40f
                    || winnerRuler?.GetTraitLevel(DefaultTraits.Calculating) > 0
                    || winnerRuler?.GetTraitLevel(DefaultTraits.Honor) > 0;
                TreatyRoyalMarriageCandidate marriage = dynasticSettlement
                    ? TreatyRoyalMarriageService.GetCandidates(loser, winner).FirstOrDefault()
                    : null;
                if (marriage != null)
                {
                    draft.Add(new TreatyTermRecord(TreatyTermType.ArrangeRoyalMarriage, marriage.WarScoreCost,
                        fromKingdomId: loser.StringId,
                        toKingdomId: winner.StringId,
                        heroId: marriage.ConcedingSpouse.StringId,
                        secondaryHeroId: marriage.ReceivingSpouse.StringId));
                    spent += marriage.WarScoreCost;
                }
            }

            foreach (TreatyClaimRenunciationCandidate claim in TreatyDraftService.GetAvailableClaimRenunciations(loser, winner))
            {
                if (spent >= targetSpend)
                    break;
                if (claim.WarScoreCost <= 0 || spent + claim.WarScoreCost > budget)
                    continue;

                draft.Add(new TreatyTermRecord(
                    TreatyTermType.RenounceClaim,
                    claim.WarScoreCost,
                    fromKingdomId: loser.StringId,
                    toKingdomId: winner.StringId,
                    clanId: claim.ClaimantClan.StringId,
                    titleId: claim.Title.TitleId));
                spent += claim.WarScoreCost;
                if (spent >= targetSpend)
                    break;
            }

            TreatyDiplomaticSeveranceCandidate severance = TreatyDraftService.GetAvailableDiplomaticSeverances(loser, winner)
                .OrderByDescending(candidate => ScoreDiplomaticSeverance(candidate, winner))
                .FirstOrDefault(candidate => ScoreDiplomaticSeverance(candidate, winner) >= 15f
                    && spent + candidate.WarScoreCost <= budget);
            if (spent < targetSpend && severance != null)
            {
                draft.Add(new TreatyTermRecord(
                    severance.TermType,
                    severance.WarScoreCost,
                    fromKingdomId: loser.StringId,
                    toKingdomId: winner.StringId,
                    thirdKingdomId: severance.ThirdRealm.StringId));
                spent += severance.WarScoreCost;
            }

            Hero draftingRuler = (drafter ?? winner)?.RulingClan?.Leader;
            Hero losingRuler = loser?.RulingClan?.Leader;
            if (spent < targetSpend && draftingRuler != null && losingRuler != null)
            {
                int relation = draftingRuler.GetRelation(losingRuler);
                bool severeTemperament = draftingRuler.GetTraitLevel(DefaultTraits.Calculating) > 0
                    || draftingRuler.GetTraitLevel(DefaultTraits.Honor) < 0
                    || draftingRuler.GetTraitLevel(DefaultTraits.Mercy) < 0;
                TreatyTermType rebukeType = severeTemperament && relation < 0
                    ? TreatyTermType.HumiliateRuler
                    : TreatyTermType.DiscreditRuler;
                int rebukeCost = rebukeType == TreatyTermType.HumiliateRuler
                    ? C.TreatyHumiliateRulerCost
                    : C.TreatyDiscreditRulerCost;
                bool wantsRebuke = relation < -10 || severeTemperament;
                if (wantsRebuke && spent + rebukeCost <= budget)
                {
                    draft.Add(new TreatyTermRecord(rebukeType, rebukeCost,
                        fromKingdomId: loser.StringId,
                        toKingdomId: winner.StringId));
                    spent += rebukeCost;
                }
            }

            HashSet<string> prisonersIncludedByFiefs = new HashSet<string>();
            List<TreatyFiefTransferCandidate> candidates = TreatyDraftService
                .GetAvailableFiefTransfers(war, loser, winner)
                .Where(candidate => candidate.IsOccupied)
                .OrderBy(candidate => candidate.WarScoreCost)
                .ThenByDescending(candidate => candidate.Settlement.IsTown)
                .ToList();

            foreach (TreatyFiefTransferCandidate candidate in candidates)
            {
                if (spent >= targetSpend)
                    break;
                int cost = candidate.WarScoreCost;
                if (cost <= 0 || spent + cost > budget)
                    continue;

                draft.Add(new TreatyTermRecord(TreatyTermType.TransferFief, cost, candidate.Settlement.StringId,
                    fromKingdomId: loser.StringId, toKingdomId: winner.StringId, wasOccupiedAtDrafting: true));
                spent += cost;
                foreach (TreatyPrisonerReleaseCandidate prisoner in candidate.IncludedPrisoners)
                    prisonersIncludedByFiefs.Add(prisoner.Hero.StringId);
                if (spent >= targetSpend)
                    break;
            }

            foreach (var candidate in ClientWarTerritory.Candidates(war, loser, winner).OrderBy(c => c.Cost))
            {
                if (spent >= targetSpend) break;
                if (candidate.Cost <= 0 || spent + candidate.Cost > budget) continue;
                draft.Add(candidate.Term(loser, winner));
                spent += candidate.Cost;
            }
            foreach (TreatyPrisonerReleaseCandidate prisoner in TreatyDraftService.GetAvailablePrisonerReleases(loser, winner))
            {
                if (spent >= targetSpend)
                    break;
                if (prisonersIncludedByFiefs.Contains(prisoner.Hero.StringId)
                    || prisoner.WarScoreCost <= 0
                    || spent + prisoner.WarScoreCost > budget)
                    continue;

                draft.Add(new TreatyTermRecord(
                    TreatyTermType.ReleasePrisoner,
                    prisoner.WarScoreCost,
                    fromKingdomId: loser.StringId,
                    toKingdomId: winner.StringId,
                    heroId: prisoner.Hero.StringId));
                spent += prisoner.WarScoreCost;
                if (spent >= targetSpend)
                    break;
            }

            int remaining = Math.Max(0, budget - spent);
            if (remaining <= 0)
            {
                CommitSuggestedDraft(war, proposal, winner, loser, draft);
                return;
            }

            int liquidWealth = CalculateCollectiveAvailableGold(loser);
            bool prefersTribute = winner.RulingClan?.Leader?.GetTraitLevel(DefaultTraits.Calculating) > 0;
            if (prefersTribute)
            {
                int tributeRate = BellumCivileOptions.TreatyDailyTributePerWarScore;
                int tributeScore = tributeRate <= 0
                    ? 0
                    : Math.Min(remaining, liquidWealth / (tributeRate * C.TreatyTributeDurationDays));
                if (tributeScore > 0)
                {
                    draft.Add(new TreatyTermRecord(
                        TreatyTermType.Tribute,
                        tributeScore,
                        dailyGold: TreatyTermCostModel.GetDailyTributeForWarScore(tributeScore),
                        durationDays: C.TreatyTributeDurationDays,
                        fromKingdomId: loser.StringId,
                        toKingdomId: winner.StringId));
                    spent += tributeScore;
                    remaining -= tributeScore;
                    liquidWealth = Math.Max(0, liquidWealth - TreatyTermCostModel.GetDailyTributeForWarScore(tributeScore) * C.TreatyTributeDurationDays);
                }
            }

            int reparationsRate = BellumCivileOptions.TreatyReparationsGoldPerWarScore;
            int reparationsScore = reparationsRate <= 0
                ? 0
                : Math.Min(remaining, liquidWealth / reparationsRate);
            if (reparationsScore > 0)
            {
                draft.Add(new TreatyTermRecord(
                    TreatyTermType.Reparations,
                    reparationsScore,
                    goldAmount: TreatyTermCostModel.GetReparationsForWarScore(reparationsScore),
                    fromKingdomId: loser.StringId,
                    toKingdomId: winner.StringId));
            }

            CommitSuggestedDraft(war, proposal, winner, loser, draft);
        }

        private static void CommitSuggestedDraft(
            WarScoreRecord war,
            TreatyProposalRecord proposal,
            Kingdom winner,
            Kingdom loser,
            List<TreatyTermRecord> draft)
        {
            if (TreatyDraftService.TryValidateAndNormalize(
                war,
                proposal,
                winner,
                loser,
                draft,
                out List<TreatyTermRecord> normalized,
                out _))
            {
                proposal.ReplaceTerms(normalized);
                return;
            }

            proposal.ReplaceTerms(new[] { new TreatyTermRecord(TreatyTermType.WhitePeace, 0) });
        }

        private static float GetDesiredSpendShare(Kingdom winner)
        {
            Hero ruler = winner?.RulingClan?.Leader;
            if (ruler?.GetTraitLevel(DefaultTraits.Generosity) > 0)
                return 0.65f;
            if (ruler?.GetTraitLevel(DefaultTraits.Calculating) > 0 || ruler?.GetTraitLevel(DefaultTraits.Honor) < 0)
                return 0.85f;
            return 0.75f;
        }

        private void EvaluateRatification(
            WarScoreRecord war,
            TreatyProposalRecord proposal,
            Kingdom winner,
            Kingdom loser,
            out TreatyCouncilEvaluation winnerCouncil,
            out TreatyCouncilEvaluation loserCouncil,
            out bool winnerAccepts,
            out bool loserAccepts,
            out bool winnerUsesOverride,
            out bool loserUsesOverride)
        {
            winnerCouncil = EvaluateCouncil(war, proposal, winner, winnerSide: true);
            loserCouncil = EvaluateCouncil(war, proposal, loser, winnerSide: false);
            proposal.SetRatification(winnerCouncil.Support, loserCouncil.Support, winnerCouncil.OverrideCost, loserCouncil.OverrideCost);

            winnerAccepts = WouldCouncilAccept(winner, winnerCouncil, forcedAcceptance: proposal.IsForced, playerRulerAuthorizesOverride: false, out winnerUsesOverride);
            loserAccepts = WouldCouncilAccept(loser, loserCouncil, proposal.IsForced, playerRulerAuthorizesOverride: false, out loserUsesOverride);
        }

        private static void SpendCouncilCommitments(TreatyProposalRecord proposal, params TreatyCouncilEvaluation[] councils)
        {
            HashSet<string> spentClans = new HashSet<string>();
            foreach (TreatyCouncilMemberEvaluation member in (councils ?? new TreatyCouncilEvaluation[0])
                .Where(council => council != null)
                .SelectMany(council => council.Members))
            {
                Clan clan = member?.Clan;
                if (clan == null
                    || string.IsNullOrWhiteSpace(clan.StringId)
                    || !spentClans.Add(clan.StringId)
                    || member.Stance == TreatyCouncilVoteStance.Abstain
                    || member.InfluenceCommitment <= 0)
                {
                    continue;
                }

                float amount = NpcInfluenceBudgetService.SpendUpToReserve(
                    clan,
                    member.InfluenceCommitment,
                    NpcInfluenceExpenseKind.CouncilCommitment,
                    "treaty_council_commitment",
                    C.TreatyCouncilMinimumVoteStep,
                    C.TreatyCouncilMildCommitment,
                    C.TreatyCouncilStrongCommitment);

                if (amount > 0f && clan == Clan.PlayerClan && proposal != null)
                    proposal.MarkPlayerInfluenceSpent();
            }
        }

        private static void ApplyFinalCouncilConsequences(TreatyProposalRecord proposal,
            Clan firstRuler, TreatyCouncilEvaluation firstCouncil, bool firstAccepts, bool firstOverrides,
            Clan secondRuler = null, TreatyCouncilEvaluation secondCouncil = null, bool secondAccepts = false, bool secondOverrides = false)
        {
            if (proposal == null || !proposal.TryMarkCouncilConsequences()) return;
            bool concluded = proposal.State == TreatyProposalState.Applied;
            ApplyCouncilRelationConsequences(firstRuler, firstCouncil, firstAccepts, firstOverrides, proposal.IsForced, concluded, proposal);
            ApplyCouncilRelationConsequences(secondRuler, secondCouncil, secondAccepts, secondOverrides, proposal.IsForced, concluded, proposal);
        }

        private static void ApplyCouncilRelationConsequences(
            Clan rulerClan,
            TreatyCouncilEvaluation council,
            bool rulerAccepts,
            bool rulerActivelyOverrides,
            bool forcedDecision,
            bool treatyConcluded, TreatyProposalRecord proposal)
        {
            Hero ruler = rulerClan?.Leader;
            if (forcedDecision || ruler == null || council?.Members == null)
                return;

            List<TreatyCouncilMemberEvaluation> voters = council.Members
                .Where(member => member?.Clan?.Leader != null
                    && member.Clan != rulerClan
                    && member.Stance != TreatyCouncilVoteStance.Abstain
                    && member.InfluenceCommitment > 0)
                .ToList();

            TreatyCouncilVoteStance rulerStance = rulerAccepts ? TreatyCouncilVoteStance.Yay : TreatyCouncilVoteStance.Nay;

            foreach (TreatyCouncilMemberEvaluation voter in voters)
            {
                bool aligned = voter.Stance == rulerStance;
                int change = aligned && treatyConcluded
                    ? GetRulerAgreementRelation(voter.InfluenceCommitment)
                    : !aligned && rulerActivelyOverrides && (treatyConcluded || !rulerAccepts)
                        ? -GetRulerOverrideRelation(voter.InfluenceCommitment)
                        : 0;
                if (change < 0 && !treatyConcluded)
                {
                    var war = Campaign.Current?.GetCampaignBehavior<WarScoreBehavior>()?.GetActiveWar(proposal.WarKey);
                    if (war != null && !war.TryRecordRefusalReaction(ruler.StringId, voter.Clan.Leader.StringId, proposal.Terms)) continue;
                }
                ApplyTreatyRelationChange(voter.Clan.Leader, ruler, change,
                    aligned ? RelationMemorySources.AgreedOnTreatyPosition : RelationMemorySources.OverruledTreatyPosition,
                    aligned ? 5f : 7f,
                    aligned ? "ruler confirmed vassal treaty position" : "ruler overrode vassal treaty position");
            }

            if (!treatyConcluded) return;

            for (int firstIndex = 0; firstIndex < voters.Count; firstIndex++)
            {
                for (int secondIndex = firstIndex + 1; secondIndex < voters.Count; secondIndex++)
                {
                    TreatyCouncilMemberEvaluation first = voters[firstIndex];
                    TreatyCouncilMemberEvaluation second = voters[secondIndex];
                    bool aligned = first.Stance == second.Stance;
                    int firstMagnitude = aligned
                        ? GetRulerAgreementRelation(first.InfluenceCommitment)
                        : GetRulerOverrideRelation(first.InfluenceCommitment);
                    int secondMagnitude = aligned
                        ? GetRulerAgreementRelation(second.InfluenceCommitment)
                        : GetRulerOverrideRelation(second.InfluenceCommitment);
                    int magnitude = Math.Max(1, (int)Math.Round((firstMagnitude + secondMagnitude) / 4f, MidpointRounding.AwayFromZero));
                    ApplyTreatyRelationChange(first.Clan.Leader, second.Clan.Leader, aligned ? magnitude : -magnitude,
                        aligned ? RelationMemorySources.SharedTreatyPosition : RelationMemorySources.OpposedTreatyPosition,
                        5f,
                        aligned ? "vassals backed the same treaty position" : "vassals opposed each other over treaty terms");
                }
            }
        }

        private static int GetRulerAgreementRelation(int commitment)
        {
            if (commitment >= C.TreatyCouncilStrongCommitment)
                return C.TreatyRulerAgreementRelationStrong;
            if (commitment >= C.TreatyCouncilMildCommitment)
                return C.TreatyRulerAgreementRelationMild;
            return C.TreatyRulerAgreementRelationMinimum;
        }

        private static int GetRulerOverrideRelation(int commitment)
        {
            if (commitment >= C.TreatyCouncilStrongCommitment)
                return C.TreatyRulerOverrideRelationStrong;
            if (commitment >= C.TreatyCouncilMildCommitment)
                return C.TreatyRulerOverrideRelationMild;
            return C.TreatyRulerOverrideRelationMinimum;
        }

        private static void ApplyTreatyRelationChange(
            Hero first,
            Hero second,
            int change,
            string sourceId,
            float durationYears,
            string reason)
        {
            if (first == null || second == null || first == second || change == 0)
                return;

            bool notifyPlayer = first == Hero.MainHero
                || second == Hero.MainHero
                || first.Clan == Clan.PlayerClan
                || second.Clan == Clan.PlayerClan;
            RelationMemoryService.ApplyChange(first, second, change, notifyPlayer,
                sourceId, durationYears, RelationMemoryScope.Personal);
            BellumCivileLogger.Log($"Treaty relation consequence; first={first.StringId}; second={second.StringId}; change={change:+0;-0;0}; reason={reason}.");
        }

        private static bool ShouldRulerOverride(Kingdom kingdom, TreatyCouncilEvaluation council, bool playerRulerAuthorizesOverride)
        {
            if (council == null)
                return false;
            Clan ruler = kingdom?.RulingClan;
            if (ruler == null)
                return false;

            TreatyCouncilMemberEvaluation rulerVote = council.Members.FirstOrDefault(member => member?.Clan == ruler);
            bool rulerSupports = ruler == Clan.PlayerClan
                ? playerRulerAuthorizesOverride
                : rulerVote?.Stance == TreatyCouncilVoteStance.Yay;
            int reservedCommitment = rulerVote?.InfluenceCommitment ?? 0;
            return rulerSupports && NpcInfluenceBudgetService.CanAfford(
                ruler,
                council.OverrideCost + reservedCommitment,
                NpcInfluenceExpenseKind.CouncilCommitment);
        }

        private static bool ShouldRulerRejectRatifiedTreaty(Kingdom kingdom, TreatyCouncilEvaluation council)
        {
            Clan ruler = kingdom?.RulingClan;
            if (ruler == null || ruler == Clan.PlayerClan || council == null || !council.IsRatified)
                return false;

            TreatyCouncilMemberEvaluation rulerVote = council.Members
                .FirstOrDefault(member => member?.Clan == ruler);
            if (rulerVote == null
                || rulerVote.Stance != TreatyCouncilVoteStance.Nay
                || rulerVote.Utility > C.TreatyRulerStrongRejectionUtility)
            {
                return false;
            }

            int reservedCommitment = Math.Max(0, rulerVote.InfluenceCommitment);
            return NpcInfluenceBudgetService.CanAfford(
                ruler,
                council.RejectionOverrideCost + reservedCommitment,
                NpcInfluenceExpenseKind.CouncilCommitment);
        }

        private static int GetRulerOverrideCost(TreatyCouncilEvaluation council, bool accepts)
        {
            if (council == null)
                return 0;
            return accepts ? council.OverrideCost : council.RejectionOverrideCost;
        }

        private static void SpendRulerOverride(Kingdom kingdom, int cost)
        {
            Clan ruler = kingdom?.RulingClan;
            if (ruler != null && cost > 0)
            {
                NpcInfluenceBudgetService.TrySpend(
                    ruler,
                    cost,
                    NpcInfluenceExpenseKind.CouncilCommitment,
                    "treaty_council_override");
            }
        }

        private bool ApplyTreaty(WarScoreRecord war, TreatyProposalRecord proposal, Kingdom winner, Kingdom loser)
        {
            if (!HasSettlementBudget(proposal, out _))
                return false;

            if (TryCancelStorylineProtectedProposal(war, proposal, out _))
                return false;

            if (!TryValidateCouncilControlledWhitePeace(war, proposal, out _))
                return false;

            if (!proposal.Terms.Any(t => t?.Type == TreatyTermType.HostagePeace))
                return ApplyTreatyCore(war, proposal, winner, loser);
            var custody = Campaign.Current?.GetCampaignBehavior<HostagePactBehavior>();
            try
            {
                var result = custody?.DeliverTreaty(proposal, winner, loser,
                    () => ApplyTreatyCore(war, proposal, winner, loser)) ?? HostageDeliveryResult.Rejected;
                if (result == HostageDeliveryResult.Applied) return true;
                if (result == HostageDeliveryResult.Rejected)
                    CancelInvalidProposal(war, proposal, "the promised hostage could not be delivered");
                else custody.RecoverTreatyDelivery(proposal.ProposalId);
            }
            catch (Exception ex)
            {
                BellumCivileLogger.Log($"Hostage treaty settlement interrupted; proposal={proposal.ProposalId}; {ex}");
                custody?.RecoverTreatyDelivery(proposal.ProposalId);
            }
            return false;
        }

        private bool ApplyTreatyCore(WarScoreRecord war, TreatyProposalRecord proposal, Kingdom winner, Kingdom loser)
        {
            if (!HasSettlementBudget(proposal, out _))
                return false;

            bool whitePeace = proposal.Terms.Any(term => term?.Type == TreatyTermType.WhitePeace);
            CourtAgendaBehavior.Current?.BeginRallySettlement(war);
            var rallyGold = new Dictionary<TreatyTermRecord,int>();
            var rallyDelivered = new HashSet<TreatyTermRecord>();
            int signedDailyTribute = 0;
            int tributeDurationDays = 0;
            TreatyPrisonerReleaseTally prisonerReleases = new TreatyPrisonerReleaseTally();
            if (!whitePeace)
            {
                // A royal marriage is validated while the realms are still at war. Execute it before
                // fief transfers, prisoner retention, and MakePeaceAction can alter spouse eligibility.
                ApplyRoyalMarriageTerms(proposal);
            }

            // Occupation is temporary unless the treaty explicitly cedes the fief. White peace has
            // no transfer clauses, so it must still restore every surviving wartime occupation.
            ApplyTerritorialTerms(war, proposal, winner, loser, prisonerReleases);
            if (!whitePeace)
            {
                foreach (TreatyTermRecord term in proposal.Terms)
                {
                    Kingdom payer = ResolveKingdom(term?.FromKingdomId);
                    Kingdom recipient = ResolveKingdom(term?.ToKingdomId);
                    if (term?.Type == TreatyTermType.Reparations && term.GoldAmount > 0)
                        rallyGold[term] = TransferCollectiveGold(payer, recipient, term.GoldAmount);
                    else if (term?.Type == TreatyTermType.Tribute && term.DailyGold > 0 && term.DurationDays > 0)
                    {
                        // Feed treaty tribute into Bannerlord's StanceLink through MakePeaceAction.
                        // This gives the payment its native Diplomacy icon, amount, and remaining
                        // installment count instead of maintaining a parallel invisible transfer.
                        if (payer == winner && recipient == loser)
                            signedDailyTribute += term.DailyGold;
                        else if (payer == loser && recipient == winner)
                            signedDailyTribute -= term.DailyGold;
                        tributeDurationDays = Math.Max(tributeDurationDays, term.DurationDays);
                    }
                    else if (term?.Type == TreatyTermType.ReleasePrisoner)
                        ApplyPrisonerReleaseTerm(term, prisonerReleases);
                    else if (term?.Type == TreatyTermType.RenounceClaim)
                        ApplyClaimRenunciationTerm(term);
                    else if (term?.Type == TreatyTermType.DiscreditRuler || term?.Type == TreatyTermType.HumiliateRuler)
                        ApplyRulerRebukeTerm(term);
                    else if (term?.Type == TreatyTermType.EndTradeAgreement || term?.Type == TreatyTermType.EndAlliance)
                        ApplyDiplomaticSeveranceTerm(term);
                }
            }

            proposal.SetState(TreatyProposalState.Accepted, whitePeace ? "mutual exhaustion" : "terms ratified");
            _councilSnapshots.Remove(proposal.ProposalId);
            war.EndParley();
            Campaign.Current?.GetCampaignBehavior<RetainedTreatyPrisonerBehavior>()?.RetainForeignPrisoners(winner, loser);
            BellumPeaceResolutionContext.RunBellumPeaceResolution(() =>
            {
                if (signedDailyTribute != 0 && tributeDurationDays > 0)
                    MakePeaceAction.ApplyByKingdomDecision(winner, loser, signedDailyTribute, tributeDurationDays);
                else
                    MakePeaceAction.Apply(winner, loser);
            });
            ShowTreatyAcceptedMessage(proposal, winner, loser, whitePeace, prisonerReleases);
            ApplyStructuralPoliticalTerms(proposal,rallyDelivered);
            proposal.SetState(TreatyProposalState.Applied, whitePeace ? "white peace signed" : "terms applied", (float)CampaignTime.Now.ToDays);
            ApplyConcedeDefeatTerm(proposal);
            QueueRebelDemandResolutions(proposal);
            try { CourtRallySettlement.Record(war,proposal,rallyGold,rallyDelivered); }
            catch (Exception ex) { BellumCivileLogger.Log("Court rally settlement receipt interrupted: " + ex); }
            BellumCivileLogger.Log($"Foreign treaty applied; war={war.WarKey}; winner={winner.StringId}; loser={loser.StringId}; used_ws={proposal.UsedWarScore}/{proposal.WarScoreBudget}; white_peace={whitePeace}.");
            return true;
        }

        internal static bool HasSettlementBudget(TreatyProposalRecord proposal, out string report)
        {
            report = "the draft exceeds the available war score";
            return proposal != null && !proposal.IsOverBudget;
        }

        private static void ApplyRoyalMarriageTerms(TreatyProposalRecord proposal)
        {
            foreach (TreatyTermRecord term in (proposal?.Terms ?? Enumerable.Empty<TreatyTermRecord>())
                .Where(candidate => candidate?.Type == TreatyTermType.ArrangeRoyalMarriage))
            {
                Kingdom concedingRealm = ResolveKingdom(term.FromKingdomId);
                Kingdom receivingRealm = ResolveKingdom(term.ToKingdomId);
                Hero concedingSpouse = Hero.AllAliveHeroes.FirstOrDefault(hero => hero?.StringId == term.HeroId);
                Hero receivingSpouse = Hero.AllAliveHeroes.FirstOrDefault(hero => hero?.StringId == term.SecondaryHeroId);
                string marriageReason = "candidate invalid";
                if (TreatyRoyalMarriageService.TryResolveCandidate(concedingRealm, receivingRealm,
                    term.HeroId, term.SecondaryHeroId, out TreatyRoyalMarriageCandidate candidate)
                    && TreatyRoyalMarriageService.TryApply(candidate, out marriageReason))
                {
                    ApplyTreatyRelationChange(concedingRealm?.RulingClan?.Leader, receivingRealm?.RulingClan?.Leader, 15,
                        RelationMemorySources.TreatyRoyalMarriage);
                    TextObject message = new TextObject("{=BC_Treaty_RoyalMarriageConcluded}To seal the peace between {CONCEDING_REALM} and {RECEIVING_REALM}, {FIRST_NAME} has married {SECOND_NAME} and joined the {RECEIVING_CLAN}.")
                        .SetTextVariable("CONCEDING_REALM", concedingRealm?.Name ?? TextObject.GetEmpty())
                        .SetTextVariable("RECEIVING_REALM", receivingRealm?.Name ?? TextObject.GetEmpty())
                        .SetTextVariable("FIRST_NAME", concedingSpouse?.Name ?? TextObject.GetEmpty())
                        .SetTextVariable("SECOND_NAME", receivingSpouse?.Name ?? TextObject.GetEmpty())
                        .SetTextVariable("RECEIVING_CLAN", receivingRealm?.RulingClan?.Name ?? TextObject.GetEmpty());
                    BellumCivileNotifications.Show(message, BellumNotificationColors.Success,
                        primaryKingdom: receivingRealm, secondaryKingdom: concedingRealm,
                        primaryClan: receivingRealm?.RulingClan, secondaryClan: concedingRealm?.RulingClan, isMajorEvent: true);
                }
                else
                {
                    BellumCivileLogger.Log($"Treaty royal marriage failed during final application; first={term.HeroId}; second={term.SecondaryHeroId}; reason={marriageReason}.");
                }
            }
        }

        private static void ApplyConcedeDefeatTerm(TreatyProposalRecord proposal)
        {
            TreatyTermRecord term = proposal?.Terms?.FirstOrDefault(candidate => candidate?.Type == TreatyTermType.ConcedeDefeat);
            Kingdom defeatedRealm = ResolveKingdom(term?.FromKingdomId);
            Kingdom victoriousRealm = ResolveKingdom(term?.ToKingdomId);
            Hero defeatedRuler = defeatedRealm?.RulingClan?.Leader;
            Hero victoriousRuler = victoriousRealm?.RulingClan?.Leader;
            if (term == null || defeatedRuler == null || victoriousRuler == null)
                return;

            ApplyPrestigeTransfer(
                defeatedRealm.RulingClan,
                defeatedRuler,
                victoriousRealm.RulingClan,
                victoriousRuler,
                C.TreatyConcedeDefeatRenownTransfer,
                C.TreatyConcedeDefeatInfluenceTransfer);
        }

        private void QueueRebelDemandResolutions(TreatyProposalRecord proposal)
        {
            foreach (TreatyTermRecord term in proposal?.Terms?.Where(candidate => candidate?.Type == TreatyTermType.EnforceRebelDemands)
                ?? Enumerable.Empty<TreatyTermRecord>())
            {
                if (_pendingRebelResolutions.Any(record => record?.ProposalId == proposal.ProposalId
                    && record.RebelKingdomId == term.ThirdKingdomId))
                    continue;
                _pendingRebelResolutions.Add(new PendingTreatyRebelResolutionRecord(
                    proposal.ProposalId,
                    term.ThirdKingdomId,
                    term.FromKingdomId,
                    term.ToKingdomId));
            }
        }

        private void OnHourlyTick()
        {
            EnsureCollectionsInitialized();
            Campaign.Current?.GetCampaignBehavior<HostagePactBehavior>()?.RecoverTreatyDeliveries();
            if (_pendingRebelResolutions.Count == 0)
                return;

            foreach (PendingTreatyRebelResolutionRecord pending in _pendingRebelResolutions.ToList())
            {
                _pendingRebelResolutions.Remove(pending);
                try
                {
                    TryResolveTreatyBackedRebellion(pending);
                }
                catch (Exception ex)
                {
                    BellumCivileLogger.Log($"Treaty-backed rebel resolution failed safely; proposal={pending?.ProposalId}; rebel={pending?.RebelKingdomId}; error={ex}");
                }
            }
        }

        private static void TryResolveTreatyBackedRebellion(PendingTreatyRebelResolutionRecord pending)
        {
            FactionManagerBehavior manager = Campaign.Current?.GetCampaignBehavior<FactionManagerBehavior>();
            CivilWarResolutionBehavior resolution = Campaign.Current?.GetCampaignBehavior<CivilWarResolutionBehavior>();
            FactionObject faction = manager?.GetFactionByTrackedRebelKingdomId(pending?.RebelKingdomId);
            Kingdom rebelRealm = ResolveKingdom(pending?.RebelKingdomId);
            Kingdom parentRealm = ResolveKingdom(pending?.ParentKingdomId);
            Kingdom beneficiaryRealm = ResolveKingdom(pending?.BeneficiaryKingdomId);
            if (faction == null || rebelRealm == null || parentRealm == null || resolution == null
                || faction.ParentKingdom != parentRealm || !parentRealm.IsAtWarWith(rebelRealm))
            {
                BellumCivileLogger.Log($"Treaty-backed rebellion no longer valid; proposal={pending?.ProposalId}; rebel={pending?.RebelKingdomId}.");
                return;
            }

            Hero beneficiaryRuler = beneficiaryRealm?.RulingClan?.Leader;
            ApplyNamedTreatyRelationChange(faction.Leader?.Leader, beneficiaryRuler, C.TreatyEnforceRebelRulerRelationGain,
                RelationMemorySources.LiberatedMyRealm, 20f, RelationMemoryScope.House, faction.Name?.ToString());
            foreach (Clan member in faction.Members.Where(clan => clan != null && clan != faction.Leader))
                ApplyNamedTreatyRelationChange(member.Leader, beneficiaryRuler, C.TreatyEnforceRebelLordRelationGain,
                    RelationMemorySources.LiberatedMyRealm, 20f, RelationMemoryScope.House, faction.Name?.ToString());

            if (beneficiaryRealm?.IsAtWarWith(rebelRealm) == true)
            {
                BellumPeaceResolutionContext.RunBellumPeaceResolution(() =>
                    MakePeaceAction.Apply(beneficiaryRealm, rebelRealm));
            }

            resolution.ResolveRebelVictory(faction, rebelRealm);
            BellumCivileLogger.Log($"Treaty-backed rebel demands enforced; proposal={pending.ProposalId}; rebel={rebelRealm.StringId}; parent={parentRealm.StringId}.");
        }

        private static void ShowTreatyRejectedMessage(
            TreatyProposalRecord proposal,
            Kingdom firstRejectingRealm,
            Kingdom secondRejectingRealm)
        {
            Kingdom winner = ResolveKingdom(proposal?.WinnerKingdomId);
            Kingdom loser = ResolveKingdom(proposal?.LoserKingdomId);
            if (winner == null || loser == null)
                return;

            List<Kingdom> rejectingRealms = new[] { firstRejectingRealm, secondRejectingRealm }
                .Where(realm => realm != null)
                .Distinct()
                .ToList();
            Kingdom drafter = ResolveKingdom(proposal.DrafterKingdomId);
            TextObject message;
            if (rejectingRealms.Count == 1)
            {
                Kingdom rejectingRealm = rejectingRealms[0];
                if (drafter != null && drafter != rejectingRealm)
                {
                    message = new TextObject("{=BC_Treaty_ProposalRejectedByRealm}The court of {REJECTING_REALM} has rejected {PROPOSING_REALM}'s proposed peace. The envoys have departed, and the war continues.")
                        .SetTextVariable("REJECTING_REALM", rejectingRealm.Name)
                        .SetTextVariable("PROPOSING_REALM", drafter.Name);
                }
                else
                {
                    message = new TextObject("{=BC_Treaty_ProposalRejectedByOwnCourt}The court of {REJECTING_REALM} has rejected the proposed peace. The envoys have departed, and the war continues.")
                        .SetTextVariable("REJECTING_REALM", rejectingRealm.Name);
                }
            }
            else
            {
                message = new TextObject("{=BC_Treaty_ProposalRejectedMutual}The courts of {FIRST_REALM} and {SECOND_REALM} have failed to agree on terms of peace. The envoys have departed, and the war continues.")
                    .SetTextVariable("FIRST_REALM", winner.Name)
                    .SetTextVariable("SECOND_REALM", loser.Name);
            }

            InformationManager.DisplayMessage(new InformationMessage(message.ToString(), BellumNotificationColors.Warning));
        }

        private static void ShowTreatyAcceptedMessage(
            TreatyProposalRecord proposal,
            Kingdom winner,
            Kingdom loser,
            bool whitePeace,
            TreatyPrisonerReleaseTally prisonerReleases)
        {
            if (proposal == null || winner == null || loser == null)
                return;

            TextObject message;
            if (whitePeace)
            {
                message = new TextObject("{=BC_Treaty_WhitePeaceConcluded}{FIRST_REALM} and {SECOND_REALM} have agreed to a white peace. Hostilities have ended without either realm imposing terms upon the other.")
                    .SetTextVariable("FIRST_REALM", winner.Name)
                    .SetTextVariable("SECOND_REALM", loser.Name);
            }
            else
            {
                TextObject termsSummary = BuildTreatyTermsSummary(proposal, prisonerReleases);
                if (proposal.IsForced)
                {
                    message = new TextObject("{=BC_Treaty_ForcedSettlementConcluded}Broken by war, {LOSER_REALM} has accepted the terms imposed by {WINNER_REALM}. {TERMS_SUMMARY}")
                        .SetTextVariable("LOSER_REALM", loser.Name)
                        .SetTextVariable("WINNER_REALM", winner.Name)
                        .SetTextVariable("TERMS_SUMMARY", termsSummary);
                }
                else
                {
                    // Vanilla already announces that the realms made peace. Bellum only adds
                    // the negotiated clauses here so the campaign feed does not repeat itself.
                    message = termsSummary;
                }
            }

            InformationManager.DisplayMessage(new InformationMessage(message.ToString(), BellumNotificationColors.Success));
        }

        private static TextObject BuildTreatyTermsSummary(
            TreatyProposalRecord proposal,
            TreatyPrisonerReleaseTally prisonerReleases)
        {
            List<TreatyTermRecord> terms = proposal?.Terms?.Where(term => term != null).ToList()
                ?? new List<TreatyTermRecord>();
            List<string> clauses = new List<string>();
            HashSet<string> summarizedFiefTransfers = new HashSet<string>();

            foreach (TreatyTermRecord term in terms.Where(term =>
                term.Type != TreatyTermType.WhitePeace && term.Type != TreatyTermType.ReleasePrisoner))
            {
                if (term.Type == TreatyTermType.TransferFief)
                {
                    string transferKey = (term.FromKingdomId ?? string.Empty) + "|" + (term.ToKingdomId ?? string.Empty);
                    if (!summarizedFiefTransfers.Add(transferKey))
                        continue;

                    string transferClause = BuildTreatyFiefTransferSummaryClause(terms.Where(candidate =>
                        candidate.Type == TreatyTermType.TransferFief
                        && candidate.FromKingdomId == term.FromKingdomId
                        && candidate.ToKingdomId == term.ToKingdomId));
                    if (!string.IsNullOrWhiteSpace(transferClause))
                        clauses.Add(transferClause);
                    continue;
                }

                string clause = BuildTreatyTermSummaryClause(term);
                if (!string.IsNullOrWhiteSpace(clause))
                    clauses.Add(clause);
            }

            foreach (TreatyPrisonerReleaseCount release in (prisonerReleases?.Entries
                ?? Array.Empty<TreatyPrisonerReleaseCount>())
                .Where(entry => entry?.Count > 0)
                .OrderBy(entry => entry.ReleasingRealm.StringId)
                .ThenBy(entry => entry.ReceivingRealm.StringId))
            {
                TextObject clause = release.Count == 1
                    ? new TextObject("{=BC_Treaty_SummaryReleaseOnePrisoner}{FROM_REALM} releases one noble prisoner to {TO_REALM}")
                    : new TextObject("{=BC_Treaty_SummaryReleasePrisoners}{FROM_REALM} releases {COUNT} noble prisoners to {TO_REALM}");
                clauses.Add(clause
                    .SetTextVariable("FROM_REALM", release.ReleasingRealm.Name)
                    .SetTextVariable("TO_REALM", release.ReceivingRealm.Name)
                    .SetTextVariable("COUNT", release.Count)
                    .ToString());
            }

            if (clauses.Count == 0)
                return new TextObject("{=BC_Treaty_SummaryGeneral}The negotiated terms are now in force.");

            return new TextObject("{=BC_Treaty_SummaryPrefix}Agreed terms: {TERMS}.")
                .SetTextVariable("TERMS", string.Join("; ", clauses));
        }

        private static string BuildTreatyFiefTransferSummaryClause(IEnumerable<TreatyTermRecord> transferTerms)
        {
            List<TreatyTermRecord> transfers = transferTerms?
                .Where(term => term != null && !string.IsNullOrWhiteSpace(term.SettlementId))
                .ToList() ?? new List<TreatyTermRecord>();
            if (transfers.Count == 0)
                return string.Empty;

            TreatyTermRecord first = transfers[0];
            List<TextObject> fiefNames = transfers
                .Select(term => ResolveSettlement(term.SettlementId)?.Name)
                .Where(name => name != null)
                .ToList();
            if (fiefNames.Count == 0)
                return string.Empty;

            TextObject fromRealm = ResolveKingdom(first.FromKingdomId)?.Name ?? TextObject.GetEmpty();
            TextObject toRealm = ResolveKingdom(first.ToKingdomId)?.Name ?? TextObject.GetEmpty();
            TextObject fiefList = BuildTreatySummaryList(fiefNames);
            TextObject template = fiefNames.Count == 1
                ? new TextObject("{=BC_Treaty_SummaryTransferFief}{FROM_REALM} surrenders {FIEF_NAME} to {TO_REALM}")
                : new TextObject("{=BC_Treaty_SummaryTransferFiefs}{FROM_REALM} surrenders {FIEF_LIST} to {TO_REALM}");
            return template
                .SetTextVariable("FROM_REALM", fromRealm)
                .SetTextVariable("FIEF_NAME", fiefList)
                .SetTextVariable("FIEF_LIST", fiefList)
                .SetTextVariable("TO_REALM", toRealm)
                .ToString();
        }

        private static TextObject BuildTreatySummaryList(IReadOnlyList<TextObject> items)
        {
            if (items == null || items.Count == 0)
                return TextObject.GetEmpty();
            if (items.Count == 1)
                return items[0];
            if (items.Count == 2)
            {
                return new TextObject("{=BC_Treaty_SummaryListPair}{FIRST} and {SECOND}")
                    .SetTextVariable("FIRST", items[0])
                    .SetTextVariable("SECOND", items[1]);
            }

            return new TextObject("{=BC_Treaty_SummaryListAppend}{ITEM}, {REST}")
                .SetTextVariable("ITEM", items[0])
                .SetTextVariable("REST", BuildTreatySummaryList(items.Skip(1).ToList()));
        }

        private static string BuildTreatyTermSummaryClause(TreatyTermRecord term)
        {
            if (term == null)
                return string.Empty;

            TextObject fromRealm = ResolveKingdom(term.FromKingdomId)?.Name ?? TextObject.GetEmpty();
            TextObject toRealm = ResolveKingdom(term.ToKingdomId)?.Name ?? TextObject.GetEmpty();
            switch (term.Type)
            {
                case TreatyTermType.HostagePeace:
                    return new TextObject("{=BC_Treaty_SummaryHostagePeace}{FROM_REALM} entrusts {HERO} to {TO_REALM} as a hostage to secure {DAYS} days of peace")
                        .SetTextVariable("DAYS", term.DurationDays)
                        .SetTextVariable("FROM_REALM", fromRealm).SetTextVariable("TO_REALM", toRealm)
                        .SetTextVariable("HERO", Hero.AllAliveHeroes.FirstOrDefault(h => h?.StringId == term.HeroId)?.Name ?? TextObject.GetEmpty()).ToString();
                case TreatyTermType.TransferFief:
                    Settlement settlement = ResolveSettlement(term.SettlementId);
                    return new TextObject("{=BC_Treaty_SummaryTransferFief}{FROM_REALM} surrenders {FIEF_NAME} to {TO_REALM}")
                        .SetTextVariable("FROM_REALM", fromRealm)
                        .SetTextVariable("FIEF_NAME", settlement?.Name ?? TextObject.GetEmpty())
                        .SetTextVariable("TO_REALM", toRealm)
                        .ToString();
                case TreatyTermType.RecognizeClientOccupation:
                    return new TextObject("{=BC_Treaty_SummaryClientOccupation}{FROM_REALM} recognizes {CLIENT}'s possession of {FIEF}")
                        .SetTextVariable("FROM_REALM", fromRealm).SetTextVariable("CLIENT", ResolveKingdom(term.ThirdKingdomId)?.Name ?? TextObject.GetEmpty())
                        .SetTextVariable("FIEF", ResolveSettlement(term.SettlementId)?.Name ?? TextObject.GetEmpty()).ToString();
                case TreatyTermType.Reparations:
                    if (term.GoldAmount <= 0)
                        return string.Empty;
                    return new TextObject("{=BC_Treaty_SummaryReparations}{FROM_REALM} pays {TO_REALM} {GOLD} denars in reparations")
                        .SetTextVariable("FROM_REALM", fromRealm)
                        .SetTextVariable("TO_REALM", toRealm)
                        .SetTextVariable("GOLD", term.GoldAmount)
                        .ToString();
                case TreatyTermType.Tribute:
                    if (term.DailyGold <= 0 || term.DurationDays <= 0)
                        return string.Empty;
                    return new TextObject("{=BC_Treaty_SummaryTribute}{FROM_REALM} pays {TO_REALM} {GOLD} denars per day for {DAYS} days")
                        .SetTextVariable("FROM_REALM", fromRealm)
                        .SetTextVariable("TO_REALM", toRealm)
                        .SetTextVariable("GOLD", term.DailyGold)
                        .SetTextVariable("DAYS", term.DurationDays)
                        .ToString();
                case TreatyTermType.ReleasePrisoner:
                    return string.Empty;
                case TreatyTermType.RenounceClaim:
                    Clan claimant = ResolveClan(term.ClanId);
                    FeudalTitleRecord title = Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>()?.GetTitle(term.TitleId);
                    return new TextObject("{=BC_Treaty_SummaryRenounceClaim}{CLAN_NAME} renounces its claim to {TITLE_NAME}")
                        .SetTextVariable("CLAN_NAME", claimant?.Name ?? TextObject.GetEmpty())
                        .SetTextVariable("TITLE_NAME", title == null
                            ? TextObject.GetEmpty()
                            : new TextObject(FeudalTitleDisplayHelper.FormatTitleName(title, claimant)))
                        .ToString();
                case TreatyTermType.DiscreditRuler:
                case TreatyTermType.HumiliateRuler:
                    Hero targetRuler = ResolveKingdom(term.FromKingdomId)?.RulingClan?.Leader;
                    TextObject rebukeTemplate = term.Type == TreatyTermType.HumiliateRuler
                        ? new TextObject("{=BC_Treaty_SummaryHumiliateRuler}{RULER_NAME} of {FROM_REALM} is publicly humiliated by {TO_REALM}")
                        : new TextObject("{=BC_Treaty_SummaryDiscreditRuler}{RULER_NAME} of {FROM_REALM} is publicly discredited by {TO_REALM}");
                    return rebukeTemplate
                        .SetTextVariable("RULER_NAME", targetRuler?.Name ?? TextObject.GetEmpty())
                        .SetTextVariable("FROM_REALM", fromRealm)
                        .SetTextVariable("TO_REALM", toRealm)
                        .ToString();
                case TreatyTermType.ReleaseVassal:
                    Clan releasedClan = ResolveClan(term.ClanId);
                    FeudalTitleRecord releasedTitle = Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>()?.GetTitle(term.TitleId);
                    return new TextObject("{=BC_Treaty_SummaryReleaseVassal}{FROM_REALM} releases {CLAN_NAME} as the independent {TITLE_NAME}")
                        .SetTextVariable("FROM_REALM", fromRealm)
                        .SetTextVariable("CLAN_NAME", releasedClan?.Name ?? TextObject.GetEmpty())
                        .SetTextVariable("TITLE_NAME", releasedTitle == null
                            ? TextObject.GetEmpty()
                            : new TextObject(FeudalTitleDisplayHelper.FormatTitleName(releasedTitle, releasedClan)))
                        .ToString();
                case TreatyTermType.ForceVassalization:
                    return new TextObject("{=BC_Treaty_SummaryForceVassalization}{FROM_REALM} submits as a vassal realm of {TO_REALM}")
                        .SetTextVariable("FROM_REALM", fromRealm)
                        .SetTextVariable("TO_REALM", toRealm)
                        .ToString();
                case TreatyTermType.MakeClientKingdom:
                    TextObject clientTemplate = term.WasVoluntaryOffering
                        ? new TextObject("{=BC_Treaty_SummaryOfferClientKingdom}{FROM_REALM} voluntarily enters the clientage of {TO_REALM}")
                        : new TextObject("{=BC_Treaty_SummaryMakeClientKingdom}{FROM_REALM} submits as a client kingdom of {TO_REALM}");
                    return clientTemplate
                        .SetTextVariable("FROM_REALM", fromRealm)
                        .SetTextVariable("TO_REALM", toRealm)
                        .ToString();
                case TreatyTermType.ArrangeRoyalMarriage:
                    Hero concedingSpouse = Hero.AllAliveHeroes.FirstOrDefault(candidate => candidate?.StringId == term.HeroId);
                    Hero receivingSpouse = Hero.AllAliveHeroes.FirstOrDefault(candidate => candidate?.StringId == term.SecondaryHeroId);
                    return new TextObject("{=BC_Treaty_SummaryRoyalMarriage}{FIRST_NAME} marries {SECOND_NAME} and joins the {RECEIVING_CLAN}")
                        .SetTextVariable("FIRST_NAME", concedingSpouse?.Name ?? TextObject.GetEmpty())
                        .SetTextVariable("SECOND_NAME", receivingSpouse?.Name ?? TextObject.GetEmpty())
                        .SetTextVariable("RECEIVING_CLAN", receivingSpouse?.Clan?.Name ?? TextObject.GetEmpty())
                        .ToString();
                case TreatyTermType.EndTradeAgreement:
                    return new TextObject("{=BC_Treaty_SummaryEndTradeAgreement}{FROM_REALM} ends its trade agreement with {THIRD_REALM}")
                        .SetTextVariable("FROM_REALM", fromRealm)
                        .SetTextVariable("THIRD_REALM", ResolveKingdom(term.ThirdKingdomId)?.Name ?? TextObject.GetEmpty())
                        .ToString();
                case TreatyTermType.EndAlliance:
                    return new TextObject("{=BC_Treaty_SummaryEndAlliance}{FROM_REALM} ends its alliance with {THIRD_REALM}")
                        .SetTextVariable("FROM_REALM", fromRealm)
                        .SetTextVariable("THIRD_REALM", ResolveKingdom(term.ThirdKingdomId)?.Name ?? TextObject.GetEmpty())
                        .ToString();
                case TreatyTermType.ConcedeDefeat:
                    return new TextObject("{=BC_Treaty_SummaryConcedeDefeat}{FROM_REALM} publicly concedes defeat to {TO_REALM}")
                        .SetTextVariable("FROM_REALM", fromRealm)
                        .SetTextVariable("TO_REALM", toRealm)
                        .ToString();
                case TreatyTermType.ReleaseClientState:
                    return new TextObject("{=BC_Treaty_SummaryReleaseClientState}{FROM_REALM} releases {THIRD_REALM} from clientage")
                        .SetTextVariable("FROM_REALM", fromRealm)
                        .SetTextVariable("THIRD_REALM", ResolveKingdom(term.ThirdKingdomId)?.Name ?? TextObject.GetEmpty())
                        .ToString();
                case TreatyTermType.EnforceRebelDemands:
                    FactionObject rebels = Campaign.Current?.GetCampaignBehavior<FactionManagerBehavior>()
                        ?.GetFactionByTrackedRebelKingdomId(term.ThirdKingdomId);
                    return new TextObject("{=BC_Treaty_SummaryEnforceRebelDemands}{TO_REALM} compels {FROM_REALM} to accept the demands of {FACTION_NAME}")
                        .SetTextVariable("FROM_REALM", fromRealm)
                        .SetTextVariable("TO_REALM", toRealm)
                        .SetTextVariable("FACTION_NAME", rebels?.GetDisplayName()
                            ?? ResolveKingdom(term.ThirdKingdomId)?.Name
                            ?? TextObject.GetEmpty())
                        .ToString();
                default:
                    return string.Empty;
            }
        }

        private static void ApplyPrisonerReleaseTerm(
            TreatyTermRecord term,
            TreatyPrisonerReleaseTally prisonerReleases)
        {
            Hero hero = Hero.AllAliveHeroes.FirstOrDefault(candidate => candidate?.StringId == term?.HeroId);
            Kingdom expectedCaptor = ResolveKingdom(term?.FromKingdomId);
            Kingdom expectedCaptiveRealm = ResolveKingdom(term?.ToKingdomId);
            if (hero == null
                || !hero.IsPrisoner
                || hero.Clan?.Kingdom != expectedCaptiveRealm
                || RetainedTreatyPrisonerBehavior.GetCaptorKingdom(hero) != expectedCaptor)
                return;

            RetainedTreatyPrisonerBehavior prisoners = Campaign.Current
                ?.GetCampaignBehavior<RetainedTreatyPrisonerBehavior>();
            if (prisoners?.ReleaseByTreaty(hero) == true)
                prisonerReleases?.Record(expectedCaptor, expectedCaptiveRealm);
        }

        private static void ApplyClaimRenunciationTerm(TreatyTermRecord term)
        {
            FeudalTitleBehavior titles = Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>();
            Clan claimant = ResolveClan(term?.ClanId);
            FeudalTitleRecord title = titles?.GetTitle(term?.TitleId);
            if (titles == null || claimant == null || title == null)
                return;

            titles.TryRenounceExplicitClaim(claimant, title, out _, out _);
        }

        private static void ApplyRulerRebukeTerm(TreatyTermRecord term)
        {
            Kingdom targetRealm = ResolveKingdom(term?.FromKingdomId);
            Kingdom imposingRealm = ResolveKingdom(term?.ToKingdomId);
            Clan targetClan = targetRealm?.RulingClan;
            Hero targetRuler = targetClan?.Leader;
            Hero imposingRuler = imposingRealm?.RulingClan?.Leader;
            if (targetClan == null || targetRuler == null || imposingRuler == null)
                return;

            bool severe = term.Type == TreatyTermType.HumiliateRuler;
            ApplyPrestigeTransfer(
                targetClan,
                targetRuler,
                imposingRealm.RulingClan,
                imposingRuler,
                severe ? C.TreatyHumiliateRenownTransfer : C.TreatyDiscreditRenownTransfer,
                severe ? C.TreatyHumiliateInfluenceTransfer : C.TreatyDiscreditInfluenceTransfer);

            int vassalPenalty = severe ? C.TreatyHumiliateVassalRelationLoss : C.TreatyDiscreditVassalRelationLoss;
            foreach (Clan vassal in GetEligibleClans(targetRealm).Where(clan => clan != targetClan))
                ApplyTreatyRelationChange(vassal.Leader, targetRuler, -vassalPenalty,
                    severe ? RelationMemorySources.HumiliatedOurRealm : RelationMemorySources.DiscreditedOurRealm);

            int rulerPenalty = severe ? C.TreatyHumiliateRulerRelationLoss : C.TreatyDiscreditRulerRelationLoss;
            ApplyTreatyRelationChange(targetRuler, imposingRuler, -rulerPenalty,
                severe ? RelationMemorySources.HumiliatedMe : RelationMemorySources.DiscreditedMe);
        }

        private static void ApplyPrestigeTransfer(
            Clan defeatedClan,
            Hero defeatedRuler,
            Clan victoriousClan,
            Hero victoriousRuler,
            int renownAmount,
            int influenceAmount)
        {
            if (defeatedClan == null || defeatedRuler == null || victoriousClan == null || victoriousRuler == null)
                return;

            float renownLoss = Math.Min(Math.Max(0f, defeatedClan.Renown), Math.Max(0, renownAmount));
            if (renownLoss > 0f)
            {
                GainRenownAction.Apply(defeatedRuler, -renownLoss, true);
                GainRenownAction.Apply(victoriousRuler, renownLoss, true);
            }

            float influenceLoss = NpcInfluenceBudgetService.ApplyClampedLoss(
                defeatedClan,
                Math.Max(0, influenceAmount),
                "treaty_prestige_transfer");
            if (influenceLoss > 0f)
                ChangeClanInfluenceAction.Apply(victoriousClan, influenceLoss);
        }

        private static void ApplyDiplomaticSeveranceTerm(TreatyTermRecord term)
        {
            Kingdom concedingRealm = ResolveKingdom(term?.FromKingdomId);
            Kingdom demandingRealm = ResolveKingdom(term?.ToKingdomId);
            Kingdom thirdRealm = ResolveKingdom(term?.ThirdKingdomId);
            if (concedingRealm == null || demandingRealm == null || thirdRealm == null)
                return;

            bool alliance = term.Type == TreatyTermType.EndAlliance;
            bool ended;
            if (alliance)
            {
                IAllianceCampaignBehavior alliances = Campaign.Current?.GetCampaignBehavior<IAllianceCampaignBehavior>();
                ended = alliances?.IsAllyWithKingdom(concedingRealm, thirdRealm) == true;
                if (ended)
                    alliances.EndAlliance(concedingRealm, thirdRealm);
            }
            else
            {
                ITradeAgreementsCampaignBehavior trade = Campaign.Current?.GetCampaignBehavior<ITradeAgreementsCampaignBehavior>();
                ended = trade?.HasTradeAgreement(concedingRealm, thirdRealm, out _) == true;
                if (ended)
                    trade.EndTradeAgreement(concedingRealm, thirdRealm);
            }

            if (!ended)
                return;

            int demandingPenalty = alliance
                ? C.TreatyEndAllianceDemandingRulerRelationLoss
                : C.TreatyEndTradeAgreementDemandingRulerRelationLoss;
            int concedingPenalty = alliance
                ? C.TreatyEndAllianceConcedingRulerRelationLoss
                : C.TreatyEndTradeAgreementConcedingRulerRelationLoss;
            string severanceContext = thirdRealm.Name?.ToString();
            ApplyNamedTreatyRelationChange(demandingRealm.RulingClan?.Leader, thirdRealm.RulingClan?.Leader, -demandingPenalty,
                RelationMemorySources.BrokeTreatyOrAlliance, 15f, RelationMemoryScope.House, severanceContext);
            ApplyNamedTreatyRelationChange(concedingRealm.RulingClan?.Leader, thirdRealm.RulingClan?.Leader, -concedingPenalty,
                RelationMemorySources.BrokeTreatyOrAlliance, 15f, RelationMemoryScope.House, severanceContext);

            TextObject message = alliance
                ? new TextObject("{=BC_Treaty_AllianceEndedByTreaty}Under the peace terms agreed with {DEMANDING_REALM}, {CONCEDING_REALM} has formally ended its alliance with {THIRD_REALM}.")
                : new TextObject("{=BC_Treaty_TradeEndedByTreaty}Under the peace terms agreed with {DEMANDING_REALM}, {CONCEDING_REALM} has formally ended its trade agreement with {THIRD_REALM}.");
            message.SetTextVariable("DEMANDING_REALM", demandingRealm.Name);
            message.SetTextVariable("CONCEDING_REALM", concedingRealm.Name);
            message.SetTextVariable("THIRD_REALM", thirdRealm.Name);
            BellumCivileNotifications.Show(message, BellumNotificationColors.Warning,
                primaryKingdom: concedingRealm, secondaryKingdom: thirdRealm, isMajorEvent: true);
        }

        private static float ScoreDiplomaticSeverance(TreatyDiplomaticSeveranceCandidate candidate, Kingdom beneficiary)
        {
            if (candidate?.ThirdRealm == null || beneficiary?.RulingClan?.Leader == null)
                return float.MinValue;

            bool alliance = candidate.TermType == TreatyTermType.EndAlliance;
            float score = alliance ? 25f : 5f;
            if (beneficiary.IsAtWarWith(candidate.ThirdRealm))
                score += alliance ? 40f : 20f;

            int relation = beneficiary.RulingClan.Leader.GetRelation(candidate.ThirdRealm.RulingClan?.Leader);
            score += Math.Max(-20f, Math.Min(30f, -relation * 0.5f));
            if (!alliance && beneficiary.RulingClan.Leader.GetTraitLevel(DefaultTraits.Calculating) > 0)
                score += 10f;
            if (alliance && beneficiary.RulingClan.Leader.GetTraitLevel(DefaultTraits.Valor) > 0)
                score += 5f;
            return score;
        }

        private static void ApplyStructuralPoliticalTerms(TreatyProposalRecord proposal, HashSet<TreatyTermRecord> delivered = null)
        {
            foreach (TreatyTermRecord term in (proposal?.Terms ?? Enumerable.Empty<TreatyTermRecord>())
                .OrderBy(term => term?.Type == TreatyTermType.ArrangeRoyalMarriage ? 0
                    : term?.Type == TreatyTermType.ReleaseClientState ? 1
                    : term?.Type == TreatyTermType.ReleaseVassal ? 2
                    : term?.Type == TreatyTermType.ForceVassalization ? 3
                    : term?.Type == TreatyTermType.MakeClientKingdom ? 4
                    : 5))
            {
                if (term?.Type == TreatyTermType.ReleaseClientState)
                {
                    Kingdom formerSuzerain = ResolveKingdom(term.FromKingdomId);
                    Kingdom liberatingRealm = ResolveKingdom(term.ToKingdomId);
                    Kingdom clientRealm = ResolveKingdom(term.ThirdKingdomId);
                    ClientKingdomBehavior clients = ClientKingdomBehavior.Instance;
                    if (clients?.GetSuzerain(clientRealm) == formerSuzerain
                        && clients.EndClientStatus(clientRealm, "released by peace treaty"))
                    {
                        delivered?.Add(term);
                        Hero liberator = liberatingRealm?.RulingClan?.Leader;
                        ApplyNamedTreatyRelationChange(clientRealm?.RulingClan?.Leader, liberator, C.TreatyClientReleaseRulerRelationGain,
                            RelationMemorySources.LiberatedMyRealm, 20f, RelationMemoryScope.House, clientRealm?.Name?.ToString());
                        foreach (Clan clan in clientRealm.Clans.Where(candidate => candidate != null
                            && !candidate.IsEliminated
                            && candidate != clientRealm.RulingClan
                            && !candidate.IsUnderMercenaryService))
                        {
                            ApplyNamedTreatyRelationChange(clan.Leader, liberator, C.TreatyClientReleaseLordRelationGain,
                                RelationMemorySources.LiberatedMyRealm, 20f, RelationMemoryScope.House, clientRealm?.Name?.ToString());
                        }
                        ApplyTreatyRelationChange(formerSuzerain?.RulingClan?.Leader, liberator, -C.TreatyClientReleaseFormerSuzerainRelationLoss,
                            RelationMemorySources.FreedMySubject);
                        TextObject message = new TextObject("{=BC_Treaty_ClientReleased}{CLIENT_REALM} has been released from the clientage of {FORMER_SUZERAIN}. Its ruler hails {LIBERATOR_REALM} as the realm's liberator.")
                            .SetTextVariable("CLIENT_REALM", clientRealm?.Name ?? TextObject.GetEmpty())
                            .SetTextVariable("FORMER_SUZERAIN", formerSuzerain?.Name ?? TextObject.GetEmpty())
                            .SetTextVariable("LIBERATOR_REALM", liberatingRealm?.Name ?? TextObject.GetEmpty());
                        BellumCivileNotifications.Show(message, BellumNotificationColors.Success,
                            primaryKingdom: clientRealm, secondaryKingdom: liberatingRealm,
                            primaryClan: clientRealm?.RulingClan, isMajorEvent: true);
                    }
                }
                else if (term?.Type == TreatyTermType.ReleaseVassal)
                {
                    Kingdom sourceRealm = ResolveKingdom(term.FromKingdomId);
                    Kingdom imposingRealm = ResolveKingdom(term.ToKingdomId);
                    Clan releasedClan = ResolveClan(term.ClanId);
                    FeudalTitleRecord title = Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>()?.GetTitle(term.TitleId);
                    if (TreatyRealmTransitionService.TryReleaseVassal(sourceRealm, releasedClan, title,
                        out Kingdom independentRealm, out string releaseReason))
                    {
                        delivered?.Add(term);
                        ApplyNamedTreatyRelationChange(releasedClan?.Leader, imposingRealm?.RulingClan?.Leader, 20,
                            RelationMemorySources.LiberatedMyRealm, 20f, RelationMemoryScope.House, independentRealm?.Name?.ToString());
                        ApplyTreatyRelationChange(releasedClan?.Leader, sourceRealm?.RulingClan?.Leader, -20,
                            RelationMemorySources.TreatySeparation);
                        TextObject message = new TextObject("{=BC_Treaty_VassalReleased}{SOURCE_REALM} has released the {CLAN_NAME}. By treaty, {NEW_REALM} now stands as an independent realm.")
                            .SetTextVariable("SOURCE_REALM", sourceRealm?.Name ?? TextObject.GetEmpty())
                            .SetTextVariable("CLAN_NAME", releasedClan?.Name ?? TextObject.GetEmpty())
                            .SetTextVariable("NEW_REALM", independentRealm?.Name ?? TextObject.GetEmpty());
                        BellumCivileNotifications.Show(message, BellumNotificationColors.Warning,
                            primaryKingdom: sourceRealm, secondaryKingdom: independentRealm, primaryClan: releasedClan, isMajorEvent: true);
                    }
                    else
                    {
                        BellumCivileLogger.Log($"Treaty vassal release failed during execution; clan={term.ClanId}; title={term.TitleId}; reason={releaseReason}.");
                    }
                }
                else if (term?.Type == TreatyTermType.ForceVassalization)
                {
                    Kingdom defeatedRealm = ResolveKingdom(term.FromKingdomId);
                    Kingdom victorRealm = ResolveKingdom(term.ToKingdomId);
                    Hero defeatedRuler = defeatedRealm?.RulingClan?.Leader;
                    Hero victorRuler = victorRealm?.RulingClan?.Leader;
                    TextObject defeatedName = defeatedRealm?.Name ?? TextObject.GetEmpty();
                    TextObject victorName = victorRealm?.Name ?? TextObject.GetEmpty();
                    if (TreatyRealmTransitionService.TryForceVassalize(victorRealm, defeatedRealm, out string forceReason))
                    {
                        delivered?.Add(term);
                        ApplyTreatyRelationChange(defeatedRuler, victorRuler, -30,
                            RelationMemorySources.ForcedVassalization);
                        TextObject message = new TextObject("{=BC_Treaty_RealmVassalized}{DEFEATED_REALM} has surrendered its sovereignty. Its ruler and vassals now owe allegiance to {VICTOR_REALM}.")
                            .SetTextVariable("DEFEATED_REALM", defeatedName)
                            .SetTextVariable("VICTOR_REALM", victorName);
                        BellumCivileNotifications.Show(message, BellumNotificationColors.Warning,
                            primaryKingdom: victorRealm, primaryClan: defeatedRuler?.Clan, isMajorEvent: true);
                    }
                    else
                    {
                        BellumCivileLogger.Log($"Treaty force vassalization failed during execution; defeated={term.FromKingdomId}; victor={term.ToKingdomId}; reason={forceReason}.");
                    }
                }
                else if (term?.Type == TreatyTermType.MakeClientKingdom)
                {
                    Kingdom clientRealm = ResolveKingdom(term.FromKingdomId);
                    Kingdom suzerainRealm = ResolveKingdom(term.ToKingdomId);
                    Hero clientRuler = clientRealm?.RulingClan?.Leader;
                    Hero suzerainRuler = suzerainRealm?.RulingClan?.Leader;
                    ClientKingdomBehavior clientBehavior = ClientKingdomBehavior.Instance;
                    string clientReason = "client-kingdom behavior is unavailable";
                    if (clientBehavior != null && clientBehavior.TryEstablishClientKingdom(
                        clientRealm,
                        suzerainRealm,
                        term.WasVoluntaryOffering,
                        out clientReason))
                    {
                        delivered?.Add(term);
                        ApplyTreatyRelationChange(clientRuler, suzerainRuler, term.WasVoluntaryOffering ? 5 : -30,
                            term.WasVoluntaryOffering ? RelationMemorySources.VoluntaryClientage : RelationMemorySources.ForcedClientage);
                        if (!term.WasVoluntaryOffering)
                        {
                            foreach (Clan clan in clientRealm.Clans.Where(candidate => candidate != null
                                && !candidate.IsEliminated
                                && candidate != clientRealm.RulingClan
                                && !candidate.IsUnderMercenaryService))
                            {
                                ApplyTreatyRelationChange(clan.Leader, suzerainRuler, -10,
                                    RelationMemorySources.ForcedClientage);
                            }
                        }

                    }
                    else
                    {
                        BellumCivileLogger.Log($"Treaty client kingdom failed during execution; client={term.FromKingdomId}; suzerain={term.ToKingdomId}; reason={clientReason}.");
                    }
                }
            }
        }

        private static void ApplyTreatyRelationChange(Hero first, Hero second, int change, string sourceId)
        {
            if (first == null || second == null || first == second || change == 0)
                return;

            bool notifyPlayer = first == Hero.MainHero || second == Hero.MainHero;
            RelationMemoryService.ApplyChangeWithDefaultDuration(first, second, change, notifyPlayer,
                sourceId, RelationMemoryScope.Personal);
        }

        private static void ApplyNamedTreatyRelationChange(
            Hero first,
            Hero second,
            int change,
            string sourceId,
            float durationYears = 5f,
            RelationMemoryScope scope = RelationMemoryScope.Personal,
            string contextText = null)
        {
            if (first == null || second == null || first == second || change == 0)
                return;
            bool notifyPlayer = first == Hero.MainHero || second == Hero.MainHero;
            RelationMemoryService.ApplyChange(first, second, change, notifyPlayer,
                sourceId, durationYears, scope, contextText);
        }

        private static void ApplyTerritorialTerms(
            WarScoreRecord war,
            TreatyProposalRecord proposal,
            Kingdom winner,
            Kingdom loser,
            TreatyPrisonerReleaseTally prisonerReleases)
        {
            var territory = new ClientWarTerritory(war);
            var recognitions = proposal.Terms.Where(t => t?.Type == TreatyTermType.RecognizeClientOccupation)
                .ToDictionary(t => t.SettlementId);
            Dictionary<string, TreatyTermRecord> transfers = proposal.Terms
                .Where(term => term?.Type == TreatyTermType.TransferFief && !string.IsNullOrWhiteSpace(term.SettlementId))
                .GroupBy(term => term.SettlementId)
                .ToDictionary(group => group.Key, group => group.First());

            foreach (WarScoreFiefSnapshotRecord snapshot in war.FiefSnapshots.Where(snapshot => snapshot != null))
            {
                Settlement settlement = ResolveSettlement(snapshot.SettlementId);
                if (settlement?.OwnerClan?.Kingdom == null)
                    continue;

                Kingdom currentKingdom = settlement.OwnerClan.Kingdom;
                if (!territory.Opposing(snapshot.OwnerKingdomId, currentKingdom.StringId))
                    continue;

                if (recognitions.TryGetValue(settlement.StringId, out var recognition)
                    && currentKingdom.StringId == recognition.ThirdKingdomId && settlement.OwnerClan.StringId == recognition.ClanId
                    && territory.OnSide(currentKingdom, ResolveKingdom(recognition.ToKingdomId)))
                {
                    BellumCivileLogger.Log($"Treaty client occupation retained; fief={settlement.StringId}; client={currentKingdom.StringId}; owner={settlement.OwnerClan.StringId}.");
                    continue;
                }

                if (transfers.TryGetValue(settlement.StringId, out TreatyTermRecord retained)
                    && currentKingdom.StringId == retained.ToKingdomId)
                    continue;

                Clan originalOwner = ResolveClan(snapshot.OwnerClanId);
                Kingdom originalKingdom = ResolveKingdom(snapshot.OwnerKingdomId);
                Clan recipient = originalOwner?.Kingdom == originalKingdom
                    ? originalOwner
                    : originalKingdom?.RulingClan;
                if (recipient?.Leader != null && settlement.OwnerClan != recipient)
                {
                    BellumTreatyTransferContext.Run(() =>
                        ChangeOwnerOfSettlementAction.ApplyByDefault(recipient.Leader, settlement));
                }
            }

            foreach (TreatyTermRecord term in transfers.Values)
            {
                Settlement settlement = ResolveSettlement(term.SettlementId);
                Kingdom from = ResolveKingdom(term.FromKingdomId);
                Kingdom to = ResolveKingdom(term.ToKingdomId);
                Kingdom current = settlement?.OwnerClan?.Kingdom;
                Clan receivingClan = to?.RulingClan;
                Hero receivingRuler = receivingClan?.Leader;
                if (settlement == null
                    || from == null
                    || to == null
                    || to.IsEliminated
                    || receivingClan == null
                    || receivingRuler == null
                    || receivingRuler.IsDead
                    || receivingRuler.Clan != receivingClan
                    || receivingClan.Kingdom != to
                    || current == null)
                {
                    BellumCivileLogger.Log($"Treaty territorial transfer skipped because its execution state is no longer valid; settlement={settlement?.StringId ?? "null"}; from={from?.StringId ?? "null"}; to={to?.StringId ?? "null"}; recipient={receivingRuler?.StringId ?? "null"}; current={current?.StringId ?? "null"}.");
                    continue;
                }
                if (current == to)
                    continue;
                if (current != from)
                {
                    BellumCivileLogger.Log($"Treaty territorial transfer skipped after ownership changed; settlement={settlement.StringId}; expected_from={from.StringId}; current={current.StringId}; to={to.StringId}.");
                    continue;
                }

                BellumTreatyTransferContext.Run(() =>
                {
                    // A treaty is not a siege. ApplyBySiege dereferences the capturer's mobile
                    // party while destroying the old garrison, which crashes when an AI ruler is
                    // not personally leading a party. Queue Bellum's fief vote explicitly instead.
                    ChangeOwnerOfSettlementAction.ApplyByDefault(receivingRuler, settlement);
                    PopulateTreatyTransferGarrison(settlement, to);
                });

                if (settlement.OwnerClan?.Kingdom != to)
                {
                    BellumCivileLogger.Log($"Treaty territorial transfer failed to establish the intended owner; settlement={settlement.StringId}; expected={to.StringId}; actual={settlement.OwnerClan?.Kingdom?.StringId ?? "null"}.");
                    continue;
                }

                ReleasePrisonersBundledWithFief(settlement, from, to, prisonerReleases);
                FiefDeliberationBehavior deliberation = Campaign.Current
                    ?.GetCampaignBehavior<FiefDeliberationBehavior>();
                if (deliberation == null || !deliberation.QueueTreatySettlementVote(settlement, receivingClan))
                    BellumCivileLogger.Log($"Treaty territorial transfer completed without a queued fief deliberation; settlement={settlement.StringId}; realm={to.StringId}.");
            }
        }

        private static void ReleasePrisonersBundledWithFief(
            Settlement settlement,
            Kingdom from,
            Kingdom to,
            TreatyPrisonerReleaseTally prisonerReleases)
        {
            RetainedTreatyPrisonerBehavior prisoners = Campaign.Current
                ?.GetCampaignBehavior<RetainedTreatyPrisonerBehavior>();
            if (prisoners == null)
                return;

            foreach (TreatyPrisonerReleaseCandidate candidate in TreatyDraftService
                .GetAvailablePrisonerReleases(from, to)
                .Where(candidate => RetainedTreatyPrisonerBehavior.IsHeldInSettlementDungeon(candidate.Hero, settlement))
                .ToList())
            {
                if (prisoners.ReleaseByTreaty(candidate.Hero))
                {
                    prisonerReleases?.Record(from, to);
                    BellumCivileLogger.Log($"Treaty prisoner released with ceded dungeon; hero={candidate.Hero.StringId}; settlement={settlement.StringId}; cost={candidate.WarScoreCost}.");
                }
            }
        }

        private static void PopulateTreatyTransferGarrison(Settlement settlement, Kingdom receivingRealm)
        {
            if (settlement?.Town?.GarrisonParty == null)
                return;

            CharacterObject basicTroop = settlement.OwnerClan?.Culture?.BasicTroop
                ?? receivingRealm?.Culture?.BasicTroop;
            if (basicTroop == null)
                return;

            int replacementSize = Math.Max(0, (int)Math.Floor(settlement.Militia));
            settlement.Town.GarrisonParty.MemberRoster.Clear();
            if (replacementSize <= 0)
                return;

            int upgradedCount = replacementSize / 3;
            int basicCount = replacementSize - upgradedCount;
            CharacterObject upgradedTroop = basicTroop.UpgradeTargets?.Length > 0
                ? basicTroop.UpgradeTargets[0]
                : basicTroop;
            settlement.Town.GarrisonParty.MemberRoster.AddToCounts(basicTroop, basicCount);
            if (upgradedCount > 0)
                settlement.Town.GarrisonParty.MemberRoster.AddToCounts(upgradedTroop, upgradedCount);

            BellumCivileLogger.Log($"Treaty garrison replaced; settlement={settlement.StringId}; realm={receivingRealm?.StringId}; culture={basicTroop.Culture?.StringId}; troops={replacementSize}.");
        }

        private void ProcessTributes()
        {
            foreach (ActiveTreatyTributeRecord tribute in _activeTributes.ToList())
            {
                Kingdom payer = ResolveKingdom(tribute.PayerKingdomId);
                Kingdom recipient = ResolveKingdom(tribute.RecipientKingdomId);
                if (tribute.RemainingDays <= 0 || payer == null || recipient == null || payer.IsEliminated || recipient.IsEliminated)
                {
                    _activeTributes.Remove(tribute);
                    continue;
                }

                TransferCollectiveGold(payer, recipient, tribute.DailyGold);
                tribute.TickDay();
            }
        }

        private static int TransferCollectiveGold(Kingdom payer, Kingdom recipient, int requestedAmount)
        {
            int available = CalculateCollectiveAvailableGold(payer);
            int amount = Math.Min(Math.Max(0, requestedAmount), available);
            if (amount <= 0)
                return 0;

            List<Clan> payers = GetEligibleClans(payer).Where(clan => GetAvailableGold(clan) > 0).ToList();
            int totalAvailable = payers.Sum(GetAvailableGold);
            int paid = 0;
            for (int index = 0; index < payers.Count; index++)
            {
                Clan clan = payers[index];
                int share = index == payers.Count - 1
                    ? amount - paid
                    : Math.Min(GetAvailableGold(clan), (int)Math.Floor(amount * (GetAvailableGold(clan) / (float)totalAvailable)));
                if (share <= 0)
                    continue;
                GiveGoldAction.ApplyBetweenCharacters(clan.Leader, null, share, true);
                paid += share;
            }

            if (paid <= 0)
                return 0;

            List<Clan> recipients = GetEligibleClans(recipient).ToList();
            float totalWeight = recipients.Sum(clan => Math.Max(1f, clan.CurrentTotalStrength));
            int distributed = 0;
            for (int index = 0; index < recipients.Count; index++)
            {
                Clan clan = recipients[index];
                int share = index == recipients.Count - 1
                    ? paid - distributed
                    : (int)Math.Floor(paid * (Math.Max(1f, clan.CurrentTotalStrength) / totalWeight));
                if (share <= 0)
                    continue;
                GiveGoldAction.ApplyBetweenCharacters(null, clan.Leader, share, true);
                distributed += share;
            }

            return paid;
        }

        private static int CalculateCollectiveAvailableGold(Kingdom kingdom)
        {
            return GetEligibleClans(kingdom).Sum(GetAvailableGold);
        }

        private static int GetAvailableGold(Clan clan)
        {
            return Math.Max(0, (clan?.Leader?.Gold ?? 0) - C.TreatyClanGoldReserve);
        }

        private TreatyCouncilSnapshot GetOrCreateCouncilSnapshot(WarScoreRecord war, TreatyProposalRecord proposal)
        {
            if (proposal == null)
                return null;
            if (_councilSnapshots.TryGetValue(proposal.ProposalId, out TreatyCouncilSnapshot existing))
                return existing;

            Kingdom attacker = war == null ? null : ResolveKingdom(war.AttackerKingdomId);
            Kingdom defender = war == null ? null : ResolveKingdom(war.DefenderKingdomId);
            WarPeaceRevampBehavior warWill = Campaign.Current?.GetCampaignBehavior<WarPeaceRevampBehavior>();
            TreatyCouncilSnapshot snapshot = TreatyCouncilSnapshot.Capture(
                new[] { attacker, defender },
                clan => warWill?.GetWarWill(clan) ?? 50f);
            _councilSnapshots[proposal.ProposalId] = snapshot;
            return snapshot;
        }

        private static IEnumerable<Clan> GetEligibleClans(Kingdom kingdom)
        {
            return kingdom?.Clans?.Where(clan => clan != null
                && !clan.IsEliminated
                && clan.Leader != null
                && !clan.IsUnderMercenaryService) ?? Enumerable.Empty<Clan>();
        }

        private static bool IsAiOnlyWar(Kingdom first, Kingdom second)
        {
            return first != null
                && second != null
                && !first.IsEliminated
                && !second.IsEliminated
                && !WarPeaceRevampBehavior.IsPlayerPoliticalParticipant(first, second);
        }

        private static Kingdom ResolveKingdom(string kingdomId)
        {
            return Kingdom.All.FirstOrDefault(kingdom => kingdom != null && kingdom.StringId == kingdomId);
        }

        private static Clan ResolveClan(string clanId)
        {
            return Clan.All.FirstOrDefault(clan => clan != null && clan.StringId == clanId);
        }

        private static Settlement ResolveSettlement(string settlementId)
        {
            return Settlement.All.FirstOrDefault(settlement => settlement != null && settlement.StringId == settlementId);
        }

        private void EnsureCollectionsInitialized()
        {
            if (_proposals == null)
                _proposals = new List<TreatyProposalRecord>();
            if (_activeTributes == null)
                _activeTributes = new List<ActiveTreatyTributeRecord>();
            if (_pendingRebelResolutions == null)
                _pendingRebelResolutions = new List<PendingTreatyRebelResolutionRecord>();
        }
    }
}
