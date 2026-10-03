using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using BellumCivile;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Localization;

internal static class HostagePresentationTests
{
    private static T Empty<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    internal static void Run(Action<bool, string> check)
    {
        var text = typeof(HostagePactRecord).Assembly.GetType("BellumCivile.HostagePactText");
        object Call(string method, params object[] args) => AccessTools.Method(text, method).Invoke(null, args);
        string Words(bool signed, bool active) => ((TextObject)Call("BrokerWords", signed, active))
            .SetTextVariable("HERO", "Aldric").SetTextVariable("REALM", "Vlandia").ToString();
        check(Words(true, true).Contains("treaty you agreed") && Words(true, true).Contains("Vlandia"),
            "Broker identifies player's own treaty and opposing realm");
        check(!Words(false, true).Contains("you agreed") && Words(false, true).Contains("between the two realms"),
            "Broker does not attribute predecessor's treaty to player");
        check(Words(true, false).Contains("pledge has ended") && !Words(true, false).Contains("you agreed"),
            "Broker pending disposition does not claim pact remains active");
        var first = Empty<Hero>(); var second = Empty<Hero>(); var signer = Empty<Hero>(); var otherSigner = Empty<Hero>();
        var pact = new HostagePactRecord { Phase = HostagePactPhase.Active, EndDay = 100, FirstSignatory = signer, SecondSignatory = otherSigner,
            FirstHostage = new TreatyHostageRecord { Hero = first, CustodyEstablished = true },
            SecondHostage = new TreatyHostageRecord { Hero = second, CustodyEstablished = true } };
        check((bool)Call("SignedBy", pact, first, signer) && !(bool)Call("SignedBy", pact, first, otherSigner),
            "Broker uses supplying signatory rather than receiving signatory");
        check((bool)Call("SignedBy", pact, second, otherSigner) && !(bool)Call("SignedBy", pact, second, signer),
            "Reciprocal hostage uses opposite signatory");
        check(!(bool)Call("SignedBy", pact, first, Empty<Hero>()) && !(bool)Call("SignedBy", pact, first, null),
            "Replacement or unknown ruler is not original signer");
        var behavior = new HostagePactBehavior();
        AccessTools.Field(typeof(HostagePactBehavior), "_pacts").SetValue(behavior, new List<HostagePactRecord> { pact });
        object Query(string method, Hero hero) => AccessTools.Method(typeof(HostagePactBehavior), method).Invoke(behavior, new object[] { hero });
        check(Query("GetProtectedPact", first) == pact, "Presentation finds protected hostage pact");
        pact.FirstHostage.Outcome = HostageCustodyOutcome.Retain; pact.FirstHostage.ActionCompleted = true;
        check(Query("GetProtectedPact", first) == null, "Ordinary retained prisoner no longer receives treaty hostage label");
        check(((IEnumerable<HostagePactRecord>)Query("GetHistory", first)).Count() == 1,
            "Former hostage preserves historical treaty entry");
        pact.Phase = HostagePactPhase.Preparing;
        check(!((IEnumerable<HostagePactRecord>)Query("GetHistory", first)).Any(),
            "Unestablished pact creates no encyclopedia treaty history");
    }
}
