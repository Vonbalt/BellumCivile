using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using BellumCivile;
using BellumCivile.Behaviors;
using BellumCivile.UI.Parley;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Library;
using TaleWorlds.Localization;

internal static class HostageOptionAvailabilityTests
{
    private static Kingdom _home, _foreign;
    private static Clan _homeHouse, _foreignHouse;
    private static Hero _hero;
    private static Settlement _holding;
    private static IList _candidates;
    private static bool _homeHolding, _foreignHolding, _enabled, _pending;
    private static List<TreatyTermRecord> _edited;
    private static T Empty<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    private static bool No(ref bool __result) { __result = false; return false; }
    private static bool Enabled(ref bool __result) { __result = _enabled; return false; }
    private static bool Pending(ref bool __result) { __result = _pending; return false; }
    private static bool Ruler(Kingdom __instance, ref Clan __result)
    { __result = __instance == _home ? _homeHouse : _foreignHouse; return false; }
    private static bool Holding(Clan house, ref Settlement __result)
    { __result = (house == _homeHouse ? _homeHolding : _foreignHolding) ? _holding : null; return false; }
    private static bool Candidates(ref object __result) { __result = _candidates; return false; }
    private static bool Heroes(ref MBReadOnlyList<Hero> __result)
    { __result = new MBReadOnlyList<Hero>(new List<Hero> { _hero }); return false; }
    private static bool Name(ref TextObject __result) { __result = new TextObject("Test relative"); return false; }
    private static bool Direction(ref Kingdom fromKingdom, ref Kingdom toKingdom, ref bool __result)
    { fromKingdom = _foreign; toKingdom = _home; __result = true; return false; }
    private static bool Replace(IEnumerable<TreatyTermRecord> __0) { _edited = __0.ToList(); return false; }

    internal static void Run(Action<bool, string> check)
    {
        var harmony = new Harmony("bellum.tests.hostage.option_availability");
        var assembly = typeof(TreatyTermRecord).Assembly;
        var terms = assembly.GetType("BellumCivile.TreatyHostageTerms");
        var candidateType = assembly.GetType("BellumCivile.TreatyHostageCandidate");
        void Patch(MethodBase method, string prefix) => harmony.Patch(method,
            prefix: new HarmonyMethod(typeof(HostageOptionAvailabilityTests), prefix));
        _home = Empty<Kingdom>(); _home.StringId = "home";
        _foreign = Empty<Kingdom>(); _foreign.StringId = "foreign";
        _homeHouse = Empty<Clan>(); _foreignHouse = Empty<Clan>();
        _hero = Empty<Hero>(); _hero.StringId = "relative";
        _holding = Empty<Settlement>();
        _candidates = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(candidateType));
        object Candidate(int tier) => Activator.CreateInstance(candidateType, BindingFlags.Instance | BindingFlags.NonPublic,
            null, new object[] { _hero, tier }, null);
        var candidate = Candidate(2);
        _candidates.Add(candidate);
        _enabled = _foreignHolding = true; _homeHolding = _pending = false;
        var vm = Empty<PeaceParleyVM>();
        AccessTools.Field(typeof(PeaceParleyVM), "<AvailablePoliticsTerms>k__BackingField")
            .SetValue(vm, new MBBindingList<TreatyClaimDraftOptionVM>());
        var proposal = new TreatyProposalRecord("war", "home", "foreign", "home", 0, 50, false);
        TreatyClaimDraftOptionVM Row(bool offering = false, string winner = "home", bool locked = false)
        {
            proposal = new TreatyProposalRecord("war", winner, winner == "home" ? "foreign" : "home", "home", 0, 50, false);
            AccessTools.Field(typeof(PeaceParleyVM), "_proposal").SetValue(vm, proposal);
            AccessTools.Field(typeof(PeaceParleyVM), "_isOfferingsMode").SetValue(vm, offering);
            AccessTools.Field(typeof(PeaceParleyVM), "_isDraftEditingDisabled").SetValue(vm, locked);
            return Rebuild(offering);
        }
        TreatyClaimDraftOptionVM Rebuild(bool offering = false)
        {
            vm.AvailablePoliticsTerms.Clear();
            AccessTools.Method(typeof(PeaceParleyVM), "AddHostageOption").Invoke(vm,
                new object[] { offering ? _home : _foreign, offering ? _foreign : _home });
            return vm.AvailablePoliticsTerms.Single();
        }
        string Hint(TreatyClaimDraftOptionVM row)
        {
            var hint = AccessTools.Property(typeof(TreatyClaimDraftOptionVM), "Hint").GetValue(row);
            return ((TextObject)AccessTools.Field(hint.GetType(), "HintText").GetValue(hint)).ToString();
        }
        (int Count, string Reason) Available(int days = 50)
        {
            object[] args = { _foreign, _home, null, (int?)days };
            var result = (IEnumerable)AccessTools.Method(terms, "Available",
                new[] { typeof(Kingdom), typeof(Kingdom), typeof(string).MakeByRefType(), typeof(int?) }).Invoke(null, args);
            return (result.Cast<object>().Count(), (string)args[2]);
        }

