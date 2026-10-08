using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Library;

// Historical balance comparison before the last-continuation veto; production eligibility has its own tests.
internal static class MarriageHeiressSimulation
{
    private sealed class House
    {
        internal float Risk, Politics, IncomingClaim;
        internal int Tier = 4, Spares = 2;
        internal float Age = 30;
        internal bool FertileCouple = true, CrownHeir, ClanHeir, Leader, Royal, LivingChild;
        internal bool Protected => CrownHeir || ClanHeir || Leader;
        internal House Copy() => (House)MemberwiseClone();
    }

    private sealed class Scenario
    {
        internal string Name;
        internal House Bride = new House(), Groom = new House();
        internal bool Domestic = true, SameCulture = true, Allied, Fertile = true, PlayerInvolved;
        internal float Relation;
        internal Scenario Copy(string name)
        {
            var copy = (Scenario)MemberwiseClone();
            copy.Name = name;
            copy.Bride = Bride.Copy();
            copy.Groom = Groom.Copy();
            return copy;
        }
    }

    private sealed class Result
    {
        internal float Bride, Groom;
        internal bool Matrilineal, Allowed;
        internal string Blocker;
        internal bool Accepted(float floor) => Allowed && Bride >= floor && Groom >= floor;
        internal string Display(float floor) => $"{Bride:0.##}/{Groom:0.##} ({(Accepted(floor) ? "yes" : Blocker ?? "score refusal")})";
    }

    private static Scenario _scenario;
    private static Hero _bride, _groom, _child;
    private static Clan _brideHouse, _groomHouse, _otherRuler;
    private static Kingdom _home, _foreign;
    private static CultureObject _culture, _otherCulture;
    private static Type _contextType, _healthType, _outcomeType;
    private static MethodInfo _evaluate;
    private static float _floor;

    private static T Blank<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    private static House Profile(Clan house) => house == _brideHouse ? _scenario.Bride : _scenario.Groom;
    private static bool ClanOf(Hero __instance, ref Clan __result)
    { __result = __instance == _bride ? _brideHouse : _groomHouse; return false; }
    private static bool Tier(Clan __instance, ref int __result)
    { __result = Profile(__instance).Tier; return false; }
    private static bool Age(Hero __instance, ref float __result)
    { __result = (__instance == _bride ? _scenario.Bride : _scenario.Groom).Age; return false; }
    private static bool Ruler(Kingdom __instance, ref Clan __result)
    {
        __result = __instance == _home && _scenario.Bride.Royal ? _brideHouse
            : (_scenario.Domestic || __instance == _foreign) && _scenario.Groom.Royal ? _groomHouse : _otherRuler;
        return false;
    }
    private static bool Relation(ref int __result) { __result = (int)_scenario.Relation; return false; }
    private static bool Personal(ref int __result) { __result = 0; return false; }
    private static bool Yes(ref bool __result) { __result = true; return false; }
    private static bool Allied(ref bool __result) { __result = _scenario.Allied; return false; }
    private static bool Court(ref CourtAgendaBehavior __result) { __result = null; return false; }
    private static bool Politics(Clan house, ref float __result) { __result = Profile(house).Politics; return false; }
    private static bool Claim(Clan house, ref float __result) { __result = Profile(house).IncomingClaim; return false; }

    private static void Field(object target, string name, object value) => AccessTools.Field(target.GetType(), name).SetValue(target, value);

