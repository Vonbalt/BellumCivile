using System;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using BellumCivile;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;

internal static class CourtProtectionClientageTests
{
    private static float _day;
    private static bool _throwAlignment;
    private static bool True(ref bool __result) { __result = true; return false; }
    private static bool False(ref bool __result) { __result = false; return false; }
    private static bool Skip() => false;
    private static bool Today(ref float __result) { __result = _day; return false; }
    private static bool Alignment() { if (_throwAlignment) throw new InvalidOperationException("injected alignment failure"); return false; }
    internal static void Run(Action<bool, string> check)
    {
        T Blank<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
        var client = Blank<Kingdom>(); client.StringId = "protection_client";
        var protector = Blank<Kingdom>(); protector.StringId = "protection_protector";
        var threat = Blank<Kingdom>(); threat.StringId = "protection_threat";
        var behavior = new ClientKingdomBehavior();
        var complete = AccessTools.Method(typeof(ClientKingdomBehavior), "CompleteProtectionClientage");
        var p = new CourtProtectionRecord { Client = client, Protector = protector, Threat = threat, EstablishmentAttempted = true };
        var harmony = new Harmony("bellum.test.protection_clientage");
        void Patch(MethodBase original, string prefix) => harmony.Patch(original, prefix: new HarmonyMethod(typeof(CourtProtectionClientageTests), prefix));
        try
        {
            Patch(AccessTools.Method(typeof(Kingdom), "IsAtWarWith"), nameof(True));
            Patch(AccessTools.PropertyGetter(typeof(ClientKingdomBehavior), "CurrentDay"), nameof(Today));
            Patch(AccessTools.Method(typeof(ClientKingdomBehavior), "CanEstablishClientKingdom"), nameof(True));
            Patch(AccessTools.Method(typeof(ClientKingdomBehavior), "EnsureProtectedAgreements"), nameof(Skip));
            Patch(AccessTools.Method(typeof(ClientKingdomBehavior), "EndThirdPartyAgreements"), nameof(Skip));
            Patch(AccessTools.Method(typeof(ClientKingdomBehavior), "AlignClientDiplomacyOnEstablishment"), nameof(Alignment));
            Patch(AccessTools.PropertyGetter(typeof(CourtAgendaRecord).Assembly.GetType("BellumCivile.ModIntegrationHelper"), "IsDiplomacyLoaded"), nameof(False));
            _day = 10; _throwAlignment = true;
            bool interrupted = false;
            try { complete.Invoke(behavior, new object[] { p }); } catch (TargetInvocationException) { interrupted = true; }
            var original = behavior.GetClientRecords().Single();
            float cooldown = original.LiberationCooldownUntilDay;
            check(interrupted && original.WasVoluntary && original.StartedDay == 10 && !behavior.IsSynchronizingDiplomacy,
                "Native clientage failure preserves its record and always releases recursive diplomacy guard");
            _day = 20; _throwAlignment = false;
            complete.Invoke(behavior, new object[] { p });
            complete.Invoke(behavior, new object[] { p });
            check(behavior.GetClientRecords().Count == 1 && behavior.GetClientRecords()[0] == original
                && original.StartedDay == 10 && original.LiberationCooldownUntilDay == cooldown,
                "Resuming native protection clientage neither duplicates records nor restarts liberation cooldown");
            p.Protector = Blank<Kingdom>(); p.Protector.StringId = "other_protector";
            bool rejected = false;
            try { complete.Invoke(behavior, new object[] { p }); } catch (TargetInvocationException) { rejected = true; }
            check(rejected && original.SuzerainKingdomId == protector.StringId, "Recovery cannot take a client away from a different protector");
        }
        finally { harmony.UnpatchAll(harmony.Id); }
    }
}
