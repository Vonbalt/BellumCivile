using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Localization;

namespace BellumCivile.Behaviors
{
    public sealed partial class CourtAgendaBehavior
    {
        private static bool IsAppeasement(CourtAgendaRecord agenda) => agenda?.ObjectiveData?.Kind == CourtAppeasementRules.Kind;
        private static TextObject AppeasementLabel(FactionObject faction) => new TextObject("{=BC_CrownReconcileObjective}Reconcile with the {FACTION}")
            .SetTextVariable("FACTION", faction?.GetDisplayName() ?? TextObject.GetEmpty());

        private void EndChangedRulerAccommodations()
        {
            var manager = Campaign.Current?.GetCampaignBehavior<FactionManagerBehavior>();
            if (manager == null) return;
            foreach (var realm in Kingdom.All)
                foreach (var faction in manager.GetFactionsInKingdom(realm).Where(f => f.IsIdeology))
                    if (faction.CrownAccommodation != null && !faction.CrownAccommodation.SameRuler)
                        faction.CrownAccommodation.Ended = true;
        }

        private void AdvanceAppeasement(CourtAgendaRecord agenda)
        {
            if (!agenda.SessionDate.IsPast || agenda.ResultApplied) return;
            var plan = agenda.Appeasement;
            if (plan?.Applied == true)
            { FinishAppeasement(agenda, true, "receipt_recovered"); return; }
            if (plan == null || plan.Attempted || !plan.SameRuler || plan.DurationDays <= 0
                || CrownActionCoolingDown(agenda.Realm, CourtAppeasementRules.Kind)
                || !CourtAppeasementObjectiveSource.Eligible(plan.Target, agenda.Realm)
                || CourtAppeasementRules.Cost(MemberCount(plan.Target)) != plan.QuotedCost)
            { FinishAppeasement(agenda, false, "mood_members_ruler_or_receipt_changed"); return; }
            // Seal before payment callbacks; interrupted attempts cannot charge twice.
            if (!plan.TryBegin()) return;
            if (!TryPay(agenda, plan.QuotedCost))
            { FinishAppeasement(agenda, false, "insufficient_influence"); return; }
            if (!plan.SameRuler || !CourtAppeasementObjectiveSource.Eligible(plan.Target, agenda.Realm)
                || CourtAppeasementRules.Cost(MemberCount(plan.Target)) != plan.QuotedCost)
            { FinishAppeasement(agenda, false, "changed_during_payment"); return; }
            plan.Until = CampaignTime.Now + CampaignTime.Days(plan.DurationDays);
            plan.Target.CrownAccommodation = plan;
            plan.Applied = true;
            StartCrownActionCooldown(agenda);
            FinishAppeasement(agenda, true, "accommodation_granted");
            var text = new TextObject("{=BC_CrownReconcileReport}{RULER} has spent the Crown's political influence to reach an accommodation with the {FACTION}. For now, its houses have softened their opposition, though their grievances remain.\n{COST} influence; +20 faction mood until {DATE}. An issued ultimatum or rebellion remains in force.")
                .SetTextVariable("RULER", plan.Ruler.Name).SetTextVariable("FACTION", plan.Target.GetDisplayName())
                .SetTextVariable("COST", plan.QuotedCost).SetTextVariable("DATE", plan.Until.ToString());
            BellumCivileNotifications.Show(text, BellumNotificationColors.Politics, primaryKingdom: agenda.Realm);
        }

        private void FinishAppeasement(CourtAgendaRecord agenda, bool success, string reason)
        {
            agenda.State = success ? CourtAgendaState.Completed : CourtAgendaState.Withdrawn;
            agenda.ResultApplied = agenda.PaymentSettled = true;
            agenda.ObjectiveData.Finish(success ? CourtObjectiveState.Succeeded : CourtObjectiveState.Cancelled,
                success ? CourtObjectiveCredit.Sponsor : CourtObjectiveCredit.None, reason);
            agenda.ObjectiveData.TryClaimResult();
            BellumCivileLogger.Log($"Crown appeasement concluded; realm={agenda.Realm.StringId}; target={agenda.ObjectiveData.TargetId}; result={agenda.State}; paid={agenda.PaidInfluence}; reason={reason}.");
            if (!success) NotifyAgenda(agenda);
        }
    }
}
