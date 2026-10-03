using System;
using System.Reflection;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Localization;

namespace BellumCivile
{
    internal static class DynamicMercenaryDescriptionHelper
    {
        private static readonly PropertyInfo DescriptionProperty = typeof(Clan).GetProperty(
            "EncyclopediaText", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        public static void EnsureDescription(Clan clan, DynamicMercenaryBandRecord record, Hero founder, Settlement home)
        {
            if (!DynamicMercenaryBandService.IsDynamicBand(clan) || record == null
                || !string.IsNullOrWhiteSpace(clan.EncyclopediaText?.ToString()))
                return;

            try
            {
                // Recovered orphan records contain the recovery date and current leader, not founding history.
                bool hasHistory = founder != null && !string.IsNullOrWhiteSpace(record.SourceClanId);
                TextObject description = hasHistory
                    ? new TextObject("{=BC_MercenaryFoundingDescription}On {DATE}, the {COMPANY} was founded by {FOUNDER}. The company established its base in {HOME}.")
                    : new TextObject("{=BC_MercenaryGeneralDescription}The {COMPANY} is a mercenary company based in {HOME}, offering its swords to those who can pay.");
                description.SetTextVariable("COMPANY", Snapshot(clan.Name));
                description.SetTextVariable("HOME", home == null
                    ? new TextObject("{=BC_MercenaryUnknownHome}an unrecorded settlement") : Snapshot(home.Name));
                if (hasHistory)
                {
                    description.SetTextVariable("DATE", record.CreatedAt.ToString());
                    Hero parent = founder.Father ?? founder.Mother;
                    TextObject founderText = Snapshot(founder.Name);
                    if (parent != null)
                    {
                        founderText = founder.IsFemale
                            ? new TextObject("{=BC_MercenaryFounderDaughter}{NAME}, daughter of {PARENT}")
                            : new TextObject("{=BC_MercenaryFounderSon}{NAME}, son of {PARENT}");
                        founderText.SetTextVariable("NAME", Snapshot(founder.Name));
                        founderText.SetTextVariable("PARENT", Snapshot(parent.Name));
                    }
                    description.SetTextVariable("FOUNDER", founderText);
                }

                if (DescriptionProperty?.GetSetMethod(true) == null)
                    throw new MissingMethodException("Clan.EncyclopediaText setter is unavailable.");
                DescriptionProperty.SetValue(clan, description);
            }
            catch (Exception ex)
            {
                BellumCivileLogger.Log($"Dynamic mercenary description failed; clan={clan.StringId}; error={ex}.");
            }
        }

        private static TextObject Snapshot(TextObject name)
        {
            return new TextObject("{=!}" + name?.ToString());
        }
    }
}
