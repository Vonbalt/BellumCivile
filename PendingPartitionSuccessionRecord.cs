using TaleWorlds.CampaignSystem;
using TaleWorlds.SaveSystem;

namespace BellumCivile
{
    /// <summary>
    /// Captures the landed state and natural heirs of an NPC clan at the moment
    /// its leader dies. The actual partition runs on a later daily tick, after
    /// vanilla has safely installed the new clan leader.
    /// </summary>
    public class PendingPartitionSuccessionRecord
    {
        [SaveableField(1)] private string _deadLeaderId;
        [SaveableField(2)] private string _parentClanId;
        [SaveableField(3)] private string _kingdomId;
        [SaveableField(4)] private string _fiefIds;
        [SaveableField(5)] private string _heirIds;
        [SaveableField(6)] private CampaignTime _readyDate;
        [SaveableField(7)] private bool _parentWasRulingClanAtDeath;
        [SaveableField(8)] private string _titleIds;
        [SaveableField(9)] private string _primarySovereignTitleId;
        [SaveableField(10)] public System.Collections.Generic.List<CrownPartitionPromotionRecord> CrownPromotions;
        [SaveableField(11)] public CrownPartitionBatchRecord CrownBatch;
        [SaveableField(12)] public System.Collections.Generic.List<CrownAccessionRecord> GoldPayments;
        [SaveableField(13)] public System.Collections.Generic.List<FeudalClaimRecord> OriginalClaims;
        [SaveableField(14)] public bool ClaimsStarted;
        [SaveableField(15)] public bool ClaimsReturned;
        [SaveableField(16)] public System.Collections.Generic.List<CrossClanEstateShare> EstateShares;
        [SaveableField(17)] public bool CrownRoutingRequested;
        [SaveableField(18)] public string CrownRoutingFailure;
        [SaveableField(19)] internal bool IntegrationPublished;

        public string DeadLeaderId => _deadLeaderId;
        public string ParentClanId => _parentClanId;
        public string KingdomId => _kingdomId;
        public string FiefIds => _fiefIds;
        public string HeirIds => _heirIds;
        public string TitleIds => _titleIds ?? string.Empty;
        public string PrimarySovereignTitleId => _primarySovereignTitleId ?? string.Empty;
        public CampaignTime ReadyDate { get => _readyDate; set => _readyDate = value; }
        public bool ParentWasRulingClanAtDeath => _parentWasRulingClanAtDeath;

        internal bool TryApplyClaimSettlement(System.Action apply, out string reason)
        {
            reason = "claim settlement requires the original claim snapshot and an application callback";
            if (OriginalClaims == null || apply == null) return false;
            if (ClaimsStarted != ClaimsReturned)
            { reason = "claim settlement was interrupted or has inconsistent receipts; it will not be replayed"; return false; }
            if (ClaimsReturned) { reason = null; return true; }
            try
            {
                ClaimsStarted = true;
                apply();
                ClaimsReturned = true;
                reason = null;
                return true;
            }
            catch (System.Exception ex)
            {
                reason = "claim settlement interrupted: " + ex.Message;
                return false;
            }
        }

        public PendingPartitionSuccessionRecord(
            string deadLeaderId,
            string parentClanId,
            string kingdomId,
            string fiefIds,
            string heirIds,
            CampaignTime readyDate,
            bool parentWasRulingClanAtDeath = false,
            string titleIds = "",
            string primarySovereignTitleId = "")
        {
            _deadLeaderId = deadLeaderId ?? string.Empty;
            _parentClanId = parentClanId ?? string.Empty;
            _kingdomId = kingdomId ?? string.Empty;
            _fiefIds = fiefIds ?? string.Empty;
            _heirIds = heirIds ?? string.Empty;
            _readyDate = readyDate;
            _parentWasRulingClanAtDeath = parentWasRulingClanAtDeath;
            _titleIds = titleIds ?? string.Empty;
            _primarySovereignTitleId = primarySovereignTitleId ?? string.Empty;
        }
    }
}
