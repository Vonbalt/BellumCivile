using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using BellumCivile;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.AdvancedStartOptions;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Library;

internal static class AdvancedStartTitleTests
{
    private sealed class Store : IDataStore
    {
        internal readonly Dictionary<string, object> Data = new Dictionary<string, object>();
        public bool IsLoading { get; set; }
        public bool IsSaving => !IsLoading;

        public bool SyncData<T>(string key, ref T value)
        {
            if (!IsLoading) { Data[key] = value; return true; }
            if (!Data.TryGetValue(key, out object saved)) return false;
            value = (T)saved;
            return true;
        }
    }

    private static Campaign _campaign;
    private static AdvancedStartOptionsData _options;
    private static Clan _player;
    private static Kingdom _realm;
    private static FeudalTitleRecord _barony;
    private static readonly Dictionary<SettlementComponent, Settlement> Settlements = new Dictionary<SettlementComponent, Settlement>();
    private static MBReadOnlyList<Town> _fiefs;
    private static T Blank<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    private static bool Current(ref Campaign __result) { __result = _campaign; return false; }
    private static bool Options(ref AdvancedStartOptionsData __result) { __result = _options; return false; }
    private static bool Player(ref Clan __result) { __result = _player; return false; }
    private static bool Realm(ref Kingdom __result) { __result = _realm; return false; }
    private static bool Kingdoms(ref MBReadOnlyList<Kingdom> __result) { __result = new MBReadOnlyList<Kingdom>(new[] { _realm }); return false; }
    private static bool Fiefs(ref MBReadOnlyList<Town> __result) { __result = _fiefs; return false; }
    private static bool Landed(ref bool __result) { __result = true; return false; }
    private static bool SettlementOf(SettlementComponent __instance, ref Settlement __result) { __result = Settlements[__instance]; return false; }
    private static bool Owner(ref Clan __result) { __result = _player; return false; }
    private static bool Day(ref float __result) { __result = 0f; return false; }
    private static bool NoCapital(ref Settlement __result) { __result = null; return false; }
    private static bool NoAccession(ref CrownAccessionBehavior __result) { __result = null; return false; }
    private static bool NativeName(ref string __result) { __result = "Calradia"; return false; }
    private static bool Skip() => false;
    private static bool Barony(Settlement __0, ref FeudalTitleRecord __result, ref bool __2)
    {
        if (__0 != Settlements[_fiefs[1]])
            throw new InvalidOperationException("An already-held settlement was treated as a new grant.");
        __result = _barony;
        __2 = false;
        return false;
    }

    private static void SetOptions(string scenario, string playerStart, bool fast = false)
    {
        _options = new AdvancedStartOptionsData();
        if (scenario != null)
            _options.AddData(new AdvancedStartData<string>(DefaultAdvancedStartOptions.ScenarioId, "test", scenario));
        _options.AddData(new AdvancedStartData<string>(DefaultAdvancedStartOptions.StartTypeId, "test", playerStart));
        _options.AddData(new AdvancedStartData<bool>(DefaultAdvancedStartOptions.EnableFastModeId, "test", fast));
    }

