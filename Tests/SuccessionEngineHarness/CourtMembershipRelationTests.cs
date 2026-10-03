using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;
using BellumCivile;
using BellumCivile.Behaviors;
using BellumCivile.UI;
using HarmonyLib;
using TaleWorlds.CampaignSystem;

internal static class CourtMembershipRelationTests
{
    private static Clan _first, _second;
    private static Kingdom _home, _foreign;
    private static FactionObject _firstFaction, _secondFaction;
    private static FactionManagerBehavior _manager;
    private static bool _differentRealm;
    private static int _relationEvents;
    private static bool Manager(ref FactionManagerBehavior __result) { __result = _manager; return false; }
    private static bool Realm(Clan __instance, ref Kingdom __result)
    { __result = __instance == _second && _differentRealm ? _foreign : _home; return false; }
    private static bool Faction(Clan clan, ref FactionObject __result)
    { __result = clan == _first ? _firstFaction : _secondFaction; return false; }
    private static bool Relation() { _relationEvents++; return false; }
    private static T Blank<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));

    internal static void Run(Action<bool, string> check)
    {
        var h = new Harmony("bellum.test.court_membership_relations");
        void Patch(MethodBase m, string name) => h.Patch(m, prefix: new HarmonyMethod(typeof(CourtMembershipRelationTests), name));
        FactionObject Court(FactionType type)
        {
            var faction = Blank<FactionObject>();
            AccessTools.Field(typeof(FactionObject), "_type").SetValue(faction, type);
            return faction;
        }
        _first = Blank<Clan>(); _second = Blank<Clan>();
        _home = Blank<Kingdom>(); _foreign = Blank<Kingdom>(); _manager = Blank<FactionManagerBehavior>();
        _differentRealm = false; _relationEvents = 0;
        var helper = typeof(FactionObject).Assembly.GetType("BellumCivile.DynamicRelationBaselineHelper", true);
        var scoreMethod = AccessTools.Method(helper, "AddCourtFactionScore");
        int Score()
        {
            object[] args = { 0, new List<string>(), _first, _second };
            scoreMethod.Invoke(null, args);
            return (int)args[0];
        }
        var vm = Blank<FactionsWindowVM>();
        var join = AccessTools.Method(typeof(FactionsWindowVM), "ApplyJoinRelations");
        var leave = AccessTools.Method(typeof(FactionsWindowVM), "ApplyLeaveRelations");
        try
        {
            Patch(AccessTools.PropertyGetter(typeof(FactionManagerBehavior), "Instance"), nameof(Manager));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "Kingdom"), nameof(Realm));
            Patch(AccessTools.Method(typeof(FactionManagerBehavior), "GetIdeologicalFaction"), nameof(Faction));
            Patch(AccessTools.Method(typeof(RelationMemoryService), "ApplyChange"), nameof(Relation));
            var courts = new[] { Court(FactionType.Nobility), Court(FactionType.Glory), Court(FactionType.Liberty) };
            foreach (var a in courts)
            foreach (var b in courts)
            {
                _firstFaction = a; _secondFaction = b;
                check(Score() == (a == b ? 10 : -10), "Court affiliation is +10 together / -10 apart: " + a.Type + "/" + b.Type);
            }
            _firstFaction = null; _secondFaction = courts[0];
            check(Score() == 0, "Unaffiliated character receives no court modifier");
            _firstFaction = courts[0]; _secondFaction = null;
            check(Score() == 0, "Unaffiliated counterpart receives no court modifier");
            _firstFaction = _secondFaction = courts[0]; _differentRealm = true;
            check(Score() == 0, "Matching court faction types across realms give no affiliation bonus");
            _differentRealm = false;
            foreach (var faction in courts)
            {
                _secondFaction = faction;
                for (int i = 0; i < 10; i++)
                {
                    _firstFaction = faction;
                    join.Invoke(vm, new object[] { faction });
                    check(Score() == 10, "Joining restores a single conditional bonus without stacking");
                    leave.Invoke(vm, new object[] { faction });
                    _firstFaction = null;
                    check(Score() == 0, "Leaving removes affiliation without residual relation gains");
                }
            }
            check(_relationEvents == 0, "Court join/leave never calls relation events, so Charm cannot amplify membership changes");
            check(AccessTools.Method(typeof(IdeologyPolicyRoster), "AreRivals") == null, "Retired fixed court-rivalry API was removed");
        }
        finally
        {
            h.UnpatchAll(h.Id); _manager = null; _first = _second = null;
            _firstFaction = _secondFaction = null; _home = _foreign = null;
        }
    }
}
