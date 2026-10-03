using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TaleWorlds.CampaignSystem;
using C = BellumCivile.BellumCivileConstants;

namespace BellumCivile.Behaviors
{
    /// <summary>
    /// Emits annual balance telemetry for Bellum's replacement war, War Will, and treaty systems.
    /// It reads the durable conflict/proposal records at the year boundary, so reporting adds no
    /// extra campaign simulation scans or saved state.
    /// </summary>
    public sealed class WarPeaceYearlySummaryBehavior : CampaignBehaviorBase
    {
        private int _summaryYearIndex = -1;

        public override void RegisterEvents()
        {
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
        }

        public override void SyncData(IDataStore dataStore)
        {
        }

        private void OnDailyTick()
        {
            int daysPerYear = GetCampaignDaysInYear();
            int currentYearIndex = (int)(CampaignTime.Now.ToDays / daysPerYear);
            if (_summaryYearIndex < 0)
            {
                _summaryYearIndex = currentYearIndex;
                return;
            }

            if (currentYearIndex == _summaryYearIndex)
                return;

            string report = BuildReport(_summaryYearIndex, daysPerYear);
            if (!string.IsNullOrWhiteSpace(report))
                BellumCivileDebug.TraceYearlyReport("war_peace", report);

            _summaryYearIndex = currentYearIndex;
        }

