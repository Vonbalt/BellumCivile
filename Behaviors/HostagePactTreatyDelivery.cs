using System;
using System.Linq;
using TaleWorlds.CampaignSystem;

namespace BellumCivile.Behaviors
{
    public sealed partial class HostagePactBehavior
    {
        // Reserve both hostages before the settlement callback can transfer wealth or land.
        internal HostageDeliveryResult DeliverTreaty(TreatyProposalRecord proposal, Kingdom first,
            Kingdom second, Func<bool> settle)
        {
            if (_maintaining || proposal == null || first == null || second == null
                || !TreatyHostageTerms.ValidShape(proposal.Terms, first.StringId, second.StringId, out _))
                return HostageDeliveryResult.Rejected;
            var pact = _pacts.FirstOrDefault(p => p?.TreatyProposalId == proposal.ProposalId);
            if (pact == null)
            {
                var terms = proposal.Terms.Where(t => t.Type == TreatyTermType.HostagePeace).ToList();
                if (terms.Count == 0 || _pacts.Any(p => p != null && p.Phase != HostagePactPhase.Ended
                    && ((p.FirstRealm == first && p.SecondRealm == second) || (p.FirstRealm == second && p.SecondRealm == first))))
                    return HostageDeliveryResult.Rejected;
                foreach (var term in terms)
                    if (!TreatyHostageTerms.StillDeliverable(term, term.FromKingdomId == first.StringId ? first : second,
                        term.ToKingdomId == first.StringId ? first : second)) return HostageDeliveryResult.Rejected;
                TreatyHostageRecord PrepareSide(Kingdom from, Kingdom to)
                {
                    var term = terms.FirstOrDefault(t => t.FromKingdomId == from.StringId);
                    if (term == null) return null;
                    var record = Prepare(TreatyHostageEligibility.GetCandidates(from)
                        .First(c => c.Hero.StringId == term.HeroId), from.RulingClan, to.RulingClan);
                    var war = Campaign.Current?.GetCampaignBehavior<WarScoreBehavior>()?.GetActiveWar(first, second);
                    var holding = TreatyHostageTerms.SettlementHolding(to.RulingClan, war, proposal.Terms);
                    if (record == null || holding == null) return null;
                    record.Holding = holding;
                    return record;
                }
                pact = new HostagePactRecord { Id = Guid.NewGuid().ToString("N"), TreatyProposalId = proposal.ProposalId,
                    FirstRealm = first, SecondRealm = second, FirstHouse = first.RulingClan, SecondHouse = second.RulingClan,
                    SigningDayRecorded = true, TreatySigningDay = CampaignTime.Now.ToDays,
                    FirstHostage = PrepareSide(first, second), SecondHostage = PrepareSide(second, first) };
                // Do not stage custody in a holding this very settlement is about to surrender.
                if (terms.Any(t => t.FromKingdomId == first.StringId) && pact.FirstHostage == null
                    || terms.Any(t => t.FromKingdomId == second.StringId) && pact.SecondHostage == null
                    || proposal.Terms.Any(t => ClientWarTerritory.IsTerritorial(t.Type)
                        && (t.SettlementId == pact.FirstHostage?.Holding?.StringId || t.SettlementId == pact.SecondHostage?.Holding?.StringId)))
                    return HostageDeliveryResult.Rejected;
                _pacts.Add(pact);
            }
            _maintaining = true;
            try
            {
                return HostageTreatyDelivery.Run(pact,
                    () => BellumCivileOptions.EnableWarPeaceLogicRevamp && first.RulingClan == pact.FirstHouse
                        && second.RulingClan == pact.SecondHouse && !first.IsEliminated && !second.IsEliminated,
                    () => Place(pact.FirstHostage) && Place(pact.SecondHostage), settle,
                    () => !first.IsAtWarWith(second),
                    () => first.RulingClan == pact.FirstHouse && second.RulingClan == pact.SecondHouse
                        && (pact.FirstHostage == null || InTreatyCustody(pact.FirstHostage))
                        && (pact.SecondHostage == null || InTreatyCustody(pact.SecondHostage))
                        && pact.TryActivate(pact.SigningDayRecorded ? pact.TreatySigningDay : CampaignTime.Now.ToDays),
                    () => { pact.TryAbortPreparation(); Release(pact, pact.FirstHostage); Release(pact, pact.SecondHostage); pact.TryComplete(); });
            }
            finally { _maintaining = false; }
        }
    }
}
