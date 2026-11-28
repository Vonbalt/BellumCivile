using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;

namespace BellumCivile.Behaviors
{
    /// <summary>
    /// Tracks original-house marriage standing before vanilla moves one
    /// spouse into the other's clan. This lets Bellum's political systems keep recognizing a
    /// royal marriage alliance after the spouses end up sharing a clan, but only while both
    /// spouses who created the alliance are still alive. Household-only records prevent native
    /// parent-clan checks from inventing employer alliances without guessing about older weddings.
    /// </summary>
    public class MarriageAllianceBehavior : CampaignBehaviorBase
    {
        private const string RecordVersion = "v3";
        private const string LegacyRecordVersion = "v2";
        private const string HouseholdRecordVersion = "v3h";
        private List<string> _marriageAlliancePairs = new List<string>();
        private Dictionary<string, CampaignTime> _marriageTruceWeddings = new Dictionary<string, CampaignTime>();
        private readonly Dictionary<ClanPairKey, (Hero First, Hero Second)> _activeAlliancePairs = new Dictionary<ClanPairKey, (Hero, Hero)>();
        private readonly Dictionary<ClanPairKey, CampaignTime> _latestActiveWeddings = new Dictionary<ClanPairKey, CampaignTime>();
        private readonly HashSet<string> _householdSpousePairs = new HashSet<string>();
        private bool _activeAllianceCacheDirty = true;

        public static MarriageAllianceBehavior Instance { get; private set; }

        public MarriageAllianceBehavior()
        {
            Instance = this;
        }

        public override void RegisterEvents()
        {
            Instance = this;
            CampaignEvents.BeforeHeroesMarried.AddNonSerializedListener(this, OnBeforeHeroesMarried);
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
            CampaignEvents.HeroKilledEvent.AddNonSerializedListener(this, OnHeroKilled);
            CampaignEvents.OnHeroChangedClanEvent.AddNonSerializedListener(this, OnHeroChangedClan);
        }

        public override void SyncData(IDataStore dataStore)
        {
            Instance = this;
            dataStore.SyncData("BellumCivile_MarriageAlliancePairs", ref _marriageAlliancePairs);
            dataStore.SyncData("BellumCivile_MarriageTruceWeddings", ref _marriageTruceWeddings);
            EnsureCollectionsInitialized();
            if (dataStore.IsLoading) MarkAllianceCacheDirty(invalidateRelationBaseline: false);
        }

        public bool AreClansIntermarried(Clan firstClan, Clan secondClan)
        {
            EnsureCollectionsInitialized();
            if (firstClan == null || secondClan == null || firstClan == secondClan)
                return false;

            EnsureActiveAllianceCache();
            return _activeAlliancePairs.ContainsKey(new ClanPairKey(firstClan, secondClan));
        }

        internal (Hero First, Hero Second) GetMarriageAllianceSpouses(Clan firstClan, Clan secondClan)
        {
            if (firstClan == null || secondClan == null || firstClan == secondClan) return (null, null);
            EnsureCollectionsInitialized();
            EnsureActiveAllianceCache();
            return _activeAlliancePairs.TryGetValue(new ClanPairKey(firstClan, secondClan), out var spouses) ? spouses : (null, null);
        }

        internal MarriageTruceStatus GetMarriageTruce(Clan firstClan, Clan secondClan)
        {
            int years = BellumCivileOptions.MarriageTruceYears;
            if (years == 0 || firstClan == null || secondClan == null || firstClan == secondClan
                || firstClan.IsEliminated || secondClan.IsEliminated)
                return default;

            EnsureActiveAllianceCache();
            var pair = new ClanPairKey(firstClan, secondClan);
            if (!_activeAlliancePairs.ContainsKey(pair)) return default;
            if (years == BellumCivileConstants.MarriageTruceLifetimeSetting)
                return new MarriageTruceStatus(0, isLifetime: true);
            if (!_latestActiveWeddings.TryGetValue(pair, out CampaignTime wedding)) return default;

            double remaining = years * (double)CampaignTime.DaysInYear - (CampaignTime.Now.ToDays - wedding.ToDays);
            return remaining > 0d ? new MarriageTruceStatus((int)Math.Ceiling(remaining)) : default;
        }

        internal bool HasUnexcludedNativeMarriage(Clan first, Clan second)
        {
            EnsureActiveAllianceCache();
            return HasUnexcludedNativeMarriageFrom(first, second) || HasUnexcludedNativeMarriageFrom(second, first);
        }

        private bool HasUnexcludedNativeMarriageFrom(Clan first, Clan second) => first.AliveLords.Any(hero =>
            hero?.Spouse?.Father?.Clan == second
            && !_householdSpousePairs.Contains(SpouseKey(hero.StringId, hero.Spouse.StringId)));

        private static string SpouseKey(string first, string second) => string.CompareOrdinal(first, second) <= 0
            ? first + "|" + second : second + "|" + first;

        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            EnsureCollectionsInitialized();
            PruneInvalidPairs();
        }

