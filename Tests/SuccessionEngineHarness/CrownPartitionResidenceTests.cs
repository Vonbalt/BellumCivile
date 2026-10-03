using System;
using System.Linq;
using System.Runtime.Serialization;
using BellumCivile;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;

internal static class CrownPartitionResidenceTests
{
    internal static void Run(Action<bool, string> check)
    {
        var realm = (Kingdom)FormatterServices.GetUninitializedObject(typeof(Kingdom));
        var foreign = (Kingdom)FormatterServices.GetUninitializedObject(typeof(Kingdom));
        var house = (Clan)FormatterServices.GetUninitializedObject(typeof(Clan));
        var other = (Clan)FormatterServices.GetUninitializedObject(typeof(Clan));
        AccessTools.Field(typeof(Clan), "_kingdom").SetValue(house, realm);
        AccessTools.Field(typeof(Clan), "_kingdom").SetValue(other, foreign);
        Town Town(string id, Clan owner, float prosperity)
        {
            var settlement = (Settlement)FormatterServices.GetUninitializedObject(typeof(Settlement));
            AccessTools.Property(typeof(Settlement), "StringId").SetValue(settlement, id);
            var party = (TaleWorlds.CampaignSystem.Party.PartyBase)FormatterServices.GetUninitializedObject(typeof(TaleWorlds.CampaignSystem.Party.PartyBase));
            AccessTools.Property(party.GetType(), "Settlement").SetValue(party, settlement);
            var town = (Town)FormatterServices.GetUninitializedObject(typeof(Town));
            AccessTools.Field(typeof(SettlementComponent), "_owner").SetValue(town, party);
            AccessTools.Field(typeof(Town), "_ownerClan").SetValue(town, owner);
            AccessTools.Field(typeof(Town), "_prosperity").SetValue(town, prosperity);
            return town;
        }
        var capital = Town("capital", house, 50);
        var rich = Town("rich", house, 1000);
        var outside = Town("outside", house, 9000);
        var occupied = Town("occupied", other, 10000);
        var select = AccessTools.Method(typeof(FeudalTitleRecord).Assembly.GetType("BellumCivile.CrownPartitionResidence"), "Select");
        object Select(string preferred, string[] ids, params Town[] towns) => select.Invoke(null, new object[] { realm, preferred, ids, towns });
        var territory = new[] { "capital", "rich", "occupied" };
        check(ReferenceEquals(Select("capital", territory, rich, capital, outside, occupied), capital), "Crown residence prefers controlled historical capital over prosperity");
        check(ReferenceEquals(Select("occupied", territory, rich, capital, outside, occupied), rich), "Enemy-held capital falls back within controlled Crown territory");
        check(ReferenceEquals(Select("outside", territory, rich, capital, outside), rich), "Recorded capital outside Crown territory is not accepted");
        check(Select("capital", territory, outside, occupied) == null, "No suitable Crown residence fails without map-wide fallback");
        check(Select("capital", null, capital) == null, "Missing Crown territory cannot authorize residence");
        check(Select("capital", territory, null, capital) == capital, "Missing candidate is ignored without losing valid residence");
        check(capital.OwnerClan == house && rich.OwnerClan == house && occupied.OwnerClan == other, "Residence selection does not transfer any settlement");
        var tied = Town("aaa", house, 1000);
        check(ReferenceEquals(Select(null, new[] { "aaa", "rich" }, rich, tied), tied)
            && ReferenceEquals(Select(null, new[] { "aaa", "rich" }, tied, rich), tied), "Residence tie-break does not depend on enumeration order");
        var crownCreation = AccessTools.Method(typeof(PartitionSuccessionBehavior), "CreateCrownPartitionCadetBranch");
        check(PatchProcessor.GetOriginalInstructions(crownCreation).Any(i => i.Calls(AccessTools.Method(typeof(FeudalTitleBehavior), "ResolveCrownPartitionResidence"))),
            "Crown cadet creation uses strict residence resolution");
        var realmCreation = AccessTools.Method(typeof(FeudalTitleBehavior), "CreateCrownPartitionRealmAtResidence");
        check(PatchProcessor.GetOriginalInstructions(realmCreation).Any(i => i.Calls(AccessTools.Method(typeof(FeudalTitleBehavior), "ResolveCrownPartitionResidence"))),
            "Crown realm creation revalidates residence before creating shell");
    }
}
