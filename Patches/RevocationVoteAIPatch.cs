using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Election;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Library;
using C = BellumCivile.BellumCivileConstants;

namespace BellumCivile.Patches
{
    [HarmonyPatch(typeof(SettlementClaimantPreliminaryDecision), "DetermineSupport")]
    public class RevocationVoteAIPatch
    {
        private static FieldInfo _ownerClanField;
        private static readonly Dictionary<Type, FieldInfo> _changeOwnerFieldCache = new Dictionary<Type, FieldInfo>();
        private static readonly Dictionary<Type, PropertyInfo> _changeOwnerPropCache = new Dictionary<Type, PropertyInfo>();
        private static readonly HashSet<Type> _changeOwnerInitialized = new HashSet<Type>();

        public static bool Prefix(SettlementClaimantPreliminaryDecision __instance, Clan clan, DecisionOutcome possibleOutcome, ref float __result)
        {
            if (__instance?.Settlement == null || clan?.Leader == null || possibleOutcome == null)
            {
                __result = 0f;
                return false;
            }

            Kingdom kingdom = __instance.Settlement.MapFaction as Kingdom;
            Clan owner = ResolveOwnerClan(__instance);
            if (kingdom == null || owner?.Leader == null || clan.Kingdom != kingdom)
            {
                __result = 0f;
                return false;
            }

            bool shouldRevoke = ResolveShouldSettlementOwnerChange(possibleOutcome);
            float revokeScore = CalculateSupportScore(__instance.Settlement, owner, clan, kingdom);

            __result = shouldRevoke ? revokeScore : -revokeScore;
            return false;
        }

        public static float CalculateSupportScore(Settlement settlement, Clan owner, Clan voter, Kingdom kingdom)
        {
            if (settlement == null || owner?.Leader == null || voter?.Leader == null || kingdom == null)
                return 0f;

            FactionManagerBehavior factionManager = Campaign.Current?.GetCampaignBehavior<FactionManagerBehavior>();
            FactionObject voterFaction = factionManager?.GetIdeologicalFaction(voter);
            FactionObject ownerFaction = factionManager?.GetIdeologicalFaction(owner);

            float score = 0f;

            if (voter == owner)
                score -= C.RevokeOwnerSelfDefenseScore;

            int relationToOwner = voter.Leader.GetRelation(owner.Leader);
            if (relationToOwner > 0)
                score -= relationToOwner * C.RevokeOwnerFriendRelationScale;
            else if (relationToOwner < 0)
                score += -relationToOwner * C.RevokeOwnerEnemyRelationScale;

            if (voter != owner && MarriageAllianceHelper.HasMarriageAlliance(voter, owner))
                score -= C.RevokeOwnerMarriageAllianceDefense;

            if (voterFaction != null && ownerFaction != null)
            {
                if (voterFaction.Type == ownerFaction.Type)
                    score -= C.RevokeSameFactionDefense;
            }

            score += CalculateLegalPressure(settlement, owner, voter, kingdom, voterFaction);
            score += CalculateIdeologyPressure(settlement, owner, voter, kingdom, voterFaction);

            if (voter == kingdom.RulingClan && owner == kingdom.RulingClan)
                score -= C.RevokeOwnerSelfDefenseScore;

            return score;
        }

        private static float CalculateLegalPressure(
            Settlement settlement,
            Clan owner,
            Clan voter,
            Kingdom kingdom,
            FactionObject voterFaction)
        {
            FeudalTitleBehavior titleBehavior = Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>();
            if (titleBehavior == null || !titleBehavior.TryGetBarony(settlement, out FeudalTitleRecord barony) || barony == null)
                return 0f;

            float pressure = 0f;
            float multiplier = GetLegalMultiplier(voterFaction);

            bool ownerIsDeJure = barony.DeJureHolderClanId == owner.StringId;
            bool ownerStrongClaim = titleBehavior.HasActiveClaim(owner, barony, FeudalClaimStrength.Strong);
            bool ownerWeakClaim = titleBehavior.HasActiveClaim(owner, barony, FeudalClaimStrength.Weak);

            if (ownerIsDeJure)
                pressure -= C.RevokeOwnerDeJureDefense;
            else
                pressure += C.RevokeOwnerOnlyDeFactoBonus;

            if (!ownerIsDeJure)
            {
                if (ownerStrongClaim)
                    pressure -= C.RevokeOwnerStrongClaimDefense;
                else if (ownerWeakClaim)
                    pressure -= C.RevokeOwnerWeakClaimDefense;
                else
                    pressure += C.RevokeOwnerNoClaimBonus;
            }

            ClaimantPressure bestClaimant = FindBestInternalClaimant(titleBehavior, barony, owner, voter, kingdom);
            if (bestClaimant.Score > 0f)
                pressure += bestClaimant.Score;

            return pressure * multiplier;
        }

