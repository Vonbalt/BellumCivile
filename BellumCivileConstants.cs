namespace BellumCivile
{
    /// <summary>
    /// Central repository for all tunable numeric constants in Bellum Civile.
    /// Grouping by system makes balance passes safe: change a number here once
    /// instead of hunting through ten files.
    ///
    /// Usage: add  using C = BellumCivile.BellumCivileConstants;  to any consuming file,
    /// then reference as  C.ConstantName.
    /// </summary>
    internal static class BellumCivileConstants
    {
        // ------------------------------------------------------------
        // SHARED MOOD THRESHOLDS
        // Used by Feudal Apathy, King's Fury, and any system that buckets an
        // ideology's mood into Happy / Neutral / Unhappy bands.
        // ------------------------------------------------------------
        public const float MoodThresholdHappy   =  20f;
        public const float MoodThresholdUnhappy = -20f;

        // Vanilla kingdom-election relation rewards are very generous, especially
        // full support in three-candidate votes. Bellum keeps the vanilla logic
        // but scales the final sponsor relation change down.
        public const float VanillaElectionRelationMultiplier = 0.50f;

        // ------------------------------------------------------------
        // NPC INFLUENCE BUDGETS
        // Bellum political actions share these reserves so AI clans retain
        // enough influence to participate in votes and organize armies.
        // ------------------------------------------------------------
        public const float NpcInfluenceClanReserve = 200f;
        public const float NpcInfluenceRulerReserve = 500f;
        public const float NpcInfluenceForeignWarReserveBonus = 200f;
        public const float NpcInfluenceEmergencyFloor = 200f;
        public const float NpcCourtScandalProtectedFloor = 200f;
        public const float NpcCourtScandalMinimumTargetInfluence = 500f;
        public const int NpcCourtScandalTargetCooldownDays = 90;

        // ------------------------------------------------------------
        // PRIVY COUNCIL INCIDENTS
        // The player's council is reviewed on a staggered court-style schedule.
        // Every eligible active assignment receives an independent roll, but no
        // more than one incident may be selected during the same review.
        // ------------------------------------------------------------
        public const int CouncilIncidentCheckIntervalBaseDays = 14;
        public const int CouncilIncidentCheckIntervalRandomExtraDays = 22;
        public const float CouncilIncidentAssignmentChance = 0.10f;
        public const float CouncilIncidentSpecificCooldownDays = 84f;
        public const int CouncilIncidentEndorsedEffectDays = 30;
        public const int CouncilIncidentLimitedTrialEffectDays = 15;

        // ------------------------------------------------------------
        // RELATIONSHIP MEMORY
        // The former drift rate is retained only so old saves can finish fading their migrated history exactly.
        // ------------------------------------------------------------
        public const float DynamicRelationWeeklyDrift = 1f;
        public const int DynamicRelationBaselineMin = -100;
        public const int DynamicRelationBaselineMax = 100;
        public const int DynamicRelationTraitCompatibilityStep = 10;
        public const int DynamicRelationSameCultureBonus = 10;
        public const int DynamicRelationParentChildBonus = 20;
        public const int DynamicRelationSiblingBonus = 15;
        public const int DynamicRelationExtendedFamilyBonus = 5;
        public const int DynamicRelationSpouseBonus = 15;
        public const int DynamicRelationDifferentCulturePenalty = 10;
        public const int DynamicRelationSameKingdomBonus = 5;
        public const int DynamicRelationDifferentKingdomPenalty = 5;
        public const int DynamicRelationAlliedKingdomBonus = 10;
        public const int DynamicRelationKingdomWarPenalty = 15;
        public const int DynamicRelationMarriageAllianceBonus = 25;
        public const int DynamicRelationSameCourtFactionBonus = 10;
        public const int DynamicRelationDifferentCourtFactionPenalty = 10;
        public const int DynamicRelationSameRebellionBonus = 15;
        public const int DynamicRelationOpposingRebellionPenalty = 20;
        public const int DynamicRelationOpposingCivilWarSidePenalty = 25;
        public const int DynamicRelationSharedDynastyBonus = 10;
        public const int DynamicRelationRightfulLiegeBonus = 10;
        public const int DynamicRelationUnlawfulLiegePenalty = 10;
        public const int DynamicRelationLandlessTitleDesirePenalty = 10;
        public const int DynamicRelationDesiresCountyPenalty = 10;
        public const int DynamicRelationDesiresDuchyPenalty = 15;
        public const int DynamicRelationDesiresKingdomPenalty = 20;
        public const int DynamicRelationDesiresEmpirePenalty = 25;
        public const int DynamicRelationDesiresTitleGreedyPenalty = 5;
        public const int DynamicRelationDesiresTitleDishonorablePenalty = 5;
        public const int DynamicRelationDesiresTitleCalculatingPenalty = 3;
        public const int DynamicRelationDesiresTitleGenerousReduction = 5;
        public const int DynamicRelationDesiresTitleHonorableReduction = 5;
        public const int DynamicRelationDesiresTitleMinimumPenalty = 5;
        public const int DynamicRelationStrongTitleClaimPenalty = 20;
        public const int DynamicRelationWeakTitleClaimPenalty = 10;
        public const int DynamicRelationDesiresTitleControlPenalty = 15;

        // ------------------------------------------------------------
        // FOREIGN POLICY CLAIM PRESSURE
        // Claim pressure is a percentage of the active diplomacy model's
        // decision threshold, added after the wrapped model scores strategy.
        // ------------------------------------------------------------
        public const float ForeignPolicyRealmClaimPressureCap = 25f;
        public const float ForeignPolicyPersonalClaimPressureCap = 10f;
        public const float ForeignPolicyMinimumScoreScale = 1000f;
        public const float ForeignPolicyHardWarVetoScore = -9000000f;
        public const int ProxyWarRulerCheckMinDays = 10;
        public const int ProxyWarRulerCheckRandomExtraDays = 21;
        public const int ProxyWarRulerMaxChecksPerDailyTick = 3;
        public const float ForeignPolicyTributeAffordableBurden = 0.10f;
        public const float ForeignPolicyTributeCostlyBurden = 0.25f;
        public const float ForeignPolicyTributeHumiliatingBurden = 0.50f;
        public const float ForeignPolicyVoteStrategicScale = 40f;
        public const float ForeignPolicyVoteStrategicCap = 80f;
        public const float ForeignPolicyVoteTotalCap = 100f;
        public const float ForeignPolicyVoteRealmClaimScale = 1f;
        public const float ForeignPolicyVotePersonalClaimScale = 2f;
        public const float ForeignPolicyVoteClaimantFriendScale = 0.5f;
        public const float ForeignPolicyVoteClaimantAllianceScale = 0.75f;
        public const float ForeignPolicyVoteClaimantSympathyCap = 15f;
        public const float ForeignPolicyVoteSameSponsorFaction = 15f;
        public const float ForeignPolicyVoteFactionViewpointScale = 0.5f;
        public const float ForeignPolicyVoteTargetRelationPositiveScale = 0.20f;
        public const float ForeignPolicyVoteTargetRelationNegativeScale = 0.15f;
        public const float ForeignPolicyVoteProposerRelationScale = 0.10f;
        public const float ForeignPolicyVoteForeignMarriageAlliance = 20f;
        public const float ForeignPolicyVoteWarValor = 8f;
        public const float ForeignPolicyVoteWarMercy = 6f;
        public const float ForeignPolicyVotePeaceValor = 6f;
        public const float ForeignPolicyVotePeaceMercy = 8f;
        public const float ForeignPolicyVoteObjectivesAchieved = 25f;
        public const float ForeignPolicyVoteObjectivesOutstanding = 10f;
        public const float ForeignPolicyVotePersonalObjectiveOutstanding = 20f;
        public const float ForeignPolicyVotePeaceAdditionalWar = 8f;
        public const float ForeignPolicyVoteWeakClanReadiness = 5f;
        public const float ForeignPolicyVoteStrongClanReadiness = 5f;
        public const float ForeignPolicyVotePrisonerReadiness = 20f;
        public const float ForeignPolicyVotePoorClanReadiness = 5f;

        public const float WarPeaceRevampInitialWarWill = 45f;
        public const float WarPeaceRevampMinimumTargetScore = 10f;
        public const float WarPeaceRevampWarProposalThreshold = 75f;
        public const float WarPeaceRevampPeaceProposalThreshold = 10f;
        public const float WarPeaceRevampMutualWhitePeaceThreshold = 10f;
        public const int WarPeaceDurationReluctanceDays = 100;
        public const float WarPeaceReadinessSupportThreshold = 11f;
        public const int WarPeaceRevampRecentPeaceBlockDays = 20;
        public const float WarPeaceRevampLocalLandBorderTargetScore = 25f;
        public const float WarPeaceRevampLocalMaritimeBorderTargetScore = 20f;
        public const float WarPeaceRevampRealmLandFrontierTargetScore = 8f;
        public const float WarPeaceRevampRealmMaritimeRouteTargetScore = 6f;
        public const float WarPeaceRevampMaritimeNeighborDistanceMultiplier = 3f;
        public const float WarPeaceRevampActiveWarTargetScore = 20f;
        public const float WarPeaceRevampStrongClaimTargetScore = 45f;
        public const float WarPeaceRevampWeakClaimTargetScore = 25f;
        public const float WarPeaceRevampImpliedDeJureTargetScore = 35f;
        public const float WarPeaceRevampSameCultureFiefScore = 8f;
        public const float WarPeaceRevampFrontierProsperityPerScore = 500f;
        public const float WarPeaceRevampFrontierProsperityBaseCap = 12f;
        public const float WarPeaceRevampFrontierGreedWeight = 0.75f;
        public const float WarPeaceRevampFrontierDishonorWeight = 0.50f;
        public const float WarPeaceRevampFrontierPersonalityWeightCap = 1.25f;
        public const float WarPeaceRevampFrontierProsperityScoreCap = 15f;
        public const float WarPeaceRevampInteriorFrontierProsperityMultiplier = 0.50f;
        public const float WarPeaceRevampPowerScoreSlope = 30f;
        public const float WarPeaceRevampPowerOpportunityCap = 35f;
        public const float WarPeaceRevampPowerDangerCap = 35f;
        public const float WarPeaceRevampBadRulerRelationScale = 0.15f;
        public const float WarPeaceRevampGoodRulerRelationPenaltyScale = 0.10f;
        public const float WarPeaceRevampFormalAllianceTargetPenalty = -50f;
        public const float WarPeaceRevampTradeAgreementTargetPenalty = -8f;
        public const float WarPeaceRevampCrossRealmHostileRelationScale = 0.05f;
        public const float WarPeaceRevampCrossRealmHostileRelationCap = 8f;
        public const float WarPeaceRevampCrossRealmFriendlyRelationScale = 0.04f;
        public const float WarPeaceRevampCrossRealmFriendlyRelationCap = -6f;
        public const float WarPeaceRevampCrossRealmMarriageAlliancePenalty = -10f;
        public const float WarPeaceRevampCrossRealmRulingMarriageAlliancePenalty = -15f;
        public const float WarPeaceRevampCrossRealmTieCapPositive = 10f;
        public const float WarPeaceRevampCrossRealmTieCapNegative = -15f;
        public const int WarPeaceRevampClanEvaluationIntervalDays = 7;
        public const int WarPeaceRevampPressureMemoryDays = 84;
        public const float WarPeaceRevampBattleShockMultiplier = 1f;
        public const float WarPeaceRevampRaidShockMultiplier = 1f;
        public const float WarPeaceRevampSettlementShockMultiplier = 1f;
        public const float WarPeaceRevampCaptivityShockMultiplier = 1f;
        public const float WarPeaceRevampCasualtyShockMultiplier = 1f;
        public const float WarPeaceRevampAttackerWarDeclaredShock = 10f;
        public const float WarPeaceRevampDefenderWarDeclaredShock = 25f;
        public const float WarPeaceRevampExtraWarDailyDrain = 0.50f;
        public const float WarPeaceRevampMilitaristOverflowRecoveryMultiplier = 0.90f;
        public const float WarPeaceRevampAristocratClaimOverflowRecoveryMultiplier = 0.80f;
        public const float WarPeaceRevampRoyalistOverflowRecoveryMultiplier = 0.80f;
        public const float WarPeaceRevampUnaffiliatedOverflowRecoveryMultiplier = 0.55f;
        public const float WarPeaceRevampAristocratOverflowRecoveryMultiplier = 0.50f;
        public const float WarPeaceRevampPopulistOverflowRecoveryMultiplier = 0.40f;
        public const float WarPeaceRevampMultipleFrontShockPerLoad = 10f;
        public const float WarPeaceRevampMultipleFrontShockCap = 30f;
        public const float WarPeaceRevampCivilWarConflictLoad = 2f;
        public const float WarPeaceRevampClaimFeudConflictLoad = 0.5f;
        public const float WarPeaceRevampRealmFeudConflictLoad = 0.5f;
        public const float WarPeaceRevampPositiveShockDampingPerConflict = 0.35f;
        public const float WarPeaceRevampExistingWarDeclarationThresholdPenalty = 15f;
        public const float WarPeaceRevampSecondFrontMinimumPowerRatio = 1.20f;
        public const float WarPeaceRevampSecondFrontSafePowerRatio = 1.50f;
        public const float WarPeaceRevampSecondFrontMaximumCouncilPenalty = 30f;
        public const float WarPeaceRevampSecondFrontMaximumTargetPenalty = 15f;
        public const float TreatyAdditionalForeignWarUtility = 10f;
        public const float TreatyActiveCivilWarUtility = 20f;
        public const float TreatyActiveRealmFeudUtility = 5f;
        public const float WarPeaceRevampBattleVictoryShock = 5f;
        public const float WarPeaceRevampMajorBattleVictoryShock = 10f;
        public const float WarPeaceRevampBattleDefeatShock = -8f;
        public const float WarPeaceRevampMajorBattleDefeatShock = -15f;
        public const float WarPeaceRevampCasualtyPenaltyPerTroop = -0.01f;
        public const float WarPeaceRevampCasualtyPenaltyCap = -10f;
        public const float WarPeaceRevampVillageRaidOwnerShock = 8f;
        public const float WarPeaceRevampVillageRaidNeighborShock = 3f;
        public const float WarPeaceRevampTownLostOwnerShock = -40f;
        public const float WarPeaceRevampCastleLostOwnerShock = -30f;
        public const float WarPeaceRevampFiefLostNeighborShock = -10f;
        public const float WarPeaceRevampFiefLostRealmShock = -2f;
        public const float WarPeaceRevampSettlementCapturedShock = 5f;
        public const float WarPeaceRevampClaimObjectiveAchievedShock = -20f;
        public const float WarPeaceRevampNobleCapturedShock = -2f;
        public const float WarPeaceRevampHeirCapturedShock = -20f;
        public const float WarPeaceRevampRulerCapturedShock = -40f;
        public const float WarPeaceRevampEnemyNobleCapturedShock = 2f;
        public const float WarPeaceRevampEnemyHeirCapturedShock = 12f;
        public const float WarPeaceRevampEnemyRulerCapturedShock = 25f;
        public const float WarPeaceRevampRealmRulerCapturedShock = -15f;
        public const float WarPeaceRevampRealmHeirCapturedShock = -8f;
        public const float WarPeaceRevampNobleKilledShock = -4f;
        public const float WarPeaceRevampEnemyNobleKilledShock = 2f;
        public const float WarScoreTownCaptured = 15f;
        public const float WarScoreCastleCaptured = 10f;
        public const float WarScoreCoreFiefRetakenBonus = 5f;
        public const float WarScoreVillageRaided = 1f;
        public const float WarScoreBattleCasualtyDivisor = 60f;
        public const float WarScoreBattleMinimum = 1f;
        public const float WarScoreBattleMaximum = 15f;
        public const float WarScoreMajorBattleBonus = 5f;
        public const int WarScoreMajorBattleStrength = 300;
        public const float WarScoreNobleCaptured = 2f;
        public const float WarScoreHeirCaptured = 8f;
        public const float WarScoreRulerCaptured = 15f;
        public const float WarScoreBattleCap = 40f;
        public const float WarScoreRaidCap = 15f;
        public const float WarScorePrisonerCap = 25f;
        public const float WarScoreTickingCap = 25f;
        public const float WarScoreTickingDailyGain = 0.1f;
        public const float WarScoreLandlessPressureCap = 100f;
        public const float WarScoreLandlessPressureDailyGain = 1f;
        public const float WarScoreFeudObjectiveDailyGain = 1f;
        public const float WarScoreFeudObjectiveCap = 50f;
        public const float WarScoreForcePeaceThreshold = 100f;
        public const float WarScoreExhaustedVictoryThreshold = 50f;
        public const float WarScoreWhitePeaceMaximumScore = 10f;
        public const int WarScoreInternalWhitePeaceCheckDays = 7;
        public const int WarScorePlayerWhitePeaceDeclineCooldownDays = 30;
        public const float WarScoreInternalWhitePeaceBaseChance = 0.15f;
        public const float WarScoreInternalWhitePeaceMaximumChance = 0.65f;
        public const int TreatyTownCost = 30;
        public const int TreatyCastleCost = 20;
        public const float TreatyStrongClaimDiscount = 0.30f;
        public const float TreatyWeakClaimDiscount = 0.15f;
        public const float TreatyUnoccupiedFiefCostMultiplier = 1.50f;
        public const float TreatyDistantFiefCostPerStep = 0.10f;
        public const float TreatyDistantFiefCostMaximum = 0.50f;
        public const float TreatyFiefProsperityPerWarScore = 500f;
        public const float TreatyFiefProsperityCap = 12000f;
        public const float TreatyOfferingCreditMultiplier = 0.50f;
        public const float TreatyOfferingCreditBudgetCap = 0.50f;
        public const int TreatyOfferingCreditAbsoluteCap = 50;
        public const float TreatyOfferingAssessmentPerWarScore = 0.25f;
        public const float TreatyOfferingAssessmentMaximum = 25f;
        public const int TreatyUnusedLeverageToleranceMinimum = 5;
        public const float TreatyUnusedLeverageToleranceShare = 0.10f;
        public const float TreatyUnusedLeverageAssessmentPerWarScore = 1.50f;
        public const float TreatyRulerStrongRejectionUtility = -20f;
        public const float TreatyParleyAdvantageFlipTolerance = 5f;
        public const int TreatyReparationsGoldPerWarScore = 5000;
        public const int TreatyDailyTributePerWarScore = 250;
        public const int TreatyTributeDurationDays = 100;
        public const int TreatyClanGoldReserve = 2000;
        public const int TreatyPlayerParleyResponseDays = 3;
        public const float TreatyCouncilQuorumShare = 0.25f;
        public const int TreatyCouncilMinimumQuorum = 150;
        public const int TreatyCouncilMinimumVoteStep = 25;
        public const int TreatyCouncilMildCommitment = 75;
        public const int TreatyCouncilStrongCommitment = 150;
        public const int TreatyRulerAgreementRelationMinimum = 3;
        public const int TreatyRulerAgreementRelationMild = 9;
        public const int TreatyRulerAgreementRelationStrong = 15;
        public const int TreatyRulerOverrideRelationMinimum = 5;
        public const int TreatyRulerOverrideRelationMild = 15;
        public const int TreatyRulerOverrideRelationStrong = 25;
        public const int TreatyPrisonerBaseCost = 5;
        public const int TreatyPrisonerClanLeaderSurcharge = 5;
        public const int TreatyPrisonerHeirSurcharge = 5;
        public const int TreatyPrisonerRulerSurcharge = 10;
        public const float RetainedPrisonerDungeonEscapeChancePercent = 1f;
        public const float RetainedPrisonerMobileEscapeChancePercent = 3f;
        public const int TreatyDiscreditRulerCost = 20;
        public const int TreatyHumiliateRulerCost = 30;
        public const int TreatyDiscreditInfluenceTransfer = 60;
        public const int TreatyHumiliateInfluenceTransfer = 100;
        public const int TreatyDiscreditRenownTransfer = 30;
        public const int TreatyHumiliateRenownTransfer = 50;
        public const int TreatyDiscreditVassalRelationLoss = 5;
        public const int TreatyHumiliateVassalRelationLoss = 10;
        public const int TreatyDiscreditRulerRelationLoss = 10;
        public const int TreatyHumiliateRulerRelationLoss = 25;
        public const int TreatyStructuralMinimumCost = 20;
        public const int TreatyRoyalMarriageCost = 30;
        public const int TreatyEndTradeAgreementCost = 10;
        public const int TreatyEndAllianceCost = 25;
        public const int TreatyEndTradeAgreementDemandingRulerRelationLoss = 10;
        public const int TreatyEndTradeAgreementConcedingRulerRelationLoss = 5;
        public const int TreatyEndAllianceDemandingRulerRelationLoss = 25;
        public const int TreatyEndAllianceConcedingRulerRelationLoss = 15;
        public const int TreatyConcedeDefeatCost = 10;
        public const float TreatyClientReleaseCostMultiplier = 0.50f;
        public const int TreatyClientReleaseMinimumCost = 10;
        public const int TreatyClientReleaseRulerRelationGain = 30;
        public const int TreatyClientReleaseLordRelationGain = 15;
        public const int TreatyClientReleaseFormerSuzerainRelationLoss = 15;
        public const int TreatyPrisonerReleaseRelationGain = 5;
        public const int TreatyConcedeDefeatRenownTransfer = 15;
        public const int TreatyConcedeDefeatInfluenceTransfer = 30;
        public const int TreatyEnforceRebelMinimumCost = 20;
        public const float TreatyEnforceRebelRemainingScoreMultiplier = 0.50f;
        public const int TreatyEnforceRebelRulerRelationGain = 20;
        public const int TreatyEnforceRebelLordRelationGain = 10;

        public const float ClientLibertyBase = 20f;
        public const float ClientForcedSubmissionLiberty = 20f;
        public const float ClientLawfulSuzeraintyLiberty = -15f;
        public const float ClientUnlawfulSuzeraintyLiberty = 15f;
        public const float ClientSameCultureLiberty = -5f;
        public const float ClientDifferentCultureLiberty = 10f;
        public const float ClientSuzerainRelationLibertyScale = 0.20f;
        public const float ClientSuzerainRelationLibertyCap = 20f;
        public const float ClientSuzerainMarriageLiberty = -15f;
        public const float ClientMilitaristLiberty = 10f;
        public const float ClientPopulistForeignLiberty = 10f;
        public const float ClientAristocratUnlawfulLiberty = 10f;
        public const float ClientRoyalistFriendlyLiberty = -10f;
        public const float ClientValorLibertyPerLevel = 5f;
        public const float ClientMercyLibertyPerLevel = -3f;
        public const float ClientHonorLawfulLibertyPerLevel = -5f;
        public const float ClientHonorUnlawfulLibertyPerLevel = 5f;
        public const float ClientClanLiberationDesireThreshold = 60f;
        public const float ClientRealmLiberationDesireThreshold = 60f;
        public const float ClientSuzerainAllyPowerContribution = 0.50f;
        public const float ClientOtherClientPowerContribution = 0.50f;
        public const float ClientLiberationTargetScoreScale = 0.75f;
        public const float ClientProtectedTradeAgreementDurationYears = 100f;

        // ------------------------------------------------------------
        // FEUDAL TITLES
        // Organic title formation requires a connected cluster of at least
        // three same-rank child titles. Costs are paid by the forming clan
        // leader and influence is granted to the clan.
        // ------------------------------------------------------------
        public const int FeudalTitleMinimumChildren = 2;
        public const int DynamicRelationHouseMemberPrisonerPenalty = 10;
        public const float FeudalTitleAdjacencyTownDistanceMultiplier = 1f;
        public const int FeudalTitleBaronyCost = 100000;
        public const int FeudalTitleCountyCost = 150000;
        public const int FeudalTitleDuchyCost = 200000;
        public const int FeudalTitleKingdomCost = 250000;
        public const int FeudalTitleEmpireCost = 300000;
        public const float FeudalTitleBaronyInfluenceReward = 100f;
        public const float FeudalTitleCountyInfluenceReward = 150f;
        public const float FeudalTitleDuchyInfluenceReward = 200f;
        public const float FeudalTitleKingdomInfluenceReward = 250f;
        public const float FeudalTitleEmpireInfluenceReward = 300f;
        public const float FeudalAuthorityInfluencePerBarony = 0.1f;
        public const float FeudalTitleContestedTaxFactor = -0.5f;
        public const float FeudalTitleContestedLoyaltyPenalty = -0.5f;
        public const float FeudalTitleRevocationInfluenceCost = 100f;
        public const int FeudalTitleRevocationStrongRelationPenalty = -30;
        public const int FeudalTitleRevocationWeakRelationPenalty = -50;
        public const float FeudalTitleRevocationAiThreshold = 85f;
        public const int SubinfeudationGoldCost = 20000;
        public const float SubinfeudationInfluenceCost = 500f;
        public const int SubinfeudationPossessionRelationGain = 25;
        public const int SubinfeudationFullRightsRelationGain = 50;
        public const float SubinfeudationRelationMemoryYears = 20f;
        public const float FeudalDeJureDriftBaseYears = 25f;
        public const float FeudalDeJureDriftMinimumSpeed = 0.75f;
        public const float FeudalDeJureDriftMaximumSpeed = 1.50f;
        public const float FeudalDeJureDriftStewardDivisor = 600f;
        public const float FeudalDeJureDriftRogueryDivisor = 1200f;
        public const float FeudalDeJureDriftReversalMultiplier = 2f;
        public const int FeudalClaimFabricationBaronyGoldCost = 50000;
        public const int FeudalClaimFabricationCountyGoldCost = 75000;
        public const int FeudalClaimFabricationDuchyGoldCost = 100000;
        public const int FeudalClaimFabricationKingdomGoldCost = 125000;
        public const int FeudalClaimFabricationEmpireGoldCost = 150000;
        public const float FeudalClaimFabricationBaronyInfluenceCost = 50f;
        public const float FeudalClaimFabricationCountyInfluenceCost = 75f;
        public const float FeudalClaimFabricationDuchyInfluenceCost = 100f;
        public const float FeudalClaimFabricationKingdomInfluenceCost = 125f;
        public const float FeudalClaimFabricationEmpireInfluenceCost = 150f;
        public const float FeudalClaimFabricationBaseYears = 3f;
        public const float FeudalClaimFabricationPoliticalFollowUpDays = 7f;
        public const float FeudalClaimFabricationSkillDailyScale = 0.000005f;
        public const float FeudalClaimFabricationCalculating1Multiplier = 1.15f;
        public const float FeudalClaimFabricationCalculating2Multiplier = 1.30f;
        public const float FeudalClaimFabricationDeviousMultiplier = 1.15f;
        public const float FeudalClaimFabricationDeceitfulMultiplier = 1.35f;
        public const float FeudalClaimFabricationHonestMultiplier = 0.75f;
        public const float FeudalClaimFabricationHonorableMultiplier = 0.50f;
        public const float FeudalClaimFabricationGoldRefundShare = 0.50f;
        public const bool FeudalClaimFabricationAiEnabled = true;
        public const int FeudalClaimFabricationAiGoldReserve = 25000;
        public const float FeudalClaimFabricationAiGoldCostMultiplier = 2.0f;
        public const float FeudalClaimFabricationAiInfluenceReserve = 50f;
        public const float FeudalClaimFabricationAiDeFactoMinimumScore = 60f;
        public const float FeudalClaimFabricationAiLiegeMinimumScore = 75f;
        public const float FeudalClaimFabricationAiHorizontalMinimumScore = 80f;
        public const float FeudalClaimFabricationAiVassalMinimumScore = 85f;
        public const float FeudalClaimFabricationAiReclamationMinimumScore = 65f;
        public const float FeudalClaimFabricationAiStartChanceAtThreshold = 0.25f;
        public const float FeudalClaimFabricationAiStartChancePerExcessPoint = 0.02f;
        public const float FeudalClaimFabricationAiStartChanceCap = 0.75f;
        public const float FeudalClaimFabricationAiDeFactoBaseScore = 45f;
        public const float FeudalClaimFabricationAiLiegeBaseScore = 35f;
        public const float FeudalClaimFabricationAiHorizontalBaseScore = 25f;
        public const float FeudalClaimFabricationAiVassalBaseScore = 20f;
        public const float FeudalClaimFabricationAiReclamationBaseScore = 50f;
        public const float FeudalClaimFabricationAiDeFactoDesireScale = 0.50f;
        public const float FeudalClaimFabricationAiLiegeDesireScale = 0.80f;
        public const float FeudalClaimFabricationAiHorizontalDesireScale = 1.20f;
        public const float FeudalClaimFabricationAiVassalDesireScale = 0.60f;
        public const float FeudalClaimFabricationAiReclamationDesireScale = 0.80f;
        public const float FeudalClaimFabricationAiDeFactoDesireCap = 15f;
        public const float FeudalClaimFabricationAiLiegeDesireCap = 25f;
        public const float FeudalClaimFabricationAiHorizontalDesireCap = 40f;
        public const float FeudalClaimFabricationAiVassalDesireCap = 20f;
        public const float FeudalClaimFabricationAiReclamationDesireCap = 25f;
        public const float FeudalClaimFabricationAiTitleTierScale = 5f;
        public const float FeudalClaimFabricationAiBudgetComfortCap = 15f;
        public const float FeudalClaimFabricationAiRelationScale = 0.20f;
        public const float FeudalClaimFabricationAiRelationCap = 20f;
        public const float FeudalClaimFabricationAiRulerTargetPenalty = 25f;
        public const float FeudalClaimFabricationAiMarriageAlliancePenalty = 50f;
        public const float FeudalClaimFabricationAiStrategicCalculating1Bonus = 5f;
        public const float FeudalClaimFabricationAiStrategicCalculating2Bonus = 10f;
        public const float FeudalClaimFabricationAiHorizontalImpulsive1Bonus = 5f;
        public const float FeudalClaimFabricationAiHorizontalImpulsive2Bonus = 10f;
        public const float FeudalClaimFabricationAiHorizontalValor1Bonus = 5f;
        public const float FeudalClaimFabricationAiHorizontalValor2Bonus = 10f;
        public const float FeudalClaimFabricationAiHorizontalCautious1Penalty = 10f;
        public const float FeudalClaimFabricationAiHorizontalCautious2Penalty = 20f;
        public const float FeudalClaimFabricationAiHonestMultiplier = 0.60f;
        public const float FeudalClaimFabricationAiHonorableMultiplier = 0.25f;
        public const float FeudalClaimFabricationAiDeFactoHonestMultiplier = 0.80f;
        public const float FeudalClaimFabricationAiDeFactoHonorableMultiplier = 0.50f;
        public const float FeudalClaimFabricationAiDeviousMultiplier = 1.20f;
        public const float FeudalClaimFabricationAiDeceitfulMultiplier = 1.50f;
        public const float FeudalClaimFabricationAiGenerousMultiplier = 0.80f;
        public const float FeudalClaimFabricationAiMunificentMultiplier = 0.60f;
        public const float FeudalClaimFabricationAiClosefistedMultiplier = 1.15f;
        public const float FeudalClaimFabricationAiTightfistedMultiplier = 1.30f;
        public const float FeudalClaimFabricationAiMercifulMultiplier = 0.90f;
        public const float FeudalClaimFabricationAiCompassionateMultiplier = 0.75f;
        public const float FeudalClaimFabricationAiCruelMultiplier = 1.10f;
        public const float FeudalClaimFabricationAiSadisticMultiplier = 1.20f;
        public const float FeudalClaimFabricationBaseDiscoveryChance = 0.10f;
        public const int PrisonerDonationRelationCap = 20;
        public const float PrisonerDonationMemoryYears = 5f;
        public const float FeudalClaimFabricationDiscoverySkillStep = 10f;
        public const float FeudalClaimFabricationDiscoveryChancePerSkillStep = 0.01f;
        public const float FeudalClaimFabricationMinimumDiscoveryChance = 0.02f;
        public const float FeudalClaimFabricationMaximumDiscoveryChance = 0.80f;
        public const float FeudalClaimFabricationTier1ScandalMultiplier = 0.30f;
        public const float FeudalClaimFabricationTier2ScandalMultiplier = 0.60f;
        public const float FeudalClaimFabricationTier3ScandalMultiplier = 1.00f;
        public const int FeudalClaimFabricationMinimumScandalPenalty = -5;
        public const int FeudalClaimFabricationDetectionVariance = 60;
        public const int FeudalClaimFabricationTier2HolderRelationPenalty = -10;
        public const int FeudalClaimFabricationForeignHolderScandalPenalty = -35;
        public const int FeudalClaimFabricationForeignRulerScandalPenalty = -20;
        public const int FeudalClaimFabricationSovereignHolderScandalPenalty = -25;
        public const int FeudalClaimFabricationSovereignHonorScandalPenalty = -8;
        public const int FeudalClaimFabricationDomesticHolderScandalPenalty = -40;
        public const int FeudalClaimFabricationDomesticRulerScandalPenalty = -30;

        public const bool ClaimFeudAiEnabled = true;
        public const float ClaimFeudEvaluationMinYears = 0.75f;
        public const float ClaimFeudEvaluationRandomYears = 0.75f;
        public const int ClaimFeudMaxClanEvaluationsPerDailyTick = 5;
        public const float ClaimFeudStartThreshold = 65f;
        public const float ClaimFeudInitialPressure = 10f;
        public const float ClaimFeudStrongClaimScore = 45f;
        public const float ClaimFeudWeakClaimScore = 20f;
        public const float ClaimFeudTitleTierScore = 8f;
        public const float ClaimFeudFiefDesireScale = 0.75f;
        public const float ClaimFeudFiefDesireCap = 25f;
        public const float ClaimFeudHolderRelationScale = 0.35f;
        public const float ClaimFeudHolderRelationCap = 25f;
        public const float ClaimFeudPowerAdvantageCap = 20f;
        public const float ClaimFeudCalculating1Bonus = 6f;
        public const float ClaimFeudCalculating2Bonus = 12f;
        public const float ClaimFeudImpulsive1Bonus = 4f;
        public const float ClaimFeudImpulsive2Bonus = 8f;
        public const float ClaimFeudValor1Bonus = 5f;
        public const float ClaimFeudValor2Bonus = 10f;
        public const float ClaimFeudHonor1Penalty = 12f;
        public const float ClaimFeudHonor2Penalty = 24f;
        public const float ClaimFeudMercy1Penalty = 6f;
        public const float ClaimFeudMercy2Penalty = 12f;
        public const float ClaimFeudMarriageAlliancePenalty = 45f;
        public const float ClaimFeudSameDynastyPenalty = 20f;
        public const float ClaimFeudFirstExternalWarPenalty = 15f;
        public const float ClaimFeudMultipleExternalWarsPenalty = 30f;
        public const float ClaimFeudStrongDailyPressure = 1.50f;
        public const float ClaimFeudWeakDailyPressure = 0.75f;
        public const float ClaimFeudDailyDesireScale = 0.02f;
        public const float ClaimFeudDailyRelationScale = 0.01f;
        public const float ClaimFeudDailyPressureCap = 3.0f;
        public const int ClaimFeudHarassmentRelationPenalty = -5;
        public const int ClaimFeudCallToArmsRelationPenalty = -5;
        public const float ClaimFeudDistractedRebellionPenalty = 10f;
        public const float ClaimFeudOccupiedRebellionPenalty = 15f;
        public const float ClaimFeudFocusedRebellionPenalty = 20f;
        public const float ClaimFeudWarActiveRebellionPenalty = 20f;
        public const float ClaimFeudCooldownYears = 1f;
        public const int ClaimFeudRulerUpholdRelationBonus = 8;
        public const int ClaimFeudRulerUpholdRelationPenalty = -15;
        public const int ClaimFeudRulerSuppressClaimantPenalty = -20;
        public const int ClaimFeudRulerAbstainRelationPenalty = -8;
        public const int ClaimFeudSupportFriendRelationThreshold = 60;
        public const float ClaimFeudCallVassalSupport = 60f;
        public const float ClaimFeudCallMarriageSupport = 50f;
        public const float ClaimFeudCallDynasticKinSupport = 45f;
        public const float ClaimFeudCallFriendSupportBase = 40f;
        public const float ClaimFeudCallFriendSupportScale = 0.5f;
        public const float ClaimFeudCallRelationComparisonScale = 0.25f;
        public const float ClaimFeudCallRelationComparisonCap = 20f;
        public const int ClaimFeudAiSupportRelationBonus = 8;
        public const int ClaimFeudAiRefuseVassalRelationPenalty = -15;
        public const int ClaimFeudAiRefuseMarriageRelationPenalty = -10;
        public const int ClaimFeudAiRefuseKinRelationPenalty = -10;
        public const int ClaimFeudAiRefuseFriendRelationPenalty = -5;
        public const int ClaimFeudPlayerSupportRelationBonus = 8;
        public const int ClaimFeudPlayerRefuseRelationPenalty = -12;
        public const float ClaimFeudRulingClanMarriageAllianceBonus = 20f;
        public const float ClaimFeudRulerHonorLegalBiasPerTrait = 8f;
        public const float ClaimFeudRulerPowerBiasPerTrait = 5f;
        public const float ClaimFeudRulerCalculatingPowerBiasPerTrait = 6f;
        public const float ClaimFeudRulerMercySuppressionBiasPerTrait = 8f;
        public const float ClaimFeudRulerCautiousSuppressionBiasPerTrait = 5f;

        public const int RoyalPeaceFeudBaseInfluenceCost = 300;
        public const int RoyalPeaceFeudLeaderTierCost = 50;
        public const int RoyalPeaceFeudSupporterTierCost = 25;
        public const int RoyalPeaceInvolvedRelationPenalty = -15;
        public const int RoyalPeaceRealmRelationPenalty = -5;
        public const float RoyalPeaceTyrantsDebtYears = 1f;
        public const float RoyalPeaceInfluenceGainMultiplier = 0.5f;

        // ------------------------------------------------------------
        // IDEOLOGY BASELINE (IdeologyBehavior.CalculateTargetBaseline)
        // ------------------------------------------------------------
        // Defined as int so they can be used as both int accumulators (tooltip
        // breakdown) and float targets (drift calculation) without casting.
        public const int  BaselinePolicySupportBonus   =  10;   // supported policy active
        public const int  BaselineRivalPolicyPenalty   =  10;   // rival faction's policy active
        public const int  BaselineNeutralPolicyPenalty =   5;   // unrelated policy active
        public const float BaselineGeopoliticPillar    =  10f;  // normal thematic world-state pillar
        public const float BaselineMajorCrisisPillar   =  15f;  // legitimacy, prolonged war, famine, rebellion

        // Geopolitical threshold values used in the switch block
        public const int   BaselineMilPeaceDayThreshold   =  30;      // Militarists get antsy after this many peace days
        public const int   BaselinePopPeaceDayThreshold   =  10;      // Populists want peace quickly
        public const int   BaselinePopWarDayThreshold     =  30;      // Populists suffer from prolonged war
        public const float BaselineMilRealmStrengthHigh   = 15000f;
        public const float BaselineMilRealmStrengthLow    =  5000f;
        public const float BaselinePopProsperityThreshold =  5000f;
        public const int   BaselinePopStarvingTownMinimum =     3;
        public const float BaselineRoyInfluenceHigh       =  1500f;
        public const float BaselineRoyInfluenceLow        =   200f;
        public const int   BaselineRoyRoyalFiefStrongMinimum =  5;
        public const int   BaselineRoyRoyalFiefWeakMaximum   =  2;
        public const int   BaselineRoyRightfulLiegeBonus  =    15;
        public const int   BaselineRoyUsurperPenalty      =    30;
        public const int   BaselineAriUnlawfulUpstartFiefMinimum = 3;

        // ------------------------------------------------------------
        // IDEOLOGY MOOD EVENTS  (IdeologyBehavior.Execute*Event)
        // Numeric effects are centralized here so player-facing event text
        // can append the exact applied outcome without drifting from balance.
        // ------------------------------------------------------------
        public const int MilEventParadeInfluence          =    50;
        public const float CourtAgendaSuccessShock = 10f;
        public const float CourtAgendaFailureShock = -10f;
        public const int MilEventParadeRenown             =    20;
        public const int MilEventReinforceSecurity        =    30;
        public const int MilEventReinforceLoyalty         =    20;
        public const int MilEventVeteranGiftTroops        =    15;
        public const int MilEventArmyCohesionBonus        =    30;
        public const int MilEventBraveLordRelationBonus   =     5;
        public const int MilEventArmyCohesionPenalty      =    40;
        public const int MilEventDesertionSecurityPenalty =    30;
        public const int MilEventDesertionLoyaltyPenalty  =    20;
        public const int MilEventMockeryInfluencePenalty  =    50;

        public const int PopEventFestivalLoyaltyBonus     =    20;
        public const int PopEventFestivalSecurityBonus    =    20;
        public const int PopEventSubsidiesHearthBonus     =    30;
        public const int PopEventDebtProsperityBonus      =   100;
        public const int PopEventWatchesSecurityBonus     =    15;
        public const int PopEventWatchesLoyaltyBonus      =    15;
        public const int PopEventFoodRelationBonus        =     5;
        public const int PopEventStrikesProsperityPenalty =   100;
        public const int PopEventStrikesLoyaltyPenalty    =    20;
        public const int PopEventRiotsLoyaltyPenalty      =    40;
        public const int PopEventRiotsSecurityPenalty     =    40;
        public const int PopEventSabotageHearthPenalty    =    30;
        public const int PopEventEvasionSecurityPenalty   =    15;
        public const int PopEventEvasionLoyaltyPenalty    =    15;
        public const int PopEventSlanderRelationPenalty   =     5;
        public const int PopRaidShockVillageThreshold     =     3;
        public const float PopRaidShockAmount             =    -5f;
        public const int PopRaidShockWindowDays           =     5;

        public const int RoyEventTributeGold              = 15000;
        public const int RoyEventFealtyInfluence          =    75;
        public const int RoyEventMandateInfluencePenalty  =    75;
        public const int RoyEventBoycottGoldPenalty       = 15000;
        public const int RoyEventGridlockProsperityPenalty=    50;
        public const int RoyEventGridlockSecurityPenalty  =    50;

        public const int AriEventCavalryGiftTroops        =    15;
        public const int AriEventMonopolyProsperityBonus  =   100;
        public const int AriEventBanquetRelationBonus     =     5;
        public const int AriEventStonemasonProsperityBonus=   100;
        public const int AriEventStonemasonSecurityBonus  =    20;
        public const int AriEventHoardProsperityPenalty   =   100;
        public const int AriEventSnubRelationPenalty      =     5;
        public const int AriEventLaborProsperityPenalty   =   100;
        public const int AriEventLaborSecurityPenalty     =    20;

        // Court agenda costs and faction reactions
        public const int CourtAgendaInfluenceCost = 100;  // player faction leader cost to block/override a court motion
        public const int CourtAgendaDeclineRelationPenalty = -5;
        public const int CourtAgendaBrokenPromiseRelationPenalty = -5;
        public const float CourtAgendaBrokenPromiseMemoryYears = 5f;
        public const int CourtPolicyMandateOutsideAgendaRelationPenalty = -10;
        public const int CourtPolicyMandateBetrayalRelationPenalty = -20;
        public const float CourtPolicyMandateOutsideAgendaMemoryYears = 7f;
        public const float CourtPolicyMandateBetrayalMemoryYears = 10f;
        public const float CourtRebellionSuppressMoodMin = -60f;
        public const float CourtRebellionSuppressMoodMax = -100f;
        public const int CourtRebellionSuppressInfluenceMin = 100;
        public const int CourtRebellionSuppressInfluenceMax = 500;
        public const float GrandCoalitionJoinMoodThreshold = -60f;

        // ------------------------------------------------------------
        // FEUDAL APATHY (FactionObject.CalculateLoyalistPower)
        // ------------------------------------------------------------
        public const float ApathyHappyMultiplier   = 0.75f;
        public const float ApathyNeutralMultiplier = 0.40f;
        public const float ApathyUnhappyMultiplier = 0.00f;

        // ------------------------------------------------------------
        // REBELLION DISCONTENT (FactionObject.DailyTick)
        // ------------------------------------------------------------
        public const float RebelliousIntentThreshold = 100f;
        public const float RebelliousMarriageAllianceIntentReduction = 50f;
        public const float RebelliousFiefDesireIntentCap = 30f;
        public const int RebelliousRulerReservedFiefs = 3;
        public const float RebelliousRulerHoardingIntentPerFief = 5f;
        public const float RebelliousRulerHoardingIntentCap = 30f;
        public const float RebelliousRulerHoardingSatisfiedMultiplier = 0.5f;
        public const int RebellionIntentReviewMinimumDays = 7;
        public const int RebellionIntentReviewMaximumDays = 14;
        public const float RebelFactionWeakSupportDiscontentFloor = 10f;
        public const float DiscontentGainBase    = 2f;   // base multiplier when power check passes
        public const float DiscontentDecayPerDay = 1f;
        public const float DiscontentTrigger     = 100f;
        public const float RebellionPowerThresholdBase = 0.80f;
        public const float RebelAbdicationBaseWeight = 10f;
        public const float RebelIndependenceCulturalStakeWeight = 50f;
        public const float RebelIndependenceHonorablePenalty = 20f;
        public const float RebelIndependenceBaronyTitleWeight = 10f;
        public const float RebelIndependenceCountyTitleWeight = 15f;
        public const float RebelIndependenceDuchyTitleWeight = 35f;
        public const float RebelIndependenceKingdomTitleWeight = 70f;
        public const float RebelIndependenceDeFactoOnlyTitleMultiplier = 0.5f;
        public const float RebelIndependenceCulturalTitleRegionBonus = 20f;
        public const float RebelInstallRulerStrongClaimJoinBonus = 40f;
        public const float RebelInstallRulerWeakClaimJoinBonus = 20f;
        public const float RebelInstallRulerRecognizedClaimJoinBonus = 60f;
        public const float RebelFactionRelationComparisonScale = 0.5f;
        public const float RebelFactionMaxRelationComparison = 75f;
        public const float RebelFactionFriendshipJoinBonus = 30f;
        public const float RebelFactionMaxReadinessScore = 25f;
        public const float RebelFactionPoliticalAlignmentFloor = 0f;
        public const float GrandCoalitionDesperateThresholdReduction = 0.25f;
        public const float GrandCoalitionDesperateThresholdFloor = 0.25f;

        // Immersive balance reports shown before the player commits to an internal conflict.
        // Shares between these thresholds are described as evenly matched.
        public const float ConflictBalanceAdvantageShare = 0.55f;
        public const float ConflictBalanceOverwhelmingShare = 0.65f;

        // Trait adjustments to the power-ratio threshold (shared pattern used in
        // FactionObject.DailyTick and FactionManagerBehavior succession crisis checks)
        public const float TraitThresholdAdjSmall  = 0.10f;
        public const float TraitThresholdAdjLarge  = 0.20f;
        public const float TraitThresholdFloor     = 0.40f;

        // ------------------------------------------------------------
        // FIEF VOTE AI  (FiefVoteAIPatch)
        // ------------------------------------------------------------

        // Bloc vote
        public const float FiefBlocBonus            =  35f;
        public const float FiefBlocCalculatingBonus =  10f;
        public const float FiefNonRivalPenalty      =  20f;

        // Fief need / satiation pressure
        public const float FiefNeedMissingOneBonus      =  20f;
        public const float FiefNeedMissingTwoPlusBonus  =  35f;
        public const float FiefNeedSatisfiedPenalty     =  15f;
        public const float FiefNeedExcessOnePenalty     =  40f;
        public const float FiefNeedExcessTwoPlusPenalty =  70f;

        // Candidate selection / ballot shaping
        public const float FiefCandidateNeedPerMissingFief     =  20f;
        public const float FiefCandidateNeedBonusCap           =  60f;
        public const float FiefCandidateNeedyVoterMultiplier   =   0.50f;
        public const float FiefCandidatePopulistNeedBonusOne   =   8f;
        public const float FiefCandidatePopulistNeedBonusTwoPlus = 12f;
        public const float FiefCandidateSameFactionNeedBonusOne =  6f;
        public const float FiefCandidateSameFactionNeedBonusTwoPlus = 10f;
        public const float FiefCandidateGenerousNeedBonusOne   =   5f;
        public const float FiefCandidateGenerousNeedBonusTwoPlus = 8f;
        public const float FiefCandidateMercifulNeedBonusOne   =   4f;
        public const float FiefCandidateMercifulNeedBonusTwoPlus = 6f;
        public const float FiefCandidateGreedyNeedPenaltyOne   =   8f;
        public const float FiefCandidateGreedyNeedPenaltyTwoPlus = 12f;
        public const float FiefCandidateCalculatingNeedPenaltyOne = 5f;
        public const float FiefCandidateCalculatingNeedPenaltyTwoPlus = 8f;
        public const float FiefCandidateSatisfiedPenalty       =  20f;
        public const float FiefCandidateExcessPenaltyPerFief   =  40f;
        public const int   FiefCandidateOrdinaryExcessAllowance =  2;
        public const float FiefCandidateCapturerBonus          =  30f;
        public const float FiefCandidateCultureBonus           =  15f;
        public const float FiefCandidateRenownTierScale        =   3f;
        public const float FiefCandidateStrengthScale          =   0.01f;
        public const float FiefCandidateInfluenceScale         =   0.01f;
        public const float FiefCandidateStrengthBonusCap       =  25f;
        public const float FiefCandidateInfluenceBonusCap      =  15f;
        public const float FiefCandidateMinimumScore           =   1f;

        // Legal title claims
        public const float FiefClaimDeJureBonus                =  45f;
        public const float FiefClaimStrongBonus                =  35f;
        public const float FiefClaimWeakBonus                  =  20f;
        public const float FiefClaimParentTitleMultiplier      =   0.50f;
        public const float FiefClaimAristocratMultiplier       =   1.35f;
        public const float FiefClaimRoyalistMultiplier         =   0.85f;
        public const float FiefClaimMilitaristMultiplier       =   0.50f;
        public const float FiefClaimPopulistMultiplier         =   0.35f;
        public const float FiefClaimNeutralMultiplier          =   0.75f;
        public const int   FiefClaimFriendRelationThreshold    =  60;
        public const float FiefClaimFriendSympathyMultiplier   =   0.25f;
        public const float FiefClaimFriendSympathyCap          =  15f;

        // Militarist quirks
        public const float FiefMilWeakStrengthThreshold   =  350f;
        public const float FiefMilStrongStrengthThreshold = 1200f;
        public const float FiefMilWeakClanPenalty         =   25f;
        public const float FiefMilCalcPenalty             =   20f;
        public const float FiefMilStrongClanBonus         =   20f;

        // Aristocrat quirks
        public const float FiefAristLandlessPenalty    =  25f;
        public const float FiefAristBaronyBonus        =   5f;
        public const float FiefAristCountyBonus        =  12f;
        public const float FiefAristDuchyBonus         =  22f;
        public const float FiefAristKingdomBonus       =  32f;
        public const float FiefAristEmpireBonus        =  40f;
        public const float FiefAristRelationMultiplier = 1.25f;
        public const float FiefAristHighTitleRelationMultiplier = 1.50f;
        public const float FiefAristMarriageAllianceBonus = 45f;

        // Populist quirks
        public const float FiefPopBaronyOnlyBonus     =  12f;
        public const float FiefPopHighTitlePenalty    =  15f;
        public const float FiefPopLandlessBonus       =  25f;
        public const float FiefPopLowInfluencePenalty =  10f;
        public const float FiefPopHighInfluenceBonus  =  15f;
        public const float FiefPopInfluenceLowThreshold  =  500f;
        public const float FiefPopInfluenceHighThreshold = 2000f;

        // Royalist quirks
        public const float FiefRoyKingBonus         =  25f;
        public const float FiefRoyEnemyPenalty      =  25f;
        public const float FiefRoyFriendBonus       =  15f;
        public const float FiefRoyPeerNeedBonus     =  35f;
        public const float FiefRoyKingSatisfiedPenalty = 40f;
        public const float FiefRoyKingExcessPenalty    = 80f;
        public const float FiefRoyRoyalMarriageAllianceBonus = 45f;
        public const int   FiefRoyEnemyRelThreshold = -10;
        public const int   FiefRoyFriendRelThreshold=  50;

        // Welfare / landless trait modifiers
        public const float FiefLandlessGenerousBonus  =  20f;
        public const float FiefLandlessMercifulBonus  =  15f;
        public const float FiefLandlessGreedyPenalty  =  40f;
        public const float FiefLandlessCalcPenalty    =  30f;

        // Capturer's right
        public const float FiefCapturerHonorBonus  =  20f;
        public const float FiefCapturerCalcPenalty =  10f;

        // Relationship scaling
        public const float FiefRelScale            =  0.25f;  // +100 rel -> +25 merit
        public const float FiefRelCalcMultiplier   =  0.5f;   // calculating lords care less
        public const float FiefRelHotheadMultiplier=  1.25f;  // hotheaded lords care more

        // Spite vote
        public const float FiefSpiteBase              =  20f;
        public const float FiefSpiteRebelliousBonus   =  25f;  // extra when faction mood <= FiefSpiteMoodReq
        public const float FiefSpiteHotheadBonus      =  15f;
        public const float FiefAntiEstablishmentRally =  25f;
        public const int   FiefSpiteRelThreshold      = -20;
        public const float FiefSpiteMoodReq           = -50f;  // faction mood required for rebellious spite

        // Tyrant / Good King
        public const float FiefTyrantGreedBonus       =  45f;
        public const float FiefTyrantDishonorBonus    =  45f;
        public const float FiefTyrantCalcBonus        =  25f;
        public const float FiefGoodKingGenerousPenalty=  60f;
        public const float FiefGoodKingHonorPenalty   =  35f;
        public const float FiefGoodKingLandlessBonus  =  45f;

        // ------------------------------------------------------------
        // POLICY VOTE AI  (PolicyVoteAIPatch)
        // ------------------------------------------------------------

        // POLICY DELIBERATION WINDOW  (PolicyDeliberationBehavior)
        // Days between a faction announcing a policy motion and the vote actually firing.
        // During this window the player can query lord stances and bribe votes.
        public const int PolicyDeliberationDays            = 5;
        // Bribe costs scale with how strongly the lord opposes the direction you want.
        // Neutral lord (|score| <= 10):              cheapest - light nudge needed.
        // Agrees/Disagrees (|score| 10-100):         mid-tier - real resistance to overcome.
        // Strongly for/against (|score| > 100):     most expensive - deeply committed.
        public const int PolicyBribeCostNeutral            =  50000;
        public const int PolicyBribeCostMid                = 100000;
        public const int PolicyBribeCostHigh               = 150000;
        // The forced DetermineSupport score written into the vote when a bribe succeeds.
        // Large enough to beat all other factors and lock the lord into the desired outcome.
        public const int PolicyBribeForcedSupportScore     = 500;
        // Don't offer a "sway to X" option if the lord's score already exceeds this threshold
        // in that direction - they're already firmly committed that way.
        public const int PolicyBribeSameDirectionThreshold = 100;
        public const int PolicyFactionProposalInfluenceCost = 100;

        public const float PolicyFactionLoyaltyBase    =  80f;
        public const float PolicyNeutralPenalty        =  35f;
        public const float PolicyHonorMultHigh         = 1.25f;
        public const float PolicyHonorMultLow          = 0.75f;
        public const float PolicyRelScale              = 0.5f;  // (relation / 100f) * 50f
        public const float PolicyCalcMult2             = 0.50f;
        public const float PolicyCalcMult1             = 0.75f;
        public const float PolicyCalcMultMinus1        = 1.25f;
        public const float PolicyCalcMultMinus2        = 1.50f;
        public const float PolicyMercyMultLow          = 0.5f;
        public const float PolicyMercyMultHigh         = 1.5f;
        public const float PolicyValorCowardPenalty    =  50f;
        public const float PolicyRivalrySpitePenalty   =  15f;
        public const float PolicyKingCrownInterestBonus        =  50f;
        public const float PolicyKingRoyalistIdentityBonus     =  40f;
        public const float PolicyKingCalculatingCrownBonus     =  10f;
        public const float PolicyKingTyrantCrownBonus          =  30f;
        public const float PolicyKingTyrantAristPenalty        =  70f;
        public const float PolicyKingTyrantPopulistPenalty     =  90f;
        public const float PolicyKingGoodPopulistBonus         =  45f;

        // ------------------------------------------------------------
        // KING SELECTION AI  (KingSelectionAIPatch)
        // ------------------------------------------------------------
        public const float KingSelectSelfVoteBonus = 300f;
        public const float KingSelectBaseSupport = 25f;
        public const float KingSelectMinimumSupport = -100f;
        public const float KingSelectMaximumSupport = 150f;
        public const float KingSelectRelationScale = 0.60f;
        public const float KingSelectPoliticalStandingRange = 20f;
        public const float KingSelectIdeologyPreferenceRange = 60f;
        public const float KingSelectFactionEndorsementBonus = 15f;
        public const float KingSelectNominationRelationScale = 0.35f;
        public const float KingSelectNominationSelfBonus = 15f;
        public const float KingSelectPopulistPopularityRange = 20f;
        public const float KingSelectMajorPillarWeight = 0.30f;
        public const float KingSelectMinorPillarWeight = 0.20f;
        public const float KingSelectNeutralPillarWeight = 0.125f;
        public const int   KingSelectPersonalLoveThreshold = 50;
        public const float KingSelectPersonalLoveBonus = 10f;
        public const float KingSelectRoyalistRestorationBonus = 35f;
        public const float KingSelectRoyalistAbdicationProposerPenalty = 40f;
        public const float KingSelectRoyalistPretenderPenalty = 35f;
        public const float KingSelectHonorDisciplineStep1 = 4f;
        public const float KingSelectHonorDisciplineStep2 = 8f;
        public const float KingSelectHonorLegitimacyScale = 10f;
        public const float KingSelectCalculatingPoliticalScale = 8f;
        public const float KingSelectValorStrengthScale = 6f;
        public const float KingSelectPersonalityAffinityScale = 1f;

        // ------------------------------------------------------------
        // FACTION LEADER PULL  (FiefVoteAIPatch, ExpulsionVoteAIPatch, PolicyVoteAIPatch)
        // Non-leader members get a soft nudge toward their leader's likely vote,
        // scaled by their personal trust in the leader (their relation with them).
        // Max effect: leaderRel 100 -> trust 100 -> 0.3 = 30 merit (~30% of a bloc bonus).
        // ------------------------------------------------------------
        public const float FactionLeaderPullStrength = 0.3f;

        // ------------------------------------------------------------
        // COALITION LOYALTY CHOICE  (NotificationHelper + IdeologyBehavior)
        // When a grand coalition fires and the player is asked which side they take:
        // CoalitionLoyalistRelPenalty:    per-rebel relation hit for choosing the crown
        // CoalitionLoyalistKingBonus:     one-time bonus with the king for loyalty
        // CoalitionLoyalistRoyalistBonus: small bonus with each Royalist member
        // CoalitionRebelLeaderBonus:      one-time bonus with the coalition leader for joining
        // CoalitionRebelMemberBonus:      small bonus with each coalition member for joining
        // ------------------------------------------------------------
        public const int CoalitionLoyalistRelPenalty    = 10;
        public const int CoalitionLoyalistKingBonus     = 20;
        public const int CoalitionLoyalistRoyalistBonus =  5;
        public const int CoalitionRebelLeaderBonus      = 20;
        public const int CoalitionRebelMemberBonus      =  5;

        // ------------------------------------------------------------
        // EXPULSION VOTE AI  (ExpulsionVoteAIPatch)
        // ------------------------------------------------------------
        public const float ExpelBlocBonus           =  70f;
        public const float ExpelNonRivalBonus       =  15f;
        public const float ExpelRoyalistBlindFollow =  90f;
        public const float ExpelSpiteAgainstKing    =  50f;
        public const float ExpelSpiteHotheadBonus   =  25f;
        public const int   ExpelSpiteRelThreshold   =  -20;
        public const float ExpelRulerWill           = 150f;
        public const float ExpelTyrantBonus         =  60f;
        public const int   ExpelTargetSuspicionThresholdLow    = -70;
        public const int   ExpelTargetSuspicionThresholdHigh   = -85;
        public const int   ExpelTargetSuspicionThresholdSevere = -95;
        public const float ExpelTargetSuspicionBonusLow        =  20f;
        public const float ExpelTargetSuspicionBonusHigh       =  35f;
        public const float ExpelTargetSuspicionBonusSevere     =  50f;
        public const float ExpelTargetRelationScale          = 0.25f;
        public const float ExpelTargetRelationCalcMultiplier = 0.5f;
        public const float ExpelTargetRelationHotheadMultiplier = 1.25f;

        // ------------------------------------------------------------
        // FIEF VOTE RESOLUTION - JEALOUSY ENGINE  (FiefVoteResolutionPatch)
        // ------------------------------------------------------------
        public const int FiefResolveTyrannyPenalty        = -10;
        public const int FiefResolveWinnerToRulerBonus    =  10;
        public const int FiefResolveWinnerToFellowBonus   =   5;
        public const int FiefResolveLoserToRulerPenalty   = -10;
        public const int FiefResolveLoserToWinnerPenalty  = -10;
        public const int FiefResolveLoserPeerPenalty      =  -5;
        public const int FiefResolvePersonalFriendThreshold =  20;
        public const int FiefResolvePersonalEnemyThreshold  = -20;
        public const int FiefResolvePersonalFriendBonus   =   5;
        public const int FiefResolvePersonalEnemyPenalty  =  -5;

        // Privy council appointment aftermath.
        public const float CouncilAppointmentFactionMood = 10f;
        public const float CouncilRetentionFactionMood = 5f;
        public const float CouncilPassedOverFactionMood = -5f;
        public const float CouncilOverrideFactionMood = -10f;
        public const float CouncilDismissalFactionMood = -15f;
        public const int CouncilAppointmentRelationGain = 10;
        public const int CouncilRetentionRelationGain = 5;
        public const int CouncilOverrideRelationPenalty = -5;
        public const int CouncilDismissalRelationPenalty = -15;
        public const int SuccessionVoteSupportRelationGain = 10;
        public const int SuccessionVoteOppositionRelationPenalty = -10;

        // Dispossessed lord - targeted penalties that stack on top of any faction-loss penalties
        // the stripped lord already receives as a member of a losing ideology.
        public const int   FiefResolveDispossessedRulerPenalty  = -15;  // bitter at king for permitting the revocation
        public const int   FiefResolveDispossessedWinnerPenalty = -20;  // bitter at whoever took their land
        public const float FiefResolveDispossessedFactionMoodHit =  8f; // direct faction mood drop from the humiliation
        // UI visibility duration is intentionally derived from FiefResolveDispossessedFactionMoodHit at the call site
        // (mood recovers at 1/day, so duration = magnitude of the hit)

        // Minimum relation with any rebel faction leader below which a lord refuses
        // to conspire with them, regardless of how attractive the cause may be.
        public const int RebelFactionLeaderRivalryThreshold = -50;
        // A costly bargain to join the player's conspiracy binds the recruited clan long
        // enough for a normally paced rebel faction to prepare and issue its ultimatum.
        public const int RebelFactionBribedJoinCommitmentDays = 100;
        // Stronger personal feud threshold used once a lord is inside a rebel faction.
        // If they hate the leader this much, they leave and will not reconsider that leader soon.
        public const int RebelFactionRejoinCooldownDays = 30;
        public const float RebelFactionHierarchicalLoyaltyJoinThreshold = 35f;
        public const float RebelFactionHierarchicalDirectLiegePull = 120f;
        public const float RebelFactionHierarchicalHighLiegePull = 60f;

        // Relation changes when an AI lord defects between ideology factions at a meeting.
        // Leave penalties are half the player's deliberate-leave values (-20/-10) since this is
        // organic drift, not a conscious walkout. Join bonuses are smaller still - alliance
        // formation carries less emotional weight than betrayal.

        // Court-faction selection weights used by IdeologyBehavior.CalculateRawIdeologyScores.
        // Each ideology has two political components, each bounded independently.
        public const float IdeologyPoliticalComponentCap = 12f;
        public const float IdeologyEstablishedRealmYears = 20f;
        public const float IdeologyLegacyRealmYears = 10f;
        public const float IdeologyFirstSubordinatePull = 6f;
        public const float IdeologyAdditionalSubordinatePull = 3f;
        public const float IdeologyMilitaryReferenceFloor = 300f;
        public const float IdeologyMilitaryMeanMultiplier = 2f;
        public const float IdeologyPrimaryTraitScorePerLevel = 8f;
        public const float IdeologySecondaryTraitScorePerLevel = 4f;
        public const float IdeologyFactionMemberMarriageAllianceBonus = 2f;
        public const float IdeologyFactionMemberMarriageAllianceCap = 4f;
        public const float IdeologyFactionFriendshipCap = 4f;
        public const float IdeologyFactionSocialGravityCap = 12f;
        public const float IdeologyFactionImmediateLiegePull = 4f;
        public const float IdeologyFactionHighLiegePull = 2f;
        public const int   IdeologyFactionFriendshipThreshold = 20;
        public const float IdeologyFactionFriendshipScale = 0.05f;
        public const float IdeologyInitialNoiseMin = 0f;
        public const float IdeologyInitialNoiseMax = 10f;

        // How many raw score points a rival ideology must lead by before a lord switches at a faction meeting.
        // Keeps lords from flip-flopping on minor fluctuations - a shift needs to reflect something real
        // (e.g. lost land, new vassals, or a marriage alliance breaking).
        public const float IdeologySwitchThreshold = 4f;
        public const float IdeologyLeaderSwitchThreshold = 8f;

        // ------------------------------------------------------------
        // EXPULSION DELIBERATION WINDOW  (ExpulsionDeliberationBehavior)
        // ------------------------------------------------------------
        public const int   ExpulsionDeliberationDays            = 5;
        public const int   ExpulsionBribeCostNeutral            =  50000;
        public const int   ExpulsionBribeCostMid                = 100000;
        public const int   ExpulsionBribeCostHigh               = 150000;
        public const int   ExpulsionBribeForcedSupportScore     = 500;
        public const int   ExpulsionBribeSameDirectionThreshold = 100;

        // ------------------------------------------------------------
        // POLICY VOTE RESOLUTION  (PolicyVoteResolutionPatch)
        // ------------------------------------------------------------
        public const int PolicyResolveTurncoatLeaderPenalty = -20;
        public const int PolicyResolveTurncoatPeerPenalty   = -10;
        public const int PolicyResolveWinnerToRulerBonus    =  10;
        public const int PolicyResolveLoserToRulerPenalty   =  -5;
        public const int PolicyResolveWinnerToLeaderBonus   =   5;
        public const int PolicyResolveCrossPartyBonus       =   2;
        public const int PolicyResolveBitterDefeatPenalty   =  -5;
        public const int PolicyResolveComradesBonus         =   2;
        public const int PolicyResolveTriumphantAlliesBonus =   2;

        // ------------------------------------------------------------
        // TREASON INDICTMENT CONSEQUENCES
        // ------------------------------------------------------------
        public const float ExpelFactionMoodShock         = 10f;
        public const int   TreasonRejectedRelationDrop   = 10;
        // UI visibility duration derived from ExpelFactionMoodShock at call site
        // (mood recovers at 1/day, so duration = magnitude of the hit)

        // ------------------------------------------------------------
        // EXPELLED LORD REBELLION  (IdeologyBehavior.TryExpelledLordRebellion)
        // ------------------------------------------------------------
        public const float ExpelRebelBaseChance          = 0.30f;
        // Honor: honorable accepts lawful judgment; dishonorable defies it
        public const float ExpelRebelHonorHighPenalty    = 0.15f;  // honor >= 2: -15%
        public const float ExpelRebelHonorMidPenalty     = 0.08f;  // honor == 1: -8%
        public const float ExpelRebelDisgraceMidBonus    = 0.08f;  // honor == -1: +8%
        public const float ExpelRebelDisgraceHighBonus   = 0.15f;  // honor <= -2: +15%
        // Valor: daring lords raise banners; cowards accept fate
        public const float ExpelRebelValorHighBonus      = 0.20f;  // valor >= 2: +20%
        public const float ExpelRebelValorMidBonus       = 0.10f;  // valor == 1: +10%
        public const float ExpelRebelCowMidPenalty       = 0.10f;  // valor == -1: -10%
        public const float ExpelRebelCowHighPenalty      = 0.20f;  // valor <= -2: -20%
        // Calculating: hothead ignores odds; strategist checks power ratio first
        public const float ExpelRebelHotheadHighBonus    = 0.15f;  // calc <= -2: +15%
        public const float ExpelRebelHotheadMidBonus     = 0.08f;  // calc == -1: +8%
        public const float ExpelRebelCalcPowerGate       = 0.50f;  // calc >= 1: suppress if own power < 50% of loyalist power

        // Ideology and context modifiers for the player-ruler treason indictment rebel-chance roll.
        public const int   TreasonInductRelationThreshold      = -60;
        public const int   HighTreasonInductRelationThreshold  = -100;
        public const int   TreasonDecreeRelationThreshold      = -100;
        public const int   TreasonSolidarityFriendRelationThreshold = 60;
        public const int   TreasonSolidarityRulerLoyalRelationThreshold = 60;
        public const float TreasonSolidarityMarriageSupport = 50f;
        public const float TreasonSolidarityDirectVassalSupport = 60f;
        public const float TreasonSolidarityFriendSupportBase = 30f;
        public const float TreasonSolidarityFriendSupportScale = 0.5f;
        public const float TreasonSolidaritySameFactionSupport = 15f;
        public const float TreasonSolidarityAngryFactionSupport = 10f;
        public const float TreasonSolidarityRebelliousFactionSupport = 20f;
        public const float TreasonSolidarityJoinThreshold = 70f;
        public const float TreasonSolidarityRollThreshold = 40f;
        public const float TreasonSolidarityRebelChanceScale = 500f;
        public const float TreasonSolidarityMaxRebelChanceBonus = 0.30f;

        // Civil-war outbreak solidarity. This is shared by ordinary rebel factions and
        // court grand coalitions when banners are actually raised.
        public const float CivilWarSolidarityRebelliousIntentThreshold = 100f;
        public const float CivilWarSolidarityIntentContributionScale = 0.40f;
        public const float CivilWarSolidarityMaxIntentContribution = 40f;
        public const float CivilWarSolidarityRelationComparisonScale = 0.25f;
        public const float CivilWarSolidarityMaxRelationComparison = 20f;
        public const float CivilWarSolidarityCrownMarriagePenalty = 50f;
        public const float CivilWarSolidarityLoyalRoyalistPenalty = 30f;
        public const float CivilWarSolidarityStrongRulerRelationPenalty = 30f;
        public const float CivilWarSolidarityDynasticKinSupport = 45f;
        public const int CivilWarSolidarityJoinSponsorRelation = 10;
        public const int CivilWarSolidarityJoinRulerRelation = -10;
        public const int CivilWarSolidarityRefuseLiegeRelation = -15;
        public const int CivilWarSolidarityRefuseRulerRelation = 5;
        public const int CivilWarSolidarityRefuseMarriageRelation = -10;
        public const int CivilWarSolidarityRefuseKinRelation = -10;
        public const int CivilWarSolidarityRefuseFriendRelation = -5;
        public const int CivilWarSolidarityHierarchyPassLimit = 8;

        // ------------------------------------------------------------
        // EXPULSION RESOLUTION  (ExpelClanDecisionPatch)
        // ------------------------------------------------------------
        public const int ExpelResolveVictimGrudge        = -10;
        public const int ExpelResolveFriendDragged       =  -5;
        public const int ExpelResolveFriendRelThreshold  =  10;

        // ------------------------------------------------------------
        // POST-WAR TRIBUNAL  (CivilWarResolutionBehavior.ApplyPostWarConsequences)
        // All three rolls use the ruler's traits. Execution and confiscation share
        // a linkage: confiscation is guaranteed when execution fires so the clan
        // never loses its leader while still holding fiefs.
        // ------------------------------------------------------------

        // Execution - clan leader put to death. Range: 0% (mercy 2) -> 20% (mercy -2), neutral 10%.
        public const float PostWarExecutionBase       = 0.10f;
        public const float PostWarExecutionMercyStep1 = 0.05f;  // |mercy| == 1: +/-5%
        public const float PostWarExecutionMercyStep2 = 0.10f;  // |mercy| >= 2: +/-10%

        // Confiscation - most-prosperous fief seized if execution does not fire.
        // Range: 10% (generosity 2) -> 30% (generosity -2), neutral 20%.
        public const float PostWarConfiscationBase              = 0.20f;
        public const float PostWarConfiscationGenerosityStep1   = 0.05f;  // |generosity| == 1: +/-5%
        public const float PostWarConfiscationGenerosityStep2   = 0.10f;  // |generosity| >= 2: +/-10%

        // Relation ripple applied during tribunal resolution.
        public const int PostWarPardonBonus            =  25;  // pardoned clan leader -> victor ruler
        public const int PostWarExilePenalty           = -20;
        public const int PostWarConfiscationPenalty    = -15;  // confiscated clan leader -> victor ruler

        // Loyalist reward scoring - who receives each confiscated fief.
        // Candidates are sorted once before the loop; fiefs rotate through the ranked list
        // so no single house concentrates all rewards from one resolution pass.
        public const float PostWarRewardRelWeight        =  1.0f;  // relation * this = base score
        public const float PostWarRewardLandlessBonus    = 30.0f;  // flat bonus for fiefless candidates
        public const float PostWarRewardNeedBonusPerFief = 15.0f;  // per fief below desired count
        public const float PostWarRewardPowerPer1k       = 10.0f;  // per 1000 strength units
        public const float PostWarRewardTierWeight       =  5.0f;  // tier * this = tiebreaker (0-30)
        public const float PostWarRewardTraitScaleStep1  =  0.25f; // +/-25% for need/power at trait level +/-1
        public const float PostWarRewardTraitScaleStep2  =  0.50f; // +/-50% for need/power at trait level +/-2
        public const float CivilWarVictoryMemberInfluenceReward = 100f;
        public const float CivilWarVictoryLeaderInfluenceReward = 150f;

        // ------------------------------------------------------------
        // KING'S FURY  (FactionManagerBehavior.ApplyKingsFury)
        // ------------------------------------------------------------
        public const float KingsFuryHotheadAdd        =  0.5f;
        public const float KingsFuryCalcReduce        =  0.2f;
        public const float KingsFuryMercifulReduce    =  0.3f;
        public const float KingsFuryCruelAdd          =  0.3f;
        public const float KingsFuryMultiRebellionAdd =  0.5f;
        public const float KingsFuryParanoiaMin       =  0.5f;
        public const float KingsFuryParanoiaMax       =  2.5f;
        public const int   KingsFuryLoyalistBonus     =    2;
        public const float KingsFuryNeutralBase       =   2f;  // * paranoiaMultiplier -> relation penalty
        public const float KingsFuryTraitorBase       =   5f;  // * paranoiaMultiplier -> relation penalty

        // ------------------------------------------------------------

        // Revocation vote support
        public const float RevokeOwnerSelfDefenseScore             = 400f;
        public const float RevokeOwnerFriendRelationScale          =   0.70f;
        public const float RevokeOwnerEnemyRelationScale           =   0.50f;
        public const float RevokeOwnerMarriageAllianceDefense      =  60f;
        public const float RevokeSameFactionDefense                =  35f;
        public const float RevokeOwnerDeJureDefense                =  60f;
        public const float RevokeOwnerOnlyDeFactoBonus             =  40f;
        public const float RevokeOwnerStrongClaimDefense           =  35f;
        public const float RevokeOwnerWeakClaimDefense             =  20f;
        public const float RevokeOwnerNoClaimBonus                 =  25f;
        public const float RevokeInternalDeJureClaimantBonus       =  50f;
        public const float RevokeInternalStrongClaimantBonus       =  40f;
        public const float RevokeInternalWeakClaimantBonus         =  25f;
        public const float RevokeClaimantFriendRelationScale       =   0.15f;
        public const float RevokeClaimantFriendSympathyCap         =  15f;
        public const float RevokeLegalAristocratMultiplier         =   1.35f;
        public const float RevokeLegalRoyalistMultiplier           =   0.85f;
        public const float RevokeLegalMilitaristMultiplier         =   0.50f;
        public const float RevokeLegalPopulistMultiplier           =   0.35f;
        public const float RevokeLegalNeutralMultiplier            =   0.75f;
        public const float RevokeAristocratUpstartBonus            =  25f;
        public const float RevokeAristocratOldBloodDefense         =  35f;
        public const float RevokeRoyalistDisloyaltyScale           =   0.35f;
        public const float RevokeRoyalistMarriageDefense           =  80f;
        public const float RevokeMilitaristWeakCastleHolderBonus   =  25f;
        public const float RevokeMilitaristStrongHolderDefense     =  20f;
        public const float RevokePopulistOverlandedBonus           =  25f;
        public const float RevokePopulistPoorOwnerDefense          =  35f;


        // ------------------------------------------------------------
        // FIEF CAPTURE DELIBERATION  (FiefDeliberationBehavior)
        // 5-day window between fief capture / gift-to-kingdom and the vote firing.
        // The player may bribe lords to favour a specific ideological faction.
        // ------------------------------------------------------------
        public const int FiefDeliberationDays        = 5;
        public const int FiefBribeCostNeutral        =  50000;
        public const int FiefBribeCostMid            = 100000;
        public const int FiefBribeCostHigh           = 150000;
        public const int FiefBribeForcedSupportScore = 300;   // awarded to every candidate from the preferred faction
        public const float FiefPersuasionGoal = 2f;
        public const float FiefPersuasionSuccessValue = 1f;
        public const float FiefPersuasionCriticalSuccessValue = 2f;
        public const float FiefPersuasionCriticalFailValue = 1f;
        public const float FiefBribeOpennessThreshold = 35f;
        public const float FiefBribeOpennessBase = 50f;
        public const float FiefBribeHonorPenalty = 25f;
        public const float FiefBribeMercyPenalty = 15f;
        public const float FiefBribeGenerosityPenalty = 10f;
        public const float FiefBribeDishonorBonus = 20f;
        public const float FiefBribeCrueltyBonus = 10f;
        public const float FiefBribeGreedBonus = 20f;
        public const float FiefBribeCalculatingBonus = 10f;
        public const float FiefBribeHotheadPenalty = 5f;
        public const float FiefBribePlayerRelationScale = 0.4f;
        public const float FiefBribeSelectedRelationScale = 0.25f;
        public const float FiefBribeNomineeRelationScale = 0.4f;
        public const float FiefBribeLegitimateClaimBonus = 20f;
        public const float FiefBribeWeakClaimPenalty = 20f;

        // ------------------------------------------------------------
        // DYNAMIC ARMY INFLUENCE COSTS  (DynamicArmyManagementModel)
        // Mood thresholds specific to the army cost curve (+/-60 extremes).
        // The +/-20 band reuses MoodThresholdHappy / MoodThresholdUnhappy above.
        // ------------------------------------------------------------
        public const float ArmyMoodContent =  60f;   // >= this -> content rate
        public const float ArmyMoodFurious = -60f;   // <= this -> furious rate

        // General cross-faction multipliers (ruler's mood-based curve)
        public const float ArmyCostContent      = 1.00f;  // mood >= +60
        public const float ArmyCostMildContent  = 1.20f;  // mood +20 to +60
        public const float ArmyCostNeutral      = 1.60f;  // mood -20 to +20  (baseline friction)
        public const float ArmyCostUnhappy      = 2.00f;  // mood -60 to -20
        public const float ArmyCostFurious      = 3.00f;  // mood <= -60

        // Non-ruler cross-faction floor - same-faction (1.00) is always cheaper than cross-faction.
        // Anything >= +20 mood collapses to this floor; the penalty tiers below still apply.
        public const float ArmyCostCrossFactionFloor = 1.20f;
        public const float ArmyCostUnaffiliated      = 3.00f;  // caller has no ideological affiliation

        // Royalist-specific multipliers (king calling his own loyal Royalists - softer ceiling)
        public const float ArmyCostRoyalContent     = 0.80f;  // mood >= +60  (peak loyalty discount)
        public const float ArmyCostRoyalNeutral     = 1.00f;  // mood 0 to +60
        public const float ArmyCostRoyalMildUnhappy = 1.25f;  // mood -20 to 0
        public const float ArmyCostRoyalUnhappy     = 1.50f;  // mood -60 to -20
        public const float ArmyCostRoyalFurious     = 2.00f;  // mood <= -60
        public const float ArmyCostDisloyalRoyalist = 3.00f;  // Royalists loyal to a different king
        public const int ArmyCallToArmsRelationRefusalThreshold = -60;
        public const int ArmyPersonalFriendRelationThreshold = 60;

        // ------------------------------------------------------------
        // PARTITION SUCCESSION  (PartitionSuccessionBehavior)
        // NPC-only first pass: landed noble houses can splinter into cadet
        // branches after a clan leader dies, keeping large dynasties from
        // hoarding every inherited estate forever.
        // ------------------------------------------------------------
        public const int PartitionSuccessionMainHeirReservedFiefs = 1;
        public const int PartitionSuccessionCadetTierPenalty = 1;
        public const int PartitionSuccessionMaxPendingDays = 7;
        public const int CadetBranchParentRelationBonus = 30;
        public const float CadetBranchDynasticHeadRebellionReduction = 100f;

        // ------------------------------------------------------------
        // STRATEGIC MARRIAGES  (StrategicMarriageBehavior)
        // A conservative NPC marriage layer that treats royal marriages as political
        // instruments while still prioritizing endangered dynasties.
        // ------------------------------------------------------------
        public const int   MarriageStrategyDaysPerYear = 84;
        public const float MarriageStrategyBaseAnnualChance = 0.10f;
        public const float MarriageStrategyDynasticNeedChanceScale = 0.005f;
        public const float MarriageStrategyMinimumScore = 95f;
        public const float MarriageStrategyMaximumAnnualChance = 0.85f;
        public const int   MarriagePlayerOfferCooldownDays = 7;
        public const int   MarriageMaleMinimumAge = 25;
        public const int   MarriageFemaleMinimumAge = 25;
        public const int   MarriageFemaleMaximumAge = 41;

        public const float MarriageNeedNoFertileCouple = 55f;
        public const float MarriageNeedFewAdultNobles = 35f;
        public const float MarriageNeedOneAdultNoble = 65f;
        public const float MarriageNeedOldLeader = 25f;
        public const float MarriageNeedVeryOldLeader = 45f;
        public const float MarriageNeedNoChildren = 20f;
        public const float MarriageNeedTierScale = 4f;

        public const float MarriageScoreDynasticNeedScale = 0.65f;
        public const float MarriageScoreSameKingdom = 45f;
        public const float MarriageScoreAlliedKingdom = 45f;
        public const float MarriageScoreSameCulture = 30f;
        public const float MarriageScoreClanRelationScale = 0.35f;
        public const float MarriageScorePersonalRelationScale = 0.20f;
        public const float MarriageScoreAgePenalty = 0.75f;
        public const float MarriageScoreRankGapPenalty = 18f;
        public const float MarriageScoreRankGapDesperationNeed = 90f;
        public const float MarriageScoreRankGapMaxDesperationReduction = 0.75f;
        public const float MarriageScoreRulerPacifyBase = 35f;
        public const float MarriageScoreRebelFactionLeader = 90f;
        public const float MarriageScoreRebelFactionMember = 55f;
        public const float MarriageScoreRebelDiscontentScale = 0.45f;
        public const float MarriageScoreNonRoyalistCourtFaction = 25f;
        public const float MarriageScoreRoyalistCourtFaction = 20f;
        public const float MarriageScoreTargetPowerScale = 0.01f;
        public const float MarriageScoreTargetInfluenceScale = 0.02f;
        public const float MarriageHeirWithoutChildrenValue = 80f;
        public const float MarriageHeirWithChildrenValue = 20f;
        public const float MarriageDepartingBloodlineNeedScale = 0.25f;
        public const float MarriageEndangeredHouseDepartureCost = 25f;
        public const float MarriageScoreAtWarBlockPenalty = 10000f;
        public const float MarriageScoreForeignRulingClanAllianceBase = 70f;
        public const float MarriageScoreForeignNoAllianceBonus = 40f;
        public const float MarriageScoreForeignCommonEnemyBonus = 45f;
        public const float MarriageScoreForeignThreatenedBonus = 35f;
        public const float MarriageScoreForeignSameCultureBonus = 20f;
        public const float MarriageScoreForeignRelationScale = 0.40f;
        public const float MarriageScoreForeignTooManyAlliesPenalty = 120f;
        public const float MarriageScoreRoyalDynasticReservePenalty = 90f;
        public const float MarriageScoreRoyalLastSparePenalty = 45f;
        public const int   MarriageRoyalMinimumSpareAdultsForForeign = 2;
        public const int   MarriageRoyalPreferredSpareAdults = 3;
        public const float MarriageScoreWeakTitleClaim = 15f;
        public const float MarriageScoreStrongTitleClaim = 30f;
        public const float MarriageScorePersonalTitleClaimCarrier = 15f;
        public const float MarriageScoreLocalTitleClaim = 20f;
        public const float MarriageScoreRealmTitleClaim = 8f;
        public const float MarriageScoreHigherTitleClaimTierScale = 5f;
        public const float MarriageScoreTitleClaimMax = 85f;
        public const float DynasticHeiressCadetFallbackDowryGoldShare = 0.10f;

        // ------------------------------------------------------------
        // DYNAMIC MERCENARY COMPANIES  (DynamicMercenaryBandBehavior)
        // Spare adventurous nobles may found or join cultural minor factions.
        // The long evaluation interval is sharded across individual clans.
        // ------------------------------------------------------------
        public const int DynamicMercenaryCompanyLimit = 10;
        public const int DynamicMercenaryEvaluationIntervalYears = 5;
        public const int DynamicMercenaryMaximumOfficers = 3;
        public const int DynamicMercenaryStartingTier = 2;
        public const int DynamicMercenaryStartingGold = 15000;
        public const int DynamicMercenaryMaintenanceIntervalDays = 7;
        public const int DynamicMercenaryMinimumSourceAdultLords = 3;
        public const int DynamicMercenaryMinimumAdultsRemainingInSourceClan = 2;
        public const float DynamicMercenaryBaseDepartureChance = 0.05f;
        public const float DynamicMercenaryTraitDepartureChancePerLevel = 0.15f;
        public const float DynamicMercenaryMaximumDepartureChance = 0.65f;
        public const int DynamicMercenaryPlayerSupportGold = 25000;
        public const int DynamicMercenaryPlayerSupportRelativeRelation = 15;
        public const int DynamicMercenaryPlayerSupportCaptainRelation = 10;
        public const int DynamicMercenaryPlayerBlessingRelation = 5;
        public const int DynamicMercenaryPlayerAskedStayRelation = -5;
        public const int DynamicMercenaryPlayerForbidStayRelation = -10;
        public const int DynamicMercenaryPlayerForbidLeaveRelation = -20;
        public const int DynamicMercenaryPlayerForbidCaptainRelation = -10;
        public const float DynamicMercenaryPlayerSupportMemoryYears = 15f;
        public const float DynamicMercenaryPlayerBlessingMemoryYears = 10f;
        public const float DynamicMercenaryPlayerAskedStayMemoryYears = 5f;
        public const float DynamicMercenaryPlayerForbidMemoryYears = 15f;
        public const float DynamicMercenaryRequestBaseChance = 0.25f;
        public const float DynamicMercenaryRequestRelationScale = 0.0025f;
        public const float DynamicMercenaryRequestTraitScale = 0.10f;
        public const float DynamicMercenaryRequestMinimumChance = 0.05f;
        public const float DynamicMercenaryRequestMaximumChance = 0.70f;
        public const float DynamicMercenaryObedienceBaseChance = 0.50f;
        public const float DynamicMercenaryObedienceRelationScale = 0.004f;
        public const float DynamicMercenaryObedienceTraitScale = 0.15f;
        public const float DynamicMercenaryObedienceMinimumChance = 0.10f;
        public const float DynamicMercenaryObedienceMaximumChance = 0.90f;
        public const int DynamicMercenaryPlayerIntentExpiryYears = 1;

        // ------------------------------------------------------------
        // ROYAL DOWRY  (MarriageDowryPatch)
        // Multipliers applied to the target clan's marriage valuation when the
        // proposer is of lower tier. Higher tier gap = steeper "prove yourself" tax.
        // The heir premium stacks on top and applies regardless of tier direction.
        // ------------------------------------------------------------
        // Each tier of difference makes the marriage 100% more expensive (+1x per tier gap).
        // Applied as: multiplier = 1.0 + tierDiff. Only fires when proposer is lower tier (tierDiff > 0).
        // Equal or higher-tier proposers pay no penalty.
        public const float DowryHeirPremium = 1.00f;  // flat +1x additive on top of any tier penalty - dynastic heirs are always expensive regardless of tier direction

        // ------------------------------------------------------------
        // FEUDAL SERVICE OWED
        // Legal obligations from a de facto child title to its immediate de facto liege title.
        // Tax is calculated from actual effective title revenue after title penalties.
        // ------------------------------------------------------------
        public const float FeudalServiceExemptionTaxShare = 0.00f;
        public const float FeudalServiceLessenedTaxShare = 0.10f;
        public const float FeudalServiceCustomaryTenureTaxShare = 0.25f;
        public const float FeudalServiceElevatedTaxShare = 0.40f;
        public const float FeudalServiceExtortionTaxShare = 0.50f;

        public const float FeudalServiceExemptionArmyCost = 0.50f;
        public const float FeudalServiceLessenedArmyCost = 1.00f;
        public const float FeudalServiceCustomaryTenureArmyCost = 1.50f;
        public const float FeudalServiceElevatedArmyCost = 2.00f;
        public const float FeudalServiceExtortionArmyCost = 2.50f;

        public const int FeudalServiceExemptionRelationModifier = 10;
        public const int FeudalServiceLessenedRelationModifier = 5;
        public const int FeudalServiceCustomaryTenureRelationModifier = 0;
        public const int FeudalServiceElevatedRelationModifier = -10;
        public const int FeudalServiceExtortionRelationModifier = -20;
    }
}
