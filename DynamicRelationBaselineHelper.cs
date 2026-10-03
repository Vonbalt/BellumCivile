using System;
using System.Collections.Generic;
using System.Linq;
using BellumCivile.Behaviors;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.Core;
using TaleWorlds.Localization;
using C = BellumCivile.BellumCivileConstants;

namespace BellumCivile
{
    internal sealed class DynamicRelationBaselineBreakdown
    {
        public int Foundation { get; }
        public int PoliticalConditions { get; }
        public int Total { get; }
        public IReadOnlyList<string> FoundationReasons { get; }
        public IReadOnlyList<string> PoliticalConditionReasons { get; }
        public IReadOnlyList<string> Reasons { get; }

        public DynamicRelationBaselineBreakdown(
            int foundation,
            int politicalConditions,
            int total,
            List<string> foundationReasons,
            List<string> politicalConditionReasons,
            List<string> reasons)
        {
            Foundation = foundation;
            PoliticalConditions = politicalConditions;
            Total = total;
            FoundationReasons = foundationReasons ?? new List<string>();
            PoliticalConditionReasons = politicalConditionReasons ?? new List<string>();
            Reasons = reasons ?? new List<string>();
        }
    }

    internal static class DynamicRelationBaselineHelper
    {
        private sealed class TitleClaimSnapshot
        {
            public string TargetTitleId { get; set; }
            public FeudalClaimStrength Strength { get; set; }
            public string OriginClanId { get; set; }
        }

        private sealed class TitleClanContext
        {
            public Clan Clan { get; }
            public IReadOnlyCollection<FeudalTitleRecord> DeJureTitles { get; }
            public IReadOnlyCollection<FeudalTitleRecord> DeFactoTitles { get; }
            public IReadOnlyCollection<FeudalTitleRecord> TitlesHeldEitherWay { get; }
            public List<TitleClaimSnapshot> Claims { get; }

            public TitleClanContext(
                Clan clan,
                IReadOnlyCollection<FeudalTitleRecord> deJureTitles,
                IReadOnlyCollection<FeudalTitleRecord> deFactoTitles,
                IReadOnlyCollection<FeudalTitleRecord> titlesHeldEitherWay,
                List<TitleClaimSnapshot> claims)
            {
                Clan = clan;
                DeJureTitles = deJureTitles;
                DeFactoTitles = deFactoTitles;
                TitlesHeldEitherWay = titlesHeldEitherWay;
                Claims = claims;
            }
        }

        public static int CalculateRaw(Hero firstHero, Hero secondHero)
        {
            int score = CalculateFoundationScore(firstHero, secondHero, null)
                + CalculatePoliticalConditionScore(firstHero, secondHero, null);
            return ClampScore(score);
        }

        public static int CalculateFoundationRaw(Hero firstHero, Hero secondHero)
        {
            return CalculateFoundationScore(firstHero, secondHero, null);
        }

        public static int CalculatePoliticalConditionsRaw(Hero firstHero, Hero secondHero)
        {
            return CalculatePoliticalConditionScore(firstHero, secondHero, null);
        }

        internal static int CalculateKinshipBaselineIncrease(Hero first, Hero second)
        {
            int bonus = PersonalKinshipHelper.GetBonus(first, second);
            if (bonus == 0) return 0;
            int score = CalculateFoundationScore(first, second, null) + CalculatePoliticalConditionScore(first, second, null);
            return ClampScore(score) - ClampScore(score - bonus);
        }

        internal static int CalculateSpouseBaselineIncrease(Hero first, Hero second)
        {
            int increase = PersonalKinshipHelper.GetBonus(first, second) - PersonalKinshipHelper.GetBloodBonus(first, second);
            if (increase == 0) return 0;
            int score = CalculateFoundationScore(first, second, null) + CalculatePoliticalConditionScore(first, second, null);
            return ClampScore(score) - ClampScore(score - increase);
        }

        public static DynamicRelationBaselineBreakdown Calculate(Hero firstHero, Hero secondHero)
        {
            List<string> foundationReasons = new List<string>();
            List<string> politicalConditionReasons = new List<string>();
            int foundation = CalculateFoundationScore(firstHero, secondHero, foundationReasons);
            int politicalConditions = CalculatePoliticalConditionScore(firstHero, secondHero, politicalConditionReasons);
            int score = foundation + politicalConditions;
            int clamped = ClampScore(score);
            List<string> reasons = new List<string>(foundationReasons.Count + politicalConditionReasons.Count + 1);
            reasons.AddRange(foundationReasons);
            reasons.AddRange(politicalConditionReasons);
            if (clamped != score)
            {
                TextObject cap = new TextObject("{=BC_Relation_BaselineCap}Baseline cap: {OLD_SCORE} -> {NEW_SCORE}");
                cap.SetTextVariable("OLD_SCORE", score);
                cap.SetTextVariable("NEW_SCORE", clamped);
                reasons.Add(cap.ToString());
            }

            return new DynamicRelationBaselineBreakdown(
                foundation,
                politicalConditions,
                clamped,
                foundationReasons,
                politicalConditionReasons,
                reasons);
        }

