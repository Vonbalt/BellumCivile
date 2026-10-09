using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using C = BellumCivile.BellumCivileConstants;

namespace BellumCivile.Behaviors
{
    /// <summary>
    /// Splits excess inherited fiefs from large noble houses into cadet branches
    /// after clan succession has settled. The player clan uses the same delayed
    /// path after the legal heir has been selected.
    /// </summary>
    public partial class PartitionSuccessionBehavior : CampaignBehaviorBase
    {
        private static PropertyInfo _clanTierProperty;
        private static FieldInfo _clanTierField;
        private static bool _tierReflectionInitialized;
        private static PropertyInfo _mobilePartyActualClanProperty;
        private static bool _mobilePartyActualClanReflectionInitialized;

        private List<PendingPartitionSuccessionRecord> _pendingPartitions = new List<PendingPartitionSuccessionRecord>();
        private Dictionary<string, string> _knownClanLeaderIds = new Dictionary<string, string>();

        internal bool HasPendingInheritance(Clan clan, Hero predecessor) => clan != null && predecessor != null
            && _pendingPartitions.Any(r => r != null && r.ParentClanId == clan.StringId
                && r.DeadLeaderId == predecessor.StringId);

        public override void RegisterEvents()
        {
            CampaignEvents.HeroKilledEvent.AddNonSerializedListener(this, OnHeroKilled);
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
            CampaignEvents.HourlyTickEvent.AddNonSerializedListener(this, OnHourlyTick);
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
        }

        public override void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData("BellumCivile_PendingPartitionSuccessions", ref _pendingPartitions);
            dataStore.SyncData("BellumCivile_KnownPartitionClanLeaders", ref _knownClanLeaderIds);
            dataStore.SyncData("BC_CrossClanDeathEstates", ref _crossClanEstates);
            _crossClanEstates = _crossClanEstates ?? new List<CrossClanEstateRecord>();
            EnsureCollectionsInitialized();
            PrunePendingPartitions();
        }

