using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Localization;

namespace BellumCivile.Behaviors
{
    public sealed partial class CourtAgendaBehavior
    {
        private static bool IsLiberation(CourtAgendaRecord agenda) => agenda?.ObjectiveData?.Kind == CourtLiberationRules.Kind;
        private static TextObject LiberationLabel(Kingdom suzerain) => new TextObject("{=BC_CourtLiberationLabel}Prepare to cast off {SUZERAIN}")
            .SetTextVariable("SUZERAIN", suzerain?.Name ?? TextObject.GetEmpty());
        internal bool HasLiberationObjective(Kingdom realm, CourtAgendaRecord except = null) => _agendas.Any(a => a != except
            && a.Realm == realm && IsLiberation(a) && !a.ResultApplied && (a.IsUnopened || a.IsOngoingObjective));

        private static bool LiberationCrownValid(CourtAgendaRecord agenda) => WarPeaceRevampBehavior.IsRevampEnabled()
            && ValidRealm(agenda.Realm) && agenda.Faction == null && agenda.Sponsor == agenda.Realm.RulingClan
            && agenda.Liberation?.Ruler == agenda.Realm.RulingClan.Leader;
        private static bool SameLiberationClientage(CourtAgendaRecord agenda) => LiberationCrownValid(agenda)
            && agenda.Liberation.Clientage != null && ValidRealm(agenda.Liberation.Suzerain)
            && ClientKingdomBehavior.Instance?.GetClientRecord(agenda.Realm) == agenda.Liberation.Clientage
            && ClientKingdomBehavior.Instance.GetSuzerain(agenda.Realm) == agenda.Liberation.Suzerain;

        internal float LiberationDesireBonus(Kingdom realm, ClientKingdomRecord record)
        {
            foreach (var agenda in _agendas)
            {
                if (!IsLiberation(agenda) || agenda.Realm != realm || agenda.ResultApplied || !agenda.IsOngoingObjective
                    || agenda.Liberation?.Clientage != record || !agenda.ObjectiveData.HasTermSnapshot) continue;
                var plan = agenda.Liberation;
                if (CourtLiberationRules.Active(plan.Activated, SameLiberationClientage(agenda), CampaignTime.Now.ToDays,
                    plan.ActivatedDay, agenda.ObjectiveData.DeadlineDay) && !realm.IsAtWarWith(plan.Suzerain))
                    return CourtLiberationRules.DesireBonus;
            }
            return 0;
        }

        private void MaintainLiberationObjectives()
        {
            foreach (var agenda in _agendas.Where(a => IsLiberation(a) && !a.ResultApplied
                && (a.IsUnopened || a.IsOngoingObjective)).ToList())
            {
                var plan = agenda.Liberation;
                if (plan?.WarConfirmed == true)
                { FinishLiberation(agenda, plan.Initiated ? "begun" : "attacked"); continue; }
                if (plan == null || !agenda.ObjectiveData.HasTermSnapshot || !LiberationCrownValid(agenda))
                { FinishLiberation(agenda, "changed"); continue; }
                if (CampaignTime.Now.ToDays >= agenda.ObjectiveData.DeadlineDay)
                { FinishLiberation(agenda, "expired"); continue; }
                if (!SameLiberationClientage(agenda))
                {
                    bool released = ClientKingdomBehavior.Instance?.GetClientRecord(agenda.Realm) == null
                        && (plan.Suzerain == null || plan.Suzerain.IsEliminated || !agenda.Realm.IsAtWarWith(plan.Suzerain));
                    FinishLiberation(agenda, released ? "released" : "changed");
                }
            }
        }

        private void AdvanceLiberation(CourtAgendaRecord agenda)
        {
            if (agenda.ResultApplied || agenda.IsOngoingObjective || !agenda.SessionDate.IsPast) return;
            if (!SameLiberationClientage(agenda) || !agenda.ObjectiveData.HasTermSnapshot
                || CrownActionCoolingDown(agenda.Realm, CourtLiberationRules.Kind)
                || CampaignTime.Now.ToDays >= agenda.ObjectiveData.DeadlineDay)
            { FinishLiberation(agenda, "changed"); return; }
            var facts = new CourtLiberationAssessment(agenda.Realm);
            if (!facts.Eligible || (agenda.Sponsor != Clan.PlayerClan && !facts.Viable))
            { FinishLiberation(agenda, "unready"); return; }
            if (!TryPay(agenda, CrownInitiativeCost))
            { FinishLiberation(agenda, "unready"); return; }
            agenda.Liberation.Activated = true;
            StartCrownActionCooldown(agenda);
            agenda.Liberation.ActivatedDay = CampaignTime.Now.ToDays;
            agenda.ObjectiveData.Activate();
            agenda.State = CourtAgendaState.PursuingObjective;
            Campaign.Current?.GetCampaignBehavior<WarPeaceRevampBehavior>()?.InvalidateKingdom(agenda.Realm);
            LiberationNotice(agenda, "liberation_preparing", "active");
        }

