using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using BellumCivile;
using BellumCivile.Behaviors;
using BellumCivile.Patches;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;

internal static class ClanFinanceDiagnosticTests
{
    private static readonly List<string> Lines = new List<string>();
    private static int _flushes;
    private static bool Log(string message) { Lines.Add(message); return false; }
    private static bool Flush() { _flushes++; return false; }
    private static bool NoCampaign(ref Campaign __result) { __result = null; return false; }

    internal static void Run(Action<bool, string> check)
    {
        T Blank<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
        var clan = Blank<Clan>(); clan.StringId = "bc_partition_estate_diagnostic";
        var hero = Blank<Hero>(); hero.StringId = "diagnostic_heir";
        var harmony = new Harmony("bellum.test.clan_finance_diagnostic");
        void Patch(MethodBase method, string name) => harmony.Patch(method,
            prefix: new HarmonyMethod(typeof(ClanFinanceDiagnosticTests), name));
        Lines.Clear(); _flushes = 0;
        try
        {
            Patch(AccessTools.Method(typeof(BellumCivileLogger), "Log"), nameof(Log));
            Patch(AccessTools.Method(typeof(BellumCivileLogger), "Flush"), nameof(Flush));
            Patch(AccessTools.PropertyGetter(typeof(Campaign), "Current"), nameof(NoCampaign));
            AccessTools.Field(typeof(Clan), "_leader").SetValue(clan, hero);
            ClanFinanceDiagnosticPatch.Prefix(clan);
            check(Lines.Count == 0 && _flushes == 0, "Healthy clans produce no diagnostics or flushes");
            AccessTools.Field(typeof(Clan), "_leader").SetValue(clan, null);
            AccessTools.PropertySetter(typeof(Clan), "IsBanditFaction").Invoke(clan, new object[] { true });
            ClanFinanceDiagnosticPatch.Prefix(clan);
            check(Lines.Count == 0 && _flushes == 0, "Leaderless bandits bypass finance and do not spam diagnostics");
            AccessTools.PropertySetter(typeof(Clan), "IsBanditFaction").Invoke(clan, new object[] { false });
            ClanFinanceDiagnosticPatch.Prefix(clan);
            check(Lines.Any(s => s.Contains("clan=" + clan.StringId)), "Missing leader records clan identity first");
            check(Lines.Any(s => s.Contains("members: unavailable=")) && Lines.Any(s => s.Contains("partitions:")),
                "Broken household details do not prevent remaining diagnostic sections");
            check(_flushes == 1 && clan.Leader == null, "Diagnostic flushes immediately without repairing leader");

            var cadet = new CrownAccessionRecord { Cadet = clan, Heir = hero, CadetInitialized = true };
            var share = new CrossClanEstateShare { CadetPlan = cadet, Status = "awaiting cadet preparation" };
            var estate = new CrossClanEstateRecord { Shares = new List<CrossClanEstateShare> { share } };
            var partition = new PartitionSuccessionBehavior();
            AccessTools.Field(typeof(PartitionSuccessionBehavior), "_crossClanEstates").SetValue(partition,
                new List<CrossClanEstateRecord> { estate });
            string description = (string)AccessTools.Method(typeof(ClanFinanceDiagnosticPatch), "DescribeEstates")
                .Invoke(null, new object[] { clan, partition });
            check(description.Contains("awaiting cadet preparation") && description.Contains("initialized=True")
                && description.Contains("diagnostic_heir"), "Diagnostic links leaderless cadet to its pending estate");
            check(!estate.Completed && !share.Completed && !cadet.Completed && clan.Leader == null,
                "Journal inspection does not complete succession or install a leader");

            harmony.CreateClassProcessor(typeof(ClanFinanceDiagnosticPatch)).Patch();
            var native = AccessTools.Method(typeof(ClanVariablesCampaignBehavior), "DailyTickClan");
            bool threw = false;
            try { native.Invoke(new ClanVariablesCampaignBehavior(), new object[] { clan }); }
            catch (TargetInvocationException ex) { threw = ex.InnerException is NullReferenceException; }
            check(threw && _flushes == 2, "Actual native daily tick still throws after diagnostic prefix flushes");
            check(typeof(ClanFinanceDiagnosticPatch).GetMethod("Prefix").ReturnType == typeof(void),
                "Observational prefix cannot skip native execution");
        }
        finally { harmony.UnpatchAll(harmony.Id); }
    }
}
