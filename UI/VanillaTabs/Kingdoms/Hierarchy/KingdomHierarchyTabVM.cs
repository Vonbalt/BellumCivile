using System;
using System.Collections.Generic;
using System.Linq;
using BellumCivile.Behaviors;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core.ViewModelCollection.ImageIdentifiers;
using TaleWorlds.Core.ViewModelCollection.Information;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace BellumCivile.UI.VanillaTabs.Kingdoms.Hierarchy
{
    public sealed class KingdomHierarchyTabVM : ViewModel
    {
        private readonly Kingdom _initialKingdom;
        private readonly List<HierarchyTitleNodeVM> _titleNodes = new List<HierarchyTitleNodeVM>();
        private HierarchyKingdomItemVM _selectedKingdomItem;
        private bool _isSelected;
        private bool _hasHierarchy;
        private string _selectedKingdomName;
        private string _selectedRulerName;
        private string _sovereignTitleName;
        private string _sovereignTitleSummary;
        private BannerImageIdentifierVM _sovereignBannerVisual;
        private string _legalParentText;
        private HierarchyTitleNodeVM _selectedTitle;
        private bool _hasSelectedTitle;
        private FeudalHierarchyMode _hierarchyMode = FeudalHierarchyMode.DeJure;
        private HintViewModel _deJureHint;
        private HintViewModel _deFactoHint;

        public KingdomHierarchyTabVM(Kingdom initialKingdom)
        {
            _initialKingdom = initialKingdom;
            Kingdoms = new MBBindingList<HierarchyKingdomItemVM>();
            HierarchyRoots = new MBBindingList<HierarchyTitleNodeVM>();
            DeJureHint = BuildDeJureHint(true);
            DeFactoHint = new HintViewModel(new TextObject("{=BC_Hierarchy_DeFactoHint}Show the current political hierarchy: who actually controls each title right now, including breakaway realms and occupied lands."));
            RefreshValues();
        }

        public override void RefreshValues()
        {
            base.RefreshValues();
            FeudalTitleBehavior titleBehavior = Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>();
            FactionManagerBehavior factionManager = Campaign.Current?.GetCampaignBehavior<FactionManagerBehavior>();
            string selectedKingdomId = _selectedKingdomItem?.Kingdom?.StringId ?? _initialKingdom?.StringId;

            Kingdoms.Clear();
            IEnumerable<Kingdom> validKingdoms = Kingdom.All
                .Where(kingdom => kingdom != null
                    && !kingdom.IsEliminated
                    && kingdom.RulingClan?.Leader != null
                    && !BellumKingdomVisibilityHelper.IsTemporaryBellumKingdom(kingdom)
                    && factionManager?.GetFactionByRebelKingdom(kingdom) == null)
                .OrderByDescending(kingdom => kingdom == Clan.PlayerClan?.Kingdom)
                .ThenBy(kingdom => kingdom.Name?.ToString());
            foreach (Kingdom kingdom in validKingdoms)
            {
                HierarchyKingdomItemVM item = new HierarchyKingdomItemVM(
                    kingdom,
                    ResolveDisplayRoot(titleBehavior, kingdom, GetDisplayModeForKingdom(kingdom)),
                    false,
                    SelectKingdom);
                Kingdoms.Add(item);
            }

            IEnumerable<Kingdom> historicalKingdoms = Kingdom.All
                .Where(kingdom => IsHistoricalHierarchyKingdom(kingdom, titleBehavior, factionManager))
                .OrderBy(kingdom => kingdom.Name?.ToString());
            foreach (Kingdom kingdom in historicalKingdoms)
            {
                Kingdoms.Add(new HierarchyKingdomItemVM(
                    kingdom,
                    titleBehavior.GetHistoricalRealmSovereignTitle(kingdom),
                    true,
                    SelectKingdom));
            }

            HierarchyKingdomItemVM selected = Kingdoms.FirstOrDefault(item => item.Kingdom?.StringId == selectedKingdomId)
                ?? Kingdoms.FirstOrDefault();
            SelectKingdom(selected);
        }

        private void SelectKingdom(HierarchyKingdomItemVM item)
        {
            if (item == null)
            {
                ClearHierarchy();
                return;
            }

            foreach (HierarchyKingdomItemVM candidate in Kingdoms)
                candidate.IsSelected = candidate == item;
            _selectedKingdomItem = item;
            RefreshViewModeState();
            BuildHierarchy(item);
        }

        private void BuildHierarchy(HierarchyKingdomItemVM item)
        {
            Kingdom kingdom = item?.Kingdom;
            HierarchyRoots.Clear();
            _titleNodes.Clear();
            SelectedTitle = null;
            HasSelectedTitle = false;
            FeudalTitleBehavior titleBehavior = Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>();
            RefreshViewModeState();
            FeudalHierarchyMode displayMode = GetDisplayModeForItem(item);
            FeudalTitleRecord root = item?.IsHistorical == true
                ? item.SovereignTitle
                : ResolveDisplayRoot(titleBehavior, kingdom, displayMode);
            SelectedKingdomName = kingdom?.Name?.ToString() ?? string.Empty;
            Clan lawfulHolder = ResolveClan(root?.DeJureHolderClanId);
            SelectedRulerName = item?.IsHistorical == true
                ? lawfulHolder?.Leader?.Name?.ToString() ?? string.Empty
                : kingdom?.Leader?.Name?.ToString() ?? string.Empty;
            SovereignTitleName = BuildSovereignDisplayName(root, kingdom, item?.IsHistorical == true);
            SovereignBannerVisual = kingdom?.Banner == null
                ? null
                : new BannerImageIdentifierVM(kingdom.Banner, true);
            FeudalTitleRecord legalParent = titleBehavior?.GetTitle(root?.ParentTitleId);
            LegalParentText = legalParent == null
                ? string.Empty
                : FormatLegalParent(FeudalTitleDisplayHelper.FormatTitleName(legalParent));

            if (root == null || titleBehavior == null)
            {
                HasHierarchy = false;
                SovereignTitleSummary = string.Empty;
                return;
            }

            List<FeudalTitleRecord> titles = GetDisplayTitles(titleBehavior, kingdom, root, displayMode)
                .Where(title => title != null && title.IsActive)
                .ToList();
            SovereignTitleSummary = BuildTitleCountSummary(root, titles);
            FeudalClaimFabricationRecord playerFabrication = Campaign.Current
                ?.GetCampaignBehavior<FeudalClaimFabricationBehavior>()
                ?.GetActiveFabrications()
                .FirstOrDefault(record => record.FabricatorClanId == Clan.PlayerClan?.StringId);

            Dictionary<string, List<FeudalTitleRecord>> childrenByParent = titles
                .Select(title => new
                {
                    Title = title,
                    ParentId = GetParentTitleId(title, displayMode)
                })
                .Where(entry => !string.IsNullOrWhiteSpace(entry.ParentId))
                .GroupBy(entry => entry.ParentId)
                .ToDictionary(
                    group => group.Key,
                    group => group
                        .Select(entry => entry.Title)
                        .OrderByDescending(title => title.TitleType)
                        .ThenBy(title => title.Name)
                        .ToList());
            foreach (FeudalTitleRecord rootTitle in GetDisplayRoots(titles, root, displayMode))
            {
                HierarchyTitleNodeVM rootNode = BuildTitleTree(
                    rootTitle,
                    titleBehavior,
                    kingdom,
                    playerFabrication,
                    childrenByParent,
                    new HashSet<string>());
                if (rootNode != null)
                    HierarchyRoots.Add(rootNode);
            }

            HasHierarchy = HierarchyRoots.Count > 0;
        }

        private HierarchyTitleNodeVM BuildTitleTree(
            FeudalTitleRecord title,
            FeudalTitleBehavior titleBehavior,
            Kingdom hierarchyKingdom,
            FeudalClaimFabricationRecord playerFabrication,
            IReadOnlyDictionary<string, List<FeudalTitleRecord>> childrenByParent,
            HashSet<string> visited)
        {
            if (title == null || string.IsNullOrWhiteSpace(title.TitleId) || !visited.Add(title.TitleId))
                return null;

            HierarchyTitleNodeVM node = new HierarchyTitleNodeVM(
                title,
                titleBehavior,
                hierarchyKingdom,
                playerFabrication,
                SelectTitle,
                RefreshHierarchyAfterAction);
            _titleNodes.Add(node);
            if (childrenByParent.TryGetValue(title.TitleId, out List<FeudalTitleRecord> children))
            {
                foreach (FeudalTitleRecord child in children)
                {
                    HierarchyTitleNodeVM childNode = BuildTitleTree(
                        child,
                        titleBehavior,
                        hierarchyKingdom,
                        playerFabrication,
                        childrenByParent,
                        visited);
                    if (childNode != null)
                        node.Branch.Add(childNode);
                }
            }

            node.FinalizeBranchLayout();

            return node;
        }

        private void RefreshHierarchyAfterAction(string selectedTitleId)
        {
            Kingdom kingdom = _selectedKingdomItem?.Kingdom;
            if (kingdom == null)
                return;

            if (_selectedKingdomItem.IsHistorical
                && !IsHistoricalHierarchyKingdom(
                    kingdom,
                    Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>(),
                    Campaign.Current?.GetCampaignBehavior<FactionManagerBehavior>()))
            {
                RefreshValues();
                return;
            }

            BuildHierarchy(_selectedKingdomItem);
            HierarchyTitleNodeVM restoredSelection = _titleNodes.FirstOrDefault(node =>
                node?.Title != null
                && string.Equals(node.Title.TitleId, selectedTitleId, StringComparison.Ordinal));
            if (restoredSelection != null)
                SelectTitle(restoredSelection);
        }

        private void SelectTitle(HierarchyTitleNodeVM node)
        {
            foreach (HierarchyTitleNodeVM candidate in _titleNodes)
                candidate.IsSelected = candidate == node;

            SelectedTitle = node;
            HasSelectedTitle = node != null;
        }

        public void ExecuteClearSelection()
        {
            ClearInteractionState();
            SelectTitle(null);
        }

        public void ClearInteractionState()
        {
            foreach (HierarchyTitleNodeVM node in _titleNodes)
                node?.ExecuteEndHint();
        }

        public void ExecuteShowDeJure()
        {
            if (_hierarchyMode == FeudalHierarchyMode.DeJure || !IsDeJureAvailable)
                return;

            HierarchyMode = FeudalHierarchyMode.DeJure;
            BuildHierarchy(_selectedKingdomItem);
        }

        public void ExecuteShowDeFacto()
        {
            if (_hierarchyMode == FeudalHierarchyMode.DeFacto || !IsDeFactoAvailable)
                return;

            HierarchyMode = FeudalHierarchyMode.DeFacto;
            BuildHierarchy(_selectedKingdomItem);
        }

        private void ClearHierarchy()
        {
            _selectedKingdomItem = null;
            HierarchyRoots.Clear();
            _titleNodes.Clear();
            HasHierarchy = false;
            SelectedKingdomName = string.Empty;
            SelectedRulerName = string.Empty;
            SovereignTitleName = string.Empty;
            SovereignTitleSummary = string.Empty;
            SovereignBannerVisual = null;
            LegalParentText = string.Empty;
            SelectedTitle = null;
            HasSelectedTitle = false;
            RefreshViewModeState();
        }

        private static string BuildTitlePath(FeudalTitleBehavior titleBehavior, FeudalTitleRecord title)
        {
            List<string> path = new List<string>();
            HashSet<string> visited = new HashSet<string>();
            FeudalTitleRecord current = title;
            while (current != null && visited.Add(current.TitleId))
            {
                path.Add(FeudalTitleDisplayHelper.FormatTitleName(current) ?? current.TitleId);
                current = titleBehavior.GetTitle(current.ParentTitleId);
            }
            path.Reverse();
            return string.Join("/", path);
        }

        private static Clan ResolveClan(string clanId)
        {
            return string.IsNullOrWhiteSpace(clanId)
                ? null
                : Clan.All.FirstOrDefault(clan => clan != null
                    && string.Equals(clan.StringId, clanId, StringComparison.Ordinal));
        }

        private FeudalHierarchyMode GetDisplayModeForItem(HierarchyKingdomItemVM item)
        {
            if (item?.IsHistorical == true)
                return FeudalHierarchyMode.DeJure;

            return GetDisplayModeForKingdom(item?.Kingdom);
        }

        private FeudalHierarchyMode GetDisplayModeForKingdom(Kingdom kingdom)
        {
            if (_hierarchyMode == FeudalHierarchyMode.DeJure && !HasDeJureHierarchy(kingdom))
                return FeudalHierarchyMode.DeFacto;

            return _hierarchyMode;
        }

        private static FeudalTitleRecord ResolveDisplayRoot(FeudalTitleBehavior titleBehavior, Kingdom kingdom, FeudalHierarchyMode mode)
        {
            if (titleBehavior == null || kingdom == null || kingdom.IsEliminated
                || BellumKingdomVisibilityHelper.IsTemporaryBellumKingdom(kingdom))
                return null;

            // Both views describe the same realm crown; only the parent/child relationships change.
            // Viewing its legal tree does not require the current ruler to own that crown lawfully.
            FeudalTitleRecord root = titleBehavior.GetRealmSovereignTitle(kingdom, FeudalHierarchyMode.DeFacto);
            if (root == null && mode == FeudalHierarchyMode.DeJure)
                root = titleBehavior.GetKingdomPoliticalTitle(kingdom);
            return root != null && root.IsActive ? root : null;
        }

        private bool HasDeJureHierarchy(Kingdom kingdom)
        {
            FeudalTitleBehavior titleBehavior = Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>();
            if (titleBehavior == null || kingdom == null)
                return false;

            FeudalTitleRecord root = ResolveDisplayRoot(titleBehavior, kingdom, FeudalHierarchyMode.DeJure);
            if (root == null || !root.IsActive)
                return false;

            bool hasLegalChildren = titleBehavior
                .GetTitleAndDescendants(root, FeudalHierarchyMode.DeJure)
                .Any(title => title != null && !string.Equals(title.TitleId, root.TitleId, StringComparison.Ordinal));
            bool hasLegalParent = titleBehavior.GetTitle(root.ParentTitleId) != null;
            if (!hasLegalChildren && !hasLegalParent)
                return false;

            if (!titleBehavior.IsIndependentRealmShell(kingdom))
                return true;

            return root.TitleType > FeudalTitleType.Barony;
        }

        private bool HasDeJureHierarchy(HierarchyKingdomItemVM item)
        {
            if (item == null)
                return false;
            if (!item.IsHistorical)
                return HasDeJureHierarchy(item.Kingdom);

            FeudalTitleBehavior titleBehavior = Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>();
            return titleBehavior != null
                && item.SovereignTitle != null
                && item.SovereignTitle.IsActive
                && titleBehavior.GetTitleAndDescendants(item.SovereignTitle, FeudalHierarchyMode.DeJure)
                    .Any(title => title != null
                        && title.IsActive
                        && !string.Equals(title.TitleId, item.SovereignTitle.TitleId, StringComparison.Ordinal));
        }

        private static bool IsHistoricalHierarchyKingdom(
            Kingdom kingdom,
            FeudalTitleBehavior titleBehavior,
            FactionManagerBehavior factionManager)
        {
            if (kingdom == null
                || !kingdom.IsEliminated
                || titleBehavior == null
                || BellumKingdomVisibilityHelper.IsTemporaryBellumKingdom(kingdom)
                || factionManager?.GetFactionByRebelKingdom(kingdom) != null)
            {
                return false;
            }

            FeudalTitleRecord root = titleBehavior.GetHistoricalRealmSovereignTitle(kingdom);
            return root != null
                && root.IsActive
                && !titleBehavior.IsHistoricalRealmRepresentedByActiveSuccessor(kingdom, root)
                && titleBehavior.GetTitleAndDescendants(root, FeudalHierarchyMode.DeJure)
                    .Any(title => title != null
                        && title.IsActive
                        && !string.Equals(title.TitleId, root.TitleId, StringComparison.Ordinal));
        }

        private void RefreshViewModeState()
        {
            bool deJureAvailable = IsDeJureAvailable;
            DeJureHint = BuildDeJureHint(deJureAvailable);
            DeFactoHint = _selectedKingdomItem?.IsHistorical == true
                ? new HintViewModel(new TextObject("{=BC_Hierarchy_HistoricalDeFactoHint}This realm has been destroyed. Only its surviving de jure hierarchy can be shown."))
                : new HintViewModel(new TextObject("{=BC_Hierarchy_DeFactoHint}Show the current political hierarchy: who actually controls each title right now, including breakaway realms and occupied lands."));
            OnPropertyChanged(nameof(IsDeJureAvailable));
            OnPropertyChanged(nameof(IsDeFactoAvailable));
            OnPropertyChanged(nameof(IsDeJureMode));
            OnPropertyChanged(nameof(IsDeFactoMode));
        }

        private static HintViewModel BuildDeJureHint(bool isAvailable)
        {
            return isAvailable
                ? new HintViewModel(new TextObject("{=BC_Hierarchy_DeJureHint}Show the lawful title hierarchy: who is legally bound under whom, even when lands are occupied or contested."))
                : new HintViewModel(new TextObject("{=BC_Hierarchy_DeJureUnavailableHint}This realm has no recognized de jure title hierarchy yet. Only current political control can be shown until a lawful higher title is formed or drifts into the realm."));
        }

        private static IReadOnlyCollection<FeudalTitleRecord> GetDisplayTitles(
            FeudalTitleBehavior titleBehavior,
            Kingdom kingdom,
            FeudalTitleRecord root,
            FeudalHierarchyMode mode)
        {
            if (titleBehavior == null || kingdom == null || root == null)
                return new List<FeudalTitleRecord>();

            if (mode == FeudalHierarchyMode.DeJure)
                return AddDeJureAncestors(
                    titleBehavior,
                    titleBehavior.GetTitleAndDescendants(root, FeudalHierarchyMode.DeJure));

            List<FeudalTitleRecord> controlled = kingdom.Clans
                .Where(clan => clan != null)
                .SelectMany(clan => titleBehavior.GetTitlesHeldByClan(clan, deJure: false))
                .Where(title => title != null && title.IsActive)
                .GroupBy(title => title.TitleId)
                .Select(group => group.First())
                .ToList();

            if (!controlled.Any(title => title.TitleId == root.TitleId))
                controlled.Add(root);

            return controlled;
        }

        private static IReadOnlyCollection<FeudalTitleRecord> AddDeJureAncestors(
            FeudalTitleBehavior titleBehavior,
            IReadOnlyCollection<FeudalTitleRecord> titles)
        {
            if (titleBehavior == null || titles == null || titles.Count == 0)
                return titles ?? new List<FeudalTitleRecord>();

            Dictionary<string, FeudalTitleRecord> byId = titles
                .Where(title => title != null && !string.IsNullOrWhiteSpace(title.TitleId))
                .GroupBy(title => title.TitleId)
                .ToDictionary(group => group.Key, group => group.First());

            foreach (FeudalTitleRecord title in titles.ToList())
            {
                FeudalTitleRecord current = title;
                HashSet<string> visited = new HashSet<string>();
                while (current != null && visited.Add(current.TitleId))
                {
                    string parentId = current.ParentTitleId;
                    if (string.IsNullOrWhiteSpace(parentId))
                        break;

                    FeudalTitleRecord parent = titleBehavior.GetTitle(parentId);
                    if (parent == null || !parent.IsActive || string.IsNullOrWhiteSpace(parent.TitleId))
                        break;

                    if (!byId.ContainsKey(parent.TitleId))
                        byId[parent.TitleId] = parent;

                    current = parent;
                }
            }

            return byId.Values.ToList();
        }

        private static IReadOnlyCollection<FeudalTitleRecord> GetDisplayRoots(
            IReadOnlyCollection<FeudalTitleRecord> titles,
            FeudalTitleRecord preferredRoot,
            FeudalHierarchyMode mode)
        {
            if (titles == null || titles.Count == 0)
                return new List<FeudalTitleRecord>();

            HashSet<string> titleIds = new HashSet<string>(titles.Select(title => title.TitleId));
            List<FeudalTitleRecord> roots = titles
                .Where(title =>
                {
                    string parentId = GetParentTitleId(title, mode);
                    return string.IsNullOrWhiteSpace(parentId) || !titleIds.Contains(parentId);
                })
                .OrderByDescending(title => title.TitleId == preferredRoot?.TitleId)
                .ThenByDescending(title => title.TitleType)
                .ThenBy(title => title.Name)
                .ToList();

            if (roots.Count == 0 && preferredRoot != null)
                roots.Add(preferredRoot);

            return roots;
        }

        private static string GetParentTitleId(FeudalTitleRecord title, FeudalHierarchyMode mode)
        {
            if (title == null)
                return string.Empty;

            return mode == FeudalHierarchyMode.DeFacto
                ? title.DeFactoParentTitleId
                : title.ParentTitleId;
        }

        private static string FormatLegalParent(string parentName)
        {
            TextObject text = new TextObject("{=BC_Hierarchy_DeJureLiege}De jure liege: {TITLE_NAME}");
            text.SetTextVariable("TITLE_NAME", parentName);
            return text.ToString();
        }

        private static string BuildSovereignDisplayName(FeudalTitleRecord root, Kingdom kingdom, bool isHistorical)
        {
            string nativeTitle = isHistorical ? null : kingdom?.EncyclopediaTitle?.ToString();
            if (!string.IsNullOrWhiteSpace(nativeTitle))
                return nativeTitle;

            return root == null
                ? new TextObject("{=BC_Hierarchy_NoSovereignTitle}No sovereign title").ToString()
                : FeudalTitleDisplayHelper.FormatTitleName(root, kingdom?.RulingClan);
        }

        private static string BuildTitleCountSummary(FeudalTitleRecord root, IReadOnlyCollection<FeudalTitleRecord> titles)
        {
            if (root == null || titles == null)
                return string.Empty;

            List<string> counts = new List<string>();
            if (root.TitleType >= FeudalTitleType.Empire)
                counts.Add(FormatTierCount(titles.Count(title => title.TitleType == FeudalTitleType.Kingdom), FeudalTitleType.Kingdom));
            if (root.TitleType >= FeudalTitleType.Kingdom)
                counts.Add(FormatTierCount(titles.Count(title => title.TitleType == FeudalTitleType.Duchy), FeudalTitleType.Duchy));
            if (root.TitleType >= FeudalTitleType.Duchy)
                counts.Add(FormatTierCount(titles.Count(title => title.TitleType == FeudalTitleType.County), FeudalTitleType.County));
            if (root.TitleType >= FeudalTitleType.County || root.TitleType == FeudalTitleType.Barony)
                counts.Add(FormatTierCount(titles.Count(title => title.TitleType == FeudalTitleType.Barony), FeudalTitleType.Barony));

            return string.Join(" | ", counts);
        }

        private static string FormatTierCount(int count, FeudalTitleType type)
        {
            TextObject text;
            switch (type)
            {
                case FeudalTitleType.Kingdom:
                    text = count == 1
                        ? new TextObject("{=BC_Hierarchy_CountKingdom}{COUNT} kingdom")
                        : new TextObject("{=BC_Hierarchy_CountKingdoms}{COUNT} kingdoms");
                    break;
                case FeudalTitleType.Duchy:
                    text = count == 1
                        ? new TextObject("{=BC_Hierarchy_CountDuchy}{COUNT} duchy")
                        : new TextObject("{=BC_Hierarchy_CountDuchies}{COUNT} duchies");
                    break;
                case FeudalTitleType.County:
                    text = count == 1
                        ? new TextObject("{=BC_Hierarchy_CountCounty}{COUNT} county")
                        : new TextObject("{=BC_Hierarchy_CountCounties}{COUNT} counties");
                    break;
                default:
                    text = count == 1
                        ? new TextObject("{=BC_Hierarchy_CountBarony}{COUNT} barony")
                        : new TextObject("{=BC_Hierarchy_CountBaronies}{COUNT} baronies");
                    break;
            }

            text.SetTextVariable("COUNT", count);
            return text.ToString();
        }

        [DataSourceProperty] public MBBindingList<HierarchyKingdomItemVM> Kingdoms { get; }
        [DataSourceProperty] public MBBindingList<HierarchyTitleNodeVM> HierarchyRoots { get; }
        [DataSourceProperty] public string KingdomsHeader => new TextObject("{=BC_Hierarchy_KingdomsHeader}Hierarchies").ToString();
        [DataSourceProperty] public string HierarchyHeader => new TextObject("{=BC_Hierarchy_Header}Title Hierarchy").ToString();
        [DataSourceProperty] public string ViewModeText => new TextObject("{=BC_Hierarchy_ViewMode}View").ToString();
        [DataSourceProperty] public string DeJureText => new TextObject("{=BC_Hierarchy_ViewDeJure}De Jure").ToString();
        [DataSourceProperty] public string DeFactoText => new TextObject("{=BC_Hierarchy_ViewDeFacto}De Facto").ToString();
        [DataSourceProperty] public bool IsDeJureAvailable => HasDeJureHierarchy(_selectedKingdomItem);
        [DataSourceProperty] public bool IsDeFactoAvailable => _selectedKingdomItem != null && !_selectedKingdomItem.IsHistorical;
        [DataSourceProperty] public bool IsDeJureMode => GetDisplayModeForItem(_selectedKingdomItem) == FeudalHierarchyMode.DeJure;
        [DataSourceProperty] public bool IsDeFactoMode => GetDisplayModeForItem(_selectedKingdomItem) == FeudalHierarchyMode.DeFacto;
        [DataSourceProperty]
        public HintViewModel DeJureHint
        {
            get => _deJureHint;
            private set { if (value != _deJureHint) { _deJureHint = value; OnPropertyChangedWithValue(value, nameof(DeJureHint)); } }
        }
        [DataSourceProperty]
        public HintViewModel DeFactoHint
        {
            get => _deFactoHint;
            private set { if (value != _deFactoHint) { _deFactoHint = value; OnPropertyChangedWithValue(value, nameof(DeFactoHint)); } }
        }

        private FeudalHierarchyMode HierarchyMode
        {
            get => _hierarchyMode;
            set
            {
                if (value == _hierarchyMode)
                    return;

                _hierarchyMode = value;
                RefreshViewModeState();
            }
        }

        [DataSourceProperty]
        public bool IsSelected
        {
            get => _isSelected;
            set { if (value != _isSelected) { _isSelected = value; OnPropertyChangedWithValue(value, nameof(IsSelected)); } }
        }
        [DataSourceProperty]
        public bool HasHierarchy
        {
            get => _hasHierarchy;
            set { if (value != _hasHierarchy) { _hasHierarchy = value; OnPropertyChangedWithValue(value, nameof(HasHierarchy)); } }
        }
        [DataSourceProperty]
        public string SelectedKingdomName
        {
            get => _selectedKingdomName;
            set { if (value != _selectedKingdomName) { _selectedKingdomName = value; OnPropertyChangedWithValue(value, nameof(SelectedKingdomName)); } }
        }
        [DataSourceProperty]
        public string SelectedRulerName
        {
            get => _selectedRulerName;
            set { if (value != _selectedRulerName) { _selectedRulerName = value; OnPropertyChangedWithValue(value, nameof(SelectedRulerName)); } }
        }
        [DataSourceProperty]
        public string SovereignTitleName
        {
            get => _sovereignTitleName;
            set { if (value != _sovereignTitleName) { _sovereignTitleName = value; OnPropertyChangedWithValue(value, nameof(SovereignTitleName)); } }
        }
        [DataSourceProperty]
        public string SovereignTitleSummary
        {
            get => _sovereignTitleSummary;
            set { if (value != _sovereignTitleSummary) { _sovereignTitleSummary = value; OnPropertyChangedWithValue(value, nameof(SovereignTitleSummary)); } }
        }
        [DataSourceProperty]
        public BannerImageIdentifierVM SovereignBannerVisual
        {
            get => _sovereignBannerVisual;
            set { if (value != _sovereignBannerVisual) { _sovereignBannerVisual = value; OnPropertyChangedWithValue(value, nameof(SovereignBannerVisual)); } }
        }
        [DataSourceProperty]
        public string LegalParentText
        {
            get => _legalParentText;
            set { if (value != _legalParentText) { _legalParentText = value; OnPropertyChangedWithValue(value, nameof(LegalParentText)); } }
        }
        [DataSourceProperty]
        public HierarchyTitleNodeVM SelectedTitle
        {
            get => _selectedTitle;
            set { if (value != _selectedTitle) { _selectedTitle = value; OnPropertyChangedWithValue(value, nameof(SelectedTitle)); } }
        }
        [DataSourceProperty]
        public bool HasSelectedTitle
        {
            get => _hasSelectedTitle;
            set { if (value != _hasSelectedTitle) { _hasSelectedTitle = value; OnPropertyChangedWithValue(value, nameof(HasSelectedTitle)); } }
        }
    }
}
