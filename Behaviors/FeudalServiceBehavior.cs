using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.Core;
using C = BellumCivile.BellumCivileConstants;

namespace BellumCivile.Behaviors
{
    public class FeudalServiceBehavior : CampaignBehaviorBase
    {
        public const FeudalServiceLevel DefaultLevel = FeudalServiceLevel.CustomaryTenure;
        private const int ImmediateReviewDelayDays = 3;
        private const int AiReviewMinYears = 5;
        private const int AiReviewMaxYears = 10;
        private const int AiChangeCooldownYears = 5;
        private const int DailyReviewLimit = 3;
        private const float ContractChangeThreshold = 35f;
        private const int ImmediateRelationChangePerRank = 15;
        private const int ImmediateRelationChangeCap = 30;
        public const float RelationMemoryDurationYears = 10f;

        private Dictionary<string, FeudalServiceRecord> _contractsByTitlePair = new Dictionary<string, FeudalServiceRecord>();
        private Dictionary<string, float> _nextReviewDayByTitlePair = new Dictionary<string, float>();
        private List<string> _queuedTitleReviewIds = new List<string>();

        public static FeudalServiceBehavior Instance { get; private set; }
        public int RuntimeRevision { get; private set; }

        public override void RegisterEvents()
        {
            Instance = this;
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
        }

        public override void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData("BellumCivile_FeudalServiceContracts", ref _contractsByTitlePair);
            dataStore.SyncData("BellumCivile_FeudalServiceReviewDays", ref _nextReviewDayByTitlePair);
            dataStore.SyncData("BellumCivile_FeudalServiceQueuedReviews", ref _queuedTitleReviewIds);
            EnsureCollectionsInitialized();
            RuntimeRevision++;
        }

        public FeudalServiceLevel GetServiceLevel(FeudalTitleRecord childTitle, FeudalTitleRecord parentTitle = null)
        {
            if (childTitle == null || string.IsNullOrWhiteSpace(childTitle.TitleId))
                return DefaultLevel;

            string parentId = parentTitle?.TitleId ?? childTitle.DeFactoParentTitleId;
            if (string.IsNullOrWhiteSpace(parentId))
                return DefaultLevel;

            EnsureCollectionsInitialized();
            return _contractsByTitlePair.TryGetValue(BuildContractKey(childTitle.TitleId, parentId), out FeudalServiceRecord record)
                ? Clamp(record.Level)
                : DefaultLevel;
        }

        public void SetServiceLevel(FeudalTitleRecord childTitle, FeudalTitleRecord parentTitle, FeudalServiceLevel level, Clan changedByClan, string reason)
        {
            if (childTitle == null || parentTitle == null || string.IsNullOrWhiteSpace(childTitle.TitleId) || string.IsNullOrWhiteSpace(parentTitle.TitleId))
                return;

            EnsureCollectionsInitialized();
            string key = BuildContractKey(childTitle.TitleId, parentTitle.TitleId);
            FeudalServiceLevel clamped = Clamp(level);
            FeudalServiceLevel previous = _contractsByTitlePair.TryGetValue(key, out FeudalServiceRecord existing)
                ? Clamp(existing.Level)
                : DefaultLevel;
            if (existing != null)
            {
                existing.Update(clamped, CurrentDay, changedByClan?.StringId, reason);
            }
            else
            {
                _contractsByTitlePair[key] = new FeudalServiceRecord(
                    key,
                    childTitle.TitleId,
                    parentTitle.TitleId,
                    clamped,
                    CurrentDay,
                    changedByClan?.StringId,
                    reason);
            }

            RuntimeRevision++;
            ApplyImmediateRelationChange(childTitle, parentTitle, previous, clamped);
            Campaign.Current?.GetCampaignBehavior<DynamicRelationBehavior>()?.InvalidateBaselineCache();
            BellumCivileLogger.Log($"Feudal service changed; child={childTitle.TitleId}; parent={parentTitle.TitleId}; level={clamped}; changed_by={changedByClan?.StringId ?? "null"}; reason={reason ?? "unknown"}.");
        }

