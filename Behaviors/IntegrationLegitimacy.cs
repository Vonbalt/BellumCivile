using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;

namespace BellumCivile.Behaviors
{
    public sealed partial class BellumIntegrationBehavior
    {
        private List<string> _illegitimateHeroes = new List<string>();
        private HashSet<string> _illegitimateIndex = new HashSet<string>(StringComparer.Ordinal);
        internal int LegitimacyRevision { get; private set; }
        internal static bool IsBarred(Hero hero) => hero != null && IsBarredId(hero.StringId);
        internal static bool IsBarredId(string id) => !string.IsNullOrEmpty(id) && Current?._illegitimateIndex.Contains(id) == true;

        private void SyncLegitimacy(IDataStore store)
        {
            store.SyncData("BC_Integration_IllegitimateHeroes", ref _illegitimateHeroes);
            _illegitimateHeroes = (_illegitimateHeroes ?? new List<string>()).Where(id => !string.IsNullOrEmpty(id)).Distinct().ToList();
            _illegitimateIndex = new HashSet<string>(_illegitimateHeroes, StringComparer.Ordinal);
        }

        internal bool SetIllegitimate(Hero hero, bool value)
        {
            if (hero == null || string.IsNullOrEmpty(hero.StringId)) return false;
            bool changed = value ? _illegitimateIndex.Add(hero.StringId) : _illegitimateIndex.Remove(hero.StringId);
            if (!changed) return true;
            if (value) _illegitimateHeroes.Add(hero.StringId); else _illegitimateHeroes.Remove(hero.StringId);
            LegitimacyRevision++;
            // Allocated estates and existing rulers are deliberately not rewritten.
            Patches.FeudalTitleHeroNamePatch.InvalidateCache();
            Campaign.Current?.GetCampaignBehavior<DynasticHeirBehavior>()?.RefreshLegitimacy();
            Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>()?.RefreshLegitimacyClaims();
            BellumCivileLogger.Log($"Integration legitimacy changed; hero={hero.StringId}; illegitimate={value}.");
            return true;
        }
    }
}
