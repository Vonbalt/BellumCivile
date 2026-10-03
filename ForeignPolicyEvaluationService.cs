using System;
using System.Collections.Generic;
using System.Linq;
using BellumCivile.Behaviors;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Localization;
using C = BellumCivile.BellumCivileConstants;

namespace BellumCivile
{
    public sealed class ForeignPolicyEvaluationService
    {
        private sealed class CachedEvaluation
        {
            public int CampaignDay;
            public int TitleRevision;
            public ForeignPolicyEvaluation Evaluation;
        }

        private sealed class CachedClaimStakes
        {
            public int CampaignDay;
            public int TitleRevision;
            public IReadOnlyList<ForeignPolicyClaimStake> Stakes;
        }

        private static readonly Dictionary<string, CachedEvaluation> EvaluationCache = new Dictionary<string, CachedEvaluation>();
        private static readonly Dictionary<string, CachedClaimStakes> ClaimStakeCache = new Dictionary<string, CachedClaimStakes>();

        public static void InvalidateCache()
        {
            EvaluationCache.Clear();
            ClaimStakeCache.Clear();
        }

        internal static IReadOnlyList<ForeignPolicyClaimStake> GetClaimStakes(
            Kingdom sourceKingdom,
            Kingdom targetKingdom)
        {
            if (sourceKingdom == null || targetKingdom == null)
                return new List<ForeignPolicyClaimStake>();

            int currentDay = Campaign.Current == null ? 0 : (int)CampaignTime.Now.ToDays;
            int titleRevision = FeudalTitleBehavior.Instance?.RuntimeRevision ?? 0;
            string key = sourceKingdom.StringId + "|" + targetKingdom.StringId;
            if (!ClaimStakeCache.TryGetValue(key, out CachedClaimStakes cached)
                || cached.CampaignDay != currentDay
                || cached.TitleRevision != titleRevision
                || cached.Stakes == null)
            {
                cached = new CachedClaimStakes
                {
                    CampaignDay = currentDay,
                    TitleRevision = titleRevision,
                    Stakes = BuildClaimStakes(sourceKingdom, targetKingdom)
                };
                ClaimStakeCache[key] = cached;
            }

            return cached.Stakes;
        }

        public IReadOnlyList<ForeignPolicyEvaluation> EvaluateWarTargets(Kingdom sourceKingdom, FactionObject viewpoint = null)
        {
            if (!IsValidKingdom(sourceKingdom))
                return new List<ForeignPolicyEvaluation>();

            return Kingdom.All
                .Where(target => IsValidKingdom(target)
                    && target != sourceKingdom
                    && !sourceKingdom.IsAtWarWith(target))
                .Select(target => EvaluateWarTarget(sourceKingdom, target, viewpoint))
                .Where(evaluation => evaluation.IsCandidate)
                .OrderByDescending(evaluation => evaluation.ProvisionalUrgency)
                .ThenByDescending(evaluation => evaluation.BaseDiplomacyScore)
                .ToList();
        }

        public IReadOnlyList<ForeignPolicyEvaluation> EvaluatePeaceTargets(Kingdom sourceKingdom, FactionObject viewpoint = null)
        {
            if (!IsValidKingdom(sourceKingdom))
                return new List<ForeignPolicyEvaluation>();

            return Kingdom.All
                .Where(target => IsValidKingdom(target)
                    && target != sourceKingdom
                    && sourceKingdom.IsAtWarWith(target))
                .Select(target => EvaluatePeaceTarget(sourceKingdom, target, viewpoint))
                .OrderByDescending(evaluation => evaluation.ProvisionalUrgency)
                .ToList();
        }

        public ForeignPolicyEvaluation EvaluateWarTarget(Kingdom sourceKingdom, Kingdom targetKingdom, FactionObject viewpoint = null)
        {
            ForeignPolicyEvaluation evaluation = GetCachedBaseEvaluation(
                ForeignPolicyActionType.War,
                sourceKingdom,
                targetKingdom,
                () => BuildWarTargetEvaluation(sourceKingdom, targetKingdom));
            ApplyFactionViewpoint(evaluation, viewpoint);
            return evaluation;
        }

