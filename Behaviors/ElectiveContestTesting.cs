using System;
using System.Linq;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace BellumCivile.Behaviors
{
    public sealed partial class ElectiveContestBehavior
    {
        private void RegisterControlledTestEvents() =>
            CampaignEvents.HourlyTickEvent.AddNonSerializedListener(this, AdvanceControlledTests);

        internal string ForceTestElection(Kingdom realm)
        {
            if (!ElectiveSuccessionBehavior.UsesElection(realm)) return "Error: Select a living, permanent elective realm.";
            var accession = CrownAccessionBehavior.Instance;
            var elections = ElectiveSuccessionBehavior.Instance;
            var manager = Campaign.Current.GetCampaignBehavior<FactionManagerBehavior>();
            if (accession == null || elections == null || manager == null) return "Error: Election services are unavailable.";
            if (accession.IsPending(realm)) return "Error: A Crown accession or election is already pending.";
            if (_contests.Any(c => c.Realm == realm && c.ManualTest && !c.Closed && !c.WarDispatchCompleted))
                return "Error: An election test is already open. Use civilwars.election_test_status or civilwars.stop_election_test.";
            if (CivilWarConflictBehavior.IsRealmTransferPending(realm)
                || manager.GetFactionsInKingdom(realm).Any(f => !f.IsIdeology
                    && (f.HasTrackedRebelKingdom || f.IsChallengeStartupPending || f.IsUltimatumPending)))
                return "Error: Finish the realm's ongoing civil war, ultimatum or realm transfer before testing an election.";
            // Match scheduled elections: clear completed-ballot confirmation before freezing a new ballot.
            elections.Maintain(realm);
            if (!accession.BeginMandateElection(realm, testContest: true))
                return "Error: Election could not start; check the ruler and any pending vanilla succession decision.";
            return $"Election requested in {realm.Name}. Confirm your ballot in the succession laws panel if prompted. "
                + "The winner receives a full mandate under the existing laws. Test appeals begin the next day after accession. "
                + "Acceptance and support are not forced. Yielding to one challenger leaves the other's war intact; refusing both can start a three-way war. "
                + "Use civilwars.election_test_status to inspect progress.";
        }

        internal string ForceThreeWayTest(Kingdom realm)
        {
            if (!ElectiveSuccessionBehavior.UsesElection(realm)) return "Error: Select a living, permanent elective realm.";
            if (!WarPeaceRevampBehavior.IsRevampEnabled() || CivilWarConflictBehavior.Instance == null)
                return "Error: Three-way wars require the war-score system and conflict coordinator.";
            var manager = Campaign.Current.GetCampaignBehavior<FactionManagerBehavior>();
            if (manager == null || CrownAccessionBehavior.Instance == null || ElectiveSuccessionBehavior.Instance == null)
                return "Error: Election services are unavailable.";
            if (_inquiry != null || InformationManager.IsAnyInquiryActive()) return "Error: Finish the open inquiry first.";
            if (CrownAccessionBehavior.Instance.IsPending(realm) || realm.UnresolvedDecisions.Any(d => d is TaleWorlds.CampaignSystem.Election.KingSelectionKingdomDecision))
                return "Error: Finish the pending Crown succession first.";
            if (_contests.Any(c => c.Realm == realm && c.ManualTest && !c.Closed && !c.WarDispatchCompleted))
                return "Error: An election test is already open. Inspect or stop it before starting another.";
            if (CivilWarConflictBehavior.IsRealmTransferPending(realm)
                || manager.GetFactionsInKingdom(realm).Any(f => !f.IsIdeology
                    && (f.HasTrackedRebelKingdom || f.IsChallengeStartupPending || f.IsUltimatumPending)))
                return "Error: Finish the ongoing civil war, ultimatum or realm transfer first.";
            var houses = realm.Clans.Where(c => !c.IsEliminated && !c.IsBanditFaction && !c.IsUnderMercenaryService
                && !NobleClanEligibilityHelper.IsNonPlayerMinorClan(c) && c.Leader?.IsAlive == true)
                .OrderByDescending(c => RebellionPowerHelper.CalculateLiveClanPower(c))
                .ThenBy(c => c.StringId, StringComparer.Ordinal).ToList();
            var crown = realm.RulingClan;
            if (!houses.Contains(crown) || !SuccessionChallengeBehavior.Available(crown.Leader)
                || !crown.Fiefs.Any(f => f.IsTown || f.IsCastle))
                return "Error: The current ruler must be available and hold a town or castle. No land or ruler is fabricated.";
            var challengers = houses.Where(c => c != crown && SuccessionChallengeBehavior.Available(c.Leader)
                && c.Fiefs.Any(f => f.IsTown || f.IsCastle)).Take(2).ToList();
            if (challengers.Count != 2)
                return "Error: Two other available noble clan leaders, each holding a town or castle, are required.";

            // A synthetic post-ballot fixture: no election, mandate reset, loyalty edits or random recruitment.
            var record = new ElectiveContestRecord
            {
                Id = Guid.NewGuid().ToString("N"), Realm = realm, ElectedWinner = crown.Leader, ElectedHouse = crown,
                Predecessor = crown.Leader, CapturedDay = CampaignTime.Now.ToDays, EligibleDay = CampaignTime.Now.ToDays,
                ManualTest = true, ForcedThreeWayTest = true, AccessionCompleted = true,
                PledgesCaptured = true, PledgesResolved = true, UltimataPrepared = true, RulerAnswered = true
            };
            foreach (var house in new[] { crown }.Concat(challengers))
                record.Candidates.Add(new ElectiveContestCandidate
                {
                    Candidate = house.Leader, House = house, UltimatumConfirmed = true,
                    Decision = house == crown ? ElectiveContestDecision.Accept : ElectiveContestDecision.Contest,
                    Threshold = RebellionPowerHelper.CalculateRebellionPowerThreshold(house.Leader)
                });
            foreach (var house in houses)
            {
                var side = record.Candidates.FirstOrDefault(c => c.House == house);
                // Seat all three leading houses first, then balance remaining houses by live power.
                if (side != null)
                    record.Pledges.Add(new ElectiveContestPledge { House = house, Speaker = house.Leader,
                        AssignedSide = side.Candidate, Power = RebellionPowerHelper.CalculateLiveClanPower(house), HasStronghold = true });
            }
            foreach (var house in houses.Where(h => !record.Candidates.Any(c => c.House == h)))
            {
                var side = record.Candidates.OrderBy(c => record.Pledges.Where(p => p.AssignedSide == c.Candidate).Sum(p => p.Power)).First();
                record.Pledges.Add(new ElectiveContestPledge { House = house, Speaker = house.Leader,
                    AssignedSide = side.Candidate, Power = RebellionPowerHelper.CalculateLiveClanPower(house),
                    HasStronghold = house.Fiefs.Any(f => f.IsTown || f.IsCastle) });
            }
            foreach (var candidate in record.Candidates)
                candidate.PledgedPower = record.Pledges.Where(p => p.AssignedSide == candidate.Candidate).Sum(p => p.Power);
            _contests.Add(record);
            SetTestStatus(record, "Forced three-way fixture queued; challenge rolls, power withdrawal and surrender are bypassed for this test only");
            return $"Three-way test queued in {realm.Name}: Crown {crown.Name}; challengers {challengers[0].Name} and {challengers[1].Name}. "
                + "Advance campaign time for dispatch. Noble houses, including yours, are assigned to three coalitions; mercenaries are unchanged. "
                + "The current ruler and mandate are unchanged. Use civilwars.election_test_status " + realm.StringId + " to inspect progress. Test save only.";
        }

        internal string TestReport(Kingdom realm)
        {
            var record = _contests.LastOrDefault(c => c.Realm == realm && c.ManualTest);
            if (CrownAccessionBehavior.Instance?.IsPending(realm) == true)
                return $"{realm.Name}: Crown accession/election pending. Confirm your ballot in the succession laws panel if required; advance campaign time.";
            if (record == null) return $"{realm.Name}: no recorded controlled election test.";
            var lines = new List<string> { $"Election test {record.Id}; realm={realm.Name}; winner={record.ElectedWinner?.Name}; "
                + $"armed={record.AccessionCompleted}; eligible_day={record.EligibleDay:0.##}; now={CampaignTime.Now.ToDays:0.##}",
                $"Status: {record.TestStatus ?? "Waiting for post-election test processing"}; closed={record.Closed}; dispatched={record.WarDispatchCompleted}" };
            if (record.ForcedThreeWayTest) lines.Add("Mode: forced three-way fixture (not a real ballot); candidate acceptance was not assessed.");
            foreach (var candidate in record.Candidates)
                lines.Add($"{candidate.Candidate?.Name} [{candidate.Candidate?.StringId}]: acceptance={candidate.Acceptance:0.##}%; "
                    + $"decision={candidate.Decision}; roll={(candidate.Roll < 0 ? "not rolled" : candidate.Roll.ToString("0.000"))}; "
                    + $"withdrawn={candidate.Withdrawn}; pledged_power={candidate.PledgedPower:0.##}; war_started={candidate.WarStarted}; shell={candidate.WarShell?.StringId}");
            if (record.Rivalry != null) lines.Add($"Rival war: {record.Rivalry.Id}; tracked={record.Rivalry.Score != null}; closed={record.Rivalry.Closed}");
            if (!string.IsNullOrEmpty(record.DispatchFailure)) lines.Add("Dispatch: " + record.DispatchFailure);
            if (!string.IsNullOrEmpty(record.ClosedReason)) lines.Add("Closed: " + record.ClosedReason);
            return string.Join("\n", lines);
        }

        internal string StopTest(Kingdom realm)
        {
            if (CrownAccessionBehavior.Instance?.IsPending(realm) == true)
                return "Error: Finish the pending election first. This command does not undo a Crown accession.";
            var record = _contests.LastOrDefault(c => c.Realm == realm && c.ManualTest && !c.Closed);
            if (record == null) return "No open election test was found.";
            if (record.WarDispatchStarted || record.CrownTransferStarted || _inquiry == record)
                return "Error: Cannot cancel a started war/Crown transfer or an open player inquiry. Finish the inquiry first; resolve started wars normally.";
            record.Closed = true;
            record.ClosedReason = "Controlled test stopped before dispatch";
            SetTestStatus(record, record.ClosedReason);
            return "Election contest test stopped. The elected ruler and mandate are unchanged.";
        }

        private void AdvanceControlledTests()
        {
            foreach (var record in _contests.Where(c => c.ShouldAdvance(CampaignTime.Now.ToDays)).ToList())
            {
                if (_inquiry != null || InformationManager.IsAnyInquiryActive()
                    || !(Game.Current?.GameStateManager?.ActiveState is TaleWorlds.CampaignSystem.GameState.MapState)
                    || Hero.OneToOneConversationHero != null) return;
                try { AdvanceControlledTest(record); }
                catch (Exception ex)
                {
                    SetTestStatus(record, "Deferred after error: " + ex.Message);
                    BellumCivileLogger.Log($"Elective contest deferred; contest={record.Id}; controlled_test={record.ManualTest}; error={ex}");
                }
            }
        }

        private void AdvanceControlledTest(ElectiveContestRecord record)
        {
            // A partially dispatched war has its own saved recovery checks. Do not
            // run the prewar realm-membership gate after houses have begun moving.
            if (!record.WarDispatchStarted && !record.CrownTransferStarted)
            {
                if (record.Realm?.IsEliminated != false || record.Realm.RulingClan != record.ElectedHouse
                    || record.ElectedHouse?.Leader != record.ElectedWinner || record.ElectedWinner?.IsAlive != true)
                {
                    record.Closed = true;
                    record.ClosedReason = "Realm or elected ruler changed before the appeal";
                    SetTestStatus(record, record.ClosedReason);
                    return;
                }
                if (record.RevalidateParticipants(
                    c => c.House?.Kingdom == record.Realm && c.House.Leader == c.Candidate
                        && SuccessionChallengeBehavior.Available(c.Candidate),
                    house => house?.IsEliminated == false && house.Kingdom == record.Realm
                        && house.Leader?.IsAlive == true ? house.Leader : null, Clan.PlayerClan))
                    BellumCivileLogger.Log($"Elective participants revalidated; contest={record.Id}; saved rolls retained.");
            }
            if (!record.RulerAnswered && !record.WarDispatchStarted && !record.CrownTransferStarted)
            {
                if (!AdvanceAppeal(record, () => MBRandom.RandomFloat))
                {
                    SetTestStatus(record, _inquiry == record ? "Waiting for your appeal/support decision" : "Waiting for valid appeal participants and pledges");
                    return;
                }
                if (!record.Candidates.Any(c => c.Decision == ElectiveContestDecision.Contest && !c.Withdrawn))
                {
                    record.Closed = true;
                    record.ClosedReason = record.Candidates.Any(c => c.Withdrawn)
                        ? "All challengers withdrew for insufficient support" : "No losing candidate contested the result";
                    SetTestStatus(record, record.ClosedReason);
                    return;
                }
                if (!PrepareUltimata(record))
                {
                    SetTestStatus(record, record.ClosedReason ?? "Waiting for final backing validation");
                    return;
                }
                if (record.ElectedHouse == Clan.PlayerClan)
                {
                    ShowRulerUltimata(record);
                    SetTestStatus(record, "Waiting for your answer to the ultimatum");
                    return;
                }
                if (!AnswerNpcUltimata(record, () => MBRandom.RandomFloat))
                {
                    SetTestStatus(record, "Waiting for a valid ruler response");
                    return;
                }
            }
            if (DispatchUltimata(record))
                SetTestStatus(record, record.ClosedReason ?? (record.Rivalry != null ? "Three-way civil war started" : "Single-challenger civil war started"));
            else SetTestStatus(record, record.ClosedReason ?? record.DispatchFailure ?? "Dispatch deferred by live force or participant checks");
        }

        private static void SetTestStatus(ElectiveContestRecord record, string status)
        {
            if (record.TestStatus == status) return;
            record.TestStatus = status;
            BellumCivileLogger.Log($"Elective contest {record.Id}; realm={record.Realm?.StringId}; controlled_test={record.ManualTest}; {status}");
            if (record.ManualTest)
                InformationManager.DisplayMessage(new InformationMessage("Election test: " + status, BellumNotificationColors.Warning));
        }
    }
}
