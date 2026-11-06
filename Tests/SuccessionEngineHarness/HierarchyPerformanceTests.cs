using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using BellumCivile;
using BellumCivile.Behaviors;
using BellumCivile.UI.VanillaTabs.Kingdoms.Hierarchy;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

internal static class HierarchyPerformanceTests
{
    private sealed class Manager : ICampaignBehaviorManager
    {
        internal FeudalTitleBehavior Titles;
        public T GetBehavior<T>() => new CampaignBehaviorBase[] { Titles }.OfType<T>().FirstOrDefault();
        public IEnumerable<T> GetBehaviors<T>() => new CampaignBehaviorBase[] { Titles }.OfType<T>();
        public void AddBehavior(CampaignBehaviorBase b) { }
        public void RemoveBehavior<T>() where T : CampaignBehaviorBase { }
        public void ClearBehaviors() { }
        public void InitializeCampaignBehaviors(IEnumerable<CampaignBehaviorBase> b) { }
        public void LoadBehaviorData() { }
        public void RegisterEvents() { }
    }

    private static Campaign _campaign;
    private static Clan _player;
    private static Hero _leader;
    private static Kingdom _realm;
    private static MBReadOnlyList<Kingdom> _kingdoms;
    private static readonly Dictionary<Kingdom, FeudalTitleRecord> Crowns = new Dictionary<Kingdom, FeudalTitleRecord>();
    private static readonly HashSet<Kingdom> DeadRealms = new HashSet<Kingdom>();
    private static int _formations, _grants, _adjacency, _clanReads, _realmReads, _descendantReads, _successorScans, _portraits;
    private static int _gold = 10000000;
    private static bool _canGrant = true;
    private static T Blank<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    private static T Field<T>(object instance, string field) => (T)AccessTools.Field(instance.GetType(), field).GetValue(instance);
    private static object Call(object instance, string method, params object[] args) => AccessTools.Method(instance.GetType(), method).Invoke(instance, args);
    private static bool Current(ref Campaign __result) { __result = _campaign; return false; }
    private static bool Player(ref Clan __result) { __result = _player; return false; }
    private static bool Leader(ref Hero __result) { __result = _leader; return false; }
    private static bool Realm(ref Kingdom __result) { __result = _realm; return false; }
    private static bool Clans(ref MBReadOnlyList<Clan> __result)
    { _clanReads++; __result = new MBReadOnlyList<Clan>(new[] { _player }); return false; }
    private static bool Kingdoms(ref MBReadOnlyList<Kingdom> __result) { _realmReads++; __result = _kingdoms; return false; }
    private static bool Eliminated(Kingdom __instance, ref bool __result) { __result = DeadRealms.Contains(__instance); return false; }
    private static bool No(ref bool __result) { __result = false; return false; }
    private static bool Gold(ref int __result) { __result = _gold; return false; }
    private static bool NoBanner(ref Banner __result) { __result = null; return false; }
    private static bool NoCharacter(ref CharacterObject __result) { __result = null; return false; }
    private static bool TextName(ref TextObject __result) { __result = new TextObject("Audit"); return false; }
    private static bool Crown(Kingdom __0, ref FeudalTitleRecord __result) { Crowns.TryGetValue(__0, out __result); return false; }
    private static bool Format(FeudalTitleRecord __0, ref string __result) { __result = __0?.Name ?? ""; return false; }
    private static bool CandidateName(string __1, ref string __result) { __result = __1; return false; }
    private static bool Summary(TextObject __0, ref TextObject __result) { __result = __0; return false; }
    private static bool Zero(ref float __result) { __result = 0; return false; }
    private static bool Minimum(ref int __result) { __result = 2; return false; }
    private static bool Adjacent(FeudalTitleRecord __0, FeudalTitleRecord __1, ref bool __result)
    {
        _adjacency++;
        __result = Math.Abs(int.Parse(__0.TitleId.Substring(1)) - int.Parse(__1.TitleId.Substring(1))) == 1;
        return false;
    }
    private static bool Candidate(FeudalTitleType __1, List<FeudalTitleRecord> __2,
        ref FeudalTitleFormationCandidate __4, ref FeudalTitleFormationBlockReason __5, ref string __6, ref bool __result)
    {
        __4 = new FeudalTitleFormationCandidate { TargetType = __1, Name = "New title" };
        __4.ChildTitleIds.AddRange(__2.Select(t => t.TitleId));
        __5 = FeudalTitleFormationBlockReason.None; __6 = null; __result = true; return false;
    }
    private static void FormationRead() => _formations++;
    private static void DescendantsRead() => _descendantReads++;
    private static void SuccessorRead() => _successorScans++;
    private static bool Portrait(ref CharacterCode __result) { _portraits++; __result = null; return false; }
    private static bool Grant(ref FeudalGrantPreview __result)
    { _grants++; __result = new FeudalGrantPreview { CanOpen = _canGrant, Reason = _canGrant ? "" : "insufficient influence" }; return false; }
    private static bool Usurp(ref FeudalUsurpationPreview __result) { __result = new FeudalUsurpationPreview(); return false; }
    private static bool Revoke(ref FeudalRevocationPreview __result) { __result = new FeudalRevocationPreview(); return false; }
    private static bool Service(ref FeudalServicePreview __result) { __result = new FeudalServicePreview(); return false; }
    private static bool Rename(ref FeudalRenamePreview __result) { __result = new FeudalRenamePreview(); return false; }
    private static bool Dissolve(ref FeudalDissolutionPreview __result) { __result = new FeudalDissolutionPreview(); return false; }

