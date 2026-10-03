using System;
using System.Collections.Generic;
using BellumCivile;
using BellumCivile.Behaviors;
using HarmonyLib;

internal static class RealmUnionClientTests
{
    internal static void Run(Action<bool, string> check)
    {
        RealmUnionClientProjectionTests.Run(check);
        var prepare = AccessTools.Method(typeof(RealmUnionRecord).Assembly.GetType("BellumCivile.RealmUnionClientAdapter"), "TryPrepare");
        var first = new ClientKingdomRecord("first", "source", 25, false, 90);
        var second = new ClientKingdomRecord("second", "source", 40, true, 120);
        var existing = new ClientKingdomRecord("existing", "destination", 10, true, 30);
        var unrelated = new ClientKingdomRecord("other", "third", 15, false, 60);
        var original = new List<ClientKingdomRecord> { first, second, existing, unrelated };
        var snapshotType = typeof(RealmUnionRecord).Assembly.GetType("BellumCivile.RealmUnionClientSnapshot");
        var capture = AccessTools.Method(snapshotType, "TryCapture");
        var matches = AccessTools.Method(snapshotType, "Matches");
        var captureArgs = new object[] { original, "source", null, null, null };
        check((bool)capture.Invoke(null, captureArgs), "Client history snapshot captures valid records");
        var copies = (List<ClientKingdomRecord>)captureArgs[2];
        var entries = (Dictionary<string, string>)captureArgs[3];
        check(copies.Count == 2 && entries.Count == 8 && !ReferenceEquals(copies[0], first),
            "Client snapshot deep copies only the participant's clients with all four history fields");
        check((bool)matches.Invoke(null, new object[] { copies, "source", entries }),
            "Structured client history matches its obligation fingerprint");
        check(!(bool)matches.Invoke(null, new object[] { original, "source", entries }),
            "Structured participant snapshot cannot silently include another overlord's clients");
        foreach (string key in new[] { "client:first", "client-start:first", "client-voluntary:first", "client-cooldown:first" })
        {
            var changed = new Dictionary<string, string>(entries) { [key] = "changed" };
            check(!(bool)matches.Invoke(null, new object[] { copies, "source", changed }),
                "Client snapshot detects changed " + key);
        }
        check(!(bool)matches.Invoke(null, new object[] { null, "source", entries }),
            "Missing structured client history cannot be reconstructed from current state");
        check((bool)matches.Invoke(null, new object[] { null, null, new Dictionary<string, string>() }),
            "Older journals without client obligations remain valid");
        var invalidCapture = new object[] { new[] { first, first }, "source", null, null, null };
        check(!(bool)capture.Invoke(null, invalidCapture) && invalidCapture[2] == null && invalidCapture[3] == null,
            "Duplicate client history produces no partial snapshot");
        invalidCapture = new object[] { new[] { new ClientKingdomRecord("bad", "source", 0, false, float.NaN) }, "source", null, null, null };
        check(!(bool)capture.Invoke(null, invalidCapture), "Nonfinite cooldown is rejected during snapshot capture");
        List<ClientKingdomRecord> result = null;
        string blocked = null;
        bool Prepare()
        {
            var args = new object[] { original, "source", "destination", (Func<string, bool>)(id => id == blocked), null, null };
            bool ok = (bool)prepare.Invoke(null, args);
            result = (List<ClientKingdomRecord>)args[4];
            check(ok || result == null && !string.IsNullOrEmpty(args[5] as string), "Clientage planning failure leaves no partial replacement");
            return ok;
        }
        check(Prepare() && result.Count == 4, "Absorption plans all source clients together");
        for (int i = 0; i < 2; i++)
            check(result[i].SuzerainKingdomId == "destination" && result[i].ClientKingdomId == original[i].ClientKingdomId
                && result[i].StartedDay == original[i].StartedDay && result[i].WasVoluntary == original[i].WasVoluntary
                && result[i].LiberationCooldownUntilDay == original[i].LiberationCooldownUntilDay,
                "Client inheritance changes only suzerain, retaining start, status and liberation cooldown");
        check(first.SuzerainKingdomId == "source" && second.SuzerainKingdomId == "source",
            "Original clientage records are unchanged by projection");
        check(ReferenceEquals(result[2], existing) && ReferenceEquals(result[3], unrelated),
            "Destination and unrelated client records remain untouched");
        blocked = "second";
        check(!Prepare() && first.SuzerainKingdomId == "source", "An incompatible second client prevents partial first-client transfer");
        blocked = null;
        original.Add(new ClientKingdomRecord("first", "destination", 0, true, 1));
        check(!Prepare(), "Duplicate clientage cannot be silently reassigned");
        original.RemoveAt(original.Count - 1);
        original.Add(new ClientKingdomRecord("source", "third", 0, true, 1));
        check(!Prepare(), "Absorbed realm cannot itself be a client in initial union scope");
        original.RemoveAt(original.Count - 1);
        original.Add(new ClientKingdomRecord("destination", "third", 0, true, 1));
        check(!Prepare(), "Surviving realm cannot be a subordinate overlord");
        original.RemoveAt(original.Count - 1);
        original.Add(new ClientKingdomRecord("nested", "first", 0, true, 1));
        check(!Prepare(), "Nested client hierarchy defers rather than creating unsupported chains");
        original.RemoveAt(original.Count - 1);
        original.Add(new ClientKingdomRecord("invalid", "source", float.NaN, true, 1));
        check(!Prepare(), "Invalid clientage history cannot enter inherited records");
        original.RemoveAt(original.Count - 1);
        var behavior = new ClientKingdomBehavior();
        var field = AccessTools.Field(typeof(ClientKingdomBehavior), "_clients");
        field.SetValue(behavior, original);
        int callbacks = 0;
        var arguments = new object[] { null, null, (Action)(() => callbacks++), (Action)(() => callbacks++), null };
        check(!(bool)AccessTools.Method(typeof(ClientKingdomBehavior), "TryInheritRealmUnionClients").Invoke(behavior, arguments)
            && callbacks == 0 && ReferenceEquals(field.GetValue(behavior), original),
            "Native client adapter rejects missing realms without callbacks or registry writes");
    }
}
