using System;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.Localization;

namespace BellumCivile.Behaviors
{
    public sealed partial class CourtAgendaBehavior
    {
        private static bool IsCampaignObjective(CourtAgendaRecord agenda) => agenda?.ObjectiveData?.Kind == CourtCampaignRules.Kind;
        private static TextObject CampaignLabel(Kingdom target) => new TextObject("{=BC_CourtSupportCampaign}Support a campaign against {REALM}")
            .SetTextVariable("REALM", target?.Name ?? TextObject.GetEmpty());

        private static bool CampaignOwnerValid(CourtAgendaRecord agenda) => ValidRealm(agenda.Realm)
            && agenda.Faction?.ParentKingdom == agenda.Realm && agenda.Faction.Type == FactionType.Glory
            && Campaign.Current?.GetCampaignBehavior<FactionManagerBehavior>()?.GetFactionsInKingdom(agenda.Realm).Contains(agenda.Faction) == true;

        internal float CampaignInitiativeBonus(Kingdom realm, Kingdom target, Clan voter)
        {
            if (voter == null || voter == Clan.PlayerClan || !Eligible(voter, realm) || target == null) return 0;
            foreach (var agenda in _agendas)
            {
                var plan = agenda.Campaign;
                if (!IsCampaignObjective(agenda) || agenda.Realm != realm || !agenda.IsOngoingObjective || agenda.ResultApplied
                    || plan == null || !agenda.ObjectiveData.HasTermSnapshot || !CampaignOwnerValid(agenda)
                    || !ReceivesPoliticalSupport(agenda, voter, plan.Members)) continue;
                float bonus = plan.ActiveBonus(target, CampaignTime.Now.ToDays, agenda.ObjectiveData.DeadlineDay);
                if (bonus != 0 && WarPeaceRevampBehavior.CanSupportCourtCampaign(realm, target)) return bonus;
            }
            return 0;
        }

        private void MaintainCampaignObjectives()
        {
            foreach (var agenda in _agendas.Where(a => IsCampaignObjective(a) && !a.ResultApplied
                && (a.IsOngoingObjective || a.State == CourtAgendaState.Announced)).ToList())
            {
                var plan = agenda.Campaign;
                if (!CampaignOwnerValid(agenda) || plan == null || !agenda.ObjectiveData.HasTermSnapshot
                    || !CourtCampaignObjectiveSource.ValidPair(agenda.Realm, plan.Target))
                { FinishCampaignObjective(agenda, CourtObjectiveState.Cancelled, "realm_faction_or_authority_changed"); continue; }
                if (CampaignTime.Now.ToDays > agenda.ObjectiveData.DeadlineDay)
                { FinishCampaignObjective(agenda, CourtObjectiveState.Expired, "term_expired"); continue; }
                if (agenda.Realm.IsAtWarWith(plan.Target))
                { FinishCampaignObjective(agenda, CourtObjectiveState.Cancelled, "war_started_without_verified_declaration"); continue; }
                plan.Members?.RemoveAll(c => !Eligible(c, agenda.Realm) || !agenda.Faction.Members.Contains(c));
            }
        }