        try
        {
            Patch(AccessTools.PropertyGetter(typeof(Kingdom), "IsEliminated"), nameof(No));
            Patch(AccessTools.PropertyGetter(typeof(Kingdom), "RulingClan"), nameof(Ruler));
            Patch(AccessTools.PropertyGetter(assembly.GetType("BellumCivile.BellumCivileOptions"), "EnableWarPeaceLogicRevamp"), nameof(Enabled));
            Patch(AccessTools.Method(typeof(CivilWarConflictBehavior), "IsRealmTransferPending"), nameof(Pending));
            Patch(AccessTools.Method(typeof(HostagePactBehavior), "SelectHolding"), nameof(Holding));
            Patch(AccessTools.Method(assembly.GetType("BellumCivile.TreatyHostageEligibility"), "GetCandidates"), nameof(Candidates));
            Patch(AccessTools.PropertyGetter(typeof(Hero), "AllAliveHeroes"), nameof(Heroes));
            Patch(AccessTools.PropertyGetter(typeof(Hero), "Name"), nameof(Name));
            Patch(AccessTools.Method(typeof(PeaceParleyVM), "ReplaceDraft"), nameof(Replace));
            Patch(AccessTools.Method(typeof(PeaceParleyVM), "TryResolveCurrentDirection"), nameof(Direction));

            var available = Available();
            check(available.Count == 0 && available.Reason == "the receiving ruling house has no town or castle free of siege for the hostage",
                "Landless receiving ruler gets an explicit holding requirement from shared eligibility");
            var row = Row();
            check(row.IsDisabled && !row.IsSelected && Hint(row).Contains("own a town or castle") && Hint(row).Contains("not under siege"),
                "Landless player's hostage demand stays visible and explains the holding requirement");
            check(row.CostText == string.Empty, "Unavailable candidate list does not advertise a zero-cost hostage");
            _edited = null; row.ExecuteToggle();
            check(_edited == null && proposal.Terms.Count == 0, "Disabled hostage option cannot change the draft");
            row = Row(offering: true);
            check(!row.IsDisabled && row.CostText == "24 WS", "Landless player can still offer a hostage to a landed foreign ruler at the unchanged price");
            _foreignHolding = false;
            check(Row(offering: true).IsDisabled && Hint(vm.AvailablePoliticsTerms.Single()).Contains("receiving ruling house"),
                "Offerings explain the same custody requirement when the foreign recipient lacks a holding");
            _foreignHolding = _homeHolding = true;
            available = Available();
            check(available.Count == 1 && available.Reason == null, "Eligible hostage list remains unchanged once a holding is available");
            row = Row();
            check(!row.IsDisabled && Hint(row).Contains("50 days") && row.CostText == "24 WS", "Eligible demands retain the pact description and negotiated pricing");
            row = Row(winner: "foreign");
            check(row.IsDisabled && Hint(row).Contains("winner of this treaty") && Hint(row).Contains("offer one voluntarily"),
                "Demand against the designated winner remains visible with its direction restriction");
            check(!Row(offering: true, winner: "foreign").IsDisabled, "Player on the losing side can still offer a hostage");
            check(!Row(offering: true, winner: "home").IsDisabled, "Winning player can still offer a reciprocal hostage voluntarily");
            _candidates.Clear();
            row = Row();
            check(row.IsDisabled && row.CostText == string.Empty && Hint(row).Contains("No eligible blood relative"),
                "No eligible royal relative produces a visible disabled option with a specific explanation");
            _candidates.Add(candidate); _candidates.Add(Candidate(4));
            check(Row().CostText == "12 WS", "Candidate preview still uses the cheapest eligible hostage");
            _pending = true;
            row = Row();
            check(row.IsDisabled && Hint(row).Contains("transfer of sovereignty"), "Pending realm transfer displays the existing realm eligibility explanation");
            _pending = false; _enabled = false;
            check(Row().IsDisabled, "Visible option does not bypass the disabled war-and-peace revamp");
            _enabled = true;
            check(Available(0).Count == 0 && Available(0).Reason == "hostage selection requires a valid positive pact duration",
                "Legacy zero-duration draft has an explicit selection requirement");
            check(Row(locked: true).IsDisabled, "Read-only parley permissions remain enforced for available hostages");

            Row();
            var saved = new TreatyTermRecord(TreatyTermType.HostagePeace, 24, durationDays: 133,
                fromKingdomId: "foreign", toKingdomId: "home", heroId: "relative", hostageTier: 2);
            proposal.ReplaceTerms(new[] { saved });
            _homeHolding = false; _candidates.Clear();
            row = Rebuild();
            check(row.IsSelected && !row.IsDisabled && row.HasDurationControls && row.CostText == "24 WS" && row.Name.Contains("133 days"),
                "Unavailable selected legacy pledge stays removable without rewriting duration or price");
            _edited = null; row.ExecuteToggle();
            check(_edited != null && _edited.Count == 0, "Actual selected-row command can remove an unavailable pledge");
            AccessTools.Field(typeof(PeaceParleyVM), "_isDraftEditingDisabled").SetValue(vm, true);
            row = Rebuild(); _edited = null; row.ExecuteToggle();
            check(row.IsDisabled && _edited == null, "Unavailable selected pledge does not bypass read-only permissions");
        }
        finally
        {
            harmony.UnpatchAll(harmony.Id);
            _candidates = null; _edited = null;
            _home = _foreign = null; _homeHouse = _foreignHouse = null; _hero = null; _holding = null;
        }
    }
}
