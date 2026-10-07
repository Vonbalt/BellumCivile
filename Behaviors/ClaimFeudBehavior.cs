using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using C = BellumCivile.BellumCivileConstants;
using O = BellumCivile.BellumCivileOptions;

namespace BellumCivile.Behaviors
{
    /// <summary>
    /// First-pass foundation for private title feuds. This behavior tracks claim pressure
    /// between clans, but does not yet move clans into temporary feud kingdoms.
    /// </summary>
    public partial class ClaimFeudBehavior : CampaignBehaviorBase
    {
        private List<ClaimFeudRecord> _feuds = new List<ClaimFeudRecord>();
        private readonly HashSet<string> _shownPlayerRulingInquiries = new HashSet<string>();
        private readonly HashSet<string> _shownPlayerRulerJudgmentInquiries = new HashSet<string>();
        private readonly HashSet<string> _shownPlayerSupportInquiries = new HashSet<string>();
        private readonly HashSet<string> _shownPlayerRevocationDemandInquiries = new HashSet<string>();
        private int _yearlyEvaluations;
        private int _yearlyNoActionableCandidate;
        private int _yearlyBelowThreshold;
        private int _yearlyPressableCandidates;
        private int _yearlyStartRollDeclined;
        private int _yearlyAgitationsStarted;
        private int _yearlyAgitationsReactivated;
        private int _yearlyPassed33;
        private int _yearlyPassed66;
        private int _yearlyPetitionsQueued;
        private int _yearlyPetitionsJudged;
        private int _yearlyPeacefulSettlements;
        private int _yearlyDefiedToWar;
        private int _yearlyCivilWarInterruptions;
        private int _yearlyPauses;
        private int _yearlyResumes;
        private int _yearlyInvalidated;

        private const string PlayerRulerChoiceClaimant = "claimant";
        private const string PlayerRulerChoiceHolder = "holder";
        private const string PlayerRulerChoiceAbstain = "abstain";
        private const string PlayerRulerChoiceSuppress = "suppress";
        private const string PlayerRevocationChoiceSubmit = "submit";
        private const string PlayerRevocationChoiceDefy = "defy";

        public override void RegisterEvents()
        {
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
            CampaignEvents.OnClanDestroyedEvent.AddNonSerializedListener(this, OnClanDestroyed);
            CampaignEvents.OnClanChangedKingdomEvent.AddNonSerializedListener(this, OnClanChangedKingdom);
        }

        public override void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData("BellumCivile_ClaimFeuds", ref _feuds);
            dataStore.SyncData("BellumCivile_ClaimFeudYearlyEvaluations", ref _yearlyEvaluations);
            dataStore.SyncData("BellumCivile_ClaimFeudYearlyNoActionableCandidate", ref _yearlyNoActionableCandidate);
            dataStore.SyncData("BellumCivile_ClaimFeudYearlyBelowThreshold", ref _yearlyBelowThreshold);
            dataStore.SyncData("BellumCivile_ClaimFeudYearlyPressableCandidates", ref _yearlyPressableCandidates);
            dataStore.SyncData("BellumCivile_ClaimFeudYearlyStartRollDeclined", ref _yearlyStartRollDeclined);
            dataStore.SyncData("BellumCivile_ClaimFeudYearlyAgitationsStarted", ref _yearlyAgitationsStarted);
            dataStore.SyncData("BellumCivile_ClaimFeudYearlyAgitationsReactivated", ref _yearlyAgitationsReactivated);
            dataStore.SyncData("BellumCivile_ClaimFeudYearlyPassed33", ref _yearlyPassed33);
            dataStore.SyncData("BellumCivile_ClaimFeudYearlyPassed66", ref _yearlyPassed66);
            dataStore.SyncData("BellumCivile_ClaimFeudYearlyPetitionsQueued", ref _yearlyPetitionsQueued);
            dataStore.SyncData("BellumCivile_ClaimFeudYearlyPetitionsJudged", ref _yearlyPetitionsJudged);
            dataStore.SyncData("BellumCivile_ClaimFeudYearlyPeacefulSettlements", ref _yearlyPeacefulSettlements);
            dataStore.SyncData("BellumCivile_ClaimFeudYearlyDefiedToWar", ref _yearlyDefiedToWar);
            dataStore.SyncData("BellumCivile_ClaimFeudYearlyCivilWarInterruptions", ref _yearlyCivilWarInterruptions);
            dataStore.SyncData("BellumCivile_ClaimFeudYearlyPauses", ref _yearlyPauses);
            dataStore.SyncData("BellumCivile_ClaimFeudYearlyResumes", ref _yearlyResumes);
            dataStore.SyncData("BellumCivile_ClaimFeudYearlyInvalidated", ref _yearlyInvalidated);
            EnsureCollectionsInitialized();
        }

        public ClaimFeudLifecycleYearlyTelemetry ConsumeYearlyTelemetry()
        {
            ClaimFeudLifecycleYearlyTelemetry result = new ClaimFeudLifecycleYearlyTelemetry(
                _yearlyEvaluations,
                _yearlyNoActionableCandidate,
                _yearlyBelowThreshold,
                _yearlyPressableCandidates,
                _yearlyStartRollDeclined,
                _yearlyAgitationsStarted,
                _yearlyAgitationsReactivated,
                _yearlyPassed33,
                _yearlyPassed66,
                _yearlyPetitionsQueued,
                _yearlyPetitionsJudged,
                _yearlyPeacefulSettlements,
                _yearlyDefiedToWar,
                _yearlyCivilWarInterruptions,
                _yearlyPauses,
                _yearlyResumes,
                _yearlyInvalidated);

            _yearlyEvaluations = 0;
            _yearlyNoActionableCandidate = 0;
            _yearlyBelowThreshold = 0;
            _yearlyPressableCandidates = 0;
            _yearlyStartRollDeclined = 0;
            _yearlyAgitationsStarted = 0;
            _yearlyAgitationsReactivated = 0;
            _yearlyPassed33 = 0;
            _yearlyPassed66 = 0;
            _yearlyPetitionsQueued = 0;
            _yearlyPetitionsJudged = 0;
            _yearlyPeacefulSettlements = 0;
            _yearlyDefiedToWar = 0;
            _yearlyCivilWarInterruptions = 0;
            _yearlyPauses = 0;
            _yearlyResumes = 0;
            _yearlyInvalidated = 0;
            return result;
        }

        public IReadOnlyList<ClaimFeudRecord> GetActiveFeuds()
        {
            EnsureCollectionsInitialized();
            return _feuds
                .Where(record => record != null
                              && IsActiveFeudState(record.State))
                .ToList();
        }

        public IReadOnlyList<ClaimFeudRecord> GetVisibleFeuds(Kingdom kingdom)
        {
            EnsureCollectionsInitialized();
            if (kingdom == null)
                return new List<ClaimFeudRecord>();

            return _feuds
                .Where(record => record != null
                              && IsVisibleFeudState(record.State)
                              && IsFeudInKingdom(record, kingdom))
                .ToList();
        }

        public bool TryPetitionFeud(string recordId, Clan petitioner, out string report)
        {
            EnsureCollectionsInitialized();
            report = null;

            FeudalTitleBehavior titleBehavior = Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>();
            if (titleBehavior == null)
            {
                report = new TextObject("{=BC_ClaimFeud_ReportTitleUnavailable}The title system is unavailable.").ToString();
                return false;
            }

            if (string.IsNullOrWhiteSpace(recordId) || petitioner == null)
            {
                report = new TextObject("{=BC_ClaimFeud_ReportMissingPetition}The feud or petitioner is missing.").ToString();
                return false;
            }

            ClaimFeudRecord record = _feuds.FirstOrDefault(feud =>
                feud != null && feud.RecordId == recordId && IsActiveFeudState(feud.State));
            if (record == null)
            {
                report = new TextObject("{=BC_ClaimFeud_ReportInactive}The feud is no longer active.").ToString();
                return false;
            }

            if (record.ClaimantClanId != petitioner.StringId)
            {
                report = new TextObject("{=BC_ClaimFeud_ReportClaimantOnly}Only the claimant side can petition the ruler.").ToString();
                return false;
            }

            if (record.State == ClaimFeudState.WarActive || record.State == ClaimFeudState.DefiedPendingWar)
            {
                report = new TextObject("{=BC_ClaimFeud_ReportOpenWar}The feud has already become an open war and can no longer be petitioned through the ordinary court process.").ToString();
                return false;
            }

            if (record.State != ClaimFeudState.Agitating
                && record.State != ClaimFeudState.PetitionReady
                && record.State != ClaimFeudState.Paused)
            {
                report = new TextObject("{=BC_ClaimFeud_ReportPetitionPending}The dispute is already awaiting judgment or a response.").ToString();
                return false;
            }

            if (record.Pressure < C.DiscontentTrigger * 0.8f && record.State != ClaimFeudState.PetitionReady)
            {
                report = new TextObject("{=BC_ClaimFeud_ReportLowIntensity}Feud intensity is not high enough to petition the ruler.").ToString();
                return false;
            }

            Clan claimant = ResolveClan(record.ClaimantClanId);
            Clan holder = ResolveClan(record.HolderClanId);
            FeudalTitleRecord title = titleBehavior.GetTitle(record.TargetTitleId);
            if (!IsRecordStillValid(titleBehavior, record, claimant, holder, title, out string invalidReason))
            {
                record.SetState(ClaimFeudState.Resolved);
                record.SetDebugReason($"player petition resolved invalid: {invalidReason}");
                report = new TextObject("{=BC_ClaimFeud_ReportInvalid}The feud is no longer legally valid.").ToString();
                return false;
            }

            record.SetState(ClaimFeudState.PetitionReady);
            record.SetLastTickDay(CurrentDay);
            ResolvePetition(titleBehavior, record, claimant, holder, title);
            report = new TextObject("{=BC_ClaimFeud_ReportPetitionSent}Petition sent.").ToString();
            return true;
        }

        public bool TrySuppressByRoyalPeace(string recordId, Clan enforcingClan, out string report)
        {
            EnsureCollectionsInitialized();
            report = null;

            if (string.IsNullOrWhiteSpace(recordId))
            {
                report = new TextObject("{=BC_ClaimFeud_ReportMissingFeud}The feud is missing.").ToString();
                return false;
            }

            ClaimFeudRecord record = _feuds.FirstOrDefault(feud =>
                feud != null && feud.RecordId == recordId && IsActiveFeudState(feud.State));
            if (record == null)
            {
                report = new TextObject("{=BC_ClaimFeud_ReportInactive}The feud is no longer active.").ToString();
                return false;
            }

            Kingdom kingdom = RealmPeaceEnforcementBehavior.ResolveKingdomForFeud(record);
            if (kingdom == null || kingdom.IsEliminated || enforcingClan == null || kingdom.RulingClan != enforcingClan)
            {
                report = new TextObject("{=BC_ClaimFeud_ReportRulerPeaceOnly}Only the ruler of the feud's realm can enforce peace.").ToString();
                return false;
            }

            var wars = Campaign.Current?.GetCampaignBehavior<ClaimFeudWarBehavior>();
            if (record.State == ClaimFeudState.WarActive || wars?.HasActiveWarForFeud(record.RecordId) == true)
            {
                if (wars == null)
                {
                    report = "The armed feud could not be found and was not altered.";
                    return false;
                }
                return wars.TryEnforceRoyalPeace(record, enforcingClan, out report);
            }

            record.SetState(ClaimFeudState.SuppressedCooldown);
            record.SetCooldownUntilDay(CurrentDay + GetCampaignDaysInYear() * C.ClaimFeudCooldownYears);
            record.SetDebugReason($"{record.DebugReason}; suppressed by royal peace");
            report = new TextObject("{=BC_ClaimFeud_ReportSuppressed}The feud was suppressed by the realm's peace.").ToString();
            return true;
        }

        public int CooldownFeudsForCivilWarParticipants(IEnumerable<Clan> clans, string reason)
        {
            EnsureCollectionsInitialized();
            HashSet<string> clanIds = new HashSet<string>(
                (clans ?? Enumerable.Empty<Clan>())
                .Where(clan => clan != null && !string.IsNullOrWhiteSpace(clan.StringId))
                .Select(clan => clan.StringId));

            if (clanIds.Count == 0)
                return 0;

            int affected = 0;
            foreach (ClaimFeudRecord record in _feuds.Where(record => record != null && IsActiveFeudState(record.State)).ToList())
            {
                if (record.State == ClaimFeudState.WarActive)
                    continue;

                bool claimantDrafted = clanIds.Contains(record.ClaimantClanId);
                bool holderDrafted = clanIds.Contains(record.HolderClanId);
                if (claimantDrafted || holderDrafted)
                {
                    if (record.State != ClaimFeudState.Paused)
                        _yearlyCivilWarInterruptions++;
                    PauseFeudForConflict(record, reason);
                    affected++;
                    continue;
                }

                if (RemoveCivilWarSupporters(record, clanIds))
                    affected++;
            }

            if (affected > 0)
            {
                BellumCivileLogger.Log($"Claim feuds paused/trimmed for civil war participants; affected={affected}; reason={reason ?? "unknown"}.");
                BellumCivileDebug.TraceIfEnabled(
                    "claim feud",
                    $"Claim feuds paused/trimmed for civil war participants; affected={affected}; reason={reason ?? "unknown"}.",
                    requestInGameDisplay: true);
            }

            return affected;
        }

        public bool IsClanOccupiedByClaimFeud(Clan clan)
        {
            if (clan == null || string.IsNullOrWhiteSpace(clan.StringId))
                return false;

            return GetActiveFeuds().Any(record =>
                record.ClaimantClanId == clan.StringId || record.HolderClanId == clan.StringId);
        }

        public bool HasActiveDisputeForTitle(string titleId)
        {
            if (string.IsNullOrWhiteSpace(titleId))
                return false;

            return GetActiveFeuds().Any(record =>
                string.Equals(record.TargetTitleId, titleId, StringComparison.Ordinal));
        }

        public bool IsClanCommittedToArmedClaimFeud(Clan clan, string excludingFeudRecordId = null)
        {
            if (clan == null || string.IsNullOrWhiteSpace(clan.StringId))
                return false;

            ClaimFeudWarBehavior warBehavior = Campaign.Current?.GetCampaignBehavior<ClaimFeudWarBehavior>();
            return warBehavior?.IsClanCommittedToActiveWar(clan, excludingFeudRecordId) == true;
        }

        public void PauseFeudForConflict(ClaimFeudRecord record, string reason)
        {
            if (record == null || record.State == ClaimFeudState.WarActive || !IsActiveFeudState(record.State))
                return;

            ClaimFeudState resumeState = record.State == ClaimFeudState.Paused
                ? record.ResumeState
                : record.State;
            string normalizedReason = string.IsNullOrWhiteSpace(reason) ? "another conflict requires the parties' attention" : reason;
            bool newlyPaused = record.State != ClaimFeudState.Paused;
            record.Pause(resumeState, normalizedReason, CurrentDay);
            if (newlyPaused)
            {
                _yearlyPauses++;
                record.SetDebugReason($"{record.DebugReason}; paused ({normalizedReason})");
                BellumCivileDebug.TraceIfEnabled(
                    "claim feud",
                    $"Claim feud paused; feud={record.RecordId}; resume_state={resumeState}; pressure={record.Pressure:0.0}; reason={normalizedReason}.",
                    requestInGameDisplay: true);
            }
        }

        public float GetRebelliousIntentPenalty(Clan clan)
        {
            if (clan == null || string.IsNullOrWhiteSpace(clan.StringId))
                return 0f;

            float penalty = 0f;
            foreach (ClaimFeudRecord record in GetActiveFeuds())
            {
                if (record.ClaimantClanId != clan.StringId && record.HolderClanId != clan.StringId)
                    continue;

                switch (record.State)
                {
                    case ClaimFeudState.WarActive:
                    case ClaimFeudState.DefiedPendingWar:
                        penalty = Math.Max(penalty, C.ClaimFeudWarActiveRebellionPenalty);
                        break;
                    case ClaimFeudState.PetitionReady:
                    case ClaimFeudState.AwaitingPlayerRulerJudgment:
                    case ClaimFeudState.Agitating:
                        penalty = Math.Max(penalty, GetPressureBandRebellionPenalty(record.Pressure));
                        break;
                    case ClaimFeudState.Paused:
                        penalty = Math.Max(penalty, GetPressureBandRebellionPenalty(record.Pressure));
                        break;
                }
            }

            return penalty;
        }

        public string GetRebelliousIntentPenaltyLabel(Clan clan)
        {
            if (clan == null || string.IsNullOrWhiteSpace(clan.StringId))
                return string.Empty;

            float bestPenalty = 0f;
            string bestLabel = string.Empty;
            foreach (ClaimFeudRecord record in GetActiveFeuds())
            {
                if (record.ClaimantClanId != clan.StringId && record.HolderClanId != clan.StringId)
                    continue;

                float penalty;
                if (record.State == ClaimFeudState.WarActive || record.State == ClaimFeudState.DefiedPendingWar)
                    penalty = C.ClaimFeudWarActiveRebellionPenalty;
                else if (record.State == ClaimFeudState.Agitating
                      || record.State == ClaimFeudState.PetitionReady
                      || record.State == ClaimFeudState.AwaitingPlayerRulerJudgment
                      || record.State == ClaimFeudState.Paused)
                {
                    penalty = GetPressureBandRebellionPenalty(record.Pressure);
                }
                else
                {
                    continue;
                }

                if (penalty > bestPenalty)
                {
                    bestPenalty = penalty;
                    bestLabel = GetPressureBandRebellionLabel(record.State, record.Pressure);
                }
            }

            return bestLabel;
        }

        public bool TryForceEvaluateClan(Clan clan, out string report)
        {
            EnsureCollectionsInitialized();
            report = null;

            FeudalTitleBehavior titleBehavior = Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>();
            if (titleBehavior == null)
            {
                report = "Error: FeudalTitleBehavior unavailable.";
                return false;
            }

            if (!CanStartClaimFeud(clan, out string reason))
            {
                report = $"Error: {reason}.";
                return false;
            }

            List<ClaimFeudCandidate> candidates = GetRankedFeudCandidates(titleBehavior, clan, requireThreshold: false);
            if (candidates.Count == 0)
            {
                report = $"{clan.Name} has no valid claim feud target: no same-realm held title with an active weak/strong claim.";
                return true;
            }

            ClaimFeudCandidate best = candidates[0];
            string thresholdText = best.Score >= C.ClaimFeudStartThreshold
                ? "passes"
                : $"fails by {C.ClaimFeudStartThreshold - best.Score:0}";
            report =
                $"Best claim feud target for {clan.Name}: {FeudalTitleDisplayHelper.FormatTitleName(best.Title, clan)} against {best.Holder.Name}; strength={best.Strength}; score={best.Score:0}/{C.ClaimFeudStartThreshold:0} ({thresholdText}); qualifying starts are deterministic; reasons={string.Join(", ", best.Reasons)}.\n"
                + "Top candidates:\n"
                + string.Join("\n", candidates.Take(8).Select(candidate =>
                {
                    string status = candidate.Score >= C.ClaimFeudStartThreshold
                        ? "passes"
                        : $"fails by {C.ClaimFeudStartThreshold - candidate.Score:0}";
                    return $"- {FeudalTitleDisplayHelper.FormatTitleName(candidate.Title, clan)} vs {candidate.Holder.Name}: {candidate.Strength}; score={candidate.Score:0}/{C.ClaimFeudStartThreshold:0} ({status}); reasons={string.Join(", ", candidate.Reasons)}";
                }));

            return true;
        }

        public bool TryForceStartFeud(Clan clan, out string report)
        {
            EnsureCollectionsInitialized();
            report = null;

            FeudalTitleBehavior titleBehavior = Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>();
            if (titleBehavior == null)
            {
                report = "Error: FeudalTitleBehavior unavailable.";
                return false;
            }

            if (!CanStartClaimFeud(clan, out string reason))
            {
                report = $"Error: {reason}.";
                return false;
            }

            List<ClaimFeudCandidate> candidates = GetRankedFeudCandidates(titleBehavior, clan, requireThreshold: false);
            if (candidates.Count == 0)
            {
                report = $"{clan.Name} has no valid claim feud target: no same-realm held title with an active weak/strong claim.";
                return false;
            }

            ClaimFeudCandidate best = candidates[0];
            ClaimFeudRecord record = StartFeud(best, "debug_force");
            if (record == null)
            {
                report = "A civil war has suspended private litigation in this realm.";
                return false;
            }

            report = $"Started claim feud: claimant={clan.Name}; holder={best.Holder.Name}; title={FeudalTitleDisplayHelper.FormatTitleName(best.Title, clan)}; pressure={record.Pressure:0.0}; score={best.Score:0}; reasons={string.Join(", ", best.Reasons)}.";
            return true;
        }

