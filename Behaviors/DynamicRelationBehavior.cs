using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.Core;
using TaleWorlds.Core.ViewModelCollection.Information;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using C = BellumCivile.BellumCivileConstants;

namespace BellumCivile.Behaviors
{
    public readonly struct DynamicRelationSetState
    {
        public bool IsTracked { get; }
        public int PreviousValue { get; }

        public DynamicRelationSetState(bool isTracked, int previousValue)
        {
            IsTracked = isTracked;
            PreviousValue = previousValue;
        }
    }

    public class DynamicRelationBehavior : CampaignBehaviorBase
    {
        private const int CurrentMemorySchemaVersion = 2;
        private const float FoundationCacheLifetimeDays = 30f;
        private const float PoliticalCacheLifetimeDays = 7f;
        private const int MaximumMemoriesPerPair = 48;
        private const int CachePruneBudgetPerDay = 128;
        private const int MaximumVisibleCacheEntries = 8192;

        private struct FoundationCacheEntry
        {
            public bool Spouses;
            public int Value;
            public float Day;
        }

        private struct PoliticalCacheEntry
        {
            public int CustodyModifier;
            public int Value;
            public float Day;
            public int PoliticalRevision;
            public int TitleRevision;
        }

        private struct VisibleRelationCacheEntry
        {
            public bool Spouses;
            public int CustodyModifier;
            public int Value;
            public float ValidUntilDay;
            public int FoundationRevision;
            public int PoliticalRevision;
            public int TitleRevision;
            public int MemoryRevision;
            public int HouseMemoryRevision;
        }

        private sealed class PendingDeathMemory
        {
            public Hero Victim;
            public Hero Killer;
            public Clan VictimClan;
            public KillCharacterAction.KillCharacterActionDetail Detail;
            public readonly List<Hero> Friends = new List<Hero>();
            public readonly List<Hero> Enemies = new List<Hero>();
            public readonly HashSet<Hero> Family = new HashSet<Hero>();
        }

        // Retained only to migrate saves written by the former weekly-drift system.
        private Dictionary<string, DynamicRelationRecord> _serializedRelationRecords = new Dictionary<string, DynamicRelationRecord>();
        private readonly Dictionary<DynamicRelationPairKey, DynamicRelationRecord> _runtimeRelationRecords = new Dictionary<DynamicRelationPairKey, DynamicRelationRecord>();

        private int _memorySchemaVersion;
        private bool _bloodKinshipBaselineMigrated;
        private bool _spouseBaselineMigrated;
        private float _appliedMemoryDurationMultiplier = 1f;
        private bool _memoryDurationReady;
        private List<RelationMemoryRecord> _relationMemories = new List<RelationMemoryRecord>();
        private Dictionary<string, int> _materializedPairValues = new Dictionary<string, int>(StringComparer.Ordinal);
        private Dictionary<string, int> _openingRelationAdjustments = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly Dictionary<DynamicRelationPairKey, int> _runtimeOpeningAdjustments = new Dictionary<DynamicRelationPairKey, int>();

        private readonly Dictionary<DynamicRelationPairKey, int> _runtimeMaterializedPairValues = new Dictionary<DynamicRelationPairKey, int>();
        private readonly Dictionary<DynamicRelationPairKey, List<RelationMemoryRecord>> _personalMemoriesByPair = new Dictionary<DynamicRelationPairKey, List<RelationMemoryRecord>>();
        private readonly Dictionary<ClanPairKey, List<RelationMemoryRecord>> _houseMemoriesByPair = new Dictionary<ClanPairKey, List<RelationMemoryRecord>>();
        private readonly Dictionary<ClanPairKey, int> _houseMemoryRevisions = new Dictionary<ClanPairKey, int>();
        private readonly Dictionary<DynamicRelationPairKey, FoundationCacheEntry> _foundationCache = new Dictionary<DynamicRelationPairKey, FoundationCacheEntry>();
        private readonly Dictionary<DynamicRelationPairKey, PoliticalCacheEntry> _politicalCache = new Dictionary<DynamicRelationPairKey, PoliticalCacheEntry>();
        private readonly Dictionary<DynamicRelationPairKey, VisibleRelationCacheEntry> _visibleRelationCache = new Dictionary<DynamicRelationPairKey, VisibleRelationCacheEntry>();
        private readonly Queue<DynamicRelationPairKey> _visiblePruneQueue = new Queue<DynamicRelationPairKey>();
        private readonly HashSet<DynamicRelationPairKey> _queuedVisibleKeys = new HashSet<DynamicRelationPairKey>();
        private readonly Queue<DynamicRelationPairKey> _foundationPruneQueue = new Queue<DynamicRelationPairKey>();
        private readonly HashSet<DynamicRelationPairKey> _queuedFoundationKeys = new HashSet<DynamicRelationPairKey>();
        private readonly Queue<DynamicRelationPairKey> _politicalPruneQueue = new Queue<DynamicRelationPairKey>();
        private readonly HashSet<DynamicRelationPairKey> _queuedPoliticalKeys = new HashSet<DynamicRelationPairKey>();
        private readonly Dictionary<string, Hero> _heroesById = new Dictionary<string, Hero>(StringComparer.Ordinal);
        private readonly Dictionary<string, Clan> _clansById = new Dictionary<string, Clan>(StringComparer.Ordinal);
        private readonly List<DynamicRelationPairKey> _cacheRemovalBuffer = new List<DynamicRelationPairKey>();
        private int _foundationRevision;
        private int _politicalRevision;
        private int _memoryRevision;
        private float _nextMemoryPruneDay = float.MaxValue;
        private PendingDeathMemory _pendingDeathMemory;
        private bool _handlingNativeExecutionRelations;

#if DEBUG
        private long _diagnosticRelationReads;
        private long _diagnosticEligibleReads;
        private long _diagnosticFoundationHits;
        private long _diagnosticFoundationMisses;
        private long _diagnosticPoliticalHits;
        private long _diagnosticPoliticalMisses;
        private long _diagnosticPoliticalInvalidations;
        private long _diagnosticVisibleHits, _diagnosticVisibleMisses;
        private long _diagnosticPruneRuns, _diagnosticPruneScanned, _diagnosticPruneRemoved, _diagnosticPruneTicks;
        private float? _diagnosticStartDay;
#endif

        public static DynamicRelationBehavior Instance { get; private set; }
        internal static bool SuppressRelationPatch { get; private set; }

        public DynamicRelationBehavior()
        {
            Instance = this;
        }

        public override void RegisterEvents()
        {
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
            CampaignEvents.HeroGainedSkill.AddNonSerializedListener(this, OnHeroGainedSkill);
            CampaignEvents.PlayerTraitChangedEvent.AddNonSerializedListener(this, OnPlayerTraitChanged);
            CampaignEvents.OnClanChangedKingdomEvent.AddNonSerializedListener(this, OnClanChangedKingdom);
            CampaignEvents.WarDeclared.AddNonSerializedListener(this, OnWarDeclared);
            CampaignEvents.MakePeace.AddNonSerializedListener(this, OnPeaceMade);
            CampaignEvents.BeforeHeroKilledEvent.AddNonSerializedListener(this, OnBeforeHeroKilled);
        }

        public override void SyncData(IDataStore dataStore)
        {
            EnsureCollectionsInitialized();
            if (dataStore.IsSaving)
            {
                MigrateBloodKinshipBaseline();
                RefreshMemoryDurationMultiplier();
                if (_memorySchemaVersion < CurrentMemorySchemaVersion)
                    RebuildSerializedRecordsFromRuntime();
                RebuildSerializedMaterializedValues();
                _openingRelationAdjustments = _runtimeOpeningAdjustments
                    .Where(pair => ShouldAffectPair(pair.Key.FirstHero, pair.Key.SecondHero))
                    .ToDictionary(pair => pair.Key.ToSerializedKey(), pair => pair.Value, StringComparer.Ordinal);
            }

            dataStore.SyncData("BellumCivile_DynamicRelationRecords", ref _serializedRelationRecords);
            dataStore.SyncData("BellumCivile_RelationMemoryVersion", ref _memorySchemaVersion);
            dataStore.SyncData("BellumCivile_MemoryDurationMultiplier", ref _appliedMemoryDurationMultiplier);
            dataStore.SyncData("BellumCivile_RelationMemories", ref _relationMemories);
            dataStore.SyncData("BellumCivile_RelationMaterializedValues", ref _materializedPairValues);
            dataStore.SyncData("BellumCivile_RelationOpeningAdjustments", ref _openingRelationAdjustments);
            dataStore.SyncData("BellumCivile_BloodKinshipBaselineMigrated", ref _bloodKinshipBaselineMigrated);
            dataStore.SyncData("BellumCivile_SpouseBaselineMigrated", ref _spouseBaselineMigrated);
            EnsureCollectionsInitialized();

            if (dataStore.IsLoading)
            {
                RebuildObjectResolutionCaches();
                RebuildRuntimeRecordsFromSerialized();
                RebuildRuntimeMaterializedValues();
                RebuildOpeningAdjustments();
                RebuildMemoryIndexes();
            }

            Instance = this;
        }

