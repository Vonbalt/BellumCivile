using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Conversation;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Localization;

internal static partial class AsylumVisitTests
{
    private sealed class AudienceLine
    {
        internal string Id, Input, Output, Text;
        internal bool Player;
        internal Delegate Condition, Consequence, Clickable;
    }
    private static readonly Dictionary<string, AudienceLine> AudienceLines = new Dictionary<string, AudienceLine>();
    private static Settlement _narrativeEstate;
    private static bool CaptureAudience(MethodBase __originalMethod, object[] __args, ref ConversationSentence __result)
    {
        bool repeat = __originalMethod.Name == "AddRepeatablePlayerLine";
        var line = new AudienceLine { Id = (string)__args[0], Input = (string)__args[1], Output = (string)__args[2],
            Text = (string)__args[3], Player = __originalMethod.Name != "AddDialogLine",
            Condition = (Delegate)__args[repeat ? 6 : 4], Consequence = (Delegate)__args[repeat ? 7 : 5],
            Clickable = (Delegate)__args[repeat ? 9 : 7] };
        AudienceLines.Add(line.Id, line);
        if (repeat) AudienceLines.Add(line.Id + "_continue", new AudienceLine { Id = line.Id + "_continue", Player = true,
            Input = line.Input, Output = (string)__args[5], Text = (string)__args[4] });
        __result = null; return false;
    }
    private static bool NarrativeVillages(ref IEnumerable<Settlement> __result)
    { __result = new[] { _narrativeEstate }; return false; }

    private static void CheckAsylumNarrative(Action<bool, string> check, Harmony h, Action reset, Settlement keep, LordshipBehavior registry)
    {
        foreach (var method in typeof(CampaignGameStarter).GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => new[] { "AddPlayerLine", "AddDialogLine", "AddRepeatablePlayerLine" }.Contains(m.Name)))
            h.Patch(method, prefix: new HarmonyMethod(typeof(AsylumVisitTests), nameof(CaptureAudience)));
        h.Patch(AccessTools.Method(typeof(LordshipBehavior), "Villages"),
            prefix: new HarmonyMethod(typeof(AsylumVisitTests), nameof(NarrativeVillages)));
        void Register()
        {
            AudienceLines.Clear(); Call(_behavior, "RegisterAsylumDialogues", Blank<CampaignGameStarter>());
        }
        void Act(string id) => AudienceLines[id].Consequence?.DynamicInvoke();
        bool Available(string id) => AudienceLines[id].Condition == null || (bool)AudienceLines[id].Condition.DynamicInvoke();
        reset(); Call(_behavior, "TryCreateAsylumVisit", _exile, _realm);
        Locations[_ruler] = keep; Call(_behavior, "BeginAsylumConversation"); Register();
        var visit = Visits()[_exile.StringId];
        var tokens = new HashSet<string>(AudienceLines.Values.Select(l => l.Input));
        check(AudienceLines.Values.All(l => l.Output == "close_window" || tokens.Contains(l.Output)),
            "Every revised dialogue branch leads to a registered response or closes the audience");
        var context = (Dictionary<string, string>)AccessTools.Field(Behavior, "_trackedOriginKingdomIds").GetValue(_behavior);
        var causes = (Dictionary<string, int>)AccessTools.Field(Behavior, "_trackedExileCauseIds").GetValue(_behavior);
        context[_exile.StringId] = _foreign.StringId; causes[_exile.StringId] = (int)ExileCause.Treason;
        Call(_behavior, "ShowPlayerAsylumInquiry", _exile, _realm);
        check(_inquiry.TitleText == "Asylum Request" && _inquiry.Text.Contains("A messenger brings you a letter")
            && _inquiry.Text.Contains(_foreign.StringId) && _inquiry.Text.Contains(keep.StringId) && _inquiry.Text.Contains("7 days"),
            "The letter names the sender, former realm, keep and unchanged audience deadline");
        check(!_inquiry.Text.Contains("treason") && !_inquiry.Text.Contains("court") && !_inquiry.Text.Contains("Reports say"),
            "The letter does not repeat the detailed exile or former court-faction explanation");
        check(((TextObject)Call(_behavior, "AsylumExplanation", visit)).ToString().Contains("treason"),
            "The detailed reason remains in the in-person conversation");
        Women.Add(_guest); visit.Notified = false; Call(_behavior, "ShowPlayerAsylumInquiry", _exile, _realm);
        check(_inquiry.Text.Contains("She asks") && _inquiry.Text.Contains("her sword") && _inquiry.Text.Contains("receive her"),
            "The letter uses the petitioning leader's correct pronouns"); Women.Clear();
        causes[_exile.StringId] = (int)ExileCause.VoluntaryDeparture;
        check(((TextObject)Call(_behavior, "BuildAsylumLetterContext", _exile.StringId)).ToString().Contains("left the service"),
            "Voluntary departures are not described as forced exile in the letter");
        causes[_exile.StringId] = (int)ExileCause.KingdomDestroyed;
        check(((TextObject)Call(_behavior, "BuildAsylumLetterContext", _exile.StringId)).ToString().Contains("fall of"),
            "Destroyed realms receive a truthful, short letter introduction");
        context[_exile.StringId] = "missing_realm";
        check(((TextObject)Call(_behavior, "BuildAsylumLetterContext", _exile.StringId)).ToString().Contains("hard times"),
            "Unknown former realms have a complete letter fallback");

