using TaleWorlds.SaveSystem;

namespace BellumCivile
{
    public class FeudalTitleRecord
    {
        [SaveableField(1)] private string _titleId;
        [SaveableField(2)] private string _name;
        [SaveableField(3)] private FeudalTitleType _titleType;
        [SaveableField(4)] private string _deJureHolderClanId;
        [SaveableField(5)] private string _deFactoHolderClanId;
        [SaveableField(6)] private string _parentTitleId;
        [SaveableField(7)] private string _capitalSettlementId;
        [SaveableField(8)] private string _associatedKingdomId;
        [SaveableField(9)] private float _createdDay;
        [SaveableField(10)] private float _lastSyncedDay;
        [SaveableField(11)] private bool _isActive;
        [SaveableField(12)] private string _deFactoParentTitleId;
        [SaveableField(13)] private string _fallbackCultureRef;
        [SaveableField(14)] private bool _isDeliberatelyDissolved;

        public string TitleId => _titleId;
        public string Name => _name;
        public FeudalTitleType TitleType => _titleType;
        public string DeJureHolderClanId => _deJureHolderClanId;
        public string DeFactoHolderClanId => _deFactoHolderClanId;
        public string ParentTitleId => _parentTitleId;
        public string CapitalSettlementId => _capitalSettlementId;
        public string AssociatedKingdomId => _associatedKingdomId;
        public float CreatedDay => _createdDay;
        public float LastSyncedDay => _lastSyncedDay;
        public bool IsActive => _isActive;
        public string DeFactoParentTitleId => _deFactoParentTitleId ?? string.Empty;
        public string FallbackCultureRef => _fallbackCultureRef ?? string.Empty;
        public bool IsDeliberatelyDissolved => _isDeliberatelyDissolved;

        public FeudalTitleRecord(
            string titleId,
            string name,
            FeudalTitleType titleType,
            string deJureHolderClanId,
            string deFactoHolderClanId,
            string parentTitleId,
            string capitalSettlementId,
            string associatedKingdomId,
            float createdDay,
            float lastSyncedDay,
            bool isActive = true,
            string fallbackCultureRef = "")
        {
            _titleId = titleId ?? string.Empty;
            _name = name ?? string.Empty;
            _titleType = titleType;
            _deJureHolderClanId = deJureHolderClanId ?? string.Empty;
            _deFactoHolderClanId = deFactoHolderClanId ?? string.Empty;
            _parentTitleId = parentTitleId ?? string.Empty;
            _deFactoParentTitleId = _parentTitleId;
            _capitalSettlementId = capitalSettlementId ?? string.Empty;
            _associatedKingdomId = associatedKingdomId ?? string.Empty;
            _createdDay = createdDay;
            _lastSyncedDay = lastSyncedDay;
            _isActive = isActive;
            _fallbackCultureRef = fallbackCultureRef ?? string.Empty;
        }

        public void SetName(string name)
        {
            _name = name ?? string.Empty;
        }

        public void SetDeJureHolder(string clanId)
        {
            _deJureHolderClanId = clanId ?? string.Empty;
        }

        public void SetDeFactoHolder(string clanId)
        {
            _deFactoHolderClanId = clanId ?? string.Empty;
        }

        public void SetParentTitle(string titleId)
        {
            _parentTitleId = titleId ?? string.Empty;
        }

        public void SetDeFactoParentTitle(string titleId)
        {
            _deFactoParentTitleId = titleId ?? string.Empty;
        }

        public void InitializeDeFactoParentFromDeJure(bool force)
        {
            if (force || _deFactoParentTitleId == null)
                _deFactoParentTitleId = _parentTitleId ?? string.Empty;
        }

        public void SetAssociatedKingdom(string kingdomId)
        {
            _associatedKingdomId = kingdomId ?? string.Empty;
        }

        public void SetFallbackCulture(string cultureRef)
        {
            _fallbackCultureRef = cultureRef ?? string.Empty;
        }

        public void SetCapitalSettlement(string settlementId)
        {
            _capitalSettlementId = settlementId ?? string.Empty;
        }

        public void MarkSynced(float day)
        {
            _lastSyncedDay = day;
        }

        public void SetActive(bool active)
        {
            _isActive = active;
            if (active)
                _isDeliberatelyDissolved = false;
        }

        public void SetDeliberatelyDissolved(bool dissolved)
        {
            _isDeliberatelyDissolved = dissolved;
            if (dissolved)
                _isActive = false;
        }
    }
}
