using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Election;
using TaleWorlds.CampaignSystem.Settlements;

namespace BellumCivile
{
    internal static class ModIntegrationHelper
    {
        private const string DiplomacySettingsTypeName = "Diplomacy.Settings";
        private const string KeepFiefBehaviorTypeName = "Diplomacy.CampaignBehaviors.KeepFiefAfterSiegeBehavior";
        private const string DiplomacyRebelFactionsInterfaceTypeName = "Diplomacy.GauntletInterfaces.RebelFactionsInterface";
        private const string DiplomacyKingdomManagementMixinTypeName = "Diplomacy.ViewModelMixin.KingdomManagementVMMixin";
        private const string DiplomacyKingdomExtensionsTypeName = "Diplomacy.Extensions.KingdomExtensions";
        private const string AIInfluenceSettingsTypeName = "AIInfluence.ModSettings";
        private const string AIInfluenceDiplomacyPatchesTypeName = "AIInfluence.Diplomacy.DiplomacyPatches";
        private const string AIInfluenceWithBypassMethodName = "WithBypass";
        private const string GlobalSettingsTypeName = "MCM.Abstractions.Base.Global.GlobalSettings`1";
        private const string EnableFiefFirstRightPropertyName = "EnableFiefFirstRight";
        private const string EnableAIDiplomacyPropertyName = "EnableDiplomacy";
        private const string CustomSpawnsFolderName = "CustomSpawns";
        private const string CustomSpawnsDiplomacyFileName = "Diplomacy.xml";

        private static bool _diplomacyScanComplete;
        private static bool _isDiplomacyLoaded;
        private static Assembly _diplomacyAssembly;
        private static Type _diplomacySettingsType;
        private static Type _keepFiefBehaviorType;
        private static Type _diplomacyRebelFactionsInterfaceType;
        private static Type _diplomacyKingdomManagementMixinType;
        private static Type _diplomacyKingdomExtensionsType;
        private static MethodInfo _diplomacyIsRebelKingdomMethod;
        private static PropertyInfo _globalSettingsInstanceProperty;
        private static PropertyInfo _enableFiefFirstRightProperty;
        private static FieldInfo _claimantSettlementField;
        private static FieldInfo _preliminarySettlementField;
        private static bool _aiInfluenceScanComplete;
        private static bool _isAIInfluenceLoaded;
        private static Type _aiInfluenceSettingsType;
        private static Type _aiInfluenceDiplomacyPatchesType;
        private static PropertyInfo _enableAIDiplomacyProperty;
        private static MethodInfo _aiInfluenceWithBypassMethod;
        private static bool _customSpawnsForceNoKingdomScanComplete;
        private static HashSet<string> _customSpawnsForceNoKingdomClanIds;

        public static bool IsDiplomacyLoaded
        {
            get
            {
                EnsureDiplomacyReflection();
                return _isDiplomacyLoaded;
            }
        }

        public static bool RefreshDiplomacyDetection()
        {
            _diplomacyScanComplete = false;
            _isDiplomacyLoaded = false;
            EnsureDiplomacyReflection();
            return _isDiplomacyLoaded;
        }

        public static bool IsAIInfluenceDiplomacyActive
        {
            get
            {
                EnsureAIInfluenceReflection();
                if (!_isAIInfluenceLoaded || _aiInfluenceSettingsType == null) return false;

                bool? enabled = TryGetMcmBoolSetting(_aiInfluenceSettingsType, EnableAIDiplomacyPropertyName, ref _enableAIDiplomacyProperty);
                return enabled == true;
            }
        }

        public static void ExecuteWithAIInfluenceDiplomacyBypass(Action action)
        {
            if (action == null)
                return;

            EnsureAIInfluenceReflection();

            if (!IsAIInfluenceDiplomacyActive || _aiInfluenceDiplomacyPatchesType == null)
            {
                action();
                return;
            }

            try
            {
                if (_aiInfluenceWithBypassMethod == null || _aiInfluenceWithBypassMethod.DeclaringType != _aiInfluenceDiplomacyPatchesType)
                {
                    _aiInfluenceWithBypassMethod = _aiInfluenceDiplomacyPatchesType.GetMethod(
                        AIInfluenceWithBypassMethodName,
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
                        binder: null,
                        types: new[] { typeof(Action) },
                        modifiers: null);
                }

                if (_aiInfluenceWithBypassMethod != null)
                {
                    _aiInfluenceWithBypassMethod.Invoke(null, new object[] { action });
                    return;
                }
            }
            catch
            {
                // Fall back to running the action directly if AI Influence reflection fails.
            }

            action();
        }

