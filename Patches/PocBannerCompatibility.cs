using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Localization;

namespace BellumCivile.Patches
{
    internal static class PocBannerCompatibility
    {
        private static bool _attempted;
        private static bool _unavailable;
        private static Func<object> _readConfig;
        private static Func<object, IDictionary> _readKingdoms;
        private static Func<object, IDictionary> _readClans;
        private static Func<object, object> _readDefaults;
        private static readonly Dictionary<Type, Dictionary<string, PropertyInfo>> Properties =
            new Dictionary<Type, Dictionary<string, PropertyInfo>>();
        private static readonly MethodInfo NameGetter = AccessTools.PropertyGetter(typeof(Kingdom), nameof(Kingdom.Name));

        internal static void TryApply(Harmony harmony)
        {
            if (_attempted || harmony == null) return;
            Type module = AccessTools.TypeByName("PocColor.PocColorMod");
            if (module == null) return;
            _attempted = true;
            var patched = new List<MethodInfo>();
            MethodInfo transpiler = AccessTools.Method(typeof(PocBannerCompatibility), nameof(NameTranspiler));
            try
            {
                PropertyInfo config = AccessTools.Property(module, "config");
                if (config?.GetGetMethod()?.IsStatic != true)
                    throw new InvalidOperationException("Unsupported POC configuration API.");
                var instance = Expression.Parameter(typeof(object), "config");
                _readConfig = Expression.Lambda<Func<object>>(Expression.Convert(Expression.Property(null, config), typeof(object))).Compile();
                _readKingdoms = MapReader(config.PropertyType, "kingdoms", instance);
                _readClans = MapReader(config.PropertyType, "clans", instance);
                _readDefaults = Expression.Lambda<Func<object, object>>(Expression.Convert(
                    Expression.Property(Expression.Convert(instance, config.PropertyType), "defaultConfig"), typeof(object)), instance).Compile();

                // Change POC's internal lookup names only, never the game's visible names or its config file.
                var targets = module.Assembly.GetTypes()
                    .Where(t => t.FullName == "PocColor.PocColorMod" || t.FullName == "PocColor.PocColorModSetColors"
                        || t.FullName.StartsWith("PocColor.PocColorModSetColors+", StringComparison.Ordinal))
                    .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static
                        | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                    .Where(m => m.GetMethodBody() != null && PatchProcessor.GetOriginalInstructions(m).Any(i => i.Calls(NameGetter)))
                    .ToList();
                if (targets.Count == 0) throw new InvalidOperationException("No supported POC realm-name lookup paths.");
                foreach (MethodInfo method in targets)
                {
                    harmony.Patch(method, transpiler: new HarmonyMethod(transpiler));
                    patched.Add(method);
                }
                BellumCivileLogger.Log($"Enabled POC banner compatibility ({patched.Count} realm-name lookup paths).");
                PocEquipmentOwnerCompatibility.TryApply(harmony, module.Assembly);
            }
            catch (Exception ex)
            {
                foreach (MethodInfo method in patched) harmony.Unpatch(method, transpiler);
                _readConfig = null;
                _unavailable = true;
                BellumCivileLogger.Log($"POC banner adapter unavailable: {ex.GetType().Name}: {ex.Message}");
            }
        }

        private static Func<object, IDictionary> MapReader(Type type, string name, ParameterExpression instance)
        {
            return Expression.Lambda<Func<object, IDictionary>>(Expression.Convert(
                Expression.Property(Expression.Convert(instance, type), name), typeof(IDictionary)), instance).Compile();
        }

