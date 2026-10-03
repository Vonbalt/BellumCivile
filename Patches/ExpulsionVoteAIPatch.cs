using System.Reflection;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Election;
using TaleWorlds.Library;
using C = BellumCivile.BellumCivileConstants;

namespace BellumCivile.Patches
{
    /// <summary>
    /// Why did I do this file?
    /// To hijack the AI's voting logic when the King attempts to legally expel a vassal. It forces lords to vote based on ideological unity, faction rivalries, and spite against the establishment.
    /// </summary>
    [HarmonyPatch(typeof(ExpelClanFromKingdomDecision), "DetermineSupport")]
    public class ExpulsionVoteAIPatch
    {
        private static FieldInfo  _expelFieldInfo;
        private static PropertyInfo _expelPropInfo;
        private static bool _expelReflectionInitialized = false;

        public static void ResetReflectionCache()
        {
            _expelFieldInfo = null;
            _expelPropInfo = null;
            _expelReflectionInitialized = false;
        }

        // What does this method do?
        // Computes the net ideology + relationship score for a clan voting on an expulsion decision,
        // always for the "expel = true" direction. Used by ExpulsionDeliberationBehavior to show the
        // player a lord's intended stance during the deliberation window (before the vote fires).
        public static float CalculateSupportScore(Clan targetClan, Clan proposerClan, Clan voterClan, FactionManagerBehavior factionManager)
        {
            if (voterClan == null || voterClan.Leader == null || targetClan == null || factionManager == null) return 0f;

            Kingdom kingdom = voterClan.Kingdom;
            if (kingdom == null) return 0f;

            float customMerit = 0f;
            int calculating = voterClan.Leader.GetTraitLevel(DefaultTraits.Calculating);

            FactionObject voterFaction  = factionManager.GetIdeologicalFaction(voterClan);
            FactionObject targetFaction = factionManager.GetIdeologicalFaction(targetClan);

            if (kingdom.RulingClan?.Leader != null && targetClan.Leader != null)
            {
                float targetRelationWithKing = targetClan.Leader.GetRelation(kingdom.RulingClan.Leader);
                if (targetRelationWithKing <= C.ExpelTargetSuspicionThresholdSevere) customMerit += C.ExpelTargetSuspicionBonusSevere;
                else if (targetRelationWithKing <= C.ExpelTargetSuspicionThresholdHigh) customMerit += C.ExpelTargetSuspicionBonusHigh;
                else if (targetRelationWithKing <= C.ExpelTargetSuspicionThresholdLow) customMerit += C.ExpelTargetSuspicionBonusLow;
            }

            if (targetClan != voterClan && targetClan.Leader != null)
            {
                float targetRelationMerit = -voterClan.Leader.GetRelation(targetClan.Leader) * C.ExpelTargetRelationScale;
                if (calculating > 0) targetRelationMerit *= C.ExpelTargetRelationCalcMultiplier;
                else if (calculating < 0) targetRelationMerit *= C.ExpelTargetRelationHotheadMultiplier;
                customMerit += targetRelationMerit;
            }

            if (voterFaction != null && targetFaction != null)
            {
                if (voterFaction.Type == targetFaction.Type)
                    customMerit -= C.ExpelBlocBonus;
                else
                    customMerit += C.ExpelNonRivalBonus;
            }

            if (kingdom.RulingClan?.Leader != null && proposerClan == kingdom.RulingClan
                && voterClan != proposerClan && voterClan != targetClan)
            {
                float relationWithKing = voterClan.Leader.GetRelation(kingdom.RulingClan.Leader);

                if (voterFaction != null && voterFaction.Type == FactionType.Royalists)
                    customMerit += C.ExpelRoyalistBlindFollow;
                else if (relationWithKing <= C.ExpelSpiteRelThreshold)
                {
                    customMerit -= C.ExpelSpiteAgainstKing;
                    if (calculating < 0) customMerit -= C.ExpelSpiteHotheadBonus;
                }
            }

            if (voterClan == kingdom.RulingClan)
            {
                int honor = voterClan.Leader.GetTraitLevel(DefaultTraits.Honor);
                int mercy = voterClan.Leader.GetTraitLevel(DefaultTraits.Mercy);
                customMerit += C.ExpelRulerWill;
                if (honor < 0 || mercy < 0) customMerit += C.ExpelTyrantBonus;
            }

            if (voterFaction != null && voterFaction.Leader != null && voterFaction.Leader != voterClan
                && voterFaction.Leader != targetClan && targetClan.Leader != null && voterFaction.Leader.Leader != null)
            {
                float leaderRelWithTarget = voterFaction.Leader.Leader.GetRelation(targetClan.Leader);
                float trustFactor = MathF.Max(0f, (float)voterClan.Leader.GetRelation(voterFaction.Leader.Leader)) / 100f;
                customMerit -= leaderRelWithTarget * trustFactor * C.FactionLeaderPullStrength;
            }

            return customMerit;
        }

