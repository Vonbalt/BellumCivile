using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using BellumCivile;
using HarmonyLib;
using TaleWorlds.CampaignSystem;

internal static class PartitionEstateManifestTests
{
    internal static void Run(Action<bool, string> check)
    {
        Hero Person(string id)
        {
            var h = (Hero)FormatterServices.GetUninitializedObject(typeof(Hero));
            AccessTools.Property(typeof(Hero), "StringId").SetValue(h, id); return h;
        }
        var assembly = typeof(FeudalTitleRecord).Assembly;
        var planType = assembly.GetType("BellumCivile.FeudalInheritancePlan");
        var packageType = assembly.GetType("BellumCivile.FeudalInheritancePackage");
        var plan = Activator.CreateInstance(planType);
        var clan = (Clan)FormatterServices.GetUninitializedObject(typeof(Clan));
        AccessTools.Property(planType, "ParentClan").SetValue(plan, clan);
        AccessTools.Property(planType, "DeadLeader").SetValue(plan, Person("dead"));
        FeudalTitleRecord Title(string id, FeudalTitleType rank) => new FeudalTitleRecord(id, id, rank, "house", "house", "", "", "realm", 0, 0);
        var primaryCrown = Title("primary", FeudalTitleType.Kingdom);
        var secondCrown = Title("second", FeudalTitleType.Kingdom);
        var duchy = Title("duchy", FeudalTitleType.Duchy);
        var titles = (IList)AccessTools.Property(planType, "EstateTitles").GetValue(plan);
        titles.Add(primaryCrown); titles.Add(secondCrown); titles.Add(duchy);
        AccessTools.Property(planType, "PrimarySovereignTitle").SetValue(plan, primaryCrown);
        var chain = (IList)AccessTools.Property(planType, "PrimaryTitleChain").GetValue(plan); chain.Add(primaryCrown);
        object Package(FeudalTitleRecord root)
        {
            var p = Activator.CreateInstance(packageType);
            AccessTools.Property(packageType, "RootTitle").SetValue(p, root);
            ((IList)AccessTools.Property(packageType, "Titles").GetValue(p)).Add(root);
            return p;
        }
        var crownPackage = Package(secondCrown); var lesserPackage = Package(duchy);
        var packages = (IList)AccessTools.Property(planType, "SecondaryPackages").GetValue(plan);
        packages.Add(crownPackage); packages.Add(lesserPackage);
        var primary = Person("primary_heir"); var elder = Person("elder"); var younger = Person("younger");
        var capture = AccessTools.Method(assembly.GetType("BellumCivile.PartitionEstateManifest"), "TryCapture");
        List<CrossClanEstateShare> shares = null;
        bool Capture(params Hero[] heirs)
        {
            var args = new object[] { plan, primary, heirs, null, null };
            bool ok = (bool)capture.Invoke(null, args); shares = (List<CrossClanEstateShare>)args[3];
            check(ok || shares == null && !string.IsNullOrEmpty(args[4] as string), "Invalid estate capture publishes no partial shares");
            return ok;
        }
        check(Capture(elder, younger) && shares.Count == 3 && shares[0].Primary
            && shares[1].Heir == elder && shares[1].RootTitleId == "second"
            && shares[2].Heir == younger && shares[2].RootTitleId == "duchy",
            "Complete estate assigns Crown before lesser package in heir order");
        check(shares.SelectMany(s => s.Titles).Distinct().Count() == 3
            && shares.Sum(s => s.Titles.Count) == 3, "Complete manifest covers each title exactly once");
        shares[1].Titles.Clear();
        check(Capture(elder) && shares[0].Titles.Contains("duchy") && shares[1].Titles.Contains("second"),
            "Unassigned lesser estate stays primary and captured lists cannot mutate the planner");
        check(Capture() && shares.Count == 1 && shares[0].Titles.Count == 3, "No secondary heirs leaves full estate with primary");
        check(!Capture(elder, elder), "Duplicate heirs cannot receive multiple packages");
        packages[0] = lesserPackage; packages[1] = crownPackage;
        check(!Capture(elder, younger), "Lesser package cannot precede a separating Crown");
        packages[0] = crownPackage; packages[1] = crownPackage;
        check(!Capture(elder, younger), "Overlapping sibling packages cannot duplicate ownership");
        packages[1] = Package(primaryCrown);
        check(!Capture(elder, younger), "Primary Crown cannot leave the retained share");
        packages[1] = Package(Title("foreign", FeudalTitleType.Duchy));
        check(!Capture(elder, younger), "Package outside recorded estate is rejected");

        var pending = new PendingPartitionSuccessionRecord("dead", "house", "realm", "fief", "younger", default(CampaignTime));
        pending.CrownBatch = new CrownPartitionBatchRecord { SourceRealmId = "realm", RetainedHouseId = "house",
            HierarchyVerified = true, Recipients = new Dictionary<string, string>() };
        var recipient = (Clan)FormatterServices.GetUninitializedObject(typeof(Clan));
        AccessTools.Property(typeof(Clan), "StringId").SetValue(recipient, "cadet");
        var share = new CrossClanEstateShare { Heir = younger, RootTitleId = "duchy",
            Titles = new List<string> { "duchy" }, Fiefs = new List<string> { "fief" },
            PartitionFounderStarted = true, PartitionFounderReturned = true, Recipient = recipient,
            CadetPlan = new CrownAccessionRecord { Cadet = recipient, CadetId = "cadet", Heir = younger, CadetInitialized = true } };
        pending.EstateShares = new List<CrossClanEstateShare> { share };
        var project = AccessTools.Method(assembly.GetType("BellumCivile.PartitionLesserEstateProjection"), "TryApply");
        Dictionary<string, RealmUnionTitleRecord> projected = null;
        Dictionary<string, List<string>> land = null;
        Dictionary<string, string> realms = null;
        bool Project()
        {
            projected = new Dictionary<string, RealmUnionTitleRecord> {
                ["duchy"] = new RealmUnionTitleRecord { TitleId = "duchy", Rank = FeudalTitleType.Duchy,
                    LegalHolderId = "house", ActualHolderId = "house", ActualParentId = "primary" },
                ["occupied"] = new RealmUnionTitleRecord { TitleId = "occupied", Rank = FeudalTitleType.Barony,
                    LegalHolderId = "foreign", ActualHolderId = "house", CapitalId = "fief" } };
            realms = new Dictionary<string, string> { ["house"] = "realm" };
            land = new Dictionary<string, List<string>> { ["house"] = new List<string> { "fief" } };
            return (bool)project.Invoke(null, new object[] { pending, projected, realms, land, null });
        }
        check(Project() && realms["cadet"] == "realm" && land["cadet"].Count == 0,
            "Initialized lesser house joins expected membership without invented land");
        share.PartitionFounderReturned = false;
        check(!Project(), "Interrupted lesser founder cannot be inferred complete");
        share.PartitionFounderReturned = true;
        share.PartitionReceipts = new List<CrownEstateDeliveryRecord> {
            new CrownEstateDeliveryRecord { AssetId = "fief", Started = true, ActionReturned = true, Verified = true },
            new CrownEstateDeliveryRecord { AssetId = "duchy", IsTitle = true } };
        check(Project() && projected["occupied"].ActualHolderId == "cadet"
            && projected["occupied"].LegalHolderId == "foreign" && land["house"].Count == 0,
            "Lesser physical transfer preserves foreign legal rights and debits source holdings");
        share.PartitionReceipts[1].Started = true;
        check(!Project(), "Interrupted lesser title delivery blocks reconciliation");
        share.PartitionReceipts[1].ActionReturned = share.PartitionReceipts[1].Verified = true;
        share.LandedSettled = true;
        check(Project() && projected["duchy"].LegalHolderId == "cadet"
            && projected["duchy"].ActualParentId == "primary", "Completed lesser estate preserves political ancestry");
        share.PartitionReceipts.Reverse();
        check(!Project(), "Reordered lesser asset receipts cannot bypass physical-first delivery");
        share.PartitionReceipts.Reverse();
        pending.CrownBatch.HierarchyVerified = false;
        check(!Project(), "Lesser mutation cannot precede shared Crown hierarchy verification");
    }
}
