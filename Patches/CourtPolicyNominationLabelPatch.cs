using System.Reflection;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.ViewModelCollection.KingdomManagement.Policies;
using TaleWorlds.Localization;

namespace BellumCivile.Patches
{
    [HarmonyPatch(typeof(KingdomPoliciesVM), "OnPolicySelect")]
    public static class CourtPolicyNominationLabelPatch
    {
        private static readonly FieldInfo CurrentDecision = AccessTools.Field(typeof(KingdomPoliciesVM), "_currentItemsUnresolvedDecision");

        public static void Postfix(KingdomPoliciesVM __instance)
        {
            var calendar = CourtAgendaBehavior.Current;
            var realm = Clan.PlayerClan?.Kingdom;
            if (calendar?.IsNominationOpen(realm) != true || CurrentDecision == null
                || CurrentDecision.GetValue(__instance) != null || __instance.CurrentSelectedPolicy == null) return;
            __instance.ProposeOrDisavowText = new TextObject("{=BC_CourtNominatePolicy}Nominate").ToString();
            var explanation = new TextObject("{=BC_CourtNominationExplanation}Nominate this motion for your faction's session on {DATE}. No influence is spent now; filing is paid when deliberations begin.");
            explanation.SetTextVariable("DATE", calendar.GetPlayerTermAgenda(realm).SessionDate.ToString());
            __instance.ProposeActionExplanationText = explanation.ToString();
        }
    }
}
