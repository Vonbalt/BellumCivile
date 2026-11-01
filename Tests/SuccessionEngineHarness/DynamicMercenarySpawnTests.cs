using System;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using BellumCivile;
using HarmonyLib;
using Helpers;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Party.PartyComponents;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Library;

internal static class DynamicMercenarySpawnTests
{
    private static CampaignVec2 _candidate, _createdPosition;
    private static float _radius, _minimum, _maximum;
    private static int _searches, _created, _entered, _logs, _limit;
    private static Settlement _current, _createdHome;
    private static Hero _owner, _leader;
    private static T Blank<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    private static bool Search(CampaignVec2 center, MobileParty.NavigationType navigationCapability,
        float maxDistance, float minDistance, ref CampaignVec2 __result)
    {
        _searches++; _minimum = minDistance; _maximum = maxDistance; __result = _candidate; return false;
    }
    private static bool Create(Hero hero, CampaignVec2 position, float spawnRadius, Settlement spawnSettlement,
        Hero partyLeader, ref MobileParty __result)
    {
        _created++; _owner = hero; _leader = partyLeader; _createdPosition = position; _radius = spawnRadius;
        _createdHome = spawnSettlement; __result = Blank<MobileParty>(); return false;
    }
    private static bool Enter(Hero __0, Settlement __1) { _entered++; _current = __1; return false; }
    private static bool Current(ref Settlement __result) { __result = _current; return false; }
    private static bool Limit(ref int __result) { __result = _limit; return false; }
    private static bool Clock(ref CampaignTime __result) { __result = CampaignTime.Days(20); return false; }
    private static bool Log() { _logs++; return false; }

