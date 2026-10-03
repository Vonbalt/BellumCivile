using System;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.Localization;

namespace BellumCivile.Behaviors
{
    public sealed partial class CourtAgendaBehavior
    {
        private static bool IsPeaceObjective(CourtAgendaRecord agenda) => agenda?.ObjectiveData?.Kind == CourtPeaceRules.Kind;
        private static TextObject PeaceLabel(Kingdom target) => new TextObject("{=BC_CourtSeekPeace}Seek peace with {REALM}")
            .SetTextVariable("REALM", target?.Name ?? TextObject.GetEmpty());

        internal TextObject PeaceHint(Kingdom realm, FactionObject faction)
        {
            var agenda = GetDisplayedAgenda(realm, faction);
            if (!IsPeaceObjective(agenda) || agenda.CrisisInterventionPending || agenda.State == CourtAgendaState.Crisis
                || agenda.State == CourtAgendaState.AwaitingNomination || agenda.State == CourtAgendaState.NominationExpired) return null;
            return agenda.ResultApplied ? PeaceStatus(agenda)
                : new TextObject("{=BC_CourtPeaceHint}{STATUS} Success brings +10 faction mood; an unfulfilled term brings -10.")
                    .SetTextVariable("STATUS", PeaceStatus(agenda));
        }

        private static bool PeaceOwnerValid(CourtAgendaRecord agenda) => CourtPeaceObjectiveSource.Valid(agenda.Realm)
            && agenda.Faction?.ParentKingdom == agenda.Realm && agenda.Faction.Type == FactionType.Liberty
            && Campaign.Current?.GetCampaignBehavior<FactionManagerBehavior>()?.GetFactionsInKingdom(agenda.Realm).Contains(agenda.Faction) == true;

        internal float PeaceInitiativeBonus(Kingdom realm, WarScoreRecord war, Clan voter)
        {
            if (voter == null || voter == Clan.PlayerClan || !Eligible(voter, realm)
                || war == null || !WarPeaceRevampBehavior.IsRevampEnabled()) return 0;
            foreach (var agenda in _agendas)
            {
                var plan = agenda.Peace;
                if (!IsPeaceObjective(agenda) || agenda.Realm != realm || !agenda.IsOngoingObjective || agenda.ResultApplied
                    || plan == null || !PeaceOwnerValid(agenda)
                    || !agenda.ObjectiveData.HasTermSnapshot || !CourtPeaceObjectiveSource.EligibleWar(realm, plan.Target, war)
                    || !ReceivesPoliticalSupport(agenda, voter, plan.Members)) continue;
                float bonus = plan.ActiveBonus(war, CampaignTime.Now.ToDays, agenda.ObjectiveData.DeadlineDay);
                if (bonus != 0) return bonus;
            }
            return 0;
        }

        private void MaintainPeaceObjectives()
        {
            foreach (var agenda in _agendas.Where(a => IsPeaceObjective(a) && !a.ResultApplied
                && (a.IsOngoingObjective || a.State == CourtAgendaState.Announced)).ToList())
            {
                var plan = agenda.Peace;
                if (!PeaceOwnerValid(agenda) || plan == null || !agenda.ObjectiveData.HasTermSnapshot
                    || !CourtPeaceObjectiveSource.EligibleWar(agenda.Realm, plan.Target, plan.War)
                    || Campaign.Current.GetCampaignBehavior<WarScoreBehavior>()?.GetActiveWar(agenda.Realm, plan.Target) != plan.War)
                { FinishPeaceObjective(agenda, CourtObjectiveState.Cancelled, "realm_faction_or_conflict_changed"); continue; }
                if (CampaignTime.Now.ToDays > agenda.ObjectiveData.DeadlineDay)
                { FinishPeaceObjective(agenda, CourtObjectiveState.Expired, "term_expired"); continue; }
                plan.Members?.RemoveAll(c => !Eligible(c, agenda.Realm) || !agenda.Faction.Members.Contains(c));
            }
        }

