using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;

namespace BellumCivile.Patches
{
    [HarmonyPatch(typeof(CharacterRelationManager))]
    public static class DynamicRelationPatch
    {
        [HarmonyPatch(nameof(CharacterRelationManager.GetHeroRelation))]
        [HarmonyPrefix]
        public static bool GetHeroRelationPrefix(Hero hero1, Hero hero2, ref int __result)
        {
            if (hero1 != null && hero2 != null)
                return true;

            __result = 0;
            BellumCivileLogger.Log($"Guarded null CharacterRelationManager.GetHeroRelation call; hero1={hero1?.StringId ?? "null"}; hero2={hero2?.StringId ?? "null"}.");
            return false;
        }

        [HarmonyPatch(nameof(CharacterRelationManager.GetHeroRelation))]
        [HarmonyPostfix]
        public static void GetHeroRelationPostfix(Hero hero1, Hero hero2, ref int __result)
        {
            DynamicRelationBehavior behavior = DynamicRelationBehavior.Instance;
            if (behavior == null)
                return;

            __result = behavior.GetRelationForRead(hero1, hero2, __result);
        }

        [HarmonyPatch(nameof(CharacterRelationManager.SetHeroRelation))]
        [HarmonyPrefix]
        public static bool SetHeroRelationPrefix(Hero hero1, Hero hero2, out DynamicRelationSetState __state)
        {
            __state = default;
            if (hero1 != null && hero2 != null)
            {
                __state = DynamicRelationBehavior.Instance?.PrepareRelationSet(hero1, hero2) ?? default;
                return true;
            }

            BellumCivileLogger.Log($"Guarded null CharacterRelationManager.SetHeroRelation call; hero1={hero1?.StringId ?? "null"}; hero2={hero2?.StringId ?? "null"}.");
            return false;
        }

        [HarmonyPatch(nameof(CharacterRelationManager.SetHeroRelation))]
        [HarmonyPostfix]
        public static void SetHeroRelationPostfix(Hero hero1, Hero hero2, int value, DynamicRelationSetState __state)
        {
            DynamicRelationBehavior.Instance?.CaptureRelationSet(hero1, hero2, value, __state);
        }
    }
}
