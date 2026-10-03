using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using BellumCivile;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;

internal static class RealmUnionAuditTests
{
    internal static void Run(Action<bool, string> check)
    {
        var same = AccessTools.Method(typeof(RealmUnionRecord).Assembly.GetType("BellumCivile.RealmUnionPrewriteRules"), "SameClan");
        var clan = (Clan)FormatterServices.GetUninitializedObject(typeof(Clan));
        var before = new RealmUnionClanRecord { Clan = clan, Influence = 100, Debt = 20, Color = 10, Color2 = 20,
            Holdings = new List<string> { "town", "castle" } };
        RealmUnionClanRecord Fresh() => new RealmUnionClanRecord { Clan = clan, Influence = 100, Debt = 20, Color = 10, Color2 = 20,
            Holdings = new List<string> { "castle", "town" } };
        var current = Fresh();
        bool Same() => (bool)same.Invoke(null, new object[] { before, current });
        check(Same(), "Prewrite comparison accepts reordered but unchanged holdings");
        foreach (var change in new Action<RealmUnionClanRecord>[] {
            r => r.Influence++, r => r.Debt++, r => r.Color++, r => r.Color2++,
            r => r.EndMercenaryContract = true, r => r.Holdings.RemoveAt(0),
            r => r.Holdings.Add("town"), r => r.Clan = null, r => r.Influence = float.NaN })
        {
            current = Fresh(); change(current);
            check(!Same(), "Changed source clan snapshot blocks the first diplomatic write");
        }
        var started = AccessTools.Method(typeof(CrownAccessionBehavior), "HasStartedRealmUnion");
        foreach (string field in new[] { "CrownTransferReturned", "CrownVerified", "ClientTransferReturned", "TradeTransferReturned",
            "AllianceTransferReturned", "LegacyTributeTransferReturned", "RetirementReturned", "SourceRetired" })
        {
            var journal = new RealmUnionRecord();
            AccessTools.Field(typeof(RealmUnionRecord), field).SetValue(journal, true);
            check((bool)started.Invoke(null, new object[] { journal }), "Return evidence retains protection even without start flag: " + field);
        }
        foreach (string field in new[] { "ActionReturned", "ActionCompleted", "RestorationStarted", "RestorationReturned",
            "RestorationCompleted", "PostMoveCaptured" })
        {
            var receipt = new RealmUnionClanRecord();
            AccessTools.Field(typeof(RealmUnionClanRecord), field).SetValue(receipt, true);
            check((bool)started.Invoke(null, new object[] { new RealmUnionRecord { Clans = new List<RealmUnionClanRecord> { receipt } } }),
                "Clan write evidence cannot be discarded as an unstarted draft: " + field);
        }
        check((bool)started.Invoke(null, new object[] { new RealmUnionRecord { TributeTransfersReturned = new List<string> { "ally" } } }),
            "Returned tribute receipt retains union protection");
        CheckLocalization(check);
    }

    private static void CheckLocalization(Action<bool, string> check)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "BellumCivile.csproj"))) root = root.Parent;
        check(root != null, "Union localization audit locates the repository");
        var entries = XDocument.Load(Path.Combine(root.FullName, "ModuleData", "Languages", "EN", "strings.xml"))
            .Descendants("string").ToList();
        var catalog = entries.GroupBy(e => (string)e.Attribute("id")).ToDictionary(g => g.Key, g => g.ToList());
        var audited = new HashSet<string>();
        var pattern = new Regex(@"\{=(BC_[^}]+)\}([^""\r\n]*)");
        foreach (string file in Directory.GetFiles(Path.Combine(root.FullName, "Behaviors"), "*.cs")
            .Where(p => Path.GetFileName(p).StartsWith("Crown", StringComparison.Ordinal)
                || Path.GetFileName(p).StartsWith("Partition", StringComparison.Ordinal)))
        {
            foreach (Match match in pattern.Matches(File.ReadAllText(file)))
            {
                string id = match.Groups[1].Value;
                if (audited.Add(id)) check(catalog.ContainsKey(id) && catalog[id].Count == 1, "Succession localization has one catalog entry: " + id);
                if (id == "BC_RealmUnion_Inherited" || id == "BC_PartitionSuccession_SovereignRealmFounded"
                    || id == "BC_AbdicationUnionPending")
                    check((string)catalog[id][0].Attribute("text") == match.Groups[2].Value,
                        "Union/split catalog preserves exact default text and placeholders: " + id);
            }
        }
    }
}
