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
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.Election;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Party.PartyComponents;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.Siege;
using TaleWorlds.Library;
using TaleWorlds.Localization;

internal static class LandlessRealmTests
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

    private static Campaign _campaign;
    private static Clan _player, _house, _blockedHouse;
    private static Kingdom _source, _enemy, _veto, _blockedMercenaries;
    private static FeudalTitleRecord _crown, _root;
    private static CivilWarResolutionBehavior _recovery;
    private static ExiledClanRecoveryBehavior _exiles;
    private static readonly List<Kingdom> Realms = new List<Kingdom>();
    private static readonly Dictionary<Clan, Kingdom> Membership = new Dictionary<Clan, Kingdom>();
    private static readonly Dictionary<Clan, Hero> Heads = new Dictionary<Clan, Hero>();
    private static readonly Dictionary<Kingdom, Clan> Rulers = new Dictionary<Kingdom, Clan>();
    private static readonly Dictionary<Settlement, Clan> Owners = new Dictionary<Settlement, Clan>();
    private static readonly Dictionary<Town, Settlement> Towns = new Dictionary<Town, Settlement>();
    private static readonly List<FeudalTitleRecord> Titles = new List<FeudalTitleRecord>();
    private static readonly HashSet<Kingdom> Gone = new HashSet<Kingdom>(), Stale = new HashSet<Kingdom>(), Protected = new HashSet<Kingdom>();
    private static readonly List<string> Departures = new List<string>(), Logs = new List<string>();
    private static Action _afterMercenaries;
    private static bool _blockDestroy;
    private static bool _battle, _siege;
    private static MobileParty _party;
    private static WarPartyComponent _component;
    private static int _created, _retired;
    private static T Blank<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    private static object Call(object owner, string method, params object[] args)
        => AccessTools.Method(owner.GetType(), method).Invoke(owner, args);
    private static bool Current(ref Campaign __result) { __result = _campaign; return false; }
    private static bool Player(ref Clan __result) { __result = _player; return false; }
    private static bool AllRealms(ref MBReadOnlyList<Kingdom> __result) { __result = new MBReadOnlyList<Kingdom>(Realms); return false; }
    private static bool AllClans(ref MBReadOnlyList<Clan> __result) { __result = new MBReadOnlyList<Clan>(Membership.Keys.ToList()); return false; }
    private static bool AllSettlements(ref MBReadOnlyList<Settlement> __result) { __result = new MBReadOnlyList<Settlement>(Owners.Keys.ToList()); return false; }
    private static bool Clans(Kingdom __instance, ref MBReadOnlyList<Clan> __result)
    { __result = new MBReadOnlyList<Clan>(Membership.Where(p => p.Value == __instance).Select(p => p.Key).ToList()); return false; }
    private static bool Realm(Clan __instance, ref Kingdom __result) { Membership.TryGetValue(__instance, out __result); return false; }
    private static bool Ruler(Kingdom __instance, ref Clan __result) { Rulers.TryGetValue(__instance, out __result); return false; }
    private static bool SetRuler(Kingdom __instance, Clan value) { Rulers[__instance] = value; return false; }
    private static bool Head(Clan __instance, ref Hero __result) { Heads.TryGetValue(__instance, out __result); return false; }
    private static bool Heroes(Clan __instance, ref MBReadOnlyList<Hero> __result)
    { __result = new MBReadOnlyList<Hero>(new List<Hero> { Heads[__instance] }); return false; }
    private static bool ClanSettlements(Clan __instance, ref MBReadOnlyList<Settlement> __result)
    { __result = new MBReadOnlyList<Settlement>(Owners.Where(p => p.Value == __instance).Select(p => p.Key).ToList()); return false; }
    private static bool RealmSettlements(Kingdom __instance, ref MBReadOnlyList<Settlement> __result)
    { __result = new MBReadOnlyList<Settlement>(Stale.Contains(__instance) ? new List<Settlement>()
        : Owners.Where(p => Membership[p.Value] == __instance).Select(p => p.Key).ToList()); return false; }
    private static bool ClanFiefs(Clan __instance, ref MBReadOnlyList<Town> __result)
    { __result = new MBReadOnlyList<Town>(Towns.Where(p => Owners[p.Value] == __instance).Select(p => p.Key).ToList()); return false; }
    private static bool RealmFiefs(Kingdom __instance, ref MBReadOnlyList<Town> __result)
    { __result = new MBReadOnlyList<Town>(Stale.Contains(__instance) ? new List<Town>()
        : Towns.Where(p => Membership[Owners[p.Value]] == __instance).Select(p => p.Key).ToList()); return false; }
    private static bool Owner(Settlement __instance, ref Clan __result) { Owners.TryGetValue(__instance, out __result); return false; }
    private static bool TownSettlement(Town __instance, ref Settlement __result) { Towns.TryGetValue(__instance, out __result); return false; }
    private static bool Castle(Settlement __instance, ref bool __result) { __result = Towns.Values.Contains(__instance); return false; }
    private static bool Eliminated(Kingdom __instance, ref bool __result) { __result = Gone.Contains(__instance); return false; }
    private static bool Name(Clan __instance, ref TextObject __result) { __result = new TextObject(__instance.StringId); return false; }
    private static bool No(ref bool __result) { __result = false; return false; }
    private static bool Yes(ref bool __result) { __result = true; return false; }
    private static bool Now(ref CampaignTime __result) { __result = CampaignTime.Zero; return false; }
    private static bool Parties(Clan __instance, ref MBReadOnlyList<WarPartyComponent> __result)
    { __result = new MBReadOnlyList<WarPartyComponent>(__instance == _house && (_battle || _siege)
        ? new List<WarPartyComponent> { _component } : new List<WarPartyComponent>()); return false; }
    private static bool Party(ref MobileParty __result) { __result = _party; return false; }
    private static bool Battle(ref MapEvent __result) { __result = _battle ? Blank<MapEvent>() : null; return false; }
    private static bool Siege(ref SiegeEvent __result) { __result = _siege ? Blank<SiegeEvent>() : null; return false; }
    private static bool Decisions(ref MBReadOnlyList<KingdomDecision> __result)
    { __result = new MBReadOnlyList<KingdomDecision>(new List<KingdomDecision>()); return false; }
    private static bool Crown(ref FeudalTitleRecord __result) { __result = _crown; return false; }
    private static bool Title(string titleId, ref FeudalTitleRecord __result)
    { __result = Titles.FirstOrDefault(t => t.TitleId == titleId); return false; }
    private static bool Held(Clan clan, bool deJure, ref IReadOnlyCollection<FeudalTitleRecord> __result)
    { __result = Titles.Where(t => (deJure ? t.DeJureHolderClanId : t.DeFactoHolderClanId) == clan.StringId).ToList(); return false; }
    private static bool Cluster(FeudalTitleRecord rootTitle, ref IReadOnlyCollection<FeudalTitleRecord> __result)
    { __result = Titles.Where(t => t == rootTitle || t.DeFactoParentTitleId == rootTitle.TitleId).ToList(); return false; }
    private static bool Cost(IEnumerable<Settlement> settlements, ref int __result) { __result = settlements.Count() * 10; return false; }
    private static bool Create(ref Kingdom __result) { _created++; __result = null; return false; }
    private static bool Pending(Kingdom realm, ref bool __result) { __result = Protected.Contains(realm); return false; }
    private static bool NativeAllowed(Kingdom kingdom, ref bool __result)
    { __result = kingdom != _veto && kingdom != Membership[_player] && !Gone.Contains(kingdom) && !kingdom.Settlements.Any(); return false; }
    private static void Retiring() => _retired++;
    private static bool Release(Kingdom kingdom, ref bool __result)
    { var action = _afterMercenaries; _afterMercenaries = null; action?.Invoke(); __result = kingdom != _blockedMercenaries; return false; }
    private static bool Leave(Clan clan, ChangeKingdomAction.ChangeKingdomActionDetail detail)
    {
        if (detail != ChangeKingdomAction.ChangeKingdomActionDetail.LeaveByKingdomDestruction)
            throw new Exception("Unexpected native departure type: " + detail);
        if (clan == _blockedHouse) return false;
        var previous = Membership[clan]; Membership[clan] = null;
        Departures.Add(clan.StringId);
        Call(_exiles, "OnClanChangedKingdom", clan, previous, null,
            ChangeKingdomAction.ChangeKingdomActionDetail.LeaveByKingdomDestruction, false);
        return false;
    }
    private static bool Destroy(Kingdom __0) { if (!_blockDestroy) Gone.Add(__0); return false; }
    private static bool Log(string message) { Logs.Add(message); return false; }

    internal static void Run(Action<bool, string> check, bool nativeRetirementOnly = false)
    {
        var harmony = new Harmony("bellum.tests.landless_realms");
        var manager = new Manager();
        var assembly = typeof(FeudalTitleBehavior).Assembly;
        var service = assembly.GetType("BellumCivile.TreatyRealmTransitionService");
        var transition = assembly.GetType("BellumCivile.KingdomCreationSafetyHelper");
        void Patch(MethodBase target, string prefix) => harmony.Patch(target, prefix: new HarmonyMethod(typeof(LandlessRealmTests), prefix));
        void Getter(Type type, string name, string prefix)
        {
            var getter = AccessTools.PropertyGetter(type, name);
            Patch(AccessTools.DeclaredPropertyGetter(getter.DeclaringType, name), prefix);
        }
        Kingdom AddRealm(string id) { var k = Blank<Kingdom>(); k.StringId = id; Realms.Add(k); return k; }
        Clan House(string id, Kingdom realm)
        { var c = Blank<Clan>(); c.StringId = id; Membership[c] = realm; var h = Blank<Hero>(); h.StringId = id + "_head"; Heads[c] = h; return c; }
        Settlement Fief(string id, Clan owner, bool barony = true)
        {
            var s = Blank<Settlement>(); s.StringId = id; Owners[s] = owner;
            var town = Blank<Town>(); s.Town = town; Towns[town] = s;
            if (barony) Titles.Add(new FeudalTitleRecord("barony_" + id, id, FeudalTitleType.Barony,
                owner.StringId, owner.StringId, _root.TitleId, id, _source.StringId, 0, 0));
            return s;
        }
        void Reset()
        {
            Realms.Clear(); Membership.Clear(); Heads.Clear(); Rulers.Clear(); Owners.Clear(); Towns.Clear(); Titles.Clear();
            Gone.Clear(); Stale.Clear(); Protected.Clear(); Departures.Clear(); Logs.Clear();
            _created = _retired = 0; _veto = _blockedMercenaries = null; _afterMercenaries = null; _blockDestroy = false;
            _blockedHouse = null;
            _battle = _siege = false; _party = Blank<MobileParty>(); _component = Blank<LordPartyComponent>();
            _source = AddRealm("source"); _enemy = AddRealm("enemy"); _player = House("player", _enemy);
            var ruler = House("crown", _source); _house = House("count", _source); Rulers[_source] = ruler; Rulers[_enemy] = _player;
            _crown = new FeudalTitleRecord("crown", "Crown", FeudalTitleType.Kingdom, ruler.StringId, ruler.StringId, "", "", _source.StringId, 0, 0);
            _root = new FeudalTitleRecord("county", "County", FeudalTitleType.County, _house.StringId, _house.StringId, "", "", _source.StringId, 0, 0);
            _root.SetDeFactoParentTitle(_crown.TitleId); Titles.Add(_crown); Titles.Add(_root);
            _recovery = new CivilWarResolutionBehavior(); _exiles = new ExiledClanRecoveryBehavior();
            manager.Items.Clear(); manager.Items.AddRange(new CampaignBehaviorBase[] { _recovery, _exiles,
                new FeudalTitleBehavior(), new FactionDiscontinuationCampaignBehavior() });
        }
        IList Candidates(WarScoreRecord war = null, IEnumerable<TreatyTermRecord> terms = null)
            => (IList)AccessTools.Method(service, "GetReleaseCandidates").Invoke(null, new object[] { _source, war, terms });
        WarScoreRecord War(Settlement fief, Clan original) => new WarScoreRecord("enemy|source", _enemy.StringId, _source.StringId, 0,
            new[] { new WarScoreFiefSnapshotRecord(fief.StringId, Membership[original].StringId, original.StringId, false, true) });
        void Recover() => Call(_recovery, "RecoverLandlessRealms");
        try
        {
            _campaign = Blank<Campaign>(); _campaign.AddCampaignBehaviorManager(manager);
            Getter(typeof(Campaign), "Current", nameof(Current)); Getter(typeof(Clan), "PlayerClan", nameof(Player));
            Getter(typeof(Kingdom), "All", nameof(AllRealms)); Getter(typeof(Clan), "All", nameof(AllClans));
            Getter(typeof(Kingdom), "UnresolvedDecisions", nameof(Decisions));
            Getter(typeof(Settlement), "All", nameof(AllSettlements)); Getter(typeof(Kingdom), "Clans", nameof(Clans));
            Getter(typeof(Clan), "Kingdom", nameof(Realm)); Getter(typeof(Clan), "Leader", nameof(Head));
            Getter(typeof(Clan), "Heroes", nameof(Heroes)); Getter(typeof(Clan), "Name", nameof(Name));
            Getter(typeof(Clan), "Fiefs", nameof(ClanFiefs)); Getter(typeof(Kingdom), "Fiefs", nameof(RealmFiefs));
            Getter(typeof(Clan), "Settlements", nameof(ClanSettlements)); Getter(typeof(Kingdom), "Settlements", nameof(RealmSettlements));
            Getter(typeof(Kingdom), "RulingClan", nameof(Ruler)); Patch(AccessTools.PropertySetter(typeof(Kingdom), "RulingClan"), nameof(SetRuler));
            Getter(typeof(Settlement), "OwnerClan", nameof(Owner)); Getter(typeof(Settlement), "IsCastle", nameof(Castle));
            Getter(typeof(Settlement), "IsTown", nameof(No)); Getter(typeof(Town), "Settlement", nameof(TownSettlement));
            Getter(typeof(Kingdom), "IsEliminated", nameof(Eliminated)); Getter(typeof(Clan), "IsEliminated", nameof(No));
            Getter(typeof(Clan), "IsUnderMercenaryService", nameof(No)); Getter(typeof(Clan), "IsMinorFaction", nameof(No));
            Getter(typeof(Clan), "IsNoble", nameof(Yes)); Getter(typeof(Hero), "IsAlive", nameof(Yes));
            Getter(typeof(Hero), "IsDisabled", nameof(No)); Getter(typeof(Hero), "IsChild", nameof(No));
            Getter(typeof(Clan), "WarPartyComponents", nameof(Parties)); Getter(typeof(CampaignTime), "Now", nameof(Now));
            Getter(typeof(WarPartyComponent), "MobileParty", nameof(Party));
            Getter(typeof(MobileParty), "MapEvent", nameof(Battle)); Getter(typeof(MobileParty), "SiegeEvent", nameof(Siege));
            Patch(AccessTools.Method(typeof(FeudalTitleBehavior), "GetRealmSovereignTitle", new[] { typeof(Kingdom), typeof(FeudalHierarchyMode) }), nameof(Crown));
            Patch(AccessTools.Method(typeof(FeudalTitleBehavior), "GetTitle"), nameof(Title));
            Patch(AccessTools.Method(typeof(FeudalTitleBehavior), "GetTitlesHeldByClan"), nameof(Held));
            Patch(AccessTools.Method(typeof(FeudalTitleBehavior), "GetTitleAndDescendants", new[] { typeof(FeudalTitleRecord), typeof(FeudalHierarchyMode) }), nameof(Cluster));
            Patch(AccessTools.Method(assembly.GetType("BellumCivile.TreatyTermCostModel"), "GetStructuralFiefWarScoreCost"), nameof(Cost));
            Patch(AccessTools.Method(transition, "CreateKingdom"), nameof(Create));
            Patch(AccessTools.Method(typeof(CivilWarConflictBehavior), "IsOtherRealmTransferPending"), nameof(Pending));
            Patch(AccessTools.Method(typeof(FactionDiscontinuationCampaignBehavior), "CanKingdomBeDiscontinued"), nameof(NativeAllowed));
            harmony.Patch(AccessTools.Method(typeof(CivilWarResolutionBehavior), "RetireLandlessRealm"),
                prefix: new HarmonyMethod(typeof(LandlessRealmTests), nameof(Retiring)));
            Patch(AccessTools.Method(typeof(CivilWarResolutionBehavior), "ReleaseMercenariesFromKingdom"), nameof(Release));
            Patch(AccessTools.Method(typeof(ChangeKingdomAction), "ApplyInternal"), nameof(Leave));
            Patch(AccessTools.Method(typeof(DestroyKingdomAction), "ApplyInternal"), nameof(Destroy));
            Patch(AccessTools.Method(typeof(BellumCivileLogger), "Log"), nameof(Log));

            // The stable build's kingdom-creation signature differs from beta. The isolated
            // native retirement checks can run against either installation without invoking it.
            if (!nativeRetirementOnly)
            {
                Reset(); check(Candidates().Count == 0, "a county with no actual baronies cannot be released");
                AccessTools.Method(service, "TryReleaseVassal").Invoke(null, new object[] { _source, _house, _root, null, null });
                check(_created == 0, "execution rejects a landless candidate before creating a realm");
                var fief = Fief("castle", _house); var candidate = Candidates().Cast<object>().Single();
                check((int)AccessTools.Property(candidate.GetType(), "FiefCount").GetValue(candidate) == 1, "a county backed by its holder's castle can be released");
                Owners[fief] = _player;
                check(Candidates().Count == 0, "a stale barony title does not count enemy-owned territory");
                Owners[fief] = _house; var other = Fief("outside", _house, false);
                check(Candidates().Count == 0, "a house cannot take unrelated untitled territory along with a released county");
                Owners.Remove(other); Towns.Remove(other.Town);
                var term = new TreatyTermRecord(TreatyTermType.TransferFief, 10, fief.StringId, fromKingdomId: _source.StringId, toKingdomId: _enemy.StringId);
                check(Candidates(null, new[] { term }).Count == 0, "ceding the sole backing castle invalidates a release before execution");
                check(Candidates(War(fief, _player)).Count == 0, "an enemy occupation due to return at peace cannot back independence");
                check(Candidates(War(fief, _house)).Count == 1, "a castle remaining with its original house after peace still qualifies");
                other = Fief("retained", _house);
                candidate = Candidates(War(fief, _player)).Cast<object>().Single();
                check((int)AccessTools.Property(candidate.GetType(), "FiefCount").GetValue(candidate) == 1
                    && (int)AccessTools.Property(candidate.GetType(), "WarScoreCost").GetValue(candidate) == 10,
                    "release cost counts only territory the new realm actually retains after peace");
                Owners.Remove(other); Towns.Remove(other.Town); Titles.RemoveAll(t => t.CapitalSettlementId == other.StringId);
                var subordinate = House("baron", _source); Owners[fief] = subordinate;
                Titles.Single(t => t.TitleType == FeudalTitleType.Barony).SetDeFactoHolder(subordinate.StringId);
                check(Candidates().Count == 1, "a landless count can be released with a genuinely landed subordinate baron");
                Owners[fief] = _player;
                AccessTools.Method(service, "TryReleaseVassal").Invoke(null, new object[] { _source, _house, _root, null, null });
                check(_created == 0, "execution revalidates actual land lost since drafting");
            }

            Reset(); Recover();
            if (!Gone.Contains(_source)) throw new Exception(string.Join(Environment.NewLine, Logs));
            check(Gone.Contains(_source) && Departures.Count == 2 && _retired == 1, "a born-landless NPC realm retires through the native lifecycle");
            var causes = (Dictionary<string, int>)AccessTools.Field(typeof(ExiledClanRecoveryBehavior), "_trackedExileCauseIds").GetValue(_exiles);
            check(causes.Count == 2 && causes.Values.All(c => (ExileCause)c == ExileCause.KingdomDestroyed), "native departures preserve surviving houses as kingdom-destruction exiles");
            Recover(); check(_retired == 1 && Departures.Count == 2, "repeated sweeps do not retire a realm or exile a house twice");
            Reset(); Membership[_player] = _source; Recover();
            check(!Gone.Contains(_source) && !Departures.Contains("player"), "the player's current realm retains its vanilla exemption");
            Reset(); Fief("safe", _house); Recover(); check(!Gone.Contains(_source), "a landed realm is never retired");
            Stale.Add(_source); Recover(); check(!Gone.Contains(_source), "actual settlement ownership overrides a stale empty realm cache");
            Reset(); _source.StringId = "source_rebels_house"; Recover(); check(!Gone.Contains(_source), "temporary civil-war shells remain on their own lifecycle");
            Reset(); var factions = new FactionManagerBehavior(); manager.Items.Add(factions);
            var rebellion = new FactionObject("Claimants", _source, _house, FactionType.InstallRuler);
            rebellion.SetRebelKingdom(AddRealm("source_rebels_count"));
            ((List<FactionObject>)AccessTools.Field(typeof(FactionManagerBehavior), "_activeFactions").GetValue(factions)).Add(rebellion);
            Recover(); check(!Gone.Contains(_source), "a landless civil-war parent stays with the civil-war resolution system");
            Reset(); Protected.Add(_source); Recover(); check(!Gone.Contains(_source), "a pending realm transfer defers retirement");
            Protected.Clear(); Recover(); check(Gone.Contains(_source), "a deferred realm is retried once its transition completes");
            Reset();
            var pending = (Dictionary<string, string>)AccessTools.Field(typeof(CivilWarResolutionBehavior), "_pendingSuccessionCandidates").GetValue(_recovery);
            pending[_source.StringId] = _house.StringId; Recover();
            check(!Gone.Contains(_source), "pending Crown elections defer retirement");
            pending.Clear();
            var restorations = (List<RestoredRealmCreationRecord>)AccessTools.Field(typeof(CivilWarResolutionBehavior), "_restoredCreations").GetValue(_recovery);
            restorations.Add(new RestoredRealmCreationRecord { Parent = _source }); Recover();
            check(!Gone.Contains(_source), "a saved unfinished restoration protects its parent realm");
            Reset(); var scores = new WarScoreBehavior(); manager.Items.Add(scores);
            var war = new WarScoreRecord("enemy|source", _enemy.StringId, _source.StringId, 0, null);
            war.BeginParley(true, 0);
            ((List<WarScoreRecord>)AccessTools.Field(typeof(WarScoreBehavior), "_wars").GetValue(scores)).Add(war);
            Recover(); check(!Gone.Contains(_source), "an open peace parley is allowed to finish before cleanup");
            war.EndParley(); Recover(); check(Gone.Contains(_source), "ordinary landless recovery resumes after a parley closes");
            Reset(); var feuds = new ClaimFeudWarBehavior(); manager.Items.Add(feuds);
            ((List<ClaimFeudWarRecord>)AccessTools.Field(typeof(ClaimFeudWarBehavior), "_wars").GetValue(feuds))
                .Add(new ClaimFeudWarRecord("feud", "claim", _source.StringId, "a", "b", "title", "a", "b", "a", "b", "", "", 0));
            Recover(); check(!Gone.Contains(_source), "private-war parent realms wait for their dedicated resolution path");
            Reset(); _battle = true; Recover(); check(!Gone.Contains(_source) && Departures.Count == 0, "retirement never finalizes a clan party's live battle");
            _battle = false; _siege = true; Recover(); check(!Gone.Contains(_source), "retirement never finalizes a clan party's live siege");
            _siege = false; Recover(); check(Gone.Contains(_source), "retirement retries after battle and siege activity ends");
            Reset(); _veto = _source; Recover(); Recover();
            check(!Gone.Contains(_source) && Logs.Count(l => l.Contains("vetoed")) == 1, "native vetoes are respected and identical deferrals are logged only once");
            Reset(); _blockedMercenaries = _source; Recover(); check(!Gone.Contains(_source) && Departures.Count == 0, "blocked mercenary dismissal leaves the realm and its houses intact");
            _blockedMercenaries = null; Recover(); check(Gone.Contains(_source), "mercenary dismissal can be retried safely");
            Reset(); _blockedHouse = _house; Recover(); Recover();
            check(!Gone.Contains(_source) && Departures.Count == 0 && Membership[Rulers[_source]] == _source,
                "a blocked clan departure cannot destroy that clan or remove its reigning house");
            check(Logs.Count(l => l.Contains("clan departure is blocked")) == 1, "unchanged blocked-departure diagnostics are not repeated each day");
            _blockedHouse = null; Recover(); check(Gone.Contains(_source), "a blocked noble departure is retried before retirement");
            Reset(); _blockedHouse = Rulers[_source]; Recover();
            check(!Gone.Contains(_source) && Departures.SequenceEqual(new[] { _house.StringId }) && Membership[_blockedHouse] == _source,
                "the reigning house leaves last and remains in charge if its own departure is blocked");
            _blockedHouse = null; Recover(); check(Gone.Contains(_source) && Departures.Count == 2, "retry does not repeat departures completed before a blocked ruling house");
            Reset(); _afterMercenaries = () => Fief("regained", _house); Recover();
            check(!Gone.Contains(_source) && Departures.Count == 0, "a realm regaining land during departure callbacks is revalidated");
            Reset(); _blockDestroy = true; Recover(); check(!Gone.Contains(_source) && Departures.Count == 2, "interrupted native destruction does not delete surviving houses");
            _blockDestroy = false; Recover(); check(Gone.Contains(_source) && Departures.Count == 2, "partially completed retirement resumes without replaying exile departures");
            Reset(); manager.RemoveBehavior<ExiledClanRecoveryBehavior>(); Recover();
            check(!Gone.Contains(_source) && Departures.Count == 0, "cleanup does not strand houses without Bellum's exile recovery behavior");
            Reset();
            Call(_recovery, "OnDailyTick");
            var scheduled = AccessTools.Field(typeof(CivilWarResolutionBehavior), "_landlessRecoveryPending");
            check((bool)scheduled.GetValue(_recovery) && !Gone.Contains(_source), "the daily event schedules rather than mutates realms inside another event dispatch");
            Call(_recovery, "ProcessLandlessRealmRecovery");
            check((bool)scheduled.GetValue(_recovery) && !Gone.Contains(_source), "scheduled recovery waits while the campaign map is unavailable");
        }
        finally { harmony.UnpatchAll(harmony.Id); }
    }
}
