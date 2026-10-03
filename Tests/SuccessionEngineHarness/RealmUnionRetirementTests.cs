using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using BellumCivile;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;

internal static class RealmUnionRetirementTests
{
    internal static void Run(Action<bool, string> check)
    {
        CheckExecution(check);
        var verify = AccessTools.Method(typeof(RealmUnionRecord).Assembly.GetType("BellumCivile.RealmUnionRetirementRules"), "VerifyReceipts");
        RealmUnionClanRecord Delivered(string id, bool mercenary)
        {
            var clan = (Clan)FormatterServices.GetUninitializedObject(typeof(Clan));
            AccessTools.Property(typeof(Clan), "StringId").SetValue(clan, id);
            return new RealmUnionClanRecord { Clan = clan, EndMercenaryContract = mercenary, ActionStarted = true,
                ActionReturned = true, ActionCompleted = true, PostMoveCaptured = true,
                RestorationStarted = !mercenary, RestorationReturned = !mercenary, RestorationCompleted = !mercenary };
        }
        var noble = Delivered("noble", false);
        var company = Delivered("company", true);
        var journal = new RealmUnionRecord { CrownTransferStarted = true, CrownTransferReturned = true,
            CrownVerified = true, ObligationsSettled = true, Clans = new List<RealmUnionClanRecord> { noble, company } };
        bool Verify() => (bool)verify.Invoke(null, new object[] { journal, null });
        check(Verify(), "Retirement prerequisites accept restored nobles and released companies");
        foreach (string fieldName in new[] { "CrownTransferStarted", "CrownTransferReturned", "CrownVerified", "ObligationsSettled" })
        {
            var field = AccessTools.Field(typeof(RealmUnionRecord), fieldName);
            field.SetValue(journal, false);
            check(!Verify(), "Retirement rejects missing " + fieldName);
            field.SetValue(journal, true);
        }
        foreach (string fieldName in new[] { "ActionStarted", "ActionReturned", "ActionCompleted", "PostMoveCaptured",
            "RestorationStarted", "RestorationReturned", "RestorationCompleted" })
        {
            var field = AccessTools.Field(typeof(RealmUnionClanRecord), fieldName);
            field.SetValue(noble, false);
            check(!Verify(), "Retirement rejects incomplete noble receipt " + fieldName);
            field.SetValue(noble, true);
        }
        company.RestorationStarted = true;
        check(!Verify(), "Released mercenaries cannot carry noble restoration receipts");
        company.RestorationStarted = false;
        company.PostMoveInfluence = float.NaN;
        check(!Verify(), "Invalid native post-contract balance prevents retirement");
        company.PostMoveInfluence = 0;
        noble.Holdings.Add("town");
        noble.Holdings.Add("town");
        check(!Verify(), "Duplicate retained holdings invalidate retirement evidence");
        noble.Holdings.Clear();
        journal.Clans.Add(noble);
        check(!Verify(), "Duplicate clan delivery receipts cannot authorize retirement");
        journal.Clans.RemoveAt(2);
        journal.Clans.Remove(noble);
        check(!Verify(), "Mercenary-only snapshot cannot authorize hereditary absorption");
        journal.Clans.Add(noble);
        journal.SourceRetired = true;
        check(!Verify(), "Already retired source cannot enter initial retirement validation");
        journal.SourceRetired = false;
        journal.Completed = true;
        check(!Verify(), "Completed union cannot authorize another retirement");
        var behavior = new CrownAccessionBehavior();
        var args = new object[] { new CrownAccessionRecord { Union = journal }, null };
        check(!(bool)AccessTools.Method(typeof(CrownAccessionBehavior), "TryValidateRealmUnionForRetirement").Invoke(behavior, args)
            && !string.IsNullOrEmpty(args[1] as string), "Live validator refuses unregistered journal before touching campaign state");
    }

