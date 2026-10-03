using System.Linq;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Election;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace BellumCivile.Patches
{
    [HarmonyPatch(typeof(KingdomElection), "GetAiChoice")]
    internal static class CourtPolicyOverridePatch
    {
        private static bool Prefix(KingdomElection __instance, KingdomDecision ____decision, Clan ____chooser,
            MBReadOnlyList<DecisionOutcome> possibleOutcomes, ref DecisionOutcome __result)
        {
            if (!(____decision is KingdomPolicyDecision) || ____chooser == null || ____chooser == Clan.PlayerClan) return true;
            __instance.DetermineOfficialSupport();
            var popular = possibleOutcomes.OrderByDescending(o => o.TotalSupportPoints).FirstOrDefault();
            var preferred = possibleOutcomes.OrderByDescending(o => ____decision.DetermineSupport(____chooser, o)).FirstOrDefault();
            __result = popular;
            if (popular == null || preferred == popular || !____decision.IsKingsVoteAllowed) return false;
            int cost = Campaign.Current.Models.ClanPoliticsModel.GetInfluenceRequiredToOverrideKingdomDecision(popular, preferred, ____decision);
            int commitment = possibleOutcomes.SelectMany(o => o.SupporterList).Where(s => s.Clan == ____chooser)
                .Select(s => ____decision.GetInfluenceCostOfSupport(____chooser, s.SupportWeight)).DefaultIfEmpty(0).Max();
            float gap = MathF.Min(____chooser.Influence, ____decision.DetermineSupport(____chooser, preferred) - ____decision.DetermineSupport(____chooser, popular));
            if (gap > 20 + cost && NpcInfluenceBudgetService.CanAfford(____chooser, cost + commitment, NpcInfluenceExpenseKind.CouncilCommitment)
                && MBRandom.RandomFloat > (20 + cost) / gap) __result = preferred;
            return false;
        }
    }
}
