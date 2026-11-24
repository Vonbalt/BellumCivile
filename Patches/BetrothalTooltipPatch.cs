using System;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.ViewModelCollection;
using TaleWorlds.Core.ViewModelCollection.Information;
using TaleWorlds.Localization;

namespace BellumCivile.Patches
{
    internal static class BetrothalTooltipPatch
    {
        private static bool _installed;

        internal static void Initialize()
        {
            if (_installed) return;
            var harmony = new Harmony("com.bellumcivile.betrothal_tooltip");
            try
            {
                harmony.Patch(AccessTools.Method(typeof(TooltipRefresherCollection), "RefreshHeroTooltip"),
                    postfix: new HarmonyMethod(typeof(BetrothalTooltipPatch), nameof(Postfix)));
                _installed = true;
            }
            catch (Exception ex) { BellumCivileLogger.Log("Betrothal tooltip unavailable: " + ex); }
        }

        private static void Postfix(PropertyBasedTooltipVM __0, object[] __1)
        {
            if (__1 == null || __1.Length == 0 || !(__1[0] is Hero hero) || !hero.IsChild
                || CampaignUIHelper.IsHeroInformationHidden(hero, out _)) return;
            var pledge = BetrothalBehavior.Instance?.Find(hero);
            if (pledge?.Accepted != true) return;
            __0.AddProperty(new TextObject("{=BC_Betrothal_Partner}Betrothed to").ToString(), pledge.PartnerOf(hero).Name.ToString());
            __0.AddProperty(new TextObject("{=BC_Betrothal_WeddingExpected}Wedding expected").ToString(), pledge.ExpectedDate(BetrothalBehavior.Adulthood).ToString());
        }
    }
}
