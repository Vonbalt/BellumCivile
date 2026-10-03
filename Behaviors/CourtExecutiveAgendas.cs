using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Election;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Localization;

namespace BellumCivile.Behaviors
{
    public sealed partial class CourtAgendaBehavior
    {
        private List<CourtDecreeCase> _decreeCases = new List<CourtDecreeCase>();

        private static Clan ExecutiveTargetClan(CourtAgendaRecord agenda) => Clan.All.FirstOrDefault(c => c.StringId == agenda.ObjectiveData?.TargetId);
        private static Settlement ExecutiveSettlement(CourtAgendaRecord agenda) => Settlement.All.FirstOrDefault(s => s.StringId == agenda.ObjectiveData?.TargetId);
        private static TextObject AgendaOwnerName(CourtAgendaRecord agenda) => agenda.Faction?.GetDisplayName() ?? new TextObject("{=BC_CrownName}Crown");

        private static TextObject ExecutiveObjectiveText(CourtAgendaRecord agenda)
        {
            if (IsClientGrant(agenda)) return ClientGrantLabel(agenda.ClientGrant?.Fief, agenda.ClientGrant?.Client);
            if (IsRoyalPeace(agenda)) return RoyalPeaceLabel(agenda);
            if (IsCampaignObjective(agenda)) return CampaignLabel(agenda.Campaign?.Target ?? CourtCampaignObjectiveSource.Target(agenda.ObjectiveData.TargetId));
            if (IsSubjugationObjective(agenda)) return SubjugationLabel(agenda.Subjugation?.Target ?? CourtCampaignObjectiveSource.Target(agenda.ObjectiveData.TargetId));
            if (IsClaimObjective(agenda)) return ClaimLabel(agenda.Claim?.Fief, agenda.Claim?.Beneficiary);
            if (IsDynastic(agenda)) return DynasticLabel(agenda.Dynastic?.Target);
            if (IsTrade(agenda)) return TradeLabel(agenda.Trade?.Target);
            if (IsTitleGrant(agenda)) return TitleGrantLabel(agenda.TitleGrant?.TitleId, agenda.TitleGrant?.Recipient, agenda.Faction != null);
            if (IsRally(agenda)) return RallyLabel(agenda.Rally?.Target);
            if (IsLiberation(agenda)) return LiberationLabel(agenda.Liberation?.Suzerain);
            if (IsMandate(agenda)) return MandateLabel(agenda.Mandate?.NewLaw);
            if (IsProtection(agenda)) return ProtectionLabel(agenda.Protection?.Protector, agenda.Protection?.Threat);
            if (IsPeaceObjective(agenda)) return PeaceLabel(agenda.Peace?.Target ?? CourtPeaceObjectiveSource.Target(agenda.ObjectiveData.TargetId));
            if (IsAppeasement(agenda)) return AppeasementLabel(agenda.Appeasement?.Target);
            if (IsActivity(agenda)) return ActivityObjectiveText(agenda);
            if (IsCouncil(agenda)) return CouncilObjectiveText(agenda);
            string kind = agenda.ObjectiveData.Kind;
            var text = new TextObject(kind == CourtExecutiveRules.Decree ? "{=BC_CourtDecreeObjective}Decree judgment upon {TARGET}"
                : kind == CourtExecutiveRules.Treason ? "{=BC_CourtTreasonObjective}Indict {TARGET} for treason"
                : kind == CourtExecutiveRules.Grant ? "{=BC_CourtGrantObjective}Grant {TARGET}"
                : "{=BC_CourtRevokeObjective}Revoke {TARGET}");
            text.SetTextVariable("TARGET", kind == CourtExecutiveRules.Treason || kind == CourtExecutiveRules.Decree
                ? ExecutiveTargetClan(agenda)?.Name ?? new TextObject(agenda.ObjectiveData.TargetId)
                : ExecutiveSettlement(agenda)?.Name ?? new TextObject(agenda.ObjectiveData.TargetId));
            return text;
        }

