using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Election;
using TaleWorlds.Library;

namespace BellumCivile.Patches
{
    [HarmonyPatch(typeof(KingdomElection), "ApplyChosenOutcome")]
    internal static class VotePledgePaymentPatch
    {
        private static readonly ConditionalWeakTable<KingdomElection, Dictionary<Clan, string>> Payments =
            new ConditionalWeakTable<KingdomElection, Dictionary<Clan, string>>();

        internal static string GetPaymentKey(KingdomElection election, Clan clan) =>
            Payments.TryGetValue(election, out var payments) && payments.TryGetValue(clan, out string key) ? key : null;

        private static void Prefix(KingdomElection __instance, KingdomDecision ____decision,
            MBList<DecisionOutcome> ____possibleOutcomes, List<Supporter> ____supporters)
        {
            var payments = new Dictionary<Clan, string>();
            Payments.Remove(__instance);
            Payments.Add(__instance, payments);
            // Outcomes may transfer land, expel clans, and clear promises before native payment.
            foreach (var supporter in ____supporters ?? Enumerable.Empty<Supporter>())
            {
                Clan clan = supporter?.Clan;
                if (clan == null || clan == Clan.PlayerClan
                    || !VotePledgeService.TryGetPledge(____decision, clan, ____possibleOutcomes, out string key, out var pledged)) continue;
                if (pledged?.SupporterList.Any(vote => vote.Clan == clan
                    && vote.SupportWeight >= Supporter.SupportWeights.SlightlyFavor) == true)
                    payments[clan] = key;
                else VotePledgeService.ReportUnfulfilled(clan, ____decision, pledged == null);
            }
        }

        private static Exception Finalizer(KingdomElection __instance, Exception __exception)
        {
            Payments.Remove(__instance);
            return __exception;
        }
    }
}
