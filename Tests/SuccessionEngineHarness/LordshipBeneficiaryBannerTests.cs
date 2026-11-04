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

internal static partial class AsylumVisitTests
{
    private static readonly Dictionary<Clan, Banner> Banners = new Dictionary<Clan, Banner>();
    private static Banner _royalBanner;
    private static bool BannerOf(Clan __instance, ref Banner __result) { Banners.TryGetValue(__instance, out __result); return false; }
    private static bool RoyalBanner(ref Banner __result) { __result = _royalBanner; return false; }
    private static bool VisualFaction(Settlement __instance, ref IFaction __result)
    { var owner = __instance.OwnerClan; __result = (IFaction)owner?.Kingdom ?? owner; return false; }

    private static void CheckBeneficiaryBanners(Action<bool, string> check, LordshipBehavior registry, PersonalLordshipRecord outgoing)
    {
        var h = new Harmony("bellum.tests.lordship_beneficiary_banners");
        var patch = typeof(LordshipBehavior).Assembly.GetType("BellumCivile.Patches.LordshipMapBannerPatch", true);
        const string code = "11.1.1.1500.1500.764.764.0.0.0.505.2.2.300.400.764.764.0.0.0";
        BannerManager.Initialize();
        var colors = (Dictionary<int, BannerColor>)AccessTools.Field(typeof(BannerManager), "_colorPalette").GetValue(BannerManager.Instance);
        for (int i = 0; i < 6; i++) colors[i] = new BannerColor(0xff000000u | (uint)(i * 65793), true, true);
        Banner NewBanner(int color) { var banner = new Banner(code); banner.ChangePrimaryColor(BannerManager.GetColor(color)); return banner; }
        Banners[_player] = NewBanner(1); Banners[_exile] = NewBanner(2); Banners[_vassal] = NewBanner(3);
        _royalBanner = NewBanner(0);
        h.Patch(AccessTools.PropertyGetter(typeof(Clan), "Banner"), prefix: new HarmonyMethod(typeof(AsylumVisitTests), nameof(BannerOf)));
        h.Patch(AccessTools.PropertyGetter(typeof(Kingdom), "Banner"), prefix: new HarmonyMethod(typeof(AsylumVisitTests), nameof(RoyalBanner)));
        h.Patch(AccessTools.PropertyGetter(typeof(Settlement), "MapFaction"), prefix: new HarmonyMethod(typeof(AsylumVisitTests), nameof(VisualFaction)));
        Settlement Village(string id, Clan source)
        {
            var parent = Blank<Settlement>(); parent.StringId = id + "_parent"; Owners[parent] = source;
            AccessTools.Field(typeof(Settlement), "Town").SetValue(parent, Blank<Town>());
            var village = Blank<Settlement>(); village.StringId = id; Owners[village] = source;
            var component = Blank<Village>(); AccessTools.Field(typeof(Settlement), "Village").SetValue(village, component);
            AccessTools.Field(typeof(Village), "_bound").SetValue(component, parent);
            return village;
        }
        var incomingVillage = Village("incoming_player_village", _vassal);
        var vacant = Village("vacant_village", _player);
        Owners[outgoing.Village] = _player;
        var grants = (List<PersonalLordshipRecord>)AccessTools.Field(typeof(LordshipBehavior), "_grants").GetValue(registry);
        var incoming = new PersonalLordshipRecord { Village = incomingVillage, Parent = incomingVillage.Village.Bound,
            GrantingClan = _vassal, BeneficiaryClan = _player, Holder = _ruler, Grantor = Leaders[_vassal], IsVassalGrant = true };
        grants.Add(incoming); Call(registry, "Reindex");
        string Id(Banner banner) => new BannerImageIdentifierVM(banner, true).Id;
        BannerImageIdentifierVM Selected(Settlement village) => (BannerImageIdentifierVM)Call(patch, "GetBeneficiaryBannerImage", village);
        var parentNode = Blank<HierarchyTitleNodeVM>(); var neutral = Color.ConvertStringToColor("#F1D8A4FF");
        AccessTools.Field(typeof(HierarchyTitleNodeVM), "<TitleColor>k__BackingField").SetValue(parentNode, neutral);
        AccessTools.Field(typeof(HierarchyTitleNodeVM), "<Name>k__BackingField").SetValue(parentNode, "Parent barony");
        HierarchyLordshipNodeVM Node(Settlement village) => new HierarchyLordshipNodeVM(village, parentNode, null, null);
        try
        {
            check(outgoing.IsValid && incoming.IsValid, "Incoming and outgoing personal vassal grants can coexist");
            check(Selected(outgoing.Village)?.Id == Id(Banners[_exile]) && Selected(incomingVillage)?.Id == Id(Banners[_player]),
                "The display selector uses each grant's actual beneficiary clan");
            check(Node(outgoing.Village).BannerVisual.Id == Id(Banners[_exile])
                && Node(incomingVillage).BannerVisual.Id == Id(Banners[_player]), "The hierarchy displays both beneficiary banners");
            var green = (Color)AccessTools.Field(typeof(HierarchyTitleNodeVM), "PlayerHeldColor").GetValue(null);
            check(Node(outgoing.Village).TitleColor.Equals(neutral) && Node(incomingVillage).TitleColor.Equals(green),
                "Only personally player-held lordships receive the player-held title color");
            check(Selected(vacant) == null && Selected(outgoing.Parent) == null,
                "Vacant villages and parent baronies do not receive beneficiary overrides");

            Assembly.Load("SandBox.ViewModelCollection"); Call(patch, "Initialize"); Call(patch, "Initialize");
            var nameplateType = AccessTools.TypeByName("SandBox.ViewModelCollection.Nameplate.SettlementNameplateVM");
            var overlayType = AccessTools.TypeByName("TaleWorlds.CampaignSystem.ViewModelCollection.GameMenu.Overlay.SettlementMenuOverlayVM");
            var bind = AccessTools.Method(nameplateType, "RefreshBindValues");
            check(Harmony.GetPatchInfo(bind).Transpilers.Count(p => p.PatchMethod.DeclaringType == patch) == 1,
                "The generalized banner hook installs once in the real nameplate binding");
            object Plate(Settlement village, BannerImageIdentifierVM native)
            {
                var vm = FormatterServices.GetUninitializedObject(nameplateType);
                AccessTools.Field(nameplateType, "<Settlement>k__BackingField").SetValue(vm, village);
                AccessTools.Field(nameplateType, "_bindBanner").SetValue(vm, native);
                foreach (string name in new[] { "_settlementNotifications", "_settlementEvents" })
                {
                    var field = AccessTools.Field(nameplateType, name);
                    field.SetValue(vm, FormatterServices.GetUninitializedObject(field.FieldType));
                }
                return vm;
            }
            object Overlay(Settlement village)
            {
                var vm = FormatterServices.GetUninitializedObject(overlayType);
                AccessTools.Field(overlayType, "_settlement").SetValue(vm, village); return vm;
            }
            BannerImageIdentifierVM Image(object vm)
            {
                bind.Invoke(vm, null); return (BannerImageIdentifierVM)AccessTools.Property(nameplateType, "Banner").GetValue(vm);
            }
            BannerImageIdentifierVM Menu(object vm)
            {
                AccessTools.Method(overlayType, "UpdateSettlementOwnerBanner").Invoke(vm, null);
                return (BannerImageIdentifierVM)AccessTools.Property(overlayType, "SettlementOwnerBanner").GetValue(vm);
            }
            var royalImage = new BannerImageIdentifierVM(_royalBanner, true);
            var nativeVassalImage = new BannerImageIdentifierVM(Banners[_vassal], true);
            var outgoingPlate = Plate(outgoing.Village, royalImage);
            var incomingPlate = Plate(incomingVillage, nativeVassalImage);
            var outgoingMenu = Overlay(outgoing.Village); var incomingMenu = Overlay(incomingVillage);
            string royalCode = _royalBanner.Serialize(), playerCode = Banners[_player].Serialize();
            var outgoingImage = Image(outgoingPlate); var incomingImage = Image(incomingPlate);
            check(outgoingImage.Id == Id(Banners[_exile]) && incomingImage.Id == Id(Banners[_player]),
                "Native campaign nameplates display outgoing NPC and incoming player grants correctly");
            check(Menu(outgoingMenu).Id == outgoingImage.Id && Menu(incomingMenu).Id == incomingImage.Id,
                "Native village menus display beneficiary banners even above the source ruler's royal banner");
            check(Menu(Overlay(vacant)).Id == royalImage.Id && Menu(Overlay(outgoing.Parent)).Id == royalImage.Id,
                "Ungranted villages and parent castles retain their native royal banner");
            for (int i = 0; i < 4; i++)
                check(ReferenceEquals(Image(outgoingPlate), outgoingImage) && ReferenceEquals(Image(incomingPlate), incomingImage),
                    "Alternating beneficiary houses reuse their own cached image: " + i);
            check(ReferenceEquals(AccessTools.Field(nameplateType, "_bindBanner").GetValue(outgoingPlate), royalImage)
                && ReferenceEquals(AccessTools.Field(nameplateType, "_bindBanner").GetValue(incomingPlate), nativeVassalImage),
                "Neither native owner-banner cache is overwritten");
            check(_royalBanner.Serialize() == royalCode && Banners[_player].Serialize() == playerCode
                && outgoing.Parent.OwnerClan == _player, "Banner presentation changes no source banners or settlement ownership");
            Banners[_exile].ChangePrimaryColor(BannerManager.GetColor(4)); var edited = Image(outgoingPlate);
            check(!ReferenceEquals(edited, outgoingImage) && edited.Id == Id(Banners[_exile])
                && ReferenceEquals(Image(incomingPlate), incomingImage), "Editing one beneficiary banner refreshes only that clan's cached image");
            check(Menu(outgoingMenu).Id == edited.Id && Node(outgoing.Village).BannerVisual.Id == edited.Id,
                "The menu and hierarchy follow edits to an NPC beneficiary banner");
            Banners[_exile] = NewBanner(5);
            check(Image(outgoingPlate).Id == Id(Banners[_exile]), "Replacing a beneficiary banner object also refreshes its village display");
            var replacement = Banners[_exile]; Banners[_exile] = null;
            check(ReferenceEquals(Image(outgoingPlate), royalImage), "A beneficiary without a banner safely falls back to the native image");
            Banners[_exile] = replacement;
            _block = "dead";
            check(ReferenceEquals(Image(outgoingPlate), royalImage) && Menu(outgoingMenu).Id == royalImage.Id,
                "The holder's death restores native banners without requiring a new grant"); _block = null;
            Realms[_exile] = Blank<Kingdom>();
            check(ReferenceEquals(Image(outgoingPlate), royalImage) && Menu(outgoingMenu).Id == royalImage.Id,
                "The houses separating realms restores the native campaign and village-menu banners"); Realms[_exile] = _realm;
            Owners[outgoing.Parent] = Owners[outgoing.Village] = _vassal;
            check(Selected(outgoing.Village) == null && Menu(outgoingMenu).Id == nativeVassalImage.Id
                && Node(outgoing.Village).BannerVisual.Id == nativeVassalImage.Id,
                "Losing the parent barony removes the override and follows the new native owner");
            Owners[outgoing.Parent] = Owners[outgoing.Village] = _player;
            AccessTools.Field(typeof(LordshipBehavior), "_ready").SetValue(registry, false);
            check(Selected(outgoing.Village) == null && Selected(incomingVillage) == null,
                "Loading before registry readiness leaves both types of village banner untouched");
            AccessTools.Field(typeof(LordshipBehavior), "_ready").SetValue(registry, true);
            grants.Remove(outgoing); Call(registry, "Reindex");
            check(ReferenceEquals(Image(outgoingPlate), royalImage) && Menu(outgoingMenu).Id == royalImage.Id
                && Node(outgoing.Village).BannerVisual.Id == Id(Banners[_player]),
                "Revocation restores each surface's native owner banner");
            check(ReferenceEquals(Image(incomingPlate), incomingImage), "Revoking an NPC grant leaves the player's separate grant display intact");
            foreach (var getter in new[] { AccessTools.PropertyGetter(typeof(Settlement), "Banner"),
                AccessTools.PropertyGetter(typeof(Settlement), "OwnerClan"), AccessTools.PropertyGetter(typeof(Settlement), "MapFaction") })
                check(Harmony.GetPatchInfo(getter)?.Owners.Contains("com.bellumcivile.lordship.mapbanner") != true,
                    "The visual hook does not modify global settlement data: " + getter.Name);
        }
        finally
        {
            h.UnpatchAll(h.Id);
            new Harmony("com.bellumcivile.lordship.mapbanner").UnpatchAll("com.bellumcivile.lordship.mapbanner");
            AccessTools.Field(patch, "_installed").SetValue(null, false); Banners.Clear();
        }
    }
}
