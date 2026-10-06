using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using BellumCivile;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Localization;

internal static class VoteLobbyingArmyTests
{
    private static readonly Dictionary<string, Delegate> Conditions = new Dictionary<string, Delegate>();
    private static Clan _playerClan, _voterClan;
    private static Hero _player, _speaker, _leader;
    private static Kingdom _realm, _playerRealm;
    private static ElectiveSuccessionBehavior _elections;
    private static ElectionLobbyingBehavior _lobbying;
    private static ElectiveSuccessionRecord _ballot;
    private static CampaignTime _until;
    private static bool _inArmy, _mercenary, _prisoner, _canPromise;
    private static int _commits;
    private static T Blank<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    private static void Field(object target, string name, object value) => AccessTools.Field(target.GetType(), name).SetValue(target, value);
    private static object Call(object target, string method, params object[] args) => AccessTools.Method(target.GetType(), method).Invoke(target, args);
    private static bool Capture(object[] __args)
    {
        Conditions[(string)__args[0]] = __args[4] as Delegate;
        return false;
    }
    private static bool Skip() => false;
    private static bool Yes(ref bool __result) { __result = true; return false; }
    private static bool InArmy(ref bool __result) { __result = _inArmy; return false; }
    private static bool PlayerClan(ref Clan __result) { __result = _playerClan; return false; }
    private static bool HeroClan(Hero __instance, ref Clan __result)
    { __result = __instance == _player ? _playerClan : _voterClan; return false; }
    private static bool Realm(Clan __instance, ref Kingdom __result)
    { __result = __instance == _playerClan ? _playerRealm : _realm; return false; }
    private static bool Player(ref Hero __result) { __result = _player; return false; }
    private static bool Speaker(ref Hero __result) { __result = _speaker; return false; }
    private static bool Leader(ref Hero __result) { __result = _leader; return false; }
    private static bool Name(ref TextObject __result) { __result = new TextObject("Test lord"); return false; }
    private static bool Party(ref MobileParty __result) { __result = null; return false; }
    private static bool Mercenary(ref bool __result) { __result = _mercenary; return false; }
    private static bool Prisoner(ref bool __result) { __result = _prisoner; return false; }
    private static bool Now(ref CampaignTime __result) { __result = default(CampaignTime); return false; }
    private static bool Elections(ref ElectiveSuccessionBehavior __result) { __result = _elections; return false; }
    private static bool Lobbying(ref ElectionLobbyingBehavior __result) { __result = _lobbying; return false; }
    private static bool Ballot(ref ElectiveSuccessionRecord __result) { __result = _ballot; return false; }
    private static bool CanPromise(ref CampaignTime until, ref bool __result)
    { until = _until; __result = _canPromise; return false; }
    private static bool Commit(ref bool __result) { _commits++; __result = true; return false; }
    private static bool Openness(ref double __result) { __result = 100; return false; }

