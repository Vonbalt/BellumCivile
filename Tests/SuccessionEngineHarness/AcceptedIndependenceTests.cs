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
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

internal static class AcceptedIndependenceTests
{
    private sealed class Manager : ICampaignBehaviorManager
    {
        internal readonly List<CampaignBehaviorBase> Items = new List<CampaignBehaviorBase>();
        public T GetBehavior<T>() => Items.OfType<T>().FirstOrDefault();
        public IEnumerable<T> GetBehaviors<T>() => Items.OfType<T>();
        public void AddBehavior(CampaignBehaviorBase behavior) => Items.Add(behavior);
        public void RemoveBehavior<T>() where T : CampaignBehaviorBase => Items.RemoveAll(b => b is T);
        public void ClearBehaviors() => Items.Clear();
        public void InitializeCampaignBehaviors(IEnumerable<CampaignBehaviorBase> behaviors) => Items.AddRange(behaviors);
        public void LoadBehaviorData() { }
        public void RegisterEvents() { }
    }

    private sealed class Initialization
    {
        internal TextObject Name, RulerTitle;
        internal CultureObject Culture;
        internal Settlement Capital;
    }

    private static readonly Dictionary<Clan, Kingdom> Realms = new Dictionary<Clan, Kingdom>();
    private static readonly Dictionary<Clan, Hero> Heads = new Dictionary<Clan, Hero>();
    private static readonly Dictionary<Clan, CultureObject> Cultures = new Dictionary<Clan, CultureObject>();
    private static readonly Dictionary<Kingdom, Clan> Rulers = new Dictionary<Kingdom, Clan>();
    private static readonly Dictionary<Kingdom, Initialization> Initialized = new Dictionary<Kingdom, Initialization>();
    private static readonly Dictionary<Kingdom, FeudalTitleRecord> ExpectedSources = new Dictionary<Kingdom, FeudalTitleRecord>();
    private static readonly List<Kingdom> Kingdoms = new List<Kingdom>(), Created = new List<Kingdom>();
    private static readonly List<Clan> Moves = new List<Clan>(), Attempts = new List<Clan>();
    private static readonly HashSet<Kingdom> Gone = new HashSet<Kingdom>();
    private static readonly HashSet<Clan> Eliminated = new HashSet<Clan>(), Minor = new HashSet<Clan>(), Mercenary = new HashSet<Clan>();
    private static readonly HashSet<Hero> Dead = new HashSet<Hero>();
    private static readonly List<string> Forbidden = new List<string>();
    private static readonly List<Clan> CallbackLeaders = new List<Clan>();
    private static Campaign _campaign;
    private static Manager _manager;
    private static FactionManagerBehavior _factions;
    private static CivilWarResolutionBehavior _resolver;
    private static FeudalTitleBehavior _titles;
    private static RebellionSummaryBehavior _summary;
    private static FactionObject _faction;
    private static Kingdom _parent;
    private static Clan _crown, _sponsor, _blocked;
    private static Action<bool, string> _check;
    private static int _notices, _titleCallbacks, _reconciliations;
    private static string _notice;
    private static Clan _noticeSponsor;

    private static T Blank<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    private static object Call(object owner, string method, params object[] args) =>
        AccessTools.Method(owner.GetType(), method).Invoke(owner, args);
    private static Dictionary<string, FeudalTitleRecord> Registry =>
        (Dictionary<string, FeudalTitleRecord>)AccessTools.Field(typeof(FeudalTitleBehavior), "_titlesById").GetValue(_titles);
    private static HashSet<string> Outcomes =>
        (HashSet<string>)AccessTools.Field(typeof(RebellionSummaryBehavior), "_countedOutcomes").GetValue(_summary);
    private static List<FactionObject> Active =>
        (List<FactionObject>)AccessTools.Field(typeof(FactionManagerBehavior), "_activeFactions").GetValue(_factions);

