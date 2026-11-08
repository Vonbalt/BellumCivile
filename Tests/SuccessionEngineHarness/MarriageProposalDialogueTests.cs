using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Conversation;
using TaleWorlds.Localization;

internal static class MarriageProposalDialogueTests
{
    private sealed class Line
    {
        internal string Input, Output, Text;
        internal Delegate Consequence, Clickable;
    }
    private static readonly Dictionary<string, Line> Lines = new Dictionary<string, Line>();
    private static Hero _speaker;
    private static CharacterObject _current;
    private static bool Speaker(ref Hero __result) { __result = _speaker; return false; }
    private static bool Current(ref object __result) { __result = _current; return false; }
    private static bool Register(MethodBase __originalMethod, object[] __args, ref ConversationSentence __result)
    {
        bool repeat = __originalMethod.Name == "AddRepeatablePlayerLine";
        Lines[(string)__args[0]] = new Line { Input = (string)__args[1], Output = (string)__args[2], Text = (string)__args[3],
            Consequence = __args[repeat ? 7 : 5] as Delegate,
            Clickable = __args.Length > (repeat ? 9 : 7) ? __args[repeat ? 9 : 7] as Delegate : null };
        __result = null; return false;
    }

    internal static void Run(Action<bool, string> check, SpecificMarriageProposalBehavior proposal, Hero retainer, Hero other, Hero speaker)
    {
        var h = new Harmony("bellum.tests.marriage-proposal-dialogue");
        try
        {
            foreach (string name in new[] { "AddPlayerLine", "AddDialogLine", "AddRepeatablePlayerLine" })
                foreach (var method in AccessTools.GetDeclaredMethods(typeof(CampaignGameStarter)))
                    if (method.Name == name && method.GetParameters()[0].ParameterType == typeof(string))
                        h.Patch(method, prefix: new HarmonyMethod(typeof(MarriageProposalDialogueTests), nameof(Register)));
            h.Patch(AccessTools.PropertyGetter(typeof(Hero), "OneToOneConversationHero"),
                prefix: new HarmonyMethod(typeof(MarriageProposalDialogueTests), nameof(Speaker)));
            foreach (string name in new[] { "CurrentProcessedRepeatObject", "SelectedRepeatObject" })
                h.Patch(AccessTools.PropertyGetter(typeof(ConversationSentence), name),
                    prefix: new HarmonyMethod(typeof(MarriageProposalDialogueTests), nameof(Current)));
            AccessTools.Method(typeof(SpecificMarriageProposalBehavior), "OnSessionLaunched").Invoke(proposal,
                new[] { FormatterServices.GetUninitializedObject(typeof(CampaignGameStarter)) });
            var playerLine = Lines["bc_specific_marriage_choose_player_relative"];
            var npcLine = Lines["bc_specific_marriage_choose_other_relative"];
            check(playerLine.Clickable != null && npcLine.Clickable != null,
                "Both repeatable candidate lists register native disabled-option tooltips");
            check(!Lines.ContainsKey("bc_specific_marriage_choose_self")
                && new TextObject(playerLine.Text).SetTextVariable("BC_PLAYER_IS_SELF", 1).ToString() == "My own hand.",
                "The self proposal uses the sorted candidate list and preserves its natural dialogue text");
            _speaker = speaker; _current = retainer.CharacterObject;
            object[] args = { null };
            check((bool)playerLine.Clickable.DynamicInvoke(args) && args[0] == null,
                "The actual player-choice callback enables a companion with a valid match");
            playerLine.Consequence.DynamicInvoke(); _current = other.CharacterObject;
            args[0] = null;
            check((bool)npcLine.Clickable.DynamicInvoke(args) && npcLine.Output == "bc_specific_marriage_household_prompt",
                "The NPC-choice callback uses the selected companion before proceeding to household terms");
            AccessTools.Field(typeof(Hero), "_heroState").SetValue(other, Hero.CharacterStates.Prisoner);
            args[0] = null;
            check(!(bool)npcLine.Clickable.DynamicInvoke(args)
                && ((TextObject)args[0]).GetID() == "BC_Marriage_CandidatePrisoner",
                "A newly captured NPC candidate is disabled by the live dialogue callback");
        }
        finally
        {
            AccessTools.Field(typeof(Hero), "_heroState").SetValue(other, Hero.CharacterStates.Active);
            AccessTools.Method(typeof(SpecificMarriageProposalBehavior), "ClearSelection").Invoke(proposal, null);
            h.UnpatchAll(h.Id); Lines.Clear(); _speaker = null; _current = null;
        }
    }
}