        private static int CalculateFoundationScore(Hero firstHero, Hero secondHero, List<string> reasons)
        {
            int score = 0;

            if (firstHero == null || secondHero == null || firstHero == secondHero)
                return 0;

            if (firstHero.IsNotable || secondHero.IsNotable)
                return 0;

            switch (PersonalKinshipHelper.GetBond(firstHero, secondHero))
            {
                case PersonalBloodBond.ParentChild:
                    Add(ref score, reasons, C.DynamicRelationParentChildBonus, "{=BC_Relation_ParentChild}Bond of parent and child: {DELTA}"); break;
                case PersonalBloodBond.Siblings:
                    Add(ref score, reasons, C.DynamicRelationSiblingBonus, "{=BC_Relation_Siblings}Bond between siblings: {DELTA}"); break;
                case PersonalBloodBond.Extended:
                    Add(ref score, reasons, C.DynamicRelationExtendedFamilyBonus, "{=BC_Relation_ExtendedFamily}Close blood kin: {DELTA}"); break;
                case PersonalBloodBond.Spouse:
                    Add(ref score, reasons, C.DynamicRelationSpouseBonus, "{=BC_Relation_Spouse}Bond of marriage: {DELTA}"); break;
            }

            AddTraitScore(ref score, reasons, firstHero, secondHero, DefaultTraits.Calculating, "{=BC_Relation_TraitCalculating}Calculating");
            AddTraitScore(ref score, reasons, firstHero, secondHero, DefaultTraits.Generosity, "{=BC_Relation_TraitGenerosity}Generosity");
            AddTraitScore(ref score, reasons, firstHero, secondHero, DefaultTraits.Honor, "{=BC_Relation_TraitHonor}Honor");
            AddTraitScore(ref score, reasons, firstHero, secondHero, DefaultTraits.Mercy, "{=BC_Relation_TraitMercy}Mercy");
            AddTraitScore(ref score, reasons, firstHero, secondHero, DefaultTraits.Valor, "{=BC_Relation_TraitValor}Valor");

            if (firstHero.Culture != null && firstHero.Culture == secondHero.Culture)
                Add(ref score, reasons, C.DynamicRelationSameCultureBonus, "{=BC_Relation_SameCulture}Same culture: {DELTA}");
            else if (firstHero.Culture != null && secondHero.Culture != null)
                Add(ref score, reasons, -C.DynamicRelationDifferentCulturePenalty, "{=BC_Relation_DifferentCulture}Different culture: {DELTA}");

            return score;
        }

        private static int CalculatePoliticalConditionScore(Hero firstHero, Hero secondHero, List<string> reasons)
        {
            int score = 0;

            if (firstHero == null || secondHero == null || firstHero == secondHero)
                return 0;

            if (DynamicRelationBehavior.IsPersonalOnlyPair(firstHero, secondHero))
                return 0;

            Clan firstClan = firstHero.Clan;
            Clan secondClan = secondHero.Clan;

            if (firstClan != null && secondClan != null)
            {
                if (MarriageAllianceHelper.HasMarriageAlliance(firstClan, secondClan))
                    Add(ref score, reasons, C.DynamicRelationMarriageAllianceBonus, "{=BC_Relation_MarriageAlliance}Marriage alliance: {DELTA}");

                Kingdom firstKingdom = firstClan.Kingdom;
                Kingdom secondKingdom = secondClan.Kingdom;
                if (firstKingdom != null && secondKingdom != null)
                {
                    if (firstKingdom == secondKingdom)
                    {
                        Add(ref score, reasons, C.DynamicRelationSameKingdomBonus, "{=BC_Relation_SameRealm}Same realm: {DELTA}");
                    }
                    else
                    {
                        Add(ref score, reasons, -C.DynamicRelationDifferentKingdomPenalty, "{=BC_Relation_DifferentRealm}Different realm: {DELTA}");
                        if (firstKingdom.IsAtWarWith(secondKingdom))
                            Add(ref score, reasons, -C.DynamicRelationKingdomWarPenalty, "{=BC_Relation_KingdomsAtWar}Kingdoms at war: {DELTA}");
                        else if (AreAllied(firstKingdom, secondKingdom))
                            Add(ref score, reasons, C.DynamicRelationAlliedKingdomBonus, "{=BC_Relation_AlliedKingdoms}Allied kingdoms: {DELTA}");
                    }
                }

                AddLandlessRulerGrievance(ref score, reasons, firstClan, secondClan);
                AddCourtFactionScore(ref score, reasons, firstClan, secondClan);
                AddCivilWarScore(ref score, reasons, firstClan, secondClan);
                Add(ref score, reasons, ElectiveSuccessionBehavior.Instance?.EndorsementOpinion(firstHero, secondHero) ?? 0,
                    "{=BC_Election_EndorsementRelation}Current succession endorsement: {DELTA}");
                AddFeudalTitleScore(ref score, reasons, firstClan, secondClan);
                Add(ref score, reasons, GetPrisonerCustodyModifier(firstHero, secondHero),
                    "{=BC_Relation_HouseMemberPrisoner}Holding a member of the other house prisoner: {DELTA}");
            }

            return score;
        }

