using System;
using System.Collections;
using System.Linq;
using System.Xml;
using BellumCivile;
using HarmonyLib;

internal static class LandlessLeaderStyleTests
{
    internal static void Run(Action<bool, string> check)
    {
        var assembly = typeof(FeudalTitleRecord).Assembly;
        var config = assembly.GetType("BellumCivile.FeudalTitleConfig");
        var display = assembly.GetType("BellumCivile.FeudalTitleDisplayHelper");
        object Parse(string attributes)
        {
            var xml = new XmlDocument();
            xml.LoadXml("<TitleStyle culture='Vlandia'><Rank tier='Kingdom' " + attributes + " /></TitleStyle>");
            return AccessTools.Method(config, "ReadTitleStyle").Invoke(null, new object[] { xml.DocumentElement });
        }
        object Rank(object style) => ((IList)AccessTools.Field(style.GetType(), "Ranks").GetValue(style))[0];
        string Value(object rank, string name) => (string)AccessTools.Field(rank.GetType(), name).GetValue(rank);
        string Resolve(object rank, bool female, bool leader = true) => (string)AccessTools.Method(display,
            leader ? "ResolveLandlessLeaderTitle" : "ResolveNobleTitle").Invoke(null, new[] { rank, (object)female });
        var style = Parse("nobleRank='Lord' femaleNobleRank='Lady' landlessLeaderRank='Patriarch' femaleLandlessLeaderRank='Matriarch'");
        var rank = Rank(style);
        check(Value(rank, "LandlessLeaderRank") == "Patriarch" && Value(rank, "FemaleLandlessLeaderRank") == "Matriarch",
            "Both landless leader attributes parse independently");
        check(Resolve(rank, false) == "Patriarch" && Resolve(rank, true) == "Matriarch", "Landless honorific respects gender");
        check(Resolve(rank, false, false) == "Lord" && Resolve(rank, true, false) == "Lady", "Ordinary nobles retain generic ranks");
        rank = Rank(Parse("nobleRank='Lord' femaleNobleRank='Lady'"));
        check(Resolve(rank, false) == "Lord" && Resolve(rank, true) == "Lady", "Existing presets preserve noble fallbacks");
        check(Resolve(null, false) == "Lord" && Resolve(null, true) == "Lady", "Missing styles preserve default honorifics");
        foreach (string attr in new[] { "landlessLeaderRank", "femaleLandlessLeaderRank" })
        {
            rank = Rank(Parse(attr + "='House Head'"));
            check(Resolve(rank, false) == "House Head" && Resolve(rank, true) == "House Head", "Single configured gender serves both");
        }
        rank = Rank(Parse("landlessLeaderRank='{=BC_Test_HouseHead}House Head'"));
        check(Resolve(rank, false) == "House Head", "Landless honorific supports localization tokens");
        AccessTools.Method(config, "MergeStyle").Invoke(null, new[] { style, Parse("femaleLandlessLeaderRank='Dame' titleName='Realm'") });
        rank = Rank(style);
        check(Resolve(rank, false) == "Patriarch" && Resolve(rank, true) == "Dame", "Patches replace only supplied landless fields");
        AccessTools.Method(config, "MergeStyle").Invoke(null, new[] { style, Parse("nobleRank='Noble'") });
        check(Resolve(rank, false) == "Patriarch" && Resolve(rank, true) == "Dame", "Unrelated patches retain landless settings");

        var selection = AccessTools.GetDeclaredMethods(display).Single(m => m.Name == "TryGetDisplayStyle" && m.GetParameters().Length == 4);
        var calls = PatchProcessor.GetCurrentInstructions(selection).Select(i => i.operand as System.Reflection.MethodInfo).Where(m => m != null).Select(m => m.Name).ToList();
        foreach (string name in new[] { "TryGetHolderDisplayStyle", "TryGetSpouseDisplayStyle", "TryGetCrownHeirDisplayStyle", "TryGetRulerChildDisplayStyle" })
            check(calls.IndexOf(name) >= 0 && calls.IndexOf(name) < calls.IndexOf("TryGetNobleDisplayStyle"), "Landless fallback preserves priority of " + name);
        var nobleCalls = PatchProcessor.GetCurrentInstructions(AccessTools.Method(display, "TryGetNobleDisplayStyle"));
        check(nobleCalls.Any(i => i.Calls(AccessTools.Method(display, "GetHighestHeldTitle"))), "Landless fallback checks held titles, not settlement count");
        check(nobleCalls.Any(i => i.Calls(AccessTools.PropertyGetter(typeof(TaleWorlds.CampaignSystem.Clan), "Leader"))), "Fallback remains clan-leader specific");

        string Merc(object r, bool female) => (string)AccessTools.Method(display, "ResolveMercenaryLeaderTitle").Invoke(null, new[] { r, (object)female });
        var company = Parse("mercenaryLeaderRank='Captain' femaleMercenaryLeaderRank='Commander' nobleRank='Lord'");
        var companyRank = Rank(company);
        check(Merc(companyRank, false) == "Captain" && Merc(companyRank, true) == "Commander", "Mercenary leader ranks parse and respect gender");
        check(Merc(Rank(Parse("nobleRank='Lord'")), false) == null && Merc(null, true) == null,
            "Missing mercenary ranks never invent a noble or captain honorific");
        foreach (string attr in new[] { "mercenaryLeaderRank", "femaleMercenaryLeaderRank" })
        {
            var one = Rank(Parse(attr + "='{=BC_Test_Captain}Captain'"));
            check(Merc(one, false) == "Captain" && Merc(one, true) == "Captain", "Mercenary rank supports localization and cross-gender fallback");
        }
        AccessTools.Method(config, "MergeStyle").Invoke(null, new[] { company, Parse("femaleMercenaryLeaderRank='Captain-General'") });
        check(Merc(companyRank, false) == "Captain" && Merc(companyRank, true) == "Captain-General", "Mercenary patch merges fields independently");
        AccessTools.Method(config, "MergeStyle").Invoke(null, new[] { company, Parse("nobleRank='Noble'") });
        check(Merc(companyRank, false) == "Captain" && Merc(companyRank, true) == "Captain-General", "Unrelated patches preserve mercenary ranks");
    }
}
