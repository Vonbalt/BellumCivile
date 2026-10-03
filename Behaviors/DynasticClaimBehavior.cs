using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;

namespace BellumCivile.Behaviors
{
    /// <summary>
    /// Tracks short-lived, kingdom-specific dynastic claims created by Bellum systems.
    /// Claims belong to a living claimant hero, not forever to the clan, so cadet
    /// branches can act like royal offshoots without flooding long campaigns with
    /// permanent pretenders.
    /// </summary>
    public class DynasticClaimBehavior : CampaignBehaviorBase
    {
        private const string RecordVersion = "v1";
        private const string CadetTieRecordVersion = "v1";
        private List<string> _claims = new List<string>();
        private List<string> _cadetDynasticTies = new List<string>();
        private Dictionary<string, DynasticClaimIndexEntry> _claimIndex = new Dictionary<string, DynasticClaimIndexEntry>();
        private Dictionary<string, CadetTieIndexEntry> _cadetTieIndex = new Dictionary<string, CadetTieIndexEntry>();
        private bool _canResolveClaimObjects;
        private bool _runtimeIndexesInitialized;
        private bool _runtimeIndexesObjectValidated;

        public override void RegisterEvents()
        {
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
            CampaignEvents.HeroKilledEvent.AddNonSerializedListener(this, OnHeroKilled);
        }

        public override void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData("BellumCivile_DynasticClaims", ref _claims);
            dataStore.SyncData("BellumCivile_DynasticCadetTies", ref _cadetDynasticTies);
            EnsureCollectionsInitialized();
            PruneMalformedClaims();
            PruneMalformedCadetTies();
            _runtimeIndexesInitialized = false;
        }

        public void RegisterClaim(Clan claimantClan, Hero claimantHero, Kingdom targetKingdom, Clan originDynastyClan, string source)
        {
            EnsureCollectionsInitialized();

            if (!IsPotentialClaim(claimantClan, claimantHero, targetKingdom))
                return;

            string key = BuildRecord(claimantClan, claimantHero, targetKingdom, originDynastyClan, source);
            if (string.IsNullOrWhiteSpace(key))
                return;

            _claims.RemoveAll(record =>
                TryParseRecord(record, out string clanId, out _, out string kingdomId, out _, out _)
                && clanId == claimantClan.StringId
                && kingdomId == targetKingdom.StringId);

            _claims.Add(key);
            if (_runtimeIndexesInitialized)
            {
                _claimIndex[BuildClaimIndexKey(claimantClan.StringId, targetKingdom.StringId)] =
                    new DynasticClaimIndexEntry(
                        claimantClan.StringId,
                        claimantHero.StringId,
                        targetKingdom.StringId,
                        originDynastyClan?.StringId,
                        string.IsNullOrWhiteSpace(source) ? "unknown" : source,
                        claimantClan,
                        claimantHero,
                        targetKingdom);
            }
            BellumCivileLogger.Log($"Dynastic claim registered: claimant={claimantClan.StringId} hero={claimantHero.StringId} kingdom={targetKingdom.StringId} origin={originDynastyClan?.StringId ?? "none"} source={source ?? "unknown"}.");
        }

        public void RegisterCadetDynasticTie(Clan cadetClan, Hero founderHero, Kingdom targetKingdom, Clan originDynastyClan, string source)
        {
            EnsureCollectionsInitialized();

            if (!IsPotentialCadetTie(cadetClan, founderHero, targetKingdom, originDynastyClan))
                return;

            string key = BuildCadetTieRecord(cadetClan, founderHero, targetKingdom, originDynastyClan, source);
            if (string.IsNullOrWhiteSpace(key))
                return;

            _cadetDynasticTies.RemoveAll(record =>
                TryParseCadetTieRecord(record, out string clanId, out _, out string kingdomId, out string originId, out _)
                && clanId == cadetClan.StringId
                && kingdomId == targetKingdom.StringId
                && originId == originDynastyClan.StringId);

            _cadetDynasticTies.Add(key);
            if (_runtimeIndexesInitialized)
            {
                _cadetTieIndex[BuildCadetTieIndexKey(cadetClan.StringId, targetKingdom.StringId, originDynastyClan.StringId)] =
                    new CadetTieIndexEntry(
                        cadetClan.StringId,
                        founderHero.StringId,
                        targetKingdom.StringId,
                        originDynastyClan.StringId,
                        cadetClan,
                        founderHero,
                        targetKingdom,
                        originDynastyClan);
            }
            BellumCivileLogger.Log($"Dynastic cadet tie registered: cadet={cadetClan.StringId} founder={founderHero.StringId} kingdom={targetKingdom.StringId} origin={originDynastyClan.StringId} source={source ?? "unknown"}.");
        }