        private ForeignPolicyEvaluation BuildWarTargetEvaluation(Kingdom sourceKingdom, Kingdom targetKingdom)
        {
            ForeignPolicyEvaluation evaluation = CreateEvaluation(ForeignPolicyActionType.War, sourceKingdom, targetKingdom);
            if (!IsValidKingdom(sourceKingdom) || !IsValidKingdom(targetKingdom) || sourceKingdom == targetKingdom)
                return evaluation;

            evaluation.IsNeighboringRealm = AreNeighboringKingdoms(sourceKingdom, targetKingdom);
            evaluation.ClaimStakes.AddRange(GetClaimStakes(sourceKingdom, targetKingdom));
            evaluation.ClaimPressure = Math.Min(C.ForeignPolicyRealmClaimPressureCap, evaluation.ClaimStakes.Sum(stake => stake.Pressure));
            evaluation.IsCandidate = evaluation.IsNeighboringRealm || evaluation.ClaimStakes.Count > 0;
            if (!evaluation.IsCandidate)
                return evaluation;

            ReadWarDiplomacyScore(evaluation);
            evaluation.DominantMotive = SelectWarMotive(evaluation);
            evaluation.BaseUrgency = CalculateUrgency(
                evaluation.EffectiveDiplomacyScore,
                evaluation.DecisionThreshold,
                0f);

            if (evaluation.IsNeighboringRealm)
                evaluation.Reasons.Add("bordering realm");
            if (evaluation.ClaimStakes.Count > 0)
                evaluation.Reasons.Add($"{evaluation.ClaimStakes.Count} deduplicated title claim stake(s)");
            if (evaluation.PassesEffectiveThreshold)
                evaluation.Reasons.Add("passes active diplomacy model threshold");

            return evaluation;
        }

        public ForeignPolicyEvaluation EvaluatePeaceTarget(Kingdom sourceKingdom, Kingdom targetKingdom, FactionObject viewpoint = null)
        {
            ForeignPolicyEvaluation evaluation = GetCachedBaseEvaluation(
                ForeignPolicyActionType.Peace,
                sourceKingdom,
                targetKingdom,
                () => BuildPeaceTargetEvaluation(sourceKingdom, targetKingdom));
            ApplyFactionViewpoint(evaluation, viewpoint);
            return evaluation;
        }

        private ForeignPolicyEvaluation BuildPeaceTargetEvaluation(Kingdom sourceKingdom, Kingdom targetKingdom)
        {
            ForeignPolicyEvaluation evaluation = CreateEvaluation(ForeignPolicyActionType.Peace, sourceKingdom, targetKingdom);
            if (!IsValidKingdom(sourceKingdom)
                || !IsValidKingdom(targetKingdom)
                || !sourceKingdom.IsAtWarWith(targetKingdom))
            {
                return evaluation;
            }

            evaluation.IsCandidate = true;
            evaluation.IsNeighboringRealm = AreNeighboringKingdoms(sourceKingdom, targetKingdom);
            evaluation.ClaimStakes.AddRange(GetClaimStakes(sourceKingdom, targetKingdom));
            evaluation.ClaimPressure = Math.Min(C.ForeignPolicyRealmClaimPressureCap, evaluation.ClaimStakes.Sum(stake => stake.Pressure));
            var diplomacyModel = Campaign.Current?.Models?.DiplomacyModel;
            if (diplomacyModel == null)
                return evaluation;

            try
            {
                evaluation.BaseDiplomacyScore = diplomacyModel.GetScoreOfDeclaringPeace(sourceKingdom, targetKingdom);
                evaluation.DecisionThreshold = diplomacyModel.GetDecisionMakingThreshold(sourceKingdom);
                evaluation.PassesEffectiveThreshold = evaluation.BaseDiplomacyScore > evaluation.DecisionThreshold;
            }
            catch (Exception ex)
            {
                evaluation.Reasons.Add("peace score unavailable: " + ex.GetType().Name);
                return evaluation;
            }

            ForeignPolicyBehavior behavior = Campaign.Current?.GetCampaignBehavior<ForeignPolicyBehavior>();
            ActiveForeignWarRecord activeWar = behavior?.GetActiveWar(sourceKingdom, targetKingdom);
            evaluation.ObjectiveStatus = behavior?.GetObjectiveStatus(activeWar) ?? new ForeignPolicyObjectiveStatus();
            evaluation.TributeAssessment = ForeignPolicyTributeHelper.AssessCurrentTerms(sourceKingdom, targetKingdom);
            evaluation.DominantMotive = SelectPeaceMotive(evaluation);
            evaluation.BaseUrgency = CalculateUrgency(
                evaluation.BaseDiplomacyScore,
                evaluation.DecisionThreshold,
                0f);
            evaluation.Reasons.Add("active war");
            if (activeWar != null)
                evaluation.Reasons.Add($"tracked motive={activeWar.DeclaredMotive}, objectives={activeWar.TargetTitleIds.Count}");
            if (evaluation.ObjectiveStatus.HasObjectives)
                evaluation.Reasons.Add($"objectives={evaluation.ObjectiveStatus.ControlledObjectives}/{evaluation.ObjectiveStatus.TotalObjectives}");
            evaluation.Reasons.Add($"tribute={evaluation.TributeAssessment.Classification}, burden={evaluation.TributeAssessment.BurdenRatio:P0}");
            if (evaluation.PassesEffectiveThreshold)
                evaluation.Reasons.Add("passes active diplomacy model threshold");
            return evaluation;
        }

