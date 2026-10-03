using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BellumCivile.Behaviors;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Localization;
using C = BellumCivile.BellumCivileConstants;

namespace BellumCivile.WarPeace
{
    internal sealed class WarTargetScoringService
    {
        private readonly Dictionary<string, bool> _frontierCache = new Dictionary<string, bool>();
        private readonly Dictionary<string, FrontierAssessment> _detailedFrontierCache = new Dictionary<string, FrontierAssessment>();
        private int _frontierCacheDay = int.MinValue;
        private static bool? _navalDlcLoaded;

        internal CourtProtectionReach ProtectionReach(Kingdom source, Kingdom target)
        {
            if (source == null || target == null || source == target) return CourtProtectionReach.None;
            var from = BuildTargetContext(source);
            var to = BuildTargetContext(target);
            float average = Campaign.Current.GetAverageDistanceBetweenClosestTwoTownsWithNavigationType(MobileParty.NavigationType.All);
            float land = average > 0 ? average : Campaign.MapDiagonal * 0.15f;
            float sea = average > 0 ? average * C.WarPeaceRevampMaritimeNeighborDistanceMultiplier : Campaign.MapDiagonal * 0.30f;
            if (HasLandFrontier(from.Fiefs, to.Fiefs, from.FrontierKey, to.FrontierKey, land)) return CourtProtectionReach.Land;
            return IsNavalDlcLoaded() && HasMaritimeRoute(from.Ports, to.Ports, from.FrontierKey, to.FrontierKey, sea)
                ? CourtProtectionReach.Maritime : CourtProtectionReach.None;
        }

        public IReadOnlyList<WarTargetScore> RankTargets(Clan clan)
        {
            Kingdom sourceKingdom = clan?.Kingdom;
            if (!IsValidClan(clan)
                || !IsValidKingdom(sourceKingdom)
                || BellumKingdomVisibilityHelper.IsTemporaryFeudKingdom(sourceKingdom))
                return new List<WarTargetScore>();

            ScoringContext context = BuildScoringContext(clan, sourceKingdom);
            Kingdom suzerain = context.ClientKingdoms?.GetSuzerain(sourceKingdom);
            List<WarTargetScore> scores = new List<WarTargetScore>();
            foreach (Kingdom target in Kingdom.All)
            {
                if (!IsValidKingdom(target)
                    || target == sourceKingdom
                    || BellumKingdomVisibilityHelper.IsTemporaryFeudKingdom(target)
                    || (suzerain != null && target != suzerain))
                {
                    continue;
                }

                WarTargetScore score = ScoreTarget(context, BuildTargetContext(target));
                if (score != null && score.Score >= BellumCivileOptions.WarTargetMinimumScore)
                    scores.Add(score);
            }

            scores.Sort((first, second) =>
            {
                int scoreOrder = second.Score.CompareTo(first.Score);
                return scoreOrder != 0
                    ? scoreOrder
                    : string.Compare(
                        first.TargetKingdom?.Name?.ToString() ?? string.Empty,
                        second.TargetKingdom?.Name?.ToString() ?? string.Empty,
                        StringComparison.Ordinal);
            });
            return scores;
        }

