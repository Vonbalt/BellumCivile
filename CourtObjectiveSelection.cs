using System;
using System.Collections.Generic;
using System.Linq;

namespace BellumCivile
{
    internal sealed class CourtObjectiveWeight
    {
        internal double Base { get; }
        internal double Urgency { get; }
        internal double Benefit { get; }
        internal double Preference { get; }
        internal double RepetitionPenalty { get; }
        internal double Total { get; }

        internal CourtObjectiveWeight(double baseline, double urgency = 0, double benefit = 0,
            double preference = 0, double repetitionPenalty = 0)
        {
            Base = Finite(baseline);
            Urgency = Finite(urgency);
            Benefit = Finite(benefit);
            Preference = Finite(preference);
            RepetitionPenalty = Math.Max(0, Finite(repetitionPenalty));
            double total = Base + Urgency + Benefit + Preference - RepetitionPenalty;
            Total = double.IsNaN(total) ? 0 : Math.Max(0, Math.Min(1000, total));
        }

        private static double Finite(double n) => double.IsNaN(n) || double.IsInfinity(n) ? 0 : n;
        public override string ToString() => FormattableString.Invariant(
            $"base={Base}; urgency={Urgency}; benefit={Benefit}; preference={Preference}; repetition={RepetitionPenalty}; total={Total}");
    }

    internal sealed class CourtObjectiveCandidate
    {
        internal string TargetId { get; }
        internal string ActionId { get; }
        internal string BeneficiaryId { get; }
        internal CourtObjectiveCandidate(string targetId, string actionId, string beneficiaryId = null)
        {
            if (string.IsNullOrWhiteSpace(targetId)) throw new ArgumentException("A concrete target is required.", nameof(targetId));
            if (string.IsNullOrWhiteSpace(actionId)) throw new ArgumentException("An action is required.", nameof(actionId));
            TargetId = targetId;
            ActionId = actionId;
            BeneficiaryId = beneficiaryId;
        }
    }

    internal sealed class CourtObjectiveEvaluation
    {
        internal bool Eligible { get; }
        internal bool Viable { get; }
        internal CourtObjectiveWeight Weight { get; }
        internal string Reason { get; }
        internal bool Selectable => Eligible && Viable && Weight.Total > 0;

        internal CourtObjectiveEvaluation(bool eligible, bool viable, CourtObjectiveWeight weight, string reason)
        {
            Eligible = eligible;
            Viable = viable;
            Weight = weight ?? throw new ArgumentNullException(nameof(weight));
            Reason = reason ?? string.Empty;
        }
    }

    internal interface ICourtObjectiveSource<TContext, TOwner>
    {
        string Kind { get; }
        IEnumerable<CourtObjectiveCandidate> FindCandidates(TContext context, TOwner owner);
        CourtObjectiveEvaluation EvaluateCandidate(TContext context, TOwner owner, CourtObjectiveCandidate candidate);
    }

    internal sealed class CourtObjectiveChoice
    {
        internal string Kind { get; }
        internal string SelectionFamily { get; }
        internal CourtObjectiveCandidate Candidate { get; }
        internal CourtObjectiveEvaluation Evaluation { get; }
        internal CourtObjectiveChoice(string kind, CourtObjectiveCandidate candidate, CourtObjectiveEvaluation evaluation,
            string selectionFamily = null)
        {
            Kind = kind;
            SelectionFamily = selectionFamily ?? kind;
            Candidate = candidate;
            Evaluation = evaluation;
        }
    }

    internal sealed class CourtObjectiveSelector<TContext, TOwner>
    {
        private readonly List<ICourtObjectiveSource<TContext, TOwner>> _sources = new List<ICourtObjectiveSource<TContext, TOwner>>();
        private readonly Dictionary<string, string> _families = new Dictionary<string, string>();

        internal void Register(ICourtObjectiveSource<TContext, TOwner> source, string selectionFamily = null)
        {
            if (source == null || string.IsNullOrWhiteSpace(source.Kind)) throw new ArgumentException("An objective kind is required.");
            if (_sources.Any(s => s.Kind == source.Kind)) throw new ArgumentException("Duplicate objective kind: " + source.Kind);
            if (selectionFamily != null && string.IsNullOrWhiteSpace(selectionFamily))
                throw new ArgumentException("A selection family cannot be blank.", nameof(selectionFamily));
            _sources.Add(source);
            _families.Add(source.Kind, selectionFamily ?? source.Kind);
        }