        public bool HasActiveCadetDynasticTieToRulingClan(Clan cadetClan, Kingdom kingdom)
        {
            if (cadetClan == null || kingdom?.RulingClan == null)
                return false;

            EnsureRuntimeIndexes();
            string key = BuildCadetTieIndexKey(cadetClan.StringId, kingdom.StringId, kingdom.RulingClan.StringId);
            if (!_cadetTieIndex.TryGetValue(key, out CadetTieIndexEntry entry))
                return false;

            if (_canResolveClaimObjects
                && _runtimeIndexesObjectValidated
                && !IsPotentialCadetTieSafe(key, cadetClan, entry.FounderHero, kingdom, kingdom.RulingClan))
            {
                RemoveCadetTie(key);
                return false;
            }

            return true;
        }

        public bool HasActiveClaim(Clan claimantClan, Kingdom targetKingdom)
        {
            return TryGetActiveClaim(claimantClan, targetKingdom, out _, out _, out _);
        }

        public bool ShareActiveClaimOrigin(Clan firstClan, Clan secondClan, Kingdom targetKingdom)
        {
            if (!TryGetActiveClaim(firstClan, targetKingdom, out _, out string firstOrigin, out _))
                return false;

            if (!TryGetActiveClaim(secondClan, targetKingdom, out _, out string secondOrigin, out _))
                return false;

            return !string.IsNullOrWhiteSpace(firstOrigin)
                && string.Equals(firstOrigin, secondOrigin, StringComparison.Ordinal);
        }

        public bool IsDynasticClaimAlly(Clan supporterClan, Clan claimantClan, Kingdom targetKingdom)
        {
            if (supporterClan == null || claimantClan == null)
                return false;

            if (!TryGetActiveClaim(supporterClan, targetKingdom, out _, out string supporterOrigin, out _))
                return false;

            if (string.IsNullOrWhiteSpace(supporterOrigin))
                return false;

            if (string.Equals(supporterOrigin, claimantClan.StringId, StringComparison.Ordinal))
                return true;

            return TryGetActiveClaim(claimantClan, targetKingdom, out _, out string claimantOrigin, out _)
                && string.Equals(supporterOrigin, claimantOrigin, StringComparison.Ordinal);
        }