        private WarTargetScore ScoreTarget(ScoringContext context, TargetContext target)
        {
            Clan clan = context.Clan;
            Kingdom sourceKingdom = context.SourceKingdom;
            Kingdom targetKingdom = target.Kingdom;
            bool activeWar = sourceKingdom.IsAtWarWith(targetKingdom);
            ClientKingdomBehavior clients = context.ClientKingdoms;
            bool liberationTarget = clients?.IsClientOf(sourceKingdom, targetKingdom) == true;
            FrontierAssessment localLandFrontier = AssessLocalLandFrontier(
                context.LocalFiefs,
                target.Fiefs,
                context.LocalFrontierKey,
                target.FrontierKey,
                context.LandThreshold);
            bool localLandNeighbor = localLandFrontier.HasRoute;
            bool realmLandFrontier = HasLandFrontier(context.RealmFiefs, target.Fiefs, context.RealmFrontierKey, target.FrontierKey, context.LandThreshold);
            FrontierAssessment localMaritimeFrontier = context.NavalDlcLoaded
                ? AssessLocalMaritimeFrontier(
                    context.LocalPorts,
                    target.Ports,
                    context.LocalFrontierKey,
                    target.FrontierKey,
                    context.MaritimeThreshold)
                : FrontierAssessment.None;
            bool localMaritimeNeighbor = localMaritimeFrontier.HasRoute;
            bool realmMaritimeRoute = context.NavalDlcLoaded
                && HasMaritimeRoute(context.RealmPorts, target.Ports, context.RealmFrontierKey, target.FrontierKey, context.MaritimeThreshold);
            List<string> claimReasons = new List<string>();
            List<WarTargetMotive> claimMotives = new List<WarTargetMotive>();
            float claimScore = CalculateClaimScore(context, targetKingdom, claimReasons, claimMotives);
            bool hasClaim = claimScore > 0f;

            if (!activeWar && !liberationTarget && !localLandNeighbor && !localMaritimeNeighbor && !realmLandFrontier && !realmMaritimeRoute && !hasClaim)
                return null;

            WarTargetScore result = new WarTargetScore
            {
                TargetKingdom = targetKingdom,
                IsActiveWar = activeWar,
                IsNeighboringRealm = localLandNeighbor || realmLandFrontier,
                IsMaritimeNeighbor = localMaritimeNeighbor || realmMaritimeRoute,
                IsLocalLandNeighbor = localLandNeighbor,
                IsLocalMaritimeNeighbor = localMaritimeNeighbor,
                IsClaimTarget = hasClaim,
                IsLiberationTarget = liberationTarget
            };

            if (liberationTarget)
            {
                ClientLibertyAssessment liberty = clients.BuildLibertyAssessment(sourceKingdom);
                float clanDesire = liberty?.Clans.FirstOrDefault(entry => entry.Clan == clan)?.LibertyDesire ?? 0f;
                float libertyScore = clanDesire * C.ClientLiberationTargetScoreScale;
                result.Score += libertyScore;
                result.Reasons.Add($"liberty desire +{libertyScore:0}");
                // Liberation is the purpose of this declaration even when a newly
                // established client has not accumulated a numerical desire yet.
                AddMotive(result, WarTargetMotiveType.Liberation, Math.Max(0.01f, libertyScore));
                if (liberty != null)
                    result.Reasons.Add($"liberation readiness {liberty.LiberationReadiness:0}%");
            }

            if (activeWar)
            {
                result.Score += C.WarPeaceRevampActiveWarTargetScore;
                result.Reasons.Add($"active war +{C.WarPeaceRevampActiveWarTargetScore:0}");
            }

            if (localLandNeighbor)
            {
                result.Score += BellumCivileOptions.WarTargetLocalLandBorderScore;
                result.Reasons.Add($"local land border +{BellumCivileOptions.WarTargetLocalLandBorderScore:0}");
                AddMotive(result, WarTargetMotiveType.LocalLandFrontier, BellumCivileOptions.WarTargetLocalLandBorderScore);
            }
            else if (realmLandFrontier)
            {
                result.Score += BellumCivileOptions.WarTargetRealmLandFrontierScore;
                result.Reasons.Add($"realm land frontier +{BellumCivileOptions.WarTargetRealmLandFrontierScore:0}");
                AddMotive(result, WarTargetMotiveType.RealmLandFrontier, BellumCivileOptions.WarTargetRealmLandFrontierScore);
            }

            if (localMaritimeNeighbor)
            {
                result.Score += BellumCivileOptions.WarTargetLocalMaritimeBorderScore;
                result.Reasons.Add($"local maritime border +{BellumCivileOptions.WarTargetLocalMaritimeBorderScore:0}");
                AddMotive(result, WarTargetMotiveType.LocalMaritimeFrontier, BellumCivileOptions.WarTargetLocalMaritimeBorderScore);
            }
            else if (realmMaritimeRoute)
            {
                result.Score += BellumCivileOptions.WarTargetRealmMaritimeRouteScore;
                result.Reasons.Add($"realm maritime route +{BellumCivileOptions.WarTargetRealmMaritimeRouteScore:0}");
                AddMotive(result, WarTargetMotiveType.RealmMaritimeReach, BellumCivileOptions.WarTargetRealmMaritimeRouteScore);
            }

            if (claimScore > 0f)
            {
                result.Score += claimScore;
                result.Reasons.AddRange(claimReasons);
                result.Motives.AddRange(claimMotives);
            }

            float cultureScore = CalculateSameCultureFiefScore(clan, target.Fiefs);
            if (cultureScore > 0f)
            {
                result.Score += cultureScore;
                result.Reasons.Add($"same-culture fortifications +{cultureScore:0}");
                AddMotive(result, WarTargetMotiveType.CulturalUnification, cultureScore);
            }

            float predatoryWeight = CalculateFrontierPredationWeight(clan);
            bool hasLocalEconomicFrontier = localLandFrontier.HasRoute || localMaritimeFrontier.HasRoute;
            FrontierAssessment economicFrontier = SelectClosestFrontierPrize(
                localLandFrontier,
                context.LandThreshold,
                localMaritimeFrontier,
                context.MaritimeThreshold);
            float economicMultiplier = 1f;
            if (!hasLocalEconomicFrontier && predatoryWeight > 0f)
            {
                FrontierAssessment realmLandPrize = realmLandFrontier
                    ? AssessRealmLandFrontier(
                        context.RealmFiefs,
                        target.Fiefs,
                        context.RealmFrontierKey,
                        target.FrontierKey,
                        context.LandThreshold)
                    : FrontierAssessment.None;
                FrontierAssessment realmMaritimePrize = realmMaritimeRoute
                    ? AssessRealmMaritimeFrontier(
                        context.RealmPorts,
                        target.Ports,
                        context.RealmFrontierKey,
                        target.FrontierKey,
                        context.MaritimeThreshold)
                    : FrontierAssessment.None;
                economicFrontier = SelectClosestFrontierPrize(
                    realmLandPrize,
                    context.LandThreshold,
                    realmMaritimePrize,
                    context.MaritimeThreshold);
                economicMultiplier = C.WarPeaceRevampInteriorFrontierProsperityMultiplier;
            }

            float frontierProsperityScore = CalculateFrontierProsperityScore(
                economicFrontier,
                predatoryWeight,
                economicMultiplier,
                out Settlement frontierPrize);
            if (frontierProsperityScore > 0.01f && frontierPrize != null)
            {
                result.Score += frontierProsperityScore;
                result.Reasons.Add(hasLocalEconomicFrontier
                    ? $"rich frontier prize: {frontierPrize.Name} +{frontierProsperityScore:0.#}"
                    : $"realm plunder opportunity: {frontierPrize.Name} +{frontierProsperityScore:0.#}");
                AddMotive(
                    result,
                    hasLocalEconomicFrontier
                        ? WarTargetMotiveType.RichFrontierPrize
                        : WarTargetMotiveType.RealmPlunderOpportunity,
                    frontierProsperityScore,
                    frontierPrize.Name);
            }

            float powerScore = CalculatePowerScore(sourceKingdom, targetKingdom);
            if (Math.Abs(powerScore) > 0.01f)
            {
                result.Score += powerScore;
                result.Reasons.Add(powerScore > 0f
                    ? $"target looks vulnerable +{powerScore:0}"
                    : $"target looks dangerous {powerScore:0}");
                if (powerScore > 0f)
                    AddMotive(result, WarTargetMotiveType.MilitaryVulnerability, powerScore);
            }

            if (!activeWar && !liberationTarget)
            {
                WarFrontReadinessAssessment frontReadiness = WarFrontReadinessService.Assess(
                    sourceKingdom,
                    targetKingdom);
                if (frontReadiness.IsAdditionalFront)
                {
                    if (frontReadiness.TargetScorePenalty > 0.01f)
                        result.Score -= frontReadiness.TargetScorePenalty;

                    if (!frontReadiness.CanDeclare)
                    {
                        result.Reasons.Add(
                            $"combined-front strength ratio {frontReadiness.PowerRatio:0.00} "
                            + $"below {C.WarPeaceRevampSecondFrontMinimumPowerRatio:0.00}"
                            + (frontReadiness.TargetScorePenalty > 0.01f
                                ? $" {-frontReadiness.TargetScorePenalty:0}"
                                : string.Empty));
                    }
                    else if (frontReadiness.TargetScorePenalty > 0.01f)
                    {
                        result.Reasons.Add(
                            $"second-front risk {-frontReadiness.TargetScorePenalty:0} "
                            + $"(combined strength ratio {frontReadiness.PowerRatio:0.00})");
                    }
                    else
                    {
                        result.Reasons.Add(
                            $"combined-front strength secure ({frontReadiness.PowerRatio:0.00})");
                    }
                }
            }

            if (!liberationTarget)
            {
                float pactScore = CalculateDiplomaticPactScore(sourceKingdom, targetKingdom, context.TradeAgreements, result.Reasons);
                if (Math.Abs(pactScore) > 0.01f)
                    result.Score += pactScore;
            }

            float relationScore = CalculateRelationScore(clan, targetKingdom);
            if (Math.Abs(relationScore) > 0.01f)
            {
                result.Score += relationScore;
                result.Reasons.Add(relationScore > 0f
                    ? $"poor relations with target ruler +{relationScore:0}"
                    : $"good relations with target ruler {relationScore:0}");
                if (relationScore > 0f)
                    AddMotive(result, WarTargetMotiveType.HostileRuler, relationScore, targetKingdom.Leader?.Name);
            }

            float crossRealmTieScore = CalculateCrossRealmTieScore(clan, targetKingdom, result.Reasons, result.Motives);
            if (Math.Abs(crossRealmTieScore) > 0.01f)
                result.Score += crossRealmTieScore;

            float hostageDeterrence = Campaign.Current?.GetCampaignBehavior<HostagePactBehavior>()?.WarDeterrence(clan, targetKingdom) ?? 0;
            if (hostageDeterrence > 0)
            {
                result.Score -= hostageDeterrence;
                result.Reasons.Add($"hostage-backed peace -{hostageDeterrence:0}");
            }
            result.Score = Math.Max(0f, Math.Min(100f, result.Score));
            return result;
        }

