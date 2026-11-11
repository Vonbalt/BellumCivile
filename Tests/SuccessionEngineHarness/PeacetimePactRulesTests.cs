using System;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using BellumCivile;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameComponents;

internal static class PeacetimePactRulesTests
{
    private static bool MainHero(ref Hero __result) { __result = null; return false; }
    private static bool Allied(ref bool __result) { __result = true; return false; }

    internal static void Run(Action<bool, string> check)
    {
        Type rules = typeof(HostagePactRecord).Assembly.GetType("BellumCivile.PeacetimeHostagePactRules", true);
        object Call(string name, params object[] args) => AccessTools.Method(rules, name).Invoke(null, args);
        double Score(int relation = 0, bool threat = false, bool trade = false, bool marriage = false,
            float target = 0, int own = 4, int other = 4, int duration = 50, int baseline = 50, int gold = 0)
            => (double)Call("Score", relation, threat, trade, marriage, target, own, other, duration, baseline, gold);
        int Constant(string name) => Convert.ToInt32(AccessTools.Field(rules, name).GetRawConstantValue());
        check(Constant("ProposalInfluenceCost") == 100 && Constant("EvaluationIntervalDays") == 30
            && Constant("MaxRealmEvaluationsPerDay") == 5 && Constant("RefusalCooldownDays") == 90,
            "Voluntary proposal costs and fixed-day scheduler constants match the plan");
        check(Math.Abs((float)AccessTools.Field(rules, "EvaluationChance").GetRawConstantValue() - .05f) < .00001f,
            "Each scheduled NPC realm has a five-percent proposal evaluation chance");
        var payments = AccessTools.Method(typeof(VoluntaryPactAiBehavior), "CandidatePayments");
        int[] Offers(int gold) => (int[])payments.Invoke(null, new object[] { gold });
        check(Offers(0).SequenceEqual(new[] { 0 }) && Offers(50000).SequenceEqual(new[] { 0, 5000 })
            && Offers(1000000).SequenceEqual(new[] { 0, 10000, 25000, 50000, 100000 })
            && Offers(int.MaxValue).Last() == 100000,
            "NPC compensation is optional, affordable and capped at ten percent of treasury or 100000 denars");
        check(Score(relation: 30, trade: true, marriage: true) == 59,
            "Friendly connected houses can agree without compensation");
        check(Score(relation: 20, threat: true, own: 1, other: 1) == 44
            && Score(relation: 20, threat: true, own: 1, other: 1, gold: 50000) > 50,
            "Compensation can persuade a cautious house to pledge a close heir");
        check(Score(relation: -20, target: 100, own: 1, other: 1, gold: 100000) < 50,
            "A strong motive for war and close heir are not automatically bought off");
        check(Score(threat: true, duration: 100) < Score(threat: true)
            && Score(threat: true, duration: 150) < Score(threat: true, duration: 100),
            "Increasing duration increases reluctance relative to the saved minimum");
        check(Score(own: 4, other: 1) > Score(own: 4, other: 4)
            && Score(own: 1, other: 4) < Score(own: 1, other: 1),
            "Lower numerical tier is a more valuable hostage in both directions");
        check(Score(own: 4, other: 4, gold: 100000) > Score(own: 4, other: 4, gold: 50000)
            && (double)Call("PaymentBonus", 1000000) < 30,
            "Compensation is beneficial with bounded diminishing returns");
        check(Score(duration: 100, baseline: 100) == Score(duration: 50, baseline: 50),
            "The configured baseline, not a hardcoded duration, controls reluctance");
        var random = new Random(4162); int freeAccepted = 0, paidAccepted = 0;
        for (int i = 0; i < 10000; i++)
        {
            int relation = random.Next(-50, 51), own = random.Next(1, 5), other = random.Next(1, 5);
            bool threat = random.NextDouble() < .2, trade = random.NextDouble() < .1, marriage = random.NextDouble() < .05;
            float warTarget = (float)(random.NextDouble() * 100);
            if (Score(relation, threat, trade, marriage, warTarget, own, other) >= 50) freeAccepted++;
            if (Score(relation, threat, trade, marriage, warTarget, own, other, gold: 50000) >= 50) paidAccepted++;
        }
        check(paidAccepted > freeAccepted && freeAccepted > 0 && paidAccepted < 10000,
            $"Illustrative 10000-case balance sample: {freeAccepted} free versus {paidAccepted} with 50000 denars meet the target threshold");
        bool invalidDuration = false, invalidGold = false;
        try { Score(duration: 50, baseline: 100); } catch (TargetInvocationException ex) { invalidDuration = ex.InnerException is ArgumentOutOfRangeException; }
        try { Score(gold: -1); } catch (TargetInvocationException ex) { invalidGold = ex.InnerException is ArgumentOutOfRangeException; }
        check(invalidDuration && invalidGold, "Invalid terms fail closed");
        check(!new HostagePactRecord().VoluntaryPeacetime, "Legacy and wartime pacts do not acquire voluntary status by default");
        var patchType = typeof(HostagePactRecord).Assembly.GetType("BellumCivile.Patches.PeacetimePactAlliancePatch", true);
        var bonus = AccessTools.Method(patchType, "ApplyBonus");
        float Alliance(float initial, bool active)
        {
            object[] args = { new ExplainedNumber(initial), active };
            bonus.Invoke(null, args);
            return ((ExplainedNumber)args[0]).ResultNumber;
        }
        check(Alliance(46, true) == 54 && Alliance(46, false) == 46,
            "Only a qualifying voluntary pact adds eight alliance consideration points");
        check(Alliance(0, true) == 0, "An early native alliance eligibility exit is not overridden by the pact bonus");
        var allianceHarmony = new Harmony("bellum.tests.peacetime_alliance_hook");
        var nativeAlliance = AccessTools.Method(typeof(DefaultAllianceModel), "GetScoreOfStartingAlliance");
        try
        {
            allianceHarmony.Patch(nativeAlliance, postfix: new HarmonyMethod(AccessTools.Method(patchType, "Postfix")));
            check(Harmony.GetPatchInfo(nativeAlliance).Postfixes.Any(p => p.owner == allianceHarmony.Id),
                "Voluntary pact alliance hook installs on the native model");
        }
        finally { allianceHarmony.UnpatchAll(allianceHarmony.Id); }
        var behavior = new HostagePactBehavior();
        var establish = AccessTools.Method(typeof(HostagePactBehavior), "TryEstablishVoluntary");
        var realm1 = (Kingdom)FormatterServices.GetUninitializedObject(typeof(Kingdom));
        var realm2 = (Kingdom)FormatterServices.GetUninitializedObject(typeof(Kingdom));
        var hostage1 = (Hero)FormatterServices.GetUninitializedObject(typeof(Hero));
        var hostage2 = (Hero)FormatterServices.GetUninitializedObject(typeof(Hero));
        bool Establish(Kingdom a, Kingdom b, Hero x, Hero y, int duration, int baseline)
            => (bool)establish.Invoke(behavior, new object[] { a, b, x, y, duration, baseline });
        var harmony = new Harmony("bellum.tests.peacetime_pact_guards");
        try
        {
            harmony.Patch(AccessTools.PropertyGetter(typeof(Hero), "MainHero"), prefix: new HarmonyMethod(typeof(PeacetimePactRulesTests), nameof(MainHero)));
            harmony.Patch(AccessTools.Method(typeof(Kingdom), "IsAllyWith"), prefix: new HarmonyMethod(typeof(PeacetimePactRulesTests), nameof(Allied)));
            check(!Establish(realm1, realm2, hostage1, null, 50, 50)
                && !Establish(realm1, realm2, hostage1, hostage1, 50, 50),
                "Peacetime placement rejects missing or duplicate hostages before custody");
            check(!Establish(realm1, realm1, hostage1, hostage2, 50, 50)
                && !Establish(realm1, realm2, hostage1, hostage2, 50, 100),
                "Peacetime placement rejects self-pacts and stale minimum durations");
            check(!Establish(realm1, realm2, hostage1, hostage2, 50, 50),
                "Peacetime placement rejects realms already allied");
        }
        finally { harmony.UnpatchAll(harmony.Id); }
    }
}
