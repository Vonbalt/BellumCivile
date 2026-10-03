using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Localization;

namespace BellumCivile.Behaviors
{
    public partial class FeudalTitleBehavior
    {
        public sealed class ClaimResentment
        {
            public Clan House;
            public Hero Representative;
            public int Penalty;
        }

        private HashSet<string> GetProtectedFeudTitles()
        {
            var result = new HashSet<string>(StringComparer.Ordinal);
            var feuds = Campaign.Current?.GetCampaignBehavior<ClaimFeudBehavior>();
            if (feuds != null)
                foreach (var feud in feuds.GetActiveFeuds()) result.Add(feud.TargetTitleId);
            var wars = Campaign.Current?.GetCampaignBehavior<ClaimFeudWarBehavior>();
            if (wars != null)
                foreach (var war in wars.GetActiveWars()) result.Add(war.TargetTitleId);
            return result;
        }

        internal bool TitleTreesOverlap(FeudalTitleRecord first, FeudalTitleRecord second)
        {
            if (first == null || second == null) return false;
            bool Ancestor(FeudalTitleRecord ancestor, FeudalTitleRecord child, FeudalHierarchyMode mode)
            {
                var seen = new HashSet<string>();
                while (child != null && seen.Add(child.TitleId))
                {
                    if (child.TitleId == ancestor.TitleId) return true;
                    child = GetParentTitle(child, mode);
                }
                return false;
            }
            return Ancestor(first, second, FeudalHierarchyMode.DeJure) || Ancestor(second, first, FeudalHierarchyMode.DeJure)
                || Ancestor(first, second, FeudalHierarchyMode.DeFacto) || Ancestor(second, first, FeudalHierarchyMode.DeFacto);
        }

        private bool HasStructuralFeud(FeudalTitleRecord title) => GetProtectedFeudTitles()
            .Any(id => TitleTreesOverlap(title, GetTitle(id)));

        private bool IsFeudTarget(FeudalTitleRecord title) => title != null && GetProtectedFeudTitles().Contains(title.TitleId);

        public List<ClaimResentment> GetReorganizationResentment(Clan actor, IEnumerable<string> titleIds)
        {
            var claims = (titleIds ?? Enumerable.Empty<string>()).Distinct()
                .Select(GetTitle).Where(t => t != null).SelectMany(GetActiveClaimsByTitle);
            var result = new List<ClaimResentment>();
            foreach (var group in claims.Where(c => c.ClaimantClanId != actor?.StringId).GroupBy(c => c.ClaimantClanId))
            {
                var house = ResolveClan(group.Key);
                if (house == null || house.IsEliminated || house.Leader == null || house.Leader.IsDead) continue;
                result.Add(new ClaimResentment { House = house, Representative = house.Leader,
                    Penalty = group.Any(c => c.Strength == FeudalClaimStrength.Strong) ? -30 : -15 });
            }
            return result.OrderBy(r => r.House.StringId).ToList();
        }

        public string ReorganizationWarning(Clan actor, IEnumerable<string> titleIds)
        {
            var reactions = GetReorganizationResentment(actor, titleIds);
            if (reactions.Count == 0) return string.Empty;
            var text = new TextObject("{=BC_TitleClaimResentmentWarning}\n\nThese houses will resent the disregard of their ancestral claims: {HOUSES}. The grievance will last {YEARS} years.");
            text.SetTextVariable("HOUSES", string.Join("; ", reactions.Select(r => r.House.Name + " (" + r.Penalty + ")")));
            text.SetTextVariable("YEARS", (10f * BellumCivileOptions.RelationMemoryDurationMultiplier).ToString("0.##"));
            return text.ToString();
        }

        private static void ApplyReorganizationResentment(Hero actor, IEnumerable<ClaimResentment> reactions, string context)
        {
            foreach (var reaction in reactions)
            {
                if (actor == null || actor.IsDead || reaction.Representative == null || reaction.Representative.IsDead) continue;
                RelationMemoryService.ApplyChange(actor, reaction.Representative, reaction.Penalty, true,
                    RelationMemorySources.DisregardedAncestralClaims, 10, RelationMemoryScope.House, context);
            }
        }

        private void ReconcileReorganizedTitles(IEnumerable<string> changedIds)
        {
            var ids = new HashSet<string>(changedIds.Where(id => !string.IsNullOrWhiteSpace(id)));
            Campaign.Current?.GetCampaignBehavior<FeudalClaimFabricationBehavior>()?.RevalidateAfterReorganization();
            Campaign.Current?.GetCampaignBehavior<FeudalDeJureDriftBehavior>()?.ReconcileReorganization(ids);
        }
    }
}
