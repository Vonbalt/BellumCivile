using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using BellumCivile;
using HarmonyLib;
using TaleWorlds.CampaignSystem;

internal static class OverBudgetDraftTests
{
    private static readonly Dictionary<string, Kingdom> Realms = new Dictionary<string, Kingdom>();
    private static int _availableGold;

    private static bool EmptyCandidates(MethodBase __originalMethod, ref object __result)
    {
        var item = ((MethodInfo)__originalMethod).ReturnType.GetGenericArguments()[0];
        __result = Activator.CreateInstance(typeof(List<>).MakeGenericType(item));
        return false;
    }

    private static bool Resolve(string __0, ref Kingdom __result)
    {
        Realms.TryGetValue(__0 ?? string.Empty, out __result);
        return false;
    }

    private static bool AvailableGold(ref int __result)
    {
        __result = _availableGold;
        return false;
    }

    internal static void Run(Action<bool, string> check)
    {
        var assembly = typeof(TreatyTermRecord).Assembly;
        var service = assembly.GetType("BellumCivile.TreatyDraftService");
        var validator = AccessTools.Method(service, "TryValidateAndNormalize");
        var accounting = assembly.GetType("BellumCivile.TreatyWarScoreAccounting");
        var costs = assembly.GetType("BellumCivile.TreatyTermCostModel");
        var harmony = new Harmony("bellum.test.over_budget_draft");
        var patched = new List<MethodBase>();
        void Patch(Type type, string method, string prefix)
        {
            var target = AccessTools.Method(type, method);
            harmony.Patch(target, prefix: new HarmonyMethod(typeof(OverBudgetDraftTests), prefix));
            patched.Add(target);
        }

        Realms.Clear();
        foreach (string id in new[] { "winner", "loser" })
        {
            var realm = (Kingdom)FormatterServices.GetUninitializedObject(typeof(Kingdom));
            realm.StringId = id;
            Realms.Add(id, realm);
        }
        var war = new WarScoreRecord("winner|loser", "winner", "loser", 0,
            new WarScoreFiefSnapshotRecord[0]);
        TreatyTermRecord Term(TreatyTermType type, int cost, bool reverse = false, bool offering = false)
            => new TreatyTermRecord(type, cost, fromKingdomId: reverse ? "winner" : "loser",
                toKingdomId: reverse ? "loser" : "winner", wasVoluntaryOffering: offering);
        TreatyProposalRecord Proposal(int budget, bool forced = false)
            => new TreatyProposalRecord(war.WarKey, "winner", "loser", "winner", 0, budget, forced);
        bool Validate(TreatyProposalRecord proposal, IEnumerable<TreatyTermRecord> terms,
            out List<TreatyTermRecord> normalized, out string report, bool repair = false, bool? allow = null)
        {
            // Missing exercises the optional default, not an explicitly supplied strict value.
            object[] args = { war, proposal, Realms["winner"], Realms["loser"], terms,
                null, null, repair, allow.HasValue ? (object)allow.Value : Type.Missing };
            bool result = (bool)validator.Invoke(null, args);
            normalized = (List<TreatyTermRecord>)args[5];
            report = (string)args[6];
            return result;
        }
        object Summary(IEnumerable<TreatyTermRecord> terms, int budget)
            => AccessTools.Method(accounting, "Calculate").Invoke(null, new object[] { terms, "winner", budget });
        int Value(object summary, string property)
            => (int)AccessTools.Property(summary.GetType(), property).GetValue(summary);

        try
        {
            // Isolate campaign availability while leaving validation, normalization and accounting real.
            Patch(service, "GetAvailableFiefTransfers", nameof(EmptyCandidates));
            Patch(assembly.GetType("BellumCivile.ClientWarTerritory"), "Candidates", nameof(EmptyCandidates));
            Patch(service, "GetAvailablePrisonerReleases", nameof(EmptyCandidates));
            Patch(service, "ResolveKingdom", nameof(Resolve));
            Patch(service, "CalculateCollectiveAvailableGold", nameof(AvailableGold));
            _availableGold = int.MaxValue;

            var parameters = validator.GetParameters();
            check(parameters.Length == 9 && parameters[7].Name == "repairStateDrift"
                && parameters[8].Name == "allowOverBudget" && parameters[8].IsOptional
                && Equals(parameters[8].DefaultValue, false), "Over-budget permission follows repair and defaults to strict");

            foreach (bool forced in new[] { false, true })
            foreach (bool repair in new[] { false, true })
            {
                var proposal = Proposal(5, forced);
                var requested = new[] { Term(TreatyTermType.ConcedeDefeat, 1), Term(TreatyTermType.Reparations, 6) };
                proposal.ReplaceTerms(requested);
                check(!Validate(proposal, requested, out var strict, out var report, repair)
                    && report == "the draft exceeds the available war score",
                    "Default strict rejects excess even during repair; forced=" + forced + "; repair=" + repair);
                check(strict.Count == 2 && strict[0].Type == TreatyTermType.ConcedeDefeat
                    && strict[0].WarScoreCost == 10 && strict[1].WarScoreCost == 6,
                    "Strict budget failure retains all normalized demands without trimming");
                check(Validate(proposal, requested, out var permissive, out report, repair, true)
                    && report == "treaty draft validated" && permissive.Count == 2,
                    "Editing permits only budget excess, without reporting a repair");
                check(Value(Summary(permissive, 5), "UsedWarScore") == 16,
                    "Canonical concession cost, not submitted cost, determines excess");
                check(requested[0].WarScoreCost == 1 && proposal.Terms.Count == 2
                    && proposal.Terms[0].WarScoreCost == 1, "Validation leaves requested and saved draft terms untouched");
                int gold = (int)AccessTools.Method(costs, "GetReparationsForWarScore")
                    .Invoke(null, new object[] { 6 });
                check(permissive[1].GoldAmount == gold && gold > 0,
                    "Normalized financial amounts come from the real cost model");
                check(!Validate(proposal, permissive, out _, out _, repair, false),
                    "Explicit strict validation rejects the editing result");
            }

            foreach (int budget in new[] { 0, 9, 10, 11 })
            {
                check(Validate(Proposal(budget), new[] { Term(TreatyTermType.ConcedeDefeat, 999) },
                    out var normalized, out _) == (budget >= 10) && normalized.Single().WarScoreCost == 10,
                    "Strict boundary uses normalized costs even when submitted cost is inflated");
            }

            foreach (bool allow in new[] { false, true })
            {
                var invalidDrafts = new[]
                {
                    new[] { Term(TreatyTermType.WhitePeace, 0), Term(TreatyTermType.ConcedeDefeat, 10) },
                    new[] { Term(TreatyTermType.ConcedeDefeat, 10), Term(TreatyTermType.HumiliateRuler, 20) },
                    new[] { Term(TreatyTermType.Reparations, 1), Term(TreatyTermType.Reparations, 1) },
                    new[] { Term(TreatyTermType.ConcedeDefeat, 10, true) },
                    new[] { new TreatyTermRecord(TreatyTermType.Reparations, 1,
                        fromKingdomId: "stranger", toKingdomId: "winner") },
                    new[] { Term(TreatyTermType.Reparations, 0) },
                    new[] { Term((TreatyTermType)int.MaxValue, 1) },
                    new[] { new TreatyTermRecord(TreatyTermType.TransferFief, 1, settlementId: "unavailable",
                        fromKingdomId: "loser", toKingdomId: "winner") },
                    new[] { new TreatyTermRecord(TreatyTermType.ReleasePrisoner, 1, heroId: "unavailable",
                        fromKingdomId: "loser", toKingdomId: "winner") },
                    new[] { new TreatyTermRecord(TreatyTermType.HostagePeace, 1) }
                };
                foreach (var invalid in invalidDrafts)
                    check(!Validate(Proposal(0), invalid, out _, out var reason, allow: allow)
                        && !string.IsNullOrWhiteSpace(reason) && reason != "the draft exceeds the available war score",
                        "Budget permission does not bypass invalid shape or impossible transfer: " + invalid[0].Type);

                check(!Validate(Proposal(0), new[] { Term(TreatyTermType.Reparations, 2),
                    Term(TreatyTermType.Reparations, 3, true) }, out _, out var report, allow: allow)
                    && report == "reciprocal prisoner and wealth requests must be matched by equivalent exchanges",
                    "Unmatched reciprocal wealth is rejected regardless of budget permission");
                check(Validate(Proposal(0), new[] { Term(TreatyTermType.Reparations, 3),
                    Term(TreatyTermType.Reparations, 3, true) }, out var matched, out _, allow: allow)
                    && Value(Summary(matched, 0), "UsedWarScore") == 0,
                    "Matched reciprocal exchanges retain full credit in both modes");

                _availableGold = 0;
                foreach (var type in new[] { TreatyTermType.Reparations, TreatyTermType.Tribute })
                    check(!Validate(Proposal(0), new[] { Term(type, 1) }, out _, out report, allow: allow)
                        && report.Contains("cannot finance"), "Permissive budget does not create affordable " + type);
                _availableGold = int.MaxValue;
            }

            foreach (int budget in new[] { 20, 100, 200 })
            {
                int cap = Math.Min(50, budget / 2);
                foreach (int excess in new[] { 0, 1 })
                {
                    var terms = new[] { Term(TreatyTermType.Reparations, budget + cap + excess),
                        Term(TreatyTermType.Reparations, 200, true, true) };
                    var proposal = Proposal(budget);
                    check(Validate(proposal, terms, out var normalized, out _, allow: true),
                        "Editing accepts valid demands with voluntary offerings");
                    var summary = Summary(normalized, budget);
                    proposal.ReplaceTerms(normalized);
                    check(Value(summary, "RawOfferingCredit") == 100
                        && Value(summary, "AppliedOfferingCredit") == cap
                        && Value(summary, "AppliedReciprocalExchangeCredit") == 0
                        && Value(summary, "UsedWarScore") == budget + excess
                        && proposal.UsedWarScore == budget + excess,
                        "Validator output and proposal reuse proportional and absolute offering caps");
                    check(Validate(proposal, normalized, out var strict, out _, repair: true) == (excess == 0)
                        && strict.Count == 2, "Strict repair respects net capped budget without trimming either side");
                }
            }
        }
        finally
        {
            harmony.UnpatchAll(harmony.Id);
            Realms.Clear();
            _availableGold = 0;
        }
        check(patched.All(method => Harmony.GetPatchInfo(method)?.Owners.Contains(harmony.Id) != true),
            "Over-budget fixture removes all owned Harmony patches");
    }
}
