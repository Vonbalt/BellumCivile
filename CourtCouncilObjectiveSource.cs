using System;
using System.Collections.Generic;
using System.Linq;
using BellumCivile.Behaviors;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Election;

namespace BellumCivile
{
    // Facts are shared only within one realm's synchronous selection pass.
    internal sealed class CourtCouncilSelectionContext
    {
        internal readonly Kingdom Realm;
        internal readonly PrivyCouncilBehavior Council;
        private readonly Dictionary<PrivyCouncilOffice, List<Clan>> _candidates = new Dictionary<PrivyCouncilOffice, List<Clan>>();
        private readonly Dictionary<Tuple<PrivyCouncilOffice, Clan, Clan>, float> _support = new Dictionary<Tuple<PrivyCouncilOffice, Clan, Clan>, float>();
        private readonly Dictionary<Tuple<PrivyCouncilOffice, Clan>, float> _merit = new Dictionary<Tuple<PrivyCouncilOffice, Clan>, float>();
        private readonly Dictionary<PrivyCouncilOffice, List<Clan>> _shortlists = new Dictionary<PrivyCouncilOffice, List<Clan>>();
        internal readonly Dictionary<PrivyCouncilOffice, bool> Availability = new Dictionary<PrivyCouncilOffice, bool>();
        internal CourtCouncilSelectionContext(Kingdom realm)
        { Realm = realm; Council = Campaign.Current?.GetCampaignBehavior<PrivyCouncilBehavior>(); }

        internal List<Clan> Candidates(PrivyCouncilOffice office)
        {
            if (!_candidates.TryGetValue(office, out var value))
                _candidates[office] = value = Council.GetAppointmentCandidatesForVote(Realm, office).ToList();
            return value;
        }
        internal float Support(PrivyCouncilOffice office, Clan voter, Clan candidate)
        {
            var key = Tuple.Create(office, voter, candidate);
            if (!_support.TryGetValue(key, out var value))
                _support[key] = value = Council.CalculateAppointmentSupport(Realm, voter, candidate, office);
            return value;
        }
        internal float Merit(PrivyCouncilOffice office, Clan candidate)
        {
            if (candidate == null) return 0;
            var key = Tuple.Create(office, candidate);
            if (!_merit.TryGetValue(key, out var value))
                _merit[key] = value = Council.CalculateAppointmentMerit(Realm, candidate, office);
            return value;
        }
        internal List<Clan> Shortlist(PrivyCouncilOffice office, Clan formalNominee = null) =>
            CouncilAppointmentNominationHelper.BuildShortlist(RankedCandidates(office), Candidates(office), formalNominee);

        private List<Clan> RankedCandidates(PrivyCouncilOffice office)
        {
            if (_shortlists.TryGetValue(office, out var result)) return result;
            var counts = new Dictionary<Clan, int>();
            var scores = new Dictionary<Clan, float>();
            foreach (var voter in Realm.Clans.Where(c => CouncilAppointmentNominationHelper.IsValidVoter(c, Realm)))
            {
                Clan best = null;
                float bestScore = float.MinValue;
                foreach (var candidate in Candidates(office))
                {
                    float score = Support(office, voter, candidate);
                    if (best == null || score > bestScore || Math.Abs(score - bestScore) < .01f
                        && string.CompareOrdinal(candidate.StringId, best.StringId) < 0)
                    { best = candidate; bestScore = score; }
                }
                if (best == null) continue;
                counts[best] = counts.TryGetValue(best, out var count) ? count + 1 : 1;
                scores[best] = scores.TryGetValue(best, out var total) ? total + bestScore : bestScore;
            }
            result = counts.Keys.OrderByDescending(c => counts[c]).ThenByDescending(c => scores[c])
                .ThenByDescending(c => FeudalPoliticalWeightHelper.GetHighestHeldTitleRank(c).HasValue
                    ? (int)FeudalPoliticalWeightHelper.GetHighestHeldTitleRank(c).Value : -1)
                .ThenBy(c => c.StringId).Take(3).ToList();
            return _shortlists[office] = result;
        }
    }

    internal sealed class CourtCouncilObjectiveSource : ICourtAgendaObjectiveSource
    {
        internal const string CouncilKind = "council_appointment";
        public string Kind => CouncilKind;
        internal static int Cost(Clan sponsor) => Campaign.Current?.GetCampaignBehavior<PrivyCouncilBehavior>()?
            .GetFactionMotionInfluenceCost(sponsor, PrivyCouncilBehavior.AppointmentProposalInfluenceCost)
            ?? PrivyCouncilBehavior.AppointmentProposalInfluenceCost;