        private void AdvanceCampaignObjective(CourtAgendaRecord agenda)
        {
            if (agenda.ResultApplied || agenda.IsOngoingObjective || !agenda.SessionDate.IsPast) return;
            var plan = agenda.Campaign;
            if (plan == null || !CampaignOwnerValid(agenda) || !agenda.ObjectiveData.HasTermSnapshot
                || !CourtCampaignObjectiveSource.ValidPair(agenda.Realm, plan.Target) || agenda.Realm.IsAtWarWith(plan.Target))
            { FinishCampaignObjective(agenda, CourtObjectiveState.Cancelled, "unavailable_at_session"); return; }
            if (CampaignTime.Now.ToDays > agenda.ObjectiveData.DeadlineDay)
            { FinishCampaignObjective(agenda, CourtObjectiveState.Expired, "term_expired"); return; }
            // Temporary front/legal restrictions suspend assistance, not the undertaking or its deadline.
            plan.Members = agenda.Faction.Members.Where(c => Eligible(c, agenda.Realm) && c != agenda.Realm.RulingClan).Distinct().ToList();
            plan.ActivatedDay = CampaignTime.Now.ToDays;
            plan.Activated = true;
            agenda.ObjectiveData.Activate();
            agenda.State = CourtAgendaState.PursuingObjective;
            var text = new TextObject("{=BC_CourtCampaignBegun}The {FACTION} of {REALM} has called for a campaign against {TARGET}. Its houses will lend their voices to a lawful declaration, though each will still weigh the risks for itself. They expect the Crown to take up the cause before the court term ends on {DATE}.")
                .SetTextVariable("FACTION", agenda.Faction.GetDisplayName()).SetTextVariable("REALM", agenda.Realm.Name)
                .SetTextVariable("TARGET", plan.Target.Name).SetTextVariable("DATE", CampaignTime.Days((float)agenda.ObjectiveData.DeadlineDay).ToString());
            BellumCivileNotifications.Show(text, BellumNotificationColors.Politics, primaryKingdom: agenda.Realm);
            BellumCivileLogger.Log($"Court campaign initiative active; realm={agenda.Realm.StringId}; target={plan.Target.StringId}; members={plan.Members.Count}; deadline={agenda.ObjectiveData.DeadlineDay}; bonus={CourtCampaignRules.SupportBonus}.");
        }

        private void OnCourtCampaignWar(IFaction first, IFaction second, DeclareWarAction.DeclareWarDetail detail)
        {
            if (!(first is Kingdom attacker) || !(second is Kingdom defender) || !attacker.IsAtWarWith(defender)) return;
            foreach (var agenda in _agendas.Where(a => IsCampaignObjective(a) && !a.ResultApplied
                && (a.IsOngoingObjective || a.State == CourtAgendaState.Announced)).ToList())
            {
                var target = agenda.Campaign?.Target;
                bool initiated = agenda.Realm == attacker && target == defender;
                bool attacked = agenda.Realm == defender && target == attacker;
                if (!initiated && !attacked) continue;
                if (!CampaignOwnerValid(agenda) || !agenda.ObjectiveData.HasTermSnapshot
                    || !CourtCampaignObjectiveSource.ValidPair(agenda.Realm, target))
                { FinishCampaignObjective(agenda, CourtObjectiveState.Cancelled, "realm_faction_or_authority_changed"); continue; }
                // Default is also used by client propagation; only the native council-authorized cause proves this offensive.
                bool authorized = detail == DeclareWarAction.DeclareWarDetail.CausedByKingdomDecision;
                var result = CourtCampaignRules.DeclarationResult(initiated, authorized, CampaignTime.Now.ToDays,
                    agenda.ObjectiveData.SelectedDay, agenda.ObjectiveData.DeadlineDay);
                FinishCampaignObjective(agenda, result, result == CourtObjectiveState.Expired ? "declaration_after_deadline"
                    : attacked ? "target_attacked_first" : authorized ? "offensive_declared" : "war_started_outside_campaign");
            }
        }

