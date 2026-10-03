using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.Localization;

namespace BellumCivile.Behaviors
{
    public sealed partial class CourtAgendaBehavior
    {
        private List<CourtProtectionRecord> _protectionOffers = new List<CourtProtectionRecord>();
        private bool _processingProtection;
        private static bool IsProtection(CourtAgendaRecord a) => a?.ObjectiveData?.Kind == CourtProtectionRules.Kind;
        private static TextObject ProtectionLabel(Kingdom protector, Kingdom threat) => new TextObject("{=BC_ProtectionLabel}Seek {PROTECTOR}'s protection against {THREAT}")
            .SetTextVariable("PROTECTOR", protector?.Name ?? TextObject.GetEmpty()).SetTextVariable("THREAT", threat?.Name ?? TextObject.GetEmpty());
        internal bool HasProtectionOffer(Kingdom realm) => CrownActionCoolingDown(realm, CourtProtectionRules.Kind)
            || _protectionOffers.Any(p => p.Client == realm && (!p.Terminal || p.ReplyDay > 0 && CampaignTime.Now.ToDays < p.TermEnd));

        private static bool ProtectionIdentity(CourtProtectionRecord p)
        {
            if (!ValidRealm(p.Client) || !ValidRealm(p.Protector) || !ValidRealm(p.Threat)
                || p.ClientRuler?.IsAlive != true || p.ProtectorRuler?.IsAlive != true
                || p.Client.RulingClan != p.ClientHouse || p.ClientHouse.Leader != p.ClientRuler
                || p.Protector.RulingClan != p.ProtectorHouse || p.ProtectorHouse.Leader != p.ProtectorRuler
                || !p.Client.IsAtWarWith(p.Threat) || p.Client.GetStanceWith(p.Threat).WarStartDate.ToDays != p.ThreatWarStart) return false;
            return p.ThreatWar == null || p.ThreatWar.IsActive
                && Campaign.Current.GetCampaignBehavior<WarScoreBehavior>()?.GetActiveWar(p.Client, p.Threat) == p.ThreatWar;
        }

        private sealed class ProtectionExecutor : ICourtProtectionExecution
        {
            public bool IdentityValid(CourtProtectionRecord p) => ProtectionIdentity(p);
            public bool AtWar(CourtProtectionRecord p) => p.Protector.IsAtWarWith(p.Threat);
            public bool Preflight(CourtProtectionRecord p)
            {
                if (!ProtectionIdentity(p)) return false;
                var clients = ClientKingdomBehavior.Instance;
                if (p.EstablishmentAttempted && clients?.IsClientOf(p.Client, p.Protector) == true)
                    return !p.Client.IsAtWarWith(p.Protector) && AtWar(p) && !clients.IsClientKingdom(p.Protector)
                        && CourtProtectionAssessmentService.CanAlignClient(p.Client, p.Protector);
                return new CourtProtectionAssessmentService().Assess(p.Client, p.Threat, p.Protector).Eligible;
            }
            public void Declare(CourtProtectionRecord p) => DeclareWarAction.ApplyByDefault(p.Protector, p.Threat);
            public void Establish(CourtProtectionRecord p) => ClientKingdomBehavior.Instance.CompleteProtectionClientage(p);
            public bool Verify(CourtProtectionRecord p) => p.EstablishmentAttempted && ProtectionIdentity(p)
                && ClientKingdomBehavior.Instance?.ProtectionAlignmentComplete(p) == true
                && (!WarPeaceRevampBehavior.IsRevampEnabled() || Campaign.Current.GetCampaignBehavior<WarScoreBehavior>()?
                    .EnsureProtectionInterventionTracked(p.Protector, p.Threat, p.DeclarationAttempted) == true);
        }

        private void AdvanceProtection(CourtAgendaRecord a)
        {
            if (a.ResultApplied || a.IsOngoingObjective || !a.SessionDate.IsPast) return;
            var p = a.Protection;
            if (p == null) { Cancel(a, "protection_record_missing"); return; }
            if (!_protectionOffers.Contains(p))
            {
                if (HasProtectionOffer(p.Client)) { Cancel(a, "protection_offer_already_used"); return; }
                _protectionOffers.Add(p);
            }
            a.State = CourtAgendaState.PursuingObjective;
            a.ObjectiveData.Activate();
            if (!ProtectionIdentity(p) || CampaignTime.Now.ToDays >= p.TermEnd
                || !new CourtProtectionAssessmentService().Assess(p.Client, p.Threat, p.Protector).Eligible)
            { CloseProtection(p, CourtProtectionPhase.Cancelled, "circumstances_changed_before_offer"); return; }
            p.ReplyDeadline = Math.Min(p.TermEnd, CampaignTime.Now.ToDays + 3);
            if (p.ClientHouse == Clan.PlayerClan) p.Phase = CourtProtectionPhase.SenderDecision;
            else SendProtection(p);
        }