        public string BuildDebugReport(Kingdom sourceKingdom, FactionObject viewpoint = null)
        {
            if (!IsValidKingdom(sourceKingdom))
                return "Error: foreign-policy source kingdom is invalid.";

            List<string> lines = new List<string>
            {
                $"Foreign policy diagnostic for {sourceKingdom.Name} ({sourceKingdom.StringId}) viewpoint={viewpoint?.Type.ToString() ?? "Realm"}",
                "Vanilla random AI war/peace proposals suppressed=False",
                "War targets:"
            };

            List<ForeignPolicyEvaluation> warTargets = EvaluateWarTargets(sourceKingdom, viewpoint).ToList();
            if (warTargets.Count == 0)
            {
                lines.Add("- no bordering or legally connected war candidates");
            }
            else
            {
                lines.AddRange(warTargets.Take(10).Select(FormatEvaluation));
            }

            lines.Add("Peace targets:");
            List<ForeignPolicyEvaluation> peaceTargets = EvaluatePeaceTargets(sourceKingdom, viewpoint).ToList();
            if (peaceTargets.Count == 0)
                lines.Add("- no active foreign wars");
            else
                lines.AddRange(peaceTargets.Select(FormatEvaluation));

            lines.Add("Note: these rankings feed court agendas and the ruler's weekly proposal scan; individual council votes use the Bellum foreign-policy vote evaluator.");
            return string.Join("\n", lines);
        }

        private static string FormatEvaluation(ForeignPolicyEvaluation evaluation)
        {
            string claims = evaluation.ClaimStakes.Count == 0
                ? "none"
                : string.Join(",", evaluation.ClaimStakes.Select(stake =>
                    $"{stake.ClaimantClan?.StringId ?? "unknown"}:{stake.Title?.TitleId ?? "unknown"}:{stake.Strength}"));
            string reasons = evaluation.Reasons.Count == 0 ? "none" : string.Join(", ", evaluation.Reasons);
            string peaceDetails = evaluation.ActionType == ForeignPolicyActionType.Peace
                ? $"; objectives={evaluation.ObjectiveStatus?.ControlledObjectives ?? 0}/{evaluation.ObjectiveStatus?.TotalObjectives ?? 0}; tribute={evaluation.TributeAssessment?.Classification.ToString() ?? "Unknown"}; tribute_daily={evaluation.TributeAssessment?.DailyTribute ?? 0}; tribute_days={evaluation.TributeAssessment?.DurationDays ?? 0}; burden={(evaluation.TributeAssessment?.BurdenRatio ?? 0f):P0}"
                : string.Empty;
            return $"- {evaluation.TargetKingdom?.Name}: urgency={evaluation.ProvisionalUrgency:0.0}; realm_urgency={evaluation.BaseUrgency:0.0}; faction_adjustment={evaluation.FactionAdjustment:+0.0;-0.0;0.0}; motive={evaluation.DominantMotive}; raw={evaluation.BaseDiplomacyScore:0}; claim_bonus={evaluation.ClaimScoreBonus:0}; effective={evaluation.EffectiveDiplomacyScore:0}; threshold={evaluation.DecisionThreshold:0}; effective_pass={evaluation.PassesEffectiveThreshold}; neighbor={evaluation.IsNeighboringRealm}; claim_pressure={evaluation.ClaimPressure:0.0}; personal_claim_pressure={evaluation.PersonalClaimPressure:0.0}; claims={claims}{peaceDetails}; reasons={reasons}";
        }

        private static ForeignPolicyEvaluation CreateEvaluation(
            ForeignPolicyActionType actionType,
            Kingdom sourceKingdom,
            Kingdom targetKingdom)
        {
            return new ForeignPolicyEvaluation
            {
                ActionType = actionType,
                SourceKingdom = sourceKingdom,
                TargetKingdom = targetKingdom,
                DominantMotive = ForeignPolicyMotive.Unknown
            };
        }

        private static ForeignPolicyEvaluation GetCachedBaseEvaluation(
            ForeignPolicyActionType actionType,
            Kingdom sourceKingdom,
            Kingdom targetKingdom,
            Func<ForeignPolicyEvaluation> factory)
        {
            int currentDay = Campaign.Current == null ? 0 : (int)CampaignTime.Now.ToDays;
            int titleRevision = FeudalTitleBehavior.Instance?.RuntimeRevision ?? 0;
            string key = $"{actionType}|{sourceKingdom?.StringId ?? "null"}|{targetKingdom?.StringId ?? "null"}";
            if (!EvaluationCache.TryGetValue(key, out CachedEvaluation cached)
                || cached.CampaignDay != currentDay
                || cached.TitleRevision != titleRevision
                || cached.Evaluation == null)
            {
                cached = new CachedEvaluation
                {
                    CampaignDay = currentDay,
                    TitleRevision = titleRevision,
                    Evaluation = factory()
                };
                EvaluationCache[key] = cached;
            }

            return CloneEvaluation(cached.Evaluation);
        }

