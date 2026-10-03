using System.Collections.Generic;
using System.Linq;
using BellumCivile.Behaviors;
using TaleWorlds.CampaignSystem;

namespace BellumCivile
{
    internal sealed class CrownPowerProjection
    {
        internal Dictionary<Clan, CrownPowerShare> Houses { get; } = new Dictionary<Clan, CrownPowerShare>();
        internal CrownPowerShare Total { get; private set; } = new CrownPowerShare(0, 0, 0);

        internal static CrownPowerProjection Calculate(Kingdom realm)
        {
            var result = new CrownPowerProjection();
            if (realm?.RulingClan == null) return result;
            var manager = Campaign.Current?.GetCampaignBehavior<FactionManagerBehavior>();
            var ideology = Campaign.Current?.GetCampaignBehavior<IdeologyBehavior>();
            var factions = manager?.GetFactionsInKingdom(realm) ?? new List<FactionObject>();
            var recorded = factions.Where(f => !f.IsIdeology && (f.IsCivilWarActive()
                || (f.IsGrandCoalition && (f.IsUltimatumPending || f.HasTrackedRebelKingdom)))).ToList();
            var committed = new HashSet<Clan>();
            if (recorded.Count > 0)
            {
                foreach (var faction in recorded)
                {
                    var rebelRealm = faction.GetRebelKingdom();
                    committed.UnionWith(faction.Members.Where(c => c != null && (c.Kingdom == realm || (rebelRealm != null && c.Kingdom == rebelRealm))));
                    if (rebelRealm != null) committed.UnionWith(rebelRealm.Clans);
                }
            }
            else if (manager != null && ideology != null)
                committed.UnionWith(ideology.GetGrandCoalitionPreviewMembers(realm, manager));
            committed.RemoveWhere(c => c == null || c.IsEliminated || c == realm.RulingClan);

            // One combined projection prevents the same ally being counted for several blocs.
            var projection = RebellionPowerHelper.CalculateProjectedConflictPower(realm, committed, null, null,
                includeProjectedSupport: committed.Count > 0 && !recorded.Any(f => f.IsCivilWarActive()));
            var chances = projection.ProjectedSupporters.ToDictionary(s => s.Clan, s => s.JoinChance);
            var population = new HashSet<Clan>(realm.Clans.Where(c => c != null && !c.IsEliminated));
            population.UnionWith(committed);
            population.Add(realm.RulingClan);
            foreach (var clan in population)
            {
                bool ruler = clan == realm.RulingClan;
                bool rebel = committed.Contains(clan);
                bool playerLoyal = clan == Clan.PlayerClan && recorded.Any(f => f.HasLoyaltyDeclaration(clan));
                double chance = rebel ? 1 : chances.TryGetValue(clan, out float expected) ? expected : 0;
                double loyalty = playerLoyal ? 1 : RebellionPowerHelper.CalculateLoyalistContributionMultiplier(realm, clan, manager?.GetIdeologicalFaction(clan));
                var share = new CrownPowerShare(RebellionPowerHelper.CalculateClanPower(clan), chance, loyalty, ruler,
                    undecidedPlayer: clan == Clan.PlayerClan && !ruler && !rebel && !playerLoyal);
                result.Houses.Add(clan, share);
                result.Total = result.Total.Add(share);
            }
            return result;
        }
    }
}
