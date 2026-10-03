using System;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;

namespace BellumCivile.Behaviors
{
    public sealed partial class CrownAccessionBehavior
    {
        internal bool TryInheritRealmUnionAgreements(CrownAccessionRecord accession, out string reason)
        {
            if (!TryPrepareRealmUnionAgreements(accession, out reason)) return false;
            try
            {
                return TryInheritRealmUnionClients(accession, out reason)
                    && TryInheritRealmUnionTrade(accession, out reason)
                    && TryInheritRealmUnionAlliances(accession, out reason)
                    && TryInheritRealmUnionTributes(accession, out reason)
                    && TryInheritRealmUnionLegacyTributes(accession, out reason)
                    && TryVerifyRealmUnionObligations(accession, out reason);
            }
            catch (Exception ex)
            {
                reason = "agreement inheritance interrupted: " + ex.Message;
                accession.Union.PendingReason = reason;
                return false;
            }
        }

        private bool TryPrepareRealmUnionAgreements(CrownAccessionRecord accession, out string reason)
        {
            reason = "agreement inheritance requires a registered union";
            var journal = accession?.Union;
            if (journal == null || _accessions?.Contains(accession) != true || accession.Completed || journal.Completed
                || journal.SourceObligations == null || journal.DestinationObligations == null) return false;
            journal.ObligationsSettled = false;
            try
            {
                if (!TryVerifyRealmUnionAgreementProgress(accession, out reason)) return false;
                if (!RealmUnionClientProjection.TryPlan(journal, out var plannedSource, out var plannedDestination, out reason)
                    || !RealmUnionObligationRules.TryProjectTrade(plannedSource, plannedDestination,
                    out _, out _, out reason)) return false;
                var clients = Campaign.Current?.GetCampaignBehavior<ClientKingdomBehavior>();
                if (clients == null || journal.Source == null || journal.Destination == null)
                { reason = "agreement inheritance participants or diplomacy service unavailable"; return false; }
                if (!journal.ClientTransferReturned && journal.SourceClientRecords?.Count > 0
                    && !clients.CanInheritRealmUnionClients(journal.Source, journal.Destination, out reason)) return false;
                if (!TryPrepareRealmUnionTributes(accession, out reason)) return false;
                if (!TryPrepareRealmUnionLegacyTributes(accession, out reason)) return false;
                foreach (var key in journal.SourceObligations.Keys.Where(k => k.StartsWith("alliance:", StringComparison.Ordinal)
                    || k.StartsWith("trade:", StringComparison.Ordinal)))
                {
                    bool alliance = key.StartsWith("alliance:", StringComparison.Ordinal);
                    string partnerId = key.Substring(alliance ? 9 : 6);
                    Kingdom partner = Kingdom.All.FirstOrDefault(k => k.StringId == partnerId);
                    if (partner == null || partner.IsEliminated || partner == journal.Source || partner == journal.Destination
                        || BellumKingdomVisibilityHelper.IsTemporaryBellumKingdom(partner)
                        || journal.Destination.IsAtWarWith(partner)
                        || !(IsInheritedRealmUnionClient(journal, clients, partner)
                            || (alliance ? clients.CanStartAlliance(journal.Destination, partner)
                            : clients.CanMakeTradeAgreement(journal.Destination, partner))))
                    { reason = "incompatible inherited agreement: " + key; return false; }
                }
                if (!journal.AllianceTransferStarted && journal.SourceObligations.Keys.Any(k => k.StartsWith("alliance:", StringComparison.Ordinal))
                    && !RealmUnionAllianceAdapter.CanApply(Campaign.Current?.GetCampaignBehavior<IAllianceCampaignBehavior>(),
                        journal.Source, journal.Destination, partner => partner.IsEliminated
                            || BellumKingdomVisibilityHelper.IsTemporaryBellumKingdom(partner)
                            || journal.Destination.IsAtWarWith(partner) || !clients.CanStartAlliance(journal.Destination, partner)
                                && !IsInheritedRealmUnionClient(journal, clients, partner), out reason))
                    return false;
                if (!journal.TradeTransferStarted && journal.SourceObligations.Keys.Any(k => k.StartsWith("trade:", StringComparison.Ordinal))
                    && !RealmUnionTradeAdapter.CanApply(Campaign.Current?.GetCampaignBehavior<ITradeAgreementsCampaignBehavior>(),
                        journal.Source, journal.Destination, partner => partner.IsEliminated
                            || BellumKingdomVisibilityHelper.IsTemporaryBellumKingdom(partner)
                            || journal.Destination.IsAtWarWith(partner) || !clients.CanMakeTradeAgreement(journal.Destination, partner)
                                && !IsInheritedRealmUnionClient(journal, clients, partner), out reason)) return false;
                reason = null;
                return true;
            }
            catch (Exception ex)
            {
                reason = "agreement preflight interrupted: " + ex.Message;
                journal.PendingReason = reason;
                return false;
            }
        }

        private bool TryInheritRealmUnionAlliances(CrownAccessionRecord accession, out string reason)
        {
            reason = "alliance inheritance requires a registered union snapshot";
            var journal = accession?.Union;
            if (journal == null || accession.Completed || journal.Completed || journal.RetirementStarted
                || _accessions?.Contains(accession) != true || journal.SourceObligations == null || journal.DestinationObligations == null
                || journal.Source == null || journal.Destination == null || journal.Source == journal.Destination
                || journal.Source != accession.Realm || journal.Heir != accession.Heir
                || journal.Source.RulingClan != journal.PreviousHouse || journal.Destination.RulingClan != journal.SurvivingHouse
                || journal.Heir?.IsAlive != true || journal.Heir.IsDisabled || journal.Heir.Clan != journal.SurvivingHouse
                || (RegencyBehavior.Instance?.GetLegalClanHead(journal.SurvivingHouse) ?? journal.SurvivingHouse?.Leader) != journal.Heir)
                return false;
            if (!TryVerifyRealmUnionAgreementProgress(accession, out reason)) return false;
            if (journal.AllianceTransferReturned || !journal.SourceObligations.Keys.Any(k => k.StartsWith("alliance:", StringComparison.Ordinal)))
                return true;
            if (journal.CrownTransferStarted || journal.Clans?.Any(c => c?.ActionStarted == true) == true
                || !journal.TradeTransferReturned && journal.SourceObligations.Keys.Any(k => k.StartsWith("trade:", StringComparison.Ordinal)))
            { reason = "alliance inheritance must follow trade and precede Crown or clan movement"; return false; }
            var alliances = Campaign.Current?.GetCampaignBehavior<IAllianceCampaignBehavior>();
            var clients = Campaign.Current?.GetCampaignBehavior<ClientKingdomBehavior>();
            if (clients == null) { reason = "client diplomacy service unavailable"; return false; }
            if (!RealmUnionAllianceAdapter.TryApply(alliances, journal.Source, journal.Destination,
                partner => partner.IsEliminated || BellumKingdomVisibilityHelper.IsTemporaryBellumKingdom(partner)
                    || journal.Destination.IsAtWarWith(partner) || !clients.CanStartAlliance(journal.Destination, partner),
                () => journal.AllianceTransferStarted = true,
                () => journal.AllianceTransferReturned = true, out reason)) return false;
            return TryVerifyRealmUnionAgreementProgress(accession, out reason);
        }
    }
}
