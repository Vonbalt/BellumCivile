using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using C = BellumCivile.BellumCivileConstants;

namespace BellumCivile.Behaviors
{
    /// <summary>
    /// Discovers and persists de jure drift candidates, advances their progress, and
    /// reparents individually completed titles into their controlling realm.
    /// </summary>
    public class FeudalDeJureDriftBehavior : CampaignBehaviorBase
    {
        private const int MaxChecksPerWeeklyTick = 12;
        private const int InitialChecksOnSessionLaunch = 24;

        private List<FeudalDeJureDriftRecord> _drifts = new List<FeudalDeJureDriftRecord>();
        private readonly Queue<string> _pendingTitleIds = new Queue<string>();
        private readonly HashSet<string> _pendingTitleIdSet = new HashSet<string>();
        private readonly Dictionary<string, FeudalDeJureDriftRecord> _driftByTitleId = new Dictionary<string, FeudalDeJureDriftRecord>();
        private Dictionary<string, Clan> _clansById = new Dictionary<string, Clan>();
        private Dictionary<string, Kingdom> _kingdomsById = new Dictionary<string, Kingdom>();
        private int _lastObservedTitleRevision = -1;
        private bool _independentTimingInitialized;

        public override void RegisterEvents()
        {
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
            CampaignEvents.WeeklyTickEvent.AddNonSerializedListener(this, OnWeeklyTick);
            CampaignEvents.OnSettlementOwnerChangedEvent.AddNonSerializedListener(this, OnSettlementOwnerChanged);
            CampaignEvents.OnClanChangedKingdomEvent.AddNonSerializedListener(this, OnClanChangedKingdom);
        }

        public override void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData("BellumCivile_FeudalDeJureDrifts", ref _drifts);
            dataStore.SyncData("BellumCivile_IndependentDriftTiming", ref _independentTimingInitialized);
            EnsureCollectionsInitialized();
            RebuildRuntimeState();
        }

        public IReadOnlyCollection<FeudalDeJureDriftRecord> GetActiveDrifts()
        {
            EnsureCollectionsInitialized();
            return _drifts.Where(record => record != null && record.IsActive).ToList();
        }

        public FeudalDeJureDriftRecord GetDriftForTitle(string titleId)
        {
            EnsureCollectionsInitialized();
            return !string.IsNullOrWhiteSpace(titleId)
                && _driftByTitleId.TryGetValue(titleId, out FeudalDeJureDriftRecord record)
                && record.IsActive
                    ? record
                    : null;
        }

        public bool TryEvaluateEligibility(FeudalTitleRecord title, out FeudalDeJureDriftEligibility eligibility, out string reason)
        {
            eligibility = null;
            reason = null;
            FeudalTitleBehavior titleBehavior = Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>();
            if (titleBehavior == null)
            {
                reason = "title registry unavailable";
                return false;
            }

            RefreshFactionIndexes();
            return TryEvaluateEligibility(titleBehavior, title, out eligibility, out reason);
        }

        public FeudalDeJureDriftAssessment GetDisplayAssessment(FeudalTitleRecord title)
        {
            FeudalDeJureDriftAssessment hidden = new FeudalDeJureDriftAssessment();
            FeudalTitleBehavior titleBehavior = Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>();
            if (titleBehavior == null || title == null)
                return hidden;

            EnsureCollectionsInitialized();
            FeudalDeJureDriftRecord directRecord = GetDriftForTitle(title.TitleId);
            EnsureDisplayFactionIndexes(title, directRecord);
            Kingdom currentControlKingdom = ResolveClan(title.DeFactoHolderClanId)?.Kingdom;

            bool eligible = TryEvaluateEligibility(
                titleBehavior,
                title,
                out FeudalDeJureDriftEligibility eligibility,
                out string reason,
                out FeudalDeJureDriftBlockReason blockReason,
                out Kingdom prospectiveTarget,
                out int controlledTitles,
                out int requiredTitles);
            bool politicallyOutside = IsPoliticallyOutsideLegalRealm(
                titleBehavior,
                title,
                currentControlKingdom);
            if (directRecord == null && !eligible && !politicallyOutside)
                return hidden;

            return BuildDisplayAssessment(
                title,
                directRecord,
                eligibility,
                eligible,
                reason,
                blockReason,
                prospectiveTarget,
                controlledTitles,
                requiredTitles,
                currentControlKingdom,
                packageRoot: null);
        }

        public void RefreshTitleFamily(FeudalTitleRecord title)
        {
            FeudalTitleBehavior titleBehavior = Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>();
            if (titleBehavior == null || title == null)
                return;

            EnsureCollectionsInitialized();
            RefreshFactionIndexes();
            HashSet<string> titleIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (FeudalTitleRecord descendant in titleBehavior.GetTitleAndDescendants(title))
            {
                if (descendant != null && descendant.IsActive)
                    titleIds.Add(descendant.TitleId);
            }

            FeudalTitleRecord current = titleBehavior.GetTitle(title.ParentTitleId);
            while (current != null && current.IsActive && titleIds.Add(current.TitleId))
                current = titleBehavior.GetTitle(current.ParentTitleId);

            foreach (FeudalTitleRecord affectedTitle in titleIds
                .Select(titleBehavior.GetTitle)
                .Where(candidate => candidate != null && candidate.IsActive)
                .OrderByDescending(candidate => candidate.TitleType)
                .ThenBy(candidate => candidate.TitleId))
            {
                EvaluateAndMaintainRecord(titleBehavior, affectedTitle);
            }

            RemoveInactiveRecords();
            _lastObservedTitleRevision = titleBehavior.RuntimeRevision;
        }

