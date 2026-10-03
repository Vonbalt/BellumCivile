using HarmonyLib;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.Localization;

namespace BellumCivile.Patches
{
    [HarmonyPatch(typeof(EducationCampaignBehavior), "GetPageProperties")]
    internal static class ChildhoodEducationTextPatch
    {
        private static void Postfix(ref TextObject description)
        {
            if (!BellumCivileOptions.EnableCustomAdulthoodAge || description?.GetID() != "3O1Pg3Ie")
                return;

            description = new TextObject("{=BC_Education_BirthdayGift}When {CHILD.NAME} turned {EDUCATION_AGE}, you gave {?CHILD.GENDER}her{?}him{\\?} a special present. You have seen {?CHILD.GENDER}her{?}him{\\?} treasure it and believe it will shape who {?CHILD.GENDER}she{?}he{\\?} is. You gave {?CHILD.GENDER}her{?}him{\\?}...");
            description.SetTextVariable("EDUCATION_AGE", BellumCivileOptions.EducationMilestoneAge(4));
        }
    }
}
