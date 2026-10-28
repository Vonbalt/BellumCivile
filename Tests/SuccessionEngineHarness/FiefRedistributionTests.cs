using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using BellumCivile;
using BellumCivile.Behaviors;
using BellumCivile.Patches;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Election;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Library;
using TaleWorlds.Localization;

internal static class FiefRedistributionTests
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
        private readonly Dictionary<string, object> _data = new Dictionary<string, object>();
        public bool IsLoading { get; set; }
        public bool IsSaving => !IsLoading;
        public bool SyncData<T>(string key, ref T data)
        {
            if (IsSaving)
                _data[key] = data is Dictionary<string, string> strings ? new Dictionary<string, string>(strings) : (object)data;
            else if (_data.TryGetValue(key, out object value))
                data = value is Dictionary<string, string> strings ? (T)(object)new Dictionary<string, string>(strings) : (T)value;
            return true;
        }
    }

    private static readonly Dictionary<Clan, Kingdom> Realms = new Dictionary<Clan, Kingdom>();
    private static readonly Dictionary<Clan, Hero> Heads = new Dictionary<Clan, Hero>();
    private static readonly Dictionary<Kingdom, Clan> Crowns = new Dictionary<Kingdom, Clan>();
    private static Campaign _campaign;
    private static Manager _manager;
    private static FeudalTitleBehavior _titles;
    private static FiefDeliberationBehavior _votes;
    private static Kingdom _north, _south;
    private static Clan _giver, _ira, _crown, _foreign, _other, _owner;
    private static Settlement _settlement;
    private static FeudalTitleRecord _barony, _county;
    private static bool _throwAward, _nativeVoluntary;
    private static T Blank<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    private static void Set(object o, string field, object value) => AccessTools.Field(o.GetType(), field).SetValue(o, value);
    private static T Field<T>(object o, string field) => (T)AccessTools.Field(o.GetType(), field).GetValue(o);
    private static object Call(object o, string name, params object[] args) => AccessTools.Method(o.GetType(), name).Invoke(o, args);
    private static Dictionary<string, FeudalTitleRecord> Registry => Field<Dictionary<string, FeudalTitleRecord>>(_titles, "_titlesById");
    private static List<FeudalClaimRecord> Claims => Field<List<FeudalClaimRecord>>(_titles, "_claims");
    private static bool Skip() => false;
    private static bool No(ref bool __result) { __result = false; return false; }
    private static bool Yes(ref bool __result) { __result = true; return false; }
    private static bool Current(ref Campaign __result) { __result = _campaign; return false; }
    private static bool Player(ref Clan __result) { __result = _giver; return false; }
    private static bool Realm(Clan __instance, ref Kingdom __result) { Realms.TryGetValue(__instance, out __result); return false; }
    private static bool Head(Clan __instance, ref Hero __result) { Heads.TryGetValue(__instance, out __result); return false; }
    private static bool HeroHouse(Hero __instance, ref Clan __result) { __result = Heads.FirstOrDefault(p => p.Value == __instance).Key; return false; }
    private static bool Ruler(Kingdom __instance, ref Clan __result) { Crowns.TryGetValue(__instance, out __result); return false; }
    private static bool Clans(ref MBReadOnlyList<Clan> __result) { __result = new MBReadOnlyList<Clan>(Realms.Keys.ToList()); return false; }
    private static bool Kingdoms(ref MBReadOnlyList<Kingdom> __result) { __result = new MBReadOnlyList<Kingdom>(Crowns.Keys.ToList()); return false; }
    private static bool Decisions(ref MBReadOnlyList<KingdomDecision> __result) { __result = new MBReadOnlyList<KingdomDecision>(new KingdomDecision[0]); return false; }
    private static bool Owner(ref Clan __result) { __result = _owner; return false; }
    private static bool MapFaction(ref IFaction __result) { __result = Realms[_owner]; return false; }
    private static bool Now(ref CampaignTime __result) { __result = CampaignTime.Days(100); return false; }
    private static bool Day(ref float __result) { __result = 100; return false; }
    private static bool Name(ref string __result) { __result = "Syratos"; return false; }
    private static bool TextName(ref TextObject __result) { __result = new TextObject("Syratos"); return false; }
    private static bool ParentId(Kingdom __0, ref string __result) { __result = "bc_title_kingdom_" + __0.StringId; return false; }
    private static bool Sovereign(Kingdom __0, ref FeudalTitleRecord __result)
    { Registry.TryGetValue("bc_title_kingdom_" + __0.StringId, out __result); return false; }
    private static bool EnsureCrown(Kingdom __0, ref bool __2, ref FeudalTitleRecord __result)
    { __2 = false; return Sovereign(__0, ref __result); }
    private static bool NativeAward(DecisionOutcome chosenOutcome)
    {
        if (_throwAward) throw new InvalidOperationException("injected award failure");
        _votes.ClearFiefVoteStateForSettlement(_settlement, "earlier ownership listener");
        _nativeVoluntary = Voluntary();
        Award(((SettlementClaimantDecision.ClanAsDecisionOutcome)chosenOutcome).Clan);
        return false;
    }

    private static Clan House(string id, Kingdom realm)
    {
        var clan = Blank<Clan>(); clan.StringId = id; Realms[clan] = realm;
        var hero = Blank<Hero>(); hero.StringId = id + "_head"; Heads[clan] = hero; return clan;
    }
    private static FeudalTitleRecord Title(string id, FeudalTitleType rank, Clan owner, string parent, Kingdom origin)
    {
        var title = new FeudalTitleRecord(id, id, rank, owner.StringId, owner.StringId, parent, "", origin.StringId, 0, 0);
        Registry.Add(id, title); return title;
    }
    private static void Reset()
    {
        Realms.Clear(); Heads.Clear(); Crowns.Clear(); _manager.Items.Clear();
        _throwAward = _nativeVoluntary = false;
        _north = Blank<Kingdom>(); _north.StringId = "empire";
        _south = Blank<Kingdom>(); _south.StringId = "empire_s";
        _giver = House("player_faction", _south); _ira = House("ira_house", _south);
        _crown = House("south_crown", _south); _foreign = House("north_legal_house", _north); _other = House("other", _south);
        Crowns[_south] = _crown; Crowns[_north] = _foreign; _owner = _giver;
        _titles = new FeudalTitleBehavior(); _votes = new FiefDeliberationBehavior();
        _manager.Items.AddRange(new CampaignBehaviorBase[] { _titles, _votes });
        Title("bc_title_kingdom_empire", FeudalTitleType.Kingdom, _foreign, "", _north);
        Title("bc_title_kingdom_empire_s", FeudalTitleType.Kingdom, _crown, "", _south);
        _county = Title("county_ophthys", FeudalTitleType.County, _giver, "bc_title_kingdom_empire", _north);
        _barony = Title("bc_title_barony_castle_EN8", FeudalTitleType.Barony, _giver, _county.TitleId, _north);
        _barony.SetCapitalSettlement("castle_EN8");
        _settlement = Blank<Settlement>(); _settlement.StringId = "castle_EN8";
        Set(_settlement, "Town", Blank<Town>());
        Call(_titles, "RebuildRuntimeIndexes", true, true);
    }
    private static SettlementClaimantDecision Decision(Clan proposer = null, Clan excluded = null, Settlement settlement = null)
    {
        var decision = Blank<SettlementClaimantDecision>();
        Set(decision, "_kingdom", _south); Set(decision, "<ProposerClan>k__BackingField", proposer ?? _giver);
        Set(decision, "ClanToExclude", excluded ?? _giver); Set(decision, "Settlement", settlement ?? _settlement);
        return decision;
    }
    private static void Award(Clan recipient, ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail detail = ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail.ByKingDecision)
    {
        var previous = _owner; _owner = recipient;
        Call(_titles, "OnSettlementOwnerChanged", _settlement, false, Heads[recipient], Heads[previous], null, detail);
    }
    private static bool Voluntary(string giver = null, Clan recipient = null, Settlement settlement = null)
        => (bool)AccessTools.Method(typeof(FiefVoteResolutionPatch), "IsVoluntaryRelinquishment").Invoke(null,
            new object[] { settlement ?? _settlement, giver ?? _giver.StringId, recipient ?? _ira });
    private static void InVote(SettlementClaimantDecision decision, Action action)
    {
        FiefVoteResolutionPatch.Prefix(decision);
        try { action(); }
        finally { FiefVoteResolutionPatch.Finalizer(decision, null); }
    }
    private static void CheckHierarchy(Action<bool, string> check, string scenario)
    {
        check(_barony.DeJureHolderClanId == _ira.StringId && _barony.DeFactoHolderClanId == _ira.StringId,
            scenario + ": recipient receives both ownership rights");
        check(_barony.ParentTitleId == _county.TitleId && _barony.AssociatedKingdomId == _north.StringId,
            scenario + ": existing legal parent and origin are preserved");
    }

    internal static void Run(Action<bool, string> check)
    {
        var h = new Harmony("bellum.tests.fief_redistribution");
        _manager = new Manager(); _campaign = Blank<Campaign>(); _campaign.AddCampaignBehaviorManager(_manager);
        var ticks = AccessTools.Field(typeof(CampaignTime), "TimeTicksPerDay"); var previousTicks = ticks.GetValue(null);
        void Patch(MethodBase method, string name)
        {
            if (method == null) throw new Exception("Missing redistribution fixture method: " + name);
            h.Patch(method, prefix: new HarmonyMethod(typeof(FiefRedistributionTests), name));
        }
        void Getter(Type type, string name, string prefix) => Patch(AccessTools.PropertyGetter(type, name), prefix);
        var type = typeof(FeudalTitleBehavior); var mod = type.Assembly;
        try
        {
            ticks.SetValue(null, 24000L);
            Getter(typeof(Campaign), "Current", nameof(Current)); Getter(typeof(CampaignTime), "Now", nameof(Now));
            Getter(typeof(Clan), "PlayerClan", nameof(Player)); Getter(typeof(Clan), "Kingdom", nameof(Realm));
            Getter(typeof(Clan), "Leader", nameof(Head)); Getter(typeof(Clan), "All", nameof(Clans));
            Getter(typeof(Hero), "Clan", nameof(HeroHouse)); Getter(typeof(Hero), "IsDead", nameof(No));
            Getter(typeof(Clan), "IsEliminated", nameof(No));
            Getter(typeof(Kingdom), "RulingClan", nameof(Ruler)); Getter(typeof(Kingdom), "All", nameof(Kingdoms));
            Getter(typeof(Kingdom), "UnresolvedDecisions", nameof(Decisions));
            foreach (string flag in new[] { "IsEliminated", "IsMinorFaction", "IsBanditFaction" }) Getter(typeof(Kingdom), flag, nameof(No));
            Getter(typeof(Settlement), "OwnerClan", nameof(Owner)); Getter(typeof(Settlement), "MapFaction", nameof(MapFaction));
            Getter(typeof(Settlement), "Name", nameof(TextName)); Getter(typeof(Clan), "Name", nameof(TextName));
            Getter(typeof(Settlement), "IsCastle", nameof(Yes)); Getter(typeof(Settlement), "IsTown", nameof(No));
            Getter(type, "CurrentDay", nameof(Day));
            Patch(AccessTools.Method(type, "BuildConfiguredFallbackTitleRoot"), nameof(Name));
            Patch(AccessTools.Method(type, "ResolveEffectiveConfiguredTitleName"), nameof(Name));
            Patch(AccessTools.Method(type, "ResolveKingdomLegalParentTitleId"), nameof(ParentId));
            Patch(AccessTools.Method(type, "GetRealmSovereignTitle", new[] { typeof(Kingdom), typeof(FeudalHierarchyMode) }), nameof(Sovereign));
            Patch(AccessTools.Method(type, "EnsureKingdomTitle", new[] { typeof(Kingdom), typeof(string), typeof(bool).MakeByRefType() }), nameof(EnsureCrown));
            Patch(AccessTools.Method(type, "QueueServiceReview"), nameof(Skip));
            Patch(AccessTools.Method(typeof(FiefDeliberationBehavior), "BuildPreliminaryNominations"), nameof(Skip));
            Patch(AccessTools.Method(typeof(BellumCivileLogger), "Log", new[] { typeof(string) }), nameof(Skip));
            foreach (var method in mod.GetType("BellumCivile.BellumCivileNotifications").GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(m => m.Name == "Show")) Patch(method, nameof(Skip));
            check(h.CreateClassProcessor(typeof(FiefVoteResolutionPatch)).Patch().Count == 1, "Native fief-award scope patch binds to the selected game");
            h.Patch(AccessTools.Method(typeof(SettlementClaimantDecision), "ApplyChosenOutcome"),
                prefix: new HarmonyMethod(typeof(FiefRedistributionTests), nameof(NativeAward)) { priority = Priority.Last });
            RunCases(check);
            CourtPoliticalReactionTests.Run(check);
        }
        finally { h.UnpatchAll(h.Id); ticks.SetValue(null, previousTicks); _campaign = null; }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void RunCases(Action<bool, string> check)
    {
        Reset(); InVote(Decision(), () => Award(_ira));
        CheckHierarchy(check, "Voluntary Syratos award");
        check(_barony.DeFactoParentTitleId == _county.TitleId, "Actual lower-rank vassalage still follows the controlled county");
        check(!_titles.HasActiveClaim(_giver, _barony), "Voluntary giver receives no new strong dispossession claim");
        check(!Voluntary(), "Completed ballot leaves no voluntary-award scope behind");

        Reset(); InVote(Decision(_crown), () => Award(_ira));
        CheckHierarchy(check, "Involuntary domestic redistribution");
        check(_titles.GetStrongestActiveClaim(_giver, _barony)?.Strength == FeudalClaimStrength.Strong,
            "Involuntary domestic dispossession still creates its rightful strong claim");

        Reset(); _barony.SetAssociatedKingdom(""); InVote(Decision(), () => Award(_ira));
        CheckHierarchy(check, "Origin inferred from legal ancestors");

        Reset(); _barony.SetParentTitle("missing_parent"); InVote(Decision(), () => Award(_ira));
        check(_barony.ParentTitleId == "bc_title_kingdom_empire_s", "Missing legal parent still receives the existing fallback repair");

        Reset(); _barony.SetDeJureHolder(_foreign.StringId); Call(_titles, "RebuildRuntimeIndexes", true, true);
        _titles.RegisterClaim(_giver, _barony, FeudalClaimStrength.Weak, "fixture");
        var otherTitle = Title("another_title", FeudalTitleType.Barony, _other, _county.TitleId, _north);
        _titles.RegisterClaim(_giver, otherTitle, FeudalClaimStrength.Weak, "fixture");
        InVote(Decision(), () => Award(_ira));
        check(_barony.DeJureHolderClanId == _foreign.StringId, "Giving away possession cannot convey a third party's legal ownership");
        check(_barony.ParentTitleId == _county.TitleId && _barony.AssociatedKingdomId == _north.StringId,
            "Third-party rights retain their legal topology");
        check(!_titles.HasActiveClaim(_giver, _barony) && _titles.HasActiveClaim(_giver, otherTitle),
            "Only the voluntarily surrendered fief's existing giver claims are relinquished");

        Reset(); _barony.SetDeJureHolder(_foreign.StringId); Call(_titles, "RebuildRuntimeIndexes", true, true);
        _titles.RegisterClaim(_ira, _barony, FeudalClaimStrength.Weak, "fixture");
        InVote(Decision(), () => Award(_ira));
        CheckHierarchy(check, "Domestic award with a foreign legal claimant");
        check(_titles.GetStrongestActiveClaim(_foreign, _barony)?.Strength == FeudalClaimStrength.Strong,
            "Voluntary surrender of possession does not suppress another legal owner's dispossession claim");

        Reset(); InVote(Decision(), () => Award(_foreign));
        check(_titles.GetStrongestActiveClaim(_giver, _barony)?.Strength == FeudalClaimStrength.Strong,
            "A foreign recipient cannot consume a domestic voluntary-award context");

        Reset(); var initial = Decision();
        InVote(initial, () => {
            check(Voluntary(), "Self-proposed exclusion identifies voluntary surrender");
            check(!Voluntary(_other.StringId) && !Voluntary(recipient: _giver), "Context rejects a different giver and self-award");
            var otherSettlement = Blank<Settlement>(); otherSettlement.StringId = "castle_other";
            check(!Voluntary(settlement: otherSettlement), "Context is restricted to the exact settlement");
            var nested = Decision(_crown);
            InVote(nested, () => check(!Voluntary(), "Nested nonvoluntary ballot cannot borrow outer voluntary intent"));
            check(Voluntary(), "Nested finalizer restores the original award context");
        });
        check(!Voluntary(), "Outermost finalizer clears all intent");
        var failedDecision = Decision(); FiefVoteResolutionPatch.Prefix(failedDecision);
        var failure = new InvalidOperationException("fixture");
        check(ReferenceEquals(FiefVoteResolutionPatch.Finalizer(failedDecision, failure), failure) && !Voluntary(),
            "Native failures retain their original exception and release voluntary intent");

        Reset(); _barony.SetDeJureHolder(_foreign.StringId); Call(_titles, "RebuildRuntimeIndexes", true, true);
        _titles.RegisterClaim(_giver, _barony, FeudalClaimStrength.Weak, "fixture");
        check(_votes.QueueRelinquishedSettlementVote(_settlement), "Claimant may queue voluntary surrender of actual possession");
        _votes.ClearFiefVoteStateForSettlement(_settlement, "cancelled before award");
        check(_titles.HasActiveClaim(_giver, _barony) && _barony.DeFactoHolderClanId == _giver.StringId,
            "Cancelling deliberation does not relinquish rights or possession before an award occurs");

        Reset(); check(_votes.QueueRelinquishedSettlementVote(_settlement), "Real voluntary queue accepts the player's fief");
        var store = new Store(); _votes.SyncData(store); _manager.Items.Remove(_votes);
        _votes = new FiefDeliberationBehavior(); _manager.Items.Add(_votes); store.IsLoading = true; _votes.SyncData(store);
        string key = "empire_s|castle_EN8";
        var proposers = Field<Dictionary<string, string>>(_votes, "_pendingFiefProposer");
        var exclusions = Field<Dictionary<string, string>>(_votes, "_pendingFiefExclude");
        check(proposers[key] == _giver.StringId && exclusions[key] == _giver.StringId,
            "Existing saved queue fields retain the voluntary grantor without a new save schema");
        var reloaded = Decision(Realms.Keys.Single(c => c.StringId == proposers[key]), Realms.Keys.Single(c => c.StringId == exclusions[key]));
        reloaded.ApplyChosenOutcome(new SettlementClaimantDecision.ClanAsDecisionOutcome(_ira));
        check(_nativeVoluntary && !proposers.ContainsKey(key), "Native award scope survives pending-metadata cleanup before the title callback");
        CheckHierarchy(check, "Loaded pending vote");
        check(!_titles.HasActiveClaim(_giver, _barony), "Loaded voluntary vote does not generate a new claim");
        check(!Voluntary(), "Native award finalizer releases the voluntary context");

        Reset(); _throwAward = true;
        bool threw = false;
        try { Decision().ApplyChosenOutcome(new SettlementClaimantDecision.ClanAsDecisionOutcome(_ira)); }
        catch (InvalidOperationException) { threw = true; }
        check(threw && !Voluntary() && _barony.DeJureHolderClanId == _giver.StringId,
            "Native award failure does not leak context or transfer the title");

        foreach (var rank in new[] { FeudalTitleType.County, FeudalTitleType.Duchy })
        {
            Reset(); var estate = Title("ira_estate", rank, _ira, "bc_title_kingdom_empire_s", _south);
            Call(_titles, "RebuildRuntimeIndexes", true, true); InVote(Decision(), () => Award(_ira));
            CheckHierarchy(check, "Rank-aware award to " + rank);
            check(_barony.DeFactoParentTitleId == estate.TitleId, "Recipient is not subordinated to an equal/lower-rank county: " + rank);
        }
    }
}
