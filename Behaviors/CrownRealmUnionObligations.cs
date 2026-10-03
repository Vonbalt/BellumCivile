using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;

namespace BellumCivile.Behaviors
{
    public sealed partial class CrownAccessionBehavior
    {
        private static bool TryReadRealmUnionObligations(Kingdom realm, Kingdom otherParticipant,
            out Dictionary<string, string> result, out string reason, Kingdom retiredParticipant = null)
        {
            result = null;
            reason = "absorption diplomatic services unavailable";
            var alliances = Campaign.Current?.GetCampaignBehavior<IAllianceCampaignBehavior>();
            var trade = Campaign.Current?.GetCampaignBehavior<ITradeAgreementsCampaignBehavior>();
            var clients = Campaign.Current?.GetCampaignBehavior<ClientKingdomBehavior>();
            var hostages = Campaign.Current?.GetCampaignBehavior<HostagePactBehavior>();
            var treaties = Campaign.Current?.GetCampaignBehavior<ForeignTreatyBehavior>();
            if (realm == null || realm.IsEliminated && realm != retiredParticipant
                || otherParticipant == null || otherParticipant.IsEliminated && otherParticipant != retiredParticipant
                || realm == otherParticipant || alliances == null || trade == null || clients == null
                || hostages == null || treaties == null) return false;
            if (BellumKingdomVisibilityHelper.IsTemporaryBellumKingdom(realm) || clients.IsClientKingdom(realm)
                || SuccessionRealmRules.Classify(SuccessionLawHelper.GetLawsForKingdom(realm).SuccessionLaw) != RealmSuccessionSystem.Hereditary)
            { reason = "absorption requires two independent hereditary realms"; return false; }
            if (hostages.HasPendingPartitionCustody(realm))
            { reason = "hostage custody must be reconciled before absorption"; return false; }
            if (!RealmUnionAllianceAdapter.CanTransferCalls(alliances, realm, otherParticipant, out reason)) return false;
            if (treaties.HasPendingRealmUnionSettlement(realm.StringId) || realm.UnresolvedDecisions.Any())
            { reason = "pending treaty or kingdom decision must be settled before absorption"; return false; }
            var snapshot = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var other in Kingdom.All.Where(k => k != null && !k.IsEliminated && k != realm))
            {
                if (realm.IsAtWarWith(other))
                {
                    if (other == otherParticipant || BellumKingdomVisibilityHelper.IsTemporaryBellumKingdom(other)
                        || StorylineWarProtectionHelper.IsPeaceBlocked(realm, other))
                    { reason = "internal, mutual or scripted war requires separate absorption handling"; return false; }
                    snapshot.Add("war:" + other.StringId, "active");
                }
                if (alliances.IsAllyWithKingdom(realm, other))
                    snapshot.Add("alliance:" + other.StringId, Number(alliances.GetAllianceEndDate(realm, other).ToDays));
                if (trade.HasTradeAgreement(realm, other, out var end))
                    snapshot.Add("trade:" + other.StringId, Number(end.EndTime.ToDays));
                var stance = realm.GetStanceWith(other);
                int tribute = stance?.GetDailyTributeToPay(realm) ?? 0;
                if (tribute != 0)
                {
                    snapshot.Add("tribute:" + other.StringId, tribute.ToString(CultureInfo.InvariantCulture));
                    snapshot.Add("tribute-date:" + other.StringId, Number(stance.PeaceDeclarationDate.ToDays));
                    snapshot.Add("tribute-installments:" + other.StringId, stance.DailyTributeInstallments.ToString(CultureInfo.InvariantCulture));
                    snapshot.Add("tribute-paid:" + other.StringId, stance.GetTotalTributePaid(realm).ToString(CultureInfo.InvariantCulture));
                }
            }
            if (!RealmUnionClientSnapshot.TryCapture(clients.GetClientRecords(), realm.StringId,
                out _, out var clientEntries, out reason)) return false;
            foreach (var entry in clientEntries) snapshot.Add(entry.Key, entry.Value);
            foreach (var entry in RealmUnionLegacyTributeProjection.Snapshot(treaties.GetRealmUnionTributes(realm.StringId), realm.StringId))
                snapshot.Add(entry.Key, entry.Value);
            result = snapshot;
            reason = null;
            return true;
        }

        private static string Number(double value) => value.ToString("R", CultureInfo.InvariantCulture);

        internal bool TryCaptureRealmUnionObligations(CrownAccessionRecord accession, out string reason)
        {
            var journal = accession?.Union;
            reason = "obligation capture requires a registered unstarted absorption";
            if (journal == null || accession.Completed || journal.Completed || _accessions?.Contains(accession) != true
                || HasStartedRealmUnion(journal)) return false;
            if (journal.SourceObligations != null || journal.DestinationObligations != null)
                return TryVerifyRealmUnionAgreementProgress(accession, out reason);
            if (!TryReadRealmUnionObligations(journal.Source, journal.Destination, out var source, out reason)
                || !TryReadRealmUnionObligations(journal.Destination, journal.Source, out var destination, out reason)) return false;
            var treaties = Campaign.Current.GetCampaignBehavior<ForeignTreatyBehavior>();
            var sourceSchedules = RealmUnionLegacyTributeProjection.Copy(treaties.GetRealmUnionTributes(journal.Source.StringId));
            var destinationSchedules = RealmUnionLegacyTributeProjection.Copy(treaties.GetRealmUnionTributes(journal.Destination.StringId));
            var clients = Campaign.Current.GetCampaignBehavior<ClientKingdomBehavior>().GetClientRecords();
            if (!RealmUnionClientSnapshot.TryCapture(clients, journal.Source.StringId, out var sourceClients, out _, out reason)
                || !RealmUnionClientSnapshot.TryCapture(clients, journal.Destination.StringId, out var destinationClients, out _, out reason)) return false;
            journal.SourceObligations = source;
            journal.DestinationObligations = destination;
            journal.SourceLegacyTributes = sourceSchedules;
            journal.DestinationLegacyTributes = destinationSchedules;
            journal.SourceClientRecords = sourceClients;
            journal.DestinationClientRecords = destinationClients;
            return TryVerifyRealmUnionAgreementProgress(accession, out reason);
        }

        internal bool TryVerifyRealmUnionObligations(CrownAccessionRecord accession, out string reason)
            => TryVerifyRealmUnionObligationState(accession, true, out reason);

        private bool TryVerifyRealmUnionAgreementProgress(CrownAccessionRecord accession, out string reason)
            => TryVerifyRealmUnionObligationState(accession, false, out reason);

        private bool TryVerifyRealmUnionObligationState(CrownAccessionRecord accession, bool requireComplete, out string reason)
        {
            reason = "obligation verification requires a registered absorption";
            var journal = accession?.Union;
            if (journal == null || _accessions?.Contains(accession) != true) return false;
            journal.ObligationsSettled = false;
            if (accession.Completed || journal.Completed || journal.SourceRetired) return false;
            if (!TryReadRealmUnionObligations(journal.Source, journal.Destination, out var source, out reason)
                || !TryReadRealmUnionObligations(journal.Destination, journal.Source, out var destination, out reason)
                || !(requireComplete ? RealmUnionObligationRules.VerifyUnchanged(journal, source, destination, out reason)
                    : RealmUnionObligationRules.VerifyProgress(journal, source, destination, out reason)))
            { journal.PendingReason = reason; return false; }
            journal.ObligationsSettled = requireComplete;
            return true;
        }
    }
}
