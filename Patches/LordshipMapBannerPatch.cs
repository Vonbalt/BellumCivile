using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Emit;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Core.ViewModelCollection.ImageIdentifiers;

namespace BellumCivile.Patches
{
    internal static class LordshipMapBannerPatch
    {
        private static bool _installed;
        private static Func<object, Settlement> _settlement;
        private static Banner _latestBanner;
        private static int _latestVersion;
        private static BannerImageIdentifierVM _image;

        internal static void Initialize()
        {
            if (_installed) return;
            var harmony = new Harmony("com.bellumcivile.lordship.mapbanner");
            try
            {
                var type = Type.GetType("SandBox.ViewModelCollection.Nameplate.SettlementNameplateVM, SandBox.ViewModelCollection", true);
                var instance = Expression.Parameter(typeof(object), "instance");
                _settlement = Expression.Lambda<Func<object, Settlement>>(
                    Expression.Property(Expression.Convert(instance, type), "Settlement"), instance).Compile();
                harmony.Patch(AccessTools.Method(type, "RefreshBindValues"),
                    transpiler: new HarmonyMethod(typeof(LordshipMapBannerPatch), nameof(BannerBinding)));
                _installed = true;
            }
            catch { harmony.UnpatchAll(harmony.Id); _settlement = null; throw; }
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
        {
            var registry = LordshipBehavior.Instance;
            if (registry?.Ready != true) return native;
            var grant = registry.GetGrant(Hero.MainHero);
            if (grant?.IsVassalGrant != true || grant.Village != _settlement(nameplate) || !grant.IsValid) return native;
            var banner = Clan.PlayerClan?.Banner;
            if (banner == null) return native;
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
