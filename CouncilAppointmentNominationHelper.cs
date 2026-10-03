using System;
using System.Collections.Generic;
using System.Linq;
using BellumCivile.Behaviors;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Localization;

namespace BellumCivile
{
    internal sealed class CouncilAppointmentNominationResult
    {
        public Clan Candidate { get; set; }
        public float Score { get; set; }
        public List<string> Reasons { get; } = new List<string>();
    }

    internal sealed class CouncilAppointmentNominationRanking
    {
        public Clan Candidate { get; set; }
        public int Count { get; set; }
        public float Score { get; set; }
    }

    internal static class CouncilAppointmentNominationHelper
    {
        private sealed class WeightedReason
        {
            public string Id;
            public float Weight;
        }

        private sealed class CandidatePool
        {
            internal readonly IReadOnlyList<Clan> Ordered;
            internal readonly HashSet<Clan> Eligible;
            internal CandidatePool(IReadOnlyList<Clan> candidates)
            { Ordered = candidates; Eligible = new HashSet<Clan>(candidates); }
        }

        internal static bool IsValidVoter(Clan clan, Kingdom kingdom)
        {
            return clan != null
                && kingdom != null
                && clan.Kingdom == kingdom
                && clan != kingdom.RulingClan
                && NobleClanEligibilityHelper.IsLiveNobleClan(clan)
                && clan.Leader != null;
        }

        internal static CouncilAppointmentNominationResult ChooseNominee(
            Clan voter,
            Kingdom kingdom,
            PrivyCouncilOffice office,
            PrivyCouncilBehavior council)
        {
            if (!IsValidVoter(voter, kingdom) || council == null)
                return null;

            return ChooseNominee(voter, kingdom, office, council,
                new CandidatePool(council.GetAppointmentCandidatesForVote(kingdom, office)));
        }

        private static CouncilAppointmentNominationResult ChooseNominee(
            Clan voter, Kingdom kingdom, PrivyCouncilOffice office, PrivyCouncilBehavior council,
            CandidatePool candidates)
        {
            if (!IsValidVoter(voter, kingdom)) return null;

            CouncilAppointmentNominationResult best = null;
            foreach (Clan candidate in candidates.Ordered)
            {
                CouncilAppointmentNominationResult result = ScoreCandidate(voter, candidate, kingdom, office, council, candidates);
                if (result == null)
                    continue;

                if (best == null
                    || result.Score > best.Score
                    || (Math.Abs(result.Score - best.Score) < 0.01f
                        && string.CompareOrdinal(result.Candidate.StringId, best.Candidate.StringId) < 0))
                {
                    best = result;
                }
            }

            return best;
        }

        internal static CouncilAppointmentNominationResult ScoreCandidate(
            Clan voter,
            Clan candidate,
            Kingdom kingdom,
            PrivyCouncilOffice office,
            PrivyCouncilBehavior council)
        {
            if (!IsValidVoter(voter, kingdom) || council == null) return null;
            return ScoreCandidate(voter, candidate, kingdom, office, council,
                new CandidatePool(council.GetAppointmentCandidatesForVote(kingdom, office)));
        }

        private static CouncilAppointmentNominationResult ScoreCandidate(
            Clan voter, Clan candidate, Kingdom kingdom, PrivyCouncilOffice office,
            PrivyCouncilBehavior council, CandidatePool candidates)
        {
            if (!IsValidVoter(voter, kingdom)
                || candidate?.Leader == null
                || candidate == kingdom?.RulingClan
                || council == null
                || !candidates.Eligible.Contains(candidate))
            {
                return null;
            }

            float score = council.CalculateAppointmentSupport(kingdom, voter, candidate, office);
            float competence = PrivyCouncilBehavior.CalculateCompetence(candidate.Leader, office);
            List<WeightedReason> reasons = new List<WeightedReason>();

            if (candidate == voter)
                reasons.Add(new WeightedReason { Id = "self", Weight = 35f });

            if (competence >= 70f)
                reasons.Add(new WeightedReason { Id = "competence", Weight = competence * 0.35f });

            int relation = voter.Leader.GetRelation(candidate.Leader);
            if (candidate != voter && relation >= 20)
                reasons.Add(new WeightedReason { Id = "friendship", Weight = relation * 0.55f });

            FactionManagerBehavior factions = Campaign.Current?.GetCampaignBehavior<FactionManagerBehavior>();
            FactionObject voterFaction = factions?.GetIdeologicalFaction(voter);
            FactionObject candidateFaction = factions?.GetIdeologicalFaction(candidate);
            if (voterFaction != null && voterFaction == candidateFaction)
                reasons.Add(new WeightedReason { Id = "same_faction", Weight = 15f });

            if (candidate != voter && MarriageAllianceHelper.HasMarriageAlliance(voter, candidate))
                reasons.Add(new WeightedReason { Id = "marriage", Weight = 15f });

            if (FeudalPoliticalWeightHelper.GetHighestHeldTitleRank(candidate).HasValue)
                reasons.Add(new WeightedReason { Id = "standing", Weight = 10f });

            Clan incumbent = council.GetOfficeHolder(kingdom, office);
            if (candidate == incumbent)
            {
                PrivyCouncilOfficeRecord record = council.GetOfficeRecord(kingdom, office);
                if ((record?.Controversy ?? 100f) < 40f)
                    reasons.Add(new WeightedReason { Id = "continuity", Weight = 15f });
            }

            CouncilAppointmentNominationResult result = new CouncilAppointmentNominationResult
            {
                Candidate = candidate,
                Score = score
            };

            foreach (string reason in reasons
                .OrderByDescending(entry => entry.Weight)
                .Select(entry => entry.Id)
                .Distinct()
                .Take(3))
            {
                result.Reasons.Add(reason);
            }

            if (result.Reasons.Count == 0)
                result.Reasons.Add("judgment");

            return result;
        }

