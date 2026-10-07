using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Localization;
using C = BellumCivile.BellumCivileConstants;

namespace BellumCivile.Behaviors
{
    /// <summary>
    /// Central legal registry for landed titles. This behavior owns the title and claim records;
    /// other Bellum systems should consult it instead of separately inferring de jure rights.
    /// </summary>
    public partial class FeudalTitleBehavior : CampaignBehaviorBase
    {
        private const float RegistryIntegrityAuditIntervalDays = 30f;
        private const int CurrentHierarchySchemaVersion = 2;
        public static FeudalTitleBehavior Instance { get; private set; }

        private Dictionary<string, FeudalTitleRecord> _titlesById = new Dictionary<string, FeudalTitleRecord>();
        private List<FeudalClaimRecord> _claims = new List<FeudalClaimRecord>();
        private Dictionary<string, string> _independentRealmSourceTitleByKingdomId = new Dictionary<string, string>();
        private Dictionary<string, string> _currentRealmTitleByKingdomId = new Dictionary<string, string>();
        private Dictionary<string, string> _realmIdentityRoots = new Dictionary<string, string>();
        private Dictionary<string, string> _realmIdentityNativeNames = new Dictionary<string, string>();
        private Dictionary<string, string> _realmTitleStyleSources = new Dictionary<string, string>();
        private Dictionary<string, string> _historicalRealmSovereignTitleByKingdomId = new Dictionary<string, string>();
        private Dictionary<string, string> _playerTitleNameOverrides = new Dictionary<string, string>();
        private Dictionary<string, string> _subinfeudationParentByTitleId = new Dictionary<string, string>();
        private int _hierarchySchemaVersion;

        private readonly Dictionary<string, string> _baronyTitleBySettlementId = new Dictionary<string, string>();
        private readonly Dictionary<string, List<string>> _titlesByDeJureHolder = new Dictionary<string, List<string>>();
        private readonly Dictionary<string, List<string>> _titlesByDeFactoHolder = new Dictionary<string, List<string>>();
        private readonly Dictionary<string, List<string>> _claimsByClan = new Dictionary<string, List<string>>();
        private readonly Dictionary<string, Dictionary<string, List<FeudalClaimRecord>>> _claimsByClanAndTitle = new Dictionary<string, Dictionary<string, List<FeudalClaimRecord>>>();
        private readonly Dictionary<string, List<FeudalClaimRecord>> _claimsByTitle = new Dictionary<string, List<FeudalClaimRecord>>();
        private readonly Dictionary<string, FeudalClaimRecord> _claimsById = new Dictionary<string, FeudalClaimRecord>();
        private readonly Dictionary<string, List<string>> _childrenByParentTitleId = new Dictionary<string, List<string>>();
        private readonly Dictionary<string, List<string>> _childrenByDeFactoParentTitleId = new Dictionary<string, List<string>>();
        private readonly Dictionary<string, float> _titleDistanceCache = new Dictionary<string, float>();
        private readonly Dictionary<string, int> _deFactoAuthorityBaronyCountByClanId = new Dictionary<string, int>();
        private readonly Dictionary<string, float> _orphanedUpperTitleFirstSeenDay = new Dictionary<string, float>();
        private readonly HashSet<string> _knownClanLeaderIds = new HashSet<string>();
        private readonly Dictionary<string, Clan> _clansByReferenceId = new Dictionary<string, Clan>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Clan> _clansByReferenceName = new Dictionary<string, Clan>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Clan> _clansByLeaderReferenceName = new Dictionary<string, Clan>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Kingdom> _kingdomsByReferenceId = new Dictionary<string, Kingdom>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Kingdom> _kingdomsByReferenceName = new Dictionary<string, Kingdom>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Settlement> _settlementsByReferenceId = new Dictionary<string, Settlement>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<Settlement>> _settlementsByNormalizedReference = new Dictionary<string, List<Settlement>>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _configuredTitleIdsCache = new HashSet<string>(StringComparer.Ordinal);
        private int _deFactoAuthorityCacheRevision = -1;
        private bool _referenceLookupCachesReady;
        private bool _configuredTitleIdsCacheReady;
        private bool _registryRepairRequested;
        private float _nextRegistryIntegrityAuditDay = float.MaxValue;
        private string _deFactoOnlySettlementTransferTitleId;
        private SubinfeudationGrantContext _activeSubinfeudationGrant;
        private bool _titleDisplayRefreshPending;
        private int _lastTitleDisplayRefreshRevision = -1;
        private bool _sessionLaunched;
        private int _claimIndexBatchDepth;
        private bool _claimIndexRebuildPending;

        public int RuntimeRevision { get; private set; }
        public int DisplayRevision { get; private set; }

        public override void RegisterEvents()
        {
            Instance = this;
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
            CampaignEvents.HourlyTickEvent.AddNonSerializedListener(this, OnHourlyTick);
            CampaignEvents.WeeklyTickEvent.AddNonSerializedListener(this, OnWeeklyTick);
            CampaignEvents.OnSettlementOwnerChangedEvent.AddNonSerializedListener(this, OnSettlementOwnerChanged);
            CampaignEvents.OnClanChangedKingdomEvent.AddNonSerializedListener(this, OnClanChangedKingdom);
            CampaignEvents.OnClanCreatedEvent.AddNonSerializedListener(this, OnClanCreated);
            CampaignEvents.RulingClanChanged.AddNonSerializedListener(this, OnRulingClanChanged);
            CampaignEvents.KingdomDestroyedEvent.AddNonSerializedListener(this, OnKingdomDestroyed);
            CampaignEvents.HeroKilledEvent.AddNonSerializedListener(this, OnHeroKilled);
            CampaignEvents.BeforeHeroesMarried.AddNonSerializedListener(this, OnBeforeHeroesMarried);
        }

        public override void SyncData(IDataStore dataStore)
        {
            if (!dataStore.IsLoading && _hierarchySchemaVersion < CurrentHierarchySchemaVersion)
                _hierarchySchemaVersion = CurrentHierarchySchemaVersion;

            dataStore.SyncData("BellumCivile_FeudalTitles", ref _titlesById);
            dataStore.SyncData("BellumCivile_FeudalClaims", ref _claims);
            dataStore.SyncData("BellumCivile_AllocationCustodians", ref _allocationCustodianByTitle);
            if (_allocationCustodianByTitle == null)
                _allocationCustodianByTitle = new Dictionary<string, string>();
            dataStore.SyncData("BellumCivile_IndependentRealmSourceTitles", ref _independentRealmSourceTitleByKingdomId);
            dataStore.SyncData("BellumCivile_CurrentRealmTitles", ref _currentRealmTitleByKingdomId);
            dataStore.SyncData("BellumCivile_RealmIdentityRoots", ref _realmIdentityRoots);
            dataStore.SyncData("BellumCivile_RealmIdentityNativeNames", ref _realmIdentityNativeNames);
            dataStore.SyncData("BellumCivile_RealmTitleStyleSources", ref _realmTitleStyleSources);
            dataStore.SyncData("BellumCivile_HistoricalRealmSovereignTitles", ref _historicalRealmSovereignTitleByKingdomId);
            dataStore.SyncData("BellumCivile_PlayerTitleNameOverrides", ref _playerTitleNameOverrides);
            dataStore.SyncData("BellumCivile_SubinfeudationParents", ref _subinfeudationParentByTitleId);
            dataStore.SyncData("BellumCivile_FeudalHierarchySchemaVersion", ref _hierarchySchemaVersion);
            EnsureCollectionsInitialized();
            if (dataStore.IsLoading)
                MigrateDeFactoHierarchyIfNeeded();
            DeactivateTemporaryFeudKingdomTitles("load repair");
            int duplicateClaimsDeactivated = dataStore.IsLoading
                ? NormalizeActiveClaimDuplicates()
                : 0;
            RebuildRuntimeIndexes();
            if (duplicateClaimsDeactivated > 0)
                BellumCivileLogger.Log($"Feudal claim load repair deactivated duplicate active clan/title claims; duplicates={duplicateClaimsDeactivated}.");
        }

        public FeudalTitleRecord GetTitle(string titleId)
        {
            EnsureCollectionsInitialized();
            return !string.IsNullOrWhiteSpace(titleId) && _titlesById.TryGetValue(titleId, out FeudalTitleRecord title)
                ? title
                : null;
        }

        public IReadOnlyCollection<FeudalTitleRecord> GetAllTitles()
        {
            EnsureCollectionsInitialized();
            return _titlesById.Values.ToList();
        }

        public IReadOnlyCollection<FeudalClaimRecord> GetAllClaims()
        {
            EnsureCollectionsInitialized();
            return _claims.ToList();
        }

        public IReadOnlyCollection<FeudalClaimRecord> GetActiveClaims()
        {
            EnsureCollectionsInitialized();
            return _claims.Where(IsClaimCurrentlyActive).ToList();
        }

        public IReadOnlyCollection<FeudalClaimRecord> GetActiveClaimsByClan(Clan clan)
        {
            EnsureCollectionsInitialized();
            if (clan == null || string.IsNullOrWhiteSpace(clan.StringId))
                return Array.Empty<FeudalClaimRecord>();

            if (!_claimsByClan.TryGetValue(clan.StringId, out List<string> claimIds))
                return Array.Empty<FeudalClaimRecord>();

            List<FeudalClaimRecord> activeClaims = new List<FeudalClaimRecord>(claimIds.Count);
            foreach (string claimId in claimIds)
            {
                if (_claimsById.TryGetValue(claimId, out FeudalClaimRecord claim) && IsClaimCurrentlyActive(claim))
                    activeClaims.Add(claim);
            }

            return activeClaims;
        }

        public IReadOnlyCollection<FeudalClaimRecord> GetActiveClaimsByTitle(FeudalTitleRecord title)
        {
            EnsureCollectionsInitialized();
            if (title == null || string.IsNullOrWhiteSpace(title.TitleId))
                return Array.Empty<FeudalClaimRecord>();

            if (!_claimsByTitle.TryGetValue(title.TitleId, out List<FeudalClaimRecord> claims))
                return Array.Empty<FeudalClaimRecord>();

            return claims.Where(IsClaimCurrentlyActive).ToList();
        }

        public IReadOnlyCollection<FeudalClaimRecord> GetActiveClaims(Clan clan, FeudalTitleRecord title)
        {
            EnsureCollectionsInitialized();
            if (clan == null
                || title == null
                || string.IsNullOrWhiteSpace(clan.StringId)
                || string.IsNullOrWhiteSpace(title.TitleId))
            {
                return Array.Empty<FeudalClaimRecord>();
            }

            if (!_claimsByClanAndTitle.TryGetValue(clan.StringId, out Dictionary<string, List<FeudalClaimRecord>> claimsByTitle)
                || !claimsByTitle.TryGetValue(title.TitleId, out List<FeudalClaimRecord> claims))
            {
                return Array.Empty<FeudalClaimRecord>();
            }

            return claims.Where(IsClaimCurrentlyActive).ToList();
        }

        public FeudalClaimRecord GetStrongestActiveClaim(Clan clan, FeudalTitleRecord title)
        {
            return GetActiveClaims(clan, title)
                .OrderByDescending(claim => claim.Strength)
                .ThenByDescending(claim => claim.CreatedDay)
                .FirstOrDefault();
        }

        public bool TryRenounceExplicitClaim(
            Clan claimantClan,
            FeudalTitleRecord title,
            out FeudalClaimStrength renouncedStrength,
            out string reason)
        {
            renouncedStrength = FeudalClaimStrength.Weak;
            reason = "no active explicit claim exists";
            EnsureCollectionsInitialized();
            if (claimantClan == null || title == null || !title.IsActive)
                return false;

            List<FeudalClaimRecord> claims = _claims
                .Where(claim => claim != null
                    && IsClaimCurrentlyActive(claim)
                    && claim.ClaimantClanId == claimantClan.StringId
                    && claim.TargetTitleId == title.TitleId)
                .ToList();
            if (claims.Count == 0)
                return false;

            renouncedStrength = claims.Max(claim => claim.Strength);
            foreach (FeudalClaimRecord claim in claims)
                claim.SetActive(false);
            RebuildRuntimeIndexes();
            reason = $"claimant={claimantClan.StringId}; title={title.TitleId}; strength={renouncedStrength}";
            BellumCivileLogger.Log($"Feudal claim publicly renounced; {reason}.");
            return true;
        }

        public IReadOnlyCollection<FeudalTitleRecord> GetChildTitles(FeudalTitleRecord parentTitle)
        {
            return GetChildTitles(parentTitle, FeudalHierarchyMode.DeJure);
        }

        public IReadOnlyCollection<FeudalTitleRecord> GetChildTitles(FeudalTitleRecord parentTitle, FeudalHierarchyMode mode)
        {
            EnsureCollectionsInitialized();
            if (parentTitle == null || string.IsNullOrWhiteSpace(parentTitle.TitleId))
                return new List<FeudalTitleRecord>();

            Dictionary<string, List<string>> index = mode == FeudalHierarchyMode.DeFacto
                ? _childrenByDeFactoParentTitleId
                : _childrenByParentTitleId;
            if (!index.TryGetValue(parentTitle.TitleId, out List<string> childIds))
                return new List<FeudalTitleRecord>();

            return childIds
                .Select(GetTitle)
                .Where(title => title != null && title.IsActive)
                .OrderBy(title => title.TitleType)
                .ThenBy(title => title.Name)
                .ToList();
        }

        public IReadOnlyCollection<FeudalTitleRecord> GetTitleAndDescendants(FeudalTitleRecord rootTitle)
        {
            return GetTitleAndDescendants(rootTitle, FeudalHierarchyMode.DeJure);
        }

        public IReadOnlyCollection<FeudalTitleRecord> GetTitleAndDescendants(FeudalTitleRecord rootTitle, FeudalHierarchyMode mode)
        {
            EnsureCollectionsInitialized();
            List<FeudalTitleRecord> titles = new List<FeudalTitleRecord>();
            CollectTitleAndDescendants(rootTitle, titles, new HashSet<string>(), mode);
            return titles;
        }

        public FeudalTitleRecord GetParentTitle(FeudalTitleRecord title, FeudalHierarchyMode mode)
        {
            if (title == null)
                return null;

            string parentId = mode == FeudalHierarchyMode.DeFacto
                ? title.DeFactoParentTitleId
                : title.ParentTitleId;
            return GetTitle(parentId);
        }

        public FeudalTitleRecord GetHighestDeFactoTitleHeldByClan(Clan clan)
        {
            EnsureCollectionsInitialized();
            if (clan == null || string.IsNullOrWhiteSpace(clan.StringId))
                return null;

            return GetTitlesHeldByClan(clan, deJure: false)
                .Where(title => title != null && title.IsActive)
                .OrderByDescending(title => title.TitleType)
                .ThenByDescending(title => string.Equals(title.DeJureHolderClanId, clan.StringId, StringComparison.Ordinal))
                .ThenBy(title => title.TitleId)
                .FirstOrDefault();
        }

        public bool IsServiceDecoupledByRank(FeudalTitleRecord title)
        {
            FeudalTitleRecord legalParent = GetParentTitle(title, FeudalHierarchyMode.DeJure);
            FeudalTitleRecord effectiveParent = GetParentTitle(title, FeudalHierarchyMode.DeFacto);
            return IsServiceDecoupledByRank(title, legalParent, effectiveParent);
        }

        public int CountDeFactoAuthorityBaronies(Clan clan)
        {
            EnsureCollectionsInitialized();
            if (clan == null || string.IsNullOrWhiteSpace(clan.StringId))
                return 0;

            EnsureDeFactoAuthorityCache();
            return _deFactoAuthorityBaronyCountByClanId.TryGetValue(clan.StringId, out int count)
                ? count
                : 0;
        }

        private void EnsureDeFactoAuthorityCache()
        {
            if (_deFactoAuthorityCacheRevision == RuntimeRevision)
                return;

            _deFactoAuthorityBaronyCountByClanId.Clear();
            foreach (FeudalTitleRecord barony in _titlesById.Values)
            {
                if (barony == null || !barony.IsActive || barony.TitleType != FeudalTitleType.Barony)
                    continue;

                HashSet<string> visitedTitles = new HashSet<string>();
                HashSet<string> creditedClans = new HashSet<string>();
                FeudalTitleRecord current = barony;
                int guard = 0;
                while (current != null && guard++ < 32 && visitedTitles.Add(current.TitleId))
                {
                    string holderId = current.DeFactoHolderClanId;
                    if (!string.IsNullOrWhiteSpace(holderId) && creditedClans.Add(holderId))
                    {
                        _deFactoAuthorityBaronyCountByClanId.TryGetValue(holderId, out int existing);
                        _deFactoAuthorityBaronyCountByClanId[holderId] = existing + 1;
                    }

                    current = !string.IsNullOrWhiteSpace(current.DeFactoParentTitleId)
                        && _titlesById.TryGetValue(current.DeFactoParentTitleId, out FeudalTitleRecord parent)
                            ? parent
                            : null;
                }
            }

            _deFactoAuthorityCacheRevision = RuntimeRevision;
        }

        public float CalculateDeFactoAuthorityInfluence(Clan clan)
        {
            return CountDeFactoAuthorityBaronies(clan) * C.FeudalAuthorityInfluencePerBarony;
        }

        public bool IsClanWithinDeFactoAuthority(Clan possibleVassal, Clan possibleLiege)
        {
            EnsureCollectionsInitialized();
            if (possibleVassal == null
                || possibleLiege == null
                || possibleVassal == possibleLiege
                || string.IsNullOrWhiteSpace(possibleVassal.StringId)
                || string.IsNullOrWhiteSpace(possibleLiege.StringId))
            {
                return false;
            }

            foreach (FeudalTitleRecord title in GetTitlesHeldByClan(possibleVassal, deJure: false))
            {
                if (DoesDeFactoAuthorityFlowFromTitleToClan(title, possibleLiege, includeHeldTitle: false))
                    return true;
            }

            return false;
        }

        private bool DoesDeFactoAuthorityFlowToClan(FeudalTitleRecord title, Clan clan)
        {
            return DoesDeFactoAuthorityFlowFromTitleToClan(title, clan, includeHeldTitle: true);
        }

        public bool IsTitleWithinDeFactoAuthority(FeudalTitleRecord title, Clan clan)
        {
            EnsureCollectionsInitialized();
            return DoesDeFactoAuthorityFlowToClan(title, clan);
        }

        private bool DoesDeFactoAuthorityFlowFromTitleToClan(FeudalTitleRecord title, Clan clan, bool includeHeldTitle)
        {
            if (title == null || clan == null || string.IsNullOrWhiteSpace(clan.StringId))
                return false;

            FeudalTitleRecord current = includeHeldTitle ? title : GetParentTitle(title, FeudalHierarchyMode.DeFacto);
            HashSet<string> visited = new HashSet<string>();
            int guard = 0;
            while (current != null && guard++ < 32 && visited.Add(current.TitleId))
            {
                if (string.Equals(current.DeFactoHolderClanId, clan.StringId, StringComparison.Ordinal))
                    return true;

                current = GetTitle(current.DeFactoParentTitleId);
            }

            return false;
        }

        public bool AreTitlesAdjacentForTitleLogic(FeudalTitleRecord firstTitle, FeudalTitleRecord secondTitle)
        {
            EnsureCollectionsInitialized();
            return AreTitlesAdjacent(firstTitle, secondTitle);
        }

        public IReadOnlyCollection<FeudalTitleRecord> GetDescendantBaronyTitles(FeudalTitleRecord rootTitle)
        {
            return GetDescendantBaronyTitles(rootTitle, FeudalHierarchyMode.DeJure);
        }

        public IReadOnlyCollection<FeudalTitleRecord> GetDescendantBaronyTitles(FeudalTitleRecord rootTitle, FeudalHierarchyMode mode)
        {
            return GetTitleAndDescendants(rootTitle, mode)
                .Where(title => title != null && title.IsActive && title.TitleType == FeudalTitleType.Barony)
                .ToList();
        }

        public void RegisterIndependentRealmShell(Kingdom kingdom, FeudalTitleRecord sourceTitle, string reason)
        {
            EnsureCollectionsInitialized();
            if (kingdom == null || sourceTitle == null || string.IsNullOrWhiteSpace(kingdom.StringId) || string.IsNullOrWhiteSpace(sourceTitle.TitleId))
                return;

            _independentRealmSourceTitleByKingdomId[kingdom.StringId] = sourceTitle.TitleId;
            RememberRealmIdentity(kingdom, sourceTitle);
            sourceTitle.SetAssociatedKingdom(kingdom.StringId);
            sourceTitle.MarkSynced(CurrentDay);
            RebuildRuntimeIndexes();
            QueueServiceReviewForTitleAndChildren(sourceTitle, reason);
            BellumCivileLogger.Log($"Registered independent realm shell; kingdom={kingdom.StringId}; source_title={sourceTitle.TitleId}; type={sourceTitle.TitleType}; reason={reason ?? "unknown"}.");
        }

        private void RememberRealmIdentity(Kingdom kingdom, FeudalTitleRecord sourceTitle)
        {
            if (_realmIdentityRoots.ContainsKey(kingdom.StringId) || sourceTitle == null) return;
            string native = DynamicKingdomTitleNameHelper.GetNativeName(kingdom);
            if (native != FeudalTitleDisplayHelper.FormatTitleName(sourceTitle, kingdom.RulingClan)) return;
            _realmIdentityRoots[kingdom.StringId] = sourceTitle.Name;
            _realmIdentityNativeNames[kingdom.StringId] = native;
        }

        internal string GetRealmTitleStyleSourceId(Kingdom realm)
        {
            EnsureCollectionsInitialized();
            if (realm == null) return null;
            return _realmTitleStyleSources.TryGetValue(realm.StringId, out var source) ? source : realm.StringId;
        }

        private void RecoverRestoredRealmTitleStyles()
        {
            // Existing saves record the exact mantle transfer. Never infer ancestry
            // from a generated kingdom's name or an ID prefix alone.
            foreach (var realm in _independentRealmSourceTitleByKingdomId)
            {
                if (_realmTitleStyleSources.ContainsKey(realm.Key)) continue;
                var original = _historicalRealmSovereignTitleByKingdomId.FirstOrDefault(history =>
                    history.Key != realm.Key && history.Value == realm.Value
                    && string.Equals(history.Value, "bc_title_kingdom_" + history.Key, StringComparison.Ordinal));
                if (string.IsNullOrEmpty(original.Key)) continue;
                _realmTitleStyleSources[realm.Key] = _realmTitleStyleSources.TryGetValue(original.Key, out var source)
                    ? source : original.Key;
                BellumCivileLogger.Log($"Recovered restored realm title style; realm={realm.Key}; source={_realmTitleStyleSources[realm.Key]}; mantle={realm.Value}.");
            }
        }

        public string GetRealmIdentityRoot(Kingdom kingdom, string nativeName)
        {
            EnsureCollectionsInitialized();
            if (kingdom == null) return null;
            if (!_realmIdentityRoots.ContainsKey(kingdom.StringId))
                RememberRealmIdentity(kingdom, GetIndependentRealmSovereignTitle(kingdom));
            return _realmIdentityNativeNames.TryGetValue(kingdom.StringId, out string expected)
                && (expected == nativeName || new TextObject(expected ?? string.Empty).ToString() == nativeName)
                && _realmIdentityRoots.TryGetValue(kingdom.StringId, out string root) ? root : null;
        }

        public void RecordRealmIdentityRename(Kingdom kingdom, TextObject name)
        {
            EnsureCollectionsInitialized();
            if (kingdom == null || name == null) return;
            // These existing save fields accept both legacy rendered names and raw
            // localization tokens. Runtime variables still need a literal snapshot.
            string identity = name.Attributes == null || name.Attributes.Count == 0
                ? name.Value : "{=!}" + name.ToString();
            _realmIdentityRoots[kingdom.StringId] = identity;
            _realmIdentityNativeNames[kingdom.StringId] = identity;
        }

        public void UnregisterIndependentRealmShell(Kingdom kingdom, string reason)
        {
            EnsureCollectionsInitialized();
            if (kingdom == null || string.IsNullOrWhiteSpace(kingdom.StringId))
                return;

            if (_independentRealmSourceTitleByKingdomId.Remove(kingdom.StringId))
                BellumCivileLogger.Log($"Unregistered independent realm shell; kingdom={kingdom.StringId}; reason={reason ?? "unknown"}.");
        }

        public int ReconcilePoliticalHierarchy(string reason)
        {
            EnsureCollectionsInitialized();
            int repairs = ReconcileAllDeFactoParents(reason);
            RebuildRuntimeIndexes();
            return repairs;
        }

        public void RegisterRestoredRealmMantle(Kingdom oldRealm, Kingdom restoredRealm, Clan successorClan, string reason)
        {
            EnsureCollectionsInitialized();
            if (oldRealm == null
                || restoredRealm == null
                || successorClan == null
                || string.IsNullOrWhiteSpace(oldRealm.StringId)
                || string.IsNullOrWhiteSpace(restoredRealm.StringId)
                || string.IsNullOrWhiteSpace(successorClan.StringId))
            {
                return;
            }

            string titleId = BuildKingdomTitleId(oldRealm);
            FeudalTitleRecord title = GetHistoricalRealmSovereignTitle(oldRealm);
            if (title == null && IsValidPermanentKingdom(oldRealm))
                title = EnsureKingdomTitle(oldRealm, reason);

            if (title == null || !title.IsActive)
            {
                BellumCivileLogger.Log($"Failed to register restored realm mantle; old_realm={oldRealm.StringId}; restored_realm={restoredRealm.StringId}; successor={successorClan.StringId}; missing_title={titleId}; reason={reason ?? "unknown"}.");
                return;
            }

            string oldDeFacto = title.DeFactoHolderClanId;
            string oldAssociatedKingdom = title.AssociatedKingdomId;
            _historicalRealmSovereignTitleByKingdomId[oldRealm.StringId] = title.TitleId;
            title.SetDeFactoHolder(successorClan.StringId);
            title.SetAssociatedKingdom(restoredRealm.StringId);
            title.SetDeFactoParentTitle(string.Empty);
            title.MarkSynced(CurrentDay);
            _independentRealmSourceTitleByKingdomId[restoredRealm.StringId] = title.TitleId;
            _realmTitleStyleSources[restoredRealm.StringId] = GetRealmTitleStyleSourceId(oldRealm);
            RebuildRuntimeIndexes();
            int politicalRepairs = ReconcileAllDeFactoParents($"restored realm mantle {restoredRealm.StringId}");
            RebuildRuntimeIndexes();
            QueueServiceReviewForTitleAndChildren(title, reason);

            // Crown transitions are immediately visible and should not retain a former
            // ruler's honorific while the campaign is paused on the decision result.
            RefreshCachedTitleNames();

            BellumCivileLogger.Log(
                $"Registered restored realm mantle; title={title.TitleId}; old_realm={oldRealm.StringId}; restored_realm={restoredRealm.StringId}; successor={successorClan.StringId}; de_jure={title.DeJureHolderClanId}; de_facto={oldDeFacto}->{title.DeFactoHolderClanId}; associated={oldAssociatedKingdom}->{title.AssociatedKingdomId}; political_repairs={politicalRepairs}; reason={reason ?? "unknown"}.");
        }

        public bool IsIndependentRealmShell(Kingdom kingdom)
        {
            EnsureCollectionsInitialized();
            return kingdom != null
                && !string.IsNullOrWhiteSpace(kingdom.StringId)
                && _independentRealmSourceTitleByKingdomId.ContainsKey(kingdom.StringId);
        }

        public FeudalTitleRecord GetIndependentRealmSovereignTitle(Kingdom kingdom)
        {
            EnsureCollectionsInitialized();
            if (kingdom == null
                || string.IsNullOrWhiteSpace(kingdom.StringId)
                || !_independentRealmSourceTitleByKingdomId.TryGetValue(kingdom.StringId, out string sourceTitleId))
            {
                return null;
            }

            FeudalTitleRecord title = GetTitle(sourceTitleId);
            return title != null && title.IsActive ? title : null;
        }

        public FeudalTitleRecord GetRealmSovereignTitle(Kingdom kingdom)
        {
            return GetRealmSovereignTitle(kingdom, FeudalHierarchyMode.DeJure);
        }

        public FeudalTitleRecord GetHistoricalRealmSovereignTitle(Kingdom kingdom)
        {
            EnsureCollectionsInitialized();
            if (kingdom == null || string.IsNullOrWhiteSpace(kingdom.StringId))
                return null;

            FeudalTitleRecord title = null;
            if (_historicalRealmSovereignTitleByKingdomId.TryGetValue(kingdom.StringId, out string historicalTitleId))
                title = GetTitle(historicalTitleId);

            title = title
                ?? GetIndependentRealmSovereignTitle(kingdom)
                ?? GetTitle(BuildKingdomTitleId(kingdom));
            if (title == null || !title.IsActive)
                return null;

            string associatedKingdomId = title.AssociatedKingdomId;
            string lawfulHolderId = title.DeJureHolderClanId;
            FeudalTitleRecord sovereignTitle = title;
            HashSet<string> visited = new HashSet<string> { title.TitleId };
            while (!string.IsNullOrWhiteSpace(sovereignTitle.ParentTitleId))
            {
                FeudalTitleRecord parent = GetTitle(sovereignTitle.ParentTitleId);
                if (parent == null
                    || !parent.IsActive
                    || !visited.Add(parent.TitleId)
                    || !string.Equals(parent.AssociatedKingdomId, associatedKingdomId, StringComparison.Ordinal)
                    || !string.Equals(parent.DeJureHolderClanId, lawfulHolderId, StringComparison.Ordinal))
                {
                    break;
                }

                sovereignTitle = parent;
            }

            return sovereignTitle;
        }

        public bool IsHistoricalRealmRepresentedByActiveSuccessor(Kingdom historicalKingdom, FeudalTitleRecord historicalTitle)
        {
            EnsureCollectionsInitialized();
            if (historicalKingdom == null || historicalTitle == null || string.IsNullOrWhiteSpace(historicalTitle.TitleId))
                return false;

            return Kingdom.All.Any(candidate =>
            {
                if (candidate == null
                    || candidate == historicalKingdom
                    || candidate.IsEliminated
                    || BellumKingdomVisibilityHelper.IsTemporaryBellumKingdom(candidate))
                {
                    return false;
                }

                FeudalTitleRecord candidateTitle = GetRealmSovereignTitle(candidate, FeudalHierarchyMode.DeFacto)
                    ?? GetIndependentRealmSovereignTitle(candidate);
                return string.Equals(candidateTitle?.TitleId, historicalTitle.TitleId, StringComparison.Ordinal);
            });
        }

        public FeudalTitleRecord GetKingdomPoliticalTitle(Kingdom kingdom)
        {
            EnsureCollectionsInitialized();
            if (kingdom == null || kingdom.IsEliminated)
                return null;

            // The last reconciled crown remains the accession target while the ruling clan changes.
            if (_currentRealmTitleByKingdomId.TryGetValue(kingdom.StringId, out string currentTitleId))
            {
                FeudalTitleRecord current = GetTitle(currentTitleId);
                if (current != null && current.IsActive) return current;
            }
            FeudalTitleRecord independentTitle = GetIndependentRealmSovereignTitle(kingdom);
            if (independentTitle != null)
                return independentTitle;

            return GetTitle(BuildKingdomTitleId(kingdom));
        }

        public Clan GetDeFactoSovereignClan(Kingdom kingdom)
        {
            FeudalTitleRecord title = GetKingdomPoliticalTitle(kingdom);
            return ResolveClan(title?.DeFactoHolderClanId);
        }

        public bool TrySetKingdomTitleRuler(Kingdom kingdom, Clan rulerClan, bool legalTransfer, string reason)
        {
            EnsureCollectionsInitialized();

            if (!IsValidPermanentKingdom(kingdom)
                || IsTemporaryRebelKingdom(kingdom)
                || rulerClan == null
                || rulerClan.IsEliminated
                || rulerClan.Kingdom != kingdom
                || string.IsNullOrWhiteSpace(rulerClan.StringId))
            {
                return false;
            }

            FeudalTitleRecord title = GetKingdomPoliticalTitle(kingdom);
            if (title == null && !IsIndependentRealmShell(kingdom))
                title = EnsureKingdomTitle(kingdom, reason);
            if (title == null)
                return false;

            string oldDeJure = title.DeJureHolderClanId;
            string oldDeFacto = title.DeFactoHolderClanId;
            Clan oldLegalHolder = ResolveClan(oldDeJure);

            title.SetDeFactoHolder(rulerClan.StringId);
            if (legalTransfer)
            {
                title.SetDeJureHolder(rulerClan.StringId);
                if (oldLegalHolder?.Leader != null
                    && !oldLegalHolder.Leader.IsDead
                    && oldLegalHolder != rulerClan)
                {
                    RegisterClaim(
                        oldLegalHolder,
                        title,
                        FeudalClaimStrength.Strong,
                        "kingdom_title_transferred",
                        rulerClan.Leader,
                        oldLegalHolder,
                        carrierHero: oldLegalHolder.Leader,
                        generationDepth: 1);
                }

                RemoveRedundantClaimsForHolder(rulerClan, title);
            }

            title.SetAssociatedKingdom(kingdom.StringId);
            title.MarkSynced(CurrentDay);
            RebuildRuntimeIndexes();
            int politicalRepairs = ReconcileAllDeFactoParents($"kingdom title ruler transfer {kingdom.StringId}");
            QueueServiceReviewForTitleAndChildren(title, reason);

            // Party and army labels cache the ruler's name independently of Hero.Name.
            // Refresh them immediately so accession and abdication are visible at once.
            RefreshCachedTitleNames();

            BellumCivileLogger.Log(
                $"Feudal kingdom title ruler synced; kingdom={kingdom.StringId}; title={title.TitleId}; ruler={rulerClan.StringId}; legal_transfer={legalTransfer}; de_jure={oldDeJure}->{title.DeJureHolderClanId}; de_facto={oldDeFacto}->{title.DeFactoHolderClanId}; political_repairs={politicalRepairs}; reason={reason ?? "unknown"}.");
            // Journal-owned accessions announce after their other settlement steps. Direct claimant transfers finish here.
            if (legalTransfer && oldDeJure != rulerClan.StringId && oldLegalHolder != null
                && CrownAccessionBehavior.Instance?.IsPending(kingdom) != true)
                Campaign.Current.GetCampaignBehavior<IdeologyEventShockBehavior>()?
                    .CompleteCrownAccession(kingdom, ElectiveSuccessionBehavior.LegalHead(rulerClan));
            return true;
        }

        public FeudalTitleRecord GetRealmSovereignTitle(Kingdom kingdom, FeudalHierarchyMode mode)
        {
            EnsureCollectionsInitialized();
            if (kingdom == null || kingdom.IsEliminated || kingdom.RulingClan == null
                || kingdom.RulingClan.IsEliminated || BellumKingdomVisibilityHelper.IsTemporaryBellumKingdom(kingdom))
                return null;

            string preferred;
            if (!_currentRealmTitleByKingdomId.TryGetValue(kingdom.StringId, out preferred))
                preferred = GetKingdomPoliticalTitle(kingdom)?.TitleId;

            // Derive rank from current holdings, not the historical founding or succession title.
            // A lawful receiver must also be held de jure; political rank requires possession only.
            FeudalTitleRecord current = RealmSovereignSelection.Select(
                GetTitlesHeldByClan(kingdom.RulingClan, deJure: false), kingdom.RulingClan.StringId,
                mode == FeudalHierarchyMode.DeJure, preferred);
            return current;
        }

        public bool IsRealmSovereignTitle(Kingdom kingdom, FeudalTitleRecord title)
        {
            EnsureCollectionsInitialized();
            if (kingdom == null || title == null || !title.IsActive)
                return false;

            return string.Equals(title.TitleId, GetRealmSovereignTitle(kingdom, FeudalHierarchyMode.DeJure)?.TitleId, StringComparison.Ordinal)
                || string.Equals(title.TitleId, GetRealmSovereignTitle(kingdom, FeudalHierarchyMode.DeFacto)?.TitleId, StringComparison.Ordinal)
                || string.Equals(title.TitleId, GetIndependentRealmSovereignTitle(kingdom)?.TitleId, StringComparison.Ordinal);
        }

        public IReadOnlyCollection<FeudalTitleFormationCandidate> GetFormableTitles(Clan clan, FeudalTitleType targetType)
        {
            return GetFormableTitles(clan, targetType, allowReorganization: false);
        }

        public IReadOnlyCollection<FeudalTitleFormationCandidate> GetFormableTitles(
            Clan clan,
            FeudalTitleType targetType,
            bool allowReorganization)
        {
            EnsureCollectionsInitialized();
            if (!IsValidFormationTarget(targetType))
                return new List<FeudalTitleFormationCandidate>();

            List<FeudalTitleRecord> children = GetEligibleFormationChildren(
                clan,
                GetChildTypeForFormation(targetType),
                allowReorganization);
            List<FeudalTitleFormationCandidate> candidates = BuildFormationCandidates(
                clan,
                targetType,
                children,
                allowReorganization);
            return candidates;
        }

