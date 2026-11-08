using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using BellumCivile;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;

internal static class MarriageRevisionTests
{
    private static readonly Assembly Mod = typeof(BellumMarriageModel).Assembly;
    private static readonly Type Membership = Mod.GetType("BellumCivile.HouseholdMembership");
    private static readonly Type Helper = Mod.GetType("BellumCivile.Behaviors.BellumMarriageStrategyHelper");
    private static readonly List<Hero> Roster = new List<Hero>();
    private static readonly Dictionary<Hero, float> Ages = new Dictionary<Hero, float>();
    private static readonly Dictionary<Hero, PersonalLordshipRecord> Grants = new Dictionary<Hero, PersonalLordshipRecord>();
    private static readonly HashSet<Hero> Family = new HashSet<Hero>();
    private static Clan _a, _b;
    private static LordshipBehavior _registry;
    private static int _vacancies, _reads, _vacancyReads;
    private static T Blank<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    private static bool No(ref bool __result) { __result = false; return false; }
    private static bool Yes(ref bool __result) { __result = true; return false; }
    private static bool Skip() => false;
    private static bool Members(ref IEnumerable<Hero> __result) { _reads++; __result = Roster; return false; }
    private static bool Age(Hero __instance, ref float __result) { __result = Ages[__instance]; return false; }
    private static bool Child(Hero __instance, ref bool __result) { __result = Ages[__instance] < 18; return false; }
    private static bool Prospect(Hero hero, ref bool __result) { __result = hero.Spouse == null; return false; }
    private static bool Registry(ref LordshipBehavior __result) { __result = _registry; return false; }
    private static bool Vacancies(ref int __result) { _vacancyReads++; __result = _vacancies; return false; }
    private static bool Grant(Hero hero, ref PersonalLordshipRecord __result) { Grants.TryGetValue(hero, out __result); return false; }
    private static bool NoRegent(ref RegencyBehavior __result) { __result = null; return false; }
    private static bool NoPlayer(ref Clan __result) { __result = null; return false; }
    private static bool IsFamily(Hero hero, ref bool __result) { __result = Family.Contains(hero); return false; }
    private static bool FindClan(string clanId, ref Clan __result) { __result = clanId == _a.StringId ? _a : clanId == _b.StringId ? _b : null; return false; }
    private static bool FindHero(string heroId, ref Hero __result) { __result = Roster.FirstOrDefault(h => h.StringId == heroId); return false; }
    private static bool Living(ref MBReadOnlyList<Hero> __result) { __result = new MBReadOnlyList<Hero>(Roster.Where(h => h.IsAlive).ToList()); return false; }

    internal static void Run(Action<bool, string> check)
    {
        Rules(check);
        Snapshots(check);
        Alliances(check);
    }