        private static ForeignPolicyEvaluation CloneEvaluation(ForeignPolicyEvaluation source)
        {
            ForeignPolicyEvaluation clone = new ForeignPolicyEvaluation
            {
                ActionType = source.ActionType,
                SourceKingdom = source.SourceKingdom,
                TargetKingdom = source.TargetKingdom,
                DominantMotive = source.DominantMotive,
                IsNeighboringRealm = source.IsNeighboringRealm,
                IsCandidate = source.IsCandidate,
                PassesEffectiveThreshold = source.PassesEffectiveThreshold,
                BaseDiplomacyScore = source.BaseDiplomacyScore,
                ClaimScoreBonus = source.ClaimScoreBonus,
                PersonalClaimPressure = source.PersonalClaimPressure,
                DecisionThreshold = source.DecisionThreshold,
                ClaimPressure = source.ClaimPressure,
                BaseUrgency = source.BaseUrgency,
                FactionAdjustment = 0f,
                CourtFactionType = null,
                ObjectiveStatus = CloneObjectiveStatus(source.ObjectiveStatus),
                TributeAssessment = CloneTributeAssessment(source.TributeAssessment)
            };
            clone.ClaimStakes.AddRange(source.ClaimStakes.Select(stake => new ForeignPolicyClaimStake
            {
                ClaimantClan = stake.ClaimantClan,
                Title = stake.Title,
                Strength = stake.Strength,
                IsImpliedDeJureClaim = stake.IsImpliedDeJureClaim,
                Pressure = stake.Pressure
            }));
            clone.Reasons.AddRange(source.Reasons);
            return clone;
        }

        private static ForeignPolicyObjectiveStatus CloneObjectiveStatus(ForeignPolicyObjectiveStatus source)
        {
            if (source == null)
                return null;

            ForeignPolicyObjectiveStatus clone = new ForeignPolicyObjectiveStatus
            {
                TotalObjectives = source.TotalObjectives,
                ControlledObjectives = source.ControlledObjectives
            };
            clone.ControlledTitleIds.AddRange(source.ControlledTitleIds);
            clone.OutstandingTitleIds.AddRange(source.OutstandingTitleIds);
            return clone;
        }

        private static ForeignPolicyTributeAssessment CloneTributeAssessment(ForeignPolicyTributeAssessment source)
        {
            if (source == null)
                return null;

            return new ForeignPolicyTributeAssessment
            {
                Classification = source.Classification,
                DailyTribute = source.DailyTribute,
                DurationDays = source.DurationDays,
                TotalCost = source.TotalCost,
                RealmLiquidWealth = source.RealmLiquidWealth,
                BurdenRatio = source.BurdenRatio
            };
        }

        private static void ApplyFactionViewpoint(ForeignPolicyEvaluation evaluation, FactionObject viewpoint)
        {
            if (evaluation == null
                || viewpoint == null
                || !viewpoint.IsIdeology
                || viewpoint.ParentKingdom != evaluation.SourceKingdom)
            {
                return;
            }

            float adjustment = evaluation.ActionType == ForeignPolicyActionType.War
                ? CalculateFactionWarAdjustment(evaluation, viewpoint)
                : CalculateFactionPeaceAdjustment(evaluation, viewpoint);
            evaluation.CourtFactionType = viewpoint.Type;
            evaluation.FactionAdjustment = adjustment;
            evaluation.Reasons.Add($"{viewpoint.Type} interpretation {adjustment:+0.0;-0.0;0.0}");
            evaluation.Reasons.Add(BuildFactionInputSummary(evaluation, viewpoint));
        }

        private static string BuildFactionInputSummary(ForeignPolicyEvaluation evaluation, FactionObject viewpoint)
        {
            Kingdom source = evaluation.SourceKingdom;
            Kingdom target = evaluation.TargetKingdom;
            Hero leader = viewpoint.Leader?.Leader;
            float memberClaims = evaluation.ClaimStakes
                .Where(stake => viewpoint.Members.Contains(stake.ClaimantClan))
                .Sum(stake => stake.Pressure);
            float crownClaims = evaluation.ClaimStakes
                .Where(stake => stake.ClaimantClan == source.RulingClan)
                .Sum(stake => stake.Pressure);
            float strengthRatio = Math.Max(1f, source.CurrentTotalStrength) / Math.Max(1f, target.CurrentTotalStrength);
            return $"inputs strength_ratio={strengthRatio:0.00}, source_wars={CountActiveWars(source)}, target_wars={CountActiveWars(target)}, member_claims={memberClaims:0.0}, crown_claims={crownClaims:0.0}, leader_traits=V{GetTraitLevel(leader, DefaultTraits.Valor)}/M{GetTraitLevel(leader, DefaultTraits.Mercy)}/H{GetTraitLevel(leader, DefaultTraits.Honor)}/C{GetTraitLevel(leader, DefaultTraits.Calculating)}";
        }

