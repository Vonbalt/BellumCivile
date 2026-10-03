using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using BellumCivile;
using HarmonyLib;
using TaleWorlds.CampaignSystem;

internal static class RealmUnionLegacyTributeProjectionTests
{
    internal static void Run(Action<bool, string> check)
    {
        Kingdom Realm(string id)
        {
            var realm = (Kingdom)FormatterServices.GetUninitializedObject(typeof(Kingdom));
            AccessTools.Property(typeof(Kingdom), "StringId").SetValue(realm, id);
            return realm;
        }
        var type = typeof(RealmUnionRecord).Assembly.GetType("BellumCivile.RealmUnionLegacyTributeProjection");
        var rules = typeof(RealmUnionRecord).Assembly.GetType("BellumCivile.RealmUnionObligationRules");
        var original = new List<ActiveTreatyTributeRecord> { new ActiveTreatyTributeRecord("source", "partner", 100, 75),
            new ActiveTreatyTributeRecord("other", "source", 20, 12) };
        var copied = (List<ActiveTreatyTributeRecord>)AccessTools.Method(type, "Copy").Invoke(null, new object[] { original });
        original[0].TickDay();
        check(copied[0].RemainingDays == 75 && original[0].RemainingDays == 74, "Legacy capture deep-copies mutable remaining-day schedules");
        Dictionary<string, string> Snapshot(List<ActiveTreatyTributeRecord> records, string realm) =>
            (Dictionary<string, string>)AccessTools.Method(type, "Snapshot").Invoke(null, new object[] { records, realm });
        var journal = new RealmUnionRecord { Source = Realm("source"), Destination = Realm("destination"),
            SourceLegacyTributes = copied, DestinationLegacyTributes = new List<ActiveTreatyTributeRecord> {
                new ActiveTreatyTributeRecord("destination", "partner", 10, 3) } };
        journal.SourceObligations = Snapshot(journal.SourceLegacyTributes, "source");
        journal.DestinationObligations = Snapshot(journal.DestinationLegacyTributes, "destination");
        List<ActiveTreatyTributeRecord> projected = null;
        bool Prepare()
        {
            var args = new object[] { journal, null, null };
            bool ok = (bool)AccessTools.Method(type, "TryPrepare").Invoke(null, args);
            projected = (List<ActiveTreatyTributeRecord>)args[1];
            return ok;
        }
        check(Prepare() && projected.Count == 3, "Structured legacy records project all distinct payment schedules");
        var source = new Dictionary<string, string>();
        var destination = Snapshot(projected, "destination");
        bool Verify() => (bool)AccessTools.Method(rules, "VerifyUnchanged").Invoke(null, new object[] { journal, source, destination, null });
        check(!Verify(), "Moved legacy schedules require recorded delivery");
        journal.LegacyTributeTransferStarted = true;
        check(!Verify(), "Interrupted legacy write cannot be inferred complete");
        journal.LegacyTributeTransferReturned = true;
        check(Verify(), "Returned legacy schedule transfer verifies final obligations");
        var restored = new RealmUnionRecord();
        foreach (var field in typeof(RealmUnionRecord).GetFields()) field.SetValue(restored, field.GetValue(journal));
        journal = restored;
        check(Verify(), "Reconstructed journal retains structured legacy projection");
        projected[0].TickDay();
        destination = Snapshot(projected, "destination");
        check(!Verify(), "Subsequent daily collection is detected instead of restoring old remaining days");
        journal.SourceLegacyTributes = null;
        check(!Prepare(), "Missing structured snapshot cannot be reconstructed from diagnostic strings");
        journal.SourceLegacyTributes = copied;
        journal.DestinationObligations["tribute:partner"] = "-10";
        check(!Prepare(), "Opposite native and legacy payment directions defer before writes");
        journal.DestinationObligations.Remove("tribute:partner");
        journal.DestinationObligations["alliance:partner"] = "200";
        check(!Prepare(), "Inherited legacy debt incompatible with an alliance defers");
    }
}