        private void ScanDecreeCases(Kingdom realm, IdeologyBehavior ideology)
        {
            if (realm.RulingClan == Clan.PlayerClan) return;
            var ruler = realm.RulingClan.Leader;
            _decreeCases.RemoveAll(c => c.Realm == null || c.Realm.IsEliminated
                || c.Realm == realm && (c.Ruler != ruler || !Eligible(c.Accused, realm)
                    || c.Closed && !ideology.CanScheduleTreasonDecree(realm, c.Accused)));
            foreach (var entry in _decreeCases.Where(c => c.Realm == realm && !c.Closed && c.Assigned))
                if (!_agendas.Any(a => a.DecreeCaseId == entry.Id && (a.IsUnopened || a.IsFiled))) entry.Closed = true;
            foreach (var entry in _decreeCases.Where(c => c.Realm == realm && !c.Closed && !c.Assigned))
                if (!ideology.CanScheduleTreasonDecree(realm, entry.Accused)) entry.Closed = true;

            // The toggle gates new cases, never maintenance or execution of recorded proceedings.
            if (!BellumCivileOptions.EnableAutomaticTreasonIndictments) return;
            foreach (var target in realm.Clans.Where(c => ideology.CanScheduleTreasonDecree(realm, c)).ToList())
            {
                string id = realm.StringId + "|" + ruler.StringId + "|" + target.StringId;
                if (_decreeCases.Any(c => c.Id == id)) continue;
                var entry = new CourtDecreeCase { Id = id, Realm = realm, Accused = target, Ruler = ruler };
                _decreeCases.Add(entry);
                var agenda = GetAgenda(realm, null);
                bool otherCrownMotion = _agendas.Any(a => a.Realm == realm && a.Sponsor == realm.RulingClan && a.IsFiled);
                if (!otherCrownMotion && agenda != null && !IsRoyalPeace(agenda) && agenda.ObjectiveData?.Kind != CourtExecutiveRules.Decree
                    && CourtExecutiveRules.CanOverwrite(agenda.IsFiled, agenda.IsUnopened, agenda.SessionDate.ToDays, CampaignTime.Now.ToDays))
                    AssignDecree(agenda, entry);
                else AnnounceDeferredDecree(entry);
            }
        }

        private void AnnounceDeferredDecree(CourtDecreeCase entry)
        {
            if (entry.Announced) return;
            entry.Announced = true;
            var text = new TextObject("{=BC_CrownDecreeDeferred}The crown has reserved judgment upon the {CLAN} for its next court agenda. The present proceedings will continue undisturbed.");
            text.SetTextVariable("CLAN", entry.Accused.Name);
            BellumCivileNotifications.Show(text, BellumNotificationColors.Danger, primaryKingdom: entry.Realm, secondaryClan: entry.Accused);
        }

        private bool AssignPriorityDecree(CourtAgendaRecord agenda)
        {
            var entry = _decreeCases.Where(c => c.Realm == agenda.Realm && !c.Closed && !c.Assigned)
                .Where(c => c.Ruler == agenda.Realm.RulingClan.Leader
                    && Campaign.Current.GetCampaignBehavior<IdeologyBehavior>()?.CanScheduleTreasonDecree(c.Realm, c.Accused) == true)
                .OrderBy(c => c.Id, StringComparer.Ordinal).FirstOrDefault();
            if (entry == null) return false;
            AssignDecree(agenda, entry);
            return true;
        }

        private void AssignDecree(CourtAgendaRecord agenda, CourtDecreeCase entry)
        {
            var previous = agenda.GetObjective();
            BellumCivileLogger.Log($"Crown agenda superseded before filing; realm={agenda.Realm.StringId}; kind={previous.Kind}; target={previous.TargetId}; decree={entry.Id}.");
            agenda.ObjectiveData = new CourtObjectiveRecord { Kind = CourtExecutiveRules.Decree, TargetId = entry.Accused.StringId, ActionId = "decree" };
            if (previous.HasTermSnapshot) agenda.ObjectiveData.FreezeTerm(previous.SelectedDay, previous.DeadlineDay);
            agenda.PolicyId = null;
            agenda.State = CourtAgendaState.Announced;
            agenda.DecreeCaseId = entry.Id;
            agenda.CrisisInterventionPending = false;
            entry.Assigned = true;
            var text = new TextObject("{=BC_CrownDecreeScheduled}The crown has summoned the {CLAN} to answer for its disloyalty. On {DATE}, {RULER} will pronounce judgment by royal decree.");
            text.SetTextVariable("CLAN", entry.Accused.Name);
            text.SetTextVariable("DATE", agenda.SessionDate.ToString());
            text.SetTextVariable("RULER", entry.Ruler.Name);
            BellumCivileNotifications.Show(text, BellumNotificationColors.Danger, primaryKingdom: agenda.Realm, secondaryClan: entry.Accused);
        }