    private static void Rules(Action<bool, string> check)
    {
        var rules = Mod.GetType("BellumCivile.Behaviors.MarriageHealthRules");
        float F(string name, params object[] args) => (float)AccessTools.Method(rules, name).Invoke(null, args);
        int Capacity(int free, int endowed, int waiting, int spare) => (int)AccessTools.Method(rules, "ServiceCapacity")
            .Invoke(null, new object[] { free, endowed, waiting, spare });
        for (int i = 0; i <= 100; i++)
        {
            float risk = i / 100f;
            check(Math.Abs(F("MixedStatusCost", true, false, risk) - 25 * (1 - risk)) < .001f,
                "Mixed-status reluctance decreases with the noble side's dynasty risk");
            check(F("MixedStatusCost", false, true, risk) == 0 && F("MixedStatusCost", false, false, risk) == 0,
                "Retainers do not inherit their employers' noble status reluctance");
            float chance = F("HouseholdChance", risk);
            check(chance >= .20f && chance <= .50f, "Household opportunity remains a bounded chance, never a guaranteed wedding");
        }
        check(F("HouseholdMarriageValue", false) == 25 && F("HouseholdMarriageValue", true) == 20,
            "Household ordinary value favors a same-status marriage without forbidding mixed pairs");
        for (int waiting = 0; waiting < 8; waiting++)
        for (int spare = 0; spare < 8; spare++)
            check(Capacity(2, 1, waiting, spare) == 3 + Math.Min(waiting, spare),
                "Only retainers supportable by actual endowments count toward potential appointments");
        check(F("HouseholdNeed", 1f, 0, 0f, false) == 0 && F("HouseholdNeed", 1f, 0, 0f, true) == 1,
            "No vacant endowment prevents an invented staffing-urgency bonus");
        check(F("HouseholdNeed", 1f, 1, 1f, true) < F("HouseholdNeed", 1f, 0, 0f, true),
            "Existing children and reproductive households reduce replacement urgency");
        check(F("StaffDepartureCost", 3, 3, 4) == 0 && F("StaffDepartureCost", 3, 2, 4) == 10,
            "A replaceable endowment holder is not charged as a lost heir or irreplaceable worker");
        var pricing = Mod.GetType("BellumCivile.PlayerMarriagePricing");
        int Quote(int native, float risk, bool leaves) => (int)AccessTools.Method(pricing, "CalculateHousehold")
            .Invoke(null, new object[] { native, risk, leaves });
        check(Quote(-10000, 0, true) == 10000 && Quote(-10000, 1, true) == 15000 && Quote(-10000, 1, false) == 7500,
            "Retainer negotiation uses bounded staffing cost rather than a borrowed dynasty premium");
        check(Quote(int.MinValue, 1, true) == int.MaxValue && Quote(500, 1, true) == 0,
            "Household quotes preserve saturation and cannot become negative");
        int Status(int amount, bool playerFamily, bool otherFamily) => (int)AccessTools.Method(pricing, "AdjustStatus")
            .Invoke(null, new object[] { amount, playerFamily, otherFamily });
        check(Status(10000, false, true) == 12500 && Status(0, false, true) == 2500
            && Status(1000, false, true) == 3500,
            "Offering a retainer to a noble family adds 25 percent, with a minimum additional 2500 denars");
        check(Status(10000, true, false) == 7500 && Status(0, true, false) == 0,
            "Offering family to a retainer discounts compensation by 25 percent without creating a reverse payment");
        foreach (int amount in new[] { 0, 1, 9999, 10000, 10001, int.MaxValue })
        {
            check(Status(amount, true, true) == amount && Status(amount, false, false) == amount,
                "Same-status negotiations retain their original quote: " + amount);
            check(Status(amount, false, true) >= amount && Status(amount, true, false) <= amount,
                "Status adjustments are monotonic and remain inside integer bounds: " + amount);
        }
        check(Status(int.MaxValue, false, true) == int.MaxValue && Status(10001, true, false) == 7501,
            "Status pricing saturates safely and rounds fractional denars upward");
    }

