using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using System.Xml;
using Bannerlord.UIExtenderEx;
using Bannerlord.UIExtenderEx.Attributes;
using Bannerlord.UIExtenderEx.Prefabs;
using PrefabExtensionSetAttributePatch = Bannerlord.UIExtenderEx.Prefabs2.PrefabExtensionSetAttributePatch;
using InsertPatch = Bannerlord.UIExtenderEx.Prefabs2.PrefabExtensionInsertPatch;
using InsertType = Bannerlord.UIExtenderEx.Prefabs2.InsertType;
using BellumCivile;
using HarmonyLib;

internal sealed class EncyclopediaFamilyCompatibilityTests : IDisposable
{
    private static readonly Assembly Core = typeof(FeudalTitleRecord).Assembly;
    private static readonly Type Policy = Core.GetType("BellumCivile.UI.EncyclopediaFamilyLayoutCompatibility", true);
    private static IEnumerable _runtimes = Array.Empty<object>();
    private readonly Harmony _harmony = new Harmony("bellum.tests.family_compatibility");
    private XmlDocument _native;

    internal EncyclopediaFamilyCompatibilityTests()
    {
        _harmony.Patch(AccessTools.Method(Policy, "GetRuntimes"),
            prefix: new HarmonyMethod(typeof(EncyclopediaFamilyCompatibilityTests), nameof(Runtimes)));
    }

    private static bool Runtimes(ref IEnumerable __result) { __result = _runtimes; return false; }
    private static bool Enabled => (bool)AccessTools.Property(Policy, "Enabled").GetValue(null);
    private static void Apply(XmlDocument document)
    {
        var type = Core.GetType("BellumCivile.ViewModelMixin.EncyclopediaHeroFamilyPrefabPatch", true);
        AccessTools.Method(type, "Apply").Invoke(Activator.CreateInstance(type, true), new object[] { document });
    }
    private XmlDocument Page() => (XmlDocument)_native.CloneNode(true);

    private static void FinalizePrefab(XmlDocument document) => AccessTools.Method(
        Core.GetType("BellumCivile.UI.EncyclopediaFamilyPrefabFinalizationPatch", true), "Postfix")
        .Invoke(null, new object[] { "EncyclopediaHeroPage", document });

    internal void SetSharedLayout(bool shared)
    {
        _runtimes = shared ? new[] { Runtime(typeof(LineageEditor), true) } : Array.Empty<object>();
        Apply(Page());
    }