        private void AdvancePeaceObjective(CourtAgendaRecord agenda)
        {
            if (agenda.ResultApplied || agenda.IsOngoingObjective || !agenda.SessionDate.IsPast) return;
            var plan = agenda.Peace;
            if (plan == null || !PeaceOwnerValid(agenda) || !agenda.ObjectiveData.HasTermSnapshot
                || !CourtPeaceObjectiveSource.EligibleWar(agenda.Realm, plan.Target, plan.War)
                || Campaign.Current.GetCampaignBehavior<WarScoreBehavior>()?.GetActiveWar(agenda.Realm, plan.Target) != plan.War)
            { FinishPeaceObjective(agenda, CourtObjectiveState.Cancelled, "unavailable_at_session"); return; }
            if (CampaignTime.Now.ToDays > agenda.ObjectiveData.DeadlineDay)
            { FinishPeaceObjective(agenda, CourtObjectiveState.Expired, "term_expired"); return; }
            plan.Members = agenda.Faction.Members.Where(c => Eligible(c, agenda.Realm) && c != agenda.Realm.RulingClan).Distinct().ToList();
            plan.ActivatedDay = CampaignTime.Now.ToDays;
            plan.Activated = true;
            agenda.ObjectiveData.Activate();
            agenda.State = CourtAgendaState.PursuingObjective;
            var text = new TextObject("{=BC_CourtPeaceBegun}The {FACTION} of {REALM} has called for an end to the war with {TARGET}. Its houses will lend greater weight to a settlement, though each will still judge the terms for itself. The court will reckon the outcome at the end of this term, on {DATE}.")
                .SetTextVariable("FACTION", agenda.Faction.GetDisplayName()).SetTextVariable("REALM", agenda.Realm.Name)
                .SetTextVariable("TARGET", plan.Target.Name).SetTextVariable("DATE", CampaignTime.Days((float)agenda.ObjectiveData.DeadlineDay).ToString());
            BellumCivileNotifications.Show(text, BellumNotificationColors.Politics, primaryKingdom: agenda.Realm);
            BellumCivileLogger.Log($"Court peace initiative active; realm={agenda.Realm.StringId}; target={plan.Target.StringId}; war={plan.War.WarKey}; started={plan.War.StartedDay}; members={plan.Members.Count}; deadline={agenda.ObjectiveData.DeadlineDay}; bonus={CourtPeaceRules.AcceptanceBonus}.");
        }

        private void OnCourtPeace(IFaction first, IFaction second, MakePeaceAction.MakePeaceDetail detail)
        {
            if (!(first is Kingdom a) || !(second is Kingdom b) || a.IsAtWarWith(b)) return;
            foreach (var agenda in _agendas.Where(x => IsPeaceObjective(x) && !x.ResultApplied
                && (x.IsOngoingObjective || x.State == CourtAgendaState.Announced)).ToList())
            {
                var plan = agenda.Peace;
                if (plan?.War == null || !((agenda.Realm == a && plan.Target == b) || (agenda.Realm == b && plan.Target == a))) continue;
                if (!WarPeaceRevampBehavior.IsRevampEnabled() || !PeaceOwnerValid(agenda) || !CourtPeaceObjectiveSource.Valid(plan.Target))
                { FinishPeaceObjective(agenda, CourtObjectiveState.Cancelled, "realm_eliminated"); continue; }
                // Pair keys are reused by later wars. Match the saved record, even if its peace listener ran first.
                var scores = Campaign.Current.GetCampaignBehavior<WarScoreBehavior>();
                var latest = scores?.GetTrackedWars().LastOrDefault(w => w.WarKey == plan.War.WarKey);
                if (latest != plan.War || !agenda.ObjectiveData.HasTermSnapshot) continue;
                if (!CourtPeaceRules.InWindow(CampaignTime.Now.ToDays, agenda.ObjectiveData.SelectedDay, agenda.ObjectiveData.DeadlineDay))
                { FinishPeaceObjective(agenda, CourtObjectiveState.Expired, "peace_after_deadline"); continue; }
                FinishPeaceObjective(agenda, CourtObjectiveState.Succeeded, "peace_concluded");
            }
        }