        private static float CalculateClaimScore(
            ScoringContext context,
            Kingdom targetKingdom,
            List<string> reasons,
            List<WarTargetMotive> motives)
        {
            Clan clan = context.Clan;
            FeudalTitleBehavior titleBehavior = context.TitleBehavior;
            if (titleBehavior == null || clan == null || targetKingdom == null)
                return 0f;

            float score = 0f;
            HashSet<string> countedTitles = new HashSet<string>();

            foreach (FeudalClaimRecord claim in titleBehavior.GetActiveClaimsByClan(clan))
            {
                FeudalTitleRecord title = titleBehavior.GetTitle(claim.TargetTitleId);
                if (!IsTitleControlledByKingdom(title, targetKingdom, context.ClansById) || !countedTitles.Add(title.TitleId))
                    continue;

                float value = claim.Strength == FeudalClaimStrength.Strong
                    ? BellumCivileOptions.WarTargetStrongClaimScore
                    : BellumCivileOptions.WarTargetWeakClaimScore;
                score += value;
                reasons.Add($"{claim.Strength.ToString().ToLowerInvariant()} claim to {title.Name} +{value:0}");
                motives?.Add(new WarTargetMotive
                {
                    Type = claim.Strength == FeudalClaimStrength.Strong
                        ? WarTargetMotiveType.StrongClaim
                        : WarTargetMotiveType.WeakClaim,
                    Score = value,
                    Subject = new TextObject(title.Name ?? string.Empty)
                });
            }

            foreach (FeudalTitleRecord title in context.AllTitles)
            {
                if (title == null
                    || !title.IsActive
                    || title.DeJureHolderClanId != clan.StringId
                    || title.DeFactoHolderClanId == clan.StringId
                    || !IsTitleControlledByKingdom(title, targetKingdom, context.ClansById)
                    || !countedTitles.Add(title.TitleId))
                {
                    continue;
                }

                score += BellumCivileOptions.WarTargetImpliedDeJureScore;
                reasons.Add($"legal title occupied: {title.Name} +{BellumCivileOptions.WarTargetImpliedDeJureScore:0}");
                motives?.Add(new WarTargetMotive
                {
                    Type = WarTargetMotiveType.DeJureReclamation,
                    Score = BellumCivileOptions.WarTargetImpliedDeJureScore,
                    Subject = new TextObject(title.Name ?? string.Empty)
                });
            }

            return Math.Min(100f, score);
        }

