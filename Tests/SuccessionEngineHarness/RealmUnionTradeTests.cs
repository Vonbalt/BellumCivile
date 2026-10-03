using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using BellumCivile;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using Agreement = TaleWorlds.CampaignSystem.CampaignBehaviors.TradeAgreementsCampaignBehavior.TradeAgreement;

internal static class RealmUnionTradeTests
{
    internal static void Run(Action<bool, string> check)
    {
        Kingdom Realm() => (Kingdom)FormatterServices.GetUninitializedObject(typeof(Kingdom));
        var source = Realm(); var destination = Realm(); var partner = Realm(); var unrelated = Realm();
        var inherited = new Agreement(partner, source, CampaignTime.Days(90)) {
            Kingdom1GoldGained = 11, Kingdom2GoldGained = 22,
            Kingdom1GoldGainedTotal = 111, Kingdom2GoldGainedTotal = 222 };
        var existing = new Agreement(destination, partner, CampaignTime.Days(100)) {
            Kingdom1GoldGained = 3, Kingdom2GoldGained = 4,
            Kingdom1GoldGainedTotal = 30, Kingdom2GoldGainedTotal = 40 };
        var untouched = new Agreement(partner, unrelated, CampaignTime.Days(120));
        var original = new List<Agreement> { inherited, existing, untouched };
        var adapter = typeof(RealmUnionRecord).Assembly.GetType("BellumCivile.RealmUnionTradeAdapter");
        var prepare = AccessTools.Method(adapter, "TryPrepare");
        List<Agreement> result = null;
        bool conflict = false;
        bool Prepare()
        {
            var args = new object[] { original, source, destination, (Func<Kingdom, bool>)(_ => conflict), null, null };
            bool ok = (bool)prepare.Invoke(null, args);
            result = (List<Agreement>)args[4];
            check(ok || result == null && !string.IsNullOrEmpty(args[5] as string), "Failed trade plan has no partial output and explains refusal");
            return ok;
        }
        check(Prepare() && result.Count == 2 && original.Count == 3, "Trade merge does not mutate the original list");
        var merged = result.Single(a => a.Kingdom1 == destination);
        check(merged.EndTime == existing.EndTime && merged.Kingdom2 == partner,
            "Shared trade partner keeps the longer existing expiry, not a fresh duration");
        check(merged.Kingdom1GoldGained == 25 && merged.Kingdom2GoldGained == 15
            && merged.Kingdom1GoldGainedTotal == 252 && merged.Kingdom2GoldGainedTotal == 151,
            "Trade counters merge by realm identity despite reversed endpoint order");
        check(result.Any(a => a.Kingdom1 == partner && a.Kingdom2 == unrelated && a.EndTime == untouched.EndTime),
            "Unrelated trade agreement remains untouched");
        original = new List<Agreement> { inherited };
        check(Prepare() && result[0].EndTime == inherited.EndTime && result[0].Kingdom1GoldGained == 22,
            "New inherited partner preserves exact source expiry and accumulated earnings");
        conflict = true;
        check(!Prepare() && original.Count == 1, "Diplomatically incompatible partner defers without writes");
        conflict = false;
        original.Add(inherited);
        check(!Prepare(), "Duplicate source agreements are not silently combined");
        original = new List<Agreement> { new Agreement(source, destination, CampaignTime.Days(90)) };
        check(!Prepare(), "Bilateral trade inside union requires separate settlement");
        existing.Kingdom1GoldGained = int.MaxValue;
        original = new List<Agreement> { inherited, existing };
        check(!Prepare(), "Trade counter overflow defers before modifying native storage");

        var native = (TradeAgreementsCampaignBehavior)FormatterServices.GetUninitializedObject(typeof(TradeAgreementsCampaignBehavior));
        var field = AccessTools.Field(typeof(TradeAgreementsCampaignBehavior), "_tradeAgreements");
        check(field?.FieldType == typeof(List<Agreement>), "Native trade adapter matches installed storage schema");
        original = new List<Agreement> { inherited, untouched };
        field.SetValue(native, original);
        var dryRun = new object[] { native, source, destination, (Func<Kingdom, bool>)(_ => false), null };
        check((bool)AccessTools.Method(adapter, "CanApply").Invoke(null, dryRun)
            && ReferenceEquals(field.GetValue(native), original), "Trade preflight validates native storage without replacing records");
        dryRun[3] = (Func<Kingdom, bool>)(_ => true);
        check(!(bool)AccessTools.Method(adapter, "CanApply").Invoke(null, dryRun)
            && ReferenceEquals(field.GetValue(native), original), "Trade preflight rejects conflicts before client registry writes");
        bool started = false, returned = false;
        var call = new object[] { native, source, destination, (Func<Kingdom, bool>)(_ => false),
            (Action)(() => started = true), (Action)(() => returned = true), null };
        check((bool)AccessTools.Method(adapter, "TryApply").Invoke(null, call) && started && returned,
            "Native trade storage replacement surrounds write with journal callbacks");
        var applied = (List<Agreement>)field.GetValue(native);
        check(!ReferenceEquals(applied, original) && original[0].Kingdom2 == source
            && applied.All(a => a.Kingdom1 != source && a.Kingdom2 != source),
            "Native adapter swaps prepared records without native signing events or altering original snapshot");
    }
}
