using System;
using C = BellumCivile.BellumCivileConstants;

namespace BellumCivile
{
    internal static class BellumCivileOptions
    {
        private static BellumCivileSettings _settings;
        private static BellumCivileSettings Settings => _settings ?? (_settings = BellumCivileSettings.Instance);

        public static bool ShowDebugMessagesInGame => Settings?.ShowDebugMessagesInGame ?? false;
        public static RealmNameDisplayMode RealmNameDisplay
        {
            get
            {
                int index = Settings?.RealmNameDisplay?.SelectedIndex ?? (int)RealmNameDisplayMode.SovereignTitle;
                return index >= 0 && index <= (int)RealmNameDisplayMode.SovereignTitle
                    ? (RealmNameDisplayMode)index : RealmNameDisplayMode.SovereignTitle;
            }
        }
        public static bool UseSovereignTitlesAsRealmNames => RealmNameDisplay == RealmNameDisplayMode.SovereignTitle;

        public static float RebelliousIntentThreshold => Settings?.RebellionThreshold ?? C.RebelliousIntentThreshold;
        public static float DiscontentTrigger => C.DiscontentTrigger;
        public static float DiscontentGainBase => C.DiscontentGainBase;
        public static float DiscontentDecayPerDay => C.DiscontentDecayPerDay;
        public static int RebelFactionLeaderRivalryThreshold => C.RebelFactionLeaderRivalryThreshold;

        public static int CourtAgendaInfluenceCost => C.CourtAgendaInfluenceCost;
        public static int PoliticalDeliberationDays => Settings?.PoliticalDeliberationDays ?? C.PolicyDeliberationDays;
        public static float CourtTermDays => TaleWorlds.CampaignSystem.CampaignTime.DaysInYear * Math.Max(0.25f, Math.Min(4f, Settings?.CourtTermYears ?? 1f));
        public static bool EnableNpcSubterfugeMissions =>
            Settings?.EnableNpcSubterfugeMissions ?? true;

        public static bool EnableWarPeaceLogicRevamp => Settings?.EnableWarPeaceLogicRevamp ?? true;
        public static bool EnableWarScoreMapWidget => Settings?.EnableWarScoreMapWidget ?? true;
        public static bool EnableClientStateMapWidget => Settings?.EnableClientStateMapWidget ?? true;
        public static int WarDurationReluctanceDays => Math.Max(1, Math.Min(500,
            Settings?.WarDurationReluctanceDays ?? C.WarPeaceDurationReluctanceDays));
        public static int MinimumPeaceBeforeRenewedWarDays =>
            Math.Max(0, Math.Min(500, Settings?.MinimumPeaceBeforeRenewedWarDays ?? C.WarPeaceRevampRecentPeaceBlockDays));
        public static int TreatyReparationsGoldPerWarScore =>
            Math.Max(0, Math.Min(100000, Settings?.TreatyReparationsGoldPerWarScore ?? C.TreatyReparationsGoldPerWarScore));
        public static int TreatyDailyTributePerWarScore =>
            Math.Max(0, Math.Min(100000, Settings?.TreatyDailyTributePerWarScore ?? C.TreatyDailyTributePerWarScore));
        public static float WarWillInitial => C.WarPeaceRevampInitialWarWill;
        public static int WarWillClanEvaluationIntervalDays => C.WarPeaceRevampClanEvaluationIntervalDays;
        public static int WarWillPressureMemoryDays => C.WarPeaceRevampPressureMemoryDays;
        public static float WarWillDeclareThreshold => C.WarPeaceRevampWarProposalThreshold;
        public static float WarWillPeaceThreshold => C.WarPeaceRevampPeaceProposalThreshold;
        public static float WarWillMutualWhitePeaceThreshold => C.WarPeaceRevampMutualWhitePeaceThreshold;
        public static float WarWillAttackerWarDeclaredShock => C.WarPeaceRevampAttackerWarDeclaredShock;
        public static float WarWillDefenderWarDeclaredShock => C.WarPeaceRevampDefenderWarDeclaredShock;
        public static float WarWillExtraWarDailyDrain => C.WarPeaceRevampExtraWarDailyDrain;
        public static float WarWillMultipleFrontShockPerLoad => C.WarPeaceRevampMultipleFrontShockPerLoad;
        public static float WarTargetMinimumScore => C.WarPeaceRevampMinimumTargetScore;
        public static float WarTargetLocalLandBorderScore => C.WarPeaceRevampLocalLandBorderTargetScore;
        public static float WarTargetLocalMaritimeBorderScore => C.WarPeaceRevampLocalMaritimeBorderTargetScore;
        public static float WarTargetRealmLandFrontierScore => C.WarPeaceRevampRealmLandFrontierTargetScore;
        public static float WarTargetRealmMaritimeRouteScore => C.WarPeaceRevampRealmMaritimeRouteTargetScore;
        public static float WarTargetStrongClaimScore => C.WarPeaceRevampStrongClaimTargetScore;
        public static float WarTargetWeakClaimScore => C.WarPeaceRevampWeakClaimTargetScore;
        public static float WarTargetImpliedDeJureScore => C.WarPeaceRevampImpliedDeJureTargetScore;
        public static float WarTargetFormalAlliancePenalty => C.WarPeaceRevampFormalAllianceTargetPenalty;
        public static float WarTargetTradeAgreementPenalty => C.WarPeaceRevampTradeAgreementTargetPenalty;
        public static float WarTargetMarriageAlliancePenalty => C.WarPeaceRevampCrossRealmMarriageAlliancePenalty;
        public static float WarTargetRulingMarriageAlliancePenalty => C.WarPeaceRevampCrossRealmRulingMarriageAlliancePenalty;
        public static float WarWillBattleShockMultiplier => C.WarPeaceRevampBattleShockMultiplier;
        public static float WarWillRaidShockMultiplier => C.WarPeaceRevampRaidShockMultiplier;
        public static float WarWillSettlementShockMultiplier => C.WarPeaceRevampSettlementShockMultiplier;
        public static float WarWillCaptivityShockMultiplier => C.WarPeaceRevampCaptivityShockMultiplier;
        public static float WarWillCasualtyShockMultiplier => C.WarPeaceRevampCasualtyShockMultiplier;
        public static float RetainedPrisonerDungeonEscapeChance =>
            Clamp(Settings?.RetainedPrisonerDungeonEscapeChancePercent ?? C.RetainedPrisonerDungeonEscapeChancePercent, 0f, 100f) / 100f;
        public static float RetainedPrisonerMobileEscapeChance =>
            Clamp(Settings?.RetainedPrisonerMobileEscapeChancePercent ?? C.RetainedPrisonerMobileEscapeChancePercent, 0f, 100f) / 100f;

