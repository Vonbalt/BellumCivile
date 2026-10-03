using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.BarterSystem.Barterables;
using C = BellumCivile.BellumCivileConstants;

namespace BellumCivile.Patches
{
    /// <summary>
    /// Why did I do this file?
    /// To penalize cross-tier marriages by inflating the target clan's perceived value when a
    /// lower-tier suitor proposes. The gap matters: a baron trying to wed a duke's daughter pays
    /// a steep "prove yourself" premium. Marrying a kingdom's dynastic heir carries an additional
    /// flat multiplier, reflecting their political significance and the dynasty's reluctance to
    /// give up their succession line.
    /// </summary>
    [HarmonyPatch(typeof(MarriageBarterable), "GetUnitValueForFaction")]
    public class MarriageDowryPatch
    {
        // What does this method do?
        // Multiplies the target clan's evaluation of the marriage upward when the proposing clan
        // is of lower tier. A higher return value from the target side signals that they demand
        // more compensation to agree to the match. The heir premium stacks on top of any tier
        // penalty and applies regardless of tier direction; dynastic heirs are always expensive.
        [HarmonyPostfix]
        public static void GetUnitValueForFactionPostfix(MarriageBarterable __instance, IFaction faction, ref int __result)
        {
            Hero offered = __instance.ProposingHero;
            Hero suitor  = __instance.HeroBeingProposedTo;

            if (offered?.Clan == null || suitor?.Clan == null) return;

            IFaction offeredClanFaction    = offered.Clan;
            IFaction offeredKingdomFaction = offered.Clan.Kingdom;
            if (faction != offeredClanFaction && faction != offeredKingdomFaction) return;

            float multiplier = 1.0f;

            int tierDiff = offered.Clan.Tier - suitor.Clan.Tier;
            if (tierDiff > 0)
                multiplier = 1.0f + tierDiff;

            if (offered.Clan.Kingdom != null)
            {
                var heirBehavior = Campaign.Current.GetCampaignBehavior<DynasticHeirBehavior>();
                Hero dynasticHeir = heirBehavior?.GetDynasticHeir(offered.Clan.Kingdom);
                if (dynasticHeir == offered)
                    multiplier += C.DowryHeirPremium;
            }

            if (multiplier > 1.0f)
                __result = (int)(__result * multiplier);
        }
    }
}