        private static IEnumerable<CodeInstruction> NameTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            foreach (CodeInstruction instruction in instructions)
            {
                if (instruction.Calls(NameGetter))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = AccessTools.Method(typeof(PocBannerCompatibility), nameof(ReadKingdomName));
                }
                yield return instruction;
            }
        }

        internal static TextObject ReadKingdomName(Kingdom kingdom)
        {
            if (kingdom == null) return null;
            TextObject displayed = kingdom.Name;
            if (_unavailable) return displayed;
            try
            {
                object config = _readConfig?.Invoke();
                if (config == null) return displayed;
                IDictionary kingdoms = _readKingdoms(config);
                if (HasEntry(kingdoms, displayed?.ToString())) return displayed;
                TextObject native = DynamicKingdomTitleNameHelper.GetNativeNameText(kingdom);
                return SelectName(kingdoms, displayed, native);
            }
            catch (Exception ex)
            {
                Disable(ex);
                return displayed;
            }
        }

        internal static TextObject SelectName(IDictionary kingdoms, TextObject displayed, TextObject native)
        {
            if (HasEntry(kingdoms, displayed?.ToString())) return displayed;
            return HasEntry(kingdoms, native?.ToString()) ? native : displayed;
        }

        // Read only the banner/color rules, independently of shields, troop patterns and uniforms.
        internal static bool TryGetPolicy(Clan clan, out bool customBanner, out bool controlsColors)
        {
            customBanner = false;
            // If an installed POC build is unsupported, leave its color rules in charge.
            controlsColors = _unavailable;
            if (_unavailable) return true;
            try { return ReadPolicy(clan, out customBanner, out controlsColors); }
            catch (Exception ex)
            {
                Disable(ex);
                controlsColors = true;
                return true;
            }
        }

        private static bool ReadPolicy(Clan clan, out bool customBanner, out bool controlsColors)
        {
            customBanner = false;
            controlsColors = false;
            object config = _readConfig?.Invoke();
            if (clan == null || config == null) return false;
            Kingdom kingdom = clan.Kingdom;
            IDictionary kingdoms = _readKingdoms(config);
            IDictionary clans = _readClans(config);
            object realm = Entry(kingdoms, ReadKingdomName(kingdom)?.ToString());
            if (realm == null && kingdom != null && kingdom == Clan.PlayerClan?.Kingdom)
                realm = Entry(kingdoms, "PlayerKingdom");
            object house = Entry(clans, clan.Name?.ToString());
            if (house == null && clan == Clan.PlayerClan) house = Entry(clans, "PlayerClan");
            IDictionary realmClans = Value(realm, "clans") as IDictionary;
            object realmHouse = Entry(realmClans, clan.Name?.ToString());
            if (realmHouse == null && clan == Clan.PlayerClan) realmHouse = Entry(realmClans, "PlayerClan");

            object[] scopes = { _readDefaults(config), realm, house, realmHouse };
            string follow = null, followBackground = null;
            string artwork = null;
            for (int i = 0; i < scopes.Length; i++)
            {
                object scope = scopes[i];
                follow = Value(scope, "FollowKingdomColors") as string ?? follow;
                followBackground = Value(scope, "FollowKingdomBackgroundOnly") as string ?? followBackground;
                if (i > 0) artwork = Value(scope, "clanBanner") as string ?? artwork;
            }
            customBanner = !string.IsNullOrEmpty(artwork);
            // Explicit palettes and configured artwork also belong to POC, even when not following a kingdom.
            controlsColors = customBanner || IsTrue(follow) || IsTrue(followBackground)
                || Value(house, "primaryColor") != null || Value(house, "secondaryColor") != null
                || Value(realmHouse, "primaryColor") != null || Value(realmHouse, "secondaryColor") != null;
            return true;
        }

        private static void Disable(Exception exception)
        {
            if (_unavailable) return;
            _unavailable = true;
            BellumCivileLogger.Log($"POC banner adapter stopped after an unsupported runtime state: {exception.GetType().Name}: {exception.Message}");
        }

        private static bool IsTrue(string value) => bool.TryParse(value, out bool result) && result;
        private static bool HasEntry(IDictionary map, string key) => Entry(map, key) != null;
        private static object Entry(IDictionary map, string key) => map != null && key != null && map.Contains(key) ? map[key] : null;

        private static object Value(object instance, string name)
        {
            if (instance == null) return null;
            Type type = instance.GetType();
            PropertyInfo property;
            lock (Properties)
            {
                if (!Properties.TryGetValue(type, out var properties))
                    Properties[type] = properties = new Dictionary<string, PropertyInfo>();
                if (!properties.TryGetValue(name, out property))
                    properties[name] = property = type.GetProperty(name);
            }
            return property?.GetValue(instance);
        }
    }
}
