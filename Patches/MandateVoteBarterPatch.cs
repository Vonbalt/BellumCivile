using System.Linq;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.BarterSystem;

namespace BellumCivile.Patches
{
    [HarmonyPatch(typeof(BarterManager), "ApplyAndFinalizePlayerBarter")]
    internal static class MandateVoteBarterPatch
    {
        private static bool Prefix(BarterManager __instance, Hero offererHero, Hero otherHero, BarterData barterData)
        {
            var votes = barterData.GetBarterables().OfType<MandateVoteBribeBarterable>().ToList();
            if (votes.Count == 0) return true;
            if (votes.Count == 1 && votes[0].IsOffered && votes[0].CurrentAmount > 0
                && __instance.IsOfferAcceptable(barterData, otherHero, barterData.OtherParty)
                && votes[0].TrySecure(offererHero, otherHero)) return true;
            __instance.CancelAndFinalizePlayerBarter(offererHero, otherHero, barterData);
            return false;
        }
    }
}
