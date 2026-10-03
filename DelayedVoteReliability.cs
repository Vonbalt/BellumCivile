using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Election;

namespace BellumCivile
{
    internal static class DelayedVoteReliability
    {
        internal const int MaxFailedAttempts = 7;
        internal const float MaxPendingAgeDays = 60f;
        internal const float MaxLiveDecisionAgeDays = 30f;

        internal static float CurrentDay => Campaign.Current == null
            ? 0f
            : (float)CampaignTime.Now.ToDays;

        internal static void EnsureLifecycle(
            string key,
            CampaignTime dueDate,
            IDictionary<string, float> createdDays,
            IDictionary<string, int> failedAttempts)
        {
            if (string.IsNullOrEmpty(key))
                return;

            if (!createdDays.ContainsKey(key))
            {
                float inferredCreatedDay = Math.Max(
                    0f,
                    (float)dueDate.ToDays - Math.Max(0, BellumCivileOptions.PoliticalDeliberationDays));
                createdDays[key] = inferredCreatedDay;
            }

            if (!failedAttempts.ContainsKey(key))
                failedAttempts[key] = 0;
        }

        internal static int RegisterFailure(string key, IDictionary<string, int> failedAttempts)
        {
            if (string.IsNullOrEmpty(key))
                return MaxFailedAttempts;

            int attempts = failedAttempts.TryGetValue(key, out int stored) ? stored + 1 : 1;
            failedAttempts[key] = attempts;
            return attempts;
        }

        internal static bool IsPendingExpired(
            string key,
            IDictionary<string, float> createdDays,
            IDictionary<string, int> failedAttempts)
        {
            float age = GetPendingAgeDays(key, createdDays);
            int failures = failedAttempts.TryGetValue(key, out int stored) ? stored : 0;
            return age >= MaxPendingAgeDays || failures >= MaxFailedAttempts;
        }

        internal static float GetPendingAgeDays(string key, IDictionary<string, float> createdDays)
        {
            if (string.IsNullOrEmpty(key) || !createdDays.TryGetValue(key, out float createdDay))
                return 0f;

            return Math.Max(0f, CurrentDay - createdDay);
        }

        internal static int GetFailureCount(string key, IDictionary<string, int> failedAttempts)
        {
            return !string.IsNullOrEmpty(key) && failedAttempts.TryGetValue(key, out int stored)
                ? stored
                : 0;
        }

        internal static float GetDecisionAgeDays(KingdomDecision decision)
        {
            if (decision == null)
                return 0f;

            float triggerDay = (float)decision.TriggerTime.ToDays;
            if (triggerDay <= 0f)
                return 0f;

            return Math.Max(0f, CurrentDay - triggerDay);
        }

        internal static bool IsLiveDecisionStale(KingdomDecision decision)
        {
            return GetDecisionAgeDays(decision) >= MaxLiveDecisionAgeDays;
        }
    }
}