    internal static void AddonProbe(Action<bool, string> check, string game, string moduleDirectory)
    {
        using var environment = new EncyclopediaFamilyCompatibilityTests();
        environment._native = new XmlDocument();
        environment._native.Load(Path.Combine(game, "Modules/SandBox/GUI/Prefabs/Encyclopedia/EncyclopediaSubPages/EncyclopediaHeroPage.xml"));
        string dll = Path.Combine(moduleDirectory, "bin/Win64_Shipping_Client/Bannerlord.FamilyTree.v1.4.8.dll");
        var addon = Assembly.LoadFrom(dll);
        check(addon.GetType("Bannerlord.FamilyTree.Patches.EncyclopediaHeroPageVMPatch3") == null,
            "The supported Family Tree build has no alternating native-refresh blocker");
        var runtimes = new List<object>();
        foreach (var type in addon.GetTypes())
        {
            var extensions = type.GetCustomAttributes<PrefabExtensionAttribute>(false).Where(a => a.Movie == "EncyclopediaHeroPage").ToArray();
            if (extensions.Length == 0) continue;
            var patch = (InsertPatch)Activator.CreateInstance(type, true);
            var content = type.GetProperties().Single(p => p.GetCustomAttributes(false)
                .Any(a => a.GetType().Name == "PrefabExtensionFileNameAttribute"));
            string file = Path.Combine(moduleDirectory, "GUI/Prefabs/Encyclopedia/EncyclopediaSubPages", (string)content.GetValue(patch) + ".xml");
            var payload = new XmlDocument(); payload.Load(file);
            runtimes.Add(Runtime(type, true, document => {
                foreach (var extension in extensions)
                {
                    var target = document.SelectSingleNode(extension.XPath)
                        ?? throw new Exception("Missing actual Family Tree target: " + extension.XPath);
                    var inserted = document.ImportNode(payload.DocumentElement, true);
                    if (patch.Type == InsertType.Replace) target.ParentNode.ReplaceChild(inserted, target);
                    else if (patch.Type == InsertType.Append) target.ParentNode.InsertAfter(inserted, target);
                    else throw new Exception("Unexpected actual Family Tree insertion kind: " + patch.Type);
                }
            }));
        }
        check(runtimes.Count == 6, "All six extensions from the actual supported Family Tree DLL are tested");
        _runtimes = runtimes;
        foreach (bool foreignFirst in new[] { false, true })
        {
            var document = environment.Page();
            var titlesType = Core.GetType("BellumCivile.ViewModelMixin.EncyclopediaHeroFeudalTitlesPrefabExtension", true);
            var titles = (XmlDocument)AccessTools.Method(titlesType, "GetPrefabExtension")
                .Invoke(Activator.CreateInstance(titlesType, true), null);
            var allies = document.SelectSingleNode("//*[@Id='AlliesDivider']");
            allies.ParentNode.ReplaceChild(document.ImportNode(titles.DocumentElement, true), allies);
            if (foreignFirst) foreach (var runtime in runtimes) Process(runtime, document);
            Apply(document);
            if (!foreignFirst) foreach (var runtime in runtimes) Process(runtime, document);
            FinalizePrefab(document);
            check(Enabled && document.SelectNodes("//*[@Id='BellumFamilySection']").Count == 1
                && document.SelectNodes("//*[@Id='FamilyTreeWidget']").Count == 1,
                "Bellum's grouped family and the actual Family Tree coexist: foreign first=" + foreignFirst);
            check(document.SelectSingleNode("//*[@Id='FamilyGrid' and @IsHidden='true' and @DataSource='{Family}']") != null
                && document.SelectSingleNode("//*[@Id='FamilyDivider' and @IsHidden='true']") != null,
                "The old flat family list is hidden but its original extension anchors remain: " + foreignFirst);
            check(document.SelectSingleNode("//*[@Id='FamilyTreeWidget']/ancestor::*[@DataSource='{BellumFamily}']") == null
                && document.SelectSingleNode("//*[@Id='FamilyTreeWidget' and @IsHidden]") == null,
                "The actual tree keeps the native page's data context and remains visible: " + foreignFirst);
            check(document.SelectNodes("//*[@Id='BellumTitlesDivider' or @Id='BellumClaimsDivider' or @Id='BellumInheritanceDivider']").Count == 3,
                "Other Bellum sections remain present with the actual Family Tree build: " + foreignFirst);
        }
    }

    internal static void RegistryProbe(Action<bool, string> check, string game)
    {
        UIExtender extender = null;
        var harmony = new Harmony("bellum.tests.family_finalization");
        try
        {
            var native = new XmlDocument();
            native.Load(Path.Combine(game, "Modules/SandBox/GUI/Prefabs/Encyclopedia/EncyclopediaSubPages/EncyclopediaHeroPage.xml"));
            var previouslyOwned = (XmlDocument)native.CloneNode(true);
            Apply(previouslyOwned);
            check(Enabled, "The real UIExtender registry can be inspected without a mock or a running campaign");
            harmony.CreateClassProcessor(Core.GetType("BellumCivile.UI.EncyclopediaFamilyPrefabFinalizationPatch", true)).Patch();
            extender = UIExtender.Create("bellum.tests.family.registry");
            extender.Register(new[] { typeof(RegistryFamilyEditor) });
            extender.Enable();
            AccessTools.Method(typeof(UIExtender).Assembly.GetType("Bannerlord.UIExtenderEx.Patches.WidgetPrefabPatch", true), "ApplyPatches")
                .Invoke(null, new object[] { "EncyclopediaHeroPage", previouslyOwned });
            check(!Enabled && previouslyOwned.SelectSingleNode("//*[@Id='FamilyGrid' and @MarginTop='31' and not(@IsHidden)]") != null,
                "Actual UIExtender patch completion restores shared visibility before prefab compilation");
            var document = (XmlDocument)native.CloneNode(true);
            Apply(document);
            check(!Enabled && document.OuterXml == native.OuterXml,
                "An actual enabled foreign UIExtender registration restores the shared layout");
            extender.Disable(typeof(RegistryFamilyEditor));
            Apply((XmlDocument)native.CloneNode(true));
            check(Enabled, "Disabling the actual foreign patch returns layout ownership to Bellum");
        }
        finally
        {
            extender?.Deregister();
            harmony.UnpatchAll(harmony.Id);
        }
    }

