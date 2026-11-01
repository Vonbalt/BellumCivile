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
    private static bool VisualBanner(Clan __instance, ref Banner __result)
    {
        if (!VisualBanners.TryGetValue(__instance, out __result)) return true;
        return false;
    }

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
        VisualBanners[grant.GrantingClan] = nativeBanner; VisualBanners[_player] = playerBanner;
        harmony.Patch(AccessTools.PropertyGetter(typeof(Clan), "Banner"), prefix: new HarmonyMethod(typeof(LordshipTests), nameof(VisualBanner)));
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
            var other = Villages[grant.GrantingClan].Select(v => v.Settlement).First(v => v != grant.Village);
            check(new HierarchyLordshipNodeVM(other, parent, null, null).TitleColor.Equals(neutral),
                "Unheld lordships keep the parent barony's existing hierarchy color");

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
            var plate = Plate(grant.Village, nativeImage);
            BannerImageIdentifierVM Image(object vm)
            {
                bind.Invoke(vm, null);
                return (BannerImageIdentifierVM)AccessTools.Property(type, "Banner").GetValue(vm);
            }
            string ownerCode = nativeBanner.Serialize();
            var image = Image(plate);
            check(image != nativeImage && image.Id == new BannerImageIdentifierVM(playerBanner, true).Id,
                "The native village nameplate displays the player clan's banner for its joining lordship");
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
            VisualBanners[_player] = new Banner(code);
            check(Image(plate).Id == new BannerImageIdentifierVM(VisualBanners[_player], true).Id,
                "Replacing the player's banner object also refreshes the displayed village banner");
            var realm = _player.Kingdom;
            AccessTools.Field(typeof(Clan), "_kingdom").SetValue(_player, null);
            check(ReferenceEquals(Image(plate), nativeImage)
                && new HierarchyLordshipNodeVM(grant.Village, parent, null, null).TitleColor.Equals(neutral),
                "A grant no longer valid across realms restores the native banner and inherited hierarchy color");
            AccessTools.Field(typeof(Clan), "_kingdom").SetValue(_player, realm);
            AccessTools.Field(typeof(LordshipBehavior), "_ready").SetValue(registry, false);
            check(ReferenceEquals(Image(plate), nativeImage), "Loading before lordship readiness cannot replace a native village banner");
            AccessTools.Field(typeof(LordshipBehavior), "_ready").SetValue(registry, true);
            var grants = (List<PersonalLordshipRecord>)AccessTools.Field(typeof(LordshipBehavior), "_grants").GetValue(registry);
            grants.Remove(grant); Call(typeof(LordshipBehavior), "Reindex", registry);
            check(ReferenceEquals(Image(plate), nativeImage), "Revoking or surrendering the grant restores the native banner on the next binding refresh");
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