        public bool TryNominateTreason(Kingdom realm, Clan target, Clan proposer, out TextObject explanation)
        {
            if (!CanNominateTreason(realm, target, proposer, out explanation)) return false;
            var agenda = GetAgenda(realm, null);
            var source = new CourtExecutiveObjectiveSource(CourtExecutiveRules.Treason);
            source.ApplySelection(agenda, new CourtObjectiveChoice(source.Kind,
                new CourtObjectiveCandidate(target.StringId, "indict"), new CourtObjectiveEvaluation(true, true, new CourtObjectiveWeight(1), "player_nomination")));
            agenda.PlayerSelectionConfirmed = true;
            agenda.State = CourtAgendaState.Announced;
            NotifyAgenda(agenda);
            return true;
        }

        internal bool CanNominateTreason(Kingdom realm, Clan target, Clan proposer, out TextObject explanation)
        {
            explanation = new TextObject("{=BC_TreasonTermOnly}Charges of treason must be placed upon the crown's agenda before its appointed session.");
            if (!ValidRealm(realm) || proposer != realm.RulingClan || proposer != Clan.PlayerClan || target?.Leader == null) return false;
            var agenda = GetAgenda(realm, null);
            if (agenda == null || agenda.PlayerSelectionConfirmed || agenda.IsFiled || agenda.ResultApplied || !agenda.SessionDate.IsFuture
                || agenda.ObjectiveData?.Kind == CourtExecutiveRules.Decree
                || target.Leader.GetRelation(realm.RulingClan.Leader) <= -100
                || !(agenda.IsUnopened || agenda.State == CourtAgendaState.NotProposed)) return false;
            return Campaign.Current.GetCampaignBehavior<IdeologyBehavior>()?.CanRulerIndictClan(realm, target, false, out explanation, true) == true;
        }

        private KingdomDecision ExecutiveDecision(CourtAgendaRecord agenda)
        {
            if (agenda.ObjectiveData.Kind == CourtExecutiveRules.Treason)
                return ExecutiveTargetClan(agenda) is Clan target ? new ExpelClanFromKingdomDecision(agenda.Sponsor, target) : null;
            var settlement = ExecutiveSettlement(agenda);
            if (settlement == null) return null;
            return agenda.ObjectiveData.Kind == CourtExecutiveRules.Grant || agenda.AllocationStage
                ? (KingdomDecision)new SettlementClaimantDecision(agenda.Sponsor, settlement, null, agenda.OriginalHolder)
                : new SettlementClaimantPreliminaryDecision(agenda.Sponsor, settlement);
        }

