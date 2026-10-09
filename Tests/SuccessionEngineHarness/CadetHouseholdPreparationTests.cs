using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using BellumCivile;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Library;

internal static class CadetHouseholdPreparationTests
{
    private sealed class Manager : ICampaignBehaviorManager
    {
        public T GetBehavior<T>() => default;
        public IEnumerable<T> GetBehaviors<T>() => Enumerable.Empty<T>();
        public void AddBehavior(CampaignBehaviorBase b) { }
        public void RemoveBehavior<T>() where T : CampaignBehaviorBase { }
        public void ClearBehaviors() { }
        public void InitializeCampaignBehaviors(IEnumerable<CampaignBehaviorBase> b) { }
        public void LoadBehaviorData() { }
        public void RegisterEvents() { }
    }

    private static readonly Dictionary<string, Hero> Heroes = new Dictionary<string, Hero>();
    private static readonly Dictionary<Hero, Clan> Houses = new Dictionary<Hero, Clan>();
    private static readonly Dictionary<Clan, Hero> Heads = new Dictionary<Clan, Hero>();
    private static readonly HashSet<Hero> Dead = new HashSet<Hero>();
    private static readonly HashSet<Hero> Captive = new HashSet<Hero>();
    private static readonly List<Hero> Transfers = new List<Hero>();
    private static readonly List<string> Logs = new List<string>();
    private static Clan _source, _cadet, _foreign;
    private static Hero _heir, _spouse, _child, _sourceHead, _failBefore, _failAfter, _moveChild, _noMove;
    private static Settlement _home;
    private static Kingdom _realm;
    private static float _age;
    private static bool _headBeforeDependants;
    private static int _foundings, _homeRefreshes;
    private static T Blank<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    private static bool House(Hero __instance, ref Clan __result)
    { __result = Houses.TryGetValue(__instance, out var house) ? house : null; return false; }
    private static bool Head(Clan __instance, ref Hero __result)
    { __result = Heads.TryGetValue(__instance, out var head) ? head : null; return false; }
    private static bool Alive(Hero __instance, ref bool __result) { __result = !Dead.Contains(__instance); return false; }
    private static bool IsDead(Hero __instance, ref bool __result) { __result = Dead.Contains(__instance); return false; }
    private static bool Prisoner(Hero __instance, ref bool __result) { __result = Captive.Contains(__instance); return false; }
    private static bool Age(ref float __result) { __result = _age; return false; }
    private static bool No(ref bool __result) { __result = false; return false; }
    private static bool MainHero(ref Hero __result) { __result = null; return false; }
    private static bool FindHero(string id, ref Hero __result)
    { __result = Heroes.TryGetValue(id, out var hero) ? hero : null; return false; }
    private static bool FindHome(ref Settlement __result) { __result = _home; return false; }
    private static bool Realm(ref Kingdom __result) { __result = _realm; return false; }
    private static bool Clans(ref MBReadOnlyList<Clan> __result)
    { __result = new MBList<Clan> { _source, _cadet, _foreign }; return false; }
    private static bool Skip() => false;
    private static bool Log(string message) { Logs.Add(message); return false; }
    private static bool Found(Clan __instance, Hero leader)
    { Heads[__instance] = leader; _foundings++; return false; }
    private static bool ChangeHouse(Hero __instance, Clan value) { Houses[__instance] = value; return false; }
    private static bool Members(Clan __instance, ref MBReadOnlyList<Hero> __result)
    { __result = new MBList<Hero>(Houses.Where(pair => pair.Value == __instance).Select(pair => pair.Key)); return false; }
    private static bool RefreshHome()
    {
        if (Heads[_cadet] != _heir) throw new InvalidOperationException("home refresh saw a leaderless cadet");
        _homeRefreshes++;
        return false;
    }
    private static bool Transfer(Hero hero, Clan parentClan, Clan cadetClan)
    {
        Transfers.Add(hero);
        if (hero != _heir) _headBeforeDependants &= Heads[_cadet] == _heir;
        if (hero == _failBefore) throw new InvalidOperationException("simulated transfer failure");
        if (hero == _noMove) return false;
        Houses[hero] = cadetClan;
        if (hero == _heir && _moveChild != null) Houses[_moveChild] = _foreign;
        if (hero == _failAfter) throw new InvalidOperationException("simulated post-transfer failure");
        return false;
    }