        internal static List<CouncilAppointmentNominationRanking> BuildRanking(
            Kingdom kingdom,
            PrivyCouncilOffice office,
            PrivyCouncilBehavior council,
            Func<Clan, Clan> committedCandidate = null)
        {
            Dictionary<Clan, CouncilAppointmentNominationRanking> ranking =
                new Dictionary<Clan, CouncilAppointmentNominationRanking>();

            if (kingdom == null || council == null)
                return new List<CouncilAppointmentNominationRanking>();

            // Eligibility is stable for this synchronous ranking pass; never retain it across ticks.
            var candidates = new CandidatePool(council.GetAppointmentCandidatesForVote(kingdom, office));

            foreach (Clan voter in kingdom.Clans.Where(clan => IsValidVoter(clan, kingdom)))
            {
                Clan committed = committedCandidate?.Invoke(voter);
                CouncilAppointmentNominationResult nomination = committed != null
                    ? ScoreCandidate(voter, committed, kingdom, office, council, candidates)
                    : ChooseNominee(voter, kingdom, office, council, candidates);
                if (nomination?.Candidate == null)
                    continue;

                if (!ranking.TryGetValue(nomination.Candidate, out CouncilAppointmentNominationRanking entry))
                {
                    entry = new CouncilAppointmentNominationRanking { Candidate = nomination.Candidate };
                    ranking.Add(nomination.Candidate, entry);
                }

                entry.Count++;
                entry.Score += nomination.Score;
            }

            return ranking.Values
                .OrderByDescending(entry => entry.Count)
                .ThenByDescending(entry => entry.Score)
                .ThenByDescending(entry => FeudalPoliticalWeightHelper.GetHighestHeldTitleRank(entry.Candidate).HasValue
                    ? (int)FeudalPoliticalWeightHelper.GetHighestHeldTitleRank(entry.Candidate).Value
                    : -1)
                .ThenBy(entry => entry.Candidate.StringId)
                .ToList();
        }

        internal static TextObject BuildReasonText(IEnumerable<string> reasonIds)
        {
            string primary = reasonIds?.FirstOrDefault() ?? "judgment";
            switch (primary)
            {
                case "self":
                    return new TextObject("{=BC_CouncilNomination_ReasonSelf}My own house is best placed to carry this responsibility.");
                case "competence":
                    return new TextObject("{=BC_CouncilNomination_ReasonCompetence}Their ability is well suited to the duties of this office.");
                case "friendship":
                    return new TextObject("{=BC_CouncilNomination_ReasonFriendship}I know their character and trust their judgment.");
                case "same_faction":
                    return new TextObject("{=BC_CouncilNomination_ReasonFaction}Our allies in court should stand together in this appointment.");
                case "marriage":
                    return new TextObject("{=BC_CouncilNomination_ReasonMarriage}The bonds between our houses give me confidence in them.");
                case "standing":
                    return new TextObject("{=BC_CouncilNomination_ReasonStanding}Their station and reputation give proper weight to their candidacy.");
                case "continuity":
                    return new TextObject("{=BC_CouncilNomination_ReasonContinuity}The present councillor has served without enough cause to disturb the office.");
                default:
                    return new TextObject("{=BC_CouncilNomination_ReasonJudgment}After weighing the candidates, I consider them the soundest choice.");
            }
        }
    }
}