        public void ReconcileReorganization(ISet<string> changedIds)
        {
            var titles = Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>();
            if (titles == null) return;
            RefreshFactionIndexes();
            var affected = new HashSet<string>(changedIds);
            foreach (var record in _drifts)
                if (changedIds.Contains(record.OriginalParentTitleId) || changedIds.Contains(record.TargetParentTitleId))
                    affected.Add(record.TitleId);
            foreach (var id in affected)
            {
                var title = titles.GetTitle(id);
                if (title != null) RefreshTitleFamily(title);
            }
        }

        public string BuildDebugReport(FeudalTitleRecord title, bool refreshRecord)
        {
            if (title == null)
                return "Error: title is null.";

            FeudalTitleBehavior titleBehavior = Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>();
            if (titleBehavior == null)
                return "Error: title registry unavailable.";

            RefreshFactionIndexes();
            if (refreshRecord)
                EvaluateAndMaintainRecord(titleBehavior, title);

            FeudalDeJureDriftRecord record = GetDriftForTitle(title.TitleId);
            if (!TryEvaluateEligibility(titleBehavior, title, out FeudalDeJureDriftEligibility result, out string reason))
            {
                return $"title={title.TitleId}; eligible=false; reason={reason}; "
                    + (record == null
                        ? "record=none"
                        : $"record_state={record.State}; progress={record.Progress:P1}; target_parent={record.TargetParentTitleId}; target_kingdom={record.TargetKingdomId}");
            }

            return $"title={title.TitleId}; eligible=true; original_parent={result.OriginalParent.TitleId}; "
                + $"target_parent={result.TargetParent.TitleId}; target_kingdom={result.TargetKingdom.StringId}; "
                + $"integrator={result.Integrator?.StringId ?? "none"}; state={record?.State.ToString() ?? "untracked"}; progress={(record?.Progress ?? 0f):P1}";
        }

        public string DebugAdvanceDrift(FeudalTitleRecord title, int campaignYears)
        {
            if (title == null || campaignYears <= 0)
                return "Error: title is missing or campaign years must be positive.";

            FeudalTitleBehavior titleBehavior = Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>();
            if (titleBehavior == null)
                return "Error: title registry unavailable.";

            RefreshFactionIndexes();
            EvaluateAndMaintainRecord(titleBehavior, title);
            FeudalDeJureDriftRecord record = GetDriftForTitle(title.TitleId);
            string reason = null;
            FeudalDeJureDriftEligibility eligibility = null;
            if (record == null || !TryEvaluateEligibility(titleBehavior, title, out eligibility, out reason))
                return $"Error: title is not eligible for advancing de jure drift: {reason ?? "no active record"}.";

            int daysPerYear = Math.Max(1, CampaignTime.DaysInYear > 0 ? CampaignTime.DaysInYear : C.MarriageStrategyDaysPerYear);
            record.SetState(FeudalDeJureDriftState.Advancing);
            record.AddProgress(CalculateProgressDelta(campaignYears * daysPerYear, eligibility.Integrator));
            record.MarkEvaluated(CurrentDay);
            float resultingProgress = record.Progress;
            if (record.Progress >= 1f)
                CompleteDrift(titleBehavior, record);
            RemoveInactiveRecords();

            return $"Advanced {title.Name} by {campaignYears} campaign years; speed={CalculateSpeedMultiplier(eligibility.Integrator):0.00}x; progress={resultingProgress:P1}; completed={resultingProgress >= 1f}.";
        }

        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            EnsureCollectionsInitialized();
            RebuildRuntimeState();
            FeudalTitleBehavior titleBehavior = Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>();
            if (titleBehavior == null)
                return;

