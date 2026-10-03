using System;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Localization;

namespace BellumCivile.Behaviors
{
    public sealed partial class CourtAgendaBehavior
    {
        private static bool IsClientGrant(CourtAgendaRecord a) => a?.ObjectiveData?.Kind == CourtClientGrantRules.Kind;
        internal bool HasClientGrantFor(Settlement fief, CourtAgendaRecord except) => _agendas.Any(a => a != except
            && IsClientGrant(a) && a.ClientGrant?.Fief == fief && !a.ResultApplied && (a.IsUnopened || a.IsOngoingObjective));
        private static TextObject ClientGrantLabel(Settlement fief, Kingdom client) => new TextObject(
            "{=BC_ClientGrantLabel}Grant {FIEF} to {CLIENT}").SetTextVariable("FIEF", fief?.Name ?? TextObject.GetEmpty())
            .SetTextVariable("CLIENT", client?.Name ?? TextObject.GetEmpty());
        private void MaintainClientGrants()
        {
            foreach (var a in _agendas.Where(a => IsClientGrant(a) && a.IsOngoingObjective && !a.ResultApplied).ToList())
                AdvanceClientGrant(a);
        }
        private static bool ClientGrantIdentity(CourtAgendaRecord a)
        {
            var p = a.ClientGrant;
            return p != null && ValidRealm(a.Realm) && ValidRealm(p.Client)
                && a.Realm.RulingClan == p.Grantor && p.Grantor.Leader == p.Ruler && p.Ruler.IsAlive
                && p.Client.RulingClan == p.Recipient && p.Recipient.Leader == p.Beneficiary && p.Beneficiary.IsAlive
                && ClientKingdomBehavior.Instance?.GetSuzerain(p.Client) == a.Realm && !p.Client.IsAtWarWith(a.Realm);
        }
        private void AdvanceClientGrant(CourtAgendaRecord a)
        {
            if (a.ResultApplied || !a.SessionDate.IsPast) return;
            var p = a.ClientGrant;
            if (!ClientGrantIdentity(a)) { FinishClientGrant(a, false, "authority_changed"); return; }
            var title = CourtTitleGrantObjectiveSource.Title(p.TitleId);
            if ((!p.TransferAttempted || p.Fief?.OwnerClan == p.Grantor)
                && (title?.DeJureHolderClanId != p.OldLegal
                    || !CourtClientGrantObjectiveSource.Eligible(a.Realm, p.Fief, p.Client)
                    || !p.Paid && !a.Manual && !CourtClientGrantObjectiveSource.NpcEligible(a.Realm, p.Fief, p.Client)))
            { FinishClientGrant(a, false, "land_or_circumstances_changed"); return; }
            if (!p.Paid)
            {
                if (!TryPay(a, CourtClientGrantRules.Cost)) { FinishClientGrant(a, false, "unaffordable"); return; }
                p.Paid = true;
            }
            a.State = CourtAgendaState.PursuingObjective;
            a.ObjectiveData.Activate();
            p.TransferAttempted = true;
            p.Attempts++;
            try
            {
                if (!CourtTitleGrantObjectiveSource.Titles.CompleteClientLandGrant(p))
                { FinishClientGrant(a, false, "ownership_changed"); return; }
                if (!p.RelationApplied)
                {
                    p.RelationApplied = true;
                    int reward = Campaign.Current.GetCampaignBehavior<DynamicRelationBehavior>()?
                        .LimitClientGrantGain(p.Ruler, p.Beneficiary) ?? CourtClientGrantRules.Gratitude;
                    RelationMemoryService.ApplyChange(p.Ruler, p.Beneficiary, reward, true,
                        RelationMemorySources.ClientLandGrant, 10, RelationMemoryScope.Personal);
                }
                FinishClientGrant(a, true, "delivered");
            }
            catch (Exception ex)
            {
                BellumCivileLogger.Log("Client land grant interrupted; fief=" + p.Fief.StringId + "; " + ex);
                if (p.Attempts >= 3) FinishClientGrant(a, false, "delivery_interrupted");
            }
        }
        private void FinishClientGrant(CourtAgendaRecord a, bool success, string reason)
        {
            if (a.ResultApplied) return;
            var p = a.ClientGrant;
            a.ResultApplied = a.PaymentSettled = true;
            a.State = success ? CourtAgendaState.Completed : CourtAgendaState.Cancelled;
            a.ObjectiveData.Finish(success ? CourtObjectiveState.Succeeded : CourtObjectiveState.Cancelled,
                success ? CourtObjectiveCredit.Sponsor : CourtObjectiveCredit.None, reason);
            a.ObjectiveData.TryClaimResult();
            if (!success && p?.Paid == true && p.Fief?.OwnerClan == p.Grantor)
                NpcInfluenceBudgetService.Refund(p.Grantor, CourtClientGrantRules.Cost, NpcInfluenceExpenseKind.Discretionary, "client_grant_failed");
            if (p != null) ReportClientGrant(a, success);
            BellumCivileLogger.Log($"Client land grant; realm={a.Realm?.StringId}; client={p?.Client?.StringId}; fief={p?.Fief?.StringId}; success={success}; reason={reason}.");
        }
        private static void ReportClientGrant(CourtAgendaRecord a, bool success)
        {
            var p = a.ClientGrant;
            BellumCivileNotifications.Show(new TextObject(success
                    ? "{=BC_ClientGrantDelivered}{RULER} has granted {FIEF} to {CLIENT}, entrusting its lands to {RECIPIENT}. The gift strengthens their realm and gives its ruler cause to remember this generosity."
                    : "{=BC_ClientGrantCancelled}The Crown's intended grant of {FIEF} to {CLIENT} could not be completed. No further transfer will be attempted under this authorization.")
                    .SetTextVariable("RULER", p.Ruler?.Name ?? TextObject.GetEmpty()).SetTextVariable("FIEF", p.Fief?.Name ?? TextObject.GetEmpty())
                    .SetTextVariable("CLIENT", p.Client?.Name ?? TextObject.GetEmpty()).SetTextVariable("RECIPIENT", p.Beneficiary?.Name ?? TextObject.GetEmpty()),
                    BellumNotificationColors.Land, primaryKingdom: a.Realm, secondaryClan: p.Recipient);
        }
    }
}
