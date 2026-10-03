using HarmonyLib;
using BellumCivile.Behaviors;
using System.Linq;
using System.Reflection;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;

namespace BellumCivile.Patches
{
    /// <summary>
    /// Why did I do this file?
    /// To intercept peace between a parent kingdom and its active rebel kingdom before vanilla or Diplomacy can
    /// destroy, legitimize, or merge one side out from under BellumCivile's own civil-war resolution. This keeps
    /// claimant and rebel peace on the mod's scripted path instead of the generic kingdom cleanup path.
    /// </summary>
    [HarmonyPatch(typeof(MakePeaceAction), "Apply")]
    public class CivilWarMakePeacePatch
    {
        private const double AIInfluenceFreshCivilWarProtectionDays = 3d;

        [HarmonyPrefix]
        public static bool Prefix(IFaction faction1, IFaction faction2)
        {
            return ShouldAllowPeaceCall(faction1, faction2);
        }

        internal static bool ShouldAllowPeaceCall(IFaction faction1, IFaction faction2)
        {
            if (CivilWarResolutionBehavior.IsPeaceHandlingSuppressed) return true;
            if (ClaimFeudWarBehavior.IsPeaceHandlingSuppressed) return true;
            if (ClientKingdomBehavior.Instance?.IsSynchronizingDiplomacy == true) return true;

            Kingdom kingdom1 = faction1 as Kingdom;
            Kingdom kingdom2 = faction2 as Kingdom;
            if (kingdom1 == null || kingdom2 == null) return true;

            if (InternalPeaceSettlementBehavior.TryIdentify(kingdom1, kingdom2, out string internalKey, out bool rivalry)
                && (rivalry || internalKey.StartsWith("feud:")))
            {
                if (!rivalry) InternalPeaceSettlementBehavior.Current?.QueueWhitePeace(kingdom1, kingdom2);
                return false;
            }

            FactionManagerBehavior factionManager = Campaign.Current?.GetCampaignBehavior<FactionManagerBehavior>();
            CivilWarResolutionBehavior resolutionBehavior = Campaign.Current?.GetCampaignBehavior<CivilWarResolutionBehavior>();
            if (factionManager == null || resolutionBehavior == null) return true;

            FactionObject faction = factionManager.GetFactionByRebelKingdom(kingdom1);
            Kingdom rebelKingdom = kingdom1;
            if (faction == null || faction.ParentKingdom != kingdom2)
            {
                faction = factionManager.GetFactionByRebelKingdom(kingdom2);
                rebelKingdom = kingdom2;
                if (faction == null || faction.ParentKingdom != kingdom1)
                {
                    return ShouldAllowOrdinaryKingdomPeace(kingdom1, kingdom2);
                }
            }

            if (ShouldBlockEarlyAIInfluencePeace(faction, rebelKingdom))
                return false;

            InternalPeaceSettlementBehavior.Current?.QueueWhitePeace(kingdom1, kingdom2);
            return false;
        }

        private static bool ShouldAllowOrdinaryKingdomPeace(Kingdom kingdom1, Kingdom kingdom2)
        {
            if (!WarPeaceRevampBehavior.IsRevampEnabled())
                return true;

            if (BellumPeaceResolutionContext.IsBellumPeaceResolution)
                return true;

            if (!IsOrdinaryPermanentKingdom(kingdom1) || !IsOrdinaryPermanentKingdom(kingdom2))
                return true;

            WarScoreBehavior warScore = Campaign.Current?.GetCampaignBehavior<WarScoreBehavior>();
            bool tracked = warScore?.IsOrdinaryTrackedWar(kingdom1, kingdom2) == true;
            BellumCivileLogger.Log(
                $"Blocked non-Bellum international peace; first={kingdom1.StringId}; second={kingdom2.StringId}; tracked_war_score={tracked}. Bellum War Score/parley controls war duration while the revamp is enabled.");
            return false;
        }

        private static bool IsOrdinaryPermanentKingdom(Kingdom kingdom)
        {
            if (kingdom == null || kingdom.IsEliminated || kingdom.RulingClan == null)
                return false;

            if (!string.IsNullOrWhiteSpace(kingdom.StringId) && kingdom.StringId.StartsWith("bc_feud_"))
                return false;

            // Diplomacy's temporary rebel kingdoms must remain on Diplomacy's own consolidation
            // path. Treating them as permanent realms here would leave an existing civil war unable
            // to conclude when a player enables Bellum's foreign war revamp mid-campaign.
            if (ModIntegrationHelper.IsDiplomacyRebelKingdom(kingdom))
                return false;

            FactionManagerBehavior factionManager = Campaign.Current?.GetCampaignBehavior<FactionManagerBehavior>();
            return factionManager?.GetFactionByRebelKingdom(kingdom) == null;
        }

        private static bool ShouldBlockEarlyAIInfluencePeace(FactionObject faction, Kingdom rebelKingdom)
        {
            if (faction == null || rebelKingdom == null || faction.ParentKingdom == null)
                return false;

            if (!ModIntegrationHelper.IsAIInfluenceDiplomacyActive)
                return false;

            if (!faction.IsCivilWarActive())
                return false;

            double warAgeDays = CampaignTime.Now.ToDays - faction.CreationDate.ToDays;
            if (warAgeDays > AIInfluenceFreshCivilWarProtectionDays)
                return false;

            int parentStrongholds = CountStrongholds(faction.ParentKingdom);
            int rebelStrongholds = CountStrongholds(rebelKingdom);
            if (parentStrongholds <= 0 || rebelStrongholds <= 0)
                return false;

            BellumCivileLogger.Log(
                $"Blocked early AI Influence peace on fresh civil war; faction={faction.Name} type={faction.Type} parent={faction.ParentKingdom.StringId} rebel={rebelKingdom.StringId} age_days={warAgeDays:0.00} parent_strongholds={parentStrongholds} rebel_strongholds={rebelStrongholds}.");
            return true;
        }

        private static int CountStrongholds(Kingdom kingdom)
        {
            return kingdom?.Fiefs.Count(f => f != null && (f.IsTown || f.IsCastle)) ?? 0;
        }
    }

    [HarmonyPatch]
    public class CivilWarMakePeaceByDecisionPatch
    {
        private static MethodBase TargetMethod()
        {
            return typeof(MakePeaceAction)
                .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                .FirstOrDefault(m =>
                {
                    if (m.Name != "ApplyByKingdomDecision") return false;
                    ParameterInfo[] parameters = m.GetParameters();
                    return parameters.Length >= 2
                        && typeof(IFaction).IsAssignableFrom(parameters[0].ParameterType)
                        && typeof(IFaction).IsAssignableFrom(parameters[1].ParameterType);
                });
        }

        [HarmonyPrefix]
        public static bool Prefix(object[] __args)
        {
            if (__args == null || __args.Length < 2) return true;
            IFaction faction1 = __args[0] as IFaction;
            IFaction faction2 = __args[1] as IFaction;
            return CivilWarMakePeacePatch.ShouldAllowPeaceCall(faction1, faction2);
        }
    }
}
