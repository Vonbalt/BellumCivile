using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace BellumCivile.Behaviors
{
    public sealed partial class CourtAgendaBehavior
    {
        private CourtProtectionRecord _activeProtectionInquiry;
        private bool ProcessProtectionInquiry()
        {
            var report = _protectionOffers.FirstOrDefault(x => x.Terminal && x.PopupPending);
            if (report != null)
            {
                _activeProtectionInquiry = report;
                InformationManager.ShowInquiry(new InquiryData(new TextObject("{=BC_ProtectionTitle}A Crown Under Protection").ToString(),
                    ProtectionOutcome(report).ToString(), true, false, new TextObject("{=BC_ProtectionContinue}Continue").ToString(), null,
                    () => { if (_activeProtectionInquiry == report) { report.PopupPending = false; _activeProtectionInquiry = null; } }, null), true);
                return true;
            }
            var p = _protectionOffers.FirstOrDefault(x => x.Phase == CourtProtectionPhase.SenderDecision && x.ClientHouse == Clan.PlayerClan
                || x.Phase == CourtProtectionPhase.Offered && x.ProtectorHouse == Clan.PlayerClan && CampaignTime.Now.ToDays >= x.ReplyDay);
            if (p == null) return false;
            if (!ProtectionIdentity(p) || CampaignTime.Now.ToDays >= p.ReplyDeadline)
            { CloseProtection(p, CourtProtectionPhase.Cancelled, "player_offer_invalidated"); return false; }
            var assessment = new CourtProtectionAssessmentService().Assess(p.Client, p.Threat, p.Protector);
            if (!assessment.Eligible) { CloseProtection(p, CourtProtectionPhase.Cancelled, assessment.BlockReason); return false; }
            bool sender = p.Phase == CourtProtectionPhase.SenderDecision;
            var phase = p.Phase;
            string body = sender
                ? "{=BC_ProtectionSender}With {THREAT} threatening our realm, we may seek the protection of {PROTECTOR}. We would offer our submission in exchange for their intervention in this war.\n\nIf accepted, {CLIENT} will become their client kingdom: we will follow their foreign policy, join their wars, and end our other independent wars and foreign agreements where required.\n\nShall we send this appeal?"
                : "{=BC_ProtectionRecipient}Facing defeat at the hands of {THREAT}, {RULER} offers to place {CLIENT} under your protection as a client kingdom. In return, they ask you to take up their defense against {THREAT}.\n\nAcceptance commits you and your client states to that conflict, unless already engaged. The new client will follow your foreign policy and join your wars; its unrelated independent wars and foreign agreements will be settled under the ordinary terms of clientage.\n\n{BALANCE}";
            var ratio = assessment.AlliedStrength / assessment.EnemyStrength;
            var balance = new TextObject(ratio >= 1.5 ? "{=BC_ProtectionBalanceStrong}Our forces and those of the new client appear to hold a clear advantage over the named enemy and its client states."
                : ratio >= .85 ? "{=BC_ProtectionBalanceEven}Our forces and those of the new client appear broadly matched against the named enemy and its client states."
                : "{=BC_ProtectionBalanceWeak}Even with the new client's forces, the named enemy and its client states appear stronger than our side.");
            var text = ProtectionText(p, body).SetTextVariable("BALANCE", balance);
            if (assessment.OtherEnemies.Count > 0)
                text = new TextObject("{=BC_ProtectionOtherFronts}{BODY}\n\n{PROTECTOR} and its client states also face {ENEMIES}. Their forces must meet those commitments as well.")
                    .SetTextVariable("BODY", text).SetTextVariable("PROTECTOR", p.Protector.Name)
                    .SetTextVariable("ENEMIES", string.Join(", ", assessment.OtherEnemies.Select(k => k.Name.ToString())));
            _activeProtectionInquiry = p;
            InformationManager.ShowInquiry(new InquiryData(new TextObject("{=BC_ProtectionTitle}A Crown Under Protection").ToString(), text.ToString(), true, true,
                new TextObject(sender ? "{=BC_ProtectionSend}Send the appeal" : "{=BC_ProtectionAccept}Accept their submission and defend them").ToString(),
                new TextObject("{=BC_ProtectionReject}Decline the proposal").ToString(), () =>
                {
                    if (!ValidProtectionCallback(p, phase)) return;
                    _activeProtectionInquiry = null;
                    if (sender) SendProtection(p); else AcceptProtection(p);
                }, () =>
                {
                    if (!ValidProtectionCallback(p, phase)) return;
                    _activeProtectionInquiry = null;
                    CloseProtection(p, CourtProtectionPhase.Declined, sender ? "player_withheld_appeal" : "player_declined_protection");
                }), true);
            return true;
        }

        private bool ValidProtectionCallback(CourtProtectionRecord p, CourtProtectionPhase phase) => _activeProtectionInquiry == p
            && _protectionOffers.Contains(p) && p.Phase == phase && ProtectionIdentity(p)
            && (phase == CourtProtectionPhase.SenderDecision ? p.ClientHouse : p.ProtectorHouse) == Clan.PlayerClan
            && CampaignTime.Now.ToDays < p.ReplyDeadline;
    }
}