        private static string BuildReport(int yearIndex, int daysPerYear)
        {
            float startDay = yearIndex * daysPerYear;
            float endDay = (yearIndex + 1) * daysPerYear;
            WarScoreBehavior warScore = Campaign.Current?.GetCampaignBehavior<WarScoreBehavior>();
            ForeignTreatyBehavior treaties = Campaign.Current?.GetCampaignBehavior<ForeignTreatyBehavior>();
            WarPeaceRevampBehavior warWill = Campaign.Current?.GetCampaignBehavior<WarPeaceRevampBehavior>();
            ClaimFeudWarYearlyTelemetry feudTelemetry = Campaign.Current?.GetCampaignBehavior<ClaimFeudWarBehavior>()?.ConsumeYearlyTelemetry()
                ?? new ClaimFeudWarYearlyTelemetry(0, 0, 0);
            ClaimFeudLifecycleYearlyTelemetry feudLifecycle = Campaign.Current?.GetCampaignBehavior<ClaimFeudBehavior>()?.ConsumeYearlyTelemetry()
                ?? new ClaimFeudLifecycleYearlyTelemetry(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
            RetainedPrisonerYearlyTelemetry prisonerTelemetry = Campaign.Current?.GetCampaignBehavior<RetainedTreatyPrisonerBehavior>()?.ConsumeYearlyTelemetry()
                ?? new RetainedPrisonerYearlyTelemetry(0, 0, 0, 0, 0, 0, 0, 0);
            float snapshotDay = (float)CampaignTime.Now.ToDays;
            List<FeudalClaimRecord> activeClaims = Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>()?
                .GetActiveClaims()
                .Where(claim => claim != null
                    && (claim.ExpiresDay < 0f || claim.ExpiresDay > snapshotDay))
                .ToList() ?? new List<FeudalClaimRecord>();

            List<WarScoreRecord> allWars = warScore?.GetTrackedWars().Where(war => war != null).ToList()
                ?? new List<WarScoreRecord>();
            List<WarScoreRecord> started = allWars
                .Where(war => war.StartedDay >= startDay && war.StartedDay < endDay)
                .ToList();
            List<WarScoreRecord> ended = allWars
                .Where(war => war.EndedDay >= startDay && war.EndedDay < endDay)
                .ToList();
            List<WarScoreEventRecord> events = allWars
                .SelectMany(war => war.Events ?? new List<WarScoreEventRecord>())
                .Where(evt => evt != null && evt.Day >= startDay && evt.Day < endDay)
                .ToList();
            List<TreatyProposalRecord> allProposals = treaties?.GetProposals()
                .Where(proposal => proposal != null).ToList() ?? new List<TreatyProposalRecord>();
            List<TreatyProposalRecord> proposals = allProposals
                .Where(proposal => proposal.CreatedDay >= startDay && proposal.CreatedDay < endDay).ToList();
            List<TreatyProposalRecord> resolvedProposals = allProposals
                .Where(proposal => ResolvedInPeriod(proposal, startDay, endDay)).ToList();

            List<Clan> trackedClans = GetPermanentRealmClans().ToList();
            List<float> warWillValues = warWill == null
                ? new List<float>()
                : trackedClans.Select(warWill.GetWarWill).ToList();

            bool hasActivity = started.Count > 0 || ended.Count > 0 || proposals.Count > 0 || resolvedProposals.Count > 0 || allWars.Any(war => war.IsActive) || activeClaims.Count > 0;
            if (!hasActivity && warWillValues.Count == 0)
                return string.Empty;

            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"yearly war and peace summary: campaign year {yearIndex + 1} ({daysPerYear} days/year); revamp_enabled={WarPeaceRevampBehavior.IsRevampEnabled()}");
            sb.AppendLine($"conflicts_started: foreign={CountType(started, WarScoreConflictType.ForeignWar)}; civil_wars={CountType(started, WarScoreConflictType.CivilWar)}; claim_feuds={CountType(started, WarScoreConflictType.ClaimFeud)}; total={started.Count}");
            sb.AppendLine($"conflicts_ended: foreign={CountType(ended, WarScoreConflictType.ForeignWar)}; civil_wars={CountType(ended, WarScoreConflictType.CivilWar)}; claim_feuds={CountType(ended, WarScoreConflictType.ClaimFeud)}; total={ended.Count}; avg_duration_days={AverageDuration(ended):0.0}; avg_abs_final_score={AverageAbsoluteScore(ended):0.0}");
            sb.AppendLine($"active_end_year: foreign={CountActiveType(allWars, WarScoreConflictType.ForeignWar)}; civil_wars={CountActiveType(allWars, WarScoreConflictType.CivilWar)}; claim_feuds={CountActiveType(allWars, WarScoreConflictType.ClaimFeud)}");
            sb.AppendLine($"claim_feud_outcomes: claimant_victory={feudTelemetry.ClaimantVictories}; holder_victory={feudTelemetry.HolderVictories}; white_peace={feudTelemetry.WhitePeaces}");
            sb.AppendLine($"claim_feud_funnel: evaluations={feudLifecycle.Evaluations}; no_actionable={feudLifecycle.NoActionableCandidates}; below_threshold={feudLifecycle.BelowThreshold}; pressable={feudLifecycle.PressableCandidates}; rolls_declined={feudLifecycle.StartRollsDeclined}; started={feudLifecycle.AgitationsStarted}; reactivated={feudLifecycle.AgitationsReactivated}");
            sb.AppendLine($"claim_feud_progress: passed_33={feudLifecycle.Passed33}; passed_66={feudLifecycle.Passed66}; petitions={feudLifecycle.PetitionsQueued}; judgments={feudLifecycle.PetitionsJudged}; peaceful_settlements={feudLifecycle.PeacefulSettlements}; defied_to_war={feudLifecycle.DefiedToWar}; civil_war_interruptions={feudLifecycle.CivilWarInterruptions}; pauses={feudLifecycle.Pauses}; resumes={feudLifecycle.Resumes}; invalidated={feudLifecycle.Invalidated}");
            int duplicateActivePairs = activeClaims
                .GroupBy(claim => claim.ClaimantClanId + "|" + claim.TargetTitleId)
                .Count(group => group.Count() > 1);
            sb.AppendLine($"active_claims: total={activeClaims.Count}; strong={activeClaims.Count(claim => claim.Strength == FeudalClaimStrength.Strong)}; weak={activeClaims.Count(claim => claim.Strength == FeudalClaimStrength.Weak)}; unique_clan_title_pairs={activeClaims.Select(claim => claim.ClaimantClanId + "|" + claim.TargetTitleId).Distinct().Count()}; duplicate_active_pairs={duplicateActivePairs}; generation_depths=({FormatClaimGenerationDepths(activeClaims)})");
            sb.AppendLine($"active_claim_age: average_years={AverageClaimAgeYears(activeClaims, snapshotDay, daysPerYear):0.0}; oldest_years={OldestClaimAgeYears(activeClaims, snapshotDay, daysPerYear):0.0}; under_1y={CountClaimsByAge(activeClaims, snapshotDay, 0f, daysPerYear)}; age_1_to_10y={CountClaimsByAge(activeClaims, snapshotDay, daysPerYear, 10f * daysPerYear)}; age_10_to_25y={CountClaimsByAge(activeClaims, snapshotDay, 10f * daysPerYear, 25f * daysPerYear)}; age_25y_plus={CountClaimsByAge(activeClaims, snapshotDay, 25f * daysPerYear, float.MaxValue)}");
            sb.AppendLine($"active_claim_sources: {FormatClaimSources(activeClaims)}");
            sb.AppendLine($"war_events: battles={CountEvents(events, WarScoreEventType.BattleWon, WarScoreEventType.MajorBattleWon)}; fiefs_captured={CountEvents(events, WarScoreEventType.TownCaptured, WarScoreEventType.CastleCaptured)}; raids={CountEvents(events, WarScoreEventType.VillageRaided)}; prisoners={CountEvents(events, WarScoreEventType.NobleCaptured, WarScoreEventType.HeirCaptured, WarScoreEventType.RulerCaptured)}; full_occupations={CountEvents(events, WarScoreEventType.FullOccupation)}; white_peaces={CountEvents(events, WarScoreEventType.WhitePeace)}");
            sb.AppendLine($"parleys: opened={proposals.Count}; applied={resolvedProposals.Count(proposal => proposal.State == TreatyProposalState.Applied)}; rejected={resolvedProposals.Count(proposal => proposal.State == TreatyProposalState.Rejected)}; cancelled={resolvedProposals.Count(proposal => proposal.State == TreatyProposalState.Cancelled)}; pending={allProposals.Count(proposal => proposal.State == TreatyProposalState.ParleyPending)}; white_peace_drafts={proposals.Count(IsWhitePeaceProposal)}; avg_war_score_spent={AverageWarScoreSpent(resolvedProposals):0.0}");
            sb.AppendLine($"retained_prisoners: newly_retained={prisonerTelemetry.NewlyRetained}; peace_releases_blocked={prisonerTelemetry.PeaceReleasesBlocked}; treaty_released={prisonerTelemetry.TreatyReleased}; escaped={prisonerTelemetry.Escaped}; ransomed={prisonerTelemetry.Ransomed}; other_released={prisonerTelemetry.OtherReleased}; stale_removed={prisonerTelemetry.StaleRemoved}; active_end_year={prisonerTelemetry.CurrentlyRetained}");
            sb.AppendLine($"war_will: clans={warWillValues.Count}; average={Average(warWillValues):0.0}; at_or_below_peace_threshold={warWillValues.Count(value => value <= BellumCivileOptions.WarWillPeaceThreshold)}; at_or_above_declare_threshold={warWillValues.Count(value => value >= BellumCivileOptions.WarWillDeclareThreshold)}");
            return sb.ToString().TrimEnd();
        }

