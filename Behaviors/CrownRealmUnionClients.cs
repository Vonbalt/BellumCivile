using System.Linq;
using TaleWorlds.CampaignSystem;

namespace BellumCivile.Behaviors
{
    public sealed partial class CrownAccessionBehavior
    {
        private static bool IsInheritedRealmUnionClient(RealmUnionRecord journal, ClientKingdomBehavior clients, Kingdom partner)
            => !journal.ClientTransferStarted && partner != null
                && journal.SourceClientRecords?.Any(c => c.ClientKingdomId == partner.StringId) == true
                && clients.GetSuzerain(partner) == journal.Source;

        private bool TryInheritRealmUnionClients(CrownAccessionRecord accession, out string reason)
        {
            var journal = accession?.Union;
            reason = "client inheritance requires a registered union with unchanged rulers";
            if (journal == null || _accessions?.Contains(accession) != true || accession.Completed || journal.Completed
                || journal.RetirementStarted || journal.Source == null || journal.Destination == null
                || journal.Source != accession.Realm || journal.Heir != accession.Heir
                || journal.Source.RulingClan != journal.PreviousHouse || journal.Destination.RulingClan != journal.SurvivingHouse
                || journal.Heir?.IsAlive != true || journal.Heir.IsDisabled || journal.Heir.Clan != journal.SurvivingHouse
                || (RegencyBehavior.Instance?.GetLegalClanHead(journal.SurvivingHouse) ?? journal.SurvivingHouse?.Leader) != journal.Heir)
                return false;
            if (!TryVerifyRealmUnionAgreementProgress(accession, out reason)) return false;
            if (journal.ClientTransferReturned || (journal.SourceClientRecords?.Count ?? 0) == 0) return true;
            if (HasStartedRealmUnion(journal))
            { reason = "client registry inheritance must be the first absorption transfer"; return false; }
            var clients = Campaign.Current?.GetCampaignBehavior<ClientKingdomBehavior>();
            if (clients == null) { reason = "client registry unavailable"; return false; }
            if (!clients.TryInheritRealmUnionClients(journal.Source, journal.Destination,
                () => journal.ClientTransferStarted = true, () => journal.ClientTransferReturned = true, out reason)) return false;
            return TryVerifyRealmUnionAgreementProgress(accession, out reason);
        }

        internal bool IsRealmUnionClientProtected(string clientId) => !string.IsNullOrWhiteSpace(clientId)
            && _accessions?.Any(a => a?.Union != null && !a.Union.Completed && HasStartedRealmUnion(a.Union)
                && (a.Union.SourceClientRecords?.Any(c => c?.ClientKingdomId == clientId) == true
                    || a.Union.DestinationClientRecords?.Any(c => c?.ClientKingdomId == clientId) == true)) == true;
    }
}
