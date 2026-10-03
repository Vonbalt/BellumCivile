using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using BellumCivile;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;

internal static class HostagePoliticsTests
{
    private static readonly List<string> Changes = new List<string>();
    private static T Empty<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    private static bool Capture(int relationChange, string sourceId, float durationYears, RelationMemoryScope scope)
    { Changes.Add($"{sourceId}:{relationChange}:{durationYears}:{scope}"); return false; }
    private static bool Alive(ref bool __result) { __result = true; return false; }
    private static bool Free(ref bool __result) { __result = false; return false; }
    internal static void Run(Action<bool, string> check)
    {
        var harmony = new Harmony("bellum.tests.hostage.politics");
        harmony.Patch(AccessTools.Method(typeof(RelationMemoryService), "ApplyChange"), prefix: new HarmonyMethod(typeof(HostagePoliticsTests), "Capture"));
        harmony.Patch(AccessTools.PropertyGetter(typeof(Hero), "IsAlive"), prefix: new HarmonyMethod(typeof(HostagePoliticsTests), "Alive"));
        harmony.Patch(AccessTools.PropertyGetter(typeof(Hero), "IsPrisoner"), prefix: new HarmonyMethod(typeof(HostagePoliticsTests), "Free"));
        try
        {
            HostagePactRecord Make()
            {
                Changes.Clear();
                var p = new HostagePactRecord { EndReason = HostagePactEndReason.War,
                    FirstRealm = Empty<Kingdom>(), SecondRealm = Empty<Kingdom>(),
                    FirstHouse = Empty<Clan>(), SecondHouse = Empty<Clan>(),
                    FirstHostage = new TreatyHostageRecord { Hero = Empty<Hero>(), ActionCompleted = true, Outcome = HostageCustodyOutcome.Retain },
                    SecondHostage = new TreatyHostageRecord { Hero = Empty<Hero>(), ActionCompleted = true, Outcome = HostageCustodyOutcome.Retain } };
                p.VoluntaryAggressor = p.FirstRealm;
                return p;
            }
            void Apply(HostagePactRecord p) => AccessTools.Method(typeof(HostagePactBehavior), "ApplyPactMemories").Invoke(null, new object[] { p });
            var pact = Make(); Apply(pact); Apply(pact);
            check(Changes.SequenceEqual(new[] { "broke_hostage_peace:-20:10:House" }), "Pact breach is one ten-year house memory, never repeated");
            pact = Make(); pact.SecondHostage.ExecutionSucceeded = true; Apply(pact);
            check(Changes.SequenceEqual(new[] { "betrayed_hostage_pledge:-30:10:House" }), "Aggressor executing hostage replaces rather than stacks breach penalty");
            pact = Make(); pact.FirstHostage.ExecutionSucceeded = true; Apply(pact);
            check(Changes.SequenceEqual(new[] { "broke_hostage_peace:-20:10:House" }), "Defender retaliation does not duplicate ordinary execution memories");
            pact = Make(); pact.FirstHostage.Outcome = HostageCustodyOutcome.Release; pact.FirstHostage.ClemencyChosen = true;
            Apply(pact); Apply(pact);
            check(Changes.SequenceEqual(new[] { "broke_hostage_peace:-20:10:House", "spared_treaty_hostage:10:5:House" }),
                "Voluntary mercy despite supplier breach grants one five-year house memory");
            pact = Make(); pact.SecondHostage.Outcome = HostageCustodyOutcome.Release; pact.SecondHostage.ClemencyChosen = true; Apply(pact);
            check(Changes.Count == 1, "Aggressor cannot earn clemency by creating its own breach");
            pact = Make(); pact.FirstHostage.Outcome = HostageCustodyOutcome.Release; Apply(pact);
            check(Changes.Count == 1, "Mandatory release after stale judgment earns no clemency");
            pact = Make(); pact.VoluntaryAggressor = null; Apply(pact);
            check(Changes.Count == 0 && !pact.BreachMemoryApplied, "Unknown war provenance grants no betrayal or mercy memory");
            pact = Make(); pact.EndReason = HostagePactEndReason.Expired; Apply(pact);
            check(Changes.Count == 0, "Ordinary expiry grants no extra relationship rewards");
            pact = Make(); pact.SecondHostage.ActionCompleted = false; Apply(pact);
            check(Changes.Count == 0 && !pact.BreachMemoryApplied, "Pending reciprocal judgment defers final breach weight");
        }
        finally { harmony.UnpatchAll(harmony.Id); Changes.Clear(); }
    }
}