    private static void Snapshots(Action<bool, string> check)
    {
        var h = new Harmony("bellum.test.marriage_revision_health");
        void Patch(MethodBase target, string method) => h.Patch(target, prefix: new HarmonyMethod(typeof(MarriageRevisionTests), method));
        _a = Blank<Clan>(); _a.StringId = "revision_house";
        Roster.Clear(); Ages.Clear(); Grants.Clear(); _registry = new LordshipBehavior();
        _reads = _vacancyReads = _vacancies = 0;
        Hero Add(string id, bool female = false, float age = 30)
        {
            var hero = Blank<Hero>(); hero.StringId = id; hero.IsFemale = female;
            AccessTools.Field(typeof(Hero), "_clan").SetValue(hero, _a);
            AccessTools.Field(typeof(Hero), "_heroState").SetValue(hero, Hero.CharacterStates.Active);
            AccessTools.Field(typeof(Hero), "_children").SetValue(hero, new MBList<Hero>());
            AccessTools.Field(typeof(Hero), "_exSpouses").SetValue(hero, new MBList<Hero>());
            AccessTools.PropertySetter(typeof(Hero), "Occupation").Invoke(hero, new object[] { Occupation.Lord });
            Roster.Add(hero); Ages[hero] = age; return hero;
        }
        var head = Add("head", age: 60);
        AccessTools.Field(typeof(Clan), "_leader").SetValue(_a, head);
        AccessTools.Field(typeof(Clan), "_heroesCache").SetValue(_a, new MBList<Hero> { head });
        AccessTools.Field(typeof(Clan), "_companionsCache").SetValue(_a, new MBList<Hero>());
        var worker = Add("worker"); var wife = Add("worker_wife", true);
        worker.Spouse = wife;
        object Snapshot() => AccessTools.Method(Helper, "AssessHouseHealth").Invoke(null, new object[] { _a });
        float Value(object snapshot, string name) => Convert.ToSingle(AccessTools.Field(snapshot.GetType(), name).GetValue(snapshot));
        try
        {
            Patch(AccessTools.Method(Membership, "Members"), nameof(Members));
            Patch(AccessTools.PropertyGetter(typeof(RegencyBehavior), "Instance"), nameof(NoRegent));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "PlayerClan"), nameof(NoPlayer));
            Patch(AccessTools.PropertyGetter(typeof(Hero), "Age"), nameof(Age));
            Patch(AccessTools.PropertyGetter(typeof(Hero), "IsChild"), nameof(Child));
            Patch(AccessTools.Method(Helper, "HouseholdMarriageProspect"), nameof(Prospect));
            Patch(AccessTools.PropertyGetter(typeof(LordshipBehavior), "Instance"), nameof(Registry));
            Patch(AccessTools.PropertyGetter(typeof(LordshipBehavior), "Ready"), nameof(Yes));
            Patch(AccessTools.Method(typeof(LordshipBehavior), "CountVacancies"), nameof(Vacancies));
            Patch(AccessTools.Method(typeof(LordshipBehavior), "GetGrant", new[] { typeof(Hero) }), nameof(Grant));
            var initial = Snapshot();
            check(Value(initial, "Risk") == 1 && Value(initial, "BloodAdults") == 1
                && !(bool)AccessTools.Field(initial.GetType(), "HasFertileCouple").GetValue(initial),
                "An unrelated retainer couple cannot disguise an endangered dynasty");
            check(Value(initial, "ServiceAdults") == 1 && Value(initial, "ManpowerRisk") == .5f,
                "Unendowed retainers do not pretend they can already fill restricted appointments");
            _vacancies = 2;
            var endowedCapacity = Snapshot();
            check(Value(endowedCapacity, "Risk") == 1 && Value(endowedCapacity, "ManpowerRisk") == 0,
                "Workforce can be healthy while dynasty remains endangered");
            check(_reads == 3 && _vacancyReads == 2 && Grants.Count == 0,
                "Snapshots reuse family classification after one cold-cache fill and never grant an estate");
            worker.Father = head;
            AccessTools.Method(Membership, "Invalidate").Invoke(null, new object[] { null });
            var bloodline = Snapshot();
            check(Value(bloodline, "Risk") == 0 && Value(bloodline, "BloodAdults") == 2,
                "A reproductive couple with a genuine blood parent covers dynasty survival");
            var child = Add("child", age: 10); child.Father = worker;
            var withChild = Snapshot();
            check(Value(withChild, "Children") == 1 && Value(withChild, "ServiceAdults") == 3,
                "Blood children improve continuity but cannot fill adult service positions");
            worker.Father = null;
            AccessTools.Method(Membership, "Invalidate").Invoke(null, new object[] { null });
            _vacancies = 0;
            Grants[worker] = new PersonalLordshipRecord { Holder = worker, GrantingClan = _a };
            var supported = Snapshot();
            float Departure(Hero hero) => (float)AccessTools.Method(supported.GetType(), "StaffDepartureCost").Invoke(supported, new object[] { hero });
            check(Departure(worker) == 0 && Value(supported, "Children") == 0,
                "A leaving retainer frees an endowment for a replacement but creates no employer dynasty claim");
            Grants.Clear(); _vacancies = 0;
            head.Spouse = wife; wife.Spouse = head;
            AccessTools.Method(Membership, "Invalidate").Invoke(null, new object[] { null });
            var family = Snapshot();
            AccessTools.Field(typeof(Hero), "_heroState").SetValue(head, Hero.CharacterStates.Prisoner);
            check(Value(Snapshot(), "Risk") == Value(family, "Risk"),
                "Captivity alone does not erase a living reproductive dynasty");
            AccessTools.Field(typeof(Clan), "_fiefsCache").SetValue(_a, new MBList<Town> { Blank<Town>() });
            var understaffed = Snapshot();
            var incoming = Add("outside_groom");
            AccessTools.Field(typeof(Hero), "_clan").SetValue(incoming, Blank<Clan>());
            float Incoming(Hero member) => (float)AccessTools.Method(understaffed.GetType(), "StaffIncomingValue")
                .Invoke(understaffed, new object[] { member, incoming });
            check(Incoming(head) > 0 && Incoming(wife) == 0,
                "A blood relative's spouse is family, but a married-in relative's next spouse still needs an endowment");
        }
        finally
        {
            h.UnpatchAll(h.Id);
            AccessTools.Method(Membership, "Invalidate").Invoke(null, new object[] { null });
        }
    }

    private static void Alliances(Action<bool, string> check)
    {
        var h = new Harmony("bellum.test.marriage_revision_alliances");
        void Patch(MethodBase target, string method) => h.Patch(target, prefix: new HarmonyMethod(typeof(MarriageRevisionTests), method));
        var previous = MarriageAllianceBehavior.Instance;
        var behavior = new MarriageAllianceBehavior();
        var type = typeof(MarriageAllianceBehavior);
        var records = (List<string>)AccessTools.Field(type, "_marriageAlliancePairs").GetValue(behavior);
        _a = Blank<Clan>(); _a.StringId = "alliance_a";
        _b = Blank<Clan>(); _b.StringId = "alliance_b";
        Roster.Clear(); Family.Clear();
        var first = Blank<Hero>(); first.StringId = "first";
        var second = Blank<Hero>(); second.StringId = "second";
        AccessTools.Field(typeof(Hero), "_clan").SetValue(first, _a);
        AccessTools.Field(typeof(Hero), "_clan").SetValue(second, _b);
        AccessTools.Field(typeof(Clan), "_aliveLordsCache").SetValue(_a, new MBList<Hero> { first });
        AccessTools.Field(typeof(Clan), "_aliveLordsCache").SetValue(_b, new MBList<Hero> { second });
        var father = Blank<Hero>();
        AccessTools.Field(typeof(Hero), "_children").SetValue(father, new MBList<Hero>());
        AccessTools.Field(typeof(Hero), "_clan").SetValue(father, _b);
        second.Father = father;
        Roster.Add(first); Roster.Add(second);
        void Observe() => AccessTools.Method(type, "OnBeforeHeroesMarried").Invoke(behavior, new object[] { first, second, false });
        void Dirty() => AccessTools.Field(type, "_activeAllianceCacheDirty").SetValue(behavior, true);
        try
        {
            Patch(AccessTools.Method(Membership, "IsFamily"), nameof(IsFamily));
            Patch(AccessTools.Method(Membership, "Supports"), nameof(Yes));
            Patch(AccessTools.PropertyGetter(typeof(Hero), "IsAlive"), nameof(Yes));
            Patch(AccessTools.PropertyGetter(typeof(Hero), "AllAliveHeroes"), nameof(Living));
            Patch(AccessTools.Method(type, "CanResolveCampaignObjects"), nameof(Yes));
            Patch(AccessTools.Method(type, "FindClan"), nameof(FindClan));
            Patch(AccessTools.Method(type, "FindHero"), nameof(FindHero));
            Patch(AccessTools.Method(type, "TraceMarriage"), nameof(Skip));
            Patch(AccessTools.Method(typeof(BellumCivileLogger), "Log"), nameof(Skip));
            Observe(); Family.Add(first); Observe();
            check(records.Count == 1 && records[0].StartsWith("v3h|") && !behavior.AreClansIntermarried(_a, _b),
                "Retainer/Retainer and mixed weddings record exclusion without a political employer alliance");
            records = (List<string>)AccessTools.Field(type, "_marriageAlliancePairs").GetValue(behavior);
            Family.Add(second); Observe(); Observe();
            check(records.Count == 2 && records.Count(r => r.StartsWith("v3|")) == 1,
                "Family/Family standing is recorded once before wedding clan transfer");
            AccessTools.Field(typeof(Hero), "_clan").SetValue(second, _a);
            Family.Clear();
            check(behavior.AreClansIntermarried(_a, _b),
                "A qualifying original-house tie survives household transfer or later family reclassification");
            records = (List<string>)AccessTools.Field(type, "_marriageAlliancePairs").GetValue(behavior);
            records.Clear(); records.Add("v2|alliance_a|alliance_b|first|second"); Dirty();
            check(behavior.AreClansIntermarried(_a, _b), "Ambiguous legacy v2 alliances retain their normal lifetime");
            AccessTools.Field(typeof(Hero), "_clan").SetValue(second, _b);
            first.Spouse = second;
            records = (List<string>)AccessTools.Field(type, "_marriageAlliancePairs").GetValue(behavior);
            records.Clear(); Dirty();
            check(!behavior.AreClansIntermarried(_a, _b), "Separate-clan retainer spouses cannot resurrect an excluded employer tie");
            var recognition = Mod.GetType("BellumCivile.Patches.CourtDynasticMarriageRecognitionPatch");
            var args = new object[] { _a, _b, true };
            AccessTools.Method(recognition, "Postfix").Invoke(null, args);
            check((bool)args[2], "Unclassified historical native marriage evidence is preserved rather than guessed away");
            Observe();
            args[2] = true;
            AccessTools.Method(recognition, "Postfix").Invoke(null, args);
            check(!(bool)args[2], "Native positive parent-clan recognition cannot bypass excluded retainer alliances");
            check(h.CreateClassProcessor(recognition).Patch().Count > 0,
                "Native marriage-alliance recognition patch binds to this game's actual API");
            var native = AccessTools.Method(typeof(DefaultAllianceModel), "IsThereMarriageBetweenClans");
            check(!(bool)native.Invoke(new DefaultAllianceModel(), new object[] { _a, _b }),
                "Actual native father-clan lookup rejects the recorded non-dynastic wedding");
            Family.Add(first); Family.Add(second); Dirty();
            check(!behavior.AreClansIntermarried(_a, _b), "Later family standing does not rewrite an explicitly non-dynastic wedding");
            records = (List<string>)AccessTools.Field(type, "_marriageAlliancePairs").GetValue(behavior);
            records.Clear(); Dirty();
            check(behavior.AreClansIntermarried(_a, _b), "Unrecorded separate-house family marriages retain the qualifying live fallback");
            records = (List<string>)AccessTools.Field(type, "_marriageAlliancePairs").GetValue(behavior);
            records.Add("v2|alliance_a|alliance_b|missing|second"); Dirty();
            behavior.AreClansIntermarried(_a, _b);
            check(records.Count == 1 && ((List<string>)AccessTools.Field(type, "_marriageAlliancePairs").GetValue(behavior)).Count == 0,
                "Unresolvable spouses still expire through ordinary legacy-record validation");
        }
        finally
        {
            h.UnpatchAll(h.Id);
            AccessTools.Field(type, "<Instance>k__BackingField").SetValue(null, previous);
        }
    }
}
