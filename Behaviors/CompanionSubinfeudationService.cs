using System;
using System.Collections.Generic;
using System.Linq;
using Helpers;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Election;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Party.PartyComponents;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using C = BellumCivile.BellumCivileConstants;

namespace BellumCivile.Behaviors
{
    internal sealed class CompanionSubinfeudationPreview
    {
        public Settlement Settlement { get; set; }
        public FeudalTitleRecord BaronyTitle { get; set; }
        public FeudalTitleRecord LiegeTitle { get; set; }
        public bool TransfersDeJure { get; set; }
        public int RelationGain { get; set; }
    }

    internal static class CompanionSubinfeudationService
    {
        public static bool CanDiscussSubinfeudation(Hero companion)
        {
            Clan playerClan = Clan.PlayerClan;
            Kingdom kingdom = playerClan?.Kingdom;
            if (Hero.MainHero == null
                || playerClan == null
                || !IsPermanentKingdom(kingdom)
                || kingdom.RulingClan == playerClan
                || playerClan.IsUnderMercenaryService)
            {
                return false;
            }

            if (companion == null
                || !companion.IsAlive
                || companion.IsPrisoner
                || !companion.IsPlayerCompanion
                || companion.Clan != playerClan)
            {
                return false;
            }

            if (companion.PartyBelongedTo?.IsCurrentlyAtSea == true
                || MobileParty.MainParty?.IsCurrentlyAtSea == true)
            {
                return false;
            }

            return true;
        }

        public static bool IsLeadingCaravan(Hero companion)
        {
            return companion?.PartyBelongedTo?.IsCaravan == true;
        }

        public static List<CompanionSubinfeudationPreview> GetEligibleGrants(Hero companion)
        {
            List<CompanionSubinfeudationPreview> result = new List<CompanionSubinfeudationPreview>();
            Clan playerClan = Clan.PlayerClan;
            if (!CanDiscussSubinfeudation(companion) || IsLeadingCaravan(companion) || playerClan == null)
                return result;

            foreach (Settlement settlement in playerClan.Settlements
                .Where(item => item != null && (item.IsTown || item.IsCastle))
                .OrderBy(item => item.Name?.ToString()))
            {
                if (TryBuildPreview(companion, settlement, out CompanionSubinfeudationPreview preview, out _))
                    result.Add(preview);
            }

            return result;
        }

