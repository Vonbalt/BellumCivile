using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using BellumCivile;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Library;

internal static class MarriageMatchmakingTests
{
    private sealed class Manager : ICampaignBehaviorManager
    {
        private readonly List<CampaignBehaviorBase> _items = new List<CampaignBehaviorBase>();
        public T GetBehavior<T>() => _items.OfType<T>().FirstOrDefault();
        public IEnumerable<T> GetBehaviors<T>() => _items.OfType<T>();
        public void AddBehavior(CampaignBehaviorBase b) { _items.Add(b); }
        public void RemoveBehavior<T>() where T : CampaignBehaviorBase { }
        public void ClearBehaviors() { }
        public void InitializeCampaignBehaviors(IEnumerable<CampaignBehaviorBase> b) { }
        public void LoadBehaviorData() { }
        public void RegisterEvents() { }
    }
    private static int _day;
    private static int _rolls, _searches;
    private static bool _available, _refused;
    private static Hero _hero;
    private static Kingdom _realm, _parent, _otherParent, _shell, _otherShell;
    private static Clan _first, _second;
    private static FactionObject _firstFaction, _secondFaction;
    private static bool _rebels;
    private static bool _feud;
    private static bool FeudParent(ref Kingdom __result) { __result = _feud ? _parent : null; return false; }
    private static bool Day(ref int __result) { __result = _day; return false; }
    private static bool Year(ref int __result) { __result = 24; return false; }
    private static bool Available(ref bool __result) { __result = _available; return false; }
    private static bool Yes(ref bool __result) { __result = true; return false; }
    private static bool NoCourt(ref CourtAgendaBehavior __result) { __result = null; return false; }
    private static bool Roll(ref float __result) { _rolls++; __result = .5f; return false; }
    private static bool Chance(ref float __result) { __result = 1; return false; }
    private static bool Need(ref float __result) { __result = 99; return false; }
    private static bool Label(ref string __result) { __result = "test marriage hero"; return false; }
    private static bool Skip() => false;
    private static bool House(ref Clan __result) { __result = _first; return false; }
    private static bool Heroes(ref MBReadOnlyList<Hero> __result)
    { __result = new MBReadOnlyList<Hero>(new List<Hero> { _hero }); return false; }
    private static bool Search(object stats)
    {
        _searches++;
        AccessTools.Field(stats.GetType(), "TemporaryParticipants").SetValue(stats, _refused ? 0 : 1);
        AccessTools.Field(stats.GetType(), "LegalPairs").SetValue(stats, _refused ? 1 : 0);
        return false;
    }
    private static bool Player(ref Clan __result) { __result = null; return false; }
    private static bool Realm(Clan __instance, ref Kingdom __result)
    { __result = _rebels ? (__instance == _first ? _shell : _otherShell) : _realm; return false; }
    private static bool Membership(Clan clan, ref FactionObject activeFaction, ref Kingdom rebelKingdom, ref bool __result)
    {
        activeFaction = clan == _first ? _firstFaction : _secondFaction;
        rebelKingdom = clan == _first ? _shell : _otherShell;
        __result = _rebels; return false;
    }

