using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Serialization;
using System.Threading;
using BellumCivile.Patches;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Library;

internal static class SiegeOwnershipTests
{
    private static Settlement _settlement;
    private static Town _town;
    private static Hero _oldOwner, _newOwner, _capturer, _eventCapturer;
    private static Clan _oldClan, _newClan, _capturerClan;
    private static MobileParty _garrison, _heroParty;
    private static PartyBase _destroyer;
    private static int _events;
    private static bool _openToClaim;
    private static ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail _detail;
    private static readonly List<string> Steps = new List<string>();
    private static T Empty<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    private static void Set(object target, string field, object value) => AccessTools.Field(target.GetType(), field).SetValue(target, value);
    private static bool Yes(ref bool __result) { __result = true; return false; }
    private static bool Skip() => false;
    private static bool Owner(ref Clan __result) { __result = _town.OwnerClan; return false; }
    private static bool Leader(Clan __instance, ref Hero __result)
    { __result = __instance == _oldClan ? _oldOwner : _newOwner; return false; }
    private static bool HeroParty(ref MobileParty __result) { __result = _heroParty; return false; }
    private static bool NoFaction(ref IFaction __result) { __result = null; return false; }
    private static bool NoClans(ref IEnumerable<Clan> __result) { __result = Enumerable.Empty<Clan>(); return false; }
    private static bool Garrison(ref MobileParty __result) { __result = _garrison; return false; }
    private static bool ChangeClan(Clan value) { Set(_town, "_ownerClan", value); Steps.Add("owner"); return false; }
    private static bool Destroy(PartyBase destroyerParty, MobileParty destroyedParty)
    {
        if (_town.OwnerClan != _newClan || destroyedParty != _garrison)
            throw new Exception("Garrison cleanup ran before assignment or targeted the wrong party.");
        _destroyer = destroyerParty; _garrison = null; Steps.Add("destroy"); return false;
    }
    private static bool AddGarrison() { _garrison = Empty<MobileParty>(); Steps.Add("garrison"); return false; }
    private static bool RemoveGovernor() { Steps.Add("governor"); return false; }
    private static bool Dispatcher(ref CampaignEventDispatcher __result) { __result = Empty<CampaignEventDispatcher>(); return false; }
    private static bool OwnerChanged(Settlement settlement, bool openToClaim, Hero newOwner, Hero oldOwner,
        Hero capturerHero, ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail detail)
    {
        if (settlement != _settlement || newOwner != _newOwner || oldOwner != _oldOwner)
            throw new Exception("Ownership event arguments changed.");
        _events++; _eventCapturer = capturerHero; _detail = detail; _openToClaim = openToClaim;
        Steps.Add("event"); return false;
    }

    private static object Enter(Settlement settlement, MobileParty party)
    {
        object[] args = { settlement, party, null };
        AccessTools.Method(typeof(SiegeCaptureContextPatch), "Prefix").Invoke(null, args);
        return args[2];
    }

    private static Exception Leave(object state, Exception error = null) =>
        (Exception)AccessTools.Method(typeof(SiegeCaptureContextPatch), "Finalizer").Invoke(null, new[] { error, state });

    private static MobileParty Party()
    {
        var mobile = Empty<MobileParty>();
        Set(mobile, "<Party>k__BackingField", Empty<PartyBase>());
        return mobile;
    }

    private static void Reset(bool hasGarrison = true)
    {
        Set(_town, "_ownerClan", _oldClan);
        _town.LastCapturedBy = null;
        _town.IsOwnerUnassigned = true;
        _garrison = hasGarrison ? Empty<MobileParty>() : null;
        _destroyer = null; _events = 0; _eventCapturer = null; Steps.Clear();
    }

