using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using BellumCivile.Behaviors;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Localization;
using TaleWorlds.ModuleManager;

namespace BellumCivile
{
    internal enum KingdomDisplayNameField
    {
        Name,
        EncyclopediaTitle
    }

    internal static class DynamicKingdomTitleNameHelper
    {
        private const int CacheTtlMs = 30000;
        private static readonly object CacheLock = new object();
        private static readonly Dictionary<Kingdom, CachedRealmName> NameCache = new Dictionary<Kingdom, CachedRealmName>();
        private static readonly Dictionary<Kingdom, NativeRealmNames> NativeNameRegistry = new Dictionary<Kingdom, NativeRealmNames>();
        private static readonly Dictionary<string, NativeRealmNames> XmlNativeNameRegistry =
            new Dictionary<string, NativeRealmNames>(StringComparer.OrdinalIgnoreCase);
        private static FeudalTitleBehavior _cacheBehavior;
        private static bool? _lastDynamicNamesEnabled;
        private static bool _xmlNativeNamesLoaded;

        [ThreadStatic]
        private static int _resolutionDepth;

        [ThreadStatic]
        private static int _suppressionDepth;

        [ThreadStatic]
        private static int _rawReadDepth;

        private sealed class CachedRealmName
        {
            public TextObject Text { get; }
            public string RulingClanId { get; }
            public int TitleRevision { get; }
            public int Timestamp { get; }

            public CachedRealmName(TextObject text, string rulingClanId, int titleRevision, int timestamp)
            {
                Text = text;
                RulingClanId = rulingClanId;
                TitleRevision = titleRevision;
                Timestamp = timestamp;
            }
        }

        private sealed class NativeRealmNames
        {
            public TextObject Name;
            public TextObject InformalName;
            public TextObject EncyclopediaTitle;
        }

        private sealed class NativeNameReadScope : IDisposable
        {
            private bool _disposed;

            public void Dispose()
            {
                if (_disposed)
                    return;

                _disposed = true;
                if (_suppressionDepth > 0)
                    _suppressionDepth--;
            }
        }

        private sealed class RawNameReadScope : IDisposable
        {
            private bool _disposed;

            public void Dispose()
            {
                if (_disposed)
                    return;

                _disposed = true;
                if (_rawReadDepth > 0)
                    _rawReadDepth--;
            }
        }

        public static void BeginCampaign()
        {
            lock (CacheLock)
            {
                NameCache.Clear();
                NativeNameRegistry.Clear();
                XmlNativeNameRegistry.Clear();
                _cacheBehavior = null;
                _lastDynamicNamesEnabled = null;
                _xmlNativeNamesLoaded = false;
                RealmNameConfig.Reset();
            }
        }

        public static void CaptureNativeNamesAfterLoad()
        {
            EnsureXmlNativeNamesLoaded();

            HashSet<Kingdom> activeKingdoms = new HashSet<Kingdom>(
                (Kingdom.All ?? Enumerable.Empty<Kingdom>()).Where(kingdom => kingdom != null));

            foreach (Kingdom kingdom in activeKingdoms)
            {
                TextObject nativeName;
                TextObject nativeEncyclopediaTitle;
                using (BeginRawNameReadScope())
                {
                    nativeName = kingdom.Name;
                    nativeEncyclopediaTitle = kingdom.EncyclopediaTitle;
                }

                RecordLoadedNativeNames(
                    kingdom,
                    nativeName,
                    kingdom.InformalName,
                    nativeEncyclopediaTitle);
            }
        }

