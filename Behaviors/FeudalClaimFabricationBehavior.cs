using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.GameState;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.SaveSystem;
using C = BellumCivile.BellumCivileConstants;

namespace BellumCivile.Behaviors
{
    /// <summary>
    /// Handles saved claim-fabrication progress, recoverable setbacks and final exposure.
    /// </summary>
    public partial class FeudalClaimFabricationBehavior : CampaignBehaviorBase
    {
        private List<FeudalClaimFabricationRecord> _fabrications = new List<FeudalClaimFabricationRecord>();
        private List<PendingFabricationOutcome> _pendingPlayerOutcomes = new List<PendingFabricationOutcome>();
        private bool _isShowingPlayerOutcome;

        public override void RegisterEvents()
        {
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
            CampaignEvents.TickEvent.AddNonSerializedListener(this, OnTick);
            CampaignEvents.OnSettlementOwnerChangedEvent.AddNonSerializedListener(this, OnSettlementOwnerChanged);
            CampaignEvents.OnClanChangedKingdomEvent.AddNonSerializedListener(this, OnClanChangedKingdom);
            CampaignEvents.HeroKilledEvent.AddNonSerializedListener(this, OnHeroKilled);
        }

        public override void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData("BellumCivile_FeudalClaimFabrications", ref _fabrications);
            dataStore.SyncData("BellumCivile_FabricationReports", ref _pendingPlayerOutcomes);
            dataStore.SyncData("BellumCivile_FabricationCooldowns", ref _retryAfter);
            if (_retryAfter == null) _retryAfter = new Dictionary<string, float>();
            if (_pendingPlayerOutcomes == null)
                _pendingPlayerOutcomes = new List<PendingFabricationOutcome>();
            if (dataStore.IsLoading)
            {
                _isShowingPlayerOutcome = false;
                _pendingPlayerOutcomes.RemoveAll(outcome => outcome == null);
            }
            EnsureCollectionsInitialized();
            foreach (FeudalClaimFabricationRecord record in _fabrications)
                record?.ClearLegacyPauseState();
        }

        public IReadOnlyList<FeudalClaimFabricationRecord> GetActiveFabrications()
        {
            EnsureCollectionsInitialized();
            return _fabrications.Where(record => record != null && record.IsActive).ToList();
        }

        public FeudalFabricationPreview GetFabricationPreview(Clan clan, FeudalTitleRecord targetTitle)
        {
            EnsureCollectionsInitialized();
            FeudalFabricationPreview preview = new FeudalFabricationPreview
            {
                Reason = string.Empty,
                GoldCost = targetTitle == null ? 0 : GetFabricationGoldCost(targetTitle.TitleType),
                InfluenceCost = targetTitle == null ? 0f : GetFabricationInfluenceCost(targetTitle.TitleType)
            };

            FeudalTitleBehavior titleBehavior = Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>();
            if (titleBehavior == null)
            {
                preview.Reason = "title behavior unavailable";
                return preview;
            }
            if (clan?.Leader == null || clan.Leader.IsDead || string.IsNullOrWhiteSpace(clan.StringId))
            {
                preview.Reason = "fabricating clan has no living leader";
                return preview;
            }
            if (targetTitle == null || !targetTitle.IsActive || targetTitle.IsDeliberatelyDissolved)
            {
                preview.Reason = "target title is missing or inactive";
                return preview;
            }

            FeudalClaimFabricationRecord activeRecord = _fabrications.FirstOrDefault(existing =>
                existing != null && existing.IsActive && existing.FabricatorClanId == clan.StringId);
            preview.ActiveRecord = activeRecord;
            if (activeRecord != null)
            {
                preview.Reason = activeRecord.TargetTitleId == targetTitle.TitleId
                    ? "fabrication is already underway"
                    : "clan is already fabricating another claim";
                return preview;
            }
            if (IsOnCooldown(clan, targetTitle))
            {
                preview.Reason = new TextObject("{=BC_Fabrication_RetryWait}Your house must wait before pursuing another forgery for this title.").ToString();
                return preview;
            }
            if (targetTitle.DeJureHolderClanId == clan.StringId)
            {
                preview.Reason = "clan is already the de jure holder";
                return preview;
            }
            if (titleBehavior.HasActiveClaim(clan, targetTitle))
            {
                preview.Reason = "clan already has an active claim";
                return preview;
            }
            if (!TryGetFabricationTrack(titleBehavior, clan, targetTitle, out FeudalClaimFabricationTrack track, out string reason))
            {
                preview.Reason = reason;
                return preview;
            }

            preview.Track = track;
            preview.DailyProgress = CalculateDailyProgressDelta(clan.Leader);
            preview.EstimatedDays = preview.DailyProgress > 0f ? 1f / preview.DailyProgress : 0f;
            if (clan.Leader.Gold < preview.GoldCost)
            {
                preview.Reason = "insufficient gold";
                return preview;
            }
            if (clan.Influence < preview.InfluenceCost)
            {
                preview.Reason = "insufficient influence";
                return preview;
            }

            preview.CanStart = true;
            return preview;
        }

        public bool TryStartFabrication(Clan clan, FeudalTitleRecord targetTitle, bool chargeCost, out FeudalClaimFabricationRecord record, out string reason)
        {
            record = null;
            reason = null;
            EnsureCollectionsInitialized();

            FeudalTitleBehavior titleBehavior = Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>();
            if (titleBehavior == null)
            {
                reason = "title behavior unavailable";
                return false;
            }

            if (clan?.Leader == null || clan.Leader.IsDead || string.IsNullOrWhiteSpace(clan.StringId))
            {
                reason = "fabricating clan has no living leader";
                return false;
            }

            if (targetTitle == null || !targetTitle.IsActive || targetTitle.IsDeliberatelyDissolved)
            {
                reason = "target title is missing or inactive";
                return false;
            }

            if (targetTitle.DeJureHolderClanId == clan.StringId)
            {
                reason = "clan is already the de jure holder";
                return false;
            }

            if (titleBehavior.HasActiveClaim(clan, targetTitle))
            {
                reason = "clan already has an active claim";
                return false;
            }

            if (_fabrications.Any(existing => existing != null && existing.IsActive && existing.FabricatorClanId == clan.StringId))
            {
                reason = "clan is already fabricating another claim";
                return false;
            }

            if (!TryGetFabricationTrack(titleBehavior, clan, targetTitle, out FeudalClaimFabricationTrack track, out reason))
                return false;

            int goldCost = GetFabricationGoldCost(targetTitle.TitleType);
            if (IsOnCooldown(clan, targetTitle))
            {
                reason = new TextObject("{=BC_Fabrication_RetryWait}Your house must wait before pursuing another forgery for this title.").ToString();
                return false;
            }
            float influenceCost = GetFabricationInfluenceCost(targetTitle.TitleType);

            if (chargeCost)
            {
                if (clan.Leader.Gold < goldCost)
                {
                    reason = "insufficient gold";
                    return false;
                }

                if (!NpcInfluenceBudgetService.CanAfford(
                    clan,
                    influenceCost,
                    NpcInfluenceExpenseKind.Discretionary))
                {
                    reason = clan == Clan.PlayerClan
                        ? "insufficient influence"
                        : "insufficient influence reserve";
                    return false;
                }

                if (influenceCost > 0f
                    && !NpcInfluenceBudgetService.TrySpend(
                        clan,
                        influenceCost,
                        NpcInfluenceExpenseKind.Discretionary,
                        "claim_fabrication"))
                {
                    reason = clan == Clan.PlayerClan
                        ? "insufficient influence"
                        : "insufficient influence reserve";
                    return false;
                }

                if (goldCost > 0)
                    GiveGoldAction.ApplyBetweenCharacters(clan.Leader, null, goldCost, true);
            }

            record = new FeudalClaimFabricationRecord(
                BuildFabricationId(clan, targetTitle),
                clan.Leader.StringId,
                clan.StringId,
                targetTitle.TitleId,
                track,
                0f,
                CalculateDailyProgressDelta(clan.Leader),
                CurrentDay,
                chargeCost ? goldCost : 0,
                chargeCost ? influenceCost : 0f);

            _fabrications.Add(record);
            BellumCivileLogger.Log($"Feudal claim fabrication started; clan={clan.StringId}; hero={clan.Leader.StringId}; title={targetTitle.TitleId}; track={track}; gold={record.PaidGoldCost}; influence={record.PaidInfluenceCost:0}; daily_delta={record.DailyProgressDelta:0.000000}.");
            if (clan == Clan.PlayerClan || clan.Leader == Hero.MainHero)
                NotificationHelper.ShowPlayerFeudalClaimFabricationStarted(clan, clan.Leader, targetTitle);
            return true;
        }

