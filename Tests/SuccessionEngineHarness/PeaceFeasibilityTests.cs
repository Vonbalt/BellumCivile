using System;
using System.Runtime.Serialization;
using BellumCivile;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;

internal static class PeaceFeasibilityTests
{
    private static Clan _player, _winnerRuler, _loserRuler;
    private static Kingdom _winner;
    private static bool _winnerAccepts, _loserAccepts;
    private static int _evaluations;
    private static bool Player(ref Clan __result) { __result = _player; return false; }
    private static bool Ruler(Kingdom __instance, ref Clan __result)
    { __result = __instance == _winner ? _winnerRuler : _loserRuler; return false; }
    private static bool Now(ref CampaignTime __result) { __result = CampaignTime.Days(40); return false; }
    private static bool Evaluate(bool winnerSide, ref TreatyCouncilEvaluation __result)
    {
        _evaluations++;
        __result = new TreatyCouncilEvaluation(0, 0, 0, 0, 0, null, true,
            winnerSide ? _winnerAccepts : _loserAccepts);
        return false;
    }

    internal static void Run(Action<bool, string> check)
    {
        T Blank<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
        var h = new Harmony("bellum.test.peace_feasibility");
        var ticks = AccessTools.Field(typeof(CampaignTime), "TimeTicksPerDay");
        var oldTicks = ticks.GetValue(null);
        var behavior = new ForeignTreatyBehavior();
        var gate = AccessTools.Method(typeof(ForeignTreatyBehavior), "CanSubmitAutomaticDraft");
        _winner = Blank<Kingdom>(); var loser = Blank<Kingdom>();
        _winner.StringId = "a"; loser.StringId = "b";
        _player = Blank<Clan>(); _winnerRuler = Blank<Clan>(); _loserRuler = Blank<Clan>();
        try
        {
            ticks.SetValue(null, 1000L);
            h.Patch(AccessTools.PropertyGetter(typeof(Clan), "PlayerClan"), prefix: new HarmonyMethod(typeof(PeaceFeasibilityTests), nameof(Player)));
            h.Patch(AccessTools.PropertyGetter(typeof(Kingdom), "RulingClan"), prefix: new HarmonyMethod(typeof(PeaceFeasibilityTests), nameof(Ruler)));
            h.Patch(AccessTools.PropertyGetter(typeof(CampaignTime), "Now"), prefix: new HarmonyMethod(typeof(PeaceFeasibilityTests), nameof(Now)));
            h.Patch(AccessTools.Method(typeof(ForeignTreatyBehavior), "EvaluateCouncil"), prefix: new HarmonyMethod(typeof(PeaceFeasibilityTests), nameof(Evaluate)));
            foreach (bool first in new[] { false, true })
            foreach (bool second in new[] { false, true })
            {
                _winnerAccepts = first; _loserAccepts = second;
                var war = new WarScoreRecord("a|b", "a", "b", 0, null);
                var proposal = new TreatyProposalRecord("a|b", "a", "b", "a", 40, 50, false);
                bool allowed = (bool)gate.Invoke(behavior, new object[] { war, proposal, _winner, loser });
                check(allowed == (first && second), "Automatic admission requires both NPC answers: " + first + "/" + second);
                bool ready = (bool)AccessTools.Method(typeof(WarScoreRecord), "CanReconsiderPeace").Invoke(war, new object[] { 41f });
                check(ready == allowed && !war.ParleyPending && !proposal.PlayerInfluenceSpent,
                    "Deferred preview sets backoff without opening parley or spending player influence");
            }
            _winnerRuler = _player; _winnerAccepts = false; _loserAccepts = true;
            var manualWar = new WarScoreRecord("a|b", "a", "b", 0, null);
            var incoming = new TreatyProposalRecord("a|b", "a", "b", "b", 40, 50, false);
            check((bool)gate.Invoke(behavior, new object[] { manualWar, incoming, _winner, loser }),
                "Incoming offer awaits human ruler answer rather than assuming rejection");
            _winnerAccepts = _loserAccepts = false; _evaluations = 0;
            var forced = new TreatyProposalRecord("a|b", "a", "b", "a", 40, 100, true);
            check((bool)gate.Invoke(behavior, new object[] { manualWar, forced, _winner, loser }) && _evaluations == 0,
                "Forced settlement bypasses automatic willingness gate");
        }
        finally { h.UnpatchAll(h.Id); ticks.SetValue(null, oldTicks); }
    }
}