        public static bool TryBuildPreview(
            Hero companion,
            Settlement settlement,
            out CompanionSubinfeudationPreview preview,
            out TextObject failure)
        {
            preview = null;
            failure = null;
            Clan playerClan = Clan.PlayerClan;
            Kingdom kingdom = playerClan?.Kingdom;
            FeudalTitleBehavior titleBehavior = Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>();

            if (!CanDiscussSubinfeudation(companion))
            {
                failure = new TextObject("{=BC_Subinfeudation_InvalidParticipants}You and this companion are not presently able to establish a new vassal house.");
                return false;
            }

            if (IsLeadingCaravan(companion))
            {
                failure = new TextObject("{=BC_Subinfeudation_Caravan}This companion must be relieved from caravan duty first.");
                return false;
            }

            if (titleBehavior == null || settlement == null || (!settlement.IsTown && !settlement.IsCastle))
            {
                failure = new TextObject("{=BC_Subinfeudation_TitleUnavailable}The barony's feudal title is unavailable.");
                return false;
            }

            if (settlement.OwnerClan != playerClan
                || !titleBehavior.TryGetBarony(settlement, out FeudalTitleRecord barony)
                || barony == null
                || !barony.IsActive
                || barony.TitleType != FeudalTitleType.Barony
                || !string.Equals(barony.DeFactoHolderClanId, playerClan.StringId, StringComparison.Ordinal))
            {
                failure = new TextObject("{=BC_Subinfeudation_NotPossessed}You must personally possess this barony before granting it to a sub-vassal.");
                return false;
            }

            FeudalTitleRecord liegeTitle = titleBehavior.GetParentTitle(barony, FeudalHierarchyMode.DeFacto);
            if (!HasPlayerHeldAncestor(titleBehavior, barony, playerClan))
            {
                failure = new TextObject("{=BC_Subinfeudation_NoLiegeTitle}You must hold a superior title in this barony's de facto hierarchy within your realm. Its existing immediate liege will remain unchanged.");
                return false;
            }

            if (settlement.SiegeEvent != null)
            {
                failure = new TextObject("{=BC_Subinfeudation_UnderSiege}The settlement is under siege.");
                return false;
            }

            if (settlement.Town == null || settlement.Town.IsOwnerUnassigned || HasPendingOwnershipDecision(kingdom, settlement))
            {
                failure = new TextObject("{=BC_Subinfeudation_PendingDecision}The settlement's ownership is awaiting a realm decision.");
                return false;
            }

            ClaimFeudBehavior feudBehavior = Campaign.Current?.GetCampaignBehavior<ClaimFeudBehavior>();
            if (feudBehavior?.HasActiveDisputeForTitle(barony.TitleId) == true)
            {
                failure = new TextObject("{=BC_Subinfeudation_ActiveFeud}This barony is already contested in an active claim feud.");
                return false;
            }

            bool transfersDeJure = string.Equals(barony.DeJureHolderClanId, playerClan.StringId, StringComparison.Ordinal);
            preview = new CompanionSubinfeudationPreview
            {
                Settlement = settlement,
                BaronyTitle = barony,
                LiegeTitle = liegeTitle,
                TransfersDeJure = transfersDeJure,
                RelationGain = transfersDeJure
                    ? C.SubinfeudationFullRightsRelationGain
                    : C.SubinfeudationPossessionRelationGain
            };
            return true;
        }

        private static bool HasPlayerHeldAncestor(FeudalTitleBehavior titles, FeudalTitleRecord barony, Clan playerClan)
        {
            var visited = new HashSet<string>(StringComparer.Ordinal) { barony.TitleId };
            FeudalTitleRecord child = barony;
            FeudalTitleRecord parent = titles.GetParentTitle(child, FeudalHierarchyMode.DeFacto);
            while (parent != null)
            {
                if (!parent.IsActive || parent.TitleType <= child.TitleType || !visited.Add(parent.TitleId))
                    return false;

                Clan holder = Clan.All.FirstOrDefault(clan => clan != null && clan.StringId == parent.DeFactoHolderClanId);
                if (holder == null || holder.IsEliminated || holder.Kingdom != playerClan.Kingdom)
                    return false;
                if (holder == playerClan)
                    return true;

                child = parent;
                parent = titles.GetParentTitle(child, FeudalHierarchyMode.DeFacto);
            }
            return false;
        }