        internal static bool IsCaptivityAppointment(CourtCouncilSelectionContext facts, CourtObjectiveOwner owner, PrivyCouncilOffice office) =>
            owner.Faction == null && owner.Sponsor == facts.Realm.RulingClan && owner.Sponsor != Clan.PlayerClan
            && facts.Council?.GetOfficeRecord(facts.Realm, office)?.IsCaptivityVacancy == true;

        public IEnumerable<CourtObjectiveCandidate> FindCandidates(CourtTermContext context, CourtObjectiveOwner owner)
        {
            if (!context.ManualSelection && !NpcInfluenceBudgetService.CanAfford(owner.Sponsor, Cost(owner.Sponsor), NpcInfluenceExpenseKind.Discretionary)) yield break;
            var facts = context.Council;
            if (facts.Council == null || CourtAgendaBehavior.Current?.HasCouncilReservation(context.Realm, context.PlayerAgenda) == true
                || CouncilAppointmentDeliberationBehavior.Current?.HasPendingAppointment(context.Realm) == true
                || context.Realm.UnresolvedDecisions.OfType<PrivyCouncilAppointmentDecision>().Any()) yield break;
            foreach (var record in facts.Council.GetOfficeRecords(context.Realm))
            {
                if (!OfficeAvailableFor(context, owner, record.Office)) continue;
                var incumbent = facts.Council.GetOfficeHolder(context.Realm, record.Office);
                if (context.ManualSelection)
                {
                    foreach (var candidate in facts.Candidates(record.Office).Where(c => c != incumbent))
                        yield return new CourtObjectiveCandidate(record.Office.ToString(), incumbent == null ? "fill" : "replace", candidate.StringId);
                    continue;
                }
                var members = OwnerVoters(context, owner);
                var preferred = facts.Shortlist(record.Office).Where(c => c != incumbent)
                    .OrderByDescending(c => members.Average(v => facts.Support(record.Office, v, c)))
                    .ThenByDescending(c => facts.Merit(record.Office, c)).ThenBy(c => c.StringId).FirstOrDefault();
                if (preferred != null)
                    yield return new CourtObjectiveCandidate(record.Office.ToString(), incumbent == null ? "fill" : "replace", preferred.StringId);
            }
        }

        private static List<Clan> OwnerVoters(CourtTermContext context, CourtObjectiveOwner owner)
        {
            var voters = owner.Faction?.Members.Where(c => CourtAgendaBehavior.Eligible(c, context.Realm)).Distinct().ToList();
            return voters != null && voters.Count > 0 ? voters : new List<Clan> { owner.Sponsor };
        }

        internal static bool OfficeAvailable(CourtCouncilSelectionContext facts, PrivyCouncilOffice office)
        {
            if (!facts.Availability.TryGetValue(office, out bool available))
                facts.Availability[office] = available = CheckOfficeAvailable(facts, office);
            return available;
        }

        private static bool CheckOfficeAvailable(CourtCouncilSelectionContext facts, PrivyCouncilOffice office)
        {
            var council = facts.Council;
            var record = council?.GetOfficeRecord(facts.Realm, office);
            if (record == null || !council.IsOfficeUnlocked(facts.Realm, office)
                || CourtAgendaBehavior.Current?.IsCouncilOfficeSettled(facts.Realm, office) == true) return false;
            var incumbent = council.GetOfficeHolder(facts.Realm, office);
            if (incumbent == null) return record.IsCaptivityVacancy || council.GetVacancyDays(record) > PrivyCouncilBehavior.VacancyGraceDays
                && (office > PrivyCouncilOffice.Spymaster || !council.IsVacancyExcused(facts.Realm, office));
            return (council.GetOfficeTenureDays(facts.Realm, office) >= BellumCivileOptions.CourtTermDays || record.Controversy >= 60)
                && (council.GetOfficeSupportPercent(facts.Realm, office) < 50 || record.Controversy >= 60);
        }

