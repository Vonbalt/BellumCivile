using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;

namespace BellumCivile.Behaviors
{
    public sealed partial class CrownAccessionBehavior
    {
        private bool TryPrepareRealmUnionTributes(CrownAccessionRecord accession, out string reason)
        {
            reason = "native tribute inheritance requires a registered union snapshot";
            var journal = accession?.Union;
            if (journal == null || _accessions?.Contains(accession) != true || accession.Completed || journal.Completed
                || journal.RetirementStarted || journal.SourceObligations == null || journal.Source == null || journal.Destination == null
                || journal.TributeTransfersStarted == null || journal.TributeTransfersReturned == null) return false;
            if (!TryVerifyRealmUnionAgreementProgress(accession, out reason)) return false;
            var dates = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string id in RealmUnionTributeProjection.Partners(journal.SourceObligations))
            {
                if (journal.TributeTransfersReturned.Contains(id))
                {
                    if (journal.TributeDestinationPeaceDates == null || !journal.TributeDestinationPeaceDates.TryGetValue(id, out string savedDate))
                    { reason = "returned native tribute has no destination date snapshot"; return false; }
                    dates.Add(id, savedDate);
                    continue;
                }
                Kingdom partner = Kingdom.All.FirstOrDefault(k => k.StringId == id);
                if (partner == null || partner.IsEliminated || BellumKingdomVisibilityHelper.IsTemporaryBellumKingdom(partner)
                    || !RealmUnionTributeAdapter.TryPrepare(journal.Source.GetStanceWith(partner), journal.Destination.GetStanceWith(partner),
                        journal.Source, journal.Destination, partner, out var plan, out reason))
                { reason = reason ?? "native tribute partner is unavailable"; return false; }
                dates.Add(id, Number(plan.DestinationPeaceDate.ToDays));
            }
            if (journal.TributeDestinationPeaceDates == null)
            {
                if (journal.TributeTransfersStarted.Count != 0)
                { reason = "started tribute cannot recapture destination history"; return false; }
                journal.TributeDestinationPeaceDates = dates;
            }
            else if (!RealmUnionObligationRules.Same(journal.TributeDestinationPeaceDates, dates))
            { reason = "destination peace history changed after tribute capture"; return false; }
            return true;
        }

        private bool TryInheritRealmUnionTributes(CrownAccessionRecord accession, out string reason)
        {
            if (!TryPrepareRealmUnionTributes(accession, out reason)) return false;
            var journal = accession.Union;
            if (journal.CrownTransferStarted || journal.Clans?.Any(c => c?.ActionStarted == true) == true)
            {
                if (RealmUnionTributeProjection.Partners(journal.SourceObligations).All(journal.TributeTransfersReturned.Contains))
                    return TryVerifyRealmUnionAgreementProgress(accession, out reason);
                reason = "native tribute must transfer before Crown and clans";
                return false;
            }
            if (journal.SourceObligations.Keys.Any(k => k.StartsWith("trade:", StringComparison.Ordinal)) && !journal.TradeTransferReturned
                || journal.SourceObligations.Keys.Any(k => k.StartsWith("alliance:", StringComparison.Ordinal)) && !journal.AllianceTransferReturned)
            { reason = "native tribute must follow agreement transfers"; return false; }
            foreach (string id in RealmUnionTributeProjection.Partners(journal.SourceObligations))
            {
                if (journal.TributeTransfersReturned.Contains(id)) continue;
                Kingdom partner = Kingdom.All.FirstOrDefault(k => k.StringId == id && !k.IsEliminated);
                if (partner == null) { reason = "native tribute partner became unavailable"; return false; }
                if (!TryVerifyRealmUnionAgreementProgress(accession, out reason)
                    || !RealmUnionTributeAdapter.TryPrepare(journal.Source.GetStanceWith(partner), journal.Destination.GetStanceWith(partner),
                        journal.Source, journal.Destination, partner, out var plan, out reason)) return false;
                if (!RealmUnionTributeAdapter.TryApply(plan, () => journal.TributeTransfersStarted.Add(id),
                    () => journal.TributeTransfersReturned.Add(id), out reason)) return false;
                if (!TryVerifyRealmUnionAgreementProgress(accession, out reason)) return false;
            }
            return true;
        }
    }
}