    internal void Run(Action<bool, string> check, string game)
    {
        _native = new XmlDocument();
        _native.Load(Path.Combine(game, "Modules/SandBox/GUI/Prefabs/Encyclopedia/EncyclopediaSubPages/EncyclopediaHeroPage.xml"));
        check(AccessTools.Property(Core.GetType("BellumCivile.BellumCivileSettings"), "EnableEncyclopediaFamilyLayout") == null,
            "Family compatibility is automatic rather than controlled by an MCM toggle");

        var document = Page();
        Apply(document);
        check(Enabled && document.SelectNodes("//*[@IsHidden='true' and (@Id='FamilyGrid' or @Id='FamilyDivider' or @ScopeParent='..\\FamilyGrid')]").Count == 3,
            "Standalone Bellum hides native family presentation without removing its anchors");
        CheckDividerGuard(check);
        foreach (Type type in new[] { typeof(TreeEditor), typeof(LineageEditor), typeof(ScopeEditor), typeof(OpaqueEditor), typeof(ProgrammaticEditor) })
        {
            foreach (bool foreignFirst in new[] { false, true })
            {
                document = Page();
                var runtime = Runtime(type, true);
                _runtimes = new[] { runtime };
                if (foreignFirst) Process(runtime, document);
                string original = document.OuterXml;
                Apply(document);
                bool composable = type == typeof(TreeEditor) || type == typeof(ScopeEditor);
                check(Enabled == composable && (composable || document.OuterXml == original),
                    "Bellum keeps additive extensions and yields to replacements: " + type.Name + ", foreign first=" + foreignFirst);
                if (!foreignFirst) Process(runtime, document);
                FinalizePrefab(document);
                check(Enabled == composable, "Final prefab ownership is independent of extension order: " + type.Name + ", foreign first=" + foreignFirst);
                if (type == typeof(TreeEditor))
                    check(document.SelectSingleNode("//*[@Id='ForeignFamilyTree']") != null
                        && document.SelectSingleNode("//*[@Id='FamilyGrid']") != null,
                        "Append-style family trees retain their original grid and inserted tree: " + foreignFirst);
                if (type == typeof(LineageEditor))
                    check(document.SelectSingleNode("//*[@Id='ForeignRelationshipSections']") != null
                        && document.SelectSingleNode("//*[@Id='ForeignLineageDivider']") != null,
                        "Replacement-style lineage panels replace all native targets without missing nodes: " + foreignFirst);
            }
        }

        foreach (Type type in new[] { typeof(UnrelatedEditor), typeof(OtherMovieEditor),
            Core.GetType("BellumCivile.ViewModelMixin.EncyclopediaHeroFeudalTitlesPrefabExtension", true) })
        {
            _runtimes = new[] { Runtime(type, true) };
            Apply(Page());
            check(Enabled, "Unrelated or Bellum-owned extensions do not disable family grouping: " + type.Name);
        }
        _runtimes = new[] { Runtime(typeof(TreeEditor), false) };
        Apply(Page());
        check(Enabled, "Registered but disabled family extensions do not trigger a fallback");

        _runtimes = new[] { new object() };
        document = Page(); string unchanged = document.OuterXml; Apply(document);
        check(!Enabled && document.OuterXml == unchanged, "Unknown UIExtender registry shapes fail safely to the shared family layout");

        _runtimes = Array.Empty<object>();
        document = Page();
        ((XmlElement)document.SelectSingleNode("//*[@Id='FamilyGrid']")).SetAttribute("IsVisible", "@ForeignFamilyVisibility");
        unchanged = document.OuterXml; Apply(document);
        check(!Enabled && document.OuterXml == unchanged, "Unrecognized family visibility bindings are not overwritten");

        foreach (var target in new[] {
            new[] { "descendant::TextWidget[@Text='@NameText']", "False" },
            new[] { "//ListPanel[Children/EncyclopediaDivider[@Id='AlliesDivider']]", "True" },
            new[] { "//NavigatableGridWidget[@Id='FamilyGrid']/ItemTemplate", "True" },
            new[] { "/", "True" }, new[] { "", "True" } })
            check((bool)AccessTools.Method(Policy, "TargetsFamily").Invoke(null, new object[] { target[0], _native }) == bool.Parse(target[1]),
                "Family extension classification handles bounded, ancestor and opaque targets: " + target[0]);

        document = Page(); Apply(document);
        _runtimes = new[] { Runtime(typeof(LineageEditor), true) };
        Apply(document);
        check(!Enabled && document.SelectNodes("//*[@Id='FamilyGrid' or @Id='FamilyDivider']").Count == 2
            && document.SelectNodes("//*[@IsHidden='true']").Count == 0,
            "A later registration can restore native visibility without reconstructing deleted anchors");
        _runtimes = Array.Empty<object>();
        Apply(Page());
    }