    private static CrownAccessionRecord Reset()
    {
        Houses[_heir] = Houses[_spouse] = Houses[_child] = Houses[_sourceHead] = _source;
        Heads[_source] = _sourceHead; Heads[_cadet] = null;
        Dead.Clear(); Captive.Clear(); Transfers.Clear(); Logs.Clear();
        _failBefore = _failAfter = _moveChild = _noMove = null;
        _age = 30; _headBeforeDependants = true; _foundings = 0;
        return new CrownAccessionRecord
        {
            PreviousHouse = _source, Heir = _heir, Realm = _realm, Cadet = _cadet,
            CadetId = _cadet.StringId, CadetInitialized = true, CadetAnnounced = true,
            EndowmentFiefs = new List<string> { "home" },
            Household = new List<string> { _heir.StringId, _spouse.StringId, _child.StringId }
        };
    }

    internal static void Run(Action<bool, string> check)
    {
        var oldCampaign = Campaign.Current;
        var harmony = new Harmony("bellum.test.cadet_household_preparation");
        void Patch(MethodBase target, string name) => harmony.Patch(target,
            prefix: new HarmonyMethod(typeof(CadetHouseholdPreparationTests), name));
        Hero Hero(string id)
        { var hero = Blank<Hero>(); hero.StringId = id; Heroes[id] = hero; return hero; }
        Heroes.Clear(); Houses.Clear(); Heads.Clear();
        _heir = Hero("founder"); _spouse = Hero("spouse"); _child = Hero("child_beneficiary");
        _sourceHead = Hero("source_head");
        _source = Blank<Clan>(); _source.StringId = "estate_house";
        _cadet = Blank<Clan>(); _cadet.StringId = "bc_partition_estate_test";
        _foreign = Blank<Clan>(); _foreign.StringId = "independent_child_house";
        _home = Blank<Settlement>(); _realm = Blank<Kingdom>();
        var behavior = new PartitionSuccessionBehavior();
        var prepare = AccessTools.Method(typeof(PartitionSuccessionBehavior), "PrepareAbdicationCadet");
        var reconcile = AccessTools.Method(typeof(PartitionSuccessionBehavior), "ReconcileEstateCadetHousehold");
        bool Prepare(CrownAccessionRecord r) => (bool)prepare.Invoke(behavior, new object[] { r });
        bool Throws(CrownAccessionRecord r)
        {
            try { Prepare(r); return false; }
            catch (TargetInvocationException ex) { return ex.InnerException is InvalidOperationException; }
        }
        void Reconcile(CrownAccessionRecord r, bool separateBeneficiary)
        {
            var share = new CrossClanEstateShare { Heir = _heir, CadetPlan = r };
            var estate = new CrossClanEstateRecord { Source = _source, Shares = new List<CrossClanEstateShare> { share } };
            if (separateBeneficiary) estate.Shares.Add(new CrossClanEstateShare { Heir = _child });
            reconcile.Invoke(null, new object[] { estate, share });
        }
        try
        {
            var campaign = Blank<Campaign>();
            AccessTools.PropertySetter(typeof(Campaign), "Current").Invoke(null, new object[] { campaign });
            campaign.AddCampaignBehaviorManager(new Manager());
            Patch(AccessTools.PropertyGetter(typeof(Hero), "Clan"), nameof(House));
            Patch(AccessTools.PropertySetter(typeof(Hero), "Clan"), nameof(ChangeHouse));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "Heroes"), nameof(Members));
            Patch(AccessTools.Method(typeof(Hero), "UpdateHomeSettlement"), nameof(RefreshHome));
            Patch(AccessTools.PropertyGetter(typeof(Hero), "IsAlive"), nameof(Alive));
            Patch(AccessTools.PropertyGetter(typeof(Hero), "IsDead"), nameof(IsDead));
            Patch(AccessTools.PropertyGetter(typeof(Hero), "IsDisabled"), nameof(No));
            Patch(AccessTools.PropertyGetter(typeof(Hero), "IsTraveling"), nameof(No));
            Patch(AccessTools.PropertyGetter(typeof(Hero), "IsPrisoner"), nameof(Prisoner));
            Patch(AccessTools.PropertyGetter(typeof(Hero), "MainHero"), nameof(MainHero));
            Patch(AccessTools.PropertyGetter(typeof(Hero), "Age"), nameof(Age));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "Leader"), nameof(Head));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "IsEliminated"), nameof(No));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "Kingdom"), nameof(Realm));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "All"), nameof(Clans));
            Patch(AccessTools.Method(typeof(Clan), "SetLeader"), nameof(Found));
            Patch(AccessTools.Method(typeof(Clan), "ConsiderAndUpdateHomeSettlement"), nameof(Skip));
            Patch(AccessTools.Method(typeof(Settlement), "Find"), nameof(FindHome));
            Patch(AccessTools.Method(typeof(CrownAccessionBehavior), "ResolveAbdicationHero"), nameof(FindHero));
            Patch(AccessTools.Method(typeof(PartitionSuccessionBehavior), "TransferHeroToClan"), nameof(Transfer));
            Patch(AccessTools.Method(typeof(BellumCivileLogger), "Log"), nameof(Log));

            var r = Reset(); Houses[_child] = _foreign;
            check(!Prepare(r) && Transfers.Count == 0 && Heads[_cadet] == null,
                "Whole-household preflight blocks a stale child before either parent moves");
            check(r.AbdicationFailure.Contains(_child.StringId) && r.AbdicationFailure.Contains(_foreign.StringId),
                "Deferred preparation identifies the specific member and current house");
            int logCount = Logs.Count;
            check(!Prepare(r) && Logs.Count == logCount, "Identical preparation failures do not spam logs");
            Reconcile(r, true);
            check(r.Household.SequenceEqual(new[] { _heir.StringId, _spouse.StringId }) && Houses[_child] == _foreign,
                "An independently inherited child's stale move is removed without stealing the child");
            check(Prepare(r) && Heads[_cadet] == _heir && _headBeforeDependants && _foundings == 1,
                "Actual preparation establishes the adult founder before moving dependants");
            check(r.AbdicationFailure == null && !r.Completed && !r.EndowmentSettled && r.DeliveredFiefs.Count == 0,
                "Household completion clears diagnostics but does not deliver or complete the estate");
            int moved = Transfers.Count;
            check(Prepare(r) && Transfers.Count == moved && _foundings == 1, "Completed household preparation is idempotent");

            r = Reset(); Reconcile(r, true);
            check(!r.Household.Contains(_child.StringId) && Houses[_child] == _source,
                "A co-beneficiary is reserved before either inheritance household moves");
            r = Reset(); Houses[_child] = _foreign; Reconcile(r, false);
            check(!r.Household.Contains(_child.StringId), "A dependant who married into another house is not reclaimed");
            r = Reset(); Houses[_child] = _cadet; Reconcile(r, true);
            check(r.Household.Contains(_child.StringId), "Reconciliation does not undo an already-executed household move");
            r = Reset(); Dead.Add(_child); Reconcile(r, false);
            check(!r.Household.Contains(_child.StringId), "Dead dependants do not permanently block the household");
            r = Reset(); r.Household.Add(_sourceHead.StringId); Reconcile(r, false);
            check(!r.Household.Contains(_sourceHead.StringId), "A newly installed source-clan leader remains protected");
            r = Reset(); r.Household.Add("unresolved"); Reconcile(r, false);
            check(r.Household.Contains("unresolved") && !Prepare(r) && Transfers.Count == 0,
                "Unresolved references defer safely instead of being silently discarded");
            r = Reset(); r.Household.Remove(_heir.StringId);
            check(!Prepare(r) && Transfers.Count == 0, "A household missing its founder cannot start");
            r = Reset(); r.Cadet = null; r.CadetId = "uncreated"; r.CadetInitialized = false; Captive.Add(_spouse);
            check(!Prepare(r) && r.Cadet == null && Transfers.Count == 0,
                "A temporarily unavailable dependant prevents registration of a new cadet");
            r = Reset(); r.Household.Reverse();
            check(Prepare(r) && Transfers[0] == _heir && _headBeforeDependants,
                "Legacy household ordering cannot move dependants ahead of the founder");

            r = Reset(); r.CadetAnnounced = false; _failBefore = _spouse;
            check(Throws(r) && Heads[_cadet] == _heir && Houses[_spouse] == _source && !r.CadetAnnounced,
                "A later transfer exception leaves the cadet headed without announcing success");
            r = Reset(); r.CadetAnnounced = false; _failAfter = _heir;
            check(Throws(r) && Heads[_cadet] == _heir && Houses[_spouse] == _source && !r.CadetAnnounced,
                "An exception after the founder changes house still installs a safe provisional head");
            r = Reset(); _noMove = _spouse;
            check(!Prepare(r) && Heads[_cadet] == _heir && !r.Completed,
                "A transfer that returns without moving a dependant preserves the head and pending estate");
            r = Reset(); _moveChild = _child;
            check(!Prepare(r) && Heads[_cadet] == _heir && Houses[_child] == _foreign,
                "Membership changed by an intervening callback is revalidated without stealing the member");

            r = Reset(); Houses[_heir] = Houses[_spouse] = _cadet; Houses[_child] = _foreign;
            r.Cadet = null;
            r.DeliveredFiefs.Add("delivered_fief"); r.DeliveredTitles.Add("delivered_title");
            r.GoldDebited = r.GoldCredited = true; r.DeliveredGold = 125;
            check(!Prepare(r) && r.Cadet == _cadet && Heads[_cadet] == _heir && Transfers.Count == 0,
                "Legacy partial preparation reuses the reserved clan and restores its head before any deferral");
            Reconcile(r, true);
            check(Prepare(r) && Transfers.Count == 0 && r.DeliveredFiefs.SequenceEqual(new[] { "delivered_fief" })
                && r.DeliveredTitles.SequenceEqual(new[] { "delivered_title" }) && r.GoldDebited && r.GoldCredited
                && r.DeliveredGold == 125 && !r.Completed,
                "Recovering the crash state neither replays transfers nor resets estate receipts");

            r = Reset(); _age = 12;
            check(!Prepare(r) && Heads[_cadet] == _heir && !r.Completed && r.AbdicationFailure.Contains("regent"),
                "Unavailable regency retains a provisional child head without completing inheritance");
            Houses[_heir] = _cadet; Heads[_cadet] = _spouse; Captive.Add(_child);
            check(!Prepare(r) && Heads[_cadet] == _spouse, "An existing regent is not overwritten on deferred retries");

            r = Reset(); Houses[_heir] = _cadet;
            var share = new CrossClanEstateShare { Heir = _heir, CadetPlan = r };
            var pending = Blank<PendingPartitionSuccessionRecord>(); pending.EstateShares = new List<CrossClanEstateShare> { share };
            AccessTools.Field(typeof(PartitionSuccessionBehavior), "_pendingPartitions").SetValue(behavior,
                new List<PendingPartitionSuccessionRecord> { pending });
            AccessTools.Method(typeof(PartitionSuccessionBehavior), "RestorePendingEstateHeads").Invoke(behavior, null);
            check(Heads[_cadet] == _heir && !share.Completed && !share.LandedSettled,
                "Session recovery also protects pending lesser Crown-estate cadets without settling their share");

            // Exercise the real transfer helper as well as the fault-injected preparation flow.
            r = Reset(); _homeRefreshes = 0;
            var transfer = AccessTools.Method(typeof(PartitionSuccessionBehavior), "TransferHeroToClan");
            harmony.Unpatch(transfer, HarmonyPatchType.Prefix, harmony.Id);
            transfer.Invoke(null, new object[] { _heir, _source, _cadet, true, null, true });
            check(Houses[_heir] == _cadet && Heads[_cadet] == _heir && _homeRefreshes > 0,
                "Real founder transfer installs its head before native home-settlement updates");
            int refreshes = _homeRefreshes;
            transfer.Invoke(null, new object[] { _heir, _source, _cadet, true, null, true });
            check(_homeRefreshes == refreshes && _foundings == 1, "Real transfer retry does not move or found the heir twice");
        }
        finally
        {
            harmony.UnpatchAll(harmony.Id);
            AccessTools.PropertySetter(typeof(Campaign), "Current").Invoke(null, new object[] { oldCampaign });
        }
    }
}
