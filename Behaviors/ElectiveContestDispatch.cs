using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace BellumCivile.Behaviors
{
    public sealed partial class ElectiveContestBehavior
    {
        private readonly HashSet<ElectiveContestRecord> _dispatching = new HashSet<ElectiveContestRecord>();

        internal bool PrepareUltimata(ElectiveContestRecord record)
        {
            if (!AppealCurrent(record) || !record.PledgesResolved || record.RulerAnswered) return false;
            if (record.UltimataPrepared) return true;
            if (ElectiveContestUltimatumRules.Challengers(record).Any(c => c.Candidate == Hero.MainHero && !c.UltimatumConfirmed)) return false;
            // Refresh all houses before resolving any withdrawal. No allegiance rerolls.
            foreach (var pledge in record.Pledges)
            {
                pledge.Power = RebellionPowerHelper.CalculateLiveClanPower(pledge.House);
                pledge.HasStronghold = pledge.House.Fiefs.Any(f => f.IsTown || f.IsCastle);
            }
            foreach (var candidate in ElectiveContestUltimatumRules.Challengers(record))
            {
                if (!SuccessionChallengeBehavior.Available(candidate.Candidate) || candidate.House.Leader != candidate.Candidate) return false;
                candidate.Threshold = RebellionPowerHelper.CalculateRebellionPowerThreshold(candidate.Candidate);
            }
            record.PledgesResolved = false;
            if (!ResolvePledges(record)) return false;
            if (ElectiveContestUltimatumRules.Challengers(record).Count == 0)
            {
                record.Closed = true;
                record.ClosedReason = "No challenger remained after final force validation";
                return false;
            }
            record.UltimataPrepared = true;
            return true;
        }

        internal bool AnswerUltimata(ElectiveContestRecord record, Hero surrenderTo)
        {
            if (!AppealCurrent(record) || !ElectiveContestUltimatumRules.CanAnswer(record, surrenderTo)) return false;
            if (ElectiveContestUltimatumRules.Challengers(record).Any(c => c.House.Leader != c.Candidate
                || !SuccessionChallengeBehavior.Available(c.Candidate))) return false;
            record.SurrenderTo = surrenderTo;
            record.RulerAnswered = true;
            return true;
        }

        internal bool AnswerNpcUltimata(ElectiveContestRecord record, Func<float> nextRoll)
        {
            if (!AppealCurrent(record) || !record.UltimataPrepared || record.RulerAnswered
                || record.ElectedHouse == Clan.PlayerClan || nextRoll == null) return false;
            if (record.RulerRoll < 0)
            {
                double roll = nextRoll();
                if (!SuccessionChallengeRules.Finite(roll) || roll < 0 || roll >= 1) return false;
                record.RulerRoll = roll;
            }
            var ruler = record.ElectedWinner;
            var recipient = ElectiveContestUltimatumRules.ChooseSurrender(record, record.RulerRoll,
                Kingdom.All.Any(k => k != record.Realm && k.IsAtWarWith(record.Realm)),
                ruler.GetTraitLevel(DefaultTraits.Calculating), ruler.GetTraitLevel(DefaultTraits.Valor), ruler.GetTraitLevel(DefaultTraits.Mercy));
            return AnswerUltimata(record, recipient?.Candidate);
        }

        internal void ShowRulerUltimata(ElectiveContestRecord record)
        {
            if (_inquiry != null || InformationManager.IsAnyInquiryActive() || !AppealCurrent(record)
                || !record.UltimataPrepared || record.RulerAnswered || record.ElectedHouse != Clan.PlayerClan) return;
            var choices = new List<InquiryElement>();
            var lines = new List<string>();
            foreach (var candidate in ElectiveContestUltimatumRules.Challengers(record))
            {
                var demand = new TextObject("{=BC_Contest_RulerDemand}{CLAIMANT} disputes the election and demands that you yield the crown, threatening civil war if you refuse.");
                demand.SetTextVariable("CLAIMANT", candidate.Candidate.Name);
                lines.Add(demand.ToString());
                var power = new TextObject(ComparisonText(record.Candidates.First(c => c.Candidate == record.ElectedWinner).PledgedPower,
                    candidate.PledgedPower));
                power.SetTextVariable("OPPONENT", candidate.Candidate.Name);
                lines.Add(power.ToString());
                var yield = new TextObject("{=BC_Contest_YieldTo}Yield the crown to {CLAIMANT}");
                yield.SetTextVariable("CLAIMANT", candidate.Candidate.Name);
                choices.Add(new InquiryElement(candidate.Candidate, yield.ToString(), null));
            }
            if (choices.Count > 1) lines.Add(ContestText("{=BC_Contest_RivalPersists}Yielding to one claimant will not settle the other's challenge."));
            choices.Add(new InquiryElement(record.ElectedWinner, ContestText("{=BC_Contest_RefuseAll}Refuse the demands"), null));
            _inquiry = record;
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                ContestText("{=BC_Contest_Title}A Disputed Election"), string.Join("\n\n", lines), choices, false, 1, 1,
                ContestText("{=BC_Contest_Answer}Give your answer"), "", selected =>
                {
                    _inquiry = null;
                    var chosen = selected?.FirstOrDefault()?.Identifier as Hero;
                    if (chosen == null) return;
                    if (!AnswerUltimata(record, chosen == record.ElectedWinner ? null : chosen))
                        InformationManager.DisplayMessage(new InformationMessage(ContestText("{=BC_Contest_ResponseChanged}The circumstances have changed. Your answer could not be delivered.")));
                }, null), true);
        }

        private bool DispatchCurrent(ElectiveContestRecord record)
        {
            if (!_contests.Contains(record) || record.Closed || !record.RulerAnswered || record.Realm?.IsEliminated != false) return false;
            var expected = record.CrownTransferStarted ? record.SurrenderTo : record.ElectedWinner;
            if (record.CrownTransferred ? record.Realm.Leader != expected
                : record.Realm.Leader != record.ElectedWinner && record.Realm.Leader != expected) return false;
            return record.Pledges.All(p => p.House?.IsEliminated == false && p.House.Leader == p.Speaker
                && (p.House.Kingdom == record.Realm || record.Candidates.Any(c => c.Candidate == p.AssignedSide
                    && c.WarShell != null && p.House.Kingdom == c.WarShell)));
        }

        // Both shells use one saved startup; rivalry begins only after both Crown wars exist.
        internal bool DispatchUltimata(ElectiveContestRecord record)
        {
            if (!_dispatching.Add(record)) return false;
            try
            {
                if (record == null || !_contests.Contains(record)) return false;
                foreach (var candidate in record.Candidates)
                    candidate.WarShell = candidate.WarShell ?? candidate.WarFaction?.GetTrackedRebelKingdomIncludingEliminated();
                if (record.WarDispatchCompleted) return true;
                if (record.StartupAborted) return FinishAbortedStartup(record);
                if (!DispatchCurrent(record))
                {
                    if (record.WarDispatchStarted) return AbortStartup(record, "Participants changed during election-war startup");
                    record.DispatchFailure = "Waiting for the recorded ruler and pledging houses";
                    return false;
                }
                var challengers = ElectiveContestUltimatumRules.Challengers(record);
                if (challengers.Count == 0) return false;
                var warChallengers = ElectiveContestUltimatumRules.WarChallengers(record);
                if (warChallengers.Count > 1 && (!WarPeaceRevampBehavior.IsRevampEnabled()
                    || CivilWarConflictBehavior.Instance == null))
                {
                    record.DispatchFailure = "Three-way election wars require the war-score system and conflict coordinator";
                    return false;
                }
                var manager = Campaign.Current.GetCampaignBehavior<FactionManagerBehavior>();
                if (manager == null) return false;
                if (challengers.Any(c => !SuccessionChallengeBehavior.Available(c.Candidate)
                    || c.House.Leader != c.Candidate))
                {
                    if (record.WarDispatchStarted) return AbortStartup(record, "A claimant became unavailable during startup");
                    record.Closed = true;
                    record.ClosedReason = "A claimant became unavailable before dispatch";
                    return false;
                }
                if (!record.WarDispatchStarted)
                {
                    if (CivilWarConflictBehavior.IsRealmTransferPending(record.Realm)) return false;
                    if (record.Pledges.Any(p => manager.GetRebelFaction(p.House)?.HasTrackedRebelKingdom == true)) return false;
                    foreach (var candidate in challengers)
                    {
                        var coalition = record.Pledges.Where(p => p.AssignedSide == candidate.Candidate).ToList();
                        double backing = coalition.Sum(p => (double)RebellionPowerHelper.CalculateLiveClanPower(p.House));
                        double opposition = ElectiveContestUltimatumRules.DispatchOpposition(record, candidate.Candidate,
                            house => RebellionPowerHelper.CalculateLiveClanPower(house));
                        if (!SuccessionChallengeRules.CanProceed(backing, opposition, candidate.Threshold,
                            candidate.Candidate == Hero.MainHero || record.BypassTestPowerGate,
                            coalition.Any(p => p.House.Fiefs.Any(f => f.IsTown || f.IsCastle))))
                        {
                            candidate.Withdrawn = true;
                            record.PledgesResolved = false;
                            record.UltimataPrepared = false;
                            record.RulerAnswered = false;
                            record.SurrenderTo = null;
                            record.DispatchFailure = "A challenger lost the required backing before dispatch";
                            return false;
                        }
                    }
                    record.WarDispatchStarted = true;
                }
                // Freeze every remaining coalition before transferring the Crown or moving any house.
                foreach (var candidate in warChallengers)
                {
                    if (candidate.WarStarted) continue;
                    if (candidate.WarFaction == null)
                    {
                        candidate.WarFaction = new FactionObject("Disputed election " + record.Id, record.Realm, candidate.House, FactionType.InstallRuler);
                        candidate.WarFaction.BindSuccessionChallenge("elective_" + record.Id + "_" + candidate.House.StringId);
                        foreach (var pledge in record.Pledges.Where(p => p.AssignedSide == candidate.Candidate && p.House != candidate.House))
                            candidate.WarFaction.Members.Add(pledge.House);
                        candidate.WarFaction.CaptureCivilWarStartFiefCounts(record.Pledges.Select(p => p.House));
                        candidate.WarFaction.CaptureCivilWarStartInfluence(record.Pledges.Select(p => p.House));
                    }
                }
                if (record.SurrenderTo != null && !SettleElectiveSurrender(record)) return false;
                if (warChallengers.Count == 0)
                {
                    record.Closed = true;
                    record.ClosedReason = "Crown yielded to sole challenger";
                    record.WarDispatchCompleted = true;
                    record.DispatchFailure = null;
                    return true;
                }
                foreach (var candidate in warChallengers)
                {
                    if (candidate.WarStarted) continue;
                    foreach (var member in candidate.WarFaction.Members)
                        foreach (var previous in manager.GetFactionsInKingdom(record.Realm)
                            .Where(f => f != candidate.WarFaction && !f.IsIdeology && f.Members.Contains(member)).ToList())
                        {
                            if (previous.HasTrackedRebelKingdom || previous.IsCivilWarActive()) return false;
                            previous.RemoveMember(member);
                            if (previous.Members.Count == 0) manager.RemoveFaction(previous);
                        }
                    manager.RegisterNewFaction(candidate.WarFaction);
                    bool started = candidate.WarFaction.StartFrozenSuccessionRebellion(out string failure);
                    candidate.WarShell = candidate.WarFaction.GetTrackedRebelKingdomIncludingEliminated();
                    if (!started) { record.DispatchFailure = failure; return false; }
                    candidate.WarStarted = true;
                }
                if (warChallengers.Count > 1 && !StartRivalry(record, warChallengers[0], warChallengers[1])) return false;
                record.WarDispatchCompleted = true;
                record.DispatchFailure = null;
                return true;
            }
            catch (Exception ex)
            {
                foreach (var candidate in record.Candidates)
                    candidate.WarShell = candidate.WarShell ?? candidate.WarFaction?.GetTrackedRebelKingdomIncludingEliminated();
                record.DispatchFailure = ex.Message;
                BellumCivileLogger.Log($"Elective dispatch deferred; contest={record.Id}; error={ex}");
                return false;
            }
            finally { _dispatching.Remove(record); }
        }

        private static bool SettleElectiveSurrender(ElectiveContestRecord record)
        {
            var receiving = record.Candidates.First(c => c.Candidate == record.SurrenderTo).House;
            var titles = Campaign.Current.GetCampaignBehavior<FeudalTitleBehavior>();
            var resolution = Campaign.Current.GetCampaignBehavior<CivilWarResolutionBehavior>();
            var elections = ElectiveSuccessionBehavior.Instance;
            if (titles == null || resolution == null || elections == null || receiving.Leader != record.SurrenderTo
                || receiving.Kingdom != record.Realm) return false;
            record.CrownTransferStarted = true;
            if (record.Realm.RulingClan != receiving)
            {
                resolution.ClearSuccessionStateForKingdom(record.Realm, "accepted elective contest ultimatum");
                record.Realm.RulingClan = receiving;
            }
            if (!titles.TrySetKingdomTitleRuler(record.Realm, receiving, legalTransfer: true, reason: "accepted elective contest ultimatum")) return false;
            var crown = titles.GetRealmSovereignTitle(record.Realm);
            if (crown?.DeJureHolderClanId != receiving.StringId || crown.DeFactoHolderClanId != receiving.StringId) return false;
            record.CrownTransferred = true;
            if (!record.MandateStarted)
            {
                elections.BeginSuccessorMandate(record.Realm);
                record.MandateStarted = true;
            }
            record.DispatchFailure = null;
            return true;
        }
    }
}
