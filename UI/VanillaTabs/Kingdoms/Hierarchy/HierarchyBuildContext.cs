using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;

namespace BellumCivile.UI.VanillaTabs.Kingdoms.Hierarchy
{
    // Scoped to one tree build; tooltips and commands continue to read live campaign state.
    internal sealed class HierarchyBuildContext
    {
        private readonly Dictionary<string, Clan> _clans = new Dictionary<string, Clan>(StringComparer.Ordinal);
        private readonly Dictionary<FeudalTitleRecord, string> _names = new Dictionary<FeudalTitleRecord, string>();
        private readonly Dictionary<CharacterObject, CharacterCode> _portraits = new Dictionary<CharacterObject, CharacterCode>();

        internal HierarchyBuildContext(IEnumerable<Clan> clans)
        {
            foreach (Clan clan in clans)
            {
                if (clan != null && !string.IsNullOrWhiteSpace(clan.StringId) && !_clans.ContainsKey(clan.StringId))
                    _clans.Add(clan.StringId, clan);
            }
        }

        internal Clan ResolveClan(string id)
        {
            return !string.IsNullOrWhiteSpace(id) && _clans.TryGetValue(id, out Clan clan) ? clan : null;
        }

        internal string FormatTitleName(FeudalTitleRecord title)
        {
            if (title == null) return string.Empty;
            if (!_names.TryGetValue(title, out string name))
            {
                name = FeudalTitleDisplayHelper.FormatTitleName(title,
                    ResolveClan(title.DeFactoHolderClanId) ?? ResolveClan(title.DeJureHolderClanId));
                _names.Add(title, name);
            }
            return name;
        }

        internal CharacterCode GetPortrait(CharacterObject character)
        {
            if (!_portraits.TryGetValue(character, out CharacterCode portrait))
            {
                portrait = PortraitAppearance.Create(character);
                _portraits.Add(character, portrait);
            }
            return portrait;
        }
    }
}
