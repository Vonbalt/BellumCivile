using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BellumCivile;

internal static class FeudalCrownPartitionPlanTests
{
    internal static void Run(Action<bool, string> check)
    {
        var create = typeof(FeudalTitleRecord).Assembly.GetType("BellumCivile.FeudalCrownPartitionPlan")
            .GetMethod("TryCreate", BindingFlags.Static | BindingFlags.NonPublic);
        FeudalTitleRecord Title(string id, FeudalTitleType rank, string parent = "", string holder = "royal")
            => new FeudalTitleRecord(id, id, rank, holder, holder, parent, "", "realm", 0, 0);
        object[] result = null;
        bool Plan(params FeudalTitleRecord[] titles)
        {
            var args = new object[] { "royal", "primary", titles, null, null };
            bool ok = (bool)create.Invoke(null, args);
            result = ok ? ((IEnumerable)args[3]).Cast<object>().ToArray() : null;
            check(ok || args[3] == null && !string.IsNullOrEmpty(args[4] as string), "Invalid Crown topology has no partial plan and explains failure");
            return ok;
        }
        object Property(object item, string name) => item.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(item);
        string[] Ids(object item, string name) => ((IEnumerable<string>)Property(item, name)).ToArray();
        var primary = Title("primary", FeudalTitleType.Kingdom);
        var secondary = Title("secondary", FeudalTitleType.Kingdom);
        var vassal = Title("vassal", FeudalTitleType.Duchy, "secondary", "vassal_house");
        var barony = Title("barony", FeudalTitleType.Barony, "vassal", "vassal_house");
        check(Plan(secondary, barony, primary, vassal), "Vassal-only secondary Crown requires no royal fief");
        check(result.Length == 2 && (string)Property(result[0], "CrownId") == "primary", "Existing sovereign Crown always comes first");
        check(Ids(result[1], "TitleIds").SequenceEqual(new[] { "barony", "secondary", "vassal" }), "Crown package preserves whole legal subtree");
        check(Ids(result[1], "PersonalTitleIds").SequenceEqual(new[] { "secondary" }), "Vassal titles are not royal personal property");
        var saved = result[1];
        barony.SetDeJureHolder("royal");
        check(Ids(saved, "PersonalTitleIds").Length == 1, "Selected package is a copied immutable snapshot");
        var empire = Title("empire", FeudalTitleType.Empire);
        check(Plan(primary, secondary, empire) && result.Length == 2, "Unrelated empire does not bind separate Crowns");
        primary.SetParentTitle("empire");
        check(Plan(primary, secondary, empire) && result.Length == 2, "Empire above only one Crown does not bind both");
        secondary.SetParentTitle("empire");
        check(Plan(primary, secondary, empire) && result.Length == 1, "Fully held common higher ancestor binds both Crowns");
        empire.SetDeFactoHolder("other");
        check(Plan(primary, secondary, empire) && result.Length == 2, "Uncontrolled higher title cannot bind the estate");
        primary.SetParentTitle("");
        secondary.SetParentTitle("");
        secondary.SetDeFactoHolder("other");
        check(Plan(primary, secondary) && result.Length == 1, "Contested secondary Crown is not an independent package");
        secondary.SetDeFactoHolder("royal");
        check(!Plan(primary, primary), "Duplicate title snapshots fail closed");
        check(!Plan(primary, null), "Null title snapshot fails closed");
        primary.SetParentTitle("missing");
        check(!Plan(primary, secondary), "Missing ancestor blocks incomplete hierarchy");
        primary.SetParentTitle("secondary");
        check(!Plan(primary, secondary), "Equal-rank parent does not become a legal binding title");
        primary.SetParentTitle("primary");
        check(!Plan(primary), "Self-parent cycle fails closed");
        primary.SetParentTitle("");
        primary.SetDeJureHolder("other");
        check(!Plan(primary, secondary), "Primary Crown must be fully held");
        check(!Plan(Title("primary", FeudalTitleType.Duchy)), "Ordinary landed estates do not enter Crown planning");
    }
}