        public FeudalFormationPreview GetFormationAssessment(
            Clan clan,
            FeudalTitleRecord selectedTitle,
            bool allowReorganization,
            bool checkResources)
        {
            EnsureCollectionsInitialized();
            FeudalFormationPreview assessment = new FeudalFormationPreview
            {
                BlockReason = FeudalTitleFormationBlockReason.Unavailable,
                Reason = "title formation is unavailable",
                RequiredTitles = GetMinimumChildrenForFormation(),
                CurrentGold = clan?.Leader?.Gold ?? 0
            };

            if (selectedTitle == null || !selectedTitle.IsActive || selectedTitle.IsDeliberatelyDissolved)
                return assessment;

            if (selectedTitle.TitleType >= FeudalTitleType.Empire)
            {
                assessment.BlockReason = FeudalTitleFormationBlockReason.HighestTier;
                assessment.Reason = "the selected title is already of the highest tier";
                return assessment;
            }

            assessment.IsVisible = true;
            assessment.TargetType = (FeudalTitleType)((int)selectedTitle.TitleType + 1);
            assessment.GoldCost = GetFormationGoldCost(assessment.TargetType);
            assessment.InfluenceReward = GetFormationInfluenceReward(assessment.TargetType);

            if (clan?.Leader == null || clan.Leader.IsDead)
            {
                assessment.BlockReason = FeudalTitleFormationBlockReason.MissingLeader;
                assessment.Reason = "forming clan has no living leader";
                return assessment;
            }

            if (!IsFullyHeldByClan(selectedTitle, clan))
            {
                assessment.BlockReason = FeudalTitleFormationBlockReason.SeatNotFullyHeld;
                assessment.Reason = "the selected seat must be held both de jure and de facto by your clan";
                return assessment;
            }

            if (HasStructuralFeud(selectedTitle))
            {
                assessment.BlockReason = FeudalTitleFormationBlockReason.SeatDisputed;
                assessment.Reason = "the selected seat is involved in an active claim or dispute";
                return assessment;
            }

            FeudalTitleType childType = selectedTitle.TitleType;
            List<FeudalTitleRecord> sameTierTitles = _titlesById.Values
                .Where(title => title != null
                    && title.IsActive
                    && title.TitleType == childType
                    && !string.Equals(title.TitleId, selectedTitle.TitleId, StringComparison.Ordinal))
                .ToList();
            List<FeudalTitleRecord> neighboringTitles = sameTierTitles
                .Where(title => AreTitlesAdjacent(selectedTitle, title))
                .OrderByDescending(GetTitleProsperityScore)
                .ThenBy(title => title.TitleId)
                .ToList();
            assessment.NeighboringTitles = neighboringTitles.Count;

            List<FeudalTitleRecord> eligibleChildren = GetEligibleFormationChildren(
                clan,
                childType,
                allowReorganization);
            HashSet<string> eligibleTitleIds = new HashSet<string>(
                eligibleChildren.Select(title => title.TitleId),
                StringComparer.Ordinal);
            assessment.EligibleNeighboringTitles = neighboringTitles.Count(title => eligibleTitleIds.Contains(title.TitleId));

            foreach (FeudalTitleRecord neighboringTitle in neighboringTitles.Where(title => !eligibleTitleIds.Contains(title.TitleId)))
            {
                assessment.Obstacles.Add(new FeudalTitleFormationObstacle
                {
                    Title = neighboringTitle,
                    Reason = GetFormationChildBlockReason(neighboringTitle, clan, childType, allowReorganization)
                });
            }

            List<FeudalTitleRecord> component = BuildConnectedComponents(eligibleChildren)
                .FirstOrDefault(group => group.Any(title => title.TitleId == selectedTitle.TitleId))
                ?? new List<FeudalTitleRecord>();
            assessment.ContiguousEligibleTitles = component.Count;

            List<FeudalTitleFormationBlockReason> diagnosticFailures = new List<FeudalTitleFormationBlockReason>();
            List<FeudalTitleFormationCandidate> candidates = BuildFormationCandidates(
                    clan,
                    assessment.TargetType,
                    component,
                    allowReorganization,
                    selectedTitle.TitleId,
                    diagnosticFailures)
                .Where(candidate => candidate.ChildTitleIds.Contains(selectedTitle.TitleId))
                .ToList();
            assessment.Candidates.AddRange(candidates);

            HashSet<string> selectableTitleIds = new HashSet<string>(
                candidates.SelectMany(candidate => candidate.ChildTitleIds),
                StringComparer.Ordinal);
            assessment.SelectableTitles.AddRange(component
                .Where(title => selectableTitleIds.Contains(title.TitleId))
                .OrderByDescending(title => title.TitleId == selectedTitle.TitleId)
                .ThenByDescending(title => IsFullyHeldByClan(title, clan))
                .ThenByDescending(GetTitleProsperityScore)
                .ThenBy(title => title.TitleId));

            if (assessment.Candidates.Count == 0
                || assessment.SelectableTitles.Count < assessment.RequiredTitles)
            {
                if (assessment.ContiguousEligibleTitles < assessment.RequiredTitles)
                {
                    if (assessment.NeighboringTitles == 0)
                    {
                        assessment.BlockReason = FeudalTitleFormationBlockReason.NoNeighboringTitles;
                        assessment.Reason = "no neighboring titles of the required tier";
                    }
                    else if (assessment.EligibleNeighboringTitles == 0)
                    {
                        assessment.BlockReason = FeudalTitleFormationBlockReason.NeighboringTitlesIneligible;
                        assessment.Reason = "neighboring titles exist but none are eligible for formation";
                    }
                    else
                    {
                        assessment.BlockReason = FeudalTitleFormationBlockReason.InsufficientContiguousTitles;
                        assessment.Reason = "too few eligible contiguous titles around the selected seat";
                    }
                }
                else
                {
                    assessment.BlockReason = SelectFormationBlockReason(diagnosticFailures);
                    assessment.Reason = GetFormationTechnicalReason(assessment.BlockReason);
                }

                return assessment;
            }

            if (checkResources && clan.Leader.Gold < assessment.GoldCost)
            {
                assessment.BlockReason = FeudalTitleFormationBlockReason.InsufficientGold;
                assessment.Reason = "insufficient gold";
                return assessment;
            }

            assessment.CanForm = true;
            assessment.BlockReason = FeudalTitleFormationBlockReason.None;
            assessment.Reason = string.Empty;
            return assessment;
        }

        public IReadOnlyCollection<FeudalTitleRecord> GetFormationSelectionTitles(Clan clan, FeudalTitleRecord seedTitle)
        {
            return GetFormationSelectionTitles(clan, seedTitle, allowReorganization: false);
        }

        public IReadOnlyCollection<FeudalTitleRecord> GetFormationSelectionTitles(
            Clan clan,
            FeudalTitleRecord seedTitle,
            bool allowReorganization)
        {
            EnsureCollectionsInitialized();
            if (clan == null
                || seedTitle == null
                || !seedTitle.IsActive
                || seedTitle.TitleType >= FeudalTitleType.Empire
                || !IsFullyHeldByClan(seedTitle, clan)
                || !IsEligibleFormationChild(seedTitle, clan, seedTitle.TitleType, allowReorganization))
            {
                return Array.Empty<FeudalTitleRecord>();
            }

            List<FeudalTitleRecord> eligible = GetEligibleFormationChildren(clan, seedTitle.TitleType, allowReorganization);
            List<FeudalTitleRecord> component = BuildConnectedComponents(eligible)
                .FirstOrDefault(group => group.Any(title => title.TitleId == seedTitle.TitleId));
            if (component == null)
                return Array.Empty<FeudalTitleRecord>();

            return component
                .OrderByDescending(title => title.TitleId == seedTitle.TitleId)
                .ThenByDescending(title => IsFullyHeldByClan(title, clan))
                .ThenByDescending(GetTitleProsperityScore)
                .ThenBy(title => title.TitleId)
                .ToList();
        }

        public bool TryBuildFormationCandidate(
            Clan clan,
            FeudalTitleType targetType,
            IEnumerable<string> childTitleIds,
            string seedTitleId,
            string requestedName,
            out FeudalTitleFormationCandidate candidate,
            out string reason)
        {
            return TryBuildFormationCandidate(
                clan,
                targetType,
                childTitleIds,
                seedTitleId,
                requestedName,
                allowReorganization: false,
                out candidate,
                out reason);
        }

        public bool TryBuildFormationCandidate(
            Clan clan,
            FeudalTitleType targetType,
            IEnumerable<string> childTitleIds,
            string seedTitleId,
            string requestedName,
            bool allowReorganization,
            out FeudalTitleFormationCandidate candidate,
            out string reason)
        {
            candidate = null;
            reason = null;
            EnsureCollectionsInitialized();

            if (clan == null || !IsValidFormationTarget(targetType))
            {
                reason = "clan or target title tier is invalid";
                return false;
            }

            List<string> distinctIds = childTitleIds?
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.Ordinal)
                .ToList() ?? new List<string>();
            if (distinctIds.Count < GetMinimumChildrenForFormation())
            {
                reason = "too few subordinate titles selected";
                return false;
            }

            FeudalTitleType childType = GetChildTypeForFormation(targetType);
            List<FeudalTitleRecord> children = distinctIds.Select(GetTitle).Where(title => title != null).ToList();
            if (children.Count != distinctIds.Count
                || children.Any(title => !IsEligibleFormationChild(title, clan, childType, allowReorganization)))
            {
                reason = "one or more selected titles are no longer eligible";
                return false;
            }

            FeudalTitleRecord seedTitle = children.FirstOrDefault(title => title.TitleId == seedTitleId);
            if (seedTitle == null)
            {
                reason = "the selected seed title is missing";
                return false;
            }

            if (!IsFullyHeldByClan(seedTitle, clan))
            {
                reason = "the selected seat must be held both de jure and de facto by your clan";
                return false;
            }

            if (!IsConnectedSubset(children))
            {
                reason = "selected titles are not contiguous";
                return false;
            }

            if (!HasLawfulFormationSeed(clan, children))
            {
                reason = "selected titles contain no lawful seed";
                return false;
            }

            if (!TryBuildCandidateFromCluster(
                clan,
                targetType,
                children,
                allowReorganization,
                out candidate,
                out _,
                out reason))
            {
                reason = reason ?? "the selected title cluster is not eligible";
                return false;
            }

            string name = string.IsNullOrWhiteSpace(requestedName)
                ? GetDefaultFormationTitleRoot(seedTitle, targetType)
                : requestedName.Trim();
            if (!IsValidCustomTitleName(name, out reason))
            {
                candidate = null;
                return false;
            }

            candidate.SeedTitleId = seedTitle.TitleId;
            candidate.CapitalSettlementId = ResolveCapitalSettlementId(seedTitle);
            candidate.Name = name;
            return true;
        }

        public string GetDefaultFormationTitleRoot(FeudalTitleRecord seedTitle, FeudalTitleType targetType)
        {
            if (seedTitle == null)
                return targetType.ToString();

            string capitalSettlementId = ResolveCapitalSettlementId(seedTitle);
            string seedName = ResolveSettlement(capitalSettlementId)?.Name?.ToString() ?? seedTitle.Name;
            return BuildTitleRootName(targetType, seedName);
        }

        public static bool IsValidCustomTitleName(string name, out string reason)
        {
            reason = null;
            string value = name?.Trim() ?? string.Empty;
            if (value.Length == 0)
            {
                reason = "title name cannot be empty";
                return false;
            }

            if (value.Length > 64)
            {
                reason = "title name is too long";
                return false;
            }

            if (value.Any(char.IsControl) || value.IndexOf('{') >= 0 || value.IndexOf('}') >= 0)
            {
                reason = "title name contains invalid characters";
                return false;
            }

            return true;
        }

        public static int GetMinimumFormationChildren()
        {
            return GetMinimumChildrenForFormation();
        }

        public bool CanRenameTitle(Clan clan, FeudalTitleRecord title, out string reason)
        {
            reason = null;
            if (clan?.Leader == null || title == null || !title.IsActive)
            {
                reason = "title renaming is unavailable";
                return false;
            }

            if (!string.Equals(title.DeJureHolderClanId, clan.StringId, StringComparison.Ordinal)
                || !string.Equals(title.DeFactoHolderClanId, clan.StringId, StringComparison.Ordinal))
            {
                reason = "title is not fully held by your clan";
                return false;
            }

            return true;
        }

        public bool TryRenameTitle(Clan clan, FeudalTitleRecord title, string requestedName, out string reason)
        {
            EnsureCollectionsInitialized();
            if (!CanRenameTitle(clan, title, out reason))
                return false;

            string name = requestedName?.Trim() ?? string.Empty;
            if (!IsValidCustomTitleName(name, out reason))
                return false;

            title.SetName(name);
            title.MarkSynced(CurrentDay);
            _playerTitleNameOverrides[title.TitleId] = name;
            RebuildRuntimeIndexes();
            BellumCivileLogger.Log($"Feudal title renamed by holder; clan={clan.StringId}; title={title.TitleId}; name={name}.");
            return true;
        }

        public bool TryGetBestFormableTitle(Clan clan, FeudalTitleType targetType, out FeudalTitleFormationCandidate candidate, out string reason)
        {
            candidate = null;
            reason = null;

            if (clan == null)
            {
                reason = "clan is missing";
                return false;
            }

            if (!IsValidFormationTarget(targetType))
            {
                reason = $"cannot form {targetType} titles from this command";
                return false;
            }

            List<FeudalTitleFormationCandidate> candidates = GetFormableTitles(clan, targetType).ToList();
            if (candidates.Count == 0)
            {
                reason = $"no valid contiguous {GetChildTypeForFormation(targetType).ToString().ToLowerInvariant()} cluster with a lawful seed title";
                return false;
            }

            candidate = candidates
                .OrderByDescending(c => c.ChildTitleIds.Count)
                .ThenByDescending(c => c.ProsperityScore)
                .ThenBy(c => c.CapitalSettlementId ?? string.Empty)
                .First();
            return true;
        }

        public bool TryFormTitle(Clan clan, FeudalTitleType targetType, bool chargeCost, out FeudalTitleRecord title, out string reason)
        {
            title = null;
            reason = null;

            EnsureCollectionsInitialized();

            if (!TryGetBestFormableTitle(clan, targetType, out FeudalTitleFormationCandidate candidate, out reason))
                return false;

            // Non-player callers use the conservative settlement if revalidation reveals that
            // the new title equals or outranks the clan's current sovereign.
            return TryFormTitle(
                clan,
                candidate,
                chargeCost,
                FeudalSovereignElevationChoice.PeacefulSeparation,
                out title,
                out _,
                out reason);
        }

        public bool TryFormTitle(Clan clan, FeudalTitleFormationCandidate requestedCandidate, bool chargeCost, out FeudalTitleRecord title, out string reason)
        {
            // Carry the peaceful choice through candidate rebuilding. The execution overload
            // discards it when no sovereign elevation is required.
            return TryFormTitle(
                clan,
                requestedCandidate,
                chargeCost,
                FeudalSovereignElevationChoice.PeacefulSeparation,
                out title,
                out _,
                out reason);
        }

        public bool TryFormTitle(
            Clan clan,
            FeudalTitleFormationCandidate requestedCandidate,
            bool chargeCost,
            FeudalSovereignElevationChoice elevationChoice,
            out FeudalTitleRecord title,
            out Kingdom independentKingdom,
            out string reason)
        {
            title = null;
            independentKingdom = null;
            reason = null;
            EnsureCollectionsInitialized();

            if (clan == null || requestedCandidate == null)
            {
                reason = "clan or formation candidate is missing";
                return false;
            }

            if (!TryBuildFormationCandidate(
                clan,
                requestedCandidate.TargetType,
                requestedCandidate.ChildTitleIds,
                string.IsNullOrWhiteSpace(requestedCandidate.SeedTitleId)
                    ? requestedCandidate.ChildTitleIds.FirstOrDefault()
                    : requestedCandidate.SeedTitleId,
                requestedCandidate.Name,
                allowReorganization: requestedCandidate.Mode != FeudalTitleFormationMode.Consolidation,
                out FeudalTitleFormationCandidate candidate,
                out reason))
            {
                return false;
            }

            FeudalSovereignElevationPreview elevation = GetFormationSovereignElevationPreview(clan, candidate);
            if (elevation.IsRequired)
            {
                if (elevationChoice != FeudalSovereignElevationChoice.PeacefulSeparation
                    && elevationChoice != FeudalSovereignElevationChoice.RetainHoldingsAndRebel)
                {
                    reason = "choose how the new sovereign realm will separate from its former liege";
                    return false;
                }

                if (!elevation.HasExternalHoldings)
                    elevationChoice = FeudalSovereignElevationChoice.PeacefulSeparation;
            }
            else
            {
                elevationChoice = FeudalSovereignElevationChoice.NotApplicable;
            }

            if (chargeCost)
            {
                Hero sponsor = clan?.Leader;
                if (sponsor == null)
                {
                    reason = "forming clan has no leader";
                    return false;
                }

                if (sponsor.Gold < candidate.GoldCost)
                {
                    reason = $"{sponsor.Name} needs {candidate.GoldCost} denars to form a {candidate.TargetType.ToString().ToLowerInvariant()} title";
                    return false;
                }
            }

            var resentment = GetReorganizationResentment(clan, candidate.ChildTitleIds.Concat(candidate.AffectedParentTitleIds));
            Hero reorganizer = clan.Leader;
            var historicOrigins = candidate.ChildTitleIds.Select(GetTitle).Where(t => t != null)
                .ToDictionary(t => t.TitleId, GetDriftOriginKingdomId);
            string titleId = BuildUniqueHigherTitleId(candidate.TargetType, candidate.CapitalSettlementId, clan.StringId);
            string parentTitleId = ResolveParentForNewTitle(clan, candidate.TargetType, candidate.ChildTitleIds);
            string kingdomId = clan?.Kingdom?.StringId ?? string.Empty;

            title = new FeudalTitleRecord(
                titleId,
                candidate.Name,
                candidate.TargetType,
                clan?.StringId,
                clan?.StringId,
                parentTitleId,
                candidate.CapitalSettlementId,
                kingdomId,
                CurrentDay,
                CurrentDay);

            _titlesById[titleId] = title;

            bool promotesIndependentShell = ShouldPromoteIndependentRealmShell(clan?.Kingdom, title, candidate.ChildTitleIds);
            if (promotesIndependentShell)
                title.SetDeFactoParentTitle(string.Empty);

            Dictionary<string, string> oldDeJureParents = new Dictionary<string, string>(StringComparer.Ordinal);
            Dictionary<string, string> oldDeFactoParents = new Dictionary<string, string>(StringComparer.Ordinal);
            Dictionary<string, string> oldAssociatedKingdoms = new Dictionary<string, string>(StringComparer.Ordinal);
            try
            {
                foreach (FeudalTitleFormationMove move in candidate.Moves)
                {
                    FeudalTitleRecord childTitle = GetTitle(move.ChildTitleId);
                    if (childTitle == null || !childTitle.IsActive)
                        throw new InvalidOperationException($"Formation child '{move.ChildTitleId}' disappeared during execution.");

                    oldDeJureParents[childTitle.TitleId] = childTitle.ParentTitleId;
                    oldDeFactoParents[childTitle.TitleId] = childTitle.DeFactoParentTitleId;
                    oldAssociatedKingdoms[childTitle.TitleId] = childTitle.AssociatedKingdomId;
                    if (move.ReparentDeJure)
                        childTitle.SetParentTitle(titleId);
                    if (move.ReparentDeFacto)
                        childTitle.SetDeFactoParentTitle(titleId);
                    if (move.ReparentDeJure || move.ReparentDeFacto)
                        childTitle.SetAssociatedKingdom(historicOrigins[childTitle.TitleId]);
                    childTitle.MarkSynced(CurrentDay);
                }

                RebuildRuntimeIndexes();
                int politicalRepairs = ReconcileAllDeFactoParents("higher title formation");
                if (politicalRepairs > 0)
                    RebuildRuntimeIndexes();
            }
            catch (Exception ex)
            {
                foreach (string childTitleId in oldDeJureParents.Keys)
                {
                    FeudalTitleRecord childTitle = GetTitle(childTitleId);
                    if (childTitle == null)
                        continue;

                    childTitle.SetParentTitle(oldDeJureParents[childTitleId]);
                    childTitle.SetDeFactoParentTitle(oldDeFactoParents[childTitleId]);
                    childTitle.SetAssociatedKingdom(oldAssociatedKingdoms[childTitleId]);
                    childTitle.MarkSynced(CurrentDay);
                }

                _titlesById.Remove(titleId);
                RebuildRuntimeIndexes();
                title = null;
                reason = "the title hierarchy changed before formation could be completed";
                BellumCivileLogger.Log($"Feudal title formation rolled back; clan={clan?.StringId ?? "null"}; error={ex}.");
                return false;
            }

            Kingdom formerKingdom = elevation.ParentKingdom;
            if (elevation.IsRequired
                && !TryPromoteSovereignTitleToIndependentRealm(
                    clan,
                    title,
                    elevationChoice,
                    "sovereign title formation",
                    out independentKingdom,
                    out reason))
            {
                foreach (string childTitleId in oldDeJureParents.Keys)
                {
                    FeudalTitleRecord childTitle = GetTitle(childTitleId);
                    if (childTitle == null)
                        continue;

                    childTitle.SetParentTitle(oldDeJureParents[childTitleId]);
                    childTitle.SetDeFactoParentTitle(oldDeFactoParents[childTitleId]);
                    childTitle.SetAssociatedKingdom(oldAssociatedKingdoms[childTitleId]);
                    childTitle.MarkSynced(CurrentDay);
                }

                _titlesById.Remove(titleId);
                RebuildRuntimeIndexes();
                title = null;
                BellumCivileLogger.Log(
                    $"Feudal sovereign formation rolled back; clan={clan?.StringId ?? "null"}; type={candidate.TargetType}; reason={reason ?? "unknown"}.");
                return false;
            }

            if (chargeCost)
                GiveGoldAction.ApplyBetweenCharacters(clan.Leader, null, candidate.GoldCost, true);
            if (chargeCost && clan != null && candidate.InfluenceReward > 0f)
                ChangeClanInfluenceAction.Apply(clan, candidate.InfluenceReward);

            if (promotesIndependentShell)
                ReconcileCurrentRealmTitles();

            ReconcileReorganizedTitles(candidate.ChildTitleIds.Concat(candidate.AffectedParentTitleIds));
            ApplyReorganizationResentment(reorganizer, resentment, title.Name);
            QueueServiceReviewForTitleAndChildren(title, "higher title formation");
            foreach (string childTitleId in candidate.ChildTitleIds)
                QueueServiceReview(GetTitle(childTitleId), "higher title formation child");
            foreach (string parentTitleIdToReview in candidate.AffectedParentTitleIds)
                QueueServiceReview(GetTitle(parentTitleIdToReview), "higher title formation source parent");
            BellumCivileLogger.Log(
                $"Feudal title formed: clan={clan?.StringId ?? "null"}; title={title.TitleId}; type={candidate.TargetType}; mode={candidate.Mode}; children={candidate.ChildTitleIds.Count}; affected_parents={candidate.AffectedParentTitleIds.Count}; vassals={candidate.VassalClanIds.Count}; coequal_sovereign={candidate.CreatesCoequalSovereignTitle}; succession_risk={candidate.HasIndependentSuccessionRisk}; legal_seed={candidate.DeJureHeldChildren}/{candidate.RequiredDeJureChildren}; cost={(chargeCost ? candidate.GoldCost : 0)}; influence={(chargeCost ? candidate.InfluenceReward : 0f):0}.");
            NotificationHelper.ShowFeudalTitleFormed(clan, title, candidate.ChildTitleIds.Count);
            if (independentKingdom != null)
            {
                NotificationHelper.ShowSovereignTitleElevated(
                    clan,
                    formerKingdom,
                    independentKingdom,
                    title,
                    elevationChoice == FeudalSovereignElevationChoice.RetainHoldingsAndRebel);
            }

            return true;
        }

        public float GetTitleDissolutionInfluenceCost(FeudalTitleType titleType)
        {
            return GetFormationInfluenceReward(titleType);
        }

        public bool CanDissolveTitle(Clan clan, FeudalTitleRecord title, out string reason)
        {
            reason = null;
            EnsureCollectionsInitialized();
            if (clan == null || title == null || !title.IsActive || title.IsDeliberatelyDissolved || title.TitleType == FeudalTitleType.Barony)
            {
                reason = "only active upper titles can be dissolved";
                return false;
            }

            if (title.DeJureHolderClanId != clan.StringId || title.DeFactoHolderClanId != clan.StringId)
            {
                reason = "title is not fully held by your clan";
                return false;
            }

            if (IsSovereignTitle(title, clan.Kingdom))
            {
                reason = "a sovereign title cannot be dissolved";
                return false;
            }

            if (HasStructuralFeud(title))
            {
                reason = "title is involved in an active claim or dispute";
                return false;
            }

            float influenceCost = GetTitleDissolutionInfluenceCost(title.TitleType);
            if (clan.Influence < influenceCost)
            {
                reason = "insufficient influence";
                return false;
            }

            return true;
        }

        public bool TryDissolveTitle(Clan clan, FeudalTitleRecord title, bool chargeCost, out string reason)
        {
            reason = null;
            if (!CanDissolveTitle(clan, title, out reason))
                return false;

            float influenceCost = GetTitleDissolutionInfluenceCost(title.TitleType);
            var resentment = GetReorganizationResentment(clan, new[] { title.TitleId });
            Hero dissolver = clan.Leader;
            var affected = GetTitleAndDescendants(title).Concat(GetTitleAndDescendants(title, FeudalHierarchyMode.DeFacto))
                .Select(t => t.TitleId).Distinct().ToList();
            if (!DissolveTitleInternal(title, deliberatelyDissolved: true, "player title dissolution", out reason))
                return false;

            if (chargeCost && influenceCost > 0f)
                ChangeClanInfluenceAction.Apply(clan, -influenceCost);

            ReconcileReorganizedTitles(affected);
            ApplyReorganizationResentment(dissolver, resentment, title.Name);
            BellumCivileLogger.Log($"Feudal title dissolved by holder; clan={clan.StringId}; title={title.TitleId}; type={title.TitleType}; influence_cost={(chargeCost ? influenceCost : 0f):0}.");
            return true;
        }

        private bool DissolveTitleInternal(FeudalTitleRecord title, bool deliberatelyDissolved, string reason, out string failureReason)
        {
            failureReason = null;
            if (title == null || !title.IsActive || title.IsDeliberatelyDissolved || title.TitleType == FeudalTitleType.Barony)
            {
                failureReason = "only active upper titles can be dissolved";
                return false;
            }

            FeudalTitleRecord deJureParent = GetTitle(title.ParentTitleId);
            FeudalTitleRecord deFactoParent = GetTitle(title.DeFactoParentTitleId);
            string deJureParentId = deJureParent != null && deJureParent.IsActive && deJureParent.TitleType > title.TitleType
                ? deJureParent.TitleId
                : string.Empty;
            string deFactoParentId = deFactoParent != null && deFactoParent.IsActive && deFactoParent.TitleType > title.TitleType
                ? deFactoParent.TitleId
                : deJureParentId;

            List<FeudalTitleRecord> deJureChildren = _titlesById.Values
                .Where(child => child != null && child.IsActive && child.ParentTitleId == title.TitleId)
                .ToList();
            List<FeudalTitleRecord> deFactoChildren = _titlesById.Values
                .Where(child => child != null && child.IsActive && child.DeFactoParentTitleId == title.TitleId)
                .ToList();

            var inheritedOrigins = deJureChildren.Where(child => string.IsNullOrWhiteSpace(child.AssociatedKingdomId))
                .ToDictionary(child => child, GetDriftOriginKingdomId);
            foreach (var entry in inheritedOrigins)
                entry.Key.SetAssociatedKingdom(entry.Value);

            foreach (FeudalTitleRecord child in deJureChildren)
            {
                child.SetParentTitle(deJureParentId);
                child.MarkSynced(CurrentDay);
                QueueServiceReview(child, "dissolved de jure parent");
            }

            foreach (FeudalTitleRecord child in deFactoChildren)
            {
                child.SetDeFactoParentTitle(deFactoParentId);
                child.MarkSynced(CurrentDay);
                QueueServiceReview(child, "dissolved de facto parent");
            }

            title.SetDeJureHolder(string.Empty);
            title.SetDeFactoHolder(string.Empty);
            title.SetParentTitle(string.Empty);
            title.SetDeFactoParentTitle(string.Empty);
            title.SetDeliberatelyDissolved(deliberatelyDissolved);
            if (!deliberatelyDissolved)
                title.SetActive(false);
            title.MarkSynced(CurrentDay);

            RebuildRuntimeIndexes();
            BellumCivileLogger.Log($"Feudal title deactivated and children reparented; title={title.TitleId}; de_jure_children={deJureChildren.Count}; de_facto_children={deFactoChildren.Count}; deliberate={deliberatelyDissolved}; reason={reason ?? "unknown"}.");
            return true;
        }

        private bool IsSovereignTitle(FeudalTitleRecord title, Kingdom holderKingdom)
        {
            if (title == null)
                return false;

            if (holderKingdom != null && IsRealmSovereignTitle(holderKingdom, title))
                return true;

            return Kingdom.All.Where(IsValidPermanentKingdom).Any(kingdom =>
                IsRealmSovereignTitle(kingdom, title));
        }

        private bool HasActiveTitleDispute(FeudalTitleRecord title)
        {
            if (title == null)
                return false;

            if (_claimsByTitle.TryGetValue(title.TitleId, out List<FeudalClaimRecord> titleClaims))
            {
                foreach (FeudalClaimRecord claim in titleClaims)
                {
                    if (IsClaimCurrentlyActive(claim))
                        return true;
                }
            }

            FeudalClaimFabricationBehavior fabricationBehavior = Campaign.Current?.GetCampaignBehavior<FeudalClaimFabricationBehavior>();
            if (fabricationBehavior?.GetActiveFabrications().Any(record => record != null && record.TargetTitleId == title.TitleId) == true)
                return true;

            ClaimFeudBehavior feudBehavior = Campaign.Current?.GetCampaignBehavior<ClaimFeudBehavior>();
            if (feudBehavior?.GetActiveFeuds().Any(record => record != null && record.TargetTitleId == title.TitleId) == true)
                return true;

            FeudalDeJureDriftBehavior driftBehavior = Campaign.Current?.GetCampaignBehavior<FeudalDeJureDriftBehavior>();
            return driftBehavior?.GetDriftForTitle(title.TitleId) != null;
        }

        public bool HasActiveClaim(Clan claimantClan, FeudalTitleRecord targetTitle, FeudalClaimStrength? strength = null)
        {
            EnsureCollectionsInitialized();
            if (claimantClan == null || targetTitle == null || string.IsNullOrWhiteSpace(claimantClan.StringId) || string.IsNullOrWhiteSpace(targetTitle.TitleId))
                return false;

            if (targetTitle.DeJureHolderClanId == claimantClan.StringId
                && (!strength.HasValue || strength.Value == FeudalClaimStrength.Strong))
            {
                return true;
            }

            if (!_claimsByClanAndTitle.TryGetValue(claimantClan.StringId, out Dictionary<string, List<FeudalClaimRecord>> claimsByTitle)
                || !claimsByTitle.TryGetValue(targetTitle.TitleId, out List<FeudalClaimRecord> claims))
            {
                return false;
            }

            foreach (FeudalClaimRecord claim in claims)
            {
                if (IsClaimCurrentlyActive(claim)
                    && (!strength.HasValue || claim.Strength == strength.Value))
                {
                    return true;
                }
            }

            return false;
        }

        public void DeactivateFabricatedClaimsByCarrier(Hero carrier)
        {
            EnsureCollectionsInitialized();
            if (carrier == null || string.IsNullOrWhiteSpace(carrier.StringId))
                return;

            int deactivated = 0;
            foreach (FeudalClaimRecord claim in _claims)
            {
                if (claim == null
                    || !claim.IsActive
                    || claim.Source != "fabricated_claim"
                    || claim.CarrierHeroId != carrier.StringId)
                {
                    continue;
                }

                claim.SetActive(false);
                deactivated++;
            }

            if (deactivated > 0)
            {
                RebuildRuntimeIndexes();
                BellumCivileLogger.Log($"Feudal fabricated claims deactivated after carrier death; carrier={carrier.StringId}; claims={deactivated}.");
            }
        }

        public int GetTitleFormationGoldCost(FeudalTitleType titleType)
        {
            return GetFormationGoldCost(titleType);
        }

        public float GetTitleFormationInfluenceCost(FeudalTitleType titleType)
        {
            return GetFormationInfluenceReward(titleType);
        }

        public bool TryGetBarony(Settlement settlement, out FeudalTitleRecord title)
        {
            title = null;
            if (!IsLandedSettlement(settlement))
                return false;

            EnsureCollectionsInitialized();
            string settlementId = settlement.StringId;
            if (!_baronyTitleBySettlementId.TryGetValue(settlementId, out string titleId))
                return false;

            return _titlesById.TryGetValue(titleId, out title);
        }

        public IDisposable BeginSubinfeudationGrant(
            FeudalTitleRecord baronyTitle,
            FeudalTitleRecord liegeTitle,
            Clan grantorClan,
            bool transferDeJure)
        {
            EnsureCollectionsInitialized();
            if (_activeSubinfeudationGrant != null)
                throw new InvalidOperationException("A subinfeudation grant is already being processed.");
            if (baronyTitle == null || !baronyTitle.IsActive || baronyTitle.TitleType != FeudalTitleType.Barony)
                throw new ArgumentException("The granted title must be an active barony.", nameof(baronyTitle));
            if (liegeTitle == null || !liegeTitle.IsActive || liegeTitle.TitleType <= baronyTitle.TitleType)
                throw new ArgumentException("The liege title must be an active superior title.", nameof(liegeTitle));
            if (grantorClan == null || string.IsNullOrWhiteSpace(grantorClan.StringId))
                throw new ArgumentNullException(nameof(grantorClan));

            SubinfeudationGrantContext context = new SubinfeudationGrantContext(
                baronyTitle,
                liegeTitle,
                grantorClan,
                transferDeJure);
            _activeSubinfeudationGrant = context;
            return new SubinfeudationGrantScope(this, context);
        }

        public Clan GetDeJureHolder(Settlement settlement)
        {
            return TryGetBarony(settlement, out FeudalTitleRecord title)
                ? ResolveClan(title.DeJureHolderClanId)
                : null;
        }

        public Clan GetDeFactoHolder(Settlement settlement)
        {
            return TryGetBarony(settlement, out FeudalTitleRecord title)
                ? ResolveClan(title.DeFactoHolderClanId)
                : null;
        }

        public IReadOnlyCollection<FeudalTitleRecord> GetTitlesHeldByClan(Clan clan, bool deJure = true, FeudalTitleType? titleType = null)
        {
            EnsureCollectionsInitialized();
            if (clan == null || string.IsNullOrWhiteSpace(clan.StringId))
                return Array.Empty<FeudalTitleRecord>();

            Dictionary<string, List<string>> index = deJure ? _titlesByDeJureHolder : _titlesByDeFactoHolder;
            if (!index.TryGetValue(clan.StringId, out List<string> titleIds))
                return Array.Empty<FeudalTitleRecord>();

            List<FeudalTitleRecord> titles = new List<FeudalTitleRecord>(titleIds.Count);
            foreach (string titleId in titleIds)
            {
                FeudalTitleRecord title = GetTitle(titleId);
                if (title != null && title.IsActive && (!titleType.HasValue || title.TitleType == titleType.Value))
                    titles.Add(title);
            }

            return titles;
        }

