using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Settlements;

namespace BellumCivile.Behaviors
{
    public partial class FeudalTitleBehavior
    {
        // Separate from the vote queue: that queue may be removed before ownership events fire.
        private Dictionary<string, string> _allocationCustodianByTitle = new Dictionary<string, string>();

        internal void RecordAllocationCustody(Settlement settlement, Clan custodian)
        {
            if (settlement == null || custodian == null || settlement.OwnerClan == custodian)
                return;
            string titleId = BuildBaronyTitleId(settlement);
            if (GetTitle(titleId)?.DeJureHolderClanId == custodian.StringId)
                return;
            _allocationCustodianByTitle[titleId] = custodian.StringId;
            BellumCivileLogger.Log($"Feudal allocation custody recorded; title={titleId}; custodian={custodian.StringId}.");
        }

        private bool ConsumeAllocationCustody(string titleId, string previousHolderId, string newHolderId,
            ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail detail)
        {
            if (!_allocationCustodianByTitle.TryGetValue(titleId, out string custodian))
                return false;
            // The initial transfer into custody is not distribution of the estate.
            if (custodian != previousHolderId && custodian == newHolderId
                && detail != ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail.BySiege)
                return false;
            bool completed = detail == ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail.ByKingDecision
                || detail == ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail.BySiege
                || previousHolderId != newHolderId;
            if (!completed)
                return false;
            _allocationCustodianByTitle.Remove(titleId);
            return custodian == previousHolderId;
        }

        private bool IsAllocationCustodian(string titleId, string clanId)
            => _allocationCustodianByTitle.TryGetValue(titleId, out string custodian) && custodian == clanId;
    }
}
