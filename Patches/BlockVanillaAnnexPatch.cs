using System.Reflection;
using HarmonyLib;

namespace BellumCivile.Patches
{
    /// <summary>
    /// Why did I do this file?
    /// To block the vanilla AI from randomly proposing fief revocations. 100% of revocation proposals
    /// are now driven by ideological faction mood through the BellumCivile Fief Ambition system in IdeologyBehavior.
    /// </summary>
    [HarmonyPatch]
    public class BlockVanillaAnnexPatch
    {
        static MethodBase TargetMethod()
        {
            return AccessTools.Method("TaleWorlds.CampaignSystem.CampaignBehaviors.KingdomDecisionProposalBehavior:ConsiderAnnex");
        }

        public static bool Prefix()
        {
            return false;
        }
    }
}