    private static void ResetCounts()
    { _formations = _grants = _adjacency = _clanReads = _realmReads = _descendantReads = _successorScans = 0; }

    internal static void Run(Action<bool, string> check)
    {
        var harmony = new Harmony("bellum.tests.hierarchy.performance");
        var mod = typeof(FeudalTitleBehavior).Assembly;
        var type = typeof(FeudalTitleBehavior);
        var titles = new FeudalTitleBehavior();
        _campaign = Blank<Campaign>(); _campaign.AddCampaignBehaviorManager(new Manager { Titles = titles });
        _player = Blank<Clan>(); _player.StringId = "audit_house";
        _leader = Blank<Hero>(); _realm = Blank<Kingdom>(); _realm.StringId = "audit_realm";
        _kingdoms = new MBReadOnlyList<Kingdom>(new List<Kingdom> { _realm });
        Crowns.Clear(); DeadRealms.Clear(); _gold = 10000000; _canGrant = true;
        void Patch(MethodBase method, string prefix) => harmony.Patch(method, prefix: new HarmonyMethod(typeof(HierarchyPerformanceTests), prefix));
        void Getter(Type target, string name, string prefix) => Patch(AccessTools.PropertyGetter(target, name), prefix);
        try
        {
            Getter(typeof(Campaign), "Current", nameof(Current));
            Getter(typeof(Clan), "PlayerClan", nameof(Player)); Getter(typeof(Clan), "Leader", nameof(Leader));
            Getter(typeof(Clan), "Kingdom", nameof(Realm)); Getter(typeof(Clan), "All", nameof(Clans));
            Getter(typeof(Clan), "Banner", nameof(NoBanner)); Getter(typeof(Clan), "Name", nameof(TextName));
            Getter(typeof(Kingdom), "All", nameof(Kingdoms)); Getter(typeof(Kingdom), "RulingClan", nameof(Player));
            Getter(typeof(Kingdom), "Clans", nameof(Clans));
            Getter(typeof(Kingdom), "Name", nameof(TextName)); Getter(typeof(Kingdom), "IsEliminated", nameof(Eliminated));
            Getter(typeof(Hero), "IsDead", nameof(No)); Getter(typeof(Hero), "Gold", nameof(Gold));
            Getter(typeof(Hero), "Name", nameof(TextName)); Getter(typeof(Hero), "CharacterObject", nameof(NoCharacter));
            Patch(AccessTools.Method(mod.GetType("BellumCivile.BellumKingdomVisibilityHelper"), "IsTemporaryBellumKingdom"), nameof(No));
            Patch(AccessTools.Method(type, "GetRealmSovereignTitle", new[] { typeof(Kingdom), typeof(FeudalHierarchyMode) }), nameof(Crown));
            Patch(AccessTools.Method(type, "GetIndependentRealmSovereignTitle"), nameof(Crown));
            Patch(AccessTools.Method(type, "GetTitleProsperityScore"), nameof(Zero));
            Patch(AccessTools.Method(type, "HasStructuralFeud"), nameof(No));
            Patch(AccessTools.Method(type, "GetMinimumChildrenForFormation"), nameof(Minimum));
            Patch(AccessTools.Method(type, "AreTitlesAdjacent"), nameof(Adjacent));
            Patch(AccessTools.Method(type, "TryBuildCandidateFromCluster"), nameof(Candidate));
            var display = mod.GetType("BellumCivile.FeudalTitleDisplayHelper");
            Patch(AccessTools.Method(display, "FormatTitleName", new[] { typeof(FeudalTitleRecord), typeof(Clan) }), nameof(Format));
            Patch(AccessTools.Method(display, "FormatTitleName", new[] { typeof(FeudalTitleType), typeof(string), typeof(Clan), typeof(Kingdom), typeof(string) }), nameof(CandidateName));
            Patch(AccessTools.Method(typeof(HierarchyTitleNodeVM), "AppendFabricationSummary"), nameof(Summary));
            Patch(AccessTools.Method(mod.GetType("BellumCivile.UI.PortraitAppearance"), "Create"), nameof(Portrait));
            var actions = typeof(FeudalTitlePlayerActionService);
            foreach (var pair in new[] { ("GetGrantPreview", nameof(Grant)), ("GetUsurpationPreview", nameof(Usurp)),
                ("GetRevocationPreview", nameof(Revoke)), ("GetServicePreview", nameof(Service)),
                ("GetRenamePreview", nameof(Rename)), ("GetDissolutionPreview", nameof(Dissolve)) })
                Patch(AccessTools.Method(actions, pair.Item1), pair.Item2);
            harmony.Patch(AccessTools.Method(type, "GetFormationAssessment"), prefix: new HarmonyMethod(typeof(HierarchyPerformanceTests), nameof(FormationRead)));
            harmony.Patch(AccessTools.Method(type, "GetTitleAndDescendants", new[] { typeof(FeudalTitleRecord), typeof(FeudalHierarchyMode) }),
                prefix: new HarmonyMethod(typeof(HierarchyPerformanceTests), nameof(DescendantsRead)));
            harmony.Patch(AccessTools.Method(type, "IsHistoricalRealmRepresentedByActiveSuccessor"),
                prefix: new HarmonyMethod(typeof(HierarchyPerformanceTests), nameof(SuccessorRead)));

            var registry = Field<Dictionary<string, FeudalTitleRecord>>(titles, "_titlesById");
            var legal = Field<Dictionary<string, List<string>>>(titles, "_childrenByParentTitleId");
            var actual = Field<Dictionary<string, List<string>>>(titles, "_childrenByDeFactoParentTitleId");
            var root = new FeudalTitleRecord("crown", "Crown", FeudalTitleType.Kingdom, _player.StringId, _player.StringId, "", "", _realm.StringId, 0, 0);
            registry[root.TitleId] = root; Crowns[_realm] = root;
            foreach (int count in new[] { 8, 32, 128 })
            {
                registry.Clear(); registry[root.TitleId] = root;
                legal.Clear(); actual.Clear();
                var children = new List<string>(); legal[root.TitleId] = children; actual[root.TitleId] = children;
                for (int i = 0; i < count; i++)
                {
                    var title = new FeudalTitleRecord("b" + i.ToString("D4"), "Barony " + i, FeudalTitleType.Barony,
                        _player.StringId, _player.StringId, root.TitleId, "", _realm.StringId, 0, 0);
                    registry[title.TitleId] = title; children.Add(title.TitleId);
                }
                foreach (string index in new[] { "_titlesByDeJureHolder", "_titlesByDeFactoHolder" })
                    Field<Dictionary<string, List<string>>>(titles, index)[_player.StringId] = registry.Keys.ToList();
                ResetCounts(); var timer = Stopwatch.StartNew();
                var tab = new KingdomHierarchyTabVM(_realm); timer.Stop();
                var nodes = Field<List<HierarchyTitleNodeVM>>(tab, "_titleNodes");
                check(nodes.Count == count + 1 && nodes.All(n => n.Name.Length > 0), "All titles and names load in a " + count + "-barony hierarchy");
                check(_formations == 0 && _grants == 0 && _adjacency == 0, "Opening " + count + " baronies runs no formation searches or grant previews");
                check(_clanReads == 1 && _realmReads == 1 && _descendantReads == 1, "Opening " + count + " baronies uses one clan snapshot, realm snapshot and display traversal");
                check(tab.IsDeJureAvailable && tab.IsDeJureMode && !tab.HasSelectedTitle, "Initial legal hierarchy and empty selection are preserved");
                for (int i = 0; i < 1000; i++) { _ = tab.IsDeJureAvailable; _ = tab.IsDeJureMode; _ = tab.IsDeFactoMode; }
                check(_descendantReads == 1, "Repeated UI binding reads do not traverse the hierarchy again");
                Console.WriteLine("MEASURE: {0} baronies opened in {1:F2} ms; formation/adjacency/grant calls: 0/0/0", count, timer.Elapsed.TotalMilliseconds);

                var node = nodes.Single(n => n.Title.TitleId == "b0000");
                node.ExecuteSelect();
                check(tab.SelectedTitle == node && node.IsSelected && node.IsFormationEnabled && node.IsGrantEnabled,
                    "Selecting a title populates the existing action availability");
                check(node.ClaimHint != null && node.FormationHint != null && node.GrantHint != null && _formations == 1 && _grants == 1,
                    "Only the selected title builds complete previews and hints");
                tab.ExecuteClearSelection(); _gold = 0; node.ExecuteSelect();
                check(!node.IsFormationEnabled && node.FormationHint.HintText.ToString().Contains("only 0"), "Reselection observes current gold and updates the formation hint");
                _gold = 10000000; tab.ExecuteClearSelection(); node.ExecuteSelect();
                int before = _formations; _gold = 0; node.ExecuteForm();
                check(_formations > before && !node.IsFormationEnabled, "Form command revalidates resources instead of trusting its earlier preview");
                _gold = 10000000; _canGrant = false; before = _grants; node.ExecuteGrant();
                check(_grants > before && !node.IsGrantEnabled, "Grant command revalidates its recipient preview before opening a dialogue");
                _canGrant = true;
                tab.ExecuteShowDeFacto();
                check(tab.IsDeFactoMode && Field<List<HierarchyTitleNodeVM>>(tab, "_titleNodes").Count == count + 1, "Political hierarchy retains every controlled title");
                tab.ExecuteShowDeJure(); tab.RefreshValues();
                check(tab.IsDeJureMode && !tab.HasSelectedTitle && tab.HierarchyRoots.Count == 1, "View switching and reopening clear stale selections without duplicating roots");
            }

            ResetCounts();
            var direct = new HierarchyTitleNodeVM(root, titles, _realm, null, null, null);
            check(_formations == 0 && _grants == 0 && direct.FormationHint == null, "Public node construction also leaves unopened action previews deferred");

            var child = registry["b0000"];
            child.SetDeJureHolder("foreign_house");
            var splitTab = new KingdomHierarchyTabVM(_realm);
            var split = Field<List<HierarchyTitleNodeVM>>(splitTab, "_titleNodes").Single(n => n.Title == child);
            split.ExecuteSelect();
            check(split.Status == "de facto only" && !split.IsFormationEnabled
                && split.FormationHint.HintText.ToString().Contains("both de jure and de facto"),
                "Split legal and political ownership retains its tenure and formation restriction");
            child.SetDeJureHolder(_player.StringId);

            var mixinType = mod.GetType("BellumCivile.ViewModelMixin.KingdomManagementVMMixin");
            var vmProperty = AccessTools.Property(mixinType, "ViewModel");
            var nativeVm = FormatterServices.GetUninitializedObject(vmProperty.PropertyType);
            Getter(vmProperty.PropertyType, "Kingdom", nameof(Realm));
            var mixin = FormatterServices.GetUninitializedObject(mixinType);
            var vmField = AccessTools.Field(mixinType, "_vm");
            vmField.SetValue(mixin, Activator.CreateInstance(vmField.FieldType, new[] { nativeVm }));
            ResetCounts();
            var initial = (KingdomHierarchyTabVM)Call(mixin, "EnsureBellumHierarchy", true);
            check(initial != null && _descendantReads == 1, "First tab initialization builds its hierarchy exactly once");
            check(Call(mixin, "EnsureBellumHierarchy", true) == initial && _descendantReads == 2,
                "An existing tab refreshes exactly once when requested");
            check(Call(mixin, "EnsureBellumHierarchy", false) == initial && _descendantReads == 2,
                "Looking up an existing tab without refresh does not rebuild it");
            GC.KeepAlive(nativeVm);

            bool Has(FeudalTitleRecord title, FeudalHierarchyMode mode) => (bool)Call(titles, "HasActiveChildTitle", title, mode);
            check(Has(root, FeudalHierarchyMode.DeJure) && !Has(child, FeudalHierarchyMode.DeJure), "Indexed existence check distinguishes parents and leaves");
            legal[root.TitleId] = new List<string> { root.TitleId, "missing", child.TitleId };
            child.SetActive(false);
            check(!Has(root, FeudalHierarchyMode.DeJure) && Has(root, FeudalHierarchyMode.DeFacto), "Missing, inactive and self-linked children do not invent legal descendants");
            child.SetActive(true);
            check(Has(root, FeudalHierarchyMode.DeJure), "Active legal children are recognized immediately");
            root.SetActive(false);
            check(!Has(root, FeudalHierarchyMode.DeJure) && !Has(null, FeudalHierarchyMode.DeJure), "Inactive or absent roots remain unavailable");
            root.SetActive(true);
            legal[root.TitleId] = new List<string>();
            var fallback = new KingdomHierarchyTabVM(_realm);
            check(!fallback.IsDeJureAvailable && fallback.IsDeFactoAvailable && fallback.IsDeFactoMode,
                "A realm without legal descendants still falls back to its political hierarchy");
            legal[root.TitleId].Add(child.TitleId); fallback.RefreshValues();
            check(fallback.IsDeJureAvailable && fallback.IsDeJureMode,
                "Refreshing after a legal hierarchy appears updates the cached toggle state");

            var history = Field<Dictionary<string, string>>(titles, "_historicalRealmSovereignTitleByKingdomId");
            var old = Blank<Kingdom>(); old.StringId = "old_realm"; DeadRealms.Add(old); history[old.StringId] = root.TitleId;
            _kingdoms.Add(old); ResetCounts();
            var historicalTab = new KingdomHierarchyTabVM(_realm);
            check(historicalTab.Kingdoms.Count == 1 && _successorScans == 0, "A living successor suppresses the historical duplicate without per-realm global scans");
            Crowns.Remove(_realm); ResetCounts(); historicalTab.RefreshValues();
            check(historicalTab.Kingdoms.Any(k => k.IsHistorical) && _successorScans == 0, "An unrepresented historical crown remains visible using the refresh snapshot");
            historicalTab.Kingdoms.Single(k => k.IsHistorical).ExecuteSelect();
            check(historicalTab.IsDeJureMode && !historicalTab.IsDeFactoAvailable, "Historical trees remain legal-only");
            Crowns[_realm] = root;

            var contextType = mod.GetType("BellumCivile.UI.VanillaTabs.Kingdoms.Hierarchy.HierarchyBuildContext");
            object Context() => Activator.CreateInstance(contextType, BindingFlags.Instance | BindingFlags.NonPublic, null, new object[] { new[] { _player } }, null);
            var context = Context(); var character = Blank<CharacterObject>(); _portraits = 0;
            for (int i = 0; i < 20; i++) Call(context, "GetPortrait", character);
            check(_portraits == 1, "Repeated holders prepare one portrait appearance per tree build");
            Call(Context(), "GetPortrait", character);
            check(_portraits == 2, "A new build refreshes portraits instead of retaining campaign-wide appearance caches");
            check(Call(context, "ResolveClan", _player.StringId) == _player && Call(context, "ResolveClan", "missing") == null,
                "Clan lookup snapshot preserves existing and missing-holder semantics");
            string originalName = (string)Call(context, "FormatTitleName", child);
            child.SetName("Renamed barony");
            check((string)Call(Context(), "FormatTitleName", child) == "Renamed barony" && originalName != "Renamed barony",
                "A new tree build observes renamed titles without a persistent display cache");
        }
        finally { harmony.UnpatchAll(harmony.Id); Crowns.Clear(); DeadRealms.Clear(); _campaign = null; }
    }
}
