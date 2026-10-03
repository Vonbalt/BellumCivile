using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Election;
using TaleWorlds.Library;

namespace BellumCivile.Patches
{
    internal enum KingdomVoteAlignment
    {
        Neutral,
        SupportedOutcome,
        OpposedOutcome
    }

    internal static class KingdomVoteAlignmentTracker
    {
        private static readonly Dictionary<KingdomDecision, Dictionary<Clan, KingdomVoteAlignment>> VoteSnapshots
            = new Dictionary<KingdomDecision, Dictionary<Clan, KingdomVoteAlignment>>();

        internal static void Capture(
            KingdomDecision decision,
            IEnumerable<DecisionOutcome> possibleOutcomes,
            DecisionOutcome chosenOutcome)
        {
            if (decision == null || possibleOutcomes == null || chosenOutcome == null)
                return;

            var alignments = new Dictionary<Clan, KingdomVoteAlignment>();
            foreach (DecisionOutcome outcome in possibleOutcomes)
            {
                if (outcome?.SupporterList == null)
                    continue;

                foreach (Supporter supporter in outcome.SupporterList)
                {
                    Clan clan = supporter?.Clan;
                    if (clan == null)
                        continue;

                    if (supporter.SupportWeight == Supporter.SupportWeights.StayNeutral)
                    {
                        alignments[clan] = KingdomVoteAlignment.Neutral;
                    }
                    else
                    {
                        alignments[clan] = outcome == chosenOutcome
                            ? KingdomVoteAlignment.SupportedOutcome
                            : KingdomVoteAlignment.OpposedOutcome;
                    }
                }
            }

            VoteSnapshots[decision] = alignments;
        }

        internal static KingdomVoteAlignment GetAlignment(
            KingdomDecision decision,
            DecisionOutcome chosenOutcome,
            Clan clan)
        {
            if (decision != null
                && clan != null
                && VoteSnapshots.TryGetValue(decision, out Dictionary<Clan, KingdomVoteAlignment> alignments)
                && alignments.TryGetValue(clan, out KingdomVoteAlignment alignment))
            {
                return alignment;
            }

            if (chosenOutcome?.SupporterList != null)
            {
                foreach (Supporter supporter in chosenOutcome.SupporterList)
                {
                    if (supporter?.Clan != clan)
                        continue;

                    return supporter.SupportWeight == Supporter.SupportWeights.StayNeutral
                        ? KingdomVoteAlignment.Neutral
                        : KingdomVoteAlignment.SupportedOutcome;
                }
            }

            return KingdomVoteAlignment.Neutral;
        }

        internal static void ApplyRulerRelationChanges(
            KingdomDecision decision,
            DecisionOutcome chosenOutcome,
            Kingdom kingdom,
            Hero ruler,
            int supportBonus,
            int oppositionPenalty,
            string supportSourceId = RelationMemorySources.VoteAlignment,
            string oppositionSourceId = RelationMemorySources.VoteAlignment,
            float durationYears = 5f,
            RelationMemoryScope scope = RelationMemoryScope.House,
            string contextText = null)
        {
            if (decision == null || kingdom == null || ruler == null)
                return;

            foreach (Clan clan in kingdom.Clans)
            {
                if (clan == kingdom.RulingClan || !NobleClanEligibilityHelper.IsLiveNobleClan(clan))
                    continue;

                KingdomVoteAlignment alignment = GetAlignment(decision, chosenOutcome, clan);
                int relationChange = alignment == KingdomVoteAlignment.SupportedOutcome
                    ? supportBonus
                    : alignment == KingdomVoteAlignment.OpposedOutcome
                        ? oppositionPenalty
                        : 0;

                if (relationChange != 0)
                {
                    string sourceId = alignment == KingdomVoteAlignment.SupportedOutcome
                        ? supportSourceId
                        : oppositionSourceId;
                    RelationMemoryService.ApplyChange(clan.Leader, ruler, relationChange, false,
                        sourceId, durationYears, scope, contextText);
                }
            }
        }

        internal static void Clear(KingdomDecision decision)
        {
            if (decision != null)
                VoteSnapshots.Remove(decision);
        }
    }

    [HarmonyPatch]
    internal static class KingdomElectionVoteSnapshotPatch
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(KingdomElection), "ApplyChosenOutcome");
        }

        private static void Prefix(
            KingdomDecision ____decision,
            MBList<DecisionOutcome> ____possibleOutcomes,
            DecisionOutcome ____chosenOutcome)
        {
            KingdomVoteAlignmentTracker.Capture(____decision, ____possibleOutcomes, ____chosenOutcome);
        }

        private static void Postfix(KingdomDecision ____decision, DecisionOutcome ____chosenOutcome)
        {
            if (____decision is KingSelectionKingdomDecision succession)
            {
                Kingdom kingdom = succession.Kingdom;
                Clan chosenClan = KingSelectionAIPatch.ResolveCandidateClan(____chosenOutcome);
                if (kingdom != null
                    && chosenClan != null
                    && kingdom.RulingClan == chosenClan
                    && chosenClan.Leader != null)
                {
                    KingdomVoteAlignmentTracker.ApplyRulerRelationChanges(
                        succession,
                        ____chosenOutcome,
                        kingdom,
                        chosenClan.Leader,
                        BellumCivileConstants.SuccessionVoteSupportRelationGain,
                        BellumCivileConstants.SuccessionVoteOppositionRelationPenalty,
                        RelationMemorySources.BackedMySuccession,
                        RelationMemorySources.OpposedMySuccession,
                        15f,
                        RelationMemoryScope.House);
                }
            }

            KingdomVoteAlignmentTracker.Clear(____decision);
        }
    }
}