    internal static void Run(Action<bool, string> check)
    {
        var harmony = new Harmony("bellum.tests.siege_ownership");
        void Patch(MethodBase method, string prefix) => harmony.Patch(method,
            prefix: new HarmonyMethod(typeof(SiegeOwnershipTests), prefix));
        _settlement = Empty<Settlement>(); _town = Empty<Town>();
        _oldOwner = Empty<Hero>(); _newOwner = Empty<Hero>(); _capturer = Empty<Hero>();
        _oldClan = Empty<Clan>(); _newClan = Empty<Clan>(); _capturerClan = Empty<Clan>();
        Set(_oldOwner, "_clan", _oldClan); Set(_newOwner, "_clan", _newClan); Set(_capturer, "_clan", _capturerClan);
        Set(_settlement, "Town", _town);
        Set(_settlement, "<Party>k__BackingField", Empty<PartyBase>());
        Set(_settlement, "_boundVillages", new MBList<Village>());
        _heroParty = null;
        try
        {
            // Stub world services, but execute the installed native ownership action and its branches.
            Patch(AccessTools.PropertyGetter(typeof(Settlement), "IsFortification"), nameof(Yes));
            Patch(AccessTools.PropertyGetter(typeof(Settlement), "OwnerClan"), nameof(Owner));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "Leader"), nameof(Leader));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "NonBanditFactions"), nameof(NoClans));
            Patch(AccessTools.PropertyGetter(typeof(Hero), "PartyBelongedTo"), nameof(HeroParty));
            Patch(AccessTools.PropertyGetter(typeof(Hero), "MapFaction"), nameof(NoFaction));
            Patch(AccessTools.PropertyGetter(typeof(Fief), "GarrisonParty"), nameof(Garrison));
            Patch(AccessTools.Method(typeof(Town), "ChangeClanInternal"), nameof(ChangeClan));
            Patch(AccessTools.Method(typeof(DestroyPartyAction), "Apply"), nameof(Destroy));
            Patch(AccessTools.Method(typeof(Settlement), "AddGarrisonParty"), nameof(AddGarrison));
            Patch(AccessTools.Method(typeof(ChangeGovernorAction), "RemoveGovernorOfIfExists"), nameof(RemoveGovernor));
            Patch(AccessTools.Method(typeof(PartyBase), "SetVisualAsDirty"), nameof(Skip));
            Patch(AccessTools.PropertyGetter(typeof(CampaignEventDispatcher), "Instance"), nameof(Dispatcher));
            Patch(AccessTools.Method(typeof(CampaignEventDispatcher), "OnSettlementOwnerChanged"), nameof(OwnerChanged));

            Reset();
            bool crashed = false;
            try { ChangeOwnerOfSettlementAction.ApplyBySiege(_newOwner, _capturer, _settlement); }
            catch (NullReferenceException) { crashed = true; }
            check(crashed && _town.OwnerClan == _newClan && _events == 0 && Steps.SequenceEqual(new[] { "owner" }),
                "Unpatched native action reproduces the crash after assignment and before ownership notification");

            check(harmony.CreateClassProcessor(typeof(SiegeCaptureContextPatch)).Patch().Count == 1,
                "Capturing-party context binds to installed SiegeCompleted");
            check(harmony.CreateClassProcessor(typeof(SiegeGarrisonOwnershipPatch)).Patch().Count == 1,
                "Garrison guard matches and compiles against installed ApplyInternal");
            CheckRewrite(check);
            var capturingParty = Party();
            var otherSettlement = Empty<Settlement>();
            object outer = Enter(_settlement, capturingParty);
            try
            {
                check(SiegeGarrisonOwnershipPatch.ResolveDestroyer(_capturer, _settlement) == capturingParty.Party,
                    "Absent owner uses the actual capturing party without needing a leader");
                check(SiegeGarrisonOwnershipPatch.ResolveDestroyer(_capturer, otherSettlement) == null,
                    "Another settlement cannot borrow the current capturing party");
                PartyBase threaded = capturingParty.Party;
                var thread = new Thread(() => threaded = SiegeGarrisonOwnershipPatch.ResolveDestroyer(_capturer, _settlement));
                thread.Start(); thread.Join();
                check(threaded == null, "Capturing-party context is isolated to the siege thread");
                var innerParty = Party();
                object inner = Enter(otherSettlement, innerParty);
                try
                {
                    check(SiegeGarrisonOwnershipPatch.ResolveDestroyer(_capturer, otherSettlement) == innerParty.Party
                        && SiegeGarrisonOwnershipPatch.ResolveDestroyer(_capturer, _settlement) == null,
                        "Nested siege owns only its own settlement context");
                }
                finally { Leave(inner); }
                check(SiegeGarrisonOwnershipPatch.ResolveDestroyer(_capturer, _settlement) == capturingParty.Party,
                    "Nested completion restores the outer siege");
                Leave(null);
                check(SiegeGarrisonOwnershipPatch.ResolveDestroyer(_capturer, _settlement) == capturingParty.Party,
                    "Skipped prefix does not clear an enclosing siege context");

                foreach (bool hasGarrison in new[] { true, false })
                foreach (bool ownerHasParty in new[] { false, true })
                {
                    Reset(hasGarrison); _heroParty = ownerHasParty ? Party() : null;
                    ChangeOwnerOfSettlementAction.ApplyBySiege(_newOwner, _capturer, _settlement);
                    check(_events == 1 && _eventCapturer == _capturer && _openToClaim
                        && _detail == ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail.BySiege
                        && _town.LastCapturedBy == _capturerClan && _town.OwnerClan == _newClan,
                        $"Native siege completes once with original capturer, clan credit and transfer type (events={_events}, capturer={_eventCapturer == _capturer}, claim={_openToClaim}, detail={_detail}, credit={_town.LastCapturedBy == _capturerClan}, owner={_town.OwnerClan == _newClan})");
                    check(Steps.SequenceEqual(hasGarrison ? new[] { "owner", "destroy", "garrison", "governor", "event" }
                        : new[] { "owner", "garrison", "governor", "event" }),
                        "Assignment, optional garrison destruction, replacement and notification retain native order");
                    check(!hasGarrison || _destroyer == (ownerHasParty ? _heroParty.Party : capturingParty.Party),
                        "Existing hero party retains priority; missing party uses captured context");
                }
                _heroParty = null;
                foreach (Action<Hero, Settlement> transfer in new Action<Hero, Settlement>[] {
                    ChangeOwnerOfSettlementAction.ApplyByDefault, ChangeOwnerOfSettlementAction.ApplyByKingDecision,
                    ChangeOwnerOfSettlementAction.ApplyByBarter })
                {
                    Reset(); var garrison = _garrison; transfer(_newOwner, _settlement);
                    check(_events == 1 && _eventCapturer == null && _garrison == garrison && !Steps.Contains("destroy"),
                        "Non-siege ownership transfer leaves existing garrison and attribution unchanged");
                }

                var manager = Empty<KingdomManager>();
                manager.SiegeCompleted(otherSettlement, innerParty, false, MapEvent.BattleTypes.Siege);
                check(SiegeGarrisonOwnershipPatch.ResolveDestroyer(_capturer, _settlement) == capturingParty.Party,
                    "Real native early return restores outer context through Harmony finalizer");
                bool failed = false;
                try { manager.SiegeCompleted(otherSettlement, null, true, MapEvent.BattleTypes.Siege); }
                catch (NullReferenceException) { failed = true; }
                check(failed && SiegeGarrisonOwnershipPatch.ResolveDestroyer(_capturer, _settlement) == capturingParty.Party,
                    "Real native exception propagates and Harmony finalizer restores outer context");
            }
            finally { _heroParty = null; Leave(outer); }

            check(SiegeGarrisonOwnershipPatch.ResolveDestroyer(_capturer, _settlement) == null,
                "Completed siege cannot leak a party into later ownership actions");
            Reset(); ChangeOwnerOfSettlementAction.ApplyBySiege(_newOwner, _capturer, _settlement);
            check(_events == 1 && _destroyer == null, "Direct siege action without context uses native-supported unknown destroyer");
            var error = new InvalidOperationException("unrelated failure");
            check(ReferenceEquals(Leave(Enter(_settlement, capturingParty), error), error),
                "Context guard never swallows or replaces an unrelated exception");
        }
        finally { harmony.UnpatchAll(harmony.Id); _heroParty = null; }
    }

    private static void CheckRewrite(Action<bool, string> check)
    {
        var action = AccessTools.Method(typeof(ChangeOwnerOfSettlementAction), "ApplyInternal");
        var original = PatchProcessor.GetOriginalInstructions(action);
        var rewritten = SiegeGarrisonOwnershipPatch.Transpiler(original.Select(i => new CodeInstruction(i))).ToList();
        var differences = original.Select((instruction, index) => index).Where(i =>
            original[i].opcode != rewritten[i].opcode || !Equals(original[i].operand, rewritten[i].operand)).ToList();
        check(original.Count == rewritten.Count && differences.Count == 2 && differences[1] == differences[0] + 1,
            "Transpiler replaces exactly two adjacent lookup instructions, not native ownership/event logic");
        check(original.Select((instruction, i) => instruction.labels.SequenceEqual(rewritten[i].labels)
            && instruction.blocks.SequenceEqual(rewritten[i].blocks)).All(same => same),
            "Every native branch label and exception boundary survives rewriting");
        foreach (var changed in new[] { new List<CodeInstruction>(), original.Concat(original).ToList() })
        {
            bool rejected = false;
            try { SiegeGarrisonOwnershipPatch.Transpiler(changed); }
            catch (InvalidOperationException) { rejected = true; }
            check(rejected, "Missing or ambiguous native cleanup anchors reject the patch explicitly");
        }
    }
}