        internal static bool ResolvedInPeriod(TreatyProposalRecord proposal, float startDay, float endDay) =>
            proposal != null && proposal.ResolvedDay >= startDay && proposal.ResolvedDay < endDay
            && (proposal.State == TreatyProposalState.Applied || proposal.State == TreatyProposalState.Rejected
                || proposal.State == TreatyProposalState.Cancelled);

        private static IEnumerable<Clan> GetPermanentRealmClans()
        {
            FactionManagerBehavior factions = Campaign.Current?.GetCampaignBehavior<FactionManagerBehavior>();
            return Kingdom.All
                .Where(kingdom => kingdom != null
                    && !kingdom.IsEliminated
                    && kingdom.RulingClan != null
                    && !kingdom.StringId.StartsWith("bc_feud_", StringComparison.OrdinalIgnoreCase)
                    && factions?.GetFactionByRebelKingdom(kingdom) == null)
                .SelectMany(kingdom => kingdom.Clans)
                .Where(clan => clan != null
                    && !clan.IsEliminated
                    && !clan.IsBanditFaction
                    && !clan.IsUnderMercenaryService
                    && clan.Leader != null
                    && !clan.Leader.IsDead)
                .Distinct();
        }

        private static int CountType(IEnumerable<WarScoreRecord> wars, WarScoreConflictType type)
        {
            return wars.Count(war => war.ConflictType == type);
        }

