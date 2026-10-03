using System;
using System.Linq;
using TaleWorlds.CampaignSystem;

namespace BellumCivile.Behaviors
{
    public sealed partial class CrownAccessionBehavior
    {
        private bool TryPrepareRealmUnionLegacyTributes(CrownAccessionRecord accession, out string reason)
        {
            reason = "legacy tribute requires a registered absorption snapshot";
            var journal = accession?.Union;
            if (journal == null || _accessions?.Contains(accession) != true || accession.Completed || journal.Completed
                || journal.RetirementStarted || journal.SourceObligations == null) return false;
            if (!TryVerifyRealmUnionAgreementProgress(accession, out reason)) return false;
            if (!journal.SourceObligations.Keys.Any(k => k.StartsWith("legacy-tribute:", StringComparison.Ordinal))) return true;
            if (!RealmUnionLegacyTributeProjection.TryPrepare(journal, out var projected, out reason)) return false;
            foreach (var obligation in projected)
            {
                string partnerId = obligation.PayerKingdomId == journal.Destination.StringId
                    ? obligation.RecipientKingdomId : obligation.PayerKingdomId;
                var partner = Kingdom.All.FirstOrDefault(k => k.StringId == partnerId);
                if (partner == null || partner.IsEliminated || BellumKingdomVisibilityHelper.IsTemporaryBellumKingdom(partner)
                    || journal.Destination.IsAtWarWith(partner))
                { reason = "legacy tribute counterparty is unavailable or incompatible"; return false; }
            }
            return true;
        }

        private bool TryInheritRealmUnionLegacyTributes(CrownAccessionRecord accession, out string reason)
        {
            if (!TryPrepareRealmUnionLegacyTributes(accession, out reason)) return false;
            var journal = accession.Union;
            if (journal.LegacyTributeTransferReturned
                || !journal.SourceObligations.Keys.Any(k => k.StartsWith("legacy-tribute:", StringComparison.Ordinal))) return true;
            if (journal.CrownTransferStarted || journal.Clans?.Any(c => c?.ActionStarted == true) == true
                || journal.SourceObligations.Keys.Any(k => k.StartsWith("trade:", StringComparison.Ordinal)) && !journal.TradeTransferReturned
                || journal.SourceObligations.Keys.Any(k => k.StartsWith("alliance:", StringComparison.Ordinal)) && !journal.AllianceTransferReturned
                || !RealmUnionTributeProjection.Partners(journal.SourceObligations).All(journal.TributeTransfersReturned.Contains))
            { reason = "legacy tribute must follow other obligations and precede Crown or clan movement"; return false; }
            var treaties = Campaign.Current?.GetCampaignBehavior<ForeignTreatyBehavior>();
            if (treaties == null) { reason = "legacy tribute service unavailable"; return false; }
            if (!treaties.TryInheritRealmUnionLegacyTributes(journal.Source.StringId, journal.Destination.StringId,
                id => {
                    var partner = Kingdom.All.FirstOrDefault(k => k.StringId == id);
                    return partner == null || partner.IsEliminated || BellumKingdomVisibilityHelper.IsTemporaryBellumKingdom(partner)
                        || journal.Destination.IsAtWarWith(partner);
                }, () => journal.LegacyTributeTransferStarted = true,
                () => journal.LegacyTributeTransferReturned = true, out reason)) return false;
            return TryVerifyRealmUnionAgreementProgress(accession, out reason);
        }
    }
}
