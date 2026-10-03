using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using BellumCivile;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;

internal static class CourtTribunalReactionTests
{
    private static bool SkipFlush() => false;
    internal static void Run(Action<bool, string> check)
    {
        var type = typeof(CourtTribunalReactionRecord);
        void Settle(CourtTribunalReactionRecord r, CourtTribunalMemberReaction m, int amount, bool pardon = false) =>
            AccessTools.Method(type, "Settle").Invoke(r, new object[] { m, amount, pardon });
        int Total(CourtTribunalReactionRecord r, FactionType bloc) =>
            (int)AccessTools.Method(type, "MemberTotal").Invoke(r, new object[] { bloc });
        bool Ready(CourtTribunalReactionRecord r) => (bool)AccessTools.Property(type, "Ready").GetValue(r);
        foreach (int count in Enumerable.Range(1, 8))
        {
            var r = new CourtTribunalReactionRecord();
            for (int i = 0; i < count; i++)
            {
                var m = new CourtTribunalMemberReaction { Bloc = (int)FactionType.Nobility };
                r.Members.Add(m); Settle(r, m, 10, true); Settle(r, m, -30);
            }
            check(Total(r, FactionType.Nobility) == Math.Min(30, count * 10), "Pardons cap at +30 and repeated verdict cannot overwrite, count=" + count);
            check(Total(r, FactionType.Glory) == 0 && r.Clemency, "Unrelated bloc unaffected; clemency recorded once");
        }
        foreach (int first in new[] { -30, -10, 10 })
        foreach (int second in new[] { -30, -10, 10 })
        foreach (int third in new[] { -30, -10, 10 })
        {
            var r = new CourtTribunalReactionRecord();
            foreach (int amount in new[] { first, second, third })
            {
                var m = new CourtTribunalMemberReaction { Bloc = (int)FactionType.Liberty };
                r.Members.Add(m); Settle(r, m, amount, amount > 0);
            }
            check(Total(r, FactionType.Liberty) == Math.Max(-30, Math.Min(30, first + second + third)), "Mixed verdicts sum before clamping");
        }
        var pending = new CourtTribunalReactionRecord { Id = "fixture", Closed = true };
        var clan = (Clan)FormatterServices.GetUninitializedObject(typeof(Clan));
        var member = new CourtTribunalMemberReaction { Clan = clan, Bloc = (int)FactionType.Glory, ExecutionId = "sentence" };
        pending.Members.Add(member);
        check(!Ready(pending) && Total(pending, FactionType.Glory) == 0, "Queued execution neither applies nor closes reactions");
        Settle(pending, member, -30);
        check(Ready(pending) && Total(pending, FactionType.Glory) == -30, "Actual execution releases group with affected-bloc penalty");
        pending.Applied = true;
        check(!Ready(pending), "Applied group cannot be delivered again");

        var h = new Harmony("bellum.test.tribunal_reactions");
        var behavior = new CivilWarResolutionBehavior();
        var behaviorType = typeof(CivilWarResolutionBehavior);
        var records = (List<CourtTribunalReactionRecord>)AccessTools.Field(behaviorType, "_tribunalReactions").GetValue(behavior);
        void Call(string name, params object[] args) => AccessTools.Method(behaviorType, name).Invoke(behavior, args);
        try
        {
            h.Patch(AccessTools.Method(behaviorType, "FlushTribunalReactions"), prefix: new HarmonyMethod(typeof(CourtTribunalReactionTests), nameof(SkipFlush)));
            foreach (var pair in new[] { Tuple.Create(false, false, 0), Tuple.Create(false, true, -10), Tuple.Create(true, true, -30) })
            {
                pending = new CourtTribunalReactionRecord { Id = "fixture", Closed = true };
                member = new CourtTribunalMemberReaction { Clan = clan, Bloc = (int)FactionType.Glory, ExecutionId = "sentence" };
                pending.Members.Add(member); records.Add(pending);
                Call("RecordTribunalReaction", "fixture", clan, 0, false);
                check(!member.Settled && member.ExecutionId == "sentence", "Duplicate sentence cannot erase original pending reaction");
                Call("CompleteTribunalExecution", "sentence", pair.Item1, pair.Item2);
                Call("CompleteTribunalExecution", "sentence", true, true);
                check(Ready(pending) && Total(pending, FactionType.Glory) == pair.Item3, "Actual death/exile/cancellation recorded once");
                records.Clear();
            }
            records.Add(new CourtTribunalReactionRecord { Id = "cancel" });
            Call("CloseTribunalReactions", "cancel", false);
            check(records.Count == 0, "Cancelled tribunal discards its reactions");
        }
        finally { h.UnpatchAll(h.Id); }

        foreach (var recordType in new[] { type, typeof(CourtTribunalMemberReaction) })
        {
            var fields = recordType.GetFields();
            check(fields.All(f => f.GetCustomAttributes(false).Any(a => a.GetType().Name == "SaveableFieldAttribute")), "Every tribunal record field is persisted: " + recordType.Name);
            var ids = fields.Select(f => Convert.ToInt32(f.GetCustomAttributesData()
                .Single(a => a.AttributeType.Name == "SaveableFieldAttribute").ConstructorArguments[0].Value)).ToList();
            check(ids.Distinct().Count() == ids.Count, "Tribunal save field IDs are unique: " + recordType.Name);
        }
    }
}
