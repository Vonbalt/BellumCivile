using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using TaleWorlds.CampaignSystem;
using BellumCivile;
using BellumCivile.Behaviors;
using HarmonyLib;

internal static class RealmUnionObligationTests
{
    internal static void Run(Action<bool, string> check)
    {
        var verify = AccessTools.Method(typeof(RealmUnionRecord).Assembly.GetType("BellumCivile.RealmUnionObligationRules"), "VerifyUnchanged");
        var source = new Dictionary<string, string>();
        var destination = new Dictionary<string, string> { ["alliance:ally"] = "200", ["client:client"] = "primary",
            ["client-start:client"] = "10", ["client-voluntary:client"] = "true", ["client-cooldown:client"] = "30" };
        var primary = (Kingdom)FormatterServices.GetUninitializedObject(typeof(Kingdom));
        AccessTools.Property(typeof(Kingdom), "StringId").SetValue(primary, "primary");
        var journal = new RealmUnionRecord { Destination = primary,
            DestinationClientRecords = new List<ClientKingdomRecord> { new ClientKingdomRecord("client", "primary", 10, true, 30) } };
        bool Verify() => (bool)verify.Invoke(null, new object[] { journal, source, destination, null });
        void Capture()
        {
            journal.SourceObligations = new Dictionary<string, string>(source);
            journal.DestinationObligations = new Dictionary<string, string>(destination);
        }
        check(!Verify(), "Missing obligation snapshot cannot authorize absorption");
        Capture();
        check(Verify(), "Unchanged primary agreements survive an obligation-free source");
        destination["alliance:ally"] = "201";
        check(!Verify(), "Renewed primary alliance requires reconciliation");
        Capture();
        source["war:enemy"] = "active";
        Capture();
        check(!Verify(), "Unmatched inherited war blocks movement");
        destination["war:enemy"] = "active";
        Capture();
        check(Verify(), "Already aligned foreign wars pass obligation preflight");
        foreach (string key in new[] { "alliance:ally", "trade:ally", "tribute:ally", "legacy-tribute:0", "client:client" })
        {
            source[key] = "obligation";
            Capture();
            check(!Verify(), "Source commitment cannot disappear through absorption: " + key);
            source.Remove(key);
        }
        Capture();
        destination.Remove("client:client");
        check(!Verify(), "Missing primary client blocks absorption verification");

        var treaties = new ForeignTreatyBehavior();
        var type = typeof(ForeignTreatyBehavior);
        var pending = AccessTools.Method(type, "HasPendingRealmUnionSettlement");
        bool Pending(string id) => (bool)pending.Invoke(treaties, new object[] { id });
        var proposal = new TreatyProposalRecord("war", "source", "enemy", "source", 0, 50, false);
        AccessTools.Field(type, "_proposals").SetValue(treaties, new List<TreatyProposalRecord> { proposal });
        check(Pending("source") && Pending("enemy") && !Pending("unrelated"), "Both parties to a live parley block absorption");
        AccessTools.Field(typeof(TreatyProposalRecord), "_state").SetValue(proposal, TreatyProposalState.Applied);
        check(!Pending("source"), "Historical applied treaties do not block absorption alone");
        AccessTools.Field(type, "_pendingRebelResolutions").SetValue(treaties,
            new List<PendingTreatyRebelResolutionRecord> { new PendingTreatyRebelResolutionRecord("p", "rebel", "parent", "beneficiary") });
        check(Pending("parent") && Pending("rebel") && Pending("beneficiary"), "Deferred rebel settlement protects all affected realms");
        AccessTools.Field(type, "_activeTributes").SetValue(treaties, new List<ActiveTreatyTributeRecord> {
            new ActiveTreatyTributeRecord("source", "enemy", 10, 5),
            new ActiveTreatyTributeRecord("enemy", "source", 20, 8),
            new ActiveTreatyTributeRecord("source", "enemy", 30, 0) });
        var tributes = (IReadOnlyList<ActiveTreatyTributeRecord>)AccessTools.Method(type, "GetRealmUnionTributes")
            .Invoke(treaties, new object[] { "source" });
        check(tributes.Count == 2, "Legacy tribute inspection includes payer and recipient, excludes expired obligations");
        CheckTradeProjection(check);
    }

    private static void CheckTradeProjection(Action<bool, string> check)
    {
        var type = typeof(RealmUnionRecord).Assembly.GetType("BellumCivile.RealmUnionObligationRules");
        var project = AccessTools.Method(type, "TryProjectTrade");
        var verify = AccessTools.Method(type, "VerifyUnchanged");
        var source = new Dictionary<string, string> { ["trade:shared"] = "90", ["trade:new"] = "150", ["war:enemy"] = "active" };
        var destination = new Dictionary<string, string> { ["trade:shared"] = "100", ["alliance:ally"] = "200", ["war:enemy"] = "active" };
        Dictionary<string, string> afterSource = null, afterDestination = null;
        bool Project()
        {
            var args = new object[] { source, destination, null, null, null };
            bool ok = (bool)project.Invoke(null, args);
            afterSource = (Dictionary<string, string>)args[2];
            afterDestination = (Dictionary<string, string>)args[3];
            check(ok || afterSource == null && afterDestination == null && !string.IsNullOrEmpty(args[4] as string),
                "Invalid obligation projection leaves no partial expected state");
            return ok;
        }
        check(Project() && afterSource.Count == 1 && afterDestination["trade:shared"] == "100"
            && afterDestination["trade:new"] == "150" && afterDestination["alliance:ally"] == "200",
            "Trade projection transfers terms without changing other primary obligations");
        check(source.Count == 3 && destination.Count == 3, "Trade projection preserves original captured obligations");
        var journal = new RealmUnionRecord { SourceObligations = source, DestinationObligations = destination };
        bool Verify() => (bool)verify.Invoke(null, new object[] { journal, afterSource, afterDestination, null });
        check(!Verify(), "Changed native agreements cannot substitute for transfer receipts");
        journal.TradeTransferStarted = true;
        check(!Verify(), "Started-only trade transfer cannot be inferred complete");
        journal.TradeTransferReturned = true;
        check(Verify(), "Returned trade receipt verifies projected commitments");
        var restored = new RealmUnionRecord();
        foreach (var field in typeof(RealmUnionRecord).GetFields()) field.SetValue(restored, field.GetValue(journal));
        journal = restored;
        check(Verify(), "Reconstructed saved trade receipts preserve expected obligations");
        afterDestination["trade:new"] = "151";
        check(!Verify(), "Post-transfer expiry changes defer rather than replay native write");
        journal.TradeTransferStarted = false;
        check(!Verify(), "Returned-without-start trade receipt is rejected");
        source["trade:new"] = "NaN";
        check(!Project(), "Non-finite trade expiry cannot enter a plan");
        source["trade:new"] = "150";
        source["tribute:ally"] = "10";
        check(!Project(), "Unimplemented source tribute prevents partial trade-only absorption");
        source.Remove("tribute:ally");
        source["war:new"] = destination["war:new"] = "active";
        check(!Project(), "Matching war sets still cannot authorize trade with an enemy");
    }
}