    private static bool Skip() => false;
    private static bool Current(ref Campaign __result) { __result = _campaign; return false; }
    private static bool Now(ref CampaignTime __result) { __result = CampaignTime.Zero; return false; }
    private static bool Player(ref Clan __result) { __result = null; return false; }
    private static bool Head(Clan __instance, ref Hero __result) { Heads.TryGetValue(__instance, out __result); return false; }
    private static bool Realm(Clan __instance, ref Kingdom __result) { Realms.TryGetValue(__instance, out __result); return false; }
    private static bool Culture(Clan __instance, ref CultureObject __result) { Cultures.TryGetValue(__instance, out __result); return false; }
    private static bool ClanName(Clan __instance, ref TextObject __result) { __result = new TextObject(__instance.StringId); return false; }
    private static bool CultureName(BasicCultureObject __instance, ref TextObject __result) { __result = new TextObject(__instance.StringId); return false; }
    private static bool RealmName(Kingdom __instance, ref TextObject __result)
    { __result = Initialized.TryGetValue(__instance, out var data) ? data.Name : new TextObject(__instance.StringId); return false; }
    private static bool RealmCulture(Kingdom __instance, ref CultureObject __result)
    { __result = Initialized.TryGetValue(__instance, out var data) ? data.Culture : Cultures[_crown]; return false; }
    private static bool Ruler(Kingdom __instance, ref Clan __result) { Rulers.TryGetValue(__instance, out __result); return false; }
    private static bool SetRuler(Kingdom __instance, Clan value) { Rulers[__instance] = value; return false; }
    private static bool RealmHead(Kingdom __instance, ref Hero __result)
    { __result = Rulers.TryGetValue(__instance, out var clan) && clan != null ? Heads[clan] : null; return false; }
    private static bool ClanGone(Clan __instance, ref bool __result) { __result = Eliminated.Contains(__instance); return false; }
    private static bool IsMinor(Clan __instance, ref bool __result) { __result = Minor.Contains(__instance); return false; }
    private static bool IsMercenary(Clan __instance, ref bool __result) { __result = Mercenary.Contains(__instance); return false; }
    private static bool IsDead(Hero __instance, ref bool __result) { __result = Dead.Contains(__instance); return false; }
    private static bool RealmGone(Kingdom __instance, ref bool __result) { __result = Gone.Contains(__instance); return false; }
    private static bool All(ref MBReadOnlyList<Kingdom> __result) { __result = new MBReadOnlyList<Kingdom>(Kingdoms); return false; }
    private static bool AllClans(ref MBReadOnlyList<Clan> __result) { __result = new MBReadOnlyList<Clan>(Realms.Keys.ToList()); return false; }
    private static bool Clans(Kingdom __instance, ref MBReadOnlyList<Clan> __result)
    { __result = new MBReadOnlyList<Clan>(Realms.Where(p => p.Value == __instance).Select(p => p.Key).ToList()); return false; }
    private static bool NoSettlements(ref MBReadOnlyList<Settlement> __result)
    { __result = new MBReadOnlyList<Settlement>(new List<Settlement>()); return false; }
    private static bool NoDecisions(ref MBReadOnlyList<KingdomDecision> __result)
    { __result = new MBReadOnlyList<KingdomDecision>(new List<KingdomDecision>()); return false; }
    private static bool Influence(ref float __result) { __result = 10f; return false; }

    // Keep the title records and their parent links real; only the registry's cached lookup is replaced.
    private static bool Held(Clan clan, bool deJure, FeudalTitleType? titleType, ref IReadOnlyCollection<FeudalTitleRecord> __result)
    {
        __result = Registry.Values.Where(t => (deJure ? t.DeJureHolderClanId : t.DeFactoHolderClanId) == clan.StringId
            && (!titleType.HasValue || t.TitleType == titleType.Value)).ToList();
        return false;
    }
    private static void Reconciled() { _reconciliations++; }
    private static bool Trap(MethodBase __originalMethod)
    { Forbidden.Add(__originalMethod.DeclaringType.Name + "." + __originalMethod.Name); return false; }
    private static bool Notice(TextObject text, Clan primaryClan)
    { _notices++; _notice = text.ToString(); _noticeSponsor = primaryClan; return false; }
    private static bool Banners(ref Dictionary<Clan, Banner> __result) { __result = new Dictionary<Clan, Banner>(); return false; }

