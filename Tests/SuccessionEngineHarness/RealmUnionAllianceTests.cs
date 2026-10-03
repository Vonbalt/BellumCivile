using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;
using BellumCivile;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;

internal static class RealmUnionAllianceTests
{
    private static readonly HashSet<Kingdom> Refreshed = new HashSet<Kingdom>();
    private static bool Refresh(Kingdom __instance) { Refreshed.Add(__instance); return false; }

    internal static void Run(Action<bool, string> check)
    {
        Kingdom Realm() => (Kingdom)FormatterServices.GetUninitializedObject(typeof(Kingdom));
        var source = Realm(); var destination = Realm(); var partner = Realm(); var unrelated = Realm();
        var native = (AllianceCampaignBehavior)FormatterServices.GetUninitializedObject(typeof(AllianceCampaignBehavior));
        var nativeType = typeof(AllianceCampaignBehavior);
        var alliance = nativeType.GetNestedType("Alliance", BindingFlags.NonPublic | BindingFlags.Public);
        var calls = nativeType.GetNestedType("CallToWarAgreement", BindingFlags.NonPublic | BindingFlags.Public);
        IList List(Type item) => (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(item));
        object Entry(Kingdom first, Kingdom second, float day) => AccessTools.Constructor(alliance,
            new[] { typeof(Kingdom), typeof(Kingdom), typeof(CampaignTime) }).Invoke(new object[] { first, second, CampaignTime.Days(day) });
        var original = List(alliance);
        original.Add(Entry(partner, source, 90)); original.Add(Entry(destination, partner, 100));
        original.Add(Entry(unrelated, partner, 120));
        var field = AccessTools.Field(nativeType, "_alliances");
        var callField = AccessTools.Field(nativeType, "_callToWarAgreements");
        field.SetValue(native, original); callField.SetValue(native, List(calls));
        var adapter = typeof(RealmUnionRecord).Assembly.GetType("BellumCivile.RealmUnionAllianceAdapter");
        var prepare = AccessTools.Method(adapter, "TryPrepare");
        IList result = null;
        bool conflict = false;
        bool Prepare()
        {
            var args = new object[] { original, source, destination, (Func<Kingdom, bool>)(_ => conflict), null, null, null };
            bool ok = (bool)prepare.Invoke(null, args);
            result = (IList)args[4];
            check(ok || result == null && args[5] == null && !string.IsNullOrEmpty(args[6] as string),
                "Refused alliance plan leaves no partial output");
            return ok;
        }
        check(Prepare() && result.Count == 2 && original.Count == 3, "Alliance projection preserves original native list");
        var last = result[result.Count - 1];
        check((Kingdom)AccessTools.Field(alliance, "Kingdom1").GetValue(last) == destination
            && (Kingdom)AccessTools.Field(alliance, "Kingdom2").GetValue(last) == partner
            && (CampaignTime)AccessTools.Field(alliance, "EndTime").GetValue(last) == CampaignTime.Days(100),
            "Inherited alliance uses destination and retains longer expiry across reversed endpoints");
        conflict = true;
        check(!Prepare(), "Conflicting alliance defers before native writes");
        conflict = false;
        original.Add(Entry(source, partner, 150));
        check(!Prepare(), "Duplicate source alliance is rejected");
        original.RemoveAt(original.Count - 1);
        original.Add(Entry(source, destination, 150));
        check(!Prepare(), "Alliance between merging realms requires separate handling");
        original.RemoveAt(original.Count - 1);
        var canTransfer = AccessTools.Method(adapter, "CanTransferCalls");
        bool CallsAllowed() => (bool)canTransfer.Invoke(null, new object[] { native, source, destination, null });
        check(CallsAllowed(), "Empty native call-to-war list permits alliance transfer");
        var dryRun = new object[] { native, source, destination, (Func<Kingdom, bool>)(_ => false), null };
        check((bool)AccessTools.Method(adapter, "CanApply").Invoke(null, dryRun)
            && ReferenceEquals(field.GetValue(native), original), "Combined agreement preflight validates alliance storage without writes");
        var callConstructor = AccessTools.Constructor(calls, new[] { typeof(Kingdom), typeof(Kingdom), typeof(Kingdom), typeof(CampaignTime) });
        foreach (var involved in new[] { source, destination })
            for (int role = 0; role < 3; role++)
            {
                var roles = new object[] { partner, unrelated, Realm(), CampaignTime.Days(200) };
                roles[role] = involved;
                var entries = List(calls); entries.Add(callConstructor.Invoke(roles)); callField.SetValue(native, entries);
                check(!CallsAllowed(), "Call-to-war involving either participant in any role blocks absorption");
            }
        callField.SetValue(native, List(calls));
        var harmony = new Harmony("bellum.test.realm_union_alliances");
        try
        {
            Refreshed.Clear();
            harmony.Patch(AccessTools.Method(typeof(Kingdom), "UpdateAlliedKingdoms"),
                prefix: new HarmonyMethod(typeof(RealmUnionAllianceTests), nameof(Refresh)));
            bool started = false, returned = false;
            var args = new object[] { native, source, destination, (Func<Kingdom, bool>)(_ => false),
                (Action)(() => started = true), (Action)(() => returned = true), null };
            check((bool)AccessTools.Method(adapter, "TryApply").Invoke(null, args) && started && returned,
                "Native alliance storage replacement surrounds write and cache refresh with receipts");
            check(Refreshed.SetEquals(new[] { source, destination, partner }), "Alliance cache refresh covers only affected realms");
            check(!ReferenceEquals(original, field.GetValue(native)) && original.Count == 3,
                "Alliance adapter replaces native records without changing original snapshot");
        }
        finally { harmony.UnpatchAll(harmony.Id); Refreshed.Clear(); }
    }
}