        public FeudalClaimRecord RegisterClaim(
            Clan claimantClan,
            FeudalTitleRecord targetTitle,
            FeudalClaimStrength strength,
            string source,
            Hero sourceHero = null,
            Clan originClan = null,
            float expiresDay = -1f,
            Hero carrierHero = null,
            int generationDepth = 0)
        {
            EnsureCollectionsInitialized();

            if (claimantClan == null || targetTitle == null || string.IsNullOrWhiteSpace(claimantClan.StringId) || string.IsNullOrWhiteSpace(targetTitle.TitleId))
                return null;

            if (targetTitle.DeJureHolderClanId == claimantClan.StringId)
                return null;

            List<FeudalClaimRecord> activePairClaims = _claimsByClanAndTitle
                .TryGetValue(claimantClan.StringId, out Dictionary<string, List<FeudalClaimRecord>> claimsByTitle)
                && claimsByTitle.TryGetValue(targetTitle.TitleId, out List<FeudalClaimRecord> pairClaims)
                    ? pairClaims.Where(IsClaimCurrentlyActive).ToList()
                    : new List<FeudalClaimRecord>();
            FeudalClaimRecord strongestExisting = activePairClaims
                .OrderByDescending(claim => claim.Strength)
                .ThenByDescending(claim => claim.CreatedDay)
                .FirstOrDefault();
            if (strongestExisting != null && strongestExisting.Strength >= strength)
            {
                int duplicatesDeactivated = 0;
                foreach (FeudalClaimRecord duplicate in activePairClaims.Where(claim => claim != strongestExisting))
                {
                    duplicate.SetActive(false);
                    duplicatesDeactivated++;
                }

                if (duplicatesDeactivated > 0)
                {
                    RequestClaimIndexRebuild();
                    BellumCivileLogger.Log($"Feudal duplicate active claims normalized during registration; claimant={claimantClan.StringId}; title={targetTitle.TitleId}; retained_strength={strongestExisting.Strength}; duplicates={duplicatesDeactivated}.");
                }

                return null;
            }

            if (strongestExisting != null)
            {
                foreach (FeudalClaimRecord existing in activePairClaims)
                    existing.SetActive(false);

                BellumCivileLogger.Log($"Feudal claim upgraded; claimant={claimantClan.StringId}; title={targetTitle.TitleId}; old_strength={strongestExisting.Strength}; new_strength={strength}; source={source ?? "unknown"}.");
            }

            string claimId = BuildClaimId(claimantClan.StringId, targetTitle.TitleId, strength, source);
            _claims.RemoveAll(claim => claim != null
                && claim.ClaimantClanId == claimantClan.StringId
                && claim.TargetTitleId == targetTitle.TitleId
                && claim.Strength == strength);

            FeudalClaimRecord record = new FeudalClaimRecord(
                claimId,
                claimantClan.StringId,
                targetTitle.TitleId,
                strength,
                source,
                sourceHero?.StringId,
                originClan?.StringId,
                CurrentDay,
                expiresDay,
                generationDepth,
                carrierHero?.StringId ?? sourceHero?.StringId,
                isActive: true);

            _claims.Add(record);
            RequestClaimIndexRebuild(record);
            if (_sessionLaunched)
            {
                Campaign.Current?.GetCampaignBehavior<FeudalPoliticalOptionsBehavior>()?.QueueClanForEvaluation(
                    claimantClan,
                    $"new {strength.ToString().ToLowerInvariant()} claim to {targetTitle.TitleId}",
                    C.FeudalClaimFabricationPoliticalFollowUpDays);
            }
            BellumCivileLogger.Log($"Feudal claim registered: claimant={claimantClan.StringId}; title={targetTitle.TitleId}; strength={strength}; depth={generationDepth}; carrier={record.CarrierHeroId}; source={source ?? "unknown"}.");
            return record;
        }

        public int ConfiscateNonBaronyTitlesForExile(Clan strippedClan, Clan recipientClan, Kingdom originKingdom, string reason)
        {
            EnsureCollectionsInitialized();

            if (strippedClan == null || string.IsNullOrWhiteSpace(strippedClan.StringId))
                return 0;

            recipientClan = recipientClan ?? originKingdom?.RulingClan;
            if (recipientClan == null
                || recipientClan == strippedClan
                || string.IsNullOrWhiteSpace(recipientClan.StringId))
            {
                return 0;
            }

            FeudalTitleRecord originPoliticalTitle = GetKingdomPoliticalTitle(originKingdom);
            string strippedClanId = strippedClan.StringId;
            string recipientClanId = recipientClan.StringId;
            List<FeudalTitleRecord> titlesToConfiscate = _titlesById.Values
                .Where(title =>
                    title != null
                    && title.IsActive
                    && title.TitleType > FeudalTitleType.Barony
                    && IsTitleHeldByClan(title, strippedClanId)
                    && !string.Equals(title.TitleId, originPoliticalTitle?.TitleId, StringComparison.Ordinal)
                    && IsTitleWithinOriginRealm(title, originKingdom))
                .OrderBy(title => title.TitleType)
                .ThenBy(title => title.TitleId)
                .ToList();

            int transferred = 0;
            foreach (FeudalTitleRecord title in titlesToConfiscate)
            {
                bool strippedHeldDeJure = string.Equals(title.DeJureHolderClanId, strippedClanId, StringComparison.Ordinal);
                bool strippedHeldDeFacto = string.Equals(title.DeFactoHolderClanId, strippedClanId, StringComparison.Ordinal);
                if (!strippedHeldDeJure && !strippedHeldDeFacto)
                    continue;

                string oldDeJure = title.DeJureHolderClanId;
                string oldDeFacto = title.DeFactoHolderClanId;

                if (strippedHeldDeJure)
                    title.SetDeJureHolder(recipientClanId);
                if (strippedHeldDeFacto)
                    title.SetDeFactoHolder(recipientClanId);

                if (strippedHeldDeJure && strippedClan.Leader != null && !strippedClan.Leader.IsDead)
                {
                    RegisterClaim(
                        strippedClan,
                        title,
                        FeudalClaimStrength.Strong,
                        "title_confiscated_exile",
                        recipientClan.Leader,
                        strippedClan,
                        carrierHero: strippedClan.Leader,
                        generationDepth: 1);
                }

                if (originKingdom != null && string.IsNullOrWhiteSpace(title.AssociatedKingdomId))
                    title.SetAssociatedKingdom(originKingdom.StringId);

                title.MarkSynced(CurrentDay);
                RemoveRedundantClaimsForHolder(recipientClan, title);
                QueueServiceReviewForTitleAndChildren(title, reason);
                transferred++;

                BellumCivileLogger.Log(
                    $"Confiscated non-barony feudal title during exile; title={title.TitleId}; stripped={strippedClanId}; recipient={recipientClanId}; de_jure={oldDeJure}->{title.DeJureHolderClanId}; de_facto={oldDeFacto}->{title.DeFactoHolderClanId}; origin={originKingdom?.StringId ?? "none"}; reason={reason ?? "unknown"}.");
            }

            if (transferred <= 0)
                return 0;

            RebuildRuntimeIndexes();
            int politicalRepairs = ReconcileAllDeFactoParents($"title confiscation after exile of {strippedClanId}");
            if (politicalRepairs > 0)
                RebuildRuntimeIndexes();

            BellumCivileLogger.Log(
                $"Confiscated non-barony titles for exiled clan; clan={strippedClanId}; recipient={recipientClanId}; count={transferred}; political_repairs={politicalRepairs}; reason={reason ?? "unknown"}.");
            return transferred;
        }

        public bool TryEscheatEstateForExtinctSuccessionLine(
            Clan sourceClan,
            Clan crownClan,
            Kingdom kingdom,
            Hero triggerHero,
            bool dynasticLineExtinction,
            out int transferredSettlements,
            out int transferredTitles,
            out string failureReason)
        {
            transferredSettlements = 0;
            transferredTitles = 0;
            failureReason = null;
            EnsureCollectionsInitialized();

            if (sourceClan == null
                || crownClan?.Leader == null
                || kingdom == null
                || sourceClan == crownClan
                || sourceClan.Kingdom != kingdom
                || crownClan.Kingdom != kingdom
                || kingdom.RulingClan != crownClan)
            {
                failureReason = "invalid source clan, crown, or realm";
                return false;
            }

            string sourceClanId = sourceClan.StringId;
            string crownClanId = crownClan.StringId;
            string claimSource = dynasticLineExtinction
                ? "dynastic_line_extinction_escheat"
                : "gender_line_extinction_escheat";
            string repairReason = dynasticLineExtinction
                ? "dynastic line extinction escheat"
                : "gender line extinction escheat";
            var heldTitles = _titlesById.Values
                .Where(title => title != null
                    && title.IsActive
                    && (string.Equals(title.DeJureHolderClanId, sourceClanId, StringComparison.Ordinal)
                        || string.Equals(title.DeFactoHolderClanId, sourceClanId, StringComparison.Ordinal)))
                .Select(title => new
                {
                    Title = title,
                    HeldDeJure = string.Equals(title.DeJureHolderClanId, sourceClanId, StringComparison.Ordinal),
                    HeldDeFacto = string.Equals(title.DeFactoHolderClanId, sourceClanId, StringComparison.Ordinal)
                })
                .ToList();
            List<Settlement> settlements = sourceClan.Settlements?
                .Where(settlement => settlement != null && settlement.OwnerClan == sourceClan)
                .ToList() ?? new List<Settlement>();

            if (heldTitles.Count == 0 && settlements.Count == 0)
                return true;

            foreach (Settlement settlement in settlements)
            {
                if (settlement.OwnerClan != sourceClan)
                    continue;

                ChangeOwnerOfSettlementAction.ApplyByDefault(crownClan.Leader, settlement);
                transferredSettlements++;
            }

            Hero claimCarrier = sourceClan.Leader != null && !sourceClan.Leader.IsDead
                ? sourceClan.Leader
                : null;
            foreach (var held in heldTitles)
            {
                FeudalTitleRecord title = held.Title;
                if (held.HeldDeJure && !string.Equals(title.DeJureHolderClanId, crownClanId, StringComparison.Ordinal))
                    title.SetDeJureHolder(crownClanId);

                if (held.HeldDeFacto && !string.Equals(title.DeFactoHolderClanId, crownClanId, StringComparison.Ordinal))
                    title.SetDeFactoHolder(crownClanId);

                title.SetAssociatedKingdom(kingdom.StringId);
                title.MarkSynced(CurrentDay);
                RemoveRedundantClaimsForHolder(crownClan, title);
                RegisterClaim(
                    sourceClan,
                    title,
                    FeudalClaimStrength.Strong,
                    claimSource,
                    triggerHero,
                    sourceClan,
                    carrierHero: claimCarrier,
                    generationDepth: 0);
                QueueServiceReviewForTitleAndChildren(title, repairReason);
                transferredTitles++;
            }

            RebuildRuntimeIndexes();
            int politicalRepairs = ReconcileAllDeFactoParents(repairReason);
            if (politicalRepairs > 0)
                RebuildRuntimeIndexes();

            BellumCivileLogger.Log(
                $"Succession-line estate escheated; cause={(dynasticLineExtinction ? "dynastic" : "gender")}; source={sourceClanId}; crown={crownClanId}; kingdom={kingdom.StringId}; trigger_hero={triggerHero?.StringId ?? "unknown"}; settlements={transferredSettlements}; titles={transferredTitles}; political_repairs={politicalRepairs}.");
            return true;
        }

        public void RegisterPartitionCadetClaims(Clan parentClan, Clan cadetClan, Hero deadLeader, Hero cadetFounder)
        {
            EnsureCollectionsInitialized();
            if (parentClan == null || cadetClan == null || deadLeader == null || cadetFounder == null || parentClan == cadetClan)
                return;

            int registered = 0;
            BeginClaimIndexBatch();
            try
            {
                foreach (FeudalTitleRecord title in GetTitlesHeldByClan(parentClan, deJure: true))
                {
                    if (title == null || !title.IsActive || title.DeFactoHolderClanId == cadetClan.StringId)
                        continue;

                    if (RegisterClaim(
                        cadetClan,
                        title,
                        FeudalClaimStrength.Strong,
                        "partition_bloodright",
                        deadLeader,
                        parentClan,
                        carrierHero: cadetFounder,
                        generationDepth: 1) != null)
                    {
                        registered++;
                    }
                }

                foreach (FeudalClaimRecord inheritedClaim in _claims.ToList())
                {
                    if (inheritedClaim == null
                        || !IsClaimCurrentlyActive(inheritedClaim)
                        || inheritedClaim.ClaimantClanId != parentClan.StringId)
                    {
                        continue;
                    }

                    FeudalTitleRecord title = GetTitle(inheritedClaim.TargetTitleId);
                    FeudalClaimStrength? inheritedStrength = GetInheritedClaimStrength(inheritedClaim);
                    if (title == null || !inheritedStrength.HasValue)
                        continue;

                    if (RegisterClaim(
                        cadetClan,
                        title,
                        inheritedStrength.Value,
                        "partition_inherited_claim",
                        deadLeader,
                        ResolveClan(inheritedClaim.OriginClanId) ?? parentClan,
                        inheritedClaim.ExpiresDay,
                        cadetFounder,
                        inheritedClaim.GenerationDepth + 1) != null)
                    {
                        registered++;
                    }
                }
            }
            finally
            {
                EndClaimIndexBatch();
            }

            if (registered > 0)
                BellumCivileLogger.Log($"Feudal claims inherited by partition cadet; parent={parentClan.StringId}; cadet={cadetClan.StringId}; founder={cadetFounder.StringId}; claims={registered}.");
        }

        public void LegalizePartitionInheritance(Clan heirClan, Town inheritedFief)
        {
            EnsureCollectionsInitialized();
            if (heirClan == null || inheritedFief?.Settlement == null || !TryGetBarony(inheritedFief.Settlement, out FeudalTitleRecord title))
                return;

            string oldDeJure = title.DeJureHolderClanId;
            string oldDeFacto = title.DeFactoHolderClanId;
            title.SetDeJureHolder(heirClan.StringId);
            title.SetDeFactoHolder(heirClan.StringId);
            title.MarkSynced(CurrentDay);

            _claims.RemoveAll(claim => claim != null
                && claim.ClaimantClanId == heirClan.StringId
                && claim.TargetTitleId == title.TitleId);

            RebuildRuntimeIndexes();
            QueueServiceReview(title, "partition title legalized");
            BellumCivileLogger.Log(
                $"Feudal partition title legalized; title={title.TitleId}; heir_clan={heirClan.StringId}; de_jure={oldDeJure}->{title.DeJureHolderClanId}; de_facto={oldDeFacto}->{title.DeFactoHolderClanId}.");
        }

        public void LegalizeTitleInheritance(Clan heirClan, FeudalTitleRecord inheritedTitle, string reason)
        {
            LegalizeTitleInheritance(heirClan, inheritedTitle, reason, preserveDeFacto: false);
        }

        public void LegalizeTitleInheritance(Clan heirClan, FeudalTitleRecord inheritedTitle, string reason, bool preserveDeFacto)
        {
            EnsureCollectionsInitialized();
            if (heirClan == null || inheritedTitle == null || !inheritedTitle.IsActive || string.IsNullOrWhiteSpace(heirClan.StringId))
                return;

            string oldDeJure = inheritedTitle.DeJureHolderClanId;
            string oldDeFacto = inheritedTitle.DeFactoHolderClanId;
            inheritedTitle.SetDeJureHolder(heirClan.StringId);
            if (!preserveDeFacto) inheritedTitle.SetDeFactoHolder(heirClan.StringId);
            inheritedTitle.MarkSynced(CurrentDay);
            RemoveRedundantClaimsForHolder(heirClan, inheritedTitle);
            RebuildRuntimeIndexes();
            QueueServiceReviewForTitleAndChildren(inheritedTitle, reason);

            BellumCivileLogger.Log(
                $"Feudal title inheritance legalized; title={inheritedTitle.TitleId}; heir_clan={heirClan.StringId}; type={inheritedTitle.TitleType}; de_jure={oldDeJure}->{inheritedTitle.DeJureHolderClanId}; de_facto={oldDeFacto}->{inheritedTitle.DeFactoHolderClanId}; reason={reason ?? "unknown"}.");
        }

        internal bool CompleteCourtTitleGrant(CourtTitleGrantRecord plan, out string reason)
        {
            reason = "court title rights changed";
            var title = GetTitle(plan.TitleId);
            if (!plan.Paid || !plan.TransferAttempted || title?.IsActive != true
                || !CourtTitleGrantRules.Recoverable(plan, title.DeJureHolderClanId, title.DeFactoHolderClanId)) return false;
            if (!CourtTitleGrantRules.Delivered(plan, title.DeJureHolderClanId, title.DeFactoHolderClanId)
                && !TryGrantTitleByRuler(plan.Grantor, plan.Recipient, title, false, out _, out reason)) return false;
            // A native callback may interrupt after ownership changes but before hierarchy repair.
            RebuildRuntimeIndexes();
            ReconcileAllDeFactoParents("court title delivery recovery");
            RebuildRuntimeIndexes();
            QueueServiceReviewForTitleAndChildren(title, "court title delivery");
            return CourtTitleGrantRules.Delivered(plan, title.DeJureHolderClanId, title.DeFactoHolderClanId);
        }

        public bool TryGrantTitleByRuler(
            Clan grantorClan,
            Clan recipientClan,
            FeudalTitleRecord title,
            bool createIndependentRealm,
            out Kingdom independentKingdom,
            out string failureReason)
        {
            independentKingdom = null;
            failureReason = null;
            EnsureCollectionsInitialized();

            if (grantorClan == null || recipientClan == null || title == null || !title.IsActive || title.IsDeliberatelyDissolved)
            {
                failureReason = "missing grantor, recipient, or title";
                return false;
            }

            if (grantorClan == recipientClan)
            {
                failureReason = "cannot grant title to self";
                return false;
            }

            Kingdom grantorKingdom = grantorClan.Kingdom;
            if (grantorKingdom == null || grantorKingdom.RulingClan != grantorClan)
            {
                failureReason = "grantor is not the ruling clan";
                return false;
            }

            if (recipientClan.Kingdom != grantorKingdom || recipientClan.IsUnderMercenaryService || recipientClan.IsEliminated)
            {
                failureReason = "recipient is not a settled vassal";
                return false;
            }

            bool grantorHeldDeJure = string.Equals(title.DeJureHolderClanId, grantorClan.StringId, StringComparison.Ordinal);
            bool grantorHeldDeFacto = string.Equals(title.DeFactoHolderClanId, grantorClan.StringId, StringComparison.Ordinal);
            if (!grantorHeldDeJure && !grantorHeldDeFacto)
            {
                failureReason = "title is not held directly by your clan";
                return false;
            }

            if (IsRealmSovereignTitle(grantorKingdom, title))
            {
                failureReason = "the sovereign title of the realm cannot be granted";
                return false;
            }

            FeudalTitleRecord grantorSovereign = GetRealmSovereignTitle(grantorKingdom, FeudalHierarchyMode.DeFacto) ?? GetKingdomPoliticalTitle(grantorKingdom);
            if (createIndependentRealm
                && (grantorSovereign == null
                    || title.TitleType != grantorSovereign.TitleType
                    || string.Equals(title.TitleId, grantorSovereign.TitleId, StringComparison.Ordinal)))
            {
                failureReason = "title is not a spare peer title";
                return false;
            }

            if (title.TitleType > FeudalTitleType.Barony && !RecipientHoldsImmediateChildTitle(recipientClan, title))
            {
                failureReason = "recipient holds no immediate child title";
                return false;
            }

            string oldDeJure = title.DeJureHolderClanId;
            string oldDeFacto = title.DeFactoHolderClanId;
            Kingdom oldRecipientKingdom = recipientClan.Kingdom;

            if (createIndependentRealm)
            {
                independentKingdom = CreateIndependentRealmFromGrantedTitle(grantorKingdom, recipientClan, title);
                if (independentKingdom == null)
                {
                    failureReason = "independent realm could not be created";
                    return false;
                }
            }

            if (grantorHeldDeFacto && title.TitleType == FeudalTitleType.Barony)
            {
                Settlement settlement = ResolveSettlement(title.CapitalSettlementId);
                if (settlement != null && settlement.OwnerClan != recipientClan && recipientClan.Leader != null)
                    ChangeOwnerOfSettlementAction.ApplyByKingDecision(recipientClan.Leader, settlement);
            }

            if (grantorHeldDeJure)
                title.SetDeJureHolder(recipientClan.StringId);
            if (grantorHeldDeFacto)
                title.SetDeFactoHolder(recipientClan.StringId);

            if (createIndependentRealm)
            {
                title.SetDeFactoParentTitle(string.Empty);
                MoveGrantedTitleClusterToIndependentRealm(grantorKingdom, independentKingdom, recipientClan, title);
                RegisterIndependentRealmShell(independentKingdom, title, "royal title grant");
            }

            title.SetAssociatedKingdom(createIndependentRealm && independentKingdom != null
                ? independentKingdom.StringId
                : grantorKingdom.StringId);
            title.MarkSynced(CurrentDay);
            RemoveRedundantClaimsForHolder(recipientClan, title);
            if (grantorHeldDeJure)
                RemoveClaimsForClanToTitle(grantorClan, title);

            RebuildRuntimeIndexes();
            int politicalRepairs = ReconcileAllDeFactoParents($"royal title grant {grantorClan.StringId} to {recipientClan.StringId}");
            if (politicalRepairs > 0)
                RebuildRuntimeIndexes();
            QueueServiceReviewForTitleAndChildren(title, "royal title grant");

            BellumCivileLogger.Log(
                $"Feudal title granted by ruler; grantor={grantorClan.StringId}; recipient={recipientClan.StringId}; title={title.TitleId}; type={title.TitleType}; independent={independentKingdom?.StringId ?? "no"}; recipient_old_kingdom={oldRecipientKingdom?.StringId ?? "none"}; de_jure={oldDeJure}->{title.DeJureHolderClanId}; de_facto={oldDeFacto}->{title.DeFactoHolderClanId}; repairs={politicalRepairs}.");
            return true;
        }

        public bool TryPromotePartitionInheritanceToIndependentRealm(
            Kingdom parentKingdom,
            Clan heirClan,
            FeudalTitleRecord inheritedTitle,
            out Kingdom independentKingdom,
            out string failureReason)
        {
            independentKingdom = null;
            failureReason = null;
            EnsureCollectionsInitialized();

            if (!IsValidPermanentKingdom(parentKingdom)
                || heirClan == null
                || heirClan.IsEliminated
                || heirClan.Kingdom != parentKingdom
                || inheritedTitle == null
                || !inheritedTitle.IsActive)
            {
                failureReason = "missing parent realm, inheriting clan, or inherited title";
                return false;
            }

            string heirClanId = heirClan.StringId;
            if (!string.Equals(inheritedTitle.DeJureHolderClanId, heirClanId, StringComparison.Ordinal)
                || !string.Equals(inheritedTitle.DeFactoHolderClanId, heirClanId, StringComparison.Ordinal))
            {
                failureReason = "the inheriting clan does not fully hold the peer title";
                return false;
            }

            FeudalTitleRecord parentSovereign = GetRealmSovereignTitle(parentKingdom, FeudalHierarchyMode.DeFacto)
                ?? GetKingdomPoliticalTitle(parentKingdom);
            if (parentSovereign == null
                || parentSovereign.TitleId == inheritedTitle.TitleId
                || parentSovereign.TitleType != inheritedTitle.TitleType)
            {
                failureReason = "the inherited title is not a coequal sovereign title";
                return false;
            }

            HashSet<string> inheritedSettlementIds = new HashSet<string>(GetDescendantBaronyTitles(inheritedTitle, FeudalHierarchyMode.DeFacto)
                .Where(title => title != null && title.IsActive && !string.IsNullOrWhiteSpace(title.CapitalSettlementId))
                .Select(title => title.CapitalSettlementId));
            if (inheritedTitle.TitleType == FeudalTitleType.Barony && !string.IsNullOrWhiteSpace(inheritedTitle.CapitalSettlementId))
                inheritedSettlementIds.Add(inheritedTitle.CapitalSettlementId);
            if (!heirClan.Settlements.Any(settlement => settlement != null && inheritedSettlementIds.Contains(settlement.StringId)))
            {
                failureReason = "the inherited peer title has no landed capital under the inheriting clan";
                return false;
            }

            List<Kingdom> inheritedEnemies = Kingdom.All
                .Where(kingdom => kingdom != null
                    && kingdom != parentKingdom
                    && !kingdom.IsEliminated
                    && !BellumKingdomVisibilityHelper.IsTemporaryBellumKingdom(kingdom)
                    && parentKingdom.IsAtWarWith(kingdom))
                .ToList();
            ClientKingdomBehavior clientBehavior = Campaign.Current?.GetCampaignBehavior<ClientKingdomBehavior>();
            Kingdom inheritedSuzerain = clientBehavior?.GetSuzerain(parentKingdom);

            TextObject encyclopediaText = new TextObject("{=BC_PartitionSuccession_IndependentRealmText}Divided from {PARENT_KINGDOM} by dynastic succession, {NEW_KINGDOM} passed to a coequal blood heir as an independent realm.");
            independentKingdom = CreateIndependentRealmFromTitle(
                parentKingdom,
                heirClan,
                inheritedTitle,
                "bc_partition_indep",
                encyclopediaText);
            if (independentKingdom == null)
            {
                failureReason = "the independent successor realm could not be created";
                return false;
            }
            Kingdom successorKingdom = independentKingdom;

            inheritedTitle.SetDeFactoParentTitle(string.Empty);
            MoveGrantedTitleClusterToIndependentRealm(
                parentKingdom,
                successorKingdom,
                heirClan,
                inheritedTitle,
                requireExclusiveCluster: true,
                serviceReason: "coequal partition succession");
            RegisterIndependentRealmShell(successorKingdom, inheritedTitle, "coequal partition succession");
            inheritedTitle.SetAssociatedKingdom(successorKingdom.StringId);
            inheritedTitle.MarkSynced(CurrentDay);
            RebelPolicyHelper.CopyPolicies(parentKingdom, independentKingdom);

            if (inheritedSuzerain != null && !inheritedSuzerain.IsEliminated)
            {
                clientBehavior?.TryInheritClientage(parentKingdom, successorKingdom, out _);
            }

            foreach (Kingdom enemy in inheritedEnemies)
            {
                if (enemy == inheritedSuzerain || successorKingdom.IsAtWarWith(enemy))
                    continue;

                ModIntegrationHelper.ExecuteWithAIInfluenceDiplomacyBypass(
                    () => DeclareWarAction.ApplyByDefault(successorKingdom, enemy));
            }

            RebuildRuntimeIndexes();
            int politicalRepairs = ReconcileAllDeFactoParents($"coequal partition succession {inheritedTitle.TitleId}");
            RebuildRuntimeIndexes();
            QueueServiceReviewForTitleAndChildren(inheritedTitle, "coequal partition succession");
            Campaign.Current?.GetCampaignBehavior<IdeologyBehavior>()?.RefreshKingdomIdeologies(parentKingdom);
            Campaign.Current?.GetCampaignBehavior<IdeologyBehavior>()?.RefreshKingdomIdeologies(successorKingdom);

            BellumCivileLogger.Log(
                $"Coequal partition successor created; parent={parentKingdom.StringId}; successor={successorKingdom.StringId}; heir_clan={heirClan.StringId}; title={inheritedTitle.TitleId}; type={inheritedTitle.TitleType}; inherited_wars={inheritedEnemies.Count}; inherited_clientage={inheritedSuzerain?.StringId ?? "none"}; political_repairs={politicalRepairs}.");
            return true;
        }

        public bool TryGrantTitleForTesting(
            Clan recipientClan,
            FeudalTitleRecord title,
            bool transferDeJure,
            out Settlement transferredSettlement,
            out string failureReason)
        {
            transferredSettlement = null;
            failureReason = null;
            EnsureCollectionsInitialized();

            if (recipientClan == null || recipientClan.IsEliminated || string.IsNullOrWhiteSpace(recipientClan.StringId))
            {
                failureReason = "the player clan is unavailable";
                return false;
            }

            if (recipientClan.Leader == null || recipientClan.Leader.IsDead)
            {
                failureReason = "the player clan has no living leader";
                return false;
            }

            if (title == null || !title.IsActive)
            {
                failureReason = "the selected title is unavailable or inactive";
                return false;
            }

            string oldDeJure = title.DeJureHolderClanId;
            string oldDeFacto = title.DeFactoHolderClanId;

            if (title.TitleType == FeudalTitleType.Barony && !string.IsNullOrWhiteSpace(title.CapitalSettlementId))
            {
                Settlement settlement = ResolveSettlement(title.CapitalSettlementId);
                if (settlement != null && settlement.OwnerClan != recipientClan)
                {
                    try
                    {
                        if (!transferDeJure)
                            _deFactoOnlySettlementTransferTitleId = title.TitleId;

                        ChangeOwnerOfSettlementAction.ApplyByKingDecision(recipientClan.Leader, settlement);
                        transferredSettlement = settlement;
                    }
                    finally
                    {
                        _deFactoOnlySettlementTransferTitleId = null;
                    }
                }
            }

            title.SetDeFactoHolder(recipientClan.StringId);
            if (transferDeJure)
                title.SetDeJureHolder(recipientClan.StringId);
            else
                title.SetDeJureHolder(oldDeJure);

            title.MarkSynced(CurrentDay);
            if (transferDeJure)
                RemoveRedundantClaimsForHolder(recipientClan, title);
            RebuildRuntimeIndexes();
            int politicalRepairs = ReconcileAllDeFactoParents(
                $"testing title grant {title.TitleId} to {recipientClan.StringId}");
            if (politicalRepairs > 0)
                RebuildRuntimeIndexes();
            QueueServiceReviewForTitleAndChildren(title, "testing title grant");
            ReconcileCurrentRealmTitles();
            Campaign.Current?.GetCampaignBehavior<FeudalDeJureDriftBehavior>()?.RefreshTitleFamily(title);

            BellumCivileLogger.Log(
                $"Feudal title granted by console for testing; recipient={recipientClan.StringId}; title={title.TitleId}; type={title.TitleType}; full={transferDeJure}; settlement={transferredSettlement?.StringId ?? "none"}; de_jure={oldDeJure}->{title.DeJureHolderClanId}; de_facto={oldDeFacto}->{title.DeFactoHolderClanId}; repairs={politicalRepairs}.");
            return true;
        }

        public bool TryResolveClaimFeudForClaimant(
            Clan claimantClan,
            Clan formerHolderClan,
            FeudalTitleRecord title,
            string reason,
            out string result)
        {
            result = null;
            EnsureCollectionsInitialized();

            if (claimantClan == null || title == null || !title.IsActive || string.IsNullOrWhiteSpace(claimantClan.StringId))
            {
                result = "missing claimant or title";
                return false;
            }

            string oldDeJure = title.DeJureHolderClanId;
            string oldDeFacto = title.DeFactoHolderClanId;

            if (title.TitleType == FeudalTitleType.Barony)
            {
                Settlement settlement = ResolveSettlement(title.CapitalSettlementId);
                if (settlement != null)
                {
                    if (claimantClan.Leader == null || claimantClan.Leader.IsDead)
                    {
                        result = "claimant has no living leader to receive the disputed settlement";
                        return false;
                    }

                    if (settlement.OwnerClan != claimantClan)
                        ChangeOwnerOfSettlementAction.ApplyByKingDecision(claimantClan.Leader, settlement);
                    else if (settlement.Town != null)
                        settlement.Town.IsOwnerUnassigned = false;

                    if (settlement.OwnerClan != claimantClan
                        || (settlement.Town != null && settlement.Town.IsOwnerUnassigned))
                    {
                        result = "the disputed settlement could not be assigned permanently to the claimant";
                        return false;
                    }

                    Campaign.Current?.GetCampaignBehavior<FiefDeliberationBehavior>()?
                        .ClearFiefVoteStateForSettlement(settlement, reason ?? "claim feud claimant victory");
                }
            }

            title.SetDeJureHolder(claimantClan.StringId);
            title.SetDeFactoHolder(claimantClan.StringId);
            title.MarkSynced(CurrentDay);
            RemoveRedundantClaimsForHolder(claimantClan, title);

            if (formerHolderClan != null && formerHolderClan != claimantClan && formerHolderClan.Leader != null && !formerHolderClan.Leader.IsDead)
            {
                RegisterClaim(
                    formerHolderClan,
                    title,
                    FeudalClaimStrength.Weak,
                    "claim_feud_dispossessed",
                    claimantClan.Leader,
                    formerHolderClan,
                    carrierHero: formerHolderClan.Leader,
                    generationDepth: 1);
            }

            RebuildRuntimeIndexes();
            QueueServiceReviewForTitleAndChildren(title, reason);
            result = $"title={title.TitleId}; claimant={claimantClan.StringId}; old_de_jure={oldDeJure}; old_de_facto={oldDeFacto}; reason={reason ?? "unknown"}";
            BellumCivileLogger.Log($"Feudal claim feud resolved for claimant; {result}.");
            return true;
        }

        public bool TryResolveClaimFeudForHolder(
            Clan claimantClan,
            Clan holderClan,
            FeudalTitleRecord title,
            FeudalClaimStrength claimStrength,
            string reason,
            out string result)
        {
            result = null;
            EnsureCollectionsInitialized();

            if (claimantClan == null || holderClan == null || title == null || !title.IsActive)
            {
                result = "missing claimant, holder, or title";
                return false;
            }

            string oldDeJure = title.DeJureHolderClanId;
            string oldDeFacto = title.DeFactoHolderClanId;

            if (title.DeJureHolderClanId == claimantClan.StringId)
                title.SetDeJureHolder(holderClan.StringId);

            DeactivateClaimsForTitle(claimantClan, title);
            if (claimStrength == FeudalClaimStrength.Strong && claimantClan.Leader != null && !claimantClan.Leader.IsDead)
            {
                RegisterClaim(
                    claimantClan,
                    title,
                    FeudalClaimStrength.Weak,
                    "claim_feud_downgraded",
                    holderClan.Leader,
                    claimantClan,
                    carrierHero: claimantClan.Leader,
                    generationDepth: 2);
            }

            title.MarkSynced(CurrentDay);
            RebuildRuntimeIndexes();
            QueueServiceReviewForTitleAndChildren(title, reason);
            result = $"title={title.TitleId}; holder={holderClan.StringId}; claimant={claimantClan.StringId}; old_de_jure={oldDeJure}; old_de_facto={oldDeFacto}; strength={claimStrength}; reason={reason ?? "unknown"}";
            BellumCivileLogger.Log($"Feudal claim feud resolved for holder; {result}.");
            return true;
        }

        public bool TryUsurpTitle(Clan usurperClan, FeudalTitleRecord title, string reason, out string failureReason)
        {
            failureReason = null;
            EnsureCollectionsInitialized();

            FeudalTitleUsurpationAssessment assessment = FeudalTitleUsurpationAssessmentService.Evaluate(
                this,
                usurperClan,
                title,
                checkResources: false);
            if (!assessment.CanUsurp)
            {
                failureReason = assessment.Reason;
                return false;
            }

            Clan oldHolder = ResolveClan(title.DeJureHolderClanId);
            Hero usurperLeader = usurperClan.Leader;
            if (usurperLeader == null || usurperLeader.IsDead)
            {
                failureReason = "usurper has no living leader";
                return false;
            }

            string oldDeJure = title.DeJureHolderClanId;
            string oldDeFacto = title.DeFactoHolderClanId;
            title.SetDeJureHolder(usurperClan.StringId);
            title.SetDeFactoHolder(usurperClan.StringId);
            if (oldHolder?.Leader != null && !oldHolder.Leader.IsDead && oldHolder != usurperClan)
            {
                RegisterClaim(
                    oldHolder,
                    title,
                    FeudalClaimStrength.Strong,
                    "title_usurped",
                    usurperLeader,
                    oldHolder,
                    carrierHero: oldHolder.Leader,
                    generationDepth: 1);
            }

            title.MarkSynced(CurrentDay);
            RemoveRedundantClaimsForHolder(usurperClan, title);
            RebuildRuntimeIndexes();
            int hierarchyRepairs = ReconcileAllDeFactoParents($"title usurpation {title.TitleId}");
            if (hierarchyRepairs > 0)
                RebuildRuntimeIndexes();
            QueueServiceReviewForTitleAndChildren(title, reason);

            BellumCivileLogger.Log(
                $"Feudal title usurped; title={title.TitleId}; type={title.TitleType}; usurper={usurperClan.StringId}; old_de_jure={oldDeJure}; old_de_facto={oldDeFacto}; reason={reason ?? "unknown"}.");
            ReconcileCurrentRealmTitles();
            return true;
        }

        public bool IsCurrentRealmSovereignTitle(Clan claimantClan, FeudalTitleRecord title)
        {
            Kingdom kingdom = claimantClan?.Kingdom;
            if (claimantClan == null
                || title == null
                || claimantClan == kingdom?.RulingClan
                || !IsValidPermanentKingdom(kingdom))
            {
                return false;
            }

            FeudalTitleRecord deJureSovereign = GetRealmSovereignTitle(kingdom, FeudalHierarchyMode.DeJure);
            FeudalTitleRecord deFactoSovereign = GetRealmSovereignTitle(kingdom, FeudalHierarchyMode.DeFacto);
            FeudalTitleRecord politicalTitle = GetKingdomPoliticalTitle(kingdom);
            return string.Equals(title.TitleId, deJureSovereign?.TitleId, StringComparison.Ordinal)
                || string.Equals(title.TitleId, deFactoSovereign?.TitleId, StringComparison.Ordinal)
                || string.Equals(title.TitleId, politicalTitle?.TitleId, StringComparison.Ordinal);
        }