        public IReadOnlyList<ClaimFeudActionPreview> GetPressableFeudPreviews(Clan clan)
        {
            EnsureCollectionsInitialized();
            List<ClaimFeudActionPreview> previews = new List<ClaimFeudActionPreview>();

            FeudalTitleBehavior titleBehavior = Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>();
            if (titleBehavior == null || clan == null)
                return previews;

            if (!CanStartClaimFeud(clan, out _))
                return previews;

            foreach (ClaimFeudCandidate candidate in GetRankedFeudCandidates(titleBehavior, clan, requireThreshold: false))
            {
                ClaimFeudActionPreview preview = new ClaimFeudActionPreview
                {
                    TitleId = candidate.Title?.TitleId,
                    TitleName = FeudalTitleDisplayHelper.FormatTitleName(candidate.Title, clan),
                    HolderClanId = candidate.Holder?.StringId,
                    HolderName = candidate.Holder?.Name?.ToString() ?? string.Empty,
                    Strength = candidate.Strength,
                    Score = candidate.Score,
                    IsEnabled = true,
                    DisabledReason = string.Empty
                };
                preview.Reasons.AddRange(candidate.Reasons);
                previews.Add(preview);
            }

            AppendCooldownFeudPreviews(titleBehavior, clan, previews);

            return previews;
        }

        private void AppendCooldownFeudPreviews(FeudalTitleBehavior titleBehavior, Clan clan, List<ClaimFeudActionPreview> previews)
        {
            HashSet<string> seenTitles = new HashSet<string>(previews
                .Where(preview => preview != null && !string.IsNullOrWhiteSpace(preview.TitleId))
                .Select(preview => preview.TitleId));

            foreach (FeudalClaimRecord claim in titleBehavior.GetActiveClaimsByClan(clan))
            {
                FeudalTitleRecord title = titleBehavior.GetTitle(claim.TargetTitleId);
                if (title == null || !title.IsActive || title.IsDeliberatelyDissolved || seenTitles.Contains(title.TitleId))
                    continue;

                if (TryBuildCooldownFeudPreview(titleBehavior, clan, title, claim.Strength, out ClaimFeudActionPreview preview))
                {
                    previews.Add(preview);
                    seenTitles.Add(title.TitleId);
                }
            }

            foreach (FeudalTitleRecord title in titleBehavior.GetTitlesHeldByClan(clan, deJure: true))
            {
                if (title == null || !title.IsActive || title.IsDeliberatelyDissolved || title.DeFactoHolderClanId == clan.StringId || seenTitles.Contains(title.TitleId))
                    continue;

                if (TryBuildCooldownFeudPreview(titleBehavior, clan, title, FeudalClaimStrength.Strong, out ClaimFeudActionPreview preview))
                {
                    previews.Add(preview);
                    seenTitles.Add(title.TitleId);
                }
            }
        }

        private bool TryBuildCooldownFeudPreview(
            FeudalTitleBehavior titleBehavior,
            Clan claimant,
            FeudalTitleRecord title,
            FeudalClaimStrength strength,
            out ClaimFeudActionPreview preview)
        {
            preview = null;
            Clan holder = ResolveClan(title?.DeFactoHolderClanId);
            if (title == null || claimant == null || holder == null || holder == claimant)
                return false;

            if (claimant.Kingdom == null || holder.Kingdom == null || claimant.Kingdom != holder.Kingdom)
                return false;

            ClaimFeudRecord cooldown = _feuds.FirstOrDefault(record => record != null
                && record.TargetTitleId == title.TitleId
                && ((record.ClaimantClanId == claimant.StringId && record.HolderClanId == holder.StringId)
                    || (record.ClaimantClanId == holder.StringId && record.HolderClanId == claimant.StringId))
                && !IsActiveFeudState(record.State)
                && record.CooldownUntilDay > CurrentDay);

            if (cooldown == null)
                return false;

            int remainingDays = Math.Max(1, (int)Math.Ceiling(cooldown.CooldownUntilDay - CurrentDay));
            TextObject disabled = new TextObject("{=BC_UI_Err_ClaimFeudCooldown}This dispute is cooling down for {DAYS} more days.");
            disabled.SetTextVariable("DAYS", remainingDays.ToString());

            preview = new ClaimFeudActionPreview
            {
                TitleId = title.TitleId,
                TitleName = FeudalTitleDisplayHelper.FormatTitleName(title, claimant),
                HolderClanId = holder.StringId,
                HolderName = holder.Name?.ToString() ?? string.Empty,
                Strength = strength,
                Score = 0f,
                IsEnabled = false,
                DisabledReason = disabled.ToString()
            };

            return true;
        }

