using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace BellumCivile.Behaviors
{
    public sealed partial class HostagePactBehavior
    {
        internal void RecoverTreatyDeliveries()
        {
            if (_maintaining) return;
            foreach (var pact in _pacts.Where(p => p?.Phase == HostagePactPhase.Preparing
                && p.TreatySettlementStarted).ToList()) RecoverTreatyDelivery(pact.TreatyProposalId);
        }

        internal void RecoverTreatyDelivery(string proposalId)
        {
            if (_maintaining || string.IsNullOrEmpty(proposalId)) return;
            var pact = _pacts.FirstOrDefault(p => p?.TreatyProposalId == proposalId && p.Phase == HostagePactPhase.Preparing);
            if (pact == null) return;
            var proposal = Campaign.Current?.GetCampaignBehavior<ForeignTreatyBehavior>()?.GetProposals()
                .FirstOrDefault(p => p.ProposalId == proposalId);
            bool completed = pact.TreatySettlementCompleted || proposal?.State == TreatyProposalState.Applied;
            bool intact = pact.FirstRealm != null && pact.SecondRealm != null
                && !pact.FirstRealm.IsEliminated && !pact.SecondRealm.IsEliminated
                && pact.FirstRealm.RulingClan == pact.FirstHouse && pact.SecondRealm.RulingClan == pact.SecondHouse;
            if (completed && intact && !pact.FirstRealm.IsAtWarWith(pact.SecondRealm)
                && (pact.FirstHostage == null || InTreatyCustody(pact.FirstHostage))
                && (pact.SecondHostage == null || InTreatyCustody(pact.SecondHostage)))
            {
                pact.TreatySettlementCompleted = true;
                if (pact.TryActivate(pact.SigningDayRecorded ? pact.TreatySigningDay : CampaignTime.Now.ToDays)) return;
            }
            // No generic rollback exists for transferred gold/land. Stop this proposal from
            // executing again, release its security, and explicitly report the interruption.
            if (proposal != null && proposal.State != TreatyProposalState.Applied)
                proposal.SetState(TreatyProposalState.Cancelled, "hostage treaty settlement interrupted; concessions were not replayed", (float)CampaignTime.Now.ToDays);
            if (pact.FirstRealm != null && pact.SecondRealm != null)
                Campaign.Current?.GetCampaignBehavior<WarScoreBehavior>()?.GetActiveWar(pact.FirstRealm, pact.SecondRealm)?.EndParley();
            pact.TryAbortPreparation();
            if (!pact.Reported)
            {
                pact.Reported = true;
                BellumCivileLogger.Log($"Hostage treaty delivery cancelled during recovery; proposal={proposalId}; completed={completed}; no concession replay.");
                if (Clan.PlayerClan?.Kingdom == pact.FirstRealm || Clan.PlayerClan?.Kingdom == pact.SecondRealm)
                    InformationManager.DisplayMessage(new InformationMessage(new TextObject(
                        "{=BC_Hostage_SettlementInterrupted}The treaty's hostage pledge could not be completed. The hostages are being released. Concessions already carried out remain in effect; no further concessions will be imposed under this interrupted settlement.").ToString()));
            }
            MaintainCustody();
        }
    }
}