        public static bool TryCreateVassalClan(
            Hero companion,
            CompanionSubinfeudationPreview expectedGrant,
            string clanName,
            out Clan newClan,
            out TextObject failure)
        {
            newClan = null;
            failure = null;
            Clan playerClan = Clan.PlayerClan;
            FeudalTitleBehavior titleBehavior = Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>();

            if (!TryBuildPreview(companion, expectedGrant?.Settlement, out CompanionSubinfeudationPreview current, out failure)
                || current.BaronyTitle.TitleId != expectedGrant.BaronyTitle.TitleId
                || current.LiegeTitle.TitleId != expectedGrant.LiegeTitle.TitleId
                || current.TransfersDeJure != expectedGrant.TransfersDeJure)
            {
                if (failure == null)
                    failure = new TextObject("{=BC_Subinfeudation_GrantChanged}The proposed grant has changed and must be reconsidered.");
                return false;
            }

            if ((Hero.MainHero?.Gold ?? 0) < C.SubinfeudationGoldCost)
            {
                failure = new TextObject("{=BC_Subinfeudation_NeedGold}You no longer have enough denars to establish the new house.");
                return false;
            }

            if ((playerClan?.Influence ?? 0f) < C.SubinfeudationInfluenceCost)
            {
                failure = new TextObject("{=BC_Subinfeudation_NeedInfluence}You no longer have enough influence to establish the new house.");
                return false;
            }

            if (string.IsNullOrWhiteSpace(clanName))
            {
                failure = new TextObject("{=BC_Subinfeudation_InvalidClanName}The new house requires a valid name.");
                return false;
            }

            MobileParty formerParty = companion.PartyBelongedTo;
            RemoveCompanionAction.ApplyByByTurningToLord(playerClan, companion);
            companion.SetNewOccupation(Occupation.Lord);

            TextObject formattedClanName = GameTexts.FindText("str_generic_clan_name");
            formattedClanName.SetTextVariable("CLAN_NAME", new TextObject(clanName));
            int bannerIconId = GetRandomBannerIconId(current.Settlement);

            using (titleBehavior.BeginSubinfeudationGrant(
                current.BaronyTitle,
                current.LiegeTitle,
                playerClan,
                current.TransfersDeJure))
            {
                newClan = Clan.CreateCompanionToLordClan(
                    companion,
                    current.Settlement,
                    formattedClanName,
                    bannerIconId);
            }

            if (newClan == null)
            {
                failure = new TextObject("{=BC_Subinfeudation_ClanCreationFailed}The new house could not be created.");
                return false;
            }

            Clan createdClan = newClan;
            RunPostCreationStep(
                "party setup",
                companion,
                createdClan,
                () => ConfigureNewClanParty(companion, createdClan, current.Settlement, formerParty));
            RunPostCreationStep(
                "equipment adjustment",
                companion,
                createdClan,
                () => AdjustCompanionEquipment(companion));
            RunPostCreationStep(
                "house-member generation",
                companion,
                createdClan,
                () => SpawnNewHouseMembers(companion, createdClan, current.Settlement));

            GiveGoldAction.ApplyBetweenCharacters(Hero.MainHero, companion, C.SubinfeudationGoldCost);
            GainKingdomInfluenceAction.ApplyForDefault(Hero.MainHero, -C.SubinfeudationInfluenceCost);
            RelationMemoryService.ApplyChange(
                companion,
                Hero.MainHero,
                current.RelationGain,
                true,
                RelationMemorySources.EnfeoffedMyHouse,
                C.SubinfeudationRelationMemoryYears,
                RelationMemoryScope.House,
                current.BaronyTitle.Name);

            bool validResult = newClan != null
                && newClan.Kingdom == playerClan.Kingdom
                && string.Equals(current.BaronyTitle.DeFactoHolderClanId, newClan.StringId, StringComparison.Ordinal)
                && string.Equals(current.BaronyTitle.DeFactoParentTitleId, current.LiegeTitle.TitleId, StringComparison.Ordinal)
                && (!current.TransfersDeJure
                    || string.Equals(current.BaronyTitle.DeJureHolderClanId, newClan.StringId, StringComparison.Ordinal));
            if (!validResult)
            {
                BellumCivileLogger.Log(
                    $"Companion subinfeudation completed with an invalid title postcondition; companion={companion.StringId}; clan={newClan?.StringId ?? "none"}; title={current.BaronyTitle.TitleId}; liege_title={current.LiegeTitle.TitleId}; de_jure={current.BaronyTitle.DeJureHolderClanId}; de_facto={current.BaronyTitle.DeFactoHolderClanId}; de_facto_parent={current.BaronyTitle.DeFactoParentTitleId}.");
            }

            BellumCivileLogger.Log(
                $"Companion subinfeudation completed; grantor={playerClan.StringId}; companion={companion.StringId}; new_clan={newClan?.StringId ?? "none"}; settlement={current.Settlement.StringId}; title={current.BaronyTitle.TitleId}; liege_title={current.LiegeTitle.TitleId}; full_rights={current.TransfersDeJure}; relation={current.RelationGain}; gold={C.SubinfeudationGoldCost}; influence={C.SubinfeudationInfluenceCost:0}.");
            return newClan != null;
        }

