using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using BellumCivile;
using HarmonyLib;
using TaleWorlds.CampaignSystem;

internal static class CrownPartitionReconciliationTests
{
    internal static void Run(Action<bool, string> check)
    {
        FeudalTitleRecord Title(string id, FeudalTitleType rank, string parent, string legal, string actual, string land = "")
            => new FeudalTitleRecord(id, id, rank, legal, actual, parent, land, "origin", 0, 0);
        var titles = new[] {
            Title("primary", FeudalTitleType.Kingdom, "", "ruler", "ruler"),
            Title("second", FeudalTitleType.Kingdom, "", "ruler", "ruler"),
            Title("third", FeudalTitleType.Kingdom, "", "ruler", "ruler"),
            Title("own", FeudalTitleType.Barony, "second", "ruler", "ruler", "f1"),
            Title("occupied", FeudalTitleType.Barony, "second", "foreign", "ruler", "f2"),
            Title("vassal", FeudalTitleType.Barony, "third", "vassal_house", "vassal_house", "f3") };
        var args = new object[] { "ruler", "primary", new Dictionary<string, string> { ["second"] = "a", ["third"] = "b" },
            new Dictionary<string, string[]> { ["ruler"] = new[] { "f1", "f2" }, ["vassal_house"] = new[] { "f3" } }, titles, null, null };
        var assembly = typeof(FeudalTitleRecord).Assembly;
        check((bool)AccessTools.Method(assembly.GetType("BellumCivile.CrownPartitionBatchPlan"), "TryCreate").Invoke(null, args),
            "Reconciliation fixture plans a shared Crown estate");
        var batch = (CrownPartitionBatchRecord)AccessTools.Method(typeof(CrownPartitionBatchRecord), "Capture")
            .Invoke(null, new object[] { "realm", "dead", args[5] });
        var realm = (Kingdom)FormatterServices.GetUninitializedObject(typeof(Kingdom));
        var clan = (Clan)FormatterServices.GetUninitializedObject(typeof(Clan));
        AccessTools.Property(typeof(Kingdom), "StringId").SetValue(realm, "realm");
        AccessTools.Property(typeof(Clan), "StringId").SetValue(clan, "ruler");
        var a = new CrownPartitionPromotionRecord { CrownId = "second", FounderId = "a", SuccessorId = "realm_a", Parent = realm, RetainedHouse = clan,
            EstateTitleIds = new List<string> { "own", "second" }, EstateFiefIds = new List<string> { "f1", "f2" } };
        var b = new CrownPartitionPromotionRecord { CrownId = "third", FounderId = "b", SuccessorId = "realm_b", Parent = realm, RetainedHouse = clan,
            EstateTitleIds = new List<string> { "third" } };
        var promotions = new List<CrownPartitionPromotionRecord> { a, b };
        Clan House(string id)
        {
            var house = (Clan)FormatterServices.GetUninitializedObject(typeof(Clan));
            AccessTools.Property(typeof(Clan), "StringId").SetValue(house, id);
            return house;
        }
        Kingdom Shell(string id)
        {
            var shell = (Kingdom)FormatterServices.GetUninitializedObject(typeof(Kingdom));
            AccessTools.Property(typeof(Kingdom), "StringId").SetValue(shell, id);
            return shell;
        }
        a.Houses.Add(new CrownPartitionHouseRecord { PrincipalTitleId = batch.Principals["a"], Transfer = new RealmUnionClanRecord() });
        b.Houses.Add(new CrownPartitionHouseRecord { PrincipalTitleId = batch.Principals["b"], Transfer = new RealmUnionClanRecord() });
        b.Houses.Add(new CrownPartitionHouseRecord { PrincipalTitleId = batch.Principals["vassal_house"],
            Transfer = new RealmUnionClanRecord { Clan = House("vassal_house"), Holdings = new List<string> { "f3" } } });
        var currentRealms = new Dictionary<string, string> { ["ruler"] = "realm", ["vassal_house"] = "realm" };
        var currentLand = new Dictionary<string, string[]> { ["ruler"] = new[] { "f1", "f2" }, ["vassal_house"] = new[] { "f3" } };
        var physicalOwners = new Dictionary<string, string> { ["f1"] = "ruler", ["f2"] = "ruler", ["f3"] = "vassal_house" };
        var verifyHouses = AccessTools.Method(assembly.GetType("BellumCivile.CrownPartitionBatchHoldings"), "TryVerify");
        bool VerifyHouses() => (bool)verifyHouses.Invoke(null, new object[] { batch, promotions, currentRealms, currentLand, physicalOwners, null });
        check(VerifyHouses(), "Uncreated founders are absent from expected native clan membership");
        var project = AccessTools.Method(assembly.GetType("BellumCivile.CrownPartitionBatchReconciliation"), "TryProjectTitles");
        Dictionary<string, RealmUnionTitleRecord> expected = null;
        bool Project()
        {
            var input = new object[] { batch, promotions, null, null };
            bool ok = (bool)project.Invoke(null, input);
            expected = (Dictionary<string, RealmUnionTitleRecord>)input[2];
            check(ok || expected == null && !string.IsNullOrWhiteSpace(input[3] as string), "Invalid receipts publish no partial expected state");
            return ok;
        }
        check(Project() && expected["own"].ActualHolderId == "ruler", "Unstarted batch projects original estate");
        a.EstateDeliveryStarted = a.FounderCreationStarted = a.FounderInitializationCompleted = a.FounderVerified = true;
        a.Founder = House("a"); a.Houses[0].Transfer.Clan = a.Founder;
        a.EstateReceipts = new List<CrownEstateDeliveryRecord> {
            new CrownEstateDeliveryRecord { AssetId = "f1", Started = true, ActionReturned = true, Verified = true },
            new CrownEstateDeliveryRecord { AssetId = "f2" },
            new CrownEstateDeliveryRecord { AssetId = "own", IsTitle = true },
            new CrownEstateDeliveryRecord { AssetId = "second", IsTitle = true } };
        check(Project() && expected["own"].ActualHolderId == "a" && expected["own"].LegalHolderId == "ruler"
            && expected["third"].ActualHolderId == "ruler", "Partial physical transfer changes possession only and leaves sibling estate untouched");
        currentRealms["a"] = "realm";
        currentLand["ruler"] = new[] { "f2" }; currentLand["a"] = new[] { "f1" }; physicalOwners["f1"] = "a";
        check(VerifyHouses(), "Partial estate delivery independently reconciles donor and founder physical holdings");
        physicalOwners["f1"] = "foreign";
        check(!VerifyHouses(), "Native settlement ownership mismatch blocks even matching clan holding lists");
        physicalOwners["f1"] = "a";
        a.EstateReceipts[1].Started = true;
        check(!Project(), "Interrupted physical action prevents reconciliation");
        a.EstateReceipts[1].ActionReturned = a.EstateReceipts[1].Verified = true;
        currentLand["ruler"] = new string[0]; currentLand["a"] = new[] { "f1", "f2" }; physicalOwners["f2"] = "a";
        check(Project() && expected["occupied"].ActualHolderId == "a" && expected["occupied"].LegalHolderId == "foreign",
            "Occupied fief retains foreign legal ownership after delivery");
        a.EstateReceipts[3].Started = a.EstateReceipts[3].ActionReturned = a.EstateReceipts[3].Verified = true;
        check(!Project(), "Out-of-order legal delivery cannot skip an earlier unstarted asset");
        a.EstateReceipts[2].Started = a.EstateReceipts[2].ActionReturned = a.EstateReceipts[2].Verified = true;
        a.EstateVerified = true;
        check(Project() && expected["own"].LegalHolderId == "a" && expected["second"].ActualHolderId == "a",
            "Verified legal delivery projects inherited Crown and personal rights");
        var projectRegistration = AccessTools.Method(assembly.GetType("BellumCivile.CrownPartitionBatchReconciliation"), "TryProjectCrownRegistration");
        Dictionary<string, RealmUnionTitleRecord> registrationExpected = null;
        bool Registration(CrownPartitionPromotionRecord journal)
        {
            var input = new object[] { batch, promotions, journal, null, null };
            bool ok = (bool)projectRegistration.Invoke(null, input);
            registrationExpected = (Dictionary<string, RealmUnionTitleRecord>)input[3];
            check(ok || registrationExpected == null && !string.IsNullOrEmpty(input[4] as string),
                "Invalid registration prediction publishes no partial snapshot");
            return ok;
        }
        check(!Registration(a), "Registration prediction requires initialized successor shell");
        a.RealmCreationStarted = a.RealmInitializationCompleted = a.RealmVerified = true;
        check(Registration(a) && registrationExpected["second"].OriginRealmId == "realm_a"
            && registrationExpected["third"].OriginRealmId == "origin"
            && registrationExpected["occupied"].LegalHolderId == "foreign",
            "Registration prediction changes only the selected Crown and preserves foreign rights");
        check(!a.CrownRegistrationStarted && !a.CrownRegistrationReturned
            && batch.Titles.Single(t => t.TitleId == "second").OriginRealmId == "origin",
            "Prediction does not forge action receipts or mutate the saved baseline");
        a.CrownRegistrationStarted = true;
        check(!Registration(a), "Interrupted registration cannot be predicted for replay");
        check(!Project(), "Interrupted Crown registration cannot be inferred successful");
        a.CrownRegistrationReturned = a.RealmCreationStarted = a.RealmInitializationCompleted = a.RealmVerified = true;
        a.Successor = Shell("realm_a");
        check(Project() && expected["second"].OriginRealmId == "realm_a" && expected["own"].OriginRealmId == "origin",
            "Registration changes Crown shell association without naturalizing descendant land");
        promotions.Reverse();
        check(Project() && expected["second"].LegalHolderId == "a", "Projection is independent of sibling enumeration order");
        var current = expected.Values.Select(t =>
        {
            var title = Title(t.TitleId, t.Rank, t.LegalParentId, t.LegalHolderId, t.ActualHolderId, t.CapitalId);
            title.SetDeFactoParentTitle(t.ActualParentId); title.SetAssociatedKingdom(t.OriginRealmId);
            return title;
        }).ToList();
        var verify = AccessTools.Method(assembly.GetType("BellumCivile.CrownPartitionBatchReconciliation"), "TryVerifyTitles");
        bool Verify() => (bool)verify.Invoke(null, new object[] { batch, promotions, current, null });
        check(Verify(), "Current title registry matching completed sibling receipts passes reconciliation");
        current.Single(t => t.TitleId == "occupied").SetDeJureHolder("a");
        check(!Verify(), "Unreceipted legal ownership change is rejected");
        current.Single(t => t.TitleId == "occupied").SetDeJureHolder("foreign");
        current.Single(t => t.TitleId == "third").SetParentTitle("second");
        check(!Verify(), "Unreceipted sibling hierarchy change is rejected");
        current.Single(t => t.TitleId == "third").SetParentTitle("");
        current.Add(current[0]);
        check(!Verify(), "Duplicate current title cannot disguise a changed registry");
        current.RemoveAt(current.Count - 1);
        check(Verify(), "Reconciliation never mutates either current titles or original receipts");
        check(batch.Titles.Single(t => t.TitleId == "second").LegalHolderId == "ruler"
            && !b.EstateDeliveryStarted, "Projection does not mutate baseline or sibling receipts");
        b.SuccessorId = a.SuccessorId;
        check(!Project(), "Two Crowns cannot reserve the same successor shell");
        b.SuccessorId = "realm_b";
        b.EstateTitleIds.Add("own");
        check(!Project(), "Sibling cannot claim another Crown's personal title");
        b.EstateTitleIds.Remove("own");
        void Move(CrownPartitionHouseRecord house)
        {
            house.Transfer.ActionStarted = house.Transfer.ActionCompleted = house.Transfer.RestorationStarted = house.Transfer.RestorationCompleted = true;
            house.MovementReturned = house.RestorationReturned = house.PostMoveBalancesCaptured = true;
        }
        a.GovernmentVerified = a.TransferDiplomacyPrepared = true;
        Move(a.Houses[0]); currentRealms["a"] = "realm_a";
        check(VerifyHouses(), "Completed founder join changes expected realm while retaining inherited fiefs");
        a.Houses[0].RestorationReturned = false;
        check(!VerifyHouses(), "Interrupted balance restoration prevents verified house reconciliation");
        a.Houses[0].RestorationReturned = true;
        b.FounderCreationStarted = b.FounderInitializationCompleted = b.FounderVerified = true;
        b.Founder = House("b"); b.Houses[0].Transfer.Clan = b.Founder;
        b.EstateDeliveryStarted = b.EstateVerified = true;
        b.EstateReceipts = new List<CrownEstateDeliveryRecord> { new CrownEstateDeliveryRecord {
            AssetId = "third", IsTitle = true, Started = true, ActionReturned = true, Verified = true } };
        b.RealmCreationStarted = b.RealmInitializationCompleted = b.RealmVerified = b.CrownRegistrationStarted = b.CrownRegistrationReturned = true;
        b.GovernmentVerified = b.TransferDiplomacyPrepared = true; b.Successor = Shell("realm_b");
        b.CrownRegistrationStarted = b.CrownRegistrationReturned = false;
        check(Registration(b) && registrationExpected["second"].OriginRealmId == "realm_a"
            && registrationExpected["own"].LegalHolderId == "a"
            && registrationExpected["third"].OriginRealmId == "realm_b",
            "Second registration preserves a sibling's completed inheritance and shell association");
        b.CrownRegistrationStarted = b.CrownRegistrationReturned = true;
        check(!Registration(b), "Completed registration cannot be started again through batch prediction");
        currentRealms["b"] = "realm"; currentLand["b"] = new string[0];
        check(VerifyHouses(), "Landless sovereign founder can remain in source before recorded movement");
        Move(b.Houses[0]); currentRealms["b"] = "realm_b";
        b.Houses[1].Transfer.ActionStarted = true;
        check(!VerifyHouses(), "Interrupted vassal join cannot be assumed to have completed");
        Move(b.Houses[1]); currentRealms["vassal_house"] = "realm_b";
        check(VerifyHouses() && currentLand["vassal_house"].SequenceEqual(new[] { "f3" }), "Vassal follows its Crown with all physical holdings preserved");
        currentRealms["ruler"] = "realm_b";
        check(!VerifyHouses(), "Primary house cannot move without an authorized assignment");
        currentRealms["ruler"] = "realm";
        currentRealms["extra"] = "realm"; currentLand["extra"] = new string[0];
        check(!VerifyHouses(), "Unrecorded new house blocks a stale batch membership snapshot");
        currentRealms.Remove("extra"); currentLand.Remove("extra");
        check(Project(), "Completed house transfers retain a valid title projection before shared hierarchy");
        batch.PoliticalParentTargets = expected.ToDictionary(pair => pair.Key, pair => pair.Value.ActualParentId);
        batch.HierarchyStarted = true;
        check(!Project(), "Interrupted shared hierarchy cannot be mistaken for a completed sibling change");
        batch.HierarchyReturned = true;
        check(Project() && VerifyHouses(), "Shared hierarchy verifies after all houses and estates have completed");
        batch.PoliticalParentTargets["own"] = "third";
        check(!Project(), "Political ancestry cannot attach a title to the wrong sibling realm");
        batch.PoliticalParentTargets["own"] = "second";
        batch.PoliticalParentTargets["second"] = "primary";
        check(!Project(), "Separating Crown cannot remain politically subordinate to primary Crown");
        batch.PoliticalParentTargets["second"] = "";
        batch.PoliticalParentTargets["own"] = "";
        check(!Project(), "Subordinate fief cannot silently detach from its successor's political tree");
        batch.PoliticalParentTargets["own"] = "second";
        b.Houses[1].Transfer.RestorationCompleted = false;
        check(!Project(), "Final shared hierarchy waits for every vassal restoration receipt");
        b.Houses[1].Transfer.RestorationCompleted = true;
        check(Project() && expected["occupied"].LegalHolderId == "foreign"
            && expected["own"].LegalParentId == "second", "Final shared hierarchy preserves original legal rights and ancestry");
        a.HierarchyStarted = true;
        check(!Project(), "Estate projection does not silently accept unsupported later hierarchy changes");
    }
}