        private void FinishCampaignObjective(CourtAgendaRecord agenda, CourtObjectiveState result, string reason)
        {
            if (agenda.ResultApplied) return;
            bool success = result == CourtObjectiveState.Succeeded, failure = result == CourtObjectiveState.Expired;
            var credit = success ? (agenda.Campaign?.Activated == true ? CourtObjectiveCredit.Sponsor : CourtObjectiveCredit.FulfilledElsewhere)
                : CourtObjectiveCredit.None;
            if (!agenda.ObjectiveData.Finish(result, credit, reason) || !agenda.ObjectiveData.TryClaimResult()) return;
            agenda.ResultApplied = agenda.PaymentSettled = true;
            agenda.State = success ? CourtAgendaState.Completed : failure ? CourtAgendaState.NotProposed : CourtAgendaState.Cancelled;
            float shock = success ? BellumCivileConstants.CourtAgendaSuccessShock : failure ? BellumCivileConstants.CourtAgendaFailureShock : 0;
            float actualMoodChange = 0;
            if (CampaignOwnerValid(agenda) && shock != 0)
            {
                float before = agenda.Faction.Mood;
                agenda.Faction.Mood = Math.Max(-100, Math.Min(100, agenda.Faction.Mood + shock));
                actualMoodChange = agenda.Faction.Mood - before;
                RecordResultHistory(agenda, shock);
            }
            var text = new TextObject(success
                ? "{=BC_CourtCampaignSucceeded}The banners of {REALM} have been raised against {TARGET}. The lords of the {FACTION} welcome the Crown's decision to take up their cause, lending their approval to the undertaking."
                : failure ? "{=BC_CourtCampaignFailed}Another court term has passed without the proposed campaign against {TARGET}. The lords of the {FACTION} in {REALM} condemn the Crown's inaction, their enthusiasm giving way to resentment."
                : reason == "target_attacked_first" ? "{=BC_CourtCampaignAttacked}{TARGET} has struck first. With {REALM} now called to defend itself, the {FACTION}'s proposed offensive has been overtaken by events and is set aside without blame."
                : "{=BC_CourtCampaignCancelled}Changed circumstances have overtaken the {FACTION}'s proposed campaign against {TARGET}. The undertaking has been set aside without blame.")
                .SetTextVariable("FACTION", agenda.Faction?.GetDisplayName() ?? TextObject.GetEmpty())
                .SetTextVariable("REALM", agenda.Realm?.Name ?? TextObject.GetEmpty())
                .SetTextVariable("TARGET", agenda.Campaign?.Target?.Name ?? TextObject.GetEmpty());
            text = CourtObjectiveReports.WithMood(text, agenda.Faction?.GetDisplayName() ?? TextObject.GetEmpty(), actualMoodChange, result);
            BellumCivileNotifications.Show(text, CourtObjectiveReports.Color(result), primaryKingdom: agenda.Realm);
            BellumCivileLogger.Log($"Court campaign initiative concluded; realm={agenda.Realm?.StringId}; target={agenda.ObjectiveData.TargetId}; result={result}; credit={credit}; mood={shock}; actual_mood={actualMoodChange}; reason={reason}.");
        }

        internal TextObject CampaignHint(Kingdom realm, FactionObject faction)
        {
            var agenda = GetDisplayedAgenda(realm, faction);
            if (!IsCampaignObjective(agenda) || agenda.CrisisInterventionPending || agenda.State == CourtAgendaState.Crisis
                || agenda.State == CourtAgendaState.AwaitingNomination || agenda.State == CourtAgendaState.NominationExpired) return null;
            return agenda.ResultApplied ? CampaignStatus(agenda)
                : new TextObject("{=BC_CourtCampaignHint}{STATUS} A lawful offensive declaration brings +10 faction mood; an unfulfilled term brings -10. If the target attacks first, the undertaking ends without penalty.")
                    .SetTextVariable("STATUS", CampaignStatus(agenda));
        }

        private static TextObject CampaignStatus(CourtAgendaRecord agenda) => new TextObject(agenda.IsOngoingObjective
            ? "{=BC_CourtCampaignStatus}Participating houses weigh a lawful declaration more favorably (+15 war support) until {DATE}. Other interests still govern their votes; your own vote remains yours. Existing proposal and front restrictions remain in force."
            : agenda.State == CourtAgendaState.Announced
                ? "{=BC_CourtCampaignScheduled}The faction will begin pressing for a campaign on {SESSION}, pursuing a declaration until {DATE}."
                : agenda.ObjectiveData.State == CourtObjectiveState.Expired ? "{=BC_CourtCampaignExpired}The term ended without the proposed offensive."
                : agenda.State == CourtAgendaState.Completed ? "{=BC_CourtCampaignComplete}The realm declared its offensive within the term."
                : "{=BC_CourtCampaignClosed}This campaign initiative is no longer being pursued.")
            .SetTextVariable("DATE", CampaignTime.Days((float)agenda.ObjectiveData.DeadlineDay).ToString())
            .SetTextVariable("SESSION", agenda.SessionDate.ToString());
    }
}