    // Native object registration/initialization/rendering needs a running game. The safety helper stays real.
    private static bool Create(string __0, ref Kingdom __result)
    { __result = NewRealm(__0); Created.Add(__result); return false; }
    private static bool Initialize(Kingdom __instance, object[] __args)
    {
        if (Initialized.ContainsKey(__instance)) throw new Exception("Kingdom initialized twice");
        Initialized[__instance] = new Initialization
        {
            Name = (TextObject)__args[0], Culture = (CultureObject)__args[2],
            Capital = (Settlement)__args[6], RulerTitle = (TextObject)__args[9]
        };
        _check(Rulers.TryGetValue(__instance, out var founder) && founder != null,
            "Native initialization is reached with the intended founder already seeded");
        ExpectedSources[__instance] = Registry.Values.Where(t => t.IsActive && t.DeFactoHolderClanId == founder.StringId)
            .OrderByDescending(t => t.TitleType).FirstOrDefault();
        return false;
    }
    private static bool Destroy(Kingdom destroyedKingdom)
    {
        _check(!Realms.Values.Contains(destroyedKingdom), "Failed realm is empty before native destruction");
        _check(!_titles.IsIndependentRealmShell(destroyedKingdom), "Failed realm source registration is removed before destruction");
        _check(!Registry.Values.Any(t => t.AssociatedKingdomId == destroyedKingdom.StringId) && _reconciliations == 1,
            "Failed source association is restored and the real hierarchy reconciled before destruction");
        Gone.Add(destroyedKingdom);
        return false;
    }
    private static bool Join(Clan clan, Kingdom targetKingdom)
    {
        Attempts.Add(clan);
        var sourceTitle = ExpectedSources[targetKingdom];
        if (sourceTitle != null)
            _check(_titles.GetIndependentRealmSovereignTitle(targetKingdom) == sourceTitle
                && sourceTitle.AssociatedKingdomId == targetKingdom.StringId,
                "Source title is registered before the native join attempt");
        if (clan == _blocked) return false;
        var source = Realms[clan];
        Realms[clan] = targetKingdom;
        Moves.Add(clan);
        // These are the actual production handlers, including roster pruning and leader replacement.
        Call(_factions, "OnClanChangedKingdom", clan, source, targetKingdom,
            ChangeKingdomAction.ChangeKingdomActionDetail.JoinKingdom, false);
        CallbackLeaders.Add(_faction.Leader);
        Call(_titles, "OnClanChangedKingdom", clan, source, targetKingdom,
            ChangeKingdomAction.ChangeKingdomActionDetail.JoinKingdom, false);
        _titleCallbacks++;
        return false;
    }