        internal ICourtObjectiveSource<TContext, TOwner> FindSource(string kind) => _sources.FirstOrDefault(s => s.Kind == kind);

        internal CourtObjectiveChoice Select(TContext context, TOwner owner, Func<double> random,
            Action<CourtObjectiveChoice> diagnostic = null)
            => SelectEvaluated(Evaluate(context, owner, diagnostic), random);

        private List<CourtObjectiveChoice> Evaluate(TContext context, TOwner owner,
            Action<CourtObjectiveChoice> diagnostic)
        {
            var choices = new List<CourtObjectiveChoice>();
            foreach (var source in _sources)
            {
                var seen = new HashSet<Tuple<string, string>>();
                foreach (var candidate in source.FindCandidates(context, owner))
                {
                    if (!seen.Add(Tuple.Create(candidate.TargetId, candidate.ActionId))) continue;
                    var choice = new CourtObjectiveChoice(source.Kind, candidate,
                        source.EvaluateCandidate(context, owner, candidate), _families[source.Kind]);
                    diagnostic?.Invoke(choice);
                    if (choice.Evaluation.Selectable) choices.Add(choice);
                }
            }
            return choices;
        }

        private static CourtObjectiveChoice SelectEvaluated(IEnumerable<CourtObjectiveChoice> choices, Func<double> random)
        {
            var categories = choices.GroupBy(c => c.SelectionFamily).Select(g => g.ToList()).ToList();
            if (categories.Count == 0) return null;
            // The best opportunity sets category weight; extra targets do not buy extra lottery tickets.
            var category = Pick(categories, c => c.Max(x => x.Evaluation.Weight.Total), random);
            // Distinct handlers can share a family without target count favoring one action.
            var kinds = category.GroupBy(c => c.Kind).Select(g => g.ToList()).ToList();
            var kind = Pick(kinds, c => c.Max(x => x.Evaluation.Weight.Total), random);
            return Pick(kind, c => c.Evaluation.Weight.Total, random);
        }

        // Resolve one shared proceeding after every owner has made its tentative choice.
        // Losing sponsors fall back once, without repeating expensive forecasts.
        internal IReadOnlyList<CourtObjectiveChoice> SelectBatch(TContext context, IReadOnlyList<TOwner> owners,
            string exclusiveKind, Func<double> random, Action<TOwner, CourtObjectiveChoice> diagnostic = null)
            => SelectBatch(context, owners, new[] { exclusiveKind }, random, diagnostic);

        internal IReadOnlyList<CourtObjectiveChoice> SelectBatch(TContext context, IReadOnlyList<TOwner> owners,
            IReadOnlyList<string> exclusiveKinds, Func<double> random, Action<TOwner, CourtObjectiveChoice> diagnostic = null)
        {
            var pools = new List<List<CourtObjectiveChoice>>();
            var selected = new List<CourtObjectiveChoice>();
            foreach (var owner in owners)
            {
                var pool = Evaluate(context, owner, c => diagnostic?.Invoke(owner, c));
                pools.Add(pool);
                selected.Add(SelectEvaluated(pool, random));
            }
            var resolved = new HashSet<string>();
            foreach (string exclusiveKind in exclusiveKinds)
            {
                var contenders = Enumerable.Range(0, selected.Count)
                    .Where(i => selected[i] != null && selected[i].Kind == exclusiveKind).ToList();
                resolved.Add(exclusiveKind);
                if (contenders.Count <= 1) continue;
                int winner = Pick(contenders, _ => 1d, random);
                foreach (int loser in contenders.Where(i => i != winner))
                    selected[loser] = SelectEvaluated(pools[loser].Where(c => !resolved.Contains(c.Kind)), random);
            }
            return selected;
        }

        private static T Pick<T>(IReadOnlyList<T> items, Func<T, double> weight, Func<double> random)
        {
            if (items.Count == 1) return items[0];
            double roll = random();
            if (double.IsNaN(roll) || double.IsInfinity(roll) || roll < 0 || roll >= 1)
                throw new ArgumentOutOfRangeException(nameof(random), "Random draws must be in [0, 1).");
            double position = roll * items.Sum(weight);
            foreach (var item in items)
            {
                position -= weight(item);
                if (position < 0) return item;
            }
            return items[items.Count - 1];
        }
    }
}
