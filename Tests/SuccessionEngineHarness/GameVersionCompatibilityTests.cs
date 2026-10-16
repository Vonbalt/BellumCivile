using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Xml;
using BellumCivile;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Incidents;

internal static class GameVersionCompatibilityTests
{
    private sealed class RelationModel : DefaultDiplomacyModel
    {
        internal int Calls;
        public override int GetEffectiveRelationChange(Hero first, Hero second, int change)
        { Calls++; return change + 7; }
    }

    internal static void Run(Action<bool, string> check, string game)
    {
        var mod = typeof(FeudalTitleRecord).Assembly;
        var failures = new List<string>();
        void Verify(bool success, string name)
        {
            if (success) check(true, name);
            else { failures.Add(name); Console.Error.WriteLine("CONTRACT FAILED: " + name); }
        }

        // Include reflected members that are read at runtime rather than patched at startup.
        foreach (var type in mod.GetTypes())
        foreach (var field in type.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (!field.IsInitOnly || !typeof(MemberInfo).IsAssignableFrom(field.FieldType)) continue;
            try { Verify(field.GetValue(null) != null, "Reflected member resolves: " + type.Name + "." + field.Name); }
            catch (Exception ex) { Verify(false, type.Name + "." + field.Name + ": " + ex.GetBaseException().Message); }
        }

        var inner = new RelationModel();
        var wrapped = new CivilWarDiplomacyModel(inner);
        Verify(wrapped.GetEffectiveRelationChange(null, null, 10) == 17 && inner.Calls == 1,
            "Diplomacy wrapper delegates beta relation modifiers exactly once");

        var runtime = mod.GetType("BellumCivile.CouncilAssignmentRuntimePatches", true);
        try
        {
            AccessTools.Method(runtime, "Initialize").Invoke(null, new object[] {
                FormatterServices.GetUninitializedObject(typeof(DefaultVillageProductionCalculatorModel)),
                FormatterServices.GetUninitializedObject(typeof(DefaultMobilePartyFoodConsumptionModel)),
                FormatterServices.GetUninitializedObject(typeof(DefaultPartySizeLimitModel)) });
            var patched = ((IEnumerable)AccessTools.Field(runtime, "PatchedMethods").GetValue(null)).Cast<MethodBase>().ToArray();
            Verify(patched.Length == 4, "All four council assignment model hooks install");
        }
        finally { new Harmony("com.bellumcivile.council.assignments").UnpatchAll("com.bellumcivile.council.assignments"); }

        var incidents = new CouncilIncidentBehavior();
        var contextType = mod.GetType("BellumCivile.CouncilIncidentContext", true);
        var qualityType = mod.GetType("BellumCivile.CouncilIncidentQuality", true);
        var context = Activator.CreateInstance(contextType, new object[] {
            incidents, null, null, PrivyCouncilOffice.Marshal, "marshal_organize_patrols", null, 50f,
            Enum.Parse(qualityType, "Success") });
        var definition = AccessTools.Method(mod.GetType("BellumCivile.CouncilIncidentRegistry", true), "GetDefinition")
            .Invoke(null, new object[] { "marshal_organize_patrols_bolster_watch" });
        var incident = (Incident)AccessTools.Method(typeof(CouncilIncidentBehavior), "BuildRuntimeIncident")
            .Invoke(incidents, new[] { definition, context });
        Verify(incident.NumOfOptions == 3 && incident.CanIncidentBeInvoked()
            && incident.TypeId == TaleWorlds.CampaignSystem.CampaignBehaviors.IncidentsCampaignBehaviour.IncidentType.FiefManagement,
            "Council incident retains its type, three choices and availability in the beta API");
        string hints = incident.GetOptionHint(0).Children.Single().Text.ToString();
        Verify(hints.Split('\n').Length == 4 && hints.Contains("50%") && hints.Contains("25%"),
            "Beta incident hint retains every weighted outcome and relation consequence");

        var assemblies = new List<Assembly> { mod };
        string naval = Path.Combine("bin", "Release", "net472", "BellumCivile.NavalDLCPatch.dll");
        if (Directory.Exists(Path.Combine(game, "Modules", "NavalDLC")))
        {
            Verify(File.Exists(naval), "War Sails companion has been built (run from repository root)");
            if (File.Exists(naval)) assemblies.Add(Assembly.LoadFrom(Path.GetFullPath(naval)));
        }
        var prefabs = new[] { "Native", "SandBox", "NavalDLC" }
            .Select(id => Path.Combine(game, "Modules", id, "GUI", "Prefabs"))
            .Where(Directory.Exists).SelectMany(path => Directory.EnumerateFiles(path, "*.xml", SearchOption.AllDirectories))
            .ToArray();
        foreach (var type in assemblies.SelectMany(a => a.GetTypes()))
        foreach (var attribute in type.GetCustomAttributesData())
        {
            if (attribute.AttributeType.Name == "PrefabExtensionAttribute")
            {
                string movie = (string)attribute.ConstructorArguments[0].Value;
                string xpath = (string)attribute.ConstructorArguments[1].Value;
                if (xpath == null) continue; // Custom document patches have separate behavioral tests.
                var paths = prefabs.Where(p => Path.GetFileNameWithoutExtension(p) == movie).ToArray();
                Verify(paths.Length > 0, "Native prefab exists: " + movie);
                foreach (string path in paths)
                {
                    var document = new XmlDocument(); document.Load(path);
                    Verify(document.SelectNodes(xpath).Count == 1, "UI anchor is unique: " + type.Name);
                }
                var factory = type.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .FirstOrDefault(m => m.GetCustomAttributesData().Any(a => a.AttributeType.Name == "PrefabExtensionXmlNodeAttribute"));
                if (factory != null)
                {
                    var xml = factory.Invoke(Activator.CreateInstance(type, true), null) as XmlDocument;
                    Verify(xml?.DocumentElement != null, "UI insertion XML is valid: " + type.Name);
                }
            }
            if (attribute.AttributeType.Name == "ViewModelMixinAttribute")
            {
                Type parent = type.BaseType;
                while (parent != null && (!parent.IsGenericType || parent.GetGenericTypeDefinition().Name != "BaseViewModelMixin`1"))
                    parent = parent.BaseType;
                string method = (string)attribute.ConstructorArguments[0].Value;
                Verify(parent != null && AccessTools.Method(parent.GetGenericArguments()[0], method) != null,
                    "View-model refresh hook resolves: " + type.Name);
            }
        }
        MapWidgetTooltipTests.Run(Verify);
        check(failures.Count == 0, "Game-version contracts: " + string.Join("; ", failures));
    }
}