    private static void CheckExecution(Action<bool, string> check)
    {
        var assembly = typeof(RealmUnionRecord).Assembly;
        var execute = AccessTools.Method(assembly.GetType("BellumCivile.RealmUnionRetirement"), "Execute");
        var journal = new RealmUnionRecord();
        int calls = 0;
        bool ready = false, eliminated = false, interrupt = false, blocked = false;
        bool Run()
        {
            Action retire = () => { calls++; eliminated = !blocked; if (interrupt) throw new InvalidOperationException("callback interrupted"); };
            var args = new object[] { journal, (Func<bool>)(() => ready), retire, (Func<bool>)(() => eliminated), null };
            bool ok = (bool)execute.Invoke(null, args);
            check(ok || !string.IsNullOrEmpty(args[4] as string), "Retirement failures explain why completion was refused");
            return ok;
        }
        check(!Run() && calls == 0 && !journal.RetirementStarted, "Failed preflight never begins native destruction");
        ready = true;
        interrupt = true;
        check(!Run() && eliminated && journal.RetirementStarted && !journal.RetirementReturned && !journal.SourceRetired,
            "Elimination before callback failure does not publish a completed retirement");
        interrupt = false;
        check(!Run() && calls == 1, "Interrupted native destruction is not replayed from visible elimination");
        journal = new RealmUnionRecord();
        eliminated = false;
        blocked = true;
        check(!Run() && journal.RetirementReturned && !journal.SourceRetired, "A blocked native call cannot mark a live source retired");
        blocked = false;
        check(!Run() && calls == 2, "Returned but unverified retirement is not blindly replayed");
        journal = new RealmUnionRecord();
        check(Run() && journal.SourceRetired && journal.RetirementReturned && calls == 3,
            "Successful retirement publishes return and post-verification receipts");
        check(Run() && calls == 3, "Verified retirement can resume without another destruction");
        eliminated = false;
        check(!Run() && calls == 3, "Resumed retired receipt still requires current-state verification");
        journal = new RealmUnionRecord { SourceRetired = true };
        check(!Run() && calls == 3, "Retired flag alone cannot authorize retirement recovery");

        var verify = AccessTools.Method(assembly.GetType("BellumCivile.RealmUnionObligationRules"), "VerifyRetired");
        journal = new RealmUnionRecord { RetirementStarted = true, RetirementReturned = true,
            SourceObligations = new Dictionary<string, string> { ["war:enemy"] = "active" },
            DestinationObligations = new Dictionary<string, string> { ["war:enemy"] = "active", ["alliance:ally"] = "100" } };
        var source = new Dictionary<string, string>();
        var destination = new Dictionary<string, string>(journal.DestinationObligations);
        bool Verify() => (bool)verify.Invoke(null, new object[] { journal, source, destination, null });
        check(Verify(), "Retirement removes only source war stances while retaining destination war and alliance");
        destination.Remove("war:enemy");
        check(!Verify(), "Retirement cannot silently end the surviving realm's war");
        destination["war:enemy"] = "active";
        destination["alliance:ally"] = "101";
        check(!Verify(), "Changed surviving agreement after destruction blocks final verification");
        destination["alliance:ally"] = "100";
        source["client:leftover"] = "source";
        check(!Verify(), "Any leftover source obligation blocks retirement completion");
        source.Clear();
        journal.RetirementReturned = false;
        check(!Verify(), "Post-retirement obligations cannot replace a missing native return receipt");

        var behavior = new CrownAccessionBehavior();
        var realm = (Kingdom)FormatterServices.GetUninitializedObject(typeof(Kingdom));
        var consume = AccessTools.Method(typeof(CrownAccessionBehavior), "TryConsumeRealmUnionRetirementAuthorization");
        check(!(bool)consume.Invoke(behavior, new object[] { realm }), "No runtime authorization means destruction stays blocked");
        var authorization = AccessTools.Field(typeof(CrownAccessionBehavior), "_realmUnionRetirementAuthorization");
        authorization.SetValue(behavior, new RealmUnionRecord { Source = realm, RetirementStarted = true, Completed = true });
        check(!(bool)consume.Invoke(behavior, new object[] { realm }) && authorization.GetValue(behavior) == null,
            "Invalid authorization is consumed and cannot be reused");
        var argsNative = new object[] { new CrownAccessionRecord { Union = journal }, null };
        check(!(bool)AccessTools.Method(typeof(CrownAccessionBehavior), "TryRetireRealmUnionSource").Invoke(behavior, argsNative),
            "Native retirement rejects unregistered journals before campaign operations");
    }
}
