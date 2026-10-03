﻿﻿using System.Linq;
using HarmonyLib;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Election;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using BellumCivile.Behaviors;
using C = BellumCivile.BellumCivileConstants;

namespace BellumCivile.Patches
{
    /// <summary>
    /// Why did I do this file?
    /// To process the political fallout of a Kingdom Law being passed or repealed. It actively identifies "Turncoats" who voted against their own party's agenda and applies cross-party relationship bonuses or penalties based on the election's outcome.
    /// </summary>
    [HarmonyPatch(typeof(KingdomPolicyDecision), "ApplyChosenOutcome")]
    public class PolicyVoteResolutionPatch
    {
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<KingdomPolicyDecision, object> Resolved =
            new System.Runtime.CompilerServices.ConditionalWeakTable<KingdomPolicyDecision, object>();

        public static void Prefix(KingdomPolicyDecision __instance, out Dictionary<FactionObject, CourtPolicyStance> __state)
        {
            __state = new Dictionary<FactionObject, CourtPolicyStance>();
            var manager = Campaign.Current?.GetCampaignBehavior<FactionManagerBehavior>();
            if (__instance?.Kingdom == null || __instance.Policy == null || manager == null) return;
            __state = CaptureStances(manager.GetFactionsInKingdom(__instance.Kingdom), __instance.Policy);
        }

        internal static Dictionary<FactionObject, CourtPolicyStance> CaptureStances(IEnumerable<FactionObject> factions, PolicyObject policy)
            => factions.Where(f => f.IsIdeology).Distinct().ToDictionary(f => f,
                f => IdeologyPolicyRoster.GetEffectiveStance(f, policy));