        internal static int GetPrisonerCustodyModifier(Hero firstHero, Hero secondHero)
        {
            Clan firstClan = firstHero?.Clan;
            Clan secondClan = secondHero?.Clan;
            if (firstClan == null || secondClan == null || firstClan == secondClan
                || firstClan.IsEliminated || secondClan.IsEliminated
                || firstHero.IsDead || secondHero.IsDead
                || firstClan.Leader != firstHero || secondClan.Leader != secondHero)
                return 0;

            return HoldsHouseMember(firstClan, secondClan) || HoldsHouseMember(secondClan, firstClan)
                ? -C.DynamicRelationHouseMemberPrisonerPenalty : 0;
        }

        private static bool HoldsHouseMember(Clan captorClan, Clan captiveClan)
        {
            foreach (Hero member in captiveClan.Heroes)
            {
                if (member == null || member.IsDead || !member.IsPrisoner || member.Clan != captiveClan)
                    continue;
                var custody = member.PartyBelongedToAsPrisoner;
                Clan owner = custody?.Settlement?.OwnerClan ?? custody?.MobileParty?.ActualClan;
                if (owner == captorClan)
                    return true;
            }
            return false;
        }

        private static int ClampScore(int score)
        {
            return Math.Max(C.DynamicRelationBaselineMin, Math.Min(C.DynamicRelationBaselineMax, score));
        }

        private static void AddLandlessRulerGrievance(ref int score, List<string> reasons, Clan firstClan, Clan secondClan)
        {
            if (firstClan?.Kingdom == null
                || secondClan?.Kingdom == null
                || firstClan.Kingdom != secondClan.Kingdom
                || firstClan == secondClan)
            {
                return;
            }

            Kingdom kingdom = firstClan.Kingdom;
            Clan rulingClan = kingdom.RulingClan;
            if (rulingClan == null)
                return;

            Clan aggrievedClan = null;
            if (IsLandlessNobleVassal(firstClan, rulingClan) && secondClan == rulingClan)
                aggrievedClan = firstClan;
            else if (IsLandlessNobleVassal(secondClan, rulingClan) && firstClan == rulingClan)
                aggrievedClan = secondClan;

            if (aggrievedClan == null)
                return;

            if (aggrievedClan.Fiefs.Count > 0)
                return;

            Add(ref score, reasons, -C.DynamicRelationLandlessTitleDesirePenalty, "{=BC_Relation_LandlessTitleDesire}Desires landed title: {DELTA}");
        }

        private static bool IsLandlessNobleVassal(Clan clan, Clan rulingClan)
        {
            if (clan == null
                || clan == rulingClan
                || clan.IsUnderMercenaryService
                || (clan.IsMinorFaction && clan != Clan.PlayerClan)
                || clan.Fiefs.Count > 0)
            {
                return false;
            }

            FeudalTitleBehavior titleBehavior = FeudalTitleBehavior.Instance;
            return titleBehavior == null || !titleBehavior.GetTitlesHeldByClan(clan, deJure: false).Any(title => title != null && title.IsActive);
        }