        public int GetRelationForRead(Hero firstHero, Hero secondHero, int vanillaStoredValue)
        {
            RefreshMemoryDurationMultiplier();
#if DEBUG
            _diagnosticRelationReads++;
#endif
            if (!BellumCivileOptions.EnableDynamicRelationDrift || SuppressRelationPatch)
                return vanillaStoredValue;
            if (!ShouldAffectPair(firstHero, secondHero))
                return vanillaStoredValue;

#if DEBUG
            _diagnosticEligibleReads++;
#endif
            EnsureCollectionsInitialized();
            DynamicRelationPairKey key = new DynamicRelationPairKey(firstHero, secondHero);
            MigrateBloodKinshipBaseline();
            // Notable inspection should not manufacture relationships or persistent zero records.
            if ((firstHero.IsNotable || secondHero.IsNotable) && vanillaStoredValue == 0
                && !_runtimeMaterializedPairValues.ContainsKey(key) && !_personalMemoriesByPair.ContainsKey(key)
                && !_runtimeOpeningAdjustments.ContainsKey(key))
                return 0;
            if (_runtimeMaterializedPairValues.TryGetValue(key, out int cachedMaterialized)
                && vanillaStoredValue == cachedMaterialized
                && TryGetCachedVisibleRelation(key, out int cachedVisible))
            {
#if DEBUG
                _diagnosticVisibleHits++;
#endif
                return cachedVisible;
            }
#if DEBUG
            _diagnosticVisibleMisses++;
#endif

            int naturalRelation = GetNaturalRelationRaw(key, firstHero, secondHero);
            bool initializedNow = EnsurePairInitialized(key, firstHero, secondHero, naturalRelation, vanillaStoredValue);

            if (!initializedNow && _runtimeMaterializedPairValues.TryGetValue(key, out int materialized) && vanillaStoredValue != materialized)
            {
                int externalChange = vanillaStoredValue - materialized;
                AddMemoryForChange(firstHero, secondHero, externalChange, RelationMemoryService.BuildFallbackDescriptor(externalChange));
            }

            int visible = MBMath.ClampInt(CalculateVisibleRelation(firstHero, secondHero, naturalRelation, out float validUntilDay), -100, 100);
            _runtimeMaterializedPairValues[key] = visible;
            CacheVisibleRelation(key, visible, validUntilDay);
            if (vanillaStoredValue != visible)
                SetRawRelation(firstHero, secondHero, visible);
            return visible;
        }

        public DynamicRelationSetState PrepareRelationSet(Hero firstHero, Hero secondHero)
        {
            if (!BellumCivileOptions.EnableDynamicRelationDrift || SuppressRelationPatch || !ShouldAffectPair(firstHero, secondHero))
                return default;
            int raw = GetRawRelation(firstHero, secondHero);
            return new DynamicRelationSetState(true, GetRelationForRead(firstHero, secondHero, raw));
        }

        public void CaptureRelationSet(Hero firstHero, Hero secondHero, int value, DynamicRelationSetState state)
        {
            if (!state.IsTracked || !BellumCivileOptions.EnableDynamicRelationDrift || SuppressRelationPatch || !ShouldAffectPair(firstHero, secondHero))
                return;

            int target = MBMath.ClampInt(value, -100, 100);
            int change = target - state.PreviousValue;
            if (IsManagedDeathRelationPair(firstHero, secondHero))
            {
                RestoreMaterializedRelation(firstHero, secondHero);
                return;
            }

            if (change != 0)
            {
                RelationMemoryDescriptor descriptor = RelationMemoryService.ResolveCapturedDescriptor(change);
                AddMemoryForChange(firstHero, secondHero, change, descriptor);
            }

            _runtimeMaterializedPairValues[new DynamicRelationPairKey(firstHero, secondHero)] = target;
        }

        public void InvalidateBaselineCache()
        {
#if DEBUG
            _diagnosticPoliticalInvalidations++;
#endif
            _politicalRevision++;
            _politicalCache.Clear();
            _politicalPruneQueue.Clear();
            _queuedPoliticalKeys.Clear();
        }

        public string BuildDebugBreakdown(Hero firstHero, Hero secondHero)
        {
            if (firstHero == null || secondHero == null)
                return "Error: both heroes must be valid.";

            bool eligible = ShouldAffectPair(firstHero, secondHero);
            int raw = GetRawRelation(firstHero, secondHero);
            int visible = GetRelationForRead(firstHero, secondHero, raw);
            DynamicRelationBaselineBreakdown breakdown = eligible ? DynamicRelationBaselineHelper.Calculate(firstHero, secondHero) : null;
            List<RelationMemoryRecord> memories = eligible ? GetActiveMemories(firstHero, secondHero) : new List<RelationMemoryRecord>();
            string memoryText = memories.Count == 0
                ? "none"
                : string.Join("; ", memories.Select(memory => RelationMemorySources.GetDisplayText(memory.SourceId, memory.ContextText) + "=" + FormatSigned(memory.GetCurrentValue(CurrentDay))));
            string reasonText = breakdown?.Reasons != null && breakdown.Reasons.Count > 0 ? string.Join("; ", breakdown.Reasons) : "none";
            _runtimeOpeningAdjustments.TryGetValue(new DynamicRelationPairKey(firstHero, secondHero), out int opening);
            return $"Relation memory {firstHero.Name} <-> {secondHero.Name}: enabled={BellumCivileOptions.EnableDynamicRelationDrift}; eligible={eligible}; raw={raw}; visible={visible}; foundation={breakdown?.Foundation ?? 0}; political={breakdown?.PoliticalConditions ?? 0}; natural={breakdown?.Total ?? 0}; opening_history={opening}; memories={memoryText}; {BuildCacheDiagnosticText()}; reasons={reasonText}.";
        }

        public bool TryBuildEncyclopediaThreshold(Hero viewedHero, out int baseline, out string tooltip)
        {
            baseline = 0;
            tooltip = string.Empty;
            Hero playerHero = Hero.MainHero;
            if (!CanBuildPlayerRelationTooltip(playerHero, viewedHero))
                return false;

            GetRelationForRead(playerHero, viewedHero, GetRawRelation(playerHero, viewedHero));
            DynamicRelationBaselineBreakdown breakdown = DynamicRelationBaselineHelper.Calculate(playerHero, viewedHero);
            baseline = breakdown.Total;
            List<string> lines = new List<string> { BuildOpinionTooltipTitle(viewedHero, playerHero) };
            lines.AddRange(breakdown.FoundationReasons);
            lines.AddRange(breakdown.PoliticalConditionReasons);
            _runtimeOpeningAdjustments.TryGetValue(new DynamicRelationPairKey(playerHero, viewedHero), out int opening);
            if (opening != 0)
                lines.Add(new TextObject("{=BC_Relation_OpeningHistory}Prior personal history").ToString() + ": " + FormatSigned(opening));
            List<RelationMemoryRecord> memories = GetActiveMemories(playerHero, viewedHero)
                .OrderByDescending(item => item.StartDay)
                .ToList();
            foreach (RelationMemoryRecord memory in memories)
                lines.Add(BuildMemoryText(memory));
            if (breakdown.FoundationReasons.Count == 0
                && breakdown.PoliticalConditionReasons.Count == 0
                && memories.Count == 0 && opening == 0)
            {
                lines.Add(new TextObject("{=BC_RelationMemory_NoMotives}No particular motives or memories.").ToString());
            }
            tooltip = string.Join("\n", lines);
            return true;
        }

        public bool TryBuildEncyclopediaThresholdTooltip(Hero viewedHero, out int currentRelation, out List<TooltipProperty> properties)
        {
            RefreshMemoryDurationMultiplier();
            currentRelation = 0;
            properties = null;
            Hero playerHero = Hero.MainHero;
            if (!CanBuildPlayerRelationTooltip(playerHero, viewedHero))
                return false;

            int visible = GetRelationForRead(playerHero, viewedHero, GetRawRelation(playerHero, viewedHero));
            DynamicRelationBaselineBreakdown breakdown = DynamicRelationBaselineHelper.Calculate(playerHero, viewedHero);
            currentRelation = visible;
            properties = new List<TooltipProperty>
            {
                new TooltipProperty(
                    BuildOpinionTooltipTitleSubject(viewedHero),
                    BuildOpinionTooltipTitlePredicate(playerHero),
                    0,
                    false,
                    TooltipProperty.TooltipPropertyFlags.Title)
            };
            AddTooltipSeparator(properties);

            foreach (string reason in breakdown.FoundationReasons)
                AddTooltipBreakdownLine(properties, reason);
            foreach (string reason in breakdown.PoliticalConditionReasons)
                AddTooltipBreakdownLine(properties, reason);

            var pairKey = new DynamicRelationPairKey(playerHero, viewedHero);
            if (_runtimeOpeningAdjustments.TryGetValue(pairKey, out int opening) && opening != 0)
                properties.Add(new TooltipProperty(new TextObject("{=BC_Relation_OpeningHistory}Prior personal history").ToString(), FormatSigned(opening), 0));

            List<RelationMemoryRecord> memories = GetActiveMemories(playerHero, viewedHero);
            foreach (RelationMemoryRecord memory in memories.OrderByDescending(item => item.StartDay))
            {
                properties.Add(new TooltipProperty(
                    RelationMemorySources.GetDisplayText(memory.SourceId, memory.ContextText),
                    FormatSigned(memory.GetCurrentValue(CurrentDay)) + " (" + BuildMemoryDurationText(memory) + ")",
                    0));
            }

            if (breakdown.FoundationReasons.Count == 0
                && breakdown.PoliticalConditionReasons.Count == 0
                && memories.Count == 0 && opening == 0)
            {
                properties.Add(new TooltipProperty(
                    new TextObject("{=BC_RelationMemory_NoMotives}No particular motives or memories.").ToString(),
                    string.Empty,
                    0,
                    false,
                    TooltipProperty.TooltipPropertyFlags.MultiLine));
            }
            return true;
        }

        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            EnsureCollectionsInitialized();
            Instance = this;
            RebuildObjectResolutionCaches();
            RebuildRuntimeRecordsFromSerialized();
            RebuildMemoryIndexes();
            MigrateBloodKinshipBaseline();
            MigrateLegacyRelationRecords();
            _memoryDurationReady = true;
            RefreshMemoryDurationMultiplier();
            _foundationCache.Clear();
            ClearVisibleRelationCache();
            _foundationPruneQueue.Clear();
            _queuedFoundationKeys.Clear();
            _foundationRevision++;
            InvalidateBaselineCache();
            PruneExpiredMemories(true);
        }