        public static void Postfix(KingdomPolicyDecision __instance, DecisionOutcome chosenOutcome,
            Dictionary<FactionObject, CourtPolicyStance> __state)
        {
            if (__instance == null || chosenOutcome == null || Resolved.TryGetValue(__instance, out _)) return;
            Resolved.Add(__instance, new object());
            PreserveFiledPolicyMotionPatch.MarkConcluded(__instance);

            Kingdom kingdom = __instance.Kingdom;
            PolicyObject policy = __instance.Policy;

            if (kingdom == null || policy == null || chosenOutcome == null) return;

            Campaign.Current.GetCampaignBehavior<PolicyDeliberationBehavior>()?.ClearBribedVotesForPolicy(kingdom, policy);

            bool abolish = PolicyDeliberationBehavior.ResolvePolicyAbolish(__instance);
            bool isPolicyActive = kingdom.ActivePolicies.Contains(policy);
            bool motionSucceeded = isPolicyActive != abolish;
            CourtAgendaBehavior.Current?.Conclude(kingdom, policy, __instance.ProposerClan, abolish, motionSucceeded, __state);
            BellumCivileLogger.Log(
                $"Policy vote concluded; kingdom={kingdom.StringId}; policy={policy.StringId}; proposer={__instance.ProposerClan?.StringId ?? "null"}; " +
                $"motion={(abolish ? "repeal" : "enact")}; result={(isPolicyActive ? "active" : "inactive")}; " +
                $"motion_succeeded={motionSucceeded}; winning_supporters={chosenOutcome.SupporterList?.Count ?? 0}.");

            var factionManager = Campaign.Current.GetCampaignBehavior<FactionManagerBehavior>();
            if (factionManager == null) return;
            // Missing snapshots are neutral, never retroactively reconstructed after result shocks.
            CourtPolicyStance Stance(FactionObject f) => __state != null && __state.TryGetValue(f, out var value)
                ? value : CourtPolicyStance.Neutral;
            bool isPlayerKingdom = Clan.PlayerClan.Kingdom != null && Clan.PlayerClan.Kingdom == kingdom;

            var winningSupporters = new HashSet<Clan>(chosenOutcome.SupporterList
                .Where(s => s.SupportWeight != Supporter.SupportWeights.StayNeutral)
                .Select(s => s.Clan));

            foreach (Clan voter in kingdom.Clans)
            {
                if (voter == null || voter == kingdom.RulingClan) continue;
                var alignment = KingdomVoteAlignmentTracker.GetAlignment(__instance, chosenOutcome, voter);
                if (alignment == KingdomVoteAlignment.Neutral) continue;

                FactionObject voterFaction = factionManager.GetIdeologicalFaction(voter);
                if (voterFaction == null) continue;

                CourtPolicyStance voterStance = Stance(voterFaction);
                if (voterStance == CourtPolicyStance.Neutral) continue;
                bool factionSupportsPolicy = voterStance == CourtPolicyStance.Support;

                bool votedActive = alignment == KingdomVoteAlignment.SupportedOutcome ? isPolicyActive : !isPolicyActive;
                bool isTraitor = votedActive != factionSupportsPolicy;

                if (isTraitor && voter.Leader != null && !voter.Leader.IsDead)
                {
                    if (isPlayerKingdom)
                    {
                        TextObject text = new TextObject("{=BC_Policy_TurncoatFurious}The lords of the {FACTION_NAME} are furious that {CLAN_NAME} voted against their political agenda!");
                        text.SetTextVariable("FACTION_NAME", voterFaction.GetDisplayName());
                        text.SetTextVariable("CLAN_NAME", voter.Name);
                        BellumCivileNotifications.Show(text, BellumNotificationColors.Danger, primaryKingdom: kingdom, primaryClan: voter);
                    }

                    if (voterFaction.Leader != voter && voterFaction.Leader?.Leader != null)
                    {
                        RelationMemoryService.ApplyChange(voter.Leader, voterFaction.Leader.Leader, C.PolicyResolveTurncoatLeaderPenalty, true,
                            RelationMemorySources.BetrayedFactionAgenda, 7f, RelationMemoryScope.House, policy.Name?.ToString());
                    }

                    foreach (Clan member in voterFaction.Members.ToList())
                    {
                        var memberAlignment = KingdomVoteAlignmentTracker.GetAlignment(__instance, chosenOutcome, member);
                        bool memberAlsoTraitor = memberAlignment != KingdomVoteAlignment.Neutral
                            && (memberAlignment == KingdomVoteAlignment.SupportedOutcome ? isPolicyActive : !isPolicyActive) != factionSupportsPolicy;

                        if (member != voter && member.Leader != null && !member.Leader.IsDead && !memberAlsoTraitor)
                        {
                            RelationMemoryService.ApplyChange(voter.Leader, member.Leader, C.PolicyResolveTurncoatPeerPenalty, false,
                                RelationMemorySources.BetrayedFactionAgenda, 7f, RelationMemoryScope.House, policy.Name?.ToString());
                        }
                    }
                }
            }

            Hero ruler = kingdom.RulingClan?.Leader;
            if (ruler == null) return;
            KingdomVoteAlignmentTracker.ApplyRulerRelationChanges(
                __instance,
                chosenOutcome,
                kingdom,
                ruler,
                C.PolicyResolveWinnerToRulerBonus,
                C.PolicyResolveLoserToRulerPenalty,
                RelationMemorySources.BackedCrownPolicyVote,
                RelationMemorySources.OpposedCrownPolicyVote,
                5f,
                RelationMemoryScope.House,
                policy.Name?.ToString());

            var allIdeologies = factionManager.GetFactionsInKingdom(kingdom).Where(f => f.IsIdeology).ToList();

            foreach (FactionObject faction in allIdeologies)
            {
                if (Stance(faction) == CourtPolicyStance.Neutral) continue;
                bool factionSupportsPolicy = Stance(faction) == CourtPolicyStance.Support;
                bool factionWon = (factionSupportsPolicy && isPolicyActive) || (!factionSupportsPolicy && !isPolicyActive);



                foreach (Clan memberA in faction.Members.ToList())
                {
                    if (memberA.Leader == null || memberA.Leader.IsDead || memberA == kingdom.RulingClan) continue;
                    if (!factionWon && winningSupporters.Contains(memberA)) continue;

                    var sponsorAgenda = CourtAgendaBehavior.Current?.GetAgenda(kingdom, faction);
                    if (factionWon && sponsorAgenda?.PolicyId == policy.StringId && sponsorAgenda.State == CourtAgendaState.Passed
                        && memberA != faction.Leader && faction.Leader?.Leader != null)
                    {
                        RelationMemoryService.ApplyChange(memberA.Leader, faction.Leader.Leader, C.PolicyResolveWinnerToLeaderBonus, false,
                            RelationMemorySources.CourtPolitics, 5f, RelationMemoryScope.House, policy.Name?.ToString());
                    }

                    bool memberAVotedWinner = winningSupporters.Contains(memberA);

                    foreach (FactionObject otherFaction in allIdeologies)
                    {
                        if (Stance(otherFaction) == CourtPolicyStance.Neutral) continue;
                        if (faction.Type >= otherFaction.Type) continue;

                        bool otherSupports = Stance(otherFaction) == CourtPolicyStance.Support;
                        bool otherFactionWon = (otherSupports && isPolicyActive) || (!otherSupports && !isPolicyActive);

                        foreach (Clan memberB in otherFaction.Members.ToList())
                        {
                            if (memberB.Leader == null || memberB.Leader.IsDead || memberB == kingdom.RulingClan || memberB == memberA) continue;

                            bool memberBVotedWinner = winningSupporters.Contains(memberB);
                            int relationChange = 0;

                            if (factionWon != otherFactionWon)
                            {
                                bool loserVotedWithWinner = factionWon ? memberBVotedWinner : memberAVotedWinner;

                                if (loserVotedWithWinner) relationChange = C.PolicyResolveCrossPartyBonus; 
                                else relationChange = C.PolicyResolveBitterDefeatPenalty; 
                            }
                            else if (!factionWon && !otherFactionWon)
                            {
                                if (!memberAVotedWinner && !memberBVotedWinner)
                                {
                                    relationChange = C.PolicyResolveComradesBonus; 
                                }
                            }
                            else if (factionWon && otherFactionWon)
                            {
                                if (memberAVotedWinner && memberBVotedWinner)
                                {
                                    relationChange = C.PolicyResolveTriumphantAlliesBonus; 
                                }
                            }

                            if (relationChange != 0)
                            {
                                RelationMemoryService.ApplyChange(memberA.Leader, memberB.Leader, relationChange, false,
                                    RelationMemorySources.CourtPolitics, 5f, RelationMemoryScope.House, policy.Name?.ToString());
                            }
                        }
                    }
                }
            }
        }
    }
}