        private static void AddCourtFactionScore(ref int score, List<string> reasons, Clan firstClan, Clan secondClan)
        {
            FactionManagerBehavior manager = FactionManagerBehavior.Instance;
            if (manager == null || firstClan?.Kingdom == null || firstClan.Kingdom != secondClan?.Kingdom)
                return;

            FactionObject firstFaction = manager.GetIdeologicalFaction(firstClan);
            FactionObject secondFaction = manager.GetIdeologicalFaction(secondClan);
            if (firstFaction == null || secondFaction == null)
                return;

            if (firstFaction == secondFaction)
            {
                Add(ref score, reasons, C.DynamicRelationSameCourtFactionBonus, "{=BC_Relation_SameCourtFaction}Same court faction: {DELTA}");
            }
            else
            {
                Add(ref score, reasons, -C.DynamicRelationDifferentCourtFactionPenalty, "{=BC_Relation_DifferentCourtFactions}Different court faction: {DELTA}");
            }
        }

        private static void AddCivilWarScore(ref int score, List<string> reasons, Clan firstClan, Clan secondClan)
        {
            FactionManagerBehavior manager = FactionManagerBehavior.Instance;
            if (manager == null)
                return;

            bool firstRebel = manager.IsClanOnActiveCivilWarRebelSide(firstClan, out FactionObject firstRebelFaction, out Kingdom firstRebelKingdom);
            bool secondRebel = manager.IsClanOnActiveCivilWarRebelSide(secondClan, out FactionObject secondRebelFaction, out Kingdom secondRebelKingdom);

            if (firstRebel && secondRebel)
            {
                if (firstRebelFaction == secondRebelFaction)
                    Add(ref score, reasons, C.DynamicRelationSameRebellionBonus, "{=BC_Relation_SameRebellion}Same rebellion: {DELTA}");
                else
                    Add(ref score, reasons, -C.DynamicRelationOpposingRebellionPenalty, "{=BC_Relation_RivalRebellions}Rival rebellions: {DELTA}");
                return;
            }

            if (firstRebel && IsLoyalistAgainst(secondClan, firstRebelFaction, firstRebelKingdom))
                Add(ref score, reasons, -C.DynamicRelationOpposingCivilWarSidePenalty, "{=BC_Relation_OpposingCivilWarSides}Opposing civil-war sides: {DELTA}");

            if (secondRebel && IsLoyalistAgainst(firstClan, secondRebelFaction, secondRebelKingdom))
                Add(ref score, reasons, -C.DynamicRelationOpposingCivilWarSidePenalty, "{=BC_Relation_OpposingCivilWarSides}Opposing civil-war sides: {DELTA}");
        }

        private static void AddFeudalTitleScore(ref int score, List<string> reasons, Clan firstClan, Clan secondClan)
        {
            FeudalTitleBehavior titleBehavior = FeudalTitleBehavior.Instance;
            if (titleBehavior == null || firstClan == null || secondClan == null || firstClan == secondClan)
                return;

            TitleClanContext firstContext = BuildTitleContext(titleBehavior, firstClan);
            TitleClanContext secondContext = BuildTitleContext(titleBehavior, secondClan);

            if (HaveSharedDynasticOrigin(firstClan, secondClan, firstContext.Claims, secondContext.Claims))
                Add(ref score, reasons, C.DynamicRelationSharedDynastyBonus, "{=BC_Relation_SharedDynasty}Shared dynasty: {DELTA}");

            if (HasRightfulImmediateLiege(titleBehavior, firstContext, secondClan)
                || HasRightfulImmediateLiege(titleBehavior, secondContext, firstClan))
            {
                Add(ref score, reasons, C.DynamicRelationRightfulLiegeBonus, "{=BC_Relation_RightfulLiege}Rightful liege: {DELTA}");
            }

            if (HasUnlawfulImmediateLiege(titleBehavior, firstContext, secondClan)
                || HasUnlawfulImmediateLiege(titleBehavior, secondContext, firstClan))
            {
                Add(ref score, reasons, -C.DynamicRelationUnlawfulLiegePenalty, "{=BC_Relation_UnlawfulLiege}Unlawful liege: {DELTA}");
            }

            int serviceModifier = FeudalServiceBehavior.Instance?.GetOngoingRelationModifierBetween(
                firstClan,
                secondClan,
                firstContext.DeFactoTitles,
                secondContext.DeFactoTitles) ?? 0;
            Add(ref score, reasons, serviceModifier, "{=BC_Relation_ServiceOwed}Feudal service: {DELTA}");

            AddDeJureTitleDesire(ref score, reasons, titleBehavior, firstContext, secondContext);
            AddDecoupledServiceGrievances(ref score, reasons, titleBehavior, firstContext, secondContext);

            Dictionary<string, FeudalClaimStrength> conflictStrengthByTitle = new Dictionary<string, FeudalClaimStrength>(StringComparer.Ordinal);
            AddTitleClaimConflicts(titleBehavior, firstContext.Claims, firstClan, secondClan, conflictStrengthByTitle);
            AddTitleClaimConflicts(titleBehavior, secondContext.Claims, secondClan, firstClan, conflictStrengthByTitle);

            // Claims in either direction produce one penalty, using the strongest conflict.
            if (conflictStrengthByTitle.Values.Contains(FeudalClaimStrength.Strong))
            {
                Add(ref score, reasons, -C.DynamicRelationStrongTitleClaimPenalty, "{=BC_Relation_ContestedStrongClaim}Contested strong claim: {DELTA}");
            }
            else if (conflictStrengthByTitle.Count > 0)
            {
                Add(ref score, reasons, -C.DynamicRelationWeakTitleClaimPenalty, "{=BC_Relation_ContestedWeakClaim}Contested weak claim: {DELTA}");
            }
        }

