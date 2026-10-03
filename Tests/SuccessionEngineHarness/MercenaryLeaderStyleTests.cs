using System;
using System.Runtime.Serialization;
using BellumCivile;
using HarmonyLib;
using TaleWorlds.CampaignSystem;

internal static class MercenaryLeaderStyleTests
{
    private static Clan _clan;
    private static Hero _leader;
    private static bool _minor, _hired, _child, _disabled, _notable, _bandit, _eliminated;
    private static bool _alive = true;
    private static bool Clan(ref Clan __result) { __result = _clan; return false; }
    private static bool Leader(ref Hero __result) { __result = _leader; return false; }
    private static bool Minor(ref bool __result) { __result = _minor; return false; }
    private static bool Hired(ref bool __result) { __result = _hired; return false; }
    private static bool Child(ref bool __result) { __result = _child; return false; }
    private static bool Disabled(ref bool __result) { __result = _disabled; return false; }
    private static bool Notable(ref bool __result) { __result = _notable; return false; }
    private static bool Bandit(ref bool __result) { __result = _bandit; return false; }
    private static bool Eliminated(ref bool __result) { __result = _eliminated; return false; }
    private static bool Alive(ref bool __result) { __result = _alive; return false; }
    internal static void Run(Action<bool, string> check)
    {
        var harmony = new Harmony("bellum.test.mercenary_titles");
        try
        {
            void Patch(Type t, string property, string prefix) => harmony.Patch(AccessTools.PropertyGetter(t, property),
                prefix: new HarmonyMethod(typeof(MercenaryLeaderStyleTests), prefix));
            Patch(typeof(Hero), "Clan", nameof(Clan)); Patch(typeof(Clan), "Leader", nameof(Leader));
            Patch(typeof(Clan), "IsMinorFaction", nameof(Minor)); Patch(typeof(Clan), "IsUnderMercenaryService", nameof(Hired));
            Patch(typeof(Clan), "IsBanditFaction", nameof(Bandit)); Patch(typeof(Clan), "IsEliminated", nameof(Eliminated));
            Patch(typeof(Hero), "IsChild", nameof(Child)); Patch(typeof(Hero), "IsDisabled", nameof(Disabled));
            Patch(typeof(Hero), "IsNotable", nameof(Notable)); Patch(typeof(Hero), "IsAlive", nameof(Alive));
            _clan = (Clan)FormatterServices.GetUninitializedObject(typeof(Clan));
            _leader = (Hero)FormatterServices.GetUninitializedObject(typeof(Hero));
            var method = typeof(FeudalTitleRecord).Assembly.GetType("BellumCivile.FeudalTitleDisplayHelper");
            bool Eligible(Hero hero) => (bool)AccessTools.Method(method, "IsMercenaryLeader").Invoke(null, new object[] { hero });
            foreach (bool minor in new[] { false, true })
            foreach (bool hired in new[] { false, true })
            {
                _minor = minor; _hired = hired;
                check(Eligible(_leader) == (minor || hired), "Minor leaders qualify between contracts; regular clan leaders only while hired");
            }
            check(!Eligible(null), "Null hero cannot receive company title");
            check(!Eligible((Hero)FormatterServices.GetUninitializedObject(typeof(Hero))), "Company title never passes to ordinary members or spouse");
            foreach (string field in new[] { "_child", "_disabled", "_notable", "_bandit", "_eliminated" })
            {
                AccessTools.Field(typeof(MercenaryLeaderStyleTests), field).SetValue(null, true);
                check(!Eligible(_leader), "Mercenary title excludes " + field);
                AccessTools.Field(typeof(MercenaryLeaderStyleTests), field).SetValue(null, false);
            }
            _alive = false; check(!Eligible(_leader), "Dead company leaders remain unstyled");
        }
        finally
        {
            harmony.UnpatchAll(harmony.Id);
            _clan = null; _leader = null; _alive = true;
            _minor = _hired = _child = _disabled = _notable = _bandit = _eliminated = false;
        }
    }
}
