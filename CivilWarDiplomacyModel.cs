using System;
using System.Collections.Generic;
using System.Linq;
using BellumCivile.Behaviors;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.BarterSystem;
using TaleWorlds.CampaignSystem.ComponentInterfaces;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.LogEntries;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Localization;
using C = BellumCivile.BellumCivileConstants;

namespace BellumCivile
{
    /// <summary>
    /// Wraps the active diplomacy model so Bellum can add civil-war rules without erasing other mods' diplomacy logic.
    /// This matters for Custom Spawns / Calradia at War, which enforce hostile minor-faction behavior through their own DiplomacyModel.
    /// </summary>
    public class CivilWarDiplomacyModel : DiplomacyModel
    {
        private readonly DiplomacyModel _baseModel;
        private static readonly object InvalidDiplomacyLogSync = new object();
        private static readonly HashSet<string> InvalidDiplomacyLogKeys = new HashSet<string>();

        public CivilWarDiplomacyModel(DiplomacyModel baseModel)
        {
            _baseModel = baseModel ?? new DefaultDiplomacyModel();
        }

        public override float GetScoreOfMercenaryToJoinKingdom(Clan clan, Kingdom kingdom)
        {
            if (!HasValidMercenaryNegotiationState(clan, kingdom, "mercenary join score"))
                return 0f;

            if (ModIntegrationHelper.IsCustomSpawnsForceNoKingdomClan(clan))
                return 0f;

            try
            {
                return _baseModel.GetScoreOfMercenaryToJoinKingdom(clan, kingdom);
            }
            catch (NullReferenceException ex)
            {
                LogInvalidDiplomacyStateOnce(
                    $"mercenary_join_exception|{clan.StringId}|{kingdom.StringId}",
                    $"Suppressed wrapped mercenary-join score crash; {BuildMercenaryNegotiationDetails(clan, kingdom)} error={ex.Message}.");
                return 0f;
            }
        }

        public override float GetScoreOfMercenaryToLeaveKingdom(Clan clan, Kingdom kingdom)
        {
            if (!HasValidMercenaryNegotiationState(clan, kingdom, "mercenary leave score"))
                return 1f;

            if (ModIntegrationHelper.IsCustomSpawnsForceNoKingdomClan(clan))
                return 1f;

            try
            {
                return _baseModel.GetScoreOfMercenaryToLeaveKingdom(clan, kingdom);
            }
            catch (NullReferenceException ex)
            {
                LogInvalidDiplomacyStateOnce(
                    $"mercenary_leave_exception|{clan.StringId}|{kingdom.StringId}",
                    $"Suppressed wrapped mercenary-leave score crash; {BuildMercenaryNegotiationDetails(clan, kingdom)} error={ex.Message}.");
                return 1f;
            }
        }

        public override float GetScoreOfKingdomToHireMercenary(Kingdom kingdom, Clan mercenaryClan)
        {
            if (!HasValidMercenaryNegotiationState(mercenaryClan, kingdom, "kingdom hire-mercenary score"))
                return 0f;

            if (ModIntegrationHelper.IsCustomSpawnsForceNoKingdomClan(mercenaryClan))
                return 0f;

            try
            {
                return _baseModel.GetScoreOfKingdomToHireMercenary(kingdom, mercenaryClan);
            }
            catch (NullReferenceException ex)
            {
                LogInvalidDiplomacyStateOnce(
                    $"kingdom_hire_mercenary_exception|{kingdom.StringId}|{mercenaryClan.StringId}",
                    $"Suppressed wrapped kingdom hire-mercenary score crash; {BuildMercenaryNegotiationDetails(mercenaryClan, kingdom)} error={ex.Message}.");
                return 0f;
            }
        }