        public FeudalSovereignElevationPreview GetSovereignElevationPreview(Clan claimantClan, FeudalTitleRecord title)
        {
            EnsureCollectionsInitialized();
            if (title == null || !title.IsActive)
                return new FeudalSovereignElevationPreview();

            HashSet<string> retainedTitleIds = new HashSet<string>(
                GetTitleAndDescendants(title, FeudalHierarchyMode.DeJure)
                    .Where(record => record != null && record.IsActive)
                    .Select(record => record.TitleId),
                StringComparer.Ordinal);
            retainedTitleIds.Add(title.TitleId);
            return BuildSovereignElevationPreview(
                claimantClan,
                title.TitleType,
                title.TitleId,
                retainedTitleIds);
        }

        public FeudalSovereignElevationPreview GetFormationSovereignElevationPreview(
            Clan claimantClan,
            FeudalTitleFormationCandidate candidate)
        {
            EnsureCollectionsInitialized();
            if (candidate == null || candidate.ChildTitleIds == null || candidate.ChildTitleIds.Count == 0)
                return new FeudalSovereignElevationPreview();

            HashSet<string> retainedTitleIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (FeudalTitleRecord childTitle in candidate.ChildTitleIds
                .Select(GetTitle)
                .Where(record => record != null && record.IsActive))
            {
                foreach (FeudalTitleRecord retainedTitle in GetTitleAndDescendants(childTitle, FeudalHierarchyMode.DeJure))
                {
                    if (retainedTitle != null && retainedTitle.IsActive)
                        retainedTitleIds.Add(retainedTitle.TitleId);
                }
            }

            return BuildSovereignElevationPreview(
                claimantClan,
                candidate.TargetType,
                prospectiveTitleId: null,
                retainedTitleIds);
        }

        private FeudalSovereignElevationPreview BuildSovereignElevationPreview(
            Clan claimantClan,
            FeudalTitleType acquiredTitleType,
            string prospectiveTitleId,
            HashSet<string> retainedTitleIds)
        {
            FeudalSovereignElevationPreview preview = new FeudalSovereignElevationPreview();
            Kingdom parentKingdom = claimantClan?.Kingdom;
            if (claimantClan == null
                || !IsValidPermanentKingdom(parentKingdom)
                || claimantClan == parentKingdom.RulingClan)
            {
                return preview;
            }

            FeudalTitleRecord parentSovereign = GetRealmSovereignTitle(parentKingdom, FeudalHierarchyMode.DeFacto)
                ?? GetKingdomPoliticalTitle(parentKingdom);
            if (parentSovereign == null
                || (!string.IsNullOrWhiteSpace(prospectiveTitleId)
                    && string.Equals(parentSovereign.TitleId, prospectiveTitleId, StringComparison.Ordinal))
                || acquiredTitleType < parentSovereign.TitleType)
            {
                return preview;
            }

            retainedTitleIds = retainedTitleIds ?? new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> retainedSettlementIds = new HashSet<string>(
                retainedTitleIds
                    .Select(GetTitle)
                    .Where(record => record != null
                        && record.TitleType == FeudalTitleType.Barony
                        && !string.IsNullOrWhiteSpace(record.CapitalSettlementId))
                    .Select(record => record.CapitalSettlementId),
                StringComparer.Ordinal);

            preview.IsRequired = true;
            preview.ParentKingdom = parentKingdom;
            preview.ParentSovereignTitle = parentSovereign;
            preview.ExternalSettlementCount = claimantClan.Settlements
                .Count(settlement => settlement != null && !retainedSettlementIds.Contains(settlement.StringId));
            preview.ExternalUpperTitleCount = _titlesById.Values.Count(record => record != null
                && record.IsActive
                && record.TitleType > FeudalTitleType.Barony
                && !retainedTitleIds.Contains(record.TitleId)
                && (string.Equals(record.DeJureHolderClanId, claimantClan.StringId, StringComparison.Ordinal)
                    || string.Equals(record.DeFactoHolderClanId, claimantClan.StringId, StringComparison.Ordinal)));
            return preview;
        }

        public bool TryPromoteSovereignTitleToIndependentRealm(
            Clan claimantClan,
            FeudalTitleRecord sovereignTitle,
            FeudalSovereignElevationChoice choice,
            string acquisitionReason,
            out Kingdom independentKingdom,
            out string failureReason)
        {
            independentKingdom = null;
            failureReason = null;
            EnsureCollectionsInitialized();
            string transitionReason = string.IsNullOrWhiteSpace(acquisitionReason)
                ? "sovereign title acquisition"
                : acquisitionReason;

            FeudalSovereignElevationPreview preview = GetSovereignElevationPreview(claimantClan, sovereignTitle);
            if (!preview.IsRequired || preview.ParentKingdom == null)
            {
                failureReason = "the title does not require an independent sovereign realm";
                return false;
            }

            if (choice != FeudalSovereignElevationChoice.PeacefulSeparation
                && choice != FeudalSovereignElevationChoice.RetainHoldingsAndRebel)
            {
                failureReason = "no valid terms of separation were chosen";
                return false;
            }

            if (!string.Equals(sovereignTitle.DeJureHolderClanId, claimantClan.StringId, StringComparison.Ordinal)
                || !string.Equals(sovereignTitle.DeFactoHolderClanId, claimantClan.StringId, StringComparison.Ordinal))
            {
                failureReason = "the claimant does not fully hold the sovereign title";
                return false;
            }

            Kingdom parentKingdom = preview.ParentKingdom;
            Clan parentRulingClan = parentKingdom.RulingClan;
            if (parentRulingClan?.Leader == null || parentRulingClan.Leader.IsDead)
            {
                failureReason = "the former realm has no ruling clan able to receive relinquished lands";
                return false;
            }

            TextObject encyclopediaText = new TextObject("{=BC_SovereignElevation_IndependentRealmText}Having secured the {TITLE_NAME}, {RULER_NAME} separated {NEW_KINGDOM} from {PARENT_KINGDOM} as an independent realm.");
            encyclopediaText.SetTextVariable("TITLE_NAME", new TextObject("{=!}" + FeudalTitleDisplayHelper.FormatTitleName(sovereignTitle, claimantClan)));
            encyclopediaText.SetTextVariable("RULER_NAME", claimantClan.Leader?.Name ?? claimantClan.Name ?? new TextObject("?"));
            independentKingdom = CreateIndependentRealmFromTitle(
                parentKingdom,
                claimantClan,
                sovereignTitle,
                "bc_title_indep",
                encyclopediaText);
            if (independentKingdom == null)
            {
                failureReason = "the independent realm could not be created";
                return false;
            }

            HashSet<string> retainedTitleIds = new HashSet<string>(
                GetTitleAndDescendants(sovereignTitle, FeudalHierarchyMode.DeJure)
                    .Where(record => record != null && record.IsActive)
                    .Select(record => record.TitleId),
                StringComparer.Ordinal);
            retainedTitleIds.Add(sovereignTitle.TitleId);

            int relinquishedSettlements = 0;
            int relinquishedUpperTitles = 0;
            if (choice == FeudalSovereignElevationChoice.PeacefulSeparation)
            {
                RelinquishHoldingsOutsideSovereignTitle(
                    claimantClan,
                    parentKingdom,
                    parentRulingClan,
                    retainedTitleIds,
                    out relinquishedSettlements,
                    out relinquishedUpperTitles);
            }

            sovereignTitle.SetDeFactoParentTitle(string.Empty);
            sovereignTitle.SetAssociatedKingdom(independentKingdom.StringId);
            sovereignTitle.MarkSynced(CurrentDay);
            // Clan transfers immediately reconcile every held title. Register the new crown
            // first so that event sees the correct sovereign instead of flattening its tree.
            RegisterIndependentRealmShell(independentKingdom, sovereignTitle, transitionReason);
            MoveGrantedTitleClusterToIndependentRealm(
                parentKingdom,
                independentKingdom,
                claimantClan,
                sovereignTitle,
                requireExclusiveCluster: true,
                serviceReason: transitionReason);
            RebelPolicyHelper.CopyPolicies(parentKingdom, independentKingdom);

            ApplySovereignDepartureRelations(
                claimantClan,
                parentKingdom,
                choice == FeudalSovereignElevationChoice.RetainHoldingsAndRebel ? -40 : -20);

            if (choice == FeudalSovereignElevationChoice.RetainHoldingsAndRebel
                && !independentKingdom.IsAtWarWith(parentKingdom))
            {
                Kingdom newlyIndependentKingdom = independentKingdom;
                ModIntegrationHelper.ExecuteWithAIInfluenceDiplomacyBypass(
                    () => DeclareWarAction.ApplyByDefault(newlyIndependentKingdom, parentKingdom));
            }

            RebuildRuntimeIndexes();
            int politicalRepairs = ReconcileAllDeFactoParents($"{transitionReason} {sovereignTitle.TitleId}");
            RebuildRuntimeIndexes();
            QueueServiceReviewForTitleAndChildren(sovereignTitle, transitionReason);
            Campaign.Current?.GetCampaignBehavior<IdeologyBehavior>()?.RefreshKingdomIdeologies(parentKingdom);
            Campaign.Current?.GetCampaignBehavior<IdeologyBehavior>()?.RefreshKingdomIdeologies(independentKingdom);
            RefreshCachedTitleNames();

            BellumCivileLogger.Log(
                $"Sovereign title acquisition elevated to independent realm; parent={parentKingdom.StringId}; new={independentKingdom.StringId}; claimant={claimantClan.StringId}; title={sovereignTitle.TitleId}; choice={choice}; source={transitionReason}; relinquished_settlements={relinquishedSettlements}; relinquished_upper_titles={relinquishedUpperTitles}; political_repairs={politicalRepairs}.");
            return true;
        }

        public bool TryCompleteDeJureDrift(
            FeudalDeJureDriftRecord drift,
            out FeudalTitleRecord title,
            out FeudalTitleRecord oldParent,
            out FeudalTitleRecord newParent,
            out string failureReason)
        {
            title = null;
            oldParent = null;
            newParent = null;
            failureReason = null;
            EnsureCollectionsInitialized();

            if (drift == null || !drift.IsActive || drift.Progress < 1f)
            {
                failureReason = "drift record is missing, inactive, or incomplete";
                return false;
            }

            title = GetTitle(drift.TitleId);
            oldParent = GetTitle(drift.OriginalParentTitleId);
            newParent = GetTitle(drift.TargetParentTitleId);
            if (title == null || oldParent == null || newParent == null
                || !title.IsActive || !oldParent.IsActive || !newParent.IsActive)
            {
                failureReason = "title or parent record is missing or inactive";
                return false;
            }

            if (!string.Equals(title.ParentTitleId, oldParent.TitleId, StringComparison.Ordinal))
            {
                failureReason = "title no longer belongs to the recorded original parent";
                return false;
            }

            if (newParent.TitleType <= title.TitleType)
            {
                failureReason = "target parent is not a higher-ranked title";
                return false;
            }

            string newParentId = newParent.TitleId;
            if (GetTitleAndDescendants(title).Any(descendant => descendant?.TitleId == newParentId))
            {
                failureReason = "target parent would create a title hierarchy cycle";
                return false;
            }

            string previousParentId = title.ParentTitleId;
            string previousKingdomId = title.AssociatedKingdomId;
            List<FeudalTitleRecord> driftingPackage = GetTitleAndDescendants(title)
                .Where(descendant => descendant != null && descendant.IsActive)
                .ToList();
            // Preserve inherited origin before moving the parent. Each child has its own clock.
            var inheritedOrigins = driftingPackage.Where(child => child.TitleId != drift.TitleId && string.IsNullOrWhiteSpace(child.AssociatedKingdomId))
                .ToDictionary(child => child, child => GetDriftOriginKingdomId(child));
            foreach (var entry in inheritedOrigins)
                if (!string.IsNullOrWhiteSpace(entry.Value)) entry.Key.SetAssociatedKingdom(entry.Value);
            title.SetParentTitle(newParent.TitleId);
            title.SetDeFactoParentTitle(newParent.TitleId);
            title.SetAssociatedKingdom(drift.TargetKingdomId);
            title.MarkSynced(CurrentDay);
            RebuildRuntimeIndexes();
            QueueServiceReviewForTitleAndChildren(title, "de jure drift completed");

            BellumCivileLogger.Log(
                $"De jure drift completed; title={title.TitleId}; old_parent={previousParentId}; new_parent={newParent.TitleId}; old_kingdom={previousKingdomId}; new_kingdom={drift.TargetKingdomId}; independent_descendants={driftingPackage.Count - 1}.");
            return true;
        }

        private string GetDriftOriginKingdomId(FeudalTitleRecord title)
        {
            var visited = new HashSet<string>(StringComparer.Ordinal);
            while (title != null && visited.Add(title.TitleId))
            {
                if (!string.IsNullOrWhiteSpace(title.AssociatedKingdomId)) return title.AssociatedKingdomId;
                title = GetTitle(title.ParentTitleId);
            }
            return string.Empty;
        }

        public void RegisterPartitionHouseClaims(
            Clan parentClan,
            Hero deadLeader,
            IReadOnlyCollection<Tuple<Clan, Hero>> heirBranches,
            IReadOnlyCollection<Town> partitionFiefs,
            IReadOnlyCollection<FeudalTitleRecord> partitionTitles = null)
            => RegisterPartitionHouseClaimsCore(parentClan, deadLeader, heirBranches, partitionFiefs, partitionTitles, null);

        internal void RegisterPartitionHouseClaimsFromSnapshot(
            Clan parentClan, Hero deadLeader, IReadOnlyCollection<Tuple<Clan, Hero>> heirBranches,
            IReadOnlyCollection<Town> partitionFiefs, IReadOnlyCollection<FeudalTitleRecord> partitionTitles,
            IReadOnlyCollection<FeudalClaimRecord> originalClaims)
        {
            if (originalClaims == null) throw new ArgumentNullException(nameof(originalClaims));
            RegisterPartitionHouseClaimsCore(parentClan, deadLeader, heirBranches, partitionFiefs, partitionTitles, originalClaims);
        }

        private void RegisterPartitionHouseClaimsCore(
            Clan parentClan, Hero deadLeader, IReadOnlyCollection<Tuple<Clan, Hero>> heirBranches,
            IReadOnlyCollection<Town> partitionFiefs, IReadOnlyCollection<FeudalTitleRecord> partitionTitles,
            IReadOnlyCollection<FeudalClaimRecord> originalClaims)
        {
            EnsureCollectionsInitialized();
            if (parentClan == null || deadLeader == null || heirBranches == null || partitionFiefs == null)
                return;

            List<Tuple<Clan, Hero>> validBranches = heirBranches
                .Where(branch => branch?.Item1 != null && branch.Item2 != null && !branch.Item1.IsEliminated)
                .GroupBy(branch => branch.Item1.StringId)
                .Select(group => group.First())
                .ToList();

            List<FeudalTitleRecord> titlesForClaims = partitionFiefs
                .Where(fief => fief?.Settlement != null)
                .Select(fief => TryGetBarony(fief.Settlement, out FeudalTitleRecord title) ? title : null)
                .Where(title => title != null && title.IsActive)
                .Concat(partitionTitles ?? new List<FeudalTitleRecord>())
                .Where(title => title != null && title.IsActive)
                .GroupBy(title => title.TitleId)
                .Select(group => group.First())
                .ToList();

            List<FeudalClaimRecord> preExistingParentClaims = (originalClaims ?? _claims)
                .Where(claim => claim != null
                    && IsClaimCurrentlyActive(claim)
                    && claim.ClaimantClanId == parentClan.StringId)
                .ToList();

            int registered = 0;
            BeginClaimIndexBatch();
            try
            {
                foreach (Tuple<Clan, Hero> branch in validBranches)
                {
                    Clan claimantClan = branch.Item1;
                    Hero carrierHero = branch.Item2;
                    foreach (FeudalTitleRecord title in titlesForClaims)
                    {
                        if (title.DeJureHolderClanId == claimantClan.StringId)
                            continue;

                        if (RegisterClaim(
                            claimantClan,
                            title,
                            FeudalClaimStrength.Strong,
                            "partition_bloodright",
                            deadLeader,
                            parentClan,
                            carrierHero: carrierHero,
                            generationDepth: 1) != null)
                        {
                            registered++;
                        }
                    }
                }

                List<Tuple<Clan, Hero>> cadetBranches = validBranches
                    .Where(branch => branch.Item1 != parentClan)
                    .ToList();

                foreach (FeudalClaimRecord inheritedClaim in preExistingParentClaims)
                {
                    FeudalTitleRecord title = GetTitle(inheritedClaim.TargetTitleId);
                    FeudalClaimStrength? inheritedStrength = GetInheritedClaimStrength(inheritedClaim);
                    if (title == null || !inheritedStrength.HasValue)
                        continue;

                    foreach (Tuple<Clan, Hero> branch in cadetBranches)
                    {
                        if (RegisterClaim(
                            branch.Item1,
                            title,
                            inheritedStrength.Value,
                            "partition_inherited_claim",
                            deadLeader,
                            ResolveClan(inheritedClaim.OriginClanId) ?? parentClan,
                            inheritedClaim.ExpiresDay,
                            branch.Item2,
                            inheritedClaim.GenerationDepth + 1) != null)
                        {
                            registered++;
                        }
                    }
                }
            }
            finally
            {
                EndClaimIndexBatch();
            }

            if (registered > 0)
                BellumCivileLogger.Log($"Feudal partition house claims registered; parent={parentClan.StringId}; dead_leader={deadLeader.StringId}; branches={validBranches.Count}; titles={titlesForClaims.Count}; claims={registered}.");
        }

        public void RegisterDynasticCadetTitleClaims(Clan dynastyClan, Clan cadetClan, Hero founderHero, string source)
        {
            EnsureCollectionsInitialized();
            if (dynastyClan == null || cadetClan == null || founderHero == null || dynastyClan == cadetClan)
                return;

            int registered = 0;
            BeginClaimIndexBatch();
            try
            {
                foreach (FeudalTitleRecord title in GetTitlesHeldByClan(dynastyClan, deJure: true))
                {
                    if (title == null || !title.IsActive || title.DeFactoHolderClanId == cadetClan.StringId)
                        continue;

                    if (RegisterClaim(
                        cadetClan,
                        title,
                        FeudalClaimStrength.Strong,
                        source,
                        founderHero,
                        dynastyClan,
                        carrierHero: founderHero,
                        generationDepth: 1) != null)
                    {
                        registered++;
                    }
                }
            }
            finally
            {
                EndClaimIndexBatch();
            }

            if (registered > 0)
                BellumCivileLogger.Log($"Feudal claims inherited by dynastic cadet; dynasty={dynastyClan.StringId}; cadet={cadetClan.StringId}; founder={founderHero.StringId}; claims={registered}; source={source ?? "unknown"}.");
        }

        public void RegisterDynasticDowryParentClaim(Clan dynastyClan, Clan cadetClan, Hero founderHero, Town dowryFief)
        {
            EnsureCollectionsInitialized();
            if (dynastyClan == null
                || cadetClan == null
                || founderHero == null
                || dowryFief?.Settlement == null
                || dynastyClan == cadetClan
                || !TryGetBarony(dowryFief.Settlement, out FeudalTitleRecord title))
            {
                return;
            }

            if (title.DeJureHolderClanId == dynastyClan.StringId)
                return;

            Hero carrierHero = dynastyClan.Leader ?? founderHero;
            FeudalClaimRecord record = RegisterClaim(
                dynastyClan,
                title,
                FeudalClaimStrength.Strong,
                "royal_heiress_dowry_parent_claim",
                founderHero,
                dynastyClan,
                carrierHero: carrierHero,
                generationDepth: 1);

            if (record != null)
            {
                BellumCivileLogger.Log(
                    $"Feudal parent claim retained after royal heiress dowry; dynasty={dynastyClan.StringId}; cadet={cadetClan.StringId}; title={title.TitleId}; carrier={record.CarrierHeroId}.");
            }
        }

        public void RegisterMarriageFounderBirthrightClaims(Clan birthClan, Clan receivingClan, Hero founderHero, string source)
        {
            EnsureCollectionsInitialized();
            if (birthClan == null
                || receivingClan == null
                || founderHero == null
                || birthClan == receivingClan
                || !IsCloseBloodClaimantOfClan(founderHero, birthClan))
            {
                return;
            }

            int registered = 0;
            BeginClaimIndexBatch();
            try
            {
                foreach (FeudalTitleRecord title in GetTitlesHeldByClan(birthClan, deJure: true))
                {
                    if (title == null || !title.IsActive || title.DeFactoHolderClanId == receivingClan.StringId)
                        continue;

                    if (RegisterClaim(
                        receivingClan,
                        title,
                        FeudalClaimStrength.Strong,
                        source,
                        founderHero,
                        birthClan,
                        carrierHero: founderHero,
                        generationDepth: 1) != null)
                    {
                        registered++;
                    }
                }
            }
            finally
            {
                EndClaimIndexBatch();
            }

            if (registered > 0)
            {
                BellumCivileLogger.Log(
                    $"Feudal birthright claims carried through marriage; birth_clan={birthClan.StringId}; receiving_clan={receivingClan.StringId}; carrier={founderHero.StringId}; claims={registered}; source={source ?? "unknown"}.");
            }
        }

