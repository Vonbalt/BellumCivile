using System;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;

namespace BellumCivile.Behaviors
{
    public sealed partial class CrownAccessionBehavior
    {
        // Read current facts immediately before retirement. Saved flags are prerequisites,
        // not proof that the native kingdom is empty or that transferred assets survived.
        internal bool TryValidateRealmUnionForRetirement(CrownAccessionRecord accession, out string reason)
            => TryValidateRealmUnionRetirementState(accession, false, out reason);

        private bool TryValidateRealmUnionRetirementState(CrownAccessionRecord accession, bool retired, out string reason)
        {
            reason = "retirement validation requires a registered live absorption";
            var journal = accession?.Union;
            if (journal == null || _accessions?.Contains(accession) != true || accession.Completed || journal.Completed
                || journal.Source == null || journal.Destination == null
                || (!retired && (journal.RetirementStarted || journal.SourceRetired))
                || (retired && (!journal.RetirementStarted || !journal.RetirementReturned))
                || journal.Source == journal.Destination || journal.Source.IsEliminated != retired || journal.Destination.IsEliminated
                || journal.Source != accession.Realm || journal.Heir != accession.Heir
                || journal.Destination.RulingClan != journal.SurvivingHouse || journal.SurvivingHouse?.Kingdom != journal.Destination
                || journal.Heir?.IsAlive != true || journal.Heir.IsDisabled || journal.Heir.Clan != journal.SurvivingHouse
                || (RegencyBehavior.Instance?.GetLegalClanHead(journal.SurvivingHouse) ?? journal.SurvivingHouse?.Leader) != journal.Heir)
                return false;
            try
            {
                if (!journal.CrownTransferStarted || !journal.CrownTransferReturned || !journal.CrownVerified) return false;
                if (!(retired ? RealmUnionRetirementRules.VerifyDeliveredClans(journal, out reason)
                    : RealmUnionRetirementRules.VerifyReceipts(journal, out reason))) return false;
                // Check both sides of native membership; DestroyKingdomAction iterates Clans
                // and can destroy a house even when its Kingdom backlink disagrees.
                if (journal.Source.Clans.Any() || Clan.All.Any(c => c.Kingdom == journal.Source)
                    || journal.Source.Settlements.Any() || Settlement.All.Any(s => s.OwnerClan?.Kingdom == journal.Source))
                { reason = "absorbed kingdom still contains clans or settlements"; return false; }
                if (journal.Source.Armies.Any())
                { reason = "absorbed kingdom still has an army"; return false; }
                var factions = Campaign.Current?.GetCampaignBehavior<FactionManagerBehavior>();
                if (factions == null || factions.GetFactionsInKingdom(journal.Source).Any(f => !f.IsIdeology))
                { reason = "source political conflicts must be reconciled before retirement"; return false; }
                if (retired)
                {
                    if (!TryReadRealmUnionObligations(journal.Source, journal.Destination, out var source, out reason, journal.Source)
                        || !TryReadRealmUnionObligations(journal.Destination, journal.Source, out var destination, out reason, journal.Source)
                        || !RealmUnionObligationRules.VerifyRetired(journal, source, destination, out reason)) return false;
                }
                else if (!TryVerifyRealmUnionObligations(accession, out reason)) return false;
                var titles = Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>();
                if (titles == null) { reason = "title service unavailable for final hierarchy verification"; return false; }
                if (!RealmUnionCrownTransfer.VerifyTitles(journal, journal.PreviousHouse?.StringId,
                    journal.SurvivingHouse.StringId, titles.GetTitle, true, out reason)) return false;
                if (titles.GetKingdomPoliticalTitle(journal.Destination)?.TitleId != journal.PrimaryCrownId
                    || (retired ? titles.GetHistoricalRealmSovereignTitle(journal.Source)
                        : titles.GetKingdomPoliticalTitle(journal.Source))?.TitleId != journal.InheritedCrownId)
                { reason = "primary or inherited Crown identity changed before retirement"; return false; }
                foreach (var receipt in journal.Clans)
                {
                    var clan = receipt.Clan;
                    if (clan.IsEliminated || clan.IsBanditFaction || clan.IsUnderMercenaryService
                        || (receipt.EndMercenaryContract ? clan.Kingdom != null : clan.Kingdom != journal.Destination)
                        || clan.Influence != (receipt.EndMercenaryContract ? receipt.PostMoveInfluence : receipt.Influence)
                        || clan.DebtToKingdom != (receipt.EndMercenaryContract ? receipt.PostMoveDebt : receipt.Debt)
                        || clan.Color != receipt.Color || clan.Color2 != receipt.Color2
                        || (clan.ClanOriginalBanner?.Serialize() ?? "") != (receipt.OriginalBanner?.Serialize() ?? "")
                        || !receipt.Holdings.OrderBy(id => id, StringComparer.Ordinal).SequenceEqual(
                            clan.Settlements.Select(s => s.StringId).OrderBy(id => id, StringComparer.Ordinal)))
                    { reason = "transferred clan assets or allegiance changed: " + clan.StringId; return false; }
                }
                reason = null;
                return true;
            }
            catch (Exception ex)
            {
                reason = "retirement validation failed: " + ex.Message;
                journal.PendingReason = reason;
                return false;
            }
        }
    }
}
