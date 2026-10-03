using System.Reflection;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Localization;

namespace BellumCivile.Patches
{
    /// <summary>
    /// Why did I do this file?
    /// To restrict the in-game expel clan button so that only the ruling clan can call for an expulsion vote,
    /// mirroring the BellumCivile treason system where only the king may move against a vassal's loyalty
    /// when relations deteriorate to -60 or lower.
    /// </summary>
    [HarmonyPatch]
    public class KingdomExpelButtonPatch
    {
        private static readonly FieldInfo CurrentClanField = AccessTools.Field(
            "TaleWorlds.CampaignSystem.ViewModelCollection.KingdomManagement.Clans.KingdomClanVM:_currentSelectedClan");

        private static readonly FieldInfo ClanItemClanField = AccessTools.Field(
            "TaleWorlds.CampaignSystem.ViewModelCollection.KingdomManagement.Clans.KingdomClanItemVM:Clan");

        static MethodBase TargetMethod() =>
            AccessTools.Method("TaleWorlds.CampaignSystem.ViewModelCollection.KingdomManagement.Clans.KingdomClanVM:GetCanExpelCurrentClanWithReason");

        public static void Postfix(object __instance, ref bool __result, ref TextObject disabledReason)
        {
            if (!__result) return;

            if (Clan.PlayerClan?.Kingdom == null) return;

            if (Clan.PlayerClan != Clan.PlayerClan.Kingdom.RulingClan)
            {
                __result = false;
                disabledReason = new TextObject("{=BC_UI_ExpelDisabled}Only the ruler of the realm may call for a vote to expel a vassal lord.");
                return;
            }

            object currentClanVm = CurrentClanField?.GetValue(__instance);
            Clan targetClan = currentClanVm != null
                ? ClanItemClanField?.GetValue(currentClanVm) as Clan
                : null;
            IdeologyBehavior ideologyBehavior = Campaign.Current?.GetCampaignBehavior<IdeologyBehavior>();
            if (ideologyBehavior != null
                && !ideologyBehavior.CanRulerIndictClan(
                    Clan.PlayerClan.Kingdom,
                    targetClan,
                    false,
                    out TextObject explanation))
            {
                __result = false;
                disabledReason = explanation;
            }
        }
    }
}
