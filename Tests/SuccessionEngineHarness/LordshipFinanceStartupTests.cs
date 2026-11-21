using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using BellumCivile;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Party.PartyComponents;
using TaleWorlds.Core;

internal static class LordshipFinanceStartupTests
{
    private static readonly Type Installer = typeof(SubModule).Assembly.GetType("BellumCivile.Patches.LordshipFinancePatches", true);
    private static readonly List<string> Logs = new List<string>();
    private static int _skimCalls;
    private static bool _hasServiceSupport;
    private static bool Log(string __0) { Logs.Add(__0); return false; }
    private static T Blank<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    private static void Set(object instance, string field, object value) => AccessTools.Field(instance.GetType(), field).SetValue(instance, value);
    private static bool ServiceSupport(ref bool __result) { __result = _hasServiceSupport; return false; }
    private static bool RbmSkim(bool applyWithdrawals, ref int __result)
    {
        if (applyWithdrawals) _skimCalls++;
        __result = 1000;
        return false;
    }

    // This probe needs its own process so the native type has not already been initialized.
    internal static void Run(Action<bool, string> check)
    {
        check(Game.Current == null, "Finance startup begins without a current game");
        var initialize = AccessTools.Method(Installer, "Initialize");
        bool Installed(string field) => (bool)AccessTools.Field(Installer, field).GetValue(null);
        foreach (string name in new[] { "LordshipVillageIncomePatch", "LordshipSurplusPatch" })
        {
            var patch = typeof(SubModule).Assembly.GetType("BellumCivile.Patches." + name, true);
            check((bool)AccessTools.Method(typeof(SubModule), "ShouldSkipOptionalPatch").Invoke(null, new object[] { patch }),
                "Module startup excludes finance targets before native game initialization: " + name);
        }
        initialize.Invoke(null, null);
        check(!Installed("_villagePatched") && !Installed("_surplusPatched"), "An early finance initialization request leaves both patches deferred");
        var game = (Game)FormatterServices.GetUninitializedObject(typeof(Game));
        var current = AccessTools.Field(typeof(Game), "_current");
        var oldTexts = AccessTools.Field(typeof(GameTexts), "_gameTextManager").GetValue(null);
        var harmony = new Harmony("bellum.tests.finance_startup");
        var rbm = new Harmony("com.rbmcampaign");
        var lordshipInstance = AccessTools.PropertySetter(typeof(LordshipBehavior), "Instance");
        var oldLordship = LordshipBehavior.Instance;
        try
        {
            current.SetValue(null, game);
            initialize.Invoke(null, null);
            check(!Installed("_villagePatched") && !Installed("_surplusPatched"), "A current game without its text manager still defers finance patches");
            var texts = new GameTextManager();
            AccessTools.Field(typeof(Game), "<GameTextManager>k__BackingField").SetValue(game, texts);
            GameTexts.Initialize(texts);
            // Force the eager initialization that exposes the Proton failure, now at a safe point.
            RuntimeHelpers.RunClassConstructor(typeof(DefaultClanFinanceModel).TypeHandle);
            check(AccessTools.Field(typeof(DefaultClanFinanceModel), "_projectsIncomeText").GetValue(null) != null,
                "Native finance static initialization succeeds after deferred startup, even when forced eagerly");
            var remittance = AccessTools.Method(typeof(DefaultClanFinanceModel), "AddIncomeFromParty");
            rbm.Patch(remittance, prefix: new HarmonyMethod(typeof(LordshipFinanceStartupTests), nameof(RbmSkim)));
            initialize.Invoke(null, null);
            initialize.Invoke(null, null);
            check(Installed("_villagePatched") && Installed("_surplusPatched"), "Both finance patches install once game texts are available");
            var village = typeof(SubModule).Assembly.GetType("BellumCivile.Patches.LordshipVillageIncomePatch", true);
            var surplus = typeof(SubModule).Assembly.GetType("BellumCivile.Patches.LordshipSurplusPatch", true);
            check(Harmony.GetPatchInfo(AccessTools.Method(typeof(DefaultClanFinanceModel), "CalculateVillageIncome"))
                .Postfixes.Count(p => p.PatchMethod.DeclaringType == village) == 1,
                "Repeated campaign/session initialization installs the village observer exactly once");
            check(Harmony.GetPatchInfo(remittance)
                .Prefixes.Count(p => p.PatchMethod.DeclaringType == surplus) == 1,
                "Repeated campaign/session initialization installs the surplus guard exactly once");

            var registry = new LordshipBehavior();
            Set(registry, "_ready", true);
            Set(registry, "_campaign", Campaign.Current);
            lordshipInstance.Invoke(null, new object[] { registry });
            harmony.Patch(AccessTools.Method(typeof(LordshipBehavior), "HasServiceSupport"),
                prefix: new HarmonyMethod(typeof(LordshipFinanceStartupTests), nameof(ServiceSupport)));
            var clan = Blank<Clan>();
            Set(clan, "_leader", Blank<Hero>());
            var party = Blank<MobileParty>();
            var component = Blank<LordPartyComponent>();
            Set(component, "_leader", Blank<Hero>());
            Set(party, "_partyComponent", component);
            AccessTools.PropertySetter(typeof(MobileParty), "IsLordParty").Invoke(party, new object[] { true });
            var finance = new DefaultClanFinanceModel();
            int Skim(bool apply) => (int)remittance.Invoke(finance, new object[] { party, clan, default(ExplainedNumber), apply });
            _skimCalls = 0;
            _hasServiceSupport = true;
            check(Skim(false) == 0 && Skim(true) == 0 && _skimCalls == 0,
                "Bellum blocks an earlier-installed RBM-style skim and its withdrawal side effect for supported retainers");
            _hasServiceSupport = false;
            check(Skim(false) == 1000 && Skim(true) == 1000 && _skimCalls == 1,
                "An ordinary clan party still uses RBM-style preview and withdrawal");

            foreach (var method in new[] { AccessTools.Method(typeof(SubModule), "OnGameStart"),
                AccessTools.Method(typeof(BellumCivile.Behaviors.LordshipBehavior), "OnLoaded") })
                check(PatchProcessor.GetOriginalInstructions(method).Any(i => i.Calls(initialize)),
                    "Deferred finance registration covers new games and loaded sessions: " + method.Name);

            harmony.Patch(AccessTools.Method(typeof(BellumCivileLogger), "Log"), prefix: new HarmonyMethod(typeof(LordshipFinanceStartupTests), nameof(Log)));
            var args = new object[] { typeof(FailedPatch), false };
            AccessTools.Method(Installer, "Install").Invoke(null, args);
            check(!(bool)args[1], "A failed finance patch is not marked installed and can be retried");
            check(Logs.Any(line => line.Contains("finance startup outer") && line.Contains("finance startup inner") && line.Contains(nameof(FailedPatch))),
                "Finance installation failures retain nested exceptions and patch identity in the log");
            check(Installed("_villagePatched") && Installed("_surplusPatched"), "An independent patch failure does not disable the working finance hooks");
        }
        finally
        {
            rbm.UnpatchAll(rbm.Id);
            harmony.UnpatchAll(harmony.Id);
            lordshipInstance.Invoke(null, new object[] { oldLordship });
            foreach (string name in new[] { "LordshipVillageIncomePatch", "LordshipSurplusPatch", nameof(FailedPatch) })
            {
                string owner = "com.bellumcivile.lordship.finance." + name;
                new Harmony(owner).UnpatchAll(owner);
            }
            AccessTools.Field(Installer, "_villagePatched").SetValue(null, false);
            AccessTools.Field(Installer, "_surplusPatched").SetValue(null, false);
            current.SetValue(null, null);
            AccessTools.Field(typeof(GameTexts), "_gameTextManager").SetValue(null, oldTexts);
            Logs.Clear();
        }
    }

    [HarmonyPatch]
    private static class FailedPatch
    {
        private static bool Prepare() => throw new InvalidOperationException("finance startup outer", new Exception("finance startup inner"));
    }
}
