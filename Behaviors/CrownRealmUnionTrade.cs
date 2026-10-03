using System;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;

namespace BellumCivile.Behaviors
{
    public sealed partial class CrownAccessionBehavior
    {
        internal bool TryInheritRealmUnionTrade(CrownAccessionRecord accession, out string reason)
        {
            reason = "trade inheritance requires a registered absorption snapshot";
            var journal = accession?.Union;
            if (journal == null || accession.Completed || journal.Completed || journal.RetirementStarted
                || _accessions?.Contains(accession) != true || journal.SourceObligations == null || journal.DestinationObligations == null
                || journal.Source == null || journal.Destination == null || journal.Source == journal.Destination
                || journal.Source != accession.Realm || journal.Heir != accession.Heir
                || journal.Source.RulingClan != journal.PreviousHouse || journal.Destination.RulingClan != journal.SurvivingHouse
                || journal.Heir?.IsAlive != true || journal.Heir.IsDisabled || journal.Heir.Clan != journal.SurvivingHouse
                || (RegencyBehavior.Instance?.GetLegalClanHead(journal.SurvivingHouse) ?? journal.SurvivingHouse?.Leader) != journal.Heir)
                return false;
            journal.ObligationsSettled = false;
            if (journal.TradeTransferStarted || journal.TradeTransferReturned)
                return TryVerifyRealmUnionAgreementProgress(accession, out reason);
            if (!journal.SourceObligations.Keys.Any(k => k.StartsWith("trade:", StringComparison.Ordinal)))
                return TryVerifyRealmUnionAgreementProgress(accession, out reason);
            if (journal.CrownTransferStarted || journal.AllianceTransferStarted || journal.LegacyTributeTransferStarted
                || journal.TributeTransfersStarted?.Count > 0 || journal.Clans?.Any(c => c?.ActionStarted == true) == true
                || journal.SourceClientRecords?.Count > 0 && !journal.ClientTransferReturned)
            { reason = "trade inheritance must precede Crown and clan movement"; return false; }
            try
            {
                if (!TryReadRealmUnionObligations(journal.Source, journal.Destination, out var source, out reason)
                    || !TryReadRealmUnionObligations(journal.Destination, journal.Source, out var destination, out reason)) return false;
                if (!RealmUnionObligationRules.VerifyProgress(journal, source, destination, out reason)) return false;
                if (!RealmUnionObligationRules.TryProjectTrade(source, destination, out _, out _, out reason)) return false;
                if (!source.Keys.Any(k => k.StartsWith("trade:", StringComparison.Ordinal)))
                    return TryVerifyRealmUnionAgreementProgress(accession, out reason);
                var trade = Campaign.Current?.GetCampaignBehavior<ITradeAgreementsCampaignBehavior>();
                var clients = Campaign.Current?.GetCampaignBehavior<ClientKingdomBehavior>();
                if (clients == null) { reason = "client diplomacy service unavailable"; return false; }
                if (!RealmUnionTradeAdapter.TryApply(trade, journal.Source, journal.Destination,
                    partner => partner.IsEliminated || BellumKingdomVisibilityHelper.IsTemporaryBellumKingdom(partner)
                        || journal.Destination.IsAtWarWith(partner) || !clients.CanMakeTradeAgreement(journal.Destination, partner),
                    () => journal.TradeTransferStarted = true,
                    () => journal.TradeTransferReturned = true, out reason)) return false;
                return TryVerifyRealmUnionAgreementProgress(accession, out reason);
            }
            catch (Exception ex)
            {
                reason = "trade inheritance interrupted: " + ex.Message;
                journal.PendingReason = reason;
                return false;
            }
        }
    }
}