        private void OnBeforeHeroesMarried(Hero firstHero, Hero secondHero, bool showNotification)
        {
            Clan firstClan = firstHero?.Clan;
            Clan secondClan = secondHero?.Clan;
            TraceMarriage($"marriage observed: {HeroLabel(firstHero)} + {HeroLabel(secondHero)}; clans={firstClan?.StringId ?? "none"} / {secondClan?.StringId ?? "none"}.");

            if (firstClan == null || secondClan == null || firstClan == secondClan)
            {
                TraceMarriage("no inter-clan marriage alliance recorded: missing clan or both spouses already share a clan.");
                return;
            }
            // Capture standing in the original houses, before the wedding transfers either spouse.
            bool dynastic = MarriageParticipantPolicy.IsDynasticPair(firstHero, secondHero);
            string key = GetRecordKey(firstClan, secondClan, firstHero, secondHero);
            if (!dynastic && !string.IsNullOrEmpty(key)) key = HouseholdRecordVersion + key.Substring(RecordVersion.Length);
            if (!string.IsNullOrEmpty(key) && !_marriageAlliancePairs.Contains(key))
            {
                _marriageAlliancePairs.Add(key);
                if (dynastic) _marriageTruceWeddings[key] = CampaignTime.Now;
                MarkAllianceCacheDirty();
                if (!dynastic)
                {
                    TraceMarriage("household wedding recorded without a political employer alliance.");
                    return;
                }
                BellumCivileLogger.Log($"Marriage alliance recorded: {firstClan.StringId} <-> {secondClan.StringId} through {firstHero.StringId} and {secondHero.StringId}.");
                TraceMarriage($"alliance recorded: {firstClan.StringId} <-> {secondClan.StringId}; expires if {firstHero.StringId} or {secondHero.StringId} dies.");
            }
        }

        private void OnHeroKilled(Hero victim, Hero killer, KillCharacterAction.KillCharacterActionDetail detail, bool showNotification)
        {
            if (victim != null)
                MarkAllianceCacheDirty();
        }

        private void OnHeroChangedClan(Hero hero, Clan oldClan)
        {
            if (hero?.Spouse != null) MarkAllianceCacheDirty();
        }

        private void EnsureCollectionsInitialized()
        {
            if (_marriageAlliancePairs == null)
                _marriageAlliancePairs = new List<string>();
            if (_marriageTruceWeddings == null)
                _marriageTruceWeddings = new Dictionary<string, CampaignTime>();
        }

        private void PruneInvalidPairs()
        {
            if (!CanResolveCampaignObjects())
                return;

            List<string> pruned = new List<string>();
            List<string> validPairs = new List<string>();
            _activeAlliancePairs.Clear();
            _latestActiveWeddings.Clear();
            _householdSpousePairs.Clear();

            foreach (string pair in _marriageAlliancePairs)
            {
                if (TryResolvePair(pair, out var firstClan, out var secondClan, out var firstHero, out var secondHero, out string pruneReason))
                {
                    if (validPairs.Contains(pair)) continue;
                    validPairs.Add(pair);
                    if (pair.StartsWith(HouseholdRecordVersion + "|", StringComparison.Ordinal))
                        _householdSpousePairs.Add(SpouseKey(firstHero.StringId, secondHero.StringId));
                    else
                    {
                        AddActiveAlliance(firstClan, secondClan, firstHero, secondHero);
                        if (firstClan != secondClan && _marriageTruceWeddings.TryGetValue(pair, out CampaignTime wedding))
                        {
                            var key = new ClanPairKey(firstClan, secondClan);
                            if (!_latestActiveWeddings.TryGetValue(key, out CampaignTime latest) || wedding > latest)
                                _latestActiveWeddings[key] = wedding;
                        }
                    }
                }
                else
                {
                    if (pair != null) _marriageTruceWeddings.Remove(pair);
                    pruned.Add($"{pair} ({pruneReason})");
                }
            }

            _marriageAlliancePairs = validPairs;
            foreach (Hero hero in Hero.AllAliveHeroes)
            {
                Hero spouse = hero?.Spouse;
                Clan firstClan = hero?.Clan;
                Clan secondClan = spouse?.Clan;
                if (firstClan != null && secondClan != null && firstClan != secondClan && MarriageParticipantPolicy.IsDynasticPair(hero, spouse)
                    && !_householdSpousePairs.Contains(SpouseKey(hero.StringId, spouse.StringId)))
                    AddActiveAlliance(firstClan, secondClan, hero, spouse);
            }
            _activeAllianceCacheDirty = false;

            foreach (string entry in pruned)
                TraceMarriage($"alliance pruned: {entry}.");
        }

        private void EnsureActiveAllianceCache()
        {
            if (!_activeAllianceCacheDirty)
                return;

            PruneInvalidPairs();
        }

        private void AddActiveAlliance(Clan first, Clan second, Hero firstHero, Hero secondHero)
        {
            if (first == second) return;
            var key = new ClanPairKey(first, second);
            if (!_activeAlliancePairs.ContainsKey(key)) _activeAlliancePairs.Add(key, (firstHero, secondHero));
        }