        public bool TryStartPlayerFeud(Clan clan, string titleId, out ClaimFeudRecord record, out string report)
        {
            EnsureCollectionsInitialized();
            record = null;
            report = null;

            FeudalTitleBehavior titleBehavior = Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>();
            if (titleBehavior == null)
            {
                report = "Error: FeudalTitleBehavior unavailable.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(titleId))
            {
                report = "Error: no title selected.";
                return false;
            }

            if (!CanStartClaimFeud(clan, out string reason))
            {
                report = $"Error: {reason}.";
                return false;
            }

            ClaimFeudCandidate candidate = GetRankedFeudCandidates(titleBehavior, clan, requireThreshold: false)
                .FirstOrDefault(item => item?.Title?.TitleId == titleId);
            if (candidate == null)
            {
                report = "No valid claim feud target was found for that title.";
                return false;
            }

            record = StartFeud(candidate, "player_action");
            if (record == null)
            {
                report = "A civil war has suspended private litigation in this realm.";
                return false;
            }

            report = $"Started claim feud over {FeudalTitleDisplayHelper.FormatTitleName(candidate.Title, clan)}.";
            return true;
        }

        public bool WouldHolderDefyRevocation(
            FeudalTitleBehavior titleBehavior,
            Clan revoker,
            Clan holder,
            FeudalTitleRecord title,
            FeudalClaimStrength strength,
            out float revokerPower,
            out float holderPower)
        {
            revokerPower = 0f;
            holderPower = 0f;
            Kingdom kingdom = revoker?.Kingdom ?? holder?.Kingdom;
            if (titleBehavior == null || revoker == null || holder == null || title == null || kingdom == null)
                return false;

            List<Clan> revokerSide = BuildFeudSide(titleBehavior, revoker, holder, kingdom);
            List<Clan> holderSide = BuildFeudSide(titleBehavior, holder, revoker, kingdom);
            revokerPower = RebellionPowerHelper.CalculateFactionPower(revokerSide);
            holderPower = RebellionPowerHelper.CalculateFactionPower(holderSide);
            return IsHolderReadyToDefyRevocation(title, revoker, holder, strength, revokerPower, holderPower);
        }

        public bool TryStartImmediateRevocationWar(
            FeudalTitleBehavior titleBehavior,
            Clan revoker,
            Clan holder,
            FeudalTitleRecord title,
            FeudalClaimStrength strength,
            out ClaimFeudRecord record,
            out string report)
        {
            EnsureCollectionsInitialized();
            record = null;
            report = null;

            Kingdom kingdom = revoker?.Kingdom ?? holder?.Kingdom;
            if (titleBehavior == null || revoker == null || holder == null || title == null || kingdom == null)
            {
                report = "missing revocation context";
                return false;
            }

            if (!IsValidFeudClan(revoker, out report) || !IsValidFeudClan(holder, out report))
                return false;

            if (revoker.Kingdom == null || holder.Kingdom == null || revoker.Kingdom != holder.Kingdom)
            {
                report = "revocation parties are not in the same realm";
                return false;
            }

            string recordId = BuildRecordId(revoker, holder, title);
            ClaimFeudRecord existing = _feuds.FirstOrDefault(feud => feud != null
                && feud.RecordId == recordId
                && IsActiveFeudState(feud.State));
            if (existing != null)
            {
                record = existing;
                report = "a feud over this title is already active";
                return false;
            }

            record = new ClaimFeudRecord(
                recordId,
                kingdom.StringId,
                revoker.StringId,
                holder.StringId,
                title.TitleId,
                strength,
                100f,
                CurrentDay,
                "claimed_title_revocation",
                "claimed title revocation; calls to arms pending");
            record.SetState(ClaimFeudState.DefiedPendingWar);
            _feuds.Add(record);

            ResolveFeudCallsToArms(
                titleBehavior,
                record,
                revoker,
                holder,
                title,
                kingdom,
                out List<Clan> revokerSide,
                out List<Clan> holderSide);
            float revokerPower = RebellionPowerHelper.CalculateFactionPower(revokerSide);
            float holderPower = RebellionPowerHelper.CalculateFactionPower(holderSide);
            record.RecordRuling(
                ClaimFeudJudgment.UpholdClaimant,
                ClaimFeudResponse.Accept,
                ClaimFeudResponse.Defy,
                CurrentDay,
                revokerPower,
                holderPower,
                string.Join(",", revokerSide.Select(clan => clan.StringId)),
                string.Join(",", holderSide.Select(clan => clan.StringId)));
            record.SetDebugReason($"claimed title revocation; claimant_power={revokerPower:0}; holder_power={holderPower:0}");

            BellumCivileDebug.Trace(
                "claim feud",
                $"Claimed title revocation defied; revoker={revoker.Name} ({revoker.StringId}); holder={holder.Name} ({holder.StringId}); title={FeudalTitleDisplayHelper.FormatTitleName(title, revoker)}; strength={strength}; power={revokerPower:0}/{holderPower:0}.",
                requestInGameDisplay: true);
            return true;
        }

        public bool TryAbandonPlayerFeud(string recordId, Clan claimant, out string report)
        {
            EnsureCollectionsInitialized();
            report = null;

            ClaimFeudRecord record = _feuds.FirstOrDefault(feud => feud != null && feud.RecordId == recordId);
            if (record == null)
            {
                report = "No active feud was found.";
                return false;
            }

            if (claimant == null || record.ClaimantClanId != claimant.StringId)
            {
                report = "Only the claimant can abandon this feud.";
                return false;
            }

            if (record.State != ClaimFeudState.Agitating && record.State != ClaimFeudState.PetitionReady)
            {
                report = "This feud has already escalated too far to abandon.";
                return false;
            }

            record.SetState(ClaimFeudState.Cooldown);
            record.SetCooldownUntilDay(CurrentDay + GetCampaignDaysInYear() * C.ClaimFeudCooldownYears);
            record.SetDebugReason($"{record.DebugReason}; abandoned by claimant");
            BellumCivileDebug.Trace(
                "claim feud",
                $"Claim feud abandoned by claimant; claimant={claimant.Name} ({claimant.StringId}); record={record.RecordId}; cooldown_until={record.CooldownUntilDay:0}.",
                requestInGameDisplay: true);
            report = "The feud was cooled down.";
            return true;
        }

        public bool TrySurrenderPlayerFeud(string recordId, Clan claimant, out string report)
        {
            EnsureCollectionsInitialized();
            report = null;

            ClaimFeudRecord record = _feuds.FirstOrDefault(feud => feud != null && feud.RecordId == recordId);
            if (record == null)
            {
                report = "No active feud was found.";
                return false;
            }

            if (claimant == null || record.ClaimantClanId != claimant.StringId)
            {
                report = "Only the claimant can surrender this feud.";
                return false;
            }

            ClaimFeudWarBehavior warBehavior = Campaign.Current?.GetCampaignBehavior<ClaimFeudWarBehavior>();
            bool hasActiveWar = warBehavior?.HasActiveWarForFeud(record.RecordId) == true;
            if (!hasActiveWar
                && record.State != ClaimFeudState.DefiedPendingWar
                && record.State != ClaimFeudState.WarActive)
            {
                report = "This feud has not escalated into open defiance.";
                return false;
            }

            // Once the armed phase exists it owns peace, title resolution, clan restoration,
            // temporary-kingdom cleanup, and War Score cleanup as one atomic outcome.
            if (hasActiveWar)
                return warBehavior.TryResolveClaimantSurrender(record.RecordId, out report);

            if (record.State == ClaimFeudState.WarActive)
            {
                report = "The feud is marked as an open war, but its armed conflict record could not be found. No partial surrender was applied.";
                return false;
            }

            FeudalTitleBehavior titleBehavior = Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>();
            if (titleBehavior == null)
            {
                report = "FeudalTitleBehavior unavailable.";
                return false;
            }

            FeudalTitleRecord title = titleBehavior.GetTitle(record.TargetTitleId);
            Clan holder = ResolveClan(record.HolderClanId);
            bool settled = titleBehavior.TryResolveClaimFeudForHolder(
                claimant,
                holder,
                title,
                record.ClaimStrength,
                "claim_feud_player_surrender",
                out string result);

            record.SetState(settled ? ClaimFeudState.Settled : ClaimFeudState.Resolved);
            record.SetCooldownUntilDay(CurrentDay + GetCampaignDaysInYear() * C.ClaimFeudCooldownYears);
            record.SetDebugReason($"{record.DebugReason}; surrendered by claimant; result={result}");
            BellumCivileDebug.Trace(
                "claim feud",
                $"Claim feud surrendered by claimant; claimant={claimant.Name} ({claimant.StringId}); holder={holder?.Name}; title={title?.TitleId}; result={result}.",
                requestInGameDisplay: true);
            report = settled ? "The claimant surrendered and the holder's right was upheld." : result ?? "The feud was ended.";
            return true;
        }

        public bool TryAdvanceFeudPressure(Clan clan, float amount, out string report)
        {
            EnsureCollectionsInitialized();
            report = null;

            FeudalTitleBehavior titleBehavior = Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>();
            if (titleBehavior == null)
            {
                report = "Error: FeudalTitleBehavior unavailable.";
                return false;
            }

            if (clan == null || string.IsNullOrWhiteSpace(clan.StringId))
            {
                report = "Error: missing clan.";
                return false;
            }

            ClaimFeudRecord record = GetActiveFeuds()
                .FirstOrDefault(feud => feud.ClaimantClanId == clan.StringId || feud.HolderClanId == clan.StringId);
            if (record == null)
            {
                report = $"{clan.Name} is not part of an active claim feud.";
                return false;
            }

            Clan claimant = ResolveClan(record.ClaimantClanId);
            Clan holder = ResolveClan(record.HolderClanId);
            FeudalTitleRecord title = titleBehavior.GetTitle(record.TargetTitleId);
            if (!IsRecordStillValid(titleBehavior, record, claimant, holder, title, out string invalidReason))
            {
                InvalidateFeud(record, $"debug advance: {invalidReason}");
                report = $"Claim feud resolved as invalid: {invalidReason}.";
                return false;
            }

            if (record.State == ClaimFeudState.Paused)
            {
                report = $"Claim feud is paused: {record.PauseReason}.";
                return false;
            }

            float oldPressure = record.Pressure;
            record.SetPressure(Math.Min(100f, Math.Max(0f, record.Pressure + amount)));
            record.SetLastTickDay(CurrentDay);
            if (record.State == ClaimFeudState.Agitating)
                HandleFeudMilestones(titleBehavior, record, claimant, holder, title, oldPressure, record.Pressure);

            if (record.Pressure >= 100f && record.State == ClaimFeudState.Agitating)
            {
                record.SetState(ClaimFeudState.PetitionReady);
            }

            report = $"Advanced claim feud: claimant={claimant?.Name}; holder={holder?.Name}; title={FeudalTitleDisplayHelper.FormatTitleName(title, claimant ?? holder)}; pressure={record.Pressure:0.0}; state={record.State}; judgment={record.Judgment}; responses={record.ClaimantResponse}/{record.HolderResponse}.";
            return true;
        }

        public string BuildDebugReport()
        {
            EnsureCollectionsInitialized();
            FeudalTitleBehavior titleBehavior = Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>();
            List<ClaimFeudRecord> active = _feuds
                .Where(record => record != null && record.State != ClaimFeudState.Resolved)
                .ToList();
            if (active.Count == 0)
                return "No tracked claim feuds.";

            return "Tracked claim feuds:\n" + string.Join("\n", active.Select(record =>
            {
                Clan claimant = ResolveClan(record.ClaimantClanId);
                Clan holder = ResolveClan(record.HolderClanId);
                FeudalTitleRecord title = titleBehavior?.GetTitle(record.TargetTitleId);
                string ruling = record.Judgment == ClaimFeudJudgment.None
                    ? string.Empty
                    : $"; judgment={record.Judgment}; responses={record.ClaimantResponse}/{record.HolderResponse}; power={record.ClaimantSidePower:0}/{record.HolderSidePower:0}";
                string pause = record.State == ClaimFeudState.Paused
                    ? $"; resume={record.ResumeState}; pause_reason={record.PauseReason}"
                    : string.Empty;
                return $"- {record.State}: claimant={claimant?.Name?.ToString() ?? record.ClaimantClanId}; holder={holder?.Name?.ToString() ?? record.HolderClanId}; title={FeudalTitleDisplayHelper.FormatTitleName(title, claimant ?? holder)}; strength={record.ClaimStrength}; pressure={record.Pressure:0.0}{ruling}{pause}; reason={record.DebugReason}";
            }));
        }

        private void OnDailyTick()
        {
            EnsureCollectionsInitialized();

            FeudalTitleBehavior titleBehavior = Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>();
            if (titleBehavior == null)
                return;

            TickActiveFeudPressure(titleBehavior);

        }

        private void TickActiveFeudPressure(FeudalTitleBehavior titleBehavior)
        {
            float currentDay = CurrentDay;
            foreach (ClaimFeudRecord record in GetActiveFeuds().ToList())
            {
                Clan claimant = ResolveClan(record.ClaimantClanId);
                Clan holder = ResolveClan(record.HolderClanId);
                FeudalTitleRecord title = titleBehavior.GetTitle(record.TargetTitleId);
                if (!IsRecordStillValid(titleBehavior, record, claimant, holder, title, out string invalidReason))
                {
                    InvalidateFeud(record, invalidReason);
                    BellumCivileDebug.Trace("claim feud", $"Claim feud resolved before petition; record={record.RecordId}; reason={invalidReason}.", requestInGameDisplay: true);
                    continue;
                }

                if (TryGetTemporaryPauseReason(record, claimant, holder, out string pauseReason))
                {
                    PauseFeudForConflict(record, pauseReason);
                    continue;
                }

                if (record.State == ClaimFeudState.Paused)
                {
                    ClaimFeudState restoredState = record.Resume(currentDay);
                    _yearlyResumes++;
                    record.SetDebugReason($"{record.DebugReason}; resumed as {restoredState}");
                    BellumCivileDebug.TraceIfEnabled(
                        "claim feud",
                        $"Claim feud resumed; feud={record.RecordId}; state={restoredState}; pressure={record.Pressure:0.0}.",
                        requestInGameDisplay: true);
                }

                if (record.State == ClaimFeudState.PetitionReady && record.Judgment == ClaimFeudJudgment.None)
                {
                    ResolvePetition(titleBehavior, record, claimant, holder, title);
                    continue;
                }

                if (record.State == ClaimFeudState.AwaitingPlayerResponse)
                {
                    TryShowPendingPlayerRulingInquiry(record, claimant, holder, title);
                    continue;
                }

                if (record.State == ClaimFeudState.AwaitingPlayerRulerJudgment)
                {
                    TryShowPendingPlayerRulerJudgmentInquiry(titleBehavior, record, claimant, holder, title);
                    continue;
                }

                if (record.State != ClaimFeudState.Agitating)
                    continue;

                float elapsedDays = Math.Max(1f, currentDay - record.LastTickDay);
                float dailyPressure = CalculateDailyPressure(record, claimant, holder);
                float oldPressure = record.Pressure;
                record.SetPressure(Math.Min(100f, record.Pressure + dailyPressure * elapsedDays));
                record.SetLastTickDay(currentDay);

                HandleFeudMilestones(titleBehavior, record, claimant, holder, title, oldPressure, record.Pressure);
                if (record.State != ClaimFeudState.Agitating)
                    continue;

                if (record.Pressure >= 100f)
                {
                    record.SetState(ClaimFeudState.PetitionReady);
                    record.SetLastTickDay(currentDay);
                    _yearlyPetitionsQueued++;
                    BellumCivileDebug.Trace(
                        "claim feud",
                        $"Claim feud petition queued for next daily tick; claimant={claimant.Name} ({claimant.StringId}); holder={holder.Name} ({holder.StringId}); title={title.Name} ({title.TitleId}); strength={record.ClaimStrength}.",
                        requestInGameDisplay: true);
                }
            }

            _feuds.RemoveAll(record => record == null || record.State == ClaimFeudState.Resolved);
        }

        private void HandleFeudMilestones(
            FeudalTitleBehavior titleBehavior,
            ClaimFeudRecord record,
            Clan claimant,
            Clan holder,
            FeudalTitleRecord title,
            float oldPressure,
            float newPressure)
        {
            if (record == null || record.State != ClaimFeudState.Agitating || claimant == null || holder == null || title == null)
                return;

            if (!record.HasPassed33 && oldPressure < 33f && newPressure >= 33f)
            {
                record.MarkMilestonePassed(33f);
                _yearlyPassed33++;
                ChangeRelation(claimant.Leader, holder.Leader, C.ClaimFeudHarassmentRelationPenalty,
                    RelationMemorySources.FeudHarassment);
                NotificationHelper.ShowClaimFeudTensionsMount(claimant, holder, title);
                BellumCivileDebug.Trace(
                    "claim feud",
                    $"Claim feud passed 33%; claimant={claimant.StringId}; holder={holder.StringId}; title={title.TitleId}; relation_penalty={C.ClaimFeudHarassmentRelationPenalty}.",
                    requestInGameDisplay: true);
            }

            if (!record.HasPassed66 && oldPressure < 66f && newPressure >= 66f)
            {
                record.MarkMilestonePassed(66f);
                Kingdom kingdom = claimant.Kingdom ?? holder.Kingdom;
                ResolveFeudCallsToArms(
                    titleBehavior,
                    record,
                    claimant,
                    holder,
                    title,
                    kingdom,
                    out List<Clan> claimantSide,
                    out List<Clan> holderSide);
                float claimantPower = RebellionPowerHelper.CalculateFactionPower(claimantSide);
                float holderPower = RebellionPowerHelper.CalculateFactionPower(holderSide);
                bool claimantReady = IsSideReadyToDefy(claimant, claimantPower, holderPower);

                _yearlyPassed66++;
                if (!claimantReady)
                {
                    BellumCivileDebug.Trace(
                        "claim feud",
                        $"Claim feud claimant is outmatched at 66% but may still petition the crown; claimant={claimant.StringId}; holder={holder.StringId}; title={title.TitleId}; claimant_power={claimantPower:0}; holder_power={holderPower:0}.",
                        requestInGameDisplay: true);
                }

                ChangeRelation(claimant.Leader, holder.Leader, C.ClaimFeudCallToArmsRelationPenalty,
                    RelationMemorySources.FeudEscalation);
                record.RecordSupporters(
                    claimantPower,
                    holderPower,
                    string.Join(",", claimantSide.Select(clan => clan.StringId)),
                    string.Join(",", holderSide.Select(clan => clan.StringId)));
                TryShowPlayerSupportInvitation(titleBehavior, record, claimant, holder, title, kingdom);
                NotificationHelper.ShowClaimFeudCallToArms(claimant, holder, title);
                BellumCivileDebug.Trace(
                    "claim feud",
                    $"Claim feud passed 66%; claimant={claimant.StringId}; holder={holder.StringId}; title={title.TitleId}; claimant_power={claimantPower:0}; holder_power={holderPower:0}; claimant_supporters={string.Join(",", claimantSide.Select(clan => clan.StringId))}; holder_supporters={string.Join(",", holderSide.Select(clan => clan.StringId))}.",
                    requestInGameDisplay: true);
            }
        }

        private void ResolvePetition(
            FeudalTitleBehavior titleBehavior,
            ClaimFeudRecord record,
            Clan claimant,
            Clan holder,
            FeudalTitleRecord title)
        {
            if (record == null || claimant == null || holder == null || title == null)
                return;

            Kingdom kingdom = claimant.Kingdom ?? holder.Kingdom;
            Hero ruler = kingdom?.RulingClan?.Leader;
            if (kingdom == null || ruler == null || ruler.IsDead)
            {
                record.SetState(ClaimFeudState.DefiedPendingWar);
                record.SetDebugReason($"{record.DebugReason}; petition found no valid ruler");
                return;
            }

            List<Clan> claimantSide = BuildFeudSide(titleBehavior, claimant, holder, kingdom, record);
            List<Clan> holderSide = BuildFeudSide(titleBehavior, holder, claimant, kingdom, record);
            float claimantPower = RebellionPowerHelper.CalculateFactionPower(claimantSide);
            float holderPower = RebellionPowerHelper.CalculateFactionPower(holderSide);
            RefreshRecordedSupporters(record, claimantSide, holderSide, claimantPower, holderPower);
            bool canEnforcePeace = CanRulerEnforcePeace(record, kingdom);
            bool playerIsClaimant = claimant == Clan.PlayerClan;
            bool playerIsHolder = holder == Clan.PlayerClan;
            bool playerIsRuler = kingdom.RulingClan == Clan.PlayerClan;

            if (playerIsRuler && !playerIsClaimant && !playerIsHolder)
            {
                record.RecordRuling(
                    ClaimFeudJudgment.None,
                    ClaimFeudResponse.None,
                    ClaimFeudResponse.None,
                    CurrentDay,
                    claimantPower,
                    holderPower,
                    string.Join(",", claimantSide.Select(clan => clan.StringId)),
                    string.Join(",", holderSide.Select(clan => clan.StringId)));

                record.SetState(ClaimFeudState.AwaitingPlayerRulerJudgment);
                record.SetDebugReason($"{record.DebugReason}; awaiting player ruler judgment");
                ShowPlayerRulerJudgmentInquiry(titleBehavior, record, kingdom, ruler, claimant, holder, title);

                BellumCivileDebug.Trace(
                    "claim feud",
                    $"Claim feud petition awaiting player ruler judgment; kingdom={kingdom.Name}; claimant={claimant.Name} ({claimant.StringId}); holder={holder.Name} ({holder.StringId}); title={FeudalTitleDisplayHelper.FormatTitleName(title, claimant)}; power={claimantPower:0}/{holderPower:0}; can_enforce_peace={canEnforcePeace}.",
                    requestInGameDisplay: true);
                return;
            }

            ClaimFeudJudgment judgment = ChooseRulerJudgment(
                titleBehavior,
                kingdom,
                ruler,
                claimant,
                holder,
                title,
                record.ClaimStrength,
                claimantPower,
                holderPower,
                canEnforcePeace);

            GetRulingResponses(
                judgment,
                kingdom,
                ruler,
                claimant,
                holder,
                title,
                record.ClaimStrength,
                claimantPower,
                holderPower,
                out ClaimFeudResponse claimantResponse,
                out ClaimFeudResponse holderResponse,
                out string claimantAssessment,
                out string holderAssessment);

            if (playerIsClaimant || playerIsHolder)
            {
                if (playerIsClaimant)
                    claimantResponse = ClaimFeudResponse.None;
                if (playerIsHolder)
                    holderResponse = ClaimFeudResponse.None;

                record.RecordRuling(
                    judgment,
                    claimantResponse,
                    holderResponse,
                    CurrentDay,
                    claimantPower,
                    holderPower,
                    string.Join(",", claimantSide.Select(clan => clan.StringId)),
                    string.Join(",", holderSide.Select(clan => clan.StringId)));

                record.SetState(ClaimFeudState.AwaitingPlayerResponse);
                record.SetDebugReason($"{record.DebugReason}; awaiting player response to judgment={judgment}");
                ShowPlayerRulingInquiry(record, kingdom, ruler, claimant, holder, title);

                BellumCivileDebug.Trace(
                    "claim feud",
                    $"Claim feud petition awaiting player response; kingdom={kingdom.Name}; claimant={claimant.Name} ({claimant.StringId}); holder={holder.Name} ({holder.StringId}); title={FeudalTitleDisplayHelper.FormatTitleName(title, claimant)}; judgment={judgment}; responses={claimantResponse}/{holderResponse}; power={claimantPower:0}/{holderPower:0}.",
                    requestInGameDisplay: true);
                return;
            }

            record.RecordRuling(
                judgment,
                claimantResponse,
                holderResponse,
                CurrentDay,
                claimantPower,
                holderPower,
                string.Join(",", claimantSide.Select(clan => clan.StringId)),
                string.Join(",", holderSide.Select(clan => clan.StringId)));

            ApplyRulingOutcome(titleBehavior, record, kingdom, ruler, claimant, holder, title, judgment, claimantResponse, holderResponse);
            NotificationHelper.ShowClaimFeudRuling(kingdom, ruler, claimant, holder, title, judgment, claimantResponse, holderResponse);

            BellumCivileDebug.Trace(
                "claim feud",
                $"Claim feud petition judged; kingdom={kingdom.Name}; claimant={claimant.Name} ({claimant.StringId}); holder={holder.Name} ({holder.StringId}); title={FeudalTitleDisplayHelper.FormatTitleName(title, claimant)}; judgment={judgment}; responses={claimantResponse}/{holderResponse}; power={claimantPower:0}/{holderPower:0}; claimant_assessment={claimantAssessment}; holder_assessment={holderAssessment}; state={record.State}.",
                requestInGameDisplay: true);
        }

        private static void GetRulingResponses(
            ClaimFeudJudgment judgment,
            Kingdom kingdom,
            Hero ruler,
            Clan claimant,
            Clan holder,
            FeudalTitleRecord title,
            FeudalClaimStrength strength,
            float claimantPower,
            float holderPower,
            out ClaimFeudResponse claimantResponse,
            out ClaimFeudResponse holderResponse,
            out string claimantAssessment,
            out string holderAssessment)
        {
            claimantResponse = ClaimFeudResponse.Accept;
            holderResponse = ClaimFeudResponse.Accept;
            claimantAssessment = "not wronged by ruling";
            holderAssessment = "not wronged by ruling";
            switch (judgment)
            {
                case ClaimFeudJudgment.UpholdClaimant:
                {
                    ClaimFeudComplianceAssessment assessment = AssessRulingCompliance(
                        holder,
                        claimant,
                        kingdom,
                        ruler,
                        title,
                        strength,
                        claimantSide: false,
                        judgment,
                        holderPower,
                        claimantPower);
                    holderResponse = assessment.Defies ? ClaimFeudResponse.Defy : ClaimFeudResponse.Accept;
                    holderAssessment = assessment.Summary;
                    break;
                }
                case ClaimFeudJudgment.UpholdHolder:
                {
                    ClaimFeudComplianceAssessment assessment = AssessRulingCompliance(
                        claimant,
                        holder,
                        kingdom,
                        ruler,
                        title,
                        strength,
                        claimantSide: true,
                        judgment,
                        claimantPower,
                        holderPower);
                    claimantResponse = assessment.Defies ? ClaimFeudResponse.Defy : ClaimFeudResponse.Accept;
                    claimantAssessment = assessment.Summary;
                    break;
                }
                case ClaimFeudJudgment.Suppress:
                {
                    ClaimFeudComplianceAssessment assessment = AssessRulingCompliance(
                        claimant,
                        holder,
                        kingdom,
                        ruler,
                        title,
                        strength,
                        claimantSide: true,
                        judgment,
                        claimantPower,
                        holderPower);
                    claimantResponse = assessment.Defies ? ClaimFeudResponse.Defy : ClaimFeudResponse.Accept;
                    claimantAssessment = assessment.Summary;
                    break;
                }
                case ClaimFeudJudgment.Abstain:
                {
                    ClaimFeudComplianceAssessment claimantResult = AssessRulingCompliance(
                        claimant,
                        holder,
                        kingdom,
                        ruler,
                        title,
                        strength,
                        claimantSide: true,
                        judgment,
                        claimantPower,
                        holderPower);
                    claimantResponse = claimantResult.Defies ? ClaimFeudResponse.Defy : ClaimFeudResponse.Accept;
                    claimantAssessment = claimantResult.Summary;

                    if (holder?.Leader != null
                        && claimant?.Leader != null
                        && holder.Leader.GetRelation(claimant.Leader) < 0)
                    {
                        ClaimFeudComplianceAssessment holderResult = AssessRulingCompliance(
                            holder,
                            claimant,
                            kingdom,
                            ruler,
                            title,
                            strength,
                            claimantSide: false,
                            judgment,
                            holderPower,
                            claimantPower);
                        holderResponse = holderResult.Defies ? ClaimFeudResponse.Defy : ClaimFeudResponse.Accept;
                        holderAssessment = holderResult.Summary;
                    }
                    break;
                }
            }
        }

        private static ClaimFeudComplianceAssessment AssessRulingCompliance(
            Clan side,
            Clan opponent,
            Kingdom kingdom,
            Hero ruler,
            FeudalTitleRecord title,
            FeudalClaimStrength strength,
            bool claimantSide,
            ClaimFeudJudgment judgment,
            float sidePower,
            float opposingPower)
        {
            Hero leader = side?.Leader;
            float threshold = RebellionPowerHelper.CalculateRebellionPowerThreshold(leader);
            List<string> factors = new List<string>();
            bool legalPosition = false;

            if (claimantSide)
            {
                if (strength == FeudalClaimStrength.Strong)
                {
                    threshold -= 0.20f;
                    legalPosition = true;
                    factors.Add("strong claim");
                }
                if (title?.DeJureHolderClanId == side?.StringId)
                {
                    threshold -= 0.20f;
                    legalPosition = true;
                    factors.Add("rightful holder");
                }
                if (judgment == ClaimFeudJudgment.Suppress)
                {
                    threshold -= 0.10f;
                    factors.Add("claim suppressed");
                }
                else if (judgment == ClaimFeudJudgment.Abstain)
                {
                    threshold -= 0.12f;
                    factors.Add("crown refused judgment");
                }
            }
            else
            {
                if (title?.DeJureHolderClanId == side?.StringId)
                {
                    threshold -= 0.20f;
                    legalPosition = true;
                    factors.Add("rightful holder");
                }
                if (title?.DeFactoHolderClanId == side?.StringId)
                {
                    threshold -= 0.10f;
                    legalPosition = true;
                    factors.Add("controls title");
                }
                if (judgment == ClaimFeudJudgment.Abstain)
                {
                    threshold -= 0.05f;
                    factors.Add("private hostility remains");
                }
            }

            int rulerRelation = leader != null && ruler != null ? leader.GetRelation(ruler) : 0;
            if (rulerRelation >= 60)
            {
                threshold += 0.35f;
                factors.Add("deep ruler loyalty");
            }
            else if (rulerRelation >= 30)
            {
                threshold += 0.20f;
                factors.Add("ruler friendship");
            }
            else if (rulerRelation <= -60)
            {
                threshold -= 0.30f;
                factors.Add("hates ruler");
            }
            else if (rulerRelation <= -30)
            {
                threshold -= 0.15f;
                factors.Add("resents ruler");
            }

            if (MarriageAllianceHelper.HasMarriageAlliance(kingdom?.RulingClan, side))
            {
                threshold += 0.20f;
                factors.Add("allied to crown");
            }

            if (leader != null)
            {
                int honor = leader.GetTraitLevel(DefaultTraits.Honor);
                if (honor > 0)
                {
                    threshold += legalPosition ? -0.08f * honor : 0.05f * honor;
                    factors.Add(legalPosition ? "honor defends legal right" : "honor counsels obedience");
                }
                else if (honor < 0)
                {
                    threshold += 0.04f * honor;
                    factors.Add("dishonorable opportunism");
                }

                int mercy = leader.GetTraitLevel(DefaultTraits.Mercy);
                if (mercy != 0)
                {
                    threshold += 0.06f * mercy;
                    factors.Add(mercy > 0 ? "merciful restraint" : "cruel defiance");
                }
            }

            threshold = Math.Max(0.25f, Math.Min(2.50f, threshold));
            float requiredPower = Math.Max(1f, opposingPower) * threshold;
            bool defies = sidePower >= requiredPower;
            string summary = $"power={sidePower:0}/{requiredPower:0}; threshold={threshold:0.00}; factors={string.Join(",", factors)}";
            return new ClaimFeudComplianceAssessment(defies, summary);
        }

        private static bool CanRulerEnforcePeace(ClaimFeudRecord record, Kingdom kingdom)
        {
            RealmPeaceEnforcementBehavior peaceBehavior = Campaign.Current?.GetCampaignBehavior<RealmPeaceEnforcementBehavior>();
            Clan rulingClan = kingdom?.RulingClan;
            if (peaceBehavior == null || rulingClan == null)
                return false;

            RoyalPeacePreview preview = peaceBehavior.GetClaimFeudPreview(record, rulingClan);
            return preview?.IsEnabled == true;
        }

        private void ApplyRulingOutcome(
            FeudalTitleBehavior titleBehavior,
            ClaimFeudRecord record,
            Kingdom kingdom,
            Hero ruler,
            Clan claimant,
            Clan holder,
            FeudalTitleRecord title,
            ClaimFeudJudgment judgment,
            ClaimFeudResponse claimantResponse,
            ClaimFeudResponse holderResponse)
        {
            _yearlyPetitionsJudged++;
            bool anyDefiance = claimantResponse == ClaimFeudResponse.Defy || holderResponse == ClaimFeudResponse.Defy;
            if (anyDefiance)
            {
                _yearlyDefiedToWar++;
                record.SetState(ClaimFeudState.DefiedPendingWar);
                record.SetDebugReason($"{record.DebugReason}; judgment={judgment}; defiance pending open-war phase");
                ApplyRulingRelations(ruler, claimant, holder, judgment, claimantResponse, holderResponse);
                return;
            }

            bool success = true;
            string result = string.Empty;
            switch (judgment)
            {
                case ClaimFeudJudgment.UpholdClaimant:
                    success = titleBehavior.TryResolveClaimFeudForClaimant(
                        claimant,
                        holder,
                        title,
                        "claim_feud_ruling",
                        out result);
                    record.SetState(success ? ClaimFeudState.Settled : ClaimFeudState.Resolved);
                    break;
                case ClaimFeudJudgment.UpholdHolder:
                    success = titleBehavior.TryResolveClaimFeudForHolder(
                        claimant,
                        holder,
                        title,
                        record.ClaimStrength,
                        "claim_feud_ruling",
                        out result);
                    record.SetState(success ? ClaimFeudState.Settled : ClaimFeudState.Resolved);
                    break;
                case ClaimFeudJudgment.Suppress:
                    RealmPeaceEnforcementBehavior peaceBehavior = Campaign.Current?.GetCampaignBehavior<RealmPeaceEnforcementBehavior>();
                    Clan rulingClan = kingdom?.RulingClan;
                    success = peaceBehavior != null
                        && rulingClan != null
                        && peaceBehavior.TryEnforceClaimFeudPeace(record, rulingClan, out result, showNotification: false);
                    if (!success)
                    {
                        record.SetState(ClaimFeudState.Settled);
                        record.SetCooldownUntilDay(CurrentDay + GetCampaignDaysInYear() * C.ClaimFeudCooldownYears);
                    }
                    break;
                case ClaimFeudJudgment.Abstain:
                    record.SetState(ClaimFeudState.Settled);
                    record.SetCooldownUntilDay(CurrentDay + GetCampaignDaysInYear() * C.ClaimFeudCooldownYears);
                    break;
                default:
                    record.SetState(ClaimFeudState.Resolved);
                    break;
            }

            ApplyRulingRelations(ruler, claimant, holder, judgment, claimantResponse, holderResponse);
            record.SetDebugReason($"{record.DebugReason}; judgment={judgment}; result={result}");
            if (record.State == ClaimFeudState.Settled || record.State == ClaimFeudState.SuppressedCooldown)
                _yearlyPeacefulSettlements++;
        }

        private void TryShowPendingPlayerRulingInquiry(ClaimFeudRecord record, Clan claimant, Clan holder, FeudalTitleRecord title)
        {
            if (record == null || record.State != ClaimFeudState.AwaitingPlayerResponse)
                return;

            if (_shownPlayerRulingInquiries.Contains(record.RecordId))
                return;

            Kingdom kingdom = claimant?.Kingdom ?? holder?.Kingdom;
            Hero ruler = kingdom?.RulingClan?.Leader;
            if (kingdom == null || ruler == null || ruler.IsDead || claimant == null || holder == null || title == null)
                return;

            ShowPlayerRulingInquiry(record, kingdom, ruler, claimant, holder, title);
        }

        private void TryShowPendingPlayerRulerJudgmentInquiry(
            FeudalTitleBehavior titleBehavior,
            ClaimFeudRecord record,
            Clan claimant,
            Clan holder,
            FeudalTitleRecord title)
        {
            if (record == null || record.State != ClaimFeudState.AwaitingPlayerRulerJudgment)
                return;

            if (_shownPlayerRulerJudgmentInquiries.Contains(record.RecordId))
                return;

            Kingdom kingdom = claimant?.Kingdom ?? holder?.Kingdom;
            Hero ruler = kingdom?.RulingClan?.Leader;
            if (titleBehavior == null || kingdom == null || ruler == null || ruler.IsDead || claimant == null || holder == null || title == null)
                return;

            if (kingdom.RulingClan != Clan.PlayerClan)
            {
                record.SetState(ClaimFeudState.PetitionReady);
                return;
            }

            ShowPlayerRulerJudgmentInquiry(titleBehavior, record, kingdom, ruler, claimant, holder, title);
        }

        private void ShowPlayerRulerJudgmentInquiry(
            FeudalTitleBehavior titleBehavior,
            ClaimFeudRecord record,
            Kingdom kingdom,
            Hero ruler,
            Clan claimant,
            Clan holder,
            FeudalTitleRecord title)
        {
            if (titleBehavior == null || record == null || kingdom == null || ruler == null || claimant == null || holder == null || title == null)
                return;

            if (kingdom.RulingClan != Clan.PlayerClan)
                return;

            _shownPlayerRulerJudgmentInquiries.Add(record.RecordId);

            List<Clan> claimantSide = BuildFeudSide(titleBehavior, claimant, holder, kingdom, record);
            List<Clan> holderSide = BuildFeudSide(titleBehavior, holder, claimant, kingdom, record);
            float claimantPower = RebellionPowerHelper.CalculateFactionPower(claimantSide);
            float holderPower = RebellionPowerHelper.CalculateFactionPower(holderSide);
            RefreshRecordedSupporters(record, claimantSide, holderSide, claimantPower, holderPower);

            RealmPeaceEnforcementBehavior peaceBehavior = Campaign.Current?.GetCampaignBehavior<RealmPeaceEnforcementBehavior>();
            RoyalPeacePreview peacePreview = peaceBehavior?.GetClaimFeudPreview(record, Clan.PlayerClan);

            TextObject titleText = new TextObject("{=BC_ClaimFeud_PlayerRuler_Title}Petition for Judgment");
            TextObject desc = new TextObject("{=BC_ClaimFeud_PlayerRuler_Desc}A messenger arrives carrying the seal of {CLAIMANT_NAME} of the {CLAIMANT_CLAN}:{newline}{newline}\"I hope this letter finds you well, my liege. As you happen to know, I hold a legal right to the {TITLE_NAME}, held wrongfully by {HOLDER_NAME} of the {HOLDER_CLAN}, and would seek your justice on this matter. I await your decision.\"");
            desc.SetTextVariable("CLAIMANT_NAME", claimant.Leader?.Name ?? claimant.Name);
            desc.SetTextVariable("CLAIMANT_CLAN", claimant.Name);
            desc.SetTextVariable("TITLE_NAME", new TextObject("{=!}" + FeudalTitleDisplayHelper.FormatTitleName(title, claimant)));
            desc.SetTextVariable("HOLDER_NAME", holder.Leader?.Name ?? holder.Name);
            desc.SetTextVariable("HOLDER_CLAN", holder.Name);
            desc.SetTextVariable("newline", "\n");

            List<InquiryElement> choices = new List<InquiryElement>
            {
                new InquiryElement(
                    PlayerRulerChoiceClaimant,
                    new TextObject("{=BC_ClaimFeud_PlayerRuler_UpholdClaimant}Rule for the Claimant").ToString(),
                    null,
                    true,
                    BuildPlayerRulerJudgmentTooltip(ClaimFeudJudgment.UpholdClaimant, record, kingdom, ruler, claimant, holder, title, claimantPower, holderPower, peacePreview)),
                new InquiryElement(
                    PlayerRulerChoiceHolder,
                    new TextObject("{=BC_ClaimFeud_PlayerRuler_UpholdHolder}Rule for the Holder").ToString(),
                    null,
                    true,
                    BuildPlayerRulerJudgmentTooltip(ClaimFeudJudgment.UpholdHolder, record, kingdom, ruler, claimant, holder, title, claimantPower, holderPower, peacePreview)),
                new InquiryElement(
                    PlayerRulerChoiceAbstain,
                    new TextObject("{=BC_ClaimFeud_PlayerRuler_Abstain}Refuse to Intervene").ToString(),
                    null,
                    true,
                    BuildPlayerRulerJudgmentTooltip(ClaimFeudJudgment.Abstain, record, kingdom, ruler, claimant, holder, title, claimantPower, holderPower, peacePreview)),
                new InquiryElement(
                    PlayerRulerChoiceSuppress,
                    new TextObject("{=BC_ClaimFeud_PlayerRuler_EnforcePeace}Enforce the Realm's Peace").ToString(),
                    null,
                    peacePreview?.IsEnabled == true,
                    BuildPlayerRulerJudgmentTooltip(ClaimFeudJudgment.Suppress, record, kingdom, ruler, claimant, holder, title, claimantPower, holderPower, peacePreview))
            };

            MultiSelectionInquiryData inquiry = new MultiSelectionInquiryData(
                titleText.ToString(),
                desc.ToString(),
                choices,
                true,
                1,
                1,
                new TextObject("{=BC_ClaimFeud_PlayerRuler_Confirm}Pass Judgment").ToString(),
                new TextObject("{=BC_ClaimFeud_PlayerRuler_Defer}Decide Later").ToString(),
                selected => OnPlayerRulerJudgmentSelected(record.RecordId, selected),
                _ => _shownPlayerRulerJudgmentInquiries.Remove(record.RecordId));

            MBInformationManager.ShowMultiSelectionInquiry(inquiry, true);
        }

        private string BuildPlayerRulerJudgmentTooltip(
            ClaimFeudJudgment judgment,
            ClaimFeudRecord record,
            Kingdom kingdom,
            Hero ruler,
            Clan claimant,
            Clan holder,
            FeudalTitleRecord title,
            float claimantPower,
            float holderPower,
            RoyalPeacePreview peacePreview)
        {
            string titleName = FeudalTitleDisplayHelper.FormatTitleName(title, claimant ?? holder);
            string claimantName = claimant?.Name?.ToString() ?? "?";
            string holderName = holder?.Name?.ToString() ?? "?";
            GetRulingResponses(
                judgment,
                kingdom,
                ruler,
                claimant,
                holder,
                title,
                record?.ClaimStrength ?? FeudalClaimStrength.Weak,
                claimantPower,
                holderPower,
                out ClaimFeudResponse claimantResponse,
                out ClaimFeudResponse holderResponse,
                out _,
                out _);

            switch (judgment)
            {
                case ClaimFeudJudgment.UpholdClaimant:
                {
                    TextObject text = new TextObject("{=BC_ClaimFeud_PlayerRuler_TooltipClaimant}<a style=\"Tooltip.Value.Text\"><b>Legal effect</b></a>{newline}{CLAIMANT_CLAN} receives the {TITLE_NAME} if the ruling is obeyed.{newline}{newline}<a style=\"Tooltip.Value.Text\"><b>Likely consequence</b></a>{newline}{HOLDER_CLAN} {RESPONSE}.");
                    text.SetTextVariable("CLAIMANT_CLAN", new TextObject("{=!}" + claimantName));
                    text.SetTextVariable("HOLDER_CLAN", new TextObject("{=!}" + holderName));
                    text.SetTextVariable("TITLE_NAME", new TextObject("{=!}" + titleName));
                    text.SetTextVariable("RESPONSE", GetRulingResponseText(holderResponse));
                    text.SetTextVariable("newline", "\n");
                    return text.ToString();
                }
                case ClaimFeudJudgment.UpholdHolder:
                {
                    TextObject text = new TextObject("{=BC_ClaimFeud_PlayerRuler_TooltipHolder}<a style=\"Tooltip.Value.Text\"><b>Legal effect</b></a>{newline}{HOLDER_CLAN} keeps the {TITLE_NAME}. The claimant's claim is weakened or dismissed if the ruling is obeyed.{newline}{newline}<a style=\"Tooltip.Value.Text\"><b>Likely consequence</b></a>{newline}{CLAIMANT_CLAN} {RESPONSE}.");
                    text.SetTextVariable("CLAIMANT_CLAN", new TextObject("{=!}" + claimantName));
                    text.SetTextVariable("HOLDER_CLAN", new TextObject("{=!}" + holderName));
                    text.SetTextVariable("TITLE_NAME", new TextObject("{=!}" + titleName));
                    text.SetTextVariable("RESPONSE", GetRulingResponseText(claimantResponse));
                    text.SetTextVariable("newline", "\n");
                    return text.ToString();
                }
                case ClaimFeudJudgment.Abstain:
                {
                    TextObject text = new TextObject("{=BC_ClaimFeud_PlayerRuler_TooltipAbstain}<a style=\"Tooltip.Value.Text\"><b>Legal effect</b></a>{newline}No title rights change and both houses lose faith in the crown.{newline}{newline}<a style=\"Tooltip.Value.Text\"><b>Likely consequence</b></a>{newline}{CLAIMANT_CLAN} {CLAIMANT_RESPONSE}. {HOLDER_CLAN} {HOLDER_RESPONSE}.");
                    text.SetTextVariable("CLAIMANT_CLAN", new TextObject("{=!}" + claimantName));
                    text.SetTextVariable("HOLDER_CLAN", new TextObject("{=!}" + holderName));
                    text.SetTextVariable("CLAIMANT_RESPONSE", GetRulingResponseText(claimantResponse));
                    text.SetTextVariable("HOLDER_RESPONSE", GetRulingResponseText(holderResponse));
                    text.SetTextVariable("newline", "\n");
                    return text.ToString();
                }
                case ClaimFeudJudgment.Suppress:
                {
                    if (peacePreview?.IsEnabled == true)
                    {
                        TextObject text = new TextObject("{=BC_ClaimFeud_PlayerRuler_TooltipSuppress}<a style=\"Tooltip.Value.Text\"><b>Legal effect</b></a>{newline}Suppresses the feud without changing title rights.{newline}{newline}<a style=\"Tooltip.Value.Text\"><b>Cost</b></a>{newline}{COST} influence and Tyrant's Debt for the crown.{newline}{newline}<a style=\"Tooltip.Value.Text\"><b>Likely consequence</b></a>{newline}{CLAIMANT_CLAN} {RESPONSE}.");
                        text.SetTextVariable("COST", peacePreview.InfluenceCost);
                        text.SetTextVariable("CLAIMANT_CLAN", new TextObject("{=!}" + claimantName));
                        text.SetTextVariable("RESPONSE", GetRulingResponseText(claimantResponse));
                        text.SetTextVariable("newline", "\n");
                        return text.ToString();
                    }

                    return peacePreview?.Hint?.ToString()
                        ?? new TextObject("{=BC_RoyalPeace_ErrUnavailable}The realm's peace cannot be enforced right now.").ToString();
                }
                default:
                    return string.Empty;
            }
        }

        private static string GetRulingResponseText(ClaimFeudResponse response)
        {
            return response == ClaimFeudResponse.Defy
                ? new TextObject("{=BC_ClaimFeud_PlayerRuler_ResponseDefy}is likely to defy this ruling").ToString()
                : new TextObject("{=BC_ClaimFeud_PlayerRuler_ResponseAccept}is likely to accept this ruling").ToString();
        }

        private void OnPlayerRulerJudgmentSelected(string recordId, List<InquiryElement> selected)
        {
            _shownPlayerRulerJudgmentInquiries.Remove(recordId);
            if (selected == null || selected.Count == 0)
                return;

            ClaimFeudJudgment judgment;
            switch (selected[0].Identifier as string)
            {
                case PlayerRulerChoiceClaimant:
                    judgment = ClaimFeudJudgment.UpholdClaimant;
                    break;
                case PlayerRulerChoiceHolder:
                    judgment = ClaimFeudJudgment.UpholdHolder;
                    break;
                case PlayerRulerChoiceSuppress:
                    judgment = ClaimFeudJudgment.Suppress;
                    break;
                default:
                    judgment = ClaimFeudJudgment.Abstain;
                    break;
            }

            ResolvePlayerRulerJudgment(recordId, judgment);
        }

        private void ResolvePlayerRulerJudgment(string recordId, ClaimFeudJudgment judgment)
        {
            ClaimFeudRecord record = _feuds.FirstOrDefault(feud => feud != null && feud.RecordId == recordId);
            if (record == null || record.State != ClaimFeudState.AwaitingPlayerRulerJudgment)
                return;

            FeudalTitleBehavior titleBehavior = Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>();
            Clan claimant = ResolveClan(record.ClaimantClanId);
            Clan holder = ResolveClan(record.HolderClanId);
            FeudalTitleRecord title = titleBehavior?.GetTitle(record.TargetTitleId);
            Kingdom kingdom = claimant?.Kingdom ?? holder?.Kingdom;
            Hero ruler = kingdom?.RulingClan?.Leader;
            if (titleBehavior == null || claimant == null || holder == null || title == null || kingdom == null || ruler == null || kingdom.RulingClan != Clan.PlayerClan)
            {
                record.SetState(ClaimFeudState.Resolved);
                record.SetDebugReason($"{record.DebugReason}; player ruler judgment failed missing context");
                return;
            }

            if (judgment == ClaimFeudJudgment.Suppress && !CanRulerEnforcePeace(record, kingdom))
            {
                record.SetState(ClaimFeudState.AwaitingPlayerRulerJudgment);
                return;
            }

            List<Clan> claimantSide = BuildFeudSide(titleBehavior, claimant, holder, kingdom, record);
            List<Clan> holderSide = BuildFeudSide(titleBehavior, holder, claimant, kingdom, record);
            float claimantPower = RebellionPowerHelper.CalculateFactionPower(claimantSide);
            float holderPower = RebellionPowerHelper.CalculateFactionPower(holderSide);
            RefreshRecordedSupporters(record, claimantSide, holderSide, claimantPower, holderPower);
            GetRulingResponses(
                judgment,
                kingdom,
                ruler,
                claimant,
                holder,
                title,
                record.ClaimStrength,
                claimantPower,
                holderPower,
                out ClaimFeudResponse claimantResponse,
                out ClaimFeudResponse holderResponse,
                out string claimantAssessment,
                out string holderAssessment);

            record.RecordRuling(
                judgment,
                claimantResponse,
                holderResponse,
                CurrentDay,
                claimantPower,
                holderPower,
                string.Join(",", claimantSide.Select(clan => clan.StringId)),
                string.Join(",", holderSide.Select(clan => clan.StringId)));

            ApplyRulingOutcome(titleBehavior, record, kingdom, ruler, claimant, holder, title, judgment, claimantResponse, holderResponse);
            NotificationHelper.ShowClaimFeudRuling(kingdom, ruler, claimant, holder, title, judgment, claimantResponse, holderResponse);

            BellumCivileDebug.Trace(
                "claim feud",
                $"Player ruler judged claim feud; kingdom={kingdom.StringId}; claimant={claimant.StringId}; holder={holder.StringId}; title={title.TitleId}; judgment={judgment}; responses={claimantResponse}/{holderResponse}; power={claimantPower:0}/{holderPower:0}; claimant_assessment={claimantAssessment}; holder_assessment={holderAssessment}; state={record.State}.",
                requestInGameDisplay: true);
        }

        private void ShowPlayerRulingInquiry(
            ClaimFeudRecord record,
            Kingdom kingdom,
            Hero ruler,
            Clan claimant,
            Clan holder,
            FeudalTitleRecord title)
        {
            if (record == null || ruler == null || claimant == null || holder == null || title == null)
                return;

            bool playerIsClaimant = claimant == Clan.PlayerClan;
            bool playerIsHolder = holder == Clan.PlayerClan;
            if (!playerIsClaimant && !playerIsHolder)
                return;

            _shownPlayerRulingInquiries.Add(record.RecordId);

            bool canRefuse = CanPlayerRefuseRuling(record.Judgment, playerIsClaimant);
            TextObject header = new TextObject("{=BC_ClaimFeud_PlayerRuling_Title}Message from {RULER_TITLE} {RULER_NAME}");
            header.SetTextVariable("RULER_TITLE", GetHeroTitleText(ruler));
            header.SetTextVariable("RULER_NAME", ruler.Name);

            TextObject description = BuildPlayerRulingDescription(record.Judgment, playerIsClaimant, ruler, claimant, holder, title);
            InformationManager.ShowInquiry(new InquiryData(
                header.ToString(),
                description.ToString(),
                true,
                canRefuse,
                new TextObject("{=BC_ClaimFeud_PlayerRuling_Accept}Accept Judgment").ToString(),
                canRefuse ? new TextObject("{=BC_ClaimFeud_PlayerRuling_Refuse}Refuse & Fight").ToString() : string.Empty,
                () => ResolvePlayerRulingResponse(record.RecordId, defy: false),
                canRefuse ? (Action)(() => ResolvePlayerRulingResponse(record.RecordId, defy: true)) : null), true);
        }

        private static bool CanPlayerRefuseRuling(ClaimFeudJudgment judgment, bool playerIsClaimant)
        {
            if (judgment == ClaimFeudJudgment.Suppress)
                return false;

            if (playerIsClaimant)
                return judgment != ClaimFeudJudgment.UpholdClaimant;

            return judgment == ClaimFeudJudgment.UpholdClaimant || judgment == ClaimFeudJudgment.Abstain;
        }

        private static TextObject BuildPlayerRulingDescription(
            ClaimFeudJudgment judgment,
            bool playerIsClaimant,
            Hero ruler,
            Clan claimant,
            Clan holder,
            FeudalTitleRecord title)
        {
            TextObject text;
            switch (judgment)
            {
                case ClaimFeudJudgment.UpholdClaimant:
                    text = playerIsClaimant
                        ? new TextObject("{=BC_ClaimFeud_PlayerRuling_UpholdPlayerClaimant}\"After reviewing your claim, I have come to the following conclusion: your claim is justified. I have decided to rule in your favor and demand that {HOLDER_NAME} of the {HOLDER_CLAN} surrender the {TITLE_NAME} fully into your hands.\"")
                        : new TextObject("{=BC_ClaimFeud_PlayerRuling_AgainstPlayerHolder}\"After reviewing {CLAIMANT_NAME}'s claim, I have come to the following conclusion: {CLAIMANT_POSSESSIVE} claim is justified. I demand that you surrender the {TITLE_NAME} fully into {CLAIMANT_POSSESSIVE} hands.\"");
                    break;
                case ClaimFeudJudgment.UpholdHolder:
                    text = playerIsClaimant
                        ? new TextObject("{=BC_ClaimFeud_PlayerRuling_RejectPlayerClaimant}\"After reviewing your claim, I have come to the following conclusion: {HOLDER_NAME} of the {HOLDER_CLAN} holds the stronger right. I have ruled against your case. You are to abandon your pursuit of the {TITLE_NAME}. This is my decree.\"")
                        : new TextObject("{=BC_ClaimFeud_PlayerRuling_UpholdPlayerHolder}\"After reviewing {CLAIMANT_NAME}'s claim, I have come to the following conclusion: your right is the stronger one. I have ruled in your favor, and {CLAIMANT_NAME} is to abandon the pursuit of the {TITLE_NAME}.\"");
                    break;
                case ClaimFeudJudgment.Suppress:
                    text = new TextObject("{=BC_ClaimFeud_PlayerRuling_Suppress}\"This dispute has gone far enough. By my authority, I command both houses to cease their feud over the {TITLE_NAME}.\"");
                    break;
                case ClaimFeudJudgment.Abstain:
                    text = new TextObject("{=BC_ClaimFeud_PlayerRuling_Abstain}\"I will not pass judgment on this matter. If your houses cannot settle the dispute over the {TITLE_NAME} by law, then the crown will not shield either side from the consequences.\"");
                    break;
                default:
                    text = new TextObject("{=BC_ClaimFeud_PlayerRuling_Abstain}\"I will not pass judgment on this matter. If your houses cannot settle the dispute over the {TITLE_NAME} by law, then the crown will not shield either side from the consequences.\"");
                    break;
            }

            text.SetTextVariable("RULER_NAME", ruler?.Name);
            text.SetTextVariable("CLAIMANT_NAME", claimant?.Leader?.Name);
            text.SetTextVariable("CLAIMANT_CLAN", claimant?.Name);
            text.SetTextVariable("CLAIMANT_POSSESSIVE", GetPossessive(claimant?.Leader));
            text.SetTextVariable("HOLDER_NAME", holder?.Leader?.Name);
            text.SetTextVariable("HOLDER_CLAN", holder?.Name);
            text.SetTextVariable("TITLE_NAME", new TextObject("{=!}" + FeudalTitleDisplayHelper.FormatTitleName(title, claimant ?? holder)));
            return text;
        }

        private void ResolvePlayerRulingResponse(string recordId, bool defy)
        {
            _shownPlayerRulingInquiries.Remove(recordId);

            ClaimFeudRecord record = _feuds.FirstOrDefault(feud => feud != null && feud.RecordId == recordId);
            if (record == null || record.State != ClaimFeudState.AwaitingPlayerResponse)
                return;

            FeudalTitleBehavior titleBehavior = Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>();
            Clan claimant = ResolveClan(record.ClaimantClanId);
            Clan holder = ResolveClan(record.HolderClanId);
            FeudalTitleRecord title = titleBehavior?.GetTitle(record.TargetTitleId);
            Kingdom kingdom = claimant?.Kingdom ?? holder?.Kingdom;
            Hero ruler = kingdom?.RulingClan?.Leader;
            if (titleBehavior == null || claimant == null || holder == null || title == null || kingdom == null || ruler == null)
            {
                record.SetState(ClaimFeudState.Resolved);
                record.SetDebugReason($"{record.DebugReason}; player response failed missing context");
                return;
            }

            ClaimFeudResponse playerResponse = defy ? ClaimFeudResponse.Defy : ClaimFeudResponse.Accept;
            ClaimFeudResponse claimantResponse = record.ClaimantResponse == ClaimFeudResponse.None
                ? playerResponse
                : record.ClaimantResponse;
            ClaimFeudResponse holderResponse = record.HolderResponse == ClaimFeudResponse.None
                ? playerResponse
                : record.HolderResponse;

            if (claimantResponse == ClaimFeudResponse.None)
                claimantResponse = ClaimFeudResponse.Accept;
            if (holderResponse == ClaimFeudResponse.None)
                holderResponse = ClaimFeudResponse.Accept;

            record.RecordRuling(
                record.Judgment,
                claimantResponse,
                holderResponse,
                record.RulingDay > 0f ? record.RulingDay : CurrentDay,
                record.ClaimantSidePower,
                record.HolderSidePower,
                record.ClaimantSupporterIds,
                record.HolderSupporterIds);

            ApplyRulingOutcome(titleBehavior, record, kingdom, ruler, claimant, holder, title, record.Judgment, claimantResponse, holderResponse);
            NotificationHelper.ShowClaimFeudRuling(kingdom, ruler, claimant, holder, title, record.Judgment, claimantResponse, holderResponse);

            BellumCivileDebug.Trace(
                "claim feud",
                $"Player answered claim feud ruling; claimant={claimant.StringId}; holder={holder.StringId}; title={title.TitleId}; judgment={record.Judgment}; responses={claimantResponse}/{holderResponse}; state={record.State}.",
                requestInGameDisplay: true);
        }

        private void ShowPlayerRevocationDemandInquiry(
            FeudalTitleBehavior titleBehavior,
            Clan revoker,
            FeudalTitleRecord title,
            FeudalRevocationPreview preview,
            float score,
            List<string> reasons)
        {
            Clan holder = preview?.HolderClan;
            if (titleBehavior == null || revoker?.Leader == null || holder != Clan.PlayerClan || title == null)
                return;

            string key = BuildPlayerRevocationDemandKey(revoker, title);
            if (!_shownPlayerRevocationDemandInquiries.Add(key))
                return;

            FeudalTitleRecord parentTitle = titleBehavior.GetParentTitle(title, FeudalHierarchyMode.DeFacto);
            TextObject header = new TextObject("{=BC_ClaimFeud_PlayerRevocationDemand_Title}A Demand from {LIEGE_NAME}");
            header.SetTextVariable("LIEGE_NAME", revoker.Leader.Name);

            TextObject desc = new TextObject("{=BC_ClaimFeud_PlayerRevocationDemand_Desc}A messenger arrives with a letter from {LIEGE_NAME} of the {LIEGE_CLAN}, your liege {LIEGE_TITLE_LINE}.{newline}{newline}\"By right of my claim, I demand that you surrender the {TITLE_NAME} into my hands. Submit to lawful revocation, or answer for your defiance before the realm.\"");
            desc.SetTextVariable("LIEGE_NAME", revoker.Leader.Name);
            desc.SetTextVariable("LIEGE_CLAN", revoker.Name);
            TextObject liegeTitleLine = new TextObject("{=BC_ClaimFeud_PlayerRevocationDemand_LiegeTitle}over the {TITLE_NAME}");
            liegeTitleLine.SetTextVariable("TITLE_NAME", new TextObject("{=!}" + FeudalTitleDisplayHelper.FormatTitleName(parentTitle, revoker)));
            desc.SetTextVariable("LIEGE_TITLE_LINE", parentTitle != null ? liegeTitleLine : new TextObject("{=BC_ClaimFeud_PlayerRevocationDemand_LiegeFallback}over this title"));
            desc.SetTextVariable("TITLE_NAME", new TextObject("{=!}" + FeudalTitleDisplayHelper.FormatTitleName(title, holder)));
            desc.SetTextVariable("newline", "\n");

            List<InquiryElement> choices = new List<InquiryElement>
            {
                new InquiryElement(
                    PlayerRevocationChoiceSubmit,
                    new TextObject("{=BC_ClaimFeud_PlayerRevocationDemand_Submit}Submit").ToString(),
                    null,
                    true,
                    BuildPlayerRevocationDemandTooltip(title, revoker, holder, preview, defy: false)),
                new InquiryElement(
                    PlayerRevocationChoiceDefy,
                    new TextObject("{=BC_ClaimFeud_PlayerRevocationDemand_Defy}Refuse & Fight").ToString(),
                    null,
                    true,
                    BuildPlayerRevocationDemandTooltip(title, revoker, holder, preview, defy: true))
            };

            MultiSelectionInquiryData inquiry = new MultiSelectionInquiryData(
                header.ToString(),
                desc.ToString(),
                choices,
                true,
                1,
                1,
                new TextObject("{=BC_ClaimFeud_PlayerRevocationDemand_Confirm}Answer Demand").ToString(),
                new TextObject("{=BC_ClaimFeud_PlayerRevocationDemand_Defer}Decide Later").ToString(),
                selected => OnPlayerRevocationDemandSelected(revoker.StringId, title.TitleId, selected),
                _ => _shownPlayerRevocationDemandInquiries.Remove(key));

            MBInformationManager.ShowMultiSelectionInquiry(inquiry, true);

            BellumCivileDebug.Trace(
                "claim feud",
                $"AI demanded title revocation from player; revoker={revoker.StringId}; title={title.TitleId}; score={score:0}; claim={preview.ClaimStrength}; likely_defy={preview.HolderLikelyDefies}; reasons={string.Join(", ", reasons ?? new List<string>())}.",
                requestInGameDisplay: true);
        }

        private static string BuildPlayerRevocationDemandTooltip(FeudalTitleRecord title, Clan revoker, Clan holder, FeudalRevocationPreview preview, bool defy)
        {
            TextObject text = defy
                ? new TextObject("{=BC_ClaimFeud_PlayerRevocationDemand_DefyHint}<a style=\"Tooltip.Value.Text\"><b>Effect</b></a>{newline}Refuse the demand and keep the {TITLE_NAME} for now.{newline}{newline}<a style=\"Tooltip.Value.Text\"><b>Consequence</b></a>{newline}The dispute immediately becomes a private feud war against the {LIEGE_CLAN}.")
                : new TextObject("{=BC_ClaimFeud_PlayerRevocationDemand_SubmitHint}<a style=\"Tooltip.Value.Text\"><b>Effect</b></a>{newline}Surrender the {TITLE_NAME} to the {LIEGE_CLAN}.{newline}{newline}<a style=\"Tooltip.Value.Text\"><b>Consequence</b></a>{newline}No war begins. Your house keeps a weak claim, preserving its grievance.");

            text.SetTextVariable("TITLE_NAME", new TextObject("{=!}" + FeudalTitleDisplayHelper.FormatTitleName(title, holder ?? revoker)));
            text.SetTextVariable("LIEGE_CLAN", revoker?.Name);
            text.SetTextVariable("newline", "\n");
            return text.ToString();
        }

        private void OnPlayerRevocationDemandSelected(string revokerClanId, string titleId, List<InquiryElement> selected)
        {
            if (selected == null || selected.Count == 0)
                return;

            bool defy = (selected[0].Identifier as string) == PlayerRevocationChoiceDefy;
            ResolvePlayerRevocationDemand(revokerClanId, titleId, defy);
        }

        private void ResolvePlayerRevocationDemand(string revokerClanId, string titleId, bool defy)
        {
            FeudalTitleBehavior titleBehavior = Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>();
            Clan revoker = ResolveClan(revokerClanId);
            FeudalTitleRecord title = titleBehavior?.GetTitle(titleId);
            if (revoker != null && title != null)
                _shownPlayerRevocationDemandInquiries.Remove(BuildPlayerRevocationDemandKey(revoker, title));

            if (revoker == null || title == null)
                return;

            if (!FeudalTitlePlayerActionService.TryExecuteRevocationWithResponse(revoker, title, defy, out bool holderDefied, out string reason))
            {
                TextObject fail = new TextObject("{=BC_ClaimFeud_PlayerRevocationDemand_Failed}The demand could not be resolved: {REASON}");
                fail.SetTextVariable("REASON", reason ?? "unknown");
                InformationManager.DisplayMessage(new InformationMessage(fail.ToString(), Colors.Red));
                return;
            }

            BellumCivileDebug.Trace(
                "claim feud",
                $"Player answered title revocation demand; revoker={revoker.StringId}; title={title.TitleId}; defied={holderDefied}.",
                requestInGameDisplay: true);
        }

        private static string BuildPlayerRevocationDemandKey(Clan revoker, FeudalTitleRecord title)
        {
            return $"{revoker?.StringId ?? "null"}:{Clan.PlayerClan?.StringId ?? "player"}:{title?.TitleId ?? "null"}";
        }

        private static string GetHeroTitleText(Hero hero)
        {
            if (hero != null && FeudalTitleDisplayHelper.TryGetHighestDisplayTitle(hero, out string title) && !string.IsNullOrWhiteSpace(title))
                return title;

            return new TextObject("{=BC_TitleDisplay_King}King").ToString();
        }

        private static string GetPossessive(Hero hero)
        {
            return hero?.IsFemale == true
                ? new TextObject("{=BC_Possessive_Her}her").ToString()
                : new TextObject("{=BC_Possessive_His}his").ToString();
        }

        private static ClaimFeudJudgment ChooseRulerJudgment(
            FeudalTitleBehavior titleBehavior,
            Kingdom kingdom,
            Hero ruler,
            Clan claimant,
            Clan holder,
            FeudalTitleRecord title,
            FeudalClaimStrength strength,
            float claimantPower,
            float holderPower,
            bool canSuppress)
        {
            float claimantCase = strength == FeudalClaimStrength.Strong ? 35f : 12f;
            float holderCase = 10f;
            float suppressionInclination = 0f;

            if (title.DeJureHolderClanId == claimant.StringId)
                claimantCase += 30f;
            if (title.DeJureHolderClanId == holder.StringId)
                holderCase += 30f;
            if (title.DeFactoHolderClanId == holder.StringId)
                holderCase += 10f;

            if (ruler != null)
            {
                if (claimant.Leader != null)
                    claimantCase += claimant.Leader.GetRelation(ruler) * 0.25f;
                if (holder.Leader != null)
                    holderCase += holder.Leader.GetRelation(ruler) * 0.25f;
            }

            Clan rulingClan = kingdom?.RulingClan;
            if (MarriageAllianceHelper.HasMarriageAlliance(rulingClan, claimant))
                claimantCase += C.ClaimFeudRulingClanMarriageAllianceBonus;
            if (MarriageAllianceHelper.HasMarriageAlliance(rulingClan, holder))
                holderCase += C.ClaimFeudRulingClanMarriageAllianceBonus;

            float totalPower = Math.Max(1f, claimantPower + holderPower);
            claimantCase += claimantPower / totalPower * 20f;
            holderCase += holderPower / totalPower * 20f;
            ApplyRulerPersonalityToJudgment(
                ruler,
                claimant,
                holder,
                title,
                strength,
                claimantPower,
                holderPower,
                ref claimantCase,
                ref holderCase,
                ref suppressionInclination);

            float difference = Math.Abs(claimantCase - holderCase);
            float suppressionWindow = Math.Max(5f, Math.Min(35f, 15f + suppressionInclination));
            if (canSuppress && difference < suppressionWindow)
                return ClaimFeudJudgment.Suppress;

            if (difference < 8f)
                return canSuppress ? ClaimFeudJudgment.Suppress : ClaimFeudJudgment.Abstain;

            return claimantCase > holderCase
                ? ClaimFeudJudgment.UpholdClaimant
                : ClaimFeudJudgment.UpholdHolder;
        }

        private static void ApplyRulerPersonalityToJudgment(
            Hero ruler,
            Clan claimant,
            Clan holder,
            FeudalTitleRecord title,
            FeudalClaimStrength strength,
            float claimantPower,
            float holderPower,
            ref float claimantCase,
            ref float holderCase,
            ref float suppressionInclination)
        {
            if (ruler == null)
                return;

            int honor = ruler.GetTraitLevel(DefaultTraits.Honor);
            if (honor != 0)
            {
                float legalBias = honor * C.ClaimFeudRulerHonorLegalBiasPerTrait;
                if (strength == FeudalClaimStrength.Strong || title.DeJureHolderClanId == claimant.StringId)
                    claimantCase += legalBias;
                if (title.DeJureHolderClanId == holder.StringId)
                    holderCase += legalBias;
            }

            int mercy = ruler.GetTraitLevel(DefaultTraits.Mercy);
            if (mercy != 0)
                suppressionInclination += mercy * C.ClaimFeudRulerMercySuppressionBiasPerTrait;

            int valor = ruler.GetTraitLevel(DefaultTraits.Valor);
            if (valor > 0)
            {
                if (claimantPower >= holderPower)
                    claimantCase += valor * C.ClaimFeudRulerPowerBiasPerTrait;
                else
                    holderCase += valor * C.ClaimFeudRulerPowerBiasPerTrait;
            }
            else if (valor < 0)
            {
                suppressionInclination += -valor * C.ClaimFeudRulerCautiousSuppressionBiasPerTrait;
            }

            int calculating = ruler.GetTraitLevel(DefaultTraits.Calculating);
            if (calculating > 0)
            {
                if (claimantPower >= holderPower)
                    claimantCase += calculating * C.ClaimFeudRulerCalculatingPowerBiasPerTrait;
                else
                    holderCase += calculating * C.ClaimFeudRulerCalculatingPowerBiasPerTrait;

                suppressionInclination += calculating * 2f;
            }
            else if (calculating < 0)
            {
                suppressionInclination -= -calculating * C.ClaimFeudRulerCautiousSuppressionBiasPerTrait;
            }
        }

        private static void ApplyRulingRelations(
            Hero ruler,
            Clan claimant,
            Clan holder,
            ClaimFeudJudgment judgment,
            ClaimFeudResponse claimantResponse,
            ClaimFeudResponse holderResponse)
        {
            if (ruler == null)
                return;

            switch (judgment)
            {
                case ClaimFeudJudgment.UpholdClaimant:
                    ChangeRelation(claimant?.Leader, ruler, C.ClaimFeudRulerUpholdRelationBonus, RelationMemorySources.UpheldMyTitleRights);
                    ChangeRelation(holder?.Leader, ruler, C.ClaimFeudRulerUpholdRelationPenalty, RelationMemorySources.RejectedMyTitleRights);
                    break;
                case ClaimFeudJudgment.UpholdHolder:
                    ChangeRelation(holder?.Leader, ruler, C.ClaimFeudRulerUpholdRelationBonus, RelationMemorySources.UpheldMyTitleRights);
                    ChangeRelation(claimant?.Leader, ruler, C.ClaimFeudRulerUpholdRelationPenalty, RelationMemorySources.RejectedMyTitleRights);
                    break;
                case ClaimFeudJudgment.Suppress:
                    ChangeRelation(claimant?.Leader, ruler, C.ClaimFeudRulerSuppressClaimantPenalty, RelationMemorySources.SuppressedMyClaim);
                    break;
                case ClaimFeudJudgment.Abstain:
                    ChangeRelation(claimant?.Leader, ruler, C.ClaimFeudRulerAbstainRelationPenalty, RelationMemorySources.WithheldFeudJudgment);
                    ChangeRelation(holder?.Leader, ruler, C.ClaimFeudRulerAbstainRelationPenalty, RelationMemorySources.WithheldFeudJudgment);
                    break;
            }

            if (claimantResponse == ClaimFeudResponse.Defy)
                ChangeRelation(claimant?.Leader, ruler, C.ClaimFeudRulerUpholdRelationPenalty, RelationMemorySources.DefiedFeudJudgment);
            if (holderResponse == ClaimFeudResponse.Defy)
                ChangeRelation(holder?.Leader, ruler, C.ClaimFeudRulerUpholdRelationPenalty, RelationMemorySources.DefiedFeudJudgment);
        }

        private static void ChangeRelation(Hero first, Hero second, int amount, string sourceId)
        {
            if (first == null || second == null || first == second || amount == 0)
                return;

            RelationMemoryService.ApplyChangeWithDefaultDuration(first, second, amount, false,
                sourceId, RelationMemoryScope.Personal);
        }

        private List<Clan> BuildFeudSide(
            FeudalTitleBehavior titleBehavior,
            Clan leader,
            Clan opponent,
            Kingdom kingdom,
            ClaimFeudRecord record = null)
        {
            List<Clan> side = new List<Clan>();
            if (leader == null)
                return side;

            if (record != null)
            {
                string encoded = leader.StringId == record.ClaimantClanId
                    ? record.ClaimantSupporterIds
                    : leader.StringId == record.HolderClanId
                        ? record.HolderSupporterIds
                        : null;
                if (!string.IsNullOrWhiteSpace(encoded))
                {
                    side.Add(leader);
                    foreach (Clan supporter in SplitClanIds(encoded).Select(ResolveClan).Distinct())
                    {
                        if (supporter == null
                            || record.HasWithdrawn(supporter.StringId)
                            || supporter == leader
                            || supporter == opponent
                            || supporter == kingdom?.RulingClan
                            || supporter.Kingdom != kingdom
                            || !IsValidFeudClan(supporter, out _)
                            || IsClanCommittedToArmedClaimFeud(supporter, record.RecordId))
                        {
                            continue;
                        }

                        side.Add(supporter);
                    }

                    return side;
                }
            }

            side.Add(leader);
            IEnumerable<Clan> realmClans = kingdom?.Clans?.AsEnumerable() ?? Enumerable.Empty<Clan>();
            Clan rulingClan = kingdom?.RulingClan;
            foreach (Clan clan in realmClans)
            {
                if (clan == null || clan == leader || clan == opponent || side.Contains(clan))
                    continue;

                if (clan == rulingClan)
                    continue;

                if (clan == Clan.PlayerClan)
                    continue;

                if (!IsValidFeudClan(clan, out _))
                    continue;

                if (IsClanCommittedToArmedClaimFeud(clan, record?.RecordId))
                    continue;

                if (WouldSupportFeudSide(titleBehavior, clan, leader, opponent))
                    side.Add(clan);
            }

            return side;
        }

        private void ResolveFeudCallsToArms(
            FeudalTitleBehavior titleBehavior,
            ClaimFeudRecord record,
            Clan claimant,
            Clan holder,
            FeudalTitleRecord title,
            Kingdom kingdom,
            out List<Clan> claimantSide,
            out List<Clan> holderSide)
        {
            claimantSide = new List<Clan> { claimant };
            holderSide = new List<Clan> { holder };
            if (titleBehavior == null || record == null || claimant == null || holder == null || kingdom == null)
                return;

            foreach (Clan candidate in kingdom.Clans.ToList())
            {
                if (candidate == null || candidate == claimant || candidate == holder
                    || candidate == kingdom.RulingClan || candidate == Clan.PlayerClan
                    || !IsValidFeudClan(candidate, out _)
                    || IsClanCommittedToArmedClaimFeud(candidate, record.RecordId))
                {
                    continue;
                }

                FeudCallAssessment claimantCall = AssessFeudCall(titleBehavior, candidate, claimant, holder);
                FeudCallAssessment holderCall = AssessFeudCall(titleBehavior, candidate, holder, claimant);
                if (claimantCall == null && holderCall == null)
                    continue;

                bool choseClaimant = holderCall == null
                    || (claimantCall != null && (claimantCall.Score > holderCall.Score
                        || (Math.Abs(claimantCall.Score - holderCall.Score) < 0.01f
                            && candidate.Leader.GetRelation(claimant.Leader) >= candidate.Leader.GetRelation(holder.Leader))));
                FeudCallAssessment chosen = choseClaimant ? claimantCall : holderCall;
                Clan chosenLeader = choseClaimant ? claimant : holder;
                Clan chosenOpponent = choseClaimant ? holder : claimant;
                bool joined = RollFeudCall(chosen.Score);

                if (joined)
                {
                    (choseClaimant ? claimantSide : holderSide).Add(candidate);
                    RelationMemoryService.ApplyChange(candidate.Leader, chosenLeader.Leader, C.ClaimFeudAiSupportRelationBonus, false,
                        RelationMemorySources.HonoredCallToArms, 10f, RelationMemoryScope.House, title?.Name?.ToString());
                    ShowFeudCallOutcome(candidate, chosenLeader, kingdom, accepted: true);
                    QueueFeudCallResponse(record, candidate, chosenLeader, chosenOpponent, chosen.Reason, true, title, choseClaimant);

                    FeudCallAssessment declinedAlternative = choseClaimant ? holderCall : claimantCall;
                    Clan alternativeLeader = choseClaimant ? holder : claimant;
                    if (declinedAlternative != null && alternativeLeader == Clan.PlayerClan)
                    {
                        ApplyFeudRefusalRelation(candidate, alternativeLeader, declinedAlternative.Reason, title?.Name?.ToString());
                        QueueFeudCallResponse(record, candidate, alternativeLeader, chosenLeader, declinedAlternative.Reason, false, title, !choseClaimant);
                    }
                }
                else
                {
                    ApplyFeudRefusalRelation(candidate, chosenLeader, chosen.Reason, title?.Name?.ToString());
                    ShowFeudCallOutcome(candidate, chosenLeader, kingdom, accepted: false);
                    QueueFeudCallResponse(record, candidate, chosenLeader, chosenOpponent, chosen.Reason, false, title, choseClaimant);

                    FeudCallAssessment declinedAlternative = choseClaimant ? holderCall : claimantCall;
                    Clan alternativeLeader = choseClaimant ? holder : claimant;
                    if (declinedAlternative != null && alternativeLeader == Clan.PlayerClan)
                    {
                        ApplyFeudRefusalRelation(candidate, alternativeLeader, declinedAlternative.Reason, title?.Name?.ToString());
                        QueueFeudCallResponse(record, candidate, alternativeLeader, chosenLeader, declinedAlternative.Reason, false, title, !choseClaimant);
                    }
                }
            }

            record.RecordSupporters(
                RebellionPowerHelper.CalculateFactionPower(claimantSide),
                RebellionPowerHelper.CalculateFactionPower(holderSide),
                string.Join(",", claimantSide.Select(clan => clan.StringId)),
                string.Join(",", holderSide.Select(clan => clan.StringId)));
        }

        private FeudCallAssessment AssessFeudCall(
            FeudalTitleBehavior titleBehavior,
            Clan supporter,
            Clan sideLeader,
            Clan opponent)
        {
            if (supporter?.Leader == null || sideLeader?.Leader == null)
                return null;

            bool directVassal = IsDirectTitleVassalOf(titleBehavior, supporter, sideLeader);
            bool marriage = MarriageAllianceHelper.HasMarriageAlliance(supporter, sideLeader);
            bool dynasticKin = AreCloseDynasticKin(supporter, sideLeader);
            int sideRelation = supporter.Leader.GetRelation(sideLeader.Leader);
            bool friend = sideRelation >= C.ClaimFeudSupportFriendRelationThreshold;
            if (!directVassal && !marriage && !dynasticKin && !friend)
                return null;

            float score = 0f;
            if (directVassal)
                score += C.ClaimFeudCallVassalSupport;
            if (marriage)
                score += C.ClaimFeudCallMarriageSupport;
            if (dynasticKin)
                score += C.ClaimFeudCallDynasticKinSupport;
            if (friend)
            {
                score += C.ClaimFeudCallFriendSupportBase
                    + ((sideRelation - C.ClaimFeudSupportFriendRelationThreshold) * C.ClaimFeudCallFriendSupportScale);
            }

            // Shared ideology can make an existing ally more receptive, but it never
            // creates eligibility for a feud call without a personal or feudal tie.
            FactionManagerBehavior factionManager = Campaign.Current?.GetCampaignBehavior<FactionManagerBehavior>();
            FactionObject supporterIdeology = factionManager?.GetIdeologicalFaction(supporter);
            if (supporterIdeology != null && supporterIdeology == factionManager.GetIdeologicalFaction(sideLeader))
                score += C.TreasonSolidaritySameFactionSupport;

            int opponentRelation = opponent?.Leader != null ? supporter.Leader.GetRelation(opponent.Leader) : 0;
            score += MathF.Clamp(
                (sideRelation - opponentRelation) * C.ClaimFeudCallRelationComparisonScale,
                -C.ClaimFeudCallRelationComparisonCap,
                C.ClaimFeudCallRelationComparisonCap);

            CivilWarSolidarityReason reason = directVassal
                ? CivilWarSolidarityReason.DirectVassal
                : marriage
                    ? CivilWarSolidarityReason.MarriageAlliance
                    : dynasticKin
                        ? CivilWarSolidarityReason.DynasticKin
                        : CivilWarSolidarityReason.Friendship;
            return new FeudCallAssessment(score, reason);
        }

        private static bool RollFeudCall(float score)
        {
            float chance = CivilWarSolidarityHelper.CalculateJoinChance(score);
            return chance >= 1f || (chance > 0f && MBRandom.RandomFloat <= chance);
        }

        private static void ApplyFeudRefusalRelation(Clan supporter, Clan sideLeader, CivilWarSolidarityReason reason, string contextText)
        {
            int penalty;
            switch (reason)
            {
                case CivilWarSolidarityReason.DirectVassal:
                    penalty = C.ClaimFeudAiRefuseVassalRelationPenalty;
                    break;
                case CivilWarSolidarityReason.MarriageAlliance:
                    penalty = C.ClaimFeudAiRefuseMarriageRelationPenalty;
                    break;
                case CivilWarSolidarityReason.DynasticKin:
                    penalty = C.ClaimFeudAiRefuseKinRelationPenalty;
                    break;
                case CivilWarSolidarityReason.Friendship:
                    penalty = C.ClaimFeudAiRefuseFriendRelationPenalty;
                    break;
                default:
                    penalty = 0;
                    break;
            }

            RelationMemoryService.ApplyChange(supporter?.Leader, sideLeader?.Leader, penalty, false,
                RelationMemorySources.RefusedCallToArms, 10f, RelationMemoryScope.House, contextText);
        }

        private static void ShowFeudCallOutcome(Clan supporter, Clan sideLeader, Kingdom kingdom, bool accepted)
        {
            TextObject text = accepted
                ? new TextObject("{=BC_ClaimFeud_AllyJoined}{LORD_NAME} of the {CLAN_NAME} has answered {SIDE_LEADER}'s call and pledged support in the feud.")
                : new TextObject("{=BC_ClaimFeud_AllyRefused}{LORD_NAME} of the {CLAN_NAME} has refused {SIDE_LEADER}'s call to arms and will remain apart from the feud.");
            text.SetTextVariable("LORD_NAME", supporter?.Leader?.Name ?? supporter?.Name ?? new TextObject("?"));
            text.SetTextVariable("CLAN_NAME", supporter?.Name ?? new TextObject("?"));
            text.SetTextVariable("SIDE_LEADER", sideLeader?.Leader?.Name ?? sideLeader?.Name ?? new TextObject("?"));
            BellumCivileNotifications.Show(
                text,
                accepted ? BellumNotificationColors.Rebellion : BellumNotificationColors.Warning,
                primaryKingdom: kingdom,
                primaryClan: supporter,
                secondaryClan: sideLeader);
        }

        private static void QueueFeudCallResponse(
            ClaimFeudRecord record,
            Clan responder,
            Clan sideLeader,
            Clan opponent,
            CivilWarSolidarityReason reason,
            bool accepted,
            FeudalTitleRecord title,
            bool claimantSide)
        {
            Campaign.Current?.GetCampaignBehavior<ConflictCallResponseBehavior>()
                ?.QueueFeudResponse(record, responder, sideLeader, opponent, reason, accepted, title, defending: !claimantSide);
        }

        private bool WouldSupportFeudSide(FeudalTitleBehavior titleBehavior, Clan supporter, Clan sideLeader, Clan opponent)
        {
            if (supporter?.Leader == null || sideLeader?.Leader == null)
                return false;

            bool supportsLeader = HasFeudSupportTie(titleBehavior, supporter, sideLeader);
            bool supportsOpponent = HasFeudSupportTie(titleBehavior, supporter, opponent);
            if (supportsLeader && !supportsOpponent)
                return true;
            if (!supportsLeader)
                return false;

            int leaderRelation = supporter.Leader.GetRelation(sideLeader.Leader);
            int opponentRelation = opponent?.Leader != null ? supporter.Leader.GetRelation(opponent.Leader) : -100;
            return leaderRelation > opponentRelation;
        }

        private bool HasFeudSupportTie(FeudalTitleBehavior titleBehavior, Clan supporter, Clan sideLeader)
        {
            if (supporter == null || sideLeader == null || supporter == sideLeader)
                return false;

            if (MarriageAllianceHelper.HasMarriageAlliance(supporter, sideLeader))
                return true;

            if (supporter.Leader != null
                && sideLeader.Leader != null
                && supporter.Leader.GetRelation(sideLeader.Leader) >= C.ClaimFeudSupportFriendRelationThreshold)
            {
                return true;
            }

            if (AreCloseDynasticKin(supporter, sideLeader))
                return true;

            return IsDirectTitleVassalOf(titleBehavior, supporter, sideLeader);
        }

        private static bool IsDirectTitleVassalOf(FeudalTitleBehavior titleBehavior, Clan possibleVassal, Clan possibleLiege)
        {
            return CivilWarSolidarityHelper.IsImmediateVassalOf(titleBehavior, possibleVassal, possibleLiege);
        }

        private void TryShowPlayerSupportInvitation(
            FeudalTitleBehavior titleBehavior,
            ClaimFeudRecord record,
            Clan claimant,
            Clan holder,
            FeudalTitleRecord title,
            Kingdom kingdom)
        {
            Clan playerClan = Clan.PlayerClan;
            if (record?.HasWithdrawn(playerClan?.StringId) == true) return;
            if (titleBehavior == null
                || record == null
                || claimant == null
                || holder == null
                || title == null
                || kingdom == null
                || playerClan?.Leader == null
                || !IsValidFeudClan(playerClan, out _)
                || playerClan.Kingdom != kingdom
                || playerClan == claimant
                || playerClan == holder
                || playerClan == kingdom.RulingClan)
            {
                return;
            }

            bool supportsClaimant = WouldSupportFeudSide(titleBehavior, playerClan, claimant, holder);
            bool supportsHolder = WouldSupportFeudSide(titleBehavior, playerClan, holder, claimant);
            if (!supportsClaimant && !supportsHolder)
                return;

            bool supportClaimant = ChoosePlayerInvitationSide(playerClan, claimant, holder, supportsClaimant, supportsHolder);
            Clan invitingClan = supportClaimant ? claimant : holder;
            Clan opposingClan = supportClaimant ? holder : claimant;
            string inquiryKey = $"{record.RecordId}:{invitingClan.StringId}";
            if (!_shownPlayerSupportInquiries.Add(inquiryKey))
                return;

            TextObject titleText = new TextObject("{=BC_ClaimFeud_PlayerSupport_Title}A Call from {CLAN_NAME}");
            titleText.SetTextVariable("CLAN_NAME", invitingClan.Name);

            TextObject balanceReport = ConflictBalanceReportHelper.Build(
                ConflictBalanceReportHelper.BuildLeaderSupportersSideName(claimant.Leader?.Name ?? claimant.Name),
                record.ClaimantSidePower,
                ConflictBalanceReportHelper.BuildLeaderSupportersSideName(holder.Leader?.Name ?? holder.Name),
                record.HolderSidePower,
                playerUncommitted: true);

            TextObject description = new TextObject("{=BC_ClaimFeud_PlayerSupport_Desc}{LEADER_NAME} of the {CLAN_NAME} has called upon your house to honor its ties and support the feud against the {OPPONENT_CLAN} over the {TITLE_NAME}.\n\n{BALANCE_REPORT}\n\nIf you answer, your clan will be counted among their supporters when the dispute is brought before the ruler. If you refuse, the slight will not be forgotten.");
            description.SetTextVariable("LEADER_NAME", invitingClan.Leader?.Name ?? invitingClan.Name);
            description.SetTextVariable("CLAN_NAME", invitingClan.Name);
            description.SetTextVariable("OPPONENT_CLAN", opposingClan.Name);
            description.SetTextVariable("TITLE_NAME", new TextObject("{=!}" + FeudalTitleDisplayHelper.FormatTitleName(title, invitingClan)));
            description.SetTextVariable("BALANCE_REPORT", balanceReport);

            InformationManager.ShowInquiry(new InquiryData(
                titleText.ToString(),
                description.ToString(),
                true,
                true,
                new TextObject("{=BC_ClaimFeud_PlayerSupport_Accept}Pledge Support").ToString(),
                new TextObject("{=BC_ClaimFeud_PlayerSupport_Refuse}Refuse").ToString(),
                () => ResolvePlayerSupportInvitation(record.RecordId, supportClaimant, accepted: true),
                () => ResolvePlayerSupportInvitation(record.RecordId, supportClaimant, accepted: false)), true);
        }

        private static bool ChoosePlayerInvitationSide(Clan playerClan, Clan claimant, Clan holder, bool supportsClaimant, bool supportsHolder)
        {
            if (supportsClaimant && !supportsHolder)
                return true;
            if (supportsHolder && !supportsClaimant)
                return false;

            int claimantRelation = claimant?.Leader != null && playerClan?.Leader != null
                ? playerClan.Leader.GetRelation(claimant.Leader)
                : 0;
            int holderRelation = holder?.Leader != null && playerClan?.Leader != null
                ? playerClan.Leader.GetRelation(holder.Leader)
                : 0;
            return claimantRelation >= holderRelation;
        }

        private void ResolvePlayerSupportInvitation(string recordId, bool supportClaimant, bool accepted)
        {
            ClaimFeudRecord record = _feuds.FirstOrDefault(feud => feud != null && feud.RecordId == recordId);
            if (record == null || !CanAnswerPlayerSupportSummons(record.State))
                return;

            Clan playerClan = Clan.PlayerClan;
            Clan claimant = ResolveClan(record.ClaimantClanId);
            Clan holder = ResolveClan(record.HolderClanId);
            Clan invitingClan = supportClaimant ? claimant : holder;
            FeudalTitleRecord title = FeudalTitleBehavior.Instance?.GetTitle(record.TargetTitleId);
            if (!CanPlayerSupportFeud(record, playerClan, claimant, holder) || invitingClan?.Leader == null)
                return;

            if (accepted)
            {
                AddPlayerSupportToRecord(record, supportClaimant);
                RelationMemoryService.ApplyChange(playerClan.Leader, invitingClan.Leader, C.ClaimFeudPlayerSupportRelationBonus, false,
                    RelationMemorySources.HonoredCallToArms, 10f, RelationMemoryScope.House, title?.Name?.ToString());
                BellumCivileDebug.Trace(
                    "claim feud",
                    $"Player accepted claim feud support summons; feud={record.RecordId}; side={(supportClaimant ? "claimant" : "holder")}; clan={invitingClan.StringId}.",
                    requestInGameDisplay: true);
            }
            else
            {
                RelationMemoryService.ApplyChange(playerClan.Leader, invitingClan.Leader, C.ClaimFeudPlayerRefuseRelationPenalty, false,
                    RelationMemorySources.RefusedCallToArms, 10f, RelationMemoryScope.House, title?.Name?.ToString());
                BellumCivileDebug.Trace(
                    "claim feud",
                    $"Player refused claim feud support summons; feud={record.RecordId}; side={(supportClaimant ? "claimant" : "holder")}; clan={invitingClan.StringId}.",
                    requestInGameDisplay: true);
            }
        }

        private void AddPlayerSupportToRecord(ClaimFeudRecord record, bool supportClaimant)
        {
            if (record == null || !CanPlayerSupportFeud(record, Clan.PlayerClan,
                ResolveClan(record.ClaimantClanId), ResolveClan(record.HolderClanId)))
                return;

            List<string> claimantIds = SplitClanIds(record.ClaimantSupporterIds);
            List<string> holderIds = SplitClanIds(record.HolderSupporterIds);
            if (!claimantIds.Contains(record.ClaimantClanId))
                claimantIds.Insert(0, record.ClaimantClanId);
            if (!holderIds.Contains(record.HolderClanId))
                holderIds.Insert(0, record.HolderClanId);
            claimantIds.RemoveAll(id => id == Clan.PlayerClan.StringId);
            holderIds.RemoveAll(id => id == Clan.PlayerClan.StringId);

            if (supportClaimant)
                claimantIds.Add(Clan.PlayerClan.StringId);
            else
                holderIds.Add(Clan.PlayerClan.StringId);

            List<Clan> claimantSide = claimantIds.Select(ResolveClan).Where(clan => clan != null).Distinct().ToList();
            List<Clan> holderSide = holderIds.Select(ResolveClan).Where(clan => clan != null).Distinct().ToList();
            record.RecordSupporters(
                RebellionPowerHelper.CalculateFactionPower(claimantSide),
                RebellionPowerHelper.CalculateFactionPower(holderSide),
                string.Join(",", claimantIds.Distinct()),
                string.Join(",", holderIds.Distinct()));
        }

        private static void RefreshRecordedSupporters(
            ClaimFeudRecord record,
            List<Clan> claimantSide,
            List<Clan> holderSide,
            float claimantPower,
            float holderPower)
        {
            if (record == null)
                return;

            record.RecordSupporters(
                claimantPower,
                holderPower,
                string.Join(",", (claimantSide ?? new List<Clan>()).Where(clan => clan != null).Select(clan => clan.StringId).Distinct()),
                string.Join(",", (holderSide ?? new List<Clan>()).Where(clan => clan != null).Select(clan => clan.StringId).Distinct()));
        }

        private static List<string> SplitClanIds(string encoded)
        {
            return string.IsNullOrWhiteSpace(encoded)
                ? new List<string>()
                : encoded.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(id => id.Trim())
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Distinct()
                    .ToList();
        }

        private static bool CanPlayerSupportFeud(ClaimFeudRecord record, Clan playerClan, Clan claimant, Clan holder)
        {
            if (record == null || record.HasWithdrawn(playerClan?.StringId) || !CanAnswerPlayerSupportSummons(record.State)
                || !IsValidFeudClan(playerClan, out _))
                return false;

            Kingdom kingdom = playerClan.Kingdom;
            return kingdom.StringId == record.ParentKingdomId
                && claimant?.Kingdom == kingdom && holder?.Kingdom == kingdom
                && playerClan != claimant && playerClan != holder && playerClan != kingdom.RulingClan;
        }

        private static bool CanAnswerPlayerSupportSummons(ClaimFeudState state)
        {
            return state == ClaimFeudState.Agitating
                || state == ClaimFeudState.PetitionReady
                || state == ClaimFeudState.AwaitingPlayerRulerJudgment
                || state == ClaimFeudState.Paused;
        }

        private static bool IsSideReadyToDefy(Clan sideLeader, float sidePower, float opposingPower)
        {
            float threshold = RebellionPowerHelper.CalculateRebellionPowerThreshold(sideLeader?.Leader);
            return sidePower >= Math.Max(1f, opposingPower) * threshold;
        }

        private static bool IsHolderReadyToDefyRevocation(
            FeudalTitleRecord title,
            Clan revoker,
            Clan holder,
            FeudalClaimStrength strength,
            float revokerPower,
            float holderPower)
        {
            if (holder?.Leader == null || revoker?.Leader == null)
                return false;

            float threshold = RebellionPowerHelper.CalculateRebellionPowerThreshold(holder.Leader);

            if (string.Equals(title?.DeJureHolderClanId, holder.StringId, StringComparison.Ordinal))
                threshold -= 0.15f;
            if (strength == FeudalClaimStrength.Strong)
                threshold += 0.10f;
            else
                threshold -= 0.10f;

            int relation = holder.Leader.GetRelation(revoker.Leader);
            if (relation <= -70)
                threshold -= 0.25f;
            else if (relation <= -40)
                threshold -= 0.15f;
            else if (relation >= 60)
                threshold += 0.25f;
            else if (relation >= 30)
                threshold += 0.15f;

            int honor = holder.Leader.GetTraitLevel(DefaultTraits.Honor);
            if (strength == FeudalClaimStrength.Strong)
                threshold += Math.Max(0, honor) * 0.10f;
            if (string.Equals(title?.DeJureHolderClanId, holder.StringId, StringComparison.Ordinal))
                threshold -= Math.Max(0, honor) * 0.05f;

            int valor = holder.Leader.GetTraitLevel(DefaultTraits.Valor);
            threshold -= valor * 0.05f;

            int mercy = holder.Leader.GetTraitLevel(DefaultTraits.Mercy);
            threshold += mercy * 0.05f;

            if (MarriageAllianceHelper.HasMarriageAlliance(holder, revoker))
                threshold += 0.25f;

            threshold = Math.Max(0.35f, Math.Min(2.50f, threshold));
            return holderPower >= Math.Max(1f, revokerPower) * threshold;
        }

        public bool TryRunAutonomousLandedAmbitionEvaluationForClan(FeudalTitleBehavior titleBehavior, Clan clan, out string report)
        {
            report = null;
            EnsureCollectionsInitialized();

            if (clan != null && (clan == Clan.PlayerClan || clan.Leader == Hero.MainHero))
            {
                report = "player clan controls its own landed ambitions";
                return false;
            }

            if (titleBehavior == null)
            {
                report = "title behavior unavailable";
                return false;
            }

            if (!IsValidFeudClan(clan, out string clanReason))
            {
                report = clanReason;
                return false;
            }

            if (EvaluateClanForClaimedRevocation(titleBehavior, clan))
            {
                report = "claimed revocation action selected";
                return true;
            }

            bool hadPressableClaim = false;
            if (C.ClaimFeudAiEnabled)
            {
                bool feudStarted = EvaluateClanForFeud(titleBehavior, clan, out hadPressableClaim);
                if (feudStarted || hadPressableClaim)
                {
                    report = feudStarted
                        ? "claim feud action selected"
                        : "pressable claim found";
                    return feudStarted;
                }
            }

            FeudalClaimFabricationBehavior fabricationBehavior = Campaign.Current?.GetCampaignBehavior<FeudalClaimFabricationBehavior>();
            if (fabricationBehavior == null)
            {
                report = "fabrication behavior unavailable";
                return false;
            }

            bool fabricationStarted = fabricationBehavior.TryRunAutonomousAiEvaluationForClan(titleBehavior, clan, out string fabricationReport);
            report = fabricationReport;
            if (!fabricationStarted)
            {
                BellumCivileDebug.TraceIfEnabled(
                    "fabrication",
                    $"AI found no claim fabrication action from landed ambition pass; clan={clan.Name} ({clan.StringId}); reason={fabricationReport ?? "no report"}.",
                requestInGameDisplay: true);
            }

            return fabricationStarted;
        }

        private bool EvaluateClanForClaimedRevocation(FeudalTitleBehavior titleBehavior, Clan clan)
        {
            if (!C.ClaimFeudAiEnabled || titleBehavior == null || clan?.Leader == null || clan == Clan.PlayerClan)
                return false;

            List<RevocationCandidate> candidates = BuildRevocationCandidates(titleBehavior, clan)
                .Where(candidate => candidate.Score >= C.FeudalTitleRevocationAiThreshold)
                .OrderByDescending(candidate => candidate.Score)
                .ThenByDescending(candidate => candidate.Preview.ClaimStrength)
                .ThenByDescending(candidate => candidate.Title.TitleType)
                .ToList();

            if (candidates.Count == 0)
                return false;

            RevocationCandidate best = candidates[0];
            float chance = Math.Min(0.50f, 0.10f + (best.Score - C.FeudalTitleRevocationAiThreshold) * 0.01f);
            float roll = MBRandom.RandomFloat;
            if (roll >= chance)
            {
                BellumCivileDebug.TraceIfEnabled(
                    "claim feud",
                    $"AI declined claimed title revocation after roll; clan={clan.Name} ({clan.StringId}); title={FeudalTitleDisplayHelper.FormatTitleName(best.Title, clan)}; holder={best.Preview.HolderClan?.Name}; score={best.Score:0}/{C.FeudalTitleRevocationAiThreshold:0}; chance={chance:P0}; roll={roll:P0}; reasons={string.Join(", ", best.Reasons)}.",
                    requestInGameDisplay: true);
                return false;
            }

            if (best.Preview.HolderClan == Clan.PlayerClan)
            {
                ShowPlayerRevocationDemandInquiry(titleBehavior, clan, best.Title, best.Preview, best.Score, best.Reasons);
                return true;
            }

            if (!FeudalTitlePlayerActionService.TryExecuteRevocation(clan, best.Title, out bool holderDefied, out string reason))
            {
                BellumCivileDebug.TraceIfEnabled(
                    "claim feud",
                    $"AI failed claimed title revocation; clan={clan.Name} ({clan.StringId}); title={FeudalTitleDisplayHelper.FormatTitleName(best.Title, clan)}; reason={reason ?? "unknown"}.",
                    requestInGameDisplay: true);
                return false;
            }

            BellumCivileDebug.Trace(
                "claim feud",
                $"AI demanded claimed title revocation; clan={clan.Name} ({clan.StringId}); title={FeudalTitleDisplayHelper.FormatTitleName(best.Title, clan)}; holder={best.Preview.HolderClan?.Name}; defied={holderDefied}; score={best.Score:0}; reasons={string.Join(", ", best.Reasons)}.",
                requestInGameDisplay: true);
            return true;
        }

        private bool TryGetTemporaryPauseReason(
            ClaimFeudRecord record,
            Clan claimant,
            Clan holder,
            out string reason)
        {
            reason = null;
            if (record == null || record.State == ClaimFeudState.WarActive)
                return false;

            Kingdom parent = ResolveKingdom(record.ParentKingdomId);
            if (parent == null || parent.IsEliminated)
                return false;

            if (claimant?.Kingdom != parent || holder?.Kingdom != parent)
            {
                reason = "one of the principal houses is serving in another internal conflict";
                return true;
            }

            if (HasActiveCivilWar(parent))
            {
                reason = "civil war has suspended private litigation";
                return true;
            }

            if (IsClanCommittedToArmedClaimFeud(claimant, record.RecordId)
                || IsClanCommittedToArmedClaimFeud(holder, record.RecordId))
            {
                reason = "one of the principal houses is already fighting another private war";
                return true;
            }

            ClaimFeudState effectiveState = record.State == ClaimFeudState.Paused
                ? record.ResumeState
                : record.State;
            if (effectiveState == ClaimFeudState.DefiedPendingWar && CountExternalWars(parent) > 0)
            {
                reason = "the realm is at foreign war, so private banners cannot yet be raised";
                return true;
            }

            return false;
        }

        private static bool IsClanAttachedToParentRealm(ClaimFeudRecord record, Clan clan, Kingdom parent)
        {
            if (record == null || clan == null || parent == null)
                return false;
            if (clan.Kingdom == parent)
                return true;
            if (clan.Kingdom == null || !BellumKingdomVisibilityHelper.IsTemporaryBellumKingdom(clan.Kingdom))
                return false;

            return IsTemporaryRealmForFeud(record, clan.Kingdom)
                || Campaign.Current?.GetCampaignBehavior<ClaimFeudWarBehavior>()?.GetParentKingdomForTemporaryRealm(clan.Kingdom) == parent
                || Campaign.Current?.GetCampaignBehavior<FactionManagerBehavior>()?.GetFactionByRebelKingdom(clan.Kingdom)?.ParentKingdom == parent;
        }

        private static bool IsTemporaryRealmForFeud(ClaimFeudRecord record, Kingdom kingdom)
        {
            return record != null
                && kingdom != null
                && !string.IsNullOrWhiteSpace(kingdom.StringId)
                && kingdom.StringId.StartsWith("bc_feud_" + record.RecordId, StringComparison.Ordinal);
        }

        private void InvalidateFeud(ClaimFeudRecord record, string reason)
        {
            if (record == null || !IsActiveFeudState(record.State))
                return;

            if (record.State == ClaimFeudState.WarActive)
            {
                Campaign.Current?.GetCampaignBehavior<ClaimFeudWarBehavior>()?
                    .TryCancelActiveWarForFeud(record.RecordId, $"invalid feud state: {reason ?? "unknown"}");
            }

            _yearlyInvalidated++;
            record.SetState(ClaimFeudState.Resolved);
            record.SetDebugReason($"{record.DebugReason}; invalidated ({reason ?? "unknown"})");
            BellumCivileLogger.Log($"Claim feud invalidated; feud={record.RecordId}; pressure={record.Pressure:0.0}; reason={reason ?? "unknown"}.");
        }

        private List<RevocationCandidate> BuildRevocationCandidates(FeudalTitleBehavior titleBehavior, Clan clan)
        {
            List<RevocationCandidate> candidates = new List<RevocationCandidate>();
            if (titleBehavior == null || clan?.Leader == null)
                return candidates;

            IEnumerable<FeudalTitleRecord> parentTitles = titleBehavior.GetTitlesHeldByClan(clan, deJure: false)
                .Where(title => title != null && title.IsActive)
                .OrderByDescending(title => title.TitleType);

            HashSet<string> seen = new HashSet<string>();
            foreach (FeudalTitleRecord parentTitle in parentTitles)
            {
                foreach (FeudalTitleRecord child in titleBehavior.GetChildTitles(parentTitle, FeudalHierarchyMode.DeFacto))
                {
                    if (child == null || !child.IsActive || !seen.Add(child.TitleId))
                        continue;

                    FeudalRevocationPreview preview = FeudalTitlePlayerActionService.GetRevocationPreview(clan, child);
                    if (!preview.CanRevoke)
                        continue;

                    float score = CalculateRevocationScore(clan, child, preview, out List<string> reasons);
                    candidates.Add(new RevocationCandidate(child, preview, score, reasons));
                }
            }

            return candidates;
        }

        private static float CalculateRevocationScore(Clan revoker, FeudalTitleRecord title, FeudalRevocationPreview preview, out List<string> reasons)
        {
            reasons = new List<string>();
            Hero leader = revoker?.Leader;
            Clan holder = preview?.HolderClan;
            if (leader == null || holder?.Leader == null || title == null || preview == null)
                return 0f;

            int honor = leader.GetTraitLevel(DefaultTraits.Honor);
            if (honor >= 2 && !(preview.ClaimStrength == FeudalClaimStrength.Strong && holder.Leader.GetRelation(leader) <= -80))
            {
                reasons.Add("high honor blocks revocation");
                return 0f;
            }
            if (honor >= 1 && preview.ClaimStrength == FeudalClaimStrength.Weak)
            {
                reasons.Add("honor blocks weak claim revocation");
                return 0f;
            }

            float score = preview.ClaimStrength == FeudalClaimStrength.Strong ? 35f : 15f;
            reasons.Add(preview.ClaimStrength == FeudalClaimStrength.Strong ? "strong claim +35" : "weak claim +15");

            float tier = ((int)title.TitleType + 1) * 6f;
            score += tier;
            reasons.Add($"title rank +{tier:0}");

            int relation = leader.GetRelation(holder.Leader);
            float relationScore = Math.Max(-20f, Math.Min(30f, -relation * 0.35f));
            if (Math.Abs(relationScore) >= 0.5f)
            {
                score += relationScore;
                reasons.Add(relationScore >= 0f ? $"hostile vassal +{relationScore:0}" : $"friendly vassal {relationScore:0}");
            }

            float powerTotal = Math.Max(1f, preview.RevokerPower + preview.HolderPower);
            float powerScore = preview.RevokerPower / powerTotal * 20f;
            score += powerScore;
            reasons.Add($"side strength +{powerScore:0}");

            int calculating = leader.GetTraitLevel(DefaultTraits.Calculating);
            if (calculating > 0)
            {
                float value = calculating * 8f;
                score += value;
                reasons.Add($"calculating +{value:0}");
            }
            else if (calculating < 0)
            {
                float value = -calculating * 5f;
                score += value;
                reasons.Add($"impulsive +{value:0}");
            }

            int mercy = leader.GetTraitLevel(DefaultTraits.Mercy);
            if (mercy > 0)
            {
                float value = mercy * 8f;
                score -= value;
                reasons.Add($"merciful -{value:0}");
            }

            if (honor > 0)
            {
                float value = honor * 15f;
                score -= value;
                reasons.Add($"honorable -{value:0}");
            }
            else if (honor < 0)
            {
                float value = -honor * 12f;
                score += value;
                reasons.Add($"dishonorable +{value:0}");
            }

            if (MarriageAllianceHelper.HasMarriageAlliance(revoker, holder))
            {
                score -= 35f;
                reasons.Add("marriage alliance -35");
            }

            if (preview.HolderLikelyDefies && preview.RevokerPower < preview.HolderPower)
            {
                score -= 25f;
                reasons.Add("dangerous defiance -25");
            }

            return score;
        }

        private bool EvaluateClanForFeud(FeudalTitleBehavior titleBehavior, Clan clan, out bool hadPressableClaim)
        {
            hadPressableClaim = false;
            if (clan == null || clan == Clan.PlayerClan || clan.Leader == Hero.MainHero)
                return false;

            _yearlyEvaluations++;
            if (!TryFindBestFeudCandidate(titleBehavior, clan, out ClaimFeudCandidate best, out string reason))
            {
                BellumCivileDebug.TraceIfEnabled("claim feud", $"AI found no claim feud target; clan={clan.Name} ({clan.StringId}); reason={reason}.", requestInGameDisplay: true);
                return false;
            }

            hadPressableClaim = true;
            return StartFeud(best, "ai_evaluation") != null;
        }

        private ClaimFeudRecord StartFeud(ClaimFeudCandidate candidate, string source)
        {
            if (candidate?.Claimant?.Kingdom == null || HasActiveCivilWar(candidate.Claimant.Kingdom))
            {
                BellumCivileDebug.TraceIfEnabled(
                    "claim feud",
                    $"Claim feud start skipped because civil war has suspended private litigation; claimant={candidate?.Claimant?.Name}; source={source ?? "unknown"}.",
                    requestInGameDisplay: true);
                return null;
            }

            float currentDay = CurrentDay;
            string recordId = BuildRecordId(candidate.Claimant, candidate.Holder, candidate.Title);
            ClaimFeudRecord existing = _feuds.FirstOrDefault(feud => feud != null && feud.RecordId == recordId);
            if (existing != null)
            {
                if (IsActiveFeudState(existing.State))
                    return existing;

                existing.Reactivate(
                    candidate.Claimant.Kingdom?.StringId,
                    candidate.Strength,
                    C.ClaimFeudInitialPressure,
                    currentDay,
                    candidate.SourceClaimId,
                    $"{source}; reactivated; score={candidate.Score:0}; {string.Join(", ", candidate.Reasons)}");
                _yearlyAgitationsReactivated++;
                NotificationHelper.ShowClaimFeudStarted(candidate.Claimant, candidate.Holder, candidate.Title);
                BellumCivileDebug.Trace(
                    "claim feud",
                    $"Claim feud agitation reactivated; claimant={candidate.Claimant.Name} ({candidate.Claimant.StringId}); holder={candidate.Holder.Name} ({candidate.Holder.StringId}); title={candidate.Title.Name} ({candidate.Title.TitleId}); strength={candidate.Strength}; score={candidate.Score:0}; reasons={string.Join(", ", candidate.Reasons)}.",
                    requestInGameDisplay: true);
                return existing;
            }

            ClaimFeudRecord record = new ClaimFeudRecord(
                recordId,
                candidate.Claimant.Kingdom?.StringId,
                candidate.Claimant.StringId,
                candidate.Holder.StringId,
                candidate.Title.TitleId,
                candidate.Strength,
                C.ClaimFeudInitialPressure,
                currentDay,
                candidate.SourceClaimId,
                $"{source}; score={candidate.Score:0}; {string.Join(", ", candidate.Reasons)}");

            _feuds.Add(record);
            _yearlyAgitationsStarted++;
            NotificationHelper.ShowClaimFeudStarted(candidate.Claimant, candidate.Holder, candidate.Title);
            BellumCivileDebug.Trace(
                "claim feud",
                $"Claim feud agitation started; claimant={candidate.Claimant.Name} ({candidate.Claimant.StringId}); holder={candidate.Holder.Name} ({candidate.Holder.StringId}); title={candidate.Title.Name} ({candidate.Title.TitleId}); strength={candidate.Strength}; score={candidate.Score:0}; reasons={string.Join(", ", candidate.Reasons)}.",
                requestInGameDisplay: true);
            return record;
        }

        private bool TryFindBestFeudCandidate(FeudalTitleBehavior titleBehavior, Clan clan, out ClaimFeudCandidate bestCandidate, out string reason)
        {
            bestCandidate = null;
            reason = null;

            if (!CanStartClaimFeud(clan, out reason))
                return false;

            List<ClaimFeudCandidate> candidates = GetRankedFeudCandidates(titleBehavior, clan, requireThreshold: false);

            if (candidates.Count == 0)
            {
                _yearlyNoActionableCandidate++;
                reason = "no actionable same-realm claim was found";
                return false;
            }

            bestCandidate = candidates[0];
            if (bestCandidate.Score < C.ClaimFeudStartThreshold)
            {
                _yearlyBelowThreshold++;
                reason = $"best actionable claim scored {bestCandidate.Score:0}/{C.ClaimFeudStartThreshold:0}";
                bestCandidate = null;
                return false;
            }

            _yearlyPressableCandidates++;
            return true;
        }

        private List<ClaimFeudCandidate> GetRankedFeudCandidates(FeudalTitleBehavior titleBehavior, Clan clan, bool requireThreshold)
        {
            IEnumerable<ClaimFeudCandidate> candidates = BuildFeudCandidates(titleBehavior, clan);
            if (requireThreshold)
                candidates = candidates.Where(candidate => candidate.Score >= C.ClaimFeudStartThreshold);

            return candidates
                .OrderByDescending(candidate => candidate.Score)
                .ThenByDescending(candidate => candidate.Strength)
                .ThenByDescending(candidate => candidate.Title.TitleType)
                .ThenBy(candidate => candidate.Title.Name ?? string.Empty)
                .ToList();
        }

        private List<ClaimFeudCandidate> BuildFeudCandidates(FeudalTitleBehavior titleBehavior, Clan clan)
        {
            List<ClaimFeudCandidate> candidates = new List<ClaimFeudCandidate>();
            HashSet<string> seenTitles = new HashSet<string>();

            foreach (FeudalClaimRecord claim in titleBehavior.GetActiveClaimsByClan(clan))
            {
                FeudalTitleRecord title = titleBehavior.GetTitle(claim.TargetTitleId);
                if (title == null || !title.IsActive || title.IsDeliberatelyDissolved || !seenTitles.Add(title.TitleId))
                    continue;

                if (TryBuildCandidate(titleBehavior, clan, title, claim.Strength, claim.ClaimId, out ClaimFeudCandidate candidate))
                    candidates.Add(candidate);
            }

            foreach (FeudalTitleRecord title in titleBehavior.GetTitlesHeldByClan(clan, deJure: true))
            {
                if (title == null || !title.IsActive || title.IsDeliberatelyDissolved || title.DeFactoHolderClanId == clan.StringId || !seenTitles.Add(title.TitleId))
                    continue;

                if (TryBuildCandidate(titleBehavior, clan, title, FeudalClaimStrength.Strong, "implied_de_jure", out ClaimFeudCandidate candidate))
                    candidates.Add(candidate);
            }

            return candidates;
        }

        private bool TryBuildCandidate(
            FeudalTitleBehavior titleBehavior,
            Clan claimant,
            FeudalTitleRecord title,
            FeudalClaimStrength strength,
            string sourceClaimId,
            out ClaimFeudCandidate candidate)
        {
            candidate = null;
            Clan holder = ResolveClan(title.DeFactoHolderClanId);
            if (holder == null || holder == claimant)
                return false;

            if (holder.Kingdom == null || claimant.Kingdom == null || holder.Kingdom != claimant.Kingdom)
                return false;

            if (IsRulingClanInvolvedInFeud(claimant, holder))
                return false;

            if (!IsValidFeudClan(holder, out _))
                return false;

            if (_feuds.Any(record => record != null
                                  && record.TargetTitleId == title.TitleId
                                  && IsActiveFeudState(record.State)))
            {
                return false;
            }

            if (_feuds.Any(record => record != null
                                  && record.TargetTitleId == title.TitleId
                                  && ((record.ClaimantClanId == claimant.StringId && record.HolderClanId == holder.StringId)
                                      || (record.ClaimantClanId == holder.StringId && record.HolderClanId == claimant.StringId))
                                  && record.CooldownUntilDay > CurrentDay))
            {
                return false;
            }

            List<string> reasons = new List<string>();
            float score = strength == FeudalClaimStrength.Strong
                ? C.ClaimFeudStrongClaimScore
                : C.ClaimFeudWeakClaimScore;
            reasons.Add(strength == FeudalClaimStrength.Strong
                ? $"strong claim +{C.ClaimFeudStrongClaimScore:0}"
                : $"weak claim +{C.ClaimFeudWeakClaimScore:0}");

            float titleScore = ((int)title.TitleType + 1) * C.ClaimFeudTitleTierScore;
            score += titleScore;
            reasons.Add($"title rank +{titleScore:0}");

            float desirePressure = ClanFiefDesireHelper.CalculateRebelliousFiefDesirePressure(claimant);
            float desireScore = Math.Min(C.ClaimFeudFiefDesireCap, desirePressure * C.ClaimFeudFiefDesireScale);
            if (desireScore > 0f)
            {
                score += desireScore;
                reasons.Add($"fief desire +{desireScore:0}");
            }

            int relation = claimant.Leader.GetRelation(holder.Leader);
            float relationScore = Math.Max(-C.ClaimFeudHolderRelationCap, Math.Min(C.ClaimFeudHolderRelationCap, -relation * C.ClaimFeudHolderRelationScale));
            if (Math.Abs(relationScore) >= 0.5f)
            {
                score += relationScore;
                reasons.Add(relationScore >= 0f ? $"rival holder +{relationScore:0}" : $"friendly holder {relationScore:0}");
            }

            float powerScore = CalculatePowerAdvantageScore(claimant, holder);
            if (Math.Abs(powerScore) >= 0.5f)
            {
                score += powerScore;
                reasons.Add(powerScore >= 0f ? $"power advantage +{powerScore:0}" : $"power disadvantage {powerScore:0}");
            }

            score += CalculatePersonalityScore(claimant.Leader, strength, reasons);

            if (MarriageAllianceHelper.HasMarriageAlliance(claimant, holder))
            {
                score -= C.ClaimFeudMarriageAlliancePenalty;
                reasons.Add($"marriage alliance -{C.ClaimFeudMarriageAlliancePenalty:0}");
            }

            if (AreCloseDynasticKin(claimant, holder))
            {
                score -= C.ClaimFeudSameDynastyPenalty;
                reasons.Add($"shared dynasty -{C.ClaimFeudSameDynastyPenalty:0}");
            }

            candidate = new ClaimFeudCandidate(claimant, holder, title, strength, sourceClaimId, score, reasons);
            return true;
        }

        private static float CalculatePowerAdvantageScore(Clan claimant, Clan holder)
        {
            float claimantPower = Math.Max(1f, Campaign.Current?.Models?.DiplomacyModel?.GetClanStrength(claimant) ?? 1f);
            float holderPower = Math.Max(1f, Campaign.Current?.Models?.DiplomacyModel?.GetClanStrength(holder) ?? 1f);
            float ratio = (claimantPower - holderPower) / Math.Max(claimantPower, holderPower);
            return Math.Max(-C.ClaimFeudPowerAdvantageCap, Math.Min(C.ClaimFeudPowerAdvantageCap, ratio * C.ClaimFeudPowerAdvantageCap));
        }

        private static float CalculatePersonalityScore(Hero leader, FeudalClaimStrength strength, List<string> reasons)
        {
            if (leader == null)
                return 0f;

            float score = 0f;
            int calculating = leader.GetTraitLevel(DefaultTraits.Calculating);
            if (calculating >= 2)
            {
                score += C.ClaimFeudCalculating2Bonus;
                reasons.Add($"cerebral pressure +{C.ClaimFeudCalculating2Bonus:0}");
            }
            else if (calculating == 1)
            {
                score += C.ClaimFeudCalculating1Bonus;
                reasons.Add($"calculating pressure +{C.ClaimFeudCalculating1Bonus:0}");
            }
            else if (calculating == -1)
            {
                score += C.ClaimFeudImpulsive1Bonus;
                reasons.Add($"impulsive pressure +{C.ClaimFeudImpulsive1Bonus:0}");
            }
            else if (calculating <= -2)
            {
                score += C.ClaimFeudImpulsive2Bonus;
                reasons.Add($"hotheaded pressure +{C.ClaimFeudImpulsive2Bonus:0}");
            }

            int valor = leader.GetTraitLevel(DefaultTraits.Valor);
            if (valor >= 2)
            {
                score += C.ClaimFeudValor2Bonus;
                reasons.Add($"fearless +{C.ClaimFeudValor2Bonus:0}");
            }
            else if (valor == 1)
            {
                score += C.ClaimFeudValor1Bonus;
                reasons.Add($"daring +{C.ClaimFeudValor1Bonus:0}");
            }

            int honor = leader.GetTraitLevel(DefaultTraits.Honor);
            if (strength == FeudalClaimStrength.Weak && honor >= 2)
            {
                score -= C.ClaimFeudHonor2Penalty;
                reasons.Add($"honorable doubt in weak claim -{C.ClaimFeudHonor2Penalty:0}");
            }
            else if (strength == FeudalClaimStrength.Weak && honor == 1)
            {
                score -= C.ClaimFeudHonor1Penalty;
                reasons.Add($"honest doubt in weak claim -{C.ClaimFeudHonor1Penalty:0}");
            }

            int mercy = leader.GetTraitLevel(DefaultTraits.Mercy);
            if (mercy >= 2)
            {
                score -= C.ClaimFeudMercy2Penalty;
                reasons.Add($"compassionate -{C.ClaimFeudMercy2Penalty:0}");
            }
            else if (mercy == 1)
            {
                score -= C.ClaimFeudMercy1Penalty;
                reasons.Add($"merciful -{C.ClaimFeudMercy1Penalty:0}");
            }

            return score;
        }

        private float CalculateDailyPressure(ClaimFeudRecord record, Clan claimant, Clan holder)
        {
            float daily = record.ClaimStrength == FeudalClaimStrength.Strong
                ? C.ClaimFeudStrongDailyPressure
                : C.ClaimFeudWeakDailyPressure;

            daily += Math.Min(C.ClaimFeudFiefDesireCap, ClanFiefDesireHelper.CalculateRebelliousFiefDesirePressure(claimant)) * C.ClaimFeudDailyDesireScale;

            if (claimant?.Leader != null && holder?.Leader != null)
            {
                int relation = claimant.Leader.GetRelation(holder.Leader);
                if (relation < 0)
                    daily += (-relation) * C.ClaimFeudDailyRelationScale;
            }

            Kingdom parentKingdom = claimant?.Kingdom == holder?.Kingdom ? claimant?.Kingdom : null;
            float chancellorMultiplier = Campaign.Current?
                .GetCampaignBehavior<PrivyCouncilBehavior>()?
                .GetClaimFeudPressureMultiplier(parentKingdom) ?? 1f;
            daily *= chancellorMultiplier;

            return Math.Max(0.1f, Math.Min(C.ClaimFeudDailyPressureCap, daily));
        }

        private bool IsRecordStillValid(
            FeudalTitleBehavior titleBehavior,
            ClaimFeudRecord record,
            Clan claimant,
            Clan holder,
            FeudalTitleRecord title,
            out string reason)
        {
            reason = null;
            if (claimant == null || holder == null || title == null || !title.IsActive || title.IsDeliberatelyDissolved)
            {
                reason = "missing clan or title";
                return false;
            }

            if (claimant.IsEliminated || holder.IsEliminated)
            {
                reason = "clan eliminated";
                return false;
            }

            Kingdom parentKingdom = ResolveKingdom(record.ParentKingdomId);
            if (parentKingdom == null || parentKingdom.IsEliminated)
            {
                reason = "parent realm no longer exists";
                return false;
            }

            if (record.State == ClaimFeudState.WarActive)
            {
                ClaimFeudWarBehavior warBehavior = Campaign.Current?.GetCampaignBehavior<ClaimFeudWarBehavior>();
                if (warBehavior != null && warBehavior.HasActiveWarForFeud(record.RecordId))
                    return true;

                reason = "active feud war record is missing";
                return false;
            }

            if (!IsClanAttachedToParentRealm(record, claimant, parentKingdom)
                || !IsClanAttachedToParentRealm(record, holder, parentKingdom))
            {
                reason = "one of the principal houses permanently left the parent realm";
                return false;
            }

            if (parentKingdom.RulingClan != null
                && (claimant == parentKingdom.RulingClan || holder == parentKingdom.RulingClan))
            {
                reason = "ruling clan cannot be party to a private claim feud";
                return false;
            }

            if (title.DeFactoHolderClanId == claimant.StringId)
            {
                reason = "claimant already controls the title";
                return false;
            }

            if (title.DeFactoHolderClanId != holder.StringId)
            {
                reason = "target title changed hands";
                return false;
            }

            if (!titleBehavior.HasActiveClaim(claimant, title, record.ClaimStrength)
                && !(record.ClaimStrength == FeudalClaimStrength.Strong && title.DeJureHolderClanId == claimant.StringId))
            {
                reason = "claim is no longer active";
                return false;
            }

            return true;
        }

        private static bool IsRulingClanInvolvedInFeud(Clan claimant, Clan holder)
        {
            Kingdom kingdom = claimant?.Kingdom;
            if (kingdom == null || holder == null || holder.Kingdom != kingdom)
                return false;

            Clan rulingClan = kingdom.RulingClan;
            return rulingClan != null && (claimant == rulingClan || holder == rulingClan);
        }

        private static bool IsValidFeudClan(Clan clan, out string reason)
        {
            reason = null;
            if (clan == null)
            {
                reason = "clan is missing";
                return false;
            }

            if (clan.IsEliminated || clan.IsUnderMercenaryService || (clan.IsMinorFaction && clan != Clan.PlayerClan))
            {
                reason = "clan is not an active noble house";
                return false;
            }

            if (clan.Kingdom == null || clan.Kingdom.IsEliminated)
            {
                reason = "clan has no active kingdom";
                return false;
            }

            if (!string.IsNullOrWhiteSpace(clan.Kingdom.StringId) && clan.Kingdom.StringId.Contains("_rebels_"))
            {
                reason = "clan is in a temporary rebel kingdom";
                return false;
            }

            if (clan.Leader == null || clan.Leader.IsDead)
            {
                reason = "clan has no living leader";
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

        private static bool CanStartClaimFeud(Clan clan, out string reason)
        {
            if (!IsValidFeudClan(clan, out reason))
                return false;

            if (HasActiveCivilWar(clan.Kingdom))
            {
                reason = "civil war has suspended private litigation";
                return false;
            }

            return true;
        }

        private static bool HasActiveCivilWar(Kingdom kingdom)
        {
            if (kingdom == null || kingdom.IsEliminated)
                return false;

            FactionManagerBehavior factionManager = Campaign.Current?.GetCampaignBehavior<FactionManagerBehavior>();
            return factionManager?.GetFactionsInKingdom(kingdom)
                .Any(faction => faction != null && !faction.IsIdeology && faction.IsCivilWarActive()) == true;
        }

        private void OnClanDestroyed(Clan clan)
        {
            if (clan == null || string.IsNullOrWhiteSpace(clan.StringId))
                return;

            foreach (ClaimFeudRecord record in _feuds.Where(record => record != null
                && (record.ClaimantClanId == clan.StringId || record.HolderClanId == clan.StringId)))
            {
                InvalidateFeud(record, $"{clan.Name} was destroyed");
            }
        }

        private void OnClanChangedKingdom(Clan clan, Kingdom oldKingdom, Kingdom newKingdom, ChangeKingdomAction.ChangeKingdomActionDetail detail, bool showNotification = true)
        {
            if (clan == null || string.IsNullOrWhiteSpace(clan.StringId))
                return;

            foreach (ClaimFeudRecord record in _feuds.Where(record => record != null
                && (record.ClaimantClanId == clan.StringId || record.HolderClanId == clan.StringId)))
            {
                if (!IsActiveFeudState(record.State))
                    continue;

                Kingdom parent = ResolveKingdom(record.ParentKingdomId);
                bool oldIsOwnFeudShell = IsTemporaryRealmForFeud(record, oldKingdom);
                bool newIsOwnFeudShell = IsTemporaryRealmForFeud(record, newKingdom);
                // The temporary-realm moves fire this callback before TryStartFeudWar can
                // finish changing DefiedPendingWar to WarActive. They are part of this
                // feud's launch/cleanup, not a departure from the parent realm.
                if (newIsOwnFeudShell || (oldIsOwnFeudShell && newKingdom == parent))
                {
                    continue;
                }

                // Vanilla removes clans while it is still dismantling the kingdom. Resolving
                // the feud here would re-enter ChangeKingdomAction; the kingdom-destroyed
                // callback queues the appropriate outcome once that operation has completed.
                if (record.State == ClaimFeudState.WarActive
                    && oldIsOwnFeudShell
                    && detail == ChangeKingdomAction.ChangeKingdomActionDetail.LeaveByKingdomDestruction)
                {
                    continue;
                }

                if (parent != null && IsClanAttachedToParentRealm(record, clan, parent))
                {
                    if (clan.Kingdom != parent && record.State != ClaimFeudState.WarActive)
                        PauseFeudForConflict(record, $"{clan.Name} entered another internal conflict during {detail}");
                    continue;
                }

                InvalidateFeud(record, $"{clan.Name} left the parent realm during {detail}");
            }
        }

        private static bool RemoveCivilWarSupporters(ClaimFeudRecord record, HashSet<string> clanIds)
        {
            if (record == null || clanIds == null || clanIds.Count == 0)
                return false;

            List<string> claimantSupporters = SplitClanIds(record.ClaimantSupporterIds);
            List<string> holderSupporters = SplitClanIds(record.HolderSupporterIds);
            int beforeCount = claimantSupporters.Count + holderSupporters.Count;

            claimantSupporters = claimantSupporters
                .Where(id => !clanIds.Contains(id))
                .ToList();
            holderSupporters = holderSupporters
                .Where(id => !clanIds.Contains(id))
                .ToList();

            if (beforeCount == claimantSupporters.Count + holderSupporters.Count)
                return false;

            record.RecordSupporters(
                record.ClaimantSidePower,
                record.HolderSidePower,
                string.Join(",", claimantSupporters),
                string.Join(",", holderSupporters));
            record.SetDebugReason($"{record.DebugReason}; civil war supporters left feud");
            return true;
        }

        private static float GetPressureBandRebellionPenalty(float pressure)
        {
            if (pressure >= 66f)
                return C.ClaimFeudFocusedRebellionPenalty;
            if (pressure >= 33f)
                return C.ClaimFeudOccupiedRebellionPenalty;
            return C.ClaimFeudDistractedRebellionPenalty;
        }

        private static string GetPressureBandRebellionLabel(ClaimFeudState state, float pressure)
        {
            if (state == ClaimFeudState.WarActive || state == ClaimFeudState.DefiedPendingWar || pressure >= 66f)
                return new TextObject("{=BC_Score_Label_ClaimFeudFocused}Focused on Feud").ToString();
            if (pressure >= 33f)
                return new TextObject("{=BC_Score_Label_ClaimFeudOccupied}Occupied with Feud").ToString();
            return new TextObject("{=BC_Score_Label_ClaimFeudDistracted}Distracted by Feud").ToString();
        }

        private static bool IsActiveFeudState(ClaimFeudState state)
        {
            return state == ClaimFeudState.Agitating
                || state == ClaimFeudState.PetitionReady
                || state == ClaimFeudState.WarActive
                || state == ClaimFeudState.DefiedPendingWar
                || state == ClaimFeudState.AwaitingPlayerResponse
                || state == ClaimFeudState.AwaitingPlayerRulerJudgment
                || state == ClaimFeudState.Paused;
        }

        private static bool IsVisibleFeudState(ClaimFeudState state)
        {
            return IsActiveFeudState(state);
        }

        private static bool IsFeudInKingdom(ClaimFeudRecord record, Kingdom kingdom)
        {
            if (record == null || kingdom == null)
                return false;

            if (!string.IsNullOrWhiteSpace(record.ParentKingdomId) && record.ParentKingdomId == kingdom.StringId)
                return true;

            Clan claimant = ResolveClan(record.ClaimantClanId);
            Clan holder = ResolveClan(record.HolderClanId);
            return claimant?.Kingdom == kingdom || holder?.Kingdom == kingdom;
        }

        private static int CountExternalWars(Kingdom kingdom)
        {
            if (kingdom == null)
                return 0;

            return Kingdom.All.Count(other => other != null
                && other != kingdom
                && !other.IsEliminated
                && !other.IsMinorFaction
                && !BellumKingdomVisibilityHelper.IsTemporaryBellumKingdom(other)
                && kingdom.IsAtWarWith(other));
        }

        private static bool AreCloseDynasticKin(Clan first, Clan second)
        {
            return CivilWarSolidarityHelper.AreCloseDynasticKin(first, second);
        }

        private static string BuildRecordId(Clan claimant, Clan holder, FeudalTitleRecord title)
        {
            return $"bc_claim_feud_{claimant?.StringId ?? "none"}_{holder?.StringId ?? "none"}_{title?.TitleId ?? "none"}";
        }

        private static Clan ResolveClan(string clanId)
        {
            return string.IsNullOrWhiteSpace(clanId)
                ? null
                : Clan.All?.FirstOrDefault(clan => clan != null && clan.StringId == clanId);
        }

        private static Kingdom ResolveKingdom(string kingdomId)
        {
            return string.IsNullOrWhiteSpace(kingdomId)
                ? null
                : Kingdom.All?.FirstOrDefault(kingdom => kingdom != null && kingdom.StringId == kingdomId);
        }

        private static int GetCampaignDaysInYear()
        {
            return Math.Max(1, CampaignTime.DaysInYear > 0 ? CampaignTime.DaysInYear : C.MarriageStrategyDaysPerYear);
        }

        private void EnsureCollectionsInitialized()
        {
            if (_feuds == null)
                _feuds = new List<ClaimFeudRecord>();
        }

        private static float CurrentDay => (float)CampaignTime.Now.ToDays;

        private sealed class FeudCallAssessment
        {
            internal FeudCallAssessment(float score, CivilWarSolidarityReason reason)
            {
                Score = score;
                Reason = reason;
            }

            internal float Score { get; }
            internal CivilWarSolidarityReason Reason { get; }
        }

        private sealed class ClaimFeudCandidate
        {
            public Clan Claimant { get; }
            public Clan Holder { get; }
            public FeudalTitleRecord Title { get; }
            public FeudalClaimStrength Strength { get; }
            public string SourceClaimId { get; }
            public float Score { get; }
            public List<string> Reasons { get; }

            public ClaimFeudCandidate(
                Clan claimant,
                Clan holder,
                FeudalTitleRecord title,
                FeudalClaimStrength strength,
                string sourceClaimId,
                float score,
                List<string> reasons)
            {
                Claimant = claimant;
                Holder = holder;
                Title = title;
                Strength = strength;
                SourceClaimId = sourceClaimId ?? string.Empty;
                Score = score;
                Reasons = reasons ?? new List<string>();
            }
        }

        private sealed class RevocationCandidate
        {
            public RevocationCandidate(FeudalTitleRecord title, FeudalRevocationPreview preview, float score, List<string> reasons)
            {
                Title = title;
                Preview = preview;
                Score = score;
                Reasons = reasons ?? new List<string>();
            }

            public FeudalTitleRecord Title { get; }
            public FeudalRevocationPreview Preview { get; }
            public float Score { get; }
            public List<string> Reasons { get; }
        }

        private sealed class ClaimFeudComplianceAssessment
        {
            public ClaimFeudComplianceAssessment(bool defies, string summary)
            {
                Defies = defies;
                Summary = summary ?? string.Empty;
            }

            public bool Defies { get; }
            public string Summary { get; }
        }
    }

    public sealed class ClaimFeudLifecycleYearlyTelemetry
    {
        public ClaimFeudLifecycleYearlyTelemetry(
            int evaluations,
            int noActionableCandidates,
            int belowThreshold,
            int pressableCandidates,
            int startRollsDeclined,
            int agitationsStarted,
            int agitationsReactivated,
            int passed33,
            int passed66,
            int petitionsQueued,
            int petitionsJudged,
            int peacefulSettlements,
            int defiedToWar,
            int civilWarInterruptions,
            int pauses,
            int resumes,
            int invalidated)
        {
            Evaluations = evaluations;
            NoActionableCandidates = noActionableCandidates;
            BelowThreshold = belowThreshold;
            PressableCandidates = pressableCandidates;
            StartRollsDeclined = startRollsDeclined;
            AgitationsStarted = agitationsStarted;
            AgitationsReactivated = agitationsReactivated;
            Passed33 = passed33;
            Passed66 = passed66;
            PetitionsQueued = petitionsQueued;
            PetitionsJudged = petitionsJudged;
            PeacefulSettlements = peacefulSettlements;
            DefiedToWar = defiedToWar;
            CivilWarInterruptions = civilWarInterruptions;
            Pauses = pauses;
            Resumes = resumes;
            Invalidated = invalidated;
        }

        public int Evaluations { get; }
        public int NoActionableCandidates { get; }
        public int BelowThreshold { get; }
        public int PressableCandidates { get; }
        public int StartRollsDeclined { get; }
        public int AgitationsStarted { get; }
        public int AgitationsReactivated { get; }
        public int Passed33 { get; }
        public int Passed66 { get; }
        public int PetitionsQueued { get; }
        public int PetitionsJudged { get; }
        public int PeacefulSettlements { get; }
        public int DefiedToWar { get; }
        public int CivilWarInterruptions { get; }
        public int Pauses { get; }
        public int Resumes { get; }
        public int Invalidated { get; }
    }
}
