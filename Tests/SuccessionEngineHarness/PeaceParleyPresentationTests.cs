using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Xml;
using System.IO;
using BellumCivile;
using BellumCivile.Behaviors;
using BellumCivile.UI.Parley;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

internal static class PeaceParleyPresentationTests
{
    private static Clan _ruler, _player;
    private static Hero _leader;
    private static float _influence;
    private static int _begun, _ended;
    private static bool Ruler(ref Clan __result) { __result = _ruler; return false; }
    private static bool Player(ref Clan __result) { __result = _player; return false; }
    private static bool Leader(ref Hero __result) { __result = _leader; return false; }
    private static bool Name(ref TextObject __result) { __result = new TextObject("King Derthert"); return false; }
    private static bool RealmName(ref TextObject __result) { __result = new TextObject("Vlandia"); return false; }
    private static bool ClanName(ref TextObject __result) { __result = new TextObject("dey Meroc"); return false; }
    private static bool NoCharacter(ref CharacterObject __result) { __result = null; return false; }
    private static bool NoBanner(ref Banner __result) { __result = null; return false; }
    private static bool Begin() { _begun++; return false; }
    private static bool End() { _ended++; return false; }
    private static bool Budget(Clan clan, float cost, MethodBase __originalMethod, ref object __result)
    {
        __result = Activator.CreateInstance(((MethodInfo)__originalMethod).ReturnType,
            clan, _influence, cost, 300f);
        return false;
    }
    private static T Blank<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));

    internal static void Run(Action<bool, string> check)
    {
        var harmony = new Harmony("bellum.test.parley_presentation");
        var kingdom = Blank<Kingdom>();
        _ruler = Blank<Clan>(); _player = Blank<Clan>(); _leader = Blank<Hero>();
        var assembly = typeof(TreatyCouncilEvaluation).Assembly;
        var budget = assembly.GetType("BellumCivile.NpcInfluenceBudgetService", true);
        var tallyType = assembly.GetType("BellumCivile.PoliticalInfluenceVoteTally", true);
        var tooltipType = Assembly.Load("TaleWorlds.Core.ViewModelCollection")
            .GetType("TaleWorlds.Core.ViewModelCollection.Information.BasicTooltipViewModel", true);
        void Patch(MethodBase method, string name) => harmony.Patch(method,
            prefix: new HarmonyMethod(typeof(PeaceParleyPresentationTests), name));
        TreatyCouncilEvaluation Council(int yay, int nay, TreatyCouncilVoteStance stance, int commitment = 75,
            float utility = 40, bool sole = false, bool blocked = false)
        {
            object tally = Activator.CreateInstance(tallyType, yay, nay, 0);
            return new TreatyCouncilEvaluation(yay, nay, 0,
                (int)AccessTools.Property(tallyType, "RatificationOverrideCost").GetValue(tally),
                (int)AccessTools.Property(tallyType, "RejectionOverrideCost").GetValue(tally),
                new[] { new TreatyCouncilMemberEvaluation(_ruler, 44, _influence, utility, stance, commitment, null) },
                isSoleRulerDecision: sole, soleRulerAccepts: utility >= 10, isBudgetBlocked: blocked);
        }
        TreatyCouncilSummaryVM Summary(TreatyCouncilEvaluation council, bool forced = false)
        {
            bool accepts = new ForeignTreatyBehavior().WouldCouncilAccept(kingdom, council, forced,
                _ruler == _player, out bool usesOverride);
            return new TreatyCouncilSummaryVM(kingdom, council, forced, accepts, usesOverride);
        }
        string[] TooltipRows(object owner, string field)
        {
            object tooltip = AccessTools.Field(owner.GetType(), field).GetValue(owner);
            var callback = (Delegate)AccessTools.Field(tooltipType, "_tooltipProperties").GetValue(tooltip);
            return ((IEnumerable)callback.DynamicInvoke()).Cast<object>().Select(row =>
                AccessTools.Property(row.GetType(), "DefinitionLabel").GetValue(row) + ":"
                + AccessTools.Property(row.GetType(), "ValueLabel").GetValue(row)).ToArray();
        }
        try
        {
            Patch(AccessTools.PropertyGetter(typeof(Kingdom), "RulingClan"), nameof(Ruler));
            Patch(AccessTools.PropertyGetter(typeof(Kingdom), "Name"), nameof(RealmName));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "PlayerClan"), nameof(Player));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "Leader"), nameof(Leader));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "Name"), nameof(ClanName));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "Banner"), nameof(NoBanner));
            Patch(AccessTools.PropertyGetter(typeof(Hero), "Name"), nameof(Name));
            Patch(AccessTools.PropertyGetter(typeof(Hero), "CharacterObject"), nameof(NoCharacter));
            Patch(AccessTools.Method(budget, "Assess"), nameof(Budget));

            _influence = 675;
            var opposed = Council(75, 350, TreatyCouncilVoteStance.Yay);
            var approval = Summary(opposed);
            check(approval.YayInfluence == 75 && approval.TotalInfluence == 425
                && approval.BalanceText == "18% YAY / 82% NAY", "Bar keeps exact vote weights and rounds only its visible percentages");
            check(approval.CouncilText == "The council of Vlandia opposes this treaty, with 350 influence committed NAY against 75 committed YAY."
                && approval.DecisionText == "King Derthert would approve the treaty with 300 additional influence to overrule the council.",
                "Short tooltip matches the agreed wording and real override calculation");
            check(!approval.SummaryText.Contains("%") && !approval.SummaryText.Contains("remaining")
                && approval.RulerOverrideCommitment == 300, "Realm tooltip omits percentages and remaining balances");
            check(TooltipRows(approval, "_tooltip").SequenceEqual(new[] { approval.CouncilText + ":", approval.DecisionText + ":" }),
                "Realm hover contains only the two concise paragraphs");

            var rulerRow = new TreatyCouncilMemberVM(opposed.Members[0], true, true,
                approval.RulerOverrideCommitment, approval.RulerReason);
            check(rulerRow.InfluenceText == "Influence commitment: 375", "Ruler row includes the ordinary vote plus the override");
            var rows = TooltipRows(rulerRow, "_reasonTooltip");
            check(rows.Contains("Council vote commitment:75 (YAY)")
                && rows.Contains("Additional override commitment:300")
                && rows.Contains("Committed Influence:375"), "Ruler portrait separates the ordinary vote, override and total");

            _influence = 674.99f;
            var cannotApprove = Summary(opposed);
            check(cannotApprove.DecisionText == "King Derthert would reject the treaty."
                && cannotApprove.RulerOverrideCommitment == 0 && cannotApprove.RulerReason.Contains("lack the influence"),
                "Unfunded acceptance override respects the protected reserve without claiming spending");

            var supported = Council(350, 75, TreatyCouncilVoteStance.Nay, utility: -100);
            var cannotReject = Summary(supported);
            check(cannotReject.DecisionText == "King Derthert would approve the treaty."
                && cannotReject.RulerOverrideCommitment == 0 && cannotReject.RulerReason.Contains("lack the influence"),
                "Protected reserve explains why an opposing ruler yields despite sufficient raw influence");
            check(new TreatyCouncilMemberVM(supported.Members[0], true, true).InfluenceText == "Influence commitment: 75",
                "Ordinary ruler commitment remains visible when no override occurs");
            _influence = 675;
            var rejection = Summary(supported);
            check(rejection.RulerOverrideCommitment == 300
                && rejection.DecisionText.Contains("reject the treaty with 300 additional influence"),
                "Funded rejection uses the actual rejection override cost");

            var tie = Summary(Council(25, 25, TreatyCouncilVoteStance.Yay, 25));
            check(tie.CouncilText.Contains("is divided") && tie.BalanceText == "50% YAY / 50% NAY"
                && tie.RulerOverrideCommitment == 25, "Tied votes need the existing twenty-five influence margin");
            foreach (float utility in new[] { 30f, -30f })
            {
                var empty = Summary(Council(0, 0, TreatyCouncilVoteStance.Abstain, 0, utility));
                check(empty.TotalInfluence == 0 && empty.BalanceText == "Uncommitted"
                    && empty.CouncilText.Contains("committed no influence") && !empty.SummaryText.Contains("overrule")
                    && empty.RulerOverrideCommitment == 0 && string.IsNullOrEmpty(empty.RulerReason),
                    "Empty council directly follows its ruler without an invented override: " + utility);
            }
            var sole = Summary(Council(0, 0, TreatyCouncilVoteStance.Yay, 0, sole: true));
            check(sole.BalanceText == "Ruler decision" && sole.CouncilText.Contains("no vassal council")
                && !sole.SummaryText.Contains("overrule") && sole.RulerOverrideCommitment == 0,
                "Single-house realm has a neutral bar and a sovereign decision");
            var compulsory = Summary(opposed, forced: true);
            check(compulsory.DecisionText.Contains("Peace is compulsory") && compulsory.RulerOverrideCommitment == 0
                && string.IsNullOrEmpty(compulsory.RulerReason), "Compulsory peace never invents an override cost or ruler motive");
            var overBudget = Summary(Council(350, 75, TreatyCouncilVoteStance.Yay, blocked: true), forced: true);
            check(overBudget.DecisionText.Contains("exceeds the available war score") && !overBudget.SummaryText.Contains("compulsory")
                && overBudget.RulerOverrideCommitment == 0, "Budget failure takes precedence over forced acceptance");
            var absent = new TreatyCouncilSummaryVM(kingdom, null, false, false, false);
            check(absent.TotalInfluence == 0 && absent.CouncilText.Contains("unavailable"), "Unavailable evaluations reset to a neutral summary");
            _leader = null;
            check(Summary(opposed).DecisionText.Contains("No ruler is available"), "Missing ruler names cannot crash the summary");
            _leader = Blank<Hero>();
            _player = _ruler;
            var player = Summary(Council(0, 0, TreatyCouncilVoteStance.Abstain, 0));
            check(player.DecisionText.Contains("would approve") && player.RulerOverrideCommitment == 0,
                "Player ruler directly approves an unopposed draft without a fictitious cost");

            Patch(AccessTools.Method(tooltipType, "ExecuteBeginHint"), nameof(Begin));
            Patch(AccessTools.Method(tooltipType, "ExecuteEndHint"), nameof(End));
            _begun = _ended = 0;
            approval.ExecuteEndHint(); rulerRow.ExecuteEndReasonHint();
            check(_ended == 0, "Inactive hover cleanup leaves unrelated tooltips alone");
            var vm = Blank<PeaceParleyVM>();
            AccessTools.Field(typeof(PeaceParleyVM), "<PlayerCouncil>k__BackingField")
                .SetValue(vm, new MBBindingList<TreatyCouncilMemberVM> { rulerRow });
            AccessTools.Field(typeof(PeaceParleyVM), "<OpponentCouncil>k__BackingField")
                .SetValue(vm, new MBBindingList<TreatyCouncilMemberVM>());
            vm.PlayerCouncilSummary = approval;
            vm.OpponentCouncilSummary = rejection;
            AccessTools.Field(typeof(PeaceParleyVM), "_predictedOutcomeTooltip")
                .SetValue(vm, AccessTools.Field(typeof(TreatyCouncilSummaryVM), "_tooltip").GetValue(approval));
            approval.ExecuteBeginHint(); rejection.ExecuteBeginHint(); rulerRow.ExecuteBeginReasonHint();
            vm.ExecuteBeginPredictedOutcomeHint();
            vm.CloseTooltips(); vm.CloseTooltips();
            check(_begun == 4 && _ended == 4, "Draft refresh and close dismiss each active hover exactly once");

            CheckLayout(check);
        }
        finally { harmony.UnpatchAll(harmony.Id); }
    }

    private static void CheckLayout(Action<bool, string> check)
    {
        string root = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "../../../../../"));
        var document = new XmlDocument();
        document.Load(Path.Combine(root, "GUI/Prefabs/Parley/BellumPeaceParley.xml"));
        var strip = (XmlElement)document.SelectSingleNode("//*[@Id='RatificationStrip']");
        var list = (XmlElement)strip.ParentNode.ParentNode;
        check(strip.PreviousSibling == null && strip.NextSibling.Attributes["Text"].Value == "@BudgetText",
            "Ratification strip sits above Drafted Terms");
        var bars = strip.SelectNodes("Children/Widget[@DataSource]").Cast<XmlElement>().ToArray();
        check(bars.Length == 2 && bars[0].GetAttribute("DataSource") == "{PlayerCouncilSummary}"
            && bars[1].GetAttribute("DataSource") == "{OpponentCouncilSummary}", "Each bar belongs to its adjacent realm");
        foreach (var bar in bars)
        {
            check(bar.GetAttribute("SuggestedWidth") == "246" && bar.GetAttribute("SuggestedHeight") == "28"
                && bar.GetAttribute("Command.HoverBegin") == "ExecuteBeginHint"
                && !bar.HasAttribute("IsEnabled"), "Compact native-framed bar retains an enabled hover region");
            var fill = (XmlElement)bar.SelectSingleNode("Children/FillBarWidget");
            check(fill.GetAttribute("InitialAmount") == "@YayInfluence" && fill.GetAttribute("MaxAmount") == "@TotalInfluence"
                && fill.GetAttribute("Color") == "@OppositionColor", "Bar geometry uses exact commitments and a neutral empty background");
        }
        var icon = (XmlElement)strip.SelectSingleNode("Children/Widget[@Id='TreatyOutcomeIcon']");
        var visual = (XmlElement)icon.SelectSingleNode("Children/BoolBrushChangerBrushWidget");
        check(icon.GetAttribute("SuggestedWidth") == "44" && !icon.HasAttribute("Command.Click")
            && visual.GetAttribute("BooleanCheck") == "@TreatyWillPass"
            && visual.GetAttribute("TrueBrush") == "Barter.Offer.Visual.Accepted"
            && visual.GetAttribute("FalseBrush") == "Barter.Offer.Visual.NotAccepted", "Native handshake indicates the combined result without a barter action");
        check(document.SelectSingleNode("//*[@Text='@PredictedOutcomeText']") == null,
            "Old bottom outcome label is removed");
        CheckNativeAttributes(strip, check);
        var brushes = new XmlDocument();
        brushes.Load(Path.Combine(root, "GUI/Brushes/BellumParley.xml"));
        var brush = (XmlElement)brushes.SelectSingleNode("/Brushes/Brush[@Name='Bellum.Parley.VoteBalance.Text']");
        var style = (XmlElement)brush.SelectSingleNode("Styles/Style[@Name='Default']");
        check(style.GetAttribute("FontSize") == "17" && style.GetAttribute("TextOutlineColor") == "#000000FF"
            && style.GetAttribute("TextOutlineAmount") == "0.3", "Percentage text uses a small outlined native brush style");
        int width = 600 - int.Parse(list.GetAttribute("MarginLeft")) - int.Parse(list.GetAttribute("MarginRight"));
        check(width == 552 && 246 * 2 + 44 + 16 == width, "Both bars and the icon fit the unchanged center width with eight-unit gaps");
        foreach (var mode in new[] { new { Name = "ruler", Height = 351, Terms = 199, Controls = 0 },
            new { Name = "vassal", Height = 609, Terms = 277, Controls = 196 } })
        {
            int available = mode.Height - int.Parse(list.GetAttribute("MarginTop")) - int.Parse(list.GetAttribute("MarginBottom"));
            int required = 44 + 6 + 30 + 4 + mode.Terms + mode.Controls;
            check(required <= available, "Ratification strip preserves the " + mode.Name + " panel's existing height and controls");
        }
    }

    private static void CheckNativeAttributes(XmlElement strip, Action<bool, string> check)
    {
        var ui = Assembly.Load("TaleWorlds.GauntletUI");
        var extras = Assembly.Load("TaleWorlds.GauntletUI.ExtraWidgets");
        var gameWidgets = Assembly.Load("TaleWorlds.MountAndBlade.GauntletUI.Widgets");
        var knownTypes = new Dictionary<string, Type> {
            { "Widget", ui.GetType("TaleWorlds.GauntletUI.BaseTypes.Widget", true) },
            { "TextWidget", ui.GetType("TaleWorlds.GauntletUI.BaseTypes.TextWidget", true) },
            { "FillBarWidget", extras.GetType("TaleWorlds.GauntletUI.ExtraWidgets.FillBarWidget", true) },
            { "BoolBrushChangerBrushWidget", gameWidgets.GetType("TaleWorlds.MountAndBlade.GauntletUI.Widgets.BoolBrushChangerBrushWidget", true) }
        };
        var unsupported = new HashSet<string>();
        foreach (XmlElement widget in strip.SelectNodes("descendant-or-self::*"))
        {
            if (!knownTypes.TryGetValue(widget.Name, out Type type)) continue;
            foreach (XmlAttribute attribute in widget.Attributes)
            {
                string name = attribute.Name;
                if (name.StartsWith("Command.") || name == "DataSource" || name == "Id") continue;
                if (AccessTools.Property(type, name) == null) unsupported.Add(widget.Name + "." + name);
            }
        }
        check(unsupported.Count == 0, "Ratification attributes exist in the actual native widget API: "
            + string.Join(", ", unsupported));
    }
}
