using System;
using System.Linq;
using System.Runtime.Serialization;
using BellumCivile;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;

internal static class FeudWithdrawalTests
{
    private static Clan _player;
    private static bool Player(ref Clan __result) { __result = _player; return false; }

    internal static void Run(Action<bool, string> check)
    {
        var harmony = new Harmony("bellum.test.feud_withdrawal");
        _player = (Clan)FormatterServices.GetUninitializedObject(typeof(Clan));
        _player.StringId = "player";
        var behavior = new ClaimFeudBehavior();
        ClaimFeudRecord Record()
        {
            var r = (ClaimFeudRecord)FormatterServices.GetUninitializedObject(typeof(ClaimFeudRecord));
            AccessTools.Field(typeof(ClaimFeudRecord), "_claimantClanId").SetValue(r, "claimant");
            AccessTools.Field(typeof(ClaimFeudRecord), "_holderClanId").SetValue(r, "holder");
            return r;
        }
        var withdraw = AccessTools.Method(typeof(ClaimFeudRecord), "RecordWithdrawal");
        try
        {
            harmony.Patch(AccessTools.PropertyGetter(typeof(Clan), "PlayerClan"),
                prefix: new HarmonyMethod(typeof(FeudWithdrawalTests), nameof(Player)));
            foreach (bool claimant in new[] { true, false })
            foreach (var state in new[] { ClaimFeudState.Agitating, ClaimFeudState.PetitionReady,
                ClaimFeudState.Paused, ClaimFeudState.AwaitingPlayerRulerJudgment,
                ClaimFeudState.AwaitingPlayerResponse, ClaimFeudState.DefiedPendingWar, ClaimFeudState.WarActive })
            {
                var r = Record(); r.SetState(state);
                r.RecordSupporters(100, 100, claimant ? "claimant,player,ally" : "claimant,ally",
                    claimant ? "holder" : "holder,player");
                check(behavior.IsPlayerFeudSupporter(r), "Supporter may seek withdrawal: " + state + "/" + claimant);
                withdraw.Invoke(r, new object[] { "player" });
                withdraw.Invoke(r, new object[] { "player" });
                check(r.HasWithdrawn("player") && !behavior.IsPlayerFeudSupporter(r)
                    && !r.ClaimantSupporterIds.Split(',').Contains("player")
                    && !r.HolderSupporterIds.Split(',').Contains("player"), "Withdrawal removes pledge once: " + state);
                check(r.ClaimantSupporterIds.Contains("claimant") && r.HolderSupporterIds.Contains("holder"),
                    "Withdrawal leaves principals intact");
            }
            var resolved = Record(); resolved.RecordSupporters(1, 1, "claimant,player", "holder");
            foreach (var state in new[] { ClaimFeudState.Resolved, ClaimFeudState.Settled, ClaimFeudState.Cooldown, ClaimFeudState.SuppressedCooldown })
            {
                resolved.SetState(state);
                check(!behavior.IsPlayerFeudSupporter(resolved), "Finished feud cannot be abandoned: " + state);
            }
            resolved.SetState(ClaimFeudState.WarActive);
            foreach (string id in new[] { "claimant", "holder", "outsider" })
            {
                _player.StringId = id;
                check(!behavior.IsPlayerFeudSupporter(resolved), "Principal/outsider cannot withdraw: " + id);
            }
            var war = new ClaimFeudWarRecord("war", "feud", "parent", "left", "right", "title", "claimant", "holder",
                "claimant,player,ally", "holder,enemy", "player:100;ally:30;player2:20",
                "home:player;enemy_fief:enemy;other_home:ally;similar:player2", 0);
            AccessTools.Method(typeof(ClaimFeudWarRecord), "RemoveWithdrawnHouse").Invoke(war, new object[] { "player" });
            check(war.IsActive && war.ClaimantClanIds == "claimant,ally" && war.HolderClanIds == "holder,enemy",
                "Withdrawal keeps both war sides active without departed house");
            check(war.InfluenceSnapshot == "ally:30;player2:20", "Final settlement cannot restore departed house influence");
            check(war.FiefSnapshot == "enemy_fief:enemy;other_home:ally;similar:player2",
                "Player estates leave reconciliation; enemy estates remain for subsequent captures");
            var field = AccessTools.Field(typeof(ClaimFeudRecord), "_withdrawnClanIds");
            check(field.GetCustomAttributes(false).Any(a => a.GetType().Name == "SaveableFieldAttribute"),
                "Withdrawal lock persists in saved feud record");
        }
        finally { harmony.UnpatchAll(harmony.Id); }
    }
}
