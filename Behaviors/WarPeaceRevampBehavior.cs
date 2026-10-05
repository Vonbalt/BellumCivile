using System;
using System.Collections.Generic;
using System.Linq;
using BellumCivile.Patches;
using BellumCivile.WarPeace;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Election;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Localization;
using TaleWorlds.SaveSystem;
using C = BellumCivile.BellumCivileConstants;

namespace BellumCivile.Behaviors
{
    public sealed class WarPeaceRevampBehavior : CampaignBehaviorBase
    {
        private Dictionary<string, float> _warWillByClanId = new Dictionary<string, float>();
        private Dictionary<string, int> _nextEvaluationDayByClanId = new Dictionary<string, int>();
        private Dictionary<string, string> _preferredTargetByClanId = new Dictionary<string, string>();
        private Dictionary<string, bool> _initializedWarMomentumByConflictId = new Dictionary<string, bool>();
        private Dictionary<string, string> _pendingVanillaSecessions = new Dictionary<string, string>();
        private List<WarWillPressureRecord> _pressureRecords = new List<WarWillPressureRecord>();
        private bool _processingPendingVanillaSecessions;

        private readonly Dictionary<string, List<WarTargetScore>> _targetScoresByClanId =
            new Dictionary<string, List<WarTargetScore>>();

        private readonly WarTargetScoringService _targetScoring = new WarTargetScoringService();

        // Foreign, civil, and feud wars impose different political burdens. Keeping them
        // separate prevents a civil war from being treated as merely one more frontier.
        private sealed class WarWillConflictLoad
        {
            public int ForeignWarCount { get; }
            public int CivilWarCount { get; }
            public int ClaimFeudWarCount { get; }
            public int RealmFeudCount { get; }
            public int DirectWarCount => ForeignWarCount + CivilWarCount + ClaimFeudWarCount;
            public float AdditionalConflictLoad => Math.Max(0, ForeignWarCount - 1)
                + (CivilWarCount * C.WarPeaceRevampCivilWarConflictLoad)
                + (ClaimFeudWarCount * C.WarPeaceRevampClaimFeudConflictLoad)
                + (RealmFeudCount * C.WarPeaceRevampRealmFeudConflictLoad);

            public WarWillConflictLoad(int foreignWarCount, int civilWarCount, int claimFeudWarCount, int realmFeudCount)
            {
                ForeignWarCount = foreignWarCount;
                CivilWarCount = civilWarCount;
                ClaimFeudWarCount = claimFeudWarCount;
                RealmFeudCount = realmFeudCount;
            }
        }

        public override void RegisterEvents()
        {
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
            CampaignEvents.DailyTickClanEvent.AddNonSerializedListener(this, OnDailyTickClan);
            CampaignEvents.TickEvent.AddNonSerializedListener(this, OnTick);
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
            CampaignEvents.MakePeace.AddNonSerializedListener(this, OnPeaceMade);
            CampaignEvents.WarDeclared.AddNonSerializedListener(this, OnWarDeclared);
            CampaignEvents.OnClanChangedKingdomEvent.AddNonSerializedListener(this, OnClanChangedKingdom);
            CampaignEvents.OnSettlementOwnerChangedEvent.AddNonSerializedListener(this, OnSettlementOwnerChanged);
            CampaignEvents.MapEventEnded.AddNonSerializedListener(this, OnMapEventEnded);
            CampaignEvents.VillageLooted.AddNonSerializedListener(this, OnVillageLooted);
            CampaignEvents.HeroPrisonerTaken.AddNonSerializedListener(this, OnHeroPrisonerTaken);
            CampaignEvents.HeroKilledEvent.AddNonSerializedListener(this, OnHeroKilled);
        }

        public override void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData("BellumCivile_WarPeaceRevamp_WarWill", ref _warWillByClanId);
            dataStore.SyncData("BellumCivile_WarPeaceRevamp_NextEvaluationDay", ref _nextEvaluationDayByClanId);
            dataStore.SyncData("BellumCivile_WarPeaceRevamp_PreferredTarget", ref _preferredTargetByClanId);
            dataStore.SyncData("BellumCivile_WarPeaceRevamp_InitializedWarMomentum", ref _initializedWarMomentumByConflictId);
            dataStore.SyncData("BellumCivile_WarPeaceRevamp_PendingVanillaSecessions", ref _pendingVanillaSecessions);
            dataStore.SyncData("BellumCivile_WarPeaceRevamp_PressureRecords", ref _pressureRecords);
            EnsureCollectionsInitialized();
        }

        private void OnTick(float deltaTime)
        {
            if (_processingPendingVanillaSecessions)
                return;

            EnsureCollectionsInitialized();
            if (_pendingVanillaSecessions.Count == 0)
                return;

            _processingPendingVanillaSecessions = true;
            try
            {
                ProcessPendingVanillaSecessions();
            }
            finally
            {
                _processingPendingVanillaSecessions = false;
            }
        }

        public float GetWarWill(Clan clan)
        {
            if (clan == null || string.IsNullOrWhiteSpace(clan.StringId))
                return 0f;

            EnsureCollectionsInitialized();
            if (!_warWillByClanId.TryGetValue(clan.StringId, out float value))
            {
                value = BellumCivileOptions.WarWillInitial;
                _warWillByClanId[clan.StringId] = value;
            }

            return value;
        }

        internal float PeekWarWill(Clan clan) => clan != null && !string.IsNullOrWhiteSpace(clan.StringId)
            && _warWillByClanId != null && _warWillByClanId.TryGetValue(clan.StringId, out float value)
                ? value : BellumCivileOptions.WarWillInitial;

        internal static bool HasInternalConflict(Kingdom realm)
        {
            var load = GetConflictLoad(realm);
            return load.CivilWarCount > 0 || load.ClaimFeudWarCount > 0 || load.RealmFeudCount > 0;
        }

        internal IReadOnlyList<WarTargetScore> GetRankedTargets(Clan clan, bool forceRefresh = false)
        {
            if (clan == null || string.IsNullOrWhiteSpace(clan.StringId))
                return new List<WarTargetScore>();

            EnsureCollectionsInitialized();
            if (forceRefresh || !_targetScoresByClanId.TryGetValue(clan.StringId, out List<WarTargetScore> scores))
            {
                scores = _targetScoring.RankTargets(clan).ToList();
                _targetScoresByClanId[clan.StringId] = scores;
                _preferredTargetByClanId[clan.StringId] = scores.FirstOrDefault()?.TargetKingdom?.StringId ?? string.Empty;
            }

            return scores;
        }

        internal WarTargetScore GetPreferredTarget(Clan clan, bool forceRefresh = false)
        {
            return GetRankedTargets(clan, forceRefresh).FirstOrDefault();
        }

        public string BuildClanDebugReport(Clan clan)
        {
            if (clan == null)
                return "Error: clan not found.";

            List<WarTargetScore> targets = GetRankedTargets(clan, forceRefresh: true).ToList();
            List<string> lines = new List<string>
            {
                $"War & Peace Revamp diagnostic for {clan.Name} ({clan.StringId})",
                $"Toggle enabled: {IsRevampEnabled()}",
                $"War Will: {GetWarWill(clan):0.0}/100",
                $"Kingdom: {clan.Kingdom?.Name?.ToString() ?? "none"}",
                "Targets:"
            };

            WarWillConflictLoad conflictLoad = GetConflictLoad(clan.Kingdom);
            if (conflictLoad.DirectWarCount > 0 || conflictLoad.RealmFeudCount > 0)
            {
                lines.Insert(4,
                    $"Military commitments: foreign wars={conflictLoad.ForeignWarCount}; civil wars={conflictLoad.CivilWarCount}; direct feuds={conflictLoad.ClaimFeudWarCount}; realm feuds={conflictLoad.RealmFeudCount}; extra load={conflictLoad.AdditionalConflictLoad:0.0}");
                var enthusiasm = GetConflictLoad(clan.Kingdom, enthusiasmOnly: true);
                lines.Insert(5, $"Enthusiasm strain: direct wars={enthusiasm.DirectWarCount}; extra load={enthusiasm.AdditionalConflictLoad:0.0}; protected storyline fronts excluded.");
            }

            if (targets.Count == 0)
            {
                lines.Add("- no neighboring, maritime, active-war, or claim-linked targets");
                return string.Join(Environment.NewLine, lines);
            }

            foreach (WarTargetScore target in targets.Take(8))
            {
                string flags = string.Join(", ", new[]
                    {
                        target.IsActiveWar ? "active war" : null,
                        target.IsLocalLandNeighbor ? "local land" : target.IsNeighboringRealm ? "realm land" : null,
                        target.IsLocalMaritimeNeighbor ? "local maritime" : target.IsMaritimeNeighbor ? "realm maritime" : null,
                        target.IsClaimTarget ? "claim" : null
                    }
                    .Where(flag => !string.IsNullOrWhiteSpace(flag)));
                lines.Add($"- {target.TargetKingdom.Name}: {target.Score:0.0}" + (string.IsNullOrWhiteSpace(flags) ? string.Empty : $" ({flags})"));
                float courtCampaign = CourtAgendaBehavior.Current?.CampaignInitiativeBonus(clan.Kingdom, target.TargetKingdom, clan) ?? 0;
                if (courtCampaign != 0) lines.Add($"  - Court campaign initiative: +{courtCampaign:0} vote utility (not War Will or target score)");
                float courtClientage = CourtAgendaBehavior.Current?.SubjugationWarBonus(clan.Kingdom, target.TargetKingdom, clan) ?? 0;
                if (courtClientage != 0) lines.Add($"  - Court clientage initiative: +{courtClientage:0} vote utility (not War Will or target score)");
                float courtClaim = CourtAgendaBehavior.Current?.ClaimWarBonus(clan.Kingdom, target.TargetKingdom, clan) ?? 0;
                if (courtClaim != 0) lines.Add($"  - Court claim initiative: +{courtClaim:0} vote utility (not War Will or target score)");
                float hostagePenalty = Campaign.Current?.GetCampaignBehavior<HostagePactBehavior>()?.WarDeterrence(clan, target.TargetKingdom, council: true) ?? 0;
                if (hostagePenalty > 0) lines.Add($"  - Hostage-backed peace: -{hostagePenalty:0} vote utility in addition to target deterrence");
                foreach (string reason in target.Reasons.Take(6))
                    lines.Add($"  - {reason}");
            }

            List<WarWillPressureRecord> pressures = GetActivePressureRecords(clan)
                .OrderByDescending(record => Math.Abs(record.Amount))
                .ThenByDescending(record => record.CreatedDay)
                .Take(8)
                .ToList();
            if (pressures.Count > 0)
            {
                lines.Add("Recent War Will pressures:");
                foreach (WarWillPressureRecord pressure in pressures)
                {
                    Kingdom target = ResolveKingdom(pressure.TargetKingdomId);
                    string label = WarWillMotiveDisplayHelper.BuildShortLabel(pressure, clan);
                    lines.Add($"- {pressure.Amount:+0.0;-0.0;0.0} {label}"
                        + (target != null ? $" vs {target.Name}" : string.Empty));
                }
            }

            return string.Join(Environment.NewLine, lines);
        }

