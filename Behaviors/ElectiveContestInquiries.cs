using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace BellumCivile.Behaviors
{
    public sealed partial class ElectiveContestBehavior
    {
        private ElectiveContestRecord _inquiry;

        private bool AppealCurrent(ElectiveContestRecord record) => record != null && _contests.Contains(record)
            && !record.Closed && record.AccessionCompleted && CampaignTime.Now.ToDays >= record.EligibleDay
            && record.Realm?.IsEliminated == false && record.Realm.RulingClan == record.ElectedHouse
            && record.ElectedHouse.Leader == record.ElectedWinner && record.ElectedWinner.IsAlive
            && (!record.PledgesCaptured || record.Pledges.All(p => p.House?.IsEliminated == false
                && p.House.Kingdom == record.Realm && p.House.Leader == p.Speaker));

        // Saved decisions and pledges are shared by automatic elections and controlled tests.
        internal bool AdvanceAppeal(ElectiveContestRecord record, Func<float> nextRoll)
        {
            if (!AppealCurrent(record) || nextRoll == null) return false;
            if (record.UltimataPrepared) return true;
            foreach (var candidate in record.Candidates.Where(c => c.Decision == ElectiveContestDecision.Pending))
            {
                bool available = candidate.House?.Kingdom == record.Realm && candidate.House.Leader == candidate.Candidate
                    && SuccessionChallengeBehavior.Available(candidate.Candidate);
                bool player = candidate.Candidate == Hero.MainHero;
                candidate.Decide(available, player, available && !player && candidate.HasAssessment ? nextRoll() : 0);
            }
            if (record.Candidates.Any(c => c.Decision == ElectiveContestDecision.AwaitingPlayer))
            {
                ShowAppealInquiry(record);
                return false;
            }
            if (!record.Candidates.Any(c => c.Decision == ElectiveContestDecision.Contest && !c.Withdrawn)) return true;
            if (!CapturePledges(record, nextRoll)) return false;
            if (record.Pledges.Any(p => p.AwaitingPlayer))
            {
                ShowAppealInquiry(record);
                return false;
            }
            if (!ResolvePledges(record)) return false;
            ShowAppealInquiry(record);
            return !record.Candidates.Any(c => c.Candidate == Hero.MainHero
                && c.Decision == ElectiveContestDecision.Contest && !c.Withdrawn && !c.UltimatumConfirmed);
        }

        private static string ContestText(string text) => new TextObject(text).ToString();

        internal void ShowAppealInquiry(ElectiveContestRecord record)
        {
            if (_inquiry != null || InformationManager.IsAnyInquiryActive() || !AppealCurrent(record)
                || record.UltimataPrepared || record.Realm != Clan.PlayerClan?.Kingdom || Hero.MainHero?.IsAlive != true) return;
            var player = record.Candidates.FirstOrDefault(c => c.Candidate == Hero.MainHero);
            if (player?.Decision == ElectiveContestDecision.AwaitingPlayer)
            {
                _inquiry = record;
                var text = new TextObject("{=BC_Contest_PlayerChoice}{RULER} has won the election. Will you accept the verdict, or call upon the houses of the realm to contest it?");
                text.SetTextVariable("RULER", record.ElectedWinner.Name);
                InformationManager.ShowInquiry(new InquiryData(ContestText("{=BC_Contest_Title}A Disputed Election"), text.ToString(), true, true,
                    ContestText("{=BC_Contest_Contest}Contest the election"), ContestText("{=BC_Contest_Accept}Accept the result"),
                    () => AnswerContest(record, true), () => AnswerContest(record, false)), true);
                return;
            }
            var pledge = record.Pledges.FirstOrDefault(p => p.House == Clan.PlayerClan && p.AwaitingPlayer);
            if (pledge != null)
            {
                var rivals = record.Candidates.Where(c => c.Candidate != record.ElectedWinner && c.Candidate != Hero.MainHero
                    && c.Decision == ElectiveContestDecision.Contest && !c.Withdrawn).Select(c => c.Candidate).ToList();
                var choices = new List<InquiryElement>();
                var loyal = new TextObject("{=BC_Contest_BackCrown}Remain loyal to {RULER}").SetTextVariable("RULER", record.ElectedWinner.Name);
                choices.Add(new InquiryElement(new List<Hero>(), loyal.ToString(), null));
                foreach (var first in rivals)
                {
                    var alone = new TextObject("{=BC_Contest_BackOne}Support {FIRST}");
                    alone.SetTextVariable("FIRST", first.Name);
                    choices.Add(new InquiryElement(new List<Hero> { first }, alone.ToString(), null));
                    foreach (var second in rivals.Where(h => h != first))
                    {
                        var fallback = new TextObject("{=BC_Contest_BackFallback}Support {FIRST}, then {SECOND}");
                        fallback.SetTextVariable("FIRST", first.Name); fallback.SetTextVariable("SECOND", second.Name);
                        choices.Add(new InquiryElement(new List<Hero> { first, second }, fallback.ToString(), null));
                    }
                }
                _inquiry = record;
                string prompt = player?.Decision == ElectiveContestDecision.Contest && !player.Withdrawn
                    ? "{=BC_Contest_OwnFallback}Your house stands behind your claim. If you withdraw, whom will you support?"
                    : "{=BC_Contest_ChooseSide}The election is disputed. Whom will your house support if the claimants take up arms?";
                string promptText = ContestText(prompt);
                if (rivals.Count > 1)
                    promptText += "\n\n" + ContestText("{=BC_Contest_FallbackExplanation}You may pledge to a second claimant should your first choice withdraw.");
                MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                    ContestText("{=BC_Contest_Title}A Disputed Election"), promptText, choices, true, 1, 1,
                    ContestText("{=BC_Contest_Pledge}Pledge support"), ContestText("{=BC_Contest_Later}Decide later"),
                    selected => FinishPledgeInquiry(record, selected.FirstOrDefault()?.Identifier as List<Hero>),
                    _ => _inquiry = null), true);
                return;
            }
            if (!record.PledgesResolved || player?.Decision != ElectiveContestDecision.Contest
                || player.Withdrawn || player.UltimatumConfirmed) return;
            _inquiry = record;
            InformationManager.ShowInquiry(new InquiryData(ContestText("{=BC_Contest_Title}A Disputed Election"),
                PlayerBackingReport(record, player), true, true,
                ContestText("{=BC_Contest_Send}Issue ultimatum"), ContestText("{=BC_Contest_Withdraw}Withdraw the challenge"),
                () => AnswerFinalAppeal(record, true), () => AnswerFinalAppeal(record, false)), true);
        }

        private void AnswerContest(ElectiveContestRecord record, bool contest)
        {
            _inquiry = null;
            if (!AppealCurrent(record)) return;
            var candidate = record.Candidates.FirstOrDefault(c => c.Candidate == Hero.MainHero);
            if (candidate?.House.Leader != Hero.MainHero || !SuccessionChallengeBehavior.Available(Hero.MainHero)) return;
            candidate.AnswerPlayer(contest);
        }

        private void FinishPledgeInquiry(ElectiveContestRecord record, List<Hero> sides)
        {
            _inquiry = null;
            if (AppealCurrent(record)) AnswerPledge(record, Clan.PlayerClan, sides);
        }

        internal bool AnswerFinalAppeal(ElectiveContestRecord record, bool send)
        {
            _inquiry = null;
            if (!AppealCurrent(record) || !record.PledgesResolved || record.UltimataPrepared) return false;
            var player = record.Candidates.FirstOrDefault(c => c.Candidate == Hero.MainHero);
            if (player == null || player.Decision != ElectiveContestDecision.Contest || player.Withdrawn
                || player.UltimatumConfirmed || player.House.Leader != Hero.MainHero
                || !SuccessionChallengeBehavior.Available(Hero.MainHero)) return false;
            if (send)
            {
                var coalition = record.Pledges.Where(p => p.AssignedSide == Hero.MainHero).ToList();
                if (!coalition.Any(p => p.House.Fiefs.Any(f => f.IsTown || f.IsCastle))
                    || !SuccessionChallengeRules.Finite(player.PledgedPower) || player.PledgedPower <= 0) return false;
                player.UltimatumConfirmed = true;
            }
            else
            {
                player.Withdrawn = true;
                record.PledgesResolved = false;
                // Reuse saved fallback preferences, never roll allegiance again.
                if (!ResolvePledges(record)) return false;
            }
            return true;
        }

        internal static string PlayerBackingReport(ElectiveContestRecord record, ElectiveContestCandidate player)
        {
            var intro = new TextObject("{=BC_Contest_BackingReport}{HOUSES} other houses have answered your call and pledged to back your claim that the election was unfairly decided.");
            intro.SetTextVariable("HOUSES", record.Pledges.Count(p => p.AssignedSide == player.Candidate && p.House != player.House));
            var lines = new List<string> { intro.ToString() };
            foreach (var opponent in record.Candidates.Where(c => c.Candidate == record.ElectedWinner
                || c.Candidate != player.Candidate && c.Decision == ElectiveContestDecision.Contest && !c.Withdrawn))
            {
                var line = new TextObject(ComparisonText(player.PledgedPower, opponent.PledgedPower));
                line.SetTextVariable("OPPONENT", opponent.Candidate.Name);
                lines.Add(line.ToString());
            }
            lines.Add(ContestText("{=BC_Contest_FinalChoice}Will you press your claim to the crown, or withdraw?"));
            return string.Join("\n\n", lines);
        }

        internal static string ComparisonText(double own, double opponent)
        {
            if (!SuccessionChallengeRules.Finite(own) || !SuccessionChallengeRules.Finite(opponent) || own < 0 || opponent < 0)
                return "{=BC_Contest_PowerUnknown}Scouts cannot yet judge your strength against the supporters of {OPPONENT}.";
            if (own > opponent * 1.5) return "{=BC_Contest_PowerDominant}Scouts report that your forces greatly outnumber the supporters of {OPPONENT}.";
            if (own > opponent * 1.1) return "{=BC_Contest_PowerStronger}Scouts report that your forces outnumber the supporters of {OPPONENT}.";
            if (own >= opponent * .9) return "{=BC_Contest_PowerEven}Scouts judge your forces evenly matched against the supporters of {OPPONENT}.";
            if (own >= opponent * .5) return "{=BC_Contest_PowerWeaker}Scouts report that the supporters of {OPPONENT} outnumber your forces.";
            return "{=BC_Contest_PowerOutmatched}Scouts report that your forces are greatly outnumbered by the supporters of {OPPONENT}.";
        }
    }
}