        private void AdvanceExecutive(CourtAgendaRecord agenda, IdeologyBehavior ideology)
        {
            if (IsClientGrant(agenda)) { AdvanceClientGrant(agenda); return; }
            if (IsCampaignObjective(agenda)) { AdvanceCampaignObjective(agenda); return; }
            if (IsSubjugationObjective(agenda)) { AdvanceSubjugationObjective(agenda); return; }
            if (IsClaimObjective(agenda)) { AdvanceClaimObjective(agenda); return; }
            if (IsDynastic(agenda)) { AdvanceDynasticObjective(agenda); return; }
            if (IsTrade(agenda)) { AdvanceTrade(agenda); return; }
            if (IsTitleGrant(agenda)) { AdvanceTitleGrant(agenda); return; }
            if (IsRally(agenda)) { AdvanceRally(agenda); return; }
            if (IsLiberation(agenda)) { AdvanceLiberation(agenda); return; }
            if (IsMandate(agenda)) { AdvanceMandate(agenda); return; }
            if (IsProtection(agenda)) { AdvanceProtection(agenda); return; }
            if (IsPeaceObjective(agenda)) { AdvancePeaceObjective(agenda); return; }
            if (IsAppeasement(agenda)) { AdvanceAppeasement(agenda); return; }
            if (IsActivity(agenda)) { AdvanceActivity(agenda); return; }
            if (IsCouncil(agenda)) { AdvanceCouncil(agenda); return; }
            string kind = agenda.ObjectiveData.Kind;
            if (kind == CourtExecutiveRules.Decree)
            {
                var entry = _decreeCases.FirstOrDefault(c => c.Id == agenda.DecreeCaseId);
                var target = ExecutiveTargetClan(agenda);
                if (entry == null || entry.Ruler != agenda.Realm.RulingClan.Leader || !ideology.CanScheduleTreasonDecree(agenda.Realm, target))
                { Cancel(agenda, "decree_parties_or_relation_changed"); return; }
                if (!agenda.SessionDate.IsPast) return;
                if (!ideology.CanIssueTreasonDecree(agenda.Realm, target))
                {
                    entry.Assigned = false;
                    Cancel(agenda, "decree_blocked_by_rebellion");
                    AnnounceDeferredDecree(entry);
                    return;
                }
                // Seal the decree before its consequences can open inquiries or change kingdoms.
                if (!entry.TrySeal()) return;
                FinishExecutive(agenda, CourtAgendaState.Decreed, "decree_issued");
                try { ideology.IssueScheduledTreasonDecree(agenda.Realm, target); }
                catch (Exception ex) { BellumCivileLogger.Log("Scheduled decree aftermath failed; decree will not be replayed: " + ex); }
                return;
            }

            var decision = ExecutiveDecision(agenda);
            if (decision == null) { Cancel(agenda, "target_missing"); return; }
            if (agenda.IsFiled && kind != CourtExecutiveRules.Treason && ExecutiveSettlement(agenda)?.OwnerClan != agenda.OriginalHolder)
            { Cancel(agenda, "owner_changed_during_proceeding"); return; }
            if (agenda.IsUnopened)
            {
                if (!agenda.SessionDate.IsPast) return;
                var source = (CourtExecutiveObjectiveSource)_objectiveSelector.FindSource(kind);
                var evaluation = source.EvaluateCandidate(new CourtTermContext(agenda.Realm, p => false, IsDirectCrownBusiness(agenda) ? agenda : null),
                    new CourtObjectiveOwner(agenda.Faction, agenda.Sponsor), new CourtObjectiveCandidate(agenda.ObjectiveData.TargetId, agenda.ObjectiveData.ActionId));
                if (!evaluation.Eligible || (agenda.Sponsor != Clan.PlayerClan && !evaluation.Viable))
                { FinishExecutive(agenda, CourtAgendaState.NotProposed, evaluation.Reason); return; }
                if (kind != CourtExecutiveRules.Treason && ExecutiveSettlement(agenda)?.OwnerClan != agenda.OriginalHolder)
                { FinishExecutive(agenda, CourtAgendaState.Cancelled, "owner_changed_before_filing"); return; }
                if (!TryPay(agenda, decision.GetProposalInfluenceCost()))
                { FinishExecutive(agenda, CourtAgendaState.NotProposed, "unaffordable"); return; }
                agenda.State = CourtAgendaState.Deliberating;
                agenda.ObjectiveData.Activate();
                bool queued = true;
                if (kind == CourtExecutiveRules.Treason)
                    queued = ideology.FileAgendaTreasonVote(agenda.Realm, ExecutiveTargetClan(agenda), agenda.Sponsor, agenda.VoteDate);
                else if (kind == CourtExecutiveRules.Grant)
                    queued = Campaign.Current.GetCampaignBehavior<FiefDeliberationBehavior>()?.QueueAgendaSettlementVote(agenda, ExecutiveSettlement(agenda), false) == true;
                else
                {
                    var text = new TextObject("{=BC_CourtRevocationDeliberation}The {FACTION} has called upon the lords to consider taking {SETTLEMENT} from the {CLAN}. The court will deliberate and vote on {DATE}.");
                    text.SetTextVariable("FACTION", AgendaOwnerName(agenda));
                    text.SetTextVariable("SETTLEMENT", ExecutiveSettlement(agenda).Name);
                    text.SetTextVariable("CLAN", agenda.OriginalHolder.Name);
                    text.SetTextVariable("DATE", agenda.VoteDate.ToString());
                    BellumCivileNotifications.Show(text, BellumNotificationColors.Land, primaryKingdom: agenda.Realm);
                }
                if (!queued) Cancel(agenda, "queue_rejected_after_payment");
                return;
            }

            if (agenda.Realm.UnresolvedDecisions.Any(d => MatchesExecutive(agenda, d))) { agenda.State = CourtAgendaState.Voting; return; }
            if (kind == CourtExecutiveRules.Revoke && !agenda.AllocationStage)
            {
                if (ExecutiveSettlement(agenda)?.OwnerClan != agenda.OriginalHolder)
                { Cancel(agenda, "owner_changed_during_deliberation"); return; }
                if (!agenda.VoteDate.IsPast) return;
                if (agenda.Realm.UnresolvedDecisions.Any(d => d is SettlementClaimantDecision || d is SettlementClaimantPreliminaryDecision)) return;
                if (++agenda.ActionAttempts > 7) { Cancel(agenda, "not_allowed_timeout"); return; }
                try
                {
                    if (!decision.IsAllowed()) return;
                    agenda.State = CourtAgendaState.Voting;
                    IdeologyBehavior.AddDecisionAsModAction(agenda.Realm, decision);
                }
                catch (Exception ex)
                {
                    BellumCivileLogger.Log("Court revocation filing failed: " + ex);
                    if (!agenda.Realm.UnresolvedDecisions.Any(d => MatchesExecutive(agenda, d))) Cancel(agenda, "add_decision_exception");
                }
                return;
            }
            bool pending = kind == CourtExecutiveRules.Treason
                ? Campaign.Current.GetCampaignBehavior<ExpulsionDeliberationBehavior>()?.HasPendingExpulsionForTarget(agenda.Realm, ExecutiveTargetClan(agenda)) == true
                : Campaign.Current.GetCampaignBehavior<FiefDeliberationBehavior>()?.HasPendingFiefVoteForSettlement(agenda.Realm, ExecutiveSettlement(agenda)) == true;
            if (!pending && !agenda.ResultApplied) Cancel(agenda, "filed_motion_missing_from_queue_and_vote");
        }

