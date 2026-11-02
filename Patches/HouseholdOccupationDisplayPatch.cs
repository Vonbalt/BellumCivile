using BellumCivile.Behaviors;
using HarmonyLib;
using Helpers;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.ViewModelCollection;
using TaleWorlds.Localization;

namespace BellumCivile.Patches
{
    internal static class HouseholdOccupationDisplayPatch
    {
        private static bool _installed;

        // CampaignUIHelper must not be prepared before native game texts exist.
        internal static void Initialize()
        {
            if (_installed) return;
            var harmony = new Harmony("com.bellumcivile.household.occupationdisplay");
            try
            {
                harmony.Patch(AccessTools.Method(typeof(HeroHelper), nameof(HeroHelper.GetCharacterTypeName)),
                    postfix: new HarmonyMethod(typeof(HouseholdOccupationDisplayPatch), nameof(TypeName)));
                harmony.Patch(AccessTools.Method(typeof(CampaignUIHelper), nameof(CampaignUIHelper.GetHeroOccupationName)),
                    postfix: new HarmonyMethod(typeof(HouseholdOccupationDisplayPatch), nameof(OccupationName)));
                _installed = true;
            }
            catch { harmony.UnpatchAll(harmony.Id); throw; }
        }

        private static bool IsRetainer(Hero hero) => LordshipBehavior.Instance?.Ready == true
            && HouseholdMembership.IsRetainer(hero);

        private static TextObject Label() => new TextObject("{=BC_Lordship_Retainer}Retainer");

        private static void TypeName(Hero __0, ref TextObject __result)
        {
            if (IsRetainer(__0)) __result = Label();
        }

        private static void OccupationName(Hero __0, ref string __result)
        {
            if (IsRetainer(__0)) __result = Label().ToString();
        }
    }
}