        private void OnDailyTick()
        {
            RefreshMemoryDurationMultiplier();
            PruneExpiredCaches();
            PruneExpiredMemories(false);
        }

        private void OnHeroGainedSkill(Hero hero, SkillObject skill, int change, bool shouldNotify)
        {
            if (hero != null && skill == DefaultSkills.Charm)
                InvalidateFoundationForHero(hero);
        }

        private void OnPlayerTraitChanged(TraitObject trait, int previousLevel)
        {
            InvalidateFoundationForHero(Hero.MainHero);
        }

        private void OnClanChangedKingdom(Clan clan, Kingdom oldKingdom, Kingdom newKingdom, ChangeKingdomAction.ChangeKingdomActionDetail detail, bool showNotification)
        {
            InvalidateBaselineCache();
        }

        private void OnWarDeclared(IFaction firstFaction, IFaction secondFaction, DeclareWarAction.DeclareWarDetail detail)
        {
            InvalidateBaselineCache();
        }

        private void OnPeaceMade(IFaction firstFaction, IFaction secondFaction, MakePeaceAction.MakePeaceDetail detail)
        {
            InvalidateBaselineCache();
        }

        private void OnBeforeHeroKilled(Hero victim, Hero killer, KillCharacterAction.KillCharacterActionDetail detail, bool showNotification)
        {
            _pendingDeathMemory = null;
            if (!BellumCivileOptions.EnableDynamicRelationDrift) return;
            if (victim == null || killer == null || victim == killer || victim.Clan == null || killer.Clan == null)
                return;
            if (!IsViolentDeath(detail) || !IsEligibleHero(killer))
                return;

            PendingDeathMemory pending = new PendingDeathMemory
            {
                Victim = victim,
                Killer = killer,
                VictimClan = victim.Clan,
                Detail = detail
            };
            foreach (Hero relative in PersonalKinshipHelper.GetImmediateFamily(victim))
                if (relative != killer && !relative.IsNotable && ShouldAffectPair(relative, killer))
                    pending.Family.Add(relative);
            foreach (Hero hero in Hero.AllAliveHeroes)
            {
                if (hero == null || hero == victim || hero == killer || hero.Clan == null || hero.Clan == victim.Clan || !IsEligibleHero(hero))
                    continue;
                if (hero.IsNotable || IsPersonalOnlyPair(hero, killer) || pending.Family.Contains(hero))
                    continue;

                int relation = GetRelationForRead(hero, victim, GetRawRelation(hero, victim));
                if (relation > Campaign.Current.Models.DiplomacyModel.MaxNeutralRelationLimit)
                    pending.Friends.Add(hero);
                else if (relation < Campaign.Current.Models.DiplomacyModel.MinNeutralRelationLimit)
                    pending.Enemies.Add(hero);
            }
            _pendingDeathMemory = pending;
        }

        internal void CompleteDeathMemories(Hero victim, Hero killer, KillCharacterAction.KillCharacterActionDetail detail)
        {
            PendingDeathMemory pending = _pendingDeathMemory;
            _pendingDeathMemory = null;
            if (!BellumCivileOptions.EnableDynamicRelationDrift || pending == null || pending.Victim != victim || pending.Killer != killer
                || pending.Detail != detail || killer == null)
                return;

            bool deliberate = IsDeliberateDeath(detail);
            bool murder = detail == KillCharacterAction.KillCharacterActionDetail.Murdered;
            string eventId = "death:" + victim.StringId;
            foreach (Hero relative in pending.Family)
            {
                ApplyDirectMemory(relative, killer, deliberate ? -30 : -20,
                    murder ? RelationMemorySources.MurderedCloseFamily : deliberate
                        ? RelationMemorySources.ExecutedCloseFamily : RelationMemorySources.KilledCloseFamily,
                    deliberate ? 50f : 25f, RelationMemoryScope.Personal, victim.Name?.ToString(), eventId);
            }
            Hero victimHouseLeader = pending.VictimClan?.Leader;
            if (victimHouseLeader != null && victimHouseLeader != killer && victimHouseLeader.IsAlive
                && !IsPersonalOnlyPair(victimHouseLeader, killer))
            {
                ApplyDirectMemory(victimHouseLeader, killer, deliberate ? -30 : -20,
                    murder ? RelationMemorySources.MurderedKinsman : deliberate ? RelationMemorySources.ExecutedKinsman : RelationMemorySources.KilledKinsman,
                    deliberate ? 50f : 25f, RelationMemoryScope.House, victim.Name?.ToString(), eventId);
            }

            foreach (Hero friend in pending.Friends)
            {
                if (friend == null || !friend.IsAlive || friend == killer)
                    continue;
                ApplyDirectMemory(friend, killer, deliberate ? -15 : -10, RelationMemorySources.KilledFriend,
                    deliberate ? 20f : 10f, RelationMemoryScope.Personal, victim.Name?.ToString());
            }

            foreach (Hero enemy in pending.Enemies)
            {
                if (enemy == null || !enemy.IsAlive || enemy == killer)
                    continue;
                ApplyDirectMemory(enemy, killer, 5, RelationMemorySources.KilledEnemy, 5f,
                    RelationMemoryScope.Personal, victim.Name?.ToString());
            }
        }

        private bool IsManagedDeathRelationPair(Hero firstHero, Hero secondHero)
        {
            if (!_handlingNativeExecutionRelations || RelationMemoryService.CurrentDescriptor != null) return false;
            PendingDeathMemory pending = _pendingDeathMemory;
            if (pending == null || pending.Killer == null || (firstHero != pending.Killer && secondHero != pending.Killer))
                return false;

            Hero affectedHero = firstHero == pending.Killer ? secondHero : firstHero;
            if (pending.Family.Contains(affectedHero)) return true;
            if (IsPersonalOnlyPair(firstHero, secondHero)) return false;
            return affectedHero?.Clan == pending.VictimClan
                || pending.Friends.Contains(affectedHero)
                || pending.Enemies.Contains(affectedHero);
        }

        internal IDisposable PreserveDeathContext() => new DeathContextScope(this, _pendingDeathMemory);
        internal IDisposable BeginNativeExecutionRelations() => new NativeExecutionRelationScope(this);

        private sealed class NativeExecutionRelationScope : IDisposable
        {
            private readonly DynamicRelationBehavior _behavior;
            private readonly bool _previous;
            private bool _disposed;
            internal NativeExecutionRelationScope(DynamicRelationBehavior behavior)
            {
                _behavior = behavior;
                _previous = behavior._handlingNativeExecutionRelations;
                behavior._handlingNativeExecutionRelations = true;
            }
            public void Dispose()
            {
                if (_disposed) return;
                _behavior._handlingNativeExecutionRelations = _previous;
                _disposed = true;
            }
        }

        private sealed class DeathContextScope : IDisposable
        {
            private readonly DynamicRelationBehavior _behavior;
            private readonly PendingDeathMemory _previous;
            private readonly bool _previousNativeContext;
            private bool _disposed;
            internal DeathContextScope(DynamicRelationBehavior behavior, PendingDeathMemory previous)
            {
                _behavior = behavior;
                _previous = previous;
                _previousNativeContext = behavior._handlingNativeExecutionRelations;
                behavior._handlingNativeExecutionRelations = false;
            }
            public void Dispose()
            {
                if (_disposed) return;
                _behavior._pendingDeathMemory = _previous;
                _behavior._handlingNativeExecutionRelations = _previousNativeContext;
                _disposed = true;
            }
        }

        private void RestoreMaterializedRelation(Hero firstHero, Hero secondHero)
        {
            DynamicRelationPairKey pair = new DynamicRelationPairKey(firstHero, secondHero);
            int current = CalculateVisibleRelation(firstHero, secondHero, GetNaturalRelationRaw(pair, firstHero, secondHero));
            _runtimeMaterializedPairValues[pair] = current;
            SetRawRelation(firstHero, secondHero, current);
        }

        internal bool RefreshSuccessionConcession(Hero heir, Hero ruler)
        {
            if (!ShouldAffectPair(heir, ruler)) return false;
            EnsureCollectionsInitialized();
            RefreshMemoryDurationMultiplier();
            if (BellumCivileOptions.EnableDynamicRelationDrift)
                GetRelationForRead(heir, ruler, GetRawRelation(heir, ruler));
            string pair = RelationMemoryRecord.BuildPairKey(heir.StringId, ruler.StringId);
            _relationMemories.RemoveAll(m => m.Scope == RelationMemoryScope.Personal && m.PairKey == pair
                && m.SourceId == RelationMemorySources.SatisfiedSuccessionDemand);
            RebuildMemoryIndexes();
            AddMemoryForChange(heir, ruler, 25, new RelationMemoryDescriptor(
                RelationMemorySources.SatisfiedSuccessionDemand, null,
                10 * Math.Max(1, CampaignTime.DaysInYear), RelationMemoryScope.Personal));
            if (BellumCivileOptions.EnableDynamicRelationDrift) RestoreMaterializedRelation(heir, ruler);
            return true;
        }