    internal static void Run(Action<bool, string> check)
    {
        var h = new Harmony("bellum.tests.dynamic_mercenary_spawn");
        var previous = Campaign.Current;
        var scene = new PartyNavigationTestScene(); var campaign = scene.CreateCampaign();
        var tickField = AccessTools.Field(typeof(CampaignTime), "TimeTicksPerDay"); object ticks = tickField.GetValue(null);
        var helper = typeof(FactionObject).Assembly.GetType("BellumCivile.TownPartyPositionHelper", true);
        var service = typeof(FactionObject).Assembly.GetType("BellumCivile.DynamicMercenaryBandService", true);
        var find = AccessTools.Method(typeof(NavigationHelper), "FindReachablePointAroundPosition", new[] {
            typeof(CampaignVec2), typeof(MobileParty.NavigationType), typeof(float), typeof(float), typeof(bool) });
        void Patch(MethodBase method, string prefix) => h.Patch(method, prefix: new HarmonyMethod(typeof(DynamicMercenarySpawnTests), prefix));
        var home = Blank<Settlement>(); home.StringId = "spawn_town";
        var gate = new CampaignVec2(new Vec2(100, 100), true);
        AccessTools.PropertySetter(typeof(Settlement), "GatePosition").Invoke(home, new object[] { gate });
        var clan = Blank<Clan>(); clan.StringId = "bc_dynamic_merc_test";
        AccessTools.Field(typeof(Clan), "_warPartyComponentsCache").SetValue(clan, new MBList<WarPartyComponent>());
        var hero = Blank<Hero>(); hero.StringId = "captain";
        void Ensure() => AccessTools.Method(service, "EnsureHeroPartyOrHome").Invoke(null, new object[] { hero, clan, home });
        bool Find(out CampaignVec2 point)
        {
            var args = new object[] { home, CampaignVec2.Invalid };
            bool result = (bool)AccessTools.Method(helper, "TryFindSpawn").Invoke(null, args);
            point = (CampaignVec2)args[1]; return result;
        }
        void Reset()
        {
            _current = null; _created = _entered = _searches = _logs = 0;
            scene.InvalidPositions.Clear(); scene.DisconnectedPositions.Clear(); scene.PathsAvailable = true; scene.ThrowOnPath = false;
            _candidate = new CampaignVec2(new Vec2(108, 100), true); _limit = 3;
        }
        try
        {
            tickField.SetValue(null, 24000L);
            AccessTools.PropertySetter(typeof(GameModels), "ClanTierModel").Invoke(campaign.Models,
                new object[] { Blank<DefaultClanTierModel>() });
            Patch(AccessTools.Method(typeof(DefaultClanTierModel), "GetPartyLimitForTier"), nameof(Limit));
            Patch(find, nameof(Search)); Patch(AccessTools.Method(typeof(LordPartyComponent), "CreateLordParty"), nameof(Create));
            Patch(AccessTools.Method(typeof(EnterSettlementAction), "ApplyForCharacterOnly"), nameof(Enter));
            Patch(AccessTools.PropertyGetter(typeof(Hero), "CurrentSettlement"), nameof(Current));
            Patch(AccessTools.PropertyGetter(typeof(CampaignTime), "Now"), nameof(Clock));
            Patch(AccessTools.Method(typeof(BellumCivileLogger), "Log"), nameof(Log));
            Reset(); Ensure();
            check(_created == 1 && _entered == 0 && _createdPosition == _candidate && _radius == 0,
                "Mercenary parties use the verified land position without a second random offset");
            check(_owner == hero && _leader == hero && _createdHome == home && _minimum == 6 && _maximum == 12,
                "Native party setup preserves captain and home while reserving clearance from the town gate");
            check(scene.PathCalls > 0 && scene.LastExcludedTerrain?.Length > 0,
                "Spawn validation uses the native land path query and excluded terrain list");
            Reset(); _candidate = gate; Ensure();
            check(_created == 0 && _entered == 1 && _current == home && _logs == 1,
                "Native center fallback does not spawn a party inside the town; the hero waits safely at home");
            Ensure(); check(_entered == 1, "Already sheltered heroes are not entered into the same settlement repeatedly");
            foreach (float distance in new[] { 0f, 3f, 5.9f, 12.1f, 100f, float.NaN, float.PositiveInfinity })
            {
                Reset(); _candidate = new CampaignVec2(new Vec2(100 + distance, 100), true);
                check(!Find(out _), "Unsafe spawn distances are rejected: " + distance);
            }
            foreach (float distance in new[] { 6f, 8f, 12f })
            {
                Reset(); _candidate = new CampaignVec2(new Vec2(100 + distance, 100), true);
                check(Find(out var point) && point == _candidate, "Reachable points inside the spawn annulus are accepted: " + distance);
            }
            Reset(); _candidate.IsOnLand = false; Ensure();
            check(_created == 0 && _entered == 1, "A water result cannot produce an unintended naval mercenary party");
            Reset(); scene.InvalidPositions.Add(_candidate.ToVec2()); Ensure();
            check(_created == 0 && _entered == 1, "Invalid navigation faces leave the captain in town instead of creating a stranded party");
            Reset(); scene.PathsAvailable = false; Ensure();
            check(_created == 0 && _entered == 1, "A valid but disconnected navigation face is not an acceptable spawn");
            Reset(); scene.InvalidPositions.Add(gate.ToVec2()); Ensure();
            check(_searches == 0 && _created == 0 && _entered == 1, "An invalid gate is not used as a navigation-search anchor");
            Reset(); scene.ThrowOnPath = true; Ensure();
            check(_created == 0 && _entered == 1 && _logs == 2, "Navigation exceptions are diagnosed and still use the safe home fallback");
            Reset(); _limit = 0; Ensure();
            check(_searches == 0 && _created == 0 && _entered == 1, "Native clan party caps are still honored without unnecessary searches");
            Reset(); AccessTools.Field(typeof(Hero), "_partyBelongedTo").SetValue(hero, Blank<MobileParty>()); Ensure();
            check(_searches == 0 && _created == 0 && _entered == 0, "Existing parties are never repositioned or replaced by creation safeguards");
            AccessTools.Field(typeof(Hero), "_partyBelongedTo").SetValue(hero, null);
            Reset(); h.Unpatch(find, AccessTools.Method(typeof(DynamicMercenarySpawnTests), nameof(Search)));
            scene.PathsAvailable = false; int before = scene.PathCalls;
            check(!Find(out _) && scene.PathCalls - before == 250,
                "The installed native search's actual exhausted-search fallback is rejected after its bounded 250 attempts");
            scene.PathsAvailable = true;
            check(Find(out var nativePoint) && nativePoint.DistanceSquared(gate) >= 36 && nativePoint.DistanceSquared(gate) <= 144,
                "The real native search and Bellum validation agree on reachable land clearance");
        }
        finally
        {
            h.UnpatchAll(h.Id); tickField.SetValue(null, ticks);
            AccessTools.PropertySetter(typeof(Campaign), "Current").Invoke(null, new object[] { previous });
        }
    }
}
