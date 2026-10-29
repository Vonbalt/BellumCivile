using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using BellumCivile;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core.ViewModelCollection.Generic;
using TaleWorlds.Core.ViewModelCollection.Information;
using TaleWorlds.Localization;

internal static class EstateInheritanceTooltipTests
{
    private static T Blank<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    private static bool PersonName(Hero __instance, ref TextObject __result)
    { __result = new TextObject(__instance.StringId); return false; }
    private static bool HouseName(Clan __instance, ref TextObject __result)
    { __result = new TextObject(__instance.StringId); return false; }
    private static bool TitleName(FeudalTitleRecord title, ref string __result)
    { __result = title.Name; return false; }
    private static bool NoSettlement(ref Settlement __result) { __result = null; return false; }

    internal static void Run(Action<bool, string> check)
    {
        var display = typeof(FeudalTitleRecord).Assembly.GetType("BellumCivile.EstateInheritanceDisplay", true);
        var harmony = new Harmony("bellum.tests.estate_inheritance_tooltips");
        var oldTitles = FeudalTitleBehavior.Instance;
        try
        {
            harmony.Patch(AccessTools.PropertyGetter(typeof(Hero), "Name"), prefix: new HarmonyMethod(typeof(EstateInheritanceTooltipTests), nameof(PersonName)));
            harmony.Patch(AccessTools.PropertyGetter(typeof(Clan), "Name"), prefix: new HarmonyMethod(typeof(EstateInheritanceTooltipTests), nameof(HouseName)));
            harmony.Patch(AccessTools.Method(typeof(FeudalTitleRecord).Assembly.GetType("BellumCivile.FeudalTitleDisplayHelper", true), "FormatTitleName", new[] { typeof(FeudalTitleRecord), typeof(Clan) }),
                prefix: new HarmonyMethod(typeof(EstateInheritanceTooltipTests), nameof(TitleName)));
            harmony.Patch(AccessTools.Method(typeof(Settlement), "Find", new[] { typeof(string) }),
                prefix: new HarmonyMethod(typeof(EstateInheritanceTooltipTests), nameof(NoSettlement)));

            var titles = new FeudalTitleBehavior();
            AccessTools.Property(typeof(FeudalTitleBehavior), "Instance").SetValue(null, titles);
            var index = (Dictionary<string, FeudalTitleRecord>)AccessTools.Field(typeof(FeudalTitleBehavior), "_titlesById").GetValue(titles);
            var barony = new FeudalTitleRecord("sargot", "Baronatus Sargot", FeudalTitleType.Barony, "source", "source", "", "castle_sargot", "", 0, 0);
            var duchy = new FeudalTitleRecord("waldochi", "Ducatus Waldochi", FeudalTitleType.Duchy, "source", "source", "", "", "", 0, 0);
            var crown = new FeudalTitleRecord("second_crown", "Regnum Valandorum", FeudalTitleType.Kingdom, "source", "source", "", "", "", 0, 0);
            foreach (var title in new[] { barony, duchy, crown }) index.Add(title.TitleId, title);
            var heir = Blank<Hero>(); heir.StringId = "heir";
            var predecessor = Blank<Hero>(); predecessor.StringId = "Rex Derthert";
            var source = Blank<Clan>(); source.StringId = "Dey Meroc";
            var share = new CrossClanEstateShare { Heir = heir, DomesticOnly = true, Titles = new List<string> { barony.TitleId },
                Fiefs = new List<string> { "castle_sargot" } };
            var record = new CrossClanEstateRecord { Source = source, Deceased = predecessor, PolicyVersion = 1,
                RealmCrownTitleId = "political_crown", Shares = new List<CrossClanEstateShare> { share } };
            List<StringPairItemVM> Entries(bool pending = false)
            {
                var entries = new List<StringPairItemVM>();
                AccessTools.Method(display, "AddHeroShares").Invoke(null, new object[] { entries, heir, record, pending });
                return entries;
            }
            string Hint(StringPairItemVM entry) => ((Func<string>)AccessTools.Field(typeof(BasicTooltipViewModel), "_hintProperty").GetValue(entry.Hint))();
            List<TooltipProperty> ClanRows()
            {
                var rows = new List<TooltipProperty>();
                AccessTools.Method(display, "AddClanShares").Invoke(null, new object[] { rows, record, false });
                return rows;
            }

            var ordinary = Entries().Single();
            check(ordinary.Definition == "Baronatus Sargot" && ordinary.Value == string.Empty,
                "Ordinary inheritance shows only the full title name and deduplicates its fief");
            check(Hint(ordinary) == "Expected from Rex Derthert of Dey Meroc",
                "Ordinary inheritance provenance lives in the hover hint");
            share.DomesticOnly = false;
            var crownEstate = Entries().Single();
            check(crownEstate.Definition == "Baronatus Sargot" && crownEstate.Value == string.Empty,
                "Crown estate tags no longer lengthen the visible title");
            check(Hint(crownEstate) == "Crown estate - Expected from Rex Derthert of Dey Meroc",
                "Crown estate hover matches the requested short explanation");
            check(ClanRows().Single().ValueLabel.Contains("Baronatus Sargot (Crown estate)"),
                "Clan successor tooltip retains its existing asset labels");

            share.RootTitleId = crown.TitleId;
            share.Titles.Insert(0, crown.TitleId); share.Titles.Add(duchy.TitleId); share.Titles.Add("political_crown");
            var projected = Entries();
            check(projected.Count == 3 && projected[0].Definition == "Regnum Valandorum",
                "Political crown stays excluded while the secondary sovereign asset keeps title ordering");
            check(Hint(projected[0]) == "Sovereign succession - Expected from Rex Derthert of Dey Meroc"
                && projected.Skip(1).All(e => Hint(e).StartsWith("Crown estate - Expected from")),
                "Sovereign roots and their accompanying Crown estates keep distinct tooltip contexts");
            check(ClanRows().Single().ValueLabel.Contains("Regnum Valandorum (sovereign succession)"),
                "Clan successor tooltip retains the separate sovereign succession label");

            share.DeliveredTitles.Add(barony.TitleId);
            var pending = Entries(true);
            check(pending.All(e => e.Definition != "Baronatus Sargot")
                && pending.Any(e => e.Definition == "Fief: castle_sargot" && Hint(e) == "Crown estate - Pending from Rex Derthert of Dey Meroc"),
                "A delivered title cannot hide pending physical possession or its pending source");
            share.DeliveredTitles.Add(crown.TitleId); share.SovereignTransfers.Add(crown.TitleId);
            check(Entries(true).Any(e => e.Definition == "Regnum Valandorum" && Hint(e).StartsWith("Sovereign succession - Pending from")),
                "Outstanding sovereign transfer remains visible after legal-title delivery");
            share.DeliveredSovereignTransfers.Add(crown.TitleId);
            check(Entries(true).All(e => e.Definition != "Regnum Valandorum"), "Completed sovereign transfers are omitted from pending entries");
            share.SupersededTitles.Add(duchy.TitleId); share.SupersededFiefs.Add("castle_sargot");
            var unresolved = Entries(true).Single();
            check(unresolved.Definition == "Estate settlement outstanding" && unresolved.Value == string.Empty
                && Hint(unresolved) == "Pending from Rex Derthert of Dey Meroc",
                "Pending settlement fallback remains compact and explains its source on hover");
            share.Completed = true;
            check(Entries(true).Count == 0, "Completed shares do not leave pending inheritance rows");
            share.Completed = false; share.IsEscheat = true;
            check(Entries().Count == 0, "Escheat does not appear as personal expected inheritance");
            share.IsEscheat = false; share.Heir = predecessor;
            check(Entries().Count == 0, "Other beneficiaries cannot leak into the viewed heir's inheritance");

            share.Heir = heir; share.Titles.Clear(); share.DeliveredTitles.Clear(); share.SovereignTransfers.Clear();
            share.Fiefs.Clear(); share.RootTitleId = string.Empty;
            var custom = new FeudalTitleRecord("custom", "Baronatus (Crown estate)", FeudalTitleType.Barony, "source", "source", "", "", "", 0, 0);
            index.Add(custom.TitleId, custom); share.Titles.Add(custom.TitleId);
            check(Entries().Single().Definition == custom.Name,
                "Shortening the display never strips words from an actual custom or localized title name");
            check(share.Titles.SequenceEqual(new[] { custom.TitleId }) && share.Heir == heir && record.Source == source,
                "Building inheritance presentation does not modify saved allocations or family membership");
        }
        finally
        {
            harmony.UnpatchAll(harmony.Id);
            AccessTools.Property(typeof(FeudalTitleBehavior), "Instance").SetValue(null, oldTitles);
        }
    }
}