        private static bool IsTitleControlledByKingdom(
            FeudalTitleRecord title,
            Kingdom targetKingdom,
            Dictionary<string, Clan> clansById)
        {
            if (title == null
                || !title.IsActive
                || targetKingdom == null
                || string.IsNullOrWhiteSpace(title.DeFactoHolderClanId)
                || !clansById.TryGetValue(title.DeFactoHolderClanId, out Clan holder))
            {
                return false;
            }

            return holder.Kingdom == targetKingdom;
        }

        private static float CalculateSameCultureFiefScore(Clan clan, IReadOnlyCollection<Settlement> targetFiefs)
        {
            if (clan?.Culture == null || targetFiefs == null)
                return 0f;

            int count = targetFiefs.Count(settlement => settlement.Culture == clan.Culture);
            return Math.Min(30f, count * C.WarPeaceRevampSameCultureFiefScore);
        }

        private static float CalculatePowerScore(Kingdom sourceKingdom, Kingdom targetKingdom)
        {
            float sourceStrength = Math.Max(1f, sourceKingdom?.CurrentTotalStrength ?? 1f);
            float targetStrength = Math.Max(1f, targetKingdom?.CurrentTotalStrength ?? 1f);
            float ratio = sourceStrength / targetStrength;
            if (ratio >= 1.25f)
                return Math.Min(C.WarPeaceRevampPowerOpportunityCap, (ratio - 1f) * C.WarPeaceRevampPowerScoreSlope);
            if (ratio <= 0.85f)
                return -Math.Min(C.WarPeaceRevampPowerDangerCap, ((1f / Math.Max(0.01f, ratio)) - 1f) * C.WarPeaceRevampPowerScoreSlope);
            return 0f;
        }