        private bool TryGetActiveClaim(Clan claimantClan, Kingdom targetKingdom, out Hero claimantHero, out string originDynastyClanId, out string source)
        {
            claimantHero = null;
            originDynastyClanId = null;
            source = null;

            if (claimantClan == null || targetKingdom == null)
                return false;

            EnsureRuntimeIndexes();
            string key = BuildClaimIndexKey(claimantClan.StringId, targetKingdom.StringId);
            if (!_claimIndex.TryGetValue(key, out DynasticClaimIndexEntry entry))
                return false;

            if (_canResolveClaimObjects
                && _runtimeIndexesObjectValidated
                && !IsPotentialClaimSafe(key, claimantClan, entry.ClaimantHero, targetKingdom))
            {
                RemoveClaim(key);
                return false;
            }

            claimantHero = entry.ClaimantHero ?? ResolveHero(entry.HeroId);
            originDynastyClanId = entry.OriginDynastyClanId;
            source = entry.Source;
            return true;
        }

        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            _canResolveClaimObjects = true;
            EnsureCollectionsInitialized();
            RebuildRuntimeIndexes(validateObjects: true);
        }

        private void OnDailyTick()
        {
            _canResolveClaimObjects = true;
            EnsureCollectionsInitialized();
            RebuildRuntimeIndexes(validateObjects: true);
        }

        private void OnHeroKilled(Hero victim, Hero killer, KillCharacterAction.KillCharacterActionDetail detail, bool showNotification)
        {
            if (victim == null || string.IsNullOrWhiteSpace(victim.StringId))
                return;

            EnsureCollectionsInitialized();
            int removed = _claims.RemoveAll(record =>
                TryParseRecord(record, out _, out string heroId, out _, out _, out _)
                && heroId == victim.StringId);

            if (removed > 0)
                BellumCivileLogger.Log($"Dynastic claim expired: claimant_hero={victim.StringId} died; removed={removed}.");

            int removedCadetTies = _cadetDynasticTies.RemoveAll(record =>
                TryParseCadetTieRecord(record, out _, out string founderId, out _, out _, out _)
                && founderId == victim.StringId);

            if (removedCadetTies > 0)
                BellumCivileLogger.Log($"Dynastic cadet tie expired: founder_hero={victim.StringId} died; removed={removedCadetTies}.");

            if (removed > 0 || removedCadetTies > 0)
                RebuildRuntimeIndexes(validateObjects: _canResolveClaimObjects);
        }

        private void EnsureRuntimeIndexes()
        {
            EnsureCollectionsInitialized();
            if (!_runtimeIndexesInitialized)
                RebuildRuntimeIndexes(validateObjects: _canResolveClaimObjects);
        }

        private void PruneMalformedClaims()
        {
            if (_claims == null)
                _claims = new List<string>();

            _claims = _claims
                .Where(record => TryParseRecord(record, out _, out _, out _, out _, out _))
                .Distinct()
                .ToList();
        }

        private void RebuildRuntimeIndexes(bool validateObjects)
        {
            EnsureCollectionsInitialized();

            Dictionary<string, Clan> clans = null;
            Dictionary<string, Hero> heroes = null;
            Dictionary<string, Kingdom> kingdoms = null;
            if (validateObjects && !TryBuildObjectLookups(out clans, out heroes, out kingdoms))
                validateObjects = false;

            List<string> validClaims = new List<string>();
            HashSet<string> seenClaimRecords = new HashSet<string>(StringComparer.Ordinal);
            Dictionary<string, DynasticClaimIndexEntry> claimIndex = new Dictionary<string, DynasticClaimIndexEntry>();
            foreach (string record in _claims)
            {
                if (!seenClaimRecords.Add(record)
                    || !TryParseRecord(record, out string clanId, out string heroId, out string kingdomId, out string originId, out string source))
                {
                    continue;
                }

                Clan claimantClan = null;
                Hero claimantHero = null;
                Kingdom targetKingdom = null;
                if (validateObjects)
                {
                    clans.TryGetValue(clanId, out claimantClan);
                    heroes.TryGetValue(heroId, out claimantHero);
                    kingdoms.TryGetValue(kingdomId, out targetKingdom);
                    if (!IsPotentialClaimSafe(record, claimantClan, claimantHero, targetKingdom))
                        continue;
                }

                validClaims.Add(record);
                string indexKey = BuildClaimIndexKey(clanId, kingdomId);
                if (!claimIndex.ContainsKey(indexKey))
                {
                    claimIndex.Add(indexKey, new DynasticClaimIndexEntry(
                        clanId,
                        heroId,
                        kingdomId,
                        originId,
                        source,
                        claimantClan,
                        claimantHero,
                        targetKingdom));
                }
            }

            List<string> validCadetTies = new List<string>();
            HashSet<string> seenCadetRecords = new HashSet<string>(StringComparer.Ordinal);
            Dictionary<string, CadetTieIndexEntry> cadetTieIndex = new Dictionary<string, CadetTieIndexEntry>();
            foreach (string record in _cadetDynasticTies)
            {
                if (!seenCadetRecords.Add(record)
                    || !TryParseCadetTieRecord(record, out string clanId, out string heroId, out string kingdomId, out string originId, out _))
                {
                    continue;
                }

                Clan cadetClan = null;
                Hero founderHero = null;
                Kingdom targetKingdom = null;
                Clan originDynastyClan = null;
                if (validateObjects)
                {
                    clans.TryGetValue(clanId, out cadetClan);
                    heroes.TryGetValue(heroId, out founderHero);
                    kingdoms.TryGetValue(kingdomId, out targetKingdom);
                    clans.TryGetValue(originId, out originDynastyClan);
                    if (!IsPotentialCadetTieSafe(record, cadetClan, founderHero, targetKingdom, originDynastyClan))
                        continue;
                }

                validCadetTies.Add(record);
                string indexKey = BuildCadetTieIndexKey(clanId, kingdomId, originId);
                if (!cadetTieIndex.ContainsKey(indexKey))
                {
                    cadetTieIndex.Add(indexKey, new CadetTieIndexEntry(
                        clanId,
                        heroId,
                        kingdomId,
                        originId,
                        cadetClan,
                        founderHero,
                        targetKingdom,
                        originDynastyClan));
                }
            }

            _claims = validClaims;
            _cadetDynasticTies = validCadetTies;
            _claimIndex = claimIndex;
            _cadetTieIndex = cadetTieIndex;
            _runtimeIndexesInitialized = true;
            _runtimeIndexesObjectValidated = validateObjects;
        }

        private void RemoveClaim(string indexKey)
        {
            if (string.IsNullOrWhiteSpace(indexKey))
                return;

            _claimIndex.Remove(indexKey);
            _claims.RemoveAll(record =>
                TryParseRecord(record, out string clanId, out _, out string kingdomId, out _, out _)
                && BuildClaimIndexKey(clanId, kingdomId) == indexKey);
        }

        private void RemoveCadetTie(string indexKey)
        {
            if (string.IsNullOrWhiteSpace(indexKey))
                return;

            _cadetTieIndex.Remove(indexKey);
            _cadetDynasticTies.RemoveAll(record =>
                TryParseCadetTieRecord(record, out string clanId, out _, out string kingdomId, out string originId, out _)
                && BuildCadetTieIndexKey(clanId, kingdomId, originId) == indexKey);
        }

        private void PruneMalformedCadetTies()
        {
            if (_cadetDynasticTies == null)
                _cadetDynasticTies = new List<string>();

            _cadetDynasticTies = _cadetDynasticTies
                .Where(record => TryParseCadetTieRecord(record, out _, out _, out _, out _, out _))
                .Distinct()
                .ToList();
        }

        private static bool TryBuildObjectLookups(
            out Dictionary<string, Clan> clans,
            out Dictionary<string, Hero> heroes,
            out Dictionary<string, Kingdom> kingdoms)
        {
            clans = new Dictionary<string, Clan>(StringComparer.Ordinal);
            heroes = new Dictionary<string, Hero>(StringComparer.Ordinal);
            kingdoms = new Dictionary<string, Kingdom>(StringComparer.Ordinal);

            try
            {
                AddObjectsToLookup(Clan.All, clans, clan => clan.StringId);
                AddObjectsToLookup(Hero.AllAliveHeroes, heroes, hero => hero.StringId);
                AddObjectsToLookup(Kingdom.All, kingdoms, kingdom => kingdom.StringId);
                return true;
            }
            catch (Exception ex)
            {
                BellumCivileLogger.Log($"Dynastic runtime index lookup build failed safely; error={ex.GetType().Name}:{ex.Message}");
                return false;
            }
        }

        private static void AddObjectsToLookup<T>(
            IEnumerable<T> objects,
            IDictionary<string, T> lookup,
            Func<T, string> getId)
            where T : class
        {
            if (objects == null)
                return;

            foreach (T value in objects)
            {
                string id = value != null ? getId(value) : null;
                if (!string.IsNullOrWhiteSpace(id) && !lookup.ContainsKey(id))
                    lookup.Add(id, value);
            }
        }

        private static bool IsPotentialClaimSafe(
            string record,
            Clan claimantClan,
            Hero claimantHero,
            Kingdom targetKingdom)
        {
            try
            {
                return IsPotentialClaim(claimantClan, claimantHero, targetKingdom);
            }
            catch (Exception ex)
            {
                BellumCivileLogger.Log($"Dynastic claim validation failed safely; record={record ?? "null"} error={ex.GetType().Name}:{ex.Message}");
                return false;
            }
        }

        private static bool IsPotentialCadetTieSafe(
            string record,
            Clan cadetClan,
            Hero founderHero,
            Kingdom targetKingdom,
            Clan originDynastyClan)
        {
            try
            {
                return IsPotentialCadetTie(cadetClan, founderHero, targetKingdom, originDynastyClan);
            }
            catch (Exception ex)
            {
                BellumCivileLogger.Log($"Dynastic cadet tie validation failed safely; record={record ?? "null"} error={ex.GetType().Name}:{ex.Message}");
                return false;
            }
        }

        private static bool IsPotentialClaim(Clan claimantClan, Hero claimantHero, Kingdom targetKingdom)
        {
            return claimantClan != null
                && claimantHero != null
                && targetKingdom != null
                && !claimantClan.IsEliminated
                && !claimantClan.IsBanditFaction
                && claimantClan.Kingdom == targetKingdom
                && targetKingdom.RulingClan != claimantClan
                && claimantClan.Leader == claimantHero
                && claimantHero.IsAlive
                && !claimantHero.IsDisabled;
        }

        private static bool IsPotentialCadetTie(Clan cadetClan, Hero founderHero, Kingdom targetKingdom, Clan originDynastyClan)
        {
            return cadetClan != null
                && founderHero != null
                && targetKingdom != null
                && originDynastyClan != null
                && !cadetClan.IsEliminated
                && !originDynastyClan.IsEliminated
                && !cadetClan.IsBanditFaction
                && cadetClan.Kingdom == targetKingdom
                && originDynastyClan.Kingdom == targetKingdom
                && cadetClan != originDynastyClan
                && founderHero.IsAlive
                && !founderHero.IsDisabled
                && founderHero.Clan == cadetClan;
        }

        private static string BuildRecord(Clan claimantClan, Hero claimantHero, Kingdom targetKingdom, Clan originDynastyClan, string source)
        {
            if (claimantClan == null || claimantHero == null || targetKingdom == null)
                return string.Empty;

            return string.Join("|",
                RecordVersion,
                claimantClan.StringId ?? string.Empty,
                claimantHero.StringId ?? string.Empty,
                targetKingdom.StringId ?? string.Empty,
                originDynastyClan?.StringId ?? string.Empty,
                string.IsNullOrWhiteSpace(source) ? "unknown" : source);
        }

        private static string BuildCadetTieRecord(Clan cadetClan, Hero founderHero, Kingdom targetKingdom, Clan originDynastyClan, string source)
        {
            if (cadetClan == null || founderHero == null || targetKingdom == null || originDynastyClan == null)
                return string.Empty;

            return string.Join("|",
                CadetTieRecordVersion,
                cadetClan.StringId ?? string.Empty,
                founderHero.StringId ?? string.Empty,
                targetKingdom.StringId ?? string.Empty,
                originDynastyClan.StringId ?? string.Empty,
                string.IsNullOrWhiteSpace(source) ? "unknown" : source);
        }

        private static string BuildClaimIndexKey(string clanId, string kingdomId)
        {
            return (clanId ?? string.Empty) + "\x1F" + (kingdomId ?? string.Empty);
        }

        private static string BuildCadetTieIndexKey(string clanId, string kingdomId, string originDynastyClanId)
        {
            return BuildClaimIndexKey(clanId, kingdomId) + "\x1F" + (originDynastyClanId ?? string.Empty);
        }

        private static bool TryParseRecord(string record, out string clanId, out string heroId, out string kingdomId, out string originDynastyClanId, out string source)
        {
            clanId = null;
            heroId = null;
            kingdomId = null;
            originDynastyClanId = null;
            source = null;

            if (string.IsNullOrWhiteSpace(record))
                return false;

            string[] parts = record.Split('|');
            if (parts.Length != 6 || parts[0] != RecordVersion)
                return false;

            clanId = parts[1];
            heroId = parts[2];
            kingdomId = parts[3];
            originDynastyClanId = parts[4];
            source = parts[5];
            return !string.IsNullOrWhiteSpace(clanId)
                && !string.IsNullOrWhiteSpace(heroId)
                && !string.IsNullOrWhiteSpace(kingdomId);
        }

        private static bool TryParseCadetTieRecord(string record, out string clanId, out string heroId, out string kingdomId, out string originDynastyClanId, out string source)
        {
            clanId = null;
            heroId = null;
            kingdomId = null;
            originDynastyClanId = null;
            source = null;

            if (string.IsNullOrWhiteSpace(record))
                return false;

            string[] parts = record.Split('|');
            if (parts.Length != 6 || parts[0] != CadetTieRecordVersion)
                return false;

            clanId = parts[1];
            heroId = parts[2];
            kingdomId = parts[3];
            originDynastyClanId = parts[4];
            source = parts[5];
            return !string.IsNullOrWhiteSpace(clanId)
                && !string.IsNullOrWhiteSpace(heroId)
                && !string.IsNullOrWhiteSpace(kingdomId)
                && !string.IsNullOrWhiteSpace(originDynastyClanId);
        }

        private static Hero ResolveHero(string heroId)
        {
            if (string.IsNullOrWhiteSpace(heroId))
                return null;

            try
            {
                return Hero.AllAliveHeroes?.FirstOrDefault(h => h != null && h.StringId == heroId);
            }
            catch
            {
                return null;
            }
        }

        private void EnsureCollectionsInitialized()
        {
            if (_claims == null)
                _claims = new List<string>();

            if (_cadetDynasticTies == null)
                _cadetDynasticTies = new List<string>();

            if (_claimIndex == null)
                _claimIndex = new Dictionary<string, DynasticClaimIndexEntry>();

            if (_cadetTieIndex == null)
                _cadetTieIndex = new Dictionary<string, CadetTieIndexEntry>();
        }

        private sealed class DynasticClaimIndexEntry
        {
            public DynasticClaimIndexEntry(
                string clanId,
                string heroId,
                string kingdomId,
                string originDynastyClanId,
                string source,
                Clan claimantClan,
                Hero claimantHero,
                Kingdom targetKingdom)
            {
                ClanId = clanId;
                HeroId = heroId;
                KingdomId = kingdomId;
                OriginDynastyClanId = originDynastyClanId;
                Source = source;
                ClaimantClan = claimantClan;
                ClaimantHero = claimantHero;
                TargetKingdom = targetKingdom;
            }

            public string ClanId { get; }
            public string HeroId { get; }
            public string KingdomId { get; }
            public string OriginDynastyClanId { get; }
            public string Source { get; }
            public Clan ClaimantClan { get; }
            public Hero ClaimantHero { get; }
            public Kingdom TargetKingdom { get; }
        }

        private sealed class CadetTieIndexEntry
        {
            public CadetTieIndexEntry(
                string clanId,
                string heroId,
                string kingdomId,
                string originDynastyClanId,
                Clan cadetClan,
                Hero founderHero,
                Kingdom targetKingdom,
                Clan originDynastyClan)
            {
                ClanId = clanId;
                HeroId = heroId;
                KingdomId = kingdomId;
                OriginDynastyClanId = originDynastyClanId;
                CadetClan = cadetClan;
                FounderHero = founderHero;
                TargetKingdom = targetKingdom;
                OriginDynastyClan = originDynastyClan;
            }

            public string ClanId { get; }
            public string HeroId { get; }
            public string KingdomId { get; }
            public string OriginDynastyClanId { get; }
            public Clan CadetClan { get; }
            public Hero FounderHero { get; }
            public Kingdom TargetKingdom { get; }
            public Clan OriginDynastyClan { get; }
        }
    }
}
