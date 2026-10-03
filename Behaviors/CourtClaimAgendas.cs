using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Localization;

namespace BellumCivile.Behaviors
{
    public sealed partial class CourtAgendaBehavior
    {
        private List<CourtAgendaRecord> _claimGrace = new List<CourtAgendaRecord>();
        private static bool IsClaimObjective(CourtAgendaRecord agenda) => agenda?.ObjectiveData?.Kind == CourtClaimRules.Kind;
        private IEnumerable<CourtAgendaRecord> PendingClaims() => _agendas.Concat(_claimGrace).Where(a =>
            IsClaimObjective(a) && !a.ResultApplied && (a.IsOngoingObjective || a.State == CourtAgendaState.Announced)).Distinct();

        internal bool HasPendingClaimObjective(Kingdom realm, Settlement fief, Clan beneficiary) => PendingClaims().Any(a =>
            a.Realm == realm && a.Claim?.Fief == fief && a.Claim.Beneficiary == beneficiary);

        private static TextObject ClaimLabel(Settlement fief, Clan beneficiary) => new TextObject("{=BC_CourtRestoreClaim}Uphold {HOUSE}'s claim to {FIEF}")
            .SetTextVariable("HOUSE", beneficiary?.Name ?? TextObject.GetEmpty()).SetTextVariable("FIEF", fief?.Name ?? TextObject.GetEmpty());

        private static bool ClaimOwnerValid(CourtAgendaRecord agenda) => ValidRealm(agenda.Realm)
            && agenda.Faction?.Type == FactionType.Nobility && agenda.Faction.ParentKingdom == agenda.Realm
            && Campaign.Current?.GetCampaignBehavior<FactionManagerBehavior>()?.GetFactionsInKingdom(agenda.Realm).Contains(agenda.Faction) == true;

        private static bool ClaimBeneficiaryValid(CourtAgendaRecord agenda) => Eligible(agenda.Claim?.Beneficiary, agenda.Realm)
            && agenda.Claim.Beneficiary != agenda.Realm.RulingClan && agenda.Faction.Members.Contains(agenda.Claim.Beneficiary);

        private CourtAgendaRecord ActiveClaim(Kingdom realm, Kingdom target) => _agendas.FirstOrDefault(a =>
            IsClaimObjective(a) && a.Realm == realm && !a.ResultApplied && a.IsOngoingObjective
            && a.ObjectiveData.HasTermSnapshot && a.Claim?.Target == target && ClaimOwnerValid(a) && ClaimBeneficiaryValid(a)
            && a.Claim.InTerm(CampaignTime.Now.ToDays, a.ObjectiveData.DeadlineDay)
            && CourtClaimObjectiveSource.ValidClaim(a.Claim)
            && CourtCampaignObjectiveSource.ValidPair(realm, target)
            && ClientKingdomBehavior.Instance?.IsClientKingdom(target) != true);

        internal float ClaimWarBonus(Kingdom realm, Kingdom target, Clan voter)
        {
            var agenda = ActiveClaim(realm, target);
            return agenda != null && agenda.Claim.Fief.OwnerClan?.Kingdom == target
                && ReceivesPoliticalSupport(agenda, voter, agenda.Claim.Members)
                && WarPeaceRevampBehavior.CanSupportCourtCampaign(realm, target) ? CourtClaimRules.SupportBonus : 0;
        }

        private static CourtTreatyDraftPreference ClaimPreference(CourtAgendaRecord agenda) => new CourtTreatyDraftPreference(
            TreatyTermType.TransferFief, agenda.Claim.Target.StringId, agenda.Realm.StringId, 0, CourtClaimRules.PackageBonus,
            "Crown supports the court's named territorial claim", agenda.Claim.Fief.StringId);

        internal float ClaimTreatyBonus(Kingdom realm, WarScoreRecord war, Clan voter, IEnumerable<TreatyTermRecord> terms)
        {
            var target = realm == null || war == null ? null : Campaign.Current?.GetCampaignBehavior<WarScoreBehavior>()?.GetOpposingKingdom(war, realm);
            if (!CourtPeaceObjectiveSource.EligibleWar(realm, target, war)) return 0;
            var agenda = ActiveClaim(realm, target);
            return agenda != null && ReceivesPoliticalSupport(agenda, voter, agenda.Claim.Members)
                && ClaimPreference(agenda).Score(terms) != 0 ? CourtClaimRules.SupportBonus : 0;
        }

        private CourtTreatyDraftPreference CrownClaimPreference(Kingdom realm, Kingdom target)
        {
            var agenda = ActiveClaim(realm, target);
            return agenda != null && ReceivesPoliticalSupport(agenda, realm.RulingClan, agenda.Claim.Members) ? ClaimPreference(agenda) : null;
        }

