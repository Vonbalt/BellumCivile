using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.SaveSystem;

namespace BellumCivile
{
    /// <summary>
    /// Why did I do this file?
    /// To explicitly map custom classes, enums, and complex generic lists to unique ID spaces so the Bannerlord engine can safely serialize and reload the mod's campaign data without save corruption.
    /// </summary>
    public class BellumCivileSaveDefiner : SaveableTypeDefiner
    {
        public const int SaveBaseId = 8456123;

        public BellumCivileSaveDefiner() : base(SaveBaseId) { }

        protected override void DefineClassTypes()
        {
            AddClassDefinition(typeof(PartitionCompletionRecord), 129);
            AddClassDefinition(typeof(PartitionRecipientRecord), 130);
            AddClassDefinition(typeof(PlayerMarriageAgreement), 128);
            AddClassDefinition(typeof(CrownEstateDeliveryRecord), 126);
            AddClassDefinition(typeof(CrownPartitionBatchRecord), 127);
            AddClassDefinition(typeof(CrownPartitionPromotionRecord), 124);
            AddClassDefinition(typeof(CrownPartitionHouseRecord), 125);
            AddClassDefinition(typeof(RealmUnionRecord), 121);
            AddClassDefinition(typeof(RealmUnionClanRecord), 122);
            AddClassDefinition(typeof(RealmUnionTitleRecord), 123);
            AddClassDefinition(typeof(PendingMarriageProspect), 120);
            AddClassDefinition(typeof(FactionObject), 1);
            AddClassDefinition(typeof(PendingCadetMarriageRecord), 3);
            AddClassDefinition(typeof(PendingPartitionSuccessionRecord), 4);
            AddClassDefinition(typeof(DynamicRelationRecord), 5);
            AddClassDefinition(typeof(DynasticSuccessionStateRecord), 6);
            AddClassDefinition(typeof(FeudalTitleRecord), 7);
            AddClassDefinition(typeof(FeudalClaimRecord), 8);
            AddClassDefinition(typeof(FeudalUsurpationCandidateRecord), 11);
            AddClassDefinition(typeof(FeudalClaimFabricationRecord), 12);
            AddClassDefinition(typeof(ActiveForeignWarRecord), 14);
            AddClassDefinition(typeof(FeudalDeJureDriftRecord), 17);
            AddClassDefinition(typeof(ClaimFeudRecord), 19);
            AddClassDefinition(typeof(ClaimFeudWarRecord), 24);
            AddClassDefinition(typeof(FeudalServiceRecord), 27);
            AddClassDefinition(typeof(WarWillPressureRecord), 30);
            AddClassDefinition(typeof(WarScoreRecord), 31);
            AddClassDefinition(typeof(WarScoreFiefSnapshotRecord), 32);
            AddClassDefinition(typeof(WarScoreEventRecord), 33);
            AddClassDefinition(typeof(TreatyTermRecord), 34);
            AddClassDefinition(typeof(TreatyProposalRecord), 35);
            AddClassDefinition(typeof(ActiveTreatyTributeRecord), 36);
            AddClassDefinition(typeof(ClientKingdomRecord), 40);
            AddClassDefinition(typeof(PendingTreatyRebelResolutionRecord), 41);
            AddClassDefinition(typeof(PrivyCouncilOfficeRecord), 42);
            AddClassDefinition(typeof(PrivyCouncilAppointmentDecision), 44);
            AddClassDefinition(typeof(PrivyCouncilAppointmentDecision.PrivyCouncilAppointmentOutcome), 45);
            AddClassDefinition(typeof(RelationMemoryRecord), 46);
            AddClassDefinition(typeof(RealmLawSelectionRecord), 48);
            AddClassDefinition(typeof(PendingGenderLineEscheatRecord), 51);
            AddClassDefinition(typeof(DynamicMercenaryBandRecord), 52);
            AddClassDefinition(typeof(DynamicMercenaryDepartureIntent), 53);
            AddClassDefinition(typeof(RegencyRecord), 54);
            AddClassDefinition(typeof(Behaviors.FeudalClaimFabricationBehavior.PendingFabricationOutcome), 55);
            AddClassDefinition(typeof(CourtAgendaRecord), 56);
            AddClassDefinition(typeof(CourtObjectiveRecord), 58);
            AddClassDefinition(typeof(CourtDecreeCase), 61);
            AddClassDefinition(typeof(CrownAccessionRecord), 62);
            AddClassDefinition(typeof(CrossClanEstateRecord), 63);
            AddClassDefinition(typeof(CrossClanEstateShare), 64);
            AddClassDefinition(typeof(ElectiveSuccessionRecord), 65);
            AddClassDefinition(typeof(ElectiveCommitment), 66);
            AddClassDefinition(typeof(ElectivePreference), 67);
            AddClassDefinition(typeof(HereditaryLoyaltyMemory), 68);
            AddClassDefinition(typeof(SuccessionChallengeRecord), 69);
            AddClassDefinition(typeof(SuccessionPledgeRecord), 72);
            AddClassDefinition(typeof(ElectiveContestRecord), 75);
            AddClassDefinition(typeof(ElectiveContestCandidate), 76);
            AddClassDefinition(typeof(ElectiveContestVote), 77);
            AddClassDefinition(typeof(ElectiveContestPledge), 79);
            AddClassDefinition(typeof(ElectiveContestPreference), 80);
            AddClassDefinition(typeof(CivilWarConflictRecord), 81);
            AddClassDefinition(typeof(CivilWarSideRecord), 82);
            AddClassDefinition(typeof(CivilWarPairRecord), 83);
            AddClassDefinition(typeof(CivilWarCrownTransferRecord), 84);
            AddClassDefinition(typeof(CivilWarPairTransferRecord), 85);
            AddClassDefinition(typeof(CivilWarCollapseRecord), 86);
            AddClassDefinition(typeof(CivilWarRivalDefeatRecord), 87);
            AddClassDefinition(typeof(CivilWarRivalPromotionRecord), 88);
            AddClassDefinition(typeof(ConflictOutcomeNotice), 89);
            AddClassDefinition(typeof(CourtActivityRecord), 90);
            AddClassDefinition(typeof(CourtActivityTarget), 91);
            AddClassDefinition(typeof(CourtAppeasementRecord), 92);
            AddClassDefinition(typeof(CourtPeaceRecord), 93);
            AddClassDefinition(typeof(CourtCampaignRecord), 94);
            AddClassDefinition(typeof(CourtSubjugationRecord), 95);
            AddClassDefinition(typeof(CourtClaimRecord), 96);
            AddClassDefinition(typeof(CourtDynasticRecord), 97);
            AddClassDefinition(typeof(CourtTradeRecord), 101);
            AddClassDefinition(typeof(CourtTitleGrantRecord), 102);
            AddClassDefinition(typeof(CourtClientGrantRecord), 113);
            AddClassDefinition(typeof(CouncilSalaryCredit), 114);
            AddClassDefinition(typeof(HostagePactRecord), 115);
            AddClassDefinition(typeof(TreatyHostageRecord), 116);
            AddClassDefinition(typeof(CourtRallyRecord), 104);
            AddClassDefinition(typeof(CourtMandateRecord), 105);
            AddClassDefinition(typeof(CourtMandatePledge), 106);
            AddClassDefinition(typeof(MandateReformDecision), 107);
            AddClassDefinition(typeof(MandateReformDecision.ReformOutcome), 108);
            AddClassDefinition(typeof(CourtRoyalPeaceCase), 109);
            AddClassDefinition(typeof(CourtLiberationRecord), 110);
            AddClassDefinition(typeof(CourtTribunalReactionRecord), 111);
            AddClassDefinition(typeof(CourtTribunalMemberReaction), 112);
            AddClassDefinition(typeof(CourtProtectionRecord), 99);
        }

