using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Xml;
using Bannerlord.UIExtenderEx;
using Bannerlord.UIExtenderEx.Attributes;
using Bannerlord.UIExtenderEx.Prefabs;
using HarmonyLib;
using InsertPatch = Bannerlord.UIExtenderEx.Prefabs2.PrefabExtensionInsertPatch;
using InsertType = Bannerlord.UIExtenderEx.Prefabs2.InsertType;

namespace BellumCivile.UI
{
    internal static class EncyclopediaFamilyLayoutCompatibility
    {
        private const string Movie = "EncyclopediaHeroPage";
        private static readonly HashSet<string> LoggedReasons = new HashSet<string>();
        private static bool _nativeLayoutAvailable = true;
        private static XmlDocument _pageTemplate;

        internal static bool Enabled { get; private set; }
        internal static event Action Changed;

        internal static void Refresh(XmlDocument document = null)
        {
            string reason = null;
            bool enabled = false;
            try
            {
                if (document != null)
                {
                    _nativeLayoutAvailable = HasNativeAnchors(document);
                    _pageTemplate = document;
                }
                reason = FindFamilyEditor();
                if (reason == null && !_nativeLayoutAvailable)
                    reason = "the native family layout has been replaced or is unfamiliar";
                enabled = reason == null;
            }
            catch (Exception ex)
            {
                // Unknown UIExtender versions must leave the shared native family UI available.
                reason = "compatibility inspection unavailable: " + ex.GetType().Name;
            }

            if (!enabled && _pageTemplate != null && LoggedReasons.Add(reason))
                BellumCivileLogger.Log("[ENC-FAMILY] Using the shared family layout; " + reason
                    + ". Bellum titles, claims and inheritance remain enabled.");
            if (Enabled == enabled) return;
            Enabled = enabled;
            Changed?.Invoke();
        }

        private static IEnumerable GetRuntimes()
        {
            return (IEnumerable)AccessTools.Method(typeof(UIExtender), "GetAllRuntimes").Invoke(null, null);
        }

        private static string FindFamilyEditor()
        {
            // Inspect active registrations at page construction/parse time, after module registration.
            // No other mod's constructors, mixins or static initializers need to be executed.
            foreach (object runtime in GetRuntimes())
            {
                object component = AccessTools.Field(runtime.GetType(), "PrefabComponent").GetValue(runtime);
                var patches = (IDictionary)AccessTools.Field(component.GetType(), "MoviePatches").GetValue(component);
                if (!patches.Contains(Movie)) continue;
                var enabled = (IDictionary)AccessTools.Field(component.GetType(), "_enabledPatches").GetValue(component);
                foreach (object patch in (IEnumerable)patches[Movie])
                {
                    var property = AccessTools.Property(patch.GetType(), "Type");
                    var type = (Type)(property != null ? property.GetValue(patch)
                        : AccessTools.Field(patch.GetType(), "Item1").GetValue(patch));
                    if (type.Assembly == typeof(EncyclopediaFamilyLayoutCompatibility).Assembly
                        || !enabled.Contains(type) || !(bool)enabled[type]) continue;
                    var extensions = type.GetCustomAttributes<PrefabExtensionAttribute>(false)
                        .Where(a => a.Movie == Movie).ToArray();
                    // Programmatic or document-wide patches have no bounded target we can safely classify.
                    if (typeof(CustomPatch<XmlDocument>).IsAssignableFrom(type)
                        || extensions.Length == 0 || extensions.Any(a => TargetsFamily(a.XPath, _pageTemplate))
                            && !IsComposable(type, extensions))
                        return "another family editor is active: " + type.FullName;
                }
            }
            return null;
        }

        private static bool IsComposable(Type type, PrefabExtensionAttribute[] extensions)
        {
            if (!typeof(InsertPatch).IsAssignableFrom(type) || _pageTemplate == null) return false;
            var getter = AccessTools.PropertyGetter(type, "Type");
            var code = PatchProcessor.GetOriginalInstructions(getter).Where(i => i.opcode != OpCodes.Nop).ToArray();
            // Read literal insert kinds without instantiating or executing another mod's patch.
            if (code.Length != 2 || code[1].opcode != OpCodes.Ret) return false;
            bool sibling = code[0].LoadsConstant((int)InsertType.Append) || code[0].LoadsConstant((int)InsertType.Prepend);
            bool replace = code[0].LoadsConstant((int)InsertType.Replace);
            if (!sibling && !replace) return false;
            foreach (var extension in extensions)
            {
                if (string.IsNullOrWhiteSpace(extension.XPath)) return false;
                var targets = _pageTemplate.SelectNodes(extension.XPath);
                if (targets.Count == 0) return false;
                foreach (XmlNode target in targets)
                {
                    bool navigation = target.Name == "NavigationScopeTargeter";
                    bool anchor = target is XmlElement element
                        && (element.GetAttribute("Id") == "FamilyGrid" || element.GetAttribute("Id") == "FamilyDivider");
                    if (!navigation && (!sibling || !anchor)) return false;
                }
            }
            return true;
        }

        internal static bool TargetsFamily(string xpath, XmlDocument document)
        {
            if (string.IsNullOrWhiteSpace(xpath)) return true;
            if (xpath.IndexOf("Family", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (document == null) return true;
            var anchors = document.SelectNodes("//*[@Id='FamilyGrid' or @Id='FamilyDivider']");
            var targets = document.SelectNodes(xpath);
            if (anchors.Count == 0 || targets.Count == 0) return true;
            foreach (XmlNode target in targets)
                foreach (XmlNode anchor in anchors)
                    if (Contains(target, anchor) || Contains(anchor, target)) return true;
            return false;
        }

        private static bool Contains(XmlNode parent, XmlNode child)
        {
            for (var node = child; node != null; node = node.ParentNode)
                if (node == parent) return true;
            return false;
        }

        private static bool HasNativeAnchors(XmlDocument document)
        {
            var grid = document.SelectSingleNode("//NavigatableGridWidget[@Id='FamilyGrid' and @DataSource='{Family}']");
            var divider = grid?.ParentNode?.SelectSingleNode("EncyclopediaDivider[@Id='FamilyDivider']");
            var scope = grid?.ParentNode?.SelectSingleNode("NavigationScopeTargeter[@ScopeParent='..\\FamilyGrid']");
            if (divider == null || scope == null || document.SelectSingleNode("//EncyclopediaDivider[@Id='AlliesDivider']") == null)
                return false;
            return new[] { grid, divider, scope }.Cast<XmlElement>().All(node =>
                !node.HasAttribute("IsVisible") && !node.HasAttribute("IsHidden"));
        }
    }
}