        private void ApplyDirectMemory(Hero firstHero, Hero secondHero, int change, string sourceId, float durationYears, RelationMemoryScope scope, string contextText, string eventId = null)
        {
            if (!ShouldAffectPair(firstHero, secondHero) || change == 0)
                return;

            int previous = GetRelationForRead(firstHero, secondHero, GetRawRelation(firstHero, secondHero));
            // A house memory must not be capped using only its leader's personal opinion.
            int effectiveChange = scope == RelationMemoryScope.House ? change : MBMath.ClampInt(previous + change, -100, 100) - previous;
            if (effectiveChange == 0)
                return;

            int daysInYear = Math.Max(1, CampaignTime.DaysInYear);
            AddMemoryForChange(firstHero, secondHero, effectiveChange, new RelationMemoryDescriptor(sourceId, contextText, durationYears * daysInYear, scope, eventId));
            DynamicRelationPairKey pair = new DynamicRelationPairKey(firstHero, secondHero);
            int current = MBMath.ClampInt(CalculateVisibleRelation(firstHero, secondHero, GetNaturalRelationRaw(pair, firstHero, secondHero)), -100, 100);
            _runtimeMaterializedPairValues[pair] = current;
            SetRawRelation(firstHero, secondHero, current);
        }

        private bool EnsurePairInitialized(DynamicRelationPairKey pairKey, Hero firstHero, Hero secondHero, int naturalRelation, int rawValue)
        {
            if (_runtimeMaterializedPairValues.ContainsKey(pairKey))
                return false;

            bool preserveOpening = IsPersonalOnlyPair(firstHero, secondHero) || PersonalKinshipHelper.GetBonus(firstHero, secondHero) > 0;
            int initialVisible = preserveOpening || rawValue != 0 ? MBMath.ClampInt(rawValue, -100, 100) : naturalRelation;
            int residual = initialVisible - naturalRelation;
            if (preserveOpening)
            {
                // Opening history is durable state, not an expiring/evictable event memory.
                int existingMemories = 0;
                float validUntil = float.MaxValue;
                AddMemoryValues(_personalMemoriesByPair, pairKey, CurrentDay, ref existingMemories, ref validUntil);
                if (!IsPersonalOnlyPair(firstHero, secondHero) && firstHero.Clan != null && secondHero.Clan != null)
                    AddMemoryValues(_houseMemoriesByPair, new ClanPairKey(firstHero.Clan, secondHero.Clan), CurrentDay, ref existingMemories, ref validUntil,
                        GetPersonalBereavementEvents(pairKey, CurrentDay));
                int adjustment = residual - existingMemories;
                AddOpeningHistory(pairKey, adjustment);
            }
            else if (residual != 0)
            {
                float weeklyDecay = Math.Max(0f, BellumCivileOptions.DynamicRelationWeeklyDrift);
                AddMemoryRecord(new RelationMemoryRecord(RelationMemoryScope.Personal, firstHero.StringId, secondHero.StringId,
                    RelationMemorySources.PriorHistory, null, residual, CurrentDay,
                    CalculateLegacyExpiryDay(CurrentDay, residual, weeklyDecay), weeklyDecay));
            }

            _runtimeMaterializedPairValues[pairKey] = initialVisible;
            if (rawValue != initialVisible)
                SetRawRelation(firstHero, secondHero, initialVisible);
            return true;
        }

        private void AddMemoryForChange(Hero firstHero, Hero secondHero, int change, RelationMemoryDescriptor descriptor)
        {
            if (change == 0 || descriptor == null || firstHero == null || secondHero == null)
                return;

            RelationMemoryScope scope = descriptor.Scope;
            string firstId;
            string secondId;
            if (scope == RelationMemoryScope.House && !IsPersonalOnlyPair(firstHero, secondHero)
                && firstHero.Clan != null && secondHero.Clan != null && firstHero.Clan != secondHero.Clan)
            {
                firstId = firstHero.Clan.StringId;
                secondId = secondHero.Clan.StringId;
            }
            else
            {
                scope = RelationMemoryScope.Personal;
                firstId = firstHero.StringId;
                secondId = secondHero.StringId;
            }

            AddMemoryRecord(new RelationMemoryRecord(scope, firstId, secondId, descriptor.SourceId, descriptor.ContextText,
                change, CurrentDay, CurrentDay + Math.Max(1f, descriptor.DurationDays), eventId: descriptor.EventId));
        }

        private void AddMemoryRecord(RelationMemoryRecord record)
        {
            RefreshMemoryDurationMultiplier();
            if (record == null || record.Value == 0 || string.IsNullOrWhiteSpace(record.FirstId) || string.IsNullOrWhiteSpace(record.SecondId))
                return;

            if (_memoryDurationReady)
                record.RescaleRemainingDuration(CurrentDay, _appliedMemoryDurationMultiplier);

            if (!TryGetOrCreateMemoryBucket(record, out List<RelationMemoryRecord> pairMemories))
                return;

            RelationMemoryRecord mergeTarget = pairMemories.FirstOrDefault(existing => existing != null
                && record.SourceId != RelationMemorySources.DeliveredNoblePrisoners
                && existing.LegacyWeeklyDecay <= 0f && record.LegacyWeeklyDecay <= 0f
                && string.Equals(existing.SourceId, record.SourceId, StringComparison.Ordinal)
                && string.Equals(existing.ContextText, record.ContextText, StringComparison.Ordinal)
                && string.Equals(existing.EventId, record.EventId, StringComparison.Ordinal)
                && Math.Abs(existing.StartDay - record.StartDay) <= 1f && !existing.IsExpired(CurrentDay));
            if (mergeTarget != null)
            {
                mergeTarget.Merge(record.Value, record.ExpiryDay);
                _nextMemoryPruneDay = Math.Min(_nextMemoryPruneDay, mergeTarget.ExpiryDay);
                InvalidateMemoryPair(record);
                return;
            }

            if (pairMemories.Count >= MaximumMemoriesPerPair)
            {
                RelationMemoryRecord weakest = pairMemories
                    .Where(item => item != null)
                    .OrderBy(item => Math.Abs(item.GetCurrentValue(CurrentDay)))
                    .ThenBy(item => item.ExpiryDay < 0f ? float.MaxValue : item.ExpiryDay)
                    .ThenBy(item => item.StartDay)
                    .FirstOrDefault();
                if (weakest != null)
                {
                    pairMemories.Remove(weakest);
                    _relationMemories.Remove(weakest);
                }
            }

            pairMemories.Add(record);
            _relationMemories.Add(record);
            InvalidateMemoryPair(record);
            if (record.ExpiryDay >= 0f)
                _nextMemoryPruneDay = Math.Min(_nextMemoryPruneDay, record.ExpiryDay);
        }

        private bool TryGetOrCreateMemoryBucket(RelationMemoryRecord record, out List<RelationMemoryRecord> pairMemories)
        {
            pairMemories = null;
            if (record == null)
                return false;

            if (record.Scope == RelationMemoryScope.House)
            {
                Clan firstClan = ResolveClan(record.FirstId);
                Clan secondClan = ResolveClan(record.SecondId);
                if (firstClan == null || secondClan == null || firstClan == secondClan)
                    return false;

                ClanPairKey key = new ClanPairKey(firstClan, secondClan);
                if (!_houseMemoriesByPair.TryGetValue(key, out pairMemories))
                {
                    pairMemories = new List<RelationMemoryRecord>();
                    _houseMemoriesByPair[key] = pairMemories;
                }
                return true;
            }

            Hero firstHero = ResolveHero(record.FirstId);
            Hero secondHero = ResolveHero(record.SecondId);
            if (firstHero == null || secondHero == null || firstHero == secondHero)
                return false;

            DynamicRelationPairKey heroKey = new DynamicRelationPairKey(firstHero, secondHero);
            if (!_personalMemoriesByPair.TryGetValue(heroKey, out pairMemories))
            {
                pairMemories = new List<RelationMemoryRecord>();
                _personalMemoriesByPair[heroKey] = pairMemories;
            }
            return true;
        }

        private int CalculateVisibleRelation(Hero firstHero, Hero secondHero, int naturalRelation)
        {
            return CalculateVisibleRelation(firstHero, secondHero, naturalRelation, out _);
        }

        private int CalculateVisibleRelation(Hero firstHero, Hero secondHero, int naturalRelation, out float validUntilDay)
        {
            int memoryTotal = 0;
            _runtimeOpeningAdjustments.TryGetValue(new DynamicRelationPairKey(firstHero, secondHero), out int opening);
            float day = CurrentDay;
            validUntilDay = (float)Math.Floor(day) + 1f;
            AddMemoryValues(_personalMemoriesByPair, new DynamicRelationPairKey(firstHero, secondHero), day, ref memoryTotal, ref validUntilDay);
            if (!IsPersonalOnlyPair(firstHero, secondHero) && firstHero.Clan != null && secondHero.Clan != null && firstHero.Clan != secondHero.Clan)
                AddMemoryValues(_houseMemoriesByPair, new ClanPairKey(firstHero.Clan, secondHero.Clan), day, ref memoryTotal, ref validUntilDay,
                    GetPersonalBereavementEvents(new DynamicRelationPairKey(firstHero, secondHero), day));
            return MBMath.ClampInt(naturalRelation + opening + memoryTotal, -100, 100);
        }