            RemoveInvalidRecords(titleBehavior);
            InitializeIndependentTiming(titleBehavior);
            EnqueueAllTitles(titleBehavior);
            ProcessPendingTitles(titleBehavior, InitialChecksOnSessionLaunch);
            _lastObservedTitleRevision = titleBehavior.RuntimeRevision;
            BellumCivileLogger.Log($"De jure drift registry initialized; active={GetActiveDrifts().Count}; pending={_pendingTitleIds.Count}.");
        }

        private void OnWeeklyTick()
        {
            FeudalTitleBehavior titleBehavior = Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>();
            if (titleBehavior == null)
                return;

            EnsureCollectionsInitialized();
            RefreshFactionIndexes();
            AdvanceActiveRecords(titleBehavior);
            if (_lastObservedTitleRevision != titleBehavior.RuntimeRevision || _pendingTitleIds.Count == 0)
            {
                EnqueueAllTitles(titleBehavior);
                _lastObservedTitleRevision = titleBehavior.RuntimeRevision;
            }

            ProcessPendingTitles(titleBehavior, MaxChecksPerWeeklyTick);
        }

        private void InitializeIndependentTiming(FeudalTitleBehavior titles)
        {
            if (_independentTimingInitialized) return;
            // Older saves displayed the ancestor's clock on its children. Preserve that known
            // progress once, without replacing any surviving individual record.
            foreach (var parentRecord in _drifts.Where(r => r.IsActive && r.Progress > 0).ToList())
            {
                var root = titles.GetTitle(parentRecord.TitleId);
                if (root == null) continue;
                foreach (var child in titles.GetTitleAndDescendants(root))
                {
                    if (child == root || _driftByTitleId.ContainsKey(child.TitleId)
                        || !TryEvaluateEligibility(titles, child, out var eligible, out _)
                        || eligible.TargetKingdom.StringId != parentRecord.TargetKingdomId) continue;
                    var record = new FeudalDeJureDriftRecord(child.TitleId, eligible.OriginalParent.TitleId,
                        eligible.TargetParent.TitleId, parentRecord.TargetKingdomId, parentRecord.Progress,
                        parentRecord.StartedDay, parentRecord.LastEvaluatedDay, parentRecord.State);
                    _drifts.Add(record);
                    _driftByTitleId[child.TitleId] = record;
                }
            }
            _independentTimingInitialized = true;
        }

        private void AdvanceActiveRecords(FeudalTitleBehavior titleBehavior)
        {
            List<string> activeTitleIds = _drifts
                .Where(record => record != null && record.IsActive && !string.IsNullOrWhiteSpace(record.TitleId))
                .Select(record => record.TitleId)
                .Distinct()
                .ToList();
            foreach (string titleId in activeTitleIds)
            {
                FeudalTitleRecord title = titleBehavior.GetTitle(titleId);
                if (title != null && title.IsActive)
                {
                    EvaluateAndMaintainRecord(titleBehavior, title);
                }
                else if (_driftByTitleId.TryGetValue(titleId, out FeudalDeJureDriftRecord invalidRecord))
                {
                    invalidRecord.SetActive(false);
                }
            }

            RemoveInactiveRecords();
        }

        private void OnSettlementOwnerChanged(Settlement settlement, bool openToClaim, Hero newOwner, Hero oldOwner, Hero capturerHero, ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail detail)
        {
            FeudalTitleBehavior titleBehavior = Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>();
            if (titleBehavior != null && titleBehavior.TryGetBarony(settlement, out FeudalTitleRecord barony))
                RefreshTitleFamily(barony);
        }

        private void OnClanChangedKingdom(
            Clan clan,
            Kingdom oldKingdom,
            Kingdom newKingdom,
            ChangeKingdomAction.ChangeKingdomActionDetail detail,
            bool showNotification)
        {
            if (clan == null)
                return;

            FeudalTitleBehavior titleBehavior = Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>();
            if (titleBehavior == null)
                return;

            List<FeudalTitleRecord> affectedTitles = titleBehavior.GetTitlesHeldByClan(clan, deJure: true)
                .Concat(titleBehavior.GetTitlesHeldByClan(clan, deJure: false))
                .Where(title => title != null && title.IsActive)
                .GroupBy(title => title.TitleId)
                .Select(group => group.First())
                .ToList();
            foreach (FeudalTitleRecord title in affectedTitles)
            {
                RefreshTitleFamily(title);
            }
        }

        private void ProcessPendingTitles(FeudalTitleBehavior titleBehavior, int maxChecks)
        {
            int checks = Math.Min(maxChecks, _pendingTitleIds.Count);
            for (int index = 0; index < checks; index++)
            {
                string titleId = _pendingTitleIds.Dequeue();
                _pendingTitleIdSet.Remove(titleId);
                FeudalTitleRecord title = titleBehavior.GetTitle(titleId);
                if (title != null)
                    EvaluateAndMaintainRecord(titleBehavior, title);
            }

            RemoveInactiveRecords();
        }

        private void EvaluateAndMaintainRecord(FeudalTitleBehavior titleBehavior, FeudalTitleRecord title)
        {
            _driftByTitleId.TryGetValue(title.TitleId, out FeudalDeJureDriftRecord existing);
            float currentDay = CurrentDay;
            if (title.IsDeliberatelyDissolved)
            {
                if (existing != null)
                {
                    existing.SetState(FeudalDeJureDriftState.Paused);
                    existing.MarkEvaluated(currentDay);
                }
                return;
            }
            float elapsedDays = existing == null ? 0f : Math.Max(0f, currentDay - existing.LastEvaluatedDay);
            if (!TryEvaluateEligibility(titleBehavior, title, out FeudalDeJureDriftEligibility eligibility, out string reason))
            {
                if (existing == null)
                    return;

                bool integratedWithTarget = string.Equals(
                    ResolveRecordedLegalKingdomId(titleBehavior, title),
                    existing.TargetKingdomId,
                    StringComparison.Ordinal);
                if (integratedWithTarget)
                {
                    existing.MarkEvaluated(currentDay);
                    existing.SetActive(false);
                    BellumCivileDebug.TraceIfEnabled("titles",
                        $"De jure drift record already integrated; title={title.TitleId}; reason={reason}; target_kingdom={existing.TargetKingdomId}.",
                        requestInGameDisplay: false);
                    return;
                }

                if (existing.Progress > 0f && ShouldReverseDrift(title, existing))
                {
                    existing.SetState(FeudalDeJureDriftState.Reversing);
                    ReverseProgress(existing, elapsedDays);
                }
                else
                {
                    existing.SetState(FeudalDeJureDriftState.Paused);
                }
                existing.MarkEvaluated(currentDay);
                if (existing.Progress <= 0f)
                    existing.SetActive(false);

                BellumCivileDebug.TraceIfEnabled("titles",
                    $"De jure drift eligibility lost; title={title.TitleId}; reason={reason}; state={existing.State}; progress={existing.Progress:P1}.",
                    requestInGameDisplay: false);
                return;
            }

            if (existing == null)
            {
                existing = new FeudalDeJureDriftRecord(
                    title.TitleId,
                    eligibility.OriginalParent.TitleId,
                    eligibility.TargetParent.TitleId,
                    eligibility.TargetKingdom.StringId,
                    0f,
                    currentDay,
                    currentDay);
                _drifts.Add(existing);
                _driftByTitleId[title.TitleId] = existing;
                BellumCivileDebug.TraceIfEnabled("titles",
                    $"De jure drift candidate discovered; title={title.TitleId}; original_parent={eligibility.OriginalParent.TitleId}; target_parent={eligibility.TargetParent.TitleId}; target_kingdom={eligibility.TargetKingdom.StringId}; integrator={eligibility.Integrator?.StringId ?? "none"}.",
                    requestInGameDisplay: false);
                return;
            }

            bool targetChanged = existing.TargetKingdomId != eligibility.TargetKingdom.StringId;
            // A different receiving title within the same realm is not a new integration.
            if (!targetChanged)
                existing.UpdateParents(eligibility.OriginalParent.TitleId, eligibility.TargetParent.TitleId);
            if (targetChanged && existing.Progress > 0f)
            {
                existing.SetState(FeudalDeJureDriftState.Reversing);
                ReverseProgress(existing, elapsedDays);
                existing.MarkEvaluated(currentDay);
                if (existing.Progress <= 0f)
                {
                    existing.Retarget(
                        eligibility.OriginalParent.TitleId,
                        eligibility.TargetParent.TitleId,
                        eligibility.TargetKingdom.StringId,
                        currentDay);
                }
                return;
            }

            if (targetChanged)
            {
                existing.Retarget(
                    eligibility.OriginalParent.TitleId,
                    eligibility.TargetParent.TitleId,
                    eligibility.TargetKingdom.StringId,
                    currentDay);
            }
            else
            {
                existing.SetState(FeudalDeJureDriftState.Advancing);
                existing.AddProgress(CalculateProgressDelta(elapsedDays, eligibility.Integrator));
                existing.MarkEvaluated(currentDay);
                if (existing.Progress >= 1f)
                    CompleteDrift(titleBehavior, existing);
            }
        }

        private void CompleteDrift(FeudalTitleBehavior titleBehavior, FeudalDeJureDriftRecord record)
        {
            if (titleBehavior.TryCompleteDeJureDrift(record, out FeudalTitleRecord title, out FeudalTitleRecord oldParent, out FeudalTitleRecord newParent, out string failureReason))
            {
                record.SetActive(false);
                NotificationHelper.ShowFeudalDeJureDriftCompleted(
                    title,
                    oldParent,
                    newParent,
                    ResolveTitleKingdom(oldParent),
                    ResolveKingdom(record.TargetKingdomId));
                BellumCivileDebug.TraceIfEnabled("titles",
                    $"De jure drift finalized; title={title.TitleId}; old_parent={oldParent.TitleId}; new_parent={newParent.TitleId}; target_kingdom={record.TargetKingdomId}.",
                    requestInGameDisplay: false);
            }
            else
            {
                record.SetState(FeudalDeJureDriftState.Paused);
                BellumCivileLogger.Log($"De jure drift completion deferred; title={record.TitleId}; reason={failureReason}.");
            }
        }

        private FeudalDeJureDriftAssessment BuildDisplayAssessment(
            FeudalTitleRecord evaluatedTitle,
            FeudalDeJureDriftRecord record,
            FeudalDeJureDriftEligibility eligibility,
            bool eligible,
            string technicalReason,
            FeudalDeJureDriftBlockReason blockReason,
            Kingdom prospectiveTarget,
            int controlledTitles,
            int requiredTitles,
            Kingdom currentControlKingdom,
            FeudalTitleRecord packageRoot)
        {
            Kingdom recordedTarget = record == null ? null : ResolveKingdom(record.TargetKingdomId);
            FeudalDeJureDriftAssessment assessment = new FeudalDeJureDriftAssessment
            {
                ShouldDisplay = true,
                TargetKingdom = recordedTarget ?? eligibility?.TargetKingdom ?? prospectiveTarget,
                CurrentControlKingdom = currentControlKingdom,
                Integrator = eligibility?.Integrator
                    ?? ResolveClan(evaluatedTitle?.DeJureHolderClanId)?.Leader
                    ?? ResolveClan(evaluatedTitle?.DeFactoHolderClanId)?.Leader,
                PackageRoot = packageRoot,
                Progress = record?.Progress ?? 0f,
                ControlledTitles = controlledTitles,
                RequiredTitles = requiredTitles,
                TechnicalReason = technicalReason ?? string.Empty,
                BlockReason = blockReason
            };

            if (record == null)
            {
                assessment.State = eligible
                    ? FeudalDeJureDriftDisplayState.Ready
                    : FeudalDeJureDriftDisplayState.Blocked;
                if (!eligible && assessment.BlockReason == FeudalDeJureDriftBlockReason.None)
                    assessment.BlockReason = FeudalDeJureDriftBlockReason.RequirementsNotMet;
                return assessment;
            }

            bool targetChanged = eligible
                && eligibility?.TargetKingdom != null
                && !string.Equals(
                    record.TargetKingdomId,
                    eligibility.TargetKingdom.StringId,
                    StringComparison.Ordinal);
            if (targetChanged)
            {
                assessment.State = FeudalDeJureDriftDisplayState.Reversing;
                assessment.BlockReason = FeudalDeJureDriftBlockReason.ControlShifted;
                return assessment;
            }

            if (!eligible)
            {
                bool reversing = record.Progress > 0f && ShouldReverseDrift(evaluatedTitle, record);
                assessment.State = reversing
                    ? FeudalDeJureDriftDisplayState.Reversing
                    : FeudalDeJureDriftDisplayState.Paused;
                if (reversing)
                    assessment.BlockReason = FeudalDeJureDriftBlockReason.ControlShifted;
                else if (assessment.BlockReason == FeudalDeJureDriftBlockReason.None)
                    assessment.BlockReason = FeudalDeJureDriftBlockReason.RequirementsNotMet;
                return assessment;
            }

            switch (record.State)
            {
                case FeudalDeJureDriftState.Paused:
                    assessment.State = FeudalDeJureDriftDisplayState.Paused;
                    assessment.BlockReason = record.Progress >= 1f
                        ? FeudalDeJureDriftBlockReason.CompletionPending
                        : FeudalDeJureDriftBlockReason.AwaitingEvaluation;
                    break;
                case FeudalDeJureDriftState.Reversing:
                    assessment.State = FeudalDeJureDriftDisplayState.Reversing;
                    assessment.BlockReason = currentControlKingdom != null
                        && !string.Equals(
                            currentControlKingdom.StringId,
                            record.TargetKingdomId,
                            StringComparison.Ordinal)
                                ? FeudalDeJureDriftBlockReason.ControlShifted
                                : FeudalDeJureDriftBlockReason.AwaitingEvaluation;
                    break;
                default:
                    assessment.State = FeudalDeJureDriftDisplayState.Advancing;
                    assessment.BlockReason = FeudalDeJureDriftBlockReason.None;
                    break;
            }

            return assessment;
        }

        private bool IsPoliticallyOutsideLegalRealm(
            FeudalTitleBehavior titleBehavior,
            FeudalTitleRecord title,
            Kingdom currentControlKingdom)
        {
            Clan deFactoHolder = ResolveClan(title?.DeFactoHolderClanId);
            if (titleBehavior == null || title == null || deFactoHolder == null)
                return false;

            string legalKingdomId = ResolveRecordedLegalKingdomId(titleBehavior, title);
            if (string.IsNullOrWhiteSpace(legalKingdomId))
                return false;

            return currentControlKingdom == null
                || !string.Equals(
                    legalKingdomId,
                    currentControlKingdom.StringId,
                    StringComparison.Ordinal);
        }

        private static float CalculateProgressDelta(float elapsedDays, Hero integrator)
        {
            if (elapsedDays <= 0f)
                return 0f;
            return elapsedDays * GetBaseDailyProgress() * CalculateSpeedMultiplier(integrator);
        }

        private static void ReverseProgress(FeudalDeJureDriftRecord record, float elapsedDays)
        {
            if (record == null || elapsedDays <= 0f)
                return;
            record.AddProgress(-elapsedDays * GetBaseDailyProgress() * C.FeudalDeJureDriftReversalMultiplier);
        }

        public static float CalculateSpeedMultiplier(Hero integrator)
        {
            if (integrator == null)
                return C.FeudalDeJureDriftMinimumSpeed;

            float speed = C.FeudalDeJureDriftMinimumSpeed
                + integrator.GetSkillValue(DefaultSkills.Steward) / C.FeudalDeJureDriftStewardDivisor
                + integrator.GetSkillValue(DefaultSkills.Roguery) / C.FeudalDeJureDriftRogueryDivisor;
            return Math.Max(C.FeudalDeJureDriftMinimumSpeed, Math.Min(C.FeudalDeJureDriftMaximumSpeed, speed));
        }

        public static float CalculateEstimatedYearsRemaining(float progress, Hero integrator)
        {
            float remaining = Math.Max(0f, 1f - progress);
            return remaining * C.FeudalDeJureDriftBaseYears / CalculateSpeedMultiplier(integrator);
        }

        public static float CalculateReversalYearsRemaining(float progress)
        {
            return Math.Max(0f, progress) * C.FeudalDeJureDriftBaseYears / C.FeudalDeJureDriftReversalMultiplier;
        }

        private static float GetBaseDailyProgress()
        {
            int daysPerYear = Math.Max(1, CampaignTime.DaysInYear > 0 ? CampaignTime.DaysInYear : C.MarriageStrategyDaysPerYear);
            return 1f / (daysPerYear * C.FeudalDeJureDriftBaseYears);
        }

        private bool TryEvaluateEligibility(
            FeudalTitleBehavior titleBehavior,
            FeudalTitleRecord title,
            out FeudalDeJureDriftEligibility eligibility,
            out string reason)
        {
            return TryEvaluateEligibility(
                titleBehavior,
                title,
                out eligibility,
                out reason,
                out _,
                out _,
                out _,
                out _);
        }

        private bool TryEvaluateEligibility(
            FeudalTitleBehavior titleBehavior,
            FeudalTitleRecord title,
            out FeudalDeJureDriftEligibility eligibility,
            out string reason,
            out FeudalDeJureDriftBlockReason blockReason,
            out Kingdom prospectiveTarget,
            out int controlledTitles,
            out int requiredTitles)
        {
            eligibility = null;
            reason = null;
            blockReason = FeudalDeJureDriftBlockReason.None;
            prospectiveTarget = ResolveClan(title?.DeFactoHolderClanId)?.Kingdom;
            controlledTitles = 0;
            requiredTitles = 0;
            if (title == null || !title.IsActive || title.IsDeliberatelyDissolved)
            {
                reason = "title is missing or inactive";
                blockReason = FeudalDeJureDriftBlockReason.InactiveTitle;
                return false;
            }

            if (title.TitleType == FeudalTitleType.Empire)
            {
                reason = "empire titles have no superior tier";
                blockReason = FeudalDeJureDriftBlockReason.HighestTier;
                return false;
            }

            FeudalTitleRecord originalParent = titleBehavior.GetTitle(title.ParentTitleId);
            if (originalParent == null || !originalParent.IsActive)
            {
                reason = "title has no active legal parent";
                blockReason = FeudalDeJureDriftBlockReason.MissingLegalParent;
                return false;
            }

            if (!TryResolveUnifiedHolderKingdom(title, out Kingdom targetKingdom, out Hero integrator, out reason))
            {
                blockReason = ClassifyHolderBlock(title, out prospectiveTarget);
                return false;
            }
            prospectiveTarget = targetKingdom;
            if (!IsPermanentKingdom(targetKingdom))
            {
                reason = "controlling realm is temporary, eliminated, or invalid";
                blockReason = FeudalDeJureDriftBlockReason.TemporaryRealm;
                return false;
            }

            string originalKingdomId = ResolveRecordedLegalKingdomId(titleBehavior, title);
            if (string.Equals(originalKingdomId, targetKingdom.StringId, StringComparison.Ordinal))
            {
                reason = "title already belongs to the controlling realm's de jure hierarchy";
                blockReason = FeudalDeJureDriftBlockReason.AlreadyIntegrated;
                return false;
            }

            List<FeudalTitleRecord> packageTitles = titleBehavior.GetTitleAndDescendants(title)
                .Where(descendant => descendant != null && descendant.IsActive)
                .ToList();
            requiredTitles = packageTitles.Count;
            FeudalTitleRecord firstBlockedTitle = null;
            foreach (FeudalTitleRecord descendant in packageTitles)
            {
                if (TryResolveUnifiedHolderKingdom(descendant, out Kingdom descendantKingdom, out _, out _)
                    && descendantKingdom == targetKingdom)
                {
                    controlledTitles++;
                }
                else if (firstBlockedTitle == null)
                {
                    firstBlockedTitle = descendant;
                }
            }
            if (controlledTitles < requiredTitles)
            {
                reason = $"descendant title {firstBlockedTitle?.TitleId ?? "unknown"} lacks unified de jure and de facto control";
                blockReason = FeudalDeJureDriftBlockReason.PartialPackageControl;
                return false;
            }

            FeudalTitleRecord targetParent = FindTargetParent(titleBehavior, title, originalParent, targetKingdom);
            if (targetParent == null)
            {
                FeudalTitleRecord sovereign = titleBehavior.GetRealmSovereignTitle(targetKingdom, FeudalHierarchyMode.DeFacto);
                reason = sovereign != null && sovereign.TitleType <= title.TitleType
                    ? "controlling realm has no higher title capable of legally receiving this title"
                    : "no coherent higher title exists in the controlling realm";
                blockReason = FeudalDeJureDriftBlockReason.NoReceivingTitle;
                return false;
            }

            eligibility = new FeudalDeJureDriftEligibility(title, originalParent, targetParent, targetKingdom, integrator);
            return true;
        }

        private FeudalTitleRecord FindTargetParent(FeudalTitleBehavior titleBehavior, FeudalTitleRecord title, FeudalTitleRecord originalParent, Kingdom targetKingdom)
        {
            // The parent may finish first; the child can finish later without leaving it.
            if (originalParent != null && originalParent.IsActive && originalParent.TitleType > title.TitleType
                && ResolveRecordedLegalKingdomId(titleBehavior, originalParent) == targetKingdom.StringId
                && IsTitleHeldInsideKingdom(originalParent, targetKingdom))
                return originalParent;

            HashSet<string> descendantIds = new HashSet<string>(titleBehavior
                .GetTitleAndDescendants(title)
                .Where(candidate => candidate != null)
                .Select(candidate => candidate.TitleId));

            FeudalTitleRecord sameTierParent = titleBehavior.GetAllTitles()
                .Where(candidate => candidate != null
                    && candidate.IsActive
                    && candidate.TitleType == originalParent.TitleType
                    && candidate.TitleType > title.TitleType
                    && candidate.TitleId != originalParent.TitleId
                    && !descendantIds.Contains(candidate.TitleId)
                    && string.Equals(
                        ResolveRecordedLegalKingdomId(titleBehavior, candidate),
                        targetKingdom.StringId,
                        StringComparison.Ordinal)
                    && IsTitleHeldInsideKingdom(candidate, targetKingdom)
                    && titleBehavior.AreTitlesAdjacentForTitleLogic(title, candidate))
                .OrderBy(candidate => candidate.Name)
                .FirstOrDefault();
            if (sameTierParent != null)
                return sameTierParent;

            FeudalTitleRecord sovereign = titleBehavior.GetRealmSovereignTitle(targetKingdom, FeudalHierarchyMode.DeJure)
                ?? titleBehavior.GetKingdomPoliticalTitle(targetKingdom);
            return sovereign != null
                && sovereign.IsActive
                && sovereign.TitleType > title.TitleType
                && !descendantIds.Contains(sovereign.TitleId)
                && string.Equals(
                    ResolveRecordedLegalKingdomId(titleBehavior, sovereign),
                    targetKingdom.StringId,
                    StringComparison.Ordinal)
                    ? sovereign
                    : null;
        }

        private bool TryResolveUnifiedHolderKingdom(FeudalTitleRecord title, out Kingdom kingdom, out Hero integrator, out string reason)
        {
            kingdom = null;
            integrator = null;
            reason = null;
            Clan deJureHolder = ResolveClan(title?.DeJureHolderClanId);
            Clan deFactoHolder = ResolveClan(title?.DeFactoHolderClanId);
            if (deJureHolder == null || deFactoHolder == null)
            {
                reason = "de jure or de facto holder is unresolved";
                return false;
            }

            if (deJureHolder.Kingdom == null || deJureHolder.Kingdom != deFactoHolder.Kingdom)
            {
                reason = "de jure and de facto holders do not share one realm";
                return false;
            }

            kingdom = deJureHolder.Kingdom;
            integrator = deJureHolder.Leader ?? deFactoHolder.Leader;
            return true;
        }

        private FeudalDeJureDriftBlockReason ClassifyHolderBlock(
            FeudalTitleRecord title,
            out Kingdom prospectiveTarget)
        {
            Clan deJureHolder = ResolveClan(title?.DeJureHolderClanId);
            Clan deFactoHolder = ResolveClan(title?.DeFactoHolderClanId);
            prospectiveTarget = deFactoHolder?.Kingdom;
            if (deJureHolder == null || deFactoHolder == null)
                return FeudalDeJureDriftBlockReason.UnresolvedRights;
            if (deFactoHolder.Kingdom == null)
                return FeudalDeJureDriftBlockReason.NoControllingRealm;
            return FeudalDeJureDriftBlockReason.SplitRights;
        }

        private bool IsTitleHeldInsideKingdom(FeudalTitleRecord title, Kingdom kingdom)
        {
            return kingdom != null
                && TryResolveUnifiedHolderKingdom(title, out Kingdom holderKingdom, out _, out _)
                && holderKingdom == kingdom;
        }

        private Kingdom ResolveTitleKingdom(FeudalTitleRecord title)
        {
            if (title == null)
                return null;

            Clan deJureHolder = ResolveClan(title.DeJureHolderClanId);
            if (deJureHolder?.Kingdom != null)
                return deJureHolder.Kingdom;
            Clan deFactoHolder = ResolveClan(title.DeFactoHolderClanId);
            if (deFactoHolder?.Kingdom != null)
                return deFactoHolder.Kingdom;
            return ResolveKingdom(title.AssociatedKingdomId);
        }

        private string ResolveRecordedLegalKingdomId(FeudalTitleBehavior titleBehavior, FeudalTitleRecord title)
        {
            if (titleBehavior == null || title == null)
                return string.Empty;
            Clan holder = ResolveClan(title.DeFactoHolderClanId);
            Kingdom realm = holder?.Kingdom;
            // Recognizing a fully owned sovereign does not integrate its descendants or erase its old legal parent.
            if (realm != null && IsPermanentKingdom(realm) && realm.RulingClan == holder
                && title.DeJureHolderClanId == holder.StringId
                && titleBehavior.GetRealmSovereignTitle(realm, FeudalHierarchyMode.DeJure)?.TitleId == title.TitleId)
                return realm.StringId;
            if (!string.IsNullOrWhiteSpace(title.AssociatedKingdomId))
                return title.AssociatedKingdomId;

            HashSet<string> visited = new HashSet<string>(StringComparer.Ordinal);
            FeudalTitleRecord current = titleBehavior.GetTitle(title.ParentTitleId);
            while (current != null && current.IsActive && visited.Add(current.TitleId))
            {
                if (!string.IsNullOrWhiteSpace(current.AssociatedKingdomId))
                    return current.AssociatedKingdomId;
                current = titleBehavior.GetTitle(current.ParentTitleId);
            }

            return ResolveTitleKingdom(titleBehavior.GetTitle(title.ParentTitleId))?.StringId ?? string.Empty;
        }

        private bool ShouldReverseDrift(FeudalTitleRecord title, FeudalDeJureDriftRecord record)
        {
            return TryResolveUnifiedHolderKingdom(title, out Kingdom currentKingdom, out _, out _)
                && currentKingdom != null
                // A civil-war or feud shell interrupts integration, not the accumulated legal history.
                && IsPermanentKingdom(currentKingdom)
                && !string.Equals(currentKingdom.StringId, record.TargetKingdomId, StringComparison.Ordinal);
        }

        private bool IsPermanentKingdom(Kingdom kingdom)
        {
            if (kingdom == null || kingdom.IsEliminated || kingdom.RulingClan?.Leader == null
                || BellumKingdomVisibilityHelper.IsTemporaryBellumKingdom(kingdom))
                return false;
            FactionManagerBehavior factionManager = Campaign.Current?.GetCampaignBehavior<FactionManagerBehavior>();
            return factionManager?.GetFactionByRebelKingdom(kingdom) == null;
        }

        private void EnqueueAllTitles(FeudalTitleBehavior titleBehavior)
        {
            foreach (FeudalTitleRecord title in titleBehavior.GetAllTitles()
                .Where(title => title != null && title.IsActive)
                .OrderByDescending(title => title.TitleType)
                .ThenBy(title => title.TitleId))
            {
                Enqueue(title.TitleId);
            }
        }

        private void EnqueueTitleChain(FeudalTitleBehavior titleBehavior, FeudalTitleRecord title)
        {
            HashSet<string> visited = new HashSet<string>();
            Queue<FeudalTitleRecord> queue = new Queue<FeudalTitleRecord>();
            if (title != null)
                queue.Enqueue(title);

            while (queue.Count > 0)
            {
                FeudalTitleRecord current = queue.Dequeue();
                if (current == null || !visited.Add(current.TitleId))
                    continue;

                Enqueue(current.TitleId);
                FeudalTitleRecord legalParent = titleBehavior.GetTitle(current.ParentTitleId);
                FeudalTitleRecord politicalParent = titleBehavior.GetTitle(current.DeFactoParentTitleId);
                if (legalParent != null)
                    queue.Enqueue(legalParent);
                if (politicalParent != null)
                    queue.Enqueue(politicalParent);
            }
        }

        private void Enqueue(string titleId)
        {
            if (string.IsNullOrWhiteSpace(titleId) || !_pendingTitleIdSet.Add(titleId))
                return;
            _pendingTitleIds.Enqueue(titleId);
        }

        private void RemoveInvalidRecords(FeudalTitleBehavior titleBehavior)
        {
            foreach (FeudalDeJureDriftRecord record in _drifts.Where(record => record != null).ToList())
            {
                if (titleBehavior.GetTitle(record.TitleId) == null)
                    record.SetActive(false);
            }
            RemoveInactiveRecords();
        }

        private void RemoveInactiveRecords()
        {
            _drifts.RemoveAll(record => record == null || !record.IsActive);
            RebuildDriftIndex();
        }

        private void EnsureCollectionsInitialized()
        {
            if (_drifts == null)
                _drifts = new List<FeudalDeJureDriftRecord>();
        }

        private void RebuildRuntimeState()
        {
            _pendingTitleIds.Clear();
            _pendingTitleIdSet.Clear();
            RebuildDriftIndex();
            RefreshFactionIndexes();
        }

        private void RebuildDriftIndex()
        {
            _driftByTitleId.Clear();
            foreach (FeudalDeJureDriftRecord record in _drifts
                .Where(record => record != null && record.IsActive && !string.IsNullOrWhiteSpace(record.TitleId))
                .ToList())
            {
                if (_driftByTitleId.ContainsKey(record.TitleId))
                    record.SetActive(false);
                else
                    _driftByTitleId[record.TitleId] = record;
            }
            _drifts.RemoveAll(record => record == null || !record.IsActive);
        }

        private void RefreshFactionIndexes()
        {
            _clansById = Clan.All
                .Where(clan => clan != null && !string.IsNullOrWhiteSpace(clan.StringId))
                .GroupBy(clan => clan.StringId)
                .ToDictionary(group => group.Key, group => group.First());
            _kingdomsById = Kingdom.All
                .Where(kingdom => kingdom != null && !string.IsNullOrWhiteSpace(kingdom.StringId))
                .GroupBy(kingdom => kingdom.StringId)
                .ToDictionary(group => group.Key, group => group.First());
        }

        private void EnsureDisplayFactionIndexes(
            FeudalTitleRecord title,
            FeudalDeJureDriftRecord record)
        {
            bool missingDeJureHolder = !string.IsNullOrWhiteSpace(title?.DeJureHolderClanId)
                && (_clansById == null || !_clansById.ContainsKey(title.DeJureHolderClanId));
            bool missingDeFactoHolder = !string.IsNullOrWhiteSpace(title?.DeFactoHolderClanId)
                && (_clansById == null || !_clansById.ContainsKey(title.DeFactoHolderClanId));
            bool missingRecordedTarget = !string.IsNullOrWhiteSpace(record?.TargetKingdomId)
                && (_kingdomsById == null || !_kingdomsById.ContainsKey(record.TargetKingdomId));
            if (_clansById == null
                || _kingdomsById == null
                || missingDeJureHolder
                || missingDeFactoHolder
                || missingRecordedTarget)
            {
                RefreshFactionIndexes();
            }
        }

        private Clan ResolveClan(string clanId)
        {
            return !string.IsNullOrWhiteSpace(clanId) && _clansById.TryGetValue(clanId, out Clan clan) ? clan : null;
        }

        private Kingdom ResolveKingdom(string kingdomId)
        {
            return !string.IsNullOrWhiteSpace(kingdomId) && _kingdomsById.TryGetValue(kingdomId, out Kingdom kingdom) ? kingdom : null;
        }

        private static float CurrentDay => (float)CampaignTime.Now.ToDays;
    }
}