        private static float CalculateRelationScore(Clan clan, Kingdom targetKingdom)
        {
            Clan targetRulingClan = targetKingdom?.RulingClan;
            if (clan == null || targetRulingClan == null)
                return 0f;

            int relation = clan.GetRelationWithClan(targetRulingClan);
            if (relation < 0)
                return -relation * C.WarPeaceRevampBadRulerRelationScale;
            if (relation > 25)
                return -(relation - 25) * C.WarPeaceRevampGoodRulerRelationPenaltyScale;
            return 0f;
        }

        private static float CalculateDiplomaticPactScore(
            Kingdom sourceKingdom,
            Kingdom targetKingdom,
            ITradeAgreementsCampaignBehavior tradeAgreements,
            List<string> reasons)
        {
            if (sourceKingdom == null || targetKingdom == null || sourceKingdom.IsAtWarWith(targetKingdom))
                return 0f;

            float score = 0f;
            if (sourceKingdom.AlliedKingdoms?.Contains(targetKingdom) == true)
            {
                score += BellumCivileOptions.WarTargetFormalAlliancePenalty;
                reasons.Add($"formal alliance {BellumCivileOptions.WarTargetFormalAlliancePenalty:0}");
            }

            if (tradeAgreements != null && tradeAgreements.HasTradeAgreement(sourceKingdom, targetKingdom, out _))
            {
                score += BellumCivileOptions.WarTargetTradeAgreementPenalty;
                reasons.Add($"trade agreement {BellumCivileOptions.WarTargetTradeAgreementPenalty:0}");
            }

            return score;
        }

        private static float CalculateCrossRealmTieScore(
            Clan clan,
            Kingdom targetKingdom,
            List<string> reasons,
            List<WarTargetMotive> motives)
        {
            if (clan == null || targetKingdom?.Clans == null)
                return 0f;

            float hostileScore = 0f;
            float friendlyScore = 0f;
            float marriageScore = 0f;
            Clan hostileClan = null;
            Clan friendlyClan = null;
            Clan marriageClan = null;

            foreach (Clan targetClan in targetKingdom.Clans.Where(IsValidClan))
            {
                int relation = clan.GetRelationWithClan(targetClan);
                if (relation < 0)
                {
                    float score = Math.Min(
                        C.WarPeaceRevampCrossRealmHostileRelationCap,
                        -relation * C.WarPeaceRevampCrossRealmHostileRelationScale);
                    if (score > hostileScore)
                    {
                        hostileScore = score;
                        hostileClan = targetClan;
                    }
                }
                else if (relation > 25)
                {
                    float score = Math.Max(
                        C.WarPeaceRevampCrossRealmFriendlyRelationCap,
                        -(relation - 25) * C.WarPeaceRevampCrossRealmFriendlyRelationScale);
                    if (score < friendlyScore)
                    {
                        friendlyScore = score;
                        friendlyClan = targetClan;
                    }
                }

                if (MarriageAllianceHelper.HasMarriageAlliance(clan, targetClan))
                {
                    float score = targetClan == targetKingdom.RulingClan
                        ? BellumCivileOptions.WarTargetRulingMarriageAlliancePenalty
                        : BellumCivileOptions.WarTargetMarriageAlliancePenalty;
                    if (score < marriageScore)
                    {
                        marriageScore = score;
                        marriageClan = targetClan;
                    }
                }
            }

            float total = hostileScore + friendlyScore + marriageScore;
            total = Math.Max(C.WarPeaceRevampCrossRealmTieCapNegative, Math.Min(C.WarPeaceRevampCrossRealmTieCapPositive, total));

            if (hostileScore > 0.01f && hostileClan != null)
            {
                reasons.Add($"foreign rival {hostileClan.Name} +{hostileScore:0}");
                motives?.Add(new WarTargetMotive
                {
                    Type = WarTargetMotiveType.ForeignRival,
                    Score = hostileScore,
                    Subject = hostileClan.Name
                });
            }
            if (friendlyScore < -0.01f && friendlyClan != null)
                reasons.Add($"foreign friendship with {friendlyClan.Name} {friendlyScore:0}");
            if (marriageScore < -0.01f && marriageClan != null)
                reasons.Add($"marriage ties with {marriageClan.Name} {marriageScore:0}");

            return total;
        }

        private static void AddMotive(
            WarTargetScore target,
            WarTargetMotiveType type,
            float score,
            TextObject subject = null)
        {
            if (target == null || score <= 0f)
                return;

            target.Motives.Add(new WarTargetMotive
            {
                Type = type,
                Score = score,
                Subject = subject
            });
        }

