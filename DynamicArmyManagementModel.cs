using BellumCivile.Behaviors;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Localization;
using TaleWorlds.Library;
using System;
using System.Collections.Generic;
using System.Linq;
using C = BellumCivile.BellumCivileConstants;
using O = BellumCivile.BellumCivileOptions;

namespace BellumCivile
{
    /// <summary>
    /// Why did I do this file?
    /// To override the vanilla army management calculations, injecting ideological influence penalties. It makes it extremely expensive for a hated king to summon rebellious lords to his army, directly weaponizing political mood.
    /// </summary>
    public class DynamicArmyManagementModel : DefaultArmyManagementCalculationModel
    {
        private FactionManagerBehavior _factionManager;
        private static readonly Dictionary<string, int> ArmyCreationCrashLogDays = new Dictionary<string, int>();

        public override bool CheckPartyEligibility(MobileParty party, out TextObject explanation)
        {
            if (!base.CheckPartyEligibility(party, out explanation))
                return false;

            if (ArmySummonsEligibilityHelper.ShouldRefuseArmyMenuCall(
                MobileParty.MainParty?.LeaderHero,
                party?.LeaderHero,
                out TextObject bellumReason,
                _factionManager))
            {
                explanation = bellumReason;
                return false;
            }

            return true;
        }

        public override bool CanLordCreateArmy(MobileParty leaderParty, out MBList<MobileParty> possibleArmyMembers)
        {
            bool canCreateArmy;
            try
            {
                canCreateArmy = base.CanLordCreateArmy(leaderParty, out possibleArmyMembers);
            }
            catch (NullReferenceException ex)
            {
                possibleArmyMembers = new MBList<MobileParty>();
                LogSuppressedArmyCreationCrash(leaderParty, ex);
                return false;
            }

            Clan callerClan = leaderParty?.LeaderHero?.Clan;

            if (callerClan == null || possibleArmyMembers == null || possibleArmyMembers.Count == 0)
                return canCreateArmy;

            MBList<MobileParty> filteredParties = new MBList<MobileParty>();
            foreach (MobileParty party in possibleArmyMembers.Where(p =>
                         !ArmySummonsEligibilityHelper.ShouldRefuseArmyMenuCall(
                             leaderParty?.LeaderHero,
                             p?.LeaderHero,
                             out TextObject _,
                             _factionManager)))
                filteredParties.Add(party);

            possibleArmyMembers = filteredParties;
            return canCreateArmy && possibleArmyMembers.Count > 0;
        }

        private static void LogSuppressedArmyCreationCrash(MobileParty leaderParty, Exception ex)
        {
            string partyId = leaderParty?.StringId ?? leaderParty?.LeaderHero?.StringId ?? "unknown";
            int day = (int)CampaignTime.Now.ToDays;
            if (ArmyCreationCrashLogDays.TryGetValue(partyId, out int loggedDay) && loggedDay == day)
                return;

            ArmyCreationCrashLogDays[partyId] = day;
            BellumCivileLogger.Log(
                $"Suppressed army-creation evaluation crash; party={partyId} leader={leaderParty?.LeaderHero?.StringId ?? "null"} kingdom={leaderParty?.LeaderHero?.Clan?.Kingdom?.StringId ?? "null"} error={ex.GetType().Name}:{ex.Message}. Returning false for this army creation check.");
        }

