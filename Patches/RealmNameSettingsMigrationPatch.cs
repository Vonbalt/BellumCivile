using System;
using System.Reflection;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Xml;
using HarmonyLib;
using MCM.Abstractions.Base;

namespace BellumCivile.Patches
{
    [HarmonyPatch]
    internal static class RealmNameSettingsMigrationPatch
    {
        private static MethodBase TargetMethod() => AccessTools.Method(
            typeof(MCM.Implementation.BaseJsonSettingsFormat), "TryLoadFromJson");

        // MCM only reads annotated properties. Import the retired boolean before it
        // loads the selector, without rewriting the user's file or other settings.
        private static void Prefix(BaseSettings settings, string content)
        {
            if (!(settings is BellumCivileSettings bellum) || string.IsNullOrWhiteSpace(content)) return;
            try
            {
                var document = new XmlDocument { XmlResolver = null };
                using (var reader = JsonReaderWriterFactory.CreateJsonReader(Encoding.UTF8.GetBytes(content), XmlDictionaryReaderQuotas.Max))
                    document.Load(reader);
                if (document.SelectSingleNode("/root/RealmNameDisplay") != null) return;
                var legacy = document.SelectSingleNode("/root/UseSovereignTitlesAsRealmNames");
                if (legacy?.Attributes?["type"]?.Value == "boolean" && bool.TryParse(legacy.InnerText, out bool enabled))
                    bellum.UseSovereignTitlesAsRealmNames = enabled;
            }
            catch (Exception ex)
            {
                BellumCivileLogger.Log($"Could not migrate realm naming preference: {ex.GetType().Name}: {ex.Message}");
            }
        }
    }
}
