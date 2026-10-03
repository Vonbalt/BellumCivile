using HarmonyLib;
using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Encyclopedia;
using TaleWorlds.CampaignSystem.Encyclopedia.Pages;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace BellumCivile.Patches
{
    /// <summary>
    /// Why did I do this file?
    /// To prevent eliminated temporary Rebel Kingdoms from cluttering the in-game Encyclopedia.
    /// </summary>
    [HarmonyPatch(typeof(DefaultEncyclopediaFactionPage), "IsValidEncyclopediaItem")]
    public class EncyclopediaFactionPagePatch
    {
        public static void Postfix(object o, ref bool __result)
        {
            if (!__result || !(o is Kingdom kingdom)) return;

            if (BellumKingdomVisibilityHelper.ShouldHideFromKingdomLists(kingdom))
                __result = false;
        }
    }

    [HarmonyPatch(typeof(DefaultEncyclopediaFactionPage), "InitializeListItems")]
    public class EncyclopediaFactionListPatch
    {
        public static void Postfix(DefaultEncyclopediaFactionPage __instance, ref IEnumerable<EncyclopediaListItem> __result)
        {
            __result = BuildFilteredKingdomList(__instance, __result);
        }

        private static IEnumerable<EncyclopediaListItem> BuildFilteredKingdomList(
            DefaultEncyclopediaFactionPage page,
            IEnumerable<EncyclopediaListItem> source)
        {
            List<EncyclopediaListItem> items = new List<EncyclopediaListItem>();
            List<EncyclopediaListItem> result;

            if (source == null)
            {
                result = BuildFallbackKingdomList(page, "the patched encyclopedia source was null");
            }
            else
            {
                try
                {
                    foreach (EncyclopediaListItem item in source)
                    {
                        if (item.Object == null)
                            continue;

                        if (item.Object is Kingdom kingdom
                            && BellumKingdomVisibilityHelper.ShouldHideFromKingdomLists(kingdom))
                            continue;

                        items.Add(item);
                    }

                    result = items;
                }
                catch (Exception ex)
                {
                    BellumCivileLogger.Log(
                        $"EncyclopediaFactionListPatch: patched faction-list enumeration failed after {items.Count} entries; rebuilding safely from Kingdom.All. {ex}");
                    result = BuildFallbackKingdomList(page, ex.GetType().Name);
                }
            }

            // Keep enumeration lazy. EncyclopediaPage calls InitializeListItems before its
            // identifier table is initialized, so materializing the source in the postfix
            // produces visible entries with empty navigation targets.
            foreach (EncyclopediaListItem item in result)
                yield return item;
        }

        private static List<EncyclopediaListItem> BuildFallbackKingdomList(
            DefaultEncyclopediaFactionPage page,
            string reason)
        {
            List<EncyclopediaListItem> fallback = new List<EncyclopediaListItem>();
            string identifier = ResolveKingdomIdentifier(page);

            foreach (Kingdom kingdom in Kingdom.All)
            {
                if (kingdom == null)
                    continue;

                try
                {
                    string kingdomId = kingdom.StringId;
                    if (kingdom.IsBanditFaction
                        || string.IsNullOrWhiteSpace(kingdomId)
                        || BellumKingdomVisibilityHelper.ShouldHideFromKingdomLists(kingdom))
                    {
                        continue;
                    }

                    Kingdom registeredKingdom = Campaign.Current?.CampaignObjectManager?.Find<Kingdom>(kingdomId);
                    if (registeredKingdom == null)
                    {
                        BellumCivileLogger.Log(
                            $"EncyclopediaFactionListPatch: skipped unregistered kingdom fallback entry; kingdom={kingdomId}.");
                        continue;
                    }

                    string name = registeredKingdom.Name?.ToString();
                    if (string.IsNullOrWhiteSpace(name))
                        name = kingdomId;

                    Kingdom tooltipKingdom = registeredKingdom;
                    fallback.Add(new EncyclopediaListItem(
                        registeredKingdom,
                        name,
                        string.Empty,
                        kingdomId,
                        identifier,
                        true,
                        () => InformationManager.ShowTooltip(typeof(Kingdom), tooltipKingdom)));
                }
                catch (Exception ex)
                {
                    BellumCivileLogger.Log(
                        $"EncyclopediaFactionListPatch: skipped malformed kingdom while rebuilding the list; kingdom={SafeKingdomId(kingdom)}. {ex}");
                }
            }

            BellumCivileLogger.Log(
                $"EncyclopediaFactionListPatch: safe fallback rebuilt {fallback.Count} kingdom entries; reason={reason ?? "unknown"}; identifier={identifier ?? "<null>"}.");
            return fallback;
        }

        private static string ResolveKingdomIdentifier(DefaultEncyclopediaFactionPage page)
        {
            string identifier = string.Empty;

            try
            {
                identifier = page?.GetIdentifier(typeof(Kingdom)) ?? string.Empty;
            }
            catch (Exception ex)
            {
                BellumCivileLogger.Log($"EncyclopediaFactionListPatch: page identifier lookup failed during fallback. {ex}");
            }

            if (!string.IsNullOrWhiteSpace(identifier))
                return identifier;

            try
            {
                identifier = Campaign.Current?.EncyclopediaManager?.GetIdentifier(typeof(Kingdom)) ?? string.Empty;
            }
            catch (Exception ex)
            {
                BellumCivileLogger.Log($"EncyclopediaFactionListPatch: manager identifier lookup failed during fallback. {ex}");
            }

            if (!string.IsNullOrWhiteSpace(identifier))
                return identifier;

            try
            {
                if (Game.Current?.ObjectManager != null && Game.Current.ObjectManager.HasType(typeof(Kingdom)))
                    identifier = Game.Current.ObjectManager.FindRegisteredClassPrefix(typeof(Kingdom));
            }
            catch (Exception ex)
            {
                BellumCivileLogger.Log($"EncyclopediaFactionListPatch: object-manager identifier lookup failed during fallback. {ex}");
            }

            return string.IsNullOrWhiteSpace(identifier) ? typeof(Kingdom).Name : identifier;
        }

        private static string SafeKingdomId(Kingdom kingdom)
        {
            try
            {
                return kingdom?.StringId ?? "<null-id>";
            }
            catch
            {
                return "<unreadable-id>";
            }
        }
    }
}
