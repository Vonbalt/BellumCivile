using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;
using BellumCivile;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;

internal static class ClientLiberationEngineTests
{
    private static ClientKingdomBehavior _behavior;
    private static Kingdom _client, _suzerain;
    private static bool _war, _eligible, _cleanupThrows;
    private static int _mode, _cleanups;
    private static readonly Exception Failure = new InvalidOperationException("injected declaration failure");
    private static bool Instance(ref ClientKingdomBehavior __result) { __result = _behavior; return false; }
    private static bool War(ref bool __result) { __result = _war; return false; }
    private static bool Assessment(ref ClientLibertyAssessment __result)
    { __result = new ClientLibertyAssessment { CanAttemptLiberation = _eligible, BlockReason = "fixture blocked" }; return false; }
    private static bool Cleanup()
    { _cleanups++; if (_cleanupThrows) throw Failure; return false; }
    private static bool Resolve(string kingdomId, ref Kingdom __result)
    { __result = kingdomId == "client" ? _client : kingdomId == "suzerain" ? _suzerain : null; return false; }
    private static bool NativeDeclaration()
    {
        if (_mode == 1) return false;
        if (_mode == 2) throw Failure;
        _war = true;
        if (_mode == 3) throw Failure;
        return false;
    }
    internal static void Run(Action<bool, string> check)
    {
        T Blank<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
        var h = new Harmony("bellum.test.client_liberation");
        var type = typeof(ClientKingdomBehavior);
        void Patch(MethodBase method, string prefix) => h.Patch(method, prefix: new HarmonyMethod(typeof(ClientLiberationEngineTests), prefix));
        _behavior = new ClientKingdomBehavior();
        _client = Blank<Kingdom>(); _client.StringId = "client";
        _suzerain = Blank<Kingdom>(); _suzerain.StringId = "suzerain";
        var records = (List<ClientKingdomRecord>)AccessTools.Field(type, "_clients").GetValue(_behavior);
        ClientKingdomRecord Reset()
        {
            records.Clear(); var record = new ClientKingdomRecord("client", "suzerain", 10, false, 94);
            records.Add(record); _war = false; _eligible = true; _cleanupThrows = false; _mode = 0; _cleanups = 0;
            return record;
        }
        try
        {
            Patch(AccessTools.PropertyGetter(type, "Instance"), nameof(Instance));
            Patch(AccessTools.Method(type, "ResolveKingdom"), nameof(Resolve));
            Patch(AccessTools.Method(type, "BuildLibertyAssessment"), nameof(Assessment));
            Patch(AccessTools.Method(type, "EndProtectedAgreements"), nameof(Cleanup));
            Patch(AccessTools.Method(typeof(Kingdom), "IsAtWarWith"), nameof(War));
            var native = AccessTools.Method(typeof(FactionManager), "DeclareWar");
            var finalizer = AccessTools.Method(type.Assembly.GetType("BellumCivile.Patches.ClientLiberationWarCommitPatch"), "Finalizer");
            h.Patch(native, prefix: new HarmonyMethod(typeof(ClientLiberationEngineTests), nameof(NativeDeclaration)), finalizer: new HarmonyMethod(finalizer));
            var original = Reset();
            check(_behavior.CanDeclareWar(_client, _suzerain, out _) && _behavior.GetClientRecord(_client) == original && _cleanups == 0,
                "Liberation eligibility is read-only even when approved");
            _eligible = false;
            check(!_behavior.CanDeclareWar(_client, _suzerain, out _) && _behavior.GetClientRecord(_client) == original,
                "Rejected liberation preserves clientage");
            check(!_behavior.CanDeclareWar(_suzerain, _client, out _), "Suzerain still cannot attack its own client");
            foreach (int mode in new[] { 1, 2, 3, 0 })
            {
                original = Reset(); _mode = mode;
                Exception caught = null;
                try { FactionManager.DeclareWar(_client, _suzerain); } catch (Exception ex) { caught = ex; }
                if (mode == 1 || mode == 2)
                    check(_behavior.GetClientRecord(_client) == original && _cleanups == 0 && !_war,
                        "Veto or failure before hostility preserves agreements and clientage: " + mode);
                else
                    check(_war && _behavior.GetClientRecord(_client) == null && _cleanups == 1,
                        "Verified hostility commits clientage cleanup exactly once: " + mode);
                check((mode == 2 || mode == 3) ? ReferenceEquals(caught, Failure) : caught == null,
                    "Liberation finalizer preserves original native exception: " + mode);
            }
            FactionManager.DeclareWar(_client, _suzerain);
            check(_cleanups == 1, "Duplicate war confirmation does not repeat protected-agreement cleanup");
            original = Reset(); _cleanupThrows = true;
            FactionManager.DeclareWar(_client, _suzerain);
            check(_war && _behavior.GetClientRecord(_client) == original, "Cleanup failure retains the client record for reconciliation");
            _cleanupThrows = false;
            var commit = AccessTools.Method(type, "ConfirmLiberationWar");
            check((bool)commit.Invoke(_behavior, new object[] { _client, _suzerain }) && _behavior.GetClientRecord(_client) == null,
                "Reconciliation can finish a failed post-war cleanup");
            original = Reset();
            check(!(bool)commit.Invoke(_behavior, new object[] { _client, _suzerain }) && _behavior.GetClientRecord(_client) == original,
                "Spurious war event without hostile stance cannot release clientage");
        }
        finally { h.UnpatchAll(h.Id); }
    }
}