        public bool DebugAdvanceFabrication(Clan clan, float progressDelta, out string result)
        {
            result = null;
            EnsureCollectionsInitialized();

            if (clan == null)
            {
                result = "Error: clan is null.";
                return false;
            }

            FeudalClaimFabricationRecord record = _fabrications.FirstOrDefault(fabrication =>
                fabrication != null && fabrication.IsActive && fabrication.FabricatorClanId == clan.StringId);
            if (record == null)
            {
                result = $"Error: {clan.Name} has no active fabrication.";
                return false;
            }

            FeudalTitleBehavior titleBehavior = Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>();
            Hero fabricator = ResolveHero(record.FabricatorHeroId);
            FeudalTitleRecord title = titleBehavior?.GetTitle(record.TargetTitleId);
            if (!IsFabricationStillValid(titleBehavior, record, clan, fabricator, title, out string invalidReason))
            {
                CancelFabrication(record, clan, title, $"debug advance: {invalidReason}", refundGold: true);
                result = $"Cancelled: {invalidReason}.";
                return false;
            }

            if (progressDelta >= 1f)
            {
                if (record.AwaitingSetback)
                {
                    result = "Resolve the pending fabrication setback before advancing the scheme.";
                    return false;
                }
                record.SetProgress(1f);
                CompleteFabrication(titleBehavior, record, clan, fabricator, title);
                _fabrications.RemoveAll(fabrication => fabrication == null || !fabrication.IsActive);
                result = $"Completed fabrication for {clan.Name} on {title.Name}. Weak claim registered.";
                BellumCivileDebug.Trace(
                    "fabrication",
                    $"debug completed fabrication without discovery checks; clan={clan.StringId}; title={title.TitleId}.",
                    requestInGameDisplay: true);
                return true;
            }

            AdvanceFabricationProgress(record, clan, fabricator, title, Math.Max(0f, progressDelta));
            if (record.IsActive && record.Progress >= 1f)
            {
                CompleteFabrication(titleBehavior, record, clan, fabricator, title);
                _fabrications.RemoveAll(fabrication => fabrication == null || !fabrication.IsActive);
                result = $"Completed fabrication for {clan.Name} on {title.Name}. Weak claim registered.";
                return true;
            }

            result = record.IsActive
                ? $"Advanced fabrication for {clan.Name} on {title.Name} to {record.Progress:P1}."
                : $"Fabrication for {clan.Name} on {title.Name} was exposed and ended.";
            return true;
        }

        private void OnDailyTick()
        {
            EnsureCollectionsInitialized();
            foreach (string key in _retryAfter.Where(pair => pair.Value <= CurrentDay).Select(pair => pair.Key).ToList())
                _retryAfter.Remove(key);
            FeudalTitleBehavior titleBehavior = Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>();
            if (titleBehavior == null)
                return;

            foreach (FeudalClaimFabricationRecord record in _fabrications.Where(record => record != null && record.IsActive).ToList())
            {
                Clan clan = ResolveClan(record.FabricatorClanId);
                Hero fabricator = ResolveHero(record.FabricatorHeroId);
                FeudalTitleRecord title = titleBehavior.GetTitle(record.TargetTitleId);

                if (!IsFabricationStillValid(titleBehavior, record, clan, fabricator, title, out string invalidReason))
                {
                    CancelFabrication(record, clan, title, invalidReason, refundGold: true);
                    continue;
                }

                record.SetDailyProgressDelta(CalculateDailyProgressDelta(fabricator));
                if (record.AwaitingSetback)
                {
                    if (clan != Clan.PlayerClan) ResolveNpcSetback(titleBehavior, record, clan, title);
                    continue;
                }

                AdvanceFabricationProgress(record, clan, fabricator, title, record.DailyProgressDelta);
                if (record.IsActive && record.Progress >= 1f)
                    CompleteFabrication(titleBehavior, record, clan, fabricator, title);
            }

            _fabrications.RemoveAll(record => record == null || !record.IsActive);
        }

        private void OnTick(float dt)
        {
            if (_isShowingPlayerOutcome || InformationManager.IsAnyInquiryActive())
                return;

            if (!(Game.Current?.GameStateManager?.ActiveState is MapState))
                return;

            if (_pendingPlayerOutcomes.Count == 0)
            {
                ShowPlayerSetback();
                return;
            }

            PendingFabricationOutcome outcome = _pendingPlayerOutcomes[0];
            _isShowingPlayerOutcome = true;
            InformationManager.ShowInquiry(new InquiryData(
                outcome.Title,
                outcome.Body,
                true,
                false,
                new TextObject("{=BC_Fabrication_Result_Done}Done").ToString(),
                null,
                () => { _pendingPlayerOutcomes.Remove(outcome); _isShowingPlayerOutcome = false; },
                null), true, false);
        }

        public bool DebugEvaluateFabricationTarget(Clan clan, out string result)
        {
            result = null;
            EnsureCollectionsInitialized();

            FeudalTitleBehavior titleBehavior = Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>();
            if (titleBehavior == null)
            {
                result = "Error: title behavior unavailable.";
                return false;
            }

            if (!IsValidAiFabricationClan(clan, out string clanReason))
            {
                result = $"Error: {clanReason}.";
                return false;
            }

            FeudalClaimFabricationRecord activeRecord = _fabrications.FirstOrDefault(record =>
                record != null && record.IsActive && record.FabricatorClanId == clan.StringId);
            if (activeRecord != null)
            {
                FeudalTitleRecord activeTitle = titleBehavior.GetTitle(activeRecord.TargetTitleId);
                result = $"{clan.Name} is already fabricating a claim on {activeTitle?.Name ?? activeRecord.TargetTitleId}; progress={activeRecord.Progress:P1}.";
                return true;
            }

            List<AiFabricationCandidate> candidates = BuildAiFabricationCandidates(titleBehavior, clan)
                .OrderByDescending(candidate => candidate.Score)
                .ThenByDescending(candidate => candidate.Title.TitleType)
                .ThenBy(candidate => candidate.Title.Name ?? string.Empty)
                .Take(8)
                .ToList();

            if (candidates.Count == 0)
            {
                result = $"{clan.Name} has no valid fabrication targets.";
                return true;
            }

            AiFabricationCandidate best = candidates[0];
            float threshold = GetMinimumScore(best.Track);
            bool eligible = best.Score >= threshold;
            float startChance = eligible ? CalculateAiStartChance(best.Score, threshold) : 0f;
            result =
                $"Best fabrication target for {clan.Name}: {best.Title.Name} ({best.Title.TitleType}, {best.Track}) score={best.Score:0} threshold={threshold:0} eligible={eligible} annual_chance={startChance:P0}; reasons={string.Join(", ", best.Reasons)}\n"
                + "Top targets:\n"
                + string.Join("\n", candidates.Select(candidate =>
                    $"- {candidate.Title.Name} ({candidate.Title.TitleType}, {candidate.Track}) score={candidate.Score:0}/{GetMinimumScore(candidate.Track):0}; reasons={string.Join(", ", candidate.Reasons)}"));
            return true;
        }

        public bool TryRunAutonomousAiEvaluationForClan(FeudalTitleBehavior titleBehavior, Clan clan, out string report)
        {
            report = null;
            EnsureCollectionsInitialized();

            if (titleBehavior == null)
            {
                report = "title behavior unavailable";
                return false;
            }

            if (!IsValidAiFabricationClan(clan, out string clanReason))
            {
                report = clanReason;
                return false;
            }

            if (_fabrications.Any(record => record != null && record.IsActive && record.FabricatorClanId == clan.StringId))
            {
                report = "clan is already fabricating another claim";
                return false;
            }

            if (!TryFindBestAiFabricationTarget(titleBehavior, clan, out AiFabricationCandidate bestCandidate, out string reason))
            {
                report = reason;
                return false;
            }

            float threshold = GetMinimumScore(bestCandidate.Track);
            float startChance = CalculateAiStartChance(bestCandidate.Score, threshold);
            float chanceRoll = MBRandom.RandomFloat;
            if (chanceRoll >= startChance)
            {
                report = $"declined after roll; title={bestCandidate.Title.Name}; score={bestCandidate.Score:0}/{threshold:0}; chance={startChance:P0}; roll={chanceRoll:P0}";
                BellumCivileDebug.TraceIfEnabled(
                    "fabrication",
                    $"AI declined claim fabrication after landed ambition roll; clan={clan.Name} ({clan.StringId}); title={bestCandidate.Title.Name} ({bestCandidate.Title.TitleId}); track={bestCandidate.Track}; score={bestCandidate.Score:0}/{threshold:0}; chance={startChance:P0}; roll={chanceRoll:P0}; reasons={string.Join(", ", bestCandidate.Reasons)}.",
                    requestInGameDisplay: true);
                return false;
            }

            if (!TryStartFabrication(clan, bestCandidate.Title, chargeCost: true, out FeudalClaimFabricationRecord startedRecord, out string startReason))
            {
                report = startReason ?? reason ?? "unknown";
                BellumCivileLogger.Log($"Feudal claim fabrication AI failed to start after selection; clan={clan.StringId}; title={bestCandidate.Title.TitleId}; reason={report}.");
                return false;
            }

            report = $"started fabrication; title={bestCandidate.Title.Name}; track={startedRecord.Track}; score={bestCandidate.Score:0}/{threshold:0}; chance={startChance:P0}; roll={chanceRoll:P0}";
            BellumCivileDebug.Trace(
                "fabrication",
                $"AI started claim fabrication from landed ambition pass; clan={clan.Name} ({clan.StringId}); hero={clan.Leader.Name} ({clan.Leader.StringId}); title={bestCandidate.Title.Name} ({bestCandidate.Title.TitleId}); track={startedRecord.Track}; score={bestCandidate.Score:0}/{threshold:0}; chance={startChance:P0}; roll={chanceRoll:P0}; cost={startedRecord.PaidGoldCost}g/{startedRecord.PaidInfluenceCost:0}inf; reasons={string.Join(", ", bestCandidate.Reasons)}.",
                requestInGameDisplay: true);
            return true;
        }

