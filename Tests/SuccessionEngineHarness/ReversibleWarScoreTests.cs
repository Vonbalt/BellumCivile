using System;
using System.Collections.Generic;
using BellumCivile;
using HarmonyLib;

internal static class ReversibleWarScoreTests
{
    internal static void Run(Action<bool, string> check)
    {
        WarScoreRecord New() => new WarScoreRecord("a:b", "a", "b", 0, null, WarScoreConflictType.ClaimFeud);
        var war = New();
        void Call(string method, params object[] args) => AccessTools.Method(typeof(WarScoreRecord), method).Invoke(war, args);
        void Capture(string id, float value) => Call("RecordPrisoner", id, value, 25f);
        void Release(string id) => Call("RemovePrisoner", id, 25f);
        Capture("ruler", 15); Capture("heir", 8); Capture("noble", 2);
        check(war.PrisonerScore == 25, "Current prisoner values sum to the cap");
        Capture("extra", 8);
        check(war.PrisonerScore == 25, "Prisoner totals retain overflow above the display cap");
        Release("noble");
        check(war.PrisonerScore == 25, "Release does not reduce capped score when remaining custody still exceeds cap");
        Release("extra");
        check(war.PrisonerScore == 23, "Release exposes correct uncapped remaining total");
        Capture("heir", 15);
        check(war.PrisonerScore == 23, "Same-side transfer or role change does not duplicate or reprice existing captive");
        Release("heir"); Release("heir");
        check(war.PrisonerScore == 15, "Duplicate release cannot subtract twice");
        Capture("opponent_ruler", -15);
        check(war.PrisonerScore == 0, "Opposing prisoner leverage nets before applying the cap");
        Call("ReconcilePrisoners", new Dictionary<string, float> { ["ruler"] = 2 }, 25f);
        check(war.PrisonerScore == 15, "Custody reconciliation removes absent prisoners and preserves recorded capture value");
        Call("ReconcilePrisoners", new Dictionary<string, float>(), 25f);
        check(war.PrisonerScore == 0, "Escape, rescue, death or third-party custody clears absent captives");
        Capture("ruler", 2);
        check(war.PrisonerScore == 2, "A fresh capture after release receives its current value");
        war = New(); war.AddPrisonerScore(25, 25);
        AccessTools.Field(typeof(WarScoreRecord), "_prisonerCustody").SetValue(war, null);
        Call("ReconcilePrisoners", new Dictionary<string, float> { ["only_actual_prisoner"] = 8 }, 25f);
        check(war.PrisonerScore == 8, "Legacy cumulative prisoner score migrates to actual custody");
        var saved = new Dictionary<string, float>((Dictionary<string, float>)AccessTools.Field(typeof(WarScoreRecord), "_prisonerCustody").GetValue(war));
        war = New(); AccessTools.Field(typeof(WarScoreRecord), "_prisonerCustody").SetValue(war, saved);
        Call("ReconcilePrisoners", new Dictionary<string, float> { ["only_actual_prisoner"] = 15 }, 25f);
        check(war.PrisonerScore == 8, "Restored custody records retain original capture value");
        war = New(); war.AddBattleScore(40, 40); war.SetOccupationScore(45); Capture("ruler", 15);
        war.QueueTerminalResolution("prisoner capture"); Release("ruler");
        check(war.Score == 85 && !war.TerminalResolutionQueued, "Prisoner release cancels uncommitted victory dependent on that capture");
        war = New(); war.AddBattleScore(12, 40);
        for (int i = 0; i < 20; i++)
        {
            Call("SetObjectiveControlScore", 50f);
            check(war.Score == 62 && war.ObjectiveScore == 50, "Objective capture cycle " + i + " never stacks");
            Call("SetObjectiveControlScore", 0f);
            check(war.Score == 12 && war.ObjectiveScore == 0, "Objective recapture cycle " + i + " removes full bonus");
        }
        war.AddObjectiveScore(75, 100); Call("SetObjectiveControlScore", 50f);
        check(war.ObjectiveScore == 50, "Legacy accumulated objective score normalizes to current control");
        war = New(); war.SetOccupationScore(60); Call("SetObjectiveControlScore", 50f);
        war.QueueTerminalResolution("objective captured"); Call("SetObjectiveControlScore", 0f);
        check(war.Score == 60 && !war.TerminalResolutionQueued, "Objective recovery cancels uncommitted victory dependent on occupation bonus");
        Call("SetObjectiveControlScore", -50f);
        check(war.ObjectiveScore == -50, "Objective supports claimant on the defender side of a score record");
    }
}