        private static void AddMemoryValues<TKey>(
            Dictionary<TKey, List<RelationMemoryRecord>> index,
            TKey pairKey,
            float day,
            ref int total,
            ref float validUntilDay,
            HashSet<string> excludedEvents = null)
        {
            if (!index.TryGetValue(pairKey, out List<RelationMemoryRecord> memories))
                return;
            foreach (RelationMemoryRecord memory in memories)
            {
                if (memory == null || (!string.IsNullOrEmpty(memory.EventId) && excludedEvents?.Contains(memory.EventId) == true))
                    continue;

                int currentValue = memory.GetCurrentValue(day);
                total += currentValue;
                if (memory.ExpiryDay > day)
                    validUntilDay = Math.Min(validUntilDay, memory.ExpiryDay);

                if (currentValue != 0 && memory.LegacyWeeklyDecay > 0f)
                {
                    float elapsedWeeks = Math.Max(0f, (day - memory.StartDay) / 7f);
                    int fadedSteps = (int)Math.Floor(elapsedWeeks * memory.LegacyWeeklyDecay);
                    float nextFadeDay = memory.StartDay + ((fadedSteps + 1f) / memory.LegacyWeeklyDecay) * 7f;
                    if (nextFadeDay > day)
                        validUntilDay = Math.Min(validUntilDay, nextFadeDay);
                }
            }
        }

        private bool TryGetCachedVisibleRelation(DynamicRelationPairKey key, out int value)
        {
            value = 0;
            if (!_visibleRelationCache.TryGetValue(key, out VisibleRelationCacheEntry cached)
                || CurrentDay >= cached.ValidUntilDay
                || cached.FoundationRevision != _foundationRevision
                || cached.PoliticalRevision != _politicalRevision
                || cached.TitleRevision != (FeudalTitleBehavior.Instance?.RuntimeRevision ?? 0)
                || cached.MemoryRevision != _memoryRevision
                || cached.HouseMemoryRevision != GetHouseMemoryRevision(key.FirstHero, key.SecondHero)
                || cached.Spouses != PersonalKinshipHelper.IsSpouse(key.FirstHero, key.SecondHero)
                || cached.CustodyModifier != DynamicRelationBaselineHelper.GetPrisonerCustodyModifier(key.FirstHero, key.SecondHero))
            {
                return false;
            }

            value = cached.Value;
            return true;
        }

        private void CacheVisibleRelation(DynamicRelationPairKey key, int value, float validUntilDay)
        {
            _visibleRelationCache[key] = new VisibleRelationCacheEntry
            {
                Value = value,
                ValidUntilDay = Math.Max(CurrentDay, validUntilDay),
                FoundationRevision = _foundationRevision,
                PoliticalRevision = _politicalRevision,
                TitleRevision = FeudalTitleBehavior.Instance?.RuntimeRevision ?? 0,
                MemoryRevision = _memoryRevision,
                HouseMemoryRevision = GetHouseMemoryRevision(key.FirstHero, key.SecondHero),
                Spouses = PersonalKinshipHelper.IsSpouse(key.FirstHero, key.SecondHero),
                CustodyModifier = DynamicRelationBaselineHelper.GetPrisonerCustodyModifier(key.FirstHero, key.SecondHero)
            };
            EnqueueCacheKey(key, _visiblePruneQueue, _queuedVisibleKeys);
            // Bound the queue as well as the cache: invalidated keys may still be queued.
            while (_visiblePruneQueue.Count > MaximumVisibleCacheEntries)
            {
                DynamicRelationPairKey oldest = _visiblePruneQueue.Dequeue();
                _queuedVisibleKeys.Remove(oldest);
                _visibleRelationCache.Remove(oldest);
            }
        }

        private void ClearVisibleRelationCache()
        {
            _visibleRelationCache.Clear();
            _visiblePruneQueue.Clear();
            _queuedVisibleKeys.Clear();
        }

        internal int LimitClientGrantGain(Hero firstHero, Hero secondHero)
        {
            if (!BellumCivileOptions.EnableDynamicRelationDrift || !ShouldAffectPair(firstHero, secondHero))
                return CourtClientGrantRules.Gratitude;
            GetRelationForRead(firstHero, secondHero, GetRawRelation(firstHero, secondHero));
            return CourtClientGrantRules.Reward(GetActiveMemories(firstHero, secondHero)
                .Where(m => m.SourceId == RelationMemorySources.ClientLandGrant)
                .Sum(m => Math.Max(0, m.GetCurrentValue(CurrentDay))));
        }

        internal int LimitPrisonerDonationGain(Hero firstHero, Hero secondHero, int proposedGain)
        {
            if (!BellumCivileOptions.EnableDynamicRelationDrift || !ShouldAffectPair(firstHero, secondHero))
                return proposedGain;

            GetRelationForRead(firstHero, secondHero, GetRawRelation(firstHero, secondHero));
            int existing = GetActiveMemories(firstHero, secondHero)
                .Where(memory => memory.SourceId == RelationMemorySources.DeliveredNoblePrisoners)
                .Sum(memory => Math.Max(0, memory.GetCurrentValue(CurrentDay)));
            return Math.Min(proposedGain, Math.Max(0, BellumCivileConstants.PrisonerDonationRelationCap - existing));
        }

        private List<RelationMemoryRecord> GetActiveMemories(Hero firstHero, Hero secondHero)
        {
            List<RelationMemoryRecord> result = new List<RelationMemoryRecord>();
            AddActiveMemories(_personalMemoriesByPair, new DynamicRelationPairKey(firstHero, secondHero), result);
            if (!IsPersonalOnlyPair(firstHero, secondHero) && firstHero.Clan != null && secondHero.Clan != null && firstHero.Clan != secondHero.Clan)
            {
                AddActiveMemories(_houseMemoriesByPair, new ClanPairKey(firstHero.Clan, secondHero.Clan), result);
                var excluded = GetPersonalBereavementEvents(new DynamicRelationPairKey(firstHero, secondHero), CurrentDay);
                if (excluded != null) result.RemoveAll(m => m.Scope == RelationMemoryScope.House && excluded.Contains(m.EventId));
            }
            return result;
        }

        private HashSet<string> GetPersonalBereavementEvents(DynamicRelationPairKey pair, float day)
        {
            HashSet<string> events = null;
            if (!_personalMemoriesByPair.TryGetValue(pair, out var memories)) return null;
            foreach (var memory in memories)
            {
                if (memory == null || string.IsNullOrEmpty(memory.EventId) || memory.GetCurrentValue(day) >= 0) continue;
                if (memory.SourceId != RelationMemorySources.KilledCloseFamily && memory.SourceId != RelationMemorySources.ExecutedCloseFamily
                    && memory.SourceId != RelationMemorySources.MurderedCloseFamily) continue;
                if (events == null) events = new HashSet<string>(StringComparer.Ordinal);
                events.Add(memory.EventId);
            }
            return events;
        }

        private static void AddActiveMemories<TKey>(Dictionary<TKey, List<RelationMemoryRecord>> index, TKey pairKey, List<RelationMemoryRecord> result)
        {
            if (!index.TryGetValue(pairKey, out List<RelationMemoryRecord> memories))
                return;
            foreach (RelationMemoryRecord memory in memories)
            {
                if (memory != null && memory.GetCurrentValue(CurrentDay) != 0)
                    result.Add(memory);
            }
        }

        private int GetNaturalRelationRaw(DynamicRelationPairKey key, Hero firstHero, Hero secondHero)
        {
            // Notables have no noble foundation or political baseline to cache or invalidate.
            if (firstHero.IsNotable || secondHero.IsNotable) return 0;
            return MBMath.ClampInt(GetFoundationRaw(key, firstHero, secondHero) + GetPoliticalConditionsRaw(key, firstHero, secondHero),
                C.DynamicRelationBaselineMin, C.DynamicRelationBaselineMax);
        }

        private int GetFoundationRaw(DynamicRelationPairKey key, Hero firstHero, Hero secondHero)
        {
            float day = CurrentDay;
            if (_foundationCache.TryGetValue(key, out FoundationCacheEntry cached) && day - cached.Day < FoundationCacheLifetimeDays
                && cached.Spouses == PersonalKinshipHelper.IsSpouse(firstHero, secondHero))
            {
#if DEBUG
                _diagnosticFoundationHits++;
#endif
                return cached.Value;
            }
#if DEBUG
            _diagnosticFoundationMisses++;
#endif
            int value = DynamicRelationBaselineHelper.CalculateFoundationRaw(firstHero, secondHero);
            _foundationCache[key] = new FoundationCacheEntry { Value = value, Day = day, Spouses = PersonalKinshipHelper.IsSpouse(firstHero, secondHero) };
            EnqueueCacheKey(key, _foundationPruneQueue, _queuedFoundationKeys);
            return value;
        }