        private static bool TryFindBestAiFabricationTarget(FeudalTitleBehavior titleBehavior, Clan clan, out AiFabricationCandidate bestCandidate, out string reason)
        {
            bestCandidate = null;
            reason = null;

            if (!IsValidAiFabricationClan(clan, out reason))
                return false;

            List<AiFabricationCandidate> candidates = BuildAiFabricationCandidates(titleBehavior, clan)
                .Where(candidate => candidate.Score >= GetMinimumScore(candidate.Track))
                .OrderByDescending(candidate => candidate.Score)
                .ThenByDescending(candidate => candidate.Title.TitleType)
                .ThenBy(candidate => candidate.Title.Name ?? string.Empty)
                .ToList();

            if (candidates.Count == 0)
            {
                reason = "no target cleared score threshold";
                return false;
            }

            bestCandidate = candidates.FirstOrDefault(candidate =>
                NpcInfluenceBudgetService.CanAfford(
                    clan,
                    GetFabricationInfluenceCost(candidate.Title.TitleType),
                    NpcInfluenceExpenseKind.Discretionary));
            if (bestCandidate == null)
            {
                AiFabricationCandidate blocked = candidates[0];
                float blockedCost = GetFabricationInfluenceCost(blocked.Title.TitleType);
                NpcInfluenceBudgetService.RecordBlocked(
                    clan,
                    blockedCost,
                    NpcInfluenceExpenseKind.Discretionary,
                    "claim_fabrication");
                reason = "insufficient influence reserve";
                return false;
            }

            return true;
        }

        private static List<AiFabricationCandidate> BuildAiFabricationCandidates(FeudalTitleBehavior titleBehavior, Clan clan)
        {
            if (titleBehavior == null || clan == null)
                return new List<AiFabricationCandidate>();

            List<FeudalTitleRecord> heldTitles = GetHeldTitles(titleBehavior, clan);

            return titleBehavior.GetAllTitles()
                .Where(title => title != null && title.IsActive)
                .Where(title => title.DeJureHolderClanId != clan.StringId)
                .Where(title => !titleBehavior.HasActiveClaim(clan, title))
                .Where(title => Campaign.Current?.GetCampaignBehavior<FeudalClaimFabricationBehavior>()?.IsOnCooldown(clan, title) != true)
                .Select(title => TryBuildAiFabricationCandidate(titleBehavior, clan, title, heldTitles, out AiFabricationCandidate candidate) ? candidate : null)
                .Where(candidate => candidate != null)
                .GroupBy(candidate => candidate.Title.TitleId)
                .Select(group => group.OrderByDescending(candidate => candidate.Score).First())
                .ToList();
        }

        private static bool TryBuildAiFabricationCandidate(
            FeudalTitleBehavior titleBehavior,
            Clan clan,
            FeudalTitleRecord title,
            IReadOnlyCollection<FeudalTitleRecord> heldTitles,
            out AiFabricationCandidate candidate, bool requireStartingFunds = true)
        {
            candidate = null;
            if (!TryGetFabricationTrack(titleBehavior, clan, title, out FeudalClaimFabricationTrack track, out _, heldTitles))
                return false;

            int goldCost = GetFabricationGoldCost(title.TitleType);
            float influenceCost = GetFabricationInfluenceCost(title.TitleType);
            int requiredGold = (int)Math.Round(goldCost * C.FeudalClaimFabricationAiGoldCostMultiplier) + C.FeudalClaimFabricationAiGoldReserve;
            if (clan.Leader == null || (requireStartingFunds && clan.Leader.Gold < requiredGold))
                return false;

            List<string> reasons = new List<string>();
            float score = GetTrackBaseScore(track);
            reasons.Add($"{track} base +{GetTrackBaseScore(track):0}");

            float desirePressure = ClanFiefDesireHelper.CalculateRebelliousFiefDesirePressure(clan);
            float desireScore = Math.Min(GetTrackDesireCap(track), desirePressure * GetTrackDesireScale(track));
            if (desireScore > 0f)
            {
                score += desireScore;
                reasons.Add($"fief desire +{desireScore:0}");
            }

            float titleTierScore = ((int)title.TitleType + 1) * C.FeudalClaimFabricationAiTitleTierScale;
            score += titleTierScore;
            reasons.Add($"title rank +{titleTierScore:0}");

            float personalityScore = CalculateFabricationTacticalPersonalityScore(clan.Leader, track, reasons);
            score += personalityScore;

            float relationScore = CalculateFabricationRelationScore(clan, title, reasons);
            score += relationScore;

            float budgetScore = CalculateFabricationBudgetComfortScore(clan, goldCost, influenceCost);
            if (budgetScore > 0f)
            {
                score += budgetScore;
                reasons.Add($"budget comfort +{budgetScore:0}");
            }

            float propensityMultiplier = CalculateFabricationPersonalityMultiplier(clan.Leader, track, reasons);
            score *= propensityMultiplier;
            if (Math.Abs(propensityMultiplier - 1f) >= 0.01f)
                reasons.Add($"combined personality x{propensityMultiplier:0.00}");

            candidate = new AiFabricationCandidate(title, track, score, reasons);
            return true;
        }

        private static float CalculateFabricationTacticalPersonalityScore(Hero leader, FeudalClaimFabricationTrack track, List<string> reasons)
        {
            if (leader == null)
                return 0f;

            float score = 0f;
            int calculating = leader.GetTraitLevel(DefaultTraits.Calculating);
            if (track == FeudalClaimFabricationTrack.DeFacto
                || track == FeudalClaimFabricationTrack.Liege
                || track == FeudalClaimFabricationTrack.Vassal
                || track == FeudalClaimFabricationTrack.Reclamation)
            {
                if (calculating >= 2)
                {
                    score += C.FeudalClaimFabricationAiStrategicCalculating2Bonus;
                    reasons.Add($"cerebral consolidation +{C.FeudalClaimFabricationAiStrategicCalculating2Bonus:0}");
                }
                else if (calculating == 1)
                {
                    score += C.FeudalClaimFabricationAiStrategicCalculating1Bonus;
                    reasons.Add($"calculating consolidation +{C.FeudalClaimFabricationAiStrategicCalculating1Bonus:0}");
                }
            }
            else if (track == FeudalClaimFabricationTrack.Horizontal)
            {
                if (calculating <= -2)
                {
                    score += C.FeudalClaimFabricationAiHorizontalImpulsive2Bonus;
                    reasons.Add($"hotheaded expansion +{C.FeudalClaimFabricationAiHorizontalImpulsive2Bonus:0}");
                }
                else if (calculating == -1)
                {
                    score += C.FeudalClaimFabricationAiHorizontalImpulsive1Bonus;
                    reasons.Add($"impulsive expansion +{C.FeudalClaimFabricationAiHorizontalImpulsive1Bonus:0}");
                }

                int valor = leader.GetTraitLevel(DefaultTraits.Valor);
                if (valor >= 2)
                {
                    score += C.FeudalClaimFabricationAiHorizontalValor2Bonus;
                    reasons.Add($"fearless expansion +{C.FeudalClaimFabricationAiHorizontalValor2Bonus:0}");
                }
                else if (valor == 1)
                {
                    score += C.FeudalClaimFabricationAiHorizontalValor1Bonus;
                    reasons.Add($"daring expansion +{C.FeudalClaimFabricationAiHorizontalValor1Bonus:0}");
                }
                else if (valor <= -2)
                {
                    score -= C.FeudalClaimFabricationAiHorizontalCautious2Penalty;
                    reasons.Add($"very cautious expansion -{C.FeudalClaimFabricationAiHorizontalCautious2Penalty:0}");
                }
                else if (valor == -1)
                {
                    score -= C.FeudalClaimFabricationAiHorizontalCautious1Penalty;
                    reasons.Add($"cautious expansion -{C.FeudalClaimFabricationAiHorizontalCautious1Penalty:0}");
                }
            }

            return score;
        }