        public static int RebellionSuppressionMinimumCost => C.CourtRebellionSuppressInfluenceMin;
        public static int RebellionSuppressionMaximumCost => C.CourtRebellionSuppressInfluenceMax;
        public static float GrandCoalitionJoinMoodThreshold => C.GrandCoalitionJoinMoodThreshold;

        public static float PoliticalBribeCostMultiplier => Settings?.PoliticalBribeCostMultiplier ?? 1f;

        public static float PostWarExecutionBase => Settings?.PostWarExecutionBase ?? C.PostWarExecutionBase;
        public static float PostWarConfiscationBase => Settings?.PostWarConfiscationBase ?? C.PostWarConfiscationBase;
        public static bool EnableAutomaticTreasonIndictments => Settings?.EnableAutomaticTreasonIndictments ?? true;

        public static bool EnablePartitionSuccession => Settings?.EnablePartitionSuccession ?? true;
        public static bool EnforcePlayerSuccessionLaw => Settings?.EnforcePlayerSuccessionLaw ?? true;
        public static bool EnableCustomAdulthoodAge => Settings?.EnableCustomAdulthoodAge ?? true;
        public static int AdulthoodAge => EnableCustomAdulthoodAge
            ? Math.Max(16, Math.Min(21, Settings?.AdulthoodAge ?? 16))
            : 18;
        public static bool EnableNpcPartitionSuccession => EnablePartitionSuccession;
        public static int EducationMilestoneAge(int stage)
        {
            if (stage < 0 || stage >= 6) return -1;
            if (!EnableCustomAdulthoodAge) return new[] { 2, 5, 8, 11, 14, 16 }[stage];
            var settings = Settings;
            return settings != null ? settings.GetEducationAge(stage) : new[] { 2, 5, 8, 10, 13, 15 }[stage];
        }
        public static int PartitionSuccessionMainHeirReservedFiefs =>
            Math.Max(1, Settings?.PartitionSuccessionMainHeirReservedFiefs ?? C.PartitionSuccessionMainHeirReservedFiefs);
        public static int FeudalTitleMinimumChildren =>
            Math.Max(2, Settings?.FeudalTitleMinimumChildren ?? C.FeudalTitleMinimumChildren);
        public static float FeudalClaimFabricationBaseYears =>
            Math.Max(1f, Math.Min(10f, Settings?.FeudalClaimFabricationBaseYears ?? C.FeudalClaimFabricationBaseYears));
        public static float FeudalClaimFabricationBaseDiscoveryChance =>
            Math.Max(0f, Math.Min(1f, Settings?.FeudalClaimFabricationBaseDiscoveryChance ?? C.FeudalClaimFabricationBaseDiscoveryChance));