        public static void Postfix(ExpelClanFromKingdomDecision __instance, Clan clan, DecisionOutcome possibleOutcome, ref float __result)
        {
            if (clan == null || clan.Leader == null || __instance.ClanToExpel == null) return;
            if (clan.IsUnderMercenaryService || (clan.IsMinorFaction && clan != Clan.PlayerClan)) return;

            var factionManager = Campaign.Current.GetCampaignBehavior<FactionManagerBehavior>();
            if (factionManager == null) return;

            bool expel = false;
            bool successfullyExtracted = false;

            if (!_expelReflectionInitialized)
            {
                var type = possibleOutcome.GetType();
                const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

                System.Type searchType = type;
                while (searchType != null && _expelFieldInfo == null)
                {
                    _expelFieldInfo = searchType.GetField("ShouldBeExpelled", flags)
                                   ?? searchType.GetField("Expel", flags)
                                   ?? searchType.GetField("IsExpelled", flags)
                                   ?? searchType.GetField("bShouldBeExpelled", flags);
                    searchType = searchType.BaseType;
                }

                if (_expelFieldInfo == null)
                {
                    searchType = type;
                    while (searchType != null && _expelPropInfo == null)
                    {
                        _expelPropInfo = searchType.GetProperty("ShouldBeExpelled", flags)
                                      ?? searchType.GetProperty("Expel", flags)
                                      ?? searchType.GetProperty("IsExpelled", flags);
                        searchType = searchType.BaseType;
                    }
                }

                _expelReflectionInitialized = true;

                if (_expelFieldInfo == null && _expelPropInfo == null)
                {
                    var boolMembers = new System.Text.StringBuilder();
                    searchType = type;
                    while (searchType != null && searchType != typeof(object))
                    {
                        foreach (var f in searchType.GetFields(flags))
                            if (f.FieldType == typeof(bool)) boolMembers.Append($"  field: {searchType.Name}.{f.Name}\n");
                        foreach (var p in searchType.GetProperties(flags))
                            if (p.PropertyType == typeof(bool)) boolMembers.Append($"  prop:  {searchType.Name}.{p.Name}\n");
                        searchType = searchType.BaseType;
                    }
                    BellumCivileNotifications.ShowPersonal(
                        $"[BellumCivile] ExpulsionVoteAIPatch: Could not locate expel field on '{type.Name}'. Ideological expulsion voting disabled.\nAvailable bool members:\n{boolMembers}",
                        BellumNotificationColors.Debug);
                }
            }

            if (_expelFieldInfo != null) { expel = (bool)_expelFieldInfo.GetValue(possibleOutcome); successfullyExtracted = true; }
            else if (_expelPropInfo != null) { expel = (bool)_expelPropInfo.GetValue(possibleOutcome); successfullyExtracted = true; }

            if (!successfullyExtracted) return;

            Clan targetClan = __instance.ClanToExpel;
            Clan proposer   = __instance.ProposerClan;
            Kingdom kingdom = clan.Kingdom;

            if (kingdom == null) return;

            var deliberation = Campaign.Current.GetCampaignBehavior<ExpulsionDeliberationBehavior>();
            int? bribed = deliberation?.GetBribedVote(kingdom, targetClan, clan);
            if (bribed.HasValue)
            {
                __result = expel ? bribed.Value : -bribed.Value;
                return;
            }
            float expelSupport = CalculateSupportScore(targetClan, proposer, clan, factionManager);
            __result = MathF.Max(0f, expel ? expelSupport : -expelSupport);
        }
    }
}