    internal static void Run(Action<bool, string> check)
    {
        var harmony = new Harmony("bellum.tests.advanced-start-titles");
        void Patch(MethodBase method, string prefix) =>
            harmony.Patch(method, prefix: new HarmonyMethod(typeof(AdvancedStartTitleTests), prefix));
        Type titlesType = typeof(FeudalTitleBehavior);
        object Call(FeudalTitleBehavior behavior, string name, params object[] args) =>
            AccessTools.Method(titlesType, name).Invoke(behavior, args);
        bool UsesConfiguredHierarchy(FeudalTitleBehavior behavior) =>
            AccessTools.Property(titlesType, "ConfiguredHierarchyTitles").GetValue(behavior) != null;
        FeudalTitleType Rank(FeudalTitleBehavior behavior, string id) =>
            (FeudalTitleType)Call(behavior, "ResolveStartingCrownRank", id);

        try
        {
            _campaign = Blank<Campaign>();
            Patch(AccessTools.PropertyGetter(typeof(Campaign), nameof(Campaign.Current)), nameof(Current));
            Patch(AccessTools.PropertyGetter(typeof(Campaign), nameof(Campaign.AdvancedStartData)), nameof(Options));
            Patch(AccessTools.PropertyGetter(typeof(Kingdom), nameof(Kingdom.All)), nameof(Kingdoms));
            Patch(AccessTools.PropertyGetter(titlesType, "CurrentDay"), nameof(Day));
            var titles = new FeudalTitleBehavior();

            _options = null;
            Call(titles, "SelectStartingHierarchy", (object)null);
            check(UsesConfiguredHierarchy(titles), "Default Start without advanced options keeps configured titles");
            SetOptions(DefaultAdvancedStartOptions.ScenarioNoneId, DefaultAdvancedStartOptions.StartTypeVassalId, fast: true);
            Call(titles, "SelectStartingHierarchy", (object)null);
            check(UsesConfiguredHierarchy(titles), "Advanced Default keeps configured titles with Fast Mode and a Vassal start");
            SetOptions(null, DefaultAdvancedStartOptions.StartTypeDefaultId);
            Call(titles, "SelectStartingHierarchy", (object)null);
            check(UsesConfiguredHierarchy(titles), "An omitted Scenario value still means Default");

            foreach (string scenario in new[] { "LastStand", "InvasionId", "twofactionwar", "alternativecalradia", "nordinvasion" })
            {
                SetOptions(scenario, DefaultAdvancedStartOptions.StartTypeDefaultId);
                Call(titles, "SelectStartingHierarchy", (object)null);
                check(!UsesConfiguredHierarchy(titles) && (int)Call(titles, "ApplyConfiguredTitles", "test repair", true) == 0,
                    scenario + " skips the authored hierarchy even on a fresh registry");
                check(Rank(titles, "calradian_empire") == FeudalTitleType.Kingdom, scenario + " does not grant the special imperial rank");
                check(Rank(titles, "vlandia") == FeudalTitleType.Kingdom, scenario + " keeps the regular rank for other realms");
            }

            var store = new Store();
            SetOptions(DefaultAdvancedStartOptions.ScenarioUnitedEmpireId, DefaultAdvancedStartOptions.StartTypeKingId);
            Call(titles, "SelectStartingHierarchy", (object)null);
            Call(titles, "PrepareUnitedEmpireHierarchy");
            check(!UsesConfiguredHierarchy(titles), "United Empire falls back to generated titles if the native realm is missing");
            store.Data["BellumCivile_GeneratedHierarchyScenarioId"] = DefaultAdvancedStartOptions.ScenarioUnitedEmpireId;
            store.IsLoading = true;
            var reloaded = new FeudalTitleBehavior();
            reloaded.SyncData(store);
            check(!UsesConfiguredHierarchy(reloaded) && Rank(reloaded, "calradian_empire") == FeudalTitleType.Empire,
                "Older United Empire saves retain the generated hierarchy and imperial rank");
            var legacy = new FeudalTitleBehavior();
            legacy.SyncData(new Store { IsLoading = true });
            check(UsesConfiguredHierarchy(legacy) && Rank(legacy, "calradian_empire") == FeudalTitleType.Kingdom,
                "Legacy saves keep their previous title policy, even with alternate native options");
            check(Rank(titles, "empire") == FeudalTitleType.Kingdom && Rank(titles, "empire_w") == FeudalTitleType.Kingdom
                && Rank(titles, "empire_s") == FeudalTitleType.Kingdom, "Imperial rank does not spill over to the old regional IDs");

            Patch(AccessTools.PropertyGetter(typeof(Clan), nameof(Clan.PlayerClan)), nameof(Player));
            Patch(AccessTools.PropertyGetter(typeof(Clan), nameof(Clan.Kingdom)), nameof(Realm));
            Patch(AccessTools.PropertyGetter(typeof(Clan), nameof(Clan.Fiefs)), nameof(Fiefs));
            Patch(AccessTools.PropertyGetter(typeof(SettlementComponent), nameof(SettlementComponent.Settlement)), nameof(SettlementOf));
            Patch(AccessTools.PropertyGetter(typeof(Settlement), nameof(Settlement.OwnerClan)), nameof(Owner));
            Patch(AccessTools.Method(titlesType, "IsLandedSettlement"), nameof(Landed));
            Patch(AccessTools.Method(titlesType, "EnsureBaronyTitle", new[] { typeof(Settlement), typeof(string), typeof(bool).MakeByRefType() }), nameof(Barony));
            Patch(AccessTools.Method(titlesType, "SelectKingdomCapital"), nameof(NoCapital));
            Patch(AccessTools.PropertyGetter(typeof(CrownAccessionBehavior), nameof(CrownAccessionBehavior.Instance)), nameof(NoAccession));
            Patch(AccessTools.Method(titlesType.Assembly.GetType("BellumCivile.DynamicKingdomTitleNameHelper"), "GetNativeTitleRoot"), nameof(NativeName));
            foreach (string name in new[] { "ReconcileAllDeFactoParents", "ReconcileCurrentRealmTitles", "RefreshCachedTitleNames", "QueueServiceReviewForTitleAndChildren" })
                Patch(AccessTools.Method(titlesType, name), nameof(Skip));

            _player = Blank<Clan>(); _player.StringId = "player";
            var oldRuler = Blank<Clan>(); oldRuler.StringId = "ruler";
            _realm = Blank<Kingdom>(); _realm.StringId = "calradian_empire"; _realm.RulingClan = oldRuler;
            AccessTools.Field(typeof(Clan), "_kingdom").SetValue(oldRuler, _realm);
            Call(titles, "SelectStartingHierarchy", (object)null);
            Call(titles, "PrepareUnitedEmpireHierarchy");
            AccessTools.Field(titlesType, "_referenceLookupCachesReady").SetValue(titles, true);
            check(UsesConfiguredHierarchy(titles), "Fresh United Empire selects the customized hierarchy after native world setup");
            CheckUnitedEmpireRegistry(check, titles, titlesType);
            CheckUnitedEmpireOverrides(check, titlesType.Assembly);

            var newSave = new Store();
            titles.SyncData(newSave);
            newSave.IsLoading = true;
            var unitedReload = new FeudalTitleBehavior();
            unitedReload.SyncData(newSave);
            AccessTools.Field(titlesType, "_referenceLookupCachesReady").SetValue(unitedReload, true);
            check(UsesConfiguredHierarchy(unitedReload)
                && unitedReload.GetTitle("duchy_dryatica")?.ParentTitleId == FeudalTitleBehavior.BuildKingdomTitleId(_realm),
                "The full United Empire policy and seeded hierarchy survive save/load");
            var newFief = Blank<Town>();
            var ownedFief = Blank<Town>();
            Settlements[newFief] = Blank<Settlement>();
            Settlements[ownedFief] = Blank<Settlement>();
            _barony = new FeudalTitleRecord("barony", "Test", FeudalTitleType.Barony, "ruler", "ruler", "legal_duchy", "", "calradian_empire", 0, 0);
            string crownId = FeudalTitleBehavior.BuildKingdomTitleId(_realm);
            AccessTools.Field(titlesType, "_referenceLookupCachesReady").SetValue(titles, true);
            var crown = (FeudalTitleRecord)AccessTools.Method(titlesType, "EnsureKingdomTitle", new[] { typeof(Kingdom), typeof(string) })
                .Invoke(titles, new object[] { _realm, "test start" });
            check(crown.TitleType == FeudalTitleType.Empire && crown.TitleId == crownId && crown.DeJureHolderClanId == "ruler"
                && crown.DeFactoHolderClanId == "ruler", "The generated registry creates the canonical imperial crown for the native ruler");
            var records = (Dictionary<string, FeudalTitleRecord>)AccessTools.Field(titlesType, "_titlesById").GetValue(titles);
            records.Add(_barony.TitleId, _barony);

            _fiefs = new MBReadOnlyList<Town>(new[] { ownedFief });
            Call(titles, "ReconcileAdvancedPlayerStart", 7);
            _realm.RulingClan = _player;
            _fiefs = new MBReadOnlyList<Town>(new[] { ownedFief, newFief });
            Call(titles, "ReconcileAdvancedPlayerStart", 8);
            check(crown.DeJureHolderClanId == "ruler" && _barony.DeJureHolderClanId == "ruler",
                "The reconciliation waits for all native phase-eight handlers");
            Call(titles, "ReconcileAdvancedPlayerStart", 9);
            check(crown.DeJureHolderClanId == "player" && crown.DeFactoHolderClanId == "player" && crown.TitleType == FeudalTitleType.Empire,
                "A King start transfers both sides of the existing imperial crown to the player");
            check(_barony.DeJureHolderClanId == "player" && _barony.DeFactoHolderClanId == "player" && _barony.ParentTitleId == "legal_duchy",
                "A starting fief grants full rights without shifting its legal liege");
            _barony.SetDeJureHolder("later");
            Call(titles, "ReconcileAdvancedPlayerStart", 9);
            Call(reloaded, "ReconcileAdvancedPlayerStart", 9);
            check(_barony.DeJureHolderClanId == "later", "The phase handler cannot replay on another invocation or load");

            SetOptions(DefaultAdvancedStartOptions.ScenarioNoneId, DefaultAdvancedStartOptions.StartTypeVassalId);
            Call(titles, "SelectStartingHierarchy", (object)null);
            crown.SetDeJureHolder("ruler");
            crown.SetDeFactoHolder("ruler");
            _realm.RulingClan = oldRuler;
            _barony.SetDeJureHolder("ruler");
            _barony.SetDeFactoHolder("ruler");
            _fiefs = new MBReadOnlyList<Town>(new[] { ownedFief });
            Call(titles, "ReconcileAdvancedPlayerStart", 7);
            _fiefs = new MBReadOnlyList<Town>(new[] { ownedFief, newFief });
            Call(titles, "ReconcileAdvancedPlayerStart", 9);
            check(_barony.DeJureHolderClanId == "player" && _barony.DeFactoHolderClanId == "player"
                && _barony.ParentTitleId == "legal_duchy" && crown.DeFactoHolderClanId == "ruler" && crown.DeJureHolderClanId == "ruler",
                "A Vassal start grants full rights to the new fief and leaves both its legal liege and the crown intact");
        }
        finally
        {
            harmony.UnpatchAll(harmony.Id);
            Settlements.Clear();
        }
    }