        internal float ClaimAllocationBonus(Kingdom realm, Settlement fief, Clan candidate, Clan voter)
        {
            return PendingClaims().Any(a => a.Realm == realm && a.Claim?.Fief == fief && a.Claim.Beneficiary == candidate
                && a.ObjectiveData.HasTermSnapshot && ClaimOwnerValid(a) && ClaimBeneficiaryValid(a)
                && fief.OwnerClan?.Kingdom == realm && CourtClaimObjectiveSource.ValidClaim(a.Claim)
                && a.Claim.AllocationWindow(CampaignTime.Now.ToDays, a.ObjectiveData.DeadlineDay)
                && ReceivesPoliticalSupport(a, voter, a.Claim.Members)) ? CourtClaimRules.SupportBonus : 0;
        }

        private void OnCourtClaimOwnerChanged(Settlement fief, bool openToClaim, Hero newOwner, Hero oldOwner,
            Hero capturer, ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail detail)
        {
            foreach (var agenda in PendingClaims().Where(a => a.Claim?.Fief == fief).ToList())
            {
                var plan = agenda.Claim;
                double now = CampaignTime.Now.ToDays;
                if (newOwner?.Clan != oldOwner?.Clan) plan.LastOwnerChangeDay = now;
                if (newOwner?.Clan?.Kingdom != agenda.Realm)
                {
                    plan.AcquiredDay = -1;
                    plan.PendingAllocation = false;
                    if (plan.GraceActive) FinishClaimObjective(agenda, CourtObjectiveState.Expired, "fief_lost_during_allocation_grace");
                }
                else
                {
                    if (plan.AcquiredDay < 0 && now <= agenda.ObjectiveData.DeadlineDay) plan.AcquiredDay = now;
                    plan.PendingAllocation |= openToClaim || FiefDeliberationBehavior.IsAwaitingAllocation(fief);
                }
            }
        }

        internal void OnCourtClaimAllocationResolved(Settlement fief, Clan winner)
        {
            if (winner == null || fief?.OwnerClan != winner) return;
            foreach (var agenda in PendingClaims().Where(a => a.Claim?.Fief == fief).ToList())
            {
                agenda.Claim.LastOwnerChangeDay = CampaignTime.Now.ToDays;
                if (CampaignTime.Now.ToDays > agenda.ObjectiveData.DeadlineDay && winner != agenda.Claim.Beneficiary)
                    FinishClaimObjective(agenda, CourtObjectiveState.Expired, "allocation_awarded_to_another_house");
            }
            // The native decision may still be in the unresolved list here. Hourly maintenance checks its final cleanup.
        }

        private static bool SettledClaimOwnership(CourtAgendaRecord agenda)
        {
            var plan = agenda.Claim;
            var titles = Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>();
            return plan.Fief?.OwnerClan == plan.Beneficiary && !FiefDeliberationBehavior.IsAwaitingAllocation(plan.Fief)
                && titles?.TryGetBarony(plan.Fief, out var title) == true && title.DeFactoHolderClanId == plan.Beneficiary.StringId;
        }