        private static float CalculateFabricationPersonalityMultiplier(Hero leader, FeudalClaimFabricationTrack track, List<string> reasons)
        {
            if (leader == null)
                return 1f;

            float multiplier = 1f;
            int honor = leader.GetTraitLevel(DefaultTraits.Honor);
            if (honor >= 2)
            {
                float value = track == FeudalClaimFabricationTrack.DeFacto
                    || track == FeudalClaimFabricationTrack.Reclamation
                    ? C.FeudalClaimFabricationAiDeFactoHonorableMultiplier
                    : C.FeudalClaimFabricationAiHonorableMultiplier;
                multiplier *= value;
                reasons.Add($"honorable x{value:0.00}");
            }
            else if (honor == 1)
            {
                float value = track == FeudalClaimFabricationTrack.DeFacto
                    || track == FeudalClaimFabricationTrack.Reclamation
                    ? C.FeudalClaimFabricationAiDeFactoHonestMultiplier
                    : C.FeudalClaimFabricationAiHonestMultiplier;
                multiplier *= value;
                reasons.Add($"honest x{value:0.00}");
            }
            else if (honor <= -2)
            {
                multiplier *= C.FeudalClaimFabricationAiDeceitfulMultiplier;
                reasons.Add($"deceitful x{C.FeudalClaimFabricationAiDeceitfulMultiplier:0.00}");
            }
            else if (honor == -1)
            {
                multiplier *= C.FeudalClaimFabricationAiDeviousMultiplier;
                reasons.Add($"devious x{C.FeudalClaimFabricationAiDeviousMultiplier:0.00}");
            }

            int generosity = leader.GetTraitLevel(DefaultTraits.Generosity);
            if (generosity >= 2)
            {
                multiplier *= C.FeudalClaimFabricationAiMunificentMultiplier;
                reasons.Add($"munificent x{C.FeudalClaimFabricationAiMunificentMultiplier:0.00}");
            }
            else if (generosity == 1)
            {
                multiplier *= C.FeudalClaimFabricationAiGenerousMultiplier;
                reasons.Add($"generous x{C.FeudalClaimFabricationAiGenerousMultiplier:0.00}");
            }
            else if (generosity <= -2)
            {
                multiplier *= C.FeudalClaimFabricationAiTightfistedMultiplier;
                reasons.Add($"tightfisted x{C.FeudalClaimFabricationAiTightfistedMultiplier:0.00}");
            }
            else if (generosity == -1)
            {
                multiplier *= C.FeudalClaimFabricationAiClosefistedMultiplier;
                reasons.Add($"closefisted x{C.FeudalClaimFabricationAiClosefistedMultiplier:0.00}");
            }

            int mercy = leader.GetTraitLevel(DefaultTraits.Mercy);
            if (mercy >= 2)
            {
                multiplier *= C.FeudalClaimFabricationAiCompassionateMultiplier;
                reasons.Add($"compassionate x{C.FeudalClaimFabricationAiCompassionateMultiplier:0.00}");
            }
            else if (mercy == 1)
            {
                multiplier *= C.FeudalClaimFabricationAiMercifulMultiplier;
                reasons.Add($"merciful x{C.FeudalClaimFabricationAiMercifulMultiplier:0.00}");
            }
            else if (mercy <= -2)
            {
                multiplier *= C.FeudalClaimFabricationAiSadisticMultiplier;
                reasons.Add($"sadistic x{C.FeudalClaimFabricationAiSadisticMultiplier:0.00}");
            }
            else if (mercy == -1)
            {
                multiplier *= C.FeudalClaimFabricationAiCruelMultiplier;
                reasons.Add($"cruel x{C.FeudalClaimFabricationAiCruelMultiplier:0.00}");
            }

            return multiplier;
        }

        private static float CalculateFabricationRelationScore(Clan clan, FeudalTitleRecord title, List<string> reasons)
        {
            if (clan?.Leader == null || title == null)
                return 0f;

            Clan legalHolder = ResolveClan(title.DeJureHolderClanId);
            if (legalHolder?.Leader == null || legalHolder == clan)
                return 0f;

            float score = 0f;
            int relation = clan.Leader.GetRelation(legalHolder.Leader);
            float relationScore = Math.Max(-C.FeudalClaimFabricationAiRelationCap, Math.Min(C.FeudalClaimFabricationAiRelationCap, -relation * C.FeudalClaimFabricationAiRelationScale));
            if (Math.Abs(relationScore) >= 0.5f)
            {
                score += relationScore;
                reasons.Add(relationScore > 0f
                    ? $"rival holder +{relationScore:0}"
                    : $"friendly holder {relationScore:0}");
            }

            if (clan.Kingdom?.RulingClan == legalHolder)
            {
                score -= C.FeudalClaimFabricationAiRulerTargetPenalty;
                reasons.Add($"ruler title -{C.FeudalClaimFabricationAiRulerTargetPenalty:0}");
            }

            if (MarriageAllianceHelper.HasMarriageAlliance(clan, legalHolder))
            {
                score -= C.FeudalClaimFabricationAiMarriageAlliancePenalty;
                reasons.Add($"marriage alliance -{C.FeudalClaimFabricationAiMarriageAlliancePenalty:0}");
            }

            return score;
        }

        private static float CalculateFabricationBudgetComfortScore(Clan clan, int goldCost, float influenceCost)
        {
            if (clan?.Leader == null)
                return 0f;

            int minimumGold = (int)Math.Round(goldCost * C.FeudalClaimFabricationAiGoldCostMultiplier) + C.FeudalClaimFabricationAiGoldReserve;
            float minimumInfluence = NpcInfluenceBudgetService
                .Assess(clan, influenceCost, NpcInfluenceExpenseKind.Discretionary)
                .RequiredInfluence;
            if (minimumGold <= 0 || minimumInfluence <= 0f)
                return 0f;

            float goldComfort = Math.Max(0f, (clan.Leader.Gold - minimumGold) / (float)Math.Max(1, goldCost));
            float influenceComfort = Math.Max(0f, (clan.Influence - minimumInfluence) / Math.Max(1f, influenceCost));
            return Math.Min(C.FeudalClaimFabricationAiBudgetComfortCap, (goldComfort + influenceComfort) * 2.5f);
        }

        private static float GetTrackBaseScore(FeudalClaimFabricationTrack track)
        {
            switch (track)
            {
                case FeudalClaimFabricationTrack.DeFacto:
                    return C.FeudalClaimFabricationAiDeFactoBaseScore;
                case FeudalClaimFabricationTrack.Liege:
                    return C.FeudalClaimFabricationAiLiegeBaseScore;
                case FeudalClaimFabricationTrack.Horizontal:
                    return C.FeudalClaimFabricationAiHorizontalBaseScore;
                case FeudalClaimFabricationTrack.Vassal:
                    return C.FeudalClaimFabricationAiVassalBaseScore;
                case FeudalClaimFabricationTrack.Reclamation:
                    return C.FeudalClaimFabricationAiReclamationBaseScore;
                default:
                    return 0f;
            }
        }

        private static float GetMinimumScore(FeudalClaimFabricationTrack track)
        {
            switch (track)
            {
                case FeudalClaimFabricationTrack.DeFacto:
                    return C.FeudalClaimFabricationAiDeFactoMinimumScore;
                case FeudalClaimFabricationTrack.Liege:
                    return C.FeudalClaimFabricationAiLiegeMinimumScore;
                case FeudalClaimFabricationTrack.Horizontal:
                    return C.FeudalClaimFabricationAiHorizontalMinimumScore;
                case FeudalClaimFabricationTrack.Vassal:
                    return C.FeudalClaimFabricationAiVassalMinimumScore;
                case FeudalClaimFabricationTrack.Reclamation:
                    return C.FeudalClaimFabricationAiReclamationMinimumScore;
                default:
                    return float.MaxValue;
            }
        }

        private static float CalculateAiStartChance(float score, float threshold)
        {
            if (score < threshold)
                return 0f;

            float chance = C.FeudalClaimFabricationAiStartChanceAtThreshold
                + ((score - threshold) * C.FeudalClaimFabricationAiStartChancePerExcessPoint);
            return Math.Max(0f, Math.Min(C.FeudalClaimFabricationAiStartChanceCap, chance));
        }

        private static int GetCampaignDaysInYear()
        {
            return Math.Max(1, CampaignTime.DaysInYear > 0 ? CampaignTime.DaysInYear : C.MarriageStrategyDaysPerYear);
        }

        private static float GetTrackDesireScale(FeudalClaimFabricationTrack track)
        {
            switch (track)
            {
                case FeudalClaimFabricationTrack.DeFacto:
                    return C.FeudalClaimFabricationAiDeFactoDesireScale;
                case FeudalClaimFabricationTrack.Liege:
                    return C.FeudalClaimFabricationAiLiegeDesireScale;
                case FeudalClaimFabricationTrack.Horizontal:
                    return C.FeudalClaimFabricationAiHorizontalDesireScale;
                case FeudalClaimFabricationTrack.Vassal:
                    return C.FeudalClaimFabricationAiVassalDesireScale;
                case FeudalClaimFabricationTrack.Reclamation:
                    return C.FeudalClaimFabricationAiReclamationDesireScale;
                default:
                    return 0f;
            }
        }

        private static float GetTrackDesireCap(FeudalClaimFabricationTrack track)
        {
            switch (track)
            {
                case FeudalClaimFabricationTrack.DeFacto:
                    return C.FeudalClaimFabricationAiDeFactoDesireCap;
                case FeudalClaimFabricationTrack.Liege:
                    return C.FeudalClaimFabricationAiLiegeDesireCap;
                case FeudalClaimFabricationTrack.Horizontal:
                    return C.FeudalClaimFabricationAiHorizontalDesireCap;
                case FeudalClaimFabricationTrack.Vassal:
                    return C.FeudalClaimFabricationAiVassalDesireCap;
                case FeudalClaimFabricationTrack.Reclamation:
                    return C.FeudalClaimFabricationAiReclamationDesireCap;
                default:
                    return 0f;
            }
        }