        private static bool HasValidMercenaryNegotiationState(Clan mercenaryClan, Kingdom kingdom, string operation)
        {
            bool invalidMercenary = mercenaryClan == null
                || mercenaryClan.IsEliminated
                || mercenaryClan.Leader == null
                || mercenaryClan.Leader.IsDead;
            bool invalidKingdom = kingdom == null
                || kingdom.IsEliminated
                || kingdom.RulingClan == null
                || kingdom.RulingClan.IsEliminated
                || kingdom.Leader == null
                || kingdom.Leader.IsDead;
            bool hasLeaderlessMember = kingdom != null
                && kingdom.Clans.Any(member => member == null
                    || (!member.IsUnderMercenaryService && member.Leader == null));

            if (!invalidMercenary && !invalidKingdom && !hasLeaderlessMember)
                return true;

            string clanId = mercenaryClan?.StringId ?? "null";
            string kingdomId = kingdom?.StringId ?? "null";
            LogInvalidDiplomacyStateOnce(
                $"{operation}|{clanId}|{kingdomId}|{invalidMercenary}|{invalidKingdom}|{hasLeaderlessMember}",
                $"Skipped {operation} for invalid diplomacy state; {BuildMercenaryNegotiationDetails(mercenaryClan, kingdom)} leaderless_member={hasLeaderlessMember}.");
            return false;
        }

        private static string BuildMercenaryNegotiationDetails(Clan mercenaryClan, Kingdom kingdom)
        {
            FactionManagerBehavior factionManager = Campaign.Current?.GetCampaignBehavior<FactionManagerBehavior>();
            CivilWarResolutionBehavior resolutionBehavior = Campaign.Current?.GetCampaignBehavior<CivilWarResolutionBehavior>();
            bool feudKingdom = kingdom != null
                && !string.IsNullOrEmpty(kingdom.StringId)
                && kingdom.StringId.StartsWith("bc_feud_", StringComparison.OrdinalIgnoreCase);
            bool activeRebelKingdom = kingdom != null && factionManager?.GetFactionByRebelKingdom(kingdom) != null;
            bool referencedByBellum = kingdom != null
                && resolutionBehavior?.IsKingdomIdReferencedByBellumCivileState(kingdom.StringId) == true;

            return $"mercenary={mercenaryClan?.StringId ?? "null"} mercenary_leader={mercenaryClan?.Leader?.StringId ?? "null"} "
                + $"kingdom={kingdom?.StringId ?? "null"} kingdom_leader={kingdom?.Leader?.StringId ?? "null"} "
                + $"ruling_clan={kingdom?.RulingClan?.StringId ?? "null"} eliminated={kingdom?.IsEliminated.ToString() ?? "unknown"} "
                + $"feud_shell={feudKingdom} rebel_shell={activeRebelKingdom} referenced_by_bc={referencedByBellum}";
        }

        private static void LogInvalidDiplomacyStateOnce(string key, string message)
        {
            lock (InvalidDiplomacyLogSync)
            {
                if (!InvalidDiplomacyLogKeys.Add(key))
                    return;
            }

            BellumCivileLogger.Log(message);
        }