        private int GetPoliticalConditionsRaw(DynamicRelationPairKey key, Hero firstHero, Hero secondHero)
        {
            float day = CurrentDay;
            int titleRevision = FeudalTitleBehavior.Instance?.RuntimeRevision ?? 0;
            if (_politicalCache.TryGetValue(key, out PoliticalCacheEntry cached) && day - cached.Day < PoliticalCacheLifetimeDays
                && cached.PoliticalRevision == _politicalRevision && cached.TitleRevision == titleRevision
                && cached.CustodyModifier == DynamicRelationBaselineHelper.GetPrisonerCustodyModifier(firstHero, secondHero))
            {
#if DEBUG
                _diagnosticPoliticalHits++;
#endif
                return cached.Value;
            }
#if DEBUG
            _diagnosticPoliticalMisses++;
#endif
            int value = DynamicRelationBaselineHelper.CalculatePoliticalConditionsRaw(firstHero, secondHero);
            _politicalCache[key] = new PoliticalCacheEntry { Value = value, Day = day, PoliticalRevision = _politicalRevision, TitleRevision = titleRevision,
                CustodyModifier = DynamicRelationBaselineHelper.GetPrisonerCustodyModifier(firstHero, secondHero) };
            EnqueueCacheKey(key, _politicalPruneQueue, _queuedPoliticalKeys);
            return value;
        }

        private void InvalidateFoundationForHero(Hero hero)
        {
            if (hero == null || _foundationCache.Count == 0)
                return;
            _cacheRemovalBuffer.Clear();
            foreach (DynamicRelationPairKey key in _foundationCache.Keys)
            {
                if (key.FirstHero == hero || key.SecondHero == hero)
                    _cacheRemovalBuffer.Add(key);
            }
            foreach (DynamicRelationPairKey key in _cacheRemovalBuffer)
                _foundationCache.Remove(key);
            if (_cacheRemovalBuffer.Count > 0)
                _foundationRevision++;
            _cacheRemovalBuffer.Clear();
        }

        private void PruneExpiredCaches()
        {
            PruneCache(_visibleRelationCache, _visiblePruneQueue, _queuedVisibleKeys, CurrentDay, entry => entry.ValidUntilDay);
            PruneCache(
                _foundationCache,
                _foundationPruneQueue,
                _queuedFoundationKeys,
                CurrentDay - FoundationCacheLifetimeDays,
                entry => entry.Day);
            PruneCache(
                _politicalCache,
                _politicalPruneQueue,
                _queuedPoliticalKeys,
                CurrentDay - PoliticalCacheLifetimeDays,
                entry => entry.Day);
        }

        private static void PruneCache<TEntry>(
            Dictionary<DynamicRelationPairKey, TEntry> cache,
            Queue<DynamicRelationPairKey> queue,
            HashSet<DynamicRelationPairKey> queuedKeys,
            float cutoffDay,
            Func<TEntry, float> getDay)
        {
            if (queue.Count == 0)
                return;

            int remainingBudget = Math.Min(CachePruneBudgetPerDay, queue.Count);
            while (remainingBudget-- > 0)
            {
                DynamicRelationPairKey key = queue.Dequeue();
                queuedKeys.Remove(key);
                if (!cache.TryGetValue(key, out TEntry entry))
                    continue;

                if (getDay(entry) <= cutoffDay)
                    cache.Remove(key);
                else
                    EnqueueCacheKey(key, queue, queuedKeys);
            }
        }

        private static void EnqueueCacheKey(
            DynamicRelationPairKey key,
            Queue<DynamicRelationPairKey> queue,
            HashSet<DynamicRelationPairKey> queuedKeys)
        {
            if (queuedKeys.Add(key))
                queue.Enqueue(key);
        }

        private void PruneExpiredMemories(bool force)
        {
            float day = CurrentDay;
            if (!force && day < _nextMemoryPruneDay)
                return;

#if DEBUG
            long started = System.Diagnostics.Stopwatch.GetTimestamp();
            _diagnosticPruneRuns++;
            _diagnosticPruneScanned += _relationMemories.Count;
#endif
            _nextMemoryPruneDay = float.MaxValue;
            int retained = 0;
            for (int index = 0; index < _relationMemories.Count; index++)
            {
                RelationMemoryRecord memory = _relationMemories[index];
                if (memory == null || memory.GetCurrentValue(day) == 0)
                {
                    RemoveMemoryFromIndex(memory);
                    continue;
                }

                _relationMemories[retained++] = memory;
                if (memory.ExpiryDay >= 0f)
                    _nextMemoryPruneDay = Math.Min(_nextMemoryPruneDay, memory.ExpiryDay);
            }
            int removed = _relationMemories.Count - retained;
            if (removed > 0) _relationMemories.RemoveRange(retained, removed);
#if DEBUG
            _diagnosticPruneRemoved += removed;
            _diagnosticPruneTicks += System.Diagnostics.Stopwatch.GetTimestamp() - started;
#endif
        }

        private void RemoveMemoryFromIndex(RelationMemoryRecord memory)
        {
            if (memory == null)
                return;

            InvalidateMemoryPair(memory);

            if (memory.Scope == RelationMemoryScope.House)
            {
                Clan firstClan = ResolveClan(memory.FirstId);
                Clan secondClan = ResolveClan(memory.SecondId);
                if (firstClan == null || secondClan == null || firstClan == secondClan)
                    return;

                ClanPairKey key = new ClanPairKey(firstClan, secondClan);
                if (_houseMemoriesByPair.TryGetValue(key, out List<RelationMemoryRecord> memories))
                {
                    memories.Remove(memory);
                    if (memories.Count == 0)
                        _houseMemoriesByPair.Remove(key);
                }
                return;
            }

            Hero firstHero = ResolveHero(memory.FirstId);
            Hero secondHero = ResolveHero(memory.SecondId);
            if (firstHero == null || secondHero == null || firstHero == secondHero)
                return;

            DynamicRelationPairKey heroKey = new DynamicRelationPairKey(firstHero, secondHero);
            if (_personalMemoriesByPair.TryGetValue(heroKey, out List<RelationMemoryRecord> personalMemories))
            {
                personalMemories.Remove(memory);
                if (personalMemories.Count == 0)
                    _personalMemoriesByPair.Remove(heroKey);
            }
        }

        private void RebuildMemoryIndexes()
        {
            _houseMemoryRevisions.Clear();
            _personalMemoriesByPair.Clear();
            _houseMemoriesByPair.Clear();
            _nextMemoryPruneDay = float.MaxValue;
            if (_relationMemories == null)
                _relationMemories = new List<RelationMemoryRecord>();

            foreach (RelationMemoryRecord memory in _relationMemories)
            {
                if (memory == null || memory.GetCurrentValue(CurrentDay) == 0)
                    continue;
                if (!TryGetOrCreateMemoryBucket(memory, out List<RelationMemoryRecord> pairMemories))
                    continue;
                pairMemories.Add(memory);
                if (memory.ExpiryDay >= 0f)
                    _nextMemoryPruneDay = Math.Min(_nextMemoryPruneDay, memory.ExpiryDay);
            }

            _memoryRevision++;
        }

        private int GetHouseMemoryRevision(Hero first, Hero second)
        {
            Clan a = first?.Clan;
            Clan b = second?.Clan;
            return a != null && b != null && a != b
                && _houseMemoryRevisions.TryGetValue(new ClanPairKey(a, b), out int revision) ? revision : 0;
        }

        private void InvalidateMemoryPair(RelationMemoryRecord memory)
        {
            if (memory.Scope == RelationMemoryScope.Personal)
            {
                Hero first = ResolveHero(memory.FirstId);
                Hero second = ResolveHero(memory.SecondId);
                if (first != null && second != null)
                    _visibleRelationCache.Remove(new DynamicRelationPairKey(first, second));
            }
            else
            {
                Clan first = ResolveClan(memory.FirstId);
                Clan second = ResolveClan(memory.SecondId);
                if (first == null || second == null || first == second) return;
                var key = new ClanPairKey(first, second);
                _houseMemoryRevisions.TryGetValue(key, out int revision);
                _houseMemoryRevisions[key] = revision + 1;
            }
        }

        private void MigrateLegacyRelationRecords()
        {
            if (_memorySchemaVersion >= CurrentMemorySchemaVersion)
                return;

            int migrated = 0;
            float weeklyDecay = Math.Max(0f, BellumCivileOptions.DynamicRelationWeeklyDrift);
            foreach (KeyValuePair<DynamicRelationPairKey, DynamicRelationRecord> entry in _runtimeRelationRecords.ToList())
            {
                Hero firstHero = entry.Key.FirstHero;
                Hero secondHero = entry.Key.SecondHero;
                DynamicRelationRecord oldRecord = entry.Value;
                if (oldRecord == null || !ShouldAffectPair(firstHero, secondHero))
                    continue;

                int natural = DynamicRelationBaselineHelper.CalculateRaw(firstHero, secondHero);
                int initialResidual = MBMath.ClampInt(oldRecord.LastRecordedValue, -100, 100) - natural;
                RelationMemoryRecord priorHistory = new RelationMemoryRecord(RelationMemoryScope.Personal, firstHero.StringId, secondHero.StringId,
                    RelationMemorySources.PriorHistory, null, initialResidual, oldRecord.LastUpdateDay,
                    CalculateLegacyExpiryDay(oldRecord.LastUpdateDay, initialResidual, weeklyDecay), weeklyDecay);
                int currentResidual = priorHistory.GetCurrentValue(CurrentDay);
                if (currentResidual != 0)
                    AddMemoryRecord(priorHistory);

                int visible = MBMath.ClampInt(natural + currentResidual, -100, 100);
                _runtimeMaterializedPairValues[entry.Key] = visible;
                SetRawRelation(firstHero, secondHero, visible);
                migrated++;
            }

            _runtimeRelationRecords.Clear();
            _serializedRelationRecords.Clear();
            _memorySchemaVersion = CurrentMemorySchemaVersion;
            if (migrated > 0)
                BellumCivileLogger.Log($"Migrated {migrated} dynamic relation records into the timed relationship-memory ledger without changing their current visible values.");
        }

