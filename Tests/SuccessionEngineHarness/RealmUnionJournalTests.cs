using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BellumCivile;
using HarmonyLib;
using TaleWorlds.SaveSystem;
using TaleWorlds.SaveSystem.Definition;

internal static class RealmUnionJournalTests
{
    private static void CheckAbsorptionGuards(Action<bool, string> check)
    {
        var behavior = new BellumCivile.Behaviors.CrownAccessionBehavior();
        var type = behavior.GetType();
        var source = (TaleWorlds.CampaignSystem.Kingdom)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(TaleWorlds.CampaignSystem.Kingdom));
        var destination = (TaleWorlds.CampaignSystem.Kingdom)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(TaleWorlds.CampaignSystem.Kingdom));
        var journal = new RealmUnionRecord { Source = source, Destination = destination,
            InheritedCrownId = "secondary", PrimaryCrownId = "primary" };
        journal.Titles.Add(new RealmUnionTitleRecord { TitleId = "county" });
        var accession = new CrownAccessionRecord { Union = journal };
        AccessTools.Field(type, "_accessions").SetValue(behavior, new List<CrownAccessionRecord> { accession });
        bool Realm(TaleWorlds.CampaignSystem.Kingdom realm) => (bool)AccessTools.Method(type, "IsRealmUnionProtected").Invoke(behavior, new object[] { realm });
        bool Title(string id) => (bool)AccessTools.Method(type, "IsRealmUnionTitleProtected").Invoke(behavior, new object[] { id });
        check(!Realm(source) && !Title("county"), "Unused absorption snapshot does not freeze realm or titles");
        journal.TradeTransferStarted = true;
        check(Realm(source) && Realm(destination) && Title("county"), "Trade movement activates saved absorption protection before Crown transfer");
        journal.TradeTransferStarted = false;
        journal.AllianceTransferStarted = true;
        check(Realm(source) && Realm(destination) && Title("county"), "Alliance transfer activates persisted absorption protection");
        journal.AllianceTransferStarted = false;
        journal.TributeTransfersStarted.Add("partner");
        check(Realm(source) && Realm(destination) && Title("county"), "Native tribute transfer activates persisted union protection");
        journal.TributeTransfersStarted.Clear();
        journal.LegacyTributeTransferStarted = true;
        check(Realm(source) && Realm(destination) && Title("county"), "Legacy tribute transfer activates persisted absorption protection");
        journal.LegacyTributeTransferStarted = false;
        var receipt = new RealmUnionClanRecord { ActionStarted = true };
        journal.Clans.Add(receipt);
        check(Realm(source) && Realm(destination) && Title("county") && Title("secondary") && Title("primary"),
            "Started absorption protects both shells and recorded hierarchy");
        check(!Realm(null) && !Title(null) && !Title("unrelated"), "Absorption guards exclude unrelated identities");
        receipt.ActionStarted = false;
        journal.CrownTransferStarted = true;
        check(Realm(source) && Title("county"), "Crown transfer protects shells before clan movement");
        accession.Completed = true;
        check(Realm(source), "Incomplete union cannot lose protection from outer accession flag alone");
        journal.Completed = true;
        check(!Realm(source) && !Realm(destination) && !Title("county"), "Completed absorption releases guards");
        var args = new object[] { accession, receipt, null };
        check(!(bool)AccessTools.Method(type, "TryMoveRealmUnionClan").Invoke(behavior, args)
            && !string.IsNullOrEmpty(args[2] as string), "Native adapter rejects completed or unprepared accession before campaign access");
        accession.Completed = false;
        journal.Completed = false;
        check(!(bool)AccessTools.Method(type, "TryMoveRealmUnionClan").Invoke(behavior, args)
            && !receipt.ActionStarted, "Native adapter does not move clans before verified Crown and obligations");
    }

    private static void CheckAbsorptionTransfers(Action<bool, string> check)
    {
        var execute = AccessTools.Method(typeof(RealmUnionRecord).Assembly.GetType("BellumCivile.RealmUnionClanTransfer"), "Execute");
        foreach (bool mercenary in new[] { false, true })
        {
            var record = new RealmUnionClanRecord { EndMercenaryContract = mercenary };
            int moves = 0, restores = 0;
            bool interrupt = true;
            Func<bool> yes = () => true;
            Action move = () => { moves++; if (interrupt) throw new InvalidOperationException("callback failed after allegiance changed"); };
            Action restore = () => restores++;
            bool Run() => (bool)execute.Invoke(null, new object[] { record, yes, move, yes, yes, restore, yes, null });
            check(!Run() && record.ActionStarted && !record.ActionReturned, "Absorption records interrupted native action");
            interrupt = false;
            check(!Run() && moves == 1 && restores == 0, "Absorption does not replay interrupted join or contract release");
            record = new RealmUnionClanRecord { EndMercenaryContract = mercenary };
            check(Run() && Run() && moves == 2 && restores == (mercenary ? 0 : 1), "Absorption completes once; mercenaries skip noble restoration");
            check(record.ActionReturned && record.ActionCompleted && record.RestorationCompleted == !mercenary,
                "Absorption persists distinct native-return and verified receipts");
        }
        var interrupted = new RealmUnionClanRecord();
        int restoreCalls = 0;
        Func<bool> valid = () => true;
        Action restoreFailure = () => { restoreCalls++; throw new InvalidOperationException("partial restoration"); };
        bool Retry() => (bool)execute.Invoke(null, new object[] { interrupted, valid, (Action)(() => { }), valid, valid, restoreFailure, valid, null });
        check(!Retry() && !Retry() && restoreCalls == 1 && !interrupted.RestorationReturned,
            "Interrupted absorption restoration never reapplies captured balances");
    }

    private static readonly Dictionary<int, Type> Ids = new Dictionary<int, Type>();
    private static readonly List<Type> Containers = new List<Type>();
    private static bool RegisterClass(Type __0, int __1)
    {
        Ids.Add(__1, __0);
        return __0 == typeof(RealmUnionRecord) || __0 == typeof(RealmUnionClanRecord) || __0 == typeof(RealmUnionTitleRecord)
            || __0 == typeof(CrownPartitionPromotionRecord) || __0 == typeof(CrownPartitionHouseRecord) || __0 == typeof(CrownEstateDeliveryRecord)
            || __0 == typeof(CrownPartitionBatchRecord);
    }
    private static bool RegisterEnum(Type __0, int __1) { Ids.Add(__1, __0); return false; }
    private static bool RegisterContainer(Type __0)
    {
        Containers.Add(__0);
        return __0 == typeof(List<RealmUnionClanRecord>) || __0 == typeof(List<RealmUnionTitleRecord>)
            || __0 == typeof(List<CrownPartitionPromotionRecord>) || __0 == typeof(List<CrownPartitionHouseRecord>) || __0 == typeof(List<CrownEstateDeliveryRecord>)
            || __0 == typeof(Dictionary<string, List<string>>) || __0 == typeof(List<string>);
    }

    internal static void Run(Action<bool, string> check)
    {
        CheckAbsorptionTransfers(check);
        CheckAbsorptionGuards(check);
        foreach (var type in new[] { typeof(RealmUnionRecord), typeof(RealmUnionClanRecord), typeof(RealmUnionTitleRecord), typeof(CrownAccessionRecord),
            typeof(CrownPartitionPromotionRecord), typeof(CrownPartitionHouseRecord), typeof(CrownEstateDeliveryRecord), typeof(CrownPartitionBatchRecord) })
        {
            var fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);
            check(fields.All(f => f.GetCustomAttributesData().Any(a => a.AttributeType == typeof(SaveableFieldAttribute))),
                type.Name + " persists all journal data");
            var ids = fields.Select(f => Convert.ToInt32(f.GetCustomAttributesData().Single(a => a.AttributeType == typeof(SaveableFieldAttribute)).ConstructorArguments[0].Value)).ToList();
            check(ids.Count == ids.Distinct().Count(), type.Name + " has unique field IDs");
        }
        check(new CrownAccessionRecord().Union == null, "Legacy accessions default to no union journal");
        check(Convert.ToInt32(typeof(CrownAccessionRecord).GetField("Union").GetCustomAttributesData().Single().ConstructorArguments[0].Value) == 78,
            "Union reference appends accession field 78");
        var record = new RealmUnionClanRecord();
        bool Call(string name, params object[] args) => (bool)AccessTools.Method(typeof(RealmUnionClanRecord), name).Invoke(record, args);
        check(!Call("CompleteAction", true), "Unstarted transfer cannot claim completion");
        check(!Call("TryBeginRestoration"), "Restoration waits for verified transfer");
        check(Call("TryBeginAction") && !Call("TryBeginAction"), "Movement starts at most once");
        check(!Call("CompleteAction", false) && !record.ActionCompleted, "Unverified native result remains pending");
        // Reconstruct persisted field payloads; this is not a native save-file roundtrip.
        var restored = new RealmUnionClanRecord();
        foreach (var field in typeof(RealmUnionClanRecord).GetFields()) field.SetValue(restored, field.GetValue(record));
        record = restored;
        check(!Call("TryBeginAction"), "Reloaded started receipt blocks blind native replay");
        check(Call("CompleteAction", true) && Call("CompleteAction", true), "Verified reconciliation completes idempotently");
        check(Call("TryBeginRestoration") && !Call("TryBeginRestoration"), "Balance restoration starts at most once");
        check(!Call("CompleteRestoration", false) && !record.RestorationCompleted, "Incomplete restoration is not silently successful");
        check(Call("CompleteRestoration", true) && !Call("TryBeginRestoration"), "Completed restoration cannot overwrite later earnings again");
        record = new RealmUnionClanRecord { EndMercenaryContract = true };
        check(Call("TryBeginAction") && Call("CompleteAction", true) && !Call("TryBeginRestoration"),
            "Mercenary contract completion does not follow noble restoration path");

        var title = new FeudalTitleRecord("sturgia", "Sturgia", FeudalTitleType.Kingdom, "old", "old", "", "capital", "origin", 0, 0);
        var snapshot = (RealmUnionTitleRecord)AccessTools.Method(typeof(RealmUnionTitleRecord), "Capture").Invoke(null, new object[] { title });
        title.SetDeJureHolder("new"); title.SetDeFactoParentTitle("other"); title.SetAssociatedKingdom("changed");
        check(snapshot.LegalHolderId == "old" && snapshot.ActualParentId == "" && snapshot.OriginRealmId == "origin",
            "Title journal freezes original holders, parents and legal origin");
        var capture = AccessTools.Method(typeof(RealmUnionRecord).Assembly.GetType("BellumCivile.RealmUnionSnapshotService"), "TryCapture");
        var prior = new RealmUnionRecord();
        var accession = new CrownAccessionRecord { Union = prior };
        var arguments = new object[] { accession, null, null };
        check(!(bool)capture.Invoke(null, arguments) && ReferenceEquals(accession.Union, prior) && arguments[1] == null,
            "Capture refuses to overwrite an existing journal");
        check(!new RealmUnionRecord().CrownVerified && !new RealmUnionRecord().ObligationsSettled && !new RealmUnionRecord().SourceRetired,
            "New journals never authorize retirement by default");

        var context = new DefinitionContext();
        var initialize = AccessTools.Method(typeof(SaveableTypeDefiner), "Initialize");
        var basic = new SaveableBasicTypeDefiner();
        initialize.Invoke(basic, new object[] { context });
        AccessTools.Method(typeof(SaveableBasicTypeDefiner), "DefineBasicTypes").Invoke(basic, null);
        var definer = new BellumCivileSaveDefiner();
        initialize.Invoke(definer, new object[] { context });
        var h = new Harmony("bellum.test.realm_union_journal");
        Ids.Clear(); Containers.Clear();
        try
        {
            h.Patch(AccessTools.Method(typeof(SaveableTypeDefiner), "AddClassDefinition"), prefix: new HarmonyMethod(typeof(RealmUnionJournalTests), nameof(RegisterClass)));
            h.Patch(AccessTools.Method(typeof(SaveableTypeDefiner), "AddEnumDefinition"), prefix: new HarmonyMethod(typeof(RealmUnionJournalTests), nameof(RegisterEnum)));
            h.Patch(AccessTools.Method(typeof(SaveableTypeDefiner), "ConstructContainerDefinition"), prefix: new HarmonyMethod(typeof(RealmUnionJournalTests), nameof(RegisterContainer)));
            AccessTools.Method(typeof(BellumCivileSaveDefiner), "DefineClassTypes").Invoke(definer, null);
            AccessTools.Method(typeof(BellumCivileSaveDefiner), "DefineEnumTypes").Invoke(definer, null);
            AccessTools.Method(typeof(BellumCivileSaveDefiner), "DefineContainerDefinitions").Invoke(definer, null);
            check(Ids[121] == typeof(RealmUnionRecord) && Ids[122] == typeof(RealmUnionClanRecord) && Ids[123] == typeof(RealmUnionTitleRecord),
                "Union types use unused shared class/enum IDs 121-123");
            check(Containers[Containers.Count - 6] == typeof(List<RealmUnionClanRecord>) && Containers[Containers.Count - 5] == typeof(List<RealmUnionTitleRecord>),
                "Union containers append without shifting prior container ordering");
            var lookup = AccessTools.Method(typeof(DefinitionContext), "GetContainerDefinition");
            check(lookup.Invoke(context, new object[] { typeof(List<RealmUnionClanRecord>) }) != null
                && lookup.Invoke(context, new object[] { typeof(List<RealmUnionTitleRecord>) }) != null && !context.GotError,
                "Native definition registry accepts union record containers");
            check(Ids[124] == typeof(CrownPartitionPromotionRecord) && Ids[125] == typeof(CrownPartitionHouseRecord),
                "Partition promotion types append shared IDs 124-125");
            check(Containers[Containers.Count - 4] == typeof(List<CrownPartitionPromotionRecord>)
                && Containers[Containers.Count - 3] == typeof(List<CrownPartitionHouseRecord>)
                && lookup.Invoke(context, new object[] { typeof(List<CrownPartitionPromotionRecord>) }) != null
                && lookup.Invoke(context, new object[] { typeof(List<CrownPartitionHouseRecord>) }) != null && !context.GotError,
                "Partition containers append and register without shifting previous definitions");
            check(Ids[126] == typeof(CrownEstateDeliveryRecord) && Containers[Containers.Count - 2] == typeof(List<CrownEstateDeliveryRecord>)
                && lookup.Invoke(context, new object[] { typeof(List<CrownEstateDeliveryRecord>) }) != null && !context.GotError,
                "Estate receipt type 126 and appended container register without collisions");
            check(Ids[127] == typeof(CrownPartitionBatchRecord) && Containers.Last() == typeof(Dictionary<string, List<string>>)
                && lookup.Invoke(context, new object[] { typeof(Dictionary<string, List<string>>) }) != null && !context.GotError,
                "Batch type 127 and nested holdings container register without shifting prior IDs");
        }
        finally { h.UnpatchAll(h.Id); }
    }
}