        private static void RecordLoadedNativeNames(
            Kingdom kingdom,
            TextObject name,
            TextObject informalName,
            TextObject encyclopediaTitle)
        {
            if (kingdom == null)
                return;

            NativeRealmNames xmlNames = ResolveXmlNativeNames(kingdom.StringId);
            TextObject recoveredName = SelectLoadedNativeText(
                kingdom,
                name,
                xmlNames?.Name,
                KingdomDisplayNameField.Name);
            TextObject recoveredInformalName = SelectLoadedNativeText(
                kingdom,
                informalName,
                xmlNames?.InformalName,
                KingdomDisplayNameField.Name,
                compareWithKingdomId: false);
            TextObject recoveredEncyclopediaTitle = SelectLoadedNativeText(
                kingdom,
                encyclopediaTitle,
                xmlNames?.EncyclopediaTitle,
                KingdomDisplayNameField.EncyclopediaTitle);

            lock (CacheLock)
            {
                NativeRealmNames nativeNames = GetOrCreateNativeNames(kingdom);
                nativeNames.Name = Copy(recoveredName);
                nativeNames.InformalName = Copy(recoveredInformalName);
                nativeNames.EncyclopediaTitle = Copy(recoveredEncyclopediaTitle);
                NameCache.Remove(kingdom);
            }
        }

        private static TextObject SelectLoadedNativeText(
            Kingdom kingdom,
            TextObject loadedValue,
            TextObject xmlValue,
            KingdomDisplayNameField field,
            bool compareWithKingdomId = true)
        {
            if (!IsInvalidLoadedNativeText(kingdom, loadedValue, field, compareWithKingdomId))
                return loadedValue;

            if (xmlValue != null && !xmlValue.IsEmpty())
                return xmlValue;

            return loadedValue;
        }