        private static bool IsValidAiFabricationClan(Clan clan, out string reason)
        {
            reason = null;
            if (clan == null)
            {
                reason = "clan is missing";
                return false;
            }

            if (clan == Clan.PlayerClan)
            {
                reason = "player clan is manual-only";
                return false;
            }

            if (clan.IsEliminated || clan.IsUnderMercenaryService || clan.IsMinorFaction || clan.Kingdom == null || clan.Kingdom.IsEliminated)
            {
                reason = "clan is not an active landed noble house";
                return false;
            }

            if (clan.Leader == null || clan.Leader.IsDead)
            {
                reason = "clan has no living leader";
                return false;
            }

            if (!string.IsNullOrWhiteSpace(clan.Kingdom.StringId) && clan.Kingdom.StringId.Contains("_rebels_"))
            {
                reason = "clan is in a temporary rebel kingdom";
                return false;
            }

            FactionManagerBehavior factionManager = Campaign.Current?.GetCampaignBehavior<FactionManagerBehavior>();
            if (factionManager != null && factionManager.IsClanOnActiveCivilWarRebelSide(clan, out _, out _))
            {
                reason = "clan is fighting an active civil war";
                return false;
            }

            return true;
        }

        private static int GetStableClanDayOffset(Clan clan, int interval)
        {
            int hash = clan?.StringId?.GetHashCode() ?? clan?.Name?.ToString()?.GetHashCode() ?? 0;
            return (hash & int.MaxValue) % Math.Max(1, interval);
        }

        private void AdvanceFabricationProgress(FeudalClaimFabricationRecord record, Clan clan, Hero fabricator, FeudalTitleRecord title, float progressDelta)
        {
            if (record == null || !record.IsActive || record.AwaitingSetback || progressDelta <= 0f)
                return;

            float oldProgress = record.Progress;
            record.AddProgress(progressDelta);
            CheckFabricationMilestones(record, clan, fabricator, title, oldProgress, record.Progress);
        }

        private void CheckFabricationMilestones(FeudalClaimFabricationRecord record, Clan clan, Hero fabricator, FeudalTitleRecord title, float oldProgress, float newProgress)
        {
            if (record == null || !record.IsActive)
                return;

            if (!record.HasPassed33 && oldProgress < 0.33f && newProgress >= 0.33f && HandleFabricationMilestone(record, clan, fabricator, title, 0.33f))
                return;

            if (!record.HasPassed66 && oldProgress < 0.66f && newProgress >= 0.66f && HandleFabricationMilestone(record, clan, fabricator, title, 0.66f))
                return;

            if (!record.HasPassed100 && oldProgress < 1f && newProgress >= 1f)
                HandleFabricationMilestone(record, clan, fabricator, title, 1f);
        }

        private bool HandleFabricationMilestone(FeudalClaimFabricationRecord record, Clan clan, Hero fabricator, FeudalTitleRecord title, float threshold)
        {
            record.MarkMilestonePassed(threshold);
            Clan legalHolder = ResolveTargetHolderClan(title);
            if (legalHolder == null || legalHolder == clan)
            {
                QueuePlayerStagePassed(record, clan, title, threshold);
                BellumCivileDebug.Trace("fabrication", $"milestone passed without a hostile detector; clan={clan?.StringId ?? "null"}; title={title?.TitleId ?? "null"}; threshold={threshold:0.00}.", requestInGameDisplay: true);
                return false;
            }

            Hero detector = legalHolder?.Leader;
            if (detector == null || detector.IsDead)
            {
                QueuePlayerStagePassed(record, clan, title, threshold);
                BellumCivileDebug.Trace("fabrication", $"milestone passed without detector; clan={clan?.StringId ?? "null"}; title={title?.TitleId ?? "null"}; threshold={threshold:0.00}.", requestInGameDisplay: true);
                return false;
            }

            int stage = threshold >= 1f ? 3 : threshold >= 0.66f ? 2 : 1;
            float discoveryChance = CalculateFabricationDiscoveryChance(fabricator, detector, stage, out int plotterScore, out int detectorScore);
            float roll = MBRandom.RandomFloat;
            bool discovered = roll < discoveryChance;
            BellumCivileDebug.Trace(
                "fabrication",
                $"milestone checked; clan={clan?.StringId ?? "null"}; hero={fabricator?.StringId ?? "null"}; title={title?.TitleId ?? "null"}; threshold={threshold:0.00}; plotter_skill={plotterScore}; detector={detector.StringId}; detector_skill={detectorScore}; discovery_chance={discoveryChance:P1}; roll={roll:P1}; discovered={discovered}.",
                requestInGameDisplay: true);

            if (!discovered)
            {
                QueuePlayerStagePassed(record, clan, title, threshold);
                return false;
            }

            record.SetProgress(threshold);
            int tier = threshold >= 1f ? 3 : threshold >= 0.66f ? 2 : 1;
            if (tier < 3)
            {
                record.BeginSetback(tier);
                return true;
            }
            record.MarkStageFailed(tier);
            NotificationHelper.ShowFeudalClaimFabricationDiscovered(tier, clan, legalHolder, fabricator, title);
            ApplyForgeryScandal(record, clan, legalHolder, fabricator, title, tier);
            return true;
        }

        private void ApplyForgeryScandal(FeudalClaimFabricationRecord record, Clan clan, Clan legalHolder, Hero fabricator, FeudalTitleRecord title, int tier)
        {
            record.SetActive(false);
            SetRetryCooldown(record, 2);

            Hero titleHolder = legalHolder?.Leader;
            Kingdom plotterKingdom = clan?.Kingdom;
            Kingdom targetKingdom = legalHolder?.Kingdom;
            bool foreignTarget = plotterKingdom != null && targetKingdom != null && plotterKingdom != targetKingdom;
            bool sovereignPlotter = plotterKingdom?.Leader == fabricator;

            if (foreignTarget)
            {
                ApplyRelation(fabricator, titleHolder, ScaleScandalPenalty(C.FeudalClaimFabricationForeignHolderScandalPenalty, tier));
                ApplyRelation(plotterKingdom?.Leader, targetKingdom?.Leader, ScaleScandalPenalty(C.FeudalClaimFabricationForeignRulerScandalPenalty, tier));
            }
            else if (sovereignPlotter)
            {
                ApplyRelation(fabricator, titleHolder, ScaleScandalPenalty(C.FeudalClaimFabricationSovereignHolderScandalPenalty, tier));
                if (tier >= 2)
                {
                    foreach (Clan domesticClan in plotterKingdom.Clans)
                    {
                        Hero lord = domesticClan?.Leader;
                        if (lord != null && lord != fabricator && lord.GetTraitLevel(DefaultTraits.Honor) > 0)
                            ApplyRelation(lord, fabricator, ScaleScandalPenalty(C.FeudalClaimFabricationSovereignHonorScandalPenalty, tier));
                    }
                }
            }
            else
            {
                ApplyRelation(fabricator, titleHolder, ScaleScandalPenalty(C.FeudalClaimFabricationDomesticHolderScandalPenalty, tier));
                ApplyRelation(fabricator, plotterKingdom?.Leader, ScaleScandalPenalty(C.FeudalClaimFabricationDomesticRulerScandalPenalty, tier));
            }

            BellumCivileDebug.Trace("fabrication", $"scandal exposed; clan={clan?.StringId ?? record.FabricatorClanId}; hero={fabricator?.StringId ?? record.FabricatorHeroId}; title={title?.TitleId ?? record.TargetTitleId}; tier={tier}; foreign={foreignTarget}; sovereign={sovereignPlotter}.", requestInGameDisplay: true);
            QueuePlayerFabricationExposed(record, clan, title);
        }

        private static int ScaleScandalPenalty(int basePenalty, int tier)
        {
            if (basePenalty >= 0)
                return basePenalty;

            float multiplier;
            switch (tier)
            {
                case 1:
                    multiplier = C.FeudalClaimFabricationTier1ScandalMultiplier;
                    break;
                case 2:
                    multiplier = C.FeudalClaimFabricationTier2ScandalMultiplier;
                    break;
                default:
                    multiplier = C.FeudalClaimFabricationTier3ScandalMultiplier;
                    break;
            }

            int scaled = (int)Math.Round(basePenalty * multiplier, MidpointRounding.AwayFromZero);
            return Math.Min(C.FeudalClaimFabricationMinimumScandalPenalty, scaled);
        }

        private static void ApplyRelation(Hero first, Hero second, int change)
        {
            if (first == null || second == null || first == second || change == 0)
                return;

            ChangeRelationAction.ApplyRelationChangeBetweenHeroes(first, second, change, false);
        }

        internal static float CalculateFabricationDiscoveryChance(Hero fabricator, Hero detector, int stage, out int plotterScore, out int detectorScore)
        {
            plotterScore = CalculateFabricationSkillScore(fabricator);
            detectorScore = CalculateFabricationSkillScore(detector);

            float skillDifference = detectorScore - plotterScore;
            float skillModifier = (skillDifference / C.FeudalClaimFabricationDiscoverySkillStep)
                * C.FeudalClaimFabricationDiscoveryChancePerSkillStep;
            float chance = BellumCivileOptions.FeudalClaimFabricationBaseDiscoveryChance + skillModifier;
            chance += Campaign.Current?.GetCampaignBehavior<PrivyCouncilBehavior>()?
                .GetFabricationDiscoveryChanceModifier(fabricator?.Clan, detector?.Clan) ?? 0f;
            return Math.Max(
                C.FeudalClaimFabricationMinimumDiscoveryChance,
                Math.Min(C.FeudalClaimFabricationMaximumDiscoveryChance, chance));
        }