        public static bool IsCustomSpawnsForceNoKingdomClan(Clan clan)
        {
            if (clan == null || string.IsNullOrWhiteSpace(clan.StringId))
                return false;

            EnsureCustomSpawnsForceNoKingdomIds();
            return _customSpawnsForceNoKingdomClanIds.Contains(clan.StringId);
        }

        public static bool ShouldBypassFiefDeliberation(Settlement settlement, Kingdom kingdom, Hero capturerHero = null)
        {
            if (settlement?.Town == null || kingdom == null) return false;
            if (!IsDiplomacyLoaded) return false;

            bool? fiefFirstRightEnabled = TryIsDiplomacyFiefFirstRightEnabled();
            if (fiefFirstRightEnabled == false) return false;

            if (_keepFiefBehaviorType == null && fiefFirstRightEnabled == null) return false;

            return HasPlayerClaimedCapturedFief(settlement, kingdom, capturerHero);
        }

        public static bool? TryIsDiplomacyFiefFirstRightEnabled()
        {
            EnsureDiplomacyReflection();
            if (!_isDiplomacyLoaded || _diplomacySettingsType == null) return null;

            Type openGenericGlobalSettingsType = FindLoadedType(GlobalSettingsTypeName);
            if (openGenericGlobalSettingsType == null) return null;

            try
            {
                Type closedGlobalSettingsType = openGenericGlobalSettingsType.MakeGenericType(_diplomacySettingsType);

                if (_globalSettingsInstanceProperty == null || _globalSettingsInstanceProperty.DeclaringType != closedGlobalSettingsType)
                {
                    _globalSettingsInstanceProperty = closedGlobalSettingsType.GetProperty(
                        "Instance",
                        BindingFlags.Public | BindingFlags.Static);
                }

                object settingsInstance = _globalSettingsInstanceProperty?.GetValue(null, null);
                if (settingsInstance == null) return null;

                if (_enableFiefFirstRightProperty == null || _enableFiefFirstRightProperty.DeclaringType != settingsInstance.GetType())
                {
                    _enableFiefFirstRightProperty = settingsInstance.GetType().GetProperty(
                        EnableFiefFirstRightPropertyName,
                        BindingFlags.Public | BindingFlags.Instance);
                }

                if (_enableFiefFirstRightProperty == null) return null;

                object rawValue = _enableFiefFirstRightProperty.GetValue(settingsInstance, null);
                return rawValue is bool enabled ? enabled : (bool?)null;
            }
            catch
            {
                return null;
            }
        }

        public static bool TryGetDiplomacySetting<T>(string propertyName, out T value)
        {
            value = default(T);
            EnsureDiplomacyReflection();
            if (!_isDiplomacyLoaded || _diplomacySettingsType == null || string.IsNullOrWhiteSpace(propertyName))
                return false;

            try
            {
                object settingsInstance = TryGetMcmSettingsInstance(_diplomacySettingsType);
                PropertyInfo property = settingsInstance?.GetType().GetProperty(
                    propertyName,
                    BindingFlags.Public | BindingFlags.Instance);
                object rawValue = property?.GetValue(settingsInstance, null);
                if (rawValue is T typedValue)
                {
                    value = typedValue;
                    return true;
                }

                if (rawValue != null)
                {
                    value = (T)Convert.ChangeType(rawValue, typeof(T));
                    return true;
                }
            }
            catch (Exception ex)
            {
                BellumCivileLogger.Log(
                    $"Could not read Diplomacy setting '{propertyName}': {ex.GetType().Name}:{ex.Message}");
            }

            return false;
        }