        private static float CalculateLegacyExpiryDay(float startDay, int residual, float weeklyDecay)
        {
            if (residual == 0)
                return startDay;
            if (weeklyDecay <= 0f)
                return -1f;
            return startDay + (float)Math.Ceiling(Math.Abs(residual) / weeklyDecay) * 7f;
        }

        private string BuildCacheDiagnosticText()
        {
#if DEBUG
            return $"foundation_cache={_foundationCache.Count}; political_cache={_politicalCache.Count}; memories={_relationMemories.Count}; foundation_hits={_diagnosticFoundationHits}; foundation_misses={_diagnosticFoundationMisses}; political_hits={_diagnosticPoliticalHits}; political_misses={_diagnosticPoliticalMisses}; political_invalidations={_diagnosticPoliticalInvalidations}";
#else
            return $"foundation_cache={_foundationCache.Count}; political_cache={_politicalCache.Count}; memories={_relationMemories.Count}";
#endif
        }

        public string BuildPerformanceDiagnostics(bool reset)
        {
            string result = $"{BuildCacheDiagnosticText()}; visible_cache={_visibleRelationCache.Count}/{MaximumVisibleCacheEntries}; visible_queue={_visiblePruneQueue.Count}; materialized_pairs={_runtimeMaterializedPairValues.Count}; opening_balances={_runtimeOpeningAdjustments.Count}";
#if DEBUG
            string interval = _diagnosticStartDay.HasValue ? (CurrentDay - _diagnosticStartDay.Value).ToString("F2", System.Globalization.CultureInfo.InvariantCulture) : "since_load";
            double pruneMs = _diagnosticPruneTicks * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            result += $"; interval_days={interval}; reads={_diagnosticRelationReads}; eligible_reads={_diagnosticEligibleReads}; visible_hits={_diagnosticVisibleHits}; visible_misses={_diagnosticVisibleMisses}; prune_runs={_diagnosticPruneRuns}; prune_scanned={_diagnosticPruneScanned}; prune_removed={_diagnosticPruneRemoved}; prune_ms={pruneMs.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)}";
            if (reset)
            {
                _diagnosticRelationReads = _diagnosticEligibleReads = _diagnosticFoundationHits = _diagnosticFoundationMisses = 0;
                _diagnosticPoliticalHits = _diagnosticPoliticalMisses = _diagnosticPoliticalInvalidations = 0;
                _diagnosticVisibleHits = _diagnosticVisibleMisses = 0;
                _diagnosticPruneRuns = _diagnosticPruneScanned = _diagnosticPruneRemoved = _diagnosticPruneTicks = 0;
                _diagnosticStartDay = CurrentDay;
                result += "; counters reset (relations and caches unchanged)";
            }
#else
            result += "; detailed counters require a DEBUG build";
#endif
            return result;
        }

        private static bool ShouldAffectPair(Hero firstHero, Hero secondHero)
        {
            if (firstHero == null || secondHero == null || firstHero == secondHero || !IsEligibleHero(firstHero) || !IsEligibleHero(secondHero))
                return false;
            Clan firstClan = firstHero.Clan;
            Clan secondClan = secondHero.Clan;
            if ((firstClan != null && firstClan.IsMinorFaction && firstClan != Clan.PlayerClan && !IsRelationshipMercenaryClan(firstClan))
                || (secondClan != null && secondClan.IsMinorFaction && secondClan != Clan.PlayerClan && !IsRelationshipMercenaryClan(secondClan)))
                return false;
            if (firstHero.IsNotable || secondHero.IsNotable)
                return true;
            if (firstClan == null || secondClan == null)
                return false;
            if (firstClan == secondClan)
                return true;
            if (firstClan.Kingdom == null && secondClan.Kingdom == null && firstClan != Clan.PlayerClan && secondClan != Clan.PlayerClan)
                return false;
            return true;
        }

        private static bool IsEligibleHero(Hero hero)
        {
            if (hero == null || !hero.IsAlive || hero.IsDisabled || hero.IsChild)
                return false;
            if (hero.IsNotable)
                return true;
            if (hero == Hero.MainHero || hero.Clan == Clan.PlayerClan)
                return true;
            if (!hero.IsLord || hero.Clan == null)
                return false;
            if (hero.Clan.IsNoble)
                return true;
            if (IsRelationshipMercenaryClan(hero.Clan))
                return true;
            return hero.Clan.Kingdom != null && !hero.Clan.IsMinorFaction && !hero.Clan.IsUnderMercenaryService;
        }

        internal static bool IsPersonalOnlyPair(Hero first, Hero second) => first != null && second != null
            && (first.IsNotable || second.IsNotable || (first.Clan != null && first.Clan == second.Clan));

        private void RebuildOpeningAdjustments()
        {
            _runtimeOpeningAdjustments.Clear();
            foreach (var pair in _openingRelationAdjustments)
                if (TryResolveSerializedPair(pair.Key, out Hero first, out Hero second))
                    _runtimeOpeningAdjustments[new DynamicRelationPairKey(first, second)] = pair.Value;
        }

        private void MigrateBloodKinshipBaseline()
        {
            if (_bloodKinshipBaselineMigrated && _spouseBaselineMigrated && _runtimeOpeningAdjustments.Count == 0) return;
            // Only previously materialized pairs need rebasing. New pairs preserve their opening score on first access.
            foreach (var pair in _runtimeMaterializedPairValues)
            {
                Hero first = pair.Key.FirstHero;
                Hero second = pair.Key.SecondHero;
                if (!ShouldAffectPair(first, second)) continue;
                int increase = !_bloodKinshipBaselineMigrated
                    ? DynamicRelationBaselineHelper.CalculateKinshipBaselineIncrease(first, second)
                    : !_spouseBaselineMigrated ? DynamicRelationBaselineHelper.CalculateSpouseBaselineIncrease(first, second) : 0;
                if (increase == 0) continue;
                _runtimeOpeningAdjustments.TryGetValue(pair.Key, out int previous);
                _runtimeOpeningAdjustments[pair.Key] = previous - increase;
            }
            _bloodKinshipBaselineMigrated = true;
            _spouseBaselineMigrated = true;
            // Retain the old save key for migration, but never create permanent opening offsets again.
            foreach (var pair in _runtimeOpeningAdjustments)
                AddOpeningHistory(pair.Key, pair.Value);
            _runtimeOpeningAdjustments.Clear();
            _openingRelationAdjustments.Clear();
            _foundationCache.Clear();
            ClearVisibleRelationCache();
        }

        private void AddOpeningHistory(DynamicRelationPairKey pair, int value)
        {
            if (value == 0) return;
            float duration = RelationMemoryService.BuildFallbackDescriptor(value).DurationDays;
            var memory = new RelationMemoryRecord(RelationMemoryScope.Personal,
                pair.FirstHero.StringId, pair.SecondHero.StringId, RelationMemorySources.PriorPersonalHistory,
                null, value, CurrentDay, CurrentDay + duration);
            // Before session launch, use the saved ledger scale; launch applies any MCM change to the whole ledger.
            if (!_memoryDurationReady)
                memory.RescaleRemainingDuration(CurrentDay, _appliedMemoryDurationMultiplier);
            AddMemoryRecord(memory);
        }

        internal static bool IsRelationshipMercenaryClan(Clan clan)
        {
            if (clan == null || clan.IsEliminated || clan.IsBanditFaction)
                return false;
            if (clan == Clan.PlayerClan)
                return true;
            return clan.IsClanTypeMercenary && !ModIntegrationHelper.IsCustomSpawnsForceNoKingdomClan(clan);
        }

        private static bool IsViolentDeath(KillCharacterAction.KillCharacterActionDetail detail)
        {
            return detail == KillCharacterAction.KillCharacterActionDetail.DiedInBattle
                || detail == KillCharacterAction.KillCharacterActionDetail.WoundedInBattle
                || IsDeliberateDeath(detail);
        }

        private static bool IsDeliberateDeath(KillCharacterAction.KillCharacterActionDetail detail)
        {
            return detail == KillCharacterAction.KillCharacterActionDetail.Executed
                || detail == KillCharacterAction.KillCharacterActionDetail.ExecutionAfterMapEvent
                || detail == KillCharacterAction.KillCharacterActionDetail.Murdered;
        }

        private bool CanBuildPlayerRelationTooltip(Hero playerHero, Hero viewedHero)
        {
            return BellumCivileOptions.EnableDynamicRelationDrift && playerHero != null && viewedHero != null
                && viewedHero != playerHero && ShouldAffectPair(playerHero, viewedHero);
        }

        private static string BuildMemoryText(RelationMemoryRecord memory)
        {
            return RelationMemorySources.GetDisplayText(memory.SourceId, memory.ContextText) + ": "
                + FormatSigned(memory.GetCurrentValue(CurrentDay)) + " (" + BuildMemoryDurationText(memory) + ")";
        }

        private static string BuildOpinionTooltipTitle(Hero viewedHero, Hero playerHero)
        {
            TextObject title = new TextObject("{=BC_RelationMemory_OpinionTitle}{CHARACTER_NAME}'s Opinion of {PLAYER_NAME}");
            title.SetTextVariable("CHARACTER_NAME", viewedHero?.Name ?? new TextObject(string.Empty));
            title.SetTextVariable("PLAYER_NAME", playerHero?.Name ?? new TextObject(string.Empty));
            return title.ToString();
        }

