using System;
using System.IO;
using System.Reflection;
using System.Xml;
using Bannerlord.UIExtenderEx;
using BellumCivile.ViewModelMixin;
using HarmonyLib;

namespace BellumCivile.UI
{
    [HarmonyPatch]
    internal static class EncyclopediaFamilyPrefabFinalizationPatch
    {
        internal static bool Supported { get; private set; } = true;
        private static bool _unavailableLogged;

        private static bool Prepare()
        {
            Supported = TargetMethod() != null;
            if (!Supported && !_unavailableLogged)
            {
                _unavailableLogged = true;
                BellumCivileLogger.Log("[ENC-FAMILY] No supported prefab finalization hook in UIExtenderEx "
                    + typeof(UIExtender).Assembly.GetName().Version + ". Keeping native family and clan member lists.");
            }
            return Supported;
        }

        private static MethodBase TargetMethod() => FindTarget(typeof(UIExtender).Assembly.GetType(
            "Bannerlord.UIExtenderEx.Patches.WidgetPrefabPatch", false));

        internal static MethodInfo FindTarget(Type type)
        {
            if (type == null) return null;
            foreach (string name in new[] { "ApplyPatches", "ProcessMovie" })
            {
                foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly))
                {
                    var parameters = method.GetParameters();
                    if (method.Name == name && method.ReturnType == typeof(void) && !method.ContainsGenericParameters
                        && parameters.Length == 2 && parameters[0].ParameterType == typeof(string) && parameters[1].ParameterType == typeof(XmlDocument))
                        return method;
                }
            }
            return null;
        }

        private static void Cleanup(Exception __exception)
        {
            if (__exception == null) return;
            Supported = false;
            BellumCivileLogger.Log("[ENC-FAMILY] Prefab finalization hook failed; keeping native family and clan member lists: " + __exception);
        }

        private static void Postfix(string __0, XmlDocument __1, MethodBase __originalMethod)
            => FinalizePrefab(__originalMethod.Name == "ApplyPatches" ? __0 : Path.GetFileNameWithoutExtension(__0?.Replace('\\', '/')), __1);

        internal static void FinalizePrefab(string movieName, XmlDocument document)
        {
            // Run after every module's edits, but before UIExtender fingerprints or compiles the prefab.
            if (movieName == "EncyclopediaHeroPage") new EncyclopediaHeroFamilyPrefabPatch().Apply(document);
            if (movieName == "EncyclopediaClanPage") new EncyclopediaClanHouseholdPrefabPatch().Apply(document);
        }
    }
}
