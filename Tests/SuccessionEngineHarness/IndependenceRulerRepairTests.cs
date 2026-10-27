using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using BellumCivile;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Election;
using TaleWorlds.Library;

internal static class IndependenceRulerRepairTests
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
            if (IsLoading)
            {
                if (!Values.TryGetValue(key, out var value)) return false;
                data = (T)Clone(value);
            }
            else Values[key] = Clone(data);
            return true;
        }
        private static object Clone(object value)
        {
            if (value is List<string> list) return list.ToList();
            if (value is Dictionary<string, string> map) return new Dictionary<string, string>(map);
            if (value is Dictionary<string, FeudalTitleRecord> titles) return new Dictionary<string, FeudalTitleRecord>(titles);
            return value;
        }
    }
    private static readonly List<Kingdom> Kingdoms = new List<Kingdom>();
    private static readonly Dictionary<Clan, Kingdom> Realms = new Dictionary<Clan, Kingdom>();
    private static readonly Dictionary<Clan, Hero> Heads = new Dictionary<Clan, Hero>();
    private static readonly Dictionary<Kingdom, Clan> Rulers = new Dictionary<Kingdom, Clan>();
    private static readonly Dictionary<Kingdom, List<KingdomDecision>> Decisions = new Dictionary<Kingdom, List<KingdomDecision>>();
    private static readonly HashSet<Kingdom> Gone = new HashSet<Kingdom>(), Transferring = new HashSet<Kingdom>();
    private static readonly HashSet<Clan> Minor = new HashSet<Clan>(), Mercenary = new HashSet<Clan>(), Eliminated = new HashSet<Clan>();
    private static readonly HashSet<Hero> Dead = new HashSet<Hero>();
    private static readonly List<string> Logs = new List<string>();
    private static Campaign _campaign;
    private static Manager _manager;
    private static FeudalTitleBehavior _titles;
    private static FactionManagerBehavior _factions;
    private static CrownAccessionBehavior _accession;
    private static Kingdom _parent, _realm;
    private static Clan _founder, _foreign, _crown, _player;
    private static FeudalTitleRecord _source, _foreignTitle;
    private static int _assignments, _effects, _invalidations, _fault;
    private static bool _reenter, _omitFounder;
    private static T Blank<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    private static object Call(object o, string name, params object[] args) => AccessTools.Method(o.GetType(), name).Invoke(o, args);
    private static void Set(object o, string field, object value) => AccessTools.Field(o.GetType(), field).SetValue(o, value);
    private static T Field<T>(object o, string field) => (T)AccessTools.Field(o.GetType(), field).GetValue(o);
    private static Dictionary<string, FeudalTitleRecord> Titles => Field<Dictionary<string, FeudalTitleRecord>>(_titles, "_titlesById");
    private static Dictionary<string, string> Sources => Field<Dictionary<string, string>>(_titles, "_independentRealmSourceTitleByKingdomId");
    private static Dictionary<string, string> Crowns => Field<Dictionary<string, string>>(_titles, "_currentRealmTitleByKingdomId");
    private static List<string> Pending => Field<List<string>>(_titles, "_pendingIndependenceRulerRepairs");
    private static int Version => Field<int>(_titles, "_independenceRulerRepairVersion");
    private static List<CrownAccessionRecord> Accessions => Field<List<CrownAccessionRecord>>(_accession, "_accessions");
    private static bool Skip() => false;
    private static bool Current(ref Campaign __result) { __result = _campaign; return false; }
    private static bool Player(ref Clan __result) { __result = _player; return false; }
    private static bool Realm(Clan __instance, ref Kingdom __result) { Realms.TryGetValue(__instance, out __result); return false; }
    private static bool Head(Clan __instance, ref Hero __result) { Heads.TryGetValue(__instance, out __result); return false; }
    private static bool Ruler(Kingdom __instance, ref Clan __result) { Rulers.TryGetValue(__instance, out __result); return false; }
    private static bool SetRuler(Kingdom __instance, Clan value)
    {
        if (_fault == 1) throw new InvalidOperationException("before native assignment");
        Rulers[__instance] = value; _assignments++;
        if (_reenter) Call(_titles, "ProcessIndependenceRulerRepair");
        if (_fault == 2) throw new InvalidOperationException("after native assignment");
        return false;
    }
    private static bool All(ref MBReadOnlyList<Kingdom> __result) { __result = new MBReadOnlyList<Kingdom>(Kingdoms); return false; }
    private static bool AllClans(ref MBReadOnlyList<Clan> __result) { __result = new MBReadOnlyList<Clan>(Realms.Keys.ToList()); return false; }
    private static bool Clans(Kingdom __instance, ref MBReadOnlyList<Clan> __result)
    {
        __result = new MBReadOnlyList<Clan>(Realms.Where(p => p.Value == __instance && (!_omitFounder || p.Key != _founder)).Select(p => p.Key).ToList());
        return false;
    }
    private static bool Votes(Kingdom __instance, ref MBReadOnlyList<KingdomDecision> __result)
    { __result = new MBReadOnlyList<KingdomDecision>(Decisions[__instance]); return false; }
    private static bool RealmGone(Kingdom __instance, ref bool __result) { __result = Gone.Contains(__instance); return false; }
    private static bool ClanGone(Clan __instance, ref bool __result) { __result = Eliminated.Contains(__instance); return false; }
    private static bool IsMinor(Clan __instance, ref bool __result) { __result = Minor.Contains(__instance); return false; }
    private static bool IsMerc(Clan __instance, ref bool __result) { __result = Mercenary.Contains(__instance); return false; }
    private static bool IsDead(Hero __instance, ref bool __result) { __result = Dead.Contains(__instance); return false; }
    private static bool Transfer(Kingdom realm, ref bool __result) { __result = Transferring.Contains(realm); return false; }
    private static bool Log(string message) { if (message.Contains("independence ruler migration")) Logs.Add(message); return false; }
    private static void Invalidate() { _invalidations++; }
    private static bool Trap() { _effects++; return false; }
    private static Kingdom NewRealm(string id)
    {
        var realm = Blank<Kingdom>(); realm.StringId = id; Kingdoms.Add(realm);
        Decisions[realm] = new List<KingdomDecision>(); return realm;
    }
    private static Clan House(string id, Kingdom realm)
    {
        var clan = Blank<Clan>(); clan.StringId = id; Realms[clan] = realm;
        var hero = Blank<Hero>(); hero.StringId = id + "_head"; Heads[clan] = hero; return clan;
    }
    private static FeudalTitleRecord Title(string id, Clan owner, Kingdom realm) => new FeudalTitleRecord(id, id,
        FeudalTitleType.County, owner.StringId, owner.StringId, "legal_parent", "castle", realm.StringId, 1, 2);
    private static void Reset(string suffix = "")
    {
        Kingdoms.Clear(); Realms.Clear(); Heads.Clear(); Rulers.Clear(); Decisions.Clear(); Gone.Clear();
        Transferring.Clear(); Minor.Clear(); Mercenary.Clear(); Eliminated.Clear(); Dead.Clear(); Logs.Clear();
        _assignments = _effects = _invalidations = _fault = 0; _reenter = _omitFounder = false; _player = null;
        _manager.Items.Clear(); _titles = new FeudalTitleBehavior(); _factions = new FactionManagerBehavior();
        _accession = new CrownAccessionBehavior();
        _manager.Items.AddRange(new CampaignBehaviorBase[] { _titles, _factions, _accession, new CivilWarResolutionBehavior() });
        Set(_titles, "_hierarchySchemaVersion", 2);
        _parent = NewRealm("empire_w"); _realm = NewRealm("empire_w_indep_founder" + suffix);
        _crown = House("old_crown", _parent); _foreign = House("foreign_ruler", _parent); _founder = House("founder", _realm);
        Rulers[_parent] = _crown; Rulers[_realm] = _foreign;
        _source = Title("county_gavys", _founder, _realm); _foreignTitle = Title("duchy_vostoka", _foreign, _parent);
        Titles.Add(_source.TitleId, _source); Titles.Add(_foreignTitle.TitleId, _foreignTitle);
        Sources.Add(_realm.StringId, _source.TitleId); Crowns.Add(_realm.StringId, _foreignTitle.TitleId);
        Crowns.Add(_parent.StringId, "unrelated_parent_crown");
    }
    private static Store Save()
    { var store = new Store(); _titles.SyncData(store); return store; }
    private static void Load(Store store)
    {
        _manager.Items.Remove(_titles); _titles = new FeudalTitleBehavior(); Set(_titles, "_hierarchySchemaVersion", 2);
        _manager.Items.Add(_titles); store.IsLoading = true; _titles.SyncData(store); store.IsLoading = false;
    }
    private static void LoadLegacy()
    {
        var store = Save(); store.Values.Remove("BellumCivile_IndependenceRulerRepairVersion");
        store.Values.Remove("BellumCivile_PendingIndependenceRulerRepairs"); Load(store);
    }
    private static void Start()
    { Set(_titles, "_sessionLaunched", true); Call(_titles, "StartIndependenceRulerRepair", new object[] { null }); }
    private static void Tick() => Call(_titles, "ProcessIndependenceRulerRepair");
    private static string Snapshot(FeudalTitleRecord t) => string.Join("|", t.TitleId, t.Name, t.TitleType,
        t.DeJureHolderClanId, t.DeFactoHolderClanId, t.ParentTitleId, t.DeFactoParentTitleId, t.CapitalSettlementId,
        t.AssociatedKingdomId, t.CreatedDay, t.LastSyncedDay, t.IsActive, t.FallbackCultureRef, t.IsDeliberatelyDissolved);

    internal static void Run(Action<bool, string> check)
    {
        var harmony = new Harmony("bellum.tests.independence_ruler_repair");
        _manager = new Manager(); _campaign = Blank<Campaign>(); _campaign.AddCampaignBehaviorManager(_manager);
        void Patch(MethodBase method, string prefix) => harmony.Patch(method,
            prefix: new HarmonyMethod(typeof(IndependenceRulerRepairTests), prefix));
        void Getter(Type type, string property, string prefix) => Patch(AccessTools.PropertyGetter(type, property), prefix);
        try
        {
            Getter(typeof(Campaign), "Current", nameof(Current)); Getter(typeof(Clan), "PlayerClan", nameof(Player));
            Getter(typeof(Clan), "Kingdom", nameof(Realm)); Getter(typeof(Clan), "Leader", nameof(Head));
            Getter(typeof(Clan), "IsEliminated", nameof(ClanGone)); Getter(typeof(Clan), "IsMinorFaction", nameof(IsMinor));
            Getter(typeof(Clan), "IsUnderMercenaryService", nameof(IsMerc)); Getter(typeof(Hero), "IsDead", nameof(IsDead));
            Getter(typeof(Clan), "All", nameof(AllClans)); Getter(typeof(Kingdom), "All", nameof(All));
            Getter(typeof(Kingdom), "RulingClan", nameof(Ruler)); Getter(typeof(Kingdom), "Clans", nameof(Clans));
            Getter(typeof(Kingdom), "UnresolvedDecisions", nameof(Votes)); Getter(typeof(Kingdom), "IsEliminated", nameof(RealmGone));
            Patch(AccessTools.PropertySetter(typeof(Kingdom), "RulingClan"), nameof(SetRuler));
            Patch(AccessTools.Method(typeof(CivilWarConflictBehavior), "IsRealmTransferPending"), nameof(Transfer));
            Patch(AccessTools.Method(typeof(BellumCivileLogger), "Log"), nameof(Log));
            Patch(AccessTools.Method(typeof(FeudalTitleBehavior), "DeactivateTemporaryFeudKingdomTitles"), nameof(Skip));
            var names = typeof(FeudalTitleBehavior).Assembly.GetType("BellumCivile.Patches.FeudalTitleHeroNamePatch", true);
            harmony.Patch(AccessTools.Method(names, "InvalidateCache"), postfix: new HarmonyMethod(typeof(IndependenceRulerRepairTests), nameof(Invalidate)));
            foreach (var type in new[] { typeof(ChangeKingdomAction), typeof(ChangeOwnerOfSettlementAction), typeof(ChangeRulingClanAction) })
                foreach (var method in type.GetMethods().Where(m => m.Name.StartsWith("Apply"))) Patch(method, nameof(Trap));
            Cases(check);
        }
        finally { harmony.UnpatchAll(harmony.Id); _manager.Items.Clear(); _campaign = null; }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Cases(Action<bool, string> check)
    {
        Reset(); LoadLegacy();
        check(_realm.RulingClan == _foreign && Version == 0, "Loading a legacy save alone does not mutate a ruler");
        Tick(); Call(_titles, "StartIndependenceRulerRepair", new object[] { null });
        check(_assignments == 0 && Version == 0, "Migration waits for the initialized title registry and post-session lifecycle");
        string source = Snapshot(_source), foreign = Snapshot(_foreignTitle);
        int revision = _titles.DisplayRevision;
        Start();
        check(_realm.RulingClan == _founder && Version == 1 && Pending.Count == 0, "A proven resident founder replaces the foreign ruler and completes the migration");
        check(_foreign.Kingdom == _parent && _founder.Kingdom == _realm && _parent.RulingClan == _crown && _effects == 0,
            "Repair moves no clans, changes no parent ruler and executes no ownership or political actions");
        check(Snapshot(_source) == source && Snapshot(_foreignTitle) == foreign,
            "Every founding and foreign title field remains unchanged, including legal ownership and parent links");
        check(Crowns[_realm.StringId] == _source.TitleId && Crowns[_parent.StringId] == "unrelated_parent_crown",
            "Only the affected realm's cached crown binding is corrected, not the foreign ruler's legitimate title");
        check(_titles.DisplayRevision == revision + 1 && _invalidations == 1 && Field<bool>(_titles, "_titleDisplayRefreshPending"),
            "Repair invalidates title displays through the existing refresh mechanism");
        check(Logs.Any(l => l.Contains("stage=repaired") && l.Contains("previous=foreign_ruler") && l.Contains("source_title=county_gavys")),
            "The repair logs its verified source, old ruler and new ruler without a debug-message gate");
        Tick(); Start();
        check(_assignments == 1 && Logs.Count(l => l.Contains("stage=started")) == 1, "Completed migration is not a recurring hourly or session repair");
        var saved = Save(); Load(saved); Rulers[_realm] = _foreign; Start();
        check(Version == 1 && _realm.RulingClan == _foreign && _assignments == 1,
            "The saved completion marker suppresses a second migration, even if later unrelated state becomes invalid");

        Reset(); LoadLegacy(); var pending = new CrownAccessionRecord { Realm = _realm }; Accessions.Add(pending); Start();
        int deferredLogs = Logs.Count(l => l.Contains("stage=deferred")); Tick(); Tick();
        check(Version == 0 && Pending.SequenceEqual(new[] { _realm.StringId }) && _assignments == 0 && deferredLogs == 1
            && Logs.Count(l => l.Contains("stage=deferred")) == 1, "Pending accession is deferred without completion or repeated log spam");
        saved = Save(); Load(saved);
        var later = NewRealm("empire_w_indep_later"); var laterHouse = House("later", later);
        Rulers[later] = _foreign; var laterTitle = Title("later_title", laterHouse, later);
        Titles.Add(laterTitle.TitleId, laterTitle); Sources[later.StringId] = laterTitle.TitleId;
        Start(); check(Version == 0 && Pending.Count == 1 && _assignments == 0, "Deferred queue survives save/reload and resumes only after session initialization");
        pending.Completed = true; Tick();
        check(Version == 1 && _realm.RulingClan == _founder && later.RulingClan == _foreign && _assignments == 1,
            "Deferred completion repairs the captured realm without rescanning newly added realms");

        foreach (bool parent in new[] { false, true })
        {
            Reset(); LoadLegacy(); var guarded = parent ? _parent : _realm;
            _factions.RunWithRulerRepairSuppressed(guarded, () => Start());
            check(_assignments == 0 && Version == 0, "Scoped ruler/partition/union suppression defers migration: parent=" + parent);
            Tick(); check(_assignments == 1 && Version == 1, "Suppressed realm is repaired only after its guard ends: parent=" + parent);
            Reset(); LoadLegacy(); guarded = parent ? _parent : _realm;
            Decisions[guarded].Add(Blank<KingSelectionKingdomDecision>()); Start();
            check(_assignments == 0 && Version == 0, "Native king-selection election is preserved: parent=" + parent);
            Decisions[guarded].Clear(); Tick(); check(_assignments == 1 && Version == 1, "Election deferral can finish safely: parent=" + parent);
            Reset(); LoadLegacy(); guarded = parent ? _parent : _realm;
            Transferring.Add(guarded); Start(); check(_assignments == 0 && Version == 0, "Civil-war transfer defers repair: parent=" + parent);
            Transferring.Clear(); Tick(); check(_assignments == 1 && Version == 1, "Transfer deferral can finish safely: parent=" + parent);
        }

        void Reject(string name, Action change)
        {
            Reset(); LoadLegacy(); change(); string legal = Snapshot(_source), outside = Snapshot(_foreignTitle);
            Start(); Tick();
            check(_realm.RulingClan == _foreign && _assignments == 0 && Version == 1 && _effects == 0
                && Snapshot(_source) == legal && Snapshot(_foreignTitle) == outside, "Ambiguous or ineligible case is skipped without changing data: " + name);
            check(Logs.Any(l => l.Contains("stage=skipped")), "Skipped case is explained in the migration log: " + name);
        }
        Reject("split legal/actual ownership", () => _source.SetDeJureHolder(_foreign.StringId));
        Reject("inactive founding title", () => _source.SetActive(false));
        Reject("founding title moved elsewhere", () => _source.SetAssociatedKingdom(_parent.StringId));
        Reject("missing founding title", () => Titles.Remove(_source.TitleId));
        Reject("no unique holder", () => { var duplicate = House(_founder.StringId, _realm); });
        Reject("founder absent from native clan list", () => _omitFounder = true);
        Reject("dead founder", () => Dead.Add(Heads[_founder]));
        Reject("eliminated founder clan", () => Eliminated.Add(_founder));
        Reject("minor founder clan", () => Minor.Add(_founder));
        Reject("mercenary founder clan", () => Mercenary.Add(_founder));
        Reject("leaderless founder", () => Heads[_founder] = null);
        Reject("founder no longer resident", () => Realms[_founder] = _parent);
        Reject("protected player ruler", () => _player = _foreign);
        Reject("original parent eliminated", () => Gone.Add(_parent));
        Reject("foreign ruler moved to a third realm", () => Realms[_foreign] = NewRealm("third_realm"));
        Reject("another clan now owns the source", () => { var other = House("other", _realm); _source.SetDeJureHolder(other.StringId); _source.SetDeFactoHolder(other.StringId); });
        Reject("founder rules another live realm", () => Rulers[NewRealm("another")] = _founder);
        Reject("founding title shared with another live realm", () => Sources[NewRealm("shared").StringId] = _source.TitleId);

        Reset("_2"); LoadLegacy(); Start(); check(_assignments == 1 && Version == 1, "Exact numeric independence collision suffix is recognized");
        Reset("_restored"); LoadLegacy(); Start(); check(_assignments == 0 && Version == 1, "A generated-looking but different realm identity is not inferred from its prefix");
        Reset(); Rulers[_realm] = _founder; LoadLegacy(); Start(); check(_assignments == 0 && Version == 1, "An already-correct ruler is never replaced");
        Reset(); _player = _founder; LoadLegacy(); Start(); check(_assignments == 1 && _realm.RulingClan == _founder, "A verified original player founder may recover their own malformed realm");
        Reset(); Call(_titles, "SkipNewGameIndependenceRulerRepair", new object[] { null });
        saved = Save(); Load(saved); Start(); check(Version == 1 && _assignments == 0, "New-game exemption is persisted and does not become a repair on the next load");
        Reset(); Set(_titles, "_independenceRulerRepairVersion", 2); saved = Save(); Load(saved); Start();
        check(Version == 2 && _assignments == 0, "A later migration version is never downgraded");

        foreach (int fault in new[] { 1, 2 })
        {
            Reset(); LoadLegacy(); _fault = fault; Start();
            check(Version == 0 && Pending.Count == 1 && Crowns[_realm.StringId] == _source.TitleId,
                "Interrupted native assignment retains its retry queue and verified crown binding: fault=" + fault);
            _fault = 0; saved = Save(); Load(saved); Start();
            check(Version == 1 && _realm.RulingClan == _founder && _assignments == 1 && _effects == 0,
                "Save/reload after an interrupted assignment finishes without repeated political actions: fault=" + fault);
        }
        Reset(); LoadLegacy(); _reenter = true; Start();
        check(Version == 1 && _assignments == 1, "Reentrant native setter callbacks cannot start a second repair");
    }
}
