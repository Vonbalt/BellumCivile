using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using BellumCivile;
using BellumCivile.Behaviors;
using BellumCivile.UI.VanillaTabs.Kingdoms.Hierarchy;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Core.ViewModelCollection.ImageIdentifiers;
using TaleWorlds.Library;

internal static partial class LordshipTests
{
    private static readonly Dictionary<Clan, Banner> VisualBanners = new Dictionary<Clan, Banner>();
    private static Banner _visualRoyalBanner;
    private static bool VisualBanner(Clan __instance, ref Banner __result)
    {
        if (!VisualBanners.TryGetValue(__instance, out __result)) return true;
        return false;
    }
    private static bool VisualRoyalBanner(Kingdom __instance, ref Banner __result)
    {
        if (!VisualBanners.ContainsKey(__instance.RulingClan)) return true;
        __result = _visualRoyalBanner;
        return false;
    }
    private static bool VisualVillageOwner(Settlement __instance, ref Clan __result)
    {
        if (__instance.Village == null || !Bounds.TryGetValue(__instance.Village, out var parent)) return true;
        Owners.TryGetValue(parent, out __result);
        return false;
    }
    private static bool VisualMapFaction(Settlement __instance, ref IFaction __result)
    { __result = __instance.OwnerClan?.MapFaction; return false; }
    private static bool SkipOverlayRefresh() => false;