        private ScoringContext BuildScoringContext(Clan clan, Kingdom sourceKingdom)
        {
            int currentDay = (int)CampaignTime.Now.ToDays;
            if (_frontierCacheDay != currentDay)
            {
                _frontierCache.Clear();
                _detailedFrontierCache.Clear();
                _frontierCacheDay = currentDay;
            }

            float averageTownDistance = Campaign.Current.GetAverageDistanceBetweenClosestTwoTownsWithNavigationType(MobileParty.NavigationType.All);
            float landThreshold = averageTownDistance > 0f
                ? averageTownDistance
                : Campaign.MapDiagonal * 0.15f;
            float maritimeThreshold = averageTownDistance > 0f
                ? averageTownDistance * C.WarPeaceRevampMaritimeNeighborDistanceMultiplier
                : Campaign.MapDiagonal * 0.30f;
            bool navalDlcLoaded = IsNavalDlcLoaded();

            List<Settlement> localFiefs = clan.Settlements?.Where(IsStrategicFief).ToList() ?? new List<Settlement>();
            List<Settlement> realmFiefs = sourceKingdom.Settlements?.Where(IsStrategicFief).ToList() ?? new List<Settlement>();
            FeudalTitleBehavior titleBehavior = FeudalTitleBehavior.Instance
                ?? Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>();

            return new ScoringContext
            {
                Clan = clan,
                SourceKingdom = sourceKingdom,
                ClientKingdoms = ClientKingdomBehavior.Instance,
                TradeAgreements = Campaign.Current?.GetCampaignBehavior<ITradeAgreementsCampaignBehavior>(),
                TitleBehavior = titleBehavior,
                ClansById = Clan.All
                    .Where(candidate => candidate != null && !string.IsNullOrWhiteSpace(candidate.StringId))
                    .GroupBy(candidate => candidate.StringId)
                    .ToDictionary(group => group.Key, group => group.First()),
                AllTitles = titleBehavior?.GetAllTitles()?.Where(title => title != null).ToList() ?? new List<FeudalTitleRecord>(),
                LocalFiefs = localFiefs,
                RealmFiefs = realmFiefs,
                LocalPorts = navalDlcLoaded ? localFiefs.Where(settlement => settlement.HasPort).ToList() : new List<Settlement>(),
                RealmPorts = navalDlcLoaded ? realmFiefs.Where(settlement => settlement.HasPort).ToList() : new List<Settlement>(),
                LocalFrontierKey = BuildSettlementSetKey("clan:" + clan.StringId, localFiefs),
                RealmFrontierKey = BuildSettlementSetKey("realm:" + sourceKingdom.StringId, realmFiefs),
                LandThreshold = landThreshold,
                MaritimeThreshold = maritimeThreshold,
                NavalDlcLoaded = navalDlcLoaded
            };
        }

        private static TargetContext BuildTargetContext(Kingdom targetKingdom)
        {
            List<Settlement> fiefs = targetKingdom?.Settlements?.Where(IsStrategicFief).ToList() ?? new List<Settlement>();
            return new TargetContext
            {
                Kingdom = targetKingdom,
                Fiefs = fiefs,
                Ports = IsNavalDlcLoaded() ? fiefs.Where(settlement => settlement.HasPort).ToList() : new List<Settlement>(),
                FrontierKey = BuildSettlementSetKey("realm:" + (targetKingdom?.StringId ?? string.Empty), fiefs)
            };
        }

        private bool HasLandFrontier(
            List<Settlement> sourceFiefs,
            List<Settlement> targetFiefs,
            string sourceKey,
            string targetKey,
            float neighborThreshold)
        {
            if (sourceFiefs.Count == 0 || targetFiefs.Count == 0)
                return false;

            string cacheKey = "land|" + sourceKey + "|" + targetKey;
            if (_frontierCache.TryGetValue(cacheKey, out bool cached))
                return cached;

            foreach (Settlement sourceFief in sourceFiefs)
            {
                foreach (Settlement targetFief in targetFiefs)
                {
                    float distance = Campaign.Current.Models.MapDistanceModel.GetDistance(
                        sourceFief,
                        targetFief,
                        isFromPort: false,
                        isTargetingPort: false,
                        MobileParty.NavigationType.All);
                    if (distance <= neighborThreshold)
                    {
                        _frontierCache[cacheKey] = true;
                        return true;
                    }
                }
            }

            _frontierCache[cacheKey] = false;
            return false;
        }