    private static Kingdom NewRealm(string id)
    { var realm = Blank<Kingdom>(); realm.StringId = id; Kingdoms.Add(realm); return realm; }
    private static Clan House(string id, string culture = "vlandia", Kingdom realm = null)
    {
        var clan = Blank<Clan>(); clan.StringId = id;
        var hero = Blank<Hero>(); hero.StringId = id + "_head";
        var ethnicity = Blank<CultureObject>(); ethnicity.StringId = culture;
        Heads[clan] = hero; Cultures[clan] = ethnicity; Realms[clan] = realm ?? _parent;
        return clan;
    }
    private static FeudalTitleRecord Title(Clan holder, FeudalTitleType rank, string name, FeudalTitleRecord parent = null)
    {
        var title = new FeudalTitleRecord("title_" + name, name, rank, holder.StringId, holder.StringId,
            parent?.TitleId, "", _parent.StringId, 0, 0);
        Registry[title.TitleId] = title;
        return title;
    }
    private static void Reset(string culture = "vlandia")
    {
        Realms.Clear(); Heads.Clear(); Cultures.Clear(); Rulers.Clear(); Initialized.Clear(); ExpectedSources.Clear();
        Kingdoms.Clear(); Created.Clear(); Moves.Clear(); Attempts.Clear(); Gone.Clear();
        Eliminated.Clear(); Minor.Clear(); Mercenary.Clear(); Dead.Clear(); Forbidden.Clear(); CallbackLeaders.Clear();
        _notices = _titleCallbacks = _reconciliations = 0; _notice = null; _noticeSponsor = _blocked = null;
        _factions = new FactionManagerBehavior(); _resolver = new CivilWarResolutionBehavior();
        _titles = new FeudalTitleBehavior(); _summary = new RebellionSummaryBehavior();
        AccessTools.PropertySetter(typeof(FeudalTitleBehavior), "Instance").Invoke(null, new object[] { _titles });
        _manager.Items.Clear();
        _manager.Items.AddRange(new CampaignBehaviorBase[] { _factions, _resolver, _titles, _summary, new WarScoreBehavior() });
        _parent = NewRealm("parent"); _crown = House("loyalist_crown"); Rulers[_parent] = _crown;
        _sponsor = House("original_sponsor", culture);
        _faction = new FactionObject("Independence", _parent, _sponsor, FactionType.Independence);
        Active.Add(_faction);
    }
    private static void Add(params Clan[] clans) => _faction.Members.AddRange(clans);
    private static string Accept() => Call(_faction, "AcceptDemands").ToString();
    private static bool TryAccept(out List<Kingdom> realms)
    {
        var args = new object[] { _faction, null };
        bool result = (bool)Call(_resolver, "TryAcceptIndependence", args);
        realms = (List<Kingdom>)args[1];
        return result;
    }
    private static void CheckSuccess(int realmCount, int clanCount)
    {
        _check(Created.Count == realmCount && Initialized.Count == realmCount && Gone.Count == 0,
            "Acceptance creates and initializes exactly the planned permanent realms");
        _check(Moves.Count == clanCount && Moves.Distinct().Count() == clanCount && _titleCallbacks == clanCount,
            "Every accepted clan moves once through the real membership and title callbacks");
        _check(Created.All(k => NobleClanEligibilityHelper.IsValidRulingClan(k.RulingClan, k)),
            "Every surviving realm has a living noble ruler who actually belongs to it");
        _check(_parent.RulingClan == _crown && _crown.Kingdom == _parent && !Gone.Contains(_parent),
            "Acceptance leaves the loyalist parent crown intact");
        _check(_faction.Leader == _sponsor && _noticeSponsor == _sponsor && _notices == 1,
            "Completed notice and faction identity retain the original sponsor");
        _check(Outcomes.Count == 1 && Outcomes.Single().Contains("|" + _sponsor.StringId + "|")
            && Outcomes.Single().EndsWith("|AcceptedUltimatum"),
            "The real yearly summary keys the accepted ultimatum to the original sponsor");
        _check(!Active.Contains(_faction) && !_faction.HasTrackedRebelKingdom,
            "Acceptance retires the political faction without creating a tracked civil war");
        _check(Forbidden.Count == 0, "Acceptance invokes no war, peace, rewards, or tribunal path");
    }

