using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
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

            var rulerRow = new TreatyCouncilMemberVM(opposed.Members[0], true, true,
                approval.RulerOverrideCommitment, approval.RulerReason);
            check(rulerRow.InfluenceText == "375" && rulerRow.IsInfluenceVisible && rulerRow.VoteText == "ACCEPT",
                "Ruler row shows its exact ordinary vote plus override as a plain amount beneath the decision");
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
            check(new TreatyCouncilMemberVM(supported.Members[0], true, true).InfluenceText == "75",
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
                && compulsory.RulerReason == "The war is decided; we must accept these terms.",
                "Compulsory peace explains acceptance without inventing an override cost");
            var overBudget = Summary(Council(350, 75, TreatyCouncilVoteStance.Yay, blocked: true), forced: true);
            check(overBudget.DecisionText.Contains("exceeds the available war score") && !overBudget.SummaryText.Contains("compulsory")
                && overBudget.RulerOverrideCommitment == 0 && overBudget.RulerReason.Contains("exceeds the available war score"),
                "Budget failure takes precedence over forced acceptance in both summary and card explanation");
            foreach (var summary in new[] { compulsory, overBudget, approval, rejection, cannotApprove, cannotReject })
            {
                var row = new TreatyCouncilMemberVM(opposed.Members[0], true,
                    summary == compulsory || summary == approval || summary == cannotReject,
                    summary.RulerOverrideCommitment, summary.RulerReason);
                check(row.PrimaryReasonText == summary.RulerReason
                    && TooltipRows(row, "_reasonTooltip").Contains(summary.RulerReason + ":"),
                    "Ruler decision explanation takes priority and is shared with its tooltip: " + summary.RulerReason);
            }
            var absent = new TreatyCouncilSummaryVM(kingdom, null, false, false, false);
            check(absent.TotalInfluence == 0 && absent.CouncilText.Contains("unavailable"), "Unavailable evaluations reset to a neutral summary");
            _leader = null;
            check(Summary(opposed).DecisionText.Contains("No ruler is available"), "Missing ruler names cannot crash the summary");
            _leader = Blank<Hero>();
            _player = _ruler;
            var player = Summary(Council(0, 0, TreatyCouncilVoteStance.Abstain, 0));
            check(player.DecisionText.Contains("would approve") && player.RulerOverrideCommitment == 0,
                "Player ruler directly approves an unopposed draft without a fictitious cost");

            foreach (var stance in new[] { TreatyCouncilVoteStance.Yay, TreatyCouncilVoteStance.Nay })
            foreach (int amount in new[] { 25, 75, 150 })
            {
                var member = new TreatyCouncilMemberVM(new TreatyCouncilMemberEvaluation(
                    _ruler, 45, 1000, 0, stance, amount, null));
                check(member.InfluenceText == amount.ToString() && member.IsInfluenceVisible
                    && member.VoteText == (stance == TreatyCouncilVoteStance.Yay ? "YAY" : "NAY")
                    && member.EnthusiasmText == "Enthusiasm: 45%",
                    "Plain influence amount preserves the actual vote and separate enthusiasm: " + stance + " " + amount);
            }
            foreach (var stance in new[] { TreatyCouncilVoteStance.Yay, TreatyCouncilVoteStance.Nay, TreatyCouncilVoteStance.Abstain })
            {
                var member = new TreatyCouncilMemberVM(new TreatyCouncilMemberEvaluation(
                    _ruler, 45, 0, 0, stance, 0, null));
                check(!member.IsInfluenceVisible && member.InfluenceText == "0",
                    "Zero commitment hides both the amount and influence icon: " + stance);
            }
            var abstaining = new TreatyCouncilMemberVM(new TreatyCouncilMemberEvaluation(
                _ruler, 45, 1000, 0, TreatyCouncilVoteStance.Abstain, 150, null));
            check(!abstaining.IsInfluenceVisible && abstaining.VoteText == "ABSTAIN",
                "Abstention hides even a stale nonzero ordinary commitment");
            var rulerOverrideOnly = new TreatyCouncilMemberVM(new TreatyCouncilMemberEvaluation(
                _ruler, 45, 2000, 0, TreatyCouncilVoteStance.Abstain, 0, null), true, false, 1275);
            check(rulerOverrideOnly.IsInfluenceVisible && rulerOverrideOnly.InfluenceText == "1275"
                && rulerOverrideOnly.VoteText == "REJECT", "Ruler overrides remain visible above the ordinary voting tiers");
            var overrideRows = TooltipRows(rulerOverrideOnly, "_reasonTooltip");
            check(overrideRows.Contains("Council vote commitment:0 (ABSTAIN)")
                && overrideRows.Contains("Additional override commitment:1275")
                && overrideRows.Contains("Committed Influence:1275"), "An override-only ruler retains the full influence breakdown");
            var missing = new TreatyCouncilMemberVM(null);
            check(!missing.IsInfluenceVisible && missing.EnthusiasmValue == 0 && missing.VoteText == "ABSTAIN",
                "Unavailable noble evaluation has no fabricated influence commitment");
            foreach (int value in new[] { -5, 0, 30, 31, 45, 70, 71, 100, 105 })
            {
                int clamped = Math.Max(0, Math.Min(100, value));
                var member = new TreatyCouncilMemberVM(new TreatyCouncilMemberEvaluation(
                    _ruler, value, 0, 0, TreatyCouncilVoteStance.Abstain, 0, null));
                var color = clamped <= 30 ? Color.ConvertStringToColor("#82E06AFF")
                    : clamped <= 70 ? Color.ConvertStringToColor("#E4B85EFF") : Colors.Red;
                check(member.EnthusiasmValue == clamped && member.EnthusiasmColor == color
                    && member.EnthusiasmText == "Enthusiasm: " + clamped + "%",
                    "Enthusiasm keeps its existing range, text and war-will color: " + value);
            }

            CheckMemberExplanations(check, TooltipRows);

            Patch(AccessTools.Method(tooltipType, "ExecuteBeginHint"), nameof(Begin));
            Patch(AccessTools.Method(tooltipType, "ExecuteEndHint"), nameof(End));
            _begun = _ended = 0;
            var vm = Blank<PeaceParleyVM>();
            AccessTools.Field(typeof(PeaceParleyVM), "<PlayerCouncil>k__BackingField")
                .SetValue(vm, new MBBindingList<TreatyCouncilMemberVM> { rulerRow });
            AccessTools.Field(typeof(PeaceParleyVM), "<OpponentCouncil>k__BackingField")
                .SetValue(vm, new MBBindingList<TreatyCouncilMemberVM>());
            vm.PlayerCouncilSummary = approval;
            vm.OpponentCouncilSummary = rejection;
            vm.ExecuteBeginPredictedOutcomeHint();
            vm.CloseTooltips();
            check(_begun == 0 && _ended == 0, "Unavailable outcome and inactive cleanup leave unrelated tooltips alone");
            vm.PredictedOutcomeText = "Predicted Outcome: TREATY REJECTED";
            vm.PlayerRatificationText = approval.SummaryText;
            vm.OpponentRatificationText = rejection.SummaryText;
            var buildOutcome = AccessTools.Method(typeof(PeaceParleyVM), "BuildPredictedOutcomeTooltip");
            // Build the native callback without adding a UI assembly reference to the harness.
            var instance = Expression.Constant(vm);
            var outcomeRows = Expression.Lambda(typeof(Func<>).MakeGenericType(buildOutcome.ReturnType),
                Expression.Call(buildOutcome,
                    Expression.Property(instance, nameof(PeaceParleyVM.PredictedOutcomeText)),
                    Expression.Property(instance, nameof(PeaceParleyVM.PlayerRatificationText)),
                    Expression.Property(instance, nameof(PeaceParleyVM.OpponentRatificationText)))).Compile();
            AccessTools.Field(typeof(PeaceParleyVM), "_predictedOutcomeTooltip")
                .SetValue(vm, Activator.CreateInstance(tooltipType, outcomeRows));
            check(TooltipRows(vm, "_predictedOutcomeTooltip").SequenceEqual(new[] {
                vm.PredictedOutcomeText + ":", ":", approval.SummaryText + ":", ":", rejection.SummaryText + ":" }),
                "Single outcome tooltip preserves its prediction title, separators and both full realm decisions");
            check(!string.Join("\n", TooltipRows(vm, "_predictedOutcomeTooltip")).Contains("%")
                && !string.Join("\n", TooltipRows(vm, "_predictedOutcomeTooltip")).Contains("remaining"),
                "Combined outcome tooltip keeps percentages and remaining balances out of its text");
            vm.ExecuteBeginPredictedOutcomeHint();
            vm.ExecuteBeginPredictedOutcomeHint();
            check(_begun == 1, "Repeated hover entry opens the shared tooltip only once");
            // Match Gauntlet invocation and avoid inlining the native close call before Harmony patches it.
            var endOutcomeHint = AccessTools.Method(typeof(PeaceParleyVM), nameof(PeaceParleyVM.ExecuteEndPredictedOutcomeHint));
            endOutcomeHint.Invoke(vm, null);
            endOutcomeHint.Invoke(vm, null);
            check(_ended == 1, "Leaving the combined strip closes its tooltip only once: begun=" + _begun + ", ended=" + _ended);
            vm.ExecuteBeginPredictedOutcomeHint();
            rulerRow.ExecuteBeginReasonHint();
            vm.CloseTooltips(); vm.CloseTooltips();
            check(_begun == 3 && _ended == 3, "Draft refresh and close clean up the shared outcome and noble hovers exactly once");

            CheckLayout(check);
        }
        finally { harmony.UnpatchAll(harmony.Id); }
    }

    private static void CheckMemberExplanations(Action<bool, string> check, Func<object, string, string[]> tooltipRows)
    {
        foreach (var stance in new[] { TreatyCouncilVoteStance.Yay, TreatyCouncilVoteStance.Nay })
        {
            float sign = stance == TreatyCouncilVoteStance.Yay ? 1f : -1f;
            var reasons = new[] { new TreatyCouncilReason("Strong opposing motive", -60f * sign),
                new TreatyCouncilReason("Aligned motive", 40f * sign), new TreatyCouncilReason("Other aligned motive", 35f * sign) };
            var evaluation = new TreatyCouncilMemberEvaluation(_ruler, 45, 1000, 15f * sign, stance, 75, reasons);
            var row = new TreatyCouncilMemberVM(evaluation);
            var rows = tooltipRows(row, "_reasonTooltip");
            check(row.PrimaryReasonText == "Aligned motive" && evaluation.PrimaryReason == reasons[0],
                "Card selects the strongest vote-aligned motive without changing the public strongest-overall property: " + stance);
            check(rows.Contains("Aligned motive:") && reasons.All(reason => rows.Contains(reason.Label + ":"
                + reason.Amount.ToString("+0.0;-0.0;0.0"))) && evaluation.Reasons.Sum(reason => reason.Amount) == evaluation.Utility,
                "Tooltip shares the selected explanation and retains every weighted motive and the exact utility: " + stance);

            var tied = new TreatyCouncilMemberEvaluation(_ruler, 45, 1000, 20f * sign, stance, 75,
                new[] { new TreatyCouncilReason("First aligned motive", 40f * sign),
                    new TreatyCouncilReason("Second aligned motive", 40f * sign), new TreatyCouncilReason("Opposition", -60f * sign) });
            check(new TreatyCouncilMemberVM(tied).PrimaryReasonText == "First aligned motive",
                "Equal aligned motives preserve their original order: " + stance);

            var malformed = new TreatyCouncilMemberEvaluation(_ruler, 45, 1000, 20f * sign, stance, 75,
                new[] { null, new TreatyCouncilReason("", 100f * sign), new TreatyCouncilReason("Zero", 0),
                    new TreatyCouncilReason("NaN", float.NaN), new TreatyCouncilReason("Infinity", float.PositiveInfinity * sign),
                    new TreatyCouncilReason("Valid aligned motive", 20f * sign) });
            var safe = new TreatyCouncilMemberVM(malformed);
            check(safe.PrimaryReasonText == "Valid aligned motive" && tooltipRows(safe, "_reasonTooltip").Contains("Valid aligned motive:"),
                "Null, empty, zero and nonfinite candidates cannot replace a valid explanation or break its tooltip: " + stance);

            foreach (var candidates in new[] { Array.Empty<TreatyCouncilReason>(),
                new[] { new TreatyCouncilReason("Opposing motive", -100f * sign), new TreatyCouncilReason("Zero", 0) } })
            {
                var missing = new TreatyCouncilMemberVM(new TreatyCouncilMemberEvaluation(_ruler, 45, 1000, 0, stance, 25, candidates));
                check(missing.PrimaryReasonText == (stance == TreatyCouncilVoteStance.Yay
                    ? "I stand behind these terms." : "I cannot support these terms."),
                    "Missing aligned motives use a stance-consistent fallback, never the opposing reason: " + stance);
            }
        }

        foreach (var example in new[] {
            Tuple.Create(TreatyCouncilAbstentionReason.None, "I will abstain from this vote."),
            Tuple.Create(TreatyCouncilAbstentionReason.NeutralAssessment, "Neither side makes a convincing case; I will abstain."),
            Tuple.Create(TreatyCouncilAbstentionReason.InsufficientInfluence, "I lack the influence to cast my vote."),
            Tuple.Create(TreatyCouncilAbstentionReason.ProtectedInfluence, "I must reserve my influence for other obligations."),
            Tuple.Create(TreatyCouncilAbstentionReason.PlayerAbstained, "I withhold my voice from this treaty."),
            Tuple.Create(TreatyCouncilAbstentionReason.PlayerUndecided, "I have not yet declared my position."),
            Tuple.Create((TreatyCouncilAbstentionReason)999, "I will abstain from this vote.") })
        {
            var evaluation = new TreatyCouncilMemberEvaluation(_ruler, 45, 1000, -100, TreatyCouncilVoteStance.Abstain, 0,
                new[] { new TreatyCouncilReason("Opposing motive", -100) }, example.Item1);
            var row = new TreatyCouncilMemberVM(evaluation);
            _influence = 0;
            var rows = tooltipRows(row, "_reasonTooltip");
            check(row.PrimaryReasonText == example.Item2 && rows.Contains(example.Item2 + ":")
                && rows.Contains("Opposing motive:" + (-100f).ToString("+0.0;-0.0;0.0")) && !row.IsInfluenceVisible,
                "Captured abstention reason replaces the political motive without altering its breakdown or resampling funds: " + example.Item1);
        }
        var oldCaller = new TreatyCouncilMemberEvaluation(_ruler, 45, 0, 100, TreatyCouncilVoteStance.Abstain, 0,
            new[] { new TreatyCouncilReason("Supporting motive", 100) });
        check(oldCaller.AbstentionReason == TreatyCouncilAbstentionReason.None
            && new TreatyCouncilMemberVM(oldCaller).PrimaryReasonText == "I will abstain from this vote.",
            "Existing public constructor remains available and does not guess why an external caller abstained");

        foreach (bool accepts in new[] { false, true })
        {
            var evaluation = new TreatyCouncilMemberEvaluation(_ruler, 45, 0, accepts ? -100 : 100,
                accepts ? TreatyCouncilVoteStance.Nay : TreatyCouncilVoteStance.Abstain, 0,
                new[] { new TreatyCouncilReason("Positive motive", 30), new TreatyCouncilReason("Negative motive", -40) },
                TreatyCouncilAbstentionReason.ProtectedInfluence);
            var ruler = new TreatyCouncilMemberVM(evaluation, true, accepts);
            check(ruler.PrimaryReasonText == (accepts ? "Positive motive" : "Negative motive")
                && ruler.VoteText == (accepts ? "ACCEPT" : "REJECT"),
                "Ruler fallback follows the displayed final decision rather than an opposing or abstaining ordinary vote: " + accepts);
        }
        check(new TreatyCouncilMemberVM(null).PrimaryReasonText == "No decisive motive",
            "Unavailable evaluations retain a neutral explanation without inventing an abstention cause");
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
        check(strip.GetAttribute("DoNotPassEventsToChildren") == "true"
            && strip.GetAttribute("Command.HoverBegin") == "ExecuteBeginPredictedOutcomeHint"
            && strip.GetAttribute("Command.HoverEnd") == "ExecuteEndPredictedOutcomeHint"
            && !strip.HasAttribute("DataSource") && !strip.HasAttribute("DoNotAcceptEvents")
            && strip.SelectNodes(".//*[@Command.HoverBegin or @Command.HoverEnd]").Count == 0,
            "Both bars, the handshake and their gaps share one parent-level outcome hover without competing tooltips");
        var bars = strip.SelectNodes("Children/Widget[@DataSource]").Cast<XmlElement>().ToArray();
        check(bars.Length == 2 && bars[0].GetAttribute("DataSource") == "{PlayerCouncilSummary}"
            && bars[1].GetAttribute("DataSource") == "{OpponentCouncilSummary}", "Each bar belongs to its adjacent realm");
        foreach (var bar in bars)
        {
            check(bar.GetAttribute("SuggestedWidth") == "230.5" && bar.GetAttribute("SuggestedHeight") == "28"
                && bar.GetAttribute("DoNotAcceptEvents") == "true"
                && !bar.HasAttribute("Command.HoverBegin"), "Compact native-framed bar defers hover to the shared outcome strip");
            var fill = (XmlElement)bar.SelectSingleNode("Children/FillBarWidget");
            check(fill.GetAttribute("InitialAmount") == "@YayInfluence" && fill.GetAttribute("MaxAmount") == "@TotalInfluence"
                && fill.GetAttribute("Color") == "@OppositionColor", "Bar geometry uses exact commitments and a neutral empty background");
        }
        var icon = (XmlElement)strip.SelectSingleNode("Children/Widget[@Id='TreatyOutcomeIcon']");
        var visual = (XmlElement)icon.SelectSingleNode("Children/BoolBrushChangerBrushWidget");
        string game = (string)AccessTools.Field(typeof(Program), "_game").GetValue(null);
        var nativeBarter = new XmlDocument();
        nativeBarter.Load(Path.Combine(game, "Modules/SandBox/GUI/Prefabs/Barter/BarterScreen.xml"));
        var nativeIcon = (XmlElement)nativeBarter.SelectSingleNode("//ButtonWidget[@Id='AutoOfferButton']");
        check(icon.GetAttribute("SuggestedWidth") == nativeIcon.GetAttribute("SuggestedWidth")
            && icon.GetAttribute("SuggestedHeight") == nativeIcon.GetAttribute("SuggestedHeight")
            && strip.GetAttribute("SuggestedHeight") == nativeIcon.GetAttribute("SuggestedHeight")
            && icon.GetAttribute("HorizontalAlignment") == "Center" && icon.GetAttribute("VerticalAlignment") == "Center"
            && bars.All(bar => bar.GetAttribute("VerticalAlignment") == "Center"),
            "Handshake matches the actual native barter dimensions and stays centered between both bars");
        check(!icon.HasAttribute("Command.Click")
            && icon.GetAttribute("DoNotAcceptEvents") == "true" && !icon.HasAttribute("Command.HoverBegin")
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
        double occupiedWidth = bars.Sum(bar => double.Parse(bar.GetAttribute("SuggestedWidth"), CultureInfo.InvariantCulture))
            + int.Parse(icon.GetAttribute("SuggestedWidth"));
        check(width == 552 && occupiedWidth + 16 == width,
            "Both bars and the native-sized icon fit the unchanged center width with eight-unit gaps");
        var vmSource = File.ReadAllText(Path.Combine(root, "UI/Parley/PeaceParleyVM.cs"));
        check(vmSource.Contains("DraftTermsHeight = IsPlayerVassal ? 246 : 199;"),
            "Vassal draft viewport makes room for the native handshake without moving its vote controls");
        foreach (var mode in new[] { new { Name = "ruler", Height = 351, Terms = 199, Controls = 0 },
            new { Name = "vassal", Height = 609, Terms = 246, Controls = 196 } })
        {
            int available = mode.Height - int.Parse(list.GetAttribute("MarginTop")) - int.Parse(list.GetAttribute("MarginBottom"));
            int required = int.Parse(strip.GetAttribute("SuggestedHeight")) + 6 + 30 + 4 + mode.Terms + mode.Controls;
            check(required <= available, "Ratification strip preserves the " + mode.Name + " panel's existing height and controls");
        }
        CheckMemberLayout(document, bars[0], check);
        CheckInfluenceSizing(document, check);
    }

    private static void CheckMemberLayout(XmlDocument document, XmlElement summaryBar, Action<bool, string> check)
    {
        var cards = new[] { "PlayerCouncilList", "OpponentCouncilList" }.Select(id =>
            (XmlElement)document.SelectSingleNode("//*[@Id='" + id + "']/ItemTemplate/Widget")).ToArray();
        check(cards[0].OuterXml == cards[1].OuterXml, "Both realms use the exact same noble card layout");
        foreach (var card in cards)
        {
            check(card.GetAttribute("SuggestedHeight") == "124" && card.GetAttribute("MarginBottom") == "8"
                && card.GetAttribute("DoNotPassEventsToChildren") == "true"
                && card.GetAttribute("Command.HoverBegin") == "ExecuteBeginReasonHint"
                && card.GetAttribute("Command.HoverEnd") == "ExecuteEndReasonHint"
                && card.SelectNodes(".//ButtonWidget | .//*[@Command.Click or @Command.HoverBegin]").Count == 0,
                "Whole noble card has a single read-only hover target and unchanged row spacing");
            var details = (XmlElement)card.SelectSingleNode("Children/Widget[@Id='CouncilMemberDetails']");
            var description = (XmlElement)details.SelectSingleNode("Children/ListPanel[@Id='CouncilMemberDescription']");
            var column = (XmlElement)details.SelectSingleNode("Children/ListPanel[@Id='CouncilVoteColumn']");
            int reserve = int.Parse(description.GetAttribute("MarginRight"));
            int columnWidth = int.Parse(column.GetAttribute("SuggestedWidth"));
            check(details.GetAttribute("MarginLeft") == "120" && details.GetAttribute("MarginRight") == "4"
                && column.GetAttribute("WidthSizePolicy") == "Fixed" && columnWidth == 112
                && column.GetAttribute("HorizontalAlignment") == "Right" && reserve - columnWidth == 12,
                "Portrait, description and fixed vote column have separate bounds with a twelve-unit gap");
            var name = (XmlElement)description.SelectSingleNode("Children/TextWidget[@Text='@DisplayName']");
            var reason = (XmlElement)description.SelectSingleNode("Children/TextWidget[@Text='@PrimaryReasonText']");
            var bar = (XmlElement)description.SelectSingleNode("Children/Widget[@Id='CouncilEnthusiasmBar']");
            var fill = (XmlElement)bar.SelectSingleNode("Children/FillBarWidget");
            var label = (XmlElement)bar.SelectSingleNode("Children/TextWidget");
            check(bar.GetAttribute("SuggestedHeight") == summaryBar.GetAttribute("SuggestedHeight")
                && bar.GetAttribute("Sprite") == summaryBar.GetAttribute("Sprite")
                && fill.GetAttribute("SuggestedHeight") == "18"
                && fill.GetAttribute("MarginLeft") == "4" && fill.GetAttribute("MarginRight") == "4"
                && fill.GetAttribute("VerticalAlignment") == "Center",
                "Noble enthusiasm reuses the middle bar's frame, outer height and inset fill");
            check(fill.GetAttribute("InitialAmount") == "@EnthusiasmValue" && fill.GetAttribute("MaxAmount") == "100"
                && fill.GetAttribute("Color") == "#555555FF"
                && fill.SelectSingleNode("Children/Widget").Attributes["Color"].Value == "@EnthusiasmColor"
                && label.GetAttribute("Brush") == "Bellum.Parley.VoteBalance.Text"
                && label.GetAttribute("Text") == "@EnthusiasmText"
                && label.GetAttribute("WidthSizePolicy") == "StretchToParent"
                && label.GetAttribute("HeightSizePolicy") == "StretchToParent",
                "Centered outlined enthusiasm text overlays its original war-will fill, not treaty support");
            int usedHeight = description.SelectNodes("Children/*").Cast<XmlElement>().Sum(widget =>
                int.Parse(widget.GetAttribute("SuggestedHeight"))
                + (int.TryParse(widget.GetAttribute("MarginTop"), out int top) ? top : 0));
            int availableHeight = int.Parse(card.GetAttribute("SuggestedHeight"))
                - int.Parse(details.GetAttribute("MarginTop")) - int.Parse(details.GetAttribute("MarginBottom"));
            check(usedHeight <= availableHeight && reason.GetAttribute("SuggestedHeight") == "40"
                && reason.GetAttribute("Brush.FontSize") == "18" && reason.GetAttribute("ClipContents") == "true"
                && name.GetAttribute("HeightSizePolicy") == "Fixed" && name.GetAttribute("ClipContents") == "true",
                "Bounded name and two-line reason fit the original card height without touching its divider");
            var vote = (XmlElement)column.SelectSingleNode("Children/TextWidget");
            var influence = (XmlElement)column.SelectSingleNode("Children/ListPanel[@Id='CouncilInfluence']");
            var amount = (XmlElement)influence.SelectSingleNode("Children/TextWidget");
            var icon = (XmlElement)influence.SelectSingleNode("Children/BrushWidget");
            int voteTop = int.Parse(column.GetAttribute("MarginTop"));
            int barTop = int.Parse(name.GetAttribute("SuggestedHeight")) + int.Parse(bar.GetAttribute("MarginTop"));
            int influenceTop = voteTop + int.Parse(vote.GetAttribute("SuggestedHeight"))
                + int.Parse(influence.GetAttribute("MarginTop"));
            check(vote.GetAttribute("Text") == "@VoteText" && vote.GetAttribute("Brush.FontColor") == "@VoteColor"
                && voteTop == barTop && vote.GetAttribute("SuggestedHeight") == bar.GetAttribute("SuggestedHeight")
                && vote.GetAttribute("Brush.TextVerticalAlignment") == "Center"
                && influenceTop >= barTop + int.Parse(bar.GetAttribute("SuggestedHeight")) + 4,
                "Vote aligns vertically with the enthusiasm bar and the commitment sits beneath it");
            check(influenceTop + int.Parse(influence.GetAttribute("SuggestedHeight")) <= availableHeight,
                "Lowered vote and enlarged commitment still fit above the original card divider");
            check(influence.GetAttribute("IsVisible") == "@IsInfluenceVisible"
                && influence.GetAttribute("HorizontalAlignment") == "Center"
                && !influence.HasAttribute("Sprite") && !influence.HasAttribute("Brush")
                && influence.SelectNodes("Children/*").Count == 2
                && amount.GetAttribute("Text") == "@InfluenceText"
                && amount.GetAttribute("Brush") == "Kingdom.Voter.SupportAmount.Text"
                && amount.GetAttribute("Brush.FontColor") == "#FCA060FF"
                && icon.GetAttribute("Brush") == "Kingdom.VoteStrengthInfluence.Icon",
                "Only the highlighted number and native influence icon are shown, with no button frame");
            check(int.Parse(amount.GetAttribute("MaxWidth")) + int.Parse(icon.GetAttribute("SuggestedWidth"))
                + int.Parse(icon.GetAttribute("MarginLeft")) <= columnWidth
                && amount.GetAttribute("CanBreakWords") == "false"
                && amount.GetAttribute("HeightSizePolicy") == "Fixed",
                "Large ruler totals have a bounded single-number region that cannot overlap the bar");
            CheckNativeAttributes(card, check);
        }
    }

    private static void CheckInfluenceSizing(XmlDocument document, Action<bool, string> check)
    {
        string game = (string)AccessTools.Field(typeof(Program), "_game").GetValue(null);
        var diplomacy = new XmlDocument();
        diplomacy.Load(Path.Combine(game, "Modules/SandBox/GUI/Prefabs/KingdomManagement/Diplomacy/DiplomacyPanel.xml"));
        var nativeAmount = (XmlElement)diplomacy.SelectSingleNode("//TextWidget[@IntText='@InfluenceCost']");
        var nativeIcon = (XmlElement)nativeAmount.ParentNode.SelectSingleNode("Widget[@Sprite]");
        var brushes = new XmlDocument();
        brushes.Load(Path.Combine(game, "Modules/SandBox/GUI/Brushes/Kingdom.xml"));
        var nativeBrush = (XmlElement)brushes.SelectSingleNode("//Brush[@Name='" + nativeAmount.GetAttribute("Brush") + "']");
        string font = nativeBrush.GetAttribute("Font");
        string fontSize = nativeBrush.SelectSingleNode("Styles/Style[@Name='Default']").Attributes["FontSize"].Value;
        var amounts = document.SelectNodes("//*[@Text='@InfluenceText'] | //ButtonWidget[starts-with(@Command.Click,'ExecuteCommit')]/Children/ListPanel/Children/TextWidget")
            .Cast<XmlElement>().ToArray();
        check(amounts.Length == 5, "Both noble commitments and all three player voting amounts use the sizing check");
        foreach (var amount in amounts)
        {
            var icon = (XmlElement)amount.ParentNode.SelectSingleNode("BrushWidget");
            var iconBrush = brushes.SelectSingleNode("//Brush[@Name='" + icon.GetAttribute("Brush") + "']");
            check(amount.GetAttribute("Brush.Font") == font && amount.GetAttribute("Brush.FontSize") == fontSize
                && icon.GetAttribute("SuggestedWidth") == nativeIcon.GetAttribute("SuggestedWidth")
                && icon.GetAttribute("SuggestedHeight") == nativeIcon.GetAttribute("SuggestedHeight")
                && iconBrush.SelectSingleNode("Layers/BrushLayer").Attributes["Sprite"].Value == nativeIcon.GetAttribute("Sprite"),
                "Influence text and icon match native Propose Peace proportions: " + amount.GetAttribute("Text"));
            check(amount.GetAttribute("SuggestedHeight") == "32" && amount.GetAttribute("HeightSizePolicy") == "Fixed"
                && icon.GetAttribute("MarginLeft") == "4" && !icon.HasAttribute("PositionYOffset")
                && icon.GetAttribute("VerticalAlignment") == "Center"
                && int.Parse(icon.GetAttribute("SuggestedHeight")) <= int.Parse(amount.GetAttribute("SuggestedHeight")),
                "Larger influence glyph has explicit separation and room in its centered text row");
        }
        var buttons = document.SelectNodes("//ButtonWidget[starts-with(@Command.Click,'ExecuteCommit')]")
            .Cast<XmlElement>().ToArray();
        check(buttons.Length == 3 && buttons.Select(button => button.GetAttribute("Command.Click"))
            .SequenceEqual(new[] { "ExecuteCommit25", "ExecuteCommit75", "ExecuteCommit150" }),
            "The three player commitment commands remain unchanged");
        int index = 0;
        foreach (var button in buttons)
        {
            index++;
            check(button.GetAttribute("SuggestedWidth") == "88" && button.GetAttribute("SuggestedHeight") == "64"
                && button.GetAttribute("IsDisabled") == "@IsInfluenceSelectionDisabled"
                && button.GetAttribute("ButtonType") == "Radio"
                && button.GetAttribute("IsSelected") == "@IsCommit" + button.SelectSingleNode(".//TextWidget").Attributes["Text"].Value + "Selected"
                && button.GetAttribute("Brush") == "Kingdom.VoteStrength" + index + ".Button"
                && button.GetAttribute("UpdateChildrenStates") == "true",
                "Roomier player voting button preserves its native frame, selection and disabled behavior");
            CheckNativeAttributes(button, check);
        }
        int usedWidth = buttons.Sum(button => int.Parse(button.GetAttribute("SuggestedWidth"))
            + (int.TryParse(button.GetAttribute("MarginRight"), out int margin) ? margin : 0));
        check(usedWidth <= 552 && buttons[0].ParentNode.ParentNode.Attributes["SuggestedHeight"].Value == "64",
            "Larger commitment buttons fit within the existing center panel without increasing its height");
    }

    private static void CheckNativeAttributes(XmlElement root, Action<bool, string> check)
    {
        var ui = Assembly.Load("TaleWorlds.GauntletUI");
        var extras = Assembly.Load("TaleWorlds.GauntletUI.ExtraWidgets");
        var gameWidgets = Assembly.Load("TaleWorlds.MountAndBlade.GauntletUI.Widgets");
        var knownTypes = new Dictionary<string, Type> {
            { "Widget", ui.GetType("TaleWorlds.GauntletUI.BaseTypes.Widget", true) },
            { "TextWidget", ui.GetType("TaleWorlds.GauntletUI.BaseTypes.TextWidget", true) },
            { "ListPanel", ui.GetType("TaleWorlds.GauntletUI.BaseTypes.ListPanel", true) },
            { "BrushWidget", ui.GetType("TaleWorlds.GauntletUI.BaseTypes.BrushWidget", true) },
            { "ButtonWidget", ui.GetType("TaleWorlds.GauntletUI.BaseTypes.ButtonWidget", true) },
            { "FillBarWidget", extras.GetType("TaleWorlds.GauntletUI.ExtraWidgets.FillBarWidget", true) },
            { "BoolBrushChangerBrushWidget", gameWidgets.GetType("TaleWorlds.MountAndBlade.GauntletUI.Widgets.BoolBrushChangerBrushWidget", true) }
        };
        var unsupported = new HashSet<string>();
        foreach (XmlElement widget in root.SelectNodes("descendant-or-self::*"))
        {
            if (!knownTypes.TryGetValue(widget.Name, out Type type)) continue;
            foreach (XmlAttribute attribute in widget.Attributes)
            {
                string name = attribute.Name;
                if (name.StartsWith("Command.") || name == "DataSource" || name == "Id") continue;
                Type propertyType = type;
                foreach (string part in name.Split('.'))
                {
                    PropertyInfo property = AccessTools.Property(propertyType, part);
                    if (property == null) { unsupported.Add(widget.Name + "." + name); break; }
                    propertyType = property.PropertyType;
                }
            }
        }
        check(unsupported.Count == 0, root.GetAttribute("Id") + " attributes exist in the actual native widget API: "
            + string.Join(", ", unsupported));
    }
}
