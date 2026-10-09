using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;

namespace BellumCivile.Patches
{
    [HarmonyPatch(typeof(Clan), "UpdateBannerColorsAccordingToKingdom")]
    internal static class CustomClanBannerSyncPatch
    {
        internal sealed class Snapshot
        {
            public Banner House;
            public Kingdom Realm;
            public Banner RealmBanner;
        }

        // Capture before native synchronization and other mods' recoloring prefixes.
        [HarmonyPrefix, HarmonyPriority(Priority.First)]
        private static void Prefix(Clan __instance, out Snapshot __state)
        {
            __state = null;
            if (!KingdomVisualHelper.ShouldPreserveClanBanner(__instance)
                || (PocBannerCompatibility.TryGetPolicy(__instance, out _, out bool controlsColors) && controlsColors))
                return;
            Kingdom realm = __instance.Kingdom;
            bool rulingHouse = realm?.RulingClan == __instance;
            __state = new Snapshot
            {
                House = new Banner(__instance.ClanOriginalBanner ?? __instance.Banner),
                Realm = rulingHouse ? realm : null,
                RealmBanner = rulingHouse && realm.Banner != null ? new Banner(realm.Banner) : null
            };
        }

        [HarmonyPostfix, HarmonyPriority(Priority.Last)]
        private static void Postfix(Clan __instance, Snapshot __state)
        {
            if (__state == null) return;
            __instance.Banner = __state.House;
            // A ruling clan's Banner getter proxies the kingdom banner; these are separate artworks.
            if (__state.RealmBanner != null && __instance.Kingdom == __state.Realm && __state.Realm.RulingClan == __instance)
                __state.Realm.Banner = __state.RealmBanner;
        }
    }
}