    internal static void Run(Action<bool, string> check)
    {
        _check = check;
        var harmony = new Harmony("bellum.tests.accepted_independence");
        var mod = typeof(FactionObject).Assembly;
        var previousTitles = FeudalTitleBehavior.Instance;
        _manager = new Manager(); _campaign = Blank<Campaign>(); _campaign.AddCampaignBehaviorManager(_manager);
        void Patch(MethodBase target, string prefix)
        {
            if (target == null) throw new Exception("Missing accepted-independence fixture target: " + prefix);
            harmony.Patch(target, prefix: new HarmonyMethod(typeof(AcceptedIndependenceTests), prefix));
        }
        void Getter(Type type, string property, string prefix) => Patch(AccessTools.PropertyGetter(type, property), prefix);
        try
        {
            Getter(typeof(Campaign), "Current", nameof(Current));
            Getter(typeof(CampaignTime), "Now", nameof(Now));
            Getter(typeof(Clan), "PlayerClan", nameof(Player));
            Getter(typeof(Clan), "Leader", nameof(Head));
            Getter(typeof(Clan), "Kingdom", nameof(Realm));
            Getter(typeof(Clan), "Culture", nameof(Culture));
            Getter(typeof(Clan), "Name", nameof(ClanName));
            Getter(typeof(Clan), "Influence", nameof(Influence));
            Getter(typeof(Clan), "All", nameof(AllClans));
            Getter(typeof(Clan), "IsEliminated", nameof(ClanGone));
            Getter(typeof(Clan), "IsMinorFaction", nameof(IsMinor));
            Getter(typeof(Clan), "IsUnderMercenaryService", nameof(IsMercenary));
            Getter(typeof(Hero), "IsDead", nameof(IsDead));
            Getter(typeof(BasicCultureObject), "Name", nameof(CultureName));
            Getter(typeof(Kingdom), "All", nameof(All));
            Getter(typeof(Kingdom), "Clans", nameof(Clans));
            Getter(typeof(Kingdom), "RulingClan", nameof(Ruler));
            Patch(AccessTools.PropertySetter(typeof(Kingdom), "RulingClan"), nameof(SetRuler));
            Getter(typeof(Kingdom), "Leader", nameof(RealmHead));
            Getter(typeof(Kingdom), "Name", nameof(RealmName));
            Getter(typeof(Kingdom), "Culture", nameof(RealmCulture));
            Getter(typeof(Kingdom), "IsEliminated", nameof(RealmGone));
            Getter(typeof(Kingdom), "UnresolvedDecisions", nameof(NoDecisions));
            Getter(typeof(Clan), "Settlements", nameof(NoSettlements));
            Getter(typeof(Kingdom), "Settlements", nameof(NoSettlements));
            Getter(typeof(Settlement), "All", nameof(NoSettlements));
            Patch(AccessTools.Method(typeof(BellumCivileLogger), "Log"), nameof(Skip));
            foreach (string name in new[] { "TrackPlayerCourtTransfer", "QueueSettlementRefresh", "RepairReferencedKingdomRulerState" })
                Patch(AccessTools.Method(typeof(FactionManagerBehavior), name), nameof(Skip));
            Patch(AccessTools.Method(typeof(FeudalTitleBehavior), "GetTitlesHeldByClan"), nameof(Held));
            Patch(AccessTools.Method(typeof(FeudalTitleBehavior), "RebuildRuntimeIndexes"), nameof(Skip));
            Patch(AccessTools.Method(typeof(FeudalTitleBehavior), "QueueServiceReviewForTitleAndChildren"), nameof(Skip));
            harmony.Patch(AccessTools.Method(typeof(FeudalTitleBehavior), "ReconcilePoliticalHierarchy"),
                postfix: new HarmonyMethod(typeof(AcceptedIndependenceTests), nameof(Reconciled)));
            Patch(AccessTools.Method(typeof(Kingdom), "CreateKingdom", new[] { typeof(string) }), nameof(Create));
            foreach (var method in typeof(Kingdom).GetMethods().Where(m => m.Name == "InitializeKingdom").Distinct())
                Patch(method, nameof(Initialize));
            Patch(AccessTools.Method(typeof(DestroyKingdomAction), "Apply"), nameof(Destroy));
            var visuals = mod.GetType("BellumCivile.KingdomVisualHelper", true);
            foreach (string name in new[] { "ResolveBreakawayKingdomVisuals", "ApplyKingdomPalette", "RestoreClanBanners", "ReapplyKingdomPaletteAfterClanChanges" })
                foreach (var method in AccessTools.GetDeclaredMethods(visuals).Where(m => m.Name == name))
                    Patch(method, nameof(Skip));
            Patch(AccessTools.Method(visuals, "CaptureClanBannersToPreserve"), nameof(Banners));
            Patch(AccessTools.Method(visuals, "ApplyJoinToKingdomWithBannerPolicy"), nameof(Join));
            Patch(AccessTools.Method(mod.GetType("BellumCivile.RebelPolicyHelper", true), "ApplyIndependencePolicyProfile"), nameof(Skip));
            Patch(AccessTools.Method(mod.GetType("BellumCivile.BellumCivileNotifications", true), "Show", new[] { typeof(TextObject), typeof(Color),
                typeof(Kingdom), typeof(Kingdom), typeof(Clan), typeof(Clan), typeof(bool), typeof(bool) }), nameof(Notice));
            foreach (var type in new[] { typeof(DeclareWarAction), typeof(MakePeaceAction) })
                foreach (var method in type.GetMethods().Where(m => m.Name.StartsWith("Apply"))) Patch(method, nameof(Trap));
            foreach (string name in new[] { "ResolveRebelVictory", "ResolveWhitePeace", "ApplyCivilWarPeaceIfNeeded",
                "ApplyCivilWarVictoryInfluenceRewards", "ApplyChallengeAwareVictoryRewards", "TryQueueChallengeTribunal",
                "CaptureOutcomeTribunal", "QueuePlayerTribunal", "QueueDeferredSuccessionTribunal" })
                Patch(AccessTools.Method(typeof(CivilWarResolutionBehavior), name), nameof(Trap));
            Patch(AccessTools.Method(typeof(WarScoreBehavior), "RegisterCivilWar"), nameof(Trap));

            RunCases(check);
        }
        finally
        {
            harmony.UnpatchAll(harmony.Id);
            AccessTools.PropertySetter(typeof(FeudalTitleBehavior), "Instance").Invoke(null, new object[] { previousTitles });
            _manager.Items.Clear();
            Realms.Clear(); Heads.Clear(); Cultures.Clear(); Rulers.Clear(); Initialized.Clear(); ExpectedSources.Clear();
            Kingdoms.Clear(); Created.Clear(); Moves.Clear(); Attempts.Clear(); Gone.Clear();
            Eliminated.Clear(); Minor.Clear(); Mercenary.Clear(); Dead.Clear(); Forbidden.Clear(); CallbackLeaders.Clear();
            _campaign = null; _check = null;
        }
    }

