using System;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using BellumCivile;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.BarterSystem;
using TaleWorlds.CampaignSystem.BarterSystem.Barterables;
using TaleWorlds.CampaignSystem.Roster;

internal static class HostageCustodyGuardTests
{
    private static Hero _hostage;
    private static Hero _otherHostage;
    private static bool Protected(Hero hero, ref bool __result)
    { __result = hero != null && (hero == _hostage || hero == _otherHostage); return false; }
    private static T Empty<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    private static bool Healthy(ref bool __result) { __result = false; return false; }
    internal static void Run(Action<bool, string> check)
    {
        var assembly = typeof(HostagePactRecord).Assembly;
        var guard = assembly.GetType("BellumCivile.HostageCustodyGuard");
        object Call(string name, params object[] args) => AccessTools.Method(guard, name).Invoke(null, args);
        var harmony = new Harmony("bellum.test.hostage_custody");
        _hostage = Empty<Hero>(); _otherHostage = Empty<Hero>();
        try
        {
            harmony.Patch(AccessTools.Method(guard, "IsProtected"), prefix: new HarmonyMethod(typeof(HostageCustodyGuardTests), nameof(Protected)));
            harmony.Patch(AccessTools.PropertyGetter(typeof(Hero), "IsWounded"), prefix: new HarmonyMethod(typeof(HostageCustodyGuardTests), nameof(Healthy)));
            foreach (var type in assembly.GetTypes().Where(t => t.Namespace == "BellumCivile.Patches"
                && t.Name.StartsWith("Hostage") && t.GetCustomAttributes(typeof(HarmonyPatch), false).Length > 0))
            {
                harmony.CreateClassProcessor(type).Patch();
                check(true, "Hostage native patch binds: " + type.Name);
            }
            check((bool)Call("BlocksOrdinaryAction", _hostage) && !(bool)Call("BlocksOrdinaryAction", Empty<Hero>()),
                "Only treaty hostage ordinary actions blocked");
            using ((IDisposable)Call("Authorize", _hostage))
            {
                using ((IDisposable)Call("Authorize", _hostage))
                    check(!(bool)Call("BlocksOrdinaryAction", _hostage) && (bool)Call("BlocksOrdinaryAction", _otherHostage),
                        "Nested authorization does not unlock another hostage");
                check(!(bool)Call("BlocksOrdinaryAction", _hostage), "Outer authorization survives inner disposal");
            }
            check((bool)Call("BlocksOrdinaryAction", _hostage), "Authorization restored on disposal");
            CharacterObject Troop(Hero hero)
            {
                var troop = Empty<CharacterObject>();
                AccessTools.Field(typeof(CharacterObject), "_heroObject").SetValue(troop, hero);
                return troop;
            }
            var hostageTroop = Troop(_hostage); var ordinary = Troop(null);
            var roster = TroopRoster.CreateDummyTroopRoster();
            roster.AddToCounts(hostageTroop, 1); roster.AddToCounts(ordinary, 3);
            var filtered = (TroopRoster)Call("WithoutHostages", roster);
            check(filtered.GetTroopCount(hostageTroop) == 0 && filtered.GetTroopCount(ordinary) == 3
                && roster.GetTroopCount(hostageTroop) == 1, "Sale filters copied roster and preserves ordinary prisoners");
            check(!(bool)Call("PreservesCustody", roster, filtered)
                && (bool)Call("PreservesCustody", roster, roster.CloneRosterData()), "Party commit detects removed hostage");
            check(!(bool)Call("PreservesCustody", null, roster), "Party commit detects unauthorized hostage addition");
            var item = new SetPrisonerFreeBarterable(_hostage, null, null, null);
            var data = Empty<BarterData>();
            AccessTools.Field(typeof(BarterData), "_barterables").SetValue(data, new System.Collections.Generic.List<Barterable>());
            data.GetBarterables().Add(item);
            var listPatch = assembly.GetType("BellumCivile.Patches.HostageBarterListPatch");
            AccessTools.Method(listPatch, "Postfix").Invoke(null, new object[] { data });
            check(data.GetBarterables().Count == 0, "Protected prisoner removed from barter choices");
            var atomic = assembly.GetType("BellumCivile.Patches.HostageBarterAtomicGuardPatch");
            check(!(bool)AccessTools.Method(atomic, "Prefix").Invoke(null,
                new object[] { new System.Collections.Generic.List<Barterable> { item } }), "Stale hostage barter blocked before any item applies");
        }
        finally { harmony.UnpatchAll(harmony.Id); _hostage = null; _otherHostage = null; }
    }
}
