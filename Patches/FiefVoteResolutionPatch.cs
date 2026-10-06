using HarmonyLib;
using System.Linq;
using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Election;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using BellumCivile.Behaviors;
using C = BellumCivile.BellumCivileConstants;

namespace BellumCivile.Patches
{
    /// <summary>
    /// Why did I do this file?
    /// This is the Jealousy Engine for Fiefs. It processes the political fallout of a fief being awarded, applying relationship penalties to rival political factions to ensure that kingdom expansion naturally breeds the discontent required for civil wars.
    /// </summary>
    [HarmonyPatch(typeof(SettlementClaimantDecision), "ApplyChosenOutcome")]
    public class FiefVoteResolutionPatch
    {
        private sealed class AwardScope
        {
            internal SettlementClaimantDecision Decision;
            internal bool Transferred;
        }
        private static readonly List<AwardScope> ActiveAwards = new List<AwardScope>();
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<SettlementClaimantDecision, object> Resolved =
            new System.Runtime.CompilerServices.ConditionalWeakTable<SettlementClaimantDecision, object>();

        internal static bool OwnsAward(Settlement settlement) =>
            ActiveAwards.Any(scope => scope.Decision.Settlement == settlement);

        internal static bool DeferAward(Settlement settlement)
        {
            var scope = ActiveAwards.LastOrDefault(s => s.Decision.Settlement == settlement);
            if (scope == null) return false;
            scope.Transferred = true;
            return true;
        }

        public static void Prefix(SettlementClaimantDecision __instance)
        {
            ActiveAwards.Add(new AwardScope { Decision = __instance });
        }

        public static Exception Finalizer(SettlementClaimantDecision __instance, Exception __exception)
        {
            int index = ActiveAwards.FindLastIndex(scope => scope.Decision == __instance);
            if (index >= 0) ActiveAwards.RemoveAt(index);
            return __exception;
        }

        private static bool TryRepairOwnerMismatch(Kingdom kingdom, Settlement settlement, Clan expectedWinner)
        {
            if (kingdom == null || settlement == null || expectedWinner?.Leader == null) return false;
            if (expectedWinner.IsEliminated || (expectedWinner.IsMinorFaction && expectedWinner != Clan.PlayerClan) || expectedWinner.IsUnderMercenaryService) return false;
            if (expectedWinner.Kingdom != kingdom) return false;
            if (settlement.MapFaction != kingdom) return false;

            ChangeOwnerOfSettlementAction.ApplyByKingDecision(expectedWinner.Leader, settlement);
            return settlement.OwnerClan == expectedWinner;
        }

        private static List<Clan> GetEligibleFactionMembers(FactionObject faction, Kingdom kingdom)
        {
            if (faction?.Members == null || kingdom == null)
                return new List<Clan>();

            return faction.Members
                .Where(member => member != kingdom.RulingClan
                    && member?.Kingdom == kingdom
                    && NobleClanEligibilityHelper.IsLiveNobleClan(member))
                .Distinct()
                .ToList();
        }