        private static void AddDeJureTitleDesire(
            ref int score,
            List<string> reasons,
            FeudalTitleBehavior titleBehavior,
            TitleClanContext firstContext,
            TitleClanContext secondContext)
        {
            Clan firstClan = firstContext?.Clan;
            Clan secondClan = secondContext?.Clan;
            if (titleBehavior == null
                || firstClan == null
                || secondClan == null
                || firstClan == secondClan
                || firstClan.Kingdom == null
                || firstClan.Kingdom != secondClan.Kingdom)
            {
                return;
            }

            AddDeJureTitleDesireForVassal(ref score, reasons, titleBehavior, firstContext, secondClan);
            AddDeJureTitleDesireForVassal(ref score, reasons, titleBehavior, secondContext, firstClan);
        }

        private static void AddDeJureTitleDesireForVassal(
            ref int score,
            List<string> reasons,
            FeudalTitleBehavior titleBehavior,
            TitleClanContext lowerContext,
            Clan higherTitleHolder)
        {
            Clan lowerClan = lowerContext?.Clan;
            if (lowerClan == null)
                return;

            HashSet<string> desiredTitleIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (FeudalTitleRecord title in lowerContext.DeFactoTitles)
            {
                if (title == null || !title.IsActive || string.IsNullOrWhiteSpace(title.ParentTitleId))
                    continue;

                FeudalTitleRecord parent = titleBehavior.GetTitle(title.ParentTitleId);
                if (parent == null
                    || !parent.IsActive
                    || parent.TitleType <= title.TitleType
                    || string.Equals(parent.DeFactoHolderClanId, lowerClan.StringId, StringComparison.Ordinal)
                    || !string.Equals(parent.DeFactoHolderClanId, higherTitleHolder.StringId, StringComparison.Ordinal))
                {
                    continue;
                }

                if (!desiredTitleIds.Add(parent.TitleId))
                    continue;

                int penalty = CalculateDesiredTitlePenalty(parent.TitleType, lowerClan.Leader);
                if (reasons == null)
                {
                    score -= penalty;
                    continue;
                }

                TextObject text = new TextObject("{=BC_Relation_DesiresTitle}Desires {TITLE_NAME}: {DELTA}");
                text.SetTextVariable("TITLE_NAME", FeudalTitleDisplayHelper.FormatTitleName(parent, lowerClan));
                Add(ref score, reasons, -penalty, text);
            }
        }

        private static int CalculateDesiredTitlePenalty(FeudalTitleType titleType, Hero leader)
        {
            int penalty;
            switch (titleType)
            {
                case FeudalTitleType.County:
                    penalty = C.DynamicRelationDesiresCountyPenalty;
                    break;
                case FeudalTitleType.Duchy:
                    penalty = C.DynamicRelationDesiresDuchyPenalty;
                    break;
                case FeudalTitleType.Kingdom:
                    penalty = C.DynamicRelationDesiresKingdomPenalty;
                    break;
                case FeudalTitleType.Empire:
                    penalty = C.DynamicRelationDesiresEmpirePenalty;
                    break;
                default:
                    return 0;
            }

            if (leader != null)
            {
                if (leader.GetTraitLevel(DefaultTraits.Generosity) < 0)
                    penalty += C.DynamicRelationDesiresTitleGreedyPenalty;
                if (leader.GetTraitLevel(DefaultTraits.Honor) < 0)
                    penalty += C.DynamicRelationDesiresTitleDishonorablePenalty;
                if (leader.GetTraitLevel(DefaultTraits.Calculating) > 0)
                    penalty += C.DynamicRelationDesiresTitleCalculatingPenalty;
                if (leader.GetTraitLevel(DefaultTraits.Generosity) > 0)
                    penalty -= C.DynamicRelationDesiresTitleGenerousReduction;
                if (leader.GetTraitLevel(DefaultTraits.Honor) > 0)
                    penalty -= C.DynamicRelationDesiresTitleHonorableReduction;
            }

            return Math.Max(C.DynamicRelationDesiresTitleMinimumPenalty, penalty);
        }