    private static void CheckLordshipVisuals(Action<bool, string> check, LordshipBehavior registry, PersonalLordshipRecord grant)
    {
        var harmony = new Harmony("bellum.tests.lordship.visuals");
        var patch = Type("Patches.LordshipMapBannerPatch");
        BannerManager.Initialize();
        var colors = (Dictionary<int, BannerColor>)AccessTools.Field(typeof(BannerManager), "_colorPalette").GetValue(BannerManager.Instance);
        for (int i = 0; i < 5; i++) colors[i] = new BannerColor(0xff000000u | (uint)(i * 65793), true, true);
        const string code = "11.1.1.1500.1500.764.764.0.0.0.505.2.2.300.400.764.764.0.0.0";
        var nativeBanner = new Banner(code); var playerBanner = new Banner(code);
        playerBanner.ChangePrimaryColor(BannerManager.GetColor(3));
        _visualRoyalBanner = new Banner(code);
        _visualRoyalBanner.ChangePrimaryColor(BannerManager.GetColor(0));
        VisualBanners[grant.GrantingClan] = nativeBanner; VisualBanners[_player] = playerBanner;
        harmony.Patch(AccessTools.PropertyGetter(typeof(Clan), "Banner"), prefix: new HarmonyMethod(typeof(LordshipTests), nameof(VisualBanner)));
        harmony.Patch(AccessTools.PropertyGetter(typeof(Kingdom), "Banner"), prefix: new HarmonyMethod(typeof(LordshipTests), nameof(VisualRoyalBanner)));
        harmony.Patch(AccessTools.PropertyGetter(typeof(Settlement), "OwnerClan"), prefix:
            new HarmonyMethod(typeof(LordshipTests), nameof(VisualVillageOwner)) { priority = Priority.First });
        harmony.Patch(AccessTools.PropertyGetter(typeof(Settlement), "MapFaction"), prefix: new HarmonyMethod(typeof(LordshipTests), nameof(VisualMapFaction)));
        try
        {
            var parent = Blank<HierarchyTitleNodeVM>();
            Color neutral = Color.ConvertStringToColor("#F1D8A4FF");
            AccessTools.Field(typeof(HierarchyTitleNodeVM), "<TitleColor>k__BackingField").SetValue(parent, neutral);
            AccessTools.Field(typeof(HierarchyTitleNodeVM), "<Name>k__BackingField").SetValue(parent, "Parent barony");
            var node = new HierarchyLordshipNodeVM(grant.Village, parent, null, null);
            var green = (Color)AccessTools.Field(typeof(HierarchyTitleNodeVM), "PlayerHeldColor").GetValue(null);
            check(node.TitleColor.Equals(green) && !parent.TitleColor.Equals(green),
                "A player-held joining lordship is green even when its parent belongs to another house");
            check(node.BannerVisual.Id == new BannerImageIdentifierVM(playerBanner, true).Id,
                "The hierarchy uses the player's banner for the granted lordship instead of its parent owner's royal banner");
            var other = Villages[grant.GrantingClan].Select(v => v.Settlement).First(v => v != grant.Village);
            check(new HierarchyLordshipNodeVM(other, parent, null, null).TitleColor.Equals(neutral),
                "Unheld lordships keep the parent barony's existing hierarchy color");
            check(new HierarchyLordshipNodeVM(other, parent, null, null).BannerVisual.Id == new BannerImageIdentifierVM(nativeBanner, true).Id,
                "Unheld lordships retain the parent house's banner");

            Assembly.Load("SandBox.ViewModelCollection");
            Call(patch, "Initialize", null); Call(patch, "Initialize", null);
            var type = System.Type.GetType("SandBox.ViewModelCollection.Nameplate.SettlementNameplateVM, SandBox.ViewModelCollection", true);
            var bind = AccessTools.Method(type, "RefreshBindValues");
            check(Harmony.GetPatchInfo(bind).Transpilers.Count(p => p.PatchMethod.DeclaringType == patch) == 1,
                "The village banner binding patch installs once in the actual native nameplate VM");
            object Plate(Settlement settlement, BannerImageIdentifierVM native)
            {
                var plate = FormatterServices.GetUninitializedObject(type);
                AccessTools.Field(type, "<Settlement>k__BackingField").SetValue(plate, settlement);
                AccessTools.Field(type, "_bindBanner").SetValue(plate, native);
                foreach (string field in new[] { "_settlementNotifications", "_settlementEvents" })
                {
                    var f = AccessTools.Field(type, field);
                    f.SetValue(plate, FormatterServices.GetUninitializedObject(f.FieldType));
                }
                return plate;
            }
            var nativeImage = new BannerImageIdentifierVM(nativeBanner, true);
            var royalImage = new BannerImageIdentifierVM(_visualRoyalBanner, true);
            var plate = Plate(grant.Village, nativeImage);
            var overlayType = AccessTools.TypeByName("TaleWorlds.CampaignSystem.ViewModelCollection.GameMenu.Overlay.SettlementMenuOverlayVM");
            object Overlay(Settlement settlement)
            {
                var vm = FormatterServices.GetUninitializedObject(overlayType);
                AccessTools.Field(overlayType, "_settlement").SetValue(vm, settlement);
                return vm;
            }
            BannerImageIdentifierVM MenuBanner(object vm) => (BannerImageIdentifierVM)AccessTools.Property(overlayType, "SettlementOwnerBanner").GetValue(vm);
            BannerImageIdentifierVM MenuImage(object vm)
            {
                AccessTools.Method(overlayType, "UpdateSettlementOwnerBanner").Invoke(vm, null);
                return MenuBanner(vm);
            }
            var menu = Overlay(grant.Village);
            BannerImageIdentifierVM Image(object vm)
            {
                bind.Invoke(vm, null);
                return (BannerImageIdentifierVM)AccessTools.Property(type, "Banner").GetValue(vm);
            }
            string ownerCode = nativeBanner.Serialize();
            var image = Image(plate);
            check(image != nativeImage && image.Id == new BannerImageIdentifierVM(playerBanner, true).Id,
                "The native village nameplate displays the player clan's banner for its joining lordship");
            check(MenuImage(menu).Id == image.Id && grant.GrantingClan.Kingdom.RulingClan == grant.GrantingClan,
                "The real menu banner update displays the player's banner even when native code selects the ruling house's kingdom banner");
            check(royalImage.Id != nativeImage.Id && royalImage.Id != image.Id
                && MenuImage(Overlay(other)).Id == royalImage.Id && MenuImage(Overlay(grant.Parent)).Id == royalImage.Id,
                "Other village menus and the parent castle retain their native royal banner");
            harmony.Patch(AccessTools.Method(overlayType, "Refresh"),
                prefix: new HarmonyMethod(typeof(LordshipTests), nameof(SkipOverlayRefresh)));
            void RefreshMenu() => AccessTools.Method(overlayType, "Refresh").Invoke(menu, null);
            RefreshMenu();
            check(MenuBanner(menu).Id == image.Id,
                "Ordinary menu refreshes reapply the personal grant banner through the native owner-banner update");
            check(ReferenceEquals(AccessTools.Field(type, "_bindBanner").GetValue(plate), nativeImage)
                && grant.Parent.OwnerClan == grant.GrantingClan && nativeBanner.Serialize() == ownerCode,
                "Visual substitution leaves the native banner cache, parent ownership and granting house's banner untouched");
            check(ReferenceEquals(Image(plate), image), "Unchanged frames reuse the same banner image without alternating UI values");
            check(ReferenceEquals(Image(Plate(other, nativeImage)), nativeImage)
                && ReferenceEquals(Image(Plate(grant.Parent, nativeImage)), nativeImage),
                "Other villages and the parent castle keep their native banners");
            playerBanner.ChangePrimaryColor(BannerManager.GetColor(4));
            var edited = Image(plate);
            check(!ReferenceEquals(image, edited) && edited.Id == new BannerImageIdentifierVM(playerBanner, true).Id,
                "Editing the player's banner refreshes the lordship display through the native banner version");
            check(MenuImage(menu).Id == edited.Id && new HierarchyLordshipNodeVM(grant.Village, parent, null, null).BannerVisual.Id == edited.Id,
                "The menu and rebuilt hierarchy also follow edits to the player's banner");
            VisualBanners[_player] = new Banner(code);
            check(Image(plate).Id == new BannerImageIdentifierVM(VisualBanners[_player], true).Id,
                "Replacing the player's banner object also refreshes the displayed village banner");
            var realm = _player.Kingdom;
            AccessTools.Field(typeof(Clan), "_kingdom").SetValue(_player, null);
            check(ReferenceEquals(Image(plate), nativeImage)
                && new HierarchyLordshipNodeVM(grant.Village, parent, null, null).TitleColor.Equals(neutral),
                "A grant no longer valid across realms restores the native banner and inherited hierarchy color");
            RefreshMenu();
            check(MenuBanner(menu).Id == royalImage.Id
                && new HierarchyLordshipNodeVM(grant.Village, parent, null, null).BannerVisual.Id == nativeImage.Id,
                "An invalid grant restores the royal banner in an already-open menu and in the hierarchy");
            AccessTools.Field(typeof(Clan), "_kingdom").SetValue(_player, realm);
            AccessTools.Field(typeof(LordshipBehavior), "_ready").SetValue(registry, false);
            check(ReferenceEquals(Image(plate), nativeImage), "Loading before lordship readiness cannot replace a native village banner");
            AccessTools.Field(typeof(LordshipBehavior), "_ready").SetValue(registry, true);
            var grants = (List<PersonalLordshipRecord>)AccessTools.Field(typeof(LordshipBehavior), "_grants").GetValue(registry);
            grants.Remove(grant); Call(typeof(LordshipBehavior), "Reindex", registry);
            check(ReferenceEquals(Image(plate), nativeImage), "Revoking or surrendering the grant restores the native banner on the next binding refresh");
            RefreshMenu();
            check(MenuBanner(menu).Id == royalImage.Id,
                "Revoking the grant restores the real native menu banner on refresh");
            grants.Add(grant); Call(typeof(LordshipBehavior), "Reindex", registry);
            check(Image(plate).Id == new BannerImageIdentifierVM(VisualBanners[_player], true).Id,
                "An active grant becomes visible again after the runtime indexes are rebuilt");
            foreach (var method in new[] { AccessTools.PropertyGetter(typeof(Settlement), "Banner"),
                AccessTools.PropertyGetter(typeof(Settlement), "OwnerClan"), AccessTools.PropertyGetter(typeof(Settlement), "MapFaction") })
                check(Harmony.GetPatchInfo(method)?.Owners.Contains("com.bellumcivile.lordship.mapbanner") != true,
                    "The visual patch does not change global settlement data: " + method.Name);
        }
        finally
        {
            harmony.UnpatchAll(harmony.Id);
            new Harmony("com.bellumcivile.lordship.mapbanner").UnpatchAll("com.bellumcivile.lordship.mapbanner");
            AccessTools.Field(patch, "_installed").SetValue(null, false);
            VisualBanners.Clear();
        }
    }
}