    private static Result Score(Scenario scenario, bool prototype)
    {
        _scenario = scenario;
        _bride.Culture = _culture;
        _groom.Culture = scenario.SameCulture ? _culture : _otherCulture;
        Field(_bride, "_children", scenario.Bride.LivingChild ? new MBList<Hero> { _child } : new MBList<Hero>());
        Field(_groom, "_children", scenario.Groom.LivingChild ? new MBList<Hero> { _child } : new MBList<Hero>());
        bool matrilineal = scenario.Bride.Leader && !scenario.Groom.Leader;
        if (prototype && !scenario.PlayerInvolved && scenario.Bride.Protected) matrilineal = true;
        Clan destination = matrilineal ? _brideHouse : _groomHouse;
        var context = FormatterServices.GetUninitializedObject(_contextType);
        foreach (string name in new[] { "Realms", "_health", "CrownHeirs" })
        {
            var field = AccessTools.Field(_contextType, name);
            field.SetValue(context, Activator.CreateInstance(field.FieldType));
        }
        var realms = (IDictionary)AccessTools.Field(_contextType, "Realms").GetValue(context);
        realms[_brideHouse] = _home;
        realms[_groomHouse] = scenario.Domestic ? _home : _foreign;
        var health = (IDictionary)AccessTools.Field(_contextType, "_health").GetValue(context);
        var heirs = (HashSet<Hero>)AccessTools.Field(_contextType, "CrownHeirs").GetValue(context);
        foreach (var hero in new[] { _bride, _groom })
        {
            Clan house = hero == _bride ? _brideHouse : _groomHouse;
            House side = Profile(house);
            var snapshot = FormatterServices.GetUninitializedObject(_healthType);
            Field(snapshot, "Risk", side.Risk);
            Field(snapshot, "HasFertileCouple", side.FertileCouple);
            var prospects = new HashSet<Hero> { hero };
            for (int i = 0; i < side.Spares; i++) prospects.Add(Blank<Hero>());
            Field(snapshot, "Prospects", prospects);
            Field(snapshot, "BloodProspects", prospects);
            health[house] = snapshot;
            // Prototype separates retained-household Crown continuity from outgoing kinship value.
            if (side.CrownHeir && (!prototype || scenario.PlayerInvolved || destination == house)) heirs.Add(hero);
        }
        var outcome = FormatterServices.GetUninitializedObject(_outcomeType);
        Field(outcome, "<Destination>k__BackingField", destination);
        Field(outcome, "<HasReproductiveOpportunity>k__BackingField", scenario.Fertile);
        float Evaluate(Hero member, Hero spouse) => (float)_evaluate.Invoke(null,
            new object[] { member, spouse, outcome, context, new List<string>() });
        bool conflict = prototype && !scenario.PlayerInvolved && scenario.Bride.Protected && scenario.Groom.Protected;
        bool twoLeaders = scenario.Bride.Leader && scenario.Groom.Leader;
        return new Result { Bride = Evaluate(_bride, _groom), Groom = Evaluate(_groom, _bride),
            Matrilineal = matrilineal, Allowed = !conflict && !twoLeaders,
            Blocker = conflict ? "both require retention" : twoLeaders ? "two leaders" : null };
    }

