using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.BarterSystem;
using TaleWorlds.CampaignSystem.BarterSystem.Barterables;
using TaleWorlds.CampaignSystem.Conversation;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Localization;

internal static partial class MarriageHouseholdTests
{
    private sealed class BetrothalDialogueLine
    {
        internal string Input, Output, Text;
        internal Delegate Condition, Consequence, Clickable;
    }

    private static readonly Dictionary<string, BetrothalDialogueLine> BetrothalLines = new Dictionary<string, BetrothalDialogueLine>();
    private static Hero _betrothalSpeaker, _betrothalSelected;
    private static IReadOnlyList<object> _betrothalCandidates;
    private static TextObject _betrothalRepeatLine;
    private static BarterData _betrothalBarter;

    private static bool RegisterBetrothalLine(MethodBase __originalMethod, object[] __args, ref ConversationSentence __result)
    {
        bool repeat = __originalMethod.Name == "AddRepeatablePlayerLine";
        BetrothalLines[(string)__args[0]] = new BetrothalDialogueLine {
            Input = (string)__args[1], Output = (string)__args[2], Text = (string)__args[3], Condition = __args[repeat ? 6 : 4] as Delegate,
            Consequence = __args[repeat ? 7 : 5] as Delegate, Clickable = repeat ? null : __args.ElementAtOrDefault(7) as Delegate
        };
        __result = null;
        return false;
    }

    private static bool BetrothalSpeaker(ref Hero __result) { __result = _betrothalSpeaker; return false; }
    private static bool BetrothalSelected(ref object __result) { __result = _betrothalSelected; return false; }
    private static bool BetrothalRepeatLine(ref TextObject __result) { __result = _betrothalRepeatLine; return false; }
    private static bool CaptureBetrothalCandidates(IReadOnlyList<object> __0) { _betrothalCandidates = __0; return false; }
    private static bool CaptureBetrothalBarter(BarterData __0) { _betrothalBarter = __0; return false; }
    private static bool BetrothalTestParty(ref PartyBase __result) { __result = Blank<PartyBase>(); return false; }