        check(AudienceLines["bc_asylum_accept_estate"].Input == "bc_asylum_conclusion"
            && !AudienceLines.Values.Any(l => l.Input == "bc_asylum_choices" && l.Id == "bc_asylum_accept_estate"),
            "An estate is offered only at the conclusion, not before the oath");
        check(!Available("bc_asylum_accept_estate") && _joins == 0, "The post-oath estate path cannot be entered before admission");
        var playerIds = new[] { "bc_asylum_oath_prompt", "bc_asylum_oath_bond", "bc_asylum_oath_third_prompt", "bc_asylum_oath_fourth_prompt" };
        var nobleIds = new[] { "bc_asylum_oath_first", "bc_asylum_oath_second", "bc_asylum_oath_third", "bc_asylum_oath_fourth" };
        for (int i = 0; i < 4; i++)
        {
            var ruler = AudienceLines[playerIds[i]]; var noble = AudienceLines[nobleIds[i]];
            string words = "{BC_ASYLUM_OATH_" + (i + 1) + "}";
            check(ruler.Player && !noble.Player && ruler.Output == noble.Input && ruler.Text.Contains(words) && noble.Text.Contains(words),
                "Both speakers recite the same native-style oath line: " + (i + 1));
            Act(nobleIds[i]);
            check(_joins == 0 && _relations == 0, "Reciting an oath line cannot grant membership or rewards: " + (i + 1));
        }
        Act("bc_asylum_oath_accept");
        check(_joins == 1 && _relations == 10 && Available("bc_asylum_accept_estate")
            && AudienceLines["bc_asylum_oath_success"].Output == "bc_asylum_conclusion",
            "The ruler's acknowledgement establishes vassalage before reaching the reward choices");
        var previousGrant = registry.GetGrant(_guest);
        if (previousGrant != null) Call(registry, "Remove", previousGrant);
        var village = Blank<Settlement>(); village.StringId = "post_oath_estate";
        var component = Blank<Village>(); AccessTools.Field(typeof(Settlement), "Village").SetValue(village, component);
        AccessTools.Field(typeof(Village), "_bound").SetValue(component, keep); _narrativeEstate = village;
        object[] clickable = { null };
        check((bool)AudienceLines["bc_asylum_accept_estate"].Clickable.DynamicInvoke(clickable),
            "The admitted landless leader can be offered a vacant estate through the real grant rules");
        AccessTools.Field(Behavior, "_asylumOfferedVillage").SetValue(_behavior, village);
        Act("bc_asylum_estate_cancel");
        check(Realms[_exile] == _realm && _relations == 10 && registry.GetGrant(_guest) == null
            && AudienceLines["bc_asylum_conclusion_resume"].Output == "bc_asylum_conclusion",
            "Canceling estate selection preserves the oath and returns to a safe conclusion choice");
        AccessTools.Field(Behavior, "_asylumOfferedVillage").SetValue(_behavior, village);
        Act("bc_asylum_estate_confirm"); Act("bc_asylum_estate_confirm");
        check(_joins == 1 && _relations == 20 && registry.GetGrant(_guest)?.Village == village
            && AudienceLines["bc_asylum_estate_confirm"].Output == "bc_asylum_estate_result",
            "The final spoken estate award grants it once, after the oath and its acceptance");
        check(AudienceLines["bc_asylum_conclude"].Output == "bc_asylum_service_thanks"
            && AudienceLines["bc_asylum_service_thanks"].Output == "close_window", "A no-gift conclusion has a complete exit path");
        foreach (var line in AudienceLines.Values.Where(l => l.Input == "bc_asylum_conclusion" || l.Input == "bc_asylum_estates"
            || l.Input == "bc_asylum_estate_confirm").Where(l => l.Id.EndsWith("_stale")))
            check(!Available(line.Id), "Valid admitted visitors do not see stale-state exits: " + line.Input);
        _block = "prisoner";
        check(Available("bc_asylum_conclusion_stale") && !Available("bc_asylum_accept_estate"),
            "An invalidated admitted visitor still has a safe dialogue exit"); _block = null;