    private static void CheckUnitedEmpireRegistry(Action<bool, string> check, FeudalTitleBehavior titles, Type titlesType)
    {
        T Field<T>(string name) => (T)AccessTools.Field(titlesType, name).GetValue(titles);
        var foreign = Blank<Kingdom>(); foreign.StringId = "vlandia";
        var foreignRuler = Blank<Clan>(); foreignRuler.StringId = "clan_vlandia_1";
        foreign.RulingClan = foreignRuler;
        Field<Dictionary<string, Clan>>("_clansByReferenceId").Add(foreignRuler.StringId, foreignRuler);
        var realms = Field<Dictionary<string, Kingdom>>("_kingdomsByReferenceId");
        realms.Add(_realm.StringId, _realm);
        realms.Add(foreign.StringId, foreign);
        AccessTools.Field(titlesType, "_referenceLookupCachesReady").SetValue(titles, true);

        var records = Field<Dictionary<string, FeudalTitleRecord>>("_titlesById");
        var castle = Blank<Settlement>(); castle.StringId = "castle_EN3";
        Field<Dictionary<string, Settlement>>("_settlementsByReferenceId").Add(castle.StringId, castle);
        string baronyId = FeudalTitleBehavior.BuildBaronyTitleId(castle);
        records.Add(baronyId, new FeudalTitleRecord(baronyId, "Rhesos", FeudalTitleType.Barony, "ruler", "ruler",
            "", castle.StringId, _realm.StringId, 0, 0));

        var ensure = AccessTools.Method(titlesType, "EnsureKingdomTitle", new[] { typeof(Kingdom), typeof(string) });
        var crown = (FeudalTitleRecord)ensure.Invoke(titles, new object[] { _realm, "test start" });
        var apply = AccessTools.Method(titlesType, "ApplyConfiguredTitles");
        apply.Invoke(titles, new object[] { "test start", true });

        string[] duchies = { "duchy_dryatica", "duchy_laconia", "duchy_lycarea", "duchy_tanaesia",
            "duchy_tripotamia", "duchy_ilatarea", "duchy_aminys", "duchy_erithrys" };
        check(duchies.All(id => records.TryGetValue(id, out FeudalTitleRecord duchy)
            && duchy.ParentTitleId == crown.TitleId && duchy.AssociatedKingdomId == _realm.StringId),
            "All eight imperial duchies attach directly to the one live imperial crown");
        check(records.Values.Count(t => t.TitleType == FeudalTitleType.Empire) == 1
            && !new[] { "empire", "empire_n", "empire_s", "empire_w" }.Any(id => records.ContainsKey("bc_title_kingdom_" + id)),
            "No regional crown or duplicate titular Empire is seeded");
        check(records["county_diatagmatia"].ParentTitleId == "duchy_dryatica"
            && records[baronyId].ParentTitleId == "county_diatagmatia",
            "Imperial county and barony borders are preserved");
        check(records["duchy_walhdok"].ParentTitleId == FeudalTitleBehavior.BuildKingdomTitleId(foreign)
            && records["duchy_walhdok"].DeJureHolderClanId == foreignRuler.StringId,
            "Vlandia keeps its ordinary hierarchy and holders");

        records[baronyId].SetParentTitle("later_legal_parent");
        apply.Invoke(titles, new object[] { "test repair", false });
        check(records[baronyId].ParentTitleId == "later_legal_parent", "Registry repair does not reapply starting legal borders");

        foreach (string houseId in new[] { "clan_empire_north_1", "clan_empire_south_1", "clan_empire_west_1" })
        {
            var unifier = Blank<Clan>(); unifier.StringId = houseId;
            AccessTools.Field(typeof(Clan), "_kingdom").SetValue(unifier, _realm);
            var originalRuler = _realm.RulingClan;
            _realm.RulingClan = unifier;
            var freshTitles = new FeudalTitleBehavior();
            CallNewUnifiedStart(freshTitles, titlesType);
            var freshCrown = (FeudalTitleRecord)ensure.Invoke(freshTitles, new object[] { _realm, "test unifier" });
            check(freshCrown.TitleType == FeudalTitleType.Empire && freshCrown.DeJureHolderClanId == houseId
                && freshCrown.DeFactoHolderClanId == houseId, "The native unifier owns the single Empire crown: " + houseId);
            _realm.RulingClan = originalRuler;
        }
    }