    internal static void Run(Action<bool, string> check)
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        var assembly = typeof(StrategicMarriageBehavior).Assembly;
        var helper = assembly.GetType("BellumCivile.Behaviors.BellumMarriageStrategyHelper");
        var constants = assembly.GetType("BellumCivile.BellumCivileConstants");
        _floor = (float)AccessTools.Field(constants, "MarriageStrategyMinimumScore").GetRawConstantValue();
        _contextType = helper.GetNestedType("EvaluationContext", BindingFlags.NonPublic);
        _healthType = helper.GetNestedType("HouseHealth", BindingFlags.NonPublic);
        _outcomeType = assembly.GetType("BellumCivile.Behaviors.MarriageOutcome");
        _evaluate = AccessTools.Method(helper, "EvaluateHouse");
        _bride = Blank<Hero>(); _groom = Blank<Hero>(); _child = Blank<Hero>();
        _brideHouse = Blank<Clan>(); _groomHouse = Blank<Clan>(); _otherRuler = Blank<Clan>();
        _brideHouse.StringId = "bride_house"; _groomHouse.StringId = "groom_house";
        _home = Blank<Kingdom>(); _foreign = Blank<Kingdom>();
        _culture = Blank<CultureObject>(); _otherCulture = Blank<CultureObject>();
        var harmony = new Harmony("bellum.simulation.heiress_households");
        void Patch(MethodBase target, string method) => harmony.Patch(target, prefix: new HarmonyMethod(typeof(MarriageHeiressSimulation), method));
        try
        {
            Patch(AccessTools.PropertyGetter(typeof(Hero), "Clan"), nameof(ClanOf));
            Patch(AccessTools.PropertyGetter(typeof(Hero), "Age"), nameof(Age));
            Patch(AccessTools.PropertyGetter(typeof(Hero), "IsAlive"), nameof(Yes));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "Tier"), nameof(Tier));
            Patch(AccessTools.PropertyGetter(typeof(Kingdom), "RulingClan"), nameof(Ruler));
            Patch(AccessTools.Method(typeof(Clan), "GetRelationWithClan"), nameof(Relation));
            Patch(AccessTools.Method(typeof(Hero), "GetRelation", new[] { typeof(Hero) }), nameof(Personal));
            Patch(AccessTools.PropertyGetter(typeof(CourtAgendaBehavior), "Current"), nameof(Court));
            Patch(AccessTools.Method(helper, "AreKingdomsAllied"), nameof(Allied));
            Patch(AccessTools.Method(_contextType, "IsBloodMember"), nameof(Yes));
            Patch(AccessTools.Method(_contextType, "PoliticalValue"), nameof(Politics));
            Patch(AccessTools.Method(_contextType, "IncomingClaimValue"), nameof(Claim));

            float ForeignPolitics(bool sameCulture, bool room, float relation = 0) => (float)AccessTools.Method(
                assembly.GetType("BellumCivile.Behaviors.MarriagePoliticalRules"), "ForeignPartner").Invoke(null,
                    new object[] { 1000f, 1000f, 0f, false, false, room, false, sameCulture, relation });
            var crown = new Scenario { Name = "Domestic Crown heiress / healthy spare" };
            crown.Bride.CrownHeir = crown.Bride.Royal = true;
            var clan = crown.Copy("Domestic clan heiress / healthy spare");
            clan.Bride.CrownHeir = clan.Bride.Royal = false; clan.Bride.ClanHeir = true;
            var foreign = crown.Copy("Foreign Crown heiress / healthy spare, same culture");
            foreign.Domestic = false; foreign.Groom.Royal = true;
            foreign.Bride.Politics = foreign.Groom.Politics = ForeignPolitics(true, true);
            var cross = foreign.Copy("Foreign cross-cultural / alliance room");
            cross.SameCulture = false;
            cross.Bride.Politics = cross.Groom.Politics = ForeignPolitics(false, true);
            var full = cross.Copy("Foreign cross-cultural / alliance slots full");
            full.Bride.Politics = full.Groom.Politics = ForeignPolitics(false, false);
            var friends = full.Copy("Same full-slot match / relations +15"); friends.Relation = 15;
            friends.Bride.Politics = friends.Groom.Politics = ForeignPolitics(false, false, 15);
            var allied = full.Copy("Foreign cross-cultural / existing realm alliance"); allied.Allied = true;
            var endangered = crown.Copy("Domestic / groom's house endangered");
            endangered.Groom.Risk = 1; endangered.Groom.FertileCouple = false; endangered.Groom.Spares = 0;
            var pressured = foreign.Copy("Foreign / groom's house endangered");
            pressured.Groom.Risk = 1; pressured.Groom.FertileCouple = false; pressured.Groom.Spares = 0;
            var exception = endangered.Copy("Endangered non-heir groom / political value 100");
            exception.Bride.Politics = exception.Groom.Politics = 100;
            var both = cross.Copy("Two Crown heirs"); both.Groom.CrownHeir = true;
            var clanHeirGroom = crown.Copy("Crown heiress / groom is clan heir"); clanHeirGroom.Groom.ClanHeir = true;
            var down = clan.Copy("Healthy clan heiress / two-tier lower groom"); down.Bride.Tier = 6;
            var useful = down.Copy("Same lower-tier groom / bride's political benefit 40"); useful.Bride.Politics = 40;
            var survival = down.Copy("Endangered clan heiress / two-tier lower groom");
            survival.Bride.Risk = 1; survival.Bride.FertileCouple = false; survival.Bride.Spares = 0;
            var claims = clan.Copy("Clan heiress / incoming groom claim value 53"); claims.Bride.IncomingClaim = 53;
            var ageGap = clan.Copy("Clan heiress / six-year age gap, no political value"); ageGap.Groom.Age = 36;
            var usefulAgeGap = ageGap.Copy("Same six-year gap / useful domestic partner");
            usefulAgeGap.Bride.Politics = usefulAgeGap.Groom.Politics = (float)AccessTools.Method(
                assembly.GetType("BellumCivile.Behaviors.MarriagePoliticalRules"), "DomesticPartner").Invoke(null,
                    new object[] { 1000f, 1000f, 100f, false });
            var widowed = crown.Copy("Crown heiress with living child"); widowed.Bride.LivingChild = true;
            var leader = clan.Copy("Existing female clan leader"); leader.Bride.Leader = true;
            var ordinary = new Scenario { Name = "Ordinary non-heiress marriage unchanged" };
            var player = crown.Copy("Player-clan offer unchanged in first phase"); player.PlayerInvolved = true;
            var scenarios = new[] { crown, clan, foreign, cross, full, friends, allied, endangered, pressured, exception,
                both, clanHeirGroom, down, useful, survival, claims, ageGap, usefulAgeGap, widowed, leader, ordinary, player };
            Console.WriteLine("Controlled inputs: age 30, equal tier, fertile match, same culture, zero relations/politics/claims unless named.");
            Console.WriteLine("Foreign royal politics uses current ForeignPartner: no common enemy/threat, at least one existing ally, room as named.");
            Console.WriteLine("Scores are BRIDE HOUSE / GROOM HOUSE. Both require " + _floor + ". Protected status and risk are scenario inputs, not succession discovery tests.");
            Console.WriteLine("Score-only comparison: the later production last-continuation veto is covered by --marriage-households.");
            Console.WriteLine("Scenario | Baseline household | Matrilineal scoring / heir retention");
            foreach (var s in scenarios)
                Console.WriteLine($"{s.Name} | {Score(s, false).Display(_floor)} | {Score(s, true).Display(_floor)}");

            check(Score(crown, false).Bride == 195 && Score(crown, true).Bride == 175
                && Score(crown, true).Groom == 115, "Destination changes Crown-household incentives without lowering acceptance floor");
            check(Score(clan, true).Accepted(_floor), "Ordinary clan heiress can retain house without inventing an extra Crown bonus");
            check(!Score(cross, false).Accepted(_floor) && Score(cross, true).Accepted(_floor), "Matrilineal destination can unblock a useful foreign spare-son match");
            check(!Score(full, true).Accepted(_floor) && Score(friends, true).Accepted(_floor), "Marginal foreign match still needs benefit to both houses");
            check(!Score(endangered, true).Accepted(_floor) && !Score(pressured, true).Accepted(_floor), "Existing risk/departure scoring deters endangered donors in ordinary examples");
            check(Score(exception, true).Accepted(_floor), "Existing soft survival penalties still permit exceptional domestic political benefit");
            check(!Score(both, true).Allowed && !Score(clanHeirGroom, true).Allowed, "Prototype retention condition rejects moving another protected heir even with high scores");
            check(!Score(down, true).Accepted(_floor) && Score(useful, true).Accepted(_floor)
                && Score(survival, true).Accepted(_floor), "Healthy clan heiress retains standards; strategy or survival can justify lower rank");
            check(Score(claims, true).Bride - Score(clan, true).Bride == 53
                && Score(claims, true).Groom == Score(clan, true).Groom, "Claims affect the correct receiving house only");
            check(!Score(ageGap, true).Accepted(_floor) && Score(usefulAgeGap, true).Accepted(_floor),
                "A baseline-only clan heiress has little headroom, but current useful-partner scoring covers an ordinary age gap");
            var candidates = new[] { both, foreign, pressured };
            Scenario Best(bool prototype) => candidates.Where(s => Score(s, prototype).Accepted(_floor))
                .OrderByDescending(s => Math.Min(Score(s, prototype).Bride, Score(s, prototype).Groom)).FirstOrDefault();
            check(Best(false) == pressured && Best(true) == foreign,
                "Illustrative candidate pool switches from sending heiress to endangered foreign house to receiving healthy foreign spare");
            Console.WriteLine("Controlled candidate pool: old best = endangered foreign house receives heiress; proposed best = healthy foreign spare joins heiress. Not a campaign partner-availability estimate.");
            foreach (var unchanged in new[] { ordinary, player, leader })
            {
                Result a = Score(unchanged, false), b = Score(unchanged, true);
                check(a.Bride == b.Bride && a.Groom == b.Groom && a.Matrilineal == b.Matrilineal,
                    unchanged.Name + ": same scores and destination");
            }

            int points = 0;
            foreach (float risk in new[] { 0f, .25f, .5f, .75f, 1f })
            foreach (bool domestic in new[] { true, false })
            foreach (bool culture in new[] { true, false })
            foreach (int politics in new[] { 0, 25, 50, 75, 100 })
            foreach (int tierGap in new[] { -2, 0, 2 })
            {
                var s = crown.Copy("sweep"); s.Domestic = domestic; s.SameCulture = culture;
                s.Groom.Risk = risk; s.Groom.Spares = 0; s.Groom.FertileCouple = false;
                s.Groom.Royal = !domestic; s.Groom.Tier += tierGap;
                s.Bride.Politics = s.Groom.Politics = politics;
                Result result = Score(s, true);
                if (!result.Matrilineal || result.Accepted(_floor) && (result.Bride < _floor || result.Groom < _floor))
                    throw new Exception("Prototype destination/consent invariant failed");
                s.Groom.ClanHeir = true;
                if (Score(s, true).Allowed) throw new Exception("Protected groom silently moved");
                points++;
            }
            check(points == 300, "300 controlled parameter combinations retain the heiress and honor both-house consent / groom protection");

            Console.WriteLine("Sensitivity: domestic clan heiress, two-tier lower groom; needed bride-side political utility");
            foreach (float risk in new[] { 0f, .25f, .5f, .75f, 1f })
            {
                var s = down.Copy("sensitivity"); s.Bride.Risk = risk;
                float required = Math.Max(0, (_floor - Score(s, true).Bride) / (1 - .25f * risk));
                Console.WriteLine($"risk={risk:0.##}: political utility >= {required:0.##}");
            }
            Console.WriteLine("Sensitivity: foreign same-culture royal spare donor, no fertile household, no remaining prospects; politics 80");
            foreach (float risk in new[] { 0f, .25f, .5f, .75f, 1f })
            {
                var s = pressured.Copy("donor_sensitivity"); s.Groom.Risk = risk;
                Console.WriteLine($"risk={risk:0.##}: groom house score={Score(s, true).Groom:0.##}");
            }
            Console.WriteLine("Simulation limitations: controlled political/claim inputs, no candidate-population or campaign-frequency estimate; no eligibility, save/load or wedding execution test.");
        }
        finally { harmony.UnpatchAll(harmony.Id); }
    }
}
