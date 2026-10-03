using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.Localization;
using BellumCivile.Behaviors;

namespace BellumCivile.Patches
{
    /// <summary>
    /// Why did I do this file?
    /// To block the vanilla AI from declaring "White Peace" between a parent kingdom and its active civil war rebellion, forcing the conflict to a decisive conclusion.
    /// </summary>
    [HarmonyPatch(typeof(DefaultDiplomacyModel), "GetScoreOfDeclaringPeace")]
    public class BlockWhitePeacePatch
    {
        public static void Postfix(IFaction factionDeclaresPeace, IFaction factionDeclaredPeace, ref float __result)
        {
            if (factionDeclaresPeace != null && factionDeclaredPeace != null && 
                factionDeclaresPeace.IsKingdomFaction && factionDeclaredPeace.IsKingdomFaction)
            {
                FactionManagerBehavior factionManager = Campaign.Current?.GetCampaignBehavior<FactionManagerBehavior>();
                Kingdom declaringKingdom = factionDeclaresPeace as Kingdom;
                Kingdom declaredKingdom = factionDeclaredPeace as Kingdom;

                if (factionManager != null && declaringKingdom != null && declaredKingdom != null)
                {
                    FactionObject firstRebelFaction = factionManager.GetFactionByRebelKingdom(declaringKingdom);
                    if (firstRebelFaction != null && firstRebelFaction.ParentKingdom == declaredKingdom)
                    {
                        __result = -9999999f;
                        return;
                    }

                    FactionObject secondRebelFaction = factionManager.GetFactionByRebelKingdom(declaredKingdom);
                    if (secondRebelFaction != null && secondRebelFaction.ParentKingdom == declaringKingdom)
                    {
                        __result = -9999999f;
                        return;
                    }
                }

                string id1 = factionDeclaresPeace.StringId;
                string id2 = factionDeclaredPeace.StringId;

                bool isCivilWar = (id1.Contains("_rebels_") && id1.StartsWith(id2 + "_rebels_")) || 
                                  (id2.Contains("_rebels_") && id2.StartsWith(id1 + "_rebels_"));

                if (isCivilWar)
                {
                    __result = -9999999f;
                }
            }
        }
    }
}
