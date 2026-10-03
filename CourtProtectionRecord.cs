using TaleWorlds.CampaignSystem;
using TaleWorlds.SaveSystem;

namespace BellumCivile
{
    public enum CourtProtectionPhase { Selected, SenderDecision, Offered, Accepted, Completed, Declined, Cancelled, Failed }

    public sealed class CourtProtectionRecord
    {
        [SaveableField(1)] public Kingdom Client;
        [SaveableField(2)] public Kingdom Protector;
        [SaveableField(3)] public Kingdom Threat;
        [SaveableField(4)] public Hero ClientRuler;
        [SaveableField(5)] public Hero ProtectorRuler;
        [SaveableField(6)] public Clan ClientHouse;
        [SaveableField(7)] public Clan ProtectorHouse;
        [SaveableField(8)] public WarScoreRecord ThreatWar;
        [SaveableField(9)] public double ThreatWarStart;
        [SaveableField(10)] public double TermStart;
        [SaveableField(11)] public double TermEnd;
        [SaveableField(12)] public CourtProtectionPhase Phase;
        [SaveableField(13)] public double ReplyDay;
        [SaveableField(14)] public double ReplyDeadline;
        [SaveableField(15)] public double RecoveryUntil;
        [SaveableField(16)] public double NextAttempt;
        [SaveableField(17)] public int Attempts;
        [SaveableField(18)] public bool DeclarationAttempted;
        [SaveableField(19)] public bool EstablishmentAttempted;
        [SaveableField(20)] public bool Reported;
        [SaveableField(21)] public bool PopupPending;
        [SaveableField(22)] public string ResultReason;
        [SaveableField(23)] public bool CourtReceiptApplied;

        internal bool Terminal => Phase == CourtProtectionPhase.Completed || Phase == CourtProtectionPhase.Declined
            || Phase == CourtProtectionPhase.Cancelled || Phase == CourtProtectionPhase.Failed;
    }

    internal interface ICourtProtectionExecution
    {
        bool IdentityValid(CourtProtectionRecord offer);
        bool Preflight(CourtProtectionRecord offer);
        bool AtWar(CourtProtectionRecord offer);
        void Declare(CourtProtectionRecord offer);
        void Establish(CourtProtectionRecord offer);
        bool Verify(CourtProtectionRecord offer);
    }

    internal static class CourtProtectionExecution
    {
        internal static void Step(CourtProtectionRecord p, ICourtProtectionExecution execution, double now)
        {
            if (p.Phase != CourtProtectionPhase.Accepted || !CourtProtectionRules.Finite(now) || now < p.NextAttempt) return;
            if (p.Attempts >= 3 || now > p.RecoveryUntil) { Fail(p, "recovery_expired"); return; }
            p.Attempts++;
            p.NextAttempt = now + 0.5;
            try
            {
                if (!execution.IdentityValid(p)) { Fail(p, "accepted_parties_or_war_changed"); return; }
                if (execution.Verify(p)) { Complete(p); return; }
                if (!execution.Preflight(p)) { Fail(p, "accepted_preflight_failed"); return; }
                if (!execution.AtWar(p))
                {
                    // A declaration is attempted once, even if a callback throws after changing stance.
                    if (p.DeclarationAttempted) { Fail(p, "intervention_no_longer_active"); return; }
                    p.DeclarationAttempted = true;
                    execution.Declare(p);
                    if (!execution.AtWar(p)) { Fail(p, "declaration_blocked"); return; }
                }
                if (!execution.IdentityValid(p) || !execution.Preflight(p)) { Fail(p, "changed_during_intervention"); return; }
                p.EstablishmentAttempted = true;
                execution.Establish(p);
                if (execution.Verify(p)) Complete(p);
                else if (p.Attempts >= 3) Fail(p, "incomplete_alignment");
            }
            catch (System.Exception ex)
            {
                p.ResultReason = "interrupted: " + ex.GetType().Name;
                BellumCivileLogger.Log("Protection execution interrupted; client=" + p.Client?.StringId + "; " + ex);
                if (p.Attempts >= 3) Fail(p, "repeated_execution_failure");
            }
        }
        private static void Complete(CourtProtectionRecord p) { p.Phase = CourtProtectionPhase.Completed; p.ResultReason = "protection_fulfilled"; }
        private static void Fail(CourtProtectionRecord p, string reason) { p.Phase = CourtProtectionPhase.Failed; p.ResultReason = reason; }
    }
}
