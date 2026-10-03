using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using BellumCivile;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;

internal static class TreatyRelationOutcomeTests
{
    private static readonly Dictionary<Clan, Hero> Leaders = new Dictionary<Clan, Hero>();
    private static readonly List<Tuple<Hero, Hero, int>> Changes = new List<Tuple<Hero, Hero, int>>();
    private static bool Leader(Clan __instance, ref Hero __result) { __result = Leaders[__instance]; return false; }
    private static bool Change(Hero first, Hero second, int change)
    {
        if (change != 0) Changes.Add(Tuple.Create(first, second, change));
        return false;
    }
    private static T Blank<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));

    internal static void Run(Action<bool, string> check)
    {
        var harmony = new Harmony("bellum.test.treaty_relation_outcomes");
        var ruler = Blank<Clan>(); var yay = Blank<Clan>(); var nay = Blank<Clan>();
        foreach (var house in new[] { ruler, yay, nay }) Leaders[house] = Blank<Hero>();
        var method = AccessTools.Method(typeof(ForeignTreatyBehavior), "ApplyFinalCouncilConsequences");
        TreatyCouncilMemberEvaluation Member(Clan c, TreatyCouncilVoteStance stance, int commitment) =>
            new TreatyCouncilMemberEvaluation(c, 0, commitment, 0, stance, commitment, null);
        TreatyCouncilEvaluation Council(int commitment, TreatyCouncilVoteStance rulerStance) =>
            new TreatyCouncilEvaluation(commitment, commitment, 0, 0, 0, new[] {
                Member(ruler, rulerStance, commitment), Member(yay, TreatyCouncilVoteStance.Yay, commitment),
                Member(nay, TreatyCouncilVoteStance.Nay, commitment) });
        TreatyProposalRecord Proposal(TreatyProposalState state, bool forced = false)
        {
            var p = new TreatyProposalRecord("war", "one", "two", "one", 0, 100, forced);
            p.SetState(state, "test"); return p;
        }
        void Apply(TreatyProposalRecord p, TreatyCouncilEvaluation c, bool accepts, bool overrides) =>
            method.Invoke(null, new object[] { p, ruler, c, accepts, overrides, null, null, false, false });
        try
        {
            harmony.Patch(AccessTools.PropertyGetter(typeof(Clan), "Leader"), prefix: new HarmonyMethod(typeof(TreatyRelationOutcomeTests), nameof(Leader)));
            harmony.Patch(AccessTools.Method(typeof(ForeignTreatyBehavior), "ApplyTreatyRelationChange",
                new[] { typeof(Hero), typeof(Hero), typeof(int), typeof(string), typeof(float), typeof(string) }),
                prefix: new HarmonyMethod(typeof(TreatyRelationOutcomeTests), nameof(Change)));
            foreach (int commitment in new[] { 20, 75, 150 })
            foreach (var evaluatedStance in new[] { TreatyCouncilVoteStance.Yay, TreatyCouncilVoteStance.Nay, TreatyCouncilVoteStance.Abstain })
            {
                var council = Council(commitment, evaluatedStance);
                Changes.Clear();
                var rejected = Proposal(TreatyProposalState.Rejected);
                Apply(rejected, council, false, true);
                int penalty = commitment >= 150 ? 25 : commitment >= 75 ? 15 : 5;
                check(Changes.Count == 1 && Changes[0].Item1 == Leaders[yay]
                    && Changes[0].Item2 == Leaders[ruler] && Changes[0].Item3 == -penalty,
                    "Refusal only penalizes peace supporters, regardless of ruler's evaluated stance");
                Apply(rejected, council, false, true);
                check(Changes.Count == 1, "Finalized proposal cannot repeat refusal consequences");
                Changes.Clear();
                Apply(Proposal(TreatyProposalState.Rejected), council, true, true);
                check(Changes.Count == 0, "Foreign rejection does not reward or penalize the accepting court");
                Apply(Proposal(TreatyProposalState.Rejected), council, false, false);
                check(Changes.Count == 0, "Council refusal without a ruler override has no relation effects");
                Changes.Clear();
                var signed = Proposal(TreatyProposalState.Applied);
                Apply(signed, council, true, true);
                int reward = commitment >= 150 ? 15 : commitment >= 75 ? 9 : 3;
                check(Changes.Count == 3 && Changes.Any(c => c.Item1 == Leaders[yay] && c.Item2 == Leaders[ruler] && c.Item3 == reward)
                    && Changes.Any(c => c.Item1 == Leaders[nay] && c.Item2 == Leaders[ruler] && c.Item3 == -penalty),
                    "Applied treaty retains rewards, override penalties and inter-lord reactions using actual acceptance");
                Apply(signed, council, true, true);
                check(Changes.Count == 3, "Applied treaty consequences are one-shot");
            }
            Changes.Clear();
            foreach (var state in new[] { TreatyProposalState.ParleyPending, TreatyProposalState.Cancelled, TreatyProposalState.Accepted })
                Apply(Proposal(state), Council(150, TreatyCouncilVoteStance.Yay), true, true);
            check(Changes.Count == 0, "Drafts, invalid proposals and incomplete application grant no relations");
            foreach (var state in new[] { TreatyProposalState.Applied, TreatyProposalState.Rejected })
                Apply(Proposal(state, true), Council(150, TreatyCouncilVoteStance.Yay), state == TreatyProposalState.Applied, true);
            check(Changes.Count == 0, "Forced settlements retain no council-relation consequences");
            for (int i = 0; i < 10; i++)
                Apply(Proposal(TreatyProposalState.Rejected), new TreatyCouncilEvaluation(0, 150, 0, 0, 0,
                    new[] { Member(nay, TreatyCouncilVoteStance.Nay, 150) }), false, true);
            check(Changes.Count == 0, "Repeated manufactured bad proposals cannot farm agreement relations");
            check(AccessTools.Field(typeof(TreatyProposalRecord), "_councilConsequencesApplied").GetCustomAttributes(false)
                .Any(a => a.GetType().Name == "SaveableFieldAttribute"), "One-shot consequence receipt persists with proposal");
        }
        finally { harmony.UnpatchAll(harmony.Id); Leaders.Clear(); Changes.Clear(); }
    }
}
