using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text.RegularExpressions;
using BellumCivile;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Localization;

internal static class PeaceCouncilReadinessTests
{
    private static Clan _clan;
    private static float _day, _rallyBonus;
    private static Clan _rallyClan;
    private static WarScoreRecord _rallyWar;
    private static float _rallyBaseline;
    private static int _settlementEntryCalls;

    private static bool NoCampaign(ref Campaign __result) { __result = null; return false; }
    private static bool NoCourt(ref CourtAgendaBehavior __result) { __result = null; return false; }
    private static bool NoPlayer(ref Clan __result) { __result = null; return false; }
    private static bool Ruler(ref Clan __result) { __result = _clan; return false; }
    private static bool Eligible(ref IEnumerable<Clan> __result) { __result = new[] { _clan }; return false; }
    private static bool Influence(ref float __result) { __result = 100f; return false; }
    private static bool Now(ref CampaignTime __result) { __result = CampaignTime.Days(_day); return false; }
    private static bool Days(ref double __result) { __result = _day; return false; }
    private static bool Skip() => false;
    private static bool SettlementReached()
    {
        _settlementEntryCalls++;
        throw new InvalidOperationException("Readiness fixture settlement boundary reached");
    }
    private static bool Rally(Clan __0, WarScoreRecord __1, float __2, ref float __result)
    {
        _rallyClan = __0;
        _rallyWar = __1;
        _rallyBaseline = __2;
        __result = Math.Min(100f, __2 + _rallyBonus);
        return false;
    }

    private static bool Near(float actual, float expected) => Math.Abs(actual - expected) < .001f;
    private static T Blank<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    private static float Part(object assessment, string name)
        => (float)AccessTools.Property(assessment.GetType(), name).GetValue(assessment);

    internal static void Run(Action<bool, string> check)
    {
        var assembly = typeof(WarScoreRecord).Assembly;
        var ratifierType = assembly.GetType("BellumCivile.TreatyRatificationService", true);
        var settingsType = assembly.GetType("BellumCivile.BellumCivileSettings", true);
        var optionsType = assembly.GetType("BellumCivile.BellumCivileOptions", true);
        var settingsCache = AccessTools.Field(optionsType, "_settings");
        object previousSettings = settingsCache.GetValue(null);
        var period = AccessTools.Property(settingsType, "WarDurationReluctanceDays");
        var option = AccessTools.Property(optionsType, "WarDurationReluctanceDays");
        var readiness = AccessTools.Method(assembly.GetType("BellumCivile.PeaceReadiness", true), "Evaluate");
        var snapshotType = assembly.GetType("BellumCivile.TreatyCouncilSnapshot", true);
        var rowType = assembly.GetType("BellumCivile.TreatyCouncilClanSnapshot", true);
        var snapshotConstructor = snapshotType.GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic).Single();
        var templates = (IReadOnlyDictionary<string, string>)AccessTools.Field(ratifierType, "LocalizedReasonTemplates").GetValue(null);
        string Label(string key) => new TextObject(templates[key]).ToString();
        float Amount(TreatyCouncilMemberEvaluation member, string key)
            => member.Reasons.Where(reason => reason.Label == Label(key)).Sum(reason => reason.Amount);
        bool Has(TreatyCouncilMemberEvaluation member, string key)
            => member.Reasons.Any(reason => reason.Label == Label(key));
        object Assessment(float age, float score, float enthusiasm, int duration = 100)
            => readiness.Invoke(null, new object[] { age, score, enthusiasm, duration });