        private static float CalculateFactionWarAdjustment(ForeignPolicyEvaluation evaluation, FactionObject viewpoint)
        {
            Kingdom source = evaluation.SourceKingdom;
            Kingdom target = evaluation.TargetKingdom;
            float sourceStrength = Math.Max(1f, source.CurrentTotalStrength);
            float targetStrength = Math.Max(1f, target.CurrentTotalStrength);
            float strengthRatio = sourceStrength / targetStrength;
            float memberClaimPressure = evaluation.ClaimStakes
                .Where(stake => viewpoint.Members.Contains(stake.ClaimantClan))
                .Sum(stake => stake.Pressure);
            float crownClaimPressure = evaluation.ClaimStakes
                .Where(stake => stake.ClaimantClan == source.RulingClan)
                .Sum(stake => stake.Pressure);
            Hero factionLeader = viewpoint.Leader?.Leader;
            int sourceWars = CountActiveWars(source);
            int targetWars = CountActiveWars(target);
            float adjustment = 0f;

            switch (viewpoint.Type)
            {
                case FactionType.Glory:
                    if (strengthRatio >= 1.5f) adjustment += 15f;
                    else if (strengthRatio >= 1.15f) adjustment += 8f;
                    else if (strengthRatio < 0.75f) adjustment -= 15f;
                    if (sourceWars == 0 && targetWars > 0) adjustment += 10f;
                    adjustment += evaluation.ClaimPressure * 0.25f;
                    adjustment += GetTraitLevel(factionLeader, DefaultTraits.Valor) * 3f;
                    adjustment -= GetTraitLevel(factionLeader, DefaultTraits.Mercy) * 2f;
                    break;

                case FactionType.Nobility:
                    adjustment += Math.Min(15f, memberClaimPressure);
                    adjustment += Math.Max(0f, evaluation.ClaimPressure - memberClaimPressure) * 0.25f;
                    if (evaluation.ClaimPressure <= 0f) adjustment -= 8f;
                    adjustment += GetTraitLevel(factionLeader, DefaultTraits.Calculating) * 2f;
                    if (evaluation.ClaimPressure > 0f)
                        adjustment += GetTraitLevel(factionLeader, DefaultTraits.Honor) * 2f;
                    break;

                case FactionType.Liberty:
                    adjustment -= 10f;
                    adjustment += memberClaimPressure * 0.2f;
                    adjustment -= GetTraitLevel(factionLeader, DefaultTraits.Mercy) * 3f;
                    adjustment += GetTraitLevel(factionLeader, DefaultTraits.Valor);
                    break;

                case FactionType.Royalists:
                    adjustment += crownClaimPressure;
                    adjustment += Math.Max(0f, evaluation.ClaimPressure - crownClaimPressure) * 0.15f;
                    adjustment -= CalculateRoyalistVassalSafeguard(evaluation, source);
                    adjustment += GetTraitLevel(factionLeader, DefaultTraits.Honor) * 2f;
                    break;
            }

            return Clamp(adjustment, -35f, 35f);
        }

        private static float CalculateFactionPeaceAdjustment(ForeignPolicyEvaluation evaluation, FactionObject viewpoint)
        {
            Kingdom source = evaluation.SourceKingdom;
            Kingdom target = evaluation.TargetKingdom;
            ForeignPolicyBehavior behavior = Campaign.Current?.GetCampaignBehavior<ForeignPolicyBehavior>();
            ActiveForeignWarRecord war = behavior?.GetActiveWar(source, target);
            float strengthRatio = Math.Max(1f, source.CurrentTotalStrength) / Math.Max(1f, target.CurrentTotalStrength);
            float memberClaimPressure = evaluation.ClaimStakes
                .Where(stake => viewpoint.Members.Contains(stake.ClaimantClan))
                .Sum(stake => stake.Pressure);
            float crownClaimPressure = evaluation.ClaimStakes
                .Where(stake => stake.ClaimantClan == source.RulingClan)
                .Sum(stake => stake.Pressure);
            float warDuration = war != null && war.StartedDay >= 0f
                ? Math.Max(0f, (float)CampaignTime.Now.ToDays - war.StartedDay)
                : 0f;
            Hero factionLeader = viewpoint.Leader?.Leader;
            float adjustment = 0f;

            switch (viewpoint.Type)
            {
                case FactionType.Glory:
                    if (strengthRatio >= 1.25f) adjustment -= 12f;
                    else if (strengthRatio < 0.75f) adjustment += 15f;
                    if (evaluation.DominantMotive == ForeignPolicyMotive.ObjectivesAchieved) adjustment += 8f;
                    if (evaluation.DominantMotive == ForeignPolicyMotive.StrategicRealignment) adjustment += 8f;
                    adjustment -= GetTraitLevel(factionLeader, DefaultTraits.Valor) * 3f;
                    break;

                case FactionType.Nobility:
                    if (evaluation.DominantMotive == ForeignPolicyMotive.ObjectivesAchieved)
                        adjustment += 18f;
                    else
                        adjustment -= Math.Min(18f, memberClaimPressure);
                    adjustment += GetTraitLevel(factionLeader, DefaultTraits.Calculating) * 2f;
                    break;

                case FactionType.Liberty:
                    adjustment += 10f;
                    adjustment += Math.Min(15f, warDuration / 20f);
                    adjustment += GetTraitLevel(factionLeader, DefaultTraits.Mercy) * 3f;
                    break;

                case FactionType.Royalists:
                    if (evaluation.DominantMotive == ForeignPolicyMotive.ObjectivesAchieved) adjustment += 10f;
                    if (evaluation.DominantMotive == ForeignPolicyMotive.MilitaryCollapse) adjustment += 15f;
                    if (strengthRatio >= 1.25f) adjustment -= 8f;
                    if (evaluation.DominantMotive != ForeignPolicyMotive.ObjectivesAchieved)
                        adjustment -= Math.Min(15f, crownClaimPressure);
                    break;
            }

            adjustment += GetTributePeaceAdjustment(
                viewpoint.Type,
                evaluation.TributeAssessment?.Classification ?? ForeignPolicyTributeClass.None);

            return Clamp(adjustment, -35f, 35f);
        }