        public static void Postfix(SettlementClaimantDecision __instance, DecisionOutcome chosenOutcome)
        {
            if (__instance == null || chosenOutcome == null) return;
            Kingdom kingdom = __instance.Kingdom;
            Settlement settlement = __instance.Settlement;

            if (kingdom == null || settlement == null) return;

            Campaign.Current.GetCampaignBehavior<FiefDeliberationBehavior>()
                ?.ClearBribedVotesForSettlement(kingdom, settlement);
            Campaign.Current.GetCampaignBehavior<FiefDeliberationBehavior>()
                ?.CleanupFiefDecisionsForSettlement(kingdom, settlement, __instance);

            var factionManager = Campaign.Current.GetCampaignBehavior<FactionManagerBehavior>();
            if (factionManager == null) return;

            Clan expectedWinner = FiefVoteAIPatch.GetCandidateClan(chosenOutcome);
            if (expectedWinner != null && settlement.OwnerClan != expectedWinner)
            {
                if (!TryRepairOwnerMismatch(kingdom, settlement, expectedWinner))
                    return;
            }

            Clan winner = settlement.OwnerClan;
            if (winner == null || (expectedWinner != null && winner != expectedWinner)) return;

            FiefDeliberationBehavior.Current?.ClearOpenedAllocations(settlement);

            bool isPlayerKingdom = Clan.PlayerClan.Kingdom != null && Clan.PlayerClan.Kingdom == kingdom;
            Hero ruler = kingdom.RulingClan?.Leader;
            if (ruler == null) return;

            if (Resolved.TryGetValue(__instance, out _)) return;
            Resolved.Add(__instance, new object());
            var winningSupporters = new HashSet<Clan>(chosenOutcome.SupporterList
                .Where(s => s.SupportWeight != Supporter.SupportWeights.StayNeutral)
                .Select(s => s.Clan));
            if (ActiveAwards.LastOrDefault(scope => scope.Decision == __instance)?.Transferred == true)
                Campaign.Current.GetCampaignBehavior<IdeologyEventShockBehavior>()
                    ?.RecordFiefAward(kingdom, winner, winningSupporters);

            Clan dispossessed = __instance.ClanToExclude;
            if (dispossessed != null &&
                dispossessed.Leader != null &&
                dispossessed != winner &&
                dispossessed != kingdom.RulingClan &&
                dispossessed.Kingdom == kingdom &&
                dispossessed != __instance.ProposerClan)
            {
                RelationMemoryService.ApplyChange(dispossessed.Leader, ruler, C.FiefResolveDispossessedRulerPenalty, false,
                    RelationMemorySources.AllowedMyDispossession, 10f, RelationMemoryScope.House, settlement.Name?.ToString());

                if (winner.Leader != null && winner != kingdom.RulingClan)
                    RelationMemoryService.ApplyChange(dispossessed.Leader, winner.Leader, C.FiefResolveDispossessedWinnerPenalty, false,
                        RelationMemorySources.SeizedMyFief, 10f, RelationMemoryScope.House, settlement.Name?.ToString());

                FactionObject dispossessedFaction = factionManager.GetIdeologicalFaction(dispossessed);
                if (dispossessedFaction != null)
                {
                    dispossessedFaction.Mood = MathF.Max(dispossessedFaction.Mood - C.FiefResolveDispossessedFactionMoodHit, -100f);

                    var shockBehavior = Campaign.Current.GetCampaignBehavior<IdeologyEventShockBehavior>();
                    shockBehavior?.RecordFiefHumiliation(dispossessedFaction, (int)C.FiefResolveDispossessedFactionMoodHit);

                    if (isPlayerKingdom)
                    {
                        TextObject text = new TextObject("{=BC_Fief_Dispossessed_Furious}The {FACTION_NAME} are furious, {CLAN_NAME} has been stripped of {SETTLEMENT_NAME}!");
                        text.SetTextVariable("FACTION_NAME", dispossessedFaction.GetDisplayName());
                        text.SetTextVariable("CLAN_NAME", dispossessed.Name);
                        text.SetTextVariable("SETTLEMENT_NAME", settlement.Name);
                        BellumCivileNotifications.Show(text, BellumNotificationColors.Danger, primaryKingdom: kingdom, primaryClan: dispossessed);
                    }
                }
            }

            var allIdeologies = factionManager.GetFactionsInKingdom(kingdom).Where(f => f.IsIdeology).ToList();
            FactionObject winningFaction = factionManager.GetIdeologicalFaction(winner);

            if (winner == kingdom.RulingClan)
            {
                List<Clan> jealousVassals = allIdeologies
                    .Where(faction => faction.Type != FactionType.Royalists)
                    .SelectMany(faction => GetEligibleFactionMembers(faction, kingdom))
                    .Distinct()
                    .ToList();

                if (isPlayerKingdom && jealousVassals.Count > 0)
                {
                    TextObject text = new TextObject("{=BC_Fief_RulerClaimed}{RULER_NAME} has claimed {SETTLEMENT_NAME} for the crown. The realm's factions grow jealous!");
                    text.SetTextVariable("RULER_NAME", ruler.Name);
                    text.SetTextVariable("SETTLEMENT_NAME", settlement.Name);
                    BellumCivileNotifications.Show(text, BellumNotificationColors.Danger, primaryKingdom: kingdom, primaryClan: kingdom.RulingClan);
                }

                foreach (Clan member in jealousVassals)
                {
                    RelationMemoryService.ApplyChange(member.Leader, ruler, C.FiefResolveTyrannyPenalty, false,
                        RelationMemorySources.ClaimedFiefForCrown, 10f, RelationMemoryScope.House, settlement.Name?.ToString());
                }
                return; 
            }

            KingdomVoteAlignmentTracker.ApplyRulerRelationChanges(
                __instance,
                chosenOutcome,
                kingdom,
                ruler,
                C.FiefResolveWinnerToRulerBonus,
                C.FiefResolveLoserToRulerPenalty,
                RelationMemorySources.BackedCrownFiefVote,
                RelationMemorySources.OpposedCrownFiefVote,
                5f,
                RelationMemoryScope.House,
                settlement.Name?.ToString());

            if (winningFaction != null)
            {
                if (isPlayerKingdom)
                {
                    TextObject text = new TextObject("{=BC_Fief_FactionSecured}A member of the {FACTION_NAME} has secured {SETTLEMENT_NAME}!");
                    text.SetTextVariable("FACTION_NAME", winningFaction.GetDisplayName());
                    text.SetTextVariable("SETTLEMENT_NAME", settlement.Name);
                    BellumCivileNotifications.Show(text, BellumNotificationColors.Land, primaryKingdom: kingdom, primaryClan: winner);
                }

                List<Clan> eligibleWinningMembers = GetEligibleFactionMembers(winningFaction, kingdom);

                foreach (FactionObject faction in allIdeologies)
                {
                    List<Clan> eligibleMembers = GetEligibleFactionMembers(faction, kingdom);
                    if (eligibleMembers.Count == 0)
                        continue;

                    bool isWinningFaction = (faction == winningFaction);

                    if (!isWinningFaction && winningSupporters.Contains(faction.Leader))
                    {
                        if (isPlayerKingdom)
                        {
                            TextObject text = new TextObject("{=BC_Fief_PragmaticAccept}Following their leader's pragmatic vote, the {FACTION_NAME} accepts the decision without complaint.");
                            text.SetTextVariable("FACTION_NAME", faction.GetDisplayName());
                            BellumCivileNotifications.Show(text, BellumNotificationColors.Neutral, primaryKingdom: kingdom, primaryClan: faction.Leader);
                        }
                        continue; 
                    }

                    if (!isWinningFaction && isPlayerKingdom)
                    {
                        TextObject text = new TextObject("{=BC_Fief_OutragedPassedOver}The {FACTION_NAME} are outraged that they were passed over!");
                        text.SetTextVariable("FACTION_NAME", faction.GetDisplayName());
                        BellumCivileNotifications.Show(text, BellumNotificationColors.Danger, primaryKingdom: kingdom, primaryClan: faction.Leader);
                    }

                    foreach (Clan member in eligibleMembers)
                    {
                        if (!isWinningFaction && winningSupporters.Contains(member)) continue;

                        if (isWinningFaction)
                        {
                            if (member != winner && winner.Leader != null) RelationMemoryService.ApplyChange(member.Leader, winner.Leader, C.FiefResolveWinnerToFellowBonus, false,
                                RelationMemorySources.CourtPolitics, 5f, RelationMemoryScope.House, settlement.Name?.ToString());
                        }
                        else
                        {
                            if (winner.Leader != null) RelationMemoryService.ApplyChange(member.Leader, winner.Leader, C.FiefResolveLoserToWinnerPenalty, false,
                                RelationMemorySources.CourtPolitics, 7f, RelationMemoryScope.House, settlement.Name?.ToString());

                            foreach (Clan winningMember in eligibleWinningMembers)
                            {
                                if (member != winningMember)
                                {
                                    RelationMemoryService.ApplyChange(member.Leader, winningMember.Leader, C.FiefResolveLoserPeerPenalty, false,
                                        RelationMemorySources.CourtPolitics, 7f, RelationMemoryScope.House, settlement.Name?.ToString());
                                }
                            }
                        }
                    }
                }
            }

            if (winner.Leader == null) return;

            foreach (Clan clan in kingdom.Clans)
            {
                if (clan == winner || clan == kingdom.RulingClan || clan.Leader == null || clan.Leader.IsDead || clan.IsUnderMercenaryService || clan.IsMinorFaction) continue;

                int personalRelation = clan.Leader.GetRelation(winner.Leader);

                if (personalRelation >= C.FiefResolvePersonalFriendThreshold) 
                {
                    RelationMemoryService.ApplyChange(clan.Leader, ruler, C.FiefResolvePersonalFriendBonus, false,
                        RelationMemorySources.CourtPolitics, 3f, RelationMemoryScope.Personal, settlement.Name?.ToString());
                    RelationMemoryService.ApplyChange(clan.Leader, winner.Leader, C.FiefResolvePersonalFriendBonus, false,
                        RelationMemorySources.CourtPolitics, 3f, RelationMemoryScope.Personal, settlement.Name?.ToString());

                    if (isPlayerKingdom && clan == Clan.PlayerClan)
                    {
                        TextObject text = new TextObject("{=BC_Fief_FriendGranted}You are pleased that the King granted land to your friend, {LORD_NAME}.");
                        text.SetTextVariable("LORD_NAME", winner.Leader.Name);
                        BellumCivileNotifications.ShowPersonal(text, BellumNotificationColors.Success);
                    }
                }
                else if (personalRelation <= C.FiefResolvePersonalEnemyThreshold) 
                {
                    RelationMemoryService.ApplyChange(clan.Leader, ruler, C.FiefResolvePersonalEnemyPenalty, false,
                        RelationMemorySources.CourtPolitics, 5f, RelationMemoryScope.Personal, settlement.Name?.ToString());
                    RelationMemoryService.ApplyChange(clan.Leader, winner.Leader, C.FiefResolvePersonalEnemyPenalty, false,
                        RelationMemorySources.CourtPolitics, 5f, RelationMemoryScope.Personal, settlement.Name?.ToString());

                    if (isPlayerKingdom && clan == Clan.PlayerClan)
                    {
                        TextObject text = new TextObject("{=BC_Fief_RivalEmpowered}You are furious that the King empowered your rival, {LORD_NAME}!");
                        text.SetTextVariable("LORD_NAME", winner.Leader.Name);
                        BellumCivileNotifications.ShowPersonal(text, BellumNotificationColors.Danger);
                    }
                }
            }
        }
    }
}
