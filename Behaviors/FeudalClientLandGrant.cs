using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Settlements;

namespace BellumCivile.Behaviors
{
    public sealed partial class FeudalTitleBehavior
    {
        private Settlement _clientLandGrantInProgress;
        internal bool IsPendingClientGrantCustody(FeudalTitleRecord title, Clan holder) => IsAllocationCustodian(title.TitleId, holder.StringId);

        internal bool CompleteClientLandGrant(CourtClientGrantRecord grant)
        {
            var title = GetTitle(grant.TitleId);
            if (title?.IsActive != true || !grant.Paid || !grant.TransferAttempted
                || (grant.Fief.OwnerClan != grant.Grantor && grant.Fief.OwnerClan != grant.Recipient)
                || (title.DeFactoHolderClanId != grant.Grantor.StringId && title.DeFactoHolderClanId != grant.Recipient.StringId)
                || (title.DeJureHolderClanId != grant.OldLegal
                    && !(grant.OldLegal == grant.Grantor.StringId && title.DeJureHolderClanId == grant.Recipient.StringId))) return false;
            // This voluntary cross-realm gift is neither a conquest nor a domestic fief allocation.
            var previous = _clientLandGrantInProgress;
            _clientLandGrantInProgress = grant.Fief;
            try
            {
                if (grant.Fief.OwnerClan == grant.Grantor)
                    BellumTreatyTransferContext.Run(() => ChangeOwnerOfSettlementAction.ApplyByDefault(grant.Beneficiary, grant.Fief));
            }
            finally { _clientLandGrantInProgress = previous; }
            if (grant.Fief.OwnerClan != grant.Recipient) return false;
            title.SetDeFactoHolder(grant.Recipient.StringId);
            title.SetDeJureHolder(grant.OldLegal == grant.Grantor.StringId ? grant.Recipient.StringId : grant.OldLegal);
            title.SetAssociatedKingdom(grant.Client.StringId);
            title.MarkSynced(CurrentDay);
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
            return true;
        }
    }
}