        private void SendProtection(CourtProtectionRecord p)
        {
            if (p.Phase != CourtProtectionPhase.SenderDecision && p.Phase != CourtProtectionPhase.Selected) return;
            if (!ProtectionIdentity(p) || CampaignTime.Now.ToDays >= p.ReplyDeadline)
            { CloseProtection(p, CourtProtectionPhase.Cancelled, "offer_no_longer_valid"); return; }
            var agenda = _agendas.FirstOrDefault(a => a.Protection == p && !a.ResultApplied);
            if (agenda == null || CrownActionCoolingDown(p.Client, CourtProtectionRules.Kind)
                || !TryPay(agenda, CrownInitiativeCost))
            { CloseProtection(p, CourtProtectionPhase.Cancelled, "influence_or_cooldown"); return; }
            StartCrownActionCooldown(agenda);
            p.TermEnd = CrownActionUntil(p.Client, CourtProtectionRules.Kind).ToDays;
            p.Phase = CourtProtectionPhase.Offered;
            p.ReplyDay = CampaignTime.Now.ToDays + 1;
            BellumCivileLogger.Log($"Protection offer sent; client={p.Client.StringId}; protector={p.Protector.StringId}; threat={p.Threat.StringId}; reply_until={p.ReplyDeadline}.");
        }

        private void AcceptProtection(CourtProtectionRecord p)
        {
            if (p.Phase != CourtProtectionPhase.Offered || CampaignTime.Now.ToDays >= p.ReplyDeadline || !ProtectionIdentity(p)
                || !new CourtProtectionAssessmentService().Assess(p.Client, p.Threat, p.Protector).Eligible)
            { CloseProtection(p, CourtProtectionPhase.Cancelled, "offer_changed_before_acceptance"); return; }
            p.Phase = CourtProtectionPhase.Accepted;
            p.RecoveryUntil = CampaignTime.Now.ToDays + 1.1;
            p.NextAttempt = CampaignTime.Now.ToDays;
            // The receipt is saved before native callbacks can change diplomacy or open notifications.
            MaintainProtectionOffers();
        }

        private void MaintainProtectionOffers()
        {
            if (_processingProtection) return;
            _processingProtection = true;
            try
            {
                foreach (var p in _protectionOffers.ToList())
                {
                    if (p.Terminal) { FinalizeProtection(p); continue; }
                    if (p.Phase == CourtProtectionPhase.Accepted)
                    {
                        CourtProtectionExecution.Step(p, new ProtectionExecutor(), CampaignTime.Now.ToDays);
                        if (p.Terminal) FinalizeProtection(p);
                        continue;
                    }
                    if (!ProtectionIdentity(p) || CampaignTime.Now.ToDays >= p.ReplyDeadline)
                    { CloseProtection(p, CourtProtectionPhase.Cancelled, "war_rulers_or_offer_deadline_changed"); continue; }
                    if (p.Phase != CourtProtectionPhase.Offered || CampaignTime.Now.ToDays < p.ReplyDay || p.ProtectorHouse == Clan.PlayerClan) continue;
                    var assessment = new CourtProtectionAssessmentService().Assess(p.Client, p.Threat, p.Protector);
                    if (!assessment.Eligible) CloseProtection(p, CourtProtectionPhase.Cancelled, assessment.BlockReason);
                    else if (!assessment.Score.WouldAccept) CloseProtection(p, CourtProtectionPhase.Declined, "npc_declined");
                    else AcceptProtection(p);
                }
                _protectionOffers.RemoveAll(p => p.Terminal && p.Reported && !p.PopupPending && CampaignTime.Now.ToDays >= p.TermEnd);
            }
            finally { _processingProtection = false; }
        }