        public override float GetScoreOfDeclaringWar(IFaction factionDeclaresWar, IFaction factionDeclaredWar, Clan evaluatingClan, out TextObject reason, bool includeReason = false)
        {
            reason = null;

            if (!IsValidWarScoreFaction(factionDeclaresWar) || !IsValidWarScoreFaction(factionDeclaredWar))
            {
                if (includeReason)
                    reason = new TextObject("{=BC_Diplo_InvalidWarTarget}This war target is no longer politically valid.");

                BellumCivileLogger.Log($"Skipped war-declaration score for invalid faction input; declares={FactionLabel(factionDeclaresWar)} declared={FactionLabel(factionDeclaredWar)} evaluatingClan={evaluatingClan?.StringId ?? "null"}.");
                return 0f;
            }

            if (factionDeclaresWar.IsKingdomFaction && factionDeclaredWar.IsKingdomFaction)
            {
                ClaimFeudWarBehavior feudWarBehavior = Campaign.Current.GetCampaignBehavior<ClaimFeudWarBehavior>();
                if (feudWarBehavior != null && feudWarBehavior.ShouldBlockExternalWar(factionDeclaresWar, factionDeclaredWar, out reason))
                    return -9999999f;

                CivilWarInterventionBehavior behavior = Campaign.Current.GetCampaignBehavior<CivilWarInterventionBehavior>();
                if (behavior != null)
                {
                    if (behavior.IsProtected((Kingdom)factionDeclaredWar) && !factionDeclaresWar.IsAtWarWith(factionDeclaredWar))
                    {
                        reason = new TextObject("{=BC_Diplo_NoIntervene}They are in a civil war; we will not intervene yet.");
                        return -9999999f;
                    }

                    if (behavior.IsProtected((Kingdom)factionDeclaresWar) && !factionDeclaresWar.IsAtWarWith(factionDeclaredWar))
                    {
                        reason = new TextObject("{=BC_Diplo_FocusWar}We must focus on winning our civil war!");
                        return -9999999f;
                    }
                }
            }

            try
            {
                float rawScore = _baseModel.GetScoreOfDeclaringWar(
                    factionDeclaresWar,
                    factionDeclaredWar,
                    evaluatingClan,
                    out reason,
                    includeReason);

                if (!(factionDeclaresWar is Kingdom sourceKingdom)
                    || !(factionDeclaredWar is Kingdom targetKingdom)
                    || sourceKingdom.IsAtWarWith(targetKingdom)
                    || rawScore <= C.ForeignPolicyHardWarVetoScore)
                {
                    return rawScore;
                }

                try
                {
                    float decisionThreshold = _baseModel.GetDecisionMakingThreshold(sourceKingdom);
                    ForeignPolicyClaimScoreBreakdown claimScore = ForeignPolicyClaimScoringHelper.Calculate(
                        sourceKingdom,
                        targetKingdom,
                        evaluatingClan,
                        rawScore,
                        decisionThreshold);
                    return rawScore + claimScore.ScoreBonus;
                }
                catch (Exception ex)
                {
                    BellumCivileLogger.Log($"Skipped title-claim war score bonus; declares={sourceKingdom.StringId}; target={targetKingdom.StringId}; evaluatingClan={evaluatingClan?.StringId ?? "null"}; error={ex.GetType().Name}: {ex.Message}.");
                    return rawScore;
                }
            }
            catch (NullReferenceException ex)
            {
                if (includeReason)
                    reason = new TextObject("{=BC_Diplo_InvalidWarTarget}This war target is no longer politically valid.");

                BellumCivileLogger.Log($"Suppressed wrapped war-declaration score crash; declares={FactionLabel(factionDeclaresWar)} declared={FactionLabel(factionDeclaredWar)} evaluatingClan={evaluatingClan?.StringId ?? "null"} error={ex.Message}.");
                return 0f;
            }
        }

        private static bool IsValidWarScoreFaction(IFaction faction)
        {
            if (faction == null || faction.IsEliminated)
                return false;

            if (faction.Leader == null || faction.Leader.IsDead)
                return false;

            if (faction.IsKingdomFaction)
            {
                Kingdom kingdom = faction as Kingdom;
                return kingdom != null
                    && kingdom.RulingClan != null
                    && !kingdom.RulingClan.IsEliminated
                    && kingdom.RulingClan.Leader != null
                    && !kingdom.RulingClan.Leader.IsDead;
            }

            if (faction.IsClan)
            {
                Clan clan = faction as Clan;
                return clan != null
                    && !clan.IsEliminated
                    && clan.Leader != null
                    && !clan.Leader.IsDead;
            }

            return true;
        }

        private static string FactionLabel(IFaction faction)
        {
            if (faction == null)
                return "null";

            return $"{faction.StringId}(kingdom={faction.IsKingdomFaction}, clan={faction.IsClan}, eliminated={faction.IsEliminated}, leader={faction.Leader?.StringId ?? "null"})";
        }