    private static object Runtime(Type patchType, bool enabled, Action<XmlDocument> compose = null)
    {
        var ui = typeof(UIExtender).Assembly;
        var runtimeType = ui.GetType("Bannerlord.UIExtenderEx.UIExtenderRuntime", true);
        var componentType = ui.GetType("Bannerlord.UIExtenderEx.Components.PrefabComponent", true);
        var runtime = FormatterServices.GetUninitializedObject(runtimeType);
        var component = Activator.CreateInstance(componentType, new object[] { "family-fixture" });
        AccessTools.Field(runtimeType, "PrefabComponent").SetValue(runtime, component);
        string movie = patchType == typeof(OtherMovieEditor) ? "EncyclopediaClanPage" : "EncyclopediaHeroPage";
        Action<XmlDocument> patch = compose ?? (document => Compose(patchType, document));
        AccessTools.Method(componentType, "RegisterPatch", new[] { typeof(string), typeof(Type), typeof(Action<XmlDocument>) })
            .Invoke(component, new object[] { movie, patchType, patch });
        ((IDictionary)AccessTools.Field(componentType, "_enabledPatches").GetValue(component))[patchType] = enabled;
        return runtime;
    }

    private static bool _dividerHidden;
    private static bool HideDivider(bool value) { _dividerHidden = value; return false; }

    private void CheckDividerGuard(Action<bool, string> check)
    {
        var patchType = Core.GetType("BellumCivile.UI.EncyclopediaNativeFamilyDividerPatch", true);
        var target = (MethodBase)AccessTools.Method(patchType, "TargetMethod").Invoke(null, null);
        check(PatchProcessor.GetOriginalInstructions(target).Any(i => i.operand is MethodInfo method && method.Name == "set_IsVisible"),
            "Native divider late update can unhide a populated family grid's heading");
        var divider = FormatterServices.GetUninitializedObject(target.DeclaringType);
        var widget = target.DeclaringType.BaseType;
        var setter = AccessTools.PropertySetter(widget, "IsHidden");
        setter = AccessTools.Method(setter.DeclaringType, setter.Name);
        _harmony.Patch(setter, prefix: new HarmonyMethod(typeof(EncyclopediaFamilyCompatibilityTests), nameof(HideDivider)));
        try
        {
            AccessTools.Field(widget, "_id").SetValue(divider, "FamilyDivider");
            var prefix = AccessTools.Method(patchType, "Prefix");
            _dividerHidden = false;
            check(!(bool)prefix.Invoke(null, new[] { divider }) && _dividerHidden,
                "Bellum-owned native family headings stay hidden without replacing their widget type");
            AccessTools.Field(widget, "_id").SetValue(divider, "AlliesDivider");
            check((bool)prefix.Invoke(null, new[] { divider }), "Other encyclopedia headings keep their native late update");
            AccessTools.Field(widget, "_id").SetValue(divider, "FamilyDivider");
            SetSharedLayout(true);
            check((bool)prefix.Invoke(null, new[] { divider }), "Shared native and foreign family headings keep native behavior in compatibility mode");
            SetSharedLayout(false);
        }
        finally { _harmony.Unpatch(setter, HarmonyPatchType.Prefix, _harmony.Id); }
    }