        private static int CalculateFabricationStealthRoll(Hero hero)
        {
            return CalculateFabricationSkillScore(hero) + MBRandom.RandomInt(-C.FeudalClaimFabricationDetectionVariance, C.FeudalClaimFabricationDetectionVariance + 1);
        }

        private static int CalculateFabricationDetectionRoll(Hero hero)
        {
            return CalculateFabricationSkillScore(hero) + MBRandom.RandomInt(-C.FeudalClaimFabricationDetectionVariance, C.FeudalClaimFabricationDetectionVariance + 1);
        }

        private static int CalculateFabricationSkillScore(Hero hero)
        {
            return hero == null
                ? 0
                : hero.GetSkillValue(DefaultSkills.Steward) + hero.GetSkillValue(DefaultSkills.Roguery);
        }

        private static Clan ResolveTargetHolderClan(FeudalTitleRecord title)
        {
            return ResolveClan(title?.DeJureHolderClanId) ?? ResolveClan(title?.DeFactoHolderClanId);
        }

        private void CompleteFabrication(FeudalTitleBehavior titleBehavior, FeudalClaimFabricationRecord record, Clan clan, Hero fabricator, FeudalTitleRecord title)
        {
            if (titleBehavior == null || record == null || !record.IsActive || record.AwaitingSetback || clan == null || fabricator == null || title == null)
                return;

            titleBehavior.RegisterClaim(
                clan,
                title,
                FeudalClaimStrength.Weak,
                "fabricated_claim",
                fabricator,
                clan,
                carrierHero: fabricator,
                generationDepth: 0);

            record.SetActive(false);
            BellumCivileLogger.Log($"Feudal claim fabrication completed; clan={clan.StringId}; hero={fabricator.StringId}; title={title.TitleId}; track={record.Track}; weak_claim_registered=true.");
            NotificationHelper.ShowFeudalClaimFabricationCompleted(clan, ResolveClan(title.DeJureHolderClanId), fabricator, title);
            QueuePlayerFabricationCompleted(record, clan, title);
            Campaign.Current?.GetCampaignBehavior<FeudalPoliticalOptionsBehavior>()?.QueueClanForEvaluation(
                clan,
                "claim fabrication completed",
                C.FeudalClaimFabricationPoliticalFollowUpDays);
        }

        private void OnSettlementOwnerChanged(
            Settlement settlement,
            bool openToClaim,
            Hero newOwner,
            Hero oldOwner,
            Hero capturerHero,
            ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail detail)
        {
            ValidateFabricationsForClan(newOwner?.Clan, "settlement owner changed");
            ValidateFabricationsForClan(oldOwner?.Clan, "settlement owner changed");
        }

        private void OnClanChangedKingdom(Clan clan, Kingdom oldKingdom, Kingdom newKingdom, ChangeKingdomAction.ChangeKingdomActionDetail detail, bool showNotification)
        {
            ValidateFabricationsForClan(clan, "clan changed kingdom");
        }

        private void OnHeroKilled(Hero victim, Hero killer, KillCharacterAction.KillCharacterActionDetail detail, bool showNotification)
        {
            if (victim == null)
                return;

            FeudalTitleBehavior titleBehavior = Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>();
            titleBehavior?.DeactivateFabricatedClaimsByCarrier(victim);

            EnsureCollectionsInitialized();
            foreach (FeudalClaimFabricationRecord record in _fabrications
                .Where(record => record != null && record.IsActive && record.FabricatorHeroId == victim.StringId)
                .ToList())
            {
                CancelFabrication(record, ResolveClan(record.FabricatorClanId), titleBehavior?.GetTitle(record.TargetTitleId), "fabricator died", refundGold: true);
            }
        }

        private void ValidateFabricationsForClan(Clan clan, string reason)
        {
            if (clan == null)
                return;

            EnsureCollectionsInitialized();
            FeudalTitleBehavior titleBehavior = Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>();
            if (titleBehavior == null)
                return;

            foreach (FeudalClaimFabricationRecord record in _fabrications
                .Where(record => record != null && record.IsActive && record.FabricatorClanId == clan.StringId)
                .ToList())
            {
                Hero fabricator = ResolveHero(record.FabricatorHeroId);
                FeudalTitleRecord title = titleBehavior.GetTitle(record.TargetTitleId);
                if (!IsFabricationStillValid(titleBehavior, record, clan, fabricator, title, out string invalidReason))
                    CancelFabrication(record, clan, title, $"{reason}: {invalidReason}", refundGold: true);
            }
        }

        private bool IsFabricationStillValid(
            FeudalTitleBehavior titleBehavior,
            FeudalClaimFabricationRecord record,
            Clan clan,
            Hero fabricator,
            FeudalTitleRecord title,
            out string reason)
        {
            reason = null;

            if (record == null || !record.IsActive)
            {
                reason = "record inactive";
                return false;
            }

            if (titleBehavior == null)
            {
                reason = "title behavior unavailable";
                return false;
            }

            if (clan == null || clan.IsEliminated || clan.Leader == null || clan.Leader.IsDead)
            {
                reason = "clan invalid";
                return false;
            }

            if (fabricator == null || fabricator.IsDead || fabricator.Clan != clan)
            {
                reason = "fabricator invalid";
                return false;
            }

            if (title == null || !title.IsActive || title.IsDeliberatelyDissolved)
            {
                reason = "target title invalid";
                return false;
            }

            if (title.DeJureHolderClanId == clan.StringId)
            {
                reason = "claim already legalized";
                return false;
            }

            if (titleBehavior.HasActiveClaim(clan, title))
            {
                reason = "claim already exists";
                return false;
            }

            return IsTrackStillValid(titleBehavior, clan, title, record.Track, out reason);
        }

        public void RevalidateAfterReorganization()
        {
            var titles = Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>();
            if (titles == null) return;
            // A changed hierarchy can invalidate a liege or vassal track on another title.
            foreach (var record in GetActiveFabrications().ToList())
            {
                var title = titles.GetTitle(record.TargetTitleId);
                var clan = ResolveClan(record.FabricatorClanId);
                var hero = ResolveHero(record.FabricatorHeroId);
                if (!IsFabricationStillValid(titles, record, clan, hero, title, out string reason))
                    CancelFabrication(record, clan, title, reason, refundGold: true);
            }
        }

        private void CancelFabrication(FeudalClaimFabricationRecord record, Clan clan, FeudalTitleRecord title, string reason, bool refundGold)
        {
            if (record == null || !record.IsActive)
                return;

            record.SetActive(false);

            int refund = 0;
            if (refundGold && record.PaidGoldCost > 0 && clan?.Leader != null && !clan.Leader.IsDead)
            {
                refund = Math.Max(0, (int)Math.Round(record.PaidGoldCost * C.FeudalClaimFabricationGoldRefundShare));
                if (refund > 0)
                    GiveGoldAction.ApplyBetweenCharacters(null, clan.Leader, refund, true);
            }

            BellumCivileLogger.Log($"Feudal claim fabrication cancelled; clan={record.FabricatorClanId}; hero={record.FabricatorHeroId}; title={record.TargetTitleId}; progress={record.Progress:0.00}; refund_gold={refund}; reason={reason ?? "unknown"}.");
            QueuePlayerFabricationCancelled(record, clan, title, reason, refund);
        }

        private void QueuePlayerStagePassed(FeudalClaimFabricationRecord record, Clan clan, FeudalTitleRecord title, float threshold)
        {
            if (!BelongsToPlayerClan(record, clan) || threshold >= 1f)
                return;
            int stage = threshold < 0.66f ? 1 : 2;
            TextObject body = stage == 1
                ? new TextObject("{=BC_Fabrication_Stage1Passed}Your agents report that forgotten grants and legal precedents concerning the {TITLE_NAME} have been gathered without arousing suspicion. Their work now turns to the disputed charters.")
                : new TextObject("{=BC_Fabrication_Stage2Passed}The disputed charters concerning the {TITLE_NAME} have been prepared, and their inconsistencies have escaped scrutiny. Your scribes now undertake the final letters patent.");
            body.SetTextVariable("TITLE_NAME", GetOutcomeTitleName(title));
            _pendingPlayerOutcomes.Add(new PendingFabricationOutcome(FeudalFabricationStagePreview.GetName(stage).ToString(), body.ToString()));
        }

        private void QueuePlayerFabricationCompleted(FeudalClaimFabricationRecord record, Clan clan, FeudalTitleRecord title)
        {
            if (!BelongsToPlayerClan(record, clan))
                return;

            TextObject header = new TextObject("{=BC_Fabrication_Result_Completed_Title}A Claim Forged");
            TextObject body = new TextObject("{=BC_Fabrication_Result_Completed_Desc}Your agents have completed their work concerning the {TITLE_NAME}. A collection of disputed charters and legal precedents now supports your house's claim to the title.\n\nThe {CLAN_NAME} has gained a weak claim to the {TITLE_NAME}.");
            body.SetTextVariable("TITLE_NAME", GetOutcomeTitleName(title));
            body.SetTextVariable("CLAN_NAME", clan?.Name ?? Clan.PlayerClan?.Name ?? new TextObject("?"));
            _pendingPlayerOutcomes.Add(new PendingFabricationOutcome(header.ToString(), body.ToString()));
        }