        private static string BuildNullConstantWarDetails(IFaction attacker, IFaction enemy)
        {
            string campaignDay = Campaign.Current != null
                ? CampaignTime.Now.ToDays.ToString("0.00")
                : "unavailable";
            string attackerMapFaction;
            try
            {
                attackerMapFaction = FactionLabel(attacker?.MapFaction);
            }
            catch (Exception ex)
            {
                attackerMapFaction = $"unavailable({ex.GetType().Name})";
            }

            string nullWarListKingdoms;
            try
            {
                FactionManagerBehavior factionManager = Campaign.Current?.GetCampaignBehavior<FactionManagerBehavior>();
                CivilWarResolutionBehavior resolutionBehavior = Campaign.Current?.GetCampaignBehavior<CivilWarResolutionBehavior>();
                List<string> affectedKingdoms = new List<string>();
                foreach (Kingdom kingdom in Kingdom.All)
                {
                    if (kingdom == null
                        || kingdom.FactionsAtWarWith == null
                        || !kingdom.FactionsAtWarWith.Any(faction => faction == null))
                    {
                        continue;
                    }

                    bool feudShell = !string.IsNullOrEmpty(kingdom.StringId)
                        && kingdom.StringId.StartsWith("bc_feud_", StringComparison.OrdinalIgnoreCase);
                    bool rebelShell = factionManager?.GetFactionByRebelKingdom(kingdom) != null;
                    bool referencedByBellum = resolutionBehavior?.IsKingdomIdReferencedByBellumCivileState(kingdom.StringId) == true;
                    affectedKingdoms.Add(
                        $"{kingdom.StringId}(eliminated={kingdom.IsEliminated},ruler={kingdom.RulingClan?.StringId ?? "null"},"
                        + $"clans={kingdom.Clans?.Count ?? 0},settlements={kingdom.Settlements?.Count ?? 0},"
                        + $"feud_shell={feudShell},rebel_shell={rebelShell},referenced_by_bc={referencedByBellum})");
                }

                nullWarListKingdoms = affectedKingdoms.Count > 0
                    ? string.Join(",", affectedKingdoms)
                    : "none_detected";
            }
            catch (Exception ex)
            {
                nullWarListKingdoms = $"scan_failed({ex.GetType().Name}:{ex.Message})";
            }

            Clan attackerClan = attacker as Clan;
            return $"attacker={FactionLabel(attacker)} enemy={FactionLabel(enemy)} attacker_map={attackerMapFaction} "
                + $"attacker_kingdom={attackerClan?.Kingdom?.StringId ?? "null"} attacker_mercenary={attackerClan?.IsUnderMercenaryService.ToString() ?? "unknown"} "
                + $"campaign_day={campaignDay} null_war_list_kingdoms={nullWarListKingdoms}";
        }

        public override float GetStrengthThresholdForNonMutualWarsToBeIgnoredToJoinKingdom(Kingdom kingdomToJoin) => _baseModel.GetStrengthThresholdForNonMutualWarsToBeIgnoredToJoinKingdom(kingdomToJoin);
        public override float GetRelationIncreaseFactor(Hero hero1, Hero hero2, float relationValue) => _baseModel.GetRelationIncreaseFactor(hero1, hero2, relationValue);
        public override int GetInfluenceAwardForSettlementCapturer(Settlement settlement) => _baseModel.GetInfluenceAwardForSettlementCapturer(settlement);
        public override float GetHourlyInfluenceAwardForRaidingEnemyVillage(MobileParty mobileParty) => _baseModel.GetHourlyInfluenceAwardForRaidingEnemyVillage(mobileParty);
        public override float GetHourlyInfluenceAwardForBesiegingEnemyFortification(MobileParty mobileParty) => _baseModel.GetHourlyInfluenceAwardForBesiegingEnemyFortification(mobileParty);
        public override float GetHourlyInfluenceAwardForBeingArmyMember(MobileParty mobileParty) => _baseModel.GetHourlyInfluenceAwardForBeingArmyMember(mobileParty);
        public override float GetScoreOfClanToJoinKingdom(Clan clan, Kingdom kingdom)
        {
            if (clan == null || clan.IsEliminated || clan.Leader == null || clan.Leader.IsDead || kingdom == null || kingdom.IsEliminated)
            {
                BellumCivileLogger.Log($"Skipped wrapped clan-join score for invalid input; clan={clan?.StringId ?? "null"} clan_leader={clan?.Leader?.StringId ?? "null"} kingdom={kingdom?.StringId ?? "null"}.");
                return 0f;
            }

            try
            {
                return _baseModel.GetScoreOfClanToJoinKingdom(clan, kingdom);
            }
            catch (NullReferenceException ex)
            {
                BellumCivileLogger.Log($"Suppressed wrapped clan-join score crash; clan={clan.StringId} kingdom={kingdom.StringId} error={ex.Message}.");
                return 0f;
            }
        }