        private void MaintainClaimObjectives()
        {
            foreach (var agenda in PendingClaims().ToList())
            {
                var plan = agenda.Claim;
                if (!ClaimOwnerValid(agenda) || plan?.Fief == null || !agenda.ObjectiveData.HasTermSnapshot || !ClaimBeneficiaryValid(agenda))
                { FinishClaimObjective(agenda, CourtObjectiveState.Cancelled, "owner_or_plan_invalid"); continue; }
                double now = CampaignTime.Now.ToDays, deadline = agenda.ObjectiveData.DeadlineDay;
                bool ours = plan.Fief.OwnerClan?.Kingdom == agenda.Realm;
                bool pending = ours && FiefDeliberationBehavior.IsAwaitingAllocation(plan.Fief);
                if (ours && now <= deadline && plan.AcquiredDay < 0) plan.AcquiredDay = now;
                bool graceEligible = CourtClaimRules.CanGrace(plan.AcquiredDay, agenda.ObjectiveData.SelectedDay,
                    deadline, plan.PendingAllocation || pending, now, plan.GraceDays);
                // Restoration may consume the legal claim; settle the dated ownership receipt before testing it again.
                bool inTime = CourtAgendaRules.ObjectiveInWindow(plan.LastOwnerChangeDay, agenda.ObjectiveData.SelectedDay, deadline)
                    || ((plan.GraceActive || graceEligible) && plan.LastOwnerChangeDay > deadline
                        && plan.LastOwnerChangeDay <= CourtClaimRules.GraceEnd(deadline, plan.GraceDays));
                if (inTime && SettledClaimOwnership(agenda))
                { FinishClaimObjective(agenda, CourtObjectiveState.Succeeded, "claimant_received_fief"); continue; }
                if (plan.GraceActive && !ours)
                { FinishClaimObjective(agenda, CourtObjectiveState.Expired, "fief_lost_during_allocation_grace"); continue; }
                if (!CourtClaimObjectiveSource.ValidClaim(plan))
                { FinishClaimObjective(agenda, CourtObjectiveState.Cancelled, "claim_no_longer_valid"); continue; }
                if (!WarPeaceRevampBehavior.IsRevampEnabled() || (!ours && (plan.Fief.OwnerClan?.Kingdom != plan.Target
                    || !CourtCampaignObjectiveSource.ValidPair(agenda.Realm, plan.Target)
                    || ClientKingdomBehavior.Instance?.IsClientKingdom(plan.Target) == true)))
                { FinishClaimObjective(agenda, CourtObjectiveState.Cancelled, "foreign_context_changed"); continue; }
                if (now > deadline)
                {
                    if (!ours || now > CourtClaimRules.GraceEnd(deadline, plan.GraceDays) || (!plan.GraceActive && !graceEligible)
                        || !pending && plan.Fief.OwnerClan != plan.Beneficiary)
                    { FinishClaimObjective(agenda, CourtObjectiveState.Expired, "claim_unfulfilled"); continue; }
                    if (!plan.GraceActive)
                    {
                        plan.GraceActive = true;
                        plan.GraceUntil = CourtClaimRules.GraceEnd(deadline, plan.GraceDays);
                        _agendas.Remove(agenda);
                        _claimGrace.Add(agenda);
                        ReportClaim(agenda, new TextObject("{=BC_CourtClaimGrace}With {FIEF} now in the realm's hands, the {FACTION} awaits the judgment of the court. Its pledge to {HOUSE} will stand until {DATE}, while new business proceeds in the coming term."));
                    }
                }
                else if (!ours) { plan.AcquiredDay = -1; plan.PendingAllocation = false; }
                if (ours) plan.PendingAllocation = pending;
                plan.Members?.RemoveAll(c => !Eligible(c, agenda.Realm) || !agenda.Faction.Members.Contains(c));
            }
            _claimGrace.RemoveAll(a => a.ResultApplied);
        }

        private void AdvanceClaimObjective(CourtAgendaRecord agenda)
        {
            if (agenda.ResultApplied || agenda.IsOngoingObjective || !agenda.SessionDate.IsPast) return;
            MaintainClaimObjectives();
            if (agenda.ResultApplied) return;
            var plan = agenda.Claim;
            plan.Members = agenda.Faction.Members.Where(c => Eligible(c, agenda.Realm) && c != agenda.Realm.RulingClan).Distinct().ToList();
            plan.Activated = true;
            plan.ActivatedDay = CampaignTime.Now.ToDays;
            agenda.ObjectiveData.Activate();
            agenda.State = CourtAgendaState.PursuingObjective;
            ReportClaim(agenda, new TextObject("{=BC_CourtClaimBegun}The {FACTION} of {REALM} takes up {HOUSE}'s claim to {FIEF}. Its lords pledge their voices in council and at the peace table, expecting the house to receive its rightful holding before {DATE}."));
        }

        private static void ReportClaim(CourtAgendaRecord agenda, TextObject text, CourtObjectiveState? result = null, float mood = 0)
        {
            text.SetTextVariable("FACTION", agenda.Faction?.GetDisplayName() ?? TextObject.GetEmpty())
                .SetTextVariable("REALM", agenda.Realm?.Name ?? TextObject.GetEmpty())
                .SetTextVariable("HOUSE", agenda.Claim?.Beneficiary?.Name ?? TextObject.GetEmpty())
                .SetTextVariable("FIEF", agenda.Claim?.Fief?.Name ?? TextObject.GetEmpty())
                .SetTextVariable("DATE", CampaignTime.Days((float)(agenda.Claim?.GraceActive == true ? agenda.Claim.GraceUntil : agenda.ObjectiveData.DeadlineDay)).ToString());
            if (result.HasValue) text = CourtObjectiveReports.WithMood(text, agenda.Faction?.GetDisplayName() ?? TextObject.GetEmpty(), mood, result.Value);
            BellumCivileNotifications.Show(text, CourtObjectiveReports.Color(result), primaryKingdom: agenda.Realm);
        }