        var harmony = new Harmony("bellum.test.peace_council_readiness");
        var patched = new List<MethodBase>();
        void Patch(MethodBase method, string prefix)
        {
            if (method == null) throw new MissingMethodException(prefix);
            patched.Add(method);
            harmony.Patch(method, prefix: new HarmonyMethod(typeof(PeaceCouncilReadinessTests), prefix));
        }
        try
        {
            Patch(AccessTools.PropertyGetter(typeof(Campaign), "Current"), nameof(NoCampaign));
            Patch(AccessTools.PropertyGetter(typeof(CourtAgendaBehavior), "Current"), nameof(NoCourt));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "PlayerClan"), nameof(NoPlayer));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "Influence"), nameof(Influence));
            Patch(AccessTools.PropertyGetter(typeof(Kingdom), "RulingClan"), nameof(Ruler));
            Patch(AccessTools.PropertyGetter(typeof(CampaignTime), "Now"), nameof(Now));
            Patch(AccessTools.PropertyGetter(typeof(CampaignTime), "ToDays"), nameof(Days));
            Patch(AccessTools.Method(ratifierType, "GetEligibleClans"), nameof(Eligible));
            Patch(AccessTools.Method(typeof(CourtAgendaBehavior), "EffectiveWarWill"), nameof(Rally));
            // Avoid filesystem preset discovery, but execute the actual settings constructor/defaults.
            Patch(AccessTools.Method(assembly.GetType("BellumCivile.FeudalTitleStylePresetCatalog", true), "CreateDropdown"), nameof(Skip));
            object settings = Activator.CreateInstance(settingsType, true);
            settingsCache.SetValue(null, settings);
            check((int)period.GetValue(settings) == 100 && (int)option.GetValue(null) == 100,
                "New reluctance setting and option default to one hundred days");
            check(AccessTools.Property(settingsType, "MinimumWarDurationDays") == null
                && AccessTools.Property(optionsType, "MinimumWarDurationDays") == null,
                "Old thirty-day serialized property is not an alias for the new setting");
            var slider = period.GetCustomAttributesData().Single(attribute => attribute.AttributeType.Name == "SettingPropertyIntegerAttribute");
            check(Convert.ToInt32(slider.ConstructorArguments[1].Value) == 1
                && Convert.ToInt32(slider.ConstructorArguments[2].Value) == 500,
                "Reluctance MCM range is one through five hundred days");
            foreach (int invalid in new[] { -30, 0 })
            {
                period.SetValue(settings, invalid);
                check((int)option.GetValue(null) == 1, "Reluctance option clamps nonpositive values to one");
            }
            period.SetValue(settings, 100);

            _clan = Blank<Clan>();
            _clan.StringId = "readiness_clan";
            var kingdom = Blank<Kingdom>();
            kingdom.StringId = "readiness_realm";
            var service = Activator.CreateInstance(ratifierType, true);
            var evaluate = AccessTools.Method(ratifierType, "EvaluateCouncil");
            WarScoreRecord currentWar = null;
            TreatyCouncilEvaluation Council(float age, float score, float enthusiasm, float rally = 0f)
            {
                // A nonzero start catches accidental use of absolute campaign day instead of war age.
                const float started = 37f;
                _day = started + age;
                _rallyBonus = rally;
                currentWar = new WarScoreRecord("readiness_war", kingdom.StringId, "opponent", started, null);
                AccessTools.Field(typeof(WarScoreRecord), "_score").SetValue(currentWar, score);
                var rows = Array.CreateInstance(rowType, 1);
                rows.SetValue(Activator.CreateInstance(rowType, new object[] { _clan.StringId, enthusiasm, 100f, 100f }), 0);
                object snapshot = snapshotConstructor.Invoke(new object[] { rows });
                var proposal = new TreatyProposalRecord(currentWar.WarKey, kingdom.StringId, "opponent", kingdom.StringId, _day, 0, false);
                return (TreatyCouncilEvaluation)evaluate.Invoke(service, new object[] { currentWar, proposal, kingdom, true, snapshot });
            }

            foreach (float age in new[] { 99f, 100f })
            foreach (float enthusiasm in new[] { 0f, 5f, 10f, 50f })
            {
                var council = Council(age, 20f, enthusiasm);
                var member = council.Members.Single();
                object expected = Assessment(age, 20f, enthusiasm);
                string context = "age=" + age + "; E=" + enthusiasm;
                check(Near(member.Utility, Part(expected, "Total")) && Near(member.Enthusiasm, enthusiasm),
                    "Actual council consumes snapshot enthusiasm and shared readiness: " + context);
                check(Near(Amount(member, enthusiasm <= 20f ? "Low Enthusiasm" : "High Enthusiasm"), Part(expected, "Exhaustion"))
                    && Near(Amount(member, "Early war reluctance"), Part(expected, "EarlyWarReluctance"))
                    && Near(Amount(member, "Inconclusive campaign"), Part(expected, "InconclusiveWar"))
                    && Near(Amount(member, "Prolonged exhaustion"), Part(expected, "ProlongedExhaustion")),
                    "Actual council emits each shared reason with its full amount: " + context);
                check(Has(member, "Prolonged exhaustion") == (age == 100f && enthusiasm < 10f)
                    && Has(member, "Early war reluctance") == (age == 99f && enthusiasm > 0f)
                    && !Has(member, "Court rally"), "Boundary reasons appear only when applicable: " + context);
                check(council.IsSoleRulerDecision && council.IsRatified == (member.Utility >= 10f)
                    && Near(council.Support, member.Utility) && Near(member.Reasons.Sum(reason => reason.Amount), member.Utility),
                    "Council preserves readiness in final support and acceptance: " + context);
            }
            check(Near(Council(99, 20, 5).Members.Single().Utility, 24.5f)
                && Near(Council(100, 20, 5).Members.Single().Utility, 55f),
                "Day one hundred adds thirty prolonged support and removes the remaining half-point reluctance");

            foreach (float age in new[] { 30f, 99f, 100f })
            {
                var baseline = Council(age, 20f, 5f).Members.Single();
                var rallied = Council(age, 20f, 5f, 15f).Members.Single();
                object before = Assessment(age, 20f, 5f), after = Assessment(age, 20f, 20f);
                float expectedDelta = Part(after, "Total") - Part(before, "Total");
                check(Near(Amount(rallied, "Court rally"), expectedDelta)
                    && Near(rallied.Utility - baseline.Utility, expectedDelta)
                    && Near(rallied.Utility, Part(after, "Total")),
                    "Court rally uses the full nonlinear readiness delta at age " + age);
                check(!Near(expectedDelta, -7.5f), "Rally fixture distinguishes the obsolete linear-only delta at age " + age);
                check(_rallyClan == _clan && _rallyWar == currentWar && Near(_rallyBaseline, 5f),
                    "Rally receives the exact council clan, war and unadjusted snapshot enthusiasm");
                check(Near(Amount(rallied, "Low Enthusiasm"), Amount(baseline, "Low Enthusiasm"))
                    && Near(Amount(rallied, "Prolonged exhaustion"), Amount(baseline, "Prolonged exhaustion"))
                    && Near(rallied.Reasons.Sum(reason => reason.Amount), rallied.Utility),
                    "Rally is a separate delta without rewriting or double-counting baseline reasons");
            }
            check(Near(Amount(Council(30, 20, 5, 15).Members.Single(), "Court rally"), -69f)
                && Near(Amount(Council(100, 20, 5, 15).Members.Single(), "Court rally"), -55f),
                "Rally includes inconclusive/early pressure and the loss of prolonged exhaustion support");
            period.SetValue(settings, 50);
            check(!Has(Council(49, 20, 5).Members.Single(), "Prolonged exhaustion")
                && Has(Council(50, 20, 5).Members.Single(), "Prolonged exhaustion"),
                "Actual council reads the configured period rather than hardcoding day one hundred");

            var termUtility = AccessTools.Method(ratifierType, "ApplyTermUtility");
            var storylineGuard = AccessTools.Method(typeof(ForeignTreatyBehavior), "TryCancelStorylineProtectedProposal");
            var marriageEffects = AccessTools.Method(typeof(ForeignTreatyBehavior), "ApplyRoyalMarriageTerms");
            try
            {
                // Keep council readiness and accounting real; isolate term-specific campaign lookups.
                Patch(termUtility, nameof(Skip));
                Patch(storylineGuard, nameof(SettlementReached));
                Patch(marriageEffects, nameof(SettlementReached));
                period.SetValue(settings, 100);
                _day = 137f;
                _rallyBonus = 0f;
                var loser = Blank<Kingdom>();
                loser.StringId = "opponent";
                var war = new WarScoreRecord("budget_war", kingdom.StringId, loser.StringId, 37f, null);
                AccessTools.Field(typeof(WarScoreRecord), "_score").SetValue(war, 20f);
                var rows = Array.CreateInstance(rowType, 1);
                rows.SetValue(Activator.CreateInstance(rowType, new object[] { _clan.StringId, 0f, 100f, 100f }), 0);
                object snapshot = snapshotConstructor.Invoke(new object[] { rows });
                var behavior = Blank<ForeignTreatyBehavior>();
                var budgetGuard = AccessTools.Method(typeof(ForeignTreatyBehavior), "HasSettlementBudget");
                var settlementMethods = new[] { "ApplyTreaty", "ApplyTreatyCore" }
                    .Select(name => AccessTools.Method(typeof(ForeignTreatyBehavior), name)).ToArray();
                bool HasBudget(TreatyProposalRecord proposal, out string report)
                {
                    object[] args = { proposal, null };
                    bool result = (bool)budgetGuard.Invoke(null, args);
                    report = (string)args[1];
                    return result;
                }
                TreatyCouncilEvaluation BudgetCouncil(TreatyProposalRecord proposal, bool winnerSide)
                    => (TreatyCouncilEvaluation)evaluate.Invoke(service,
                        new object[] { war, proposal, winnerSide ? kingdom : loser, winnerSide, snapshot });
                TreatyTermRecord Demand(int cost) => new TreatyTermRecord(TreatyTermType.Reparations, cost,
                    fromKingdomId: loser.StringId, toKingdomId: kingdom.StringId);
                var offering = new TreatyTermRecord(TreatyTermType.Reparations, 200,
                    fromKingdomId: kingdom.StringId, toKingdomId: loser.StringId, wasVoluntaryOffering: true);
                const string unreasonable = "Enemy making unreasonable demands";
                foreach (bool reciprocal in new[] { false, true })
                foreach (bool priced in new[] { false, true })
                foreach (bool forcedPact in new[] { false, true })
                {
                    var pactProposal = new TreatyProposalRecord(war.WarKey, kingdom.StringId, loser.StringId,
                        kingdom.StringId, _day, 100, forcedPact);
                    pactProposal.AddTerm(new TreatyTermRecord(TreatyTermType.HostagePeace, priced ? 55 : 30,
                        durationDays: 300, fromKingdomId: loser.StringId, toKingdomId: kingdom.StringId,
                        hostageTier: 1, hostageDurationPriced: priced));
                    if (reciprocal)
                        pactProposal.AddTerm(new TreatyTermRecord(TreatyTermType.HostagePeace, priced ? 37 : 12,
                            durationDays: 300, fromKingdomId: kingdom.StringId, toKingdomId: loser.StringId,
                            hostageTier: 4, wasVoluntaryOffering: true, hostageDurationPriced: priced));
                    // Spend the remaining budget so this isolated fixture needs no live realm search.
                    pactProposal.AddTerm(Demand(100 - pactProposal.UsedWarScore));
                    foreach (bool winnerSide in new[] { false, true })
                    {
                        var member = BudgetCouncil(pactProposal, winnerSide).Members.Single();
                        check(Near(Amount(member, "Lengthy peace commitment"), priced ? -110 : 0)
                            && Near(Amount(member, "Time to recover"), priced ? 15 : 0)
                            && member.Reasons.Count(r => r.Label == Label("Lengthy peace commitment")) == (priced ? 1 : 0),
                            $"Actual council prices duration once: reciprocal={reciprocal}; new={priced}; forced={forcedPact}; winner={winnerSide}");
                        check(!forcedPact || member.Utility >= 200,
                            "Duration assessment preserves deliberate forced-capitulation behavior");
                    }
                }
                check(!HasBudget(null, out _), "Settlement budget guard rejects a missing proposal");

                foreach (bool forced in new[] { false, true })
                {
                    var proposal = new TreatyProposalRecord(war.WarKey, kingdom.StringId, loser.StringId,
                        kingdom.StringId, _day, 20, forced);
                    proposal.ReplaceTerms(new[] { Demand(31), offering });
                    check(proposal.UsedWarScore == 21 && proposal.IsOverBudget,
                        "Council fixture exceeds net budget by one after capped offering credit");
                    check(!HasBudget(proposal, out var report) && report == "the draft exceeds the available war score",
                        "Settlement budget guard rejects excess regardless of forced status: " + forced);

                    var blockedWinner = BudgetCouncil(proposal, true);
                    foreach (bool winnerSide in new[] { true, false })
                    {
                        var council = winnerSide ? blockedWinner : BudgetCouncil(proposal, false);
                        var member = council.Members.Single();
                        check(council.IsBudgetBlocked && !council.IsRatified,
                            "Actual council blocks ratification on both sides; forced=" + forced + "; winner=" + winnerSide);
                        check(winnerSide ? !Has(member, unreasonable)
                            : member.Reasons.Count(reason => reason.Label == Label(unreasonable)) == 1
                                && Near(Amount(member, unreasonable), -1000f),
                            "Only the losing council receives exactly one -1000 unreasonable-demand reason");
                        check(!Has(member, "We dictate the peace") && !Has(member, "We are beaten"),
                            "Over-budget councils receive no forced capitulation boost on either side");
                        check(Near(member.Reasons.Sum(reason => reason.Amount), member.Utility),
                            "Budget reason remains part of the actual council utility total");
                        if (winnerSide)
                            check(Near(member.Utility, Part(Assessment(100f, 20f, 0f), "Total"))
                                && member.Utility >= 10f && !council.SoleRulerAccepts,
                                "Winner utility remains favorable and unpenalized, but budget still prevents ratification: utility="
                                + member.Utility + "; expected=" + Part(Assessment(100f, 20f, 0f), "Total")
                                + "; soleAccepts=" + council.SoleRulerAccepts + "; reasons="
                                + string.Join("; ", member.Reasons.Select(reason => reason.Label + "=" + reason.Amount)));
                        foreach (bool forcedAcceptance in new[] { false, true })
                        foreach (bool authorizeOverride in new[] { false, true })
                            check(!behavior.WouldCouncilAccept(winnerSide ? kingdom : loser, council,
                                forcedAcceptance, authorizeOverride, out bool usesOverride) && !usesOverride,
                                "Budget block defeats acceptance, forced acceptance and authorized override");
                    }

                    var originalTerms = proposal.Terms.ToArray();
                    var originalState = proposal.State;
                    int originalRevision = proposal.DraftRevision;
                    _settlementEntryCalls = 0;
                    foreach (var apply in settlementMethods)
                    {
                        check(!(bool)apply.Invoke(behavior, new object[] { war, proposal, kingdom, loser })
                            && _settlementEntryCalls == 0,
                            apply.Name + " rejects excess before reaching settlement work; forced=" + forced);
                        check(proposal.State == originalState && proposal.DraftRevision == originalRevision
                            && proposal.Terms.SequenceEqual(originalTerms),
                            apply.Name + " budget failure leaves proposal state and terms untouched");
                    }

                    proposal.ReplaceTerms(new[] { Demand(30), offering });
                    check(proposal.UsedWarScore == 20 && !proposal.IsOverBudget && HasBudget(proposal, out _),
                        "Restoring exact net budget clears the settlement guard despite gross demands exceeding budget");
                    foreach (bool winnerSide in new[] { true, false })
                    {
                        var council = BudgetCouncil(proposal, winnerSide);
                        var member = council.Members.Single();
                        check(!council.IsBudgetBlocked && council.IsRatified && !Has(member, unreasonable),
                            "Reevaluating the restored draft removes unreasonable demands and both councils' budget blocks");
                        string forcedReason = winnerSide ? "We dictate the peace" : "We are beaten";
                        check(Has(member, forcedReason) == forced
                            && (!forced || Amount(member, forcedReason) >= 50f),
                            "Forced capitulation boost returns only after budget is restored");
                        check(behavior.WouldCouncilAccept(winnerSide ? kingdom : loser, council,
                            false, false, out bool usesOverride) && !usesOverride,
                            "Restored councils accept normally without force or override");
                    }

                    // Positive controls prove the sentinels are live without executing settlement effects.
                    foreach (var apply in settlementMethods)
                    {
                        bool reachedBoundary = false;
                        int previousCalls = _settlementEntryCalls;
                        try { apply.Invoke(behavior, new object[] { war, proposal, kingdom, loser }); }
                        catch (TargetInvocationException ex) when (ex.InnerException is InvalidOperationException
                            && ex.InnerException.Message == "Readiness fixture settlement boundary reached")
                        { reachedBoundary = true; }
                        check(reachedBoundary && _settlementEntryCalls == previousCalls + 1,
                            apply.Name + " proceeds past its budget guard when the same draft is restored");
                    }
                }
            }
            finally
            {
                foreach (var method in new[] { termUtility, storylineGuard, marriageEffects })
                    harmony.Unpatch(method, HarmonyPatchType.Prefix, harmony.Id);
                _settlementEntryCalls = 0;
            }
            check(new[] { termUtility, storylineGuard, marriageEffects }.All(method =>
                Harmony.GetPatchInfo(method)?.Owners.Contains(harmony.Id) != true),
                "Budget-only utility isolation and settlement sentinels are removed before leaving the new test block");
        }
        finally
        {
            try { harmony.UnpatchAll(harmony.Id); }
            finally
            {
                settingsCache.SetValue(null, previousSettings);
                _clan = _rallyClan = null;
                _rallyWar = null;
                _day = _rallyBonus = _rallyBaseline = 0f;
            }
        }
        check(patched.All(method => Harmony.GetPatchInfo(method)?.Owners.Contains(harmony.Id) != true),
            "Council fixture leaves no owned Harmony patches behind");
        CheckEntryPointSources(check);
    }

    private static void CheckEntryPointSources(Action<bool, string> check)
    {
        string root = FindRoot();
        var comparison = new Regex(@"bool\s+preferWhitePeace\s*=\s*Math\.Abs\(war\.Score\)\s*<=\s*(?:BellumCivileConstants|C)\.WarScoreWhitePeaceMaximumScore\s*;");
        string parley = File.ReadAllText(Path.Combine(root, "Patches", "PeaceParleyEntryPointPatches.cs"));
        string diplomacy = File.ReadAllText(Path.Combine(root, "Patches", "DiplomacyRuntimeCompatibilityPatches.cs"));
        check(comparison.Matches(parley).Count == 5 && comparison.Matches(diplomacy).Count == 1,
            "All six entrypoint preferences include the white-peace maximum score boundary");
        check(!parley.Contains("TreatyVoluntaryParleyMinimumScore") && !diplomacy.Contains("TreatyVoluntaryParleyMinimumScore"),
            "Entrypoint white-peace preferences no longer use the voluntary-parley minimum");
        foreach (string path in new[] { "PeaceParleyEntryPointPatches.cs", "DiplomacyRuntimeCompatibilityPatches.cs", "PeaceDeclarationDiplomacyPredictionPatch.cs" })
        {
            string source = File.ReadAllText(Path.Combine(root, "Patches", path));
            check(!source.Contains("WarPeaceMinimumDurationHelper") && !source.Contains("MinimumWarDurationDays"),
                "No obsolete fixed-duration gate remains in " + path);
        }
    }

    private static string FindRoot()
    {
        foreach (string start in new[] { Environment.CurrentDirectory, AppDomain.CurrentDomain.BaseDirectory })
            for (var directory = new DirectoryInfo(start); directory != null; directory = directory.Parent)
                if (File.Exists(Path.Combine(directory.FullName, "BellumCivile.csproj"))) return directory.FullName;
        throw new InvalidOperationException("Bellum source root not found");
    }
}
