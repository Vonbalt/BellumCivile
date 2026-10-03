using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;

namespace BellumCivile.Behaviors
{
    // Completed elective ballots and explicit test fixtures share the saved contest lifecycle.
    public sealed partial class ElectiveContestBehavior : CampaignBehaviorBase
    {
        private List<ElectiveContestRecord> _contests = new List<ElectiveContestRecord>();
        public static ElectiveContestBehavior Instance => Campaign.Current?.GetCampaignBehavior<ElectiveContestBehavior>();

        internal bool IsPressingTitleClaim(Clan clan, FeudalTitleRecord title, FeudalTitleBehavior titles)
            => clan != null && title != null && _contests.Any(r => !r.Closed
                && titles.GetRealmSovereignTitle(r.Realm)?.TitleId == title.TitleId
                && r.Candidates.Any(c => c.House == clan && !c.Withdrawn
                    && c.Decision == ElectiveContestDecision.Contest));
        public override void RegisterEvents() => RegisterControlledTestEvents();
        internal bool ResolvePledges(ElectiveContestRecord record)
        {
            if (!_contests.Contains(record) || !record.AccessionCompleted || record.Closed) return false;
            if (record.PledgesResolved) return true;
            if (record.Realm?.IsEliminated != false || record.Realm.RulingClan != record.ElectedHouse
                || record.Pledges.Any(p => p.House?.IsEliminated != false || p.House.Kingdom != record.Realm
                    || p.House.Leader != p.Speaker)) return false;
            var result = ElectiveContestPledgeRules.Resolve(record, Hero.MainHero);
            if (result == null) return false;
            foreach (var pledge in record.Pledges) pledge.AssignedSide = result.Sides[pledge.House];
            foreach (var candidate in record.Candidates)
            {
                candidate.Withdrawn = result.Withdrawn.Contains(candidate.Candidate);
                candidate.PledgedPower = result.Power[candidate.Candidate];
            }
            record.PledgesResolved = true;
            return true;
        }
        public override void SyncData(IDataStore store)
        {
            store.SyncData("BC_ElectiveContests", ref _contests);
            _contests = _contests ?? new List<ElectiveContestRecord>();
        }

        internal static ElectiveContestRecord Snapshot(string id, ElectiveSuccessionRecord ballot,
            Hero predecessor, double day, Func<Hero, ElectiveAcceptanceAssessment> assess)
        {
            var result = new ElectiveContestRecord
            {
                Id = id, Realm = ballot.Realm, ElectedWinner = ballot.Winner, ElectedHouse = ballot.Winner?.Clan,
                Predecessor = predecessor, Law = ballot.Law, Gender = ballot.Gender,
                CapturedDay = day, MandateNumber = ballot.MandateNumber
            };
            foreach (var vote in ballot.Votes)
                result.Votes.Add(new ElectiveContestVote { House = vote.Clan, Speaker = vote.Speaker,
                    Supported = vote.Supported, Weight = vote.Weight });
            foreach (var hero in ballot.Finalists.Where(h => h != null).Distinct())
            {
                var score = assess(hero);
                result.Candidates.Add(new ElectiveContestCandidate
                {
                    Candidate = hero, House = hero.Clan,
                    Support = ballot.TotalWeight > 0 ? ballot.Support(hero) * 100 / ballot.TotalWeight : 0,
                    HasAssessment = score != null && SuccessionChallengeRules.Finite(score.Total),
                    Acceptance = score?.Total ?? 100, Baseline = score?.Baseline ?? 0,
                    Honor = score?.Honor ?? 0, Mercy = score?.Mercy ?? 0, PersonalityCap = score?.PersonalityCap ?? 0,
                    Relations = score?.Relations ?? 0, Claim = score?.Claim ?? 0, Margin = score?.Margin ?? 0,
                    Military = score?.Military ?? 0, Backing = score?.Backing ?? 0,
                    Loyalists = score?.Loyalists ?? 0, Threshold = score?.Threshold ?? 0,
                    Decision = hero == ballot.Winner ? ElectiveContestDecision.Accept : ElectiveContestDecision.Pending
                });
            }
            return result;
        }

        internal void Capture(CrownAccessionRecord accession, ElectiveSuccessionRecord ballot)
        {
            if (!accession.ElectiveElection || accession.Emergency || !ballot.Frozen || ballot.Winner == null) return;
            var previous = _contests.FirstOrDefault(c => c.Id == accession.ElectiveContestId);
            if (previous?.AccessionCompleted == true) return;
            if (string.IsNullOrEmpty(accession.ElectiveContestId)) accession.ElectiveContestId = Guid.NewGuid().ToString("N");
            var snapshot = Snapshot(accession.ElectiveContestId, ballot, accession.Predecessor,
                CampaignTime.Now.ToDays, hero => ElectiveSuccessionBehavior.Instance.Acceptance.Get(ballot, hero));
            snapshot.ManualTest = accession.ElectiveContestTest;
            snapshot.AutomaticChallengeEnabled = !snapshot.ManualTest;
            // Revalidated ballots may change before accession; replace only an unarmed snapshot.
            if (previous != null) _contests.Remove(previous);
            _contests.Add(snapshot);
            BellumCivileLogger.Log($"Elective contest snapshot {snapshot.Id}; realm={ballot.Realm.StringId}; winner={ballot.Winner.StringId}; candidates={snapshot.Candidates.Count}; controlled_test={snapshot.ManualTest}.");
        }

        internal void CompleteAccession(CrownAccessionRecord accession)
        {
            var record = _contests.FirstOrDefault(c => c.Id == accession.ElectiveContestId);
            if (record == null || record.AccessionCompleted || record.Closed) return;
            if (accession.Emergency || accession.Heir != record.ElectedWinner)
            {
                record.Closed = true; record.ClosedReason = "Election superseded before accession";
                return;
            }
            if (!accession.Completed || !accession.TitleTransferred) return;
            record.MandateNumber = ElectiveSuccessionBehavior.Instance?.Get(accession.Realm)?.MandateNumber ?? record.MandateNumber;
            record.Arm(CampaignTime.Now.ToDays);
            BellumCivileLogger.Log($"Elective contest snapshot ready {record.Id}; eligible_day={record.EligibleDay}; controlled_test={record.ManualTest}.");
        }
    }
}