        private static float GetTributePeaceAdjustment(
            FactionType factionType,
            ForeignPolicyTributeClass tributeClass)
        {
            if (tributeClass == ForeignPolicyTributeClass.Favorable)
                return 5f;
            if (tributeClass == ForeignPolicyTributeClass.None
                || tributeClass == ForeignPolicyTributeClass.Affordable)
            {
                return 0f;
            }

            switch (tributeClass)
            {
                case ForeignPolicyTributeClass.Costly:
                    return factionType == FactionType.Liberty ? -3f : -6f;
                case ForeignPolicyTributeClass.Humiliating:
                    return factionType == FactionType.Liberty ? -8f
                        : factionType == FactionType.Nobility ? -10f
                        : -15f;
                case ForeignPolicyTributeClass.Ruinous:
                    return factionType == FactionType.Liberty ? -15f
                        : factionType == FactionType.Nobility ? -20f
                        : -25f;
                default:
                    return 0f;
            }
        }

        private static float CalculateRoyalistVassalSafeguard(ForeignPolicyEvaluation evaluation, Kingdom source)
        {
            Clan crownClan = source?.RulingClan;
            if (crownClan == null)
                return 0f;

            FactionManagerBehavior factionManager = Campaign.Current?.GetCampaignBehavior<FactionManagerBehavior>();
            float crownStrength = Math.Max(1f, RebellionPowerHelper.CalculateClanPower(crownClan));
            float penalty = 0f;
            foreach (IGrouping<Clan, ForeignPolicyClaimStake> group in evaluation.ClaimStakes
                .Where(stake => stake.ClaimantClan != null && stake.ClaimantClan != crownClan)
                .GroupBy(stake => stake.ClaimantClan))
            {
                Clan claimant = group.Key;
                bool aristocraticRival = factionManager?.GetIdeologicalFaction(claimant)?.Type == FactionType.Nobility;
                float relativePower = RebellionPowerHelper.CalculateClanPower(claimant) / crownStrength;
                if (aristocraticRival && relativePower >= 0.75f)
                    penalty += group.Sum(stake => stake.Pressure) * 0.75f;
            }

            return Math.Min(15f, penalty);
        }

        private static int GetTraitLevel(Hero hero, TraitObject trait)
        {
            return hero?.GetTraitLevel(trait) ?? 0;
        }

        private static int CountActiveWars(Kingdom kingdom)
        {
            return Kingdom.All.Count(other => other != null
                && other != kingdom
                && !other.IsEliminated
                && kingdom.IsAtWarWith(other));
        }

        private static float Clamp(float value, float minimum, float maximum)
        {
            return Math.Max(minimum, Math.Min(maximum, value));
        }

        private static void ReadWarDiplomacyScore(ForeignPolicyEvaluation evaluation)
        {
            var diplomacyModel = Campaign.Current?.Models?.DiplomacyModel;
            Clan evaluatingClan = evaluation.SourceKingdom?.RulingClan;
            if (diplomacyModel == null || evaluatingClan == null)
                return;

            try
            {
                TextObject reason;
                float effectiveScore = diplomacyModel.GetScoreOfDeclaringWar(
                    evaluation.SourceKingdom,
                    evaluation.TargetKingdom,
                    evaluatingClan,
                    out reason,
                    includeReason: false);
                evaluation.DecisionThreshold = diplomacyModel.GetDecisionMakingThreshold(evaluation.SourceKingdom);
                if (diplomacyModel is CivilWarDiplomacyModel)
                {
                    ForeignPolicyClaimScoreBreakdown claimScore = ForeignPolicyClaimScoringHelper.Calculate(
                        evaluation.ClaimStakes,
                        evaluation.SourceKingdom,
                        evaluatingClan,
                        effectiveScore,
                        evaluation.DecisionThreshold);
                    evaluation.ClaimScoreBonus = claimScore.ScoreBonus;
                    evaluation.PersonalClaimPressure = claimScore.PersonalPressure;
                }

                evaluation.BaseDiplomacyScore = effectiveScore - evaluation.ClaimScoreBonus;
                evaluation.PassesEffectiveThreshold = effectiveScore > evaluation.DecisionThreshold;
            }
            catch (Exception ex)
            {
                evaluation.Reasons.Add("war score unavailable: " + ex.GetType().Name);
            }
        }

