using System.Collections.Generic;
using System.Linq;
using BellumCivile.Behaviors;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Election;
using TaleWorlds.CampaignSystem.Settlements;

namespace BellumCivile
{
    internal sealed class CourtClientGrantObjectiveSource : ICourtAgendaObjectiveSource
    {
        public string Kind => CourtClientGrantRules.Kind;
        internal static Settlement Fief(string id) => Settlement.All.FirstOrDefault(s => s.StringId == id);
        internal static Kingdom Client(string id) => Kingdom.All.FirstOrDefault(k => k.StringId == id);
        internal static bool Eligible(Kingdom realm, Settlement fief, Kingdom client)
        {
            if (!CourtAgendaBehavior.ValidRealm(realm) || !CourtAgendaBehavior.ValidRealm(client) || client == realm
                || realm.Leader.IsDead || client.Leader.IsDead || client.IsAtWarWith(realm)
                || ClientKingdomBehavior.Instance?.GetSuzerain(client) != realm
                || !CourtAgendaBehavior.Eligible(realm.RulingClan, realm) || !CourtAgendaBehavior.Eligible(client.RulingClan, client)
                || fief?.Town == null || fief.OwnerClan != realm.RulingClan || fief.IsUnderSiege
                || realm.RulingClan.Fiefs.Count <= 1) return false;
            var titles = CourtTitleGrantObjectiveSource.Titles;
            if (titles == null || !titles.TryGetBarony(fief, out var title) || !title.IsActive
                || title.DeFactoHolderClanId != realm.RulingClan.StringId
                || titles.IsPendingClientGrantCustody(title, realm.RulingClan)
                || Campaign.Current.GetCampaignBehavior<ClaimFeudBehavior>()?.HasActiveDisputeForTitle(title.TitleId) == true
                || Campaign.Current.GetCampaignBehavior<FiefDeliberationBehavior>()?.HasPendingFiefVoteForSettlement(realm, fief) == true
                || realm.UnresolvedDecisions.Any(d => d is SettlementClaimantDecision claim && claim.Settlement == fief
                    || d is SettlementClaimantPreliminaryDecision revoke && revoke.Settlement == fief)) return false;
            // Any live war may still restore an occupation to its pre-war owner at peace.
            return Campaign.Current.GetCampaignBehavior<WarScoreBehavior>()?.GetActiveWars().Any(w =>
                w.GetSnapshot(fief.StringId) is WarScoreFiefSnapshotRecord snapshot
                && snapshot.OwnerKingdomId != realm.StringId) != true;
        }
        internal static bool NpcEligible(Kingdom realm, Settlement fief, Kingdom client)
        {
            var titles = CourtTitleGrantObjectiveSource.Titles;
            if (!Eligible(realm, fief, client) || !titles.TryGetBarony(fief, out var title)) return false;
            bool border = client.Fiefs.Any(t => titles.TryGetBarony(t.Settlement, out var other)
                && titles.AreTitlesAdjacentForTitleLogic(title, other));
            var crown = titles.GetRealmSovereignTitle(realm, FeudalHierarchyMode.DeFacto) ?? titles.GetKingdomPoliticalTitle(realm);
            bool peripheral = crown != null && !string.IsNullOrEmpty(crown.CapitalSettlementId) && crown.CapitalSettlementId != fief.StringId;
            float median = realm.RulingClan.Fiefs.Select(t => t.Prosperity).OrderBy(p => p)
                .ElementAt(realm.RulingClan.Fiefs.Count / 2);
            bool peace = !Kingdom.All.Any(k => !k.IsEliminated && (realm.IsAtWarWith(k) || client.IsAtWarWith(k)));
            return CourtClientGrantRules.NpcEligible(realm.RulingClan.Fiefs.Count, peace, border, peripheral,
                fief.Town.Prosperity < median, realm.Leader.GetRelation(client.Leader), realm.Leader.GetTraitLevel(DefaultTraits.Generosity));
        }
        public IEnumerable<CourtObjectiveCandidate> FindCandidates(CourtTermContext c, CourtObjectiveOwner o)
        {
            if (o.Faction != null || o.Sponsor != c.Realm.RulingClan || !c.ClientGrantAdmitted) yield break;
            foreach (var client in ClientKingdomBehavior.Instance?.GetClients(c.Realm) ?? Enumerable.Empty<Kingdom>())
                foreach (var fief in c.Realm.RulingClan.Fiefs.Select(t => t.Settlement))
                    if (Eligible(c.Realm, fief, client) && (c.ManualSelection || NpcEligible(c.Realm, fief, client)))
                        yield return new CourtObjectiveCandidate(fief.StringId, client.StringId, client.StringId);
        }
        public CourtObjectiveEvaluation EvaluateCandidate(CourtTermContext c, CourtObjectiveOwner o, CourtObjectiveCandidate x)
        {
            var client = Client(x.ActionId); var fief = Fief(x.TargetId);
            bool eligible = o.Faction == null && o.Sponsor == c.Realm.RulingClan && Eligible(c.Realm, fief, client)
                && CourtAgendaBehavior.Current?.HasClientGrantFor(fief, c.PlayerAgenda) != true;
            bool viable = eligible && (c.ManualSelection || NpcEligible(c.Realm, fief, client))
                && NpcInfluenceBudgetService.CanAfford(o.Sponsor, CourtClientGrantRules.Cost, NpcInfluenceExpenseKind.Discretionary);
            return new CourtObjectiveEvaluation(eligible, viable, new CourtObjectiveWeight(eligible
                ? CourtClientGrantRules.Weight(c.Realm.Leader.GetTraitLevel(DefaultTraits.Generosity), c.Realm.Leader.GetRelation(client.Leader)) : 0),
                "rare_client_grant; surplus_border_land; no_ballot");
        }
        public void ApplySelection(CourtAgendaRecord a, CourtObjectiveChoice choice)
        {
            var old = a.GetObjective(); var fief = Fief(choice.Candidate.TargetId); var client = Client(choice.Candidate.ActionId);
            CourtTitleGrantObjectiveSource.Titles.TryGetBarony(fief, out var title);
            a.ObjectiveData = new CourtObjectiveRecord { Kind = Kind, TargetId = fief.StringId, ActionId = client.StringId };
            if (old.HasTermSnapshot) a.ObjectiveData.FreezeTerm(old.SelectedDay, old.DeadlineDay);
            a.ClientGrant = new CourtClientGrantRecord { Client = client, Fief = fief, TitleId = title.TitleId,
                OldLegal = title.DeJureHolderClanId, Grantor = a.Realm.RulingClan, Recipient = client.RulingClan,
                Ruler = a.Realm.Leader, Beneficiary = client.Leader };
            a.PolicyId = null;
        }
    }
}
