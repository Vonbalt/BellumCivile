using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace BellumCivile.Behaviors
{
    public sealed partial class SuccessionChallengeBehavior
    {
        private SuccessionChallengeRecord _inquiry;

        private static bool NobleParticipant(Clan clan, Kingdom realm) => clan?.Kingdom == realm && !clan.IsEliminated
            && !clan.IsBanditFaction && !clan.IsUnderMercenaryService && !NobleClanEligibilityHelper.IsNonPlayerMinorClan(clan)
            && clan.Leader?.IsAlive == true;

        private bool CapturePledges(SuccessionChallengeRecord record)
        {
            if (record.PledgesCaptured) return true;
            if (!ValidParticipants(record)) return false;
            var manager = Campaign.Current.GetCampaignBehavior<FactionManagerBehavior>();
            var ideology = Campaign.Current.GetCampaignBehavior<IdeologyBehavior>();
            var titles = Campaign.Current.GetCampaignBehavior<FeudalTitleBehavior>();
            if (manager == null || ideology == null || titles == null) return false;
            var houses = record.Realm.Clans.Where(c => NobleParticipant(c, record.Realm))
                .OrderBy(c => c.StringId, StringComparer.Ordinal).ToList();
            var pledges = new List<SuccessionPledgeRecord>();
            bool dependent = record.Challenger.Clan == record.Realm.RulingClan;
            foreach (var house in houses)
            {
                var pledge = new SuccessionPledgeRecord { Clan = house, Speaker = house.Leader,
                    Power = Math.Max(0, RebellionPowerHelper.CalculateClanPower(house)), Roll = MBRandom.RandomFloat };
                if (house == record.Realm.RulingClan) pledge.Choice = SuccessionPledgeChoice.Loyal;
                else if (!dependent && house == record.Challenger.Clan) pledge.Choice = SuccessionPledgeChoice.Rebel;
                else if (house == Clan.PlayerClan) pledge.Choice = SuccessionPledgeChoice.AwaitingPlayer;
                else if (manager.IsClanPacified(house) || manager.GetRebelFaction(house)?.IsCivilWarActive() == true)
                    pledge.Choice = SuccessionPledgeChoice.Loyal;
                else
                {
                    float intent = ideology.CalculateRebellionScore(house);
                    pledge.PersonalChance = HereditaryAllegiance.Chance(intent,
                        CharacterRelationManager.GetHeroRelation(house.Leader, record.Challenger),
                        CharacterRelationManager.GetHeroRelation(house.Leader, record.Sovereign),
                        CivilWarSolidarityHelper.AreImmediateBloodKin(house.Leader, record.Challenger),
                        CivilWarSolidarityHelper.AreImmediateBloodKin(house.Leader, record.Sovereign),
                        HasRoyalMarriage(house, record.Challenger), HasRoyalMarriage(house, record.Sovereign));
                    // Only direct lieges create feudal obligations. Store every edge before any decision changes a clan.
                    foreach (var sponsor in houses.Where(s => s != house && s != record.Realm.RulingClan))
                        if (CivilWarSolidarityHelper.IsImmediateVassalOf(titles, house, sponsor))
                            pledge.LiegeChances[sponsor] = CivilWarSolidarityHelper.AssessSupport(house, sponsor,
                                record.Realm, manager, ideology, true, intent, record.Sovereign)?.JoinChance ?? 0;
                }
                pledges.Add(pledge);
            }
            record.Pledges = pledges;
            record.PledgesCaptured = true;
            return true;
        }

        private bool PledgeParticipantsUnchanged(SuccessionChallengeRecord record)
        {
            var current = record.Realm.Clans.Where(c => NobleParticipant(c, record.Realm)).ToList();
            return current.Count == record.Pledges.Count && record.Pledges.All(p => current.Contains(p.Clan) && p.Clan.Leader == p.Speaker);
        }

        private static bool HasRoyalMarriage(Clan house, Hero royal)
        {
            // Compare ties to each person, not their shared royal clan. A marriage
            // into both royals' immediate family is neutral in this dispute.
            return royal?.Spouse?.IsAlive == true && royal.Spouse.Clan == house
                || royal != null && house.Heroes.Any(h => h?.IsAlive == true && h.Spouse?.IsAlive == true
                    && (h.Spouse == royal || CivilWarSolidarityHelper.AreImmediateBloodKin(h.Spouse, royal)));
        }

        private void ResolveAppeal(SuccessionChallengeRecord record)
        {
            if (record.Phase != SuccessionChallengePhase.Gathering || !ValidParticipants(record) || !CapturePledges(record)) return;
            if (!PledgeParticipantsUnchanged(record))
            {
                record.Phase = SuccessionChallengePhase.Cancelled;
                record.Failure = "The houses or their leaders changed before pledges were sealed";
                record.ReportPending = record.Realm == Clan.PlayerClan?.Kingdom;
                return;
            }
            var rebels = SuccessionPledgeRules.Resolve(record.Pledges);
            if (rebels == null) return; // Player house has not answered yet.
            var loyal = record.Pledges.Where(p => !rebels.Contains(p.Clan)).Select(p => p.Clan).ToList();
            if (RecordBacking(record, rebels, loyal, record.Pledges.Where(p => rebels.Contains(p.Clan)).Sum(p => p.Power),
                record.Pledges.Where(p => !rebels.Contains(p.Clan)).Sum(p => p.Power)))
                BellumCivileLogger.Log($"Succession appeal {record.Id}: claimant={record.Challenger.StringId}; realm={record.Realm.StringId}; "
                    + $"backers={rebels.Count}; backing={record.BackingPower:0.##}; loyalists={record.LoyalistPower:0.##}; "
                    + $"required={record.RequiredRatio:0.##}; phase={record.Phase}.");
        }

        private void ProcessChallengeInquiry()
        {
            if (_inquiry != null || InformationManager.IsAnyInquiryActive() || Hero.MainHero?.IsAlive != true) return;
            var choice = _records.FirstOrDefault(r => r.Phase == SuccessionChallengePhase.Gathering && r.PledgesCaptured
                && r.Pledges.Any(p => p.Clan == Clan.PlayerClan && p.Choice == SuccessionPledgeChoice.AwaitingPlayer));
            if (choice != null && ValidParticipants(choice))
            {
                _inquiry = choice;
                var text = DemandMessage(choice, false);
                InformationManager.ShowInquiry(new InquiryData(Label("{=BC_Challenge_Title}Dynastic Falling-out"), text.ToString(), true, true,
                    Label("{=BC_Challenge_Back}Support the claimant"), Label("{=BC_Challenge_Loyal}Remain loyal"),
                    () => AnswerCall(choice, true), () => AnswerCall(choice, false)), true);
                return;
            }
            // The ruler learns of the dispute through the ultimatum, not the private appeal.
            // Drain saved reports too, otherwise a pre-update report would still interrupt them.
            if (Clan.PlayerClan != null)
                foreach (var pending in _records.Where(r => r.ReportPending && r.Realm?.RulingClan == Clan.PlayerClan))
                    pending.ReportPending = false;
            var report = _records.FirstOrDefault(r => r.ReportPending && r.Realm == Clan.PlayerClan?.Kingdom);
            if (report != null)
            {
                _inquiry = report;
                if (report.Challenger == Hero.MainHero && report.Phase == SuccessionChallengePhase.AwaitingResponse)
                {
                    var proposal = new TextObject("{=BC_PlayerChallenge_Support}{HOUSES} other houses have answered your call. Will you send your demand for the crown to {RULER}, or withdraw? Withdrawal will delay another challenge.");
                    SetSubjects(proposal, report);
                    proposal.SetTextVariable("HOUSES", report.Backers.Count(c => c != report.Challenger.Clan));
                    InformationManager.ShowInquiry(new InquiryData(Label("{=BC_Challenge_Title}Dynastic Falling-out"),
                        proposal + "\n\n" + PowerSummary(report), true, true,
                        Label("{=BC_PlayerChallenge_Send}Send ultimatum"), Label("{=BC_PlayerChallenge_Withdraw}Withdraw"),
                        () => AnswerPlayerUltimatum(report, true), () => AnswerPlayerUltimatum(report, false)), true);
                    return;
                }
                var text = new TextObject(report.Phase == SuccessionChallengePhase.Cancelled
                    ? "{=BC_Challenge_Cancelled}The challenge by {HEIR} has lapsed because its participants changed."
                    : report.Phase == SuccessionChallengePhase.Withdrawn
                    ? "{=BC_Challenge_Withdrawn}{HEIR} failed to secure sufficient backing and has withdrawn the challenge."
                    : "{=BC_Challenge_Backing}{HEIR} has secured enough backing to present an ultimatum to {RULER}.");
                SetSubjects(text, report);
                InformationManager.ShowInquiry(new InquiryData(Label("{=BC_Challenge_Title}Dynastic Falling-out"),
                    text + (report.Phase == SuccessionChallengePhase.Cancelled ? "" : "\n\n" + PowerSummary(report)), true, false, Label("{=BC_Election_Continue}Continue"), "",
                    () => { report.ReportPending = false; _inquiry = null; }, null), true);
                return;
            }
            ShowRulerResponse();
        }

        private void AnswerCall(SuccessionChallengeRecord record, bool support)
        {
            _inquiry = null;
            if (!_records.Contains(record) || record.Phase != SuccessionChallengePhase.Gathering || !ValidParticipants(record)) return;
            var pledge = record.Pledges.FirstOrDefault(p => p.Clan == Clan.PlayerClan && p.Speaker == Clan.PlayerClan?.Leader);
            if (pledge == null || pledge.Choice != SuccessionPledgeChoice.AwaitingPlayer) return;
            pledge.Choice = support ? SuccessionPledgeChoice.Rebel : SuccessionPledgeChoice.Loyal;
            ResolveAppeal(record);
        }

        private static string Label(string value) => new TextObject(value).ToString();
        private static void SetSubjects(TextObject text, SuccessionChallengeRecord record)
        {
            text.SetTextVariable("HEIR", record.Challenger?.Name ?? TextObject.GetEmpty());
            text.SetTextVariable("RULER", record.Sovereign?.Name ?? TextObject.GetEmpty());
            text.SetTextVariable("REALM", record.Realm?.Name ?? TextObject.GetEmpty());
        }
    }
}
