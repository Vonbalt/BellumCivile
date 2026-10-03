using System;
using System.Reflection;
using System.Runtime.Serialization;
using BellumCivile;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;

internal static class FeudMercenaryTests
{
    private static Clan _player;
    private static Kingdom _realm;
    private static Hero _hero;
    private static bool _mercenary;
    private static bool Player(ref Clan __result) { __result = _player; return false; }
    private static bool Realm(ref Kingdom __result) { __result = _realm; return false; }
    private static bool Leader(ref Hero __result) { __result = _hero; return false; }
    private static bool Mercenary(ref bool __result) { __result = _mercenary; return false; }
    private static bool Minor(ref bool __result) { __result = true; return false; }
    private static bool False(ref bool __result) { __result = false; return false; }
    private static bool NoRuler(ref Clan __result) { __result = null; return false; }
    internal static void Run(Action<bool, string> check)
    {
        var h = new Harmony("bellum.test.feud_mercenary");
        void Patch(MethodBase method, string name) => h.Patch(method, prefix: new HarmonyMethod(typeof(FeudMercenaryTests), name));
        Clan ClanOf(string id) { var c = (Clan)FormatterServices.GetUninitializedObject(typeof(Clan)); c.StringId = id; return c; }
        _player = ClanOf("player"); var claimant = ClanOf("claimant"); var holder = ClanOf("holder");
        _realm = (Kingdom)FormatterServices.GetUninitializedObject(typeof(Kingdom)); _realm.StringId = "parent";
        _hero = (Hero)FormatterServices.GetUninitializedObject(typeof(Hero));
        var record = new ClaimFeudRecord("feud", "parent", "claimant", "holder", "title", FeudalClaimStrength.Strong, 100, 1, "claim", "test");
        record.SetState(ClaimFeudState.Agitating);
        var method = AccessTools.Method(typeof(ClaimFeudBehavior), "CanPlayerSupportFeud");
        bool Can() => (bool)method.Invoke(null, new object[] { record, _player, claimant, holder });
        try
        {
            Patch(AccessTools.PropertyGetter(typeof(Clan), "PlayerClan"), nameof(Player));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "Kingdom"), nameof(Realm));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "Leader"), nameof(Leader));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "IsUnderMercenaryService"), nameof(Mercenary));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "IsMinorFaction"), nameof(Minor));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "IsEliminated"), nameof(False));
            Patch(AccessTools.PropertyGetter(typeof(Kingdom), "IsEliminated"), nameof(False));
            Patch(AccessTools.PropertyGetter(typeof(Kingdom), "RulingClan"), nameof(NoRuler));
            Patch(AccessTools.PropertyGetter(typeof(Hero), "IsDead"), nameof(False));
            _mercenary = true;
            check(!Can(), "Player minor clan under mercenary contract cannot answer feud summons");
            _mercenary = false;
            check(Can(), "Settled player vassal retains eligibility despite minor-faction flag");
            _mercenary = true;
            check(!Can(), "Accept and refuse callbacks recheck a newly acquired mercenary contract");
            _mercenary = false; _realm.StringId = "other";
            check(!Can(), "Leaving the feud's realm invalidates an outstanding summons");
            _realm.StringId = "parent"; record.SetState(ClaimFeudState.WarActive);
            check(!Can(), "An outdated preparation summons cannot join an already active feud war");
        }
        finally { h.UnpatchAll(h.Id); }
    }
}