        public static bool IsDiplomacyRebelKingdom(Kingdom kingdom)
        {
            EnsureDiplomacyReflection();
            if (!_isDiplomacyLoaded || kingdom == null)
                return false;

            try
            {
                _diplomacyKingdomExtensionsType = _diplomacyKingdomExtensionsType
                    ?? FindLoadedType(DiplomacyKingdomExtensionsTypeName);
                _diplomacyIsRebelKingdomMethod = _diplomacyIsRebelKingdomMethod
                    ?? _diplomacyKingdomExtensionsType?.GetMethod(
                        "IsRebelKingdom",
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
                        binder: null,
                        types: new[] { typeof(Kingdom) },
                        modifiers: null);
                return _diplomacyIsRebelKingdomMethod?.Invoke(null, new object[] { kingdom }) is bool isRebel
                    && isRebel;
            }
            catch (Exception ex)
            {
                BellumCivileLogger.Log(
                    $"Could not identify Diplomacy rebel kingdom '{kingdom.StringId}': {ex.GetType().Name}:{ex.Message}");
                return false;
            }
        }

        internal static bool TryReadDiplomacyNonAggressionPact(Kingdom first, Kingdom second, out bool hasPact)
        {
            hasPact = false;
            if (!IsDiplomacyLoaded) return true;
            try
            {
                var manager = FindLoadedType("Diplomacy.DiplomaticAction.DiplomaticAgreementManager");
                var query = manager?.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                    .SingleOrDefault(m => m.Name == "HasNonAggressionPact" && m.ReturnType == typeof(bool)
                        && m.GetParameters().Length == 3 && m.GetParameters()[0].ParameterType == typeof(Kingdom)
                        && m.GetParameters()[1].ParameterType == typeof(Kingdom) && m.GetParameters()[2].IsOut);
                if (query == null) return false;
                hasPact = (bool)query.Invoke(null, new object[] { first, second, null });
                return true;
            }
            catch { return false; }
        }

        internal static bool TryReadDiplomacyWarCooldown(Kingdom first, Kingdom second, out bool hasCooldown)
        {
            hasCooldown = false;
            if (!IsDiplomacyLoaded) return true;
            try
            {
                var manager = FindLoadedType("Diplomacy.CooldownManager");
                var query = manager?.GetMethod("HasDeclareWarCooldown", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
                    null, new[] { typeof(IFaction), typeof(IFaction), typeof(float).MakeByRefType() }, null);
                if (query == null || query.ReturnType != typeof(bool)) return false;
                hasCooldown = (bool)query.Invoke(null, new object[] { first, second, 0f });
                return true;
            }
            catch { return false; }
        }