        private static bool MatchesExecutive(CourtAgendaRecord agenda, KingdomDecision decision)
        {
            if (agenda.IsPolicy || decision.Kingdom != agenda.Realm || decision.ProposerClan != agenda.Sponsor) return false;
            string kind = agenda.ObjectiveData.Kind, id = agenda.ObjectiveData.TargetId;
            if (decision is ExpelClanFromKingdomDecision expel) return kind == CourtExecutiveRules.Treason && expel.ClanToExpel?.StringId == id;
            if (decision is SettlementClaimantPreliminaryDecision revoke) return kind == CourtExecutiveRules.Revoke && !agenda.AllocationStage && revoke.Settlement?.StringId == id;
            if (decision is SettlementClaimantDecision grant) return (kind == CourtExecutiveRules.Grant || kind == CourtExecutiveRules.Revoke && agenda.AllocationStage)
                && grant.Settlement?.StringId == id && grant.ClanToExclude == agenda.OriginalHolder;
            return false;
        }

        internal CampaignTime? ExecutiveVoteDate(Kingdom realm, string target, Clan sponsor) => _agendas.LastOrDefault(a => !a.IsPolicy
            && a.IsFiled && a.Realm == realm && a.ObjectiveData.TargetId == target && a.Sponsor == sponsor) is CourtAgendaRecord agenda
                ? (agenda.AllocationStage ? agenda.AllocationVoteDate : agenda.VoteDate) : (CampaignTime?)null;

        internal bool IsGrantElection(KingdomDecision decision) => _agendas.Any(a => a.IsFiled
            && a.ObjectiveData?.Kind == CourtExecutiveRules.Grant && MatchesExecutive(a, decision));

        internal void CancelExecutiveMotion(Kingdom realm, string target, Clan sponsor, string reason)
        {
            var agenda = _agendas.LastOrDefault(a => !a.IsPolicy && a.IsFiled && a.Realm == realm
                && a.Sponsor == sponsor && a.ObjectiveData.TargetId == target);
            if (agenda != null) Cancel(agenda, reason);
        }

