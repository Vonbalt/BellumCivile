using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;

namespace BellumCivile.Behaviors
{
    /// <summary>
    /// Tracks inter-clan marriage alliances at the moment of marriage, before vanilla moves one
    /// spouse into the other's clan. This lets Bellum's political systems keep recognizing a
    /// royal marriage alliance after the spouses end up sharing a clan, but only while both
    /// spouses who created the alliance are still alive.
    /// </summary>
    public class MarriageAllianceBehavior : CampaignBehaviorBase
    {
        private const string RecordVersion = "v2";
        private List<string> _marriageAlliancePairs = new List<string>();
        private readonly HashSet<ClanPairKey> _activeAlliancePairs = new HashSet<ClanPairKey>();
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
            EnsureCollectionsInitialized();
        }

        public bool AreClansIntermarried(Clan firstClan, Clan secondClan)
        {
            EnsureCollectionsInitialized();
            if (firstClan == null || secondClan == null || firstClan == secondClan)
                return false;

            EnsureActiveAllianceCache();
            return _activeAlliancePairs.Contains(new ClanPairKey(firstClan, secondClan));
        }

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

            string key = GetRecordKey(firstClan, secondClan, firstHero, secondHero);
            if (!string.IsNullOrEmpty(key) && !_marriageAlliancePairs.Contains(key))
            {
                _marriageAlliancePairs.Add(key);
                MarkAllianceCacheDirty();
                BellumCivileLogger.Log($"Marriage alliance recorded: {firstClan.StringId} <-> {secondClan.StringId} through {firstHero.StringId} and {secondHero.StringId}.");
                TraceMarriage($"alliance recorded: {firstClan.StringId} <-> {secondClan.StringId}; expires if {firstHero.StringId} or {secondHero.StringId} dies.");
            }
        }

        private void OnHeroKilled(Hero victim, Hero killer, KillCharacterAction.KillCharacterActionDetail detail, bool showNotification)
        {
            if (victim?.Clan != null)
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
        }

        private void PruneInvalidPairs()
        {
            if (!CanResolveCampaignObjects())
                return;

            List<string> pruned = new List<string>();
            List<string> validPairs = new List<string>();

            foreach (string pair in _marriageAlliancePairs)
            {
                if (IsValidPairKey(pair, out string pruneReason))
                {
                    if (!validPairs.Contains(pair))
                        validPairs.Add(pair);
                }
                else
                {
                    pruned.Add($"{pair} ({pruneReason})");
                }
            }

            _marriageAlliancePairs = validPairs;
            RebuildActiveAllianceCache();

            foreach (string entry in pruned)
                TraceMarriage($"alliance pruned: {entry}.");
        }

        private void EnsureActiveAllianceCache()
        {
            if (!_activeAllianceCacheDirty)
                return;

            PruneInvalidPairs();
        }

        private void RebuildActiveAllianceCache()
        {
            _activeAlliancePairs.Clear();

            foreach (string record in _marriageAlliancePairs)
            {
                if (!TryParseRecord(record, out string firstClanId, out string secondClanId, out _, out _))
                    continue;

                Clan firstClan = FindClan(firstClanId);
                Clan secondClan = FindClan(secondClanId);
                if (firstClan != null && secondClan != null && firstClan != secondClan)
                    _activeAlliancePairs.Add(new ClanPairKey(firstClan, secondClan));
            }

            foreach (Hero hero in Hero.AllAliveHeroes)
            {
                Clan firstClan = hero?.Clan;
                Clan secondClan = hero?.Spouse?.Clan;
                if (firstClan != null && secondClan != null && firstClan != secondClan)
                    _activeAlliancePairs.Add(new ClanPairKey(firstClan, secondClan));
            }

            _activeAllianceCacheDirty = false;
        }

        private void MarkAllianceCacheDirty(bool invalidateRelationBaseline = true)
        {
            _activeAllianceCacheDirty = true;
            if (invalidateRelationBaseline)
                Campaign.Current?.GetCampaignBehavior<DynamicRelationBehavior>()?.InvalidateBaselineCache();
        }

        private static bool IsValidPairKey(string key)
        {
            return IsValidPairKey(key, out _);
        }

        private static bool IsValidPairKey(string key, out string reason)
        {
            reason = "invalid record";

            if (!TryParseRecord(key, out string firstClanId, out string secondClanId, out string firstHeroId, out string secondHeroId))
            {
                reason = "old or malformed record";
                return false;
            }

            Clan firstClan = FindClan(firstClanId);
            Clan secondClan = FindClan(secondClanId);
            Hero firstHero = FindHero(firstHeroId);
            Hero secondHero = FindHero(secondHeroId);

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

        private static bool IsActiveRecordForClans(string record, Clan firstClan, Clan secondClan)
        {
            if (!TryParseRecord(record, out string firstClanId, out string secondClanId, out _, out _))
                return false;

            return (string.Equals(firstClanId, firstClan.StringId, StringComparison.Ordinal)
                    && string.Equals(secondClanId, secondClan.StringId, StringComparison.Ordinal))
                || (string.Equals(firstClanId, secondClan.StringId, StringComparison.Ordinal)
                    && string.Equals(secondClanId, firstClan.StringId, StringComparison.Ordinal));
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
            if (parts.Length != 5 || !string.Equals(parts[0], RecordVersion, StringComparison.Ordinal))
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
