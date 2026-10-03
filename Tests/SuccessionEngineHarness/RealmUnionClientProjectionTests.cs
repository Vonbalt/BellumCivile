using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using BellumCivile;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;

internal static class RealmUnionClientProjectionTests
{
    internal static void Run(Action<bool, string> check)
    {
        Kingdom Realm(string id)
        {
            var realm = (Kingdom)FormatterServices.GetUninitializedObject(typeof(Kingdom));
            AccessTools.Property(typeof(Kingdom), "StringId").SetValue(realm, id);
            return realm;
        }
        var assembly = typeof(RealmUnionRecord).Assembly;
        var capture = AccessTools.Method(assembly.GetType("BellumCivile.RealmUnionClientSnapshot"), "TryCapture");
        Dictionary<string, string> Snapshot(List<ClientKingdomRecord> records, string id)
        {
            var args = new object[] { records, id, null, null, null };
            check((bool)capture.Invoke(null, args), "Client projection fixture has a complete snapshot");
            return (Dictionary<string, string>)args[3];
        }
        var journal = new RealmUnionRecord { Source = Realm("source"), Destination = Realm("destination"),
            SourceClientRecords = new List<ClientKingdomRecord> { new ClientKingdomRecord("client", "source", 25, false, 90) },
            DestinationClientRecords = new List<ClientKingdomRecord> { new ClientKingdomRecord("existing", "destination", 10, true, 30) } };
        journal.SourceObligations = Snapshot(journal.SourceClientRecords, "source");
        journal.DestinationObligations = Snapshot(journal.DestinationClientRecords, "destination");
        journal.SourceObligations.Add("trade:client", "300");
        journal.SourceObligations.Add("alliance:client", "400");
        var plan = AccessTools.Method(assembly.GetType("BellumCivile.RealmUnionClientProjection"), "TryPlan");
        var argsPlan = new object[] { journal, null, null, null };
        check((bool)plan.Invoke(null, argsPlan), "Client registry inheritance has a complete prewrite projection");
        var source = (Dictionary<string, string>)argsPlan[1];
        var destination = (Dictionary<string, string>)argsPlan[2];
        check(source.Count == 2 && destination["client:client"] == "destination"
            && destination["client-start:client"] == "25" && destination["client-cooldown:client"] == "90"
            && destination["client-voluntary:client"] == "false" && destination["client:existing"] == "destination",
            "Projected client registry preserves both houses' client history while agreements remain pending");
        var rules = assembly.GetType("BellumCivile.RealmUnionObligationRules");
        bool Verify(string method, Dictionary<string, string> s, Dictionary<string, string> d)
            => (bool)AccessTools.Method(rules, method).Invoke(null, new object[] { journal, s, d, null });
        check(Verify("VerifyProgress", journal.SourceObligations, journal.DestinationObligations), "Unstarted client journal matches original registry");
        check(!Verify("VerifyProgress", source, destination), "Registry state alone cannot substitute for client receipts");
        journal.ClientTransferStarted = true;
        check(!Verify("VerifyProgress", source, destination), "Interrupted client transfer cannot be inferred complete");
        journal.ClientTransferReturned = true;
        check(Verify("VerifyProgress", source, destination), "Returned client receipt verifies registry-only progress");
        check(!Verify("VerifyUnchanged", source, destination), "Client registry alone cannot authorize Crown transfer before protected agreements");
        journal.TradeTransferStarted = journal.TradeTransferReturned = true;
        source.Remove("trade:client");
        destination.Add("trade:client", "300");
        check(Verify("VerifyProgress", source, destination), "Client and trade receipts compose without losing history");
        journal.AllianceTransferStarted = journal.AllianceTransferReturned = true;
        source.Remove("alliance:client");
        destination.Add("alliance:client", "400");
        check(Verify("VerifyUnchanged", source, destination), "Completed protected agreements settle client inheritance obligations");
        destination["client-cooldown:client"] = "91";
        check(!Verify("VerifyProgress", source, destination), "Post-transfer client history mutation prevents replay or completion");
        destination["client-cooldown:client"] = "90";
        journal.ClientTransferStarted = journal.ClientTransferReturned = false;
        check(!Verify("VerifyProgress", source, destination), "Trade cannot bypass required client inheritance receipts");
        journal.TradeTransferStarted = journal.TradeTransferReturned = false;
        journal.AllianceTransferStarted = journal.AllianceTransferReturned = false;
        journal.SourceObligations.Remove("alliance:client");
        check(!(bool)plan.Invoke(null, argsPlan), "Missing protected alliance defers client inheritance before writes");
        journal.SourceObligations.Add("alliance:client", "400");

        var behavior = new CrownAccessionBehavior();
        var accession = new CrownAccessionRecord { Union = journal };
        AccessTools.Field(typeof(CrownAccessionBehavior), "_accessions").SetValue(behavior, new List<CrownAccessionRecord> { accession });
        bool Protected(string id) => (bool)AccessTools.Method(typeof(CrownAccessionBehavior), "IsRealmUnionClientProtected")
            .Invoke(behavior, new object[] { id });
        check(!Protected("client"), "Unstarted client snapshot does not suppress maintenance");
        journal.ClientTransferStarted = true;
        check(Protected("client") && Protected("existing") && !Protected("other"),
            "Started registry transfer protects inherited and existing clients but not unrelated realms");
        check((bool)AccessTools.Method(typeof(CrownAccessionBehavior), "IsRealmUnionProtected")
            .Invoke(behavior, new object[] { journal.Source }), "Client registry write activates source retirement protection");
        accession.Completed = true;
        check(Protected("client"), "Outer accession completion cannot release an unfinished client transfer");
        journal.Completed = true;
        check(!Protected("client") && !Protected("existing"), "Completed union releases client maintenance protection");
    }
}