        private static List<ForeignPolicyClaimStake> BuildClaimStakes(Kingdom sourceKingdom, Kingdom targetKingdom)
        {
            FeudalTitleBehavior titleBehavior = FeudalTitleBehavior.Instance
                ?? Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>();
            if (titleBehavior == null)
                return new List<ForeignPolicyClaimStake>();

            Dictionary<string, Clan> clansById = Clan.All
                .Where(clan => clan != null && !string.IsNullOrWhiteSpace(clan.StringId))
                .GroupBy(clan => clan.StringId)
                .ToDictionary(group => group.Key, group => group.First());
            Dictionary<string, ForeignPolicyClaimStake> uniqueStakes = new Dictionary<string, ForeignPolicyClaimStake>();

            foreach (Clan claimantClan in clansById.Values.Where(clan => clan.Kingdom == sourceKingdom))
            {
                foreach (FeudalClaimRecord claim in titleBehavior.GetActiveClaimsByClan(claimantClan))
                {
                    FeudalTitleRecord title = titleBehavior.GetTitle(claim.TargetTitleId);
                    if (!IsTitleControlledByKingdom(title, targetKingdom, clansById))
                        continue;

                    AddOrImproveStake(uniqueStakes, claimantClan, title, claim.Strength, isImplied: false);
                }
            }

            foreach (FeudalTitleRecord title in titleBehavior.GetAllTitles())
            {
                if (title == null || !title.IsActive
                    || string.IsNullOrWhiteSpace(title.DeJureHolderClanId)
                    || title.DeJureHolderClanId == title.DeFactoHolderClanId
                    || !clansById.TryGetValue(title.DeJureHolderClanId, out Clan claimantClan)
                    || claimantClan.Kingdom != sourceKingdom
                    || !IsTitleControlledByKingdom(title, targetKingdom, clansById))
                {
                    continue;
                }

                AddOrImproveStake(uniqueStakes, claimantClan, title, FeudalClaimStrength.Strong, isImplied: true);
            }

            List<ForeignPolicyClaimStake> result = new List<ForeignPolicyClaimStake>();
            foreach (IGrouping<Clan, ForeignPolicyClaimStake> claimantGroup in uniqueStakes.Values.GroupBy(stake => stake.ClaimantClan))
            {
                foreach (ForeignPolicyClaimStake stake in claimantGroup
                    .OrderByDescending(candidate => candidate.Title.TitleType)
                    .ThenByDescending(candidate => candidate.Strength))
                {
                    if (result.Any(selected => selected.ClaimantClan == stake.ClaimantClan
                        && IsAncestorTitle(titleBehavior, selected.Title, stake.Title)))
                    {
                        continue;
                    }

                    result.Add(stake);
                }
            }

            return result;
        }

        private static void AddOrImproveStake(
            Dictionary<string, ForeignPolicyClaimStake> stakes,
            Clan claimantClan,
            FeudalTitleRecord title,
            FeudalClaimStrength strength,
            bool isImplied)
        {
            string key = claimantClan.StringId + "|" + title.TitleId;
            if (stakes.TryGetValue(key, out ForeignPolicyClaimStake existing)
                && existing.Strength >= strength)
            {
                if (isImplied)
                    existing.IsImpliedDeJureClaim = true;
                return;
            }

            stakes[key] = new ForeignPolicyClaimStake
            {
                ClaimantClan = claimantClan,
                Title = title,
                Strength = strength,
                IsImpliedDeJureClaim = isImplied,
                Pressure = GetClaimPressure(title.TitleType, strength)
            };
        }

        private static bool IsTitleControlledByKingdom(
            FeudalTitleRecord title,
            Kingdom targetKingdom,
            Dictionary<string, Clan> clansById)
        {
            return title != null
                && title.IsActive
                && !string.IsNullOrWhiteSpace(title.DeFactoHolderClanId)
                && clansById.TryGetValue(title.DeFactoHolderClanId, out Clan holderClan)
                && holderClan.Kingdom == targetKingdom;
        }

        private static bool IsAncestorTitle(
            FeudalTitleBehavior titleBehavior,
            FeudalTitleRecord possibleAncestor,
            FeudalTitleRecord possibleDescendant)
        {
            if (possibleAncestor == null || possibleDescendant == null)
                return false;

            FeudalTitleRecord current = possibleDescendant;
            HashSet<string> visited = new HashSet<string>();
            while (current != null
                && !string.IsNullOrWhiteSpace(current.ParentTitleId)
                && visited.Add(current.TitleId))
            {
                if (current.ParentTitleId == possibleAncestor.TitleId)
                    return true;
                current = titleBehavior.GetTitle(current.ParentTitleId);
            }

            return false;
        }