        MBTextManager.SetTextVariable("BC_ASYLUM_LIEGE_TITLE", "king");
        MBTextManager.SetTextVariable("CONVERSATION_NPC", TextObject.GetEmpty().SetTextVariable("GENDER", 1));
        Call(Behavior, "SetAsylumOathTexts");
        string first = new TextObject("{=!}{BC_ASYLUM_OATH_1}").ToString();
        string second = new TextObject("{=!}{BC_ASYLUM_OATH_2}").ToString();
        check(first.Contains("lawful king"), "Repeated oath text retains the configured ruler title: " + first);
        check(second.Contains("loyal follower"), "Repeated oath text retains female-vassal grammar: " + second);
        MBTextManager.SetTextVariable("CONVERSATION_NPC", TextObject.GetEmpty().SetTextVariable("GENDER", 0));
        check(new TextObject("{=!}{BC_ASYLUM_OATH_2}").ToString().Contains("loyal man"), "The same oath supports male-vassal grammar");
        Call(_behavior, "ProcessAsylumVisits");
        check(!(bool)Call(_behavior, "CanConcludeAsylum") && Realms[_exile] == _realm && _relations == 20,
            "Ending the completed audience invalidates its runtime grant context without undoing vassalage");
        var awarded = registry.GetGrant(_guest); if (awarded != null) Call(registry, "Remove", awarded);
        reset(); Call(_behavior, "TryCreateAsylumVisit", _exile, _realm); Locations[_ruler] = keep;
        Call(_behavior, "BeginAsylumConversation"); Call(_behavior, "AcknowledgeAsylumOath"); Call(_behavior, "CompleteAsylumOath");
        var save = new Store(); Call(_behavior, "SyncAsylumVisits", save); save.IsLoading = true;
        var restored = new ExiledClanRecoveryBehavior(); Call(restored, "SyncAsylumVisits", save); _behavior = restored; _day += 8;
        Call(_behavior, "ProcessAsylumVisits");
        check(Realms[_exile] == _realm && _joins == 1 && _relations == 10 && registry.GetGrant(_guest) == null && Visits().Count == 0,
            "A save after oath acceptance cannot expire the admitted clan or invent an unconfirmed gift");
        AudienceLines.Clear(); _narrativeEstate = null;
    }
}
