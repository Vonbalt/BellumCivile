using System;
using System.Linq;
using System.Runtime.Serialization;
using BellumCivile;
using HarmonyLib;
using TaleWorlds.CampaignSystem.ViewModelCollection.KingdomManagement.Diplomacy;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

internal static class DiplomacyActionExecutionTests
{
    private static MBBindingList<KingdomDiplomacyProposalActionItemVM> _fresh;
    private static bool _enabled;
    private static int _refreshes, _oldCalls, _newCalls, _denied;
    private static MultiSelectionInquiryData _opened;
    private static bool CaptureInquiry(MultiSelectionInquiryData data) { _opened = data; return false; }
    private static bool ProposeText(ref TextObject __result) { __result = new TextObject("Propose"); return false; }
    private static object _externalMixin;
    private static bool ExternalMixin(ref object __result) { __result = _externalMixin; return false; }
    private sealed class ExternalFixture
    {
        public bool IsDirectActionVisible { get; set; } = true;
        public bool IsDirectActionEnabled { get; set; } = true;
        public string DirectActionName { get; set; } = "Break Alliance";
        public string DirectActionExplanationText { get; set; } = "End the alliance";
        public TaleWorlds.Core.ViewModelCollection.Information.HintViewModel DirectActionHint { get; set; }
        private void UpdateActionAvailability() { _refreshes++; }
        public void ExecuteDirectAction() { _newCalls++; }
    }
    private static bool Enabled(ref bool __result) { __result = _enabled; return false; }
    private static bool Refresh(KingdomDiplomacyVM __instance)
    { _refreshes++; AccessTools.Field(typeof(KingdomDiplomacyVM), "_actions").SetValue(__instance, _fresh); return false; }
    private static bool Denied() { _denied++; return false; }
    private static bool IgnoreLog() => false;
    private static T Empty<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    private static KingdomDiplomacyProposalActionItemVM Action(string id, bool enabled, Action callback, int cost = 100)
        => new KingdomDiplomacyProposalActionItemVM(new TextObject("Propose"), new TextObject("{=" + id + "}Action"),
            cost, enabled, new TextObject("Unavailable"), callback);