        public static int TryExpireDiplomacyNonAggressionPacts(Kingdom client, Kingdom exceptKingdom = null)
        {
            EnsureDiplomacyReflection();
            if (!_isDiplomacyLoaded || client == null)
                return 0;

            Type managerType = FindLoadedType("Diplomacy.DiplomaticAction.DiplomaticAgreementManager");
            if (managerType == null)
                return 0;

            try
            {
                object manager = managerType.GetProperty(
                    "Instance",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null, null);
                object agreementsObject = managerType.GetProperty(
                    "Agreements",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(manager, null);
                if (!(agreementsObject is IDictionary agreements))
                    return 0;

                int expired = 0;
                foreach (DictionaryEntry pair in agreements)
                {
                    if (!(pair.Value is IEnumerable entries))
                        continue;

                    foreach (object agreement in entries)
                    {
                        if (agreement == null || !AgreementIncludesClient(agreement, client, exceptKingdom))
                            continue;

                        MethodInfo isExpiredMethod = agreement.GetType().GetMethod(
                            "IsExpired",
                            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        if (isExpiredMethod?.Invoke(agreement, null) is bool isExpired && isExpired)
                            continue;

                        MethodInfo expireMethod = agreement.GetType().GetMethod(
                            "Expire",
                            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        if (expireMethod == null)
                            continue;

                        expireMethod.Invoke(agreement, null);
                        expired++;
                    }
                }

                if (expired > 0)
                {
                    BellumCivileLogger.Log(
                        $"Expired {expired} Diplomacy non-aggression pact(s) incompatible with client status; client={client.StringId}.");
                }

                return expired;
            }
            catch (Exception ex)
            {
                BellumCivileLogger.Log(
                    $"Could not reconcile Diplomacy non-aggression pacts for client '{client.StringId}': {ex.GetType().Name}:{ex.Message}");
                return 0;
            }
        }

        private static bool AgreementIncludesClient(object agreement, Kingdom client, Kingdom exceptKingdom)
        {
            PropertyInfo factionsProperty = agreement.GetType().GetProperty(
                "Factions",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            object factions = factionsProperty?.GetValue(agreement, null);
            if (factions == null)
                return false;

            Type factionsType = factions.GetType();
            Kingdom first = factionsType.GetProperty(
                "Faction1",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(factions, null) as Kingdom;
            Kingdom second = factionsType.GetProperty(
                "Faction2",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(factions, null) as Kingdom;
            Kingdom other = first == client ? second : second == client ? first : null;
            return other != null && other != exceptKingdom;
        }

        private static object TryGetMcmSettingsInstance(Type settingsType)
        {
            Type openGenericGlobalSettingsType = FindLoadedType(GlobalSettingsTypeName);
            if (openGenericGlobalSettingsType == null || settingsType == null)
                return null;

            Type closedGlobalSettingsType = openGenericGlobalSettingsType.MakeGenericType(settingsType);
            PropertyInfo instanceProperty = closedGlobalSettingsType.GetProperty(
                "Instance",
                BindingFlags.Public | BindingFlags.Static);
            return instanceProperty?.GetValue(null, null);
        }

        private static void EnsureDiplomacyReflection()
        {
            if (_diplomacyScanComplete) return;

            // Optional module assemblies do not appear dynamically after startup. Cache a negative
            // result as well as a positive one so compatibility discovery never becomes frame work.
            _diplomacyScanComplete = true;

            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                string assemblyName = assembly.GetName().Name ?? string.Empty;

                if (_diplomacyAssembly == null
                    && (assemblyName.Equals("Bannerlord.Diplomacy", StringComparison.OrdinalIgnoreCase)
                     || assemblyName.StartsWith("Bannerlord.Diplomacy.", StringComparison.OrdinalIgnoreCase)
                     || assemblyName.Equals("Bannerlord.ModuleLoader.Bannerlord.Diplomacy", StringComparison.OrdinalIgnoreCase)))
                {
                    _diplomacyAssembly = assembly;
                }

                if (_diplomacySettingsType == null)
                    _diplomacySettingsType = assembly.GetType(DiplomacySettingsTypeName, throwOnError: false);

                if (_keepFiefBehaviorType == null)
                    _keepFiefBehaviorType = assembly.GetType(KeepFiefBehaviorTypeName, throwOnError: false);

                if (_diplomacyRebelFactionsInterfaceType == null)
                    _diplomacyRebelFactionsInterfaceType = assembly.GetType(DiplomacyRebelFactionsInterfaceTypeName, throwOnError: false);

                if (_diplomacyKingdomManagementMixinType == null)
                    _diplomacyKingdomManagementMixinType = assembly.GetType(DiplomacyKingdomManagementMixinTypeName, throwOnError: false);

                if (_diplomacyKingdomExtensionsType == null)
                    _diplomacyKingdomExtensionsType = assembly.GetType(DiplomacyKingdomExtensionsTypeName, throwOnError: false);
            }

            if (_diplomacyAssembly == null)
            {
                _diplomacyAssembly = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => a.GetType(DiplomacySettingsTypeName, throwOnError: false) != null
                                      || a.GetType(KeepFiefBehaviorTypeName, throwOnError: false) != null
                                      || a.GetType(DiplomacyRebelFactionsInterfaceTypeName, throwOnError: false) != null
                                      || a.GetType(DiplomacyKingdomManagementMixinTypeName, throwOnError: false) != null
                                      || a.GetType(DiplomacyKingdomExtensionsTypeName, throwOnError: false) != null);
            }

            _isDiplomacyLoaded =
                _diplomacySettingsType != null
                || _keepFiefBehaviorType != null
                || _diplomacyRebelFactionsInterfaceType != null
                || _diplomacyKingdomManagementMixinType != null
                || _diplomacyKingdomExtensionsType != null;
        }

        private static void EnsureAIInfluenceReflection()
        {
            if (_aiInfluenceScanComplete) return;
            _aiInfluenceScanComplete = true;

            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (_aiInfluenceSettingsType == null)
                    _aiInfluenceSettingsType = assembly.GetType(AIInfluenceSettingsTypeName, throwOnError: false);

                if (_aiInfluenceDiplomacyPatchesType == null)
                    _aiInfluenceDiplomacyPatchesType = assembly.GetType(AIInfluenceDiplomacyPatchesTypeName, throwOnError: false);
            }

            _isAIInfluenceLoaded = _aiInfluenceSettingsType != null || _aiInfluenceDiplomacyPatchesType != null;
        }

        private static bool HasPlayerClaimedCapturedFief(Settlement settlement, Kingdom kingdom, Hero capturerHero)
        {
            if (Clan.PlayerClan == null) return false;
            if (settlement.OwnerClan != Clan.PlayerClan) return false;
            if (settlement.MapFaction != kingdom) return false;
            if (settlement.Town.IsOwnerUnassigned) return false;

            Hero effectiveCapturer = capturerHero ?? settlement.LastAttackerParty?.LeaderHero;
            if (effectiveCapturer == null) return false;
            if (!effectiveCapturer.IsHumanPlayerCharacter) return false;
            if (effectiveCapturer.Clan != Clan.PlayerClan) return false;

            return !HasPendingClaimantDecisionForSettlement(kingdom, settlement);
        }

        private static bool HasPendingClaimantDecisionForSettlement(Kingdom kingdom, Settlement settlement)
        {
            if (kingdom == null || settlement == null) return false;

            EnsureDecisionReflection();

            foreach (SettlementClaimantDecision decision in kingdom.UnresolvedDecisions.OfType<SettlementClaimantDecision>())
            {
                if (ReferenceEquals(GetDecisionSettlement(decision, _claimantSettlementField), settlement))
                    return true;
            }

            foreach (SettlementClaimantPreliminaryDecision decision in kingdom.UnresolvedDecisions.OfType<SettlementClaimantPreliminaryDecision>())
            {
                if (ReferenceEquals(GetDecisionSettlement(decision, _preliminarySettlementField), settlement))
                    return true;
            }

            return false;
        }

        private static void EnsureDecisionReflection()
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

            if (_claimantSettlementField == null)
                _claimantSettlementField = typeof(SettlementClaimantDecision).GetField("Settlement", flags)
                                        ?? typeof(SettlementClaimantDecision).GetField("_settlement", flags);

            if (_preliminarySettlementField == null)
                _preliminarySettlementField = typeof(SettlementClaimantPreliminaryDecision).GetField("Settlement", flags)
                                           ?? typeof(SettlementClaimantPreliminaryDecision).GetField("_settlement", flags);
        }

        private static Settlement GetDecisionSettlement(object decision, FieldInfo settlementField)
        {
            return settlementField?.GetValue(decision) as Settlement;
        }

        private static Type FindLoadedType(string fullTypeName)
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type = assembly.GetType(fullTypeName, throwOnError: false);
                if (type != null) return type;
            }

            return null;
        }