        private void QueuePlayerFabricationExposed(FeudalClaimFabricationRecord record, Clan clan, FeudalTitleRecord title)
        {
            if (!BelongsToPlayerClan(record, clan))
                return;

            TextObject header = new TextObject("{=BC_Fabrication_Result_Exposed_Title}Forgery Exposed");
            TextObject body = new TextObject("{=BC_Fabrication_Result_Exposed_Desc}Your agents' work concerning the {TITLE_NAME} has been uncovered. The documents have been denounced as false, the fabrication has failed, and the scandal has damaged your standing with those involved.");
            body.SetTextVariable("TITLE_NAME", GetOutcomeTitleName(title));
            TextObject detail = record.DidStageFail(1)
                ? new TextObject("{=BC_Fabrication_Stage1Failed}Your agents' inquiries into forgotten grants have attracted the attention of the title holder. The supposed legal precedents have been exposed as a pretext.")
                : record.DidStageFail(2)
                    ? new TextObject("{=BC_Fabrication_Stage2Failed}Scrutiny of the disputed charters has revealed inconsistencies that your agents could not explain.")
                    : new TextObject("{=BC_Fabrication_Stage3Failed}The final letters patent have been examined and denounced as forgeries before your claim could be established.");
            var retry = new TextObject("{=BC_Fabrication_ExposureWait}Your house must let the scandal fade for two years before attempting another forgery for this title.");
            _pendingPlayerOutcomes.Add(new PendingFabricationOutcome(header.ToString(), detail.ToString() + "\n\n" + body.ToString() + "\n\n" + retry.ToString()));
        }