        private static bool IsInvalidLoadedNativeText(
            Kingdom kingdom,
            TextObject text,
            KingdomDisplayNameField field,
            bool compareWithKingdomId)
        {
            if (text == null || text.IsEmpty())
                return true;

            string value = text.Value?.Trim();
            string rendered = text.ToString()?.Trim();
            string kingdomId = kingdom?.StringId?.Trim();
            if (compareWithKingdomId
                && !string.IsNullOrWhiteSpace(kingdomId)
                && (string.Equals(value, kingdomId, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(rendered, kingdomId, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            if (!IsBellumProjection(text))
                return false;

            return LooksLikeCurrentBellumProjection(kingdom, rendered, field);
        }

        private static bool LooksLikeCurrentBellumProjection(
            Kingdom kingdom,
            string rendered,
            KingdomDisplayNameField field)
        {
            if (kingdom == null || string.IsNullOrWhiteSpace(rendered))
                return false;

            try
            {
                FeudalTitleBehavior titleBehavior = FeudalTitleBehavior.Instance;
                FeudalTitleRecord sovereignTitle = titleBehavior?.GetRealmSovereignTitle(
                    kingdom,
                    FeudalHierarchyMode.DeFacto);
                string projected = sovereignTitle == null || kingdom.RulingClan == null
                    ? null
                    : FeudalTitleDisplayHelper.FormatTitleName(sovereignTitle, kingdom.RulingClan);
                return !string.IsNullOrWhiteSpace(projected)
                    && string.Equals(rendered, projected.Trim(), StringComparison.Ordinal);
            }
            catch
            {
                return false;
            }
        }

        public static void RecordNativeNameChange(Kingdom kingdom, TextObject name, TextObject informalName)
        {
            if (kingdom == null)
                return;

            lock (CacheLock)
            {
                NativeRealmNames nativeNames = GetOrCreateNativeNames(kingdom);
                bool isProjectedReadback = NameCache.TryGetValue(kingdom, out CachedRealmName cached)
                    && ReferenceEquals(cached.Text, name);
                if (!isProjectedReadback)
                    nativeNames.Name = Copy(name);
                if (!isProjectedReadback && kingdom == Clan.PlayerClan?.Kingdom && kingdom.RulingClan == Clan.PlayerClan)
                    FeudalTitleBehavior.Instance?.RecordRealmIdentityRename(kingdom, name);
                nativeNames.InformalName = Copy(informalName);
                NameCache.Remove(kingdom);
            }
        }

        public static void RecordNativeInitialization(
            Kingdom kingdom,
            TextObject name,
            TextObject informalName,
            TextObject encyclopediaTitle)
        {
            if (kingdom == null)
                return;

            lock (CacheLock)
            {
                NativeRealmNames nativeNames = GetOrCreateNativeNames(kingdom);
                if (nativeNames.Name == null)
                    nativeNames.Name = Copy(name);
                if (nativeNames.InformalName == null)
                    nativeNames.InformalName = Copy(informalName);
                if (nativeNames.EncyclopediaTitle == null)
                    nativeNames.EncyclopediaTitle = Copy(encyclopediaTitle);
                NameCache.Remove(kingdom);
            }
        }

        public static IDisposable BeginNativeNameReadScope()
        {
            _suppressionDepth++;
            return new NativeNameReadScope();
        }

        private static IDisposable BeginRawNameReadScope()
        {
            _rawReadDepth++;
            return new RawNameReadScope();
        }

        public static TextObject ResolveNameRead(
            Kingdom kingdom,
            TextObject currentValue,
            KingdomDisplayNameField field)
        {
            if (kingdom == null)
                return currentValue;

            if (_rawReadDepth > 0)
                return currentValue;

            bool dynamicNamesEnabled = BellumCivileOptions.UseSovereignTitlesAsRealmNames;
            ObserveSettingState(dynamicNamesEnabled);

            if (_suppressionDepth > 0)
                return ResolveNativeText(kingdom, currentValue, field) ?? currentValue;

            if (TryResolveDynamicText(kingdom, out TextObject displayText))
                return displayText;

            return ResolveNativeText(kingdom, currentValue, field) ?? currentValue;
        }

        public static bool TryResolve(Kingdom kingdom, out string displayName)
        {
            displayName = null;
            if (!TryResolveDynamicText(kingdom, out TextObject displayText))
                return false;

            displayName = displayText?.ToString();
            return !string.IsNullOrWhiteSpace(displayName);
        }

        private static bool TryResolveDynamicText(Kingdom kingdom, out TextObject displayText)
        {
            displayText = null;
            if (_suppressionDepth > 0
                || _resolutionDepth > 0
                || kingdom == null
                || kingdom.IsEliminated
                || kingdom.RulingClan == null
                || kingdom.RulingClan.IsEliminated
                || BellumKingdomVisibilityHelper.IsTemporaryBellumKingdom(kingdom))
            {
                return false;
            }

            FeudalTitleBehavior titleBehavior = FeudalTitleBehavior.Instance;
            if (titleBehavior == null)
                return false;

            string rulingClanId = kingdom.RulingClan.StringId ?? string.Empty;
            int titleRevision = titleBehavior.RuntimeRevision;
            int now = Environment.TickCount;
            lock (CacheLock)
            {
                if (_cacheBehavior != titleBehavior)
                {
                    NameCache.Clear();
                    _cacheBehavior = titleBehavior;
                }

                if (NameCache.TryGetValue(kingdom, out CachedRealmName cached))
                {
                    int elapsed = now - cached.Timestamp;
                    if (cached.TitleRevision == titleRevision
                        && string.Equals(cached.RulingClanId, rulingClanId, StringComparison.Ordinal)
                        && elapsed >= 0
                        && elapsed < CacheTtlMs)
                    {
                        displayText = cached.Text;
                        return displayText != null && !string.IsNullOrWhiteSpace(displayText.ToString());
                    }
                }
            }

            _resolutionDepth++;
            try
            {
                FeudalTitleRecord sovereignTitle = titleBehavior.GetRealmSovereignTitle(kingdom, FeudalHierarchyMode.DeFacto);
                if (sovereignTitle == null
                    || !sovereignTitle.IsActive
                    || !string.Equals(sovereignTitle.DeFactoHolderClanId, rulingClanId, StringComparison.Ordinal))
                {
                    return false;
                }

                string formattedName;
                if (BellumCivileOptions.UseSovereignTitlesAsRealmNames)
                    formattedName = FeudalTitleDisplayHelper.FormatTitleName(sovereignTitle, kingdom.RulingClan);
                else
                {
                    string nativeName = GetNativeName(kingdom);
                    string root = titleBehavior.GetRealmIdentityRoot(kingdom, nativeName)
                        ?? RealmNameConfig.ResolveRoot(kingdom.StringId, nativeName);
                    if (string.IsNullOrWhiteSpace(root)) return false;
                    formattedName = FeudalTitleDisplayHelper.FormatTitleName(sovereignTitle.TitleType, root, kingdom.RulingClan, kingdom);
                }
                if (string.IsNullOrWhiteSpace(formattedName))
                    return false;

                displayText = new TextObject("{=!}" + formattedName.Trim());
                lock (CacheLock)
                {
                    NameCache[kingdom] = new CachedRealmName(displayText, rulingClanId, titleRevision, now);
                }

                return true;
            }
            catch (Exception ex)
            {
                BellumCivileLogger.Log($"Failed to resolve dynamic sovereign realm name for {kingdom.StringId ?? "unknown kingdom"}: {ex.Message}");
                return false;
            }
            finally
            {
                _resolutionDepth--;
            }
        }

        public static string GetNativeName(Kingdom kingdom)
        {
            if (kingdom == null)
                return null;

            TextObject recorded = ResolveRecordedNativeText(kingdom, KingdomDisplayNameField.Name);
            if (recorded != null)
                return recorded.ToString();

            using (BeginNativeNameReadScope())
            {
                return kingdom.Name?.ToString();
            }
        }

        private static void ObserveSettingState(bool enabled)
        {
            lock (CacheLock)
            {
                if (_lastDynamicNamesEnabled.HasValue && _lastDynamicNamesEnabled.Value != enabled)
                    NameCache.Clear();
                _lastDynamicNamesEnabled = enabled;
            }
        }

        private static TextObject ResolveNativeText(
            Kingdom kingdom,
            TextObject currentValue,
            KingdomDisplayNameField field)
        {
            TextObject recorded = ResolveRecordedNativeText(kingdom, field);
            if (recorded != null)
                return recorded;

            TextObject fallback = currentValue;
            if (IsBellumProjection(currentValue))
            {
                TextObject informalName = kingdom.InformalName;
                if (informalName != null && !informalName.IsEmpty())
                    fallback = informalName;
            }

            if (field == KingdomDisplayNameField.EncyclopediaTitle
                && (fallback == null || fallback.IsEmpty() || IsBellumProjection(fallback)))
            {
                fallback = ResolveRecordedNativeText(kingdom, KingdomDisplayNameField.Name)
                    ?? kingdom.InformalName
                    ?? currentValue;
            }

            if (fallback != null)
            {
                lock (CacheLock)
                {
                    NativeRealmNames nativeNames = GetOrCreateNativeNames(kingdom);
                    if (field == KingdomDisplayNameField.Name && nativeNames.Name == null)
                        nativeNames.Name = Copy(fallback);
                    else if (field == KingdomDisplayNameField.EncyclopediaTitle && nativeNames.EncyclopediaTitle == null)
                        nativeNames.EncyclopediaTitle = Copy(fallback);
                }
            }

            return fallback;
        }

        private static TextObject ResolveRecordedNativeText(Kingdom kingdom, KingdomDisplayNameField field)
        {
            lock (CacheLock)
            {
                if (!NativeNameRegistry.TryGetValue(kingdom, out NativeRealmNames nativeNames))
                    return null;

                return field == KingdomDisplayNameField.EncyclopediaTitle
                    ? nativeNames.EncyclopediaTitle
                    : nativeNames.Name;
            }
        }

        private static void EnsureXmlNativeNamesLoaded()
        {
            lock (CacheLock)
            {
                if (_xmlNativeNamesLoaded)
                    return;

                _xmlNativeNamesLoaded = true;
            }

            try
            {
                foreach (ModuleInfo module in ModuleHelper.GetActiveModules())
                {
                    if (module == null || string.IsNullOrWhiteSpace(module.FolderPath))
                        continue;

                    LoadKingdomXmlDeclarations(module.FolderPath);
                }
            }
            catch (Exception ex)
            {
                BellumCivileLogger.Log($"Failed to build native kingdom-name recovery catalog: {ex.GetType().Name}: {ex.Message}");
            }
        }

        private static void LoadKingdomXmlDeclarations(string moduleRoot)
        {
            string subModulePath = Path.Combine(moduleRoot, "SubModule.xml");
            if (!File.Exists(subModulePath))
                return;

            XmlDocument manifest = new XmlDocument();
            manifest.Load(subModulePath);
            XmlNodeList declarations = manifest.SelectNodes(
                "/Module/Xmls/XmlNode/XmlName[translate(@id, 'ABCDEFGHIJKLMNOPQRSTUVWXYZ', 'abcdefghijklmnopqrstuvwxyz')='kingdoms']");
            if (declarations == null)
                return;

            foreach (XmlNode declaration in declarations)
            {
                string relativePath = declaration?.Attributes?["path"]?.Value?.Trim();
                if (string.IsNullOrWhiteSpace(relativePath))
                    continue;

                string fileName = Path.HasExtension(relativePath)
                    ? relativePath
                    : relativePath + ".xml";
                string kingdomPath = Path.Combine(moduleRoot, "ModuleData", fileName);
                if (!File.Exists(kingdomPath))
                    continue;

                LoadKingdomXmlFile(kingdomPath);
            }
        }

        private static void LoadKingdomXmlFile(string path)
        {
            try
            {
                XmlDocument document = new XmlDocument();
                document.Load(path);
                XmlNodeList kingdoms = document.SelectNodes("//Kingdom[@id]");
                if (kingdoms == null)
                    return;

                foreach (XmlNode node in kingdoms)
                {
                    string kingdomId = node.Attributes?["id"]?.Value?.Trim();
                    if (string.IsNullOrWhiteSpace(kingdomId))
                        continue;

                    lock (CacheLock)
                    {
                        if (!XmlNativeNameRegistry.TryGetValue(kingdomId, out NativeRealmNames names))
                        {
                            names = new NativeRealmNames();
                            XmlNativeNameRegistry[kingdomId] = names;
                        }

                        MergeXmlText(node, "name", value => names.Name = value);
                        MergeXmlText(node, "short_name", value => names.InformalName = value);
                        MergeXmlText(node, "title", value => names.EncyclopediaTitle = value);
                    }
                }
            }
            catch (Exception ex)
            {
                BellumCivileLogger.Log($"Failed to read native kingdom names from '{path}': {ex.GetType().Name}: {ex.Message}");
            }
        }

        private static void MergeXmlText(XmlNode node, string attributeName, Action<TextObject> assign)
        {
            string raw = node?.Attributes?[attributeName]?.Value;
            if (!string.IsNullOrWhiteSpace(raw))
                assign(new TextObject(raw.Trim()));
        }

        private static NativeRealmNames ResolveXmlNativeNames(string kingdomId)
        {
            if (string.IsNullOrWhiteSpace(kingdomId))
                return null;

            lock (CacheLock)
            {
                return XmlNativeNameRegistry.TryGetValue(kingdomId, out NativeRealmNames names)
                    ? names
                    : null;
            }
        }

        private static NativeRealmNames GetOrCreateNativeNames(Kingdom kingdom)
        {
            if (!NativeNameRegistry.TryGetValue(kingdom, out NativeRealmNames nativeNames))
            {
                nativeNames = new NativeRealmNames();
                NativeNameRegistry[kingdom] = nativeNames;
            }

            return nativeNames;
        }

        private static bool IsBellumProjection(TextObject text)
        {
            return text?.Value != null && text.Value.StartsWith("{=!}", StringComparison.Ordinal);
        }

        private static TextObject Copy(TextObject text)
        {
            if (text == null)
                return null;

            try
            {
                return text.CopyTextObject();
            }
            catch
            {
                return new TextObject(text.Value ?? string.Empty);
            }
        }
    }
}
