using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using BellumCivile;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

internal static class RestoredRealmRecoveryTests
{
    private sealed class Manager : ICampaignBehaviorManager
    {
        internal readonly List<CampaignBehaviorBase> Items = new List<CampaignBehaviorBase>();
        public T GetBehavior<T>() => Items.OfType<T>().FirstOrDefault();
        public IEnumerable<T> GetBehaviors<T>() => Items.OfType<T>();
        public void AddBehavior(CampaignBehaviorBase b) => Items.Add(b);
        public void RemoveBehavior<T>() where T : CampaignBehaviorBase => Items.RemoveAll(b => b is T);
        public void ClearBehaviors() => Items.Clear();
        public void InitializeCampaignBehaviors(IEnumerable<CampaignBehaviorBase> b) => Items.AddRange(b);
        public void LoadBehaviorData() { }
        public void RegisterEvents() { }
    }
    private sealed class Store : IDataStore
    {
        internal readonly Dictionary<string, object> Values = new Dictionary<string, object>();
        public bool IsLoading { get; set; }
        public bool IsSaving => !IsLoading;
        public bool SyncData<T>(string key, ref T data)
        {
            if (IsLoading) { if (!Values.TryGetValue(key, out var value)) return false; data = (T)value; }
            else Values[key] = data;
            return true;
        }
    }
    private static Campaign _campaign;
    private static readonly List<Kingdom> Kingdoms = new List<Kingdom>();
    private static readonly Dictionary<Clan, Kingdom> Realms = new Dictionary<Clan, Kingdom>();
    private static readonly Dictionary<Clan, Hero> Heads = new Dictionary<Clan, Hero>();
    private static readonly Dictionary<Kingdom, Clan> Rulers = new Dictionary<Kingdom, Clan>();
    private static readonly Dictionary<Kingdom, FeudalTitleRecord> Crowns = new Dictionary<Kingdom, FeudalTitleRecord>();
    private static readonly HashSet<Kingdom> Gone = new HashSet<Kingdom>(), BlockDestroy = new HashSet<Kingdom>(), Protected = new HashSet<Kingdom>();
    private static readonly Dictionary<Kingdom, int> Holds = new Dictionary<Kingdom, int>();
    private static Clan _blockedHouse;
    private static Kingdom _blockedMercenaries;
    private static int _created, _initialized, _published, _destroyed;
    private static bool _throwPublish, _blockCrown;
    private static Action _duringPublish;
    private static Action _migration;
    private static T Blank<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    private static object Call(object owner, string name, params object[] args) => AccessTools.Method(typeof(CivilWarResolutionBehavior), name).Invoke(owner, args);
    private static bool Skip() => false;
    private static bool No(ref bool __result) { __result = false; return false; }
    private static bool Yes(ref bool __result) { __result = true; return false; }
    private static bool Migrate() { _migration?.Invoke(); return false; }
    private static bool Current(ref Campaign __result) { __result = _campaign; return false; }
    private static bool All(ref MBReadOnlyList<Kingdom> __result) { __result = new MBReadOnlyList<Kingdom>(Kingdoms); return false; }
    private static bool AllClans(ref MBReadOnlyList<Clan> __result) { __result = new MBReadOnlyList<Clan>(Realms.Keys.ToList()); return false; }
    private static bool NoSettlements(ref MBReadOnlyList<Settlement> __result) { __result = new MBReadOnlyList<Settlement>(new List<Settlement>()); return false; }
    private static bool NoPolicies(ref IList<PolicyObject> __result) { __result = new List<PolicyObject>(); return false; }
    private static bool Clans(Kingdom __instance, ref MBReadOnlyList<Clan> __result)
    { __result = new MBReadOnlyList<Clan>(Realms.Where(p => p.Value == __instance).Select(p => p.Key).ToList()); return false; }
    private static bool Realm(Clan __instance, ref Kingdom __result) { Realms.TryGetValue(__instance, out __result); return false; }
    private static bool Head(Clan __instance, ref Hero __result) { Heads.TryGetValue(__instance, out __result); return false; }
    private static bool Ruler(Kingdom __instance, ref Clan __result) { Rulers.TryGetValue(__instance, out __result); return false; }
    private static bool SetRuler(Kingdom __instance, Clan value) { Rulers[__instance] = value; return false; }
    private static bool Leader(Kingdom __instance, ref Hero __result)
    { __result = Rulers.TryGetValue(__instance, out var c) && c != null ? Heads[c] : null; return false; }
    private static bool Eliminated(Kingdom __instance, ref bool __result) { __result = Gone.Contains(__instance); return false; }
    private static bool OtherPending(Kingdom realm, ref bool __result) { __result = Protected.Contains(realm); return false; }
    private static bool Holding(Kingdom kingdom, ref int __result) { Holds.TryGetValue(kingdom, out __result); return false; }
    private static bool Release(Kingdom kingdom, ref bool __result) { __result = kingdom != _blockedMercenaries; return false; }
    private static bool Join(Clan clan, Kingdom targetKingdom)
    { if (clan != _blockedHouse) Realms[clan] = targetKingdom; return false; }
    private static bool Destroy(Kingdom destroyedKingdom)
    { _destroyed++; if (!BlockDestroy.Contains(destroyedKingdom)) Gone.Add(destroyedKingdom); return false; }
    private static bool Name(ref TextObject __result) { __result = new TextObject("Test realm"); return false; }
    private static bool Create(string id, Clan founder, ref Kingdom __result)
    { __result = NewRealm(id); Rulers[__result] = founder; _created++; return false; }
    private static Kingdom NewRealm(string id) { var k = Blank<Kingdom>(); k.StringId = id; Kingdoms.Add(k); return k; }
    private static bool Initialize() { _initialized++; return false; }
    private static bool Mantle(Kingdom restoredRealm, Clan successorClan)
    {
        if (!_blockCrown) Crowns[restoredRealm] = new FeudalTitleRecord("crown", "Crown", FeudalTitleType.Kingdom,
            successorClan.StringId, successorClan.StringId, "", "", restoredRealm.StringId, 0, 0);
        return false;
    }
    private static bool Crown(Kingdom kingdom, ref FeudalTitleRecord __result) { Crowns.TryGetValue(kingdom, out __result); return false; }
    private static bool Dispatcher(ref CampaignEventDispatcher __result) { __result = Blank<CampaignEventDispatcher>(); return false; }
    private static bool Publish()
    {
        _published++;
        _duringPublish?.Invoke();
        if (_throwPublish) throw new InvalidOperationException("Injected created-event interruption");
        return false;
    }

