﻿using System.Reflection;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Election;
using TaleWorlds.Core;
using TaleWorlds.Library;
using C = BellumCivile.BellumCivileConstants;

namespace BellumCivile.Patches
{
    /// <summary>
    /// Why did I do this file?
    /// To hijack the vanilla policy voting AI. Lords will now vote for laws based on their faction's political agenda, personal traits (like generosity or honor), and rivalries, overriding the default logic.
    /// </summary>
    [HarmonyPatch(typeof(KingdomPolicyDecision), "DetermineSupport")]
    public class PolicyVoteAIPatch
    {
        private static FieldInfo _enactFieldInfo;
        private static PropertyInfo _enactPropInfo;
        private static bool _reflectionInitialized = false;

        public static void ResetReflectionCache()
        {
            _enactFieldInfo = null;
            _enactPropInfo = null;
            _reflectionInitialized = false;
        }

        // What does this method do?
        // Computes the net ideology + relationship merit score for a clan voting on a policy decision,
        // without touching __result. Used by PolicyDeliberationBehavior to show the player a lord's
        // intended stance during the deliberation window (before the vote fires).
        public static float CalculateSupportScore(KingdomPolicyDecision decision, Clan clan, bool policyWillBeActive, FactionManagerBehavior factionManager)
        {
            if (clan?.Leader == null || decision?.Policy == null || factionManager == null) return 0f;
            var faction = factionManager.GetIdeologicalFaction(clan);
            FactionType? alignment = clan == decision.Kingdom.RulingClan
                ? CourtAgendaBehavior.Current?.GetFavoredBloc(decision.Kingdom) : faction?.Type;
            var alignedFaction = clan == decision.Kingdom.RulingClan && alignment.HasValue
                ? System.Linq.Enumerable.FirstOrDefault(factionManager.GetFactionsInKingdom(decision.Kingdom),
                    candidate => candidate.IsIdeology && candidate.Type == alignment.Value) : faction;
            float conviction = alignedFaction != null ? 40f * (int)IdeologyPolicyRoster.GetEffectiveStance(alignedFaction, decision.Policy) : 0;
            int honor = clan.Leader.GetTraitLevel(DefaultTraits.Honor);
            conviction *= honor > 0 ? 1.25f : honor < 0 ? 0.75f : 1f;
            if (clan == decision.Kingdom.RulingClan && IdeologyPolicyRoster.IsCrownPolicy(decision.Policy))
                conviction += 70f;
            float score = policyWillBeActive ? conviction : -conviction;
            Clan proposer = decision.ProposerClan;
            if (proposer?.Leader == null || proposer == clan) return score;
            bool passing = policyWillBeActive != PolicyDeliberationBehavior.ResolvePolicyAbolish(decision);
            float social = MathF.Clamp(clan.Leader.GetRelation(proposer.Leader) * 0.2f, -20f, 20f);
            if (proposer == decision.Kingdom.RulingClan && faction != null)
                social += MathF.Clamp(faction.Mood * 0.15f, -15f, 15f);
            if (faction?.Leader?.Leader != null && faction.Leader != clan)
            {
                float trust = MathF.Clamp(clan.Leader.GetRelation(faction.Leader.Leader) / 100f, 0f, 1f);
                social += MathF.Clamp(faction.Leader.Leader.GetRelation(proposer.Leader) * trust * 0.1f, -10f, 10f);
            }
            return score + (passing ? social : -social);
        }

        public static void Postfix(KingdomPolicyDecision __instance, Clan clan, DecisionOutcome possibleOutcome, ref float __result)
        {
            if (clan == null || clan.Leader == null || __instance.Policy == null) return;
            if (clan.IsUnderMercenaryService || clan.IsMinorFaction) return;

            var factionManager = Campaign.Current.GetCampaignBehavior<FactionManagerBehavior>();
            if (factionManager == null) return;

            bool motionWillBeEnforced = false;
            bool successfullyExtracted = false;

            if (!_reflectionInitialized)
            {
                var type = possibleOutcome.GetType();
                const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

                System.Type searchType = type;
                while (searchType != null && _enactFieldInfo == null)
                {
                    _enactFieldInfo = searchType.GetField("ShouldBeEnacted", flags)
                                   ?? searchType.GetField("ShouldDecisionBeEnacted", flags)
                                   ?? searchType.GetField("IsEnacted", flags)
                                   ?? searchType.GetField("IsDecisionEnacted", flags)
                                   ?? searchType.GetField("bIsEnacted", flags);
                    searchType = searchType.BaseType;
                }

                if (_enactFieldInfo == null)
                {
                    searchType = type;
                    while (searchType != null && _enactPropInfo == null)
                    {
                        _enactPropInfo = searchType.GetProperty("ShouldDecisionBeEnforced", flags)
                                      ?? searchType.GetProperty("ShouldBeEnacted", flags)
                                      ?? searchType.GetProperty("ShouldDecisionBeEnacted", flags)
                                      ?? searchType.GetProperty("IsEnacted", flags)
                                      ?? searchType.GetProperty("IsDecisionEnacted", flags);
                        searchType = searchType.BaseType;
                    }
                }

                _reflectionInitialized = true;

                if (_enactFieldInfo == null && _enactPropInfo == null)
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
                        $"[BellumCivile] PolicyVoteAIPatch: Could not locate enact field on '{type.Name}'. Ideological policy voting disabled.\nAvailable bool members:\n{boolMembers}",
                        BellumNotificationColors.Debug);
                }
            }

            if (_enactFieldInfo != null)
            {
                motionWillBeEnforced = (bool)_enactFieldInfo.GetValue(possibleOutcome);
                successfullyExtracted = true;
            }
            else if (_enactPropInfo != null)
            {
                motionWillBeEnforced = (bool)_enactPropInfo.GetValue(possibleOutcome);
                successfullyExtracted = true;
            }

            if (!successfullyExtracted) return;

            var deliberation = Campaign.Current.GetCampaignBehavior<PolicyDeliberationBehavior>();
            int? bribed = deliberation?.GetBribedVote(__instance.Kingdom, __instance.Policy, clan);
            if (bribed.HasValue)
            {
                // Persuasion stores support for the proposed motion, regardless of whether that
                // motion enacts or repeals the policy.
                __result = motionWillBeEnforced ? bribed.Value : -bribed.Value;
                return;
            }

            // Bannerlord's outcome flag means "enforce the proposed motion". For repeal votes an
            // enforced motion makes the policy inactive, so passing that flag directly as
            // "enact" inverted every ideological score and made repeal sponsors oppose their own
            // motions. Toggling the current state only when the motion is enforced gives the
            // actual state each outcome would produce.
            bool policyWillBeActive = motionWillBeEnforced != PolicyDeliberationBehavior.ResolvePolicyAbolish(__instance);

            // Bellum Civile fully owns policy voting once this patch runs. Keeping vanilla residue here
            // would make dialogue previews and actual vote outcomes diverge near close thresholds.
            __result = CalculateSupportScore(__instance, clan, policyWillBeActive, factionManager);
        }
    }
}