    private static void CheckBetrothalProposalDialogue(Action<bool, string> check, CampaignBehaviorBase behavior, Hero first, Hero second)
    {
        var type = behavior.GetType();
        var campaign = Campaign.Current;
        var barterManager = AccessTools.Field(typeof(Campaign), "<BarterManager>k__BackingField");
        var conversationManager = AccessTools.Field(typeof(Campaign), "<ConversationManager>k__BackingField");
        var diplomacy = AccessTools.Property(typeof(GameModels), "DiplomacyModel");
        var oldBarter = barterManager.GetValue(campaign);
        var oldConversation = conversationManager.GetValue(campaign);
        var oldDiplomacy = diplomacy.GetValue(campaign.Models);
        float originalAge = Ages[second];
        var harmony = new Harmony("bellum.tests.betrothal_proposal_dialogue");
        void Patch(MethodBase target, string prefix) =>
            harmony.Patch(target, prefix: new HarmonyMethod(typeof(MarriageHouseholdTests), prefix));
        BetrothalDialogueLine Line(string suffix) => BetrothalLines["bc_betrothal_" + suffix];
        bool CanSay(string line) => (bool)(Line(line).Condition?.DynamicInvoke() ?? true);
        void SayLine(string line) => Line(line).Consequence?.DynamicInvoke();
        var pledges = (IList)AccessTools.Field(type, "_pledges").GetValue(behavior);
        void ReachHousehold()
        {
            SayLine("propose");
            _betrothalSelected = second; SayLine("player_choice");
            _betrothalSelected = first; SayLine("other_choice");
        }
        try
        {
            foreach (var method in AccessTools.GetDeclaredMethods(typeof(CampaignGameStarter)))
                if (new[] { "AddPlayerLine", "AddDialogLine", "AddRepeatablePlayerLine" }.Contains(method.Name)
                    && method.GetParameters()[0].ParameterType == typeof(string))
                    Patch(method, nameof(RegisterBetrothalLine));
            Patch(AccessTools.PropertyGetter(typeof(Hero), "OneToOneConversationHero"), nameof(BetrothalSpeaker));
            Patch(AccessTools.PropertyGetter(typeof(ConversationSentence), "SelectedRepeatObject"), nameof(BetrothalSelected));
            Patch(AccessTools.PropertyGetter(typeof(ConversationSentence), "CurrentProcessedRepeatObject"), nameof(BetrothalSelected));
            Patch(AccessTools.PropertyGetter(typeof(ConversationSentence), "SelectedRepeatLine"), nameof(BetrothalRepeatLine));
            Patch(AccessTools.Method(typeof(ConversationSentence), "SetObjectsToRepeatOver"), nameof(CaptureBetrothalCandidates));
            Patch(AccessTools.Method(typeof(BarterManager), "BeginPlayerBarter"), nameof(CaptureBetrothalBarter));
            Patch(AccessTools.PropertyGetter(typeof(PartyBase), "MainParty"), nameof(BetrothalTestParty));
            Patch(AccessTools.Method(typeof(CampaignTime), nameof(CampaignTime.ToString), Type.EmptyTypes), nameof(FormatPledgeDate));
            Patch(AccessTools.Method(type, "BetrothalScore"), nameof(BetrothalCommandScore));
            barterManager.SetValue(campaign, new BarterManager());
            conversationManager.SetValue(campaign, Blank<ConversationManager>());
            diplomacy.SetValue(campaign.Models, new DefaultDiplomacyModel());
            AccessTools.Method(type, "RegisterDialogue").Invoke(behavior, new[] { Blank<CampaignGameStarter>() });
            _betrothalSpeaker = first.Clan.Leader;

            _betrothalSelected = Hero.MainHero;
            var self = _betrothalRepeatLine = new TextObject(Line("player_choice").Text);
            CanSay("player_choice");
            _betrothalSelected = second;
            var relative = _betrothalRepeatLine = new TextObject(Line("player_choice").Text);
            CanSay("player_choice");
            check(self.ToString() == "My own hand." && relative.ToString() == "I would pledge " + second.Name + ".",
                "Betrothal uses a self line only for the player, and a named pledge for relatives");

            Outsiders.Add(first);
            Ages[second] = 24;
            SayLine("propose");
            check(CanSay("no_candidates") && !CanSay("propose_reply")
                && Line("no_candidates").Output == "lord_pretalk" && _betrothalCandidates.Count == 0,
                "No compatible betrothal is a spoken reply that returns to the conversation");
            SayLine("no_candidates");
            Outsiders.Remove(first);
            Ages[second] = originalAge;

            SayLine("propose");
            check(CanSay("propose_reply") && Line("player_choice").Input == Line("propose_reply").Output
                && _betrothalCandidates.Contains(second) && !_betrothalCandidates.Contains(first),
                "Player relatives are normal repeatable conversation choices");
            _betrothalSelected = second;
            SayLine("player_choice");
            check(CanSay("other_prompt") && Line("other_choice").Input == Line("other_prompt").Output
                && _betrothalCandidates.Contains(first) && !_betrothalCandidates.Contains(second),
                "The next conversation list uses the chosen player relative to find compatible counterparts");
            _betrothalSelected = first;
            SayLine("other_choice");
            object[] args = { null };
            check(CanSay("household_prompt") && !(bool)Line("household_ours").Clickable.DynamicInvoke(args)
                && ((TextObject)args[0]).GetID() == "BC_Betrothal_HouseholdBlocked"
                && (bool)Line("household_theirs").Clickable.DynamicInvoke(new object[] { null }),
                "Both household choices are shown in dialogue, with a hint for the protected heiress");

            _betrothalCommandScore = 94;
            SayLine("household_theirs");
            check(CanSay("terms_refused") && !CanSay("terms_ready") && _betrothalBarter == null && pledges.Count == 0,
                "The lord speaks a refusal without opening barter or creating a pledge");
            _betrothalCommandScore = 120;
            SayLine("household_theirs");
            check(CanSay("terms_ready") && Line("offer_terms").Input == Line("terms_ready").Output && pledges.Count == 0,
                "The lord opens the final confirmation dialogue before any pledge is recorded");

            SayLine("offer_terms");
            var items = _betrothalBarter.GetOfferedBarterables();
            check(items.Count == 2 && items[0].StringID == "bc_betrothal_pledge"
                && items[0].IsContextDependent && !(items[0] is MarriageBarterable)
                && items[1] is GoldBarterable && items[1].OriginalOwner == first.Clan.Leader
                && _betrothalBarter.GetBarterables().Count == 2 && pledges.Count == 0,
                "Native barter displays the pledge and deposit in the receiving house's column without trade goods or a wedding");
            check(items[0].Group == _betrothalBarter.GetBarterGroup<OtherBarterGroup>()
                && _betrothalBarter.ContextInitializer(items[0], _betrothalBarter)
                && campaign.BarterManager.IsOfferAcceptable(_betrothalBarter, _betrothalSpeaker, null),
                "Native barter accepts the fully funded pledge, and resetting the screen preserves its terms");
            SayLine("barter_cancelled");
            items[0].Apply();
            items[1].Apply();
            check(pledges.Count == 0, "Cancelling leaves no pledge, and a closed proposal cannot be applied later");

            ReachHousehold(); SayLine("household_theirs"); SayLine("offer_terms");
            Outsiders.Add(first);
            _betrothalBarter.GetOfferedBarterables().First().Apply();
            Outsiders.Remove(first);
            Ages[second] = originalAge;
            check(CanSay("barter_stale") && pledges.Count == 0, "Barter acceptance revalidates the pair before making a pledge");
            SayLine("barter_stale");

            ReachHousehold(); SayLine("household_theirs"); SayLine("offer_terms");
            var item = _betrothalBarter.GetOfferedBarterables().First();
            item.Apply(); item.Apply();
            check(CanSay("barter_agreed") && pledges.Count == 1 && Spouses.Count == 0 && _moves == 0,
                "Accepting twice creates exactly one pledge, never a wedding or household move");
            SayLine("barter_agreed");
            AccessTools.Method(type, "End").Invoke(behavior, new[] { pledges[0], "fixture_cleanup", null });
        }
        finally
        {
            Outsiders.Remove(first);
            barterManager.SetValue(campaign, oldBarter);
            conversationManager.SetValue(campaign, oldConversation);
            diplomacy.SetValue(campaign.Models, oldDiplomacy);
            harmony.UnpatchAll(harmony.Id);
            BetrothalLines.Clear();
            _betrothalSpeaker = _betrothalSelected = null;
            _betrothalCandidates = null;
            _betrothalRepeatLine = null;
            _betrothalBarter = null;
        }
    }
}
