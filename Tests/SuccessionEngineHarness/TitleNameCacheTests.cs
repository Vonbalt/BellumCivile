using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using BellumCivile;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Library;
using TaleWorlds.Localization;

internal static class TitleNameCacheTests
{
    private static Clan _clan, _otherClan;
    private static Hero _spouse, _leader;
    private static Kingdom _home;
    private static MBReadOnlyList<Kingdom> _realms;
    private static bool _child, _styled, _bloodRelative;
    private static int _formats, _realmReads, _validations;
    private static readonly List<Kingdom> RefreshedRealms = new List<Kingdom>();
    private static bool Yes(ref bool __result) { __result = true; return false; }
    private static bool No(ref bool __result) { __result = false; return false; }
    private static bool Child(ref bool __result) { __result = _child; return false; }
    private static bool House(ref Clan __result) { __result = _clan; return false; }
    private static bool Spouse(ref Hero __result) { __result = _spouse; return false; }
    private static bool Leader(ref Hero __result) { __result = _leader; return false; }
    private static bool Realm(ref Kingdom __result) { __result = _home; return false; }
    private static bool Ruler(Kingdom __instance, ref Clan __result)
    { __result = __instance == _home ? _clan : _otherClan; return false; }
    private static bool All(ref MBReadOnlyList<Kingdom> __result)
    { _realmReads++; __result = _realms; return false; }
    private static bool Format(Hero hero, string heroName, ref string formattedName, ref bool __result)
    { _formats++; formattedName = _styled ? "Prince " + heroName : heroName; __result = _styled; return false; }
    private static bool Validate(ref Hero heir, ref bool __result)
    { _validations++; heir = null; __result = false; return false; }
    private static bool Recalculate(Kingdom kingdom) { RefreshedRealms.Add(kingdom); return false; }
    private static bool BloodRelative(ref bool __result) { __result = _bloodRelative; return false; }
    private static T Blank<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));

    internal static void Run(Action<bool, string> check)
    {
        var assembly = typeof(FeudalTitleBehavior).Assembly;
        var patch = assembly.GetType("BellumCivile.Patches.FeudalTitleHeroNamePatch", true);
        var display = assembly.GetType("BellumCivile.FeudalTitleDisplayHelper", true);
        var previousTitles = FeudalTitleBehavior.Instance;
        var previousHeirs = DynasticHeirBehavior.Instance;
        var titles = new FeudalTitleBehavior();
        var heirs = new DynasticHeirBehavior();
        var h = new Harmony("bellum.test.title_name_cache");
        void Patch(System.Reflection.MethodBase method, string prefix) => h.Patch(method,
            prefix: new HarmonyMethod(typeof(TitleNameCacheTests), prefix));
        void Clear() => AccessTools.Method(patch, "InvalidateCache").Invoke(null, null);
        TextObject Read(Hero hero, string name = "Child")
        {
            object[] args = { hero, new TextObject(name) };
            AccessTools.Method(patch, "Postfix").Invoke(null, args);
            return (TextObject)args[1];
        }
        void Set(Kingdom realm, Hero hero) => AccessTools.Method(typeof(DynasticHeirBehavior), "SetReigningDynasticHeir")
            .Invoke(heirs, new object[] { realm, hero });
        Kingdom[] Realms(Hero hero) => ((IEnumerable<Kingdom>)AccessTools.Method(typeof(DynasticHeirBehavior),
            "GetReigningHeirRealmsForDisplay").Invoke(heirs, new object[] { hero })).ToArray();
        try
        {
            _clan = Blank<Clan>(); _otherClan = Blank<Clan>(); _leader = Blank<Hero>();
            _home = Blank<Kingdom>(); _home.StringId = "cache_home";
            var foreign = Blank<Kingdom>(); foreign.StringId = "cache_foreign";
            _realms = new MBReadOnlyList<Kingdom>(new List<Kingdom> { foreign, _home });
            _child = true; _styled = false; _spouse = null;
            _formats = _realmReads = _validations = 0;
            AccessTools.PropertySetter(typeof(FeudalTitleBehavior), "Instance").Invoke(null, new object[] { titles });
            AccessTools.PropertySetter(typeof(DynasticHeirBehavior), "Instance").Invoke(null, new object[] { heirs });
            Patch(AccessTools.Method(patch, "ShouldApplyTitlePrefix"), nameof(Yes));
            Patch(AccessTools.Method(display, "IsMercenaryLeader"), nameof(No));
            Patch(AccessTools.Method(display, "TryFormatHeroName"), nameof(Format));
            Patch(AccessTools.PropertyGetter(typeof(Hero), "Clan"), nameof(House));
            Patch(AccessTools.PropertyGetter(typeof(Hero), "Spouse"), nameof(Spouse));
            Patch(AccessTools.PropertyGetter(typeof(Hero), "IsChild"), nameof(Child));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "Leader"), nameof(Leader));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "Kingdom"), nameof(Realm));
            Patch(AccessTools.PropertyGetter(typeof(Kingdom), "RulingClan"), nameof(Ruler));
            Patch(AccessTools.PropertyGetter(typeof(Kingdom), "IsEliminated"), nameof(No));
            Patch(AccessTools.PropertyGetter(typeof(Kingdom), "All"), nameof(All));
            Patch(AccessTools.Method(typeof(DynasticHeirBehavior), "TryGetCachedReigningDynasticHeir"), nameof(Validate));
            Patch(AccessTools.Method(typeof(DynasticHeirBehavior), "RecalculateHeirForKingdom"), nameof(Recalculate));
            Patch(AccessTools.Method(assembly.GetType("BellumCivile.HereditaryRealmSuccession"), "IsBloodRelative"), nameof(BloodRelative));

            Clear();
            var child = Blank<Hero>(); var next = Blank<Hero>();
            for (int i = 0; i < 1000; i++) Read(child);
            check(_formats == 1 && Read(child).ToString() == "Child",
                "One thousand untitled name reads perform one formatting pass");
            Read(child, "Renamed");
            check(_formats == 2, "Native renaming invalidates a cached untitled result");
            _child = false; Read(child, "Renamed");
            check(_formats == 3, "Coming of age invalidates untitled child names immediately");
            _spouse = Blank<Hero>(); Read(child, "Renamed");
            check(_formats == 4, "Marriage invalidates cached title context");

            Set(_home, child); Set(foreign, child);
            check(Realms(child).SequenceEqual(new[] { foreign, _home }),
                "Foreign and multiple Crown heirs retain original realm priority");
            int scans = _realmReads;
            for (int i = 0; i < 1000; i++) Realms(next);
            check(_realmReads == scans, "Ordinary hero heir lookups reuse one realm index");
            object[] styleArgs = { next, null, null, null };
            AccessTools.Method(display, "TryGetCrownHeirDisplayStyle").Invoke(null, styleArgs);
            check(_validations == 0 && _realmReads == scans,
                "Untitled heroes trigger no realm scans or succession validation in Crown styling");

            _styled = true;
            check(Read(child).ToString() == "Prince Child", "Becoming Crown heir invalidates an earlier untitled name");
            Read(next);
            int before = _formats;
            Set(_home, next);
            Read(child); Read(next);
            check(_formats == before + 2, "Heir replacement invalidates both former and new heir names");
            check(Realms(child).SequenceEqual(new[] { foreign }) && Realms(next).SequenceEqual(new[] { _home }),
                "Heir replacement updates the reverse index without losing a second Crown");
            scans = _realmReads;
            Set(_home, next); Realms(next);
            check(_realmReads == scans, "Unchanged daily heir results do not rebuild the display index");
            Set(_home, null);
            check(Realms(next).Length == 0, "A Crown without an heir removes its former display association");

            AccessTools.Field(typeof(DynasticHeirBehavior), "_canResolveSavedObjects").SetValue(heirs, true);
            var queue = AccessTools.Method(typeof(DynasticHeirBehavior), "QueueHouseholdRefresh");
            var refresh = AccessTools.Method(typeof(DynasticHeirBehavior), "RefreshChangedHouseholds");
            RefreshedRealms.Clear(); _bloodRelative = false;
            queue.Invoke(heirs, new object[] { next, null });
            queue.Invoke(heirs, new object[] { next, null });
            refresh.Invoke(heirs, null);
            check(RefreshedRealms.SequenceEqual(new[] { _home }),
                "Household events coalesce and refresh the affected house without unrelated Crowns");
            refresh.Invoke(heirs, null);
            check(RefreshedRealms.Count == 1, "Idle hourly ticks perform no succession recalculation");
            RefreshedRealms.Clear(); _bloodRelative = true;
            queue.Invoke(heirs, new object[] { next, null });
            refresh.Invoke(heirs, null);
            check(RefreshedRealms.SequenceEqual(new[] { foreign, _home }),
                "Foreign blood ties refresh the relevant foreign Crown after household changes");

            var pending = AccessTools.Field(typeof(FeudalTitleBehavior), "_titleDisplayRefreshPending");
            pending.SetValue(titles, false);
            int revision = titles.RuntimeRevision, displayRevision = titles.DisplayRevision;
            AccessTools.Method(typeof(FeudalTitleBehavior), "RequestClaimIndexRebuild").Invoke(titles, new object[] { null });
            check(titles.RuntimeRevision == revision + 1 && titles.DisplayRevision == displayRevision
                && !(bool)pending.GetValue(titles), "Claim-only rebuild preserves display caches and still advances political revision");
            before = _formats; Read(child);
            check(_formats == before, "Claim-only rebuild does not reformat an unchanged hero name");
            AccessTools.Method(typeof(FeudalTitleBehavior), "RebuildRuntimeIndexes").Invoke(titles, new object[] { true, true });
            Read(child);
            check(_formats == before + 1 && titles.DisplayRevision == displayRevision + 1
                && (bool)pending.GetValue(titles), "Ownership/title rebuild invalidates display and schedules party-name refresh");
            AccessTools.PropertySetter(typeof(FeudalTitleBehavior), "Instance").Invoke(null, new object[] { new FeudalTitleBehavior() });
            before = _formats; Read(child);
            check(_formats == before + 1, "Changing campaign registry discards the prior name cache");
            var fresh = new DynasticHeirBehavior();
            check(!((IEnumerable<Kingdom>)AccessTools.Method(typeof(DynasticHeirBehavior), "GetReigningHeirRealmsForDisplay")
                .Invoke(fresh, new object[] { child })).Any(), "A new campaign has no inherited Crown display entries");
        }
        finally
        {
            Clear();
            h.UnpatchAll(h.Id);
            AccessTools.PropertySetter(typeof(FeudalTitleBehavior), "Instance").Invoke(null, new object[] { previousTitles });
            AccessTools.PropertySetter(typeof(DynasticHeirBehavior), "Instance").Invoke(null, new object[] { previousHeirs });
            _clan = _otherClan = null; _leader = _spouse = null; _home = null; _realms = null;
            _bloodRelative = false; RefreshedRealms.Clear();
        }
    }
}
