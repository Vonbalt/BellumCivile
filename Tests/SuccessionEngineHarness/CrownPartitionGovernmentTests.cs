using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using BellumCivile;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;

internal static class CrownPartitionGovernmentTests
{
    internal static void Run(Action<bool, string> check)
    {
        var laws = new RealmLawBehavior();
        var registry = RealmLawRegistry.Instance;
        var source = (Kingdom)FormatterServices.GetUninitializedObject(typeof(Kingdom));
        var target = (Kingdom)FormatterServices.GetUninitializedObject(typeof(Kingdom));
        var clan = (Clan)FormatterServices.GetUninitializedObject(typeof(Clan));
        AccessTools.Property(typeof(Kingdom), "StringId").SetValue(source, "primary");
        AccessTools.Property(typeof(Kingdom), "StringId").SetValue(target, "bc_partition_indep_test");
        target.RulingClan = clan;
        var entries = (Dictionary<string, RealmLawSelectionRecord>)AccessTools.Field(typeof(RealmLawBehavior), "_realms").GetValue(laws);
        var selections = registry.Groups.ToDictionary(g => g, registry.DefaultFor);
        entries.Add("primary", new RealmLawSelectionRecord { RealmId = "primary", SelectedLaws = selections });
        var capture = AccessTools.Method(typeof(RealmLawBehavior), "CapturePartitionLaws");
        var snapshot = (RealmLawSelectionRecord)capture.Invoke(laws, new object[] { source });
        var alternative = registry.InGroup(RealmLawRegistry.GenderGroup).First(l => l.Id != selections[RealmLawRegistry.GenderGroup]).Id;
        selections[RealmLawRegistry.GenderGroup] = alternative;
        check(snapshot.SelectedLaws[RealmLawRegistry.GenderGroup] != alternative, "Inherited law snapshot is independent of later source edits");
        var journal = new CrownPartitionPromotionRecord { Parent = source, Successor = target, SuccessorId = target.StringId,
            Founder = clan, GovernmentStarted = true, InheritedLaws = snapshot };
        var install = AccessTools.Method(typeof(RealmLawBehavior), "InitializePartitionLaws");
        install.Invoke(laws, new object[] { journal });
        check(entries[target.StringId].SelectedLaws.All(p => snapshot.SelectedLaws[p.Key] == p.Value),
            "Reserved successor ID receives all recorded law groups explicitly");
        check(entries["primary"].SelectedLaws[RealmLawRegistry.GenderGroup] == alternative,
            "Law initialization leaves primary realm government unchanged");
        snapshot.SelectedLaws[RealmLawRegistry.GenderGroup] = alternative;
        check(entries[target.StringId].SelectedLaws[RealmLawRegistry.GenderGroup] != alternative,
            "Successor law storage does not alias mutable journal dictionary");
        var instructions = PatchProcessor.GetOriginalInstructions(install);
        check(!instructions.Any(i => i.Calls(AccessTools.Method(typeof(SuccessionLawBehavior), "CompletePlayerLawChange"))),
            "Inherited law initialization does not charge ruler or apply reform grievances");
        journal.InheritedEnemyIds = new List<string> { "enemy" };
        var declare = AccessTools.Method(typeof(RealmLawBehavior).Assembly.GetType("BellumCivile.CrownPartitionWarSetup"), "TryDeclare");
        bool war = false;
        int calls = 0;
        Action action = () => { calls++; war = true; };
        bool Run(string id = "enemy")
        {
            var args = new object[] { journal, id, (Func<bool>)(() => war), action, null };
            bool ok = (bool)declare.Invoke(null, args);
            check(ok || !string.IsNullOrEmpty(args[4] as string), "Inherited war refusal has a reason");
            return ok;
        }
        check(Run() && calls == 1, "Successor declares recorded foreign enemy once");
        check(Run() && calls == 1, "Returned war declaration verifies without repeat");
        war = false;
        check(!Run() && calls == 1, "Later peace is not undone by stale declaration receipt");
        check(!Run("unrelated"), "Unrecorded foreign enemy cannot be introduced by setup");
        journal.WarDeclarationsStarted.Clear(); journal.WarDeclarationsReturned.Clear();
        war = true;
        check(!Run() && calls == 1, "Unexpected existing war does not masquerade as journaled setup");
        war = false;
        action = () => { calls++; war = true; throw new InvalidOperationException("callback"); };
        check(!Run() && journal.WarDeclarationsStarted.Count == 1 && journal.WarDeclarationsReturned.Count == 0,
            "Interrupted war declaration keeps incomplete receipt");
        check(!Run() && calls == 2, "Interrupted declaration is never replayed");
    }
}