        private void FinishClaimObjective(CourtAgendaRecord agenda, CourtObjectiveState result, string reason)
        {
            if (agenda.ResultApplied) return;
            bool success = result == CourtObjectiveState.Succeeded, failure = result == CourtObjectiveState.Expired;
            var credit = success ? (agenda.Claim?.Activated == true ? CourtObjectiveCredit.Sponsor : CourtObjectiveCredit.FulfilledElsewhere) : CourtObjectiveCredit.None;
            if (!agenda.ObjectiveData.Finish(result, credit, reason) || !agenda.ObjectiveData.TryClaimResult()) return;
            agenda.ResultApplied = agenda.PaymentSettled = true;
            agenda.State = success ? CourtAgendaState.Completed : failure ? CourtAgendaState.NotProposed : CourtAgendaState.Cancelled;
            float shock = success ? BellumCivileConstants.CourtAgendaSuccessShock : failure ? BellumCivileConstants.CourtAgendaFailureShock : 0;
            float actual = 0;
            if (ClaimOwnerValid(agenda) && shock != 0)
            {
                float before = agenda.Faction.Mood;
                agenda.Faction.Mood = Math.Max(-100, Math.Min(100, before + shock));
                actual = agenda.Faction.Mood - before;
                RecordResultHistory(agenda, shock);
            }
            ReportClaim(agenda, new TextObject(success
                ? "{=BC_CourtClaimSucceeded}{HOUSE} has received {FIEF}. The lords of the {FACTION} praise the Crown for honoring the house's claim, their confidence in its protection of noble rights renewed."
                : failure ? "{=BC_CourtClaimFailed}The appointed time has passed, yet {HOUSE} has not received {FIEF}. The lords of the {FACTION} reproach the Crown for leaving the house's claim unfulfilled."
                : "{=BC_CourtClaimCancelled}Changed circumstances have overtaken the {FACTION}'s undertaking on behalf of {HOUSE} at {FIEF}. Its lords set the matter aside without blame."), result, actual);
            BellumCivileLogger.Log($"Court claim concluded; realm={agenda.Realm?.StringId}; fief={agenda.Claim?.Fief?.StringId}; beneficiary={agenda.Claim?.Beneficiary?.StringId}; result={result}; actual_mood={actual}; reason={reason}.");
        }

        private static TextObject ClaimStatus(CourtAgendaRecord agenda) => new TextObject(agenda.ResultApplied
            ? agenda.State == CourtAgendaState.Completed ? "{=BC_CourtClaimComplete}{HOUSE} received settled ownership of {FIEF} within the allotted time."
                : agenda.ObjectiveData.State == CourtObjectiveState.Expired ? "{=BC_CourtClaimExpired}The pledge to secure {FIEF} for {HOUSE} went unfulfilled."
                : "{=BC_CourtClaimClosed}The undertaking on behalf of {HOUSE} at {FIEF} has been set aside."
            : "{=BC_CourtClaimStatus}{HOUSE}'s claim to {FIEF} must be fulfilled by {DATE}. Participating houses and an NPC Crown favoring Nobility receive +15 support for the campaign, a treaty securing this fief, and its allocation to the claimant. Conquest alone does not fulfill the pledge. A pending allocation may receive one short grace period; only allocation support continues then.")
            .SetTextVariable("HOUSE", agenda.Claim?.Beneficiary?.Name ?? TextObject.GetEmpty()).SetTextVariable("FIEF", agenda.Claim?.Fief?.Name ?? TextObject.GetEmpty())
            .SetTextVariable("DATE", CampaignTime.Days((float)agenda.ObjectiveData.DeadlineDay).ToString());

        internal TextObject ClaimHint(Kingdom realm, FactionObject faction, TextObject current)
        {
            var agenda = GetDisplayedAgenda(realm, faction);
            if (IsClaimObjective(agenda) && (agenda.IsOngoingObjective || agenda.State == CourtAgendaState.Announced)) current = ClaimStatus(agenda);
            foreach (var grace in _claimGrace.Where(a => a.Realm == realm && a.Faction == faction && !a.ResultApplied))
            {
                var extra = new TextObject("{=BC_CourtClaimGraceHint}Previous pledge: {HOUSE}'s claim to {FIEF} awaits allocation until {DATE}. Only allocation support remains in effect.")
                    .SetTextVariable("HOUSE", grace.Claim.Beneficiary.Name).SetTextVariable("FIEF", grace.Claim.Fief.Name)
                    .SetTextVariable("DATE", CampaignTime.Days((float)grace.Claim.GraceUntil).ToString());
                current = current == null ? extra : new TextObject("{=BC_CourtClaimCombinedHint}{CURRENT}\n\n{GRACE}").SetTextVariable("CURRENT", current).SetTextVariable("GRACE", extra);
            }
            return current;
        }
    }
}