        private FrontierAssessment AssessLocalLandFrontier(
            List<Settlement> sourceFiefs,
            List<Settlement> targetFiefs,
            string sourceKey,
            string targetKey,
            float neighborThreshold)
        {
            return AssessDetailedFrontier(
                sourceFiefs,
                targetFiefs,
                "land-prize|" + sourceKey + "|" + targetKey,
                neighborThreshold,
                isMaritime: false);
        }

        private FrontierAssessment AssessRealmLandFrontier(
            List<Settlement> sourceFiefs,
            List<Settlement> targetFiefs,
            string sourceKey,
            string targetKey,
            float neighborThreshold)
        {
            return AssessDetailedFrontier(
                sourceFiefs,
                targetFiefs,
                "land-realm-prize|" + sourceKey + "|" + targetKey,
                neighborThreshold,
                isMaritime: false);
        }

        private bool HasMaritimeRoute(
            List<Settlement> sourcePorts,
            List<Settlement> targetPorts,
            string sourceKey,
            string targetKey,
            float maritimeThreshold)
        {
            if (sourcePorts.Count == 0 || targetPorts.Count == 0)
                return false;

            string cacheKey = "sea|" + sourceKey + "|" + targetKey;
            if (_frontierCache.TryGetValue(cacheKey, out bool cached))
                return cached;

            foreach (Settlement sourcePort in sourcePorts)
            {
                foreach (Settlement targetPort in targetPorts)
                {
                    float distance = Campaign.Current.Models.MapDistanceModel.GetDistance(
                        sourcePort,
                        targetPort,
                        isFromPort: true,
                        isTargetingPort: true,
                        MobileParty.NavigationType.All);
                    if (distance <= maritimeThreshold)
                    {
                        _frontierCache[cacheKey] = true;
                        return true;
                    }
                }
            }

            _frontierCache[cacheKey] = false;
            return false;
        }

        private FrontierAssessment AssessLocalMaritimeFrontier(
            List<Settlement> sourcePorts,
            List<Settlement> targetPorts,
            string sourceKey,
            string targetKey,
            float maritimeThreshold)
        {
            return AssessDetailedFrontier(
                sourcePorts,
                targetPorts,
                "sea-prize|" + sourceKey + "|" + targetKey,
                maritimeThreshold,
                isMaritime: true);
        }

        private FrontierAssessment AssessRealmMaritimeFrontier(
            List<Settlement> sourcePorts,
            List<Settlement> targetPorts,
            string sourceKey,
            string targetKey,
            float maritimeThreshold)
        {
            return AssessDetailedFrontier(
                sourcePorts,
                targetPorts,
                "sea-realm-prize|" + sourceKey + "|" + targetKey,
                maritimeThreshold,
                isMaritime: true);
        }

        private FrontierAssessment AssessDetailedFrontier(
            List<Settlement> sourceFiefs,
            List<Settlement> targetFiefs,
            string cacheKey,
            float threshold,
            bool isMaritime)
        {
            if (sourceFiefs.Count == 0 || targetFiefs.Count == 0)
                return FrontierAssessment.None;
            if (_detailedFrontierCache.TryGetValue(cacheKey, out FrontierAssessment cached))
                return cached;

            Settlement closestTarget = null;
            float closestDistance = float.MaxValue;
            foreach (Settlement sourceFief in sourceFiefs)
            {
                foreach (Settlement targetFief in targetFiefs)
                {
                    float distance = Campaign.Current.Models.MapDistanceModel.GetDistance(
                        sourceFief,
                        targetFief,
                        isFromPort: isMaritime,
                        isTargetingPort: isMaritime,
                        MobileParty.NavigationType.All);
                    if (distance <= threshold && distance < closestDistance)
                    {
                        closestTarget = targetFief;
                        closestDistance = distance;
                    }
                }
            }

            FrontierAssessment result = closestTarget != null
                ? new FrontierAssessment(closestTarget, closestDistance)
                : FrontierAssessment.None;
            _detailedFrontierCache[cacheKey] = result;
            return result;
        }

        private static FrontierAssessment SelectClosestFrontierPrize(
            FrontierAssessment land,
            float landThreshold,
            FrontierAssessment maritime,
            float maritimeThreshold)
        {
            if (!land.HasRoute)
                return maritime;
            if (!maritime.HasRoute)
                return land;

            float normalizedLandDistance = land.Distance / Math.Max(1f, landThreshold);
            float normalizedMaritimeDistance = maritime.Distance / Math.Max(1f, maritimeThreshold);
            return normalizedLandDistance <= normalizedMaritimeDistance ? land : maritime;
        }