        private static string BuildOpinionTooltipTitleSubject(Hero viewedHero)
        {
            TextObject subject = new TextObject("{=BC_RelationMemory_OpinionTitleSubject}{CHARACTER_NAME}'s");
            subject.SetTextVariable("CHARACTER_NAME", viewedHero?.Name ?? new TextObject(string.Empty));
            return subject.ToString();
        }

        private static string BuildOpinionTooltipTitlePredicate(Hero playerHero)
        {
            TextObject predicate = new TextObject("{=BC_RelationMemory_OpinionTitlePredicate}Opinion of {PLAYER_NAME}");
            predicate.SetTextVariable("PLAYER_NAME", playerHero?.Name ?? new TextObject(string.Empty));
            return predicate.ToString();
        }

        private static string BuildMemoryDurationText(RelationMemoryRecord memory)
        {
            if (memory.LegacyWeeklyDecay > 0f)
            {
                TextObject fading = new TextObject("{=BC_RelationMemory_Fading}fading by {RATE} per week");
                fading.SetTextVariable("RATE", memory.LegacyWeeklyDecay.ToString("0.##"));
                return fading.ToString();
            }
            if (memory.ExpiryDay < 0f)
                return new TextObject("{=BC_RelationMemory_Permanent}permanent").ToString();

            float remainingDays = Math.Max(0f, memory.ExpiryDay - CurrentDay);
            int daysInYear = Math.Max(1, CampaignTime.DaysInYear);
            if (remainingDays >= daysInYear)
            {
                TextObject years = new TextObject("{=BC_RelationMemory_YearsRemaining}{YEARS} years remaining");
                years.SetTextVariable("YEARS", (remainingDays / daysInYear).ToString("0.#"));
                return years.ToString();
            }
            TextObject days = new TextObject("{=BC_RelationMemory_DaysRemaining}{DAYS} days remaining");
            days.SetTextVariable("DAYS", Math.Max(1, (int)Math.Ceiling(remainingDays)));
            return days.ToString();
        }

        private void RebuildRuntimeRecordsFromSerialized()
        {
            _runtimeRelationRecords.Clear();
            if (_serializedRelationRecords == null || _serializedRelationRecords.Count == 0)
                return;
            foreach (KeyValuePair<string, DynamicRelationRecord> entry in _serializedRelationRecords)
            {
                if (entry.Value == null || !TryResolveSerializedPair(entry.Key, out Hero firstHero, out Hero secondHero) || !ShouldAffectPair(firstHero, secondHero))
                    continue;
                _runtimeRelationRecords[new DynamicRelationPairKey(firstHero, secondHero)] = entry.Value;
            }
        }

        private void RebuildSerializedRecordsFromRuntime()
        {
            if (_serializedRelationRecords == null)
                _serializedRelationRecords = new Dictionary<string, DynamicRelationRecord>();
            _serializedRelationRecords.Clear();
            foreach (KeyValuePair<DynamicRelationPairKey, DynamicRelationRecord> entry in _runtimeRelationRecords)
            {
                if (entry.Value != null && ShouldAffectPair(entry.Key.FirstHero, entry.Key.SecondHero))
                    _serializedRelationRecords[entry.Key.ToSerializedKey()] = entry.Value;
            }
        }

        private void RebuildRuntimeMaterializedValues()
        {
            _runtimeMaterializedPairValues.Clear();
            if (_materializedPairValues == null || _materializedPairValues.Count == 0)
                return;

            foreach (KeyValuePair<string, int> entry in _materializedPairValues)
            {
                if (TryResolveSerializedPair(entry.Key, out Hero firstHero, out Hero secondHero) && ShouldAffectPair(firstHero, secondHero))
                    _runtimeMaterializedPairValues[new DynamicRelationPairKey(firstHero, secondHero)] = MBMath.ClampInt(entry.Value, -100, 100);
            }
        }

        private void RebuildSerializedMaterializedValues()
        {
            if (_materializedPairValues == null)
                _materializedPairValues = new Dictionary<string, int>(StringComparer.Ordinal);
            _materializedPairValues.Clear();
            foreach (KeyValuePair<DynamicRelationPairKey, int> entry in _runtimeMaterializedPairValues)
            {
                if (ShouldAffectPair(entry.Key.FirstHero, entry.Key.SecondHero))
                    _materializedPairValues[entry.Key.ToSerializedKey()] = MBMath.ClampInt(entry.Value, -100, 100);
            }
        }

        private bool TryResolveSerializedPair(string serializedKey, out Hero firstHero, out Hero secondHero)
        {
            firstHero = null;
            secondHero = null;
            if (string.IsNullOrWhiteSpace(serializedKey))
                return false;
            int separator = serializedKey.IndexOf('|');
            if (separator <= 0 || separator >= serializedKey.Length - 1)
                return false;
            firstHero = ResolveHero(serializedKey.Substring(0, separator));
            secondHero = ResolveHero(serializedKey.Substring(separator + 1));
            return firstHero != null && secondHero != null;
        }

        private Hero ResolveHero(string heroId)
        {
            if (string.IsNullOrWhiteSpace(heroId))
                return null;
            if (_heroesById.TryGetValue(heroId, out Hero cachedHero))
                return cachedHero;
            try
            {
                Hero hero = Hero.FindFirst(candidate => candidate != null && candidate.StringId == heroId);
                if (hero != null)
                    _heroesById[heroId] = hero;
                return hero;
            }
            catch
            {
                return null;
            }
        }

        private Clan ResolveClan(string clanId)
        {
            if (string.IsNullOrWhiteSpace(clanId))
                return null;
            if (_clansById.TryGetValue(clanId, out Clan cachedClan))
                return cachedClan;
            try
            {
                Clan clan = Clan.FindFirst(candidate => candidate != null && candidate.StringId == clanId);
                if (clan != null)
                    _clansById[clanId] = clan;
                return clan;
            }
            catch
            {
                return null;
            }
        }

        private void RebuildObjectResolutionCaches()
        {
            _heroesById.Clear();
            _clansById.Clear();

            try
            {
                foreach (Hero hero in Hero.AllAliveHeroes)
                {
                    if (hero != null && !string.IsNullOrWhiteSpace(hero.StringId))
                        _heroesById[hero.StringId] = hero;
                }

                foreach (Clan clan in Clan.All)
                {
                    if (clan != null && !string.IsNullOrWhiteSpace(clan.StringId))
                        _clansById[clan.StringId] = clan;
                }
            }
            catch (Exception ex)
            {
                BellumCivileLogger.Log($"Relationship-memory object cache initialization was incomplete: {ex.GetType().Name}:{ex.Message}");
            }
        }

        private static int GetRawRelation(Hero firstHero, Hero secondHero)
        {
            SuppressRelationPatch = true;
            try
            {
                return CharacterRelationManager.GetHeroRelation(firstHero, secondHero);
            }
            finally
            {
                SuppressRelationPatch = false;
            }
        }

        private static void SetRawRelation(Hero firstHero, Hero secondHero, int value)
        {
            SuppressRelationPatch = true;
            try
            {
                CharacterRelationManager.SetHeroRelation(firstHero, secondHero, value);
            }
            finally
            {
                SuppressRelationPatch = false;
            }
        }

        private void RefreshMemoryDurationMultiplier()
        {
            if (!_memoryDurationReady)
                return;
            float multiplier = BellumCivileOptions.RelationMemoryDurationMultiplier;
            float previous = RelationMemoryRecord.NormalizeDurationMultiplier(_appliedMemoryDurationMultiplier);
            _appliedMemoryDurationMultiplier = multiplier;
            if (multiplier == previous)
                return;

            float day = CurrentDay;
            foreach (RelationMemoryRecord memory in _relationMemories)
                memory?.RescaleRemainingDuration(day, multiplier / previous);
            _memoryRevision++;
            ClearVisibleRelationCache();
            _nextMemoryPruneDay = 0f;
            BellumCivileLogger.Log($"Relation memory duration multiplier changed; previous={previous}; current={multiplier}; memories={_relationMemories.Count}.");
        }

        private void EnsureCollectionsInitialized()
        {
            if (_openingRelationAdjustments == null)
                _openingRelationAdjustments = new Dictionary<string, int>(StringComparer.Ordinal);
            if (_serializedRelationRecords == null)
                _serializedRelationRecords = new Dictionary<string, DynamicRelationRecord>();
            if (_relationMemories == null)
                _relationMemories = new List<RelationMemoryRecord>();
            if (_materializedPairValues == null)
                _materializedPairValues = new Dictionary<string, int>(StringComparer.Ordinal);
        }

        private static string FormatSigned(int value)
        {
            return value >= 0 ? "+" + value : value.ToString();
        }

        private static void AddTooltipSeparator(List<TooltipProperty> properties)
        {
            properties.Add(new TooltipProperty(string.Empty, string.Empty, 0, false, TooltipProperty.TooltipPropertyFlags.DefaultSeperator));
        }

        private static void AddTooltipBreakdownLine(List<TooltipProperty> properties, string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return;
            int split = line.LastIndexOf(':');
            if (split > 0 && split < line.Length - 1)
            {
                properties.Add(new TooltipProperty(line.Substring(0, split).Trim(), line.Substring(split + 1).Trim(), 0));
                return;
            }
            properties.Add(new TooltipProperty(line, string.Empty, 0, false, TooltipProperty.TooltipPropertyFlags.MultiLine));
        }

        private static float CurrentDay => (float)CampaignTime.Now.ToDays;
    }
}