    // Compile native-getter assertions only after Harmony registration has completed.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void RunCases(Action<bool, string> check)
    {
        Reset();
        Title(_sponsor, FeudalTitleType.Duchy, "SponsorMarch");
        var follower = House("replacement_leader"); Add(follower); Title(follower, FeudalTitleType.County, "FollowerCounty");
        check(Accept() == "DemandsAccepted", "Two clans in one partition survive the membership callback");
        CheckSuccess(1, 2);
        check(CallbackLeaders[0] == follower && !_faction.Members.Contains(_sponsor)
            && follower.Kingdom == _sponsor.Kingdom && _sponsor.Kingdom.RulingClan == _sponsor,
            "Real callback replaces the faction leader, but the frozen sponsor still rules both departing clans");
        check(_notice.Contains(_sponsor.StringId + " Secessionists") && !_notice.Contains(follower.StringId + " Secessionists"),
            "Notice display name is frozen to the original sponsor after callback leader changes");

        Reset();
        var source = Title(_sponsor, FeudalTitleType.County, "Heartland");
        var historical = Title(_crown, FeudalTitleType.Empire, "HistoricalClaim"); historical.SetDeJureHolder(_sponsor.StringId);
        check(Accept() == "DemandsAccepted", "A single-clan accepted ultimatum executes the full AcceptDemands path");
        CheckSuccess(1, 1);
        var realm = _sponsor.Kingdom;
        check(_titles.GetIndependentRealmSovereignTitle(realm) == source && source.AssociatedKingdomId == realm.StringId,
            "The captured de facto county, not a higher historical legal claim, is the new sovereign title");
        check(Initialized[realm].Name.ToString() == "County of Heartland" && Initialized[realm].RulerTitle.ToString() == "Count",
            "The real source-title profile preserves county name and ruler rank");
        check(!Registry.ContainsKey(FeudalTitleBehavior.BuildKingdomTitleId(realm)),
            "The real title callback uses the registered source without creating a synthetic kingdom-tier crown");
        check(_notice.Contains("County of Heartland") && !_notice.Contains("HistoricalClaim"), "Single-realm notice names the actual independent realm");
        int count = Created.Count;
        check(Accept() == "Failed" && Created.Count == count && _notices == 1 && Outcomes.Count == 1,
            "A duplicate accepted callback cannot create another realm, notice, or summary");

        Reset();
        Title(_sponsor, FeudalTitleType.Duchy, "MixedMarch");
        var foreignCulture = House("sturgian_follower", "sturgia"); Add(foreignCulture);
        Title(foreignCulture, FeudalTitleType.County, "MixedCounty");
        check(Accept() == "DemandsAccepted", "Mixed-culture members accept independence through the same rank partition");
        CheckSuccess(1, 2);
        check(foreignCulture.Kingdom == _sponsor.Kingdom && Initialized[_sponsor.Kingdom].Culture == _sponsor.Culture,
            "Culture does not split a lower-rank follower from its sponsor or replace the founder's culture");

        Reset();
        var sponsorTitle = Title(_sponsor, FeudalTitleType.Duchy, "SponsorMarch");
        var peer = House("equal_duke", "sturgia"); var higher = House("higher_king", "empire");
        var lower = House("lower_house"); var intermediary = House("nonparticipant_liege");
        Add(peer, higher, lower);
        var higherTitle = Title(higher, FeudalTitleType.Kingdom, "HigherCrown");
        var peerTitle = Title(peer, FeudalTitleType.Duchy, "PeerMarch", higherTitle);
        var middle = Title(intermediary, FeudalTitleType.County, "MiddleCounty", peerTitle);
        Title(lower, FeudalTitleType.Barony, "LowerBarony", middle);
        check(Accept() == "DemandsAccepted", "Equal and higher de facto ranks form separate realms in full AcceptDemands");
        CheckSuccess(3, 4);
        check(new[] { _sponsor.Kingdom, peer.Kingdom, higher.Kingdom }.Distinct().Count() == 3
            && lower.Kingdom == peer.Kingdom && intermediary.Kingdom == _parent,
            "Lower house follows the nearest participating liege through a nonparticipating title parent");
        check(_titles.GetIndependentRealmSovereignTitle(_sponsor.Kingdom) == sponsorTitle
            && _titles.GetIndependentRealmSovereignTitle(peer.Kingdom) == peerTitle
            && _titles.GetIndependentRealmSovereignTitle(higher.Kingdom) == higherTitle,
            "Each partition retains its own captured source title");
        check(Created.All(k => _notice.Contains(Initialized[k].Name.ToString())) && _notice.Contains("these realms:"),
            "Multi-realm notice lists every newly independent realm");

        Reset("sturgia"); follower = House("landless_follower", "aserai"); Add(follower);
        check(Accept() == "DemandsAccepted", "Landless title-free clans can accept independence without a capital");
        CheckSuccess(1, 2);
        check(follower.Kingdom == _sponsor.Kingdom && Initialized[_sponsor.Kingdom].Capital == null
            && Initialized[_sponsor.Kingdom].Name.ToString() == "Principality of " + _sponsor.StringId
            && Initialized[_sponsor.Kingdom].RulerTitle.ToString() == "Prince",
            "Title-free fallback uses the sponsor's culture and clan name without requiring a settlement");

        Reset(); Title(_sponsor, FeudalTitleType.County, "EligibleCounty");
        var foreign = NewRealm("foreign"); var outsider = House("outsider", realm: foreign);
        var minor = House("minor"); Minor.Add(minor);
        var mercenary = House("mercenary"); Mercenary.Add(mercenary);
        var dead = House("dead_leader"); Dead.Add(Heads[dead]);
        var eliminated = House("eliminated"); Eliminated.Add(eliminated);
        var leaderless = House("leaderless"); Heads[leaderless] = null;
        follower = House("eligible_follower");
        Add(outsider, _crown, minor, mercenary, dead, eliminated, leaderless, follower, follower, _sponsor);
        check(TryAccept(out var accepted) && accepted.Count == 1, "Helper filters ineligible and duplicate political members before partitioning");
        check(Moves.Count == 2 && Attempts.Count == 2 && Moves.Contains(_sponsor) && Moves.Contains(follower),
            "Only distinct live noble parent vassals are transferred");
        check(outsider.Kingdom == foreign && new[] { _crown, minor, mercenary, dead, eliminated, leaderless }.All(c => c.Kingdom == _parent),
            "Foreign clans, parent ruler, minor, mercenary, dead, eliminated, and leaderless clans stay put");
        check(Forbidden.Count == 0, "Filtering does not invoke a civil-war settlement side effect");

        Reset(); Title(_sponsor, FeudalTitleType.County, "FreshCounty");
        var collision = NewRealm("parent_indep_original_sponsor"); var foreignRuler = House("foreign_ruler", realm: collision); Rulers[collision] = foreignRuler;
        var eliminatedCollision = NewRealm(collision.StringId + "_1"); Gone.Add(eliminatedCollision);
        check(Accept() == "DemandsAccepted", "Existing active and eliminated IDs do not prevent accepted independence");
        check(Created.Count == 1 && _sponsor.Kingdom.StringId == collision.StringId + "_2"
            && _sponsor.Kingdom != collision && collision.RulingClan == foreignRuler && foreignRuler.Kingdom == collision,
            "ID collision always allocates a fresh suffix and never reuses or usurps a foreign realm");

        Reset(); source = Title(_sponsor, FeudalTitleType.County, "BlockedCounty");
        source.SetAssociatedKingdom("prior_source_realm"); follower = House("blocked_follower"); Add(follower); _blocked = _sponsor;
        check(Accept() == "Failed", "Full AcceptDemands fails when the founder's native join does not complete");
        check(Created.Count == 1 && Gone.Contains(Created[0]) && Moves.Count == 0 && Attempts.SequenceEqual(new[] { _sponsor })
            && follower.Kingdom == _parent && _sponsor.Kingdom == _parent,
            "A blocked founder cannot strand followers or leave an empty live foreign-ruler realm");
        check(source.AssociatedKingdomId == "prior_source_realm" && !_titles.IsIndependentRealmShell(Created[0]),
            "Failed-founder cleanup unregisters the shell and restores the exact previous source realm association");
        check(_notices == 0 && Outcomes.Count == 0 && Active.Contains(_faction)
            && !(bool)AccessTools.Field(typeof(FactionObject), "_ultimatumResolved").GetValue(_faction) && Forbidden.Count == 0,
            "Failed acceptance remains retryable without reporting success, starting war, or settling a war");

        Reset(); source = Title(_sponsor, FeudalTitleType.Duchy, "LateBlockedMarch");
        follower = House("late_blocked_follower"); Add(follower);
        Title(follower, FeudalTitleType.County, "LateBlockedCounty", source); _blocked = follower;
        check(Accept() == "Failed", "Full AcceptDemands fails when a follower is blocked after its founder has joined");
        check(Created.Count == 1 && Gone.Count == 0 && Moves.SequenceEqual(new[] { _sponsor })
            && Attempts.SequenceEqual(new[] { _sponsor, follower }) && _sponsor.Kingdom == Created[0]
            && Created[0].RulingClan == _sponsor && NobleClanEligibilityHelper.IsValidRulingClan(_sponsor, Created[0]),
            "Late member failure preserves the populated root with its resident original founder, not a foreign ruler");
        check(_titles.GetIndependentRealmSovereignTitle(Created[0]) == source
            && source.AssociatedKingdomId == Created[0].StringId,
            "Late member failure retains the populated root's source title registration");
        check(Active.Contains(_faction) && _faction.Members.SequenceEqual(new[] { follower })
            && _faction.Leader == follower && follower.Kingdom == _parent
            && !(bool)AccessTools.Field(typeof(FactionObject), "_ultimatumResolved").GetValue(_faction),
            "The remaining political faction has a parent-resident leader and an unresolved ultimatum");
        check(_notices == 0 && Outcomes.Count == 0 && Forbidden.Count == 0
            && _parent.RulingClan == _crown && _crown.Kingdom == _parent,
            "Late member failure leaves the parent intact without a false notice, outcome, or war settlement");

        // Structural contract also detects the pre-fix inline, single-realm AcceptDemands implementation.
        var acceptMethod = AccessTools.Method(typeof(FactionObject), "AcceptDemands");
        check(PatchProcessor.GetOriginalInstructions(acceptMethod).Any(i => i.operand is MethodInfo m && m.Name == "TryAcceptIndependence"),
            "AcceptDemands delegates to the shared accepted-independence flow (pre-fix source contract)");
        var wrapper = AccessTools.Method(typeof(CivilWarResolutionBehavior), "BuildIndependencePartitionGroups");
        var acceptedMethod = AccessTools.Method(typeof(CivilWarResolutionBehavior), "TryAcceptIndependence");
        check(new[] { wrapper, acceptedMethod }.All(method => PatchProcessor.GetOriginalInstructions(method)
            .Any(i => i.operand is MethodInfo m && m.Name == "BuildIndependencePartitionGroupsForClans")),
            "The preserved postwar wrapper and accepted path share the partition planner");
    }
}