        private static void AddDecoupledServiceGrievances(
            ref int score,
            List<string> reasons,
            FeudalTitleBehavior titleBehavior,
            TitleClanContext firstContext,
            TitleClanContext secondContext)
        {
            Clan firstClan = firstContext?.Clan;
            Clan secondClan = secondContext?.Clan;
            if (titleBehavior == null
                || firstClan == null
                || secondClan == null
                || firstClan == secondClan
                || firstClan.Kingdom == null
                || firstClan.Kingdom != secondClan.Kingdom)
            {
                return;
            }

            AddDecoupledServiceGrievancesForLiege(ref score, reasons, titleBehavior, firstContext, secondClan);
            AddDecoupledServiceGrievancesForLiege(ref score, reasons, titleBehavior, secondContext, firstClan);
        }

        private static void AddDecoupledServiceGrievancesForLiege(
            ref int score,
            List<string> reasons,
            FeudalTitleBehavior titleBehavior,
            TitleClanContext legalLiegeContext,
            Clan holderClan)
        {
            Clan legalLiege = legalLiegeContext?.Clan;
            if (legalLiege == null)
                return;

            foreach (FeudalTitleRecord legalParent in legalLiegeContext.DeJureTitles)
            {
                if (legalParent == null || !legalParent.IsActive)
                    continue;

                foreach (FeudalTitleRecord title in titleBehavior.GetChildTitles(legalParent, FeudalHierarchyMode.DeJure))
                {
                    if (title == null
                        || !title.IsActive
                        || !string.Equals(title.DeFactoHolderClanId, holderClan.StringId, StringComparison.Ordinal)
                        || !titleBehavior.IsServiceDecoupledByRank(title))
                    {
                        continue;
                    }

                    if (reasons == null)
                    {
                        score -= C.DynamicRelationDesiresTitleControlPenalty;
                        continue;
                    }

                    TextObject text = new TextObject("{=BC_Relation_DesiresTitleControl}Desires control of {TITLE_NAME}: {DELTA}");
                    text.SetTextVariable("TITLE_NAME", FeudalTitleDisplayHelper.FormatTitleName(title, legalLiege));
                    Add(ref score, reasons, -C.DynamicRelationDesiresTitleControlPenalty, text);
                }
            }
        }

        private static TitleClanContext BuildTitleContext(FeudalTitleBehavior titleBehavior, Clan clan)
        {
            IReadOnlyCollection<FeudalTitleRecord> deJureTitles = titleBehavior.GetTitlesHeldByClan(clan, deJure: true);
            IReadOnlyCollection<FeudalTitleRecord> deFactoTitles = titleBehavior.GetTitlesHeldByClan(clan, deJure: false);
            List<FeudalTitleRecord> titlesHeldEitherWay = new List<FeudalTitleRecord>(deJureTitles.Count + deFactoTitles.Count);
            HashSet<string> seenTitleIds = new HashSet<string>(StringComparer.Ordinal);

            foreach (FeudalTitleRecord title in deJureTitles)
            {
                if (title != null && title.IsActive && seenTitleIds.Add(title.TitleId))
                    titlesHeldEitherWay.Add(title);
            }

            foreach (FeudalTitleRecord title in deFactoTitles)
            {
                if (title != null && title.IsActive && seenTitleIds.Add(title.TitleId))
                    titlesHeldEitherWay.Add(title);
            }

            List<TitleClaimSnapshot> claims = BuildTitleClaims(
                clan,
                titleBehavior.GetActiveClaimsByClan(clan),
                deJureTitles);

            return new TitleClanContext(clan, deJureTitles, deFactoTitles, titlesHeldEitherWay, claims);
        }