        private static bool OfficeAvailableFor(CourtTermContext context, CourtObjectiveOwner owner, PrivyCouncilOffice office) =>
            CourtAgendaBehavior.IsDirectCrownBusiness(context.PlayerAgenda) && owner.Sponsor == Clan.PlayerClan
                ? context.Council.Council?.IsOfficeUnlocked(context.Realm, office) == true
                : OfficeAvailable(context.Council, office);

        public CourtObjectiveEvaluation EvaluateCandidate(CourtTermContext context, CourtObjectiveOwner owner, CourtObjectiveCandidate candidate)
        {
            var facts = context.Council;
            if (!Enum.TryParse(candidate.TargetId, out PrivyCouncilOffice office) || !Enum.IsDefined(typeof(PrivyCouncilOffice), office)
                || !OfficeAvailableFor(context, owner, office)) return Reject("office_unavailable");
            var nominee = context.FindClan(candidate.BeneficiaryId ?? "");
            var incumbent = facts.Council.GetOfficeHolder(context.Realm, office);
            if (nominee == null || nominee == incumbent || !facts.Candidates(office).Contains(nominee)
                || candidate.ActionId != (incumbent == null ? "fill" : "replace")) return Reject("candidate_or_action_invalid");
            if (context.ManualSelection) return new CourtObjectiveEvaluation(true, true, new CourtObjectiveWeight(1), "player_eligible");
            if (IsCaptivityAppointment(facts, owner, office))
            {
                bool funded = NpcInfluenceBudgetService.CanAfford(owner.Sponsor, Cost(owner.Sponsor), NpcInfluenceExpenseKind.CrownEmergency);
                return new CourtObjectiveEvaluation(true, funded, new CourtObjectiveWeight(1), "captivity_emergency; affordable=" + funded);
            }
            var members = OwnerVoters(context, owner);
            float preference = members.Average(v => facts.Support(office, v, nominee));
            float meritGap = facts.Merit(office, nominee) - facts.Merit(office, incumbent);
            var record = facts.Council.GetOfficeRecord(context.Realm, office);
            bool shortlisted = facts.Shortlist(office, nominee).Contains(nominee);
            bool credible = shortlisted && preference > 0
                && (incumbent == null || preference > members.Average(v => facts.Support(office, v, incumbent)))
                && (incumbent == null || meritGap >= 8 || record.Controversy >= 60);
            bool affordable = NpcInfluenceBudgetService.CanAfford(owner.Sponsor, Cost(owner.Sponsor), NpcInfluenceExpenseKind.Discretionary);
            bool favored = owner.Faction?.Type == FactionType.Glory && office == PrivyCouncilOffice.Marshal
                || owner.Faction?.Type == FactionType.Liberty && office == PrivyCouncilOffice.Seneschal
                || owner.Faction?.Type == FactionType.Nobility && (office == PrivyCouncilOffice.Spymaster || office >= PrivyCouncilOffice.FirstAdvisor);
            var weight = CourtCouncilWeights.Calculate(incumbent == null, facts.Council.GetVacancyDays(record),
                record.Controversy, meritGap, favored);
            return new CourtObjectiveEvaluation(true, credible && affordable, weight,
                $"preferred={nominee.StringId}; preference={preference}; merit_gap={meritGap}; shortlisted={shortlisted}; affordable={affordable}");
        }
        private static CourtObjectiveEvaluation Reject(string reason) => new CourtObjectiveEvaluation(false, false, new CourtObjectiveWeight(0), reason);

        public void ApplySelection(CourtAgendaRecord agenda, CourtObjectiveChoice choice)
        {
            var previous = agenda.GetObjective();
            agenda.ObjectiveData = new CourtObjectiveRecord { Kind = Kind, TargetId = choice.Candidate.TargetId, ActionId = choice.Candidate.ActionId };
            if (previous.HasTermSnapshot) agenda.ObjectiveData.FreezeTerm(previous.SelectedDay, previous.DeadlineDay);
            agenda.PreferredCouncilCandidate = Clan.All.FirstOrDefault(c => c.StringId == choice.Candidate.BeneficiaryId);
            agenda.CouncilMotionId = Guid.NewGuid().ToString("N");
            agenda.OriginalHolder = Campaign.Current.GetCampaignBehavior<PrivyCouncilBehavior>()?
                .GetOfficeHolder(agenda.Realm, (PrivyCouncilOffice)Enum.Parse(typeof(PrivyCouncilOffice), choice.Candidate.TargetId));
            agenda.PolicyId = null;
        }
    }
}