        public void QueueTitleRelationshipReview(FeudalTitleRecord title, string reason)
        {
            if (title == null || string.IsNullOrWhiteSpace(title.TitleId))
                return;

            EnsureCollectionsInitialized();
            if (!_queuedTitleReviewIds.Contains(title.TitleId))
                _queuedTitleReviewIds.Add(title.TitleId);

            if (!string.IsNullOrWhiteSpace(title.DeFactoParentTitleId))
            {
                string key = BuildContractKey(title.TitleId, title.DeFactoParentTitleId);
                float dueDay = CurrentDay + ImmediateReviewDelayDays;
                if (!_nextReviewDayByTitlePair.TryGetValue(key, out float existingDueDay) || existingDueDay > dueDay)
                    _nextReviewDayByTitlePair[key] = dueDay;
            }

            FeudalTitleBehavior titleBehavior = FeudalTitleBehavior.Instance ?? Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>();
            FeudalTitleRecord parent = titleBehavior?.GetParentTitle(title, FeudalHierarchyMode.DeFacto);
            Clan liege = ResolveClan(parent?.DeFactoHolderClanId);
            Campaign.Current?.GetCampaignBehavior<FeudalPoliticalOptionsBehavior>()
                ?.QueueClanForEvaluation(liege, $"service review queued: {reason ?? "unknown"}", ImmediateReviewDelayDays);
        }

        public float GetTaxShare(FeudalTitleRecord childTitle, FeudalTitleRecord parentTitle = null)
        {
            return GetTaxShare(GetServiceLevel(childTitle, parentTitle));
        }

        public float GetArmyCostMultiplier(FeudalTitleRecord childTitle, FeudalTitleRecord parentTitle = null)
        {
            return GetArmyCostMultiplier(GetServiceLevel(childTitle, parentTitle));
        }

        public int GetOngoingRelationModifier(FeudalTitleRecord childTitle, FeudalTitleRecord parentTitle = null)
        {
            return GetOngoingRelationModifier(GetServiceLevel(childTitle, parentTitle));
        }

        public int GetOngoingRelationModifierBetween(Clan firstClan, Clan secondClan)
        {
            if (firstClan == null || secondClan == null || firstClan == secondClan)
                return 0;

            FeudalTitleBehavior titleBehavior = FeudalTitleBehavior.Instance ?? Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>();
            if (titleBehavior == null)
                return 0;

            return GetOngoingRelationModifierBetween(
                firstClan,
                secondClan,
                titleBehavior.GetTitlesHeldByClan(firstClan, deJure: false),
                titleBehavior.GetTitlesHeldByClan(secondClan, deJure: false));
        }

        internal int GetOngoingRelationModifierBetween(
            Clan firstClan,
            Clan secondClan,
            IEnumerable<FeudalTitleRecord> firstDeFactoTitles,
            IEnumerable<FeudalTitleRecord> secondDeFactoTitles)
        {
            if (firstClan == null || secondClan == null || firstClan == secondClan)
                return 0;

            FeudalTitleBehavior titleBehavior = FeudalTitleBehavior.Instance ?? Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>();
            if (titleBehavior == null)
                return 0;

            int sum = 0;
            int count = 0;
            AddDirectServiceModifiers(titleBehavior, firstClan, secondClan, firstDeFactoTitles, ref sum, ref count);
            AddDirectServiceModifiers(titleBehavior, secondClan, firstClan, secondDeFactoTitles, ref sum, ref count);
            return count > 0 ? (int)Math.Round((double)sum / count) : 0;
        }

        public static float GetTaxShare(FeudalServiceLevel level)
        {
            switch (Clamp(level))
            {
                case FeudalServiceLevel.Exemption: return C.FeudalServiceExemptionTaxShare;
                case FeudalServiceLevel.Lessened: return C.FeudalServiceLessenedTaxShare;
                case FeudalServiceLevel.Elevated: return C.FeudalServiceElevatedTaxShare;
                case FeudalServiceLevel.Extortion: return C.FeudalServiceExtortionTaxShare;
                default: return C.FeudalServiceCustomaryTenureTaxShare;
            }
        }

