using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using BellumCivile;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Library;

internal static class TitleMaintenancePerformanceTests
{
    private sealed class Manager : ICampaignBehaviorManager
    {
        internal readonly List<CampaignBehaviorBase> Behaviors = new List<CampaignBehaviorBase>();
        public T GetBehavior<T>() => Behaviors.OfType<T>().FirstOrDefault();
        public IEnumerable<T> GetBehaviors<T>() => Behaviors.OfType<T>();
        public void AddBehavior(CampaignBehaviorBase behavior) => Behaviors.Add(behavior);
        public void RemoveBehavior<T>() where T : CampaignBehaviorBase => Behaviors.RemoveAll(b => b is T);
        public void ClearBehaviors() => Behaviors.Clear();
        public void InitializeCampaignBehaviors(IEnumerable<CampaignBehaviorBase> behaviors) => Behaviors.AddRange(behaviors);
        public void LoadBehaviorData() { }
        public void RegisterEvents() { }
    }

    private static readonly List<Kingdom> Realms = new List<Kingdom>();
    private static readonly List<Clan> Houses = new List<Clan>();
    private static readonly HashSet<string> Neighbors = new HashSet<string>();
    private static float _day;
    private static int _globalReads, _indexedChildLookups, _distanceChecks;
    private static T Blank<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    private static bool AllRealms(ref MBReadOnlyList<Kingdom> __result) { __result = new MBReadOnlyList<Kingdom>(Realms); return false; }
    private static bool AllHouses(ref MBReadOnlyList<Clan> __result) { __result = new MBReadOnlyList<Clan>(Houses); return false; }
    private static bool Day(ref float __result) { __result = _day; return false; }
    private static bool Skip() => false;
    private static void CountRegistry() => _globalReads++;
    private static void CountChildLookup() => _indexedChildLookups++;
    private static bool CheckDistance(FeudalTitleRecord secondTitle, ref bool __result)
    { _distanceChecks++; __result = Neighbors.Contains(secondTitle.TitleId); return false; }