        protected override void DefineEnumTypes()
        {
            AddEnumDefinition(typeof(CourtDynasticResponse), 98);
            AddEnumDefinition(typeof(CourtProtectionPhase), 100);
            AddEnumDefinition(typeof(CourtTitleResponse), 103);
            AddEnumDefinition(typeof(SuccessionChallengePhase), 70);
            AddEnumDefinition(typeof(SuccessionChallengeDemand), 71);
            AddEnumDefinition(typeof(SuccessionPledgeChoice), 73);
            AddEnumDefinition(typeof(SuccessionChallengeOutcome), 74);
            AddEnumDefinition(typeof(ElectiveContestDecision), 78);
            AddEnumDefinition(typeof(FactionType), 2);
            AddEnumDefinition(typeof(FeudalTitleType), 9);
            AddEnumDefinition(typeof(FeudalClaimStrength), 10);
            AddEnumDefinition(typeof(FeudalClaimFabricationTrack), 13);
            AddEnumDefinition(typeof(ForeignPolicyMotive), 15);
            AddEnumDefinition(typeof(ForeignPolicyActionType), 16);
            AddEnumDefinition(typeof(FeudalDeJureDriftState), 18);
            AddEnumDefinition(typeof(ClaimFeudState), 20);
            AddEnumDefinition(typeof(CrownAuthorityLevel), 21);
            AddEnumDefinition(typeof(ClaimFeudJudgment), 22);
            AddEnumDefinition(typeof(ClaimFeudResponse), 23);
            AddEnumDefinition(typeof(ClaimFeudWarOutcome), 25);
            AddEnumDefinition(typeof(FeudalServiceLevel), 26);
            AddEnumDefinition(typeof(WarWillReasonType), 28);
            AddEnumDefinition(typeof(WarScoreEventType), 29);
            // These must not overlap the class IDs above. Bannerlord's definition context keeps
            // save-type IDs in one registry and throws during module startup when they collide.
            AddEnumDefinition(typeof(WarScoreConflictType), 37);
            AddEnumDefinition(typeof(TreatyTermType), 38);
            AddEnumDefinition(typeof(TreatyProposalState), 39);
            AddEnumDefinition(typeof(PrivyCouncilOffice), 43);
            AddEnumDefinition(typeof(RelationMemoryScope), 47);
            AddEnumDefinition(typeof(GenderSuccessionLaw), 49);
            AddEnumDefinition(typeof(HouseSuccessionLaw), 50);
            AddEnumDefinition(typeof(CourtAgendaState), 57);
            AddEnumDefinition(typeof(CourtObjectiveState), 59);
            AddEnumDefinition(typeof(CourtObjectiveCredit), 60);
            AddEnumDefinition(typeof(HostagePactPhase), 117);
            AddEnumDefinition(typeof(HostagePactEndReason), 118);
            AddEnumDefinition(typeof(HostageCustodyOutcome), 119);
        }