        public static float GetArmyCostMultiplier(FeudalServiceLevel level)
        {
            switch (Clamp(level))
            {
                case FeudalServiceLevel.Exemption: return C.FeudalServiceExemptionArmyCost;
                case FeudalServiceLevel.Lessened: return C.FeudalServiceLessenedArmyCost;
                case FeudalServiceLevel.Elevated: return C.FeudalServiceElevatedArmyCost;
                case FeudalServiceLevel.Extortion: return C.FeudalServiceExtortionArmyCost;
                default: return C.FeudalServiceCustomaryTenureArmyCost;
            }
        }

        public static int GetOngoingRelationModifier(FeudalServiceLevel level)
        {
            switch (Clamp(level))
            {
                case FeudalServiceLevel.Exemption: return C.FeudalServiceExemptionRelationModifier;
                case FeudalServiceLevel.Lessened: return C.FeudalServiceLessenedRelationModifier;
                case FeudalServiceLevel.Elevated: return C.FeudalServiceElevatedRelationModifier;
                case FeudalServiceLevel.Extortion: return C.FeudalServiceExtortionRelationModifier;
                default: return C.FeudalServiceCustomaryTenureRelationModifier;
            }
        }

        public static int GetRelationMemoryChange(FeudalServiceLevel previous, FeudalServiceLevel current)
        {
            int rankDelta = (int)Clamp(current) - (int)Clamp(previous);
            return rankDelta == 0
                ? 0
                : -Math.Sign(rankDelta) * Math.Min(ImmediateRelationChangeCap, Math.Abs(rankDelta) * ImmediateRelationChangePerRank);
        }

        private void AddDirectServiceModifiers(
            FeudalTitleBehavior titleBehavior,
            Clan vassal,
            Clan liege,
            IEnumerable<FeudalTitleRecord> deFactoTitles,
            ref int sum,
            ref int count)
        {
            if (deFactoTitles == null)
                return;

            foreach (FeudalTitleRecord title in deFactoTitles)
            {
                FeudalTitleRecord parent = titleBehavior.GetParentTitle(title, FeudalHierarchyMode.DeFacto);
                if (parent == null || parent.DeFactoHolderClanId != liege.StringId)
                    continue;

                sum += GetOngoingRelationModifier(title, parent);
                count++;
            }
        }

        private static FeudalServiceLevel Clamp(FeudalServiceLevel level)
        {
            if (level < FeudalServiceLevel.Exemption)
                return FeudalServiceLevel.Exemption;
            if (level > FeudalServiceLevel.Extortion)
                return FeudalServiceLevel.Extortion;
            return level;
        }

