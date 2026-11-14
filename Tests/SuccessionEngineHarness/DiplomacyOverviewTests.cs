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
using TaleWorlds.CampaignSystem.Election;
using TaleWorlds.CampaignSystem.ViewModelCollection.KingdomManagement.Diplomacy;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

internal static class DiplomacyOverviewTests
{
    private static T Empty<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    private static bool RealmName(Kingdom __instance, ref TextObject __result)
    { __result = new TextObject(__instance.StringId); return false; }
    private sealed class PendingActions
    {
        internal KingdomDecision War;
        internal KingdomDecision Peace;
        internal void OpenWar() => Consume(War);
        internal void OpenPeace() => Consume(Peace);
        private static void Consume(KingdomDecision value) { }
    }

    internal static void Run(Action<bool, string> check, string game)
    {
        var assembly = typeof(HostagePactRecord).Assembly;
        DiplomacyActionExecutionTests.Run(check);
        var menu = assembly.GetType("BellumCivile.UI.Diplomacy.BellumDiplomacyActionMenu", true);
        var describeAction = AccessTools.Method(menu, "Describe");
        var actionLabel = AccessTools.Method(menu, "ActionLabel");
        var sameAction = AccessTools.Method(menu, "Matches");
        var buildChoice = AccessTools.Method(menu, "BuildChoice");
        KingdomDiplomacyProposalActionItemVM Action(string explanation, int cost = 0, bool enabled = true,
            string hint = "", Action callback = null) => new KingdomDiplomacyProposalActionItemVM(
                new TextObject("Propose"), new TextObject(explanation), cost, enabled, new TextObject(hint), callback ?? (() => { }));
        object Describe(KingdomDiplomacyProposalActionItemVM value) => describeAction.Invoke(null, new object[] { value });
        string Label(object value) => (string)actionLabel.Invoke(null, new[] { value });
        bool Matches(object first, object second) => (bool)sameAction.Invoke(null, new[] { first, second });
        var warAction = Action("{=PYaUEZzn}Consider declaring war (Low support)", 200, false, "Not enough influence");
        var warSelection = Describe(warAction);
        var warChoice = (InquiryElement)buildChoice.Invoke(null, new object[] { warAction });
        check(Label(warSelection) == "Declare War" && warChoice.Title.Contains("200") && warChoice.Title.Contains("<img")
            && !warChoice.IsEnabled && warChoice.Hint.Contains("Low support") && warChoice.Hint.Contains("Not enough influence"),
            "The action menu retains the native cost, disabled reason and support while using a concise localized label");
        check(Label(Describe(Action("{=MTtrk28a}Consider making an alliance"))) == "Form Alliance"
            && Label(Describe(Action("{=gdUE1h39}Consider a trade agreement"))) == "Sign Trade Agreement"
            && Label(Describe(Action("{=BC_PeaceCouncil_ProposalExplanation}Consider making peace"))) == "Negotiate Peace"
            && Label(Describe(Action("{=BC_DefensivePact_LeaveActionSupport}Leave defensive pact (Good support)"))) == "Leave Defensive Pact",
            "Native and Bellum action identities do not depend on translated display text");
        var custom = Action("A custom diplomatic action");
        check(Label(Describe(custom)) == custom.Explanation && Matches(Describe(custom), Describe(Action("A custom diplomatic action"))),
            "Unrecognized mod actions keep their original text and can be safely matched after refresh");
        var pending = new PendingActions { War = Empty<DeclareWarDecision>(), Peace = Empty<MakePeaceKingdomDecision>() };
        var resolveWar = Describe(Action("{=XgioIHv6}Resolve existing decision", callback: pending.OpenWar));
        var resolvePeace = Describe(Action("{=XgioIHv6}Resolve existing decision", callback: pending.OpenPeace));
        check(Label(resolveWar) == "Resolve War Decision" && Label(resolvePeace) == "Resolve Peace Decision"
            && !Matches(resolveWar, resolvePeace)
            && Matches(resolveWar, Describe(Action("{=XgioIHv6}Resolve existing decision", callback: pending.OpenWar))),
            "Multiple native pending decisions remain distinguishable after actions are rebuilt");
        var unknownResolve = (InquiryElement)buildChoice.Invoke(null, new object[] { Action("{=XgioIHv6}Resolve existing decision") });
        check(!unknownResolve.IsEnabled && unknownResolve.Hint.Contains("cannot be identified safely"),
            "An unidentifiable pending decision is disabled instead of risking the wrong resolution");
        var nativeClosure = typeof(KingdomDiplomacyVM).GetNestedTypes(System.Reflection.BindingFlags.NonPublic)
            .First(type => AccessTools.Field(type, "unresolvedWarDecision") != null);
        var nativeInstance = Activator.CreateInstance(nativeClosure, true);
        var nativeWar = Empty<DeclareWarDecision>();
        var nativeAlliance = Empty<StartAllianceDecision>();
        AccessTools.Field(nativeClosure, "unresolvedWarDecision").SetValue(nativeInstance, nativeWar);
        AccessTools.Field(nativeClosure, "unresolvedAllianceDecision").SetValue(nativeInstance, nativeAlliance);
        Action NativeCallback(string fieldName)
        {
            var method = nativeClosure.GetMethods(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)
                .First(candidate => candidate.ReturnType == typeof(void) && candidate.GetParameters().Length == 0
                    && PatchProcessor.GetOriginalInstructions(candidate).Any(instruction => instruction.operand is System.Reflection.FieldInfo field
                        && field.Name == fieldName));
            return (Action)Delegate.CreateDelegate(typeof(Action), nativeInstance, method);
        }
        var nativeWarAction = Describe(Action("{=XgioIHv6}Resolve existing decision", callback: NativeCallback("unresolvedWarDecision")));
        var nativeAllianceAction = Describe(Action("{=XgioIHv6}Resolve existing decision", callback: NativeCallback("unresolvedAllianceDecision")));
        check(Label(nativeWarAction) == "Resolve War Decision" && Label(nativeAllianceAction) == "Resolve Alliance Decision"
            && !Matches(nativeWarAction, nativeAllianceAction),
            "Actual Bannerlord callbacks identify simultaneous war and alliance decisions separately");
        string repoRoot = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "../../../../../"));
        var strings = new XmlDocument(); strings.Load(Path.Combine(repoRoot, "ModuleData/Languages/EN/strings.xml"));
        var localizedIds = strings.SelectNodes("//string[@id]").Cast<XmlElement>().Select(node => node.GetAttribute("id")).ToArray();
        var actionIds = new[] { "UI/Diplomacy/BellumDiplomacyActionMenu.cs", "UI/Diplomacy/KingdomDiplomacyOverviewMixin.cs",
                "Patches/DefensivePactDiplomacyPatch.cs", "DefensivePactJoinDecision.cs", "Behaviors/DefensivePactBehavior.cs" }
            .SelectMany(file => System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(Path.Combine(repoRoot, file)), @"\{=(?<id>BC_(?:Diplomacy|DefensivePact)_[A-Za-z0-9_]+)\}")
                .Cast<System.Text.RegularExpressions.Match>().Select(match => match.Groups["id"].Value)).Distinct().ToArray();
        check(actionIds.All(id => localizedIds.Count(existing => existing == id) == 1),
            "Diplomacy menu and defensive-pact vote strings have unique English localization entries");
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
        var actionPatchType = assembly.GetType("BellumCivile.ViewModelMixin.DiplomacyActionLayoutPatch", true);
        var actionPatch = Activator.CreateInstance(actionPatchType, true);
        void CheckActions(string file)
        {
            var document = new XmlDocument(); document.Load(file);
            var button = document.SelectSingleNode("//ListPanel[@DataSource='{Actions}']/ItemTemplate/ListPanel/Children/Widget[@Id='KingdomDiplomacyAction']") as XmlElement;
            string width = button.GetAttribute("SuggestedWidth");
            string nativeColumn = document.SelectSingleNode("//ListPanel[@DataSource='{Actions}']/ItemTemplate/ListPanel").OuterXml;
            string[] commands = document.SelectNodes("//*[@Command.Click]").Cast<XmlElement>().Select(n => n.GetAttribute("Command.Click")).ToArray();
            AccessTools.Method(actionPatchType, "Apply").Invoke(actionPatch, new object[] { document });
            var column = (XmlElement)document.SelectSingleNode("//ListPanel[@DataSource='{Actions}']/ItemTemplate/ListPanel");
            check(column.OuterXml == nativeColumn && button.GetAttribute("SuggestedWidth") == width,
                "Native diplomacy action columns remain unchanged when the revamp is off: " + file);
            var native = (XmlElement)document.SelectSingleNode("//*[@Id='BellumNativeDiplomacyActions']");
            var central = (XmlElement)document.SelectSingleNode("//*[@Id='BellumDiplomacyActionButton']");
            check(native?.GetAttribute("IsVisible") == "@BellumNativeDiplomacyActionsVisible"
                && central?.GetAttribute("IsVisible") == "@BellumCentralDiplomacyActionVisible"
                && central?.GetAttribute("Command.Click") == "ExecuteBellumDiplomaticAction",
                "The central button and the native action list use opposite visibility bindings: " + file);
            check(central?.GetAttribute("SuggestedWidth") == "227" && central.GetAttribute("SuggestedHeight") == "40"
                && central.GetAttribute("DoNotPassEventsToChildren") == "true",
                "Central button matches Overview and Stats dimensions and receives clicks over its label: " + file);
            foreach (XmlElement external in document.SelectNodes("//ListPanel[@DataSource='{CurrentSelectedDiplomacyItem}' and (@IsVisible='@IsDirectActionVisible' or @IsVisible='@IsNonAggressionPactVisible') ]"))
                check((external.ParentNode?.ParentNode as XmlElement)?.GetAttribute("IsVisible") == "@BellumDiplomacyExternalButtonsVisible",
                    "Diplomacy's optional action remains intact in a reversible visibility wrapper: " + external.GetAttribute("IsVisible"));
            check(commands.All(command => document.SelectNodes("//*[@Command.Click]").Cast<XmlElement>()
                .Any(n => n.GetAttribute("Command.Click") == command)), "Existing action commands are preserved: " + file);
            string once = document.OuterXml;
            AccessTools.Method(actionPatchType, "Apply").Invoke(actionPatch, new object[] { document });
            check(once == document.OuterXml, "Diplomacy action layout patch is idempotent: " + file);
        }
        CheckActions(Path.Combine(game, "Modules/SandBox/GUI/Prefabs/KingdomManagement/Diplomacy/DiplomacyPanel.xml"));
        var formatType = assembly.GetType("BellumCivile.Patches.DiplomacyActionExplanationPatch", true);
        var format = AccessTools.Method(formatType, "Format");
        string Format(string value, params string[] labels) => (string)format.Invoke(null, new object[] { value, labels });
        check(Format("Consider declaring war (Low support)", "Low support") == "Consider declaring war\n(Low support)"
            && Format("Propose non-aggression pact (Good support)", "Good support") == "Propose non-aggression pact\n(Good support)",
            "Recognized native and Bellum support labels occupy their own line");
        check(Format("Considerer une alliance (Soutien faible)", "Soutien faible") == "Considerer une alliance\n(Soutien faible)"
            && Format("Resolve existing decision (urgent)", "Low support") == "Resolve existing decision (urgent)"
            && Format("A\n(Low support)", "Low support") == "A\n(Low support)",
            "Localized support is handled without changing unrelated parentheticals or duplicating line breaks");
        var peaceType = assembly.GetType("BellumCivile.Patches.PeaceDeclarationDiplomacyHintPatch", true);
        var matchesPeace = AccessTools.Method(peaceType, "HasOriginalExplanation");
        var peaceAction = Empty<KingdomDiplomacyProposalActionItemVM>();
        const string vanillaPeace = "Consider making peace, paying 600 tribute/day for 100 days. (Low support)";
        AccessTools.Field(typeof(KingdomDiplomacyProposalActionItemVM), "_explanationText")
            .SetValue(peaceAction, new TextObject("{=!}" + vanillaPeace));
        peaceAction.Explanation = Format(vanillaPeace, "Low support");
        check(peaceAction.Explanation != vanillaPeace
            && (bool)matchesPeace.Invoke(null, new object[] { peaceAction, vanillaPeace })
            && !(bool)matchesPeace.Invoke(null, new object[] { peaceAction, "Resolve existing decision" }),
            "Peace replacement matches the original native explanation after display formatting without targeting other actions");
        AccessTools.Field(typeof(KingdomDiplomacyProposalActionItemVM), "_nameText")
            .SetValue(peaceAction, new TextObject("{=!}Propose"));
        AccessTools.Method(peaceType, "SetNegotiatedPeaceExplanation")
            .Invoke(null, new object[] { peaceAction, new TextObject("{=!}Low support") });
        string negotiated = peaceAction.Explanation;
        peaceAction.RefreshValues();
        check(negotiated.Contains("Consider making peace") && !negotiated.Contains("tribute")
            && negotiated.Contains("Low support") && peaceAction.Explanation == negotiated,
            "Bellum's negotiated peace explanation replaces and survives refresh of the original tribute text");
        string diplomacyDirectory = Path.Combine(game, "Modules/Decompiled mods/Bannerlord.Diplomacy-dev/src/Bannerlord.Diplomacy/_Module/GUI/Prefabs/KingdomManagement/Diplomacy");
        if (Directory.Exists(diplomacyDirectory))
        {
            CheckLayout(Path.Combine(diplomacyDirectory, "DiplomacyPanelCustom.xml"), true);
            CheckActions(Path.Combine(diplomacyDirectory, "DiplomacyPanelButtons.xml"));
            var external = new XmlDocument(); external.Load(Path.Combine(diplomacyDirectory, "DiplomacyPanelButtons.xml"));
            AccessTools.Method(actionPatchType, "Apply").Invoke(actionPatch, new object[] { external });
            check(external.SelectNodes("//Widget[@Id='BellumDiplomacyExternalAction']").Count == 2,
                "Both Diplomacy-specific action panels use the compatibility visibility binding");
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
            foreach (var property in new[] { "Name", "FormalName" })
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
