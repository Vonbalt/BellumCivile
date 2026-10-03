using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;

namespace BellumCivile.UI
{
    internal static class PortraitAppearance
    {
        public static CharacterCode Create(CharacterObject character)
        {
            if (character == null) return CharacterCode.CreateEmpty();

            // These political panels show known office holders regardless of encounter history.
            // Strip portrait-only equipment from a copy, never from the actual hero.
            var equipment = character.Equipment.Clone();
            equipment[EquipmentIndex.Head] = default(EquipmentElement);
            for (var slot = EquipmentIndex.WeaponItemBeginSlot; slot < EquipmentIndex.NumAllWeaponSlots; slot++)
            {
                if (equipment[slot].Item?.WeaponComponent?.PrimaryWeapon?.IsShield == true)
                    equipment[slot] = default(EquipmentElement);
            }

            return CharacterCode.CreateFrom(character, equipment);
        }
    }
}