        private void ApplyImmediateRelationChange(
            FeudalTitleRecord childTitle,
            FeudalTitleRecord parentTitle,
            FeudalServiceLevel previous,
            FeudalServiceLevel current)
        {
            int relationChange = GetRelationMemoryChange(previous, current);
            if (relationChange == 0)
                return;

            Clan vassalClan = ResolveClan(childTitle?.DeFactoHolderClanId);
            Clan liegeClan = ResolveClan(parentTitle?.DeFactoHolderClanId);
            Hero vassal = vassalClan?.Leader;
            Hero liege = liegeClan?.Leader;
            if (vassal == null || liege == null || vassal == liege || vassal.IsDead || liege.IsDead)
                return;

            RelationMemoryService.ApplyChange(vassal, liege, relationChange, false,
                RelationMemorySources.FeudalService, RelationMemoryDurationYears, RelationMemoryScope.House, childTitle.Name);
            BellumCivileLogger.Log(
                $"Feudal service relation adjusted; vassal={vassalClan.StringId}; liege={liegeClan.StringId}; previous={previous}; current={current}; relation_change={relationChange}.");
        }

        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            Instance = this;
            EnsureCollectionsInitialized();
            ScheduleMissingReviews();
        }

        public bool TryRunAutonomousServiceReviewForClan(FeudalTitleBehavior titleBehavior, Clan liegeClan, out string report)
        {
            report = null;
            EnsureCollectionsInitialized();

            if (titleBehavior == null)
            {
                report = "title behavior unavailable";
                return false;
            }

            if (!IsValidServiceLiege(liegeClan))
            {
                report = "invalid service liege";
                return false;
            }

            int processed = ProcessQueuedReviews(titleBehavior, DailyReviewLimit, liegeClan, out bool changedFromQueue);
            if (changedFromQueue)
            {
                report = "changed queued service contract";
                return true;
            }

            if (processed < DailyReviewLimit && ProcessDueReviews(titleBehavior, DailyReviewLimit - processed, liegeClan, out bool changedFromDue) > 0 && changedFromDue)
            {
                report = "changed due service contract";
                return true;
            }

            report = processed > 0 ? "reviewed service contracts without change" : "no service contracts due";
            return false;
        }

        private int ProcessQueuedReviews(FeudalTitleBehavior titleBehavior, int limit, Clan liegeFilter, out bool changed)
        {
            changed = false;
            if (limit <= 0 || _queuedTitleReviewIds.Count == 0)
                return 0;

            int processed = 0;
            foreach (string titleId in _queuedTitleReviewIds.ToList())
            {
                if (processed >= limit)
                    break;

                FeudalTitleRecord title = titleBehavior.GetTitle(titleId);
                FeudalTitleRecord parent = titleBehavior.GetParentTitle(title, FeudalHierarchyMode.DeFacto);
                if (title == null || parent == null)
                {
                    _queuedTitleReviewIds.Remove(titleId);
                    continue;
                }

                string key = BuildContractKey(title.TitleId, parent.TitleId);
                if (_nextReviewDayByTitlePair.TryGetValue(key, out float dueDay) && dueDay > CurrentDay)
                    continue;

                if (liegeFilter != null && !IsParentHeldByClan(parent, liegeFilter))
                    continue;

                _queuedTitleReviewIds.Remove(titleId);
                changed |= TryReviewTitleService(titleBehavior, title, parent, true);
                processed++;
            }

            return processed;
        }

        private int ProcessDueReviews(FeudalTitleBehavior titleBehavior, int limit, Clan liegeFilter, out bool changed)
        {
            changed = false;
            if (limit <= 0 || _nextReviewDayByTitlePair.Count == 0)
                return 0;

            int processed = 0;
            List<KeyValuePair<string, float>> dueEntries = _nextReviewDayByTitlePair
                .Where(pair => pair.Value <= CurrentDay)
                .OrderBy(pair => pair.Value)
                .Where(pair =>
                {
                    if (liegeFilter == null)
                        return true;

                    FeudalTitleRecord child = ResolveChildTitleFromContractKey(titleBehavior, pair.Key);
                    FeudalTitleRecord parentTitle = titleBehavior.GetParentTitle(child, FeudalHierarchyMode.DeFacto);
                    return IsParentHeldByClan(parentTitle, liegeFilter);
                })
                .Take(limit)
                .ToList();

            foreach (KeyValuePair<string, float> entry in dueEntries)
            {
                FeudalTitleRecord title = ResolveChildTitleFromContractKey(titleBehavior, entry.Key);
                FeudalTitleRecord parent = titleBehavior.GetParentTitle(title, FeudalHierarchyMode.DeFacto);
                _nextReviewDayByTitlePair.Remove(entry.Key);
                if (title == null || parent == null)
                    continue;

                changed |= TryReviewTitleService(titleBehavior, title, parent, false);
                processed++;
            }

            return processed;
        }

        private bool TryReviewTitleService(FeudalTitleBehavior titleBehavior, FeudalTitleRecord title, FeudalTitleRecord parent, bool eventDriven)
        {
            if (!IsValidAiServiceRelationship(titleBehavior, title, parent, out Clan vassalClan, out Clan liegeClan))
            {
                ScheduleNextReview(title, parent, false);
                return false;
            }

            string key = BuildContractKey(title.TitleId, parent.TitleId);
            if (_contractsByTitlePair.TryGetValue(key, out FeudalServiceRecord record))
            {
                float cooldownEnd = record.LastChangedDay + GetCampaignDaysInYear() * AiChangeCooldownYears;
                if (cooldownEnd > CurrentDay)
                {
                    _nextReviewDayByTitlePair[key] = cooldownEnd + MBRandom.RandomFloatRanged(3f, 14f);
                    return false;
                }
            }

            FeudalServiceLevel current = GetServiceLevel(title, parent);
            float pressure = CalculateContractPressure(title, parent, vassalClan, liegeClan);
            FeudalServiceLevel desired = current;
            if (pressure >= ContractChangeThreshold && current < FeudalServiceLevel.Extortion)
                desired = (FeudalServiceLevel)((int)current + 1);
            else if (pressure <= -ContractChangeThreshold && current > FeudalServiceLevel.Exemption)
                desired = (FeudalServiceLevel)((int)current - 1);

            if (desired == current)
            {
                ScheduleNextReview(title, parent, false);
                return false;
            }

            SetServiceLevel(title, parent, desired, liegeClan, eventDriven ? "ai_contract_review_event" : "ai_contract_review_periodic");
            ScheduleNextReview(title, parent, true);
            BellumCivileLogger.Log(
                $"AI feudal service review changed contract; title={title.TitleId}; parent={parent.TitleId}; liege={liegeClan.StringId}; vassal={vassalClan.StringId}; {current}->{desired}; pressure={pressure:0.0}; event_driven={eventDriven}.");
            return true;
        }

        private bool IsValidAiServiceRelationship(
            FeudalTitleBehavior titleBehavior,
            FeudalTitleRecord title,
            FeudalTitleRecord parent,
            out Clan vassalClan,
            out Clan liegeClan)
        {
            vassalClan = ResolveClan(title?.DeFactoHolderClanId);
            liegeClan = ResolveClan(parent?.DeFactoHolderClanId);
            if (title == null
                || parent == null
                || !title.IsActive
                || !parent.IsActive
                || vassalClan == null
                || liegeClan == null
                || vassalClan == liegeClan
                || vassalClan.IsEliminated
                || liegeClan.IsEliminated
                || liegeClan == Clan.PlayerClan
                || liegeClan.Leader == null
                || liegeClan.Leader.IsDead
                || vassalClan.Leader == null
                || vassalClan.Leader.IsDead)
            {
                return false;
            }

            if (vassalClan.IsUnderMercenaryService || liegeClan.IsUnderMercenaryService)
                return false;

            if ((vassalClan.IsMinorFaction && vassalClan != Clan.PlayerClan) || (liegeClan.IsMinorFaction && liegeClan != Clan.PlayerClan))
                return false;

            return vassalClan.Kingdom != null
                && vassalClan.Kingdom == liegeClan.Kingdom
                && titleBehavior.GetParentTitle(title, FeudalHierarchyMode.DeFacto)?.TitleId == parent.TitleId;
        }

        private static bool IsValidServiceLiege(Clan clan)
        {
            return clan != null
                && clan != Clan.PlayerClan
                && !clan.IsEliminated
                && !clan.IsUnderMercenaryService
                && (!clan.IsMinorFaction || clan == Clan.PlayerClan)
                && clan.Leader != null
                && !clan.Leader.IsDead;
        }

        private static bool IsParentHeldByClan(FeudalTitleRecord parent, Clan clan)
        {
            return parent != null
                && clan != null
                && string.Equals(parent.DeFactoHolderClanId, clan.StringId, StringComparison.Ordinal);
        }

        private float CalculateContractPressure(FeudalTitleRecord title, FeudalTitleRecord parent, Clan vassalClan, Clan liegeClan)
        {
            Hero liege = liegeClan.Leader;
            Hero vassal = vassalClan.Leader;
            float score = MBRandom.RandomFloatRanged(-8f, 8f);

            int relation = liege != null && vassal != null
                ? CharacterRelationManager.GetHeroRelation(liege, vassal)
                : 0;
            if (relation >= 60) score -= 25f;
            else if (relation >= 25) score -= 12f;
            else if (relation <= -60) score += 30f;
            else if (relation <= -25) score += 15f;

            int calculating = liege?.GetTraitLevel(DefaultTraits.Calculating) ?? 0;
            int generosity = liege?.GetTraitLevel(DefaultTraits.Generosity) ?? 0;
            int mercy = liege?.GetTraitLevel(DefaultTraits.Mercy) ?? 0;
            int honor = liege?.GetTraitLevel(DefaultTraits.Honor) ?? 0;
            int valor = liege?.GetTraitLevel(DefaultTraits.Valor) ?? 0;

            score += calculating * 10f;
            score -= generosity * 10f;
            score -= mercy * 8f;
            score -= honor * 6f;
            score -= valor * 12f;

            if (IsKingdomAtWar(liegeClan.Kingdom))
                score -= title.TitleType == FeudalTitleType.Barony ? 20f : 12f;

            if (!string.Equals(title.DeJureHolderClanId, title.DeFactoHolderClanId, StringComparison.Ordinal))
                score += 10f;

            float vassalPower = RebellionPowerHelper.CalculateClanPower(vassalClan);
            float liegePower = RebellionPowerHelper.CalculateClanPower(liegeClan);
            if (vassalPower > 0f && liegePower > 0f)
            {
                if (vassalPower >= liegePower * 0.9f)
                    score -= 15f;
                else if (vassalPower <= liegePower * 0.35f && relation < 0)
                    score += 10f;
            }

            return score;
        }

        private void ScheduleMissingReviews()
        {
            FeudalTitleBehavior titleBehavior = FeudalTitleBehavior.Instance ?? Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>();
            if (titleBehavior == null)
                return;

            foreach (FeudalTitleRecord title in titleBehavior.GetAllTitles())
            {
                FeudalTitleRecord parent = titleBehavior.GetParentTitle(title, FeudalHierarchyMode.DeFacto);
                if (title == null || parent == null)
                    continue;

                string key = BuildContractKey(title.TitleId, parent.TitleId);
                if (_nextReviewDayByTitlePair.ContainsKey(key))
                    continue;

                if (!IsValidAiServiceRelationship(titleBehavior, title, parent, out _, out _))
                    continue;

                ScheduleNextReview(title, parent, false);
            }
        }

        private void ScheduleNextReview(FeudalTitleRecord title, FeudalTitleRecord parent, bool afterChange)
        {
            if (title == null || parent == null)
                return;

            float days;
            if (afterChange)
                days = GetCampaignDaysInYear() * AiChangeCooldownYears + MBRandom.RandomFloatRanged(0f, GetCampaignDaysInYear());
            else
                days = GetCampaignDaysInYear() * MBRandom.RandomFloatRanged(AiReviewMinYears, AiReviewMaxYears);

            _nextReviewDayByTitlePair[BuildContractKey(title.TitleId, parent.TitleId)] = CurrentDay + Math.Max(1f, days);
        }

        private static bool IsKingdomAtWar(Kingdom kingdom)
        {
            return kingdom != null
                && Kingdom.All.Any(other => other != null
                    && other != kingdom
                    && !other.IsEliminated
                    && !other.IsMinorFaction
                    && kingdom.IsAtWarWith(other));
        }

        private static FeudalTitleRecord ResolveChildTitleFromContractKey(FeudalTitleBehavior titleBehavior, string key)
        {
            if (titleBehavior == null || string.IsNullOrWhiteSpace(key))
                return null;

            int separator = key.IndexOf('|');
            string titleId = separator >= 0 ? key.Substring(0, separator) : key;
            return titleBehavior.GetTitle(titleId);
        }

        private static Clan ResolveClan(string clanId)
        {
            return string.IsNullOrWhiteSpace(clanId)
                ? null
                : Clan.All?.FirstOrDefault(clan => clan != null && clan.StringId == clanId);
        }

        private static int GetCampaignDaysInYear()
        {
            return Math.Max(1, CampaignTime.DaysInYear > 0 ? CampaignTime.DaysInYear : C.MarriageStrategyDaysPerYear);
        }

        private static string BuildContractKey(string childTitleId, string parentTitleId)
        {
            return $"{childTitleId ?? string.Empty}|{parentTitleId ?? string.Empty}";
        }

        private static float CurrentDay => (float)CampaignTime.Now.ToDays;

        private void EnsureCollectionsInitialized()
        {
            if (_contractsByTitlePair == null)
                _contractsByTitlePair = new Dictionary<string, FeudalServiceRecord>();
            if (_nextReviewDayByTitlePair == null)
                _nextReviewDayByTitlePair = new Dictionary<string, float>();
            if (_queuedTitleReviewIds == null)
                _queuedTitleReviewIds = new List<string>();
        }
    }
}
