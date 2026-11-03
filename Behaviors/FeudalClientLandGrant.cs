using System;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Election;
using TaleWorlds.CampaignSystem.Settlements;

namespace BellumCivile.Behaviors
{
    public sealed partial class FeudalTitleBehavior
    {
        private Settlement _clientLandGrantInProgress;
        internal bool IsPendingClientGrantCustody(FeudalTitleRecord title, Clan holder) => IsAllocationCustodian(title.TitleId, holder.StringId);

        internal bool CompleteClientLandGrant(CourtClientGrantRecord grant)
        {
            // A ballot may already have awarded the land before an interrupted agenda resumes.
            if (grant.DistributionStarted)
                return EnsureClientGrantAllocation(grant);
            var title = GetTitle(grant.TitleId);
            if (title?.IsActive != true || !grant.Paid || !grant.TransferAttempted
                || (grant.Fief.OwnerClan != grant.Grantor && grant.Fief.OwnerClan != grant.Recipient)
                || (title.DeFactoHolderClanId != grant.Grantor.StringId && title.DeFactoHolderClanId != grant.Recipient.StringId)
                || (title.DeJureHolderClanId != grant.OldLegal
                    && !(grant.OldLegal == grant.Grantor.StringId && title.DeJureHolderClanId == grant.Recipient.StringId))) return false;
            // Transfer the realm's gift without pretending it was a siege. The client ruler is
            // only its custodian until the ordinary claimant ballot awards the holding.
            var previous = _clientLandGrantInProgress;
            _clientLandGrantInProgress = grant.Fief;
            try
            {
                if (grant.Fief.OwnerClan == grant.Grantor)
                {
                    RecordAllocationCustody(grant.Fief, grant.Recipient);
                    BellumTreatyTransferContext.Run(() => ChangeOwnerOfSettlementAction.ApplyByDefault(grant.Beneficiary, grant.Fief));
                }
            }
            finally
            {
                _clientLandGrantInProgress = previous;
                if (grant.Fief.OwnerClan != grant.Recipient && IsAllocationCustodian(title.TitleId, grant.Recipient.StringId))
                    _allocationCustodianByTitle.Remove(title.TitleId);
            }
            if (grant.Fief.OwnerClan != grant.Recipient) return false;
            if (grant.OldLegal != grant.Recipient.StringId)
                _allocationCustodianByTitle[title.TitleId] = grant.Recipient.StringId;
            title.SetDeFactoHolder(grant.Recipient.StringId);
            title.SetDeJureHolder(grant.OldLegal == grant.Grantor.StringId ? grant.Recipient.StringId : grant.OldLegal);
            title.SetAssociatedKingdom(grant.Client.StringId);
            title.MarkSynced(CurrentDay);
            if (!IsAllocationCustodian(title.TitleId, grant.Recipient.StringId))
                RemoveRedundantClaimsForHolder(grant.Recipient, title);
            if (grant.OldLegal == grant.Grantor.StringId) RemoveClaimsForClanToTitle(grant.Grantor, title);
            RebuildRuntimeIndexes();
            ReconcileAllDeFactoParents("land granted to client realm");
            RebuildRuntimeIndexes();
            QueueServiceReviewForTitleAndChildren(title, "land granted to client realm");
            var wars = Campaign.Current?.GetCampaignBehavior<WarScoreBehavior>()?.GetActiveWars();
            if (wars != null)
                foreach (var war in wars)
                    if (war.ConflictType == WarScoreConflictType.ForeignWar)
                        war.GetSnapshot(grant.Fief.StringId)?.RecordVoluntaryGrant(grant.Grantor.Kingdom.StringId,
                            grant.Grantor.StringId, grant.Client.StringId, grant.Recipient.StringId);
            grant.Fief.Town.IsOwnerUnassigned = true;
            grant.DistributionStarted = true;
            return EnsureClientGrantAllocation(grant);
        }

        private static bool EnsureClientGrantAllocation(CourtClientGrantRecord grant)
        {
            if (grant.Fief?.Town == null || grant.Fief.OwnerClan?.Kingdom != grant.Client)
                return false;
            if (!grant.Fief.Town.IsOwnerUnassigned)
                return true;

            var votes = Campaign.Current?.GetCampaignBehavior<FiefDeliberationBehavior>();
            if (votes?.HasPendingFiefVoteForSettlement(grant.Client, grant.Fief) == true
                || votes?.HasOpenedAllocation(grant.Client, grant.Fief) == true
                || grant.Client.UnresolvedDecisions.Any(d => d is SettlementClaimantDecision allocation && allocation.Settlement == grant.Fief))
                return true;
            if (votes?.QueueTreatySettlementVote(grant.Fief, grant.Client.RulingClan) == true)
                return true;

            throw new InvalidOperationException("The client land grant is awaiting its distribution ballot.");
        }
    }
}