    internal static void Run(Action<bool, string> check)
    {
        var harmony = new Harmony("bellum.tests.vote_lobbying_army");
        var dayTicks = AccessTools.Field(typeof(CampaignTime), "TimeTicksPerDay");
        var oldTicks = dayTicks.GetValue(null);
        void Patch(MethodBase target, string prefix) => harmony.Patch(target,
            prefix: new HarmonyMethod(typeof(VoteLobbyingArmyTests), prefix));
        _playerClan = Blank<Clan>(); _voterClan = Blank<Clan>();
        _player = Blank<Hero>(); _leader = _speaker = Blank<Hero>();
        _playerRealm = _realm = Blank<Kingdom>();
        _elections = new ElectiveSuccessionBehavior(); _lobbying = new ElectionLobbyingBehavior();
        _ballot = new ElectiveSuccessionRecord { Realm = _realm, MandateNumber = 1 };
        _inArmy = _mercenary = _prisoner = false; _canPromise = true; _commits = 0;
        bool Ready() => (bool)Call(_lobbying, "Ready");
        ElectionVoteBribeBarterable Bribe() => (ElectionVoteBribeBarterable)Activator.CreateInstance(
            typeof(ElectionVoteBribeBarterable), BindingFlags.Instance | BindingFlags.NonPublic, null,
            new object[] { _realm, _speaker, _player, _player, _until, 100, 0d }, null);
        bool Secure(ElectionVoteBribeBarterable bribe) => (bool)Call(bribe, "TrySecure", _player, _speaker);
        bool Available(string id) => Conditions.ContainsKey(id) && (Conditions[id] == null || (bool)Conditions[id].DynamicInvoke());
        try
        {
            dayTicks.SetValue(null, 1000L);
            _until = CampaignTime.Days(10);
            Patch(AccessTools.PropertyGetter(typeof(Clan), "PlayerClan"), nameof(PlayerClan));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "Kingdom"), nameof(Realm));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "Leader"), nameof(Leader));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "IsUnderMercenaryService"), nameof(Mercenary));
            Patch(AccessTools.PropertyGetter(typeof(Hero), "Clan"), nameof(HeroClan));
            Patch(AccessTools.PropertyGetter(typeof(Hero), "MainHero"), nameof(Player));
            Patch(AccessTools.PropertyGetter(typeof(Hero), "OneToOneConversationHero"), nameof(Speaker));
            Patch(AccessTools.PropertyGetter(typeof(Hero), "Name"), nameof(Name));
            Patch(AccessTools.PropertyGetter(typeof(Hero), "PartyBelongedTo"), nameof(Party));
            Patch(AccessTools.PropertyGetter(typeof(Hero), "IsAlive"), nameof(Yes));
            Patch(AccessTools.PropertyGetter(typeof(Hero), "IsPrisoner"), nameof(Prisoner));
            Patch(AccessTools.PropertyGetter(typeof(CampaignTime), "Now"), nameof(Now));
            Patch(AccessTools.Method(typeof(CourtAgendaBehavior).Assembly.GetType("BellumCivile.Behaviors.DeliberationDialogueHelper"),
                "IsConversationInArmy"), nameof(InArmy));
            foreach (var method in typeof(CampaignGameStarter).GetMethods().Where(m =>
                m.Name == "AddDialogLine" || m.Name == "AddPlayerLine"))
                Patch(method, nameof(Capture));
            Patch(AccessTools.PropertyGetter(typeof(ElectiveSuccessionBehavior), "Instance"), nameof(Elections));
            Patch(AccessTools.PropertyGetter(typeof(ElectionLobbyingBehavior), "Current"), nameof(Lobbying));
            Patch(AccessTools.Method(typeof(ElectiveSuccessionBehavior), "Get"), nameof(Ballot));
            Patch(AccessTools.Method(typeof(ElectiveSuccessionBehavior), "CanCommitPromise"), nameof(CanPromise));
            Patch(AccessTools.Method(typeof(ElectiveSuccessionBehavior), "TryCommitPromise"), nameof(Commit));
            Patch(AccessTools.Method(typeof(ElectionLobbyingBehavior), "BribeOpenness"), nameof(Openness));

            var council = new CouncilAppointmentDeliberationBehavior();
            var behaviors = new CampaignBehaviorBase[] { new FiefDeliberationBehavior(), new PolicyDeliberationBehavior(),
                new ExpulsionDeliberationBehavior(), council, _lobbying };
            Conditions.Clear();
            foreach (var behavior in behaviors)
            {
                var repair = AccessTools.Method(behavior.GetType(), "RunReliabilityRepair");
                if (repair != null) Patch(repair, nameof(Skip));
                Call(behavior, behavior == _lobbying ? "AddDialogues" : "OnSessionLaunched", Blank<CampaignGameStarter>());
            }
            check(!Conditions.Keys.Any(id => id.EndsWith("_in_army") || id == "el_army"),
                "Voting dialogue registration has no army refusal branches");
            Field(council, "_conversationVoter", _voterClan); Field(council, "_conversationNominee", _voterClan);
            Field(_lobbying, "_speaker", _speaker); Field(_lobbying, "_candidate", _player);
            Field(_lobbying, "_realm", _realm); Field(_lobbying, "_until", _until);
            Field(_lobbying, "_ballot", _ballot); Field(_lobbying, "_mandateNumber", 1);
            foreach (bool army in new[] { false, true })
            {
                _inArmy = army;
                foreach (string id in new[] { "fief_deliberation_choose_grant", "policy_deliberation_choose_motion",
                    "expulsion_deliberation_choose_accusation", "council_appointment_stance_self" })
                    check(Available(id), id + " available; in army=" + army);
                check(Ready(), "Royal-election persuasion and bribery readiness; in army=" + army);
                var bribe = Bribe(); int before = _commits;
                check(Secure(bribe) && _commits == before + 1, "Royal-election bribe secures pledge; in army=" + army);
                check(!Secure(bribe) && _commits == before + 1, "Confirmed bribe cannot secure a second pledge");
            }

            _leader = _player;
            check(!Available("council_appointment_stance_self"), "Council voting still requires the clan leader");
            _leader = _speaker;
            _prisoner = true;
            check(!Ready() && !Secure(Bribe()), "Captivity still blocks royal-election negotiations");
            _prisoner = false; _mercenary = true;
            check(!Ready() && !Secure(Bribe()), "Mercenary player cannot pledge electoral votes");
            _mercenary = false; _playerRealm = Blank<Kingdom>();
            check(!Ready() && !Secure(Bribe()), "Foreign player cannot negotiate this realm's election");
            _playerRealm = _realm; _canPromise = false;
            check(!Ready() && !Secure(Bribe()), "Closed or ineligible electoral promise remains blocked");
            _canPromise = true;
            var stale = Bribe(); _ballot.MandateNumber++;
            check(!Ready() && !Secure(stale), "Mandate replacement invalidates ongoing negotiations and barter");
            _ballot.MandateNumber--;
            var expired = Bribe(); _until = default(CampaignTime);
            Field(_lobbying, "_until", _until);
            check(!Ready() && !Secure(expired), "Expired negotiation cannot finalize an old offer");
        }
        finally { harmony.UnpatchAll(harmony.Id); Conditions.Clear(); dayTicks.SetValue(null, oldTicks); }
    }
}