        // Called after hostility is verified, while the original clientage record still exists.
        internal void RecordCourtLiberationWar(Kingdom attacker, Kingdom defender)
        {
            if (attacker == null || defender == null || !attacker.IsAtWarWith(defender)) return;
            foreach (var agenda in _agendas.Where(a => IsLiberation(a) && !a.ResultApplied
                && (a.IsUnopened || a.IsOngoingObjective)).ToList())
            {
                var plan = agenda.Liberation;
                bool initiated = agenda.Realm == attacker && plan?.Suzerain == defender;
                bool attacked = agenda.Realm == defender && plan?.Suzerain == attacker;
                if ((!initiated && !attacked) || !SameLiberationClientage(agenda) || !agenda.ObjectiveData.HasTermSnapshot
                    || CampaignTime.Now.ToDays >= agenda.ObjectiveData.DeadlineDay) continue;
                plan.WarConfirmed = true;
                plan.Initiated = initiated;
                plan.WarDay = CampaignTime.Now.ToDays;
                FinishLiberation(agenda, initiated ? "begun" : "attacked");
            }
        }

        private void FinishLiberation(CourtAgendaRecord agenda, string reason)
        {
            if (agenda.ResultApplied) return;
            bool success = reason == "begun" || reason == "released";
            agenda.ResultApplied = agenda.PaymentSettled = true;
            agenda.State = success ? CourtAgendaState.Completed : reason == "expired" ? CourtAgendaState.NotProposed : CourtAgendaState.Cancelled;
            agenda.ObjectiveData.Finish(success ? CourtObjectiveState.Succeeded : reason == "expired" ? CourtObjectiveState.Expired : CourtObjectiveState.Cancelled,
                reason == "begun" && agenda.Liberation?.Activated == true ? CourtObjectiveCredit.Sponsor : success ? CourtObjectiveCredit.FulfilledElsewhere : CourtObjectiveCredit.None, reason);
            agenda.ObjectiveData.TryClaimResult();
            Campaign.Current?.GetCampaignBehavior<WarPeaceRevampBehavior>()?.InvalidateKingdom(agenda.Realm);
            LiberationNotice(agenda, reason == "begun" ? "liberation_begun" : reason == "released" ? "liberation_released"
                : reason == "expired" ? "liberation_expired" : "liberation_cancelled", "result");
            BellumCivileLogger.Log($"Crown liberation objective ended; realm={agenda.Realm?.StringId}; suzerain={agenda.Liberation?.Suzerain?.StringId}; reason={reason}.");
        }

        private static void LiberationNotice(CourtAgendaRecord agenda, string kind, string suffix)
        {
            var notices = ConflictOutcomeBehavior.Current;
            var notice = notices?.Begin("court_liberation:" + agenda.Realm.StringId + ":" + agenda.Liberation?.Ruler?.StringId + ":" + agenda.Liberation?.Suzerain?.StringId + ":" + agenda.ObjectiveData.SelectedDay.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + ":" + suffix,
                agenda.Realm, null);
            if (notice == null) return;
            ConflictOutcomeBehavior.Capture(notice, "REALM", agenda.Realm.Name);
            ConflictOutcomeBehavior.Capture(notice, "SUZERAIN", agenda.Liberation?.Suzerain?.Name);
            ConflictOutcomeBehavior.Capture(notice, "RULER", agenda.Liberation?.Ruler?.Name);
            ConflictOutcomeBehavior.Capture(notice, "DATE", new TextObject("{=!}" + CampaignTime.Days((float)agenda.ObjectiveData.DeadlineDay).ToString()));
            notices.Publish(notice, kind);
        }
    }
}