        protected override void DefineContainerDefinitions()
        {
            ConstructContainerDefinition(typeof(List<PartitionCompletionRecord>));
            ConstructContainerDefinition(typeof(List<PartitionRecipientRecord>));
            ConstructContainerDefinition(typeof(List<PlayerMarriageAgreement>));
            ConstructContainerDefinition(typeof(Dictionary<FactionType, Clan>));
            ConstructContainerDefinition(typeof(List<CourtTribunalReactionRecord>));
            ConstructContainerDefinition(typeof(List<CourtTribunalMemberReaction>));
            ConstructContainerDefinition(typeof(List<CourtMandatePledge>));
            ConstructContainerDefinition(typeof(List<CourtActivityTarget>));
            ConstructContainerDefinition(typeof(List<ConflictOutcomeNotice>));
            ConstructContainerDefinition(typeof(List<CivilWarConflictRecord>));
            ConstructContainerDefinition(typeof(List<CivilWarSideRecord>));
            ConstructContainerDefinition(typeof(List<CivilWarPairRecord>));
            ConstructContainerDefinition(typeof(List<CivilWarCrownTransferRecord>));
            ConstructContainerDefinition(typeof(List<CivilWarPairTransferRecord>));
            ConstructContainerDefinition(typeof(List<CivilWarCollapseRecord>));
            ConstructContainerDefinition(typeof(List<CivilWarRivalDefeatRecord>));
            ConstructContainerDefinition(typeof(List<CivilWarRivalPromotionRecord>));
            ConstructContainerDefinition(typeof(List<FactionObject>));
            ConstructContainerDefinition(typeof(List<PendingCadetMarriageRecord>));
            ConstructContainerDefinition(typeof(List<PendingPartitionSuccessionRecord>));
            ConstructContainerDefinition(typeof(Dictionary<string, DynamicRelationRecord>));
            ConstructContainerDefinition(typeof(List<RelationMemoryRecord>));
            ConstructContainerDefinition(typeof(Dictionary<string, DynasticSuccessionStateRecord>));
            ConstructContainerDefinition(typeof(Dictionary<string, FeudalTitleRecord>));
            ConstructContainerDefinition(typeof(List<FeudalClaimRecord>));
            ConstructContainerDefinition(typeof(List<FeudalUsurpationCandidateRecord>));
            ConstructContainerDefinition(typeof(List<FeudalClaimFabricationRecord>));
            ConstructContainerDefinition(typeof(List<Behaviors.FeudalClaimFabricationBehavior.PendingFabricationOutcome>));
            ConstructContainerDefinition(typeof(List<ActiveForeignWarRecord>));
            ConstructContainerDefinition(typeof(List<FeudalDeJureDriftRecord>));
            ConstructContainerDefinition(typeof(List<ClaimFeudRecord>));
            ConstructContainerDefinition(typeof(List<ClaimFeudWarRecord>));
            ConstructContainerDefinition(typeof(Dictionary<string, FeudalServiceRecord>));
            ConstructContainerDefinition(typeof(List<WarWillPressureRecord>));
            ConstructContainerDefinition(typeof(List<WarScoreRecord>));
            ConstructContainerDefinition(typeof(List<WarScoreFiefSnapshotRecord>));
            ConstructContainerDefinition(typeof(List<WarScoreEventRecord>));
            ConstructContainerDefinition(typeof(List<TreatyTermRecord>));
            ConstructContainerDefinition(typeof(List<TreatyProposalRecord>));
            ConstructContainerDefinition(typeof(List<ActiveTreatyTributeRecord>));
            ConstructContainerDefinition(typeof(List<ClientKingdomRecord>));
            ConstructContainerDefinition(typeof(List<CourtProtectionRecord>));
            ConstructContainerDefinition(typeof(List<CourtTitleGrantRecord>));
            ConstructContainerDefinition(typeof(List<PendingTreatyRebelResolutionRecord>));
            ConstructContainerDefinition(typeof(List<PrivyCouncilOfficeRecord>));
            ConstructContainerDefinition(typeof(List<CouncilSalaryCredit>));
            ConstructContainerDefinition(typeof(List<HostagePactRecord>));
            ConstructContainerDefinition(typeof(Dictionary<Kingdom, CampaignTime>));
            ConstructContainerDefinition(typeof(Dictionary<Clan, CampaignTime>));
            ConstructContainerDefinition(typeof(List<Clan>));
            ConstructContainerDefinition(typeof(Dictionary<string, CampaignTime>)); 
            ConstructContainerDefinition(typeof(Dictionary<string, int>)); 
            ConstructContainerDefinition(typeof(Dictionary<string, float>));
            ConstructContainerDefinition(typeof(Dictionary<string, bool>));
            ConstructContainerDefinition(typeof(Dictionary<string, Hero>));
            ConstructContainerDefinition(typeof(Dictionary<string, string>));
            ConstructContainerDefinition(typeof(Dictionary<Kingdom, int>));
            ConstructContainerDefinition(typeof(List<string>));
            ConstructContainerDefinition(typeof(List<float>));
            ConstructContainerDefinition(typeof(Dictionary<string, RealmLawSelectionRecord>));
            ConstructContainerDefinition(typeof(List<PendingGenderLineEscheatRecord>));
            ConstructContainerDefinition(typeof(List<DynamicMercenaryBandRecord>));
            ConstructContainerDefinition(typeof(List<DynamicMercenaryDepartureIntent>));
            ConstructContainerDefinition(typeof(List<RegencyRecord>));
            ConstructContainerDefinition(typeof(List<CourtAgendaRecord>));
            ConstructContainerDefinition(typeof(List<CourtDecreeCase>));
            ConstructContainerDefinition(typeof(List<CourtRoyalPeaceCase>));
            ConstructContainerDefinition(typeof(List<CrownAccessionRecord>));
            ConstructContainerDefinition(typeof(List<CrossClanEstateRecord>));
            ConstructContainerDefinition(typeof(List<CrossClanEstateShare>));
            ConstructContainerDefinition(typeof(List<ElectiveSuccessionRecord>));
            ConstructContainerDefinition(typeof(List<ElectiveCommitment>));
            ConstructContainerDefinition(typeof(List<ElectivePreference>));
            ConstructContainerDefinition(typeof(List<HereditaryLoyaltyMemory>));
            ConstructContainerDefinition(typeof(List<SuccessionChallengeRecord>));
            ConstructContainerDefinition(typeof(List<SuccessionPledgeRecord>));
            ConstructContainerDefinition(typeof(List<ElectiveContestRecord>));
            ConstructContainerDefinition(typeof(List<ElectiveContestCandidate>));
            ConstructContainerDefinition(typeof(List<ElectiveContestVote>));
            ConstructContainerDefinition(typeof(List<ElectiveContestPledge>));
            ConstructContainerDefinition(typeof(List<ElectiveContestPreference>));
            ConstructContainerDefinition(typeof(Dictionary<Clan, float>));
            ConstructContainerDefinition(typeof(Dictionary<string, TaleWorlds.CampaignSystem.Election.KingdomDecision>));
            ConstructContainerDefinition(typeof(Dictionary<string, double>));
            ConstructContainerDefinition(typeof(List<PendingMarriageProspect>));
            ConstructContainerDefinition(typeof(List<RealmUnionClanRecord>));
            ConstructContainerDefinition(typeof(List<RealmUnionTitleRecord>));
            ConstructContainerDefinition(typeof(List<CrownPartitionPromotionRecord>));
            ConstructContainerDefinition(typeof(List<CrownPartitionHouseRecord>));
            ConstructContainerDefinition(typeof(List<CrownEstateDeliveryRecord>));
            ConstructContainerDefinition(typeof(Dictionary<string, List<string>>));
        }
    }
}