    internal static void Run(Action<bool, string> check)
    {
        var assembly = typeof(HostagePactRecord).Assembly;
        var menu = assembly.GetType("BellumCivile.UI.Diplomacy.BellumDiplomacyActionMenu", true);
        var describe = AccessTools.Method(menu, "Describe");
        var execute = AccessTools.Method(menu, "ExecuteFresh");
        var harmony = new Harmony("bellum.test.diplomacy_action_execution");
        void Patch(System.Reflection.MethodBase method, string name)
        {
            if (method == null) throw new InvalidOperationException("Missing action fixture target: " + name);
            harmony.Patch(method, prefix: new HarmonyMethod(typeof(DiplomacyActionExecutionTests), name));
        }
        var vm = Empty<KingdomDiplomacyVM>();
        var selected = Empty<KingdomTruceItemVM>();
        AccessTools.Field(typeof(KingdomDiplomacyVM), "_currentSelectedItem").SetValue(vm, selected);
        var original = Action("PYaUEZzn", true, () => _oldCalls++);
        object selection = describe.Invoke(null, new object[] { original });
        void Fresh(params KingdomDiplomacyProposalActionItemVM[] values)
        { _fresh = new MBBindingList<KingdomDiplomacyProposalActionItemVM>(); foreach (var value in values) _fresh.Add(value); }
        void Execute() => execute.Invoke(null, new[] { (object)vm, selected, selection });
        try
        {
            _enabled = true; _refreshes = _oldCalls = _newCalls = _denied = 0;
            Patch(AccessTools.PropertyGetter(assembly.GetType("BellumCivile.BellumCivileOptions"), "EnableWarPeaceLogicRevamp"), nameof(Enabled));
            Patch(AccessTools.Method(typeof(KingdomDiplomacyVM), "OnSetCurrentDiplomacyItem"), nameof(Refresh));
            Patch(AccessTools.Method(typeof(InformationManager), "ShowInquiry", new[] { typeof(InquiryData), typeof(bool), typeof(bool) }), nameof(Denied));
            Patch(AccessTools.Method(typeof(BellumCivileLogger), "Log"), nameof(IgnoreLog));
            Fresh(Action("PYaUEZzn", true, () => _newCalls++));
            AccessTools.Field(typeof(KingdomDiplomacyVM), "_actions").SetValue(vm, _fresh);
            Patch(AccessTools.Method(typeof(MBInformationManager), "ShowMultiSelectionInquiry"), nameof(CaptureInquiry));
            Patch(AccessTools.Method(typeof(GameTexts), "FindText", new[] { typeof(string), typeof(string) }), nameof(ProposeText));
            _opened = null;
            AccessTools.Method(menu, "Open").Invoke(null, new object[] { vm });
            check(_opened != null, "Opening the central diplomacy action creates a native selection inquiry");
            Execute();
            check(_refreshes == 1 && _newCalls == 1 && _oldCalls == 0 && _denied == 0,
                "A diplomacy choice executes the refreshed native action, never its stale callback");
            Fresh(Action("PYaUEZzn", false, () => _newCalls++));
            Execute();
            check(_newCalls == 1 && _denied == 1, "A newly disabled action is rejected after refresh");
            Fresh(Action("PYaUEZzn", true, () => _newCalls++, 200));
            Execute();
            check(_newCalls == 1 && _denied == 2, "A changed influence cost is rejected instead of charging a different price");
            Fresh(Action("PYaUEZzn", true, () => _newCalls++), Action("PYaUEZzn", true, () => _newCalls++));
            Execute();
            check(_newCalls == 1 && _denied == 3, "Ambiguous matching actions are rejected rather than executing the wrong command");
            AccessTools.Field(typeof(KingdomDiplomacyVM), "_currentSelectedItem").SetValue(vm, Empty<KingdomTruceItemVM>());
            Execute();
            check(_newCalls == 1 && _denied == 4 && _refreshes == 4,
                "Changing the selected realm invalidates the old menu without refreshing it");
            AccessTools.Field(typeof(KingdomDiplomacyVM), "_currentSelectedItem").SetValue(vm, selected);
            _enabled = false; Execute();
            check(_newCalls == 1 && _denied == 5 && _refreshes == 4,
                "Disabling the revamp invalidates an already-open action menu");
            var voluntary = describe.Invoke(null, new object[] { Action("BC_VoluntaryPact_ActionSupport", true, () => { }) });
            _enabled = true; Fresh(Action("BC_VoluntaryPact_ActionDescription", true, () => _newCalls++));
            execute.Invoke(null, new[] { (object)vm, selected, voluntary });
            check(_newCalls == 2, "A changed support-preview label still resolves to the same diplomatic action");

            var adapter = assembly.GetType("BellumCivile.UI.Diplomacy.DiplomacyExternalActionAdapter", true);
            check(!(bool)AccessTools.Method(adapter, "CanReplace").Invoke(null, new object[] { selected }),
                "An absent Diplomacy mixin leaves its original buttons available");
            Patch(AccessTools.Method(adapter, "FindMixin"), nameof(ExternalMixin));
            AccessTools.Field(typeof(KingdomTruceItemVM), "_hasAlliance").SetValue(selected, true);
            _externalMixin = new object();
            check(!(bool)AccessTools.Method(adapter, "CanReplace").Invoke(null, new object[] { selected }),
                "An unsupported Diplomacy action shape keeps its original controls visible");
            var fixture = new ExternalFixture(); _externalMixin = fixture;
            check((bool)AccessTools.Method(adapter, "CanReplace").Invoke(null, new object[] { selected }),
                "A recognized Diplomacy action can move into the central menu");
            var actionType = AccessTools.Inner(menu, "Selection");
            object externalChoice = Activator.CreateInstance(actionType, true);
            AccessTools.Field(actionType, "DiplomacyAlliance").SetValue(externalChoice, true);
            AccessTools.Field(actionType, "Fallback").SetValue(externalChoice, "Break Alliance");
            execute.Invoke(null, new[] { (object)vm, selected, externalChoice });
            check(_newCalls == 3, "The central menu invokes Diplomacy's own alliance cancellation after revalidation");
            fixture.IsDirectActionEnabled = false;
            execute.Invoke(null, new[] { (object)vm, selected, externalChoice });
            check(_newCalls == 3 && _denied == 6, "A newly disabled external alliance action cannot execute");
            fixture.IsDirectActionEnabled = true;
            AccessTools.Field(typeof(KingdomTruceItemVM), "_hasAlliance").SetValue(selected, false);
            execute.Invoke(null, new[] { (object)vm, selected, externalChoice });
            check(_newCalls == 3 && _denied == 7, "An alliance that ended while the menu was open cannot be ended again");
        }
        finally { harmony.UnpatchAll(harmony.Id); _fresh = null; _externalMixin = null; }
    }
}