    private static void Process(object runtime, XmlDocument document)
    {
        object component = AccessTools.Field(runtime.GetType(), "PrefabComponent").GetValue(runtime);
        AccessTools.Method(component.GetType(), "ProcessMovieIfNeeded")
            .Invoke(component, new object[] { "EncyclopediaHeroPage", document });
    }

    private static void Compose(Type type, XmlDocument document)
    {
        if (type != typeof(TreeEditor) && type != typeof(LineageEditor)) return;
        var grid = document.SelectSingleNode("//NavigatableGridWidget[@Id='FamilyGrid']")
            ?? throw new Exception("Foreign family editor lost its native grid target");
        var scope = document.SelectSingleNode("//NavigationScopeTargeter[@ScopeID='EncyclopediaHeroFamilyContentScope']")
            ?? throw new Exception("Foreign family editor lost its native navigation target");
        if (type == typeof(TreeEditor))
        {
            var tree = document.CreateElement("Widget"); tree.SetAttribute("Id", "ForeignFamilyTree");
            grid.ParentNode.InsertAfter(tree, grid);
            ((XmlElement)scope).SetAttribute("DownNavigationScope", "ForeignTreeScope");
            return;
        }
        var divider = document.SelectSingleNode("//EncyclopediaDivider[@Id='FamilyDivider']")
            ?? throw new Exception("Foreign lineage editor lost its native divider target");
        var replacement = document.CreateElement("Widget"); replacement.SetAttribute("Id", "ForeignRelationshipSections");
        grid.ParentNode.ReplaceChild(replacement, grid);
        var heading = document.CreateElement("EncyclopediaDivider"); heading.SetAttribute("Id", "ForeignLineageDivider");
        divider.ParentNode.ReplaceChild(heading, divider);
        ((XmlElement)scope).SetAttribute("ScopeParent", "..\\ForeignRelationshipSections");
    }

    public void Dispose()
    {
        _runtimes = Array.Empty<object>();
        if (_native != null) Apply(Page());
        _harmony.UnpatchAll(_harmony.Id);
    }

    [PrefabExtension("EncyclopediaHeroPage", "descendant::NavigatableGridWidget[@Id='FamilyGrid']")]
    private sealed class TreeEditor : InsertPatch { public override InsertType Type => InsertType.Append; }
    [PrefabExtension("EncyclopediaHeroPage", "descendant::NavigatableGridWidget[@Id='FamilyGrid']")]
    [PrefabExtension("EncyclopediaHeroPage", "descendant::EncyclopediaDivider[@Id='FamilyDivider']")]
    private sealed class LineageEditor : InsertPatch { public override InsertType Type => InsertType.Replace; }
    [PrefabExtension("EncyclopediaHeroPage", "descendant::NavigationScopeTargeter[@ScopeID='EncyclopediaHeroFamilyContentScope']")]
    private sealed class ScopeEditor : InsertPatch { public override InsertType Type => InsertType.Replace; }
    [PrefabExtension("EncyclopediaHeroPage", null)]
    private sealed class OpaqueEditor : CustomPatch<XmlDocument>
    {
        public override string Id => "opaque-fixture";
        public override void Apply(XmlDocument document) { }
    }
    private sealed class ProgrammaticEditor { }
    [PrefabExtension("EncyclopediaHeroPage", "descendant::TextWidget[@Text='@NameText']")]
    private sealed class UnrelatedEditor { }
    [PrefabExtension("EncyclopediaClanPage", "descendant::NavigatableGridWidget[@Id='FamilyGrid']")]
    private sealed class OtherMovieEditor { }

    [PrefabExtension("EncyclopediaHeroPage", "descendant::NavigatableGridWidget[@Id='FamilyGrid']")]
    public sealed class RegistryFamilyEditor : PrefabExtensionSetAttributePatch
    {
        public override List<PrefabExtensionSetAttributePatch.Attribute> Attributes
            => new List<PrefabExtensionSetAttributePatch.Attribute> { new PrefabExtensionSetAttributePatch.Attribute("MarginTop", "31") };
    }
}