        private void FinishPeaceObjective(CourtAgendaRecord agenda, CourtObjectiveState result, string reason)
        {
            if (agenda.ResultApplied) return;
            bool success = result == CourtObjectiveState.Succeeded;
            bool failure = result == CourtObjectiveState.Expired;
            var credit = success ? (agenda.Peace?.Activated == true ? CourtObjectiveCredit.Sponsor : CourtObjectiveCredit.FulfilledElsewhere)
                : CourtObjectiveCredit.None;
            if (!agenda.ObjectiveData.Finish(result, credit, reason) || !agenda.ObjectiveData.TryClaimResult()) return;
            agenda.ResultApplied = agenda.PaymentSettled = true;
            agenda.State = success ? CourtAgendaState.Completed : failure ? CourtAgendaState.NotProposed : CourtAgendaState.Cancelled;
            float shock = success ? BellumCivileConstants.CourtAgendaSuccessShock : failure ? BellumCivileConstants.CourtAgendaFailureShock : 0;
            float actualMoodChange = 0;
            if (PeaceOwnerValid(agenda) && shock != 0)
            {
                float before = agenda.Faction.Mood;
                agenda.Faction.Mood = Math.Max(-100, Math.Min(100, agenda.Faction.Mood + shock));
                actualMoodChange = agenda.Faction.Mood - before;
                RecordResultHistory(agenda, shock);
            }
            var text = new TextObject(success
                ? "{=BC_CourtPeaceSucceeded}Peace has been concluded between {REALM} and {TARGET}. The lords of the {FACTION} welcome the settlement, praising the Crown for bringing the fighting to an end."
                : failure ? "{=BC_CourtPeaceFailed}The court term ends with {REALM} and {TARGET} still at war. The lords of the {FACTION} reproach the Crown for failing to secure the peace they sought."
                : "{=BC_CourtPeaceCancelled}Changed circumstances have overtaken the {FACTION}'s initiative for peace with {TARGET}. The undertaking has been set aside without blame.")
                .SetTextVariable("FACTION", agenda.Faction?.GetDisplayName() ?? TextObject.GetEmpty())
                .SetTextVariable("REALM", agenda.Realm?.Name ?? TextObject.GetEmpty())
                .SetTextVariable("TARGET", agenda.Peace?.Target?.Name ?? TextObject.GetEmpty());
            text = CourtObjectiveReports.WithMood(text, agenda.Faction?.GetDisplayName() ?? TextObject.GetEmpty(), actualMoodChange, result);
            BellumCivileNotifications.Show(text, CourtObjectiveReports.Color(result), primaryKingdom: agenda.Realm);
            BellumCivileLogger.Log($"Court peace initiative concluded; realm={agenda.Realm?.StringId}; target={agenda.ObjectiveData.TargetId}; result={result}; credit={credit}; mood={shock}; actual_mood={actualMoodChange}; reason={reason}.");
        }

        private static TextObject PeaceStatus(CourtAgendaRecord agenda) => new TextObject(agenda.IsOngoingObjective
            ? "{=BC_CourtPeaceStatus}The faction's participating houses weigh peace more favorably (+15 acceptance) until {DATE}. Other interests and treaty terms still govern their votes; your own vote remains yours."
            : agenda.State == CourtAgendaState.Announced
                ? "{=BC_CourtPeaceScheduled}The faction will begin pressing for peace on {SESSION}, pursuing a settlement until {DATE}."
                : agenda.ObjectiveData.State == CourtObjectiveState.Expired ? "{=BC_CourtPeaceExpired}The term ended without a peace settlement."
                : agenda.State == CourtAgendaState.Completed ? "{=BC_CourtPeaceComplete}Peace was concluded within the term."
                : "{=BC_CourtPeaceClosed}This peace initiative is no longer being pursued.")
            .SetTextVariable("DATE", CampaignTime.Days((float)agenda.ObjectiveData.DeadlineDay).ToString())
            .SetTextVariable("SESSION", agenda.SessionDate.ToString());
    }
}