        public static bool EnableRebelliousArmyRefusal => Settings?.EnableRebelliousArmyRefusal ?? true;
        public static float ArmyRefusalMoodThreshold => Settings?.ArmyRefusalMoodThreshold ?? C.ArmyMoodFurious;
        public static bool EnableFeudalArmySummons => Settings?.EnableFeudalArmySummons ?? true;
        public static int ArmyPersonalFriendRelationThreshold =>
            Settings?.ArmyPersonalFriendRelationThreshold ?? C.ArmyPersonalFriendRelationThreshold;

        public static bool EnableBellumStrategicMarriageLogic => Settings?.EnableBellumStrategicMarriageLogic ?? true;
        public static bool UseBellumStrategicNpcMarriagesOnly => Settings?.UseBellumStrategicNpcMarriagesOnly ?? false;
        public static int MarriageMaleMinimumAge => Math.Max(AdulthoodAge, Settings?.MarriageMaleMinimumAge ?? C.MarriageMaleMinimumAge);
        public static int MarriageFemaleMinimumAge => Math.Max(AdulthoodAge, Settings?.MarriageFemaleMinimumAge ?? C.MarriageFemaleMinimumAge);
        public static int MarriageFemaleMaximumAge => Math.Max(MarriageFemaleMinimumAge, Settings?.MarriageFemaleMaximumAge ?? C.MarriageFemaleMaximumAge);
        public static int DynamicMercenaryCompanyLimit =>
            Math.Max(0, Math.Min(100, Settings?.DynamicMercenaryCompanyLimit ?? C.DynamicMercenaryCompanyLimit));
        public static bool EnableDynamicMercenaryBands => DynamicMercenaryCompanyLimit > 0;
        public static int DynamicMercenaryEvaluationIntervalYears =>
            Math.Max(1, Settings?.DynamicMercenaryEvaluationIntervalYears ?? C.DynamicMercenaryEvaluationIntervalYears);
        public static int DynamicMercenaryMaximumOfficers =>
            Math.Max(1, Settings?.DynamicMercenaryMaximumOfficers ?? C.DynamicMercenaryMaximumOfficers);
        public static int DynamicMercenaryStartingTier =>
            Math.Max(1, Math.Min(4, Settings?.DynamicMercenaryStartingTier ?? C.DynamicMercenaryStartingTier));
        public static bool EnableDynamicRelationDrift => Settings?.EnableDynamicRelationDrift ?? true;
        public static float RelationMemoryDurationMultiplier => RelationMemoryRecord.NormalizeDurationMultiplier(Settings?.RelationMemoryDurationMultiplier ?? 1f);
        public static float DynamicRelationWeeklyDrift => Math.Max(0f, Settings?.DynamicRelationWeeklyDrift ?? C.DynamicRelationWeeklyDrift);
        public static BellumNotificationScope BellumCivileNotifications
        {
            get
            {
                int selectedIndex = Settings?.BellumCivileNotifications?.SelectedIndex ?? (int)BellumNotificationScope.KingdomOnly;
                if (selectedIndex < (int)BellumNotificationScope.Disabled)
                    return BellumNotificationScope.Disabled;
                if (selectedIndex > (int)BellumNotificationScope.Global)
                    return BellumNotificationScope.Global;

                return (BellumNotificationScope)selectedIndex;
            }
        }

        public static int ApplyBribeCostMultiplier(int baseCost)
        {
            float multiplier = Math.Max(0f, PoliticalBribeCostMultiplier);
            return Math.Max(0, (int)Math.Round(baseCost * multiplier));
        }

        private static float Clamp(float value, float min, float max)
        {
            if (value < min)
                return min;
            if (value > max)
                return max;
            return value;
        }

        private static float NormalizeDailyDurationSetting(float value)
        {
            value = Math.Max(0f, value);
            return value > 10f ? value / 50f : value;
        }
    }
}