        private static float CalculateFrontierPredationWeight(Clan clan)
        {
            Hero leader = clan?.Leader;
            if (leader == null)
                return 0f;

            int greed = Math.Max(0, -leader.GetTraitLevel(DefaultTraits.Generosity));
            int dishonor = Math.Max(0, -leader.GetTraitLevel(DefaultTraits.Honor));
            return Math.Min(
                C.WarPeaceRevampFrontierPersonalityWeightCap,
                greed * C.WarPeaceRevampFrontierGreedWeight
                    + dishonor * C.WarPeaceRevampFrontierDishonorWeight);
        }

        private static float CalculateFrontierProsperityScore(
            FrontierAssessment frontier,
            float personalityWeight,
            float distanceMultiplier,
            out Settlement prize)
        {
            prize = frontier?.TargetSettlement;
            if (personalityWeight <= 0f || distanceMultiplier <= 0f || prize?.Town == null)
                return 0f;

            float prosperityScore = Math.Min(
                C.WarPeaceRevampFrontierProsperityBaseCap,
                Math.Max(0f, prize.Town.Prosperity) / C.WarPeaceRevampFrontierProsperityPerScore);
            return Math.Min(
                C.WarPeaceRevampFrontierProsperityScoreCap * distanceMultiplier,
                prosperityScore * personalityWeight * distanceMultiplier);
        }

        private static string BuildSettlementSetKey(string ownerKey, List<Settlement> settlements)
        {
            unchecked
            {
                int hash = 17;
                foreach (Settlement settlement in settlements.OrderBy(item => item.StringId))
                {
                    string id = settlement.StringId ?? string.Empty;
                    foreach (char character in id)
                        hash = hash * 31 + character;
                }

                return ownerKey + ":" + settlements.Count + ":" + hash;
            }
        }

        private static bool IsNavalDlcLoaded()
        {
            if (_navalDlcLoaded.HasValue)
                return _navalDlcLoaded.Value;

            string mapDistanceModelName = Campaign.Current?.Models?.MapDistanceModel?.GetType().FullName ?? string.Empty;
            if (mapDistanceModelName.IndexOf("NavalDLC", StringComparison.OrdinalIgnoreCase) >= 0)
                return (_navalDlcLoaded = true).Value;

            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                string assemblyName = assembly.GetName().Name ?? string.Empty;
                if (assemblyName.Equals("NavalDLC", StringComparison.OrdinalIgnoreCase)
                    || assemblyName.Equals("NavalDLC.ViewModelCollection", StringComparison.OrdinalIgnoreCase)
                    || assembly.GetType("NavalDLC.GameComponents.NavalDLCMapDistanceModel", throwOnError: false) != null)
                {
                    return (_navalDlcLoaded = true).Value;
                }
            }

            return (_navalDlcLoaded = false).Value;
        }

        private static bool IsValidClan(Clan clan)
        {
            return clan != null
                && !clan.IsEliminated
                && !clan.IsBanditFaction
                && clan.Leader != null
                && !clan.Leader.IsDead
                && clan.Kingdom != null
                && !clan.IsUnderMercenaryService;
        }

        private static bool IsValidKingdom(Kingdom kingdom)
        {
            return kingdom != null
                && !kingdom.IsEliminated
                && kingdom.RulingClan != null
                && kingdom.RulingClan.Leader != null;
        }

        private static bool IsStrategicFief(Settlement settlement)
        {
            return settlement != null && (settlement.IsTown || settlement.IsCastle);
        }

        private sealed class ScoringContext
        {
            public Clan Clan;
            public Kingdom SourceKingdom;
            public ClientKingdomBehavior ClientKingdoms;
            public ITradeAgreementsCampaignBehavior TradeAgreements;
            public FeudalTitleBehavior TitleBehavior;
            public Dictionary<string, Clan> ClansById;
            public List<FeudalTitleRecord> AllTitles;
            public List<Settlement> LocalFiefs;
            public List<Settlement> RealmFiefs;
            public List<Settlement> LocalPorts;
            public List<Settlement> RealmPorts;
            public string LocalFrontierKey;
            public string RealmFrontierKey;
            public float LandThreshold;
            public float MaritimeThreshold;
            public bool NavalDlcLoaded;
        }

        private sealed class TargetContext
        {
            public Kingdom Kingdom;
            public List<Settlement> Fiefs;
            public List<Settlement> Ports;
            public string FrontierKey;
        }

        private sealed class FrontierAssessment
        {
            public static readonly FrontierAssessment None = new FrontierAssessment(null, float.MaxValue);

            public Settlement TargetSettlement { get; }
            public float Distance { get; }
            public bool HasRoute => TargetSettlement != null;

            public FrontierAssessment(Settlement targetSettlement, float distance)
            {
                TargetSettlement = targetSettlement;
                Distance = distance;
            }
        }
    }
}
