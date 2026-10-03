using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;

namespace BellumCivile.Behaviors
{
    public partial class CivilWarResolutionBehavior
    {
        internal void ReconcileSatisfiedCivilWarDemands(IEnumerable<FactionObject> factions)
        {
            foreach (var faction in factions.Where(f => !f.IsIdeology && f.HasTrackedRebelKingdom).ToList())
                TrySettleSatisfiedCivilWarDemand(faction);
        }

        private bool TrySettleSatisfiedCivilWarDemand(FactionObject faction)
        {
            if (CivilWarConflictBehavior.IsFactionTransferPending(faction)) return false;
            var realm = faction?.ParentKingdom;
            if (realm?.IsEliminated != false || faction.IsChallengeStartupPending
                || CrownAccessionBehavior.Instance?.IsPending(realm) == true
                || _pendingSuccessionCandidates.ContainsKey(realm.StringId)
                || realm.UnresolvedDecisions.Any(d => d is TaleWorlds.CampaignSystem.Election.KingSelectionKingdomDecision)) return false;
            var legalRuler = RegencyBehavior.Instance?.GetLegalClanHead(realm.RulingClan) ?? realm.Leader;
            if (legalRuler?.IsAlive != true) return false;
            var titles = Campaign.Current.GetCampaignBehavior<FeudalTitleBehavior>();
            var crown = titles?.GetRealmSovereignTitle(realm);
            if (crown == null || crown.DeJureHolderClanId != realm.RulingClan.StringId
                || crown.DeFactoHolderClanId != realm.RulingClan.StringId) return false;
            bool satisfied = CivilWarContinuationRules.DemandSatisfied(faction.Type,
                faction.AbdicationMonarch != null && legalRuler != faction.AbdicationMonarch,
                faction.Leader == realm.RulingClan);
            if (!satisfied) return false;
            var rebel = faction.GetTrackedRebelKingdomIncludingEliminated();
            if (rebel == null || rebel == realm || rebel.IsEliminated) return false;
            // This settles only this coalition, with no tribunal or victory over another side.
            ResolveWhitePeace(faction, rebel, demandSatisfied: true);
            return true;
        }
    }
}