        public WarWillMotiveBreakdown BuildWarWillMotiveBreakdown(Clan clan, Kingdom target = null)
        {
            if (clan == null)
            {
                return new WarWillMotiveBreakdown(
                    0f,
                    WarWillVoteStance.Neutral,
                    Enumerable.Empty<WarWillMotiveLine>());
            }

            var breakdown = WarWillMotiveDisplayHelper.BuildBreakdown(
                clan,
                target,
                GetWarWill(clan),
                GetActivePressureRecords(clan),
                BellumCivileOptions.WarWillPeaceThreshold,
                BellumCivileOptions.WarWillDeclareThreshold);
            var war = target == null ? null : Campaign.Current?.GetCampaignBehavior<WarScoreBehavior>()?.GetActiveWar(clan.Kingdom,target);
            return CourtAgendaBehavior.Current?.RallyBreakdown(clan,war,breakdown) ?? breakdown;
        }

        public string BuildKingdomDebugReport(Kingdom kingdom)
        {
            if (kingdom == null)
                return "Error: kingdom not found.";

            List<Clan> clans = kingdom.Clans?
                .Where(IsValidEvaluatedClan)
                .OrderByDescending(GetWarWill)
                .ToList() ?? new List<Clan>();

            List<string> lines = new List<string>
            {
                $"War & Peace Revamp realm diagnostic for {kingdom.Name} ({kingdom.StringId})",
                $"Toggle enabled: {IsRevampEnabled()}",
                $"Eligible clans: {clans.Count}"
            };

            foreach (Clan clan in clans)
            {
                WarTargetScore preferred = GetPreferredTarget(clan, forceRefresh: true);
                lines.Add($"- {clan.Name}: war_will={GetWarWill(clan):0.0}; preferred={preferred?.TargetKingdom?.Name?.ToString() ?? "none"} ({preferred?.Score.ToString("0.0") ?? "0"})");
            }

            return string.Join(Environment.NewLine, lines);
        }

        internal bool TryCreateWarDecision(Clan clan, out KingdomDecision decision, out string report)
        {
            decision = null;
            report = "revamp disabled";
            if (!IsRevampEnabled())
                return false;
            if (!IsValidEvaluatedClan(clan) || clan.Kingdom == null)
            {
                report = "invalid proposing clan";
                return false;
            }
            if (clan.Kingdom.UnresolvedDecisions.Any(existing => existing is DeclareWarDecision))
            {
                report = "kingdom already has an unresolved war decision";
                return false;
            }

            ClientKingdomBehavior clients = ClientKingdomBehavior.Instance;
            Kingdom liberationSuzerain = null;
            bool liberationProposal = clients?.IsClientKingdom(clan.Kingdom) == true;
            if (liberationProposal
                && !clients.TryCanProposeLiberation(clan, out liberationSuzerain, out report))
            {
                return false;
            }

            WarWillConflictLoad conflictLoad = GetConflictLoad(clan.Kingdom);
            if (!liberationProposal && (conflictLoad.CivilWarCount > 0
                || conflictLoad.ClaimFeudWarCount > 0
                || conflictLoad.RealmFeudCount > 0))
            {
                report = "the realm cannot begin a foreign offensive while an internal war is active";
                return false;
            }
            if (!liberationProposal && conflictLoad.ForeignWarCount >= 2)
            {
                report = $"the realm is already overextended across {conflictLoad.ForeignWarCount} foreign wars";
                return false;
            }

            float requiredWarWill = BellumCivileOptions.WarWillDeclareThreshold
                + (!liberationProposal && conflictLoad.ForeignWarCount == 1
                    ? C.WarPeaceRevampExistingWarDeclarationThresholdPenalty
                    : 0f);
            float warWill = GetWarWill(clan);
            // Client eligibility above already checks War Will with liberation resolve.
            if (!liberationProposal && warWill < requiredWarWill)
            {
                report = $"war will too low ({warWill:0.0}/{requiredWarWill:0.0})";
                return false;
            }

            List<WarTargetScore> eligibleTargets = GetRankedTargets(clan, forceRefresh: true)
                .Where(score => score.TargetKingdom != null
                    && !clan.Kingdom.IsAtWarWith(score.TargetKingdom)
                    && (score.IsLiberationTarget
                        || BellumCivileOptions.MinimumPeaceBeforeRenewedWarDays <= 0
                        || score.TargetKingdom.GetStanceWith(clan.Kingdom).PeaceDeclarationDate.ElapsedDaysUntilNow
                            > BellumCivileOptions.MinimumPeaceBeforeRenewedWarDays))
                .ToList();
            WarTargetScore targetScore = eligibleTargets.FirstOrDefault(score =>
                score.IsLiberationTarget
                || WarFrontReadinessService.Assess(clan.Kingdom, score.TargetKingdom).CanDeclare);
            if (targetScore == null)
            {
                WarTargetScore blockedTarget = eligibleTargets.FirstOrDefault();
                if (!liberationProposal && blockedTarget != null)
                {
                    WarFrontReadinessAssessment blocked = WarFrontReadinessService.Assess(
                        clan.Kingdom,
                        blockedTarget.TargetKingdom);
                    if (blocked.IsAdditionalFront && !blocked.CanDeclare)
                    {
                        report = blocked.IsThirdOrLaterFront
                            ? $"the realm is already overextended across {blocked.ExistingForeignWarCount} foreign wars"
                            : $"combined-front strength is insufficient against {blockedTarget.TargetKingdom.Name} "
                                + $"({blocked.PowerRatio:0.00}/{C.WarPeaceRevampSecondFrontMinimumPowerRatio:0.00})";
                        return false;
                    }
                }

                report = "no credible war target is currently available";
                return false;
            }

            int influenceCost = Campaign.Current?.Models?.DiplomacyModel?.GetInfluenceCostOfProposingWar(clan) ?? 200;
            if (!NpcInfluenceBudgetService.CanAfford(
                clan,
                influenceCost,
                NpcInfluenceExpenseKind.Discretionary))
            {
                NpcInfluenceBudgetAssessment budget = NpcInfluenceBudgetService.Assess(
                    clan,
                    influenceCost,
                    NpcInfluenceExpenseKind.Discretionary);
                NpcInfluenceBudgetService.RecordBlocked(
                    clan,
                    influenceCost,
                    NpcInfluenceExpenseKind.Discretionary,
                    "war_proposal",
                    budget.ProtectedReserve);
                report = $"insufficient influence reserve ({clan.Influence:0}/{budget.RequiredInfluence:0})";
                return false;
            }

            DeclareWarDecision warDecision = new DeclareWarDecision(clan, targetScore.TargetKingdom);
            if (!warDecision.CanMakeDecision(out TaleWorlds.Localization.TextObject reason))
            {
                report = reason?.ToString() ?? "war decision not allowed";
                return false;
            }

            decision = warDecision;
            report = $"war will {warWill:0.0}; target={targetScore.TargetKingdom.Name}; score={targetScore.Score:0.0}";
            return true;
        }

        internal bool TryCreatePeaceDecision(Clan clan, out KingdomDecision decision, out string report)
        {
            decision = null;
            report = "revamp disabled";
            if (!IsRevampEnabled())
                return false;
            if (!IsValidEvaluatedClan(clan) || clan.Kingdom == null)
            {
                report = "invalid proposing clan";
                return false;
            }
            if (ClientKingdomBehavior.Instance?.IsClientKingdom(clan.Kingdom) == true)
            {
                report = "client kingdoms cannot negotiate peace independently";
                return false;
            }
            if (clan.Kingdom.UnresolvedDecisions.Any(existing => existing is MakePeaceKingdomDecision))
            {
                report = "kingdom already has an unresolved peace decision";
                return false;
            }

            float warWill = GetWarWill(clan);
            if (warWill >= BellumCivileOptions.WarWillPeaceThreshold)
            {
                report = $"war will still favors continuing war ({warWill:0.0})";
                return false;
            }

            WarScoreBehavior warScore = Campaign.Current?.GetCampaignBehavior<WarScoreBehavior>();
            if (warScore == null)
            {
                report = "war score tracker unavailable";
                return false;
            }

            if (!warScore.TryGetForeignParleyProposal(clan, out WarScoreRecord selectedWar, out bool preferWhitePeace, out report, requireLowWill: true))
                return false;

            Kingdom target = Kingdom.All.FirstOrDefault(kingdom => kingdom != null
                && (kingdom.StringId == selectedWar.AttackerKingdomId || kingdom.StringId == selectedWar.DefenderKingdomId)
                && kingdom != clan.Kingdom);
            if (target == null)
            {
                report = "the selected peace target no longer exists";
                return false;
            }

            bool playerInvolved = IsPlayerPoliticalParticipant(clan.Kingdom, target);
            if (playerInvolved)
            {
                decision = new MakePeaceKingdomDecision(clan, target, 0, 0);
                report = preferWhitePeace
                    ? $"mutual exhaustion supports white peace with {target.Name}"
                    : $"low war will supports opening treaty negotiations with {target.Name}";
                return true;
            }

            ForeignTreatyBehavior treaties = Campaign.Current?.GetCampaignBehavior<ForeignTreatyBehavior>();
            treaties?.TryQueueAiParley(
                selectedWar,
                forced: false,
                preferWhitePeace,
                preferWhitePeace ? "mutual exhaustion" : "low-war-will peace proposal",
                clan.Kingdom,
                out report);
            return false;
        }

