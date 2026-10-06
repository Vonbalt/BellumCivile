using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
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
        InformalName,
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
        private static RealmNameDisplayMode? _lastMode;
        private static string _lastLanguage;
        private static ConditionalWeakTable<TextObject, ProjectionOrigin> _projections = new ConditionalWeakTable<TextObject, ProjectionOrigin>();
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
            public TextObject ShortText { get; }
            public string RulingClanId { get; }
            public Hero Ruler { get; }
            public int TitleRevision { get; }
            public int Timestamp { get; }

            public CachedRealmName(TextObject text, TextObject shortText, Clan rulingClan, int titleRevision, int timestamp)
            {
                Text = text;
                ShortText = shortText;
                RulingClanId = rulingClan.StringId;
                Ruler = rulingClan.Leader;
                TitleRevision = titleRevision;
                Timestamp = timestamp;
            }
        }

        private sealed class ProjectionOrigin
        {
            public Kingdom Kingdom;
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
                _lastMode = null;
                _lastLanguage = null;
                _projections = new ConditionalWeakTable<TextObject, ProjectionOrigin>();
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
                TextObject nativeInformalName;
                TextObject nativeEncyclopediaTitle;
                using (BeginRawNameReadScope())
                {
                    nativeName = kingdom.Name;
                    nativeInformalName = kingdom.InformalName;
                    nativeEncyclopediaTitle = kingdom.EncyclopediaTitle;
                }

                RecordLoadedNativeNames(
                    kingdom,
                    nativeName,
                    nativeInformalName,
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
                KingdomDisplayNameField.InformalName,
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

        public static void RecordNativeNameChange(Kingdom kingdom, TextObject name, TextObject informalName, bool projectedNameReadback = false)
        {
            if (kingdom == null)
                return;

            lock (CacheLock)
            {
                NativeRealmNames nativeNames = GetOrCreateNativeNames(kingdom);
                bool isProjectedReadback = projectedNameReadback || IsProjectedReadback(kingdom, name);
                if (!isProjectedReadback)
                    nativeNames.Name = Copy(name);
                if (!isProjectedReadback && kingdom == Clan.PlayerClan?.Kingdom && kingdom.RulingClan == Clan.PlayerClan)
                    FeudalTitleBehavior.Instance?.RecordRealmIdentityRename(kingdom, name);
                if (!IsProjectedReadback(kingdom, informalName))
                    nativeNames.InformalName = Copy(informalName);
                NameCache.Remove(kingdom);
            }
        }

        private static bool IsProjectedReadback(Kingdom kingdom, TextObject value) => value != null
            && _projections.TryGetValue(value, out ProjectionOrigin origin) && origin.Kingdom == kingdom;

        public static bool RestoreNativeRenameArguments(Kingdom kingdom, ref TextObject name, ref TextObject informalName)
        {
            // A caller may pass a displayed name back into ChangeKingdomName. Do not
            // let a presentation-only value become the native backing field.
            bool projectedName = IsProjectedReadback(kingdom, name);
            if (projectedName)
                name = ResolveRecordedNativeText(kingdom, KingdomDisplayNameField.Name) ?? name;
            if (IsProjectedReadback(kingdom, informalName))
                informalName = ResolveRecordedNativeText(kingdom, KingdomDisplayNameField.InformalName) ?? informalName;
            return projectedName;
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

            TextObject nativeText = ResolveNativeText(kingdom, currentValue, field) ?? currentValue;
            if (TryResolveDisplayText(kingdom, field, out TextObject displayText))
                return displayText;
            return nativeText;
        }

        public static bool TryResolve(Kingdom kingdom, out string displayName)
        {
            displayName = null;
            if (!TryResolveDisplayText(kingdom, KingdomDisplayNameField.Name, out TextObject displayText))
                return false;

            displayName = displayText?.ToString();
            return !string.IsNullOrWhiteSpace(displayName);
        }

        public static bool TryResolveDisplayText(Kingdom kingdom, KingdomDisplayNameField field, out TextObject displayText)
        {
            displayText = null;
            RealmNameDisplayMode mode = BellumCivileOptions.RealmNameDisplay;
            ObserveSettingState(mode);
            if (mode == RealmNameDisplayMode.Native || _rawReadDepth > 0 || _suppressionDepth > 0
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
            int titleRevision = titleBehavior.DisplayRevision;
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
                        && cached.Ruler == kingdom.RulingClan.Leader
                        && string.Equals(cached.RulingClanId, rulingClanId, StringComparison.Ordinal)
                        && elapsed >= 0
                        && elapsed < CacheTtlMs)
                    {
                        displayText = field == KingdomDisplayNameField.InformalName ? cached.ShortText : cached.Text;
                        return displayText != null;
                    }
                }
            }

            _resolutionDepth++;
            try
            {
                using (BeginRawNameReadScope())
                    RecordNativeInitialization(kingdom, kingdom.Name, kingdom.InformalName, kingdom.EncyclopediaTitle);
                FeudalTitleRecord sovereignTitle = titleBehavior.GetRealmSovereignTitle(kingdom, FeudalHierarchyMode.DeFacto);
                if (sovereignTitle == null
                    || !sovereignTitle.IsActive
                    || !string.Equals(sovereignTitle.DeFactoHolderClanId, rulingClanId, StringComparison.Ordinal))
                {
                    return false;
                }

                TextObject root;
                if (mode == RealmNameDisplayMode.SovereignTitle)
                    root = string.IsNullOrWhiteSpace(sovereignTitle.Name) ? null : new TextObject(sovereignTitle.Name);
                else
                {
                    string nativeName = GetNativeName(kingdom);
                    string identity = titleBehavior.GetRealmIdentityRoot(kingdom, nativeName);
                    root = !string.IsNullOrWhiteSpace(identity) ? new TextObject(identity)
                        : RealmNameConfig.ResolveRootText(kingdom.StringId, nativeName)
                            ?? GetGeneratedRealmRoot(kingdom);
                }
                if (root == null || root.IsEmpty())
                    return false;

                TextObject fullName = FeudalTitleDisplayHelper.FormatRealmName(sovereignTitle.TitleType,
                    root, kingdom.RulingClan, kingdom, sovereignTitle.FallbackCultureRef);
                displayText = field == KingdomDisplayNameField.InformalName ? root : fullName;
                lock (CacheLock)
                {
                    NameCache[kingdom] = new CachedRealmName(fullName, root, kingdom.RulingClan, titleRevision, now);
                    _projections.GetValue(fullName, _ => new ProjectionOrigin { Kingdom = kingdom });
                    _projections.GetValue(root, _ => new ProjectionOrigin { Kingdom = kingdom });
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
            return GetNativeNameText(kingdom)?.ToString();
        }

        internal static TextObject GetGeneratedRealmRoot(Kingdom kingdom)
        {
            return IndependentKingdomProfileHelper.ResolveFallbackNameRoot(GetNativeNameText(kingdom));
        }

        internal static string GetNativeTitleRoot(Kingdom kingdom)
        {
            TextObject root = GetGeneratedRealmRoot(kingdom);
            if (root == null)
                return GetNativeName(kingdom);

            // Title records store strings. Keep translation tokens where possible,
            // and snapshot dynamic clan names without leaving unbound variables.
            return root.Attributes == null || root.Attributes.Count == 0
                ? root.Value : "{=!}" + root.ToString();
        }

        private static TextObject GetNativeNameText(Kingdom kingdom)
        {
            if (kingdom == null)
                return null;

            TextObject recorded = ResolveRecordedNativeText(kingdom, KingdomDisplayNameField.Name);
            if (recorded != null)
                return recorded;

            using (BeginNativeNameReadScope())
            {
                return kingdom.Name;
            }
        }

        private static void ObserveSettingState(RealmNameDisplayMode mode)
        {
            lock (CacheLock)
            {
                string language = MBTextManager.ActiveTextLanguage;
                if (_lastMode != mode || _lastLanguage != language)
                    NameCache.Clear();
                _lastMode = mode;
                _lastLanguage = language;
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

            if (currentValue != null)
            {
                lock (CacheLock)
                {
                    NativeRealmNames nativeNames = GetOrCreateNativeNames(kingdom);
                    if (field == KingdomDisplayNameField.Name && nativeNames.Name == null)
                        nativeNames.Name = Copy(currentValue);
                    else if (field == KingdomDisplayNameField.InformalName && nativeNames.InformalName == null)
                        nativeNames.InformalName = Copy(currentValue);
                    else if (field == KingdomDisplayNameField.EncyclopediaTitle && nativeNames.EncyclopediaTitle == null)
                        nativeNames.EncyclopediaTitle = Copy(currentValue);
                }
            }

            // The collector and member-value writer must receive the SAME object,
            // including when the first name read occurs while saving a new realm.
            return ResolveRecordedNativeText(kingdom, field) ?? currentValue;
        }

        private static TextObject ResolveRecordedNativeText(Kingdom kingdom, KingdomDisplayNameField field)
        {
            lock (CacheLock)
            {
                if (!NativeNameRegistry.TryGetValue(kingdom, out NativeRealmNames nativeNames))
                    return null;

                return field == KingdomDisplayNameField.EncyclopediaTitle
                    ? nativeNames.EncyclopediaTitle
                    : field == KingdomDisplayNameField.InformalName ? nativeNames.InformalName : nativeNames.Name;
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