        public override float GetScoreOfClanToLeaveKingdom(Clan clan, Kingdom kingdom)
        {
            if (clan == null || clan.IsEliminated || clan.Leader == null || clan.Leader.IsDead || kingdom == null || kingdom.IsEliminated)
            {
                BellumCivileLogger.Log($"Skipped wrapped clan-leave score for invalid input; clan={clan?.StringId ?? "null"} clan_leader={clan?.Leader?.StringId ?? "null"} kingdom={kingdom?.StringId ?? "null"}.");
                return 0f;
            }

            try
            {
                return _baseModel.GetScoreOfClanToLeaveKingdom(clan, kingdom);
            }
            catch (NullReferenceException ex)
            {
                BellumCivileLogger.Log($"Suppressed wrapped clan-leave score crash; clan={clan.StringId} kingdom={kingdom.StringId} error={ex.Message}.");
                return 0f;
            }
        }

        public override float GetScoreOfKingdomToGetClan(Kingdom kingdom, Clan clan)
        {
            if (clan == null || clan.IsEliminated || clan.Leader == null || clan.Leader.IsDead || kingdom == null || kingdom.IsEliminated)
            {
                BellumCivileLogger.Log($"Skipped wrapped kingdom-get-clan score for invalid input; kingdom={kingdom?.StringId ?? "null"} clan={clan?.StringId ?? "null"} clan_leader={clan?.Leader?.StringId ?? "null"}.");
                return 0f;
            }

            try
            {
                return _baseModel.GetScoreOfKingdomToGetClan(kingdom, clan);
            }
            catch (NullReferenceException ex)
            {
                BellumCivileLogger.Log($"Suppressed wrapped kingdom-get-clan score crash; kingdom={kingdom.StringId} clan={clan.StringId} error={ex.Message}.");
                return 0f;
            }
        }
        public override float GetScoreOfKingdomToSackClan(Kingdom kingdom, Clan clan) => _baseModel.GetScoreOfKingdomToSackClan(kingdom, clan);
        public override float GetScoreOfKingdomToSackMercenary(Kingdom kingdom, Clan mercenaryClan) => _baseModel.GetScoreOfKingdomToSackMercenary(kingdom, mercenaryClan);
        public override float GetScoreOfDeclaringPeaceForClan(IFaction factionDeclaresPeace, IFaction factionDeclaredPeace, Clan evaluatingClan, out TextObject reason, bool includeReason = false) => _baseModel.GetScoreOfDeclaringPeaceForClan(factionDeclaresPeace, factionDeclaredPeace, evaluatingClan, out reason, includeReason);
        public override float GetScoreOfDeclaringPeace(IFaction factionDeclaresPeace, IFaction factionDeclaredPeace) => _baseModel.GetScoreOfDeclaringPeace(factionDeclaresPeace, factionDeclaredPeace);
        public override bool IsPeaceSuitable(IFaction factionDeclaresPeace, IFaction factionDeclaredPeace) => _baseModel.IsPeaceSuitable(factionDeclaresPeace, factionDeclaredPeace);
        public override ExplainedNumber GetWarProgressScore(IFaction factionDeclaresWar, IFaction factionDeclaredWar, bool includeDescriptions = false) => _baseModel.GetWarProgressScore(factionDeclaresWar, factionDeclaredWar, includeDescriptions);
        public override float GetScoreOfLettingPartyGo(MobileParty party, MobileParty partyToLetGo) => _baseModel.GetScoreOfLettingPartyGo(party, partyToLetGo);
        public override float GetValueOfHeroForFaction(Hero examinedHero, IFaction targetFaction, bool forMarriage = false) => _baseModel.GetValueOfHeroForFaction(examinedHero, targetFaction, forMarriage);
        public override int GetRelationCostOfExpellingClanFromKingdom() => _baseModel.GetRelationCostOfExpellingClanFromKingdom();
        public override int GetInfluenceCostOfSupportingClan() => _baseModel.GetInfluenceCostOfSupportingClan();
        public override int GetInfluenceCostOfExpellingClan(Clan proposingClan) => _baseModel.GetInfluenceCostOfExpellingClan(proposingClan);
        public override int GetInfluenceCostOfProposingPeace(Clan proposingClan) => _baseModel.GetInfluenceCostOfProposingPeace(proposingClan);
        public override int GetInfluenceCostOfProposingWar(Clan proposingClan) => _baseModel.GetInfluenceCostOfProposingWar(proposingClan);
        public override int GetInfluenceValueOfSupportingClan() => _baseModel.GetInfluenceValueOfSupportingClan();
        public override int GetRelationValueOfSupportingClan() => _baseModel.GetRelationValueOfSupportingClan();
        public override int GetInfluenceCostOfAnnexation(Clan proposingClan) => _baseModel.GetInfluenceCostOfAnnexation(proposingClan);
        public override int GetInfluenceCostOfChangingLeaderOfArmy() => _baseModel.GetInfluenceCostOfChangingLeaderOfArmy();
        public override int GetInfluenceCostOfDisbandingArmy() => _baseModel.GetInfluenceCostOfDisbandingArmy();
        public override int GetRelationCostOfDisbandingArmy(bool isLeaderParty) => _baseModel.GetRelationCostOfDisbandingArmy(isLeaderParty);
        public override int GetInfluenceCostOfPolicyProposalAndDisavowal(Clan proposingClan) => _baseModel.GetInfluenceCostOfPolicyProposalAndDisavowal(proposingClan);
        public override int GetInfluenceCostOfAbandoningArmy() => _baseModel.GetInfluenceCostOfAbandoningArmy();
        public override int GetEffectiveRelation(Hero hero, Hero hero1)
        {
            if (!HasValidEffectiveRelationIdentity(hero) || !HasValidEffectiveRelationIdentity(hero1))
            {
                LogInvalidDiplomacyStateOnce(
                    $"effective_relation|{hero?.StringId ?? "null"}|{hero1?.StringId ?? "null"}",
                    $"Returned neutral effective relation for invalid hero input; first={BuildHeroRelationDetails(hero)} second={BuildHeroRelationDetails(hero1)}.");
                return 0;
            }

            return _baseModel.GetEffectiveRelation(hero, hero1);
        }
        public override int GetBaseRelation(Hero hero, Hero hero1) => _baseModel.GetBaseRelation(hero, hero1);
        public override void GetHeroesForEffectiveRelation(Hero hero1, Hero hero2, out Hero effectiveHero1, out Hero effectiveHero2)
        {
            if (!HasValidEffectiveRelationIdentity(hero1) || !HasValidEffectiveRelationIdentity(hero2))
            {
                effectiveHero1 = ResolveSafeEffectiveRelationHero(hero1);
                effectiveHero2 = ResolveSafeEffectiveRelationHero(hero2);
                LogInvalidDiplomacyStateOnce(
                    $"effective_relation_heroes|{hero1?.StringId ?? "null"}|{hero2?.StringId ?? "null"}",
                    $"Resolved effective-relation heroes defensively; first={BuildHeroRelationDetails(hero1)} second={BuildHeroRelationDetails(hero2)}.");
                return;
            }

            _baseModel.GetHeroesForEffectiveRelation(hero1, hero2, out effectiveHero1, out effectiveHero2);
            if (effectiveHero1 == null)
                effectiveHero1 = hero1;
            if (effectiveHero2 == null)
                effectiveHero2 = hero2;
        }

