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
using TaleWorlds.Library;

internal static class CollapseClaimantRecoveryTests
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
            if (IsLoading) { if (Values.TryGetValue(key, out var value)) data = (T)value; }
            else Values[key] = data;
            return true;
        }
    }
    private static readonly Dictionary<Clan, Kingdom> Realms = new Dictionary<Clan, Kingdom>();
    private static readonly Dictionary<Clan, Hero> Heads = new Dictionary<Clan, Hero>();
    private static readonly Dictionary<Kingdom, Clan> Rulers = new Dictionary<Kingdom, Clan>();
    private static readonly HashSet<Kingdom> Gone = new HashSet<Kingdom>(), BlockDestroy = new HashSet<Kingdom>();
    private static readonly HashSet<Hero> Dead = new HashSet<Hero>();
    private static FactionManagerBehavior _factions;
    private static SuccessionChallengeRecord _claim;
    private static FeudalTitleRecord _crown;
    private static Clan _blockedHouse;
    private static bool _blockTitle, _interruptTitle;
    private static Action<Clan> _duringMove;
    private static int _moves, _titles, _tribunals, _rewards, _published, _completed;
    private static T Blank<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    private static object Call(object owner, string name, params object[] args) => AccessTools.Method(owner.GetType(), name).Invoke(owner, args);
    private static bool Skip() => false;
    private static bool Yes(ref bool __result) { __result = true; return false; }
    private static bool No(ref bool __result) { __result = false; return false; }
    private static bool Player(ref Clan __result) { __result = null; return false; }
    private static bool Alive(Hero __instance, ref bool __result) { __result = !Dead.Contains(__instance); return false; }
    private static bool IsDead(Hero __instance, ref bool __result) { __result = Dead.Contains(__instance); return false; }
    private static bool Head(Clan __instance, ref Hero __result) { Heads.TryGetValue(__instance, out __result); return false; }
    private static bool Realm(Clan __instance, ref Kingdom __result) { Realms.TryGetValue(__instance, out __result); return false; }
    private static bool Clans(Kingdom __instance, ref MBReadOnlyList<Clan> __result)
    { __result = new MBReadOnlyList<Clan>(Realms.Where(p => p.Value == __instance).Select(p => p.Key).ToList()); return false; }
    private static bool AllClans(ref MBReadOnlyList<Clan> __result) { __result = new MBReadOnlyList<Clan>(Realms.Keys.ToList()); return false; }
    private static bool NoSettlements(ref MBReadOnlyList<Settlement> __result) { __result = new MBReadOnlyList<Settlement>(new List<Settlement>()); return false; }
    private static bool Holding(ref int __result) { __result = 0; return false; }
    private static bool Now(ref CampaignTime __result) { __result = CampaignTime.Zero; return false; }
    private static bool Ruler(Kingdom __instance, ref Clan __result) { Rulers.TryGetValue(__instance, out __result); return false; }
    private static bool SetRuler(Kingdom __instance, Clan value) { Rulers[__instance] = value; return false; }
    private static bool GoneRealm(Kingdom __instance, ref bool __result) { __result = Gone.Contains(__instance); return false; }
    private static bool Crown(ref FeudalTitleRecord __result) { __result = _crown; return false; }
    private static bool Join(Clan clan, Kingdom targetKingdom)
    {
        if (clan == _blockedHouse) return false;
        var source = Realms[clan]; Realms[clan] = targetKingdom; _moves++;
        // Exercise the real event handler that used to prune the winning coalition.
        Call(_factions, "OnClanChangedKingdom", clan, source, targetKingdom, ChangeKingdomAction.ChangeKingdomActionDetail.JoinKingdom, false);
        _duringMove?.Invoke(clan);
        return false;
    }
    private static bool Title(Kingdom kingdom, Clan rulerClan, bool legalTransfer, ref bool __result)
    {
        __result = false;
        if (_blockTitle) return false;
        _titles++; _crown.SetDeJureHolder(rulerClan.StringId); _crown.SetDeFactoHolder(rulerClan.StringId);
        _crown.SetAssociatedKingdom(kingdom.StringId); __result = true;
        if (_interruptTitle) throw new InvalidOperationException("interrupted after legal Crown delivery");
        return false;
    }
    private static bool Tribunal(ref bool __result) { if (!_claim.TribunalQueued) _tribunals++; _claim.TribunalQueued = __result = true; return false; }
    private static bool Rewards() { _rewards++; _claim.OutcomeRewardsApplied = true; return false; }
    private static bool Complete() { _completed++; _claim.ResolutionReturned = true; _claim.Phase = SuccessionChallengePhase.Settled; return false; }
    private static bool Publish() { _published++; return false; }
    private static bool Dispatcher(ref CampaignEventDispatcher __result) { __result = Blank<CampaignEventDispatcher>(); return false; }
    private static bool Destroy(Kingdom destroyedKingdom) { if (!BlockDestroy.Contains(destroyedKingdom)) Gone.Add(destroyedKingdom); return false; }

    internal static void Run(Action<bool, string> check)
    {
        var oldCampaign = Campaign.Current;
        var harmony = new Harmony("bellum.tests.collapse_claimant_recovery");
        var assembly = typeof(FactionObject).Assembly;
        var manager = new Manager(); var campaign = Blank<Campaign>(); campaign.AddCampaignBehaviorManager(manager);
        void Patch(MethodBase target, string prefix) => harmony.Patch(target, prefix: new HarmonyMethod(typeof(CollapseClaimantRecoveryTests), prefix));
        CivilWarResolutionBehavior resolver = null;
        Kingdom parent = null, shell = null, successor = null;
        Clan founder = null, backer = null;
        FactionObject faction = null;
        CivilWarCollapseRecord record = null;
        List<CivilWarCollapseRecord> Records() => (List<CivilWarCollapseRecord>)AccessTools.Field(typeof(CivilWarResolutionBehavior), "_collapses").GetValue(resolver);
        void Reset(int stage = 2, bool legacy = false)
        {
            Realms.Clear(); Heads.Clear(); Rulers.Clear(); Gone.Clear(); BlockDestroy.Clear(); Dead.Clear();
            _blockedHouse = null; _blockTitle = _interruptTitle = false;
            _duringMove = null;
            _moves = _titles = _tribunals = _rewards = _published = _completed = 0;
            resolver = new CivilWarResolutionBehavior(); _factions = new FactionManagerBehavior();
            var challenges = new SuccessionChallengeBehavior(); manager.Items.Clear();
            manager.Items.AddRange(new CampaignBehaviorBase[] { resolver, _factions, challenges, Blank<FeudalTitleBehavior>() });
            parent = Blank<Kingdom>(); parent.StringId = "rhodok";
            shell = Blank<Kingdom>(); shell.StringId = "rhodok_rebels_challenge_saved";
            successor = Blank<Kingdom>(); successor.StringId = "rhodok_restored_conflict_saved";
            Clan House(string id) { var c = Blank<Clan>(); c.StringId = id; Heads[c] = Blank<Hero>(); Heads[c].StringId = id + "_head"; Realms[c] = legacy ? successor : shell; return c; }
            founder = House("original_claimant"); backer = House("backer");
            Rulers[successor] = founder;
            faction = new FactionObject("Saved challenge", parent, founder, FactionType.InstallRuler);
            faction.Members.Add(backer); Call(faction, "SetRebelKingdom", shell);
            ((List<FactionObject>)AccessTools.Field(typeof(FactionManagerBehavior), "_activeFactions").GetValue(_factions)).Add(faction);
            _claim = new SuccessionChallengeRecord { Id = "saved", Realm = parent, WarShell = shell, WarFaction = faction,
                Challenger = Heads[founder], OutcomeVictor = founder, WarOutcome = SuccessionChallengeOutcome.Victory,
                Phase = SuccessionChallengePhase.ResolvingWar, OutcomeRealm = parent };
            ((List<SuccessionChallengeRecord>)AccessTools.Field(typeof(SuccessionChallengeBehavior), "_records").GetValue(challenges)).Add(_claim);
            _crown = new FeudalTitleRecord("old_crown", "Crown", FeudalTitleType.Kingdom, "former_ruler", founder.StringId,
                "", "", successor.StringId, 0, 0);
            record = new CivilWarCollapseRecord { Parent = parent, WinnerRealm = shell, Successor = successor,
                SuccessorId = successor.StringId, Winner = faction, WinnerHouse = founder, WinnerClans = new List<Clan> { founder, backer },
                Challenge = _claim, Stage = stage, Initialized = true, Conflict = new CivilWarConflictRecord() };
            Records().Add(record);
            if (legacy) { faction.Members.Clear(); faction.Leader = backer; Gone.Add(parent); Call(faction, "ClearRebelKingdom"); }
        }
        void Resume() => Call(resolver, "ResumePendingCollapses");
        try
        {
            AccessTools.PropertySetter(typeof(Campaign), "Current").Invoke(null, new object[] { campaign });
            Patch(AccessTools.PropertyGetter(typeof(CampaignTime), "Now"), nameof(Now));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "PlayerClan"), nameof(Player));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "Leader"), nameof(Head));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "Kingdom"), nameof(Realm));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "All"), nameof(AllClans));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "IsEliminated"), nameof(No));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "IsUnderMercenaryService"), nameof(No));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "IsMinorFaction"), nameof(No));
            Patch(AccessTools.PropertyGetter(typeof(Hero), "IsAlive"), nameof(Alive));
            Patch(AccessTools.PropertyGetter(typeof(Hero), "IsDead"), nameof(IsDead));
            Patch(AccessTools.PropertyGetter(typeof(Kingdom), "Clans"), nameof(Clans));
            Patch(AccessTools.PropertyGetter(typeof(Kingdom), "RulingClan"), nameof(Ruler));
            Patch(AccessTools.PropertySetter(typeof(Kingdom), "RulingClan"), nameof(SetRuler));
            Patch(AccessTools.PropertyGetter(typeof(Kingdom), "IsEliminated"), nameof(GoneRealm));
            Patch(AccessTools.PropertyGetter(typeof(Settlement), "All"), nameof(NoSettlements));
            Patch(AccessTools.Method(typeof(BellumCivileLogger), "Log"), nameof(Skip));
            foreach (string method in new[] { "TrackPlayerCourtTransfer", "QueueSettlementRefresh", "RepairReferencedKingdomRulerState" })
                Patch(AccessTools.Method(typeof(FactionManagerBehavior), method), nameof(Skip));
            Patch(AccessTools.Method(typeof(FactionManagerBehavior), "GetFactionByRebelKingdom"), nameof(Skip));
            Patch(AccessTools.Method(typeof(FactionObject), "CaptureCivilWarStartFiefCount", new[] { typeof(Clan) }), nameof(Skip));
            Patch(AccessTools.Method(typeof(FactionObject), "CaptureCivilWarStartInfluence", new[] { typeof(Clan) }), nameof(Skip));
            foreach (string method in new[] { "PreservePromotedRebelForeignWars", "CompleteCivilWarTracker", "CaptureOutcomeTribunal", "InheritExternalWars" })
                Patch(AccessTools.Method(typeof(CivilWarResolutionBehavior), method), nameof(Skip));
            Patch(AccessTools.Method(typeof(CivilWarResolutionBehavior), "ReleaseMercenariesFromKingdom"), nameof(Yes));
            Patch(AccessTools.Method(typeof(CivilWarResolutionBehavior), "CountStrongholds"), nameof(Holding));
            Patch(AccessTools.Method(typeof(CivilWarResolutionBehavior), "TryQueueChallengeTribunal"), nameof(Tribunal));
            Patch(AccessTools.Method(typeof(CivilWarResolutionBehavior), "ApplyChallengeAwareVictoryRewards"), nameof(Rewards));
            Patch(AccessTools.Method(typeof(SuccessionChallengeBehavior), "CompleteWarOutcome"), nameof(Complete));
            Patch(AccessTools.Method(typeof(FeudalTitleBehavior), "RegisterRestoredRealmMantle"), nameof(Skip));
            Patch(AccessTools.Method(typeof(FeudalTitleBehavior), "GetIndependentRealmSovereignTitle"), nameof(Crown));
            Patch(AccessTools.Method(typeof(FeudalTitleBehavior), "GetKingdomPoliticalTitle"), nameof(Crown));
            Patch(AccessTools.Method(typeof(FeudalTitleBehavior), "TrySetKingdomTitleRuler"), nameof(Title));
            Patch(AccessTools.Method(typeof(CampaignEventDispatcher), "OnKingdomCreated"), nameof(Publish));
            Patch(AccessTools.PropertyGetter(typeof(CampaignEventDispatcher), "Instance"), nameof(Dispatcher));
            Patch(AccessTools.Method(typeof(DestroyKingdomAction), "Apply"), nameof(Destroy));
            var visuals = assembly.GetType("BellumCivile.KingdomVisualHelper");
            Patch(AccessTools.Method(visuals, "ApplyJoinToKingdomPreservingCustomBanner"), nameof(Join));

            Reset(); _blockTitle = true; Resume();
            check(_moves == 2 && faction.Leader == founder && faction.Members.Count == 2,
                "Real clan-transfer callbacks preserve the captured winner and backers during an owned collapse");
            check(record.Stage == 3 && !record.Completed && !_claim.WarCrownTransferred && !Gone.Contains(shell),
                "A blocked legal Crown delivery retains the same stage-three settlement and predecessor");
            _blockTitle = false; Resume();
            check(record.Completed && _claim.EstateResolved && _claim.WarCrownTransferred && Gone.Contains(shell) && Gone.Contains(parent),
                "Settlement delivers the legal Crown and retires both empty predecessors");
            check(_titles == 1 && _tribunals == 1 && _rewards == 1 && _published == 1 && _completed == 1,
                "Crown, tribunal, rewards, publication and completion are delivered once");
            Resume(); check(_moves == 2 && _titles == 1 && _rewards == 1 && _published == 1,
                "Repeated hourly recovery cannot move households or grant rewards again");

            Reset(3, true); Resume();
            check(record.Completed && Gone.Contains(shell) && _moves == 0 && _titles == 1 && _claim.OutcomeVictor == founder,
                "Legacy stage-three recovery uses the saved victor even when the old coalition is empty and has another leader");
            Reset(3, true); _blockTitle = true;
            check(resolver.IsKingdomIdReferencedByBellumCivileState(shell.StringId)
                && resolver.IsKingdomIdReferencedByBellumCivileState(successor.StringId),
                "An unfinished collapse remains visible to diagnostics even after faction tracking disappears");
            foreach (string field in new[] { "association", "possession", "ruler", "elimination" })
            {
                Reset(3, true);
                if (field == "association") _crown.SetAssociatedKingdom("another_realm");
                else if (field == "possession") _crown.SetDeFactoHolder(backer.StringId);
                else if (field == "ruler") Rulers[successor] = backer;
                else Gone.Add(successor);
                Resume();
                check(!record.Completed && _titles == 0 && !Gone.Contains(shell),
                    "Uncommitted recovery refuses a changed successor Crown: " + field);
            }
            Reset(3, true); _interruptTitle = true; Resume();
            check(!record.Completed && !_claim.WarCrownTransferred && _crown.DeJureHolderClanId == founder.StringId,
                "A listener interruption after legal delivery leaves recoverable actual Crown ownership");
            _interruptTitle = false; Dead.Add(_claim.Challenger); Heads[founder] = Blank<Hero>(); Resume();
            check(record.Completed && _claim.WarCrownTransferred && _titles == 1,
                "Verified Crown delivery survives a later death without repeating the legal transfer");

            Reset(3, true); Dead.Add(_claim.Challenger); Resume();
            check(!record.Completed && _titles == 0 && !Gone.Contains(shell), "An uncrowned dead claimant is not replaced or awarded victory");
            Reset();
            _duringMove = moved => { if (moved == founder) { Dead.Add(_claim.Challenger); Heads[founder] = Blank<Hero>(); } };
            Resume();
            check(record.Stage == 3 && !record.Completed && _titles == 0 && !Gone.Contains(shell),
                "A claimant dying inside a transfer callback is rechecked before legal Crown delivery");
            Reset(3, true); _claim.OutcomeVictor = backer; Resume();
            check(!record.Completed && _titles == 0, "Conflicting saved victor identities fail closed");
            Reset(3, true); Realms[founder] = Blank<Kingdom>(); Resume();
            check(!record.Completed && _titles == 0, "An uncommitted winner defecting to an unrelated realm cannot take the Crown");
            Reset(3, true); _claim.WarCrownTransferred = _claim.EstateResolved = true; Heads[founder] = Blank<Hero>();
            Rulers[successor] = backer; _crown.SetDeFactoHolder(backer.StringId); _crown.SetDeJureHolder(backer.StringId); Resume();
            check(record.Completed && Rulers[successor] == backer && _titles == 0,
                "Historical Crown receipts permit retirement without replacing a later reigning house");

            Reset(); _blockedHouse = backer; Resume();
            check(record.Stage == 2 && Realms[founder] == successor && Realms[backer] == shell && faction.Leader == founder,
                "An interrupted household move keeps its source, winner and coalition snapshot");
            var saved = new Store(); resolver.SyncData(saved); saved.IsLoading = true;
            manager.Items.Remove(resolver); resolver = new CivilWarResolutionBehavior(); resolver.SyncData(saved); manager.Items.Add(resolver);
            _blockedHouse = null; BlockDestroy.Add(shell); Resume();
            check(!record.Completed && record.Stage == 3 && _claim.WarCrownTransferred && !Gone.Contains(shell),
                "A restored journal retains a blocked predecessor retirement after legal delivery");
            BlockDestroy.Clear(); Resume();
            check(record.Completed && _moves == 2 && _titles == 1 && _tribunals == 1,
                "Save/load recovery finishes the same realm and Crown without replaying transfers");

            Reset(); var foreign = Blank<Kingdom>(); Realms[founder] = foreign;
            Call(_factions, "OnClanChangedKingdom", founder, shell, foreign, ChangeKingdomAction.ChangeKingdomActionDetail.JoinKingdom, false);
            check(!faction.Members.Contains(founder), "Ordinary external defections are not hidden by the owned-transfer guard");
        }
        finally
        {
            harmony.UnpatchAll(harmony.Id);
            AccessTools.PropertySetter(typeof(Campaign), "Current").Invoke(null, new object[] { oldCampaign });
        }
    }
}