        private void MarkAllianceCacheDirty(bool invalidateRelationBaseline = true)
        {
            _activeAllianceCacheDirty = true;
            if (invalidateRelationBaseline)
                Campaign.Current?.GetCampaignBehavior<DynamicRelationBehavior>()?.InvalidateBaselineCache();
        }

        private static bool TryResolvePair(string key, out Clan firstClan, out Clan secondClan,
            out Hero firstHero, out Hero secondHero, out string reason)
        {
            reason = "invalid record";
            firstClan = secondClan = null;
            firstHero = secondHero = null;

            if (!TryParseRecord(key, out string firstClanId, out string secondClanId, out string firstHeroId, out string secondHeroId))
            {
                reason = "old or malformed record";
                return false;
            }

            firstClan = FindClan(firstClanId);
            secondClan = FindClan(secondClanId);
            firstHero = FindHero(firstHeroId);
            secondHero = FindHero(secondHeroId);

            if (firstClan == null || secondClan == null)
            {
                reason = "clan missing";
                return false;
            }

            if (firstClan.IsEliminated || secondClan.IsEliminated)
            {
                reason = "clan eliminated";
                return false;
            }

            if (firstHero == null || secondHero == null)
            {
                reason = "spouse missing";
                return false;
            }

            if (!firstHero.IsAlive || !secondHero.IsAlive)
            {
                reason = "one spouse died";
                return false;
            }

            reason = null;
            return true;
        }

        private static bool TryParseRecord(string record, out string firstClanId, out string secondClanId, out string firstHeroId, out string secondHeroId)
        {
            firstClanId = null;
            secondClanId = null;
            firstHeroId = null;
            secondHeroId = null;

            if (string.IsNullOrWhiteSpace(record))
                return false;

            string[] parts = record.Split('|');
            // Old records lack pre-wedding household standing; retain those ties until normal expiry.
            if (parts.Length != 5 || !string.Equals(parts[0], RecordVersion, StringComparison.Ordinal)
                && !string.Equals(parts[0], LegacyRecordVersion, StringComparison.Ordinal)
                && !string.Equals(parts[0], HouseholdRecordVersion, StringComparison.Ordinal))
                return false;

            firstClanId = parts[1];
            secondClanId = parts[2];
            firstHeroId = parts[3];
            secondHeroId = parts[4];

            return !string.IsNullOrWhiteSpace(firstClanId)
                && !string.IsNullOrWhiteSpace(secondClanId)
                && !string.IsNullOrWhiteSpace(firstHeroId)
                && !string.IsNullOrWhiteSpace(secondHeroId);
        }

        private static string GetRecordKey(Clan firstClan, Clan secondClan, Hero firstHero, Hero secondHero)
        {
            if (firstClan == null || secondClan == null || firstHero == null || secondHero == null
                || string.IsNullOrEmpty(firstClan.StringId) || string.IsNullOrEmpty(secondClan.StringId)
                || string.IsNullOrEmpty(firstHero.StringId) || string.IsNullOrEmpty(secondHero.StringId))
                return string.Empty;

            return string.CompareOrdinal(firstClan.StringId, secondClan.StringId) <= 0
                ? $"{RecordVersion}|{firstClan.StringId}|{secondClan.StringId}|{firstHero.StringId}|{secondHero.StringId}"
                : $"{RecordVersion}|{secondClan.StringId}|{firstClan.StringId}|{secondHero.StringId}|{firstHero.StringId}";
        }

        private static Hero FindHero(string heroId)
        {
            if (string.IsNullOrWhiteSpace(heroId))
                return null;

            try
            {
                return Hero.FindFirst(hero => hero.StringId == heroId);
            }
            catch (ArgumentNullException)
            {
                return null;
            }
            catch (NullReferenceException)
            {
                return null;
            }
        }

        private static Clan FindClan(string clanId)
        {
            if (string.IsNullOrWhiteSpace(clanId))
                return null;

            try
            {
                return Clan.All?.FirstOrDefault(c => c.StringId == clanId);
            }
            catch (ArgumentNullException)
            {
                return null;
            }
            catch (NullReferenceException)
            {
                return null;
            }
        }

        private static bool CanResolveCampaignObjects()
        {
            try
            {
                if (Clan.All == null)
                    return false;

                Hero.FindFirst(_ => false);
                return true;
            }
            catch (ArgumentNullException)
            {
                return false;
            }
            catch (NullReferenceException)
            {
                return false;
            }
        }

        private static void TraceMarriage(string message)
        {
            BellumCivileDebug.TraceIfEnabled("marriage", message, requestInGameDisplay: true);
        }

        private static string HeroLabel(Hero hero)
        {
            if (hero == null)
                return "null";

            return $"{hero.StringId}({hero.Name}, clan={hero.Clan?.StringId ?? "none"})";
        }
    }
}