        private static void RunPostCreationStep(string step, Hero companion, Clan clan, Action action)
        {
            try
            {
                action?.Invoke();
            }
            catch (Exception ex)
            {
                BellumCivileLogger.Log(
                    $"Companion subinfeudation optional {step} failed; companion={companion?.StringId ?? "none"}; clan={clan?.StringId ?? "none"}; error={ex}.");
            }
        }

        private static bool HasPendingOwnershipDecision(Kingdom kingdom, Settlement settlement)
        {
            return kingdom?.UnresolvedDecisions?.Any(decision =>
                (decision is SettlementClaimantDecision claimant && claimant.Settlement == settlement)
                || (decision is SettlementClaimantPreliminaryDecision preliminary && preliminary.Settlement == settlement)) == true;
        }

        private static int GetRandomBannerIconId(Settlement settlement)
        {
            MBReadOnlyList<int> iconIds = Hero.MainHero?.MapFaction?.Culture?.PossibleClanBannerIconsIDs;
            if (iconIds == null || iconIds.Count == 0)
                iconIds = settlement?.Culture?.PossibleClanBannerIconsIDs;

            return iconIds != null && iconIds.Count > 0
                ? iconIds[MBRandom.RandomInt(iconIds.Count)]
                : 0;
        }

        private static void ConfigureNewClanParty(
            Hero companion,
            Clan clan,
            Settlement settlement,
            MobileParty formerParty)
        {
            if (companion == null || clan == null || settlement == null)
                return;

            if (formerParty == MobileParty.MainParty
                && MobileParty.MainParty?.MemberRoster?.GetTroopCount(companion.CharacterObject) > 0)
            {
                MobileParty.MainParty.MemberRoster.AddToCounts(companion.CharacterObject, -1);
            }

            MobileParty currentParty = companion.PartyBelongedTo;
            if (currentParty == null || currentParty == MobileParty.MainParty)
            {
                MobileParty party = LordPartyComponent.CreateLordParty(
                    companion.CharacterObject.StringId,
                    companion,
                    MobileParty.MainParty?.Position ?? settlement.GatePosition,
                    3f,
                    settlement,
                    companion);
                if (clan.Culture?.BasicTroop != null)
                    party.MemberRoster.AddToCounts(clan.Culture.BasicTroop, MBRandom.RandomInt(12, 15));
                if (clan.Culture?.EliteBasicTroop != null)
                    party.MemberRoster.AddToCounts(clan.Culture.EliteBasicTroop, MBRandom.RandomInt(10, 15));
                return;
            }

            currentParty.ActualClan = clan;
            currentParty.Party.SetVisualAsDirty();
        }

        private static void AdjustCompanionEquipment(Hero companion)
        {
            Equipment civilianUpgrade = Campaign.Current.Models.EquipmentSelectionModel
                .GetEquipmentForCompanionWhenTurningToLord(companion, Equipment.EquipmentType.Civilian);
            Equipment battleUpgrade = Campaign.Current.Models.EquipmentSelectionModel
                .GetEquipmentForCompanionWhenTurningToLord(companion, Equipment.EquipmentType.Battle);
            Equipment civilian = new Equipment(Equipment.EquipmentType.Civilian);
            Equipment battle = new Equipment(Equipment.EquipmentType.Battle);
            for (int index = 0; index < 12; index++)
            {
                battle[index] = IsBetterEquipment(battleUpgrade[index], companion.BattleEquipment[index])
                    ? battleUpgrade[index]
                    : companion.BattleEquipment[index];
                civilian[index] = IsBetterEquipment(civilianUpgrade[index], companion.CivilianEquipment[index])
                    ? civilianUpgrade[index]
                    : companion.CivilianEquipment[index];
            }

            EquipmentHelper.AssignHeroEquipmentFromEquipment(companion, civilian);
            EquipmentHelper.AssignHeroEquipmentFromEquipment(companion, battle);
        }