        public override int CalculatePartyInfluenceCost(MobileParty armyLeaderParty, MobileParty party)
        {
            int baseCost = base.CalculatePartyInfluenceCost(armyLeaderParty, party);

            if (party.LeaderHero?.Clan == null || armyLeaderParty.LeaderHero?.Clan == null)
                return baseCost;

            Clan callerClan = armyLeaderParty.LeaderHero.Clan;
            Clan calledClan = party.LeaderHero.Clan;

            if (callerClan.Kingdom == null || callerClan.Kingdom != calledClan.Kingdom || calledClan.IsMinorFaction || calledClan.IsUnderMercenaryService)
                return baseCost;

            if (callerClan == calledClan) return baseCost;

            if (ArmySummonsEligibilityHelper.IsTemporaryWarCommandScope(callerClan, calledClan, _factionManager))
                return baseCost;

            bool callerHasRoyalCommand = ArmySummonsEligibilityHelper.HasRoyalArmyCommand(callerClan);
            if (!callerHasRoyalCommand)
                return ApplyFeudalServiceArmyCost(baseCost, callerClan, calledClan);

            if (_factionManager == null)
                _factionManager = Campaign.Current.GetCampaignBehavior<FactionManagerBehavior>();
            if (_factionManager == null) return ApplyFeudalServiceArmyCost(baseCost, callerClan, calledClan);

            FactionObject calledIdeology = _factionManager.GetIdeologicalFaction(calledClan);
            if (calledIdeology == null) return ApplyFeudalServiceArmyCost(baseCost, callerClan, calledClan);

            float mood = calledIdeology.Mood;
            float multiplier;
            if      (mood >= C.ArmyMoodContent)      multiplier = C.ArmyCostContent;
            else if (mood >= C.MoodThresholdHappy)   multiplier = C.ArmyCostMildContent;
            else if (mood >= C.MoodThresholdUnhappy) multiplier = C.ArmyCostNeutral;
            else if (mood >= O.ArmyRefusalMoodThreshold) multiplier = C.ArmyCostUnhappy;
            else                                      multiplier = C.ArmyCostFurious;

            return ApplyFeudalServiceArmyCost((int)(baseCost * multiplier), callerClan, calledClan);
        }

        private static int ApplyFeudalServiceArmyCost(int currentCost, Clan callerClan, Clan calledClan)
        {
            if (currentCost <= 0 || callerClan == null || calledClan == null || callerClan == calledClan)
                return currentCost;

            FeudalTitleBehavior titleBehavior = FeudalTitleBehavior.Instance ?? Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>();
            FeudalServiceBehavior serviceBehavior = FeudalServiceBehavior.Instance ?? Campaign.Current?.GetCampaignBehavior<FeudalServiceBehavior>();
            if (titleBehavior == null || serviceBehavior == null)
                return currentCost;

            List<float> multipliers = new List<float>();
            foreach (FeudalTitleRecord title in titleBehavior.GetTitlesHeldByClan(calledClan, deJure: false))
            {
                FeudalTitleRecord parent = titleBehavior.GetParentTitle(title, FeudalHierarchyMode.DeFacto);
                if (parent == null || parent.DeFactoHolderClanId != callerClan.StringId)
                    continue;

                multipliers.Add(serviceBehavior.GetArmyCostMultiplier(title, parent));
            }

            if (multipliers.Count == 0)
                return currentCost;

            float multiplier = multipliers.Average();
            return Math.Max(0, (int)Math.Round(currentCost * multiplier));
        }

        public override ExplainedNumber CalculateDailyCohesionChange(Army army, bool includeDescriptions = false)
        {
            ExplainedNumber result = base.CalculateDailyCohesionChange(army, includeDescriptions);
            if (result.ResultNumber >= 0f)
                return result;

            float efficiency = Campaign.Current?.GetCampaignBehavior<PrivyCouncilBehavior>()?
                .GetAssignmentEffectMultiplier(
                    army?.Kingdom,
                    PrivyCouncilOffice.Marshal,
                    "marshal_oversee_logistics") ?? 0f;
            if (efficiency > 0f)
            {
                float incidentMultiplier = Campaign.Current?
                    .GetCampaignBehavior<CouncilIncidentBehavior>()?
                    .GetAssignmentAspectMultiplier(
                        army?.Kingdom,
                        PrivyCouncilOffice.Marshal,
                        "marshal_oversee_logistics",
                        CouncilIncidentAspects.ArmyCohesionRetention) ?? 1f;
                efficiency *= incidentMultiplier;
                result.AddFactor(
                    -0.15f * efficiency,
                    new TextObject("{=BC_Council_AssignmentArmyLogistics}Council logistics"));
            }

            return result;
        }

    }
}
