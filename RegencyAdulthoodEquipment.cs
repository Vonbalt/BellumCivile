using System;
using System.Linq;
using Helpers;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Extensions;
using TaleWorlds.Core;

namespace BellumCivile
{
    internal static class RegencyAdulthoodEquipment
    {
        internal static bool TryPrepareWard(Hero ward)
        {
            if (ward.CharacterObject == null) return false;
            return TryRepair(ward, knownWard: true);
        }

        internal static void TryRecoverLegacy(Hero hero)
        {
            if (hero?.Clan == null || !hero.IsAlive || !hero.IsActive || !hero.IsLord
                || hero == Hero.MainHero || hero.Clan == Clan.PlayerClan || hero.Clan.IsEliminated
                || hero.Clan.IsBanditFaction || hero.Clan.IsMinorFaction || hero.IsPlayerCompanion
                || hero.Age < SuccessionLawHelper.GetAgeOfMajority()
                || hero.Father == null && hero.Mother == null) return;
            TryRepair(hero, knownWard: false);
        }

        private static bool TryRepair(Hero hero, bool knownWard)
        {
            try
            {
                var battle = hero.BattleEquipment;
                var civilian = hero.CivilianEquipment;
                if (battle == null || civilian == null) return false;
                if (HasWeaponOrMount(battle)) return true;
                var templates = ChildhoodTemplates(hero).ToList();
                bool childhoodBattle = templates.Any(t => SameSlots(battle, t));
                if (!childhoodBattle && !(knownWard && battle.IsEmpty())) return true;
                var model = Campaign.Current?.Models?.EquipmentSelectionModel;
                if (model == null) return false;
                var adultBattle = model.GetEquipmentForHeroComeOfAge(hero, Equipment.EquipmentType.Battle);
                if (adultBattle == null || !adultBattle.IsBattle || adultBattle.IsEmpty()
                    || templates.Any(t => SameSlots(adultBattle, t))) return false;
                bool childhoodCivilian = templates.Any(t => SameSlots(civilian, t)) || knownWard && civilian.IsEmpty();
                var adultCivilian = childhoodCivilian
                    ? model.GetEquipmentForHeroComeOfAge(hero, Equipment.EquipmentType.Civilian) : null;
                if (childhoodCivilian && (adultCivilian == null || !adultCivilian.IsCivilian || adultCivilian.IsEmpty())) return false;
                EquipmentHelper.AssignHeroEquipmentFromEquipment(hero, adultBattle);
                if (adultCivilian != null) EquipmentHelper.AssignHeroEquipmentFromEquipment(hero, adultCivilian);
                BellumCivileLogger.Log($"Recovered missing adult noble equipment; hero={hero.StringId}; clan={hero.Clan?.StringId}; "
                    + $"known_regency_ward={knownWard}; civilian_replaced={adultCivilian != null}; skills_unchanged=true.");
                return true;
            }
            catch (Exception ex)
            {
                BellumCivileLogger.Log($"Adult noble equipment recovery deferred; hero={hero.StringId}; error={ex}");
                return false;
            }
        }

        private static System.Collections.Generic.IEnumerable<Equipment> ChildhoodTemplates(Hero hero)
        {
            var flags = EquipmentCategories.IsLordTemplate | (hero.IsFemale ? EquipmentCategories.IsFemaleTemplate : 0);
            return MBEquipmentRosterExtensions.All.Where(r => r.EquipmentCulture == hero.Culture
                && (r.EquipmentCategories == (flags | EquipmentCategories.IsChildEquipmentTemplate)
                    || r.EquipmentCategories == (flags | EquipmentCategories.IsTeenagerEquipmentTemplate)))
                .SelectMany(r => r.AllEquipments).Where(e => e.IsCivilian && !e.IsEmpty());
        }

        private static bool HasWeaponOrMount(Equipment equipment) => !equipment[EquipmentIndex.Horse].IsEmpty
            || Enumerable.Range(0, (int)EquipmentIndex.NumAllWeaponSlots).Any(i => !equipment[i].IsEmpty);

        // Native children copy civilian slots into battle equipment with a different
        // equipment type, so compare elements rather than the type-aware equality API.
        private static bool SameSlots(Equipment left, Equipment right) => Enumerable.Range(0, 12)
            .All(i => left[i].IsEqualTo(right[i]));
    }
}
