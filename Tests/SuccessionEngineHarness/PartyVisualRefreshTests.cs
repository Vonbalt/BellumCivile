using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using BellumCivile;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Party.PartyComponents;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Library;

internal static class PartyVisualRefreshTests
{
    private static readonly List<MobileParty> Parties = new List<MobileParty>();
    private static readonly Dictionary<MobileParty, PartyBase> Bases = new Dictionary<MobileParty, PartyBase>();
    private static readonly Dictionary<Settlement, Town> Towns = new Dictionary<Settlement, Town>();
    private static readonly Dictionary<Town, Clan> Owners = new Dictionary<Town, Clan>();
    private static readonly HashSet<PartyBase> Dirty = new HashSet<PartyBase>();
    private static readonly List<Settlement> Holdings = new List<Settlement>();
    private static int _settlementOwnerReads;
    private static T Blank<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    private static bool All(ref MBReadOnlyList<MobileParty> __result) { __result = new MBReadOnlyList<MobileParty>(Parties); return false; }
    private static bool Base(MobileParty __instance, ref PartyBase __result) { Bases.TryGetValue(__instance, out __result); return false; }
    private static bool Mobile(PartyBase __instance, ref MobileParty __result) { __result = Bases.FirstOrDefault(p => p.Value == __instance).Key; return false; }
    private static bool Owner(Town __instance, ref Clan __result) { Owners.TryGetValue(__instance, out __result); return false; }
    private static bool Settlements(ref MBReadOnlyList<Settlement> __result) { __result = new MBReadOnlyList<Settlement>(Holdings); return false; }
    private static bool Fiefs(ref MBReadOnlyList<Town> __result) { __result = new MBReadOnlyList<Town>(new List<Town>()); return false; }
    private static bool Mark(PartyBase __instance) { Dirty.Add(__instance); return false; }
    private static void CountOwner() { _settlementOwnerReads++; }

    internal static void Run(Action<bool, string> check)
    {
        var harmony = new Harmony("bellum.tests.party_visual_refresh");
        var helper = typeof(FactionObject).Assembly.GetType("BellumCivile.KingdomVisualHelper");
        void Patch(MethodBase target, string prefix)
        {
            if (target == null) throw new InvalidOperationException("Missing visual fixture target: " + prefix);
            harmony.Patch(target, prefix: new HarmonyMethod(typeof(PartyVisualRefreshTests), prefix));
        }
        void Set(object owner, string name, object value) => AccessTools.PropertySetter(owner.GetType(), name).Invoke(owner, new[] { value });
        Clan House(string id, Kingdom kingdom)
        {
            var house = Blank<Clan>(); house.StringId = id;
            AccessTools.Field(typeof(Clan), "_kingdom").SetValue(house, kingdom);
            return house;
        }
        Settlement Holding(string id, Clan owner)
        {
            var settlement = Blank<Settlement>(); settlement.StringId = id;
            var town = Blank<Town>(); Towns[settlement] = town; Owners[town] = owner;
            AccessTools.Field(typeof(Settlement), "Town").SetValue(settlement, town);
            return settlement;
        }
        MobileParty Party(string id, PartyComponent component, Clan actual = null)
        {
            var party = Blank<MobileParty>(); party.StringId = id; Parties.Add(party); Bases[party] = Blank<PartyBase>();
            AccessTools.Field(typeof(MobileParty), "_partyComponent").SetValue(party, component);
            AccessTools.Field(typeof(MobileParty), "_actualClan").SetValue(party, actual);
            return party;
        }
        MobileParty Militia(string id, Settlement settlement)
        {
            var component = Blank<MilitiaPartyComponent>(); Set(component, "Settlement", settlement);
            return Party(id, component);
        }
        void Refresh(string name, object target) => AccessTools.Method(helper, name).Invoke(null, new[] { target });
        try
        {
            Patch(AccessTools.PropertyGetter(typeof(MobileParty), "All"), nameof(All));
            Patch(AccessTools.PropertyGetter(typeof(MobileParty), "Party"), nameof(Base));
            Patch(AccessTools.PropertyGetter(typeof(PartyBase), "MobileParty"), nameof(Mobile));
            Patch(AccessTools.PropertyGetter(typeof(Town), "OwnerClan"), nameof(Owner));
            Patch(AccessTools.PropertyGetter(typeof(Kingdom), "Settlements"), nameof(Settlements));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "Fiefs"), nameof(Fiefs));
            Patch(AccessTools.Method(typeof(PartyBase), "SetVisualAsDirty"), nameof(Mark));
            foreach (var type in new[] { typeof(MilitiaPartyComponent), typeof(GarrisonPartyComponent), typeof(VillagerPartyComponent), typeof(PatrolPartyComponent) })
                Patch(AccessTools.PropertyGetter(type, "PartyOwner"), nameof(CountOwner));

