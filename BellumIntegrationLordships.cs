using System;
using System.Collections.Generic;
using BellumCivile.Behaviors;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;

namespace BellumCivile
{
    public static partial class BellumIntegration
    {
        /// <summary>Whether this village is the active player character's valid personal lordship.</summary>
        public static bool IsPlayerNominalFief(Settlement village)
        {
            BellumIntegrationBehavior.RequireCampaignThread();
            return village != null && GetPlayerNominalFief() == village;
        }

        /// <summary>Copies current personal lordship IDs; excludes native holdings and household grants to others.</summary>
        public static IReadOnlyList<string> GetPlayerNominalFiefIds()
        {
            BellumIntegrationBehavior.RequireCampaignThread();
            var village = GetPlayerNominalFief();
            return village == null ? Array.Empty<string>() : new[] { village.StringId };
        }

        private static Settlement GetPlayerNominalFief()
        {
            var registry = LordshipBehavior.Instance;
            var village = registry?.Ready == true ? registry.GetGrant(Hero.MainHero)?.Village : null;
            return registry?.IsPlayerLordship(village) == true && !string.IsNullOrEmpty(village.StringId) ? village : null;
        }
    }
}