        private static float GetClaimPressure(FeudalTitleType titleType, FeudalClaimStrength strength)
        {
            float strongPressure;
            switch (titleType)
            {
                case FeudalTitleType.Barony: strongPressure = 2f; break;
                case FeudalTitleType.County: strongPressure = 4f; break;
                case FeudalTitleType.Duchy: strongPressure = 8f; break;
                case FeudalTitleType.Kingdom: strongPressure = 14f; break;
                case FeudalTitleType.Empire: strongPressure = 20f; break;
                default: strongPressure = 0f; break;
            }

            return strength == FeudalClaimStrength.Strong ? strongPressure : strongPressure * 0.5f;
        }

        private static ForeignPolicyMotive SelectWarMotive(ForeignPolicyEvaluation evaluation)
        {
            if (evaluation.ClaimStakes.Count > 0)
                return ForeignPolicyMotive.Reclamation;

            int sourceWars = Kingdom.All.Count(other => other != null
                && other != evaluation.SourceKingdom
                && !other.IsEliminated
                && evaluation.SourceKingdom.IsAtWarWith(other));
            int targetWars = Kingdom.All.Count(other => other != null
                && other != evaluation.TargetKingdom
                && !other.IsEliminated
                && evaluation.TargetKingdom.IsAtWarWith(other));
            bool favorableStrength = evaluation.SourceKingdom.CurrentTotalStrength
                >= evaluation.TargetKingdom.CurrentTotalStrength * 0.8f;
            return sourceWars == 0 && targetWars > 0 && favorableStrength
                ? ForeignPolicyMotive.OpportunisticIntervention
                : ForeignPolicyMotive.GloriousExpansion;
        }

        private static ForeignPolicyMotive SelectPeaceMotive(ForeignPolicyEvaluation evaluation)
        {
            Kingdom sourceKingdom = evaluation.SourceKingdom;
            Kingdom targetKingdom = evaluation.TargetKingdom;
            if (evaluation.ObjectiveStatus?.AllObjectivesAchieved == true)
                return ForeignPolicyMotive.ObjectivesAchieved;

            if (sourceKingdom.CurrentTotalStrength < targetKingdom.CurrentTotalStrength * 0.65f)
                return ForeignPolicyMotive.MilitaryCollapse;

            int activeWars = Kingdom.All.Count(other => other != null
                && other != sourceKingdom
                && !other.IsEliminated
                && sourceKingdom.IsAtWarWith(other));
            return activeWars > 1
                ? ForeignPolicyMotive.StrategicRealignment
                : ForeignPolicyMotive.HonorableSettlement;
        }

        private static float CalculateUrgency(float score, float threshold, float additionalPressure)
        {
            if (score <= -9000000f)
                return 0f;

            float scale = Math.Max(1000f, Math.Abs(threshold));
            float relativeMargin = (score - threshold) / scale;
            float strategicUrgency = 50f + Math.Max(-50f, Math.Min(50f, relativeMargin * 25f));
            return Math.Max(0f, Math.Min(100f, strategicUrgency + additionalPressure));
        }

        private static bool AreNeighboringKingdoms(Kingdom firstKingdom, Kingdom secondKingdom)
        {
            List<Settlement> firstFiefs = firstKingdom.Settlements.Where(IsStrategicFief).ToList();
            List<Settlement> secondFiefs = secondKingdom.Settlements.Where(IsStrategicFief).ToList();
            if (firstFiefs.Count == 0 || secondFiefs.Count == 0)
                return false;

            float averageTownDistance = Campaign.Current.GetAverageDistanceBetweenClosestTwoTownsWithNavigationType(MobileParty.NavigationType.All);
            float neighborThreshold = averageTownDistance > 0f
                ? averageTownDistance
                : Campaign.MapDiagonal * 0.15f;

            foreach (Settlement firstFief in firstFiefs)
            {
                foreach (Settlement secondFief in secondFiefs)
                {
                    float distance = Campaign.Current.Models.MapDistanceModel.GetDistance(
                        firstFief,
                        secondFief,
                        isFromPort: false,
                        isTargetingPort: false,
                        MobileParty.NavigationType.All);
                    if (distance <= neighborThreshold)
                        return true;
                }
            }

            return false;
        }

        private static bool IsStrategicFief(Settlement settlement)
        {
            return settlement != null && (settlement.IsTown || settlement.IsCastle);
        }

        private static bool IsValidKingdom(Kingdom kingdom)
        {
            return kingdom != null
                && !kingdom.IsEliminated
                && kingdom.RulingClan != null
                && kingdom.RulingClan.Leader != null;
        }
    }
}
