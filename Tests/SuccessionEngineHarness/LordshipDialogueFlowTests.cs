using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.Conversation;
using TaleWorlds.Localization;

internal static class LordshipDialogueFlowTests
{
    private sealed class Line
    {
        internal string Id, Input, Output, Text;
        internal bool Player;
        internal int Priority;
        internal Delegate Consequence;
    }
    private static readonly Dictionary<string, Line> Lines = new Dictionary<string, Line>();

    private static bool Capture(MethodBase __originalMethod, object[] __args, ref ConversationSentence __result)
    {
        bool repeat = __originalMethod.Name == "AddRepeatablePlayerLine";
        var line = new Line { Id = (string)__args[0], Input = (string)__args[1], Output = (string)__args[2], Text = (string)__args[3],
            Player = __originalMethod.Name != "AddDialogLine", Priority = (int)__args[repeat ? 8 : 6], Consequence = (Delegate)__args[repeat ? 7 : 5] };
        Lines.Add(line.Id, line);
        __result = null;
        return false;
    }

    internal static void Run(Action<bool, string> check)
    {
        var harmony = new Harmony("bellum.tests.lordship_dialogue_flow");
        try
        {
            foreach (var method in typeof(CampaignGameStarter).GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(m => new[] { "AddPlayerLine", "AddDialogLine", "AddRepeatablePlayerLine" }.Contains(m.Name)))
                harmony.Patch(method, prefix: new HarmonyMethod(typeof(LordshipDialogueFlowTests), nameof(Capture)));
            var registry = new LordshipBehavior();
            AccessTools.Method(typeof(LordshipBehavior), "AddDialogues").Invoke(registry,
                new[] { FormatterServices.GetUninitializedObject(typeof(CampaignGameStarter)) });
            var native = PatchProcessor.GetOriginalInstructions(AccessTools.Method(typeof(LordConversationsCampaignBehavior), "AddHeroGeneralConversations")).ToArray();
            int bridge = Array.FindIndex(native, i => i.LoadsConstant("hero_pretalk_2"));
            check(bridge >= 0 && native[bridge + 1].LoadsConstant("lord_pretalk") && native[bridge + 2].LoadsConstant("hero_main_options"),
                "The native NPC pretalk reply returns to the main player options on this game version");

            // Exercise native sorting and matching without constructing the engine's animation/tag loaders.
            var manager = (ConversationManager)FormatterServices.GetUninitializedObject(typeof(ConversationManager));
            AccessTools.Field(typeof(ConversationManager), "_sentences").SetValue(manager, new List<ConversationSentence>());
            AccessTools.Field(typeof(ConversationManager), "_usedIndices").SetValue(manager, new HashSet<int>());
            AccessTools.Field(typeof(ConversationManager), "stateMap").SetValue(manager, new Dictionary<string, int>());
            var add = AccessTools.Method(typeof(ConversationManager), "AddDialogLine", new[] { typeof(ConversationSentence) });
            void Add(Line line)
            {
                var sentence = (ConversationSentence)FormatterServices.GetUninitializedObject(typeof(ConversationSentence));
                void Set(string name, object value) => AccessTools.PropertySetter(typeof(ConversationSentence), name).Invoke(sentence, new[] { value });
                Set("Id", line.Id); Set("Text", new TextObject(line.Text)); Set("IsPlayer", line.Player); Set("Priority", line.Priority);
                Set("InputToken", manager.GetStateIndex(line.Input)); Set("OutputToken", manager.GetStateIndex(line.Output));
                add.Invoke(manager, new object[] { sentence });
            }
            // The grant is still valid after cancellation, so its opening remains an available player choice.
            Add(Lines["bc_lordship_revoke_start"]);
            Add(new Line { Id = "hero_pretalk_2", Input = "lord_pretalk", Output = "hero_main_options", Text = "Is there anything else?", Priority = 100 });
            Add(new Line { Id = "test_leave", Input = "hero_main_options", Output = "hero_leave", Text = "I must leave now.", Player = true, Priority = 100 });
            var sentences = (List<ConversationSentence>)AccessTools.Field(typeof(ConversationManager), "_sentences").GetValue(manager);
            var match = AccessTools.Method(typeof(ConversationManager), "GetSentenceMatch");
            ConversationSentence[] Matches(string token, bool playerOnly)
            {
                manager.ActiveToken = manager.GetStateIndex(token);
                return sentences.Where((s, i) => (bool)match.Invoke(manager, new object[] { i, playerOnly })).ToArray();
            }
            check(Matches("hero_main_options", false).First().Id == "bc_lordship_revoke_start",
                "Native partner matching reproduces the old loop when a cancel jumps directly to player options");
            foreach (string id in new[] { "bc_lordship_cancel", "bc_lordship_cancel_confirm", "bc_lordship_revoke_cancel", "bc_vassal_lordship_return_cancel",
                "bc_indent_explain_cancel", "bc_indent_terms_cancel", "bc_indent_confirm_cancel" })
            {
                var cancel = Lines[id];
                check(cancel.Player && cancel.Output == "lord_pretalk", "Cancellation routes through an NPC turn: " + id);
                check(cancel.Consequence == null, "Cancellation cannot revoke a grant, change revenues or apply relations: " + id);
                var reply = Matches(cancel.Output, false).Single();
                check(!reply.IsPlayer && reply.Id == "hero_pretalk_2", "The native partner selector chooses the normal pretalk reply, not the lordship offer: " + id);
                var options = Matches("hero_main_options", true);
                check(options.Any(s => s.Id == "test_leave") && options.Any(s => s.Id == "bc_lordship_revoke_start"),
                    "After canceling, the player can leave or deliberately reopen the still-valid lordship discussion: " + id);
            }
            var inputs = Lines.Values.GroupBy(l => l.Input).ToDictionary(g => g.Key, g => g.ToArray());
            foreach (var line in Lines.Values.Where(l => l.Player && inputs.ContainsKey(l.Output)))
                check(inputs[line.Output].All(next => !next.Player), "Lordship player choices lead to NPC replies, never another player-choice state: " + line.Id);
            check(Lines["bc_lordship_revoke"].Consequence != null && Lines["bc_lordship_revoke"].Output == "bc_lordship_revoke_done"
                && Lines["bc_lordship_revoke_done"].Output == "hero_main_options", "Confirmed revocation keeps its original action and NPC acknowledgement");
            check(Lines["bc_lordship_confirm"].Consequence != null && Lines["bc_lordship_success"].Output == "hero_main_options",
                "Confirmed grants retain their original action and NPC acknowledgement");
        }
        finally { harmony.UnpatchAll(harmony.Id); Lines.Clear(); }
    }
}