        private static List<TitleClaimSnapshot> BuildTitleClaims(
            Clan clan,
            IReadOnlyCollection<FeudalClaimRecord> activeClaims,
            IReadOnlyCollection<FeudalTitleRecord> deJureTitles)
        {
            Dictionary<string, TitleClaimSnapshot> claimsByTitle = new Dictionary<string, TitleClaimSnapshot>(StringComparer.Ordinal);
            foreach (FeudalClaimRecord claim in activeClaims)
            {
                if (claim == null || string.IsNullOrWhiteSpace(claim.TargetTitleId))
                    continue;

                TitleClaimSnapshot snapshot = new TitleClaimSnapshot
                {
                    TargetTitleId = claim.TargetTitleId,
                    Strength = claim.Strength,
                    OriginClanId = claim.OriginClanId
                };

                AddOrUpgradeClaimSnapshot(claimsByTitle, snapshot);
            }

            foreach (FeudalTitleRecord title in deJureTitles)
            {
                if (title == null || !title.IsActive)
                    continue;

                if (string.IsNullOrWhiteSpace(title.DeFactoHolderClanId)
                    || string.Equals(title.DeFactoHolderClanId, clan.StringId, StringComparison.Ordinal))
                {
                    continue;
                }

                AddOrUpgradeClaimSnapshot(claimsByTitle, new TitleClaimSnapshot
                {
                    TargetTitleId = title.TitleId,
                    Strength = FeudalClaimStrength.Strong,
                    OriginClanId = string.Empty
                });
            }

            return claimsByTitle.Values.ToList();
        }

        private static void AddOrUpgradeClaimSnapshot(
            Dictionary<string, TitleClaimSnapshot> claimsByTitle,
            TitleClaimSnapshot candidate)
        {
            if (candidate == null || string.IsNullOrWhiteSpace(candidate.TargetTitleId))
                return;

            if (!claimsByTitle.TryGetValue(candidate.TargetTitleId, out TitleClaimSnapshot current)
                || (current.Strength != FeudalClaimStrength.Strong && candidate.Strength == FeudalClaimStrength.Strong))
            {
                claimsByTitle[candidate.TargetTitleId] = candidate;
            }
        }