        public string BuildSummary()
        {
            EnsureCollectionsInitialized();

            IEnumerable<FeudalTitleRecord> activeTitles = _titlesById.Values.Where(title => title != null && title.IsActive);
            string titleCounts = string.Join(", ",
                activeTitles
                    .GroupBy(title => title.TitleType)
                    .OrderBy(group => group.Key)
                    .Select(group => $"{group.Key.ToString().ToLowerInvariant()}={group.Count()}"));

            int splitBaronies = activeTitles.Count(title =>
                title.TitleType == FeudalTitleType.Barony
                && !string.IsNullOrWhiteSpace(title.DeJureHolderClanId)
                && !string.IsNullOrWhiteSpace(title.DeFactoHolderClanId)
                && title.DeJureHolderClanId != title.DeFactoHolderClanId);

            int hierarchyDivergences = activeTitles.Count(title =>
                !string.Equals(title.ParentTitleId, title.DeFactoParentTitleId, StringComparison.Ordinal));

            return $"titles: total={activeTitles.Count()} ({(string.IsNullOrWhiteSpace(titleCounts) ? "none" : titleCounts)}); claims={_claims.Count(claim => claim != null && claim.IsActive)}; de_jure_de_facto_split_baronies={splitBaronies}; hierarchy_schema={_hierarchySchemaVersion}; hierarchy_divergences={hierarchyDivergences}";
        }

        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            Instance = this;
            EnsureCollectionsInitialized();
            RecoverRestoredRealmTitleStyles();
            bool initializeConfiguredHierarchy = !_titlesById.Values.Any(title => title != null && title.IsActive);
            MigrateDeFactoHierarchyIfNeeded();
            int created = BackfillAndRepairTitles("session launched", initializeConfiguredHierarchy, out bool registryChanged);
            int destroyedRealmRepairs = Kingdom.All
                .Where(kingdom => kingdom != null && kingdom.IsEliminated)
                .Sum(kingdom => VacateDestroyedRealmSovereignTitles(kingdom, "session launched repair"));
            registryChanged |= destroyedRealmRepairs > 0;
            int politicalRepairs = ReconcileAllDeFactoParents("session launched");
            RememberCurrentClanLeaders();
            if (registryChanged || politicalRepairs > 0)
                RebuildRuntimeIndexes();
            _registryRepairRequested = false;
            _nextRegistryIntegrityAuditDay = CurrentDay + RegistryIntegrityAuditIntervalDays;
            ReconcileCurrentRealmTitles();
            RefreshCachedTitleNames();
            DynamicKingdomTitleNameHelper.CaptureNativeNamesAfterLoad();
            _sessionLaunched = true;
            BellumCivileLogger.Log($"Feudal title registry initialized; created={created}; destroyed_realm_repairs={destroyedRealmRepairs}; political_repairs={politicalRepairs}; {BuildSummary()}.");
        }

        private void OnHourlyTick()
        {
            if (!_titleDisplayRefreshPending && _lastTitleDisplayRefreshRevision == DisplayRevision)
                return;

            ReconcileCurrentRealmTitles();
            RefreshCachedTitleNames();
        }

        private void OnRulingClanChanged(Kingdom kingdom, Clan newRulingClan)
        {
            // The actual title transfer may be completed by the succession decision's
            // postfix, so defer this event-only request until the next campaign hour.
            _titleDisplayRefreshPending = true;
            if (IsValidPermanentKingdom(kingdom) && !HasRegisteredKingdomTitle(kingdom))
                RequestRegistryRepair(invalidateReferenceLookups: true);
        }

        private void RefreshCachedTitleNames()
        {
            if (Campaign.Current == null)
                return;

            BellumCivile.Patches.FeudalTitleHeroNamePatch.InvalidateCache();

            HashSet<Army> armies = new HashSet<Army>();
            int clearedParties = 0;
            int refreshedArmies = 0;
            int failures = 0;

            foreach (MobileParty party in MobileParty.All.ToList())
            {
                if (party == null)
                    continue;

                try
                {
                    if (party.IsLordParty && party.PartyComponent != null)
                    {
                        party.PartyComponent.ClearCachedName();
                        clearedParties++;
                    }

                    if (party.Army != null)
                        armies.Add(party.Army);
                }
                catch
                {
                    failures++;
                }
            }

            foreach (Army army in armies)
            {
                try
                {
                    army?.UpdateName();
                    refreshedArmies++;
                }
                catch
                {
                    failures++;
                }
            }

            _lastTitleDisplayRefreshRevision = DisplayRevision;
            _titleDisplayRefreshPending = false;

            if (failures > 0)
            {
                BellumCivileLogger.Log(
                    $"Feudal title display refresh completed with failures; revision={RuntimeRevision}; parties={clearedParties}; armies={refreshedArmies}; failures={failures}.");
            }
        }

        private void OnKingdomDestroyed(Kingdom kingdom)
        {
            VacateDestroyedRealmSovereignTitles(kingdom, "kingdom destroyed");
        }

        private int VacateDestroyedRealmSovereignTitles(Kingdom kingdom, string reason)
        {
            EnsureCollectionsInitialized();
            if (kingdom == null
                || BellumKingdomVisibilityHelper.IsTemporaryBellumKingdom(kingdom)
                || BellumKingdomVisibilityHelper.CountStrongholds(kingdom) > 0)
            {
                return 0;
            }

            bool alreadyRemembered = _historicalRealmSovereignTitleByKingdomId.TryGetValue(
                kingdom.StringId,
                out string rememberedTitleId);
            FeudalTitleRecord title = GetHistoricalRealmSovereignTitle(kingdom);
            if (title == null || !title.IsActive)
            {
                return 0;
            }

            _historicalRealmSovereignTitleByKingdomId[kingdom.StringId] = title.TitleId;
            if (IsHistoricalRealmRepresentedByActiveSuccessor(kingdom, title))
                return 0;

            // Destruction repair is intentionally one-shot. Once the historical crown has been
            // remembered, a later de facto holder may be a legitimate usurper rather than stale
            // state left behind by the eliminated kingdom.
            if (alreadyRemembered
                && string.Equals(rememberedTitleId, title.TitleId, StringComparison.Ordinal))
            {
                return 0;
            }

            int changed = 0;
            string historicalAssociatedKingdomId = title.AssociatedKingdomId;
            HashSet<string> visited = new HashSet<string>();
            FeudalTitleRecord current = title;
            while (current != null
                && current.IsActive
                && visited.Add(current.TitleId)
                && string.Equals(current.AssociatedKingdomId, historicalAssociatedKingdomId, StringComparison.Ordinal))
            {
                if (!string.IsNullOrWhiteSpace(current.DeFactoHolderClanId))
                {
                    Clan possessor = ResolveClan(current.DeFactoHolderClanId);
                    if (!DestroyedRealmTitleSafety.CanVacate(possessor, kingdom))
                    {
                        BellumCivileLogger.Log($"Preserved title during destroyed realm cleanup; kingdom={kingdom.StringId}; title={current.TitleId}; holder={current.DeFactoHolderClanId}; holder_realm={possessor?.Kingdom?.StringId ?? "none"}.");
                        current = GetTitle(current.ParentTitleId);
                        continue;
                    }
                    current.SetDeFactoHolder(string.Empty);
                    current.SetDeFactoParentTitle(string.Empty);
                    current.MarkSynced(CurrentDay);
                    changed++;
                }

                current = GetTitle(current.ParentTitleId);
            }

            if (changed <= 0)
                return 0;

            RebuildRuntimeIndexes();
            QueueServiceReviewForTitleAndChildren(title, reason);
            BellumCivileLogger.Log(
                $"Vacated destroyed realm sovereign title while preserving its de jure dynasty; kingdom={kingdom.StringId}; title={title.TitleId}; changed={changed}; reason={reason ?? "unknown"}.");
            return changed;
        }

        private void OnWeeklyTick()
        {
            EnsureCollectionsInitialized();
            bool runIntegrityAudit = _registryRepairRequested || CurrentDay >= _nextRegistryIntegrityAuditDay;
            int created = 0;
            bool registryChanged = false;
            if (runIntegrityAudit)
            {
                string repairReason = _registryRepairRequested ? "structural registry repair" : "periodic registry integrity audit";
                created = BackfillAndRepairTitles(repairReason, initializeConfiguredHierarchy: false, out registryChanged);
                _registryRepairRequested = false;
                _nextRegistryIntegrityAuditDay = CurrentDay + RegistryIntegrityAuditIntervalDays;
            }

            int destroyedRealmRepairs = Kingdom.All
                .Where(kingdom => kingdom != null && kingdom.IsEliminated)
                .Sum(kingdom => VacateDestroyedRealmSovereignTitles(kingdom, "weekly destroyed realm repair"));
            registryChanged |= destroyedRealmRepairs > 0;
            int orphanedTitlesRemoved = CleanupOrphanedUpperTitles();
            registryChanged |= orphanedTitlesRemoved > 0;
            int politicalRepairs = registryChanged ? ReconcileAllDeFactoParents("weekly repair") : 0;
            RememberCurrentClanLeaders();
            if (registryChanged || politicalRepairs > 0)
            {
                RebuildRuntimeIndexes();
                BellumCivileLogger.Log($"Feudal title registry repaired missing records; audited={runIntegrityAudit}; created={created}; destroyed_realm_repairs={destroyedRealmRepairs}; orphaned_removed={orphanedTitlesRemoved}; political_repairs={politicalRepairs}; {BuildSummary()}.");
            }
        }

        private int CleanupOrphanedUpperTitles()
        {
            const float graceDays = 7f;
            HashSet<string> configuredTitleIds = GetConfiguredTitleIds();
            List<FeudalTitleRecord> orphaned = _titlesById.Values
                .Where(title => title != null
                    && title.IsActive
                    && title.TitleType > FeudalTitleType.Barony
                    && !configuredTitleIds.Contains(title.TitleId)
                    && !IsSovereignTitle(title, ResolveClan(title.DeFactoHolderClanId)?.Kingdom)
                    && !HasActiveTitleDispute(title)
                    && !_titlesById.Values.Any(child => child != null
                        && child.IsActive
                        && (child.ParentTitleId == title.TitleId || child.DeFactoParentTitleId == title.TitleId)))
                .ToList();

            HashSet<string> currentIds = new HashSet<string>(orphaned.Select(title => title.TitleId));
            foreach (string staleId in _orphanedUpperTitleFirstSeenDay.Keys.Where(id => !currentIds.Contains(id)).ToList())
                _orphanedUpperTitleFirstSeenDay.Remove(staleId);

            int removed = 0;
            foreach (FeudalTitleRecord title in orphaned)
            {
                if (!_orphanedUpperTitleFirstSeenDay.TryGetValue(title.TitleId, out float firstSeenDay))
                {
                    _orphanedUpperTitleFirstSeenDay[title.TitleId] = CurrentDay;
                    continue;
                }

                if (CurrentDay - firstSeenDay < graceDays || CurrentDay - title.CreatedDay < graceDays)
                    continue;

                if (DissolveTitleInternal(title, deliberatelyDissolved: false, "weekly orphan cleanup", out _))
                {
                    _orphanedUpperTitleFirstSeenDay.Remove(title.TitleId);
                    removed++;
                }
            }

            return removed;
        }

        private HashSet<string> GetConfiguredTitleIds()
        {
            if (_configuredTitleIdsCacheReady)
                return _configuredTitleIdsCache;

            _configuredTitleIdsCache.Clear();
            IReadOnlyList<FeudalTitleConfig.ConfiguredTitle> configuredTitles = FeudalTitleConfig.Instance.Titles;
            if (configuredTitles == null)
            {
                _configuredTitleIdsCacheReady = true;
                return _configuredTitleIdsCache;
            }

            foreach (FeudalTitleConfig.ConfiguredTitle configured in configuredTitles)
            {
                if (configured == null || string.IsNullOrWhiteSpace(configured.Id))
                    continue;

                Settlement settlement = ResolveSettlementReference(configured.SettlementRef);
                Clan deJureClan = ResolveClanReference(configured.DeJureClanRef);
                Clan deFactoClan = ResolveClanReference(configured.DeFactoClanRef) ?? deJureClan;
                Kingdom kingdom = ResolveKingdomReference(configured.KingdomRef)
                    ?? deFactoClan?.Kingdom
                    ?? deJureClan?.Kingdom;
                string resolvedId = ResolveConfiguredTitleId(configured, settlement, kingdom);
                if (!string.IsNullOrWhiteSpace(resolvedId))
                    _configuredTitleIdsCache.Add(resolvedId);
            }

            _configuredTitleIdsCacheReady = true;
            return _configuredTitleIdsCache;
        }

        private void OnHeroKilled(Hero victim, Hero killer, KillCharacterAction.KillCharacterActionDetail detail, bool showNotification)
        {
            if (victim == null || string.IsNullOrWhiteSpace(victim.StringId))
                return;

            EnsureCollectionsInitialized();
            if (RegencyBehavior.Instance?.IsGeneratedRegentDeathInProgress(victim) == true
                && victim.Clan?.Leader is Hero successorRegent
                && successorRegent != victim
                && successorRegent.IsAlive)
            {
                ReassignClaimsForRegencyTransition(victim, successorRegent, "regent death");
                RememberCurrentClanLeaders();
                return;
            }

            List<Hero> heirs = GetEligibleClaimHeirs(victim);
            int inheritedExplicitClaims = ExpireClaimsCarriedBy(victim, heirs, out int expiredClaims);

            if (expiredClaims > 0)
            {
                BellumCivileLogger.Log(
                    $"Feudal claims resolved on carrier death; hero={victim.StringId}; expired={expiredClaims}; inherited={inheritedExplicitClaims}; heirs={string.Join(",", heirs.Select(h => h.StringId))}.");
            }

            RememberCurrentClanLeaders();
        }

        public bool TransferInheritedPossession(Clan source, Clan recipient, Settlement holding)
        {
            if (source == null || recipient?.Leader == null || holding?.OwnerClan != source
                || !TryGetBarony(holding, out FeudalTitleRecord title)) return false;
            string previousContext = _deFactoOnlySettlementTransferTitleId;
            try
            {
                if (title.DeJureHolderClanId != source.StringId)
                    _deFactoOnlySettlementTransferTitleId = title.TitleId;
                ChangeOwnerOfSettlementAction.ApplyByDefault(recipient.Leader, holding);
                return holding.OwnerClan == recipient;
            }
            finally { _deFactoOnlySettlementTransferTitleId = previousContext; }
        }

        internal bool TransferChallengePossession(Clan source, Clan recipient, Settlement holding)
        {
            if (source == null || recipient?.Leader?.IsAlive != true || holding?.OwnerClan != source
                || holding.SiegeEvent != null || holding.Party?.MapEvent != null
                || !TryGetBarony(holding, out FeudalTitleRecord title)) return false;
            string legalOwner = title.DeJureHolderClanId;
            string legalParent = title.ParentTitleId;
            string previousContext = _deFactoOnlySettlementTransferTitleId;
            try
            {
                // Seizure and restitution change possession, never legal inheritance.
                _deFactoOnlySettlementTransferTitleId = title.TitleId;
                ChangeOwnerOfSettlementAction.ApplyByDefault(recipient.Leader, holding);
                return holding.OwnerClan == recipient && title.DeJureHolderClanId == legalOwner
                    && title.ParentTitleId == legalParent;
            }
            finally { _deFactoOnlySettlementTransferTitleId = previousContext; }
        }

        public int MoveCrownHeirClaims(Hero heir, Clan previousClan, Clan receivingClan)
        {
            if (heir == null || previousClan == null || receivingClan == null
                || heir.Clan != receivingClan || previousClan == receivingClan)
                return 0;
            EnsureCollectionsInitialized();
            int moved = 0;
            foreach (FeudalClaimRecord claim in _claims.Where(IsClaimCurrentlyActive))
                if (claim.MoveWithCarrier(heir.StringId, previousClan.StringId, receivingClan.StringId))
                    moved++;
            if (moved > 0)
            {
                RequestClaimIndexRebuild();
                BellumCivileLogger.Log($"Crown heir claims followed their carrier; hero={heir.StringId}; from={previousClan.StringId}; to={receivingClan.StringId}; claims={moved}.");
            }
            return moved;
        }

        public int ReassignClaimsForRegencyTransition(Hero formerRegent, Hero successor, string reason)
        {
            if (formerRegent == null
                || successor == null
                || formerRegent == successor
                || string.IsNullOrWhiteSpace(formerRegent.StringId)
                || formerRegent.Clan == null
                || successor.Clan != formerRegent.Clan)
            {
                return 0;
            }

            EnsureCollectionsInitialized();
            int reassigned = 0;
            foreach (FeudalClaimRecord claim in _claims.Where(claim => claim != null
                && claim.IsActive
                && claim.CarrierHeroId == formerRegent.StringId))
            {
                claim.ReplaceCarrier(successor);
                reassigned++;
            }

            if (reassigned > 0)
            {
                RequestClaimIndexRebuild();
                BellumCivileLogger.Log(
                    $"Preserved clan claims across regency transition; clan={formerRegent.Clan.StringId}; old_regent={formerRegent.StringId}; successor={successor.StringId}; claims={reassigned}; reason={reason ?? "unknown"}.");
            }

            return reassigned;
        }

        private void OnBeforeHeroesMarried(Hero firstHero, Hero secondHero, bool showNotification)
        {
            if (firstHero?.Clan == null
                || secondHero?.Clan == null
                || Campaign.Current?.Models?.MarriageModel == null)
            {
                return;
            }

            Clan receivingClan = Campaign.Current.Models.MarriageModel.GetClanAfterMarriage(firstHero, secondHero);
            if (receivingClan == null || receivingClan.IsEliminated)
                return;

            RegisterMarriageBirthrightsForDepartingHero(firstHero, receivingClan);
            RegisterMarriageBirthrightsForDepartingHero(secondHero, receivingClan);
        }

        private void RegisterMarriageBirthrightsForDepartingHero(Hero hero, Clan receivingClan)
        {
            Clan birthClan = hero?.Clan;
            if (birthClan == null || receivingClan == null || birthClan == receivingClan)
                return;

            RegisterMarriageFounderBirthrightClaims(
                birthClan,
                receivingClan,
                hero,
                "marriage_birthright");
        }


        private int ExpireClaimsCarriedBy(Hero carrier, List<Hero> heirs, out int expiredCount)
        {
            expiredCount = 0;
            if (carrier == null || heirs == null)
                return 0;

            int inheritedCount = 0;
            BeginClaimIndexBatch();
            try
            {
                foreach (FeudalClaimRecord claim in _claims.ToList())
                {
                    if (claim == null || !IsClaimCurrentlyActive(claim) || !IsClaimCarriedBy(claim, carrier))
                        continue;

                    claim.SetActive(false);
                    RequestClaimIndexRebuild();
                    expiredCount++;
                    FeudalTitleRecord title = GetTitle(claim.TargetTitleId);
                    if (title == null)
                        continue;

                    int nextDepth = claim.GenerationDepth + 1;
                    FeudalClaimStrength? inheritedStrength = GetInheritedClaimStrength(claim);
                    if (!inheritedStrength.HasValue)
                        continue;

                    foreach (Hero heir in heirs)
                    {
                        Clan heirClan = heir?.Clan;
                        if (heirClan == null || heirClan.IsEliminated || title.DeFactoHolderClanId == heirClan.StringId)
                            continue;

                        if (RegisterClaim(
                            heirClan,
                            title,
                            inheritedStrength.Value,
                            "inherited_blood_claim",
                            carrier,
                            ResolveClan(claim.OriginClanId) ?? ResolveClan(claim.ClaimantClanId),
                            claim.ExpiresDay,
                            heir,
                            nextDepth) != null)
                        {
                            inheritedCount++;
                        }
                    }
                }
            }
            finally
            {
                EndClaimIndexBatch();
            }

            return inheritedCount;
        }

        private void OnSettlementOwnerChanged(
            Settlement settlement,
            bool openToClaim,
            Hero newOwner,
            Hero oldOwner,
            Hero capturerHero,
            ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail detail)
        {
            if (_clientLandGrantInProgress == settlement) return;
            if (TryApplyJournaledCrownEstateOwnership(settlement, newOwner?.Clan)) return;
            if (!IsLandedSettlement(settlement))
                return;

            EnsureCollectionsInitialized();
            bool isBarter = detail == ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail.ByBarter;
            _titlesById.TryGetValue(BuildBaronyTitleId(settlement), out FeudalTitleRecord titleBeforeTransfer);
            string previousDeJure = titleBeforeTransfer?.DeJureHolderClanId;
            string previousDeFacto = oldOwner?.Clan?.StringId ?? titleBeforeTransfer?.DeFactoHolderClanId;
            FeudalTitleRecord title = EnsureBaronyTitle(settlement, "settlement owner changed");
            if (title == null)
                return;

            // EnsureBaronyTitle synchronizes possession and fills missing legal ownership.
            // Every transfer must use the rights that existed before that synchronization.
            if (titleBeforeTransfer != null)
                title.SetDeJureHolder(previousDeJure);
            else
            {
                // A newly reconstructed record must not mistake the incoming custodian for the old owner.
                previousDeJure = oldOwner?.Clan?.StringId ?? title.DeJureHolderClanId;
                title.SetDeJureHolder(previousDeJure);
            }
            Clan newClan = newOwner?.Clan ?? settlement.OwnerClan;
            string newClanId = newClan?.StringId ?? string.Empty;
            bool wasAllocationCustody = ConsumeAllocationCustody(title.TitleId, previousDeFacto, newClanId, detail);
            Clan sellerClan = isBarter ? oldOwner?.Clan ?? ResolveClan(previousDeFacto) : null;

            if (TryApplySubinfeudationOwnershipChange(
                settlement,
                title,
                previousDeJure,
                previousDeFacto,
                newClan,
                oldOwner?.Clan,
                detail))
            {
                return;
            }

            title.SetDeFactoHolder(newClanId);
            title.MarkSynced(CurrentDay);

            bool legalTransfer = isBarter
                ? ShouldBarterTransferLegalTitle(previousDeJure, sellerClan, newClan)
                : IsValidPermanentKingdom(newClan?.Kingdom)
                    && ShouldLegalTitleTransferToHolder(title, newClan, previousDeJure, previousDeFacto, detail, openToClaim);
            if (legalTransfer)
            {
                title.SetDeJureHolder(newClanId);
                if (!IsAllocationCustodian(title.TitleId, newClanId))
                    RemoveRedundantClaimsForHolder(newClan, title);
                if (!isBarter)
                {
                    UpdateBaronyParentToCurrentKingdom(title, newClan);
                    if (!wasAllocationCustody || previousDeJure != previousDeFacto)
                        RegisterDisplacedLegalOwnerClaimIfNeeded(title, previousDeJure, newClan, detail);
                    else
                        BellumCivileLogger.Log($"Feudal allocation completed without custodian claim; title={title.TitleId}; custodian={previousDeFacto}; recipient={newClanId}; detail={detail}.");
                }
            }

            if (isBarter && newClan != null && sellerClan != null && sellerClan != newClan)
                DeactivateClaimsForTitle(sellerClan, title);

            bool politicalParentChanged = false;
            if (IsValidPermanentKingdom(newClan?.Kingdom))
                politicalParentChanged = ReconcileDeFactoParent(title, newClan.Kingdom);

            RebuildRuntimeIndexes();
            QueueServiceReview(title, "settlement owner changed");
            if (isBarter)
            {
                Campaign.Current?.GetCampaignBehavior<FeudalDeJureDriftBehavior>()?.RefreshTitleFamily(title);
                BellumCivileLogger.Log($"Feudal settlement barter; seller={sellerClan?.StringId ?? "unknown"}; buyer={newClanId}; title={title.TitleId}; legal_transfer={legalTransfer}; seller_claims_relinquished={sellerClan != null && newClan != null && sellerClan != newClan}.");
                // ByBarter also covers direct API calls; capture the caller before assuming an AI sale.
                if (sellerClan?.StringId?.StartsWith("bc_challenge_", StringComparison.Ordinal) == true)
                    BellumCivileLogger.Log($"Challenge cadet barter trace; settlement={settlement.StringId}; seller={sellerClan.StringId}; buyer={newClanId}; remaining_fiefs={sellerClan.Fiefs.Count}; caller={new System.Diagnostics.StackTrace(false)}");
            }
            BellumCivileLogger.Log(
                $"Feudal barony synced after ownership change; settlement={settlement.StringId}; detail={detail}; legal_transfer={legalTransfer}; political_parent_changed={politicalParentChanged}; de_jure={previousDeJure}->{title.DeJureHolderClanId}; de_facto={previousDeFacto}->{title.DeFactoHolderClanId}; de_facto_parent={title.DeFactoParentTitleId}.");
        }

        private bool TryApplySubinfeudationOwnershipChange(
            Settlement settlement,
            FeudalTitleRecord title,
            string previousDeJure,
            string previousDeFacto,
            Clan newClan,
            Clan oldClan,
            ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail detail)
        {
            SubinfeudationGrantContext grant = _activeSubinfeudationGrant;
            if (grant == null
                || !grant.Matches(settlement, title, oldClan, previousDeJure, previousDeFacto, detail)
                || newClan == null
                || newClan.IsEliminated
                || newClan.Kingdom != grant.GrantorClan.Kingdom)
            {
                return false;
            }

            title.SetDeFactoHolder(newClan.StringId);
            title.SetDeJureHolder(grant.TransferDeJure ? newClan.StringId : grant.OriginalDeJureHolderClanId);
            title.SetDeFactoParentTitle(grant.LiegeTitleId);
            title.SetAssociatedKingdom(newClan.Kingdom?.StringId ?? title.AssociatedKingdomId);
            title.MarkSynced(CurrentDay);
            _subinfeudationParentByTitleId[title.TitleId] = grant.LiegeTitleId;

            if (grant.TransferDeJure)
            {
                DeactivateClaimsForTitle(grant.GrantorClan, title);
                RemoveRedundantClaimsForHolder(newClan, title);
            }

            grant.MarkConsumed(newClan);
            RebuildRuntimeIndexes();
            QueueServiceReviewForTitleAndChildren(title, "companion subinfeudation");
            Campaign.Current?.GetCampaignBehavior<FeudalDeJureDriftBehavior>()?.RefreshTitleFamily(title);

            BellumCivileLogger.Log(
                $"Companion subinfeudation title transfer applied; grantor={grant.GrantorClan.StringId}; recipient={newClan.StringId}; settlement={settlement.StringId}; title={title.TitleId}; liege_title={grant.LiegeTitleId}; full_rights={grant.TransferDeJure}; de_jure={previousDeJure}->{title.DeJureHolderClanId}; de_facto={previousDeFacto}->{title.DeFactoHolderClanId}.");
            return true;
        }

        private void OnClanChangedKingdom(
            Clan clan,
            Kingdom oldKingdom,
            Kingdom newKingdom,
            ChangeKingdomAction.ChangeKingdomActionDetail detail,
            bool showNotification)
        {
            if (clan == null || string.IsNullOrWhiteSpace(clan.StringId))
                return;

            // Civil-war kingdoms are temporary transport shells, not new feudal allegiances.
            if (IsTemporaryRebelKingdom(oldKingdom) || IsTemporaryRebelKingdom(newKingdom))
                return;

            if (!IsValidPermanentKingdom(newKingdom))
                return;

            if (!HasRegisteredKingdomTitle(newKingdom))
                RequestRegistryRepair(invalidateReferenceLookups: true);

            EnsureCollectionsInitialized();
            List<FeudalTitleRecord> heldTitles = GetTitlesHeldByClan(clan, deJure: false)
                .Where(title => title != null && title.IsActive)
                .OrderByDescending(title => title.TitleType)
                .ThenBy(title => title.TitleId)
                .ToList();
            if (heldTitles.Count == 0)
                return;

            int changed = 0;
            foreach (FeudalTitleRecord title in heldTitles)
            {
                if (ReconcileDeFactoParent(title, newKingdom))
                    changed++;
            }

            if (changed <= 0)
                return;

            RebuildRuntimeIndexes();
            foreach (FeudalTitleRecord title in heldTitles)
                QueueServiceReviewForTitleAndChildren(title, "clan allegiance changed");
            BellumCivileLogger.Log(
                $"Reconciled de facto hierarchy after permanent clan allegiance change; clan={clan.StringId}; old_kingdom={oldKingdom?.StringId ?? "none"}; new_kingdom={newKingdom.StringId}; detail={detail}; titles={heldTitles.Count}; changed={changed}.");
        }

        private void OnClanCreated(Clan clan, bool isCompanion)
        {
            if (isCompanion)
                OnCompanionClanCreated(clan);
        }

        private void OnCompanionClanCreated(Clan clan)
        {
            _referenceLookupCachesReady = false;
            _configuredTitleIdsCacheReady = false;
            if (clan == null
                || clan.Kingdom == null
                || clan.Kingdom.RulingClan != Clan.PlayerClan
                || clan.Leader == null
                || clan.Fiefs == null
                || clan.Fiefs.Count == 0)
            {
                return;
            }

            EnsureCollectionsInitialized();
            string companionClanId = clan.StringId;
            string royalClanId = Clan.PlayerClan?.StringId;
            if (string.IsNullOrWhiteSpace(companionClanId) || string.IsNullOrWhiteSpace(royalClanId))
                return;

            int synced = 0;
            int legalized = 0;
            foreach (Town fief in clan.Fiefs.ToList())
            {
                Settlement settlement = fief?.Settlement;
                if (!IsLandedSettlement(settlement))
                    continue;

                FeudalTitleRecord title = EnsureBaronyTitle(settlement, "companion royal grant");
                if (title == null)
                    continue;

                string oldDeJure = title.DeJureHolderClanId;
                string oldDeFacto = title.DeFactoHolderClanId;

                if (!string.Equals(title.DeFactoHolderClanId, companionClanId, StringComparison.Ordinal))
                {
                    title.SetDeFactoHolder(companionClanId);
                    synced++;
                }

                bool wasLegalRoyalGrant = string.Equals(title.DeJureHolderClanId, royalClanId, StringComparison.Ordinal);
                bool alreadyLegalizedByGift = string.Equals(title.DeJureHolderClanId, companionClanId, StringComparison.Ordinal);
                if (!wasLegalRoyalGrant && !alreadyLegalizedByGift)
                {
                    title.MarkSynced(CurrentDay);
                    QueueServiceReview(title, "companion royal grant de facto sync");
                    continue;
                }

                if (wasLegalRoyalGrant)
                    title.SetDeJureHolder(companionClanId);
                title.SetDeFactoHolder(companionClanId);
                title.MarkSynced(CurrentDay);
                RemoveRedundantClaimsForHolder(clan, title);
                DeactivateClaimsForTitle(Clan.PlayerClan, title);
                QueueServiceReview(title, "companion royal grant legalized");
                legalized++;

                BellumCivileLogger.Log(
                    $"Legalized companion royal grant title; clan={companionClanId}; settlement={settlement.StringId}; title={title.TitleId}; de_jure={oldDeJure}->{title.DeJureHolderClanId}; de_facto={oldDeFacto}->{title.DeFactoHolderClanId}.");
            }

            if (legalized <= 0 && synced <= 0)
                return;

            RebuildRuntimeIndexes();
            int politicalRepairs = ReconcileAllDeFactoParents($"companion royal grant {companionClanId}");
            if (politicalRepairs > 0)
                RebuildRuntimeIndexes();

            BellumCivileLogger.Log(
                $"Processed companion royal grant titles; clan={companionClanId}; synced={synced}; legalized={legalized}; political_repairs={politicalRepairs}.");
        }

        private int ReconcileAllDeFactoParents(string reason)
        {
            EnsureCollectionsInitialized();
            int changed = 0;
            List<FeudalTitleRecord> titles = _titlesById.Values
                .Where(title => title != null && title.IsActive)
                .OrderByDescending(title => title.TitleType)
                .ThenBy(title => title.TitleId)
                .ToList();

            foreach (FeudalTitleRecord title in titles)
            {
                Clan holder = ResolveClan(title.DeFactoHolderClanId);
                Kingdom holderKingdom = holder?.Kingdom;
                if (!IsValidPermanentKingdom(holderKingdom) || IsTemporaryRebelKingdom(holderKingdom))
                    continue;

                if (ReconcileDeFactoParent(title, holderKingdom))
                    changed++;
            }

            if (changed > 0)
                BellumCivileLogger.Log($"Reconciled de facto title hierarchy; reason={reason ?? "unknown"}; changed={changed}.");

            return changed;
        }

        private int BackfillAndRepairTitles(string reason, bool initializeConfiguredHierarchy, out bool changed)
        {
            int beforeCount = _titlesById.Count;
            changed = false;
            RebuildReferenceLookupCaches();

            foreach (Kingdom kingdom in Kingdom.All.Where(IsValidPermanentKingdom))
            {
                if (IsIndependentRealmShell(kingdom))
                    continue;

                EnsureKingdomTitle(kingdom, reason, out bool kingdomChanged);
                changed |= kingdomChanged;
            }

            foreach (Settlement settlement in Settlement.All.Where(IsLandedSettlement))
            {
                EnsureBaronyTitle(settlement, reason, out bool baronyChanged);
                changed |= baronyChanged;
            }

            changed |= ApplyConfiguredTitles(reason, initializeConfiguredHierarchy) > 0;

            // Newly created records must be addressable by settlement/title aliases while the
            // remainder of this repair batch runs, but dependent caches should see one revision.
            if (changed)
                RebuildRuntimeIndexes(advanceRevision: false);

            changed |= ApplyConfiguredTitleNames(reason) > 0;
            changed |= DeactivateTemporaryFeudKingdomTitles(reason) > 0;
            if (changed)
                RebuildRuntimeIndexes(advanceRevision: false);

            return Math.Max(0, _titlesById.Count - beforeCount);
        }

        private int DeactivateTemporaryFeudKingdomTitles(string reason)
        {
            EnsureCollectionsInitialized();
            int deactivated = 0;
            foreach (FeudalTitleRecord title in _titlesById.Values.ToList())
            {
                if (title == null || !title.IsActive || title.TitleType != FeudalTitleType.Kingdom)
                    continue;

                bool feudKingdomTitle = title.TitleId.StartsWith("bc_title_kingdom_bc_feud_", StringComparison.OrdinalIgnoreCase)
                    || title.AssociatedKingdomId.StartsWith("bc_feud_", StringComparison.OrdinalIgnoreCase);
                if (!feudKingdomTitle)
                    continue;

                title.SetActive(false);
                title.SetDeFactoHolder(string.Empty);
                title.SetDeJureHolder(string.Empty);
                title.SetParentTitle(string.Empty);
                title.SetDeFactoParentTitle(string.Empty);
                title.MarkSynced(CurrentDay);
                deactivated++;
            }

            foreach (string kingdomId in _independentRealmSourceTitleByKingdomId.Keys
                .Where(id => !string.IsNullOrWhiteSpace(id) && id.StartsWith("bc_feud_", StringComparison.OrdinalIgnoreCase))
                .ToList())
            {
                if (_independentRealmSourceTitleByKingdomId.Remove(kingdomId))
                    deactivated++;
            }

            if (deactivated > 0)
                BellumCivileLogger.Log($"Deactivated temporary feud kingdom titles; count={deactivated}; reason={reason ?? "unknown"}.");

            return deactivated;
        }

        private int ApplyConfiguredTitles(string reason, bool initializeConfiguredHierarchy)
        {
            IReadOnlyList<FeudalTitleConfig.ConfiguredTitle> configuredTitles = FeudalTitleConfig.Instance.Titles;
            if (configuredTitles == null || configuredTitles.Count == 0)
                return 0;

            int changed = 0;
            foreach (FeudalTitleConfig.ConfiguredTitle configured in configuredTitles)
            {
                if (TryApplyConfiguredTitle(configured, reason, initializeConfiguredHierarchy))
                    changed++;
            }

            if (initializeConfiguredHierarchy)
            {
                if (changed > 0)
                    RebuildRuntimeIndexes(advanceRevision: false);

                foreach (FeudalTitleConfig.ConfiguredTitle configured in configuredTitles)
                    changed += ApplyConfiguredTitleChildren(configured);
            }

            if (changed > 0)
                BellumCivileLogger.Log($"Applied feudal title config changes; changes={changed}; reason={reason ?? "unknown"}.");

            return changed;
        }

        private int ApplyConfiguredTitleNames(string reason)
        {
            IReadOnlyDictionary<string, string> configuredNames = FeudalTitleConfig.Instance.TitleNames;
            if (configuredNames == null || configuredNames.Count == 0)
                return 0;

            int applied = 0;
            foreach (KeyValuePair<string, string> configuredName in configuredNames)
            {
                if (string.IsNullOrWhiteSpace(configuredName.Key) || string.IsNullOrWhiteSpace(configuredName.Value))
                    continue;

                string titleId = ResolveTitleIdReference(configuredName.Key);
                if (string.IsNullOrWhiteSpace(titleId)
                    || _playerTitleNameOverrides.ContainsKey(titleId)
                    || !_titlesById.TryGetValue(titleId, out FeudalTitleRecord title)
                    || title == null
                    || !title.IsActive)
                {
                    continue;
                }

                string name = configuredName.Value.Trim();
                if (string.Equals(title.Name, name, StringComparison.Ordinal))
                    continue;

                title.SetName(name);
                title.MarkSynced(CurrentDay);
                applied++;
            }

            if (applied > 0)
            {
                BellumCivileLogger.Log($"Applied feudal title name overrides; titles={applied}; reason={reason ?? "unknown"}.");
            }

            return applied;
        }

        private bool TryApplyConfiguredTitle(
            FeudalTitleConfig.ConfiguredTitle configured,
            string reason,
            bool initializeConfiguredHierarchy)
        {
            if (configured == null || string.IsNullOrWhiteSpace(configured.Id))
                return false;

            Settlement settlement = ResolveSettlementReference(configured.SettlementRef);
            if (configured.Type == FeudalTitleType.Barony && settlement == null)
            {
                BellumCivileLogger.Log($"Skipped configured barony title {configured.Id}: settlement '{configured.SettlementRef}' could not be resolved.");
                return false;
            }

            bool changed = false;
            if (configured.Type == FeudalTitleType.Barony && settlement != null)
            {
                EnsureBaronyTitle(settlement, $"configured title {configured.Id}", out bool baronyChanged);
                changed |= baronyChanged;
            }

            Clan deJureClan = ResolveClanReference(configured.DeJureClanRef);
            Clan deFactoClan = ResolveClanReference(configured.DeFactoClanRef) ?? deJureClan;
            Kingdom kingdom = ResolveKingdomReference(configured.KingdomRef)
                ?? deFactoClan?.Kingdom
                ?? deJureClan?.Kingdom;
            if (kingdom != null && !IsIndependentRealmShell(kingdom))
            {
                EnsureKingdomTitle(kingdom, $"configured title {configured.Id}", out bool kingdomChanged);
                changed |= kingdomChanged;
            }

            string titleId = ResolveConfiguredTitleId(configured, settlement, kingdom);
            Settlement capital = ResolveSettlementReference(configured.CapitalRef) ?? settlement;
            string baseName = !string.IsNullOrWhiteSpace(configured.Name)
                ? configured.Name.Trim()
                : BuildConfiguredFallbackTitleRoot(configured.Type, settlement, capital, configured.Id);
            string name = ResolveEffectiveConfiguredTitleName(titleId, baseName);
            if (initializeConfiguredHierarchy)
                changed |= DeactivateDuplicateConfiguredKingdomTitle(configured, titleId);

            string parentTitleId = ResolveTitleIdReference(configured.ParentRef);
            if (string.IsNullOrWhiteSpace(parentTitleId) && configured.Type < FeudalTitleType.Kingdom)
            {
                Kingdom parentKingdom = kingdom ?? settlement?.OwnerClan?.Kingdom ?? settlement?.MapFaction as Kingdom;
                parentTitleId = ResolveKingdomLegalParentTitleId(parentKingdom);
            }

            if (_titlesById.TryGetValue(titleId, out FeudalTitleRecord existing))
            {
                // XML remains authoritative for presentation on every load unless the player has
                // explicitly renamed this title. Initial holders and legal parentage are seed data
                // only: reapplying them here would erase conquests, grants, succession, usurpation,
                // and de jure drift.
                if (!string.Equals(existing.Name, name, StringComparison.Ordinal))
                {
                    existing.SetName(name);
                    changed = true;
                }
                if (initializeConfiguredHierarchy)
                {
                    if (!string.IsNullOrWhiteSpace(deJureClan?.StringId) || !string.IsNullOrWhiteSpace(configured.DeJureClanRef))
                    {
                        string value = deJureClan?.StringId ?? string.Empty;
                        if (!string.Equals(existing.DeJureHolderClanId, value, StringComparison.Ordinal))
                        {
                            existing.SetDeJureHolder(value);
                            changed = true;
                        }
                    }
                    if (!string.IsNullOrWhiteSpace(deFactoClan?.StringId) || !string.IsNullOrWhiteSpace(configured.DeFactoClanRef))
                    {
                        string value = deFactoClan?.StringId ?? string.Empty;
                        if (!string.Equals(existing.DeFactoHolderClanId, value, StringComparison.Ordinal))
                        {
                            existing.SetDeFactoHolder(value);
                            changed = true;
                        }
                    }
                    if (!string.IsNullOrWhiteSpace(parentTitleId))
                    {
                        if (!string.Equals(existing.ParentTitleId, parentTitleId, StringComparison.Ordinal))
                        {
                            existing.SetParentTitle(parentTitleId);
                            changed = true;
                        }
                        if (!string.Equals(existing.DeFactoParentTitleId, parentTitleId, StringComparison.Ordinal))
                        {
                            existing.SetDeFactoParentTitle(parentTitleId);
                            changed = true;
                        }
                    }
                    if (kingdom != null && !string.Equals(existing.AssociatedKingdomId, kingdom.StringId, StringComparison.Ordinal))
                    {
                        existing.SetAssociatedKingdom(kingdom.StringId);
                        changed = true;
                    }
                }
                if (capital != null && !string.Equals(existing.CapitalSettlementId, capital.StringId, StringComparison.Ordinal))
                {
                    existing.SetCapitalSettlement(capital.StringId);
                    changed = true;
                }
                if (!string.IsNullOrWhiteSpace(configured.CultureRef)
                    && !string.Equals(existing.FallbackCultureRef, configured.CultureRef, StringComparison.Ordinal))
                {
                    existing.SetFallbackCulture(configured.CultureRef);
                    changed = true;
                }
                if (!existing.IsActive && !existing.IsDeliberatelyDissolved)
                {
                    existing.SetActive(true);
                    changed = true;
                }
                if (changed)
                    existing.MarkSynced(CurrentDay);
                return changed;
            }

            if (!initializeConfiguredHierarchy)
            {
                // Introducing a new configured higher title also introduces new legal borders.
                // Existing campaigns deliberately keep the de jure map they were initialized with.
                return changed;
            }

            FeudalTitleRecord record = new FeudalTitleRecord(
                titleId,
                name,
                configured.Type,
                deJureClan?.StringId ?? string.Empty,
                deFactoClan?.StringId ?? string.Empty,
                parentTitleId,
                capital?.StringId ?? string.Empty,
                kingdom?.StringId ?? string.Empty,
                CurrentDay,
                CurrentDay,
                fallbackCultureRef: configured.CultureRef);

            _titlesById[titleId] = record;
            BellumCivileLogger.Log($"Configured feudal title created; title={titleId}; type={configured.Type}; name={name}; reason={reason ?? "unknown"}.");
            return true;
        }

        private string ResolveEffectiveConfiguredTitleName(string titleId, string fallbackName)
        {
            string effectiveName = fallbackName;
            if (!string.IsNullOrWhiteSpace(titleId)
                && _playerTitleNameOverrides.TryGetValue(titleId, out string playerName)
                && !string.IsNullOrWhiteSpace(playerName))
            {
                return playerName.Trim();
            }

            IReadOnlyDictionary<string, string> configuredNames = FeudalTitleConfig.Instance.TitleNames;
            if (string.IsNullOrWhiteSpace(titleId) || configuredNames == null)
                return effectiveName;

            // Follow the same merged override order as ApplyConfiguredTitleNames. This prevents
            // the base title XML and the selected style preset from rewriting each other on every
            // weekly repair while preserving patch-file precedence and alias resolution.
            foreach (KeyValuePair<string, string> configuredName in configuredNames)
            {
                if (string.IsNullOrWhiteSpace(configuredName.Key) || string.IsNullOrWhiteSpace(configuredName.Value))
                    continue;

                string resolvedTitleId = ResolveTitleIdReference(configuredName.Key);
                if (string.Equals(resolvedTitleId, titleId, StringComparison.OrdinalIgnoreCase))
                    effectiveName = configuredName.Value.Trim();
            }

            return effectiveName;
        }

        private string ResolveConfiguredTitleId(FeudalTitleConfig.ConfiguredTitle configured, Settlement settlement, Kingdom kingdom)
        {
            if (configured == null || string.IsNullOrWhiteSpace(configured.Id))
                return string.Empty;

            if (configured.Type == FeudalTitleType.Barony && settlement != null)
                return BuildBaronyTitleId(settlement);

            if (configured.Type == FeudalTitleType.Kingdom
                && kingdom != null
                && !IsIndependentRealmShell(kingdom))
            {
                return BuildKingdomTitleId(kingdom);
            }

            return configured.Id.Trim();
        }

        private bool DeactivateDuplicateConfiguredKingdomTitle(FeudalTitleConfig.ConfiguredTitle configured, string canonicalTitleId)
        {
            if (configured == null
                || configured.Type != FeudalTitleType.Kingdom
                || string.IsNullOrWhiteSpace(configured.Id)
                || string.IsNullOrWhiteSpace(canonicalTitleId)
                || string.Equals(configured.Id, canonicalTitleId, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string duplicateTitleId = configured.Id.Trim();
            if (!_titlesById.TryGetValue(duplicateTitleId, out FeudalTitleRecord duplicate)
                || duplicate == null
                || duplicate.TitleType != FeudalTitleType.Kingdom)
            {
                return false;
            }

            bool changed = false;
            foreach (FeudalTitleRecord child in _titlesById.Values.Where(title => title != null && title.IsActive).ToList())
            {
                if (string.Equals(child.ParentTitleId, duplicateTitleId, StringComparison.OrdinalIgnoreCase))
                {
                    child.SetParentTitle(canonicalTitleId);
                    changed = true;
                }
                if (string.Equals(child.DeFactoParentTitleId, duplicateTitleId, StringComparison.OrdinalIgnoreCase))
                {
                    child.SetDeFactoParentTitle(canonicalTitleId);
                    changed = true;
                }
            }

            if (duplicate.IsActive
                || !string.IsNullOrWhiteSpace(duplicate.DeJureHolderClanId)
                || !string.IsNullOrWhiteSpace(duplicate.DeFactoHolderClanId)
                || !string.IsNullOrWhiteSpace(duplicate.ParentTitleId)
                || !string.IsNullOrWhiteSpace(duplicate.DeFactoParentTitleId))
            {
                duplicate.SetActive(false);
                duplicate.SetDeJureHolder(string.Empty);
                duplicate.SetDeFactoHolder(string.Empty);
                duplicate.SetParentTitle(string.Empty);
                duplicate.SetDeFactoParentTitle(string.Empty);
                duplicate.MarkSynced(CurrentDay);
                changed = true;
            }

            if (changed)
                BellumCivileLogger.Log($"Deactivated duplicate configured kingdom title; duplicate={duplicateTitleId}; canonical={canonicalTitleId}.");
            return changed;
        }

        private int ApplyConfiguredTitleChildren(FeudalTitleConfig.ConfiguredTitle configured)
        {
            if (configured == null || configured.ChildRefs == null || configured.ChildRefs.Count == 0)
                return 0;

            Settlement settlement = ResolveSettlementReference(configured.SettlementRef);
            Clan deJureClan = ResolveClanReference(configured.DeJureClanRef);
            Clan deFactoClan = ResolveClanReference(configured.DeFactoClanRef) ?? deJureClan;
            Kingdom kingdom = ResolveKingdomReference(configured.KingdomRef)
                ?? deFactoClan?.Kingdom
                ?? deJureClan?.Kingdom;
            string parentId = ResolveConfiguredTitleId(configured, settlement, kingdom);
            if (string.IsNullOrWhiteSpace(parentId))
                parentId = configured.Type == FeudalTitleType.Barony && !string.IsNullOrWhiteSpace(configured.SettlementRef)
                    ? ResolveTitleIdReference(configured.SettlementRef)
                    : ResolveTitleIdReference(configured.Id);
            if (string.IsNullOrWhiteSpace(parentId) || !_titlesById.ContainsKey(parentId))
                return 0;

            int changed = 0;
            foreach (string childRef in configured.ChildRefs)
            {
                string childId = ResolveTitleIdReference(childRef);
                if (string.IsNullOrWhiteSpace(childId) || !_titlesById.TryGetValue(childId, out FeudalTitleRecord childTitle))
                {
                    BellumCivileLogger.Log($"Skipped configured child '{childRef}' for parent {parentId}: child title could not be resolved.");
                    continue;
                }

                if (childId == parentId || WouldCreateTitleCycle(parentId, childId))
                {
                    BellumCivileLogger.Log($"Skipped configured child '{childRef}' for parent {parentId}: cycle detected.");
                    continue;
                }

                bool childChanged = false;
                if (!string.Equals(childTitle.ParentTitleId, parentId, StringComparison.Ordinal))
                {
                    childTitle.SetParentTitle(parentId);
                    childChanged = true;
                }
                if (!string.Equals(childTitle.DeFactoParentTitleId, parentId, StringComparison.Ordinal))
                {
                    childTitle.SetDeFactoParentTitle(parentId);
                    childChanged = true;
                }
                if (_titlesById.TryGetValue(parentId, out FeudalTitleRecord parentTitle))
                {
                    if (!string.Equals(childTitle.AssociatedKingdomId, parentTitle.AssociatedKingdomId, StringComparison.Ordinal))
                    {
                        childTitle.SetAssociatedKingdom(parentTitle.AssociatedKingdomId);
                        childChanged = true;
                    }
                }

                if (childChanged)
                {
                    childTitle.MarkSynced(CurrentDay);
                    changed++;
                    BellumCivileLogger.Log($"Configured child title attached; parent={parentId}; child_ref='{childRef}'; child={childId}; child_type={childTitle.TitleType}.");
                }
            }

            return changed;
        }

        private FeudalTitleRecord EnsureKingdomTitle(Kingdom kingdom, string reason)
        {
            return EnsureKingdomTitle(kingdom, reason, out _);
        }

        private FeudalTitleRecord EnsureKingdomTitle(Kingdom kingdom, string reason, out bool changed)
        {
            changed = false;
            if (!IsValidPermanentKingdom(kingdom))
                return null;
            if (IsIndependentRealmShell(kingdom))
                return GetIndependentRealmSovereignTitle(kingdom);

            string titleId = BuildKingdomTitleId(kingdom);
            string holderClanId = kingdom.RulingClan?.StringId ?? string.Empty;
            string capitalSettlementId = SelectKingdomCapital(kingdom)?.StringId ?? string.Empty;
            string baseTitleName = ResolveConfiguredKingdomTitleRoot(kingdom)
                ?? DynamicKingdomTitleNameHelper.GetNativeTitleRoot(kingdom)
                ?? kingdom.StringId;
            string titleName = ResolveEffectiveConfiguredTitleName(titleId, baseTitleName);

            if (_titlesById.TryGetValue(titleId, out FeudalTitleRecord existing))
            {
                if (CrownAccessionBehavior.Instance?.IsRealmUnionTitleProtected(existing.TitleId) == true)
                    return existing;
                if (!string.Equals(existing.Name, titleName, StringComparison.Ordinal))
                {
                    existing.SetName(titleName);
                    changed = true;
                }
                if (string.IsNullOrWhiteSpace(existing.DeFactoHolderClanId))
                {
                    existing.SetDeFactoHolder(holderClanId);
                    changed = true;
                }
                if (string.IsNullOrWhiteSpace(existing.DeJureHolderClanId))
                {
                    existing.SetDeJureHolder(holderClanId);
                    changed = true;
                }
                if (!string.Equals(existing.CapitalSettlementId, capitalSettlementId, StringComparison.Ordinal))
                {
                    existing.SetCapitalSettlement(capitalSettlementId);
                    changed = true;
                }
                if (string.IsNullOrWhiteSpace(existing.AssociatedKingdomId))
                {
                    existing.SetAssociatedKingdom(kingdom.StringId);
                    changed = true;
                }
                if (!existing.IsActive)
                {
                    existing.SetActive(true);
                    changed = true;
                }
                if (changed)
                    existing.MarkSynced(CurrentDay);
                return existing;
            }

            FeudalTitleRecord record = new FeudalTitleRecord(
                titleId,
                titleName,
                FeudalTitleType.Kingdom,
                holderClanId,
                holderClanId,
                string.Empty,
                capitalSettlementId,
                kingdom.StringId,
                CurrentDay,
                CurrentDay);

            _titlesById[titleId] = record;
            changed = true;
            BellumCivileLogger.Log($"Feudal kingdom title created; title={titleId}; kingdom={kingdom.StringId}; holder={holderClanId}; reason={reason ?? "unknown"}.");
            return record;
        }

        private string ResolveConfiguredKingdomTitleRoot(Kingdom kingdom)
        {
            if (kingdom == null)
                return null;

            FeudalTitleConfig.ConfiguredTitle configured = FeudalTitleConfig.Instance.Titles?
                .Where(title => title != null && title.Type == FeudalTitleType.Kingdom)
                .FirstOrDefault(title => ResolveKingdomReference(title.KingdomRef) == kingdom);
            if (configured == null)
                return null;

            if (!string.IsNullOrWhiteSpace(configured.Name))
                return configured.Name.Trim();

            Settlement capital = ResolveSettlementReference(configured.CapitalRef) ?? SelectKingdomCapital(kingdom);
            return BuildConfiguredFallbackTitleRoot(FeudalTitleType.Kingdom, null, capital, BuildKingdomTitleId(kingdom));
        }

        private FeudalTitleRecord EnsureBaronyTitle(Settlement settlement, string reason)
        {
            return EnsureBaronyTitle(settlement, reason, out _);
        }

        private FeudalTitleRecord EnsureBaronyTitle(Settlement settlement, string reason, out bool changed)
        {
            changed = false;
            if (!IsLandedSettlement(settlement))
                return null;

            string titleId = BuildBaronyTitleId(settlement);
            Clan ownerClan = settlement.OwnerClan;
            string ownerClanId = ownerClan?.StringId ?? string.Empty;
            Kingdom ownerKingdom = ownerClan?.Kingdom ?? settlement.MapFaction as Kingdom;
            string parentTitleId = ResolveKingdomLegalParentTitleId(ownerKingdom);
            string baseTitleName = BuildConfiguredFallbackTitleRoot(FeudalTitleType.Barony, settlement, settlement, titleId);
            string titleName = ResolveEffectiveConfiguredTitleName(titleId, baseTitleName);

            if (ownerKingdom != null && !IsIndependentRealmShell(ownerKingdom))
            {
                EnsureKingdomTitle(ownerKingdom, reason, out bool kingdomChanged);
                changed |= kingdomChanged;
            }

            if (_titlesById.TryGetValue(titleId, out FeudalTitleRecord existing))
            {
                if (!string.Equals(existing.Name, titleName, StringComparison.Ordinal))
                {
                    existing.SetName(titleName);
                    changed = true;
                }
                if (!string.Equals(existing.DeFactoHolderClanId, ownerClanId, StringComparison.Ordinal))
                {
                    existing.SetDeFactoHolder(ownerClanId);
                    changed = true;
                }
                if (string.IsNullOrWhiteSpace(existing.DeJureHolderClanId))
                {
                    existing.SetDeJureHolder(ownerClanId);
                    changed = true;
                }
                if (string.IsNullOrWhiteSpace(existing.ParentTitleId) && !string.IsNullOrWhiteSpace(parentTitleId))
                {
                    existing.SetParentTitle(parentTitleId);
                    existing.SetDeFactoParentTitle(parentTitleId);
                    changed = true;
                }
                if (string.IsNullOrWhiteSpace(existing.AssociatedKingdomId) && ownerKingdom != null)
                {
                    existing.SetAssociatedKingdom(ownerKingdom.StringId);
                    changed = true;
                }
                if (!string.Equals(existing.CapitalSettlementId, settlement.StringId, StringComparison.Ordinal))
                {
                    existing.SetCapitalSettlement(settlement.StringId);
                    changed = true;
                }
                if (!existing.IsActive)
                {
                    existing.SetActive(true);
                    changed = true;
                }
                if (changed)
                    existing.MarkSynced(CurrentDay);
                return existing;
            }

            FeudalTitleRecord record = new FeudalTitleRecord(
                titleId,
                titleName,
                FeudalTitleType.Barony,
                ownerClanId,
                ownerClanId,
                parentTitleId,
                settlement.StringId,
                ownerKingdom?.StringId,
                CurrentDay,
                CurrentDay);

            _titlesById[titleId] = record;
            changed = true;
            BellumCivileLogger.Log($"Feudal barony title created; title={titleId}; settlement={settlement.StringId}; holder={ownerClanId}; parent={parentTitleId}; reason={reason ?? "unknown"}.");
            return record;
        }

        private void UpdateBaronyParentToCurrentKingdom(FeudalTitleRecord title, Clan ownerClan)
        {
            Kingdom kingdom = ownerClan?.Kingdom;
            if (!IsValidPermanentKingdom(kingdom))
                return;

            FeudalTitleRecord currentLegalParent = GetTitle(title.ParentTitleId);
            if (IsLegalParentCompatibleWithKingdom(title, currentLegalParent, kingdom))
            {
                if (string.IsNullOrWhiteSpace(title.DeFactoParentTitleId))
                    title.SetDeFactoParentTitle(currentLegalParent.TitleId);
                title.SetAssociatedKingdom(kingdom.StringId);
                return;
            }

            string parentTitleId = ResolveKingdomLegalParentTitleId(kingdom);
            if (string.IsNullOrWhiteSpace(parentTitleId))
                return;

            if (!IsIndependentRealmShell(kingdom))
                EnsureKingdomTitle(kingdom, "legal barony transfer");

            title.SetParentTitle(parentTitleId);
            title.SetDeFactoParentTitle(parentTitleId);
            title.SetAssociatedKingdom(kingdom.StringId);
        }

        private static bool IsLegalParentCompatibleWithKingdom(FeudalTitleRecord title, FeudalTitleRecord parent, Kingdom kingdom)
        {
            if (title == null || parent == null || kingdom == null || !parent.IsActive)
                return false;

            return parent.TitleType > title.TitleType
                && string.Equals(parent.AssociatedKingdomId, kingdom.StringId, StringComparison.OrdinalIgnoreCase);
        }

        private bool ReconcileDeFactoParent(FeudalTitleRecord title, Kingdom holderKingdom)
        {
            if (title == null || !title.IsActive || !IsValidPermanentKingdom(holderKingdom))
                return false;
            if (CrownAccessionBehavior.Instance?.IsRealmUnionTitleProtected(title.TitleId) == true) return false;
            if (Campaign.Current?.GetCampaignBehavior<PartitionSuccessionBehavior>()
                ?.IsCrownPromotionTitleProtected(title.TitleId) == true) return false;

            FeudalTitleRecord resolvedParent = ResolveBestDeFactoParent(title, holderKingdom);
            string resolvedParentId = resolvedParent?.TitleId ?? string.Empty;
            if (string.Equals(title.DeFactoParentTitleId, resolvedParentId, StringComparison.Ordinal))
                return false;

            title.SetDeFactoParentTitle(resolvedParentId);
            title.MarkSynced(CurrentDay);
            QueueServiceReview(title, "de facto parent reconciled");
            return true;
        }

        private FeudalTitleRecord ResolveBestDeFactoParent(FeudalTitleRecord title, Kingdom holderKingdom)
        {
            if (title == null || title.TitleType >= FeudalTitleType.Empire || !IsValidPermanentKingdom(holderKingdom))
                return null;

            Clan holderClan = ResolveClan(title.DeFactoHolderClanId);
            FeudalTitleRecord designatedSubinfeudationParent = ResolveDesignatedSubinfeudationParent(title, holderKingdom);
            if (designatedSubinfeudationParent != null)
                return designatedSubinfeudationParent;

            FeudalTitleRecord legalParent = GetTitle(title.ParentTitleId);
            FeudalTitleRecord holderHierarchyParent = ResolveHolderHierarchyDeFactoParent(title, holderClan, holderKingdom);
            if (ShouldDecoupleFromLegalParent(title, legalParent, holderClan, holderKingdom))
                return holderHierarchyParent ?? ResolveRealmSovereignDeFactoParent(title, holderKingdom);

            // A conquered title package keeps its old de jure realm until drift completes, but
            // titles already controlled by the same realm should retain their internal chain in
            // the de facto tree. AssociatedKingdomId alone would flatten the whole package under
            // the holder's highest title while that drift is still in progress.
            if (IsDeFactoParentControlledByKingdom(title, legalParent, holderKingdom)
                && !WouldCreateTitleCycle(legalParent.TitleId, title.TitleId, FeudalHierarchyMode.DeFacto))
            {
                return legalParent;
            }

            if (holderHierarchyParent != null && ShouldUseHolderHierarchyForForeignTitle(title, legalParent, holderClan))
                return holderHierarchyParent;

            return ResolveRealmSovereignDeFactoParent(title, holderKingdom);
        }

        private FeudalTitleRecord ResolveDesignatedSubinfeudationParent(FeudalTitleRecord title, Kingdom holderKingdom)
        {
            if (title == null
                || holderKingdom == null
                || !_subinfeudationParentByTitleId.TryGetValue(title.TitleId, out string parentTitleId)
                || string.IsNullOrWhiteSpace(parentTitleId))
            {
                return null;
            }

            FeudalTitleRecord parent = GetTitle(parentTitleId);
            Clan parentHolder = ResolveClan(parent?.DeFactoHolderClanId);
            bool valid = parent != null
                && parent.IsActive
                && parent.TitleType > title.TitleType
                && parentHolder != null
                && !parentHolder.IsEliminated
                && parentHolder.Kingdom == holderKingdom
                && !WouldCreateTitleCycle(parent.TitleId, title.TitleId, FeudalHierarchyMode.DeFacto);
            if (valid)
                return parent;

            _subinfeudationParentByTitleId.Remove(title.TitleId);
            BellumCivileLogger.Log(
                $"Expired invalid subinfeudation parent; title={title.TitleId}; former_parent={parentTitleId}; holder_kingdom={holderKingdom.StringId}.");
            return null;
        }

        private static bool IsDeFactoParentControlledByKingdom(
            FeudalTitleRecord title,
            FeudalTitleRecord parent,
            Kingdom holderKingdom)
        {
            return title != null
                && parent != null
                && parent.IsActive
                && parent.TitleType > title.TitleType
                && IsTitlePoliticallyControlledByKingdom(parent, holderKingdom);
        }

        private FeudalTitleRecord ResolveHolderHierarchyDeFactoParent(FeudalTitleRecord title, Clan holderClan, Kingdom holderKingdom)
        {
            if (title == null || holderClan == null || holderKingdom == null)
                return null;

            return GetTitlesHeldByClan(holderClan, deJure: false)
                .Where(candidate =>
                    candidate != null
                    && candidate.IsActive
                    && candidate.TitleId != title.TitleId
                    && candidate.TitleType > title.TitleType
                    && IsTitlePoliticallyControlledByKingdom(candidate, holderKingdom)
                    && !WouldCreateTitleCycle(candidate.TitleId, title.TitleId, FeudalHierarchyMode.DeFacto))
                .OrderByDescending(candidate => candidate.TitleType)
                .ThenByDescending(candidate => string.Equals(candidate.DeJureHolderClanId, holderClan.StringId, StringComparison.Ordinal))
                .ThenBy(candidate => candidate.TitleId)
                .FirstOrDefault();
        }

        private bool ShouldDecoupleFromLegalParent(FeudalTitleRecord title, FeudalTitleRecord legalParent, Clan holderClan, Kingdom holderKingdom)
        {
            if (title == null || legalParent == null || holderClan == null || !IsLegalParentCompatibleWithKingdom(title, legalParent, holderKingdom))
                return false;

            if (string.Equals(legalParent.DeFactoHolderClanId, holderClan.StringId, StringComparison.Ordinal))
                return false;

            FeudalTitleRecord highestHolderTitle = GetHighestDeFactoTitleHeldByClan(holderClan);
            return highestHolderTitle != null && highestHolderTitle.TitleType >= legalParent.TitleType;
        }

        private bool ShouldUseHolderHierarchyForForeignTitle(FeudalTitleRecord title, FeudalTitleRecord legalParent, Clan holderClan)
        {
            if (title == null || legalParent == null || holderClan == null)
                return false;

            FeudalTitleRecord highestHolderTitle = GetHighestDeFactoTitleHeldByClan(holderClan);
            return highestHolderTitle != null && highestHolderTitle.TitleType >= legalParent.TitleType;
        }

        private bool IsServiceDecoupledByRank(
            FeudalTitleRecord title,
            FeudalTitleRecord legalParent,
            FeudalTitleRecord effectiveParent)
        {
            if (title == null
                || legalParent == null
                || effectiveParent == null
                || string.Equals(legalParent.TitleId, effectiveParent.TitleId, StringComparison.Ordinal))
            {
                return false;
            }

            Clan holderClan = ResolveClan(title.DeFactoHolderClanId);
            if (holderClan == null || string.Equals(legalParent.DeFactoHolderClanId, holderClan.StringId, StringComparison.Ordinal))
                return false;

            FeudalTitleRecord highestHolderTitle = GetHighestDeFactoTitleHeldByClan(holderClan);
            return highestHolderTitle != null && highestHolderTitle.TitleType >= legalParent.TitleType;
        }

        private FeudalTitleRecord ResolveRealmSovereignDeFactoParent(FeudalTitleRecord title, Kingdom holderKingdom)
        {
            if (title == null || !IsValidPermanentKingdom(holderKingdom))
                return null;

            FeudalTitleRecord sovereignTitle = GetRealmSovereignTitle(holderKingdom, FeudalHierarchyMode.DeFacto);
            if (sovereignTitle == null
                || sovereignTitle.TitleId == title.TitleId
                || sovereignTitle.TitleType <= title.TitleType
                || WouldCreateTitleCycle(sovereignTitle.TitleId, title.TitleId, FeudalHierarchyMode.DeFacto))
            {
                return null;
            }

            return sovereignTitle;
        }

        private bool IsCompatibleDeFactoParent(FeudalTitleRecord title, FeudalTitleRecord parent, Kingdom holderKingdom)
        {
            if (title == null || parent == null || !parent.IsActive || !IsValidPermanentKingdom(holderKingdom))
                return false;

            FeudalTitleRecord legalParent = GetTitle(title.ParentTitleId);
            if (!IsLegalParentCompatibleWithKingdom(title, legalParent, holderKingdom))
            {
                FeudalTitleRecord sovereignTitle = GetRealmSovereignTitle(holderKingdom, FeudalHierarchyMode.DeFacto);
                return sovereignTitle != null
                    && string.Equals(parent.TitleId, sovereignTitle.TitleId, StringComparison.OrdinalIgnoreCase)
                    && parent.TitleType > title.TitleType
                    && IsTitlePoliticallyControlledByKingdom(parent, holderKingdom)
                    && !WouldCreateTitleCycle(parent.TitleId, title.TitleId, FeudalHierarchyMode.DeFacto);
            }

            return title != null
                && parent != null
                && parent.IsActive
                && parent.TitleType > title.TitleType
                && IsTitlePoliticallyControlledByKingdom(parent, holderKingdom)
                && !WouldCreateTitleCycle(parent.TitleId, title.TitleId, FeudalHierarchyMode.DeFacto);
        }

        private static bool IsTitlePoliticallyControlledByKingdom(FeudalTitleRecord title, Kingdom kingdom)
        {
            if (title == null || kingdom == null || string.IsNullOrWhiteSpace(title.DeFactoHolderClanId))
                return false;

            Clan holder = ResolveClan(title.DeFactoHolderClanId);
            return holder?.Kingdom == kingdom;
        }

        private static bool IsTitleHeldByClan(FeudalTitleRecord title, string clanId)
        {
            return title != null
                && !string.IsNullOrWhiteSpace(clanId)
                && (string.Equals(title.DeJureHolderClanId, clanId, StringComparison.Ordinal)
                    || string.Equals(title.DeFactoHolderClanId, clanId, StringComparison.Ordinal));
        }

        private bool IsTitleWithinOriginRealm(FeudalTitleRecord title, Kingdom originKingdom)
        {
            if (title == null)
                return false;
            if (originKingdom == null)
                return true;

            if (string.Equals(title.AssociatedKingdomId, originKingdom.StringId, StringComparison.Ordinal))
                return true;

            FeudalTitleRecord politicalTitle = GetKingdomPoliticalTitle(originKingdom);
            if (politicalTitle == null)
                return false;

            string currentParentId = title.ParentTitleId;
            HashSet<string> visited = new HashSet<string>();
            while (!string.IsNullOrWhiteSpace(currentParentId) && visited.Add(currentParentId))
            {
                if (string.Equals(currentParentId, politicalTitle.TitleId, StringComparison.Ordinal))
                    return true;

                FeudalTitleRecord parent = GetTitle(currentParentId);
                if (parent == null)
                    break;
                if (string.Equals(parent.AssociatedKingdomId, originKingdom.StringId, StringComparison.Ordinal))
                    return true;

                currentParentId = parent.ParentTitleId;
            }

            return false;
        }

        private static bool IsTemporaryRebelKingdom(Kingdom kingdom)
        {
            if (kingdom == null)
                return false;

            if (!string.IsNullOrWhiteSpace(kingdom.StringId)
                && (kingdom.StringId.Contains("_rebels_")
                    || kingdom.StringId.StartsWith("bc_feud_")))
            {
                return true;
            }

            FactionManagerBehavior factionManager = Campaign.Current?.GetCampaignBehavior<FactionManagerBehavior>();
            return factionManager?.GetFactionByRebelKingdom(kingdom) != null;
        }

        private bool WouldCreateTitleCycle(string parentId, string childId)
        {
            return WouldCreateTitleCycle(parentId, childId, FeudalHierarchyMode.DeJure);
        }

        private bool WouldCreateTitleCycle(string parentId, string childId, FeudalHierarchyMode mode)
        {
            string current = parentId;
            int guard = 0;
            while (!string.IsNullOrWhiteSpace(current) && guard++ < 32)
            {
                if (string.Equals(current, childId, StringComparison.OrdinalIgnoreCase))
                    return true;

                current = _titlesById.TryGetValue(current, out FeudalTitleRecord title)
                    ? mode == FeudalHierarchyMode.DeFacto
                        ? title.DeFactoParentTitleId
                        : title.ParentTitleId
                    : string.Empty;
            }

            return false;
        }

        private void EnsureCollectionsInitialized()
        {
            if (_titlesById == null)
                _titlesById = new Dictionary<string, FeudalTitleRecord>();
            if (_claims == null)
                _claims = new List<FeudalClaimRecord>();
            if (_independentRealmSourceTitleByKingdomId == null)
                _independentRealmSourceTitleByKingdomId = new Dictionary<string, string>();
            if (_currentRealmTitleByKingdomId == null)
                _currentRealmTitleByKingdomId = new Dictionary<string, string>();
            if (_realmIdentityRoots == null) _realmIdentityRoots = new Dictionary<string, string>();
            if (_realmIdentityNativeNames == null) _realmIdentityNativeNames = new Dictionary<string, string>();
            if (_realmTitleStyleSources == null) _realmTitleStyleSources = new Dictionary<string, string>();
            if (_historicalRealmSovereignTitleByKingdomId == null)
                _historicalRealmSovereignTitleByKingdomId = new Dictionary<string, string>();
            if (_playerTitleNameOverrides == null)
                _playerTitleNameOverrides = new Dictionary<string, string>();
            if (_subinfeudationParentByTitleId == null)
                _subinfeudationParentByTitleId = new Dictionary<string, string>();
        }

        private void MigrateDeFactoHierarchyIfNeeded()
        {
            bool legacySchema = _hierarchySchemaVersion < CurrentHierarchySchemaVersion;
            int initialized = 0;
            foreach (FeudalTitleRecord title in _titlesById.Values.Where(title => title != null))
            {
                string before = title.DeFactoParentTitleId;
                title.InitializeDeFactoParentFromDeJure(legacySchema);
                if (legacySchema || before != title.DeFactoParentTitleId)
                    initialized++;
            }

            if (legacySchema)
            {
                int previousVersion = _hierarchySchemaVersion;
                _hierarchySchemaVersion = CurrentHierarchySchemaVersion;
                BellumCivileLogger.Log($"Migrated feudal hierarchy schema {previousVersion}->{CurrentHierarchySchemaVersion}; initialized_de_facto_parents={initialized}.");
            }
        }

        private void RebuildRuntimeIndexes(bool advanceRevision = true, bool refreshDisplay = true)
        {
            if (advanceRevision)
                RuntimeRevision++;
            if (_claimIndexBatchDepth == 0)
                _claimIndexRebuildPending = false;
            if (refreshDisplay)
            {
                DisplayRevision++;
                _titleDisplayRefreshPending = true;
            }
            _baronyTitleBySettlementId.Clear();
            _titlesByDeJureHolder.Clear();
            _titlesByDeFactoHolder.Clear();
            _claimsByClan.Clear();
            _claimsByClanAndTitle.Clear();
            _claimsByTitle.Clear();
            _claimsById.Clear();
            _childrenByParentTitleId.Clear();
            _childrenByDeFactoParentTitleId.Clear();
            _titleDistanceCache.Clear();
            _deFactoAuthorityBaronyCountByClanId.Clear();
            _deFactoAuthorityCacheRevision = -1;

            if (_titlesById != null)
            {
                foreach (FeudalTitleRecord title in _titlesById.Values)
                {
                    if (title == null || !title.IsActive)
                        continue;

                    if (title.TitleType == FeudalTitleType.Barony && !string.IsNullOrWhiteSpace(title.CapitalSettlementId))
                        _baronyTitleBySettlementId[title.CapitalSettlementId] = title.TitleId;

                    AddToIndex(_titlesByDeJureHolder, title.DeJureHolderClanId, title.TitleId);
                    AddToIndex(_titlesByDeFactoHolder, title.DeFactoHolderClanId, title.TitleId);
                    AddToIndex(_childrenByParentTitleId, title.ParentTitleId, title.TitleId);
                    AddToIndex(_childrenByDeFactoParentTitleId, title.DeFactoParentTitleId, title.TitleId);
                }
            }

            if (_claims != null)
            {
                foreach (FeudalClaimRecord claim in _claims)
                {
                    if (claim == null || !claim.IsActive)
                        continue;

                    if (!string.IsNullOrWhiteSpace(claim.ClaimId))
                        _claimsById[claim.ClaimId] = claim;
                    AddToIndex(_claimsByClan, claim.ClaimantClanId, claim.ClaimId);
                    AddClaimToPairIndex(claim);
                    AddClaimToTitleIndex(claim);
                }
            }
        }

        private void ReconcileCurrentRealmTitles()
        {
            if (_currentRealmTitleByKingdomId == null)
                _currentRealmTitleByKingdomId = new Dictionary<string, string>();
            foreach (Kingdom realm in Kingdom.All)
            {
                if (realm == null || realm.IsEliminated || realm.RulingClan == null
                    || BellumKingdomVisibilityHelper.IsTemporaryBellumKingdom(realm)) continue;
                if (CrownAccessionBehavior.Instance?.IsRealmUnionProtected(realm) == true) continue;
                FeudalTitleRecord current = GetRealmSovereignTitle(realm, FeudalHierarchyMode.DeFacto);
                _currentRealmTitleByKingdomId.TryGetValue(realm.StringId, out string previous);
                if (current == null)
                {
                    _currentRealmTitleByKingdomId.Remove(realm.StringId);
                    continue;
                }
                if (previous == current.TitleId) continue;
                _currentRealmTitleByKingdomId[realm.StringId] = current.TitleId;
                BellumCivileLogger.Log($"Realm sovereign reconciled; realm={realm.StringId}; old_title={previous ?? "none"}; title={current.TitleId}; rank={current.TitleType}; ruler={realm.RulingClan.StringId}.");
            }
        }

        private void BeginClaimIndexBatch()
        {
            _claimIndexBatchDepth++;
        }

        private void EndClaimIndexBatch()
        {
            if (_claimIndexBatchDepth <= 0)
                return;

            _claimIndexBatchDepth--;
            if (_claimIndexBatchDepth == 0 && _claimIndexRebuildPending)
            {
                _claimIndexRebuildPending = false;
                RebuildRuntimeIndexes(refreshDisplay: false);
            }
        }

        private void RequestClaimIndexRebuild(FeudalClaimRecord newlyActiveClaim = null)
        {
            if (_claimIndexBatchDepth <= 0)
            {
                RebuildRuntimeIndexes(refreshDisplay: false);
                return;
            }

            _claimIndexRebuildPending = true;
            if (newlyActiveClaim == null || !IsClaimCurrentlyActive(newlyActiveClaim))
                return;

            if (!string.IsNullOrWhiteSpace(newlyActiveClaim.ClaimId))
                _claimsById[newlyActiveClaim.ClaimId] = newlyActiveClaim;
            AddToIndex(_claimsByClan, newlyActiveClaim.ClaimantClanId, newlyActiveClaim.ClaimId);
            AddClaimToPairIndex(newlyActiveClaim);
            AddClaimToTitleIndex(newlyActiveClaim);
        }

        private void AddClaimToTitleIndex(FeudalClaimRecord claim)
        {
            if (claim == null || string.IsNullOrWhiteSpace(claim.TargetTitleId))
                return;

            if (!_claimsByTitle.TryGetValue(claim.TargetTitleId, out List<FeudalClaimRecord> claims))
            {
                claims = new List<FeudalClaimRecord>();
                _claimsByTitle[claim.TargetTitleId] = claims;
            }

            if (!claims.Contains(claim))
                claims.Add(claim);
        }

        private void AddClaimToPairIndex(FeudalClaimRecord claim)
        {
            if (claim == null
                || string.IsNullOrWhiteSpace(claim.ClaimantClanId)
                || string.IsNullOrWhiteSpace(claim.TargetTitleId))
            {
                return;
            }

            if (!_claimsByClanAndTitle.TryGetValue(claim.ClaimantClanId, out Dictionary<string, List<FeudalClaimRecord>> claimsByTitle))
            {
                claimsByTitle = new Dictionary<string, List<FeudalClaimRecord>>();
                _claimsByClanAndTitle[claim.ClaimantClanId] = claimsByTitle;
            }

            if (!claimsByTitle.TryGetValue(claim.TargetTitleId, out List<FeudalClaimRecord> claims))
            {
                claims = new List<FeudalClaimRecord>();
                claimsByTitle[claim.TargetTitleId] = claims;
            }

            if (!claims.Contains(claim))
                claims.Add(claim);
        }

        private static void AddToIndex(Dictionary<string, List<string>> index, string key, string value)
        {
            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value))
                return;

            if (!index.TryGetValue(key, out List<string> values))
            {
                values = new List<string>();
                index[key] = values;
            }

            if (!values.Contains(value))
                values.Add(value);
        }

        private void QueueServiceReview(FeudalTitleRecord title, string reason)
        {
            if (title == null)
                return;

            (FeudalServiceBehavior.Instance ?? Campaign.Current?.GetCampaignBehavior<FeudalServiceBehavior>())
                ?.QueueTitleRelationshipReview(title, reason);
        }

        private void QueueServiceReviewForTitleAndChildren(FeudalTitleRecord title, string reason)
        {
            QueueServiceReview(title, reason);
            foreach (FeudalTitleRecord child in GetChildTitles(title, FeudalHierarchyMode.DeFacto))
                QueueServiceReview(child, reason);
        }

        private bool RecipientHoldsImmediateChildTitle(Clan recipientClan, FeudalTitleRecord title)
        {
            if (recipientClan == null || title == null || string.IsNullOrWhiteSpace(recipientClan.StringId))
                return false;

            return GetChildTitles(title, FeudalHierarchyMode.DeFacto)
                .Concat(GetChildTitles(title, FeudalHierarchyMode.DeJure))
                .Where(child => child != null && child.IsActive)
                .GroupBy(child => child.TitleId)
                .Select(group => group.First())
                .Any(child =>
                    string.Equals(child.DeFactoHolderClanId, recipientClan.StringId, StringComparison.Ordinal)
                    || string.Equals(child.DeJureHolderClanId, recipientClan.StringId, StringComparison.Ordinal));
        }

        private void RelinquishHoldingsOutsideSovereignTitle(
            Clan departingClan,
            Kingdom parentKingdom,
            Clan parentRulingClan,
            HashSet<string> retainedTitleIds,
            out int relinquishedSettlementCount,
            out int relinquishedUpperTitleCount)
        {
            relinquishedSettlementCount = 0;
            relinquishedUpperTitleCount = 0;
            if (departingClan == null
                || parentKingdom == null
                || parentRulingClan?.Leader == null
                || retainedTitleIds == null)
            {
                return;
            }

            HashSet<string> retainedSettlementIds = new HashSet<string>(
                retainedTitleIds
                    .Select(GetTitle)
                    .Where(title => title != null
                        && title.TitleType == FeudalTitleType.Barony
                        && !string.IsNullOrWhiteSpace(title.CapitalSettlementId))
                    .Select(title => title.CapitalSettlementId),
                StringComparer.Ordinal);
            List<FeudalTitleRecord> relinquishedTitles = _titlesById.Values
                .Where(title => title != null
                    && title.IsActive
                    && !retainedTitleIds.Contains(title.TitleId)
                    && (string.Equals(title.DeJureHolderClanId, departingClan.StringId, StringComparison.Ordinal)
                        || string.Equals(title.DeFactoHolderClanId, departingClan.StringId, StringComparison.Ordinal)))
                .ToList();
            List<Settlement> relinquishedSettlements = departingClan.Settlements
                .Where(settlement => settlement != null && !retainedSettlementIds.Contains(settlement.StringId))
                .ToList();

            foreach (Settlement settlement in relinquishedSettlements)
            {
                if (settlement.OwnerClan != departingClan)
                    continue;

                RecordAllocationCustody(settlement, parentRulingClan);
                ChangeOwnerOfSettlementAction.ApplyByDefault(parentRulingClan.Leader, settlement);
                relinquishedSettlementCount++;
            }

            foreach (FeudalTitleRecord relinquishedTitle in relinquishedTitles)
            {
                bool changed = false;
                if (string.Equals(relinquishedTitle.DeJureHolderClanId, departingClan.StringId, StringComparison.Ordinal))
                {
                    relinquishedTitle.SetDeJureHolder(parentRulingClan.StringId);
                    changed = true;
                }

                if (string.Equals(relinquishedTitle.DeFactoHolderClanId, departingClan.StringId, StringComparison.Ordinal))
                {
                    relinquishedTitle.SetDeFactoHolder(parentRulingClan.StringId);
                    changed = true;
                }

                if (!changed)
                    continue;

                relinquishedTitle.SetAssociatedKingdom(parentKingdom.StringId);
                relinquishedTitle.MarkSynced(CurrentDay);
                RemoveClaimsForClanToTitle(departingClan, relinquishedTitle);
                if (!IsAllocationCustodian(relinquishedTitle.TitleId, parentRulingClan.StringId))
                    RemoveRedundantClaimsForHolder(parentRulingClan, relinquishedTitle);
                QueueServiceReviewForTitleAndChildren(relinquishedTitle, "peaceful sovereign separation");
                if (relinquishedTitle.TitleType > FeudalTitleType.Barony)
                    relinquishedUpperTitleCount++;
            }

            FiefDeliberationBehavior fiefDeliberation = Campaign.Current?.GetCampaignBehavior<FiefDeliberationBehavior>();
            foreach (Settlement settlement in relinquishedSettlements)
            {
                if (settlement?.Town != null && settlement.OwnerClan == parentRulingClan)
                {
                    fiefDeliberation?.QueueSovereignSeparationSettlementVote(
                        settlement,
                        parentKingdom,
                        parentRulingClan,
                        departingClan);
                }

                if (TryGetBarony(settlement, out FeudalTitleRecord baronyTitle))
                    RemoveClaimsForClanToTitle(departingClan, baronyTitle);
            }
        }

        private static void ApplySovereignDepartureRelations(Clan departingClan, Kingdom formerKingdom, int relationPenalty)
        {
            Hero departingLeader = departingClan?.Leader;
            if (departingLeader == null || departingLeader.IsDead || formerKingdom == null || relationPenalty == 0)
                return;

            foreach (Clan formerRealmClan in formerKingdom.Clans.ToList())
            {
                Hero otherLeader = formerRealmClan?.Leader;
                if (formerRealmClan == null
                    || formerRealmClan == departingClan
                    || formerRealmClan.IsEliminated
                    || formerRealmClan.IsUnderMercenaryService
                    || otherLeader == null
                    || otherLeader.IsDead
                    || otherLeader == departingLeader)
                {
                    continue;
                }

                RelationMemoryService.ApplyChangeWithDefaultDuration(
                    departingLeader,
                    otherLeader,
                    relationPenalty,
                    false,
                    RelationMemorySources.SovereignDeparture, RelationMemoryScope.Personal);
            }
        }

        private Kingdom CreateIndependentRealmFromGrantedTitle(Kingdom parentKingdom, Clan recipientClan, FeudalTitleRecord sourceTitle)
        {
            TextObject encyclopediaText = new TextObject("{=BC_TitleGrant_IndependentRealmText}Created by a sovereign grant from {PARENT_KINGDOM}, the {NEW_KINGDOM} entered the world as an independent realm.");
            return CreateIndependentRealmFromTitle(
                parentKingdom,
                recipientClan,
                sourceTitle,
                "bc_grant_indep",
                encyclopediaText);
        }

        private Kingdom CreateIndependentRealmFromTitle(
            Kingdom parentKingdom,
            Clan recipientClan,
            FeudalTitleRecord sourceTitle,
            string kingdomIdPrefix,
            TextObject encyclopediaText)
        {
            if (recipientClan == null || sourceTitle == null)
                return null;

            Settlement capital = ResolveSettlement(sourceTitle.CapitalSettlementId)
                              ?? recipientClan.Settlements.FirstOrDefault()
                              ?? parentKingdom?.Settlements.FirstOrDefault()
                              ?? Settlement.All.FirstOrDefault(settlement => settlement.IsTown || settlement.IsCastle);
            return CreateIndependentRealmAtResidence(parentKingdom, recipientClan, sourceTitle,
                kingdomIdPrefix, encyclopediaText, capital);
        }

        internal Town ResolveCrownPartitionResidence(Kingdom parentRealm, FeudalTitleRecord crown)
        {
            if (crown?.IsActive != true || crown.TitleType < FeudalTitleType.Kingdom) return null;
            var ids = GetDescendantBaronyTitles(crown, FeudalHierarchyMode.DeJure)
                .Where(t => t != null && t.IsActive && !string.IsNullOrWhiteSpace(t.CapitalSettlementId))
                .Select(t => t.CapitalSettlementId).Distinct().ToList();
            return CrownPartitionResidence.Select(parentRealm, crown.CapitalSettlementId, ids,
                ids.Select(id => ResolveSettlement(id)?.Town));
        }

        private Kingdom CreateCrownPartitionRealmAtResidence(Kingdom parentRealm, Clan heirClan,
            FeudalTitleRecord crown, TextObject encyclopediaText, CrownPartitionPromotionRecord promotion = null)
        {
            if (heirClan == null || heirClan.Kingdom != parentRealm || crown == null
                || crown.DeJureHolderClanId != heirClan.StringId || crown.DeFactoHolderClanId != heirClan.StringId)
                return null;
            Town residence = ResolveCrownPartitionResidence(parentRealm, crown);
            if (residence == null) return null;
            return CreateIndependentRealmAtResidence(parentRealm, heirClan, crown,
                "bc_partition_indep", encyclopediaText, residence.Settlement, promotion);
        }

        internal bool TryPlanCrownPartitionAllegiance(Kingdom parentRealm, Clan heirClan,
            FeudalTitleRecord crown, out CrownPartitionAllegiancePlan plan, out string reason)
        {
            plan = null;
            reason = "missing permanent parent realm, successor house or Crown";
            if (!IsValidPermanentKingdom(parentRealm) || parentRealm.RulingClan == null
                || heirClan == null || heirClan.IsEliminated || heirClan.Kingdom != parentRealm
                || heirClan == parentRealm.RulingClan || crown == null) return false;
            EnsureCollectionsInitialized();
            var houses = parentRealm.Clans.Where(c => c != null && !c.IsEliminated
                && !c.IsUnderMercenaryService && !c.IsBanditFaction
                && !NobleClanEligibilityHelper.IsNonPlayerMinorClan(c)).ToList();
            var holdings = houses.ToDictionary(c => c.StringId, c => c.Settlements
                .Where(s => s?.Town != null && !FiefDeliberationBehavior.IsAwaitingAllocation(s))
                .Select(s => s.StringId).ToArray(), StringComparer.Ordinal);
            return CrownPartitionAllegiancePlan.TryCreate(crown.TitleId, parentRealm.RulingClan.StringId,
                heirClan.StringId, holdings, _titlesById.Values.ToList(), out plan, out reason);
        }

        private Kingdom CreateIndependentRealmAtResidence(Kingdom parentKingdom, Clan recipientClan,
            FeudalTitleRecord sourceTitle, string kingdomIdPrefix, TextObject encyclopediaText, Settlement capital,
            CrownPartitionPromotionRecord promotion = null)
        {
            string baseId = $"{kingdomIdPrefix}_{SanitizeTitleIdSeed(sourceTitle.TitleId)}_{SanitizeTitleIdSeed(recipientClan.StringId)}";
            string kingdomId = promotion?.SuccessorId ?? baseId;
            if (promotion != null && (!promotion.RealmCreationStarted || promotion.Successor != null
                || string.IsNullOrWhiteSpace(kingdomId) || Kingdom.All.Any(k => k.StringId == kingdomId))) return null;
            int suffix = 0;
            while (Kingdom.All.Any(existingKingdom => existingKingdom != null && existingKingdom.StringId == kingdomId))
                kingdomId = baseId + "_" + (++suffix);

            Kingdom kingdom = KingdomCreationSafetyHelper.CreateKingdom(kingdomId, recipientClan);
            if (kingdom == null)
                return null;
            if (promotion != null) promotion.Successor = kingdom;

            TextObject name = new TextObject("{=!}" + FeudalTitleDisplayHelper.FormatTitleName(sourceTitle, recipientClan));
            TextObject rulerTitle = new TextObject("{=!}" + FeudalTitleDisplayHelper.ResolveDisplayTitle(sourceTitle, recipientClan.Leader?.IsFemale == true, recipientClan));
            encyclopediaText = encyclopediaText ?? new TextObject("{=BC_TitleGrant_IndependentRealmText}Created by a sovereign grant from {PARENT_KINGDOM}, the {NEW_KINGDOM} entered the world as an independent realm.");
            encyclopediaText.SetTextVariable("PARENT_KINGDOM", parentKingdom?.Name ?? new TextObject("{=BC_Resolution_FormerRealm}a former realm"));
            encyclopediaText.SetTextVariable("NEW_KINGDOM", name);

            var visuals = KingdomVisualHelper.ResolveIndependentSuccessorKingdomVisuals(parentKingdom, recipientClan, kingdomId);

            kingdom.InitializeKingdom(
                name,
                name,
                recipientClan.Culture ?? parentKingdom?.Culture,
                visuals.Banner,
                visuals.PrimaryColor,
                visuals.SecondaryColor,
                capital,
                encyclopediaText,
                name,
                rulerTitle);
            KingdomVisualHelper.ApplyKingdomPalette(kingdom, visuals);
            kingdom.RulingClan = recipientClan;
            Campaign.Current?.GetCampaignBehavior<FactionManagerBehavior>()?.ClearTemporaryKingdomRepair(kingdom);
            if (promotion != null) promotion.RealmInitializationCompleted = true;
            return kingdom;
        }

        internal bool TryPrepareCrownPromotionRealm(CrownPartitionPromotionRecord journal, out string reason)
        {
            reason = "promotion founder or delivered Crown is not verified";
            if (journal == null || journal.Completed || !journal.FounderVerified || !journal.FounderInitializationCompleted
                || !journal.HasVerifiedEstateReceipts()) return false;
            var existing = Kingdom.All.Where(k => k.StringId == journal.SuccessorId).ToList();
            if (journal.RealmCreationStarted)
            {
                if (existing.Count == 1 && journal.TryRecoverRealm(existing[0])) { reason = null; return true; }
                reason = "successor shell creation is partial or inconsistent; native initialization will not be replayed";
                return false;
            }
            if (existing.Count != 0 || !IsValidPermanentKingdom(journal.Parent)
                || journal.Parent.RulingClan != journal.RetainedHouse || journal.Founder?.Kingdom != journal.Parent
                || !journal.TryRecoverFounder(journal.Founder)
                || !KingdomCreationSafetyHelper.IsValidFounder(journal.Founder)) return false;
            var crown = GetTitle(journal.CrownId);
            var residence = ResolveCrownPartitionResidence(journal.Parent, crown);
            if (crown == null || crown.DeJureHolderClanId != journal.FounderId || crown.DeFactoHolderClanId != journal.FounderId
                || residence?.Settlement != journal.Residence || !journal.TryBeginRealmCreation()) return false;
            try
            {
                var text = new TextObject("{=BC_PartitionSuccession_IndependentRealmText}Divided from {PARENT_KINGDOM} by dynastic succession, {NEW_KINGDOM} passed to a coequal blood heir as an independent realm.");
                var created = CreateCrownPartitionRealmAtResidence(journal.Parent, journal.Founder, crown, text, journal);
                if (journal.TryRecoverRealm(created)) { reason = null; return true; }
                reason = "successor shell initialization did not verify";
            }
            catch (Exception ex)
            {
                reason = "successor shell initialization interrupted: " + ex.Message;
                BellumCivileLogger.Log($"Crown partition {journal.CrownId}: {ex}");
            }
            journal.PendingReason = reason;
            return false;
        }

        internal bool IsLandlessHistoricalCrown(FeudalTitleRecord title)
        {
            if (title?.IsActive != true || title.TitleType < FeudalTitleType.Kingdom
                || string.IsNullOrWhiteSpace(title.AssociatedKingdomId)
                || Kingdom.All.FirstOrDefault(k => k.StringId == title.AssociatedKingdomId)?.IsEliminated != true)
                return false;
            if (Kingdom.All.Any(k => !k.IsEliminated && GetKingdomPoliticalTitle(k)?.TitleId == title.TitleId))
                return false;
            // A retained dignity from a destroyed realm is inheritable property when
            // no actual landed government remains beneath its political hierarchy.
            return !GetTitleAndDescendants(title, FeudalHierarchyMode.DeFacto).Any(t =>
            {
                if (!t.IsActive || t.TitleType != FeudalTitleType.Barony) return false;
                Settlement fief = Settlement.Find(t.CapitalSettlementId);
                return fief == null || fief.OwnerClan?.StringId == t.DeFactoHolderClanId;
            });
        }

        internal bool CanReceiveForeignSovereignInheritance(Clan recipient, FeudalTitleRecord title) =>
            recipient?.IsEliminated == false && recipient.Leader?.IsAlive == true
            && recipient.Kingdom != null && !recipient.Kingdom.IsEliminated
            && recipient.Kingdom.RulingClan == recipient
            && !BellumKingdomVisibilityHelper.IsTemporaryBellumKingdom(recipient.Kingdom)
            && title?.IsActive == true
            && !Kingdom.All.Any(k => !k.IsEliminated && k != recipient.Kingdom
                && GetKingdomPoliticalTitle(k)?.TitleId == title.TitleId);

        internal bool TryIntegrateForeignSovereignInheritance(Kingdom sourceRealm, Clan recipient,
            FeudalTitleRecord title, out string failure)
        {
            failure = "recipient realm or inherited title unavailable";
            if (!CanReceiveForeignSovereignInheritance(recipient, title)) return false;
            if (title.DeJureHolderClanId != recipient.StringId || title.DeFactoHolderClanId != recipient.StringId)
            { failure = "recipient no longer fully controls the inherited Crown"; return false; }
            Kingdom destination = recipient.Kingdom;
            // Inheriting an additional title must never replace either realm's reigning house.
            if (Kingdom.All.Any(k => !k.IsEliminated && k != destination
                && GetKingdomPoliticalTitle(k)?.TitleId == title.TitleId))
            { failure = "title is still the active political Crown of another realm"; return false; }
            if (sourceRealm != null && sourceRealm != destination)
                MoveGrantedTitleClusterToIndependentRealm(sourceRealm, destination, recipient, title,
                    requireExclusiveCluster: true, serviceReason: "foreign sovereign inheritance", appointRuler: false,
                    preserveLegalOrigins: true);
            RebuildRuntimeIndexes();
            ReconcileAllDeFactoParents("foreign sovereign inheritance");
            RebuildRuntimeIndexes();
            QueueServiceReviewForTitleAndChildren(title, "foreign sovereign inheritance");
            failure = null;
            return true;
        }

        private void MoveGrantedTitleClusterToIndependentRealm(
            Kingdom oldKingdom,
            Kingdom newKingdom,
            Clan recipientClan,
            FeudalTitleRecord sourceTitle,
            bool requireExclusiveCluster = false,
            string serviceReason = "royal title grant independent cluster",
            bool appointRuler = true,
            bool preserveLegalOrigins = false)
        {
            if (oldKingdom == null || newKingdom == null || recipientClan == null || sourceTitle == null)
                return;

            List<FeudalTitleRecord> clusterTitles = GetTitleAndDescendants(sourceTitle, FeudalHierarchyMode.DeFacto)
                .Where(title => title != null && title.IsActive)
                .ToList();
            HashSet<string> clusterTitleIds = new HashSet<string>(clusterTitles.Select(title => title.TitleId));
            HashSet<string> clusterSettlementIds = new HashSet<string>(clusterTitles
                .Where(title => !string.IsNullOrWhiteSpace(title.CapitalSettlementId))
                .Select(title => title.CapitalSettlementId));
            HashSet<Clan> clansToMove = new HashSet<Clan>(
                clusterTitles
                    .Select(title => ResolveClan(title.DeFactoHolderClanId))
                    .Where(clan => clan != null
                        && !clan.IsEliminated
                        && clan.Kingdom == oldKingdom
                        && clan != oldKingdom.RulingClan
                        && (!requireExclusiveCluster || ClanBelongsEntirelyToTitleCluster(clan, clusterTitleIds, clusterSettlementIds))));
            clansToMove.Add(recipientClan);

            FactionManagerBehavior factionManager = Campaign.Current?.GetCampaignBehavior<FactionManagerBehavior>();
            Action moveAction = () =>
            {
                if (recipientClan.Kingdom == oldKingdom)
                    CourtPoliticalPositionBehavior.MoveToSuccessorRealm(recipientClan, oldKingdom, newKingdom,
                        () => KingdomVisualHelper.ApplyJoinToKingdomPreservingCustomBanner(recipientClan, newKingdom, showNotification: false));
                if (appointRuler) newKingdom.RulingClan = recipientClan;

                foreach (Clan clan in clansToMove.Where(clan => clan != recipientClan).ToList())
                {
                    if (clan.Kingdom == oldKingdom)
                        CourtPoliticalPositionBehavior.MoveToSuccessorRealm(clan, oldKingdom, newKingdom,
                            () => KingdomVisualHelper.ApplyJoinToKingdomPreservingCustomBanner(clan, newKingdom, showNotification: false));
                }

                if (appointRuler) newKingdom.RulingClan = recipientClan;
            };

            if (factionManager != null)
                factionManager.RunWithRulerRepairSuppressed(oldKingdom, moveAction);
            else
                moveAction();

            foreach (FeudalTitleRecord clusterTitle in clusterTitles)
            {
                Clan holder = ResolveClan(clusterTitle.DeFactoHolderClanId);
                if (holder != null && holder.Kingdom == newKingdom)
                {
                    if (!preserveLegalOrigins) clusterTitle.SetAssociatedKingdom(newKingdom.StringId);
                    clusterTitle.MarkSynced(CurrentDay);
                    QueueServiceReview(clusterTitle, serviceReason);
                }
            }
        }

        private bool ClanBelongsEntirelyToTitleCluster(
            Clan clan,
            HashSet<string> clusterTitleIds,
            HashSet<string> clusterSettlementIds)
        {
            if (clan == null || clusterTitleIds == null || clusterSettlementIds == null)
                return false;

            bool hasClusterLand = clan.Settlements.Any(settlement => settlement != null && clusterSettlementIds.Contains(settlement.StringId));
            if (!hasClusterLand
                || clan.Settlements.Any(settlement => settlement != null && !clusterSettlementIds.Contains(settlement.StringId)))
            {
                return false;
            }

            return GetTitlesHeldByClan(clan, deJure: false)
                .Where(title => title != null && title.IsActive)
                .All(title => clusterTitleIds.Contains(title.TitleId));
        }

        private void RemoveClaimsForClanToTitle(Clan clan, FeudalTitleRecord title)
        {
            if (clan == null || title == null || string.IsNullOrWhiteSpace(clan.StringId) || string.IsNullOrWhiteSpace(title.TitleId))
                return;

            _claims.RemoveAll(claim => claim != null
                && claim.ClaimantClanId == clan.StringId
                && claim.TargetTitleId == title.TitleId);
        }

        private List<FeudalTitleRecord> GetEligibleFormationChildren(
            Clan clan,
            FeudalTitleType childType,
            bool allowReorganization = false)
        {
            if (clan == null || string.IsNullOrWhiteSpace(clan.StringId))
                return new List<FeudalTitleRecord>();

            return _titlesById.Values
                .Where(title => IsEligibleFormationChild(title, clan, childType, allowReorganization))
                .ToList();
        }

        private bool IsEligibleFormationChild(
            FeudalTitleRecord title,
            Clan clan,
            FeudalTitleType childType,
            bool allowReorganization = false)
        {
            if (title == null || clan == null || !title.IsActive)
                return false;

            if (title.TitleType != childType)
                return false;

            FeudalTitleType targetType = GetParentTypeForChild(childType);
            FeudalTitleRecord deJureParent = GetTitle(title.ParentTitleId);
            FeudalTitleRecord deFactoParent = GetTitle(title.DeFactoParentTitleId);
            bool hasImmediateParent = IsActiveFormationParent(deJureParent, targetType)
                || IsActiveFormationParent(deFactoParent, targetType);

            if (!allowReorganization)
            {
                return string.Equals(title.DeFactoHolderClanId, clan.StringId, StringComparison.Ordinal)
                    && !hasImmediateParent;
            }

            if (HasStructuralFeud(title))
                return false;

            if (IsFullyHeldByClan(title, clan))
                return true;

            if (!HasSovereignReorganizationAuthority(clan, out _))
                return false;

            Clan holder = ResolveClan(title.DeFactoHolderClanId);
            if (holder == null
                || holder == clan
                || holder.IsEliminated
                || holder.IsUnderMercenaryService
                || holder.Kingdom == null
                || holder.Kingdom != clan.Kingdom
                || !IsFullyHeldByClan(title, holder))
            {
                return false;
            }

            List<FeudalTitleRecord> immediateParents = GetImmediateFormationParents(title, targetType);
            return immediateParents.Count > 0
                && immediateParents.All(parent => IsFullyHeldByClan(parent, clan));
        }

        private FeudalTitleFormationBlockReason GetFormationChildBlockReason(
            FeudalTitleRecord title,
            Clan clan,
            FeudalTitleType childType,
            bool allowReorganization)
        {
            if (title == null || clan == null || !title.IsActive || title.IsDeliberatelyDissolved || title.TitleType != childType)
                return FeudalTitleFormationBlockReason.InvalidCluster;

            if (HasStructuralFeud(title))
                return FeudalTitleFormationBlockReason.SeatDisputed;

            if (IsFullyHeldByClan(title, clan))
                return FeudalTitleFormationBlockReason.None;

            FeudalTitleType targetType = GetParentTypeForChild(childType);
            List<FeudalTitleRecord> immediateParents = GetImmediateFormationParents(title, targetType);
            if (!allowReorganization)
            {
                return immediateParents.Count > 0
                    ? FeudalTitleFormationBlockReason.ExistingHigherTitle
                    : FeudalTitleFormationBlockReason.UnauthorizedHolder;
            }

            if (!HasSovereignReorganizationAuthority(clan, out _))
                return FeudalTitleFormationBlockReason.NoReorganizationAuthority;

            Clan holder = ResolveClan(title.DeFactoHolderClanId);
            if (holder == null
                || holder == clan
                || holder.IsEliminated
                || holder.IsUnderMercenaryService
                || holder.Kingdom == null
                || holder.Kingdom != clan.Kingdom
                || !IsFullyHeldByClan(title, holder))
            {
                return FeudalTitleFormationBlockReason.IneligibleVassal;
            }

            return immediateParents.Count == 0
                || immediateParents.Any(parent => !IsFullyHeldByClan(parent, clan))
                ? FeudalTitleFormationBlockReason.ParentNotControlled
                : FeudalTitleFormationBlockReason.InvalidCluster;
        }

        private static FeudalTitleFormationBlockReason SelectFormationBlockReason(
            IEnumerable<FeudalTitleFormationBlockReason> failures)
        {
            HashSet<FeudalTitleFormationBlockReason> available = new HashSet<FeudalTitleFormationBlockReason>(
                failures?.Where(reason => reason != FeudalTitleFormationBlockReason.None)
                ?? Enumerable.Empty<FeudalTitleFormationBlockReason>());
            FeudalTitleFormationBlockReason[] priority =
            {
                FeudalTitleFormationBlockReason.ParentDisputed,
                FeudalTitleFormationBlockReason.ConflictingParents,
                FeudalTitleFormationBlockReason.NoReorganizationAuthority,
                FeudalTitleFormationBlockReason.IneligibleVassal,
                FeudalTitleFormationBlockReason.UnauthorizedHolder,
                FeudalTitleFormationBlockReason.WouldEmptyParent,
                FeudalTitleFormationBlockReason.SplitVassalEstate,
                FeudalTitleFormationBlockReason.ExistingHigherTitle,
                FeudalTitleFormationBlockReason.InvalidCluster
            };

            return priority.FirstOrDefault(available.Contains);
        }

        private static string GetFormationTechnicalReason(FeudalTitleFormationBlockReason blockReason)
        {
            switch (blockReason)
            {
                case FeudalTitleFormationBlockReason.ParentDisputed:
                    return "a source parent title is involved in an active claim or dispute";
                case FeudalTitleFormationBlockReason.ConflictingParents:
                    return "a selected title has conflicting immediate parents";
                case FeudalTitleFormationBlockReason.NoReorganizationAuthority:
                    return "you lack authority to detach a selected title from its current parent";
                case FeudalTitleFormationBlockReason.IneligibleVassal:
                    return "a selected vassal title is not held by your direct same-realm vassal";
                case FeudalTitleFormationBlockReason.UnauthorizedHolder:
                    return "a selected title is not fully held by an authorized clan";
                case FeudalTitleFormationBlockReason.WouldEmptyParent:
                    return "formation would leave an existing parent title without subordinate titles";
                case FeudalTitleFormationBlockReason.SplitVassalEstate:
                    return "a selected vassal holds lands or titles outside the proposed sovereign title";
                case FeudalTitleFormationBlockReason.ExistingHigherTitle:
                    return "an active title of that rank already governs one or more selected titles";
                default:
                    return "the selected title cluster is not eligible";
            }
        }

        private static bool IsActiveFormationParent(FeudalTitleRecord title, FeudalTitleType targetType)
        {
            return title != null && title.IsActive && title.TitleType == targetType;
        }

        private List<FeudalTitleRecord> GetImmediateFormationParents(FeudalTitleRecord child, FeudalTitleType targetType)
        {
            if (child == null)
                return new List<FeudalTitleRecord>();

            return new[] { GetTitle(child.ParentTitleId), GetTitle(child.DeFactoParentTitleId) }
                .Where(parent => IsActiveFormationParent(parent, targetType))
                .GroupBy(parent => parent.TitleId)
                .Select(group => group.First())
                .ToList();
        }

        private bool HasSovereignReorganizationAuthority(Clan clan, out FeudalTitleRecord sovereignTitle)
        {
            sovereignTitle = null;
            Kingdom kingdom = clan?.Kingdom;
            if (!IsValidPermanentKingdom(kingdom)
                || IsTemporaryRebelKingdom(kingdom)
                || kingdom.RulingClan != clan)
            {
                return false;
            }

            sovereignTitle = GetRealmSovereignTitle(kingdom, FeudalHierarchyMode.DeFacto)
                ?? GetKingdomPoliticalTitle(kingdom);
            return IsFullyHeldByClan(sovereignTitle, clan);
        }

        private bool IsFormationParentRetained(
            FeudalTitleRecord parent,
            HashSet<string> selectedTitleIds,
            FeudalHierarchyMode mode)
        {
            if (parent == null || !parent.IsActive)
                return true;

            return _titlesById.Values.Any(title =>
                title != null
                && title.IsActive
                && !selectedTitleIds.Contains(title.TitleId)
                && string.Equals(
                    mode == FeudalHierarchyMode.DeFacto ? title.DeFactoParentTitleId : title.ParentTitleId,
                    parent.TitleId,
                    StringComparison.Ordinal));
        }

        private bool IsFormationClusterExclusiveToClan(
            Clan holder,
            IEnumerable<FeudalTitleRecord> selectedTitles)
        {
            if (holder == null || selectedTitles == null)
                return false;

            HashSet<string> clusterTitleIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (FeudalTitleRecord selectedTitle in selectedTitles.Where(title => title != null))
            {
                foreach (FeudalTitleRecord clusterTitle in GetTitleAndDescendants(selectedTitle, FeudalHierarchyMode.DeFacto))
                {
                    if (clusterTitle != null && clusterTitle.IsActive)
                        clusterTitleIds.Add(clusterTitle.TitleId);
                }
            }

            HashSet<string> clusterSettlementIds = new HashSet<string>(
                clusterTitleIds
                    .Select(GetTitle)
                    .Where(title => title != null && !string.IsNullOrWhiteSpace(title.CapitalSettlementId))
                    .Select(title => title.CapitalSettlementId),
                StringComparer.Ordinal);
            return ClanBelongsEntirelyToTitleCluster(holder, clusterTitleIds, clusterSettlementIds);
        }

        private bool HasFullyHeldHigherTitle(Clan clan, FeudalTitleType titleType)
        {
            return GetTitlesHeldByClan(clan, deJure: false)
                .Any(title => title != null
                    && title.IsActive
                    && title.TitleType > titleType
                    && IsFullyHeldByClan(title, clan));
        }

        private List<FeudalTitleFormationCandidate> BuildFormationCandidates(
            Clan clan,
            FeudalTitleType targetType,
            List<FeudalTitleRecord> eligibleChildren,
            bool allowReorganization = false,
            string diagnosticTitleId = null,
            List<FeudalTitleFormationBlockReason> diagnosticFailures = null)
        {
            int minimumChildren = GetMinimumChildrenForFormation();
            if (clan == null || eligibleChildren == null || eligibleChildren.Count < minimumChildren)
                return new List<FeudalTitleFormationCandidate>();

            List<FeudalTitleFormationCandidate> candidates = new List<FeudalTitleFormationCandidate>();
            foreach (List<FeudalTitleRecord> component in BuildConnectedComponents(eligibleChildren))
            {
                if (component.Count < minimumChildren)
                    continue;

                if (TryBuildCandidateFromCluster(
                    clan,
                    targetType,
                    component,
                    allowReorganization,
                    out FeudalTitleFormationCandidate fullClusterCandidate,
                    out FeudalTitleFormationBlockReason fullClusterFailure,
                    out _))
                {
                    candidates.Add(fullClusterCandidate);
                    continue;
                }

                if (diagnosticFailures != null
                    && (string.IsNullOrWhiteSpace(diagnosticTitleId)
                        || component.Any(title => title.TitleId == diagnosticTitleId)))
                {
                    diagnosticFailures.Add(fullClusterFailure);
                }

                foreach (List<FeudalTitleRecord> subset in FindValidFormationSubsets(clan, targetType, component))
                {
                    if (TryBuildCandidateFromCluster(
                        clan,
                        targetType,
                        subset,
                        allowReorganization,
                        out FeudalTitleFormationCandidate subsetCandidate,
                        out FeudalTitleFormationBlockReason subsetFailure,
                        out _))
                    {
                        candidates.Add(subsetCandidate);
                    }
                    else if (diagnosticFailures != null
                        && (string.IsNullOrWhiteSpace(diagnosticTitleId)
                            || subset.Any(title => title.TitleId == diagnosticTitleId)))
                    {
                        diagnosticFailures.Add(subsetFailure);
                    }
                }
            }

            return candidates
                .GroupBy(candidate => string.Join("|", candidate.ChildTitleIds.OrderBy(id => id)))
                .Select(group => group
                    .OrderByDescending(candidate => candidate.ChildTitleIds.Count)
                    .ThenByDescending(candidate => candidate.ProsperityScore)
                    .First())
                .OrderByDescending(candidate => candidate.ChildTitleIds.Count)
                .ThenByDescending(candidate => candidate.ProsperityScore)
                .ToList();
        }

        private List<List<FeudalTitleRecord>> BuildConnectedComponents(List<FeudalTitleRecord> titles)
        {
            List<List<FeudalTitleRecord>> components = new List<List<FeudalTitleRecord>>();
            HashSet<string> visited = new HashSet<string>();

            foreach (FeudalTitleRecord seed in titles.OrderBy(title => title.TitleId))
            {
                if (seed == null || visited.Contains(seed.TitleId))
                    continue;

                List<FeudalTitleRecord> component = new List<FeudalTitleRecord>();
                Queue<FeudalTitleRecord> queue = new Queue<FeudalTitleRecord>();
                queue.Enqueue(seed);
                visited.Add(seed.TitleId);

                while (queue.Count > 0)
                {
                    FeudalTitleRecord current = queue.Dequeue();
                    component.Add(current);

                    foreach (FeudalTitleRecord candidate in titles)
                    {
                        if (candidate == null || visited.Contains(candidate.TitleId))
                            continue;

                        if (!AreTitlesAdjacent(current, candidate))
                            continue;

                        visited.Add(candidate.TitleId);
                        queue.Enqueue(candidate);
                    }
                }

                components.Add(component);
            }

            return components;
        }

        private IEnumerable<List<FeudalTitleRecord>> FindValidFormationSubsets(Clan clan, FeudalTitleType targetType, List<FeudalTitleRecord> component)
        {
            List<FeudalTitleRecord> ordered = component
                .OrderByDescending(title => IsFullyHeldByClan(title, clan))
                .ThenByDescending(GetTitleProsperityScore)
                .ThenBy(title => title.TitleId)
                .ToList();

            if (ordered.Count <= 12)
            {
                int maxMask = 1 << ordered.Count;
                for (int mask = 1; mask < maxMask; mask++)
                {
                    if (CountBits(mask) < GetMinimumChildrenForFormation())
                        continue;

                    List<FeudalTitleRecord> subset = new List<FeudalTitleRecord>();
                    for (int i = 0; i < ordered.Count; i++)
                    {
                        if ((mask & (1 << i)) != 0)
                            subset.Add(ordered[i]);
                    }

                    if (IsConnectedSubset(subset) && HasLawfulFormationSeed(clan, subset))
                        yield return subset;
                }

                yield break;
            }

            foreach (FeudalTitleRecord seed in ordered)
            {
                List<FeudalTitleRecord> subset = new List<FeudalTitleRecord> { seed };
                bool expanded;
                do
                {
                    expanded = false;
                    FeudalTitleRecord next = ordered
                        .Where(title => !subset.Contains(title) && subset.Any(existing => AreTitlesAdjacent(existing, title)))
                        .OrderByDescending(title => IsFullyHeldByClan(title, clan))
                        .ThenByDescending(GetTitleProsperityScore)
                        .ThenBy(title => title.TitleId)
                        .FirstOrDefault();

                    if (next != null)
                    {
                        subset.Add(next);
                        expanded = true;
                        if (subset.Count >= GetMinimumChildrenForFormation() && HasLawfulFormationSeed(clan, subset))
                            yield return subset.ToList();
                    }
                }
                while (expanded);
            }
        }

        private bool TryBuildCandidateFromCluster(
            Clan clan,
            FeudalTitleType targetType,
            List<FeudalTitleRecord> cluster,
            bool allowReorganization,
            out FeudalTitleFormationCandidate candidate,
            out FeudalTitleFormationBlockReason blockReason,
            out string reason)
        {
            candidate = null;
            blockReason = FeudalTitleFormationBlockReason.None;
            reason = null;
            if (clan == null || cluster == null || cluster.Count < GetMinimumChildrenForFormation())
            {
                blockReason = FeudalTitleFormationBlockReason.InsufficientContiguousTitles;
                reason = "too few subordinate titles selected";
                return false;
            }

            if (!IsConnectedSubset(cluster) || !HasLawfulFormationSeed(clan, cluster))
            {
                blockReason = FeudalTitleFormationBlockReason.InvalidCluster;
                reason = "selected titles are not contiguous or contain no lawful seed";
                return false;
            }

            FeudalTitleType childType = GetChildTypeForFormation(targetType);
            if (cluster.Any(title => !IsEligibleFormationChild(title, clan, childType, allowReorganization)))
            {
                blockReason = FeudalTitleFormationBlockReason.InvalidCluster;
                reason = "one or more selected titles are no longer eligible";
                return false;
            }

            bool hasSovereignAuthority = HasSovereignReorganizationAuthority(clan, out FeudalTitleRecord sovereignTitle);
            HashSet<string> selectedTitleIds = new HashSet<string>(cluster.Select(title => title.TitleId), StringComparer.Ordinal);
            Dictionary<string, FeudalTitleRecord> affectedParents = new Dictionary<string, FeudalTitleRecord>(StringComparer.Ordinal);
            HashSet<string> vassalClanIds = new HashSet<string>(StringComparer.Ordinal);
            FeudalTitleFormationMode mode = FeudalTitleFormationMode.Consolidation;
            List<FeudalTitleFormationMove> moves = new List<FeudalTitleFormationMove>();

            foreach (FeudalTitleRecord child in cluster)
            {
                List<FeudalTitleRecord> immediateParents = GetImmediateFormationParents(child, targetType);
                if (immediateParents.Count > 1)
                {
                    blockReason = FeudalTitleFormationBlockReason.ConflictingParents;
                    reason = "a selected title has conflicting immediate parents";
                    return false;
                }

                Clan holder = ResolveClan(child.DeFactoHolderClanId);
                bool personallyHeld = IsFullyHeldByClan(child, clan);
                bool vassalHeld = holder != null && holder != clan && IsFullyHeldByClan(child, holder);

                if (!personallyHeld && !vassalHeld)
                {
                    blockReason = FeudalTitleFormationBlockReason.UnauthorizedHolder;
                    reason = "a selected title is not fully held by an authorized clan";
                    return false;
                }

                if (vassalHeld)
                {
                    if (!hasSovereignAuthority
                        || holder.Kingdom != clan.Kingdom
                        || holder.IsEliminated
                        || holder.IsUnderMercenaryService
                        || immediateParents.Count == 0
                        || immediateParents.Any(parent => !IsFullyHeldByClan(parent, clan)))
                    {
                        blockReason = FeudalTitleFormationBlockReason.IneligibleVassal;
                        reason = "a selected vassal title is not held by your direct same-realm vassal";
                        return false;
                    }

                    mode = FeudalTitleFormationMode.SovereignReorganization;
                    vassalClanIds.Add(holder.StringId);
                }
                else if (immediateParents.Count > 0)
                {
                    if (!allowReorganization)
                    {
                        blockReason = FeudalTitleFormationBlockReason.ExistingHigherTitle;
                        reason = "an active title of that rank already governs one or more selected titles";
                        return false;
                    }

                    bool parentsPersonallyHeld = immediateParents.All(parent => IsFullyHeldByClan(parent, clan));
                    if (!parentsPersonallyHeld && !hasSovereignAuthority)
                    {
                        blockReason = FeudalTitleFormationBlockReason.NoReorganizationAuthority;
                        reason = "you lack authority to detach a selected title from its current parent";
                        return false;
                    }

                    mode = parentsPersonallyHeld && mode != FeudalTitleFormationMode.SovereignReorganization
                        ? FeudalTitleFormationMode.PersonalReorganization
                        : FeudalTitleFormationMode.SovereignReorganization;
                }

                foreach (FeudalTitleRecord parent in immediateParents)
                    affectedParents[parent.TitleId] = parent;

                moves.Add(new FeudalTitleFormationMove
                {
                    ChildTitleId = child.TitleId,
                    HolderClanId = holder?.StringId ?? string.Empty,
                    OldDeJureParentTitleId = child.ParentTitleId,
                    OldDeFactoParentTitleId = child.DeFactoParentTitleId,
                    ReparentDeJure = personallyHeld || vassalHeld,
                    ReparentDeFacto = true,
                    IsVassalTitle = vassalHeld
                });
            }

            if (affectedParents.Values.Any(IsFeudTarget))
            {
                blockReason = FeudalTitleFormationBlockReason.ParentDisputed;
                reason = "a source parent title is involved in an active claim or dispute";
                return false;
            }

            foreach (FeudalTitleRecord parent in affectedParents.Values)
            {
                bool losesDeJureChildren = cluster.Any(child =>
                    string.Equals(child.ParentTitleId, parent.TitleId, StringComparison.Ordinal));
                bool losesDeFactoChildren = cluster.Any(child =>
                    string.Equals(child.DeFactoParentTitleId, parent.TitleId, StringComparison.Ordinal));
                if ((losesDeJureChildren && !IsFormationParentRetained(parent, selectedTitleIds, FeudalHierarchyMode.DeJure))
                    || (losesDeFactoChildren && !IsFormationParentRetained(parent, selectedTitleIds, FeudalHierarchyMode.DeFacto)))
                {
                    blockReason = FeudalTitleFormationBlockReason.WouldEmptyParent;
                    reason = "formation would leave an existing parent title without subordinate titles";
                    return false;
                }
            }

            bool createsCoequalSovereignTitle = hasSovereignAuthority
                && sovereignTitle != null
                && sovereignTitle.TitleType == targetType;
            if (createsCoequalSovereignTitle)
            {
                mode = FeudalTitleFormationMode.SovereignReorganization;
                foreach (string vassalClanId in vassalClanIds)
                {
                    Clan vassal = ResolveClan(vassalClanId);
                    List<FeudalTitleRecord> vassalRoots = cluster
                        .Where(child => string.Equals(child.DeFactoHolderClanId, vassalClanId, StringComparison.Ordinal))
                        .ToList();
                    if (!IsFormationClusterExclusiveToClan(vassal, vassalRoots))
                    {
                        blockReason = FeudalTitleFormationBlockReason.SplitVassalEstate;
                        reason = "a selected vassal holds lands or titles outside the proposed sovereign title";
                        return false;
                    }
                }
            }

            FeudalTitleRecord capitalTitle = cluster
                .OrderByDescending(GetTitleProsperityScore)
                .ThenBy(title => title.TitleId)
                .FirstOrDefault();

            string capitalSettlementId = ResolveCapitalSettlementId(capitalTitle);
            string capitalName = ResolveSettlement(capitalSettlementId)?.Name?.ToString()
                ?? capitalTitle?.Name
                ?? targetType.ToString();

            int deJureHeld = cluster.Count(title => IsFullyHeldByClan(title, clan));
            int requiredDeJure = 1;

            candidate = new FeudalTitleFormationCandidate
            {
                TargetType = targetType,
                Mode = mode,
                Name = BuildTitleRootName(targetType, capitalName),
                CapitalSettlementId = capitalSettlementId,
                SeedTitleId = cluster
                    .Where(title => IsFullyHeldByClan(title, clan))
                    .OrderByDescending(GetTitleProsperityScore)
                    .ThenBy(title => title.TitleId)
                    .Select(title => title.TitleId)
                    .FirstOrDefault() ?? string.Empty,
                DeJureHeldChildren = deJureHeld,
                RequiredDeJureChildren = requiredDeJure,
                GoldCost = GetFormationGoldCost(targetType),
                InfluenceReward = GetFormationInfluenceReward(targetType),
                ProsperityScore = cluster.Sum(GetTitleProsperityScore),
                CreatesCoequalSovereignTitle = createsCoequalSovereignTitle,
                HasIndependentSuccessionRisk = createsCoequalSovereignTitle
                    && !HasFullyHeldHigherTitle(clan, targetType)
            };

            foreach (FeudalTitleRecord child in cluster.OrderBy(title => title.TitleId))
                candidate.ChildTitleIds.Add(child.TitleId);
            candidate.Moves.AddRange(moves.OrderBy(move => move.ChildTitleId));
            candidate.AffectedParentTitleIds.AddRange(affectedParents.Keys.OrderBy(id => id));
            candidate.VassalClanIds.AddRange(vassalClanIds.OrderBy(id => id));

            return true;
        }

        private static bool HasLawfulFormationSeed(Clan clan, List<FeudalTitleRecord> cluster)
        {
            if (clan == null || cluster == null || cluster.Count < GetMinimumChildrenForFormation())
                return false;

            return cluster.Any(title => IsFullyHeldByClan(title, clan));
        }

        private static bool IsFullyHeldByClan(FeudalTitleRecord title, Clan clan)
        {
            return title != null
                && clan != null
                && string.Equals(title.DeJureHolderClanId, clan.StringId, StringComparison.Ordinal)
                && string.Equals(title.DeFactoHolderClanId, clan.StringId, StringComparison.Ordinal);
        }

        private bool ShouldPromoteIndependentRealmShell(Kingdom kingdom, FeudalTitleRecord newTitle, List<string> childTitleIds)
        {
            FeudalTitleRecord currentShell = IsIndependentRealmShell(kingdom)
                ? GetRealmSovereignTitle(kingdom, FeudalHierarchyMode.DeFacto) : null;
            return currentShell != null
                && newTitle != null
                && newTitle.TitleType > currentShell.TitleType
                && childTitleIds != null
                && childTitleIds.Contains(currentShell.TitleId);
        }

        private static int GetMinimumChildrenForFormation()
        {
            return BellumCivileOptions.FeudalTitleMinimumChildren;
        }

        private static int CountBits(int value)
        {
            int count = 0;
            while (value != 0)
            {
                value &= value - 1;
                count++;
            }

            return count;
        }

        private bool IsConnectedSubset(List<FeudalTitleRecord> subset)
        {
            if (subset == null || subset.Count == 0)
                return false;

            HashSet<string> visited = new HashSet<string>();
            Queue<FeudalTitleRecord> queue = new Queue<FeudalTitleRecord>();
            queue.Enqueue(subset[0]);
            visited.Add(subset[0].TitleId);

            while (queue.Count > 0)
            {
                FeudalTitleRecord current = queue.Dequeue();
                foreach (FeudalTitleRecord candidate in subset)
                {
                    if (candidate == null || visited.Contains(candidate.TitleId))
                        continue;

                    if (!AreTitlesAdjacent(current, candidate))
                        continue;

                    visited.Add(candidate.TitleId);
                    queue.Enqueue(candidate);
                }
            }

            return visited.Count == subset.Count;
        }

        private bool AreTitlesAdjacent(FeudalTitleRecord firstTitle, FeudalTitleRecord secondTitle)
        {
            if (firstTitle == null || secondTitle == null || firstTitle.TitleId == secondTitle.TitleId)
                return false;

            string firstId = string.CompareOrdinal(firstTitle.TitleId, secondTitle.TitleId) <= 0 ? firstTitle.TitleId : secondTitle.TitleId;
            string secondId = firstId == firstTitle.TitleId ? secondTitle.TitleId : firstTitle.TitleId;
            string cacheKey = $"{firstId}|{secondId}";

            if (!_titleDistanceCache.TryGetValue(cacheKey, out float distance))
            {
                distance = CalculateTitleDistance(firstTitle, secondTitle);
                _titleDistanceCache[cacheKey] = distance;
            }

            return distance <= GetAdjacencyDistanceThreshold();
        }

        private float CalculateTitleDistance(FeudalTitleRecord firstTitle, FeudalTitleRecord secondTitle)
        {
            List<Settlement> firstSettlements = GetTitleFootprintSettlements(firstTitle);
            List<Settlement> secondSettlements = GetTitleFootprintSettlements(secondTitle);

            if (firstSettlements.Count == 0 || secondSettlements.Count == 0)
                return float.MaxValue;

            float bestDistance = float.MaxValue;
            foreach (Settlement firstSettlement in firstSettlements)
            {
                foreach (Settlement secondSettlement in secondSettlements)
                {
                    float distance = Campaign.Current.Models.MapDistanceModel.GetDistance(
                        firstSettlement,
                        secondSettlement,
                        isFromPort: false,
                        isTargetingPort: false,
                        MobileParty.NavigationType.All);

                    if (distance < bestDistance)
                        bestDistance = distance;
                }
            }

            return bestDistance;
        }

        private List<Settlement> GetTitleFootprintSettlements(FeudalTitleRecord title)
        {
            List<Settlement> settlements = new List<Settlement>();
            CollectTitleFootprintSettlements(title, settlements, new HashSet<string>());
            return settlements;
        }

        private void CollectTitleFootprintSettlements(FeudalTitleRecord title, List<Settlement> settlements, HashSet<string> visited)
        {
            if (title == null || string.IsNullOrWhiteSpace(title.TitleId) || !visited.Add(title.TitleId))
                return;

            if (title.TitleType == FeudalTitleType.Barony)
            {
                Settlement settlement = ResolveSettlement(title.CapitalSettlementId);
                if (settlement != null)
                {
                    settlements.Add(settlement);
                    foreach (Village village in settlement.BoundVillages)
                    {
                        if (village?.Settlement != null && !settlements.Contains(village.Settlement))
                            settlements.Add(village.Settlement);
                    }
                }
                return;
            }

            if (!_childrenByParentTitleId.TryGetValue(title.TitleId, out List<string> childIds))
                return;

            foreach (string childId in childIds)
                CollectTitleFootprintSettlements(GetTitle(childId), settlements, visited);
        }

        private void CollectTitleAndDescendants(
            FeudalTitleRecord title,
            List<FeudalTitleRecord> titles,
            HashSet<string> visited,
            FeudalHierarchyMode mode)
        {
            if (title == null || string.IsNullOrWhiteSpace(title.TitleId) || !title.IsActive || !visited.Add(title.TitleId))
                return;

            titles.Add(title);

            Dictionary<string, List<string>> index = mode == FeudalHierarchyMode.DeFacto
                ? _childrenByDeFactoParentTitleId
                : _childrenByParentTitleId;
            if (!index.TryGetValue(title.TitleId, out List<string> childIds))
                return;

            foreach (string childId in childIds)
                CollectTitleAndDescendants(GetTitle(childId), titles, visited, mode);
        }

        private float GetTitleProsperityScore(FeudalTitleRecord title)
        {
            return GetTitleFootprintSettlements(title)
                .Select(settlement => settlement?.Town?.Prosperity ?? 0f)
                .DefaultIfEmpty(0f)
                .Sum();
        }

        private string ResolveCapitalSettlementId(FeudalTitleRecord title)
        {
            if (title == null)
                return string.Empty;

            if (!string.IsNullOrWhiteSpace(title.CapitalSettlementId))
                return title.CapitalSettlementId;

            return GetTitleFootprintSettlements(title)
                .OrderByDescending(settlement => settlement?.Town?.Prosperity ?? 0f)
                .Select(settlement => settlement?.StringId)
                .FirstOrDefault() ?? string.Empty;
        }

        private string ResolveParentForNewTitle(Clan clan, FeudalTitleType targetType, List<string> childTitleIds)
        {
            List<FeudalTitleRecord> childTitles = childTitleIds?
                .Select(GetTitle)
                .Where(title => title != null)
                .ToList() ?? new List<FeudalTitleRecord>();

            FeudalTitleRecord parent = childTitles
                .Select(child => ResolveNearestFormationAncestor(child, clan, targetType))
                .Where(existingParent => existingParent != null)
                .GroupBy(existingParent => existingParent.TitleId)
                .OrderByDescending(group => group.Count())
                .ThenBy(group => group.First().TitleType)
                .ThenBy(group => group.Key)
                .Select(group => group.First())
                .FirstOrDefault();

            if (parent != null)
                return parent.TitleId;

            Kingdom kingdom = clan?.Kingdom;
            FeudalTitleRecord sovereignTitle = IsValidPermanentKingdom(kingdom)
                ? GetRealmSovereignTitle(kingdom, FeudalHierarchyMode.DeJure) ?? GetKingdomPoliticalTitle(kingdom)
                : null;
            return sovereignTitle != null
                && sovereignTitle.IsActive
                && sovereignTitle.TitleType > targetType
                    ? sovereignTitle.TitleId
                    : string.Empty;
        }

        private FeudalTitleRecord ResolveNearestFormationAncestor(
            FeudalTitleRecord child,
            Clan clan,
            FeudalTitleType targetType)
        {
            if (child == null || clan == null)
                return null;

            string parentId = child.ParentTitleId;
            HashSet<string> visited = new HashSet<string>(StringComparer.Ordinal);
            while (!string.IsNullOrWhiteSpace(parentId) && visited.Add(parentId))
            {
                FeudalTitleRecord parent = GetTitle(parentId);
                if (parent == null || !parent.IsActive)
                    return null;

                bool belongsToCurrentRealm = IsValidPermanentKingdom(clan.Kingdom)
                    ? string.Equals(parent.AssociatedKingdomId, clan.Kingdom.StringId, StringComparison.Ordinal)
                    : IsFullyHeldByClan(parent, clan);
                if (parent.TitleType > targetType && belongsToCurrentRealm)
                {
                    return parent;
                }

                parentId = parent.ParentTitleId;
            }

            return null;
        }

        private string ResolveKingdomLegalParentTitleId(Kingdom kingdom)
        {
            if (!IsValidPermanentKingdom(kingdom))
                return string.Empty;

            if (IsIndependentRealmShell(kingdom))
            {
                FeudalTitleRecord currentSourceTitle = GetRealmSovereignTitle(kingdom, FeudalHierarchyMode.DeJure);
                return currentSourceTitle != null && currentSourceTitle.TitleType > FeudalTitleType.Barony
                    ? currentSourceTitle.TitleId : string.Empty;
            }

            return BuildKingdomTitleId(kingdom);
        }

        private string BuildUniqueHigherTitleId(FeudalTitleType targetType, string capitalSettlementId, string clanId)
        {
            string seed = $"{SanitizeTitleIdSeed(capitalSettlementId)}_{SanitizeTitleIdSeed(clanId)}";
            string baseId = BuildHigherTitleId(targetType, seed);
            string candidate = baseId;
            int index = 1;

            while (_titlesById.ContainsKey(candidate))
            {
                candidate = $"{baseId}_{index}";
                index++;
            }

            return candidate;
        }

        private bool TryApplyJournaledCrownEstateOwnership(Settlement settlement, Clan recipient)
        {
            var journal = Campaign.Current?.GetCampaignBehavior<PartitionSuccessionBehavior>()
                ?.GetCrownEstateDeliveryInProgress(settlement?.StringId, recipient);
            if (journal == null) return false;
            var captured = journal.Titles?.SingleOrDefault(t => t != null && t.Rank == FeudalTitleType.Barony
                && t.CapitalId == settlement.StringId);
            var title = captured == null ? null : GetTitle(captured.TitleId);
            if (title?.IsActive != true || settlement.OwnerClan != recipient
                || captured.ActualHolderId != journal.RetainedHouse?.StringId
                || title.DeFactoHolderClanId != captured.ActualHolderId && title.DeFactoHolderClanId != recipient.StringId
                || title.DeJureHolderClanId != captured.LegalHolderId
                || title.ParentTitleId != captured.LegalParentId || title.DeFactoParentTitleId != captured.ActualParentId)
                throw new InvalidOperationException("Journaled Crown estate ownership callback no longer matches captured rights.");
            title.SetDeFactoHolder(recipient.StringId);
            title.MarkSynced(CurrentDay);
            RebuildRuntimeIndexes();
            QueueServiceReview(title, "journaled Crown estate possession");
            return true;
        }

        internal static string SanitizeTitleIdSeed(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "unknown";

            char[] chars = value
                .Select(ch => char.IsLetterOrDigit(ch) || ch == '_' ? ch : '_')
                .ToArray();
            return new string(chars).Trim('_').ToLowerInvariant();
        }

        private static string BuildTitleRootName(FeudalTitleType targetType, string capitalName)
        {
            string seed = CleanTitlePlaceName(capitalName);
            return string.IsNullOrWhiteSpace(seed)
                ? targetType.ToString()
                : seed;
        }

        private static int GetFormationGoldCost(FeudalTitleType targetType)
        {
            switch (targetType)
            {
                case FeudalTitleType.Barony:
                    return C.FeudalTitleBaronyCost;
                case FeudalTitleType.County:
                    return C.FeudalTitleCountyCost;
                case FeudalTitleType.Duchy:
                    return C.FeudalTitleDuchyCost;
                case FeudalTitleType.Kingdom:
                    return C.FeudalTitleKingdomCost;
                case FeudalTitleType.Empire:
                    return C.FeudalTitleEmpireCost;
                default:
                    return 0;
            }
        }

        private static float GetFormationInfluenceReward(FeudalTitleType targetType)
        {
            switch (targetType)
            {
                case FeudalTitleType.Barony:
                    return C.FeudalTitleBaronyInfluenceReward;
                case FeudalTitleType.County:
                    return C.FeudalTitleCountyInfluenceReward;
                case FeudalTitleType.Duchy:
                    return C.FeudalTitleDuchyInfluenceReward;
                case FeudalTitleType.Kingdom:
                    return C.FeudalTitleKingdomInfluenceReward;
                case FeudalTitleType.Empire:
                    return C.FeudalTitleEmpireInfluenceReward;
                default:
                    return 0f;
            }
        }

        private static bool IsValidFormationTarget(FeudalTitleType targetType)
        {
            return targetType == FeudalTitleType.County
                || targetType == FeudalTitleType.Duchy
                || targetType == FeudalTitleType.Kingdom
                || targetType == FeudalTitleType.Empire;
        }

        private static FeudalTitleType GetChildTypeForFormation(FeudalTitleType targetType)
        {
            switch (targetType)
            {
                case FeudalTitleType.County:
                    return FeudalTitleType.Barony;
                case FeudalTitleType.Duchy:
                    return FeudalTitleType.County;
                case FeudalTitleType.Kingdom:
                    return FeudalTitleType.Duchy;
                case FeudalTitleType.Empire:
                    return FeudalTitleType.Kingdom;
                default:
                    return FeudalTitleType.Barony;
            }
        }

        private static FeudalTitleType GetParentTypeForChild(FeudalTitleType childType)
        {
            switch (childType)
            {
                case FeudalTitleType.Barony:
                    return FeudalTitleType.County;
                case FeudalTitleType.County:
                    return FeudalTitleType.Duchy;
                case FeudalTitleType.Duchy:
                    return FeudalTitleType.Kingdom;
                case FeudalTitleType.Kingdom:
                    return FeudalTitleType.Empire;
                default:
                    return FeudalTitleType.Empire;
            }
        }

        private static float GetAdjacencyDistanceThreshold()
        {
            float averageTownDistance = Campaign.Current.GetAverageDistanceBetweenClosestTwoTownsWithNavigationType(MobileParty.NavigationType.All);
            return averageTownDistance > 0f
                ? averageTownDistance * C.FeudalTitleAdjacencyTownDistanceMultiplier
                : Campaign.MapDiagonal * 0.15f;
        }

        private static bool IsLandedSettlement(Settlement settlement)
        {
            return settlement != null
                && !string.IsNullOrWhiteSpace(settlement.StringId)
                && (settlement.IsTown || settlement.IsCastle);
        }

        private static bool IsValidPermanentKingdom(Kingdom kingdom)
        {
            return kingdom != null
                && !kingdom.IsEliminated
                && !kingdom.IsMinorFaction
                && !kingdom.IsBanditFaction
                && !string.IsNullOrWhiteSpace(kingdom.StringId)
                && !kingdom.StringId.Contains("_rebels_")
                && !kingdom.StringId.StartsWith("bc_feud_");
        }

        private static bool ShouldBarterTransferLegalTitle(string legalHolderId, Clan seller, Clan buyer)
        {
            if (buyer == null || string.IsNullOrWhiteSpace(buyer.StringId) || string.IsNullOrWhiteSpace(legalHolderId))
                return false;

            // A buyer's claim alone cannot convey a third party's lawful ownership.
            return legalHolderId == buyer.StringId || (seller != null && legalHolderId == seller.StringId);
        }

        private bool ShouldLegalTitleTransferToHolder(
            FeudalTitleRecord title,
            Clan newHolder,
            string previousDeJureHolderId,
            string previousDeFactoHolderId,
            ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail detail,
            bool openToClaim = false)
        {
            if (title == null || newHolder == null || string.IsNullOrWhiteSpace(newHolder.StringId))
                return false;

            if (string.Equals(_deFactoOnlySettlementTransferTitleId, title.TitleId, StringComparison.Ordinal))
                return false;

            // Capture custody is not the final award, even if its custodian has a claim.
            if (openToClaim && detail == ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail.BySiege)
                return false;

            if (string.IsNullOrWhiteSpace(previousDeJureHolderId))
                return true;

            if (previousDeJureHolderId == newHolder.StringId)
                return true;

            if (HasActiveClaim(newHolder, title))
                return true;

            bool titleWasAlreadyDisputed = !string.IsNullOrWhiteSpace(previousDeFactoHolderId)
                && !string.IsNullOrWhiteSpace(previousDeJureHolderId)
                && previousDeFactoHolderId != previousDeJureHolderId;

            if (titleWasAlreadyDisputed)
                return false;

            return detail != ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail.BySiege;
        }

        private void RegisterDisplacedLegalOwnerClaimIfNeeded(
            FeudalTitleRecord title,
            string previousDeJureHolderId,
            Clan newHolder,
            ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail detail)
        {
            if (title == null
                || newHolder == null
                || string.IsNullOrWhiteSpace(previousDeJureHolderId)
                || previousDeJureHolderId == newHolder.StringId)
            {
                return;
            }

            // Losing possession earlier does not forfeit the former legal owner's rights.
            // Barter and explicit grants that relinquish claims use separate transfer paths.
            Clan displacedClan = ResolveClan(previousDeJureHolderId);
            if (displacedClan == null || displacedClan.IsEliminated)
                return;

            Hero carrierHero = displacedClan.Leader;
            if (carrierHero == null || carrierHero.IsDead)
                return;

            FeudalClaimRecord record = RegisterClaim(
                displacedClan,
                title,
                FeudalClaimStrength.Strong,
                ResolveLawfulGrantClaimSource(detail),
                newHolder.Leader ?? carrierHero,
                displacedClan,
                carrierHero: carrierHero,
                generationDepth: 1);

            if (record != null)
            {
                BellumCivileLogger.Log(
                    $"Feudal displaced legal owner claim retained; displaced={displacedClan.StringId}; new_holder={newHolder.StringId}; title={title.TitleId}; detail={detail}; carrier={record.CarrierHeroId}.");
            }
        }

        private static string ResolveLawfulGrantClaimSource(ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail detail)
        {
            switch (detail)
            {
                case ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail.ByKingDecision:
                    return "lawful_kingdom_grant";
                case ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail.ByGift:
                    return "lawful_fief_gift";
                default:
                    return $"lawful_title_transfer_{detail}";
            }
        }

        private static bool IsClaimCurrentlyActive(FeudalClaimRecord claim)
        {
            if (claim == null || !claim.IsActive)
                return false;

            return claim.ExpiresDay < 0f || claim.ExpiresDay > CurrentDay;
        }

        private int NormalizeActiveClaimDuplicates()
        {
            int deactivated = 0;
            foreach (IGrouping<string, FeudalClaimRecord> group in _claims
                .Where(IsClaimCurrentlyActive)
                .GroupBy(claim => $"{claim.ClaimantClanId}|{claim.TargetTitleId}")
                .Where(group => group.Count() > 1))
            {
                FeudalClaimRecord retained = group
                    .OrderByDescending(claim => claim.Strength)
                    .ThenByDescending(claim => claim.CreatedDay)
                    .First();
                foreach (FeudalClaimRecord duplicate in group.Where(claim => claim != retained))
                {
                    duplicate.SetActive(false);
                    deactivated++;
                }
            }

            return deactivated;
        }

        private void RemoveRedundantClaimsForHolder(Clan holderClan, FeudalTitleRecord title)
        {
            if (holderClan == null || title == null)
                return;

            _claims.RemoveAll(claim => claim != null
                && claim.ClaimantClanId == holderClan.StringId
                && claim.TargetTitleId == title.TitleId);
        }

        private void DeactivateClaimsForTitle(Clan claimantClan, FeudalTitleRecord title)
        {
            if (claimantClan == null || title == null)
                return;

            foreach (FeudalClaimRecord claim in _claims)
            {
                if (claim != null
                    && claim.IsActive
                    && claim.ClaimantClanId == claimantClan.StringId
                    && claim.TargetTitleId == title.TitleId)
                {
                    claim.SetActive(false);
                }
            }
        }

        internal static FeudalClaimStrength? GetInheritedClaimStrength(FeudalClaimRecord claim)
        {
            // Bloodrights degrade once and then end: strong claims pass to the next generation
            // as weak claims, while weak (including fabricated) claims die with their carrier.
            return claim?.Strength == FeudalClaimStrength.Strong
                ? FeudalClaimStrength.Weak
                : (FeudalClaimStrength?)null;
        }

        private static bool IsClaimCarriedBy(FeudalClaimRecord claim, Hero carrier)
        {
            if (claim == null || carrier == null || string.IsNullOrWhiteSpace(carrier.StringId))
                return false;

            if (!string.IsNullOrWhiteSpace(claim.CarrierHeroId))
                return claim.CarrierHeroId == carrier.StringId;

            return claim.SourceHeroId == carrier.StringId;
        }

        private static List<Hero> GetEligibleClaimHeirs(Hero deadTitleHolder)
        {
            if (deadTitleHolder?.Children == null)
                return new List<Hero>();

            return deadTitleHolder.Children
                .Where(IsEligibleClaimHeir)
                .Distinct()
                .ToList();
        }

        internal static bool IsEligibleClaimHeir(Hero hero)
        {
            return hero != null
                && hero.Clan != null
                && !hero.Clan.IsEliminated
                && hero.IsAlive
                && hero.IsLord
                && !hero.IsDisabled;
        }

        internal static bool IsCloseBloodClaimantOfClan(Hero hero, Clan clan)
        {
            if (hero == null
                || clan == null
                || !IsEligibleClaimHeir(hero)
                || hero.IsChild
                || hero.Age < SuccessionLawHelper.GetAgeOfMajority())
            {
                return false;
            }

            Hero leader = clan.Leader;
            if (leader != null)
            {
                if (hero == leader)
                    return true;

                if (hero.Father == leader || hero.Mother == leader)
                    return true;

                if (AreSiblings(hero, leader))
                    return true;

                if (hero.Father?.Father == leader || hero.Father?.Mother == leader)
                    return true;

                if (hero.Mother?.Father == leader || hero.Mother?.Mother == leader)
                    return true;
            }

            return hero.Father?.Clan == clan || hero.Mother?.Clan == clan;
        }

        private static bool AreSiblings(Hero first, Hero second)
        {
            if (first == null || second == null)
                return false;

            return (first.Father != null && first.Father == second.Father)
                || (first.Mother != null && first.Mother == second.Mother);
        }

        private void RememberCurrentClanLeaders()
        {
            if (Clan.All == null)
                return;

            foreach (Clan clan in Clan.All)
            {
                if (clan?.Leader != null && !string.IsNullOrWhiteSpace(clan.Leader.StringId))
                    _knownClanLeaderIds.Add(clan.Leader.StringId);
            }
        }

        private static Settlement SelectKingdomCapital(Kingdom kingdom)
        {
            return kingdom?.Fiefs
                .Where(town => town?.Settlement != null && (town.Settlement.IsTown || town.Settlement.IsCastle))
                .OrderByDescending(town => town.Prosperity)
                .Select(town => town.Settlement)
                .FirstOrDefault();
        }

        private static Clan ResolveClan(string clanId)
        {
            return string.IsNullOrWhiteSpace(clanId)
                ? null
                : Clan.All?.FirstOrDefault(clan => clan != null && clan.StringId == clanId);
        }

        private Clan ResolveClanReference(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            EnsureReferenceLookupCaches();
            string query = value.Trim();
            if (_clansByReferenceId.TryGetValue(query, out Clan clan)
                || _clansByReferenceName.TryGetValue(query, out clan)
                || _clansByLeaderReferenceName.TryGetValue(query, out clan))
            {
                return clan;
            }

            return null;
        }

        private Kingdom ResolveKingdomReference(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            EnsureReferenceLookupCaches();
            string query = value.Trim();
            return _kingdomsByReferenceId.TryGetValue(query, out Kingdom kingdom)
                || _kingdomsByReferenceName.TryGetValue(query, out kingdom)
                    ? kingdom
                    : null;
        }

        private static Settlement ResolveSettlement(string settlementId)
        {
            return string.IsNullOrWhiteSpace(settlementId)
                ? null
                : Settlement.All?.FirstOrDefault(settlement => settlement != null && settlement.StringId == settlementId);
        }

        private Settlement ResolveSettlementReference(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            EnsureReferenceLookupCaches();
            string query = value.Trim();
            if (_settlementsByReferenceId.TryGetValue(query, out Settlement settlement))
                return settlement;

            string cleanQuery = CleanTitlePlaceName(query);
            return _settlementsByNormalizedReference.TryGetValue(NormalizeTitlePlaceReference(query), out List<Settlement> candidates)
                ? candidates
                    .OrderByDescending(IsLandedSettlement)
                    .ThenByDescending(candidate => string.Equals(candidate.Name?.ToString(), query, StringComparison.OrdinalIgnoreCase))
                    .ThenByDescending(candidate => string.Equals(CleanTitlePlaceName(candidate.Name?.ToString()), cleanQuery, StringComparison.OrdinalIgnoreCase))
                    .FirstOrDefault()
                : null;
        }

        private void EnsureReferenceLookupCaches()
        {
            if (!_referenceLookupCachesReady)
                RebuildReferenceLookupCaches();
        }

        private void RebuildReferenceLookupCaches()
        {
            _configuredTitleIdsCacheReady = false;
            _clansByReferenceId.Clear();
            _clansByReferenceName.Clear();
            _clansByLeaderReferenceName.Clear();
            _kingdomsByReferenceId.Clear();
            _kingdomsByReferenceName.Clear();
            _settlementsByReferenceId.Clear();
            _settlementsByNormalizedReference.Clear();

            foreach (Clan clan in Clan.All ?? Enumerable.Empty<Clan>())
            {
                if (clan == null)
                    continue;

                AddReferenceIfMissing(_clansByReferenceId, clan.StringId, clan);
                AddReferenceIfMissing(_clansByReferenceName, clan.Name?.ToString(), clan);
                AddReferenceIfMissing(_clansByLeaderReferenceName, clan.Leader?.Name?.ToString(), clan);
            }

            foreach (Kingdom kingdom in Kingdom.All ?? Enumerable.Empty<Kingdom>())
            {
                if (kingdom == null)
                    continue;

                AddReferenceIfMissing(_kingdomsByReferenceId, kingdom.StringId, kingdom);
                AddReferenceIfMissing(_kingdomsByReferenceName, DynamicKingdomTitleNameHelper.GetNativeName(kingdom), kingdom);
            }

            foreach (Settlement settlement in (Settlement.All ?? Enumerable.Empty<Settlement>())
                .Where(settlement => settlement != null)
                .OrderByDescending(IsLandedSettlement))
            {
                string name = settlement.Name?.ToString();
                AddReferenceIfMissing(_settlementsByReferenceId, settlement.StringId, settlement);
                AddSettlementReference(NormalizeTitlePlaceReference(name), settlement);
                AddSettlementReference(NormalizeTitlePlaceReference(settlement.StringId), settlement);
            }

            _referenceLookupCachesReady = true;
        }

        private bool HasRegisteredKingdomTitle(Kingdom kingdom)
        {
            return kingdom != null
                && _titlesById.Values.Any(title => title != null
                    && title.IsActive
                    && title.TitleType >= FeudalTitleType.Kingdom
                    && string.Equals(title.AssociatedKingdomId, kingdom.StringId, StringComparison.Ordinal));
        }

        private void RequestRegistryRepair(bool invalidateReferenceLookups)
        {
            _registryRepairRequested = true;
            if (!invalidateReferenceLookups)
                return;

            _referenceLookupCachesReady = false;
            _configuredTitleIdsCacheReady = false;
        }

        private void AddSettlementReference(string key, Settlement settlement)
        {
            if (settlement == null || string.IsNullOrWhiteSpace(key))
                return;

            if (!_settlementsByNormalizedReference.TryGetValue(key, out List<Settlement> candidates))
            {
                candidates = new List<Settlement>();
                _settlementsByNormalizedReference[key] = candidates;
            }

            if (!candidates.Contains(settlement))
                candidates.Add(settlement);
        }

        private static void AddReferenceIfMissing<T>(Dictionary<string, T> index, string key, T value) where T : class
        {
            if (index == null || value == null || string.IsNullOrWhiteSpace(key) || index.ContainsKey(key))
                return;

            index[key] = value;
        }

        private string ResolveTitleIdReference(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            string query = value.Trim();
            string configuredAlias = ResolveConfiguredTitleAlias(query);
            if (!string.IsNullOrWhiteSpace(configuredAlias))
                return configuredAlias;

            if (_titlesById.TryGetValue(query, out FeudalTitleRecord exactTitle)
                && exactTitle != null
                && exactTitle.IsActive)
            {
                return query;
            }

            FeudalTitleRecord byTitle = _titlesById.Values.FirstOrDefault(title =>
                title != null
                && title.IsActive
                && (string.Equals(title.TitleId, query, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(title.Name, query, StringComparison.OrdinalIgnoreCase)));
            if (byTitle != null)
                return byTitle.TitleId;

            Settlement settlement = ResolveSettlementReference(query);
            if (settlement != null)
            {
                string baronyTitleId = BuildBaronyTitleId(settlement);
                if (_titlesById.ContainsKey(baronyTitleId))
                    return baronyTitleId;

                if (_baronyTitleBySettlementId.TryGetValue(settlement.StringId, out baronyTitleId))
                    return baronyTitleId;
            }

            Kingdom kingdom = ResolveKingdomReference(query);
            if (kingdom != null)
            {
                string kingdomTitleId = BuildKingdomTitleId(kingdom);
                if (_titlesById.ContainsKey(kingdomTitleId))
                    return kingdomTitleId;
            }

            return string.Empty;
        }

        private string ResolveConfiguredTitleAlias(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return string.Empty;

            FeudalTitleConfig.ConfiguredTitle configured = FeudalTitleConfig.Instance.Titles?
                .FirstOrDefault(title => title != null && string.Equals(title.Id, query, StringComparison.OrdinalIgnoreCase));
            if (configured == null)
                return string.Empty;

            Settlement settlement = ResolveSettlementReference(configured.SettlementRef);
            Clan deJureClan = ResolveClanReference(configured.DeJureClanRef);
            Clan deFactoClan = ResolveClanReference(configured.DeFactoClanRef) ?? deJureClan;
            Kingdom kingdom = ResolveKingdomReference(configured.KingdomRef)
                ?? deFactoClan?.Kingdom
                ?? deJureClan?.Kingdom;
            string resolvedId = ResolveConfiguredTitleId(configured, settlement, kingdom);
            return !string.IsNullOrWhiteSpace(resolvedId) && _titlesById.TryGetValue(resolvedId, out FeudalTitleRecord resolvedTitle) && resolvedTitle != null && resolvedTitle.IsActive
                ? resolvedId
                : string.Empty;
        }

        private static string BuildConfiguredFallbackTitleRoot(FeudalTitleType type, Settlement settlement, Settlement capital, string titleId)
        {
            if (type == FeudalTitleType.Barony && settlement != null)
                return CleanTitlePlaceName(settlement.Name?.ToString() ?? settlement.StringId);

            string placeName = CleanTitlePlaceName(capital?.Name?.ToString());
            if (string.IsNullOrWhiteSpace(placeName))
                placeName = CleanTitlePlaceName(titleId);
            return string.IsNullOrWhiteSpace(placeName)
                ? type.ToString()
                : placeName;
        }

        private static string CleanTitlePlaceName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            string cleaned = value.Trim();
            if (cleaned.EndsWith(" Castle", StringComparison.OrdinalIgnoreCase))
                cleaned = cleaned.Substring(0, cleaned.Length - " Castle".Length).Trim();

            return cleaned;
        }

        private static string NormalizeTitlePlaceReference(string value)
        {
            string cleaned = CleanTitlePlaceName(value);
            if (string.IsNullOrWhiteSpace(cleaned))
                return string.Empty;

            cleaned = string.Join(" ", cleaned.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));
            if (cleaned.EndsWith(" Town", StringComparison.OrdinalIgnoreCase))
                cleaned = cleaned.Substring(0, cleaned.Length - " Town".Length).Trim();
            if (cleaned.EndsWith(" City", StringComparison.OrdinalIgnoreCase))
                cleaned = cleaned.Substring(0, cleaned.Length - " City".Length).Trim();

            return cleaned;
        }

        public static string BuildBaronyTitleId(Settlement settlement)
        {
            return settlement == null ? string.Empty : $"bc_title_barony_{settlement.StringId}";
        }

        public static string BuildKingdomTitleId(Kingdom kingdom)
        {
            return kingdom == null ? string.Empty : $"bc_title_kingdom_{kingdom.StringId}";
        }

        public static string BuildHigherTitleId(FeudalTitleType titleType, string seed)
        {
            return string.IsNullOrWhiteSpace(seed)
                ? string.Empty
                : $"bc_title_{titleType.ToString().ToLowerInvariant()}_{seed}";
        }

        private static string BuildClaimId(string claimantClanId, string targetTitleId, FeudalClaimStrength strength, string source)
        {
            string cleanSource = string.IsNullOrWhiteSpace(source) ? "unknown" : source.Replace("|", "_");
            return $"bc_claim_{claimantClanId}_{targetTitleId}_{strength.ToString().ToLowerInvariant()}_{cleanSource}";
        }

        private void EndSubinfeudationGrant(SubinfeudationGrantContext context)
        {
            if (!ReferenceEquals(_activeSubinfeudationGrant, context))
                return;

            _activeSubinfeudationGrant = null;
            if (!context.WasConsumed)
            {
                BellumCivileLogger.Log(
                    $"Companion subinfeudation title transaction ended without an ownership event; grantor={context.GrantorClan?.StringId ?? "none"}; title={context.BaronyTitleId}; liege_title={context.LiegeTitleId}.");
            }
        }

        private sealed class SubinfeudationGrantContext
        {
            public string BaronyTitleId { get; }
            public string SettlementId { get; }
            public string LiegeTitleId { get; }
            public Clan GrantorClan { get; }
            public string OriginalDeJureHolderClanId { get; }
            public string OriginalDeFactoHolderClanId { get; }
            public bool TransferDeJure { get; }
            public bool WasConsumed { get; private set; }
            public string RecipientClanId { get; private set; }

            public SubinfeudationGrantContext(
                FeudalTitleRecord baronyTitle,
                FeudalTitleRecord liegeTitle,
                Clan grantorClan,
                bool transferDeJure)
            {
                BaronyTitleId = baronyTitle.TitleId;
                SettlementId = baronyTitle.CapitalSettlementId;
                LiegeTitleId = liegeTitle.TitleId;
                GrantorClan = grantorClan;
                OriginalDeJureHolderClanId = baronyTitle.DeJureHolderClanId;
                OriginalDeFactoHolderClanId = baronyTitle.DeFactoHolderClanId;
                TransferDeJure = transferDeJure;
            }

            public bool Matches(
                Settlement settlement,
                FeudalTitleRecord title,
                Clan oldClan,
                string previousDeJure,
                string previousDeFacto,
                ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail detail)
            {
                return !WasConsumed
                    && detail == ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail.ByGift
                    && settlement != null
                    && title != null
                    && string.Equals(settlement.StringId, SettlementId, StringComparison.Ordinal)
                    && string.Equals(title.TitleId, BaronyTitleId, StringComparison.Ordinal)
                    && string.Equals(previousDeJure, OriginalDeJureHolderClanId, StringComparison.Ordinal)
                    && string.Equals(previousDeFacto, OriginalDeFactoHolderClanId, StringComparison.Ordinal)
                    && string.Equals(previousDeFacto, GrantorClan.StringId, StringComparison.Ordinal)
                    && (oldClan == null || oldClan == GrantorClan);
            }

            public void MarkConsumed(Clan recipientClan)
            {
                WasConsumed = true;
                RecipientClanId = recipientClan?.StringId ?? string.Empty;
            }
        }

        private sealed class SubinfeudationGrantScope : IDisposable
        {
            private readonly FeudalTitleBehavior _owner;
            private readonly SubinfeudationGrantContext _context;
            private bool _disposed;

            public SubinfeudationGrantScope(FeudalTitleBehavior owner, SubinfeudationGrantContext context)
            {
                _owner = owner;
                _context = context;
            }

            public void Dispose()
            {
                if (_disposed)
                    return;

                _disposed = true;
                _owner.EndSubinfeudationGrant(_context);
            }
        }

        private static float CurrentDay => (float)CampaignTime.Now.ToDays;
    }
}
