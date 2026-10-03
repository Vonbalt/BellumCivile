using System.Linq;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.LogEntries;
using TaleWorlds.CampaignSystem.ViewModelCollection.Encyclopedia.Items;
using TaleWorlds.CampaignSystem.ViewModelCollection.Encyclopedia.Pages;
using TaleWorlds.Localization;
using TaleWorlds.ObjectSystem;

namespace BellumCivile.Patches
{
    // Derived from the saved pact, not a rewritten vanilla capture entry. Earlier
    // ordinary captures remain historical facts after a hero becomes a hostage.
    internal sealed class HostageEncyclopediaLog : IEncyclopediaLog
    {
        private readonly HostagePactRecord _pact;
        private readonly Hero _hero;
        internal HostageEncyclopediaLog(HostagePactRecord pact, Hero hero) { _pact = pact; _hero = hero; }
        public CampaignTime GameTime => CampaignTime.Days((float)_pact.SignedDay);
        public bool IsVisibleInEncyclopediaPageOf(MBObjectBase obj) => obj == _hero;
        public TextObject GetEncyclopediaText()
        {
            if (_pact.Protects(_hero)) return HostagePactText.Status(_pact, _hero);
            var text = new TextObject("{=BC_Hostage_History}{HERO} was entrusted as a hostage to secure peace with {REALM}.");
            HostagePactText.Fill(text, _pact, _hero);
            return text;
        }
    }

    internal sealed class HostageHistoryEventVM : EncyclopediaHistoryEventVM
    {
        internal HostageHistoryEventVM(HostagePactRecord pact, Hero hero)
            : base(new HostageEncyclopediaLog(pact, hero)) { }
    }

    [HarmonyPatch(typeof(EncyclopediaHeroPageVM), nameof(EncyclopediaHeroPageVM.Refresh))]
    internal static class HostageEncyclopediaHistoryPatch
    {
        private static void Postfix(EncyclopediaHeroPageVM __instance)
        {
            if (__instance.History == null) return;
            foreach (var item in __instance.History.OfType<HostageHistoryEventVM>().ToList())
                __instance.History.Remove(item);
            if (__instance.IsInformationHidden || !(__instance.Obj is Hero hero)) return;
            var behavior = Campaign.Current?.GetCampaignBehavior<HostagePactBehavior>();
            if (behavior == null) return;
            foreach (var pact in behavior.GetHistory(hero).OrderBy(p => p.SignedDay))
                __instance.History.Insert(0, new HostageHistoryEventVM(pact, hero));
        }
    }
}
