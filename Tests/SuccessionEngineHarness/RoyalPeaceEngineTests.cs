using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using BellumCivile;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;

internal static class RoyalPeaceEngineTests
{
    private static readonly Dictionary<string, Kingdom> Realms = new Dictionary<string, Kingdom>();
    private static Clan _ruler;
    private static Kingdom _parent;
    private static Kingdom _eliminated;
    private static bool _temporary;
    private static int _settlements;
    private static bool _whitePeace;
    private static Clan _enforcer;
    private static bool Resolve(string kingdomId, ref Kingdom __result)
    { __result = kingdomId != null && Realms.TryGetValue(kingdomId, out var realm) ? realm : null; return false; }
    private static bool Parent(ref Kingdom __result) { __result = _parent; return false; }
    private static bool Ruler(ref Clan __result) { __result = _ruler; return false; }
    private static bool Eliminated(Kingdom __instance, ref bool __result)
    { __result = __instance == _eliminated; return false; }
    private static bool Temporary(ref bool __result) { __result = _temporary; return false; }
    private static bool Settle(ClaimFeudWarRecord war, ClaimFeudWarOutcome outcome, Clan peaceEnforcer)
    {
        _settlements++;
        _whitePeace = outcome == ClaimFeudWarOutcome.WhitePeace;
        _enforcer = peaceEnforcer;
        war.SetActive(false);
        return false;
    }

    internal static void Run(Action<bool, string> check)
    {
        T Blank<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
        var harmony = new Harmony("bellum.test.royal_peace");
        void Patch(System.Reflection.MethodBase method, string name) =>
            harmony.Patch(method, prefix: new HarmonyMethod(typeof(RoyalPeaceEngineTests), name));
        try
        {
            Patch(AccessTools.Method(typeof(ClaimFeudWarBehavior), "ResolveKingdom"), nameof(Resolve));
            Patch(AccessTools.Method(typeof(RealmPeaceEnforcementBehavior), "ResolveKingdomForFeud"), nameof(Parent));
            Patch(AccessTools.PropertyGetter(typeof(Kingdom), "RulingClan"), nameof(Ruler));
            Patch(AccessTools.PropertyGetter(typeof(Kingdom), "IsEliminated"), nameof(Eliminated));
            Patch(AccessTools.Method(typeof(ClaimFeudWarBehavior).Assembly.GetType("BellumCivile.BellumKingdomVisibilityHelper"), "IsTemporaryBellumKingdom"), nameof(Temporary));
            Patch(AccessTools.Method(typeof(ClaimFeudWarBehavior), "ResolveWar"), nameof(Settle));
            Realms.Clear();
            foreach (var id in new[] { "parent", "claimant_shell", "holder_shell" })
            { var realm = Blank<Kingdom>(); realm.StringId = id; Realms.Add(id, realm); }
            _parent = Realms["parent"]; _ruler = Blank<Clan>(); _ruler.StringId = "crown";
            _temporary = false; _eliminated = null; _settlements = 0;
            var feud = new ClaimFeudRecord("feud", "parent", "claimant", "holder", "title", FeudalClaimStrength.Strong, 100, 1, "claim", "test");
            feud.SetState(ClaimFeudState.WarActive);
            var war = new ClaimFeudWarRecord("war", "feud", "parent", "claimant_shell", "holder_shell", "title", "claimant", "holder", "claimant", "holder", "", "", 1);
            var behavior = new ClaimFeudWarBehavior();
            ((List<ClaimFeudWarRecord>)AccessTools.Field(typeof(ClaimFeudWarBehavior), "_wars").GetValue(behavior)).Add(war);
            bool Can(ClaimFeudRecord record, Clan ruler) => behavior.CanEnforceRoyalPeace(record, ruler, out _);
            check(Can(feud, _ruler), "Royal peace validates the parent Crown while feud shells exist");
            check(!Can(null, _ruler) && !Can(feud, null), "Royal peace rejects missing parties");
            check(!Can(feud, Blank<Clan>()), "Royal peace rejects a non-ruler");
            _eliminated = _parent;
            check(!Can(feud, _ruler), "Royal peace rejects a collapsed parent");
            _eliminated = Realms["holder_shell"];
            check(!Can(feud, _ruler), "Royal peace rejects a destroyed feud shell");
            _eliminated = null; _temporary = true;
            check(!Can(feud, _ruler), "Royal peace rejects a temporary parent realm");
            _temporary = false; _parent = Realms["claimant_shell"];
            check(!Can(feud, _ruler), "Royal peace rejects mismatched parent records");
            _parent = Realms["parent"];
            war.QueueResolution(ClaimFeudWarOutcome.ClaimantVictory, "victory already decided");
            check(!Can(feud, _ruler), "Royal peace cannot erase an already pending victory");
            war.ClearPendingResolution();
            var wrong = new ClaimFeudRecord("feud", "parent", "other", "holder", "title", FeudalClaimStrength.Strong, 100, 1, "claim", "test");
            check(!Can(wrong, _ruler), "Royal peace rejects changed feud principals");
            var execute = AccessTools.Method(typeof(ClaimFeudWarBehavior), "TryEnforceRoyalPeace");
            check((bool)execute.Invoke(behavior, new object[] { feud, _ruler, null }), "Royal peace dispatches a valid settlement");
            check(_settlements == 1 && _whitePeace && _enforcer == _ruler, "Royal peace dispatches white peace with the paying Crown identity");
            check(!(bool)execute.Invoke(behavior, new object[] { feud, _ruler, null }) && _settlements == 1,
                "Royal peace cannot settle a completed war twice");
        }
        finally { harmony.UnpatchAll(harmony.Id); Realms.Clear(); }
    }
}
