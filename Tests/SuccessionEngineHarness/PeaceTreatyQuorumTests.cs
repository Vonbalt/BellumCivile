using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using BellumCivile;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;

internal static class PeaceTreatyQuorumTests
{
    private static Clan _ruler, _player;
    private static bool _canAfford;

    private static bool Ruler(ref Clan __result) { __result = _ruler; return false; }
    private static bool Player(ref Clan __result) { __result = _player; return false; }
    private static bool CanAfford(ref bool __result) { __result = _canAfford; return false; }

    internal static void Run(Action<bool, string> check)
    {
        var harmony = new Harmony("bellum.test.peace_treaty_quorum");
        var kingdom = (Kingdom)FormatterServices.GetUninitializedObject(typeof(Kingdom));
        _ruler = (Clan)FormatterServices.GetUninitializedObject(typeof(Clan));
        _player = (Clan)FormatterServices.GetUninitializedObject(typeof(Clan));
        var behavior = new ForeignTreatyBehavior();
        TreatyCouncilMemberEvaluation Member(float utility, TreatyCouncilVoteStance stance = TreatyCouncilVoteStance.Abstain) =>
            new TreatyCouncilMemberEvaluation(_ruler, 5f, 0f, utility, stance, 0, null);
        TreatyCouncilEvaluation Council(int yay, int nay, float utility, int cost = 0, int capacity = 0) =>
            new TreatyCouncilEvaluation(yay, nay, 0, cost, 25, new[] { Member(utility) },
                votingCapacity: capacity, unopposedRulerSupport: utility);
        try
        {
            harmony.Patch(AccessTools.PropertyGetter(typeof(Kingdom), "RulingClan"),
                prefix: new HarmonyMethod(typeof(PeaceTreatyQuorumTests), nameof(Ruler)));
            harmony.Patch(AccessTools.PropertyGetter(typeof(Clan), "PlayerClan"),
                prefix: new HarmonyMethod(typeof(PeaceTreatyQuorumTests), nameof(Player)));
            var budget = typeof(ForeignTreatyBehavior).Assembly.GetType("BellumCivile.NpcInfluenceBudgetService", true);
            harmony.Patch(AccessTools.Method(budget, "CanAfford"),
                prefix: new HarmonyMethod(typeof(PeaceTreatyQuorumTests), nameof(CanAfford)));

            foreach (float utility in new[] { -20f, 0f, 9f, 10f, 30f })
            {
                var council = Council(0, 0, utility);
                bool accepted = behavior.WouldCouncilAccept(kingdom, council, false, false, out bool overrideUsed);
                check(accepted == (utility >= 10f) && !overrideUsed && !council.IsRatified
                    && council.Support == utility,
                    "Unopposed zero-vote treaty follows NPC ruler utility without an override: " + utility);
            }
            var noRuler = new TreatyCouncilEvaluation(0, 0, 0, 0, 0,
                new List<TreatyCouncilMemberEvaluation>());
            _ruler = null;
            check(!behavior.WouldCouncilAccept(kingdom, noRuler, false, false, out _),
                "An unavailable ruler cannot approve an unopposed treaty");
            _ruler = (Clan)FormatterServices.GetUninitializedObject(typeof(Clan));
            var missingAssessment = new TreatyCouncilEvaluation(0, 0, 0, 0, 0,
                new List<TreatyCouncilMemberEvaluation>());
            check(!behavior.WouldCouncilAccept(kingdom, missingAssessment, false, false, out _),
                "A ruler excluded from the council cannot silently approve a treaty");

            var belowOldQuorum = Council(75, 0, 30f, capacity: 150);
            check(belowOldQuorum.IsRatified
                && behavior.WouldCouncilAccept(kingdom, belowOldQuorum, false, false, out bool majorityOverride)
                && !majorityOverride,
                "Yes majority ratifies peace without a participation quorum");
            var splitMajority = Council(75, 25, 30f, capacity: 150);
            check(splitMajority.IsRatified
                && behavior.WouldCouncilAccept(kingdom, splitMajority, false, false, out _),
                "Opposed but majority-supported treaty no longer fails a participation floor");
            var opposed = new TreatyCouncilEvaluation(25, 25, 0, 25, 0,
                new[] { Member(30f, TreatyCouncilVoteStance.Yay) });
            _canAfford = false;
            check(!opposed.IsRatified && !behavior.WouldCouncilAccept(kingdom, opposed, false, false, out _),
                "Tied peace vote still needs an affordable ruler override");
            _canAfford = true;
            check(behavior.WouldCouncilAccept(kingdom, opposed, false, false, out bool tiedOverride)
                && tiedOverride,
                "Ruler can override committed opposition when influence is affordable");
            var blocked = new TreatyCouncilEvaluation(0, 0, 0, 0, 0, new[] { Member(30f) },
                isBudgetBlocked: true);
            check(!behavior.WouldCouncilAccept(kingdom, blocked, false, false, out _),
                "Over-budget treaty remains blocked despite ruler support");

            _ruler = _player;
            var playerCouncil = Council(0, 0, -30f);
            check(behavior.WouldCouncilAccept(kingdom, playerCouncil, false, true, out bool playerOverride)
                && !playerOverride
                && !behavior.WouldCouncilAccept(kingdom, playerCouncil, false, false, out _),
                "Player ruler decides an unopposed treaty directly");
        }
        finally
        {
            harmony.UnpatchAll(harmony.Id);
            _ruler = _player = null;
            _canAfford = false;
        }
    }
}