        internal static bool CanSupportCourtCampaign(Kingdom realm, Kingdom target)
        {
            if (!CourtCampaignObjectiveSource.ValidPair(realm, target) || realm.IsAtWarWith(target)) return false;
            var load = GetConflictLoad(realm);
            if (load.CivilWarCount > 0 || load.ClaimFeudWarCount > 0 || load.RealmFeudCount > 0
                || load.ForeignWarCount >= 2 || !WarFrontReadinessService.Assess(realm, target).CanDeclare) return false;
            if (BellumCivileOptions.MinimumPeaceBeforeRenewedWarDays > 0
                && target.GetStanceWith(realm).PeaceDeclarationDate.ElapsedDaysUntilNow <= BellumCivileOptions.MinimumPeaceBeforeRenewedWarDays) return false;
            try
            {
                var permission = Campaign.Current?.Models?.KingdomDecisionPermissionModel;
                return permission != null && permission.IsWarDecisionAllowedBetweenKingdoms(realm, target, out _);
            }
            catch { return false; }
        }

        internal bool TryEvaluateWarSupport(
            DeclareWarDecision decision,
            Clan voter,
            bool supportWarOutcome,
            out float support)
        {
            support = 0f;
            if (!IsRevampEnabled() || decision == null || !IsValidEvaluatedClan(voter))
                return false;

            Kingdom source = decision.Kingdom;
            Kingdom target = decision.FactionToDeclareWarOn as Kingdom;
            if (source == null || target == null || voter.Kingdom != source)
                return false;

            WarTargetScore targetScore = GetRankedTargets(voter, forceRefresh: true)
                .FirstOrDefault(score => score.TargetKingdom == target);
            float warWill = GetWarWill(voter);
            ClientKingdomBehavior clients = ClientKingdomBehavior.Instance;
            bool liberationTarget = clients?.IsClientOf(source, target) == true;
            if (liberationTarget)
                warWill = clients.GetLiberationWarWill(voter, target, warWill);
            float targetValue = targetScore?.Score ?? 0f;
            float trait = (voter.Leader?.GetTraitLevel(DefaultTraits.Valor) ?? 0) * 6f
                - (voter.Leader?.GetTraitLevel(DefaultTraits.Mercy) ?? 0) * 5f;
            float raw = warWill - 55f + targetValue * 0.35f + trait;
            raw += CourtAgendaBehavior.Current?.CampaignInitiativeBonus(source, target, voter) ?? 0;
            raw += CourtAgendaBehavior.Current?.SubjugationWarBonus(source, target, voter) ?? 0;
            raw += CourtAgendaBehavior.Current?.ClaimWarBonus(source, target, voter) ?? 0;
            raw -= Campaign.Current?.GetCampaignBehavior<HostagePactBehavior>()?.WarDeterrence(voter, target, council: true) ?? 0;
            if (!liberationTarget)
            {
                WarFrontReadinessAssessment frontReadiness = WarFrontReadinessService.Assess(source, target);
                if (frontReadiness.IsAdditionalFront)
                {
                    raw = frontReadiness.CanDeclare
                        ? raw - frontReadiness.CouncilSupportPenalty
                        : Math.Min(raw, -100f);
                }
            }
            support = supportWarOutcome ? raw : -raw;
            return true;
        }

        internal bool TryEvaluatePeaceSupport(
            MakePeaceKingdomDecision decision,
            Clan voter,
            bool supportPeaceOutcome,
            out float support)
        {
            support = 0f;
            if (!IsRevampEnabled() || decision == null || !IsValidEvaluatedClan(voter))
                return false;

            Kingdom source = decision.Kingdom;
            Kingdom target = decision.FactionToMakePeaceWith as Kingdom;
            if (source == null || target == null || voter.Kingdom != source)
                return false;

            WarTargetScore targetScore = GetRankedTargets(voter, forceRefresh: true)
                .FirstOrDefault(score => score.TargetKingdom == target);
            float warWill = GetWarWill(voter);
            float personalStake = targetScore?.IsClaimTarget == true ? 12f : 0f;
            float trait = (voter.Leader?.GetTraitLevel(DefaultTraits.Mercy) ?? 0) * 6f
                - (voter.Leader?.GetTraitLevel(DefaultTraits.Valor) ?? 0) * 5f;
            float tributePenalty = decision.DailyTributeToBePaid > 0 ? 8f : 0f;
            var war = Campaign.Current.GetCampaignBehavior<WarScoreBehavior>()?.GetActiveWar(source, target);
            warWill = CourtAgendaBehavior.EffectiveWarWill(voter,war,warWill);
            float readiness = war == null ? 55f - warWill
                : PeaceReadiness.Evaluate((float)CampaignTime.Now.ToDays - war.StartedDay, war.Score,
                    warWill, BellumCivileOptions.WarDurationReluctanceDays).Total;
            float raw = readiness - personalStake + trait - tributePenalty;
            raw += CourtAgendaBehavior.Current?.PeaceInitiativeBonus(source, war, voter) ?? 0;
            support = supportPeaceOutcome ? raw : -raw;
            return true;
        }

        internal static bool IsRevampEnabled()
        {
            return BellumCivileOptions.EnableWarPeaceLogicRevamp;
        }

        internal static Kingdom GetPlayerPoliticalKingdom()
        {
            Clan playerClan = Clan.PlayerClan;
            return playerClan != null && !playerClan.IsUnderMercenaryService
                ? playerClan.Kingdom
                : null;
        }

        internal static bool IsPlayerPoliticalParticipant(Kingdom kingdom)
        {
            return kingdom != null && GetPlayerPoliticalKingdom() == kingdom;
        }

        internal static bool IsPlayerPoliticalParticipant(Kingdom first, Kingdom second)
        {
            Kingdom playerKingdom = GetPlayerPoliticalKingdom();
            return playerKingdom != null && (playerKingdom == first || playerKingdom == second);
        }