    internal static void Run(Action<bool, string> check)
    {
        T Blank<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
        var assembly = typeof(StrategicMarriageBehavior).Assembly;
        var rules = assembly.GetType("BellumCivile.Behaviors.MarriageMatchmakingRules");
        var departure = AccessTools.Method(rules, "DepartureCost");
        var window = AccessTools.Method(rules, "InWindow");
        float Cost(bool leaves, bool fertile, int remaining, bool royal, float risk = 1) =>
            (float)departure.Invoke(null, new object[] { leaves, fertile, remaining, royal, risk });
        foreach (bool fertile in new[] { false, true })
        foreach (bool royal in new[] { false, true })
        for (int remaining = 0; remaining < 8; remaining++)
        {
            check(Cost(false, fertile, remaining, royal) == 0, "A receiving house never pays a departure or reserve penalty");
            check(Cost(true, fertile, remaining, royal) >= 0 && Cost(true, fertile, remaining, royal) <= 45,
                "Departure score remains bounded; household retention and survival eligibility are checked separately");
            if (remaining > 0) check(Cost(true, fertile, remaining, royal) <= Cost(true, fertile, remaining - 1, royal),
                "Additional household prospects never increase departure reluctance");
        }
        check(Cost(true, false, 0, false) == 25 && Cost(true, false, 1, false) == 12.5f && Cost(true, false, 2, false) == 0,
            "Last prospect, one remaining prospect and genuine spares have different household costs");
        check(Cost(true, false, 0, true) == 45 && Cost(true, false, 1, true) == 22.5f,
            "Royal reserve cost is directional and not stacked with household cost");
        check(Cost(true, true, 0, false) == 0, "An existing reproductive household avoids an invented extinction cost");
        check(Cost(true, true, 0, true, 0) == 0 && Cost(true, true, 0, true, .5f) == 22.5f,
            "Royal reserve concern follows the house's actual survival risk");
        foreach (int days in new[] { 12, 24, 84, 365 })
        for (int offset = 0; offset < days; offset++)
        for (int day = 0; day < days; day++)
        {
            bool actual = (bool)window.Invoke(null, new object[] { days * 3 + day, days, offset });
            check(actual == (day >= offset && day <= Math.Min(days - 1, offset + Math.Min(7, Math.Max(3, days / 4)))),
                "Availability window is distributed, bounded and cannot leak across annual boundary");
        }
        // Same realm/culture, equal ages/tier, need99, no relationship or claim benefits.
        float bloodline = 75 + 99 * .25f;
        check(bloodline - Cost(true, false, 2, false) >= 95 && bloodline - Cost(true, false, 0, false) < 95,
            "Spare-relative marriage is acceptable without treating last-relative departure as equally harmless");
        check(bloodline - Cost(true, false, 0, true) + 45 >= 95,
            "A sufficiently valuable strategic benefit can outweigh even the largest royal reserve concern");

        var type = typeof(StrategicMarriageBehavior);
        var helper = assembly.GetType("BellumCivile.Behaviors.BellumMarriageStrategyHelper");
        var h = new Harmony("bellum.test.marriage_matchmaking");
        void Patch(MethodBase method, string prefix) => h.Patch(method, prefix: new HarmonyMethod(typeof(MarriageMatchmakingTests), prefix));
        _realm = Blank<Kingdom>(); _parent = Blank<Kingdom>(); _otherParent = Blank<Kingdom>();
        _shell = Blank<Kingdom>(); _otherShell = Blank<Kingdom>();
        _first = Blank<Clan>(); _second = Blank<Clan>(); _first.StringId = "marriage_window_house";
        _firstFaction = Blank<FactionObject>();
        _secondFaction = Blank<FactionObject>();
        AccessTools.Field(typeof(FactionObject), "_parentKingdom").SetValue(_firstFaction, _parent);
        AccessTools.Field(typeof(FactionObject), "_parentKingdom").SetValue(_secondFaction, _otherParent);
        var scheduler = new StrategicMarriageBehavior();
        var previousCampaign = Campaign.Current;
        Dictionary<string, int> Dict(string name) => (Dictionary<string, int>)AccessTools.Field(type, name).GetValue(scheduler);
        bool Due() => (bool)AccessTools.Method(type, "ShouldEvaluateToday").Invoke(scheduler, new object[] { _first });
        try
        {
            _rebels = false;
            Patch(AccessTools.PropertyGetter(type, "CurrentDay"), nameof(Day));
            Patch(AccessTools.Method(type, "GetCampaignDaysInYear"), nameof(Year));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "PlayerClan"), nameof(Player));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "Kingdom"), nameof(Realm));
            Patch(AccessTools.Method(typeof(FactionManagerBehavior), "IsClanOnActiveCivilWarRebelSide"), nameof(Membership));
            int offset = (int)AccessTools.Method(assembly.GetType("BellumCivile.Behaviors.MarriageOutcome"), "AnnualOffset")
                .Invoke(null, new object[] { _first.StringId, 24 });
            _day = 240 + offset;
            check(Due(), "House can use its original annual slot");
            _day++;
            check(Due(), "An unavailable house can use its annual slot on a later day in the window");
            Dict("_lastHouseEvaluationYear")[_first.StringId] = 10;
            check(!Due(), "Existing old-save annual receipt without pending search cannot be rerolled");
            Dict("_pendingMarriageYear")[_first.StringId] = 10;
            Dict("_marriageSearchCount")[_first.StringId] = 1;
            Dict("_nextMarriageSearchDay")[_first.StringId] = _day + 1;
            check(!Due(), "Pending accepted annual roll respects its retry interval");
            _day++;
            check(Due(), "Pending search resumes without reopening annual roll");
            Dict("_marriageSearchCount")[_first.StringId] = 3;
            check(!Due(), "At most three searches are permitted per successful annual roll");
            AccessTools.Method(type, "CloseMarriageSearch").Invoke(scheduler, new object[] { _first });
            check(!Due() && Dict("_pendingMarriageYear").Count == 0,
                "Closing a refused or completed opportunity retains the annual receipt");
            _rebels = true;
            var factions = Blank<FactionManagerBehavior>();
            var opposing = AccessTools.Method(helper, "AreClansOnOppositeActiveCivilWarSides");
            check(!(bool)opposing.Invoke(null, new object[] { _first, _second, factions }),
                "Separate rebellions in unrelated parent realms do not invent hostility between houses");
            AccessTools.Field(typeof(FactionObject), "_parentKingdom").SetValue(_secondFaction, _parent);
            check((bool)opposing.Invoke(null, new object[] { _first, _second, factions }),
                "Rival sides in one three-way civil war remain excluded from matchmaking");

            _rebels = false;
            var manager = new Manager(); manager.AddBehavior(factions); manager.AddBehavior(Blank<ClaimFeudWarBehavior>());
            var campaign = Blank<Campaign>(); campaign.AddCampaignBehaviorManager(manager);
            AccessTools.PropertySetter(typeof(Campaign), "Current").Invoke(null, new object[] { campaign });
            Patch(AccessTools.Method(typeof(ClaimFeudWarBehavior), "GetParentKingdomForTemporaryRealm"), nameof(FeudParent));
            var politicalRealm = AccessTools.Method(helper, "MarriagePoliticalRealm");
            _feud = false;
            check(politicalRealm.Invoke(null, new object[] { _first }) == _realm, "An independent or ordinary house retains its real political realm");
            _feud = true;
            check(politicalRealm.Invoke(null, new object[] { _first }) == _parent, "Private-feud shell resolves to its parent solely for marriage political scoring");
            _feud = false; _rebels = true;
            check(politicalRealm.Invoke(null, new object[] { _first }) == _parent, "Active civil-war house uses its parent for political-home scoring");
            _rebels = false;
            check(politicalRealm.Invoke(null, new object[] { _first }) == _realm, "Ended rebellion does not permanently attach an independent house to an old realm");
            _hero = Blank<Hero>();
            Patch(AccessTools.PropertyGetter(typeof(Clan), "Heroes"), nameof(Heroes));
            Patch(AccessTools.PropertyGetter(typeof(Hero), "Clan"), nameof(House));
            Patch(AccessTools.PropertyGetter(typeof(CourtAgendaBehavior), "Current"), nameof(NoCourt));
            Patch(AccessTools.PropertyGetter(assembly.GetType("BellumCivile.BellumCivileOptions"), "EnableBellumStrategicMarriageLogic"), nameof(Yes));
            Patch(AccessTools.Method(type, "FlushSummaryIfYearChanged"), nameof(Skip));
            Patch(AccessTools.Method(type, "HeroLabel"), nameof(Label));
            Patch(AccessTools.Method(type, "TraceMarriage"), nameof(Skip));
            Patch(AccessTools.Method(helper, "IsStrategicMarriageInitiator"), nameof(Available));
            Patch(AccessTools.Method(helper, "CalculateAnnualMarriageChance"), nameof(Chance));
            Patch(AccessTools.Method(helper, "CalculateDynasticNeed"), nameof(Need));
            Patch(AccessTools.Method(helper, "FindBestHouseMatch"), nameof(Search));
            Patch(AccessTools.PropertyGetter(typeof(MBRandom), "RandomFloat"), nameof(Roll));
            for (int i = 0; offset > 10; i++)
            {
                _first.StringId = "marriage_defer_" + i;
                offset = (int)AccessTools.Method(assembly.GetType("BellumCivile.Behaviors.MarriageOutcome"), "AnnualOffset")
                    .Invoke(null, new object[] { _first.StringId, 24 });
            }
            scheduler = new StrategicMarriageBehavior(); _day = 240 + offset;
            _available = false; _refused = false; _rolls = _searches = 0;
            void Tick() => AccessTools.Method(type, "OnDailyTickClan").Invoke(scheduler, new object[] { _first });
            Tick();
            check(_rolls == 0 && _searches == 0, "Temporary absence does not consume or roll the annual opportunity");
            _available = true; _day++; Tick();
            check(_rolls == 1 && _searches == 1, "House recovering inside its window gets one ordinary annual roll");
            _day++; Tick();
            check(_rolls == 1 && _searches == 1, "Daily ticks do not bypass the three-day search retry interval");
            _day += 2; Tick();
            check(_rolls == 1 && _searches == 2, "Temporary-blocker search retries reuse the successful annual roll");
            // Rehydrate the exact saved dictionaries, rather than trusting transient runtime state.
            var restored = new StrategicMarriageBehavior();
            foreach (var name in new[] { "_lastHouseEvaluationYear", "_pendingMarriageYear", "_nextMarriageSearchDay", "_marriageSearchCount" })
                AccessTools.Field(type, name).SetValue(restored, new Dictionary<string, int>(Dict(name)));
            scheduler = restored; Tick();
            check(_rolls == 1 && _searches == 2, "Restored retry receipts cannot reroll or repeat a search on load");
            scheduler = new StrategicMarriageBehavior(); _day = 240 + offset; _refused = true; _rolls = _searches = 0;
            Tick(); _day += 3; Tick();
            check(_rolls == 1 && _searches == 1, "A genuine scored refusal closes the ordinary opportunity instead of soliciting daily consent");
        }
        finally
        {
            h.UnpatchAll(h.Id);
            AccessTools.PropertySetter(typeof(Campaign), "Current").Invoke(null, new object[] { previousCampaign });
        }
    }
}