        private static bool HaveSharedDynasticOrigin(
            Clan firstClan,
            Clan secondClan,
            List<TitleClaimSnapshot> firstClaims,
            List<TitleClaimSnapshot> secondClaims)
        {
            foreach (TitleClaimSnapshot firstClaim in firstClaims)
            {
                string firstOrigin = GetExternalOrigin(firstClan, firstClaim);
                if (string.IsNullOrEmpty(firstOrigin))
                    continue;

                if (string.Equals(firstOrigin, secondClan.StringId, StringComparison.Ordinal))
                    return true;

                foreach (TitleClaimSnapshot secondClaim in secondClaims)
                {
                    string secondOrigin = GetExternalOrigin(secondClan, secondClaim);
                    if (!string.IsNullOrEmpty(secondOrigin)
                        && string.Equals(firstOrigin, secondOrigin, StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
            }

            foreach (TitleClaimSnapshot secondClaim in secondClaims)
            {
                string secondOrigin = GetExternalOrigin(secondClan, secondClaim);
                if (string.Equals(secondOrigin, firstClan.StringId, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        private static string GetExternalOrigin(Clan clan, TitleClaimSnapshot claim)
        {
            if (claim == null
                || string.IsNullOrWhiteSpace(claim.OriginClanId)
                || string.Equals(claim.OriginClanId, clan.StringId, StringComparison.Ordinal))
            {
                return null;
            }

            return claim.OriginClanId;
        }

        private static bool HasRightfulImmediateLiege(FeudalTitleBehavior titleBehavior, TitleClanContext vassalContext, Clan liegeClan)
        {
            Clan vassalClan = vassalContext?.Clan;
            if (titleBehavior == null || vassalClan == null || liegeClan == null || vassalClan == liegeClan)
                return false;

            foreach (FeudalTitleRecord title in vassalContext.TitlesHeldEitherWay)
            {
                if (title == null || !title.IsActive || string.IsNullOrWhiteSpace(title.ParentTitleId))
                    continue;

                FeudalTitleRecord parent = titleBehavior.GetTitle(title.ParentTitleId);
                if (parent == null || !parent.IsActive)
                    continue;

                if (string.Equals(parent.DeJureHolderClanId, liegeClan.StringId, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        private static bool HasUnlawfulImmediateLiege(FeudalTitleBehavior titleBehavior, TitleClanContext vassalContext, Clan liegeClan)
        {
            Clan vassalClan = vassalContext?.Clan;
            if (titleBehavior == null || vassalClan == null || liegeClan == null || vassalClan == liegeClan)
                return false;

            foreach (FeudalTitleRecord title in vassalContext.TitlesHeldEitherWay)
            {
                if (title == null || !title.IsActive || string.IsNullOrWhiteSpace(title.DeFactoParentTitleId))
                    continue;

                FeudalTitleRecord deFactoParent = titleBehavior.GetTitle(title.DeFactoParentTitleId);
                if (deFactoParent == null
                    || !deFactoParent.IsActive
                    || !string.Equals(deFactoParent.DeFactoHolderClanId, liegeClan.StringId, StringComparison.Ordinal))
                {
                    continue;
                }

                FeudalTitleRecord deJureParent = titleBehavior.GetTitle(title.ParentTitleId);
                if (deJureParent == null || !deJureParent.IsActive)
                    continue;

                if (!string.Equals(deJureParent.DeJureHolderClanId, liegeClan.StringId, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        private static void AddTitleClaimConflicts(
            FeudalTitleBehavior titleBehavior,
            List<TitleClaimSnapshot> claimantClaims,
            Clan claimantClan,
            Clan holderClan,
            Dictionary<string, FeudalClaimStrength> conflictStrengthByTitle)
        {
            foreach (TitleClaimSnapshot claim in claimantClaims)
            {
                if (claim == null)
                    continue;

                FeudalTitleRecord title = titleBehavior.GetTitle(claim.TargetTitleId);
                if (title == null || !title.IsActive)
                    continue;

                bool holderControlsTitle =
                    string.Equals(title.DeFactoHolderClanId, holderClan.StringId, StringComparison.Ordinal)
                    || string.Equals(title.DeJureHolderClanId, holderClan.StringId, StringComparison.Ordinal);
                if (!holderControlsTitle)
                    continue;

                bool claimantAlreadyHasPhysicalControl =
                    string.Equals(title.DeFactoHolderClanId, claimantClan.StringId, StringComparison.Ordinal);
                if (claimantAlreadyHasPhysicalControl)
                    continue;

                if (!conflictStrengthByTitle.TryGetValue(title.TitleId, out FeudalClaimStrength currentStrength)
                    || (currentStrength != FeudalClaimStrength.Strong && claim.Strength == FeudalClaimStrength.Strong))
                {
                    conflictStrengthByTitle[title.TitleId] = claim.Strength;
                }
            }
        }

        private static bool IsLoyalistAgainst(Clan clan, FactionObject rebelFaction, Kingdom rebelKingdom)
        {
            if (clan == null || rebelFaction?.ParentKingdom == null)
                return false;

            if (clan.Kingdom != rebelFaction.ParentKingdom)
                return false;

            return rebelKingdom == null || clan.Kingdom.IsAtWarWith(rebelKingdom);
        }

        private static bool AreAllied(Kingdom firstKingdom, Kingdom secondKingdom)
        {
            if (firstKingdom == null || secondKingdom == null || firstKingdom == secondKingdom)
                return false;

            try
            {
                return firstKingdom.IsAllyWith(secondKingdom) || secondKingdom.IsAllyWith(firstKingdom);
            }
            catch (NullReferenceException)
            {
                return false;
            }
            catch (ArgumentNullException)
            {
                return false;
            }
        }

        private static void AddTraitScore(ref int score, List<string> reasons, Hero firstHero, Hero secondHero, TraitObject trait, string label)
        {
            int first = firstHero.GetTraitLevel(trait);
            int second = secondHero.GetTraitLevel(trait);
            if (first == 0 || second == 0)
                return;

            int magnitude = Math.Min(Math.Abs(first), Math.Abs(second)) * C.DynamicRelationTraitCompatibilityStep;
            int delta = first * second > 0 ? magnitude : -magnitude;
            if (reasons == null)
            {
                score += delta;
                return;
            }

            TextObject reason = new TextObject("{=BC_Relation_TraitAffinity}{TRAIT} affinity: {DELTA}");
            reason.SetTextVariable("TRAIT", new TextObject(label));
            Add(ref score, reasons, delta, reason);
        }

        private static void Add(ref int score, List<string> reasons, int delta, string reason)
        {
            if (delta == 0)
                return;

            score += delta;
            if (reasons == null)
                return;

            TextObject text = new TextObject(reason);
            text.SetTextVariable("DELTA", Format(delta));
            reasons.Add(text.ToString());
        }

        private static void Add(ref int score, List<string> reasons, int delta, TextObject reason)
        {
            if (delta == 0)
                return;

            score += delta;
            if (reasons == null)
                return;

            reason.SetTextVariable("DELTA", Format(delta));
            reasons.Add(reason.ToString());
        }

        private static string Format(int value)
        {
            return value >= 0 ? $"+{value}" : value.ToString();
        }
    }
}