        private void OnDailyTickClan(Clan clan)
        {
            if (!IsRevampEnabled() || !IsValidEvaluatedClan(clan))
                return;

            EnsureCollectionsInitialized();
            // War Will is a cheap scalar and genuinely advances every day. Only target ranking remains
            // on the staggered political evaluation schedule.
            ApplyDailyWarWillDrift(clan);

            int today = (int)CampaignTime.Now.ToDays;
            if (_nextEvaluationDayByClanId.TryGetValue(clan.StringId, out int nextDay) && today < nextDay)
                return;

            GetRankedTargets(clan, forceRefresh: true);
            _nextEvaluationDayByClanId[clan.StringId] = today + BellumCivileOptions.WarWillClanEvaluationIntervalDays;
        }

        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            InitializeExistingWarMomentum();
        }

        private void OnDailyTick()
        {
            if (!IsRevampEnabled())
                return;

            EnsureCollectionsInitialized();
            // Initial campaign wars may be established after OnSessionLaunched. WarScoreBehavior runs its
            // reconciliation earlier in the daily event order, so this pass can consume the shared attacker/
            // defender assignment on the first campaign tick. Persisted war-instance keys make later calls inert.
            InitializeExistingWarMomentum();

            float today = CurrentDay;
            foreach (WarWillPressureRecord record in _pressureRecords
                .Where(record => record != null && record.IsActive && record.IsExpired(today)))
            {
                record.End();
            }
        }

        private void ApplyDailyWarWillDrift(Clan clan)
        {
            float current = GetWarWill(clan);
            float softThreshold = CalculateSoftWarWillThreshold(clan);
            WarWillConflictLoad conflictLoad = GetConflictLoad(clan.Kingdom, enthusiasmOnly: true);
            float step;
            if (conflictLoad.DirectWarCount > 0)
            {
                step = CalculateWartimeDailyStep(clan, conflictLoad.AdditionalConflictLoad);
            }
            else
            {
                // Private feuds suspend new foreign offensives at the proposal gate. They do not
                // suppress peacetime recovery across every uninvolved clan in the parent realm.
                step = CalculatePeacetimeDailyStep(clan, current, softThreshold);
            }

            _warWillByClanId[clan.StringId] = Math.Max(0f, Math.Min(100f, current + step));
        }

        private float CalculateSoftWarWillThreshold(Clan clan)
        {
            FactionType? ideology = GetIdeology(clan);

            if (ideology == FactionType.Glory)
                return 85f;
            if (ideology == FactionType.Liberty)
                return 40f;
            if (ideology == FactionType.Royalists)
            {
                Clan rulerClan = clan.Kingdom?.RulingClan;
                return rulerClan != null && rulerClan != clan ? GetWarWill(rulerClan) : 50f;
            }

            bool hasMeaningfulTarget = GetRankedTargets(clan).Any(target => target.IsClaimTarget);
            if (ideology == FactionType.Nobility)
                return hasMeaningfulTarget ? 80f : 50f;

            int valor = clan.Leader?.GetTraitLevel(DefaultTraits.Valor) ?? 0;
            int mercy = clan.Leader?.GetTraitLevel(DefaultTraits.Mercy) ?? 0;
            return Math.Max(10f, Math.Min(90f, 45f + valor * 8f - mercy * 6f));
        }

        private float CalculatePeacetimeDailyStep(Clan clan, float current, float softThreshold)
        {
            float gap = 100f - current;
            if (gap < 0.1f)
                return 0f;

            float recovery = 1f;

            // Ideology still determines the natural peacetime tempo, but no longer creates a
            // permanent ceiling that can leave an entire council unable to propose another war.
            if (current >= softThreshold)
                recovery *= CalculatePeacetimeOverflowRecoveryMultiplier(clan);

            return Math.Min(recovery, gap);
        }

        private float CalculatePeacetimeOverflowRecoveryMultiplier(Clan clan)
        {
            FactionType? ideology = GetIdeology(clan);
            if (ideology == FactionType.Glory)
                return C.WarPeaceRevampMilitaristOverflowRecoveryMultiplier;
            if (ideology == FactionType.Liberty)
                return C.WarPeaceRevampPopulistOverflowRecoveryMultiplier;
            if (ideology == FactionType.Royalists)
                return C.WarPeaceRevampRoyalistOverflowRecoveryMultiplier;
            if (ideology == FactionType.Nobility)
            {
                bool hasMeaningfulTarget = GetRankedTargets(clan)
                    .Any(target => target.IsClaimTarget);
                return hasMeaningfulTarget
                    ? C.WarPeaceRevampAristocratClaimOverflowRecoveryMultiplier
                    : C.WarPeaceRevampAristocratOverflowRecoveryMultiplier;
            }

            return C.WarPeaceRevampUnaffiliatedOverflowRecoveryMultiplier;
        }

        private float CalculateWartimeDailyStep(Clan clan, float additionalConflictLoad)
        {
            FactionType? ideology = GetIdeology(clan);
            bool activeClaimWar = GetRankedTargets(clan).Any(target => target.IsActiveWar && target.IsClaimTarget);

            float baseStep;
            if (ideology == FactionType.Glory)
                baseStep = -0.25f;
            else if (ideology == FactionType.Liberty)
                baseStep = -1.25f;
            else if (ideology == FactionType.Nobility && activeClaimWar)
                baseStep = -0.35f;
            else
                baseStep = -0.65f;

            if (additionalConflictLoad <= 0f)
                return baseStep;

            float extraDrain = CalculateModifiedConflictDrain(clan, additionalConflictLoad, activeClaimWar);
            return baseStep - extraDrain;
        }

        private float CalculateModifiedConflictDrain(Clan clan, float conflictLoad, bool activeClaimWar)
        {
            float extraDrain = BellumCivileOptions.WarWillExtraWarDailyDrain * conflictLoad;
            FactionType? ideology = GetIdeology(clan);
            if (ideology == FactionType.Glory)
                extraDrain *= 0.4f;
            else if (ideology == FactionType.Liberty)
                extraDrain *= 2f;
            else if (ideology == FactionType.Nobility && activeClaimWar)
                extraDrain *= 0.5f;

            return extraDrain;
        }

        private void OnPeaceMade(IFaction side1Faction, IFaction side2Faction, MakePeaceAction.MakePeaceDetail detail)
        {
            Kingdom first = side1Faction as Kingdom;
            Kingdom second = side2Faction as Kingdom;
            InvalidateKingdom(first);
            InvalidateKingdom(second);
            EndPressureRecordsBetween(first, second);
        }

        private void OnClanChangedKingdom(
            Clan clan,
            Kingdom oldKingdom,
            Kingdom newKingdom,
            ChangeKingdomAction.ChangeKingdomActionDetail detail,
            bool showNotification)
        {
            if (!IsRevampEnabled()
                || clan == null
                || oldKingdom == null
                || newKingdom != null
                || detail != ChangeKingdomAction.ChangeKingdomActionDetail.LeaveWithRebellion)
            {
                return;
            }

            // Vanilla copies every war of the former realm onto a clan that leaves with its fiefs.
            // Bellum treats the secession itself as the new conflict; unrelated crown wars would
            // otherwise leave an independent clan trapped in wars outside the kingdom war-score path.
            List<Kingdom> inheritedEnemies = clan.FactionsAtWarWith
                .OfType<Kingdom>()
                .Where(enemy => enemy != null
                    && enemy != oldKingdom
                    && clan.IsAtWarWith(enemy)
                    && oldKingdom.IsAtWarWith(enemy))
                .Distinct()
                .ToList();

            foreach (Kingdom enemy in inheritedEnemies)
                MakePeaceAction.Apply(clan, enemy);

            if (inheritedEnemies.Count > 0)
            {
                BellumCivileLogger.Log(
                    $"Cleared inherited crown wars after rebellious clan departure; clan={clan.StringId}; former_realm={oldKingdom.StringId}; preserved_rebellion=true; cleared={inheritedEnemies.Count}; enemies={string.Join(",", inheritedEnemies.Select(enemy => enemy.StringId))}.");
            }

            EnsureCollectionsInitialized();
            _pendingVanillaSecessions[clan.StringId] = oldKingdom.StringId;
            BellumCivileLogger.Log(
                $"Queued landed clan rebellion for Bellum independence-war adoption; clan={clan.StringId}; former_realm={oldKingdom.StringId}; strongholds={CountClanStrongholds(clan)}.");
        }

        internal bool TryStartPlayerLandedSecession(out string failureReason)
        {
            failureReason = string.Empty;
            if (!IsRevampEnabled())
            {
                failureReason = "war and peace revamp disabled";
                return false;
            }

            Clan playerClan = Clan.PlayerClan;
            Kingdom parentRealm = playerClan?.Kingdom;
            if (playerClan?.Leader == null || playerClan.Leader.IsDead || playerClan.IsEliminated)
            {
                failureReason = "player clan is unavailable";
                return false;
            }

            if (parentRealm == null || parentRealm.IsEliminated)
            {
                failureReason = "player has no valid parent realm";
                return false;
            }

            if (parentRealm.RulingClan == playerClan)
            {
                failureReason = "the ruling clan must abdicate rather than secede";
                return false;
            }

            // Bannerlord always classifies the player clan as a minor faction. For player-only
            // political paths, current kingdom membership and landed status are authoritative.
            if (playerClan.IsBanditFaction)
            {
                failureReason = "player clan is a bandit faction";
                return false;
            }

            int strongholdCount = CountClanStrongholds(playerClan);
            if (strongholdCount <= 0)
            {
                failureReason = "player clan holds no town or castle";
                return false;
            }

            FactionManagerBehavior factionManager = Campaign.Current?.GetCampaignBehavior<FactionManagerBehavior>();
            if (factionManager == null)
            {
                failureReason = "faction manager unavailable";
                return false;
            }

            FactionObject existingRebellion = factionManager.GetRebelFaction(playerClan);
            if (existingRebellion?.IsCivilWarActive() == true)
            {
                failureReason = "player clan is already fighting a civil war";
                return false;
            }

            if (existingRebellion != null)
            {
                if (existingRebellion.Leader == playerClan)
                    factionManager.RemoveFaction(existingRebellion);
                else
                    existingRebellion.RemoveMember(playerClan);
            }

            TextObject factionName = new TextObject("{=BC_FacName_Indep}{CLAN_NAME} Secessionists");
            factionName.SetTextVariable("CLAN_NAME", playerClan.Name);
            FactionObject independenceFaction = new FactionObject(
                factionName.ToString(),
                parentRealm,
                playerClan,
                FactionType.Independence);

            factionManager.RegisterNewFaction(independenceFaction);
            if (!independenceFaction.StartImmediateSecessionFromParentRealm())
            {
                factionManager.RemoveFaction(independenceFaction);
                failureReason = "independence realm transition failed";
                return false;
            }

            GuardClanChangedKingdomRelationsPatch.ApplySafeLeaveRelationPenalties(
                playerClan,
                parentRealm,
                ChangeKingdomAction.ChangeKingdomActionDetail.LeaveWithRebellion);

            BellumCivileLogger.Log(
                $"Started player landed secession directly from kingdom menu; clan={playerClan.StringId}; former_realm={parentRealm.StringId}; rebel_realm={independenceFaction.GetRebelKingdom()?.StringId ?? "null"}; strongholds={strongholdCount}; supporters={Math.Max(0, independenceFaction.Members.Count - 1)}.");
            return true;
        }

        private void ProcessPendingVanillaSecessions()
        {
            foreach (KeyValuePair<string, string> pending in _pendingVanillaSecessions.ToList())
            {
                _pendingVanillaSecessions.Remove(pending.Key);

                if (!IsRevampEnabled())
                    continue;

                Clan clan = Clan.All.FirstOrDefault(candidate => candidate != null && candidate.StringId == pending.Key);
                Kingdom formerRealm = Kingdom.All.FirstOrDefault(candidate => candidate != null && candidate.StringId == pending.Value);
                if (!CanAdoptVanillaSecession(clan, formerRealm, out string rejectionReason))
                {
                    BellumCivileLogger.Log(
                        $"Skipped Bellum independence-war adoption; clan={pending.Key}; former_realm={pending.Value}; reason={rejectionReason}.");
                    continue;
                }

                FactionManagerBehavior factionManager = Campaign.Current?.GetCampaignBehavior<FactionManagerBehavior>();
                if (factionManager == null)
                {
                    BellumCivileLogger.Log(
                        $"Skipped Bellum independence-war adoption; clan={pending.Key}; former_realm={pending.Value}; reason=faction manager unavailable.");
                    continue;
                }

                TextObject factionName = new TextObject("{=BC_FacName_Indep}{CLAN_NAME} Secessionists");
                factionName.SetTextVariable("CLAN_NAME", clan.Name);
                FactionObject independenceFaction = new FactionObject(
                    factionName.ToString(),
                    formerRealm,
                    clan,
                    FactionType.Independence);

                factionManager.RegisterNewFaction(independenceFaction);
                if (!independenceFaction.StartImmediateSecessionAfterVanillaDeparture())
                {
                    factionManager.RemoveFaction(independenceFaction);
                    BellumCivileLogger.Log(
                        $"Failed Bellum independence-war adoption after faction registration; clan={clan.StringId}; former_realm={formerRealm.StringId}. The vanilla clan war remains active.");
                    continue;
                }

                BellumCivileLogger.Log(
                    $"Adopted vanilla landed-clan rebellion as Bellum independence war; clan={clan.StringId}; former_realm={formerRealm.StringId}; rebel_realm={independenceFaction.GetRebelKingdom()?.StringId ?? "null"}; strongholds={CountClanStrongholds(clan)}.");
            }
        }

        private static bool CanAdoptVanillaSecession(Clan clan, Kingdom formerRealm, out string reason)
        {
            if (clan == null)
            {
                reason = "clan no longer exists";
                return false;
            }

            if (formerRealm == null || formerRealm.IsEliminated)
            {
                reason = "former realm no longer exists";
                return false;
            }

            bool isPlayerClan = clan == Clan.PlayerClan;
            if (clan.IsEliminated
                || clan.IsBanditFaction
                || (!isPlayerClan && (clan.IsMinorFaction || clan.IsUnderMercenaryService)))
            {
                reason = "clan is not an eligible landed noble clan";
                return false;
            }

            if (clan.Kingdom != null)
            {
                reason = "clan already joined a kingdom";
                return false;
            }

            if (CountClanStrongholds(clan) <= 0)
            {
                reason = "clan retained no town or castle";
                return false;
            }

            if (!clan.IsAtWarWith(formerRealm))
            {
                reason = "vanilla rebellion war already ended";
                return false;
            }

            reason = string.Empty;
            return true;
        }

        private static int CountClanStrongholds(Clan clan)
        {
            return clan?.Settlements?.Count(settlement => settlement != null && (settlement.IsTown || settlement.IsCastle)) ?? 0;
        }

        private void OnWarDeclared(IFaction side1Faction, IFaction side2Faction, DeclareWarAction.DeclareWarDetail detail)
        {
            Kingdom first = side1Faction as Kingdom;
            Kingdom second = side2Faction as Kingdom;
            InvalidateKingdom(first);
            InvalidateKingdom(second);
            if (!IsRevampEnabled() || !IsValidWarKingdom(first) || !IsValidWarKingdom(second))
                return;

            bool scripted = StorylineWarProtectionHelper.IsPeaceBlocked(first, second);
            ApplyWarOpeningMomentum(first, second, isDefender: false, scripted ? 0 : GetConflictLoad(first, enthusiasmOnly: true).AdditionalConflictLoad, "war declared");
            ApplyWarOpeningMomentum(second, first, isDefender: true, scripted ? 0 : GetConflictLoad(second, enthusiasmOnly: true).AdditionalConflictLoad, "realm attacked");

            _initializedWarMomentumByConflictId[BuildWarMomentumKey(first, second)] = true;
        }

        internal void RetargetCivilWarMomentum(Kingdom rebel, Kingdom previous, Kingdom successor)
        {
            EnsureCollectionsInitialized();
            string oldKey = BuildConflictKey(rebel, previous);
            string newKey = BuildConflictKey(rebel, successor);
            foreach (var pressure in _pressureRecords.Where(p => p != null))
                pressure.RetargetConflict(oldKey, newKey, previous.StringId, successor.StringId);
            _initializedWarMomentumByConflictId[BuildWarMomentumKey(rebel, successor)] = true;
            InvalidateKingdom(rebel);
            InvalidateKingdom(previous);
            InvalidateKingdom(successor);
        }

        internal void EndSupersededCivilWarMomentum(Kingdom first, Kingdom second)
        {
            EnsureCollectionsInitialized();
            string key = BuildConflictKey(first, second);
            foreach (var pressure in _pressureRecords.Where(p => p != null && p.ConflictKey == key)) pressure.End();
            InvalidateKingdom(first);
            InvalidateKingdom(second);
        }

        private void InitializeExistingWarMomentum()
        {
            if (!IsRevampEnabled())
                return;

            EnsureCollectionsInitialized();
            WarScoreBehavior warScore = Campaign.Current?.GetCampaignBehavior<WarScoreBehavior>();
            Dictionary<string, int> initializedForeignWarsByKingdomId = CountInitializedForeignWarMomentum();
            foreach (Kingdom first in Kingdom.All.Where(IsValidWarKingdom).Where(kingdom => !IsTemporaryWarKingdom(kingdom)))
            {
                foreach (Kingdom second in Kingdom.All.Where(IsValidWarKingdom).Where(kingdom => !IsTemporaryWarKingdom(kingdom)))
                {
                    if (string.CompareOrdinal(first.StringId, second.StringId) >= 0 || !first.IsAtWarWith(second))
                        continue;

                    string momentumKey = BuildWarMomentumKey(first, second);
                    if (_initializedWarMomentumByConflictId.ContainsKey(momentumKey))
                        continue;

                    WarScoreRecord war = warScore?.GetActiveWar(first, second);
                    Kingdom attacker = ResolveKingdom(war?.AttackerKingdomId);
                    Kingdom defender = ResolveKingdom(war?.DefenderKingdomId);
                    if (attacker == null || defender == null)
                    {
                        bool firstIsAttacker = MBRandom.RandomFloat < 0.5f;
                        attacker = firstIsAttacker ? first : second;
                        defender = firstIsAttacker ? second : first;
                    }

                    bool scripted = StorylineWarProtectionHelper.IsPeaceBlocked(attacker, defender);
                    float attackerExistingLoad = scripted ? 0 : GetInternalConflictLoad(attacker) + GetCount(initializedForeignWarsByKingdomId, attacker.StringId);
                    float defenderExistingLoad = scripted ? 0 : GetInternalConflictLoad(defender) + GetCount(initializedForeignWarsByKingdomId, defender.StringId);
                    ApplyWarOpeningMomentum(attacker, defender, isDefender: false, attackerExistingLoad, "starting war; aggressor inferred");
                    ApplyWarOpeningMomentum(defender, attacker, isDefender: true, defenderExistingLoad, "starting war; defender inferred");

                    _initializedWarMomentumByConflictId[momentumKey] = true;
                    if (!scripted)
                    {
                        IncrementCount(initializedForeignWarsByKingdomId, attacker.StringId);
                        IncrementCount(initializedForeignWarsByKingdomId, defender.StringId);
                    }
                    BellumCivileLogger.Log($"Initialized existing-war momentum; attacker={attacker.StringId}; defender={defender.StringId}; war={momentumKey}.");
                }
            }
        }

        private void ApplyWarOpeningMomentum(Kingdom kingdom, Kingdom target, bool isDefender, float existingConflictLoad, string normalReason)
        {
            foreach (Clan clan in GetEvaluatedClans(kingdom))
            {
                if (existingConflictLoad > 0f)
                {
                    ApplyWarWillShock(
                        clan,
                        target,
                        CalculateMultipleFrontShock(clan, target, existingConflictLoad),
                        WarWillReasonType.MultipleWars,
                        "fighting on multiple fronts");
                }
                else
                {
                    ApplyWarWillShock(
                        clan,
                        target,
                        CalculateWarStartShock(clan, target, isDefender),
                        isDefender ? WarWillReasonType.RealmAttacked : WarWillReasonType.WarDeclared,
                        normalReason);
                }
            }
        }

        private float CalculateMultipleFrontShock(Clan clan, Kingdom target, float existingConflictLoad)
        {
            float magnitude = BellumCivileOptions.WarWillMultipleFrontShockPerLoad * Math.Max(0f, existingConflictLoad);
            if (magnitude <= 0f)
                return 0f;

            FactionType? ideology = GetIdeology(clan);
            bool claimTarget = GetRankedTargets(clan).Any(score => score.TargetKingdom == target && score.IsClaimTarget);
            float multiplier = 1f;
            if (ideology == FactionType.Glory)
                multiplier = 0.40f;
            else if (ideology == FactionType.Liberty)
                multiplier = 1.50f;
            else if (ideology == FactionType.Nobility && claimTarget)
                multiplier = 0.75f;
            else if (ideology == FactionType.Royalists)
                multiplier = 0.75f;

            return -Math.Min(C.WarPeaceRevampMultipleFrontShockCap, magnitude * multiplier);
        }

        private Dictionary<string, int> CountInitializedForeignWarMomentum()
        {
            Dictionary<string, int> result = new Dictionary<string, int>();
            List<Kingdom> kingdoms = Kingdom.All
                .Where(IsValidWarKingdom)
                .Where(kingdom => !IsTemporaryWarKingdom(kingdom))
                .ToList();
            foreach (Kingdom first in kingdoms)
            {
                foreach (Kingdom second in kingdoms)
                {
                    if (string.CompareOrdinal(first.StringId, second.StringId) >= 0
                        || StorylineWarProtectionHelper.IsPeaceBlocked(first, second)
                        || !first.IsAtWarWith(second)
                        || !_initializedWarMomentumByConflictId.ContainsKey(BuildWarMomentumKey(first, second)))
                    {
                        continue;
                    }

                    IncrementCount(result, first.StringId);
                    IncrementCount(result, second.StringId);
                }
            }

            return result;
        }

        private static float GetInternalConflictLoad(Kingdom kingdom)
        {
            WarWillConflictLoad load = GetConflictLoad(kingdom);
            return (load.CivilWarCount * C.WarPeaceRevampCivilWarConflictLoad)
                + (load.ClaimFeudWarCount * C.WarPeaceRevampClaimFeudConflictLoad)
                + (load.RealmFeudCount * C.WarPeaceRevampRealmFeudConflictLoad);
        }

        private static int GetCount(Dictionary<string, int> counts, string key)
        {
            return !string.IsNullOrWhiteSpace(key) && counts.TryGetValue(key, out int count) ? count : 0;
        }

        private static void IncrementCount(Dictionary<string, int> counts, string key)
        {
            if (!string.IsNullOrWhiteSpace(key))
                counts[key] = GetCount(counts, key) + 1;
        }

        private float CalculateWarStartShock(Clan clan, Kingdom target, bool isDefender)
        {
            float baseShock = isDefender
                ? BellumCivileOptions.WarWillDefenderWarDeclaredShock
                : BellumCivileOptions.WarWillAttackerWarDeclaredShock;
            if (baseShock <= 0f)
                return 0f;

            FactionType? ideology = GetIdeology(clan);
            float multiplier = 1f;
            bool claimTarget = GetRankedTargets(clan).Any(score => score.TargetKingdom == target && score.IsClaimTarget);

            switch (ideology)
            {
                case FactionType.Glory:
                    multiplier *= isDefender ? 1.10f : 1.25f;
                    break;
                case FactionType.Liberty:
                    multiplier *= isDefender ? 1.15f : 0.50f;
                    break;
                case FactionType.Nobility:
                    if (claimTarget)
                        multiplier *= isDefender ? 1.10f : 1.20f;
                    break;
                case FactionType.Royalists:
                    if (RulingClanHasClaimAgainst(clan?.Kingdom, target))
                        multiplier *= isDefender ? 1.15f : 1.30f;
                    break;
            }

            Hero leader = clan?.Leader;
            int valor = leader?.GetTraitLevel(DefaultTraits.Valor) ?? 0;
            int mercy = leader?.GetTraitLevel(DefaultTraits.Mercy) ?? 0;
            int honor = leader?.GetTraitLevel(DefaultTraits.Honor) ?? 0;
            float traitMultiplier = 1f
                + Math.Max(0, valor) * 0.08f
                + (isDefender ? Math.Max(0, honor) * 0.05f : 0f)
                - Math.Max(0, mercy) * (isDefender ? 0.02f : 0.08f);

            traitMultiplier = Math.Max(0.25f, Math.Min(1.75f, traitMultiplier));
            return baseShock * multiplier * traitMultiplier;
        }

        private bool RulingClanHasClaimAgainst(Kingdom sourceKingdom, Kingdom targetKingdom)
        {
            Clan ruler = sourceKingdom?.RulingClan;
            if (!IsValidEvaluatedClan(ruler) || targetKingdom == null)
                return false;

            return GetRankedTargets(ruler, forceRefresh: true)
                .Any(score => score.TargetKingdom == targetKingdom && score.IsClaimTarget);
        }

        private void OnSettlementOwnerChanged(
            Settlement settlement,
            bool openToClaim,
            Hero newOwner,
            Hero oldOwner,
            Hero capturerHero,
            ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail detail)
        {
            if (BellumTreatyTransferContext.IsTreatyTransfer)
                return;
            if (!IsRevampEnabled()
                || detail != ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail.BySiege
                || !IsStrategicFief(settlement))
            {
                return;
            }

            Clan oldClan = oldOwner?.Clan;
            Clan newClan = newOwner?.Clan ?? settlement?.OwnerClan;
            Kingdom oldKingdom = oldClan?.Kingdom;
            Kingdom newKingdom = newClan?.Kingdom;
            if (!IsValidWarKingdom(oldKingdom)
                || !IsValidWarKingdom(newKingdom)
                || oldKingdom == newKingdom
                || !oldKingdom.IsAtWarWith(newKingdom))
            {
                return;
            }

            string settlementId = settlement.StringId;
            string titleId = ResolveBaronyTitleId(settlement);
            float ownerLoss = settlement.IsTown
                ? C.WarPeaceRevampTownLostOwnerShock
                : C.WarPeaceRevampCastleLostOwnerShock;
            ownerLoss *= BellumCivileOptions.WarWillSettlementShockMultiplier;

            ApplyWarWillShock(
                oldClan,
                newKingdom,
                ownerLoss,
                settlement.IsTown ? WarWillReasonType.OwnTownLost : WarWillReasonType.OwnCastleLost,
                settlement.IsTown ? "own town lost" : "own castle lost",
                settlementId,
                titleId);

            List<Clan> nearbyClans = GetNearbyClans(oldKingdom, settlement, oldClan).ToList();
            foreach (Clan neighbor in nearbyClans)
            {
                ApplyWarWillShock(neighbor, newKingdom, C.WarPeaceRevampFiefLostNeighborShock * BellumCivileOptions.WarWillSettlementShockMultiplier, WarWillReasonType.NearbyFiefLost, "nearby fief lost", settlementId, titleId);
            }

            foreach (Clan clan in GetEvaluatedClans(oldKingdom)
                .Where(clan => clan != oldClan && !nearbyClans.Contains(clan)))
            {
                ApplyWarWillShock(clan, newKingdom, C.WarPeaceRevampFiefLostRealmShock * BellumCivileOptions.WarWillSettlementShockMultiplier, WarWillReasonType.RealmFiefLost, "realm fief lost", settlementId, titleId);
            }

            ApplyWarWillShock(newClan, oldKingdom, C.WarPeaceRevampSettlementCapturedShock * BellumCivileOptions.WarWillSettlementShockMultiplier, WarWillReasonType.SettlementCaptured, "settlement captured", settlementId, titleId);
            if (HasActiveClaimToTitle(newClan, titleId))
            {
                ApplyWarWillShock(newClan, oldKingdom, C.WarPeaceRevampClaimObjectiveAchievedShock * BellumCivileOptions.WarWillSettlementShockMultiplier, WarWillReasonType.ClaimedObjectiveAchieved, "claimed objective achieved", settlementId, titleId);
            }
        }

        private void OnMapEventEnded(MapEvent mapEvent)
        {
            if (!IsRevampEnabled() || mapEvent?.AttackerSide == null || mapEvent.DefenderSide == null || mapEvent.Winner == null)
                return;

            bool majorEngagement = IsMajorEngagement(mapEvent);
            MapEventSide winningSide = mapEvent.Winner;
            MapEventSide losingSide = winningSide == mapEvent.AttackerSide
                ? mapEvent.DefenderSide
                : mapEvent.AttackerSide;

            ApplyBattleSideShock(winningSide, losingSide, majorEngagement
                ? C.WarPeaceRevampMajorBattleVictoryShock
                : C.WarPeaceRevampBattleVictoryShock,
                majorEngagement ? WarWillReasonType.MajorBattleVictory : WarWillReasonType.BattleVictory,
                majorEngagement ? "major battle victory" : "battle victory",
                BellumCivileOptions.WarWillBattleShockMultiplier);
            ApplyBattleSideShock(losingSide, winningSide, majorEngagement
                ? C.WarPeaceRevampMajorBattleDefeatShock
                : C.WarPeaceRevampBattleDefeatShock,
                majorEngagement ? WarWillReasonType.MajorBattleDefeat : WarWillReasonType.BattleDefeat,
                majorEngagement ? "major battle defeat" : "battle defeat",
                BellumCivileOptions.WarWillBattleShockMultiplier);
            ApplyCasualtyWarWill(losingSide, winningSide);
            ApplyCasualtyWarWill(winningSide, losingSide);
        }

        private void OnVillageLooted(Village village)
        {
            if (!IsRevampEnabled())
                return;

            Clan victimClan = village?.Settlement?.OwnerClan;
            Kingdom victimKingdom = victimClan?.Kingdom;
            Kingdom attackerKingdom = village?.Settlement?.LastAttackerParty?.MapFaction as Kingdom;
            if (!IsValidWarKingdom(victimKingdom)
                || !IsValidWarKingdom(attackerKingdom)
                || victimKingdom == attackerKingdom
                || !victimKingdom.IsAtWarWith(attackerKingdom))
            {
                return;
            }

            string settlementId = village.Settlement.StringId;
            ApplyWarWillShock(victimClan, attackerKingdom, C.WarPeaceRevampVillageRaidOwnerShock * BellumCivileOptions.WarWillRaidShockMultiplier, WarWillReasonType.OwnVillageRaided, "own village raided", settlementId);

            foreach (Clan neighbor in GetNearbyClans(victimKingdom, village.Settlement, victimClan))
                ApplyWarWillShock(neighbor, attackerKingdom, C.WarPeaceRevampVillageRaidNeighborShock * BellumCivileOptions.WarWillRaidShockMultiplier, WarWillReasonType.NearbyVillageRaided, "nearby village raided", settlementId);
        }

        private void OnHeroPrisonerTaken(PartyBase captor, Hero prisoner)
        {
            if (!IsRevampEnabled() || HostageCustodyGuard.IsProtected(prisoner))
                return;

            Clan victimClan = prisoner?.Clan;
            Clan captorClan = captor?.MobileParty?.LeaderHero?.Clan;
            Kingdom victimKingdom = victimClan?.Kingdom;
            Kingdom captorKingdom = captorClan?.Kingdom ?? captor?.MapFaction as Kingdom;
            if (!IsValidWarKingdom(victimKingdom)
                || !IsValidWarKingdom(captorKingdom)
                || victimKingdom == captorKingdom
                || !victimKingdom.IsAtWarWith(captorKingdom))
            {
                return;
            }

            bool rulerCaptured = IsWarLeader(victimKingdom, prisoner);
            bool heirCaptured = !rulerCaptured && IsPrimaryHeirOfWarLeader(victimKingdom, prisoner);
            float victimShock = rulerCaptured
                ? C.WarPeaceRevampRulerCapturedShock
                : heirCaptured
                    ? C.WarPeaceRevampHeirCapturedShock
                    : C.WarPeaceRevampNobleCapturedShock;
            float captorShock = rulerCaptured
                ? C.WarPeaceRevampEnemyRulerCapturedShock
                : heirCaptured
                    ? C.WarPeaceRevampEnemyHeirCapturedShock
                    : C.WarPeaceRevampEnemyNobleCapturedShock;

            string reason = rulerCaptured ? "ruler captured" : heirCaptured ? "heir captured" : "noble captured";
            WarWillReasonType victimReasonType = rulerCaptured
                ? WarWillReasonType.RulerCaptured
                : heirCaptured
                    ? WarWillReasonType.HeirCaptured
                    : WarWillReasonType.NobleCaptured;
            WarWillReasonType captorReasonType = rulerCaptured
                ? WarWillReasonType.EnemyRulerCaptured
                : heirCaptured
                    ? WarWillReasonType.EnemyHeirCaptured
                    : WarWillReasonType.EnemyNobleCaptured;
            WarWillReasonType realmReasonType = rulerCaptured
                ? WarWillReasonType.RealmRulerCaptured
                : WarWillReasonType.RealmHeirCaptured;
            victimShock *= BellumCivileOptions.WarWillCaptivityShockMultiplier;
            captorShock *= BellumCivileOptions.WarWillCaptivityShockMultiplier;
            ApplyWarWillShock(victimClan, captorKingdom, victimShock, victimReasonType, reason, contextHeroId: prisoner.StringId);
            ApplyWarWillShock(captorClan, victimKingdom, captorShock, captorReasonType, "enemy " + reason, contextHeroId: prisoner.StringId);

            if (rulerCaptured || heirCaptured)
            {
                float realmShock = rulerCaptured
                    ? C.WarPeaceRevampRealmRulerCapturedShock
                    : C.WarPeaceRevampRealmHeirCapturedShock;
                foreach (Clan clan in GetEvaluatedClans(victimKingdom).Where(clan => clan != victimClan))
                    ApplyWarWillShock(clan, captorKingdom, realmShock * BellumCivileOptions.WarWillCaptivityShockMultiplier, realmReasonType, "realm " + reason, contextHeroId: prisoner.StringId);
            }
        }

        private void OnHeroKilled(
            Hero victim,
            Hero killer,
            KillCharacterAction.KillCharacterActionDetail detail,
            bool showNotification)
        {
            if (!IsRevampEnabled() || detail != KillCharacterAction.KillCharacterActionDetail.DiedInBattle)
                return;

            Clan victimClan = victim?.Clan;
            Clan killerClan = killer?.Clan;
            Kingdom victimKingdom = victimClan?.Kingdom;
            Kingdom killerKingdom = killerClan?.Kingdom;
            if (!IsValidWarKingdom(victimKingdom)
                || !IsValidWarKingdom(killerKingdom)
                || victimKingdom == killerKingdom
                || !victimKingdom.IsAtWarWith(killerKingdom))
            {
                return;
            }

            ApplyWarWillShock(victimClan, killerKingdom, C.WarPeaceRevampNobleKilledShock * BellumCivileOptions.WarWillBattleShockMultiplier, WarWillReasonType.NobleKilledInBattle, "noble killed in battle", contextHeroId: victim.StringId);
            ApplyWarWillShock(killerClan, victimKingdom, C.WarPeaceRevampEnemyNobleKilledShock * BellumCivileOptions.WarWillBattleShockMultiplier, WarWillReasonType.EnemyNobleKilled, "enemy noble killed", contextHeroId: victim.StringId);
        }

        internal void InvalidateKingdom(Kingdom kingdom)
        {
            if (kingdom?.Clans == null)
                return;

            foreach (Clan clan in kingdom.Clans)
                _targetScoresByClanId.Remove(clan.StringId);
        }

        private void ApplyWarWillShock(
            Clan clan,
            Kingdom target,
            float amount,
            WarWillReasonType reasonType,
            string reason,
            string contextSettlementId = "",
            string contextTitleId = "",
            string contextHeroId = "")
        {
            if (!IsRevampEnabled()
                || !IsValidEvaluatedClan(clan)
                || !IsValidWarKingdom(target)
                || Math.Abs(amount) < 0.0001f)
            {
                return;
            }

            EnsureCollectionsInitialized();
            if (amount > 0f)
            {
                float load = GetConflictLoad(clan.Kingdom, enthusiasmOnly: true).AdditionalConflictLoad;
                if (load > 0f)
                    amount /= 1f + (C.WarPeaceRevampPositiveShockDampingPerConflict * load);
            }

            float before = GetWarWill(clan);
            float after = ClampWarWill(before + amount);
            float applied = after - before;
            if (Math.Abs(applied) < 0.0001f)
                return;

            _warWillByClanId[clan.StringId] = after;
            _pressureRecords.Add(new WarWillPressureRecord(
                Guid.NewGuid().ToString("N"),
                clan.StringId,
                target.StringId,
                BuildConflictKey(clan.Kingdom, target),
                applied,
                reason,
                CurrentDay,
                CurrentDay + BellumCivileOptions.WarWillPressureMemoryDays,
                reasonType,
                contextSettlementId,
                contextTitleId,
                contextHeroId));

            // Tiny casualty shares are numerous in large late-game battles and add little diagnostic
            // value individually. Their pressure records remain intact; only the repetitive trace is skipped.
            if (reasonType != WarWillReasonType.BattleCasualties || Math.Abs(applied) >= 1f)
            {
                BellumCivileDebug.TraceIfEnabled(
                    "war will",
                    $"clan={clan.StringId}; target={target.StringId}; delta={applied:+0.0;-0.0;0.0}; value={after:0.0}; reason={reason}",
                    requestInGameDisplay: false);
            }
        }

        private IReadOnlyList<WarWillPressureRecord> GetActivePressureRecords(Clan clan)
        {
            EnsureCollectionsInitialized();
            if (clan == null || string.IsNullOrWhiteSpace(clan.StringId))
                return new List<WarWillPressureRecord>();

            float today = CurrentDay;
            return _pressureRecords
                .Where(record => record != null
                              && record.IsActive
                              && record.ClanId == clan.StringId
                              && !record.IsExpired(today))
                .ToList();
        }

        private void EndPressureRecordsBetween(Kingdom first, Kingdom second)
        {
            if (!IsValidWarKingdom(first) || !IsValidWarKingdom(second))
                return;

            EnsureCollectionsInitialized();
            string conflictKey = BuildConflictKey(first, second);
            foreach (WarWillPressureRecord record in _pressureRecords
                .Where(record => record != null && record.IsActive && record.ConflictKey == conflictKey))
            {
                record.End();
            }
        }

        private static IEnumerable<Clan> GetEvaluatedClans(Kingdom kingdom)
        {
            return kingdom?.Clans?.Where(IsValidEvaluatedClan) ?? Enumerable.Empty<Clan>();
        }

        private static FactionType? GetIdeology(Clan clan)
        {
            return Campaign.Current?.GetCampaignBehavior<FactionManagerBehavior>()
                ?.GetIdeologicalFaction(clan)
                ?.Type;
        }

        private static WarWillConflictLoad GetConflictLoad(Kingdom kingdom, bool enthusiasmOnly = false)
        {
            int foreignWars = 0;
            int civilWars = 0;
            int claimFeudWars = 0;
            WarScoreBehavior warScore = Campaign.Current?.GetCampaignBehavior<WarScoreBehavior>();
            foreach (Kingdom opponent in GetActiveDirectWarKingdoms(kingdom))
            {
                // Quest-locked fronts still count for strategic readiness, not passive fatigue.
                if (enthusiasmOnly && StorylineWarProtectionHelper.IsPeaceBlocked(kingdom, opponent)) continue;
                WarScoreConflictType conflictType = warScore?.GetActiveWar(kingdom, opponent)?.ConflictType
                    ?? WarScoreConflictType.ForeignWar;
                if (conflictType == WarScoreConflictType.CivilWar)
                    civilWars++;
                else if (conflictType == WarScoreConflictType.ClaimFeud)
                    claimFeudWars++;
                else
                    foreignWars++;
            }

            int realmFeuds = IsTemporaryWarKingdom(kingdom) ? 0 : CountActiveRealmFeuds(kingdom);
            return new WarWillConflictLoad(foreignWars, civilWars, claimFeudWars, realmFeuds);
        }

        private static IEnumerable<Kingdom> GetActiveDirectWarKingdoms(Kingdom kingdom)
        {
            if (kingdom?.FactionsAtWarWith == null)
                return Enumerable.Empty<Kingdom>();

            return kingdom.FactionsAtWarWith
                .OfType<Kingdom>()
                .Where(other => other != null && other != kingdom && !other.IsEliminated)
                .Distinct();
        }

        private static int CountActiveRealmFeuds(Kingdom kingdom)
        {
            if (kingdom == null || string.IsNullOrWhiteSpace(kingdom.StringId))
                return 0;

            return Campaign.Current?.GetCampaignBehavior<ClaimFeudWarBehavior>()?
                .GetActiveWars()
                .Count(war => war != null && war.ParentKingdomId == kingdom.StringId) ?? 0;
        }

        private IEnumerable<Clan> GetNearbyClans(Kingdom kingdom, Settlement settlement, Clan excludedClan)
        {
            if (kingdom == null || settlement == null)
                yield break;

            float threshold = GetNearbySettlementThreshold();
            foreach (Clan clan in GetEvaluatedClans(kingdom))
            {
                if (clan == excludedClan || clan.Settlements == null)
                    continue;

                bool hasNearbyHolding = clan.Settlements
                    .Where(IsStrategicFief)
                    .Any(holding => holding != null
                                 && Campaign.Current.Models.MapDistanceModel.GetDistance(
                                     settlement,
                                     holding,
                                     isFromPort: false,
                                     isTargetingPort: false,
                                     MobileParty.NavigationType.All) <= threshold);
                if (hasNearbyHolding)
                    yield return clan;
            }
        }

        private static float GetNearbySettlementThreshold()
        {
            float averageTownDistance = Campaign.Current.GetAverageDistanceBetweenClosestTwoTownsWithNavigationType(MobileParty.NavigationType.All);
            return averageTownDistance > 0f
                ? averageTownDistance * C.FeudalTitleAdjacencyTownDistanceMultiplier
                : Campaign.MapDiagonal * 0.15f;
        }

        private static bool IsStrategicFief(Settlement settlement)
        {
            return settlement != null && (settlement.IsTown || settlement.IsCastle);
        }

        private static string ResolveBaronyTitleId(Settlement settlement)
        {
            FeudalTitleBehavior titleBehavior = Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>();
            return titleBehavior != null
                && titleBehavior.TryGetBarony(settlement, out FeudalTitleRecord title)
                && title != null
                    ? title.TitleId
                    : string.Empty;
        }

        private static bool HasActiveClaimToTitle(Clan clan, string titleId)
        {
            if (clan == null || string.IsNullOrWhiteSpace(titleId))
                return false;

            FeudalTitleBehavior titleBehavior = Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>();
            return titleBehavior?.GetActiveClaimsByClan(clan)
                .Any(claim => claim != null && claim.TargetTitleId == titleId) == true;
        }

        private static bool IsMajorEngagement(MapEvent mapEvent)
        {
            if (mapEvent == null)
                return false;

            int startingStrength = CountStartingHealthyTroops(mapEvent.AttackerSide)
                + CountStartingHealthyTroops(mapEvent.DefenderSide);
            return startingStrength >= 300;
        }

        private static int CountStartingHealthyTroops(MapEventSide side)
        {
            if (side == null)
                return 0;

            return side.Parties.Sum(party => Math.Max(0, party?.HealthyManCountAtStart ?? 0));
        }

        private void ApplyBattleSideShock(MapEventSide side, MapEventSide opposingSide, float amount, WarWillReasonType reasonType, string reason, float multiplier)
        {
            if (side == null || opposingSide == null)
                return;

            foreach (Clan clan in side.Parties
                .Select(ResolveMapEventPartyClan)
                .Where(IsValidEvaluatedClan)
                .Distinct())
            {
                List<Kingdom> opponents = GetOpposingWarKingdoms(clan.Kingdom, opposingSide).ToList();
                if (opponents.Count == 0)
                    continue;

                float share = amount * multiplier / opponents.Count;
                foreach (Kingdom opponent in opponents)
                    ApplyWarWillShock(clan, opponent, share, reasonType, reason);
            }
        }

        private void ApplyCasualtyWarWill(MapEventSide side, MapEventSide opposingSide)
        {
            if (side == null || opposingSide == null)
                return;

            Dictionary<Clan, int> casualtiesByClan = new Dictionary<Clan, int>();
            foreach (MapEventParty eventParty in side.Parties)
            {
                Clan clan = ResolveMapEventPartyClan(eventParty);
                if (!IsValidEvaluatedClan(clan))
                    continue;

                int currentHealthy = eventParty.Party?.NumberOfHealthyMembers ?? 0;
                int casualties = Math.Max(0, eventParty.HealthyManCountAtStart - currentHealthy);
                if (casualties <= 0)
                    continue;

                casualtiesByClan[clan] = (casualtiesByClan.TryGetValue(clan, out int existing) ? existing : 0) + casualties;
            }

            foreach (KeyValuePair<Clan, int> entry in casualtiesByClan)
            {
                List<Kingdom> opponents = GetOpposingWarKingdoms(entry.Key.Kingdom, opposingSide).ToList();
                if (opponents.Count == 0)
                    continue;

                float shock = Math.Max(
                    C.WarPeaceRevampCasualtyPenaltyCap,
                    entry.Value * C.WarPeaceRevampCasualtyPenaltyPerTroop);
                shock *= BellumCivileOptions.WarWillCasualtyShockMultiplier;
                float share = shock / opponents.Count;
                foreach (Kingdom opponent in opponents)
                    ApplyWarWillShock(entry.Key, opponent, share, WarWillReasonType.BattleCasualties, "battle casualties");
            }
        }

        private static Clan ResolveMapEventPartyClan(MapEventParty eventParty)
        {
            PartyBase party = eventParty?.Party;
            if (party == null)
                return null;

            if (party.IsMobile)
                return party.MobileParty?.ActualClan
                    ?? party.MobileParty?.LeaderHero?.Clan
                    ?? party.Owner?.Clan;
            if (party.IsSettlement)
                return party.Settlement?.OwnerClan ?? party.Owner?.Clan;
            return party.Owner?.Clan;
        }

        private static IEnumerable<Kingdom> GetOpposingWarKingdoms(Kingdom kingdom, MapEventSide opposingSide)
        {
            if (kingdom == null || opposingSide == null)
                yield break;

            foreach (Kingdom opponent in opposingSide.Parties
                .Select(party => party?.Party?.MapFaction as Kingdom)
                .Where(opponent => opponent != null && opponent != kingdom && kingdom.IsAtWarWith(opponent))
                .Distinct())
            {
                yield return opponent;
            }
        }

        private static bool IsWarLeader(Kingdom kingdom, Hero hero)
        {
            return hero != null && GetWarLeader(kingdom) == hero;
        }

        private static Hero GetWarLeader(Kingdom kingdom)
        {
            return kingdom?.RulingClan?.Leader ?? kingdom?.Leader;
        }

        private static bool IsPrimaryHeirOfWarLeader(Kingdom kingdom, Hero hero)
        {
            if (kingdom == null || hero == null)
                return false;

            Clan leaderClan = kingdom.RulingClan ?? kingdom.Leader?.Clan;
            if (leaderClan == null)
                return false;

            Hero heir = ResolvePrimaryHeir(kingdom, leaderClan);
            return heir != null && heir != GetWarLeader(kingdom) && heir == hero;
        }

        private static Hero ResolvePrimaryHeir(Kingdom kingdom, Clan leaderClan)
        {
            if (leaderClan == null)
                return null;

            if (kingdom?.RulingClan == leaderClan && !IsTemporaryWarKingdom(kingdom))
            {
                Hero dynasticCandidate = Campaign.Current
                    ?.GetCampaignBehavior<DynasticHeirBehavior>()
                    ?.GetDynasticSuccessionCandidate(kingdom);
                if (IsValidHeirCandidate(dynasticCandidate))
                    return dynasticCandidate;
            }

            return SuccessionLawHelper.GetOrderedSuccessionLine(leaderClan)
                .FirstOrDefault(IsValidHeirCandidate);
        }

        private static bool IsValidHeirCandidate(Hero hero)
        {
            return hero != null
                && hero.IsAlive
                && !hero.IsDisabled
                && !hero.IsDead;
        }

        private static Kingdom ResolveKingdom(string kingdomId)
        {
            if (string.IsNullOrWhiteSpace(kingdomId))
                return null;

            return Kingdom.All.FirstOrDefault(kingdom => kingdom != null && kingdom.StringId == kingdomId);
        }

        private static bool IsValidWarKingdom(Kingdom kingdom)
        {
            return kingdom != null
                && !kingdom.IsEliminated
                && !string.IsNullOrWhiteSpace(kingdom.StringId);
        }

        private static bool IsTemporaryWarKingdom(Kingdom kingdom)
        {
            return kingdom == null
                || kingdom.StringId.StartsWith("bc_feud_")
                || Campaign.Current?.GetCampaignBehavior<FactionManagerBehavior>()?.GetFactionByRebelKingdom(kingdom) != null
                || Campaign.Current?.GetCampaignBehavior<ClaimFeudWarBehavior>()?.IsTemporaryFeudKingdom(kingdom) == true;
        }

        private static string BuildConflictKey(Kingdom first, Kingdom second)
        {
            string firstId = first?.StringId ?? string.Empty;
            string secondId = second?.StringId ?? string.Empty;
            return string.CompareOrdinal(firstId, secondId) <= 0
                ? firstId + "|" + secondId
                : secondId + "|" + firstId;
        }

        private static string BuildWarMomentumKey(Kingdom first, Kingdom second)
        {
            StanceLink stance = first?.GetStanceWith(second);
            long startMarker = stance == null ? 0L : (long)Math.Floor(stance.WarStartDate.ToDays * 1000d);
            return BuildConflictKey(first, second) + "|" + startMarker;
        }

        private static float ClampWarWill(float value)
        {
            if (value < 0f)
                return 0f;
            if (value > 100f)
                return 100f;
            return value;
        }

        private static float CurrentDay => (float)CampaignTime.Now.ToDays;

        private static bool IsValidEvaluatedClan(Clan clan)
        {
            return clan != null
                && !clan.IsEliminated
                && !clan.IsBanditFaction
                && !clan.IsUnderMercenaryService
                && clan.Leader != null
                && !clan.Leader.IsDead
                && clan.Kingdom != null
                && clan.CurrentTotalStrength > 0f;
        }

        private void EnsureCollectionsInitialized()
        {
            _warWillByClanId = _warWillByClanId ?? new Dictionary<string, float>();
            _nextEvaluationDayByClanId = _nextEvaluationDayByClanId ?? new Dictionary<string, int>();
            _preferredTargetByClanId = _preferredTargetByClanId ?? new Dictionary<string, string>();
            _initializedWarMomentumByConflictId = _initializedWarMomentumByConflictId ?? new Dictionary<string, bool>();
            _pendingVanillaSecessions = _pendingVanillaSecessions ?? new Dictionary<string, string>();
            _pressureRecords = _pressureRecords ?? new List<WarWillPressureRecord>();
        }
    }
}