        private static void EnsureCustomSpawnsForceNoKingdomIds()
        {
            if (_customSpawnsForceNoKingdomScanComplete)
                return;

            _customSpawnsForceNoKingdomScanComplete = true;
            _customSpawnsForceNoKingdomClanIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (string diplomacyPath in GetCustomSpawnsDiplomacyPaths())
            {
                TryLoadForceNoKingdomClanIds(diplomacyPath, _customSpawnsForceNoKingdomClanIds);
            }

            if (_customSpawnsForceNoKingdomClanIds.Count > 0)
            {
                BellumCivileLogger.Log($"Loaded {_customSpawnsForceNoKingdomClanIds.Count} Custom Spawns ForceNoKingdom clan ids for mercenary recruitment compatibility.");
            }
        }

        private static IEnumerable<string> GetCustomSpawnsDiplomacyPaths()
        {
            HashSet<string> moduleRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> modulesDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly == null || assembly.IsDynamic)
                    continue;

                string location;
                try
                {
                    location = assembly.Location;
                }
                catch
                {
                    continue;
                }

                if (TryGetModuleRootFromPath(location, out string moduleRoot, out string modulesDirectory))
                    moduleRoots.Add(moduleRoot);

                if (!string.IsNullOrWhiteSpace(modulesDirectory))
                    modulesDirectories.Add(modulesDirectory);
            }

            string ownLocation = Assembly.GetExecutingAssembly().Location;
            if (TryGetModuleRootFromPath(ownLocation, out string ownModuleRoot, out string ownModulesDirectory))
            {
                moduleRoots.Add(ownModuleRoot);
                if (!string.IsNullOrWhiteSpace(ownModulesDirectory))
                    modulesDirectories.Add(ownModulesDirectory);
            }