    internal static void Run(Action<bool, string> check)
    {
        var harmony = new Harmony("bellum.tests.restored_realm_recovery");
        var assembly = typeof(FactionObject).Assembly;
        var manager = new Manager();
        var resolver = new CivilWarResolutionBehavior();
        var behaviorType = typeof(CivilWarResolutionBehavior);
        var recordsField = AccessTools.Field(behaviorType, "_restoredCreations");
        var queueField = AccessTools.Field(behaviorType, "_pendingRebelCleanupOrder");
        void Patch(MethodBase target, string prefix) => harmony.Patch(target, prefix: new HarmonyMethod(typeof(RestoredRealmRecoveryTests), prefix));
        List<RestoredRealmCreationRecord> Records() => (List<RestoredRealmCreationRecord>)recordsField.GetValue(resolver);
        List<string> Queue() => (List<string>)queueField.GetValue(resolver);
        Clan House(string id, Kingdom realm)
        {
            var c = Blank<Clan>(); c.StringId = id; Realms[c] = realm;
            var h = Blank<Hero>(); h.StringId = id + "_head"; Heads[c] = h; return c;
        }
        void Reset()
        {
            Kingdoms.Clear(); Realms.Clear(); Heads.Clear(); Rulers.Clear(); Crowns.Clear(); Gone.Clear(); BlockDestroy.Clear(); Protected.Clear(); Holds.Clear();
            _created = _initialized = _published = _destroyed = 0;
            _throwPublish = _blockCrown = false; _duringPublish = null; _blockedHouse = null; _blockedMercenaries = null;
            resolver = new CivilWarResolutionBehavior(); manager.Items.Clear(); manager.Items.Add(resolver);
            manager.Items.Add(Blank<FeudalTitleBehavior>());
        }
        Kingdom Prepare(Kingdom parent, Kingdom source, Clan founder, bool replacement = false) => (Kingdom)Call(resolver,
            "PrepareRestoredRealm", parent, source, founder, replacement, false, null, null, new List<Kingdom>());
        bool Drain(Kingdom realm, Kingdom fallback, bool deferred = true) => (bool)Call(resolver, "DrainAndDestroyRebelKingdom", realm, fallback, deferred, false);
        try
        {
            _campaign = Blank<Campaign>(); _campaign.AddCampaignBehaviorManager(manager);
            Patch(AccessTools.PropertyGetter(typeof(Campaign), "Current"), nameof(Current));
            Patch(AccessTools.PropertyGetter(typeof(Kingdom), "All"), nameof(All));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "All"), nameof(AllClans));
            Patch(AccessTools.PropertyGetter(typeof(Settlement), "All"), nameof(NoSettlements));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "Settlements"), nameof(NoSettlements));
            Patch(AccessTools.PropertyGetter(typeof(Kingdom), "Settlements"), nameof(NoSettlements));
            Patch(AccessTools.PropertyGetter(typeof(Kingdom), "ActivePolicies"), nameof(NoPolicies));
            Patch(AccessTools.PropertyGetter(typeof(Kingdom), "Clans"), nameof(Clans));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "Kingdom"), nameof(Realm));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "Leader"), nameof(Head));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "IsUnderMercenaryService"), nameof(No));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "IsMinorFaction"), nameof(No));
            Patch(AccessTools.PropertyGetter(typeof(Kingdom), "RulingClan"), nameof(Ruler));
            Patch(AccessTools.PropertySetter(typeof(Kingdom), "RulingClan"), nameof(SetRuler));
            Patch(AccessTools.PropertyGetter(typeof(Kingdom), "Leader"), nameof(Leader));
            Patch(AccessTools.PropertyGetter(typeof(Kingdom), "IsEliminated"), nameof(Eliminated));
            Patch(AccessTools.PropertyGetter(typeof(Kingdom), "Name"), nameof(Name));
            Patch(AccessTools.Method(typeof(DestroyKingdomAction), "Apply"), nameof(Destroy));
            Patch(AccessTools.Method(typeof(BellumCivileLogger), "Log"), nameof(Skip));
            Patch(AccessTools.Method(typeof(CivilWarConflictBehavior), "IsOtherRealmTransferPending"), nameof(OtherPending));
            Patch(AccessTools.Method(behaviorType, "CountStrongholds"), nameof(Holding));
            Patch(AccessTools.Method(behaviorType, "ReleaseMercenariesFromKingdom"), nameof(Release));
            Patch(AccessTools.Method(behaviorType, "PreservePromotedRebelForeignWars"), nameof(Skip));
            Patch(AccessTools.Method(behaviorType, "RepairCompatibilityStateNow"), nameof(Skip));
            Patch(AccessTools.Method(behaviorType, "ActivateCompatibilityRepairWindow"), nameof(Skip));
            Patch(AccessTools.Method(assembly.GetType("BellumCivile.KingdomCreationSafetyHelper"), "CreateKingdom"), nameof(Create));
            var visuals = assembly.GetType("BellumCivile.KingdomVisualHelper");
            Patch(AccessTools.Method(visuals, "ResolveInheritedKingdomVisuals"), nameof(Skip));
            Patch(AccessTools.Method(visuals, "ApplyKingdomPalette", new[] { typeof(Kingdom), visuals.GetNestedType("KingdomFoundingVisuals", BindingFlags.NonPublic) }), nameof(Skip));
            Patch(AccessTools.Method(visuals, "ApplyJoinToKingdomPreservingCustomBanner"), nameof(Join));
            Patch(AccessTools.Method(typeof(Kingdom), "InitializeKingdom"), nameof(Initialize));
            Patch(AccessTools.Method(assembly.GetType("BellumCivile.RebelPolicyHelper"), "CopyPolicies"), nameof(Skip));
            Patch(AccessTools.Method(typeof(FeudalTitleBehavior), "RegisterRestoredRealmMantle"), nameof(Mantle));
            Patch(AccessTools.Method(typeof(FeudalTitleBehavior), "GetIndependentRealmSovereignTitle"), nameof(Crown));
            Patch(AccessTools.PropertyGetter(typeof(CampaignEventDispatcher), "Instance"), nameof(Dispatcher));
            Patch(AccessTools.Method(typeof(CampaignEventDispatcher), "OnKingdomCreated"), nameof(Publish));

            Reset(); var parent = NewRealm("parent"); var source = NewRealm("parent_rebels_house"); var founder = House("founder", source); Rulers[source] = founder;
            BlockDestroy.Add(source);
            check(Prepare(parent, source, founder) == null && Records().Count == 1 && _created == 1 && _published == 0
                && Records()[0].MovesComplete && !Records()[0].Ready && Queue().Contains(source.StringId),
                "A blocked source destruction retains its cleanup and saved creation without publishing readiness");
            var successor = Records()[0].Successor;
            check(Prepare(parent, source, founder) == null && _created == 1 && _initialized == 1 && Realms[founder] == successor,
                "A retry reuses the successor identity and never moves its founder again");
            var store = new Store(); resolver.SyncData(store); store.IsLoading = true;
            resolver = new CivilWarResolutionBehavior(); resolver.SyncData(store);
            manager.Items.RemoveAll(b => b is CivilWarResolutionBehavior); manager.Items.Add(resolver);
            BlockDestroy.Clear();
            Protected.Add(parent);
            check(Prepare(parent, source, founder) == null && !Gone.Contains(parent) && _published == 0,
                "Restoration cannot authorize its own retirement over another pending transfer's protection");
            Protected.Clear();
            var resumed = Prepare(parent, source, founder);
            check(resumed == successor && Records()[0].Ready && Gone.Contains(source) && Gone.Contains(parent)
                && _created == 1 && _published == 1 && Crowns[successor].AssociatedKingdomId == successor.StringId,
                "A loaded retry verifies both retirements and crown delivery before publishing the original successor once");
            check(Prepare(parent, source, founder) == successor && _published == 1, "A completed creation does not replay kingdom-created listeners");

            Reset(); parent = NewRealm("parent"); source = NewRealm("parent_rebels_house"); founder = House("founder", source);
            _duringPublish = () => {
                check(Records()[0].CreatedNotified && !Records()[0].Ready && Gone.Contains(parent) && Gone.Contains(source)
                    && Realms[founder] == Records()[0].Successor, "Created listeners see fully moved households and retired predecessors");
                check(Prepare(parent, source, founder) == null && _created == 1, "A recursive created listener cannot allocate or publish another successor");
            };
            check(Prepare(parent, source, founder) != null && _published == 1, "Creation completes after reentrant notification safely returns");

            Reset(); parent = NewRealm("parent"); source = NewRealm("parent_rebels_house"); founder = House("founder", source); var follower = House("follower", source);
            Rulers[source] = founder; _blockedHouse = founder;
            check(Prepare(parent, source, founder) == null && Realms[follower] == source && !Records()[0].MovesComplete && _published == 0,
                "Blocked founder movement cannot strand followers in a household without its ruler");
            _blockedHouse = follower;
            check(Prepare(parent, source, founder) == null && Realms[founder] == Records()[0].Successor && Realms[follower] == source
                && !Gone.Contains(source) && _published == 0,
                "A no-op follower transfer keeps the original realm alive and the creation recoverable");
            _blockedHouse = null;
            check(Prepare(parent, source, founder) != null && _created == 1 && _published == 1 && Realms[founder] == Realms[follower],
                "The transfer retry completes both houses without duplicating the realm");

            Reset(); parent = NewRealm("parent"); source = NewRealm("parent_rebels_house"); founder = House("founder", source); var loyalist = House("loyalist", parent);
            Rulers[source] = founder; Rulers[parent] = loyalist; _blockedHouse = loyalist;
            check(Prepare(parent, source, founder) == null && !Gone.Contains(parent) && Realms[loyalist] == parent && _published == 0,
                "Parent household transfer also blocks readiness instead of being discarded after rebel promotion");
            _blockedHouse = null; BlockDestroy.Add(parent);
            check(Prepare(parent, source, founder) == null && Queue().Contains(parent.StringId) && _published == 0,
                "A blocked old-parent retirement retains an explicit retry after every house has moved");
            BlockDestroy.Clear();
            check(Prepare(parent, source, founder) != null && Gone.Contains(parent) && _created == 1 && _published == 1,
                "Parent retirement can resume without recreating or republishing the successor");

            Reset(); parent = NewRealm("eliminated_parent"); Gone.Add(parent); founder = House("founder", parent); follower = House("follower", parent); _blockedHouse = follower;
            check(Prepare(parent, parent, founder, true) == null && !Records()[0].Ready && _published == 0,
                "Emergency replacement cannot report success while a live household remains in the eliminated parent");
            _blockedHouse = null;
            Call(resolver, "ResumePendingRestoredCreations");
            check(Records()[0].Ready && _created == 1 && _published == 1,
                "Hourly emergency replacement recovery resumes its saved realm after the missing move succeeds");

            Reset(); parent = NewRealm("parent"); source = NewRealm("parent_rebels_house"); founder = House("founder", source); _blockCrown = true;
            check(Prepare(parent, source, founder) == null && _published == 0 && Records()[0].Failure == "successor crown transfer pending",
                "Missing crown delivery cannot publish a seemingly ready restored realm");
            _blockCrown = false; _throwPublish = true;
            check(Prepare(parent, source, founder) == null && _published == 1 && Records()[0].CreatedNotified && !Records()[0].Ready,
                "An interrupted created-event listener leaves a saved receipt before callbacks");
            _throwPublish = false;
            check(Prepare(parent, source, founder) != null && _published == 1 && _created == 1,
                "Retry after a listener exception neither duplicates earlier notifications nor creates another realm");

            Reset(); parent = NewRealm("parent"); source = NewRealm("parent_rebels_house"); founder = House("founder", source);
            _duringPublish = () => Gone.Add(Records()[0].Successor);
            check(Prepare(parent, source, founder) == null && !Records()[0].Ready && _published == 1,
                "A listener retiring the new realm cannot leave a false readiness receipt");
            _duringPublish = null;
            check(Prepare(parent, source, founder) == null && _created == 1,
                "An unavailable recorded successor is diagnosed instead of replaced with another generated realm");

            Reset(); parent = NewRealm("parent"); source = NewRealm("parent_rebels_house"); founder = House("founder", source);
            _blockedMercenaries = source;
            check(Prepare(parent, source, founder) == null && _created == 0 && Records().Count == 0,
                "Blocked mercenary dismissal defers before allocating a restored realm");
            _blockedMercenaries = null;
            check(Prepare(parent, source, founder) != null && _created == 1, "Restoration proceeds when contract release becomes possible");

            Reset(); parent = NewRealm("parent"); source = NewRealm("parent_rebels_house"); founder = House("founder", source);
            successor = NewRealm("parent_restored_challenge_old"); Rulers[successor] = founder;
            var oldChallenge = new SuccessionChallengeRecord { Id = "old", OutcomeRealm = parent, RestoredRealmRequested = true };
            check(Call(resolver, "PrepareRestoredRealm", parent, source, founder, false, false, oldChallenge, null, new List<Kingdom>()) == successor
                && _created == 0 && _initialized == 1 && oldChallenge.RestoredRealmReady,
                "Legacy interrupted challenges adopt their exact planned successor instead of making a suffixed duplicate");

            Reset(); parent = NewRealm("parent"); source = NewRealm("parent_rebels_house"); successor = NewRealm("successor");
            founder = House("ruler", successor); Rulers[successor] = founder;
            var collapse = new CivilWarCollapseRecord { Parent = parent, WinnerRealm = source, Successor = successor,
                SuccessorId = successor.StringId, WinnerHouse = founder, Stage = 4, RewardsApplied = true, Announced = true };
            _throwPublish = true;
            Call(resolver, "ResumeCollapse", collapse);
            check(collapse.CreatedNotified && !collapse.Completed && _published == 1,
                "Staged collapse also records a created-event receipt before a listener can interrupt it");
            _throwPublish = false; Call(resolver, "ResumeCollapse", collapse);
            check(collapse.Completed && _published == 1, "Staged collapse resumes final bookkeeping without replaying created listeners");

            Reset(); parent = NewRealm("parent"); var target = NewRealm("target"); BlockDestroy.Add(parent);
            check(!Drain(parent, target) && Queue().Contains(parent.StringId), "A silent destruction refusal is never returned as successful cleanup");
            Call(resolver, "ProcessPendingRebelCleanups");
            check(Queue().Contains(parent.StringId), "The cleanup processor cannot drop a retry after a no-op destruction action");
            BlockDestroy.Clear(); Call(resolver, "ProcessPendingRebelCleanups");
            check(Gone.Contains(parent) && !Queue().Contains(parent.StringId), "Only verified elimination removes the cleanup record");
            var held = NewRealm("cached_holdings"); Holds[held] = 1; int attempts = _destroyed;
            check(!Drain(held, target) && _destroyed == attempts, "A supposedly empty realm with remaining holdings is not destroyed");
            var pendingRealm = NewRealm("protected"); Protected.Add(pendingRealm);
            check(!Drain(pendingRealm, target) && Queue().Contains(pendingRealm.StringId), "Other pending crown transfers retain their protection and a future cleanup attempt");

            Reset(); var orphan = NewRealm("vlandia_restored"); var nextRealm = NewRealm("vlandia_restored_restored");
            var monarch = House("successor_monarch", nextRealm); Rulers[nextRealm] = monarch;
            check((bool)Call(resolver, "IsOrphanedRestoredRealm", orphan), "An empty, unowned, rulerless restored predecessor is eligible for conservative recovery");
            Call(resolver, "RetireOrphanedRestoredRealms");
            check(Gone.Contains(orphan) && !Gone.Contains(nextRealm) && Realms[monarch] == nextRealm,
                "Orphan recovery retires only the empty predecessor and leaves the live restored kingdom alone");
            var landed = NewRealm("landed_restored"); Holds[landed] = 1;
            check(!(bool)Call(resolver, "IsOrphanedRestoredRealm", landed), "A restored realm with a stronghold is never an orphan-cleanup target");
            var occupied = NewRealm("occupied_restored"); House("minor_or_noble_house", occupied);
            check(!(bool)Call(resolver, "IsOrphanedRestoredRealm", occupied), "Even a leaderless member house prevents automatic retirement");
            var live = NewRealm("healthy_restored"); var liveRuler = House("live_ruler", live); Rulers[live] = liveRuler;
            check(!(bool)Call(resolver, "IsOrphanedRestoredRealm", live), "Healthy landless restored rulers retain their realm");
            var shared = NewRealm("shared_restored"); Rulers[shared] = monarch;
            check(!(bool)Call(resolver, "IsOrphanedRestoredRealm", shared), "A realm sharing a living foreign ruler is not classified as a rulerless orphan");
            var reserved = NewRealm("reserved_restored"); Records().Add(new RestoredRealmCreationRecord { Parent = reserved });
            check(!(bool)Call(resolver, "IsOrphanedRestoredRealm", reserved), "A saved unfinished restoration is not treated as an unowned historical orphan");
            var emptyStore = new Store { IsLoading = true }; new CivilWarResolutionBehavior().SyncData(emptyStore);
            check(true, "Older saves without restoration records load safely");
            var ids = typeof(RestoredRealmCreationRecord).GetFields().Select(f => Convert.ToInt32(f.GetCustomAttributesData()
                .Single(a => a.AttributeType.Name == "SaveableFieldAttribute").ConstructorArguments[0].Value)).ToArray();
            check(ids.Length == 17 && ids.Distinct().Count() == 17 && ids.Max() == 17,
                "Saved restoration identity, household snapshots and publication receipts use unique append-only field IDs");

            var migrationPatch = assembly.GetType("BellumCivile.Patches.ProtectedRealmLoadMigrationPatch", true);
            Patch(AccessTools.Method(migrationPatch, "IsMigrationActive"), nameof(Yes));
            var loadFinished = AccessTools.Method(typeof(TaleWorlds.CampaignSystem.CampaignBehaviors.ClanVariablesCampaignBehavior), "OnGameLoadFinished");
            harmony.Patch(loadFinished, prefix: new HarmonyMethod(typeof(RestoredRealmRecoveryTests), nameof(Migrate)),
                finalizer: new HarmonyMethod(migrationPatch, "Finalizer"));
            harmony.Patch(loadFinished, prefix: new HarmonyMethod(migrationPatch, "Prefix") { priority = Priority.First });
            void Load() => loadFinished.Invoke(Blank<TaleWorlds.CampaignSystem.CampaignBehaviors.ClanVariablesCampaignBehavior>(), null);
            Reset(); parent = NewRealm("migration_parent"); founder = House("original_ruler", parent); Rulers[parent] = founder;
            Protected.Add(parent); _migration = () => Rulers[parent] = null;
            Load(); check(Rulers[parent] == founder, "A deferred native duplicate-ruler migration restores the protected realm's living prior house");
            Protected.Clear(); Load(); check(Rulers[parent] == null, "Migration recovery does not revive an unprotected obsolete realm");
            Rulers[parent] = founder; Protected.Add(parent); _migration = () => { Rulers[parent] = null; Gone.Add(parent); };
            Load(); check(Rulers[parent] == null, "A completed native destruction is never rolled back by load recovery");
            Gone.Clear(); Rulers[parent] = founder; var newRuler = House("new_ruler", parent); _migration = () => Rulers[parent] = newRuler;
            Load(); check(Rulers[parent] == newRuler, "Migration recovery cannot overwrite an installed successor");
            Rulers[parent] = founder; _migration = () => { Rulers[parent] = null; throw new InvalidOperationException("Injected native migration exception"); };
            bool threw = false;
            try { Load(); } catch (TargetInvocationException ex) { threw = ex.InnerException is InvalidOperationException; }
            check(threw && Rulers[parent] == founder, "Migration finalization preserves the native exception while restoring deferred protected leadership");
            Rulers[parent] = founder; _migration = () => { Rulers[parent] = null; Heads[founder] = null; };
            Load(); check(Rulers[parent] == null, "A prior house losing its living head during migration is not restored");
            Heads[founder] = Blank<Hero>(); Rulers[parent] = founder; _migration = () => Rulers[parent] = null;
            harmony.Unpatch(AccessTools.Method(migrationPatch, "IsMigrationActive"), HarmonyPatchType.Prefix, harmony.Id);
            Patch(AccessTools.Method(migrationPatch, "IsMigrationActive"), nameof(No));
            Load(); check(Rulers[parent] == null, "An ordinary load outside the version migration receives no ruler rollback");
        }
        finally { harmony.UnpatchAll(harmony.Id); _campaign = null; _migration = null; }
    }
}