        private static int CountActiveType(IEnumerable<WarScoreRecord> wars, WarScoreConflictType type)
        {
            return wars.Count(war => war.IsActive && war.ConflictType == type);
        }

        private static int CountEvents(IEnumerable<WarScoreEventRecord> events, params WarScoreEventType[] types)
        {
            HashSet<WarScoreEventType> accepted = new HashSet<WarScoreEventType>(types);
            return events.Count(evt => accepted.Contains(evt.EventType));
        }

        private static float AverageDuration(List<WarScoreRecord> wars)
        {
            return wars.Count == 0 ? 0f : wars.Average(war => Math.Max(0f, war.EndedDay - war.StartedDay));
        }

        private static float AverageAbsoluteScore(List<WarScoreRecord> wars)
        {
            return wars.Count == 0 ? 0f : wars.Average(war => Math.Abs(war.Score));
        }

        private static float AverageWarScoreSpent(List<TreatyProposalRecord> proposals)
        {
            List<TreatyProposalRecord> resolved = proposals
                .Where(proposal => proposal.State == TreatyProposalState.Applied)
                .ToList();
            return resolved.Count == 0 ? 0f : (float)resolved.Average(proposal => Math.Abs(proposal.UsedWarScore));
        }

        private static float Average(List<float> values)
        {
            return values.Count == 0 ? 0f : values.Average();
        }

        private static double AverageClaimAgeYears(List<FeudalClaimRecord> claims, float snapshotDay, int daysPerYear)
        {
            return claims.Count == 0
                ? 0d
                : claims.Average(claim => Math.Max(0f, snapshotDay - claim.CreatedDay) / daysPerYear);
        }

        private static float OldestClaimAgeYears(List<FeudalClaimRecord> claims, float snapshotDay, int daysPerYear)
        {
            return claims.Count == 0
                ? 0f
                : claims.Max(claim => Math.Max(0f, snapshotDay - claim.CreatedDay) / daysPerYear);
        }

        private static int CountClaimsByAge(List<FeudalClaimRecord> claims, float snapshotDay, float minimumDays, float maximumDays)
        {
            return claims.Count(claim =>
            {
                float ageDays = Math.Max(0f, snapshotDay - claim.CreatedDay);
                return ageDays >= minimumDays && ageDays < maximumDays;
            });
        }

        private static string FormatClaimGenerationDepths(List<FeudalClaimRecord> claims)
        {
            if (claims.Count == 0)
                return "none";

            return string.Join(", ", claims
                .GroupBy(claim => claim.GenerationDepth)
                .OrderBy(group => group.Key)
                .Select(group => $"depth_{group.Key}={group.Count()}"));
        }

        private static string FormatClaimSources(List<FeudalClaimRecord> claims)
        {
            if (claims.Count == 0)
                return "none";

            return string.Join(", ", claims
                .GroupBy(claim => string.IsNullOrWhiteSpace(claim.Source) ? "unknown" : claim.Source)
                .OrderByDescending(group => group.Count())
                .ThenBy(group => group.Key)
                .Select(group => $"{group.Key}={group.Count()}"));
        }

        private static bool IsWhitePeaceProposal(TreatyProposalRecord proposal)
        {
            return proposal.Terms?.Any(term => term?.Type == TreatyTermType.WhitePeace) == true;
        }

        private static int GetCampaignDaysInYear()
        {
            return Math.Max(1, CampaignTime.DaysInYear > 0 ? CampaignTime.DaysInYear : C.MarriageStrategyDaysPerYear);
        }
    }
}
