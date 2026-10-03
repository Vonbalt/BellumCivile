using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;

namespace BellumCivile
{
    internal static class ElectiveContestUltimatumRules
    {
        internal static List<ElectiveContestCandidate> Challengers(ElectiveContestRecord record) =>
            record.Candidates.Where(c => c.Candidate != record.ElectedWinner
                && c.Decision == ElectiveContestDecision.Contest && !c.Withdrawn).ToList();

        internal static List<ElectiveContestCandidate> WarChallengers(ElectiveContestRecord record) =>
            Challengers(record).Where(c => c.Candidate != record.SurrenderTo).ToList();

        internal static double Opposition(double crownPower, double rivalPower) => crownPower + 0.5 * rivalPower;

        internal static double DispatchOpposition(ElectiveContestRecord record, Hero challenger, Func<Clan, double> power)
        {
            double crown = record.Pledges.Where(p => p.AssignedSide == record.ElectedWinner).Sum(p => power(p.House));
            double rivals = record.Pledges.Where(p => p.AssignedSide != record.ElectedWinner && p.AssignedSide != challenger)
                .Sum(p => power(p.House));
            // Validate the forces behind the already-issued demands, before any surrender or clan movement.
            return Opposition(crown, rivals);
        }

        internal static ElectiveContestCandidate ChooseSurrender(ElectiveContestRecord record, double roll,
            bool externalWar, int calculating, int valor, int mercy)
        {
            if (!SuccessionChallengeRules.Finite(roll) || roll < 0 || roll >= 1)
                throw new ArgumentOutOfRangeException(nameof(roll));
            var crown = record.Candidates.First(c => c.Candidate == record.ElectedWinner);
            // A ruler can yield only one Crown. Compare the strongest demand first;
            // one saved roll avoids granting extra surrender chances for extra rivals.
            return Challengers(record).OrderByDescending(c => c.PledgedPower)
                .ThenBy(c => c.House.StringId, StringComparer.Ordinal)
                .FirstOrDefault(c => roll < UltimatumAcceptanceRules.Calculate(c.PledgedPower,
                    crown.PledgedPower, externalWar, calculating, valor, mercy));
        }

        internal static bool CanAnswer(ElectiveContestRecord record, Hero recipient) =>
            record.UltimataPrepared && !record.RulerAnswered && !record.Closed
            && (recipient == null || Challengers(record).Any(c => c.Candidate == recipient));
    }
}