            var realm = Blank<Kingdom>(); realm.StringId = "new_rebel_realm";
            var rival = Blank<Kingdom>(); rival.StringId = "unrelated_realm";
            var house = House("founder", realm); var other = House("other", rival);
            var town = Holding("healthy_town", house); var foreignTown = Holding("foreign_town", other);
            var orphanTown = Holding("ownerless_town", null);
            var brokenMilitia = Militia("missing_home", null);
            var ownerlessMilitia = Militia("missing_owner", orphanTown);
            bool nativeThrows = false;
            try { var owner = brokenMilitia.Party.Owner; }
            catch (NullReferenceException) { nativeThrows = true; }
            check(nativeThrows, "Native militia Owner getter reproduces the report's null-reference path");
            _settlementOwnerReads = 0;
            var goodMilitia = Militia("healthy_militia", town);
            var foreignMilitia = Militia("foreign_militia", foreignTown);
            var garrison = Blank<GarrisonPartyComponent>(); Set(garrison, "Settlement", town);
            var goodGarrison = Party("garrison", garrison);
            Party("incomplete_garrison", Blank<GarrisonPartyComponent>());
            var village = Blank<Village>(); AccessTools.Field(typeof(Village), "_bound").SetValue(village, town);
            var villageSettlement = Blank<Settlement>(); AccessTools.Field(typeof(Settlement), "Village").SetValue(villageSettlement, village);
            var villageMilitia = Militia("village_militia", villageSettlement);
            var villager = Blank<VillagerPartyComponent>(); Set(villager, "Village", village);
            var goodVillager = Party("villagers", villager);
            var unboundVillage = Blank<Village>(); var unboundVillager = Blank<VillagerPartyComponent>(); Set(unboundVillager, "Village", unboundVillage);
            Party("unbound_villagers", unboundVillager); Party("missing_village", Blank<VillagerPartyComponent>());
            Party("incomplete_patrol", Blank<PatrolPartyComponent>());
            var patrol = Blank<PatrolPartyComponent>(); AccessTools.Field(typeof(PatrolPartyComponent), "_homeSettlement").SetValue(patrol, town);
            var goodPatrol = Party("patrol", patrol);
            var healthyLord = Party("lord", Blank<LordPartyComponent>(), house);
            Party("foreign_lord", Blank<LordPartyComponent>(), other);
            var hero = Blank<Hero>(); AccessTools.Field(typeof(Hero), "_clan").SetValue(hero, house);
            var caravan = Blank<CaravanPartyComponent>(); Set(caravan, "Owner", hero); var goodCaravan = Party("caravan", caravan);
            Party("ownerless_caravan", Blank<CaravanPartyComponent>());
            var custom = Blank<CustomPartyComponent>(); AccessTools.Field(typeof(CustomPartyComponent), "_owner").SetValue(custom, hero);
            var goodCustom = Party("custom", custom);
            Parties.Add(null);
            var noBase = Party("unready_party_base", null); Bases[noBase] = null;
            var expected = new[] { healthyLord, goodMilitia, goodGarrison, goodVillager, villageMilitia, goodPatrol, goodCaravan, goodCustom };
            Refresh("MarkKingdomVisualsDirty", realm);
            check(expected.All(p => Dirty.Contains(Bases[p])) && Dirty.Count == expected.Length,
                "Kingdom refresh reaches every healthy local party and skips unrelated or incomplete parties");
            check(!Dirty.Contains(Bases[brokenMilitia]) && !Dirty.Contains(Bases[ownerlessMilitia]) && !Dirty.Contains(Bases[foreignMilitia]),
                "Missing militia ownership cannot interrupt or contaminate another realm's visual refresh");
            check(_settlementOwnerReads == 0, "Visual refresh never invokes native settlement-party Owner getters");
            var visualType = helper.GetNestedType("KingdomFoundingVisuals", BindingFlags.NonPublic);
            var palette = Activator.CreateInstance(visualType, new object[] { null, 0xff112233u, 0xffddeeffu, false });
            Dirty.Clear();
            AccessTools.Method(helper, "ApplyKingdomPalette", new[] { typeof(Kingdom), visualType }).Invoke(null, new[] { realm, palette });
            check(expected.All(p => Dirty.Contains(Bases[p])) && _settlementOwnerReads == 0,
                "The real kingdom-palette entry point tolerates the report's incomplete militia");
            Dirty.Clear(); Refresh("MarkClanVisualsDirty", house);
            check(expected.All(p => Dirty.Contains(Bases[p])) && Dirty.Count == expected.Length && _settlementOwnerReads == 0,
                "Clan refresh uses the same guarded ownership path without reading a leader from incomplete settlement data");

            Owners[Towns[town]] = other; Dirty.Clear(); Refresh("MarkKingdomVisualsDirty", realm);
            check(!Dirty.Contains(Bases[goodMilitia]) && !Dirty.Contains(Bases[goodGarrison]) && !Dirty.Contains(Bases[goodVillager])
                && !Dirty.Contains(Bases[villageMilitia]) && !Dirty.Contains(Bases[goodPatrol]) && Dirty.Contains(Bases[healthyLord]),
                "Settlement parties immediately follow changed ownership without a cached former house");
            Dirty.Clear(); Refresh("MarkClanVisualsDirty", other);
            check(Dirty.Contains(Bases[goodMilitia]) && Dirty.Contains(Bases[goodGarrison]) && Dirty.Contains(Bases[goodVillager])
                && Dirty.Contains(Bases[villageMilitia]) && Dirty.Contains(Bases[goodPatrol]),
                "New owners receive settlement-party invalidation even when their clan has no leader yet");
            Dirty.Clear(); Refresh("MarkKingdomVisualsDirty", null); Refresh("MarkClanVisualsDirty", null);
            check(Dirty.Count == 0, "Null visual-refresh targets remain harmless no-ops");
        }
        finally
        {
            harmony.UnpatchAll(harmony.Id); Parties.Clear(); Bases.Clear(); Towns.Clear(); Owners.Clear(); Dirty.Clear(); Holdings.Clear();
        }
    }
}