    internal static void Run(Action<bool, string> check)
    {
        var titleType = typeof(FeudalTitleBehavior);
        var driftType = typeof(FeudalDeJureDriftBehavior);
        var harmony = new Harmony("bellum.test.title_maintenance_performance");
        var manager = new Manager();
        var setCampaign = AccessTools.PropertySetter(typeof(Campaign), "Current");
        var previousCampaign = Campaign.Current;
        void Prefix(MethodBase target, string method) => harmony.Patch(target, prefix: new HarmonyMethod(typeof(TitleMaintenancePerformanceTests), method));
        void Set(object obj, string field, object value) => AccessTools.Field(obj.GetType(), field).SetValue(obj, value);
        void Reindex(FeudalTitleBehavior titles, bool advance = true) => AccessTools.Method(titleType, "RebuildRuntimeIndexes").Invoke(titles, new object[] { advance, false });
        Clan House(string id, Kingdom realm)
        {
            var house = Blank<Clan>(); house.StringId = id;
            Set(house, "_kingdom", realm);
            Set(house, "_leader", Blank<Hero>());
            Houses.Add(house);
            return house;
        }
        Kingdom Realm(string id)
        {
            var realm = Blank<Kingdom>(); realm.StringId = id;
            Set(realm, "_rulingClan", House(id + "_crown", realm));
            Realms.Add(realm);
            return realm;
        }
        Dictionary<string, FeudalTitleRecord> Registry(FeudalTitleBehavior titles)
            => (Dictionary<string, FeudalTitleRecord>)AccessTools.Field(titleType, "_titlesById").GetValue(titles);
        FeudalTitleRecord Add(FeudalTitleBehavior titles, string id, FeudalTitleType tier = FeudalTitleType.County,
            Clan holder = null, string origin = "", string parent = "", string name = null, bool active = true, float created = 0f)
        {
            var title = new FeudalTitleRecord(id, name ?? id, tier, holder?.StringId, holder?.StringId, parent, "", origin, created, 0f, active);
            Registry(titles).Add(id, title);
            return title;
        }
        void SetCrown(FeudalTitleBehavior titles, Kingdom realm, FeudalTitleRecord title)
            => ((Dictionary<string, string>)AccessTools.Field(titleType, "_currentRealmTitleByKingdomId").GetValue(titles))[realm.StringId] = title.TitleId;

        try
        {
            var campaign = Blank<Campaign>(); campaign.AddCampaignBehaviorManager(manager);
            setCampaign.Invoke(null, new object[] { campaign });
            Prefix(AccessTools.PropertyGetter(typeof(Kingdom), "All"), nameof(AllRealms));
            Prefix(AccessTools.PropertyGetter(typeof(Clan), "All"), nameof(AllHouses));
            Prefix(AccessTools.PropertyGetter(titleType, "CurrentDay"), nameof(Day));
            Prefix(AccessTools.Method(titleType, "GetAllTitles"), nameof(CountRegistry));
            Prefix(AccessTools.Method(titleType, "EnumerateActiveChildren"), nameof(CountChildLookup));
            Prefix(AccessTools.Method(titleType, "AreTitlesAdjacentForTitleLogic"), nameof(CheckDistance));
            Prefix(AccessTools.Method(titleType.Assembly.GetType("BellumCivile.BellumCivileLogger"), "Log"), nameof(Skip));
            var home = Realm("home"); var destination = Realm("target");
            var homeHouse = House("home_house", home); var targetHouse = House("target_house", destination);
            CheckCleanup(check, manager, destination, homeHouse);
            CheckDrift(check, home, destination, homeHouse, targetHouse);
        }
        finally
        {
            harmony.UnpatchAll(harmony.Id);
            setCampaign.Invoke(null, new object[] { previousCampaign });
            Realms.Clear(); Houses.Clear(); Neighbors.Clear();
        }

        void CheckCleanup(Action<bool, string> assert, Manager behaviors, Kingdom realm, Clan claimant)
        {
            var titles = new FeudalTitleBehavior();
            var drift = new FeudalDeJureDriftBehavior();
            var fabrication = new FeudalClaimFabricationBehavior();
            var feuds = new ClaimFeudBehavior();
            behaviors.Behaviors.AddRange(new CampaignBehaviorBase[] { titles, drift, fabrication, feuds });
            _day = 100;
            for (int i = 0; i < 3200; i++) Add(titles, "barony_" + i, FeudalTitleType.Barony);
            var crown = Add(titles, "crown", FeudalTitleType.Kingdom, realm.RulingClan, realm.StringId);
            SetCrown(titles, realm, crown);
            var configured = Add(titles, "configured");
            Set(titles, "_configuredTitleIdsCacheReady", true);
            ((HashSet<string>)AccessTools.Field(titleType, "_configuredTitleIdsCache").GetValue(titles)).Add(configured.TitleId);
            var legal = Add(titles, "legal_liege");
            var actual = Add(titles, "actual_liege");
            var old = Add(titles, "old_empty");
            var fresh = Add(titles, "newly_created", created: 101f);
            var temporaryEmpty = Add(titles, "temporary_empty");
            var claim = Add(titles, "active_claim");
            var expired = Add(titles, "expired_claim");
            var plot = Add(titles, "active_plot");
            var feud = Add(titles, "active_feud");
            var integration = Add(titles, "active_drift");
            var self = Add(titles, "self_link", parent: "self_link");
            var missingId = Add(titles, string.Empty);
            var child = Add(titles, "attached_barony", FeudalTitleType.Barony, parent: legal.TitleId);
            child.SetDeFactoParentTitle(actual.TitleId);
            Add(titles, "inactive_barony", FeudalTitleType.Barony, parent: old.TitleId, active: false);
            var claims = (List<FeudalClaimRecord>)AccessTools.Field(titleType, "_claims").GetValue(titles);
            claims.Add(new FeudalClaimRecord("valid", claimant.StringId, claim.TitleId, FeudalClaimStrength.Weak, "fixture", "", "", 0, -1));
            claims.Add(new FeudalClaimRecord("expired", claimant.StringId, expired.TitleId, FeudalClaimStrength.Weak, "fixture", "", "", 0, 30));
            ((List<FeudalClaimFabricationRecord>)AccessTools.Field(typeof(FeudalClaimFabricationBehavior), "_fabrications").GetValue(fabrication))
                .Add(new FeudalClaimFabricationRecord("plot", "", claimant.StringId, plot.TitleId, FeudalClaimFabricationTrack.Horizontal, .1f, .01f, 0, 0, 0));
            ((List<ClaimFeudRecord>)AccessTools.Field(typeof(ClaimFeudBehavior), "_feuds").GetValue(feuds))
                .Add(new ClaimFeudRecord("feud", realm.StringId, claimant.StringId, "", feud.TitleId, FeudalClaimStrength.Weak, 1, 0, "", ""));
            ((Dictionary<string, FeudalDeJureDriftRecord>)AccessTools.Field(driftType, "_driftByTitleId").GetValue(drift))[integration.TitleId]
                = new FeudalDeJureDriftRecord(integration.TitleId, "", "", realm.StringId, .1f, 0, 0);
            Reindex(titles);

            var timers = (Dictionary<string, float>)AccessTools.Field(titleType, "_orphanedUpperTitleFirstSeenDay").GetValue(titles);
            var all = Registry(titles).Values;
            int legacyVisits = 0;
            bool LegacyEmpty(FeudalTitleRecord parent) => !all.Any(entry =>
            {
                legacyVisits++;
                return entry.IsActive && (entry.ParentTitleId == parent.TitleId || entry.DeFactoParentTitleId == parent.TitleId);
            });
            var expected = new[] { legal, actual, old, fresh, temporaryEmpty, expired, self }.Where(LegacyEmpty).Select(t => t.TitleId).ToArray();
            int Cleanup() => (int)AccessTools.Method(titleType, "CleanupOrphanedUpperTitles").Invoke(titles, null);
            _indexedChildLookups = 0;
            assert(Cleanup() == 0 && timers.Keys.SequenceEqual(expected), "Cleanup matches the old scan, including legal-only, actual-only, inactive and self-linked children");
            assert(_indexedChildLookups <= 26 && legacyVisits > 20000,
                "Large-registry cleanup uses at most two child-index lookups per upper title instead of rescanning for each");
            Console.WriteLine($"Cleanup fixture: {legacyVisits} legacy child-record visits for the comparison set; {_indexedChildLookups} indexed child lookups for the entire pass.");
            _day = 107;
            child.SetParentTitle(temporaryEmpty.TitleId);
            Reindex(titles, advance: false);
            assert(Cleanup() == 2 && !old.IsActive && !expired.IsActive && fresh.IsActive && temporaryEmpty.IsActive && !timers.ContainsKey(temporaryEmpty.TitleId),
                "Seven-day grace still applies, freshly created titles wait, and a returning child clears the timer");
            assert(new[] { crown, configured, claim, plot, feud, integration, self, missingId, actual }.All(t => t.IsActive),
                "Sovereign, configured, disputed and malformed titles retain their existing protections");
            child.SetParentTitle(legal.TitleId);
            Reindex(titles);
            _day = 108;
            assert(Cleanup() == 1 && !fresh.IsActive && timers[temporaryEmpty.TitleId] == 108f,
                "A title that becomes empty again starts a fresh timer rather than using its previous one");
            _day = 115;
            assert(Cleanup() == 1 && !temporaryEmpty.IsActive && legal.IsActive && actual.IsActive,
                "Automatic cleanup removes the orphan exactly at the new deadline without disturbing either live parent link");
        }

        void CheckDrift(Action<bool, string> assert, Kingdom home, Kingdom realm, Clan abroad, Clan holder)
        {
            var titles = new FeudalTitleBehavior();
            var drift = new FeudalDeJureDriftBehavior();
            for (int i = 0; i < 3200; i++) Add(titles, "barony_" + i, FeudalTitleType.Barony);
            var sovereign = Add(titles, "receiver", FeudalTitleType.Kingdom, realm.RulingClan, home.StringId);
            SetCrown(titles, realm, sovereign);
            var parentWithRecordedOrigin = Add(titles, "recorded_origin", FeudalTitleType.Duchy, holder, realm.StringId);
            var original = Add(titles, "origin_county", holder: abroad, origin: home.StringId);
            var subject = Add(titles, "integrating_barony", FeudalTitleType.Barony, holder, home.StringId, original.TitleId);
            var first = Add(titles, "z_first", holder: holder, parent: parentWithRecordedOrigin.TitleId, name: "Twin County");
            var second = Add(titles, "a_second", holder: holder, origin: realm.StringId, name: "Twin County");
            Add(titles, "far_away", holder: holder, origin: realm.StringId, name: "A distant county");
            Add(titles, "foreign_rights", holder: holder, origin: home.StringId, name: "A foreign county");
            var split = Add(titles, "split_control", holder: holder, origin: realm.StringId, name: "A split county");
            split.SetDeJureHolder(abroad.StringId);
            Add(titles, "descendant", holder: holder, origin: realm.StringId, parent: subject.TitleId, name: "A descendant county");
            Add(titles, "inactive", holder: holder, origin: realm.StringId, name: "A dormant county", active: false);
            Add(titles, "wrong_rank", FeudalTitleType.Duchy, holder, realm.StringId, name: "A duchy");
            Neighbors.UnionWith(new[] { "z_first", "a_second", "foreign_rights", "split_control", "descendant", "inactive", "wrong_rank" });
            AccessTools.Method(driftType, "RefreshFactionIndexes").Invoke(drift, null);
            Reindex(titles);
            var find = AccessTools.Method(driftType, "FindTargetParent");
            var legalRealm = AccessTools.Method(driftType, "ResolveRecordedLegalKingdomId");
            var heldInside = AccessTools.Method(driftType, "IsTitleHeldInsideKingdom");
            FeudalTitleRecord Find() => (FeudalTitleRecord)find.Invoke(drift, new object[] { titles, subject, original, realm });
            int legacyVisits = 0;
            FeudalTitleRecord LegacyParent() => titles.GetAllTitles().Where(candidate =>
            {
                legacyVisits++;
                return candidate.IsActive && candidate.TitleType == original.TitleType && candidate.TitleType > subject.TitleType
                    && candidate != original && !titles.GetTitleAndDescendants(subject).Contains(candidate)
                    && (string)legalRealm.Invoke(drift, new object[] { titles, candidate }) == realm.StringId
                    && (bool)heldInside.Invoke(drift, new object[] { candidate, realm }) && Neighbors.Contains(candidate.TitleId);
            }).OrderBy(candidate => candidate.Name).FirstOrDefault() ?? sovereign;

            _globalReads = _distanceChecks = 0;
            var result = Find();
            object index = AccessTools.Method(titleType, "GetTitleCandidateIndex").Invoke(titles, null);
            int rankVisits = ((IEnumerable<FeudalTitleRecord>)AccessTools.Method(index.GetType(), "Rank")
                .Invoke(index, new object[] { original.TitleType })).Count();
            assert(_globalReads == 0 && rankVisits == 7 && _distanceChecks == 3,
                "Drift searches only active titles of the original parent's rank and calls exact adjacency only after legal checks");
            assert(result == LegacyParent() && result == first, "Equivalent parents keep legacy registry tie order, including inherited legal realm membership");
            Console.WriteLine($"Drift fixture: {legacyVisits} full-registry visits -> {rankVisits} same-rank candidates per search after indexing.");
            first.SetActive(false);
            Reindex(titles, advance: false);
            assert(Find() == LegacyParent() && Find() == second, "A repair rebuild without a public revision invalidates the drift candidate index");
            Neighbors.Remove(second.TitleId);
            assert(Find() == sovereign, "With no adjacent same-rank parent, drift falls back to the recognized lawful crown");
            original.SetDeJureHolder(holder.StringId); original.SetDeFactoHolder(holder.StringId);
            original.SetAssociatedKingdom(realm.StringId);
            _distanceChecks = 0;
            assert(Find() == original && _distanceChecks == 0,
                "A child still finishes in place if its original parent integrated first, without a candidate search");
        }
    }
}