        private void CancelExecutiveQueue(CourtAgendaRecord agenda)
        {
            if (agenda.Realm == null) return;
            if (IsMandate(agenda)) { ClearMandateQueue(agenda); return; }
            if (IsActivity(agenda) || IsAppeasement(agenda) || IsClientGrant(agenda)) return;
            if (IsCouncil(agenda))
            {
                if (CouncilOffice(agenda, out var office))
                    CouncilAppointmentDeliberationBehavior.Current?.CancelAgendaAppointment(agenda.Realm, office, agenda.CouncilMotionId);
                return;
            }
            if (agenda.ObjectiveData.Kind == CourtExecutiveRules.Treason)
                Campaign.Current.GetCampaignBehavior<ExpulsionDeliberationBehavior>()?.CancelAgendaVote(agenda.Realm, ExecutiveTargetClan(agenda));
            else Campaign.Current.GetCampaignBehavior<FiefDeliberationBehavior>()?.CancelAgendaVote(agenda.Realm, ExecutiveSettlement(agenda));
            foreach (var decision in agenda.Realm.UnresolvedDecisions.Where(d => MatchesExecutive(agenda, d)).ToList())
                agenda.Realm.RemoveDecision(decision);
        }

        internal void ConcludeExecutiveVote(KingdomDecision decision, bool passed)
        {
            var agenda = _agendas.LastOrDefault(a => a.IsFiled && MatchesExecutive(a, decision));
            if (agenda == null) return;
            if (decision is SettlementClaimantDecision && !passed)
            {
                Cancel(agenda, "land_transfer_unverified");
                return;
            }
            if (decision is SettlementClaimantPreliminaryDecision)
            {
                if (passed) BeginRevocationAllocation(agenda);
                else FinishExecutive(agenda, CourtAgendaState.Defeated, "revocation_rejected");
            }
            else FinishExecutive(agenda, passed ? CourtAgendaState.Passed : CourtAgendaState.Defeated, "vote_resolved");
        }

        internal bool RouteRevocationAllocation(SettlementClaimantDecision decision)
        {
            var agenda = _agendas.LastOrDefault(a => !a.IsPolicy && a.ObjectiveData.Kind == CourtExecutiveRules.Revoke
                && a.Realm == decision.Kingdom && a.ObjectiveData.TargetId == decision.Settlement?.StringId
                && a.Sponsor == decision.ProposerClan && a.IsFiled);
            if (agenda == null || !decision.IsEnforced) return false;
            if (agenda.AllocationStage) return true;
            BeginRevocationAllocation(agenda);
            return true;
        }

        private void BeginRevocationAllocation(CourtAgendaRecord agenda)
        {
            if (agenda.AllocationStage || agenda.ResultApplied) return;
            agenda.AllocationStage = true;
            agenda.State = CourtAgendaState.Deliberating;
            if (Campaign.Current.GetCampaignBehavior<FiefDeliberationBehavior>()?.QueueAgendaSettlementVote(agenda, ExecutiveSettlement(agenda), true) != true)
                Cancel(agenda, "queue_rejected_after_payment");
        }

        private void FinishExecutive(CourtAgendaRecord agenda, CourtAgendaState state, string reason)
        {
            if (agenda.ResultApplied) return;
            agenda.State = state;
            agenda.ResultApplied = true;
            agenda.PaymentSettled = true;
            bool success = state == CourtAgendaState.Passed || state == CourtAgendaState.Decreed;
            agenda.ObjectiveData.Finish(success ? CourtObjectiveState.Succeeded
                : state == CourtAgendaState.Defeated ? CourtObjectiveState.Failed : CourtObjectiveState.Cancelled,
                success ? CourtObjectiveCredit.Sponsor : CourtObjectiveCredit.None, reason);
            if (agenda.ObjectiveData.TryClaimResult() && agenda.Faction != null && (success || state == CourtAgendaState.Defeated))
            {
                float shock = success ? BellumCivileConstants.CourtAgendaSuccessShock : BellumCivileConstants.CourtAgendaFailureShock;
                agenda.Faction.Mood = Math.Max(-100, Math.Min(100, agenda.Faction.Mood + shock));
                RecordResultHistory(agenda, shock);
            }
            BellumCivileLogger.Log($"Court objective concluded; realm={agenda.Realm.StringId}; kind={agenda.ObjectiveData.Kind}; target={agenda.ObjectiveData.TargetId}; result={state}; reason={reason}.");
        }

        private void SettleClosedExecutiveObjectives()
        {
            foreach (var agenda in _agendas.Where(a => !a.IsPolicy && !a.ResultApplied && !a.IsFiled && !a.IsUnopened && !a.IsOngoingObjective))
                FinishExecutive(agenda, agenda.State, agenda.CancellationReason ?? agenda.State.ToString());
        }
    }
}
