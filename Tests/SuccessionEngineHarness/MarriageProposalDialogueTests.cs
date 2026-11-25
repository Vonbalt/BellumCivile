using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Conversation;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Localization;

internal static class MarriageProposalDialogueTests
{
    private sealed class Line
    {
        internal string Input, Output, Text;
        internal Delegate Condition, Consequence, Clickable;
    }
    private static readonly Dictionary<string, Line> Lines = new Dictionary<string, Line>();
    private static Hero _speaker;
    private static CharacterObject _current;
    private static TextObject _repeatLine;
    private static IReadOnlyList<object> _choices;
    private static bool Speaker(ref Hero __result) { __result = _speaker; return false; }
    private static bool Current(ref object __result) { __result = _current; return false; }
    private static bool RepeatLine(ref TextObject __result) { __result = _repeatLine; return false; }
    private static bool CaptureChoices(IReadOnlyList<object> __0) { _choices = __0; return false; }
    private static bool Register(MethodBase __originalMethod, object[] __args, ref ConversationSentence __result)
    {
        bool repeat = __originalMethod.Name == "AddRepeatablePlayerLine";
        Lines[(string)__args[0]] = new Line { Input = (string)__args[1], Output = (string)__args[2], Text = (string)__args[3],
            Condition = __args[repeat ? 6 : 4] as Delegate, Consequence = __args[repeat ? 7 : 5] as Delegate,
            Clickable = __args.Length > (repeat ? 9 : 7) ? __args[repeat ? 9 : 7] as Delegate : null };
        __result = null; return false;
    }

    internal static void Run(Action<bool, string> check, SpecificMarriageProposalBehavior proposal,
        Hero retainer, Hero other, Hero speaker, Hero otherNoble)
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
            h.Patch(AccessTools.PropertyGetter(typeof(ConversationSentence), "SelectedRepeatLine"),
                prefix: new HarmonyMethod(typeof(MarriageProposalDialogueTests), nameof(RepeatLine)));
            h.Patch(AccessTools.Method(typeof(ConversationSentence), "SetObjectsToRepeatOver"),
                prefix: new HarmonyMethod(typeof(MarriageProposalDialogueTests), nameof(CaptureChoices)));
            AccessTools.Method(typeof(SpecificMarriageProposalBehavior), "OnSessionLaunched").Invoke(proposal,
                new[] { FormatterServices.GetUninitializedObject(typeof(CampaignGameStarter)) });
            var playerLine = Lines["bc_specific_marriage_choose_player_relative"];
            var npcLine = Lines["bc_specific_marriage_choose_other_relative"];
            check(playerLine.Clickable != null && npcLine.Clickable != null,
                "Both repeatable candidate lists register native disabled-option tooltips");
            _current = Hero.MainHero.CharacterObject;
            var selfLine = _repeatLine = new TextObject(playerLine.Text);
            playerLine.Condition.DynamicInvoke();
            _current = retainer.CharacterObject;
            var retainerLine = _repeatLine = new TextObject(playerLine.Text);
            playerLine.Condition.DynamicInvoke();
            check(selfLine.ToString() == "My own hand." && retainerLine.ToString() == "The hand of " + retainer.Name + ".",
                "Native repeatable text keeps a distinct self line and a named retainer line");
            _speaker = speaker; _current = retainer.CharacterObject;
            object[] args = { null };
            check((bool)playerLine.Clickable.DynamicInvoke(args) && args[0] == null,
                "The actual player-choice callback enables a companion with a valid match");
            playerLine.Consequence.DynamicInvoke(); _current = other.CharacterObject;
            args[0] = null;
            check((bool)npcLine.Clickable.DynamicInvoke(args) && npcLine.Output == "bc_specific_marriage_household_prompt",
                "The NPC-choice callback uses the selected companion before proceeding to household terms");
            _current = otherNoble.CharacterObject; npcLine.Consequence.DynamicInvoke();
            var refusal = Lines["bc_specific_marriage_rank_refusal"];
            check((bool)refusal.Condition.DynamicInvoke() && refusal.Output == "lord_pretalk",
                "Offering a retainer to a noble leads to a spoken refusal, not household terms or barter");
            _current = Hero.MainHero.CharacterObject; playerLine.Consequence.DynamicInvoke();
            _current = other.CharacterObject; npcLine.Consequence.DynamicInvoke();
            var surprised = Lines["bc_specific_marriage_retainer_reply"];
            check((bool)surprised.Condition.DynamicInvoke() && surprised.Output == "bc_specific_marriage_household",
                "Asking for a retainer to enter the player's family receives the special reply");
            var femaleHouse = Lines["bc_specific_marriage_household_True"];
            var maleHouse = Lines["bc_specific_marriage_household_False"];
            check(!(bool)femaleHouse.Clickable.DynamicInvoke(new object[] { null })
                && (bool)maleHouse.Clickable.DynamicInvoke(new object[] { null }),
                "Retainer elevation only allows the household in which the retainer moves");
            _current = retainer.CharacterObject; playerLine.Consequence.DynamicInvoke();
            _current = other.CharacterObject;
            AccessTools.Field(typeof(Hero), "_heroState").SetValue(other, Hero.CharacterStates.Prisoner);
            args[0] = null;
            check(!(bool)npcLine.Clickable.DynamicInvoke(args)
                && ((TextObject)args[0]).GetID() == "BC_Marriage_CandidatePrisoner",
                "A newly captured NPC candidate is disabled by the live dialogue callback");