    private static void CallNewUnifiedStart(FeudalTitleBehavior titles, Type type)
    {
        AccessTools.Method(type, "SelectStartingHierarchy").Invoke(titles, new object[] { null });
        AccessTools.Method(type, "PrepareUnitedEmpireHierarchy").Invoke(titles, null);
        AccessTools.Field(type, "_referenceLookupCachesReady").SetValue(titles, true);
    }

    private static void CheckUnitedEmpireOverrides(Action<bool, string> check, Assembly assembly)
    {
        var configuredType = assembly.GetType("BellumCivile.FeudalTitleConfig+ConfiguredTitle");
        object Title(string id, FeudalTitleType rank, string kingdom, string parent = null, params string[] children)
        {
            var title = Activator.CreateInstance(configuredType);
            void Set(string field, object value) => configuredType.GetField(field).SetValue(title, value);
            Set("Id", id);
            Set("Type", rank);
            Set("KingdomRef", kingdom);
            Set("ParentRef", parent);
            Set("ChildRefs", children.ToList());
            return title;
        }

        object[] fixtures =
        {
            Title("renamed_east_crown", FeudalTitleType.Kingdom, "Eastern Empire", null, "new_duchy", "castle_example"),
            Title("new_duchy", FeudalTitleType.Duchy, null, null, "new_county"),
            Title("new_county", FeudalTitleType.County, "empire", "new_duchy", "castle_other"),
            Title("other_direct_duchy", FeudalTitleType.Duchy, "empire", "renamed_east_crown"),
            Title("foreign_duchy", FeudalTitleType.Duchy, "vlandia", "bc_title_kingdom_vlandia")
        };
        Array source = Array.CreateInstance(configuredType, fixtures.Length);
        Array.Copy(fixtures, source, fixtures.Length);
        var projectionType = assembly.GetType("BellumCivile.Behaviors.UnitedEmpireTitleHierarchy");
        var projection = AccessTools.Method(projectionType, "From").Invoke(null, new object[]
        {
            source, (Func<string, string>)(reference => reference == "Eastern Empire" ? "empire" : reference)
        });
        object Field(object title, string field) => configuredType.GetField(field).GetValue(title);
        var titles = ((IEnumerable)AccessTools.Property(projectionType, "Titles").GetValue(projection)).Cast<object>()
            .ToDictionary(title => (string)Field(title, "Id"));
        var directChildren = (IReadOnlyList<string>)AccessTools.Property(projectionType, "CrownChildren").GetValue(projection);
        check(!titles.ContainsKey("renamed_east_crown") && directChildren.SequenceEqual(new[] { "new_duchy", "castle_example" })
            && (string)Field(titles["new_duchy"], "KingdomRef") == "calradian_empire",
            "A patched regional crown is removed but its authored children are inherited");
        check((string)Field(titles["other_direct_duchy"], "ParentRef") == "bc_title_kingdom_calradian_empire"
            && (string)Field(titles["new_county"], "ParentRef") == "new_duchy",
            "Explicit links to a removed crown are redirected without flattening new counties");
        check(ReferenceEquals(titles["foreign_duchy"], fixtures[4]) && Field(fixtures[1], "KingdomRef") == null
            && (string)Field(fixtures[3], "ParentRef") == "renamed_east_crown",
            "Projection leaves both foreign titles and the shared source hierarchy unchanged");
    }
}