        private static ClaimantPressure FindBestInternalClaimant(
            FeudalTitleBehavior titleBehavior,
            FeudalTitleRecord title,
            Clan owner,
            Clan voter,
            Kingdom kingdom)
        {
            ClaimantPressure best = new ClaimantPressure();
            foreach (Clan candidate in Clan.All)
            {
                if (candidate == null
                    || candidate == owner
                    || candidate.Leader == null
                    || candidate.Kingdom != kingdom
                    || candidate.IsEliminated
                    || candidate.IsUnderMercenaryService
                    || (candidate.IsMinorFaction && candidate != Clan.PlayerClan))
                    continue;

                float baseScore = 0f;
                if (title.DeJureHolderClanId == candidate.StringId)
                    baseScore = C.RevokeInternalDeJureClaimantBonus;
                else if (titleBehavior.HasActiveClaim(candidate, title, FeudalClaimStrength.Strong))
                    baseScore = C.RevokeInternalStrongClaimantBonus;
                else if (titleBehavior.HasActiveClaim(candidate, title, FeudalClaimStrength.Weak))
                    baseScore = C.RevokeInternalWeakClaimantBonus;

                if (baseScore <= 0f)
                    continue;

                if (voter != candidate && voter?.Leader != null)
                {
                    int relation = voter.Leader.GetRelation(candidate.Leader);
                    if (relation >= C.FiefClaimFriendRelationThreshold)
                        baseScore += MathF.Min(C.RevokeClaimantFriendSympathyCap, relation * C.RevokeClaimantFriendRelationScale);
                }

                if (baseScore > best.Score)
                {
                    best.Score = baseScore;
                    best.Claimant = candidate;
                }
            }

            return best;
        }

        private static float CalculateIdeologyPressure(
            Settlement settlement,
            Clan owner,
            Clan voter,
            Kingdom kingdom,
            FactionObject voterFaction)
        {
            if (voterFaction == null)
                return 0f;

            float score = 0f;
            FeudalTitleType? ownerHighestTitle = FeudalPoliticalWeightHelper.GetHighestHeldTitleRank(owner);
            switch (voterFaction.Type)
            {
                case FactionType.Nobility:
                    if (!ownerHighestTitle.HasValue || ownerHighestTitle.Value == FeudalTitleType.Barony)
                        score += C.RevokeAristocratUpstartBonus;
                    if (FeudalPoliticalWeightHelper.IsHighLandedRank(ownerHighestTitle))
                        score -= C.RevokeAristocratOldBloodDefense;
                    break;

                case FactionType.Royalists:
                    if (kingdom.RulingClan?.Leader != null)
                    {
                        int relationToRuler = owner.Leader.GetRelation(kingdom.RulingClan.Leader);
                        if (relationToRuler < 0)
                            score += -relationToRuler * C.RevokeRoyalistDisloyaltyScale;
                        if (MarriageAllianceHelper.HasMarriageAlliance(owner, kingdom.RulingClan))
                            score -= C.RevokeRoyalistMarriageDefense;
                    }
                    break;

                case FactionType.Glory:
                    if (settlement.IsCastle)
                    {
                        if (owner.CurrentTotalStrength < C.FiefMilWeakStrengthThreshold)
                            score += C.RevokeMilitaristWeakCastleHolderBonus;
                        else if (owner.CurrentTotalStrength >= C.FiefMilStrongStrengthThreshold)
                            score -= C.RevokeMilitaristStrongHolderDefense;
                    }
                    break;

                case FactionType.Liberty:
                    if (owner.Fiefs.Count >= 3)
                        score += C.RevokePopulistOverlandedBonus;
                    if (!ownerHighestTitle.HasValue || ownerHighestTitle.Value == FeudalTitleType.Barony || owner.Fiefs.Count <= 1)
                        score -= C.RevokePopulistPoorOwnerDefense;
                    break;
            }

            return score;
        }

        private static float GetLegalMultiplier(FactionObject voterFaction)
        {
            if (voterFaction == null)
                return C.RevokeLegalNeutralMultiplier;

            switch (voterFaction.Type)
            {
                case FactionType.Nobility:
                    return C.RevokeLegalAristocratMultiplier;
                case FactionType.Royalists:
                    return C.RevokeLegalRoyalistMultiplier;
                case FactionType.Glory:
                    return C.RevokeLegalMilitaristMultiplier;
                case FactionType.Liberty:
                    return C.RevokeLegalPopulistMultiplier;
                default:
                    return C.RevokeLegalNeutralMultiplier;
            }
        }

        private static Clan ResolveOwnerClan(SettlementClaimantPreliminaryDecision decision)
        {
            if (decision?.Settlement?.OwnerClan != null)
                return decision.Settlement.OwnerClan;

            if (_ownerClanField == null)
            {
                const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
                _ownerClanField = typeof(SettlementClaimantPreliminaryDecision).GetField("_ownerClan", flags)
                               ?? typeof(SettlementClaimantPreliminaryDecision).GetField("OwnerClan", flags);
            }

            return _ownerClanField?.GetValue(decision) as Clan;
        }

        internal static bool ResolveShouldSettlementOwnerChange(DecisionOutcome outcome)
        {
            if (outcome == null)
                return false;

            Type type = outcome.GetType();
            if (!_changeOwnerInitialized.Contains(type))
            {
                const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

                FieldInfo field = null;
                PropertyInfo property = null;
                foreach (string name in new[] { "ShouldSettlementOwnerChange", "ShouldOwnerChange", "ShouldSettlementChangeOwner" })
                {
                    field = type.GetField(name, flags);
                    if (field != null && field.FieldType == typeof(bool))
                        break;

                    field = null;
                    property = type.GetProperty(name, flags);
                    if (property != null && property.PropertyType == typeof(bool))
                        break;

                    property = null;
                }

                if (field != null)
                    _changeOwnerFieldCache[type] = field;
                if (property != null)
                    _changeOwnerPropCache[type] = property;
                _changeOwnerInitialized.Add(type);
            }

            if (_changeOwnerFieldCache.TryGetValue(type, out FieldInfo cachedField))
                return (bool)cachedField.GetValue(outcome);
            if (_changeOwnerPropCache.TryGetValue(type, out PropertyInfo cachedProp))
                return (bool)cachedProp.GetValue(outcome);

            return false;
        }

        private struct ClaimantPressure
        {
            public Clan Claimant;
            public float Score;
        }
    }
}