        private void CloseProtection(CourtProtectionRecord p, CourtProtectionPhase phase, string reason)
        {
            if (p.Terminal) return;
            p.Phase = phase;
            p.ResultReason = reason;
            FinalizeProtection(p);
        }

        private void FinalizeProtection(CourtProtectionRecord p)
        {
            bool success = p.Phase == CourtProtectionPhase.Completed;
            foreach (var a in _agendas.Where(a => a.Protection == p && !a.ResultApplied))
            {
                a.State = success ? CourtAgendaState.Completed : p.Phase == CourtProtectionPhase.Declined ? CourtAgendaState.NotProposed : CourtAgendaState.Cancelled;
                a.ResultApplied = a.PaymentSettled = true;
                a.ObjectiveData.Finish(success ? CourtObjectiveState.Succeeded : CourtObjectiveState.Cancelled,
                    success ? CourtObjectiveCredit.Sponsor : CourtObjectiveCredit.None, p.ResultReason);
                a.ObjectiveData.TryClaimResult();
            }
            if (success && !p.CourtReceiptApplied)
            {
                p.CourtReceiptApplied = true;
                try { OnCourtClientageEstablished(p.Client, p.Protector); }
                catch (Exception ex) { BellumCivileLogger.Log("Protection court receipt interrupted: " + ex); }
            }
            if (p.Reported) return;
            p.Reported = true;
            p.PopupPending = Clan.PlayerClan?.Kingdom == p.Client || Clan.PlayerClan?.Kingdom == p.Protector;
            BellumCivileLogger.Log($"Protection offer concluded; client={p.Client?.StringId}; protector={p.Protector?.StringId}; threat={p.Threat?.StringId}; phase={p.Phase}; attempts={p.Attempts}; declaration_attempted={p.DeclarationAttempted}; establishment_attempted={p.EstablishmentAttempted}; reason={p.ResultReason}.");
            BellumCivileNotifications.Show(ProtectionOutcome(p), success ? BellumNotificationColors.Success : BellumNotificationColors.Warning,
                primaryKingdom: p.Client, secondaryKingdom: p.Protector, isMajorEvent: success);
        }

        private static TextObject ProtectionText(CourtProtectionRecord p, string text) => new TextObject(text)
            .SetTextVariable("CLIENT", p.Client?.Name ?? TextObject.GetEmpty()).SetTextVariable("PROTECTOR", p.Protector?.Name ?? TextObject.GetEmpty())
            .SetTextVariable("THREAT", p.Threat?.Name ?? TextObject.GetEmpty()).SetTextVariable("RULER", p.ClientRuler?.Name ?? TextObject.GetEmpty());

        private static TextObject ProtectionOutcome(CourtProtectionRecord p)
        {
            if (p.Phase == CourtProtectionPhase.Completed) return ProtectionText(p, "{=BC_ProtectionSucceeded}{CLIENT} has accepted the overlordship of {PROTECTOR}. In return, its new protector stands beside it in the war against {THREAT}. The threatened crown endures, though its independence has been surrendered.");
            if (p.Phase == CourtProtectionPhase.Declined) return ProtectionText(p, "{=BC_ProtectionDeclined}The proposed submission of {CLIENT} to {PROTECTOR} has been declined. No protection agreement was concluded.");
            if (p.Phase != CourtProtectionPhase.Failed) return ProtectionText(p, "{=BC_ProtectionCancelled}The appeal by {CLIENT} for {PROTECTOR}'s protection against {THREAT} has lapsed. Changed circumstances or an unanswered offer have brought the negotiations to an end.");
            return ProtectionText(p, "{=BC_ProtectionFailed}The protection agreement between {CLIENT} and {PROTECTOR} could not be fully carried out. {CLIENTAGE} {WAR}")
                .SetTextVariable("CLIENTAGE", new TextObject(ClientKingdomBehavior.Instance?.IsClientOf(p.Client, p.Protector) == true
                    ? "{=BC_ProtectionPartialClient}Clientage is already in effect." : "{=BC_ProtectionNoClient}Clientage was not established."))
                .SetTextVariable("WAR", new TextObject(p.Protector?.IsAtWarWith(p.Threat) == true
                    ? "{=BC_ProtectionPartialWar}The protector is at war with the named enemy; that war remains in force." : "{=BC_ProtectionNoWar}The promised intervention is not in force."));
        }
    }
}