        private void OnHeroKilled(Hero victim, Hero killer, KillCharacterAction.KillCharacterActionDetail detail, bool showNotification)
        {
            if (HasCrossClanEstate(victim)) return;
            if (!BellumCivileOptions.EnablePartitionSuccession)
                return;

            if (RegencyBehavior.Instance?.IsRegentDeathInProgress(victim) == true)
                return;
            if (RegencyBehavior.Instance?.IsWardDeathInProgress(victim) == true)
                return;

            EnsureCollectionsInitialized();

            Clan parentClan = ResolveVictimClan(victim);
            if (!CanPartitionClan(parentClan))
                return;

            if (!WasKnownClanLeader(parentClan, victim))
            {
                TracePartition($"skipped death of {victim?.StringId ?? "unknown"} in {parentClan.StringId}: not current or tracked clan leader; current_leader={parentClan.Leader?.StringId ?? "none"}.");
                return;
            }

            List<Town> inheritedFiefs = GetPartitionableFiefs(parentClan);
            int reservedFiefs = GetMainHeirReservedFiefs();

            List<Hero> orderedHeirs = GetOrderedNaturalHeirs(parentClan, victim);
            if (orderedHeirs.Count == 0)
            {
                TracePartition($"skipped partition for {parentClan.StringId}: natural_heirs=0.");
                return;
            }

            string parentId = parentClan.StringId;
            FeudalTitleBehavior titleBehavior = Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>();
            List<FeudalTitleRecord> inheritedTitles = titleBehavior?.GetTitlesHeldByClan(parentClan, deJure: true).ToList()
                ?? new List<FeudalTitleRecord>();
            bool parentWasRulingClan = parentClan == parentClan.Kingdom?.RulingClan;
            FeudalTitleRecord primarySovereignTitle = parentWasRulingClan
                ? titleBehavior?.GetRealmSovereignTitle(parentClan.Kingdom, FeudalHierarchyMode.DeFacto)
                    ?? titleBehavior?.GetKingdomPoliticalTitle(parentClan.Kingdom)
                : null;
            bool crownPartition = HasSeparableCrownEstate(parentClan, titleBehavior, primarySovereignTitle);
            if (inheritedFiefs.Count <= reservedFiefs && !crownPartition)
            {
                TracePartition($"skipped partition for {parentClan.StringId}: fiefs={inheritedFiefs.Count}; main_heir_reserved={reservedFiefs}; spare_fiefs=0.");
                return;
            }

            _pendingPartitions.RemoveAll(r => r != null && r.ParentClanId == parentId && !HasCrownPromotionJournal(r));
            _pendingPartitions.Add(new PendingPartitionSuccessionRecord(
                victim.StringId,
                parentId,
                parentClan.Kingdom?.StringId ?? string.Empty,
                JoinIds(inheritedFiefs.Select(f => f.StringId)),
                JoinIds(orderedHeirs.Select(h => h.StringId)),
                CampaignTime.Now + CampaignTime.Hours(1f),
                parentWasRulingClan,
                JoinIds(inheritedTitles.Select(title => title.TitleId)),
                primarySovereignTitle?.TitleId ?? string.Empty)
            {
                CrownRoutingRequested = crownPartition,
                OriginalClaims = titleBehavior?.GetActiveClaimsByClan(parentClan)
                    .OrderBy(claim => claim.ClaimId, StringComparer.Ordinal).Select(claim => claim.CopyForEstate()).ToList()
            });

            TracePartition($"queued partition for {parentClan.StringId}; dead_leader={victim.StringId}; fiefs={inheritedFiefs.Count}; fief_ids={FormatFiefIds(inheritedFiefs)}; titles={inheritedTitles.Count}; title_ids={FormatTitleIds(inheritedTitles)}; primary_sovereign={primarySovereignTitle?.TitleId ?? "none"}; natural_heirs={orderedHeirs.Count}; heir_ids={FormatHeroIds(orderedHeirs)}.");
        }

        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            EnsureCollectionsInitialized();
            RestorePendingEstateHeads();
            RepairPartitionCadetParties();
            RefreshKnownClanLeaders();
        }

        private void RestorePendingEstateHeads()
        {
            foreach (var estate in _crossClanEstates)
                if (estate != null && !estate.Completed && estate.Shares != null)
                    foreach (var share in estate.Shares)
                        if (share != null && !share.Completed && share.CadetPlan != null)
                            PreservePendingCadetHead(share.CadetPlan.Cadet, share.CadetPlan.Heir);
            foreach (var pending in _pendingPartitions)
                foreach (var share in pending?.EstateShares ?? Enumerable.Empty<CrossClanEstateShare>())
                    if (share != null && !share.Completed && share.CadetPlan != null)
                        PreservePendingCadetHead(share.CadetPlan.Cadet, share.CadetPlan.Heir);
        }

        private void OnHourlyTick()
        {
            ProcessCrossClanEstates();
            if (!BellumCivileOptions.EnablePartitionSuccession)
                return;

            EnsureCollectionsInitialized();
            RepairPartitionCadetParties();
            ProcessPendingPartitions();
        }

        private void OnDailyTick()
        {
            if (!BellumCivileOptions.EnablePartitionSuccession)
                return;

            EnsureCollectionsInitialized();
            ProcessPendingPartitions();
            RefreshKnownClanLeaders();
        }

        private void ProcessPendingPartitions()
        {
            foreach (PendingPartitionSuccessionRecord record in _pendingPartitions.ToList())
            {
                if (record == null)
                {
                    _pendingPartitions.Remove(record);
                    continue;
                }

                if (record.CrownBatch?.Completed == true)
                {
                    _pendingPartitions.Remove(record);
                    continue;
                }
                if (record.ReadyDate.IsFuture)
                    continue;

                // Once selected, a Crown estate can only resume through its own executor.
                if (HasCrownPromotionJournal(record))
                {
                    ResumeCrownPartition(record);
                    continue;
                }
                if (_pendingPartitions.Any(r => HasCrownPromotionJournal(r) && r.ParentClanId == record.ParentClanId))
                    continue;

                if (IsExpired(record))
                {
                    Clan parentClan = ResolveClan(record.ParentClanId);
                    if (RegencyBehavior.Instance?.TryGetRegency(parentClan, out _) == true)
                    {
                        record.ReadyDate = CampaignTime.Now + CampaignTime.Hours(1f);
                        continue;
                    }

                    TracePartition($"removed expired partition for {record.ParentClanId}; dead_leader={record.DeadLeaderId}.");
                    _pendingPartitions.Remove(record);
                    continue;
                }

                if (TryApplyPartition(record))
                    _pendingPartitions.Remove(record);
            }
        }

        private bool TryApplyPartition(PendingPartitionSuccessionRecord record)
        {
            Clan parentClan = ResolveClan(record.ParentClanId);
            Kingdom kingdom = ResolveKingdom(record.KingdomId);
            Hero deadLeader = ResolveHero(record.DeadLeaderId);

            if (!CanPartitionClan(parentClan) || kingdom == null || parentClan.Kingdom != kingdom)
                return true;

            if (RegencyBehavior.Instance?.TryGetRegency(parentClan, out _) == true)
                return false;

            if (parentClan.Leader == null || parentClan.Leader == deadLeader || parentClan.Leader.IsDead)
                return false;

            List<Town> snapshotFiefs = SplitIds(record.FiefIds)
                .Select(ResolveTown)
                .Where(f => f?.Settlement != null)
                .ToList();
            List<Town> fiefs = snapshotFiefs
                .Where(f => FeudalInheritancePlanner.IsHeritableFief(f, parentClan))
                .OrderByDescending(f => f.Prosperity)
                .ToList();

            TracePartition($"resolved partition fiefs for {parentClan.StringId}: snapshot={snapshotFiefs.Count}; parent_owned={fiefs.Count}; snapshot_status={FormatFiefStatus(snapshotFiefs)}; current_parent_fiefs={FormatFiefIds(GetPartitionableFiefs(parentClan))}.");

            int reservedFiefs = GetMainHeirReservedFiefs();
            if (fiefs.Count <= reservedFiefs)
                return true;

            List<Hero> secondaryHeirs = SplitIds(record.HeirIds)
                .Select(ResolveHero)
                .Where(h => IsStillEligibleSecondaryHeir(h, parentClan, deadLeader))
                .Distinct()
                .ToList();

            if (secondaryHeirs.Count == 0)
            {
                TracePartition($"skipped partition for {parentClan.StringId}: no available landed blood heirs after succession settled; current_leader={parentClan.Leader?.StringId ?? "none"}.");
                return true;
            }

            List<Clan> createdCadets = new List<Clan>();
            int spareFiefs = fiefs.Count - reservedFiefs;
            List<Town> partitionEstateFiefs = fiefs.ToList();
            FeudalTitleBehavior titleBehavior = Campaign.Current.GetCampaignBehavior<FeudalTitleBehavior>();
            List<FeudalTitleRecord> partitionEstateTitles = ResolvePartitionEstateTitles(titleBehavior, record.TitleIds, parentClan);
            FeudalTitleRecord primarySovereignTitle = titleBehavior?.GetTitle(record.PrimarySovereignTitleId);
            FeudalInheritancePlan inheritancePlan = FeudalInheritancePlanner.BuildPlan(
                parentClan,
                deadLeader,
                fiefs,
                secondaryHeirs,
                partitionEstateTitles,
                reservedFiefs,
                primarySovereignTitle?.TitleId ?? string.Empty);
            List<FeudalInheritancePackage> inheritanceUnits = inheritancePlan.SecondaryPackages;
            partitionEstateTitles = inheritancePlan.EstateTitles.ToList();
            int assignments = Math.Min(inheritanceUnits.Count, secondaryHeirs.Count);
            TracePartition($"applying partition for {parentClan.StringId}: fiefs={fiefs.Count}; reserved_personal_holdings={reservedFiefs}; spare_fiefs={spareFiefs}; estate_titles={FormatTitleIds(partitionEstateTitles)}; primary_chain={FormatTitleIds(inheritancePlan.PrimaryTitleChain)}; reserved_fiefs={FormatFiefIds(inheritancePlan.ReservedPersonalFiefs)}; units={inheritanceUnits.Count}; available_heirs={secondaryHeirs.Count}; planned_assignments={assignments}; heirs={FormatHeroIds(secondaryHeirs)}.");

            if (inheritanceUnits.Count == 0)
            {
                TracePartition($"skipped partition for {parentClan.StringId}: no secondary title packages or spare fiefs after reserving main heir holdings.");
                return true;
            }

            List<Tuple<Clan, Hero>> partitionBranches = new List<Tuple<Clan, Hero>>();
            if (parentClan.Leader != null && !parentClan.Leader.IsDead)
                partitionBranches.Add(Tuple.Create(parentClan, parentClan.Leader));

            for (int unitIndex = 0; unitIndex < inheritanceUnits.Count && unitIndex < secondaryHeirs.Count; unitIndex++)
            {
                FeudalInheritancePackage unit = inheritanceUnits[unitIndex];
                Town fief = unit.PrimaryFief;
                Hero heir = secondaryHeirs[unitIndex];
                bool createsIndependentRealm = IsEligibleCoequalSovereignPartition(
                    record,
                    parentClan,
                    kingdom,
                    primarySovereignTitle,
                    unit,
                    titleBehavior);

                if (!IsStillEligibleSecondaryHeir(heir, parentClan, deadLeader))
                {
                    TracePartition($"skipped partition heir {heir?.StringId ?? "unknown"} for {parentClan.StringId}: no longer eligible before assignment; current_clan={heir?.Clan?.StringId ?? "none"}.");
                    continue;
                }

                Clan cadetClan = CreatePartitionCadetBranch(parentClan, kingdom, heir, fief, record.ParentWasRulingClanAtDeath);
                if (cadetClan == null)
                {
                    TracePartition($"failed to create cadet branch for {parentClan.StringId}; heir={heir?.StringId ?? "unknown"}; fief={fief?.StringId ?? "unknown"}.");
                    continue;
                }

                List<Town> transferredFiefs = TransferPartitionUnitFiefs(cadetClan, unit);
                if (transferredFiefs.Count > 0)
                {
                    foreach (Town transferredFief in transferredFiefs)
                        titleBehavior?.LegalizePartitionInheritance(cadetClan, transferredFief);

                    List<FeudalTitleRecord> legalizedTitles = GetLegalizablePackageTitles(titleBehavior, unit, transferredFiefs);
                    foreach (FeudalTitleRecord title in legalizedTitles)
                        titleBehavior?.LegalizeTitleInheritance(cadetClan, title, "partition_secondary_package");

                    Kingdom successorKingdom = null;
                    string sovereignFailure = null;
                    if (createsIndependentRealm
                        && legalizedTitles.Any(title => title?.TitleId == unit.RootTitle?.TitleId)
                        && titleBehavior != null)
                    {
                        titleBehavior.TryPromotePartitionInheritanceToIndependentRealm(
                            kingdom,
                            cadetClan,
                            unit.RootTitle,
                            out successorKingdom,
                            out sovereignFailure);
                    }

                    partitionBranches.Add(Tuple.Create(cadetClan, heir));
                    createdCadets.Add(cadetClan);
                    Campaign.Current.GetCampaignBehavior<SuccessionYearlySummaryBehavior>()?.RecordPartitionCadetBranch(cadetClan, parentClan, kingdom, unit.PrimaryFief);
                    if (successorKingdom != null)
                    {
                        Campaign.Current.GetCampaignBehavior<SuccessionYearlySummaryBehavior>()?.RecordSovereignPartition(successorKingdom, unit.RootTitle);
                        ShowSovereignPartitionMessage(parentClan, cadetClan, heir, unit, kingdom, successorKingdom, titleBehavior);
                    }
                    else
                    {
                        ShowPartitionMessage(parentClan, cadetClan, heir, unit, kingdom, titleBehavior);
                    }

                    TracePartition($"created {cadetClan.StringId}; parent={parentClan.StringId}; leader={heir.StringId}; unit={unit.DebugName}; fiefs={FormatFiefIds(transferredFiefs)}; titles={FormatTitleIds(legalizedTitles)}; independent={successorKingdom?.StringId ?? "no"}; independent_failure={sovereignFailure ?? "none"}.");
                }
                else
                {
                    TracePartition($"fief transfer failed for partition cadet {cadetClan.StringId}; parent={parentClan.StringId}; heir={heir.StringId}; unit={unit.DebugName}; primary_fief={fief?.StringId ?? "none"}.");
                }
            }

            if (createdCadets.Count < assignments)
                TracePartition($"partition completed with fewer cadets than planned for {parentClan.StringId}: created={createdCadets.Count}; planned={assignments}; remaining_parent_fiefs={parentClan.Fiefs?.Count ?? 0}.");

            if (createdCadets.Count > 0)
            {
                titleBehavior?.RegisterPartitionHouseClaims(parentClan, deadLeader, partitionBranches, partitionEstateFiefs, partitionEstateTitles);
                int transferredGold = DistributePartitionGold(parentClan, createdCadets);
                Campaign.Current.GetCampaignBehavior<SuccessionYearlySummaryBehavior>()?.RecordPartitionGoldTransfer(transferredGold);
                Campaign.Current.GetCampaignBehavior<IdeologyBehavior>()?.RefreshKingdomIdeologies(kingdom);
            }

            return true;
        }

        private static bool IsEligibleCoequalSovereignPartition(
            PendingPartitionSuccessionRecord record,
            Clan parentClan,
            Kingdom parentKingdom,
            FeudalTitleRecord primarySovereignTitle,
            FeudalInheritancePackage inheritancePackage,
            FeudalTitleBehavior titleBehavior)
        {
            FeudalTitleRecord rootTitle = inheritancePackage?.RootTitle;
            if (record == null
                || !record.ParentWasRulingClanAtDeath
                || parentClan == null
                || parentKingdom?.RulingClan != parentClan
                || primarySovereignTitle == null
                || rootTitle == null
                || titleBehavior == null
                || !primarySovereignTitle.IsActive
                || !rootTitle.IsActive
                || rootTitle.TitleId == primarySovereignTitle.TitleId
                || rootTitle.TitleType != primarySovereignTitle.TitleType
                || inheritancePackage.Fiefs.Count == 0
                || inheritancePackage.PrimaryFief?.Settlement == null)
            {
                return false;
            }

            string parentClanId = parentClan.StringId;
            if (!string.Equals(primarySovereignTitle.DeJureHolderClanId, parentClanId, StringComparison.Ordinal)
                || !string.Equals(primarySovereignTitle.DeFactoHolderClanId, parentClanId, StringComparison.Ordinal)
                || !string.Equals(rootTitle.DeJureHolderClanId, parentClanId, StringComparison.Ordinal)
                || !string.Equals(rootTitle.DeFactoHolderClanId, parentClanId, StringComparison.Ordinal))
            {
                return false;
            }

            bool hasHigherBindingTitle = titleBehavior.GetTitlesHeldByClan(parentClan, deJure: false)
                .Any(title => title != null
                    && title.IsActive
                    && title.TitleType > primarySovereignTitle.TitleType
                    && string.Equals(title.DeJureHolderClanId, parentClanId, StringComparison.Ordinal));
            return !hasHigherBindingTitle;
        }

        private static List<Town> TransferPartitionUnitFiefs(Clan cadetClan, FeudalInheritancePackage unit)
        {
            List<Town> transferredFiefs = new List<Town>();
            if (cadetClan?.Leader == null || unit?.Fiefs == null)
                return transferredFiefs;

            foreach (Town fief in unit.Fiefs
                .Where(f => f?.Settlement != null)
                .OrderBy(f => f == unit.PrimaryFief ? 0 : 1)
                .ThenByDescending(f => f.Prosperity))
            {
                if (fief.OwnerClan != cadetClan)
                    ChangeOwnerOfSettlementAction.ApplyByDefault(cadetClan.Leader, fief.Settlement);

                if (fief.OwnerClan == cadetClan)
                    transferredFiefs.Add(fief);
            }

            return transferredFiefs;
        }

        private static List<FeudalTitleRecord> GetLegalizablePackageTitles(
            FeudalTitleBehavior titleBehavior,
            FeudalInheritancePackage unit,
            List<Town> transferredFiefs)
        {
            if (titleBehavior == null || unit?.Titles == null || transferredFiefs == null || transferredFiefs.Count == 0)
                return new List<FeudalTitleRecord>();

            HashSet<string> transferredSettlementIds = new HashSet<string>(transferredFiefs
                .Where(fief => fief?.Settlement != null)
                .Select(fief => fief.Settlement.StringId));

            return unit.Titles
                .Where(title => title != null && title.IsActive && UnitTitleFiefsTransferred(titleBehavior, title, unit, transferredSettlementIds))
                .OrderBy(title => title.TitleType)
                .ToList();
        }

        private static bool UnitTitleFiefsTransferred(
            FeudalTitleBehavior titleBehavior,
            FeudalTitleRecord title,
            FeudalInheritancePackage unit,
            HashSet<string> transferredSettlementIds)
        {
            if (titleBehavior == null || title == null || unit?.Fiefs == null || transferredSettlementIds == null)
                return false;

            HashSet<string> footprintSettlementIds = new HashSet<string>(titleBehavior.GetDescendantBaronyTitles(title)
                .Where(barony => barony != null && !string.IsNullOrWhiteSpace(barony.CapitalSettlementId))
                .Select(barony => barony.CapitalSettlementId));

            List<Town> unitFiefsUnderTitle = unit.Fiefs
                .Where(fief => fief?.Settlement != null && footprintSettlementIds.Contains(fief.Settlement.StringId))
                .ToList();

            return unitFiefsUnderTitle.Count > 0
                && unitFiefsUnderTitle.All(fief => transferredSettlementIds.Contains(fief.Settlement.StringId));
        }

        private static int DistributePartitionGold(Clan parentClan, IReadOnlyCollection<Clan> cadetClans)
        {
            if (parentClan?.Leader == null || cadetClans == null || cadetClans.Count == 0)
                return 0;

            int shareCount = cadetClans.Count + 1;
            int share = parentClan.Leader.Gold / shareCount;
            if (share <= 0)
                return 0;

            int totalTransferred = 0;
            foreach (Clan cadetClan in cadetClans)
            {
                if (cadetClan?.Leader == null || cadetClan.Leader == parentClan.Leader)
                    continue;

                int amount = Math.Min(share, parentClan.Leader.Gold);
                if (amount <= 0)
                    break;

                GiveGoldAction.ApplyBetweenCharacters(parentClan.Leader, cadetClan.Leader, amount, false);
                totalTransferred += amount;
                TracePartition($"transferred inheritance gold; parent={parentClan.StringId}; cadet={cadetClan.StringId}; amount={amount}.");
            }

            return totalTransferred;
        }

        internal string PreviewAbdicationCadetName(CrownAccessionRecord record)
        {
            Clan household = record.EndowmentHouse;
            if (record.Heir?.Spouse?.Clan == household
                && household != null && record.PreviousHouse != null && record.PreviousHouse != household)
                return DynasticHeirBehavior.BuildRoyalHeiressCadetName(record.PreviousHouse, household).ToString();
            return BuildCadetClanName(household, GetCadetNamingFiefIds(record).Select(Settlement.Find)
                .Select(settlement => settlement?.Town).FirstOrDefault(town => town != null)).ToString();
        }

        internal bool PrepareAbdicationCadet(CrownAccessionRecord record)
        {
            if (record == null) return false;
            if (record.Cadet == null && !string.IsNullOrWhiteSpace(record.CadetId))
                record.Cadet = Clan.All.FirstOrDefault(clan => clan.StringId == record.CadetId);
            // A resumed household may already exist even though its estate is still pending.
            PreservePendingCadetHead(record.Cadet, record.Heir);
            try { return PrepareAbdicationCadetCore(record); }
            finally { PreservePendingCadetHead(record.Cadet, record.Heir); }
        }

        private bool PrepareAbdicationCadetCore(CrownAccessionRecord record)
        {
            Clan parent = record.EndowmentHouse;
            Hero heir = record.Heir;
            if (!TryValidateCadetHousehold(record, out var household)) return false;
            Settlement home = record.EndowmentFiefs.Select(Settlement.Find).FirstOrDefault()
                ?? parent.HomeSettlement ?? Settlement.All.FirstOrDefault(s => s.IsTown || s.IsCastle);
            if (home == null || string.IsNullOrWhiteSpace(record.CadetId))
                return DeferCadetPreparation(record, "missing residence or cadet identity");
            if (record.PreserveHeirParty && !record.HeirPartyCaptured)
            {
                var party = heir.PartyBelongedTo;
                record.PreservedHeirParty = CanRetainChallengeParty(heir, parent, party) ? party : null;
                record.HeirPartyCaptured = true;
            }
            var retainedParty = record.PreservedHeirParty;
            if (retainedParty != null && (!retainedParty.IsActive || !retainedParty.IsLordParty
                || retainedParty == MobileParty.MainParty || retainedParty.LeaderHero != heir
                || retainedParty.MapEvent != null || retainedParty.SiegeEvent != null
                || (retainedParty.ActualClan != parent && retainedParty.ActualClan != record.Cadet)))
                return DeferCadetPreparation(record, "retained heir party is unavailable");
            // Save the clan reference immediately. Recovery initializes this same object,
            // never creates a second branch or computes a different inheritance package.
            if (record.Cadet == null) record.Cadet = Clan.CreateClan(record.CadetId);
            Clan cadet = record.Cadet;
            if (cadet.IsEliminated) return DeferCadetPreparation(record, "cadet house was eliminated");
            if (!record.CadetInitialized)
            {
                var visuals = KingdomVisualHelper.ResolveCadetBranchVisuals(parent, record.CadetId);
                var name = new TextObject("{=!}" + record.CadetName);
                cadet.ChangeClanName(name, name);
                cadet.Culture = parent.Culture ?? heir.Culture;
                cadet.Banner = visuals.Banner;
                cadet.UpdateBannerColor(visuals.PrimaryColor, visuals.SecondaryColor);
                cadet.Color = visuals.PrimaryColor;
                cadet.Color2 = visuals.SecondaryColor;
                cadet.IsNoble = true;
                cadet.BasicTroop = parent.BasicTroop;
                cadet.Renown = parent.Renown;
                SetClanTier(cadet, parent.Tier - C.PartitionSuccessionCadetTierPenalty);
                cadet.SetInitialHomeSettlement(home);
                KingdomVisualHelper.AssignCadetKingdom(cadet, record.HouseholdRealm, visuals);
                record.CadetInitialized = true;
            }
            if (cadet.Kingdom != record.HouseholdRealm)
                return DeferCadetPreparation(record, "cadet house changed realm");
            // Found the house before moving dependants. An interrupted later transfer
            // must not leave a registered clan without a head for native daily finance.
            foreach (Hero member in household)
            {
                if (!ValidateCadetMember(record, member)) return false;
                try
                {
                    if (member.Clan == parent) TransferHeroToClan(member, parent, cadet, preserveNonLeaderArmy: true,
                        retainedParty: retainedParty, installFoundingHead: member == heir);
                }
                finally
                {
                    if (member == heir) PreservePendingCadetHead(cadet, heir);
                }
                if (member.Clan != cadet)
                    return DeferCadetPreparation(record, "household transfer incomplete: " + member.StringId);
            }
            if (retainedParty != null)
            {
                retainedParty.ActualClan = cadet;
                retainedParty.Party.SetVisualAsDirty();
                if (heir.PartyBelongedTo != retainedParty || retainedParty.ActualClan != cadet)
                    return DeferCadetPreparation(record, "retained heir party transfer is incomplete");
            }
            if (heir.Age < SuccessionLawHelper.GetAgeOfMajority())
            {
                if (RegencyBehavior.Instance?.EnsureCrownHeirRegency(cadet, heir, record.Predecessor) != true)
                    return DeferCadetPreparation(record, "awaiting a usable regent");
            }
            else if (cadet.Leader != heir) cadet.SetLeader(heir);
            cadet.ConsiderAndUpdateHomeSettlement();
            Campaign.Current.GetCampaignBehavior<CadetHouseholdBehavior>()?.EnsureFoundingMembers(cadet);
            if (!record.CadetAnnounced)
            {
                Campaign.Current.GetCampaignBehavior<DynasticClaimBehavior>()?.RegisterCadetDynasticTie(
                    cadet, heir, record.Realm, parent, record.ChallengeConcession ? "succession_concession_cadet"
                        : record.IsAbdication ? "abdication_cadet" : "crown_accession_cadet");
                record.CadetAnnounced = true;
                CampaignEventDispatcher.Instance.OnClanCreated(cadet, isCompanion: false);
            }
            record.AbdicationFailure = null;
            return true;
        }

        private static bool TryValidateCadetHousehold(CrownAccessionRecord record, out List<Hero> household)
        {
            household = new List<Hero>();
            if (record.EndowmentHouse == null || record.EndowmentHouse.IsEliminated || record.Heir == null
                || record.Household == null || !record.Household.Contains(record.Heir.StringId))
                return DeferCadetPreparation(record, "missing source house or founding heir in household");
            // Resolve every member before registering a clan or transferring anyone.
            household.Add(record.Heir);
            foreach (string id in record.Household.Where(id => id != record.Heir.StringId).Distinct())
            {
                Hero member = CrownAccessionBehavior.ResolveAbdicationHero(id);
                if (member == null) return DeferCadetPreparation(record, "unresolved household member: " + id);
                household.Add(member);
            }
            return household.All(member => ValidateCadetMember(record, member));
        }

        private static bool ValidateCadetMember(CrownAccessionRecord record, Hero member)
        {
            Clan parent = record.EndowmentHouse;
            if (!member.IsAlive || member.IsDisabled || member == Hero.MainHero || member == parent.Leader
                || member == RegencyBehavior.Instance?.GetLegalClanHead(parent))
                return DeferCadetPreparation(record, "unavailable or protected household member: " + member.StringId);
            if (member.Clan == null || (member.Clan != parent && member.Clan != record.Cadet))
                return DeferCadetPreparation(record, "household member changed house: " + member.StringId
                    + "; current_house=" + member.Clan?.StringId);
            if (member.IsPrisoner || member.IsTraveling || member.PartyBelongedTo?.MapEvent != null
                || member.PartyBelongedTo?.SiegeEvent != null)
                return DeferCadetPreparation(record, "household member temporarily unavailable: " + member.StringId);
            return true;
        }

        private static bool DeferCadetPreparation(CrownAccessionRecord record, string reason)
        {
            if (record.AbdicationFailure != reason)
                BellumCivileLogger.Log($"Cadet household preparation deferred; cadet={record.CadetId}; heir={record.Heir?.StringId}; reason={reason}.");
            record.AbdicationFailure = reason;
            return false;
        }

        internal static void PreservePendingCadetHead(Clan cadet, Hero heir)
        {
            // A provisional head does not complete household preparation or release its estate.
            if (cadet != null && !cadet.IsEliminated && cadet.Leader == null && heir?.IsAlive == true && heir.Clan == cadet)
                cadet.SetLeader(heir);
        }

        private Clan CreatePartitionCadetBranch(Clan parentClan, Kingdom kingdom, Hero heir, Town fief, bool parentWasRulingClanAtDeath)
        {
            if (parentClan == null || kingdom == null || heir == null || fief?.Settlement == null)
                return null;

            return CreatePartitionCadetAtResidence(parentClan, kingdom, heir, fief, fief.StringId, parentWasRulingClanAtDeath);
        }

        // Not called by live succession until Crown allocation and promotion are wired together.
        private Clan CreateCrownPartitionCadetBranch(Clan parentClan, Kingdom kingdom, Hero heir,
            FeudalTitleRecord crown, FeudalTitleBehavior titles)
        {
            if (parentClan == null || kingdom?.RulingClan != parentClan || heir == null
                || crown?.IsActive != true || crown.TitleType < FeudalTitleType.Kingdom || titles == null
                || crown.DeJureHolderClanId != parentClan.StringId || crown.DeFactoHolderClanId != parentClan.StringId)
                return null;
            Town residence = titles.ResolveCrownPartitionResidence(kingdom, crown);
            if (residence == null) return null;
            return CreatePartitionCadetAtResidence(parentClan, kingdom, heir, residence, crown.TitleId, true);
        }

        private Clan CreatePartitionCadetAtResidence(Clan parentClan, Kingdom kingdom, Hero heir,
            Town residence, string identitySeed, bool parentWasRulingClanAtDeath, CrownPartitionPromotionRecord promotion = null)
        {
            string clanId = promotion?.FounderId ?? BuildUniqueClanIdFromSeed(parentClan, identitySeed, heir);
            if (promotion != null && (!promotion.FounderCreationStarted || promotion.Founder != null
                || string.IsNullOrWhiteSpace(clanId) || Clan.All.Any(c => c.StringId == clanId))) return null;
            Clan cadetClan = Clan.CreateClan(clanId);
            if (promotion != null) promotion.Founder = cadetClan;
            TextObject clanName = BuildCadetClanName(parentClan, residence);
            KingdomVisualHelper.CadetBranchVisuals visuals = KingdomVisualHelper.ResolveCadetBranchVisuals(parentClan, clanId);
            Banner banner = visuals.Banner;
            uint primary = visuals.PrimaryColor;
            uint icon = visuals.SecondaryColor;

            cadetClan.ChangeClanName(clanName, clanName);
            cadetClan.Culture = parentClan.Culture ?? heir.Culture;
            cadetClan.Banner = banner;
            cadetClan.UpdateBannerColor(primary, icon);
            cadetClan.Color = primary;
            cadetClan.Color2 = icon;
            cadetClan.IsNoble = true;
            cadetClan.BasicTroop = parentClan.BasicTroop;
            cadetClan.Renown = parentClan.Renown;
            SetClanTier(cadetClan, parentClan.Tier - C.PartitionSuccessionCadetTierPenalty);
            cadetClan.SetInitialHomeSettlement(residence.Settlement);
            KingdomVisualHelper.AssignCadetKingdom(cadetClan, kingdom, visuals);

            TransferHouseholdToCadet(parentClan, cadetClan, heir);
            cadetClan.SetLeader(heir);
            cadetClan.ConsiderAndUpdateHomeSettlement();
            Campaign.Current.GetCampaignBehavior<CadetHouseholdBehavior>()?.EnsureFoundingMembers(cadetClan);
            CampaignEventDispatcher.Instance.OnClanCreated(cadetClan, isCompanion: false);

            DynasticClaimBehavior claimBehavior = Campaign.Current.GetCampaignBehavior<DynasticClaimBehavior>();
            if (parentWasRulingClanAtDeath)
            {
                claimBehavior?.RegisterClaim(cadetClan, heir, kingdom, parentClan, "partition_cadet");
                claimBehavior?.RegisterCadetDynasticTie(cadetClan, heir, kingdom, parentClan, "partition_cadet");
            }

            ApplyCadetParentRelationBonus(cadetClan, parentClan);
            if (promotion != null) promotion.FounderInitializationCompleted = true;

            return cadetClan;
        }

        private static void ApplyCadetParentRelationBonus(Clan cadetClan, Clan parentClan)
        {
            Hero cadetLeader = cadetClan?.Leader;
            Hero parentLeader = parentClan?.Leader;
            if (cadetLeader == null || parentLeader == null || cadetLeader == parentLeader)
                return;

            RelationMemoryService.ApplyChangeWithDefaultDuration(
                cadetLeader,
                parentLeader,
                C.CadetBranchParentRelationBonus,
                false,
                RelationMemorySources.CadetHouseFounded, RelationMemoryScope.House);
        }

        private static void TransferHouseholdToCadet(Clan parentClan, Clan cadetClan, Hero heir)
        {
            if (parentClan == null || cadetClan == null || heir == null)
                return;

            List<Hero> movers = new List<Hero> { heir };
            if (heir.Spouse != null && heir.Spouse.Clan == parentClan && heir.Spouse != parentClan.Leader)
                movers.Add(heir.Spouse);

            // Partition can prepare a cadet before the pending Crown transfer resumes.
            if (CrownAccessionBehavior.Instance?.IsPendingCrownHeir(heir) != true)
                movers.AddRange(heir.Children
                    .Where(child => child != null && child.Clan == parentClan && child != parentClan.Leader));

            foreach (Hero hero in movers.Distinct().ToList())
                TransferHeroToClan(hero, parentClan, cadetClan);
        }

        internal static bool CanRetainChallengeParty(Hero heir, Clan parent, MobileParty party) =>
            heir != null && parent != null && party != null && party.IsActive && party.IsLordParty
            && party != MobileParty.MainParty && party.LeaderHero == heir && party.ActualClan == parent
            && party.MapEvent == null && party.SiegeEvent == null;

        private static void TransferHeroToClan(Hero hero, Clan parentClan, Clan cadetClan, bool preserveNonLeaderArmy = false,
            MobileParty retainedParty = null, bool installFoundingHead = false)
        {
            if (hero == null || cadetClan == null || hero.Clan == cadetClan)
                return;

            Clan oldClan = hero.Clan;
            if (hero.GovernorOf != null)
                ChangeGovernorAction.RemoveGovernorOf(hero);

            if (hero.PartyBelongedTo != null)
            {
                MobileParty party = hero.PartyBelongedTo;
                if (party.Army != null && (!preserveNonLeaderArmy || party.LeaderHero == hero))
                {
                    if (party.Army.LeaderParty == party)
                        DisbandArmyAction.ApplyByUnknownReason(party.Army);
                    else
                        party.Army = null;
                }

                if (party == retainedParty)
                {
                    // ActualClan invokes the engine's war-party removal/addition hooks.
                    // Retain the roster and inventory; do not recreate or fund this party.
                    party.ActualClan = cadetClan;
                }
                else
                {
                    bool wasPartyLeader = party.LeaderHero == hero;
                    party.MemberRoster.RemoveTroop(hero.CharacterObject);
                    MakeHeroFugitiveAction.Apply(hero);
                    if (wasPartyLeader && party.IsLordParty)
                        DisbandPartyAction.StartDisband(party);
                }
            }

            try
            {
                if (hero.Clan == parentClan) hero.Clan = cadetClan;
            }
            finally
            {
                if (installFoundingHead) PreservePendingCadetHead(cadetClan, hero);
            }

            foreach (Hero member in oldClan?.Heroes ?? Enumerable.Empty<Hero>())
                member.UpdateHomeSettlement();
            foreach (Hero member in cadetClan.Heroes)
                member.UpdateHomeSettlement();
        }

        private static void RepairPartitionCadetParties()
        {
            foreach (MobileParty party in MobileParty.All.ToList())
            {
                if (party == null || !party.IsLordParty)
                    continue;

                Hero leader = party.LeaderHero;
                Clan leaderClan = leader?.Clan;
                if (leader == null || leaderClan == null || !leaderClan.StringId.StartsWith("bc_partition_", StringComparison.OrdinalIgnoreCase))
                    continue;

                Clan actualClan = ResolveMobilePartyActualClan(party);
                if (actualClan == null || actualClan == leaderClan)
                    continue;

                if (party.Army != null)
                {
                    if (party.Army.LeaderParty == party)
                        DisbandArmyAction.ApplyByUnknownReason(party.Army);
                    else
                        party.Army = null;
                }

                party.MemberRoster.RemoveTroop(leader.CharacterObject);
                MakeHeroFugitiveAction.Apply(leader);
                DisbandPartyAction.StartDisband(party);
                TracePartition($"repaired partition cadet party ownership mismatch; party={party.StringId}; leader={leader.StringId}; actual_clan={actualClan.StringId}; leader_clan={leaderClan.StringId}.");
            }
        }

        private static Clan ResolveMobilePartyActualClan(MobileParty party)
        {
            if (party == null)
                return null;

            if (!_mobilePartyActualClanReflectionInitialized)
            {
                _mobilePartyActualClanProperty = typeof(MobileParty).GetProperty("ActualClan", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                _mobilePartyActualClanReflectionInitialized = true;
            }

            try
            {
                return _mobilePartyActualClanProperty?.GetValue(party) as Clan;
            }
            catch
            {
                return null;
            }
        }

        private static List<Town> GetPartitionableFiefs(Clan clan)
        {
            return clan?.Fiefs?
                .Where(f => FeudalInheritancePlanner.IsHeritableFief(f, clan))
                .OrderByDescending(f => f.Prosperity)
                .ToList() ?? new List<Town>();
        }

        private static List<Hero> GetOrderedNaturalHeirs(Clan clan, Hero deadLeader)
        {
            if (clan == null || deadLeader == null)
                return new List<Hero>();

            SuccessionLawSet laws = SuccessionLawHelper.GetLawsForClan(clan);
            return SuccessionLawHelper.GetLegalSuccessionLine(clan, deadLeader, laws)
                .Where(hero => SuccessionLawHelper.IsBloodRelative(hero, deadLeader))
                .ToList();
        }

        private static bool IsValidNaturalHeirAtDeath(Hero hero, Clan clan, Hero deadLeader)
        {
            return hero != null
                && clan != null
                && deadLeader != null
                && hero != deadLeader
                && hero.Clan == clan
                && hero.IsAlive
                && hero.IsLord
                && !hero.IsChild
                && hero.Age >= SuccessionLawHelper.GetAgeOfMajority()
                && !hero.IsDisabled
                && SuccessionLawHelper.IsBloodRelative(hero, deadLeader);
        }

        private static bool IsStillEligibleSecondaryHeir(Hero hero, Clan parentClan, Hero deadLeader)
        {
            return CrownAccessionBehavior.Instance?.HasInheritanceAdvance(deadLeader, hero) != true
                && IsValidNaturalHeirAtDeath(hero, parentClan, deadLeader)
                && SuccessionLawHelper.IsEligibleUnderSuccessionLaws(
                    hero,
                    deadLeader,
                    SuccessionLawHelper.GetLawsForClan(parentClan))
                && !ShouldReserveNaturalHeirWithMainClan(hero, parentClan);
        }

        private static bool ShouldReserveNaturalHeirWithMainClan(Hero hero, Clan parentClan)
        {
            return hero != null && parentClan?.Leader != null && hero == parentClan.Leader;
        }

        private static bool CanPartitionClan(Clan clan)
        {
            bool isPlayerClan = clan == Clan.PlayerClan;
            return clan != null
                && !clan.IsEliminated
                && (isPlayerClan || !clan.IsMinorFaction)
                && (isPlayerClan || !clan.IsClanTypeMercenary)
                && (isPlayerClan || !clan.IsUnderMercenaryService)
                && !clan.IsBanditFaction
                && clan.Kingdom != null
                && !clan.Kingdom.IsEliminated;
        }

        private static int GetMainHeirReservedFiefs()
        {
            return Math.Max(1, BellumCivileOptions.PartitionSuccessionMainHeirReservedFiefs);
        }

        private Clan ResolveVictimClan(Hero victim)
        {
            if (victim?.Clan != null)
                return victim.Clan;

            string victimId = victim?.StringId;
            if (string.IsNullOrWhiteSpace(victimId))
                return null;

            return Clan.All.FirstOrDefault(c =>
                c != null
                && _knownClanLeaderIds.TryGetValue(c.StringId, out string leaderId)
                && leaderId == victimId);
        }

        private bool WasKnownClanLeader(Clan clan, Hero victim)
        {
            if (clan == null || victim == null)
                return false;

            if (clan.Leader == victim)
                return true;

            return _knownClanLeaderIds.TryGetValue(clan.StringId, out string leaderId)
                && leaderId == victim.StringId;
        }

        private void RefreshKnownClanLeaders()
        {
            EnsureCollectionsInitialized();
            _knownClanLeaderIds.Clear();

            foreach (Clan clan in Clan.All)
            {
                if (clan?.Leader == null || clan.Leader.IsDead)
                    continue;

                _knownClanLeaderIds[clan.StringId] = clan.Leader.StringId;
            }
        }

        private static TextObject BuildCadetClanName(Clan parentClan, Town fief)
        {
            string fiefName = CleanFiefName(fief?.Settlement?.Name?.ToString() ?? fief?.Name?.ToString() ?? parentClan?.Name?.ToString() ?? "Cadet");
            HashSet<string> existingClanNames = GetLivingClanNames();

            foreach (string rootName in GetCadetNameRoots(parentClan, fief, fiefName))
            {
                TextObject candidate = ApplyCadetCultureNaming(parentClan, fief, rootName);
                if (!ClanNameExists(candidate.ToString(), existingClanNames))
                    return candidate;
            }

            TextObject fallback = BuildLocalizedCadetName(
                "BC_CadetName_FiefParent",
                "{FIEF_NAME}-{PARENT_CLAN}",
                ("FIEF_NAME", new TextObject("{=!}" + fiefName)),
                ("PARENT_CLAN", new TextObject("{=!}" + CleanClanName(parentClan?.Name?.ToString()))));
            string fallbackName = fallback.ToString();
            if (!ClanNameExists(fallbackName, existingClanNames))
                return fallback;

            int suffix = 2;
            TextObject uniqueFallback = fallback;
            while (ClanNameExists(uniqueFallback.ToString(), existingClanNames))
            {
                uniqueFallback = BuildLocalizedCadetName(
                    "BC_CadetName_Numbered",
                    "{BASE_NAME} {NUMBER}",
                    ("BASE_NAME", fallback),
                    ("NUMBER", new TextObject("{=!}" + suffix++)));
            }

            return uniqueFallback;
        }

        private static TextObject ApplyCadetCultureNaming(Clan parentClan, Town fief, string rootName)
        {
            TextObject root = new TextObject("{=!}" + rootName);
            string cultureId = parentClan?.Culture?.StringId?.ToLowerInvariant() ?? string.Empty;
            switch (cultureId)
            {
                case "vlandia":
                    return BuildLocalizedCadetName("BC_CadetName_Vlandia", "dey {ROOT_NAME}", ("ROOT_NAME", root));
                case "battania":
                    return BuildLocalizedCadetName("BC_CadetName_Battania", "fen {ROOT_NAME}", ("ROOT_NAME", root));
                case "aserai":
                    return BuildLocalizedCadetName("BC_CadetName_Aserai", "Banu {ROOT_NAME}", ("ROOT_NAME", root));
                case "sturgia":
                    return BuildLocalizedCadetName("BC_CadetName_Sturgia", "{ROOT_NAME}ing", ("ROOT_NAME", root));
                case "nord":
                    return BuildLocalizedCadetName("BC_CadetName_Nord", "{ROOT_NAME}ing", ("ROOT_NAME", root));
                case "khuzait":
                    return BuildLocalizedCadetName("BC_CadetName_Khuzait", "{ROOT_NAME}it", ("ROOT_NAME", root));
                case "empire":
                    return BuildImperialCadetName(parentClan, fief, root);
                default:
                    return BuildLocalizedCadetName("BC_CadetName_Default", "{ROOT_NAME}", ("ROOT_NAME", root));
            }
        }

        private static TextObject BuildImperialCadetName(Clan parentClan, Town fief, TextObject rootName)
        {
            switch (ResolveImperialCadetSuffixIndex(parentClan, fief))
            {
                case 0:
                    return BuildLocalizedCadetName("BC_CadetName_EmpireEs", "{ROOT_NAME}es", ("ROOT_NAME", rootName));
                case 1:
                    return BuildLocalizedCadetName("BC_CadetName_EmpireOs", "{ROOT_NAME}os", ("ROOT_NAME", rootName));
                default:
                    return BuildLocalizedCadetName("BC_CadetName_EmpireIs", "{ROOT_NAME}is", ("ROOT_NAME", rootName));
            }
        }

        private static TextObject BuildLocalizedCadetName(string id, string fallback, params (string Name, TextObject Value)[] variables)
        {
            TextObject text = new TextObject("{=" + id + "}" + fallback);
            foreach (var variable in variables)
                text.SetTextVariable(variable.Name, variable.Value ?? new TextObject(string.Empty));
            return text;
        }

        private static IEnumerable<string> GetCadetNameRoots(Clan parentClan, Town fief, string fiefName)
        {
            HashSet<string> yielded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (yielded.Add(fiefName))
                yield return fiefName;

            foreach (Village village in fief?.Villages ?? Enumerable.Empty<Village>())
            {
                string villageName = CleanFiefName(village?.Settlement?.Name?.ToString());
                if (!string.IsNullOrWhiteSpace(villageName) && yielded.Add(villageName))
                    yield return villageName;
            }
        }

        private static HashSet<string> GetLivingClanNames()
        {
            return new HashSet<string>(
                Clan.All
                    .Where(c => c != null && !c.IsEliminated)
                    .Select(c => NormalizeClanName(c.Name?.ToString()))
                    .Where(n => !string.IsNullOrWhiteSpace(n)),
                StringComparer.OrdinalIgnoreCase);
        }

        private static bool ClanNameExists(string name, HashSet<string> existingClanNames)
        {
            return existingClanNames != null && existingClanNames.Contains(NormalizeClanName(name));
        }

        private static string NormalizeClanName(string name)
        {
            return string.IsNullOrWhiteSpace(name)
                ? string.Empty
                : name.Trim().ToLowerInvariant();
        }

        private static string CleanClanName(string name)
        {
            return string.IsNullOrWhiteSpace(name) ? "Cadet" : name.Trim();
        }

        private static int ResolveImperialCadetSuffixIndex(Clan parentClan, Town fief)
        {
            string seed = $"{parentClan?.StringId ?? string.Empty}|{fief?.StringId ?? string.Empty}";
            int hash = 17;
            unchecked
            {
                for (int i = 0; i < seed.Length; i++)
                    hash = (hash * 31) + seed[i];
            }

            return Math.Abs(hash) % 3;
        }

        private static string CleanFiefName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return "Cadet";

            string cleaned = name.Trim();
            if (cleaned.EndsWith(" Castle", StringComparison.OrdinalIgnoreCase))
                cleaned = cleaned.Substring(0, cleaned.Length - " Castle".Length).Trim();

            return string.IsNullOrWhiteSpace(cleaned) ? name.Trim() : cleaned;
        }

        private static string BuildUniqueClanIdFromSeed(Clan parentClan, string identitySeed, Hero heir)
        {
            string baseId = $"bc_partition_{parentClan.StringId}_{identitySeed}_{heir.StringId}".ToLowerInvariant();
            string candidate = SanitizeId(baseId);
            string unique = candidate;
            int suffix = 1;
            while (Clan.All.Any(c => c.StringId == unique))
            {
                suffix++;
                unique = $"{candidate}_{suffix}";
            }

            return unique;
        }

        private static string SanitizeId(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "bc_partition_clan";

            char[] chars = value.Select(ch => char.IsLetterOrDigit(ch) || ch == '_' ? ch : '_').ToArray();
            return new string(chars);
        }

        private static void SetClanTier(Clan clan, int targetTier)
        {
            if (clan == null)
                return;

            InitializeTierReflection();
            int minTier = Campaign.Current.Models.ClanTierModel.MinClanTier;
            int maxTier = Campaign.Current.Models.ClanTierModel.MaxClanTier;
            int clampedTier = Math.Max(minTier, Math.Min(maxTier, targetTier));

            clan.Renown = Campaign.Current.Models.ClanTierModel.GetRequiredRenownForTier(clampedTier);

            try
            {
                _clanTierProperty?.SetValue(clan, clampedTier);
            }
            catch
            {
                _clanTierField?.SetValue(clan, clampedTier);
            }
        }

        private static void InitializeTierReflection()
        {
            if (_tierReflectionInitialized)
                return;

            _tierReflectionInitialized = true;
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            _clanTierProperty = typeof(Clan).GetProperty("Tier", flags);
            _clanTierField = typeof(Clan).GetField("_tier", flags);
        }

        private static void ShowPartitionMessage(Clan parentClan, Clan cadetClan, Hero heir, FeudalInheritancePackage inheritancePackage, Kingdom kingdom, FeudalTitleBehavior titleBehavior)
        {
            TextObject text = new TextObject("{=BC_PartitionSuccession_CadetFounded}Following the recent succession on the {PARENT_CLAN}, {HEIR_NAME} has inherited the {TITLE_NAME} as {POSSESSIVE} bloodright, henceforth {POSSESSIVE} side of the family shall be known as the {CADET_CLAN}.");
            text.SetTextVariable("PARENT_CLAN", parentClan?.Name ?? new TextObject("?"));
            text.SetTextVariable("HEIR_NAME", heir?.Name ?? new TextObject("?"));
            text.SetTextVariable("CADET_CLAN", cadetClan?.Name ?? new TextObject("?"));
            text.SetTextVariable("TITLE_NAME", ResolveInheritedTitleName(inheritancePackage, titleBehavior));
            text.SetTextVariable("POSSESSIVE", heir?.IsFemale == true
                ? new TextObject("{=BC_Pronoun_Her}her")
                : new TextObject("{=BC_Pronoun_His}his"));
            BellumCivileNotifications.Show(text, BellumNotificationColors.Inheritance, primaryKingdom: kingdom, primaryClan: cadetClan, secondaryClan: parentClan);
        }

        private static void ShowSovereignPartitionMessage(
            Clan parentClan,
            Clan cadetClan,
            Hero heir,
            FeudalInheritancePackage inheritancePackage,
            Kingdom parentKingdom,
            Kingdom successorKingdom,
            FeudalTitleBehavior titleBehavior)
        {
            TextObject text = new TextObject("{=BC_PartitionSuccession_SovereignRealmFounded}With no higher title to bind the inheritance, {HEIR_NAME} has inherited {TITLE_NAME} as an independent realm. The domains of {PARENT_CLAN} have been divided among its blood heirs.");
            text.SetTextVariable("HEIR_NAME", heir?.Name ?? new TextObject("?"));
            text.SetTextVariable("TITLE_NAME", ResolveInheritedTitleName(inheritancePackage, titleBehavior));
            text.SetTextVariable("PARENT_CLAN", parentClan?.Name ?? new TextObject("?"));
            BellumCivileNotifications.Show(
                text,
                BellumNotificationColors.Inheritance,
                primaryKingdom: successorKingdom,
                secondaryKingdom: parentKingdom,
                primaryClan: cadetClan,
                secondaryClan: parentClan,
                isMajorEvent: true);
        }

        private static TextObject ResolveInheritedTitleName(FeudalInheritancePackage inheritancePackage, FeudalTitleBehavior titleBehavior)
        {
            FeudalTitleRecord packageTitle = inheritancePackage?.RootTitle;
            if (packageTitle != null)
                return FormatFeudalTitleText(packageTitle);

            Town primaryFief = inheritancePackage?.PrimaryFief;
            if (primaryFief?.Settlement != null
                && titleBehavior != null
                && titleBehavior.TryGetBarony(primaryFief.Settlement, out FeudalTitleRecord baronyTitle)
                && baronyTitle != null)
            {
                return FormatFeudalTitleText(baronyTitle);
            }

            return primaryFief?.Settlement?.Name ?? primaryFief?.Name ?? new TextObject("?");
        }

        private static TextObject FormatFeudalTitleText(FeudalTitleRecord title)
        {
            string displayName = FeudalTitleDisplayHelper.FormatTitleName(title);
            if (string.IsNullOrWhiteSpace(displayName))
                displayName = title?.TitleId ?? "?";

            return new TextObject("{=!}" + displayName);
        }

        private static bool IsExpired(PendingPartitionSuccessionRecord record)
        {
            return record != null
                && (record.ReadyDate + CampaignTime.Days(C.PartitionSuccessionMaxPendingDays)).IsPast;
        }

        private void EnsureCollectionsInitialized()
        {
            if (_pendingPartitions == null)
                _pendingPartitions = new List<PendingPartitionSuccessionRecord>();
            if (_knownClanLeaderIds == null)
                _knownClanLeaderIds = new Dictionary<string, string>();
        }

        private void PrunePendingPartitions()
        {
            var journaled = _pendingPartitions.Where(HasCrownPromotionJournal).ToList();
            _pendingPartitions = _pendingPartitions
                .Where(r => r != null && !HasCrownPromotionJournal(r)
                         && !journaled.Any(j => j.ParentClanId == r.ParentClanId && j.DeadLeaderId == r.DeadLeaderId)
                         && !string.IsNullOrEmpty(r.DeadLeaderId)
                         && !string.IsNullOrEmpty(r.ParentClanId)
                         && !string.IsNullOrEmpty(r.KingdomId)
                         && !string.IsNullOrEmpty(r.FiefIds)
                         && !string.IsNullOrEmpty(r.HeirIds))
                .GroupBy(r => new { r.ParentClanId, Predecessor = journaled.Any(j => j.ParentClanId == r.ParentClanId) ? r.DeadLeaderId : string.Empty })
                .Select(g => g.OrderByDescending(r => r.ReadyDate.ToDays).First())
                .Concat(journaled)
                .ToList();
        }

        private static string JoinIds(IEnumerable<string> ids)
        {
            return string.Join("|", ids.Where(id => !string.IsNullOrWhiteSpace(id)));
        }

        private static List<FeudalTitleRecord> ResolvePartitionEstateTitles(FeudalTitleBehavior titleBehavior, string titleIds, Clan parentClan)
        {
            if (titleBehavior == null)
                return new List<FeudalTitleRecord>();

            List<FeudalTitleRecord> snapshotTitles = SplitIds(titleIds)
                .Select(titleBehavior.GetTitle)
                .Where(title => title != null && title.IsActive)
                .GroupBy(title => title.TitleId)
                .Select(group => group.First())
                .ToList();

            if (snapshotTitles.Count > 0)
                return snapshotTitles;

            return titleBehavior.GetTitlesHeldByClan(parentClan, deJure: true)
                .Where(title => title != null && title.IsActive)
                .ToList();
        }

        private static string FormatHeroIds(IEnumerable<Hero> heroes)
        {
            if (heroes == null)
                return "none";

            string text = string.Join(",", heroes
                .Where(h => h != null)
                .Select(h => h.StringId));
            return string.IsNullOrWhiteSpace(text) ? "none" : text;
        }

        private static string FormatFiefIds(IEnumerable<Town> fiefs)
        {
            if (fiefs == null)
                return "none";

            string text = string.Join(",", fiefs
                .Where(f => f != null)
                .Select(f => f.StringId));
            return string.IsNullOrWhiteSpace(text) ? "none" : text;
        }

        private static string FormatTitleIds(IEnumerable<FeudalTitleRecord> titles)
        {
            if (titles == null)
                return "none";

            string text = string.Join(",", titles
                .Where(title => title != null)
                .Select(title => title.TitleId));
            return string.IsNullOrWhiteSpace(text) ? "none" : text;
        }

        private static string FormatFiefStatus(IEnumerable<Town> fiefs)
        {
            if (fiefs == null)
                return "none";

            string text = string.Join(",", fiefs
                .Where(f => f != null)
                .Select(f => $"{f.StringId}:{f.OwnerClan?.StringId ?? "no_owner"}"));
            return string.IsNullOrWhiteSpace(text) ? "none" : text;
        }

        private static IEnumerable<string> SplitIds(string ids)
        {
            return string.IsNullOrWhiteSpace(ids)
                ? Enumerable.Empty<string>()
                : ids.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries);
        }

        private static Hero ResolveHero(string heroId)
        {
            if (string.IsNullOrEmpty(heroId))
                return null;

            return Hero.FindFirst(h => h.StringId == heroId);
        }

        private static Clan ResolveClan(string clanId)
        {
            if (string.IsNullOrEmpty(clanId))
                return null;

            return Clan.All.FirstOrDefault(c => c.StringId == clanId);
        }

        private static Kingdom ResolveKingdom(string kingdomId)
        {
            if (string.IsNullOrEmpty(kingdomId))
                return null;

            return Kingdom.All.FirstOrDefault(k => k.StringId == kingdomId);
        }

        private static Town ResolveTown(string townId)
        {
            if (string.IsNullOrEmpty(townId))
                return null;

            Town town = Town.AllTowns.FirstOrDefault(t => string.Equals(t.StringId, townId, StringComparison.OrdinalIgnoreCase));
            if (town != null)
                return town;

            Settlement settlement = Settlement.All.FirstOrDefault(s =>
                s?.Town != null
                && (string.Equals(s.StringId, townId, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(s.Town.StringId, townId, StringComparison.OrdinalIgnoreCase)));
            return settlement?.Town;
        }

        private static void TracePartition(string message)
        {
            BellumCivileDebug.Trace("partition", message, requestInGameDisplay: true);
        }
    }
}
