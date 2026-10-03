using TaleWorlds.CampaignSystem;

namespace BellumCivile
{
    internal static class KingdomCreationSafetyHelper
    {
        internal static bool IsValidFounder(Clan clan)
        {
            return clan != null && !clan.IsEliminated
                && clan.Leader != null && !clan.Leader.IsDead;
        }

        internal static Kingdom CreateKingdom(string id, Clan founder)
        {
            if (!IsValidFounder(founder))
            {
                BellumCivileLogger.Log($"Refused kingdom creation; kingdom={id}; founder={founder?.StringId ?? "null"}; no living clan leader.");
                return null;
            }

            Kingdom kingdom = Kingdom.CreateKingdom(id);
            // InitializeKingdom and clan transfers can invoke callbacks. Seed the intended
            // ruler first, as vanilla's CreateKingdom action does before clan membership changes.
            kingdom.RulingClan = founder;
            return kingdom;
        }

        internal static bool PrepareRuler(Kingdom kingdom, Clan intendedRuler, string context)
        {
            if (kingdom == null || kingdom.IsEliminated || !IsValidFounder(intendedRuler))
            {
                BellumCivileLogger.Log($"Refused kingdom transition; kingdom={kingdom?.StringId ?? "null"}; intended_ruler={intendedRuler?.StringId ?? "null"}; context={context}; invalid kingdom or ruler.");
                return false;
            }

            kingdom.RulingClan = intendedRuler;
            return true;
        }
    }
}
