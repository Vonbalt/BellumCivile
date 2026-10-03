using HarmonyLib;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Party.PartyComponents;
using TaleWorlds.CampaignSystem.Settlements;

namespace BellumCivile.Patches
{
    /// <summary>
    /// Why did I do this file?
    /// To stop vanilla hideout-home bookkeeping from crashing when BellumCivile's temporary proxy-war highwaymen
    /// switch hideouts during normal AI movement. These sabotage parties are disposable raiders, not part of the
    /// persistent bandit-hideout ecosystem, so the vanilla tracking can be skipped safely.
    /// </summary>
    [HarmonyPatch(typeof(BanditSpawnCampaignBehavior), "OnHomeHideoutChanged")]
    public class ProxyWarHideoutPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(BanditPartyComponent banditPartyComponent, Hideout oldHomeHideout)
        {
            MobileParty party = banditPartyComponent?.MobileParty;
            if (party?.StringId != null && party.StringId.StartsWith(Behaviors.ProxyWarBehavior.HighwaymenPartyIdPrefix))
            {
                return false;
            }

            return true;
        }
    }
}