        private void QueuePlayerFabricationCancelled(
            FeudalClaimFabricationRecord record,
            Clan clan,
            FeudalTitleRecord title,
            string reason,
            int refund)
        {
            if (!BelongsToPlayerClan(record, clan))
                return;

            string normalizedReason = reason ?? string.Empty;
            TextObject header;
            TextObject body;
            if (normalizedReason.IndexOf("claim already legalized", StringComparison.OrdinalIgnoreCase) >= 0
                || normalizedReason.IndexOf("claim already exists", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                header = new TextObject("{=BC_Fabrication_Result_Unneeded_Title}Fabrication No Longer Required");
                body = new TextObject("{=BC_Fabrication_Result_Unneeded_Desc}Your agents have abandoned their work concerning the {TITLE_NAME}. Your house now possesses a lawful interest in the title, making further fabrication unnecessary.");
            }
            else if (normalizedReason.IndexOf("fabricator died", StringComparison.OrdinalIgnoreCase) >= 0
                || normalizedReason.IndexOf("fabricator invalid", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                header = new TextObject("{=BC_Fabrication_Result_Abandoned_Title}Fabrication Abandoned");
                body = new TextObject("{=BC_Fabrication_Result_FabricatorLost_Desc}The work concerning the {TITLE_NAME} has been abandoned after the loss of the agent directing it. The surviving conspirators have dispersed before their activities can be uncovered.");
            }
            else if (normalizedReason.IndexOf("target title invalid", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                header = new TextObject("{=BC_Fabrication_Result_Abandoned_Title}Fabrication Abandoned");
                body = new TextObject("{=BC_Fabrication_Result_TargetLost_Desc}Your agents have abandoned their work because the {TITLE_NAME} is no longer recognized as a valid title. The gathered documents can no longer support a useful claim.");
            }
            else
            {
                header = new TextObject("{=BC_Fabrication_Result_Abandoned_Title}Fabrication Abandoned");
                body = new TextObject("{=BC_Fabrication_Result_CircumstancesChanged_Desc}Political circumstances surrounding the {TITLE_NAME} have changed, leaving your agents without a credible path to complete the fabrication. The work has been abandoned.");
            }

            body.SetTextVariable("TITLE_NAME", GetOutcomeTitleName(title));
            string resolvedBody = body.ToString();
            if (refund > 0)
            {
                TextObject refundText = new TextObject("{=BC_Fabrication_Result_Refund}Of the original expense, {GOLD_AMOUNT} denars have been recovered.");
                refundText.SetTextVariable("GOLD_AMOUNT", refund);
                resolvedBody += "\n\n" + refundText.ToString();
            }

            _pendingPlayerOutcomes.Add(new PendingFabricationOutcome(header.ToString(), resolvedBody));
        }

        private static bool BelongsToPlayerClan(FeudalClaimFabricationRecord record, Clan clan)
        {
            Clan playerClan = Clan.PlayerClan;
            return playerClan != null
                && (clan == playerClan || string.Equals(record?.FabricatorClanId, playerClan.StringId, StringComparison.Ordinal));
        }

        private static TextObject GetOutcomeTitleName(FeudalTitleRecord title)
        {
            return title != null
                ? FormatFeudalTitleText(title)
                : new TextObject("{=BC_Fabrication_Result_UnknownTitle}the disputed title");
        }

        private static bool TryGetFabricationTrack(
            FeudalTitleBehavior titleBehavior,
            Clan clan,
            FeudalTitleRecord targetTitle,
            out FeudalClaimFabricationTrack track,
            out string reason,
            IReadOnlyCollection<FeudalTitleRecord> heldTitles = null)
        {
            track = FeudalClaimFabricationTrack.DeFacto;
            reason = null;

            if (IsDeFactoTrackValid(clan, targetTitle))
            {
                track = FeudalClaimFabricationTrack.DeFacto;
                return true;
            }

            if (IsVassalTrackValid(titleBehavior, clan, targetTitle))
            {
                track = FeudalClaimFabricationTrack.Vassal;
                return true;
            }

            if (IsReclamationTrackValid(titleBehavior, clan, targetTitle))
            {
                track = FeudalClaimFabricationTrack.Reclamation;
                return true;
            }

            if (IsLiegeTrackValid(titleBehavior, clan, targetTitle, heldTitles))
            {
                track = FeudalClaimFabricationTrack.Liege;
                return true;
            }

            if (IsHorizontalTrackValid(titleBehavior, clan, targetTitle, heldTitles))
            {
                track = FeudalClaimFabricationTrack.Horizontal;
                return true;
            }

            reason = "target is not de facto held, same-rank neighboring, immediate liege, or immediate subordinate title";
            return false;
        }

        private static bool IsTrackStillValid(FeudalTitleBehavior titleBehavior, Clan clan, FeudalTitleRecord targetTitle, FeudalClaimFabricationTrack track, out string reason)
        {
            reason = null;
            bool valid;
            switch (track)
            {
                case FeudalClaimFabricationTrack.DeFacto:
                    valid = IsDeFactoTrackValid(clan, targetTitle);
                    break;
                case FeudalClaimFabricationTrack.Liege:
                    valid = IsLiegeTrackValid(titleBehavior, clan, targetTitle);
                    break;
                case FeudalClaimFabricationTrack.Horizontal:
                    valid = IsHorizontalTrackValid(titleBehavior, clan, targetTitle);
                    break;
                case FeudalClaimFabricationTrack.Vassal:
                case FeudalClaimFabricationTrack.Reclamation:
                    // The legal basis is the same immediate parent title. Do not discard years
                    // of fabrication merely because the subordinate crosses a realm boundary.
                    valid = IsVassalTrackValid(titleBehavior, clan, targetTitle)
                        || IsReclamationTrackValid(titleBehavior, clan, targetTitle);
                    break;
                default:
                    valid = false;
                    break;
            }

            if (!valid)
                reason = $"{track} path invalid";
            return valid;
        }

        private static bool IsDeFactoTrackValid(Clan clan, FeudalTitleRecord targetTitle)
        {
            return clan != null
                && targetTitle != null
                && targetTitle.DeFactoHolderClanId == clan.StringId
                && targetTitle.DeJureHolderClanId != clan.StringId;
        }

        private static bool IsLiegeTrackValid(
            FeudalTitleBehavior titleBehavior,
            Clan clan,
            FeudalTitleRecord targetTitle,
            IReadOnlyCollection<FeudalTitleRecord> heldTitles = null)
        {
            if (titleBehavior == null || clan == null || targetTitle == null)
                return false;

            IReadOnlyCollection<FeudalTitleRecord> effectiveHeldTitles = heldTitles ?? GetHeldTitles(titleBehavior, clan);
            if (targetTitle.TitleType == FeudalTitleType.Barony)
                return IsLandlessRealmBaronyTrackValid(clan, targetTitle, effectiveHeldTitles);

            return effectiveHeldTitles
                .Any(heldTitle => heldTitle != null && heldTitle.ParentTitleId == targetTitle.TitleId);
        }

        private static bool IsVassalTrackValid(
            FeudalTitleBehavior titleBehavior,
            Clan clan,
            FeudalTitleRecord targetTitle)
        {
            if (!TryGetImmediateSubordinateContext(
                    titleBehavior,
                    clan,
                    targetTitle,
                    out FeudalTitleRecord legalParent,
                    out Clan currentHolder))
            {
                return false;
            }

            if (clan.Kingdom == null
                || currentHolder.Kingdom != clan.Kingdom
                || legalParent.DeFactoHolderClanId != clan.StringId)
            {
                return false;
            }

            FeudalTitleRecord effectiveParent = titleBehavior.GetParentTitle(targetTitle, FeudalHierarchyMode.DeFacto);
            return effectiveParent != null
                && effectiveParent.TitleId == legalParent.TitleId
                && effectiveParent.DeFactoHolderClanId == clan.StringId;
        }

        private static bool IsReclamationTrackValid(
            FeudalTitleBehavior titleBehavior,
            Clan clan,
            FeudalTitleRecord targetTitle)
        {
            if (!TryGetImmediateSubordinateContext(
                    titleBehavior,
                    clan,
                    targetTitle,
                    out _,
                    out Clan currentHolder))
            {
                return false;
            }

            Kingdom claimantKingdom = clan.Kingdom;
            Kingdom holderKingdom = currentHolder.Kingdom;
            return claimantKingdom != null
                && holderKingdom != null
                && claimantKingdom != holderKingdom
                && !BellumKingdomVisibilityHelper.IsTemporaryBellumKingdom(claimantKingdom)
                && !BellumKingdomVisibilityHelper.IsTemporaryBellumKingdom(holderKingdom);
        }

        private static bool TryGetImmediateSubordinateContext(
            FeudalTitleBehavior titleBehavior,
            Clan clan,
            FeudalTitleRecord targetTitle,
            out FeudalTitleRecord legalParent,
            out Clan currentHolder)
        {
            legalParent = null;
            currentHolder = null;
            if (titleBehavior == null
                || clan == null
                || targetTitle == null
                || targetTitle.DeJureHolderClanId == clan.StringId)
            {
                return false;
            }

            legalParent = titleBehavior.GetParentTitle(targetTitle, FeudalHierarchyMode.DeJure);
            if (legalParent == null
                || !legalParent.IsActive
                || legalParent.DeJureHolderClanId != clan.StringId)
            {
                return false;
            }

            currentHolder = ResolveClan(targetTitle.DeFactoHolderClanId);
            return currentHolder != null
                && currentHolder != clan
                && !currentHolder.IsEliminated;
        }

        private static bool IsLandlessRealmBaronyTrackValid(Clan clan, FeudalTitleRecord targetTitle, IReadOnlyCollection<FeudalTitleRecord> heldTitles)
        {
            return clan?.Kingdom != null
                && targetTitle != null
                && targetTitle.TitleType == FeudalTitleType.Barony
                && (heldTitles == null || heldTitles.Count == 0)
                && targetTitle.DeJureHolderClanId != clan.StringId
                && string.Equals(targetTitle.AssociatedKingdomId, clan.Kingdom.StringId, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsHorizontalTrackValid(
            FeudalTitleBehavior titleBehavior,
            Clan clan,
            FeudalTitleRecord targetTitle,
            IReadOnlyCollection<FeudalTitleRecord> heldTitles = null)
        {
            if (titleBehavior == null || clan == null || targetTitle == null)
                return false;

            return (heldTitles ?? GetHeldTitles(titleBehavior, clan))
                .Where(heldTitle => heldTitle != null && heldTitle.TitleType == targetTitle.TitleType)
                .Any(heldTitle => AreTitlesAdjacent(titleBehavior, heldTitle, targetTitle));
        }

        private static List<FeudalTitleRecord> GetHeldTitles(FeudalTitleBehavior titleBehavior, Clan clan)
        {
            if (titleBehavior == null || clan == null)
                return new List<FeudalTitleRecord>();

            return titleBehavior.GetTitlesHeldByClan(clan, deJure: true)
                .Concat(titleBehavior.GetTitlesHeldByClan(clan, deJure: false))
                .Where(title => title != null && title.IsActive)
                .GroupBy(title => title.TitleId)
                .Select(group => group.First())
                .ToList();
        }

        private static bool AreTitlesAdjacent(FeudalTitleBehavior titleBehavior, FeudalTitleRecord firstTitle, FeudalTitleRecord secondTitle)
        {
            return titleBehavior != null && titleBehavior.AreTitlesAdjacentForTitleLogic(firstTitle, secondTitle);
        }

        private static float CalculateDailyProgressDelta(Hero fabricator)
        {
            float baseDays = Math.Max(1f, GetCampaignDaysInYear() * BellumCivileOptions.FeudalClaimFabricationBaseYears);
            float delta = 1f / baseDays;
            if (fabricator == null)
                return delta;

            delta += (fabricator.GetSkillValue(DefaultSkills.Steward) + fabricator.GetSkillValue(DefaultSkills.Roguery))
                * C.FeudalClaimFabricationSkillDailyScale;

            int calculating = fabricator.GetTraitLevel(DefaultTraits.Calculating);
            if (calculating >= 2)
                delta *= C.FeudalClaimFabricationCalculating2Multiplier;
            else if (calculating == 1)
                delta *= C.FeudalClaimFabricationCalculating1Multiplier;

            int honor = fabricator.GetTraitLevel(DefaultTraits.Honor);
            if (honor <= -2)
                delta *= C.FeudalClaimFabricationDeceitfulMultiplier;
            else if (honor == -1)
                delta *= C.FeudalClaimFabricationDeviousMultiplier;
            else if (honor == 1)
                delta *= C.FeudalClaimFabricationHonestMultiplier;
            else if (honor >= 2)
                delta *= C.FeudalClaimFabricationHonorableMultiplier;

            delta *= Campaign.Current?.GetCampaignBehavior<PrivyCouncilBehavior>()?
                .GetFabricationProgressMultiplier(fabricator.Clan) ?? 1f;

            return Math.Max(0.0001f, delta);
        }

        public static int GetFabricationGoldCost(FeudalTitleType titleType)
        {
            switch (titleType)
            {
                case FeudalTitleType.Barony:
                    return C.FeudalClaimFabricationBaronyGoldCost;
                case FeudalTitleType.County:
                    return C.FeudalClaimFabricationCountyGoldCost;
                case FeudalTitleType.Duchy:
                    return C.FeudalClaimFabricationDuchyGoldCost;
                case FeudalTitleType.Kingdom:
                    return C.FeudalClaimFabricationKingdomGoldCost;
                case FeudalTitleType.Empire:
                    return C.FeudalClaimFabricationEmpireGoldCost;
                default:
                    return 0;
            }
        }

        public static float GetFabricationInfluenceCost(FeudalTitleType titleType)
        {
            switch (titleType)
            {
                case FeudalTitleType.Barony:
                    return C.FeudalClaimFabricationBaronyInfluenceCost;
                case FeudalTitleType.County:
                    return C.FeudalClaimFabricationCountyInfluenceCost;
                case FeudalTitleType.Duchy:
                    return C.FeudalClaimFabricationDuchyInfluenceCost;
                case FeudalTitleType.Kingdom:
                    return C.FeudalClaimFabricationKingdomInfluenceCost;
                case FeudalTitleType.Empire:
                    return C.FeudalClaimFabricationEmpireInfluenceCost;
                default:
                    return 0f;
            }
        }

        private static string BuildFabricationId(Clan clan, FeudalTitleRecord title)
        {
            return $"fabricate_{clan?.StringId ?? "unknown"}_{title?.TitleId ?? "unknown"}_{(int)CurrentDay}";
        }

        private void EnsureCollectionsInitialized()
        {
            if (_fabrications == null)
                _fabrications = new List<FeudalClaimFabricationRecord>();
        }

        private static TextObject FormatFeudalTitleText(FeudalTitleRecord title)
        {
            string displayName = FeudalTitleDisplayHelper.FormatTitleName(title);
            if (string.IsNullOrWhiteSpace(displayName))
                displayName = title?.TitleId ?? "?";

            return new TextObject("{=!}" + displayName);
        }

        private static float CurrentDay => (float)CampaignTime.Now.ToDays;

        private static Clan ResolveClan(string clanId)
        {
            return string.IsNullOrWhiteSpace(clanId)
                ? null
                : Clan.All.FirstOrDefault(clan => clan != null && clan.StringId == clanId);
        }

        private static Hero ResolveHero(string heroId)
        {
            return string.IsNullOrWhiteSpace(heroId)
                ? null
                : Hero.AllAliveHeroes.FirstOrDefault(hero => hero != null && hero.StringId == heroId);
        }

        private sealed class AiFabricationCandidate
        {
            public AiFabricationCandidate(FeudalTitleRecord title, FeudalClaimFabricationTrack track, float score, List<string> reasons)
            {
                Title = title;
                Track = track;
                Score = score;
                Reasons = reasons ?? new List<string>();
            }

            public FeudalTitleRecord Title { get; }
            public FeudalClaimFabricationTrack Track { get; }
            public float Score { get; }
            public List<string> Reasons { get; }
        }

        public sealed class PendingFabricationOutcome
        {
            public PendingFabricationOutcome(string title, string body)
            {
                Title = title ?? string.Empty;
                Body = body ?? string.Empty;
            }

            [SaveableProperty(1)] public string Title { get; private set; }
            [SaveableProperty(2)] public string Body { get; private set; }
        }
    }
}
