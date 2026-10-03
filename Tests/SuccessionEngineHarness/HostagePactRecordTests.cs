using System;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using BellumCivile;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;

internal static class HostagePactRecordTests
{
    private static T Empty<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    private static object Call(object obj, string method, params object[] args)
        => obj.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(obj, args);
    internal static void Run(Action<bool, string> check)
    {
        foreach (var type in new[] { typeof(HostagePactRecord), typeof(TreatyHostageRecord) })
        {
            var fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public);
            var ids = fields.Select(f => f.GetCustomAttributesData().Single(a => a.AttributeType.Name == "SaveableFieldAttribute")
                .ConstructorArguments[0].Value).ToList();
            check(ids.Count == ids.Distinct().Count(), "Hostage record saves every field with unique IDs");
        }
        HostagePactRecord Make()
        {
            var p = new HostagePactRecord { Id = "pact-test", FirstRealm = Empty<Kingdom>(), SecondRealm = Empty<Kingdom>(),
                FirstHouse = Empty<Clan>(), SecondHouse = Empty<Clan>() };
            p.FirstHostage = new TreatyHostageRecord { Hero = Empty<Hero>(), SupplyingHouse = p.FirstHouse,
                ReceivingHouse = p.SecondHouse, Holding = Empty<Settlement>(), Tier = 1, NegotiatedCost = 30, CustodyEstablished = true };
            p.SecondHostage = new TreatyHostageRecord { Hero = Empty<Hero>(), SupplyingHouse = p.SecondHouse,
                ReceivingHouse = p.FirstHouse, Holding = Empty<Settlement>(), Tier = 4, NegotiatedCost = 12, CustodyEstablished = true };
            return p;
        }
        var pact = Make();
        check(!(bool)Call(pact, "TryActivate", double.NaN), "Invalid date cannot activate pact");
        check((bool)Call(pact, "TryActivate", 10d) && pact.EndDay == 110, "Pact freezes exact 100-day expiry");
        check(!(bool)Call(pact, "TryActivate", 20d) && pact.EndDay == 110, "Activation cannot extend signed pact");
        check(!(bool)Call(pact, "IsDue", 109.99d) && (bool)Call(pact, "IsDue", 110d), "Pact expiry boundary");
        check((bool)Call(pact, "TryBeginResolution", HostagePactEndReason.War, pact.FirstRealm), "War starts saved resolution");
        check(pact.FirstHostage.Outcome == HostageCustodyOutcome.Pending && pact.SecondHostage.Outcome == HostageCustodyOutcome.Pending,
            "Reciprocal war disposition remains independent");
        check(!(bool)Call(pact, "TryBeginResolution", HostagePactEndReason.HostageDied, null)
            && pact.EndReason == HostagePactEndReason.War, "Death callback cannot overwrite breach resolution");
        check((bool)Call(pact.FirstHostage, "TryChoose", HostageCustodyOutcome.Retain)
            && !(bool)Call(pact.FirstHostage, "TryChoose", HostageCustodyOutcome.Execute), "Disposition cannot reroll");
        check((bool)Call(pact, "Protects", pact.FirstHostage.Hero), "Pending action retains hostage protection");
        pact.FirstHostage.ActionCompleted = true;
        check(!(bool)Call(pact, "Protects", pact.FirstHostage.Hero) && !(bool)Call(pact, "TryComplete"),
            "Resolved prisoner unlocked without ending other hostage custody");
        Call(pact.SecondHostage, "TryChoose", HostageCustodyOutcome.Release);
        pact.SecondHostage.ActionCompleted = true;
        check((bool)Call(pact, "TryComplete") && !(bool)Call(pact, "TryComplete"), "Completion is one-time");
        var peaceful = Make(); Call(peaceful, "TryActivate", 20d);
        Call(peaceful, "TryBeginResolution", HostagePactEndReason.HouseReplaced, null);
        check(peaceful.FirstHostage.Outcome == HostageCustodyOutcome.Release
            && peaceful.SecondHostage.Outcome == HostageCustodyOutcome.Release, "House replacement returns both hostages");
        var malformed = Make(); malformed.FirstHostage.NegotiatedCost = 1;
        check(!(bool)Call(malformed, "TryActivate", 0d), "Invalid negotiated cost cannot activate pact");
        var interrupted = Make();
        check((bool)Call(interrupted, "TryAbortPreparation")
            && interrupted.Phase == HostagePactPhase.Resolving
            && interrupted.FirstHostage.Outcome == HostageCustodyOutcome.Release
            && interrupted.SecondHostage.Outcome == HostageCustodyOutcome.Release,
            "Interrupted reciprocal handover rolls back both reservations");
        check(!(bool)Call(interrupted, "TryActivate", 1d) && !(bool)Call(interrupted, "TryAbortPreparation"),
            "Aborted handover cannot restart or change its outcome");
        check(!(bool)Call(peaceful, "TryAbortPreparation"), "Preparation rollback cannot overwrite established pact resolution");
        var lifecycle = typeof(HostagePactRecord).Assembly.GetType("BellumCivile.HostagePactLifecycleRules");
        var behavior = typeof(BellumCivile.Behaviors.HostagePactBehavior);
        foreach (TaleWorlds.CampaignSystem.Actions.DeclareWarAction.DeclareWarDetail detail in
            Enum.GetValues(typeof(TaleWorlds.CampaignSystem.Actions.DeclareWarAction.DeclareWarDetail)))
        {
            bool expected = detail == TaleWorlds.CampaignSystem.Actions.DeclareWarAction.DeclareWarDetail.CausedByKingdomDecision
                || detail == TaleWorlds.CampaignSystem.Actions.DeclareWarAction.DeclareWarDetail.CausedByPlayerHostility;
            check((bool)AccessTools.Method(behavior, "IsVoluntaryDeclaration").Invoke(null, new object[] { detail }) == expected,
                "Hostage deliberate-war classification: " + detail);
        }
        HostagePactEndReason Reason(params object[] flags) => (HostagePactEndReason)AccessTools.Method(lifecycle, "Evaluate").Invoke(null, flags);
        check(Reason(false, false, false, false, false, true, false) == HostagePactEndReason.None,
            "Healthy pact remains active");
        check(Reason(true, true, true, false, true, false, true) == HostagePactEndReason.None,
            "Transient civil-war realm transfer defers invalidation");
        check(Reason(false, true, true, false, false, true, false) == HostagePactEndReason.RealmLost,
            "Destroyed realm peacefully invalidates pact");
        check(Reason(false, false, true, false, false, true, false) == HostagePactEndReason.HouseReplaced,
            "New ruling house invalidates pact without betrayal");
        check(Reason(false, false, false, true, false, true, false) == HostagePactEndReason.HostageDied,
            "Hostage death returns surviving reciprocal hostage");
        check(Reason(false, false, false, false, true, false, true) == HostagePactEndReason.War,
            "War cannot auto-release hostages through expiry or settings fallback");
        check(Reason(false, false, false, false, false, false, false) == HostagePactEndReason.RevampDisabled,
            "Disabling revamp peacefully releases treaty custody");
        check(Reason(false, false, false, false, false, true, true) == HostagePactEndReason.Expired,
            "Elapsed term starts peaceful release");
        var ranking = typeof(HostagePactRecord).Assembly.GetType("BellumCivile.TreatyHostageEligibility");
        var first = Empty<Hero>(); var second = Empty<Hero>();
        var line = new System.Collections.Generic.List<Hero> { first, second };
        check((int)AccessTools.Method(ranking, "TierInLine").Invoke(null, new object[] { line, second }) == 2,
            "Unavailable earlier heir does not change legal hostage rank");
        check((int)AccessTools.Method(ranking, "TierInLine").Invoke(null, new object[] { line, Empty<Hero>() }) == 4,
            "Unranked blood relative uses fourth tier");
    }
}
