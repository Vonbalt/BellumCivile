using System;
using BellumCivile;
using HarmonyLib;
using TaleWorlds.CampaignSystem.Issues;

internal static class RelationCauseLabelTests
{
    internal static void Run(Action<bool, string> check)
    {
        var service = typeof(RelationMemoryService);
        var assembly = service.Assembly;
        var resolve = AccessTools.Method(service, "ResolveCapturedDescriptor");
        var fallback = AccessTools.Method(service, "BuildFallbackDescriptor");
        var begin = AccessTools.Method(service, "BeginNativeLabels");
        object Resolve(int delta) => resolve.Invoke(null, new object[] { delta });
        string Source(object d) => (string)AccessTools.Property(d.GetType(), "SourceId").GetValue(d);
        float Duration(object d) => (float)AccessTools.Property(d.GetType(), "DurationDays").GetValue(d);
        IDisposable Begin(string positive, string negative) => (IDisposable)begin.Invoke(null, new object[] { positive, negative });

        using (Begin(RelationMemorySources.HelpedCommunity, RelationMemorySources.FailedCommunity))
        {
            foreach (int delta in new[] { -100, -10, -3, -1, 1, 3, 5, 30, 100 })
            {
                check(Source(Resolve(delta)) == (delta > 0 ? RelationMemorySources.HelpedCommunity : RelationMemorySources.FailedCommunity), "Native labels follow actual delta sign");
                check(Duration(Resolve(delta)) == Duration(fallback.Invoke(null, new object[] { delta })), "Native labels preserve fallback duration");
            }
            var quest = assembly.GetType("BellumCivile.Patches.QuestResultRelationMemoryPatch", true);
            foreach (IssueBase.IssueUpdateDetails result in new[] {
                IssueBase.IssueUpdateDetails.IssueFinishedWithSuccess, IssueBase.IssueUpdateDetails.IssueFail,
                IssueBase.IssueUpdateDetails.IssueTimedOut, IssueBase.IssueUpdateDetails.IssueFinishedWithBetrayal,
                IssueBase.IssueUpdateDetails.SentTroopsFinishedQuest, IssueBase.IssueUpdateDetails.SentTroopsFailedQuest })
            {
                object[] args = { result, null };
                AccessTools.Method(quest, "Prefix").Invoke(null, args);
                check(Source(Resolve(5)) == RelationMemorySources.FulfilledRequest, "Quest positive result label");
                check(Source(Resolve(-5)) == (result == IssueBase.IssueUpdateDetails.IssueFinishedWithBetrayal
                    ? RelationMemorySources.BetrayedTrust : RelationMemorySources.FailedRequest), "Quest negative result label");
                var error = new InvalidOperationException("test");
                check(ReferenceEquals(AccessTools.Method(quest, "Finalizer").Invoke(null, new object[] { error, args[1] }), error), "Finalizer preserves exception");
                AccessTools.Method(quest, "Postfix").Invoke(null, new[] { args[1] });
                check(Source(Resolve(1)) == RelationMemorySources.HelpedCommunity, "Nested quest scope restores community scope idempotently");
            }
            using (RelationMemoryService.Begin(RelationMemorySources.DeliveredNoblePrisoners, 2, RelationMemoryScope.House))
                check(Source(Resolve(5)) == RelationMemorySources.DeliveredNoblePrisoners, "Explicit source overrides native labels");
            object[] cancelled = { IssueBase.IssueUpdateDetails.IssueCancel, null };
            AccessTools.Method(quest, "Prefix").Invoke(null, cancelled);
            check(Source(Resolve(-5)) == RelationMemorySources.RecentGrievance, "Technical cancellation has no quest-failure label");
            ((IDisposable)cancelled[1]).Dispose();
        }
        check(Source(Resolve(1)) == RelationMemorySources.RecentFavor && Source(Resolve(-1)) == RelationMemorySources.RecentGrievance, "Labels do not leak outside native consequence");
        using (Begin(null, RelationMemorySources.RaidedLands))
            check(Source(Resolve(1)) == RelationMemorySources.RecentFavor, "Unexpected raid bonus stays generic");

        var harmony = new Harmony("bellum.test.native_relation_labels");
        try
        {
            foreach (string name in new[] { "QuestResultRelationMemoryPatch", "GrainCommunityRelationMemoryPatch", "RaidRelationMemoryPatch", "SettlementDailyRelationMemoryPatch" })
            {
                var methods = harmony.CreateClassProcessor(assembly.GetType("BellumCivile.Patches." + name, true)).Patch();
                check(methods != null && methods.Count == (name == "GrainCommunityRelationMemoryPatch" ? 4 : 1), "Native hook resolves and patches: " + name);
            }
        }
        finally { harmony.UnpatchAll(harmony.Id); }
    }
}