        private static bool HasValidEffectiveRelationIdentity(Hero hero)
        {
            return hero != null && (hero.Clan == null || hero.Clan.Leader != null);
        }

        private static Hero ResolveSafeEffectiveRelationHero(Hero hero)
        {
            return hero?.Clan?.Leader ?? hero;
        }

        private static string BuildHeroRelationDetails(Hero hero)
        {
            return $"hero={hero?.StringId ?? "null"},clan={hero?.Clan?.StringId ?? "null"},clan_leader={hero?.Clan?.Leader?.StringId ?? "null"}";
        }
        public override int GetRelationChangeAfterClanLeaderIsDead(Hero deadLeader, Hero relationHero) => _baseModel.GetRelationChangeAfterClanLeaderIsDead(deadLeader, relationHero);
        public override int GetRelationChangeAfterVotingInSettlementOwnerPreliminaryDecision(Hero supporter, bool hasHeroVotedAgainstOwner) => _baseModel.GetRelationChangeAfterVotingInSettlementOwnerPreliminaryDecision(supporter, hasHeroVotedAgainstOwner);
        public override float GetClanStrength(Clan clan) => _baseModel.GetClanStrength(clan);
        public override float GetHeroCommandingStrengthForClan(Hero hero) => _baseModel.GetHeroCommandingStrengthForClan(hero);
        public override float GetHeroGoverningStrengthForClan(Hero hero) => _baseModel.GetHeroGoverningStrengthForClan(hero);
        public override uint GetNotificationColor(ChatNotificationType notificationType) => _baseModel.GetNotificationColor(notificationType);
        public override int GetDailyTributeToPay(Clan factionToPay, Clan factionToReceive, out int tributeDurationInDays) => _baseModel.GetDailyTributeToPay(factionToPay, factionToReceive, out tributeDurationInDays);
        public override float GetDecisionMakingThreshold(IFaction consideringFaction) => _baseModel.GetDecisionMakingThreshold(consideringFaction);
        public override float GetValueOfSettlementsForFaction(IFaction faction) => _baseModel.GetValueOfSettlementsForFaction(faction);
        public override bool CanSettlementBeGifted(Settlement settlement) => _baseModel.CanSettlementBeGifted(settlement);
        public override bool IsClanEligibleToBecomeRuler(Clan clan) => _baseModel.IsClanEligibleToBecomeRuler(clan);
        public override IEnumerable<BarterGroup> GetBarterGroups() => _baseModel.GetBarterGroups();
        public override int GetCharmExperienceFromRelationGain(Hero hero, float relationChange, ChangeRelationAction.ChangeRelationDetail detail) => _baseModel.GetCharmExperienceFromRelationGain(hero, relationChange, detail);
        public override float DenarsToInfluence() => _baseModel.DenarsToInfluence();
        public override DiplomacyStance? GetShallowDiplomaticStance(IFaction faction1, IFaction faction2) => _baseModel.GetShallowDiplomaticStance(faction1, faction2);
        public override DiplomacyStance GetDefaultDiplomaticStance(IFaction faction1, IFaction faction2) => _baseModel.GetDefaultDiplomaticStance(faction1, faction2);
        public override bool IsAtConstantWar(IFaction attacker, IFaction enemy)
        {
            if (attacker == null || enemy == null)
            {
                string attackerId = attacker?.StringId ?? "null";
                string enemyId = enemy?.StringId ?? "null";
                string diagnosticDetails;
                try
                {
                    diagnosticDetails = BuildNullConstantWarDetails(attacker, enemy);
                }
                catch (Exception ex)
                {
                    diagnosticDetails = $"diagnostic_failed={ex.GetType().Name}:{ex.Message}";
                }

                LogInvalidDiplomacyStateOnce(
                    $"constant_war_null|{attackerId}|{enemyId}",
                    $"Prevented constant-war evaluation with a null faction; treating the pair as blocked from peace. {diagnosticDetails}.");
                return true;
            }

            return _baseModel.IsAtConstantWar(attacker, enemy);
        }

        public override int MaxRelationLimit => _baseModel.MaxRelationLimit;
        public override int MinRelationLimit => _baseModel.MinRelationLimit;
        public override int MaxNeutralRelationLimit => _baseModel.MaxNeutralRelationLimit;
        public override int MinNeutralRelationLimit => _baseModel.MinNeutralRelationLimit;
        public override int MinimumRelationWithConversationCharacterToJoinKingdom => _baseModel.MinimumRelationWithConversationCharacterToJoinKingdom;
        public override int GiftingTownRelationshipBonus => _baseModel.GiftingTownRelationshipBonus;
        public override int GiftingCastleRelationshipBonus => _baseModel.GiftingCastleRelationshipBonus;
        public override float WarDeclarationScorePenaltyAgainstTradePartners => _baseModel.WarDeclarationScorePenaltyAgainstTradePartners;
    }
}
