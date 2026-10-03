using System;
using System.Collections.Generic;
using System.Linq;
using BellumCivile;
using HarmonyLib;

internal static class RealmUnionCrownTests
{
    internal static void Run(Action<bool, string> check)
    {
        var helper = typeof(RealmUnionRecord).Assembly.GetType("BellumCivile.RealmUnionCrownTransfer");
        var verify = AccessTools.Method(helper, "VerifyTitles");
        var execute = AccessTools.Method(helper, "Execute");
        var titles = new Dictionary<string, FeudalTitleRecord> {
            ["secondary"] = new FeudalTitleRecord("secondary", "Second Crown", FeudalTitleType.Kingdom, "old", "old", "", "capital2", "realm2", 0, 0),
            ["primary"] = new FeudalTitleRecord("primary", "First Crown", FeudalTitleType.Kingdom, "new", "new", "", "capital1", "realm1", 0, 0),
            ["vassal"] = new FeudalTitleRecord("vassal", "County", FeudalTitleType.County, "vassal", "vassal", "secondary", "town", "realm2", 0, 0)
        };
        var capture = AccessTools.Method(typeof(RealmUnionTitleRecord), "Capture");
        var journal = new RealmUnionRecord { InheritedCrownId = "secondary", PrimaryCrownId = "primary",
            Titles = titles.Values.Select(t => (RealmUnionTitleRecord)capture.Invoke(null, new object[] { t })).ToList() };
        bool Verify(bool after) => (bool)verify.Invoke(null, new object[] { journal, "old", "new",
            (Func<string, FeudalTitleRecord>)(id => titles.TryGetValue(id, out var title) ? title : null), after, null });
        int calls = 0;
        Action transfer = () => { calls++; titles["secondary"].SetDeJureHolder("new"); titles["secondary"].SetDeFactoHolder("new"); };
        bool Run() => (bool)execute.Invoke(null, new object[] { journal, (Func<bool>)(() => Verify(false)), transfer,
            (Func<bool>)(() => Verify(true)), null });
        check(Verify(false) && !Verify(true), "Crown baseline verifies only before transfer");
        check(Run() && Run() && calls == 1 && journal.CrownTransferReturned && journal.CrownVerified,
            "Secondary Crown ownership transfers once and verifies without replay");
        check(titles["vassal"].ParentTitleId == "secondary" && titles["secondary"].AssociatedKingdomId == "realm2"
            && titles["primary"].DeJureHolderClanId == "new", "Union preserves vassal hierarchy, legal origin and primary Crown");
        titles["vassal"].SetDeFactoParentTitle("primary");
        check(!Run() && calls == 1, "Unintended political reparenting blocks completion without replay");
        titles["vassal"].SetDeFactoParentTitle("secondary");
        titles["secondary"].SetAssociatedKingdom("realm1");
        check(!Verify(true), "Union cannot rewrite secondary Crown legal origin");
        titles["secondary"].SetAssociatedKingdom("realm2");
        titles["secondary"].SetCapitalSettlement("other");
        check(!Verify(true), "Union cannot silently relocate the Crown capital");
        titles["secondary"].SetCapitalSettlement("capital2");
        journal.Titles.Add(journal.Titles[0]);
        check(!Verify(true), "Duplicate title snapshots fail closed");
        journal.Titles.RemoveAt(journal.Titles.Count - 1);
        check(Verify(true), "Expected inherited Crown state remains valid");
        journal = new RealmUnionRecord();
        calls = 0;
        transfer = () => { calls++; throw new InvalidOperationException("interrupted after holder changed"); };
        Func<bool> yes = () => true;
        bool Interrupted() => (bool)execute.Invoke(null, new object[] { journal, yes, transfer, yes, null });
        check(!Interrupted() && !Interrupted() && calls == 1 && !journal.CrownVerified,
            "Interrupted Crown callbacks cannot be replayed or inferred complete from ownership");
        journal = new RealmUnionRecord { CrownVerified = true };
        check(!Interrupted() && calls == 1, "Verified flag alone cannot authorize Crown recovery");
    }
}
