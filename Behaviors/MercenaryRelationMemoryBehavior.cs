using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;

namespace BellumCivile.Behaviors
{
    /// <summary>
    /// Records the political history shared by mercenary houses and their employers without
    /// turning temporary civil-war transfers into lasting favors or grievances.
    /// </summary>
    public sealed class MercenaryRelationMemoryBehavior : CampaignBehaviorBase
    {
        private const float ServiceMemoryYears = 3f;
        private const float LongServiceMemoryYears = 5f;
        private const float SettledContractMemoryYears = 3f;
        private const float LongServiceDays = 100f;

        private Dictionary<string, float> _contractStartDaysByClanId = new Dictionary<string, float>(StringComparer.Ordinal);
        private readonly HashSet<string> _honorablySettledDepartures = new HashSet<string>(StringComparer.Ordinal);

        public static MercenaryRelationMemoryBehavior Instance { get; private set; }

        public MercenaryRelationMemoryBehavior()
        {
            Instance = this;
        }

        public override void RegisterEvents()
        {
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
            CampaignEvents.OnClanChangedKingdomEvent.AddNonSerializedListener(this, OnClanChangedKingdom);
        }

        public override void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData("BellumCivile_MercenaryRelationContractStarts", ref _contractStartDaysByClanId);
            EnsureCollectionsInitialized();
            Instance = this;
        }

        internal void MarkPlayerDebtPaid(Clan mercenaryClan)
        {
            if (mercenaryClan != null && DynamicRelationBehavior.IsRelationshipMercenaryClan(mercenaryClan))
                _honorablySettledDepartures.Add(mercenaryClan.StringId);
        }

        internal static string GetRealmContext(Kingdom kingdom)
        {
            if (kingdom == null)
                return string.Empty;

            string conciseName = FeudalTitleDisplayHelper.ResolveConciseSovereignTitleName(kingdom);
            return string.IsNullOrWhiteSpace(conciseName) ? kingdom.Name?.ToString() ?? kingdom.StringId : conciseName;
        }

        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            EnsureCollectionsInitialized();
            Instance = this;

            foreach (Clan clan in Clan.All)
            {
                if (clan == null
                    || !clan.IsUnderMercenaryService
                    || !DynamicRelationBehavior.IsRelationshipMercenaryClan(clan)
                    || clan.Kingdom == null
                    || BellumKingdomVisibilityHelper.IsTemporaryBellumKingdom(clan.Kingdom)
                    || _contractStartDaysByClanId.ContainsKey(clan.StringId))
                {
                    continue;
                }

                float elapsedDays = Math.Max(0f, clan.LastFactionChangeTime.ElapsedDaysUntilNow);
                _contractStartDaysByClanId[clan.StringId] = CurrentDay - elapsedDays;
            }
        }

        private void OnClanChangedKingdom(
            Clan clan,
            Kingdom oldKingdom,
            Kingdom newKingdom,
            ChangeKingdomAction.ChangeKingdomActionDetail detail,
            bool showNotification)
        {
            if (clan == null || !DynamicRelationBehavior.IsRelationshipMercenaryClan(clan))
                return;

            if (detail == ChangeKingdomAction.ChangeKingdomActionDetail.JoinAsMercenary)
            {
                if (oldKingdom != null && oldKingdom != newKingdom)
                    FinalizeContract(clan, oldKingdom, showNotification);
                StartContract(clan, newKingdom);
                return;
            }

            if (detail == ChangeKingdomAction.ChangeKingdomActionDetail.LeaveAsMercenary)
                FinalizeContract(clan, oldKingdom, showNotification);
        }

        private void StartContract(Clan mercenaryClan, Kingdom employerKingdom)
        {
            _honorablySettledDepartures.Remove(mercenaryClan.StringId);
            if (employerKingdom == null || BellumKingdomVisibilityHelper.IsTemporaryBellumKingdom(employerKingdom))
            {
                _contractStartDaysByClanId.Remove(mercenaryClan.StringId);
                return;
            }

            _contractStartDaysByClanId[mercenaryClan.StringId] = CurrentDay;
            ApplyHouseMemory(
                mercenaryClan,
                employerKingdom,
                5,
                RelationMemorySources.MercenaryService,
                ServiceMemoryYears);
        }

        private void FinalizeContract(Clan mercenaryClan, Kingdom employerKingdom, bool showNotification)
        {
            bool hadStart = _contractStartDaysByClanId.TryGetValue(mercenaryClan.StringId, out float startDay);
            _contractStartDaysByClanId.Remove(mercenaryClan.StringId);
            bool settledHonorably = _honorablySettledDepartures.Remove(mercenaryClan.StringId);

            if (!showNotification
                || employerKingdom == null
                || BellumKingdomVisibilityHelper.IsTemporaryBellumKingdom(employerKingdom))
            {
                return;
            }

            if (hadStart && CurrentDay - startDay >= LongServiceDays)
            {
                ApplyHouseMemory(
                    mercenaryClan,
                    employerKingdom,
                    3,
                    RelationMemorySources.LongMercenaryService,
                    LongServiceMemoryYears);
            }

            if (settledHonorably)
            {
                ApplyHouseMemory(
                    mercenaryClan,
                    employerKingdom,
                    3,
                    RelationMemorySources.MercenaryContractSettled,
                    SettledContractMemoryYears);
            }
        }

        private static void ApplyHouseMemory(
            Clan mercenaryClan,
            Kingdom employerKingdom,
            int relationChange,
            string sourceId,
            float durationYears)
        {
            Hero mercenaryLeader = mercenaryClan?.Leader;
            Hero employer = employerKingdom?.RulingClan?.Leader;
            if (mercenaryLeader == null || employer == null || mercenaryLeader == employer)
                return;

            RelationMemoryService.ApplyChange(
                mercenaryLeader,
                employer,
                relationChange,
                false,
                sourceId,
                durationYears,
                RelationMemoryScope.House,
                GetRealmContext(employerKingdom));
        }

        private void EnsureCollectionsInitialized()
        {
            if (_contractStartDaysByClanId == null)
                _contractStartDaysByClanId = new Dictionary<string, float>(StringComparer.Ordinal);
        }

        private static float CurrentDay => (float)CampaignTime.Now.ToDays;
    }
}
