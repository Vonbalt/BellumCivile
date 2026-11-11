using System;
using System.Collections.Generic;
using System.Linq;
using BellumCivile.WarPeace;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Election;
using TaleWorlds.CampaignSystem.Encounters;
using TaleWorlds.CampaignSystem.GameState;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace BellumCivile.Behaviors
{
    // Owns pending proposals independently of the voting screen and hostage custody.
    public sealed class VoluntaryPactProposalBehavior : CampaignBehaviorBase
    {
        private sealed class PendingBallot
        {
            internal VoluntaryPactProposalRecord Proposal;
            internal bool Target;
        }
        private List<VoluntaryPactProposalRecord> _proposals = new List<VoluntaryPactProposalRecord>();
        private Dictionary<string, double> _refusalUntil = new Dictionary<string, double>();
        private Queue<VoluntaryPactProposalRecord> _deliveries = new Queue<VoluntaryPactProposalRecord>();
        private Queue<VoluntaryPactProposalRecord> _targetChoices = new Queue<VoluntaryPactProposalRecord>();
        private Queue<VoluntaryPactProposalRecord> _outcomes = new Queue<VoluntaryPactProposalRecord>();
        private Queue<PendingBallot> _ballots = new Queue<PendingBallot>();
        private bool _targetInquiryOpen;

        public override void RegisterEvents()
        {
            CampaignEvents.HourlyTickEvent.AddNonSerializedListener(this, RecoverDeliveries);
            CampaignEvents.HourlyTickEvent.AddNonSerializedListener(this, AdvanceTargetChoices);
            CampaignEvents.HourlyTickEvent.AddNonSerializedListener(this, ReportOutcomes);
            CampaignEvents.HourlyTickEvent.AddNonSerializedListener(this, ReconcileBallots);
            CampaignEvents.KingdomDecisionCancelled.AddNonSerializedListener(this, OnDecisionCancelled);
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, RecoverStartedVotes);
        }

        public override void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData("BellumCivile_VoluntaryPactProposals", ref _proposals);
            dataStore.SyncData("BellumCivile_VoluntaryPactRefusalUntil", ref _refusalUntil);
            if (_proposals == null) _proposals = new List<VoluntaryPactProposalRecord>();
            if (_refusalUntil == null) _refusalUntil = new Dictionary<string, double>();
            if (dataStore.IsLoading)
            {
                _deliveries = new Queue<VoluntaryPactProposalRecord>(_proposals.Where(p => p?.Phase == VoluntaryPactProposalPhase.Delivery));
                _targetChoices = new Queue<VoluntaryPactProposalRecord>(_proposals.Where(p => p?.Phase == VoluntaryPactProposalPhase.TargetChoice));
                _outcomes = new Queue<VoluntaryPactProposalRecord>(_proposals.Where(p => p != null && !p.OutcomeReported
                    && (p.Phase == VoluntaryPactProposalPhase.Completed || p.Phase == VoluntaryPactProposalPhase.Cancelled)));
                _ballots = new Queue<PendingBallot>(_proposals.Where(p => p != null
                    && (p.Phase == VoluntaryPactProposalPhase.SourceVote && p.SourceFilingStarted
                        || p.Phase == VoluntaryPactProposalPhase.TargetVote && p.TargetFilingStarted))
                    .Select(p => new PendingBallot { Proposal = p, Target = p.Phase == VoluntaryPactProposalPhase.TargetVote }));
            }
        }

        internal VoluntaryPactProposalRecord Find(string id)
            => string.IsNullOrWhiteSpace(id) ? null : _proposals.FirstOrDefault(p => p != null && p.Id == id);

        internal bool TryTrack(VoluntaryPactProposalRecord proposal)
        {
            if (proposal == null || proposal.Phase != VoluntaryPactProposalPhase.SourceVote || proposal.TargetHostage != null
                || !IsBallotEligible(proposal) || Find(proposal.Id) != null
                || _proposals.Any(p => IsPending(p) && SamePair(p, proposal.Source, proposal.Target)))
                return false;
            _proposals.Add(proposal);
            return true;
        }

        internal bool TryResolveVote(string id, Kingdom votingRealm, bool approved)
        {
            var proposal = Find(id);
            if (proposal == null || votingRealm == null || !IsBallotEligible(proposal)) return false;
            if (proposal.Phase == VoluntaryPactProposalPhase.SourceVote && votingRealm == proposal.Source)
            {
                if (!proposal.TryFinishSourceVote(approved)) return false;
                if (approved) _targetChoices.Enqueue(proposal);
                else QueueOutcome(proposal);
                return true;
            }
            if (proposal.Phase == VoluntaryPactProposalPhase.TargetVote && votingRealm == proposal.Target)
            {
                if (!proposal.TryFinishTargetVote(approved)) return false;
                if (approved) _deliveries.Enqueue(proposal);
                else QueueOutcome(proposal);
                return true;
            }
            return false;
        }

        internal bool TryChooseTargetHostage(string id, Hero hostage)
        {
            var proposal = Find(id);
            if (proposal == null || proposal.Phase != VoluntaryPactProposalPhase.TargetChoice
                || !IsBallotEligible(proposal) || hostage == null || hostage == Hero.MainHero
                || !TreatyHostageEligibility.GetCandidates(proposal.Target).Any(c => c.Hero == hostage)) return false;
            return proposal.TryChooseTargetHostage(hostage);
        }

        internal bool IsBallotEligible(VoluntaryPactProposalRecord proposal)
        {
            bool targetSelected = proposal?.Phase == VoluntaryPactProposalPhase.TargetVote;
            if (proposal == null || !proposal.HasValidTerms(targetSelected)
                || !proposal.PaymentPartiesCurrent()
                || !BellumCivileOptions.EnableWarPeaceLogicRevamp
                || proposal.Source.IsEliminated || proposal.Target.IsEliminated
                || proposal.Sponsor.Kingdom != proposal.Source
                || proposal.Source.RulingClan != proposal.SourceHouse || proposal.Target.RulingClan != proposal.TargetHouse
                || proposal.Source.IsAtWarWith(proposal.Target) || proposal.Source.IsAllyWith(proposal.Target)
                || BellumKingdomVisibilityHelper.IsTemporaryBellumKingdom(proposal.Source)
                || BellumKingdomVisibilityHelper.IsTemporaryBellumKingdom(proposal.Target)
                || CivilWarConflictBehavior.IsRealmTransferPending(proposal.Source)
                || CivilWarConflictBehavior.IsRealmTransferPending(proposal.Target)
                || ClientKingdomBehavior.Instance?.IsClientKingdom(proposal.Source) == true
                || ClientKingdomBehavior.Instance?.IsClientKingdom(proposal.Target) == true
                || Campaign.Current?.GetCampaignBehavior<HostagePactBehavior>()?.HasPactBetween(proposal.Source, proposal.Target) == true
                || proposal.SourceHostage == Hero.MainHero
                || (proposal.TargetHostage != null && proposal.TargetHostage == Hero.MainHero)
                || HostagePactBehavior.SelectHolding(proposal.SourceHouse) == null
                || HostagePactBehavior.SelectHolding(proposal.TargetHouse) == null
                || !TreatyHostageEligibility.GetCandidates(proposal.Source).Any(c => c.Hero == proposal.SourceHostage))
                return false;
            return TreatyHostageEligibility.GetCandidates(proposal.Target).Any(c => c.Hero != null
                && c.Hero != Hero.MainHero && c.Hero != proposal.SourceHostage
                && (!targetSelected || c.Hero == proposal.TargetHostage));
        }

        internal bool TryCancelInvalidBallot(string id)
        {
            var proposal = Find(id);
            if (proposal == null || (proposal.Phase != VoluntaryPactProposalPhase.SourceVote
                && proposal.Phase != VoluntaryPactProposalPhase.TargetChoice
                && proposal.Phase != VoluntaryPactProposalPhase.TargetVote) || IsBallotEligible(proposal)) return false;
            return CancelProposal(proposal);
        }

        private void OnDecisionCancelled(KingdomDecision decision, bool playerInvolved)
        {
            if (!(decision is VoluntaryPactDecision pactDecision)) return;
            var proposal = Find(pactDecision.ProposalId);
            if (proposal == null) return;
            bool matches = pactDecision.IsTargetVote
                ? proposal.Phase == VoluntaryPactProposalPhase.TargetVote && pactDecision.Kingdom == proposal.Target
                : proposal.Phase == VoluntaryPactProposalPhase.SourceVote && pactDecision.Kingdom == proposal.Source;
            if (matches) CancelProposal(proposal);
        }

        internal bool HasPendingPair(Kingdom first, Kingdom second)
            => first != null && second != null && first != second
                && _proposals.Any(p => IsPending(p) && SamePair(p, first, second));

        internal bool CanPropose(Kingdom source, Kingdom target, Clan sponsor, out TextObject reason)
        {
            reason = TextObject.GetEmpty();
            if (!BellumCivileOptions.EnableWarPeaceLogicRevamp || source == null || target == null || source == target
                || sponsor == null || sponsor.Kingdom != source || source.IsEliminated || target.IsEliminated
                || source.RulingClan == null || target.RulingClan == null || source.Leader == null || target.Leader == null
                || BellumKingdomVisibilityHelper.IsTemporaryBellumKingdom(source)
                || BellumKingdomVisibilityHelper.IsTemporaryBellumKingdom(target)
                || CivilWarConflictBehavior.IsRealmTransferPending(source) || CivilWarConflictBehavior.IsRealmTransferPending(target))
                reason = new TextObject("{=BC_VoluntaryPact_Unavailable}A pledge cannot be proposed under the current circumstances.");
            else if (sponsor.IsUnderMercenaryService)
                reason = new TextObject("{=BC_VoluntaryPact_MercenaryBlocked}Mercenary clans cannot propose a pledge for the realm.");
            else if (source.IsAtWarWith(target) || source.IsAllyWith(target))
                reason = new TextObject("{=BC_VoluntaryPact_WarOrAllianceBlocked}The realms must be at peace and not already allied.");
            else if (ClientKingdomBehavior.Instance?.IsClientKingdom(source) == true
                || ClientKingdomBehavior.Instance?.IsClientKingdom(target) == true)
                reason = new TextObject("{=BC_VoluntaryPact_ClientBlocked}Both realms must be sovereign to exchange hostages.");
            else if (HasPendingPair(source, target)
                || Campaign.Current?.GetCampaignBehavior<HostagePactBehavior>()?.HasPactBetween(source, target) == true)
                reason = new TextObject("{=BC_VoluntaryPact_ExistingBlocked}These realms already have a pledge or a proposal in progress.");
            else if (TryGetRefusalCooldown(source, target, out int days))
                reason = new TextObject("{=BC_VoluntaryPact_RefusalCooldown}These realms recently rejected a pledge. Another proposal may be made in {DAYS} days.")
                    .SetTextVariable("DAYS", days);
            else if (float.IsNaN(sponsor.Influence) || float.IsInfinity(sponsor.Influence)
                || sponsor.Influence < PeacetimeHostagePactRules.ProposalInfluenceCost)
                reason = new TextObject("{=BC_VoluntaryPact_InfluenceBlocked}Your clan needs 100 influence to propose this pledge.");
            else if (HostagePactBehavior.SelectHolding(source.RulingClan) == null
                || HostagePactBehavior.SelectHolding(target.RulingClan) == null)
                reason = new TextObject("{=BC_VoluntaryPact_HoldingBlocked}Each ruling house needs a secure settlement to receive a hostage.");
            else if (!TreatyHostageEligibility.GetCandidates(source).Any(c => c.Hero != null && c.Hero != Hero.MainHero)
                || !TreatyHostageEligibility.GetCandidates(target).Any(c => c.Hero != null && c.Hero != Hero.MainHero))
                reason = new TextObject("{=BC_VoluntaryPact_HostageBlocked}Each ruling house needs an eligible relative to offer as a hostage.");
            else if (!AreNeighbors(source, target))
                reason = new TextObject("{=BC_VoluntaryPact_NeighborBlocked}Only neighboring realms can propose this pledge.");
            else return true;
            return false;
        }

        internal bool TryCreateAndFile(Kingdom source, Kingdom target, Clan sponsor, Hero sourceHostage,
            int durationDays, int compensation, out string proposalId)
        {
            proposalId = null;
            if (compensation < 0 || !CanPropose(source, target, sponsor, out _)) return false;
            int baseline = BellumCivileOptions.HostagePactMinimumDurationDays;
            if (!HostagePactRules.IsNegotiableDuration(durationDays, baseline)
                || (compensation > 0 && (source.Leader == target.Leader || source.Leader.Gold < compensation
                    || target.Leader.Gold > int.MaxValue - compensation))) return false;
            var proposal = new VoluntaryPactProposalRecord
            {
                Id = Guid.NewGuid().ToString("N"), Source = source, Target = target,
                SourceHouse = source.RulingClan, TargetHouse = target.RulingClan, Sponsor = sponsor,
                SourceHostage = sourceHostage, DurationDays = durationDays, BaselineDays = baseline,
                Compensation = compensation, Payer = source.Leader, Payee = target.Leader,
                CreatedDay = CampaignTime.Now.ToDays, Phase = VoluntaryPactProposalPhase.Draft
            };
            if (!proposal.TrySubmit() || !TryTrack(proposal)) return false;
            try
            {
                if (TryFileDecision(proposal.Id, false)) { proposalId = proposal.Id; return true; }
            }
            catch (Exception ex)
            {
                BellumCivileLogger.Log($"Voluntary pact proposal could not be filed; proposal={proposal.Id}; error={ex}");
            }
            // If native filing already began, TryFileDecision owns its refund and recovery.
            if (!proposal.SourceFilingStarted) CancelProposal(proposal);
            return false;
        }

        private static bool AreNeighbors(Kingdom first, Kingdom second)
        {
            try { return new WarTargetScoringService().ProtectionReach(first, second) != CourtProtectionReach.None; }
            catch (Exception ex)
            {
                BellumCivileLogger.Log($"Voluntary pact geography unavailable; first={first?.StringId}; second={second?.StringId}; error={ex}");
                return false;
            }
        }

        // Building a ballot is side-effect free; the caller files it through Kingdom.AddDecision.
        internal bool TryBuildDecision(string id, bool targetVote, out VoluntaryPactDecision decision)
        {
            decision = null;
            var proposal = Find(id);
            if (proposal == null || !IsBallotEligible(proposal)
                || (targetVote ? proposal.Phase != VoluntaryPactProposalPhase.TargetVote
                    : proposal.Phase != VoluntaryPactProposalPhase.SourceVote)) return false;
            Kingdom realm = targetVote ? proposal.Target : proposal.Source;
            Clan proposer = targetVote ? proposal.TargetHouse : proposal.Sponsor;
            if (!PeacetimeHostagePactScoring.TryScore(proposal, realm, out float support, out float warTargetScore)) return false;
            decision = new VoluntaryPactDecision(proposer, id, targetVote, support, warTargetScore);
            return true;
        }

        internal bool TryFileDecision(string id, bool targetVote)
        {
            var proposal = Find(id);
            if (proposal == null || (targetVote ? proposal.TargetFilingStarted : proposal.SourceFilingStarted)
                || !TryBuildDecision(id, targetVote, out var decision)) return false;
            var realm = targetVote ? proposal.Target : proposal.Source;
            var expectedPhase = targetVote ? VoluntaryPactProposalPhase.TargetVote : VoluntaryPactProposalPhase.SourceVote;
            float influence = decision.ProposerClan.Influence;
            int cost = decision.GetProposalInfluenceCost();
            if (cost > 0 && (float.IsNaN(influence) || float.IsInfinity(influence) || influence < cost)) return false;

            // An interrupted native filing is never replayed: native deducts influence before callbacks.
            if (targetVote) { proposal.TargetDecision = decision; proposal.TargetFilingStarted = true; }
            else
            {
                proposal.SourceDecision = decision;
                proposal.SourceInfluenceBeforeFiling = influence;
                proposal.SourceFilingStarted = true;
            }
            try { IdeologyBehavior.AddDecisionAsModAction(realm, decision, targetVote); }
            catch (Exception ex)
            {
                BellumCivileLogger.Log($"Voluntary pact vote filing interrupted; proposal={proposal.Id}; target={targetVote}; error={ex}");
            }
            bool nativePending = IsPendingNativeDecision(realm, decision);
            if (proposal.Phase != expectedPhase || nativePending)
            {
                if (targetVote) proposal.TargetBallotFiled = true;
                else proposal.SourceBallotFiled = true;
                if (proposal.Phase == expectedPhase && nativePending)
                    _ballots.Enqueue(new PendingBallot { Proposal = proposal, Target = targetVote });
                return true;
            }
            if (CancelProposal(proposal) && !targetVote) RefundUnfiledSourceDecision(proposal);
            BellumCivileLogger.Log($"Voluntary pact vote did not reach a native ballot; proposal={proposal.Id}; target={targetVote}.");
            return false;
        }

        private static bool IsPendingNativeDecision(Kingdom realm, VoluntaryPactDecision decision)
            => decision != null && realm?.UnresolvedDecisions?.Contains(decision) == true;

        private void RecoverStartedVotes(CampaignGameStarter starter)
        {
            foreach (var proposal in _proposals)
            {
                if (proposal == null) continue;
                bool lostSource = proposal.Phase == VoluntaryPactProposalPhase.SourceVote && proposal.SourceFilingStarted
                    && !IsPendingNativeDecision(proposal.Source, proposal.SourceDecision);
                bool lostTarget = proposal.Phase == VoluntaryPactProposalPhase.TargetVote && proposal.TargetFilingStarted
                    && !IsPendingNativeDecision(proposal.Target, proposal.TargetDecision);
                if (!lostSource && !lostTarget) continue;
                if (CancelProposal(proposal))
                {
                    if (lostSource && !proposal.SourceBallotFiled) RefundUnfiledSourceDecision(proposal);
                    BellumCivileLogger.Log($"Cancelled interrupted voluntary pact ballot; proposal={proposal.Id}; target={lostTarget}.");
                }
            }
        }

        private void ReconcileBallots()
        {
            int count = Math.Min(_ballots.Count, 4);
            for (int i = 0; i < count; i++)
            {
                var pending = _ballots.Dequeue();
                var proposal = pending?.Proposal;
                var phase = pending?.Target == true ? VoluntaryPactProposalPhase.TargetVote : VoluntaryPactProposalPhase.SourceVote;
                if (proposal == null || proposal.Phase != phase) continue;
                var realm = pending.Target ? proposal.Target : proposal.Source;
                var decision = pending.Target ? proposal.TargetDecision : proposal.SourceDecision;
                if (IsPendingNativeDecision(realm, decision)) { _ballots.Enqueue(pending); continue; }
                if (CancelProposal(proposal) && !pending.Target && !proposal.SourceBallotFiled)
                    RefundUnfiledSourceDecision(proposal);
            }
        }

        private static void RefundUnfiledSourceDecision(VoluntaryPactProposalRecord proposal)
        {
            var clan = proposal?.Sponsor;
            var decision = proposal?.SourceDecision;
            if (clan == null || decision == null || proposal.SourceFilingRefundStarted || decision.ProposerClan != clan)
                return;
            float before = proposal.SourceInfluenceBeforeFiling;
            float after = clan.Influence;
            int cost = decision.GetProposalInfluenceCost();
            if (float.IsNaN(before) || float.IsInfinity(before) || float.IsNaN(after) || float.IsInfinity(after)
                || cost <= 0 || Math.Abs(after - (before - cost)) > .001f)
            {
                BellumCivileLogger.Log($"Unfiled voluntary pact influence could not be verified; proposal={proposal.Id}; before={before}; after={after}.");
                return;
            }
            proposal.SourceFilingRefundStarted = true;
            try { ChangeClanInfluenceAction.Apply(clan, cost); }
            catch (Exception ex)
            {
                BellumCivileLogger.Log($"Unfiled voluntary pact influence refund interrupted; proposal={proposal.Id}; error={ex}");
            }
        }

        private static bool IsPending(VoluntaryPactProposalRecord proposal)
            => proposal != null && proposal.Phase != VoluntaryPactProposalPhase.Completed
                && proposal.Phase != VoluntaryPactProposalPhase.Cancelled;

        private static bool SamePair(VoluntaryPactProposalRecord proposal, Kingdom first, Kingdom second)
            => proposal != null && ((proposal.Source == first && proposal.Target == second)
                || (proposal.Source == second && proposal.Target == first));

        private void RecoverDeliveries()
        {
            if (_deliveries.Count == 0) return;
            var custody = Campaign.Current?.GetCampaignBehavior<HostagePactBehavior>();
            if (custody == null) return;
            int count = Math.Min(_deliveries.Count, 4);
            for (int i = 0; i < count; i++)
            {
                var proposal = _deliveries.Dequeue();
                if (proposal?.Phase != VoluntaryPactProposalPhase.Delivery) continue;
                try { custody.DeliverVoluntaryProposal(proposal); }
                catch (Exception ex)
                {
                    BellumCivileLogger.Log($"Voluntary pact delivery deferred; proposal={proposal.Id}; error={ex}");
                }
                if (proposal.Phase == VoluntaryPactProposalPhase.Completed || proposal.Phase == VoluntaryPactProposalPhase.Cancelled)
                    QueueOutcome(proposal);
                if (proposal.Phase == VoluntaryPactProposalPhase.Delivery) _deliveries.Enqueue(proposal);
            }
        }

        private void AdvanceTargetChoices()
        {
            int count = Math.Min(_targetChoices.Count, 4);
            for (int i = 0; i < count; i++)
            {
                var proposal = _targetChoices.Dequeue();
                if (proposal?.Phase != VoluntaryPactProposalPhase.TargetChoice) continue;
                try
                {
                    if (!IsBallotEligible(proposal)) { TryCancelInvalidBallot(proposal.Id); continue; }
                    if (IsPlayerRulingHouse(proposal))
                    {
                        _targetChoices.Enqueue(proposal);
                        OfferPlayerTargetChoice(proposal);
                        continue;
                    }
                    var candidate = TreatyHostageEligibility.GetCandidates(proposal.Target)
                        .Where(c => c.Hero != null && c.Hero != Hero.MainHero && c.Hero != proposal.SourceHostage)
                        .OrderByDescending(c => c.Tier).ThenBy(c => c.Hero.StringId, StringComparer.Ordinal).FirstOrDefault();
                    if (candidate == null || !TryChooseTargetHostage(proposal.Id, candidate.Hero))
                    { CancelProposal(proposal); continue; }
                    if (!TryFileDecision(proposal.Id, true) && !proposal.TargetFilingStarted) CancelProposal(proposal);
                }
                catch (Exception ex)
                {
                    BellumCivileLogger.Log($"Voluntary pact target choice deferred; proposal={proposal.Id}; error={ex}");
                    if (proposal.Phase == VoluntaryPactProposalPhase.TargetChoice) _targetChoices.Enqueue(proposal);
                    else if (proposal.Phase == VoluntaryPactProposalPhase.TargetVote && !proposal.TargetFilingStarted)
                        CancelProposal(proposal);
                }
            }
        }

        private static bool IsPlayerRulingHouse(VoluntaryPactProposalRecord proposal)
            => Campaign.Current != null && proposal?.TargetHouse == Clan.PlayerClan;

        private void OfferPlayerTargetChoice(VoluntaryPactProposalRecord proposal)
        {
            var map = Game.Current?.GameStateManager?.ActiveState as MapState;
            if (_targetInquiryOpen || InformationManager.IsAnyInquiryActive() || map == null || map.AtMenu
                || map.MapConversationActive || map.NextIncident != null || PlayerEncounter.Current != null
                || Hero.MainHero?.IsPrisoner == true || Campaign.Current?.CurrentMenuContext != null) return;
            var choices = TreatyHostageEligibility.GetCandidates(proposal.Target)
                .Where(c => c.Hero != null && c.Hero != Hero.MainHero && c.Hero != proposal.SourceHostage)
                .Select(c => new InquiryElement(c.Hero, c.Hero.Name.ToString(), null)).ToList();
            choices.Add(new InquiryElement("decline", new TextObject("{=BC_VoluntaryPact_DeclineOffer}Decline the proposal").ToString(), null));
            var body = new TextObject("{=BC_VoluntaryPact_ChooseHostage}{SOURCE} proposes a pledge of peace for {DAYS} days, offering {HOSTAGE} as a hostage and {GOLD} denars to your house. Choose a relative to offer in return, or decline the proposal.");
            body.SetTextVariable("SOURCE", proposal.Source.Name);
            body.SetTextVariable("DAYS", proposal.DurationDays);
            body.SetTextVariable("HOSTAGE", proposal.SourceHostage.Name);
            body.SetTextVariable("GOLD", proposal.Compensation);
            _targetInquiryOpen = true;
            try
            {
                MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                    new TextObject("{=BC_VoluntaryPact_ChooseHostageTitle}A Pledge of Peace").ToString(), body.ToString(), choices,
                    false, 1, 1, new TextObject("{=BC_VoluntaryPact_ConfirmChoice}Confirm").ToString(), null,
                    selected =>
                    {
                        _targetInquiryOpen = false;
                        if (selected?.Count != 1 || !IsPlayerRulingHouse(proposal)
                            || proposal.Phase != VoluntaryPactProposalPhase.TargetChoice) return;
                        if (!(selected[0].Identifier is Hero hostage)) { CancelProposal(proposal, VoluntaryPactCloseReason.TargetDeclined); return; }
                        ApplyPlayerTargetChoice(proposal, hostage);
                    }, null), true);
            }
            catch { _targetInquiryOpen = false; throw; }
        }

        private bool ApplyPlayerTargetChoice(VoluntaryPactProposalRecord proposal, Hero hostage)
        {
            if (!IsPlayerRulingHouse(proposal) || !TryChooseTargetHostage(proposal.Id, hostage)) return false;
            try
            {
                if (TryFileDecision(proposal.Id, true)) return true;
            }
            catch (Exception ex)
            {
                BellumCivileLogger.Log($"Voluntary pact player choice could not be filed; proposal={proposal.Id}; error={ex}");
            }
            if (!proposal.TargetFilingStarted) CancelProposal(proposal);
            return false;
        }

        private bool CancelProposal(VoluntaryPactProposalRecord proposal, VoluntaryPactCloseReason reason = VoluntaryPactCloseReason.Invalid)
        {
            if (proposal?.TryCancel() != true) return false;
            proposal.CloseReason = (int)reason;
            QueueOutcome(proposal);
            return true;
        }

        private void QueueOutcome(VoluntaryPactProposalRecord proposal)
        {
            if (proposal == null) return;
            if (!proposal.RefusalCooldownRecorded && proposal.Phase == VoluntaryPactProposalPhase.Cancelled
                && proposal.CloseReason >= (int)VoluntaryPactCloseReason.SourceRejected
                && proposal.CloseReason <= (int)VoluntaryPactCloseReason.TargetDeclined)
            {
                string key = PairKey(proposal.Source, proposal.Target);
                double now = CampaignTime.Now.ToDays;
                if (key != null && !double.IsNaN(now) && !double.IsInfinity(now))
                {
                    proposal.RefusalCooldownRecorded = true;
                    _refusalUntil[key] = now + PeacetimeHostagePactRules.RefusalCooldownDays;
                }
            }
            if (!proposal.OutcomeReported) _outcomes.Enqueue(proposal);
        }

        private static string PairKey(Kingdom first, Kingdom second)
        {
            string a = first?.StringId, b = second?.StringId;
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b) || first == second) return null;
            if (string.CompareOrdinal(a, b) > 0) { string swap = a; a = b; b = swap; }
            return a.Length + ":" + a + b;
        }

        internal bool TryGetRefusalCooldown(Kingdom first, Kingdom second, out int remainingDays)
        {
            remainingDays = 0;
            string key = PairKey(first, second);
            if (key == null || !_refusalUntil.TryGetValue(key, out double until)) return false;
            double now = CampaignTime.Now.ToDays;
            if (double.IsNaN(until) || double.IsInfinity(until) || double.IsNaN(now) || double.IsInfinity(now)) return false;
            if (until <= now) { _refusalUntil.Remove(key); return false; }
            remainingDays = (int)Math.Min(int.MaxValue, Math.Ceiling(until - now));
            return true;
        }

        private void ReportOutcomes()
        {
            int count = Math.Min(_outcomes.Count, 4);
            for (int i = 0; i < count; i++)
            {
                var proposal = _outcomes.Dequeue();
                if (proposal == null || proposal.OutcomeReported) continue;
                proposal.OutcomeReported = true;
                var playerRealm = Campaign.Current == null ? null : Clan.PlayerClan?.Kingdom;
                if (!proposal.SourceBallotFiled || (playerRealm != proposal.Source && playerRealm != proposal.Target))
                { _proposals.Remove(proposal); continue; }
                var text = new TextObject(proposal.Phase == VoluntaryPactProposalPhase.Completed
                    ? "{=BC_VoluntaryPact_ResultSigned}{SOURCE} and {TARGET} have exchanged hostages and pledged peace for {DAYS} days."
                    : proposal.CloseReason == (int)VoluntaryPactCloseReason.SourceRejected
                    ? "{=BC_VoluntaryPact_ResultSourceRejected}The council of {SOURCE} rejected the proposed pledge with {TARGET}."
                    : proposal.CloseReason == (int)VoluntaryPactCloseReason.TargetRejected
                    ? "{=BC_VoluntaryPact_ResultTargetRejected}The council of {TARGET} rejected the proposed pledge with {SOURCE}."
                    : proposal.CloseReason == (int)VoluntaryPactCloseReason.TargetDeclined
                    ? "{=BC_VoluntaryPact_ResultDeclined}The ruler of {TARGET} declined the proposed pledge with {SOURCE}."
                    : "{=BC_VoluntaryPact_ResultInvalid}The proposed pledge between {SOURCE} and {TARGET} could no longer be concluded.");
                text.SetTextVariable("SOURCE", proposal.Source?.Name ?? TextObject.GetEmpty());
                text.SetTextVariable("TARGET", proposal.Target?.Name ?? TextObject.GetEmpty());
                text.SetTextVariable("DAYS", proposal.DurationDays);
                try { InformationManager.DisplayMessage(new InformationMessage(text.ToString())); }
                catch (Exception ex) { BellumCivileLogger.Log($"Voluntary pact result message failed; proposal={proposal.Id}; error={ex}"); }
                _proposals.Remove(proposal);
            }
        }
    }
}
