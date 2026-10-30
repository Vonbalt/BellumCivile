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
        private static MethodBase TargetMethod() => AccessTools.Method(typeof(UIExtender).Assembly.GetType(
            "Bannerlord.UIExtenderEx.Patches.WidgetPrefabPatch", true), "ApplyPatches");

        private static void Postfix(string movieName, XmlDocument document)
        {
            // Run after every module's edits, but before UIExtender fingerprints or compiles the prefab.
            if (movieName == "EncyclopediaHeroPage") new EncyclopediaHeroFamilyPrefabPatch().Apply(document);
        }
    }
}