            var state = AccessTools.Field(typeof(Hero), "_heroState");
            var partyField = AccessTools.Field(typeof(Hero), "_partyBelongedTo");
            var originalParty = partyField.GetValue(otherNoble);
            var unavailable = speaker.Clan.Heroes.Concat(speaker.Clan.Companions)
                .Where(hero => hero.IsFemale && hero != otherNoble).Distinct().ToDictionary(hero => hero, state.GetValue);
            try
            {
                foreach (var hero in unavailable.Keys) state.SetValue(hero, Hero.CharacterStates.Prisoner);
                var armyParty = (MobileParty)FormatterServices.GetUninitializedObject(typeof(MobileParty));
                AccessTools.Field(typeof(MobileParty), "_army").SetValue(armyParty,
                    FormatterServices.GetUninitializedObject(typeof(Army)));
                partyField.SetValue(otherNoble, armyParty);

                _current = Hero.MainHero.CharacterObject;
                args[0] = null;
                check((bool)playerLine.Clickable.DynamicInvoke(args) && args[0] == null,
                    "My own hand stays clickable when every potential counterpart is unavailable");
                playerLine.Consequence.DynamicInvoke();
                Lines[playerLine.Output].Consequence.DynamicInvoke();
                check(_choices.Contains(otherNoble.CharacterObject), "The unavailable princess remains in the second dialogue list");
                _current = otherNoble.CharacterObject;
                args[0] = null;
                check(!(bool)npcLine.Clickable.DynamicInvoke(args) && ((TextObject)args[0]).GetID() == "BC_Marriage_CandidateArmy",
                    "The specific army tooltip appears on the princess's disabled row");
            }
            finally
            {
                partyField.SetValue(otherNoble, originalParty);
                foreach (var entry in unavailable) state.SetValue(entry.Key, entry.Value);
            }
        }
        finally
        {
            AccessTools.Field(typeof(Hero), "_heroState").SetValue(other, Hero.CharacterStates.Active);
            AccessTools.Method(typeof(SpecificMarriageProposalBehavior), "ClearSelection").Invoke(proposal, null);
            h.UnpatchAll(h.Id); Lines.Clear(); _speaker = null; _current = null; _repeatLine = null;
            _choices = null;
        }
    }
}