            foreach (string modulesDirectory in modulesDirectories.ToList())
            {
                if (!Directory.Exists(modulesDirectory))
                    continue;

                try
                {
                    foreach (string moduleDirectory in Directory.GetDirectories(modulesDirectory))
                    {
                        moduleRoots.Add(moduleDirectory);
                    }
                }
                catch
                {
                    // If a mod manager exposes an unreadable folder, keep the compatibility scan best-effort.
                }
            }

            foreach (string moduleRoot in moduleRoots)
            {
                string diplomacyPath = Path.Combine(moduleRoot, CustomSpawnsFolderName, CustomSpawnsDiplomacyFileName);
                if (File.Exists(diplomacyPath))
                    yield return diplomacyPath;
            }
        }

        private static bool TryGetModuleRootFromPath(string path, out string moduleRoot, out string modulesDirectory)
        {
            moduleRoot = null;
            modulesDirectory = null;

            if (string.IsNullOrWhiteSpace(path))
                return false;

            try
            {
                string fullPath = Path.GetFullPath(path);
                string directory = File.Exists(fullPath) ? Path.GetDirectoryName(fullPath) : fullPath;
                if (string.IsNullOrWhiteSpace(directory))
                    return false;

                DirectoryInfo current = new DirectoryInfo(directory);
                while (current != null)
                {
                    DirectoryInfo parent = current.Parent;
                    if (parent != null && parent.Name.Equals("Modules", StringComparison.OrdinalIgnoreCase))
                    {
                        moduleRoot = current.FullName;
                        modulesDirectory = parent.FullName;
                        return true;
                    }

                    current = parent;
                }
            }
            catch
            {
            }

            return false;
        }

        private static void TryLoadForceNoKingdomClanIds(string diplomacyPath, HashSet<string> clanIds)
        {
            if (string.IsNullOrWhiteSpace(diplomacyPath) || clanIds == null)
                return;

            try
            {
                XDocument document = XDocument.Load(diplomacyPath);
                foreach (XElement diplomacyData in document.Descendants("DiplomacyData"))
                {
                    string target = diplomacyData.Attribute("target")?.Value;
                    if (string.IsNullOrWhiteSpace(target))
                        continue;

                    XElement forceNoKingdomElement = diplomacyData.Elements("ForceNoKingdom").FirstOrDefault();
                    if (forceNoKingdomElement == null)
                        continue;

                    string value = (forceNoKingdomElement.Value ?? string.Empty).Trim();
                    if (bool.TryParse(value, out bool forceNoKingdom) && forceNoKingdom)
                        clanIds.Add(target.Trim());
                }
            }
            catch (Exception ex)
            {
                BellumCivileLogger.Log($"Failed to read Custom Spawns diplomacy data at '{diplomacyPath}': {ex.Message}");
            }
        }

        private static bool? TryGetMcmBoolSetting(Type settingsType, string propertyName, ref PropertyInfo cachedProperty)
        {
            if (settingsType == null || string.IsNullOrEmpty(propertyName)) return null;

            Type openGenericGlobalSettingsType = FindLoadedType(GlobalSettingsTypeName);
            if (openGenericGlobalSettingsType == null) return null;

            try
            {
                Type closedGlobalSettingsType = openGenericGlobalSettingsType.MakeGenericType(settingsType);

                if (_globalSettingsInstanceProperty == null || _globalSettingsInstanceProperty.DeclaringType != closedGlobalSettingsType)
                {
                    _globalSettingsInstanceProperty = closedGlobalSettingsType.GetProperty(
                        "Instance",
                        BindingFlags.Public | BindingFlags.Static);
                }

                object settingsInstance = _globalSettingsInstanceProperty?.GetValue(null, null);
                if (settingsInstance == null) return null;

                if (cachedProperty == null || cachedProperty.DeclaringType != settingsInstance.GetType())
                {
                    cachedProperty = settingsInstance.GetType().GetProperty(
                        propertyName,
                        BindingFlags.Public | BindingFlags.Instance);
                }

                if (cachedProperty == null) return null;

                object rawValue = cachedProperty.GetValue(settingsInstance, null);
                return rawValue is bool enabled ? enabled : (bool?)null;
            }
            catch
            {
                return null;
            }
        }
    }
}