        private static bool IsBetterEquipment(EquipmentElement candidate, EquipmentElement current)
        {
            return candidate.Item != null
                && (current.Item == null || current.Item.Tier < candidate.Item.Tier);
        }

        private static void SpawnNewHouseMembers(Hero companion, Clan clan, Settlement settlement)
        {
            MBReadOnlyList<CharacterObject> templates = companion?.Culture?.LordTemplates;
            if (templates == null || templates.Count == 0 || clan == null || settlement == null)
            {
                BellumCivileLogger.Log(
                    $"Skipped companion subinfeudation house-member generation because no lord templates were available; companion={companion?.StringId ?? "none"}; clan={clan?.StringId ?? "none"}.");
                return;
            }

            List<Hero> members = new List<Hero>
            {
                CreateNewHouseMember(templates[MBRandom.RandomInt(templates.Count)], settlement, true),
                CreateNewHouseMember(templates[MBRandom.RandomInt(templates.Count)], settlement, false),
                companion
            };

            foreach (Hero member in members.Where(hero => hero != null))
            {
                member.Clan = clan;
                member.ChangeState(Hero.CharacterStates.Active);
                if (member != companion)
                    EnterSettlementAction.ApplyForCharacterOnly(member, settlement);

                foreach (Hero other in members.Where(hero => hero != null && hero != member))
                {
                    RelationMemoryService.ApplyChange(
                        member,
                        other,
                        MBRandom.RandomInt(5, 10),
                        false,
                        RelationMemorySources.RecentFavor,
                        10f,
                        RelationMemoryScope.Personal,
                        clan.Name?.ToString());
                }
            }
        }

        private static Hero CreateNewHouseMember(CharacterObject template, Settlement settlement, bool steward)
        {
            Hero hero = HeroCreator.CreateSpecialHero(
                template,
                settlement,
                null,
                null,
                MBRandom.RandomInt(Campaign.Current.Models.AgeModel.HeroComesOfAge, 50));
            InitializeNewHouseMemberSkills(hero, steward);
            return hero;
        }

        internal static void InitializeNewHouseMemberSkills(Hero hero, bool steward)
        {
            hero.HeroDeveloper.SetInitialSkillLevel(DefaultSkills.OneHanded, MBRandom.RandomInt(100, 175));
            hero.HeroDeveloper.SetInitialSkillLevel(DefaultSkills.Leadership, MBRandom.RandomInt(125, 175));
            if (steward)
            {
                hero.HeroDeveloper.SetInitialSkillLevel(DefaultSkills.Steward, MBRandom.RandomInt(100, 175));
                hero.HeroDeveloper.SetInitialSkillLevel(DefaultSkills.Medicine, MBRandom.RandomInt(125, 175));
            }
            else
            {
                hero.HeroDeveloper.SetInitialSkillLevel(DefaultSkills.Tactics, MBRandom.RandomInt(125, 175));
                hero.HeroDeveloper.SetInitialSkillLevel(DefaultSkills.Engineering, MBRandom.RandomInt(125, 175));
            }

        }

        private static bool IsPermanentKingdom(Kingdom kingdom)
        {
            return kingdom != null
                && !kingdom.IsEliminated
                && !kingdom.IsMinorFaction
                && !kingdom.IsBanditFaction
                && !string.IsNullOrWhiteSpace(kingdom.StringId)
                && !kingdom.StringId.Contains("_rebels_")
                && !kingdom.StringId.StartsWith("bc_feud_", StringComparison.OrdinalIgnoreCase);
        }
    }
}
