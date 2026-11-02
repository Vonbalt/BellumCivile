using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Emit;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.ViewModelCollection.GameMenu.Overlay;
using TaleWorlds.Core;
using TaleWorlds.Core.ViewModelCollection.ImageIdentifiers;

namespace BellumCivile.Patches
{
    internal static class LordshipMapBannerPatch
    {
        private static bool _installed;
        private static bool _unavailable;
        private static Func<object, Settlement> _settlement;
        private static Action<SettlementMenuOverlayVM> _refreshMenuBanner;
        private static Banner _latestBanner;
        private static int _latestVersion;
        private static BannerImageIdentifierVM _image;

        internal static void Initialize()
        {
            if (_installed || _unavailable) return;
            // Sandbox UI may not be loaded at session launch. Wait for its normal load context.
            var assembly = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "SandBox.ViewModelCollection");
            if (assembly == null) return;
            var harmony = new Harmony("com.bellumcivile.lordship.mapbanner");
            try
            {
                var type = assembly.GetType("SandBox.ViewModelCollection.Nameplate.SettlementNameplateVM", true);
                var instance = Expression.Parameter(typeof(object), "instance");
                _settlement = Expression.Lambda<Func<object, Settlement>>(
                    Expression.Property(Expression.Convert(instance, type), "Settlement"), instance).Compile();
                harmony.Patch(AccessTools.Method(type, "RefreshBindValues"),
                    transpiler: new HarmonyMethod(typeof(LordshipMapBannerPatch), nameof(BannerBinding)));
                var update = AccessTools.Method(typeof(SettlementMenuOverlayVM), "UpdateSettlementOwnerBanner");
                _refreshMenuBanner = AccessTools.MethodDelegate<Action<SettlementMenuOverlayVM>>(update);
                harmony.Patch(update, postfix: new HarmonyMethod(typeof(LordshipMapBannerPatch), nameof(MenuBanner)));
                harmony.Patch(AccessTools.Method(typeof(SettlementMenuOverlayVM), nameof(SettlementMenuOverlayVM.Refresh)),
                    postfix: new HarmonyMethod(typeof(LordshipMapBannerPatch), nameof(RefreshMenuBanner)));
                _installed = true;
                BellumCivileLogger.Log("Lordship village banner display initialized for campaign nameplates and settlement menus.");
            }
            catch (Exception ex)
            {
                harmony.UnpatchAll(harmony.Id);
                _settlement = null;
                _refreshMenuBanner = null;
                _unavailable = true;
                BellumCivileLogger.Log("Lordship village banner display is unavailable: " + ex);
            }
        }

        private static IEnumerable<CodeInstruction> BannerBinding(IEnumerable<CodeInstruction> instructions)
        {
            int matched = 0;
            foreach (var instruction in instructions)
            {
                yield return instruction;
                if (instruction.opcode == OpCodes.Ldfld && instruction.operand is FieldInfo field
                    && field.Name == "_bindBanner" && field.FieldType == typeof(BannerImageIdentifierVM))
                {
                    yield return new CodeInstruction(OpCodes.Ldarg_0);
                    yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(LordshipMapBannerPatch), nameof(SelectBanner)));
                    matched++;
                }
            }
            if (matched != 1) throw new InvalidOperationException("Lordship village nameplate banner contract changed.");
        }

        private static BannerImageIdentifierVM SelectBanner(BannerImageIdentifierVM native, object nameplate)
            => GetPlayerBannerImage(_settlement(nameplate)) ?? native;

        private static void MenuBanner(SettlementMenuOverlayVM __instance, Settlement ____settlement)
        {
            var image = GetPlayerBannerImage(____settlement);
            if (image != null) __instance.SettlementOwnerBanner = image;
        }

        private static void RefreshMenuBanner(SettlementMenuOverlayVM __instance) => _refreshMenuBanner(__instance);

        internal static BannerImageIdentifierVM GetPlayerBannerImage(Settlement village)
        {
            var registry = LordshipBehavior.Instance;
            if (registry?.Ready != true || village?.IsVillage != true) return null;
            var grant = registry.GetGrant(village);
            if (grant?.IsVassalGrant != true || grant.Holder != Hero.MainHero || !grant.IsValid) return null;
            var banner = Clan.PlayerClan?.Banner;
            if (banner == null) return null;
            int version = banner.GetVersionNo();
            if (_image == null || _latestBanner != banner || _latestVersion != version)
            {
                _image = new BannerImageIdentifierVM(banner, true);
                _latestBanner = banner;
                _latestVersion = version;
            }
            // Substitute only the UI binding on the main thread; keep the native cached owner banner intact.
            return _image;
        }
    }
}
