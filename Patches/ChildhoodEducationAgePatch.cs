using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.MapNotificationTypes;
using TaleWorlds.CampaignSystem.ViewModelCollection.Map.MapNotificationTypes;

namespace BellumCivile.Patches
{
    [HarmonyPatch(typeof(EducationCampaignBehavior), "ChildStateToAge")]
    internal static class ChildhoodEducationAgePatch
    {
        private static void Postfix(object __0, ref int __result)
        {
            if (BellumCivileOptions.EnableCustomAdulthoodAge)
                __result = BellumCivileOptions.EducationMilestoneAge(Convert.ToInt32(__0));
        }
    }

    [HarmonyPatch(typeof(EducationCampaignBehavior), "GetClosestStage")]
    internal static class ChildhoodEducationSequencePatch
    {
        private static readonly AccessTools.FieldRef<EducationCampaignBehavior, Dictionary<Hero, short>> PreviousEducations =
            AccessTools.FieldRefAccess<EducationCampaignBehavior, Dictionary<Hero, short>>("_previousEducations");

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            Label vanilla = generator.DefineLabel();
            yield return new CodeInstruction(OpCodes.Call, AccessTools.PropertyGetter(typeof(BellumCivileOptions), nameof(BellumCivileOptions.EnableCustomAdulthoodAge)));
            yield return new CodeInstruction(OpCodes.Brfalse, vanilla);
            yield return new CodeInstruction(OpCodes.Ldarg_0);
            yield return new CodeInstruction(OpCodes.Ldarg_1);
            yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(ChildhoodEducationSequencePatch), nameof(NextStage)));
            yield return new CodeInstruction(OpCodes.Ret);
            bool first = true;
            foreach (CodeInstruction instruction in instructions)
            {
                if (first)
                {
                    instruction.labels.Add(vanilla);
                    first = false;
                }
                yield return instruction;
            }
        }

        internal static short NextStage(EducationCampaignBehavior behavior, Hero child)
        {
            // Saved progress is a stage index, not an age. Keep it intact when schedules change.
            return PreviousEducations(behavior).TryGetValue(child, out short completed)
                ? (short)Math.Max(0, Math.Min(6, completed + 1)) : (short)0;
        }
    }

    [HarmonyPatch(typeof(EducationCampaignBehavior), "HasNotificationForAge")]
    internal static class ChildhoodEducationPendingNotificationPatch
    {
        private static bool Prefix(Hero child, ref bool __result)
        {
            if (!BellumCivileOptions.EnableCustomAdulthoodAge)
                return true;

            // A saved notice may carry an old milestone age; it still opens the next unfinished stage.
            __result = Campaign.Current.CampaignInformationManager.InformationDataExists(
                (EducationMapNotification notice) => notice.Child == child);
            return false;
        }
    }

    [HarmonyPatch(typeof(EducationCampaignBehavior), "IsValidEducationNotification")]
    internal static class ChildhoodEducationNotificationValidityPatch
    {
        private static void Postfix(EducationCampaignBehavior __instance, EducationMapNotification data, ref bool __result)
        {
            if (!__result || !BellumCivileOptions.EnableCustomAdulthoodAge)
                return;
            int stage = ChildhoodEducationSequencePatch.NextStage(__instance, data.Child);
            int age = BellumCivileOptions.EducationMilestoneAge(stage);
            __result = age > 0 && data.Child.Age >= age;
        }
    }

    [HarmonyPatch(typeof(EducationNotificationItemVM), "OnEducationCompletedForChild")]
    internal static class ChildhoodEducationNotificationCompletionPatch
    {
        private static void Prefix(ref int age)
        {
            // Remove the child's old notice even when its saved age exceeds the newly configured age.
            if (BellumCivileOptions.EnableCustomAdulthoodAge)
                age = int.MaxValue;
        }
    }
}
