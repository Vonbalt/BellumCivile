using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using BellumCivile;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;

internal static class RealmUnionTributeTests
{
    internal static void Run(Action<bool, string> check)
    {
        Kingdom Realm() => (Kingdom)FormatterServices.GetUninitializedObject(typeof(Kingdom));
        var source = Realm(); var destination = Realm(); var partner = Realm();
        StanceLink Stance(Kingdom first, Kingdom second)
        {
            var stance = (StanceLink)FormatterServices.GetUninitializedObject(typeof(StanceLink));
            AccessTools.Property(typeof(StanceLink), "Faction1").SetValue(stance, first);
            AccessTools.Property(typeof(StanceLink), "Faction2").SetValue(stance, second);
            return stance;
        }
        var adapter = typeof(RealmUnionRecord).Assembly.GetType("BellumCivile.RealmUnionTributeAdapter");
        var prepare = AccessTools.Method(adapter, "TryPrepare");
        var apply = AccessTools.Method(adapter, "TryApply");
        foreach (int sign in new[] { -1, 1 })
            foreach (bool sourceFirst in new[] { false, true })
                foreach (bool destinationFirst in new[] { false, true })
                {
                    var oldStance = sourceFirst ? Stance(source, partner) : Stance(partner, source);
                    var newStance = destinationFirst ? Stance(destination, partner) : Stance(partner, destination);
                    oldStance.SetDailyTributePaid(source, sign * 100, 100);
                    oldStance.TotalTributePaidFrom1To2 = (sourceFirst ? 1 : -1) * sign * 2500;
                    var args = new object[] { oldStance, newStance, source, destination, partner, null, null };
                    check((bool)prepare.Invoke(null, args), "Native tribute plans both directions and stance endpoint orders");
                    bool started = false, returned = false;
                    var write = new object[] { args[5], (Action)(() => started = true), (Action)(() => returned = true), null };
                    check((bool)apply.Invoke(null, write) && started && returned, "Native tribute transfer verifies before recording return");
                    check(oldStance.GetDailyTributeToPay(source) == 0 && newStance.GetDailyTributeToPay(destination) == sign * 100
                        && newStance.GetTotalTributePaid(destination) == sign * 2500 && newStance.GetRemainingTributePaymentCount() == 75,
                        "Inherited tribute retains payer direction and only the 75 unpaid installments");
                    check(!(bool)apply.Invoke(null, write), "Consumed native tribute plan cannot charge the obligation twice");
                }
        var oldLedger = Stance(source, partner); var newLedger = Stance(destination, partner);
        oldLedger.SetDailyTributePaid(source, 100, 100);
        object[] Plan() => new object[] { oldLedger, newLedger, source, destination, partner, null, null };
        var captured = Plan();
        check((bool)prepare.Invoke(null, captured), "Fresh native tribute plan captures valid ledger");
        oldLedger.TotalTributePaidFrom1To2 = 100;
        int callbacks = 0;
        check(!(bool)apply.Invoke(null, new object[] { captured[5], (Action)(() => callbacks++), (Action)(() => callbacks++), null })
            && callbacks == 0, "Payment after capture prevents stale tribute overwrite");
        newLedger.TotalTributePaidFrom1To2 = 50;
        check(!(bool)prepare.Invoke(null, Plan()), "Existing destination tribute history requires reconciliation");
        newLedger.TotalTributePaidFrom1To2 = 0;
        oldLedger.SetDailyTributePaid(source, int.MaxValue, 100);
        check(!(bool)prepare.Invoke(null, Plan()), "Native arithmetic overflow is rejected before write");
        oldLedger.SetDailyTributePaid(source, 100, 1);
        check(!(bool)prepare.Invoke(null, Plan()), "Fully paid native tribute is not reintroduced");
        CheckLegacy(check);
    }

    private static void CheckLegacy(Action<bool, string> check)
    {
        var type = typeof(RealmUnionRecord).Assembly.GetType("BellumCivile.RealmUnionLegacyTributeAdapter");
        var prepare = AccessTools.Method(type, "TryPrepare");
        var outgoing = new ActiveTreatyTributeRecord("source", "partner", 100, 75);
        var incoming = new ActiveTreatyTributeRecord("another", "source", 30, 12);
        var original = new List<ActiveTreatyTributeRecord> { outgoing, incoming,
            new ActiveTreatyTributeRecord("destination", "partner", 40, 3) };
        List<ActiveTreatyTributeRecord> result = null;
        bool Plan()
        {
            var args = new object[] { original, "source", "destination", (Func<string, bool>)(_ => false), null, null };
            bool ok = (bool)prepare.Invoke(null, args);
            result = (List<ActiveTreatyTributeRecord>)args[4];
            return ok;
        }
        check(Plan() && result.Count == 3 && result[0].PayerKingdomId == "destination"
            && result[0].DailyGold == 100 && result[0].RemainingDays == 75 && result[1].RecipientKingdomId == "destination",
            "Legacy inheritance preserves incoming/outgoing schedules and distinct maturities");
        check(outgoing.PayerKingdomId == "source" && incoming.RecipientKingdomId == "source",
            "Legacy projection leaves original records intact");
        original.Add(new ActiveTreatyTributeRecord("partner", "destination", 5, 3));
        check(!Plan() && result == null, "Opposite tribute directions defer instead of silently netting debts");
        original.RemoveAt(original.Count - 1);
        original.Add(new ActiveTreatyTributeRecord("source", "destination", 5, 3));
        check(!Plan(), "Tribute between merging realms is not silently forgiven");
        original.RemoveAt(original.Count - 1);
        var behavior = new ForeignTreatyBehavior();
        var field = AccessTools.Field(typeof(ForeignTreatyBehavior), "_activeTributes");
        field.SetValue(behavior, original);
        bool started = false, returned = false;
        var write = new object[] { "source", "destination", (Func<string, bool>)(_ => false),
            (Action)(() => started = true), (Action)(() => returned = true), null };
        check((bool)AccessTools.Method(typeof(ForeignTreatyBehavior), "TryInheritRealmUnionLegacyTributes").Invoke(behavior, write)
            && started && returned && !ReferenceEquals(field.GetValue(behavior), original),
            "Legacy adapter swaps prepared schedules without ticking or paying them");
    }
}
