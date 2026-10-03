using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Xml;
using BellumCivile;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Library;
using TaleWorlds.Localization;

internal static class DiplomacyOverviewTests
{
    private static T Empty<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    private static bool RealmName(Kingdom __instance, ref TextObject __result)
    { __result = new TextObject(__instance.StringId); return false; }

    internal static void Run(Action<bool, string> check, string game)
    {
        var assembly = typeof(HostagePactRecord).Assembly;
        var patchType = assembly.GetType("BellumCivile.ViewModelMixin.KingdomDiplomacyOverviewPrefabPatch");
        var patch = Activator.CreateInstance(patchType, true);
        void Apply(XmlDocument document) => AccessTools.Method(patchType, "Apply").Invoke(patch, new object[] { document });
        void CheckLayout(string file, bool diplomacy)
        {
            var document = new XmlDocument(); document.Load(file);
            var commandsBefore = document.SelectNodes("//*[@Command.Click]").Cast<XmlElement>()
                .Select(n => n.GetAttribute("Command.Click"))
                .Where(c => c != "ExecuteShowOverview" && c != "ExecuteShowStats").ToArray();
            Apply(document);
            check(document.SelectNodes("//BellumDiplomacyOverview").Count == 1, "Exactly one overview: " + file);
            check(document.SelectNodes("//BellumDiplomacyOverviewButtons").Count == 1, "Exactly one tab pair: " + file);
            var commandsAfter = document.SelectNodes("//*[@Command.Click]").Cast<XmlElement>()
                .Select(n => n.GetAttribute("Command.Click")).ToArray();
            check(commandsBefore.SequenceEqual(commandsAfter), "Existing diplomacy actions remain unchanged: " + file);
            if (diplomacy)
            {
                check(document.SelectNodes("//OverviewTab").Count == 0 && document.SelectNodes("//StatsTab").Count == 1,
                    "Diplomacy overview replaced without removing its statistics");
                check(document.SelectNodes("//*[@Id='ScrollContainer']").Count == 0,
                    "Diplomacy no longer nests statistics in a second fixed-height scroll panel");
                check(document.SelectNodes("//DiplomacyPanelButtons").Count == 1, "Diplomacy peace controls preserved");
            }
            else
            {
                check(document.SelectSingleNode("//Widget[@IsVisible='@BellumStatsSelected']/Children/Widget[@IsVisible='@IsDisplayingWarLogs']") != null,
                    "Native war log remains inside Stats mode");
                check(document.SelectSingleNode("//*[@Id='StatTypes' and @IsVisible='@BellumStatsControlsVisible']") != null,
                    "Native statistics toggle cannot overlay the overview");
            }
            string once = document.OuterXml;
            Apply(document);
            check(document.OuterXml == once, "Overview layout patch is idempotent: " + file);
        }
        CheckLayout(Path.Combine(game, "Modules/SandBox/GUI/Prefabs/KingdomManagement/Diplomacy/DiplomacyPanel.xml"), false);
        string diplomacyDirectory = Path.Combine(game, "Modules/Decompiled mods/Bannerlord.Diplomacy-dev/src/Bannerlord.Diplomacy/_Module/GUI/Prefabs/KingdomManagement/Diplomacy");
        if (Directory.Exists(diplomacyDirectory))
        {
            CheckLayout(Path.Combine(diplomacyDirectory, "DiplomacyPanelCustom.xml"), true);
            var stats = new XmlDocument(); stats.Load(Path.Combine(diplomacyDirectory, "StatsTab.xml"));
            var type = assembly.GetType("BellumCivile.ViewModelMixin.DiplomacyOverviewStatsVisibilityPatch");
            AccessTools.Method(type, "Apply").Invoke(Activator.CreateInstance(type, true), new object[] { stats });
            check(stats.SelectSingleNode("/Prefab/Window/Widget[@IsVisible='@BellumStatsSelected' and @HeightSizePolicy='StretchToParent']") != null,
                "Diplomacy stats use Bellum selection and a bounded scrollable viewport");
        }
        else Console.WriteLine("SKIP: optional Diplomacy source XML fixtures not installed");

        var replaced = new XmlDocument(); replaced.LoadXml("<Prefab><Window><DiplomacyPanelCustom /></Window></Prefab>");
        var unchanged = replaced.OuterXml; Apply(replaced);
        check(replaced.OuterXml == unchanged, "Native patch safely defers when Diplomacy has already replaced its panel");

        string root = FindRoot();
        var overview = new XmlDocument(); overview.Load(Path.Combine(root, "GUI/Prefabs/KingdomManagement/Diplomacy/BellumDiplomacyOverview.xml"));
        check(overview.SelectNodes("//ScrollablePanel").Count == 1 && overview.SelectSingleNode("//ScrollbarWidget[@SuggestedWidth='8']") != null,
            "All overview sections share one thin scrollbar");
        check(overview.SelectNodes("//GridWidget[@ColumnCount='4']").Count == 2,
            "Both realm columns wrap banners in bounded grids");
        check(overview.SelectSingleNode("//ListPanel[@HeightSizePolicy='Fixed' and @SuggestedHeight='@BodyHeight']") != null,
            "Overview rows have bounded heights so the stretching divider cannot inflate sections");
        check(overview.SelectSingleNode("//ListPanel[@Id='Sections']/ItemTemplate/ListPanel[@MarginBottom='8']") != null,
            "Overview sections retain only a small gap");
        var sectionType = assembly.GetType("BellumCivile.UI.Diplomacy.DiplomacyOverviewSectionVM");
        int Height(int left, int right) => (int)AccessTools.Method(sectionType, "CalculateBodyHeight")
            .Invoke(null, new object[] { left, right });
        check(Height(0, 0) == 32, "Empty relationship sections keep a compact divider");
        check(Height(1, 1) == 132 && Height(4, 0) == 132, "One banner row uses one cell height");
        check(Height(5, 1) == 264 && Height(1, 9) == 396,
            "Relationship row height follows the taller realm column");
        check(overview.SelectSingleNode("/Prefab/Window/Widget[@HeightSizePolicy='StretchToParent' and @MarginBottom='110']") != null,
            "Overview reserves room for diplomatic action buttons");

        var first = Empty<Kingdom>(); first.StringId = "first";
        var second = Empty<Kingdom>(); second.StringId = "second";
        var outsider = Empty<Kingdom>(); outsider.StringId = "outsider";
        var clientBehavior = new ClientKingdomBehavior();
        var clientRecords = new System.Collections.Generic.List<ClientKingdomRecord> {
            new ClientKingdomRecord("second", "first", 0, false, 0)
        };
        AccessTools.Field(typeof(ClientKingdomBehavior), "_clients").SetValue(clientBehavior, clientRecords);
        check(clientBehavior.IsProtectedClientPair(first, second)
            && clientBehavior.IsProtectedClientPair(second, first),
            "Overview alliance exclusion recognizes clientage from both realm perspectives");
        check(!clientBehavior.IsProtectedClientPair(first, outsider)
            && !clientBehavior.IsProtectedClientPair(second, outsider),
            "Overview clientage filter leaves unrelated alliances visible");
        clientRecords.Clear();
        check(!clientBehavior.IsProtectedClientPair(first, second),
            "Former clientage no longer excludes a surviving ordinary alliance");
        var pact = new HostagePactRecord { FirstRealm = first, SecondRealm = second, EndDay = 100, Phase = HostagePactPhase.Active };
        var isCurrent = AccessTools.Method(typeof(HostagePactBehavior), "IsCurrentAgreement");
        bool Current(Kingdom realm, double day) => (bool)isCurrent.Invoke(null, new object[] { pact, realm, day });
        check(Current(first, 99) && Current(second, 99), "Active pact is visible from either realm");
        check(!Current(outsider, 99) && !Current(null, 99), "Unrelated realms cannot list a pact");
        check(!Current(first, 100) && !Current(first, double.NaN) && !Current(first, double.NegativeInfinity),
            "Expired and invalid-date pacts do not appear active");
        foreach (var phase in new[] { HostagePactPhase.Preparing, HostagePactPhase.Resolving, HostagePactPhase.Ended })
        {
            pact.Phase = phase;
            check(!Current(first, 50), "Overview excludes " + phase + " hostage agreements");
        }

        var harmony = new Harmony("bellum.tests.overview");
        try
        {
            foreach (var property in new[] { "Name", "EncyclopediaTitle" })
                harmony.Patch(AccessTools.PropertyGetter(typeof(Kingdom), property), prefix: new HarmonyMethod(typeof(DiplomacyOverviewTests), "RealmName"));
            var war = new WarScoreRecord("first|second", "first", "second", 0, null);
            AccessTools.Field(typeof(WarScoreRecord), "_score").SetValue(war, 18f);
            AccessTools.Field(typeof(WarScoreRecord), "_battleScore").SetValue(war, 12f);
            AccessTools.Field(typeof(WarScoreRecord), "_raidScore").SetValue(war, 6f);
            var builder = AccessTools.Method(assembly.GetType("BellumCivile.UI.Map.WarScoreTooltip"), "Build");
            string[] Values(Kingdom viewing, Kingdom other) => ((IEnumerable)builder.Invoke(null, new object[] { war, viewing, other }))
                .Cast<object>().Select(row => (string)AccessTools.Property(row.GetType(), "ValueLabel").GetValue(row)).ToArray();
            check(Values(first, second).Contains("+18") && Values(second, first).Contains("-18"),
                "Overview war totals reverse with the column realm, not the player's realm");
            check(Values(first, second).Any(v => v.StartsWith("+12 /")) && Values(second, first).Any(v => v.StartsWith("-12 /")),
                "Shared map and overview war components reverse with viewing realm");
            check(!((IEnumerable)builder.Invoke(null, new object[] { null, first, second })).Cast<object>().Any(),
                "Shared war tooltip handles missing record without fabricated score");
        }
        finally { harmony.UnpatchAll(harmony.Id); }

        // No native campaign is needed to exercise tab selection and finalization.
        var vmType = Type.GetType("TaleWorlds.CampaignSystem.ViewModelCollection.KingdomManagement.Diplomacy.KingdomDiplomacyVM, TaleWorlds.CampaignSystem.ViewModelCollection", true);
        var vm = (ViewModel)FormatterServices.GetUninitializedObject(vmType);
        var mixinType = assembly.GetType("BellumCivile.ViewModelMixin.KingdomDiplomacyOverviewMixin");
        var mixin = Activator.CreateInstance(mixinType, new object[] { vm });
        bool Selected(string name) => (bool)AccessTools.Property(mixinType, name).GetValue(mixin);
        check(Selected("BellumOverviewSelected") && !Selected("BellumStatsSelected"), "Overview is the initial diplomacy view");
        AccessTools.Method(mixinType, "ExecuteBellumStats").Invoke(mixin, null);
        AccessTools.Method(mixinType, "OnRefresh").Invoke(mixin, null);
        check(!Selected("BellumOverviewSelected") && Selected("BellumStatsSelected"), "Refreshing diplomacy preserves selected Stats mode");
        AccessTools.Method(mixinType, "ExecuteBellumOverview").Invoke(mixin, null);
        check(Selected("BellumOverviewSelected") && !Selected("BellumStatsSelected"), "Overview button restores overview mode");
        AccessTools.Method(mixinType, "OnFinalize").Invoke(mixin, null);
        check(true, "Overview finalizes without a campaign or selected kingdom");
    }

    private static string FindRoot()
    {
        foreach (string start in new[] { Environment.CurrentDirectory, AppDomain.CurrentDomain.BaseDirectory })
            for (var dir = new DirectoryInfo(start); dir != null; dir = dir.Parent)
                if (File.Exists(Path.Combine(dir.FullName, "BellumCivile.csproj"))) return dir.FullName;
        throw new InvalidOperationException("Bellum source root not found");
    }
}
