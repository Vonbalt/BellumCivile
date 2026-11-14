using System;
using System.Linq;
using System.Reflection;
using Bannerlord.UIExtenderEx.ViewModels;
using HarmonyLib;
using TaleWorlds.CampaignSystem.ViewModelCollection.KingdomManagement.Diplomacy;
using TaleWorlds.Core.ViewModelCollection.Information;
using TaleWorlds.Library;

namespace BellumCivile.UI.Diplomacy
{
    // Diplomacy's direct war/peace and pact buttons overlap Bellum's own actions. The distinct
    // alliance cancellation is retained through the mod's original mixin and callback.
    internal static class DiplomacyExternalActionAdapter
    {
        private static readonly MethodInfo GetMixin = typeof(ViewModelMixins).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .FirstOrDefault(method => method.Name == "Get" && method.IsGenericMethodDefinition && method.GetParameters().Length == 1);

        internal sealed class AllianceAction
        {
            internal object Mixin;
            internal string Name;
            internal string Explanation;
            internal string Reason;
            internal bool Enabled;
        }

        private static object FindMixin(KingdomDiplomacyItemVM item)
        {
            string name = item is KingdomTruceItemVM ? "Diplomacy.ViewModelMixin.KingdomTruceItemVMMixin"
                : item is KingdomWarItemVM ? "Diplomacy.ViewModelMixin.KingdomWarItemVMMixin" : null;
            if (name == null || GetMixin == null) return null;
            try
            {
                Type type = AccessTools.TypeByName(name);
                if (type == null || AccessTools.Method(type, "ExecuteDirectAction") == null
                    || AccessTools.Method(type, "UpdateActionAvailability") == null
                    || AccessTools.Property(type, "IsDirectActionVisible") == null
                    || AccessTools.Property(type, "IsDirectActionEnabled") == null
                    || AccessTools.Property(type, "DirectActionName") == null
                    || AccessTools.Property(type, "DirectActionExplanationText") == null
                    || AccessTools.Property(type, "DirectActionHint") == null)
                    return null;
                if (item is KingdomTruceItemVM && (AccessTools.Method(type, "ProposeNonAggressionPact") == null
                    || AccessTools.Property(type, "IsNonAggressionPactVisible") == null)) return null;
                return GetMixin.MakeGenericMethod(type).Invoke(null, new object[] { item });
            }
            catch (Exception ex)
            {
                BellumCivileLogger.Log("Could not access Diplomacy action mixin: " + ex.GetType().Name + ": " + ex.Message);
                return null;
            }
        }

        internal static bool CanReplace(KingdomDiplomacyItemVM item)
        {
            object mixin = FindMixin(item);
            if (mixin == null) return false;
            try
            {
                Type type = mixin.GetType();
                // Keep the original controls if the installed version cannot provide the data
                // needed by the adapter, including the distinct break-alliance action.
                AccessTools.Property(type, "IsDirectActionVisible").GetValue(mixin);
                AccessTools.Property(type, "IsDirectActionEnabled").GetValue(mixin);
                AccessTools.Property(type, "DirectActionName").GetValue(mixin);
                AccessTools.Property(type, "DirectActionExplanationText").GetValue(mixin);
                AccessTools.Property(type, "DirectActionHint").GetValue(mixin);
                if (item is KingdomTruceItemVM truce && truce.HasAlliance)
                    return GetAllianceAction(item, false) != null;
                return true;
            }
            catch { return false; }
        }

        internal static AllianceAction GetAllianceAction(KingdomDiplomacyItemVM item, bool refresh)
        {
            if (!(item is KingdomTruceItemVM truce) || !truce.HasAlliance) return null;
            object mixin = FindMixin(item);
            if (mixin == null) return null;
            try
            {
                Type type = mixin.GetType();
                if (refresh) AccessTools.Method(type, "UpdateActionAvailability").Invoke(mixin, null);
                if (!(bool)AccessTools.Property(type, "IsDirectActionVisible").GetValue(mixin)) return null;
                var hint = AccessTools.Property(type, "DirectActionHint").GetValue(mixin) as HintViewModel;
                string name = AccessTools.Property(type, "DirectActionName").GetValue(mixin) as string;
                if (string.IsNullOrWhiteSpace(name)) return null;
                return new AllianceAction
                {
                    Mixin = mixin,
                    Name = name,
                    Explanation = AccessTools.Property(type, "DirectActionExplanationText").GetValue(mixin) as string,
                    Reason = hint?.HintText?.ToString(),
                    Enabled = (bool)AccessTools.Property(type, "IsDirectActionEnabled").GetValue(mixin)
                };
            }
            catch (Exception ex)
            {
                BellumCivileLogger.Log("Could not read Diplomacy alliance action: " + ex.GetType().Name + ": " + ex.Message);
                return null;
            }
        }

        internal static void Execute(AllianceAction action)
            => AccessTools.Method(action.Mixin.GetType(), "ExecuteDirectAction").Invoke(action.Mixin, null);
    }
}
