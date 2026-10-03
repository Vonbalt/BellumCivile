using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Localization;

namespace BellumCivile.Patches
{
    [HarmonyPatch(typeof(Hero), "Name", MethodType.Getter)]
    internal static class FeudalTitleHeroNamePatch
    {
        private const int CacheTtlMs = 30000;
        private static readonly ThreadLocal<bool> IsProcessing = new ThreadLocal<bool>(() => false);
        private static readonly Dictionary<Hero, CachedHeroName> NameCache = new Dictionary<Hero, CachedHeroName>();
        private static bool? _externalTitleNamePatchLoaded;
        private static FeudalTitleBehavior _cacheOwner;
        private static long _hits, _untitledHits, _misses, _clears, _heroInvalidations;

        private sealed class CachedHeroName
        {
            public string Name { get; }
            public int Timestamp { get; }
            public int TitleRevision { get; }
            public bool MercenaryLeader { get; }
            public string BaseName { get; }
            private readonly Clan _clan, _rulingClan, _spouseClan;
            private readonly Hero _leader, _spouse, _spouseLeader;
            private readonly Kingdom _realm;
            private readonly bool _child;

            public CachedHeroName(Hero hero, string baseName, string name, int timestamp, int titleRevision, bool mercenaryLeader)
            {
                Name = name;
                Timestamp = timestamp;
                TitleRevision = titleRevision;
                MercenaryLeader = mercenaryLeader;
                BaseName = baseName;
                _clan = hero.Clan;
                _leader = _clan?.Leader;
                _realm = _clan?.Kingdom;
                _rulingClan = _realm?.RulingClan;
                _spouse = hero.Spouse;
                _spouseClan = _spouse?.Clan;
                _spouseLeader = _spouseClan?.Leader;
                _child = hero.IsChild;
            }

            public bool Matches(Hero hero) => hero.Clan == _clan && _clan?.Leader == _leader
                && _clan?.Kingdom == _realm && _realm?.RulingClan == _rulingClan
                && hero.Spouse == _spouse && _spouse?.Clan == _spouseClan
                && _spouseClan?.Leader == _spouseLeader && hero.IsChild == _child;
        }

        internal static void InvalidateCache()
        {
            NameCache.Clear();
            _clears++;
        }

        internal static void InvalidateHero(Hero hero)
        {
            if (hero != null && NameCache.Remove(hero)) _heroInvalidations++;
        }

        internal static string BuildPerformanceDiagnostics(bool reset)
        {
            string result = $"Title names: entries={NameCache.Count}; hits={_hits}; untitled_hits={_untitledHits}; misses={_misses}; clears={_clears}; hero_invalidations={_heroInvalidations}; "
                + (DynasticHeirBehavior.Instance?.BuildDisplayCacheDiagnostics(reset) ?? "heir cache unavailable");
            if (reset) _hits = _untitledHits = _misses = _clears = _heroInvalidations = 0;
            return result;
        }

        [HarmonyPostfix]
        public static void Postfix(Hero __instance, ref TextObject __result)
        {
            if (IsProcessing.Value || __instance == null || __result == null)
                return;

            IsProcessing.Value = true;
            try
            {
                if (!ShouldApplyTitlePrefix(__instance))
                    return;

                if (_cacheOwner != FeudalTitleBehavior.Instance)
                {
                    InvalidateCache();
                    _cacheOwner = FeudalTitleBehavior.Instance;
                }
                int now = Environment.TickCount;
                int titleRevision = FeudalTitleBehavior.Instance.DisplayRevision;
                bool mercenaryLeader = FeudalTitleDisplayHelper.IsMercenaryLeader(__instance);
                string baseName = __result.ToString();
                if (string.IsNullOrWhiteSpace(baseName))
                    return;
                if (NameCache.TryGetValue(__instance, out CachedHeroName cached))
                {
                    int elapsed = now - cached.Timestamp;
                    if (cached.TitleRevision == titleRevision && cached.MercenaryLeader == mercenaryLeader
                        && elapsed >= 0 && elapsed < CacheTtlMs && cached.BaseName == baseName && cached.Matches(__instance))
                    {
                        _hits++;
                        if (cached.Name != null)
                            __result = new TextObject("{=!}" + cached.Name);
                        else _untitledHits++;
                        return;
                    }
                }

                _misses++;
                bool styled = FeudalTitleDisplayHelper.TryFormatHeroName(__instance, baseName, out string titledName)
                    && !string.Equals(baseName, titledName, StringComparison.Ordinal);
                // Null stores a successful decision to retain the native name.
                NameCache[__instance] = new CachedHeroName(__instance, baseName, styled ? titledName : null, now, titleRevision, mercenaryLeader);
                if (styled) __result = new TextObject("{=!}" + titledName);
            }
            catch (Exception ex)
            {
                BellumCivileLogger.Log($"Failed to apply feudal title name prefix to {__instance?.StringId ?? "unknown hero"}: {ex.Message}");
            }
            finally
            {
                IsProcessing.Value = false;
            }
        }

        internal static bool ShouldApplyTitlePrefix(Hero hero)
        {
            if (hero?.Clan == null)
                return false;

            bool isPlayerClan = hero.Clan == Clan.PlayerClan;

            if (!hero.IsAlive || hero.IsDisabled)
                return false;

            if (!isPlayerClan && (hero.IsWanderer || hero.IsNotable))
                return false;

            if (hero.Clan.IsMinorFaction && !isPlayerClan && !FeudalTitleDisplayHelper.IsMercenaryLeader(hero))
                return false;

            if (FeudalTitleBehavior.Instance == null)
                return false;

            return !IsExternalTitleNamePatchLoaded();
        }

        private static bool IsExternalTitleNamePatchLoaded()
        {
            if (_externalTitleNamePatchLoaded.HasValue)
                return _externalTitleNamePatchLoaded.Value;

            _externalTitleNamePatchLoaded = AppDomain.CurrentDomain.GetAssemblies().Any(assembly =>
            {
                string name = assembly.GetName().Name;
                return string.Equals(name, "Titles", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(name, "NobleTitlesPlus", StringComparison.OrdinalIgnoreCase);
            });

            return _externalTitleNamePatchLoaded.Value;
        }
    }
}
