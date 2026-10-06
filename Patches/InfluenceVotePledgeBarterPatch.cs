using System.Linq;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.BarterSystem;

namespace BellumCivile.Patches
{
    [HarmonyPatch(typeof(BarterManager), "ApplyAndFinalizePlayerBarter")]
    internal static class InfluenceVotePledgeBarterPatch
    {
        private static bool Prefix(BarterManager __instance, Hero offererHero, Hero otherHero, BarterData barterData)
        {
            var promises = barterData.GetBarterables().Where(item => item is IInfluenceVotePledgeBarterable).ToList();
            if (promises.Count == 0) return true;
            if (promises.Count == 1 && promises[0].IsOffered && promises[0].CurrentAmount > 0
                && __instance.IsOfferAcceptable(barterData, otherHero, barterData.OtherParty)
                && ((IInfluenceVotePledgeBarterable)promises[0]).TrySecure(offererHero, otherHero)) return true;
            __instance.CancelAndFinalizePlayerBarter(offererHero, otherHero, barterData);
            return false;
        }
    }
}
