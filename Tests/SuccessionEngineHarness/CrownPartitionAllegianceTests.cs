using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BellumCivile;

internal static class CrownPartitionAllegianceTests
{
    internal static void Run(Action<bool, string> check)
    {
        var type = typeof(FeudalTitleRecord).Assembly.GetType("BellumCivile.CrownPartitionAllegiancePlan");
        var create = type.GetMethod("TryCreate", BindingFlags.Static | BindingFlags.NonPublic);
        FeudalTitleRecord Title(string id, FeudalTitleType rank, string parent, string owner, string land = "")
            => new FeudalTitleRecord(id, id, rank, owner, owner, parent, land, "realm", 0, 0);
        var primary = Title("primary", FeudalTitleType.Kingdom, "", "ruler");
        var crown = Title("secondary", FeudalTitleType.Kingdom, "", "ruler");
        var duchy = Title("duchy", FeudalTitleType.Duchy, "secondary", "mixed");
        var county = Title("county", FeudalTitleType.County, "primary", "mixed");
        var secondaryBarony = Title("b1", FeudalTitleType.Barony, "duchy", "mixed", "f1");
        var primaryBarony = Title("b2", FeudalTitleType.Barony, "county", "mixed", "f2");
        var emptyDuchy = Title("empty", FeudalTitleType.Duchy, "secondary", "landless");
        var titles = new List<FeudalTitleRecord> { primary, crown, duchy, county, secondaryBarony, primaryBarony, emptyDuchy };
        var holdings = new Dictionary<string, string[]> { ["ruler"] = new string[0], ["heir"] = new string[0],
            ["mixed"] = new[] { "f1", "f2" }, ["landless"] = new string[0] };
        object plan = null;
        bool Plan()
        {
            var args = new object[] { "secondary", "ruler", "heir", holdings, titles, null, null };
            bool result = (bool)create.Invoke(null, args);
            plan = args[5];
            check(result || plan == null && !string.IsNullOrEmpty(args[6] as string), "Invalid allegiance snapshot gives reason without partial plan");
            return result;
        }
        string[] Movers() => ((IEnumerable<string>)type.GetProperty("MovingHouses", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(plan)).ToArray();
        var principalProperty = type.GetProperty("PrincipalTitles", BindingFlags.Instance | BindingFlags.NonPublic);
        check(Plan(), "Mixed estate produces frozen Crown allegiance plan");
        check(Movers().SequenceEqual(new[] { "heir", "mixed" }), "House follows higher landed title despite holdings under retained Crown");
        check(((IReadOnlyDictionary<string, string>)principalProperty.GetValue(plan))["landless"] == null, "Empty honorary title does not invent a principal landed title");
        check(secondaryBarony.DeFactoHolderClanId == "mixed" && primaryBarony.DeFactoHolderClanId == "mixed", "Allegiance planning leaves all land ownership unchanged");
        var savedMovers = Movers();
        duchy.SetParentTitle("primary");
        check(Movers().SequenceEqual(savedMovers), "Frozen allegiance is unaffected by later topology changes");
        check(Plan() && Movers().SequenceEqual(new[] { "heir" }), "House stays when principal title belongs to retained Crown");
        duchy.SetParentTitle("secondary");
        var rival = Title("aaa", FeudalTitleType.Duchy, "primary", "mixed");
        titles.Add(rival); county.SetParentTitle("aaa");
        check(Plan() && Movers().SequenceEqual(new[] { "heir" }), "Equal fully held rank uses stable title identity tie-break");
        rival.SetDeJureHolder("other");
        check(Plan() && Movers().Contains("mixed"), "Full legal ownership breaks equal-rank tie before identity");
        titles.Reverse();
        check(Plan() && Movers().Contains("mixed"), "Input enumeration does not change principal-title allegiance");
        titles.Remove(rival); county.SetParentTitle("primary");
        holdings["mixed"] = new[] { "f2" };
        check(Plan() && Movers().SequenceEqual(new[] { "heir" }), "Higher title without current personal land does not override landed principal");
        holdings["subordinate"] = new[] { "f1" };
        secondaryBarony.SetDeJureHolder("subordinate"); secondaryBarony.SetDeFactoHolder("subordinate");
        check(Plan() && Movers().SequenceEqual(new[] { "heir", "mixed", "subordinate" }),
            "Principal title over landed vassals counts without royal or ducal personal demesne");
        holdings.Remove("subordinate");
        secondaryBarony.SetDeJureHolder("mixed"); secondaryBarony.SetDeFactoHolder("mixed");
        holdings["mixed"] = new[] { "f1", "f2" };
        crown.SetDeJureHolder("heir"); crown.SetDeFactoHolder("heir");
        check(Plan(), "Allegiance capture also accepts Crown already legalized to successor");
        crown.SetDeFactoHolder("ruler");
        check(!Plan(), "Contested Crown blocks allegiance planning");
        crown.SetDeJureHolder("ruler");
        holdings["mixed"] = new[] { "f1", "f1" };
        check(!Plan(), "Repeated physical land blocks ambiguous snapshot");
        holdings["mixed"] = new[] { "missing" };
        check(!Plan(), "Missing barony mapping blocks stale holdings");
        holdings["mixed"] = new[] { "f1", "f2" };
        primaryBarony.SetDeFactoHolder("foreign");
        check(!Plan(), "Mismatched actual holder blocks plan");
        primaryBarony.SetDeFactoHolder("mixed");
        duchy.SetParentTitle("missing");
        check(!Plan(), "Missing ancestor blocks allegiance classification");
        duchy.SetParentTitle("secondary");
        holdings.Remove("heir");
        check(!Plan(), "Successor must belong to captured source membership");
    }
}
