using System;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.Core;

namespace BellumCivile.Behaviors
{
    public sealed partial class CrownAccessionBehavior
    {
        internal bool IsRealmUnionProtected(Kingdom realm) => realm != null && _accessions != null
            && _accessions.Any(a => a?.Union != null && !a.Union.Completed
                && (a.Union.Source == realm || a.Union.Destination == realm)
                && HasStartedRealmUnion(a.Union));

        private static bool HasStartedRealmUnion(RealmUnionRecord journal) => journal.CrownTransferStarted
            || journal.CrownTransferReturned || journal.CrownVerified
            || journal.ClientTransferStarted || journal.ClientTransferReturned
            || journal.TradeTransferStarted || journal.TradeTransferReturned
            || journal.AllianceTransferStarted || journal.AllianceTransferReturned
            || journal.RetirementStarted || journal.RetirementReturned || journal.SourceRetired
            || journal.TributeTransfersStarted?.Count > 0 || journal.TributeTransfersReturned?.Count > 0
            || journal.LegacyTributeTransferStarted || journal.LegacyTributeTransferReturned
            || journal.Clans?.Any(c => c != null && (c.ActionStarted || c.ActionReturned || c.ActionCompleted
                || c.RestorationStarted || c.RestorationReturned || c.RestorationCompleted || c.PostMoveCaptured)) == true;

        internal bool IsRealmUnionTitleProtected(string titleId) => !string.IsNullOrWhiteSpace(titleId)
            && _accessions != null && _accessions.Any(a => a?.Union != null && !a.Union.Completed
                && HasStartedRealmUnion(a.Union) && (a.Union.InheritedCrownId == titleId
                    || a.Union.PrimaryCrownId == titleId || a.Union.Titles?.Any(t => t?.TitleId == titleId) == true));

        // Crown/obligation adapters must publish verified prerequisites before the first
        // native move. Never move a clan from an unsaved plan.
        internal bool TryMoveRealmUnionClan(CrownAccessionRecord accession, RealmUnionClanRecord receipt, out string reason)
        {
            reason = "absorption requires a registered journal and verified Crown and obligations";
            var journal = accession?.Union;
            var clan = receipt?.Clan;
            if (journal == null || accession.Completed || journal.Completed || journal.RetirementStarted
                || _accessions == null || !_accessions.Contains(accession)
                || journal.Clans == null || !journal.Clans.Contains(receipt) || clan == null
                || !journal.CrownTransferStarted || !journal.CrownTransferReturned || !journal.CrownVerified || !journal.ObligationsSettled
                || journal.Source == null || journal.Destination == null || journal.Source == journal.Destination
                || journal.Source.IsEliminated || journal.Destination.IsEliminated
                || journal.Destination.RulingClan != journal.SurvivingHouse
                || journal.Heir?.IsAlive != true || journal.Heir.IsDisabled
                || journal.Heir.Clan != journal.SurvivingHouse
                || (RegencyBehavior.Instance?.GetLegalClanHead(journal.SurvivingHouse)
                    ?? journal.SurvivingHouse?.Leader) != journal.Heir
                || receipt.Holdings == null || receipt.Holdings.Any(string.IsNullOrWhiteSpace)
                || receipt.Holdings.Distinct(StringComparer.Ordinal).Count() != receipt.Holdings.Count
                || float.IsNaN(receipt.Influence) || float.IsInfinity(receipt.Influence)) return false;
            if (!TryVerifyRealmUnionObligations(accession, out reason)) return false;
            if (journal.Source.IsAtWarWith(journal.Destination)
                || Kingdom.All.Any(k => k != null && !k.IsEliminated && k != journal.Source && k != journal.Destination
                    && journal.Source.IsAtWarWith(k) != journal.Destination.IsAtWarWith(k)))
            { reason = "absorbing realms' foreign wars must be reconciled before clan movement"; return false; }
            var factions = Campaign.Current?.GetCampaignBehavior<FactionManagerBehavior>();
            if (factions == null) { reason = "ruler-repair suppression service is unavailable"; return false; }
            bool HoldingsMatch() => receipt.Holdings.Count == clan.Settlements.Count
                && receipt.Holdings.OrderBy(x => x, StringComparer.Ordinal).SequenceEqual(
                    clan.Settlements.Select(s => s.StringId).OrderBy(x => x, StringComparer.Ordinal));
            bool BannerMatches() => (clan.ClanOriginalBanner?.Serialize() ?? "") == (receipt.OriginalBanner?.Serialize() ?? "")
                && clan.Color == receipt.Color && clan.Color2 == receipt.Color2;
            bool Eligible() => !clan.IsEliminated && !clan.IsBanditFaction
                && (receipt.EndMercenaryContract || !NobleClanEligibilityHelper.IsNonPlayerMinorClan(clan));
            bool SafeToMove() => clan.Heroes.All(h => h.PartyBelongedTo?.MapEvent == null
                && h.PartyBelongedTo?.SiegeEvent == null && h.PartyBelongedTo?.Army == null)
                && clan.Settlements.All(s => s.SiegeEvent == null && s.Party?.MapEvent == null);
            bool Destination() => Eligible() && !clan.IsUnderMercenaryService && HoldingsMatch()
                && (receipt.EndMercenaryContract ? clan.Kingdom == null && BannerMatches() : clan.Kingdom == journal.Destination);
            bool result = RealmUnionClanTransfer.Execute(receipt,
                () => Eligible() && clan.Kingdom == journal.Source
                    && clan.IsUnderMercenaryService == receipt.EndMercenaryContract && SafeToMove()
                    && HoldingsMatch() && BannerMatches() && clan.Influence == receipt.Influence && clan.DebtToKingdom == receipt.Debt,
                () =>
                {
                    factions.RunWithRulerRepairSuppressed(journal.Source, () =>
                    {
                        if (receipt.EndMercenaryContract)
                            ChangeKingdomAction.ApplyByLeaveKingdomAsMercenary(clan, showNotification: false);
                        else
                            CourtPoliticalPositionBehavior.MoveToSuccessorRealm(clan, journal.Source, journal.Destination,
                                () => KingdomVisualHelper.ApplyJoinToKingdomPreservingCustomBanner(clan, journal.Destination, false));
                    });
                    receipt.PostMoveInfluence = clan.Influence;
                    receipt.PostMoveDebt = clan.DebtToKingdom;
                    receipt.PostMoveBanner = clan.ClanOriginalBanner == null ? null : new Banner(clan.ClanOriginalBanner);
                    receipt.PostMoveColor = clan.Color;
                    receipt.PostMoveColor2 = clan.Color2;
                    receipt.PostMoveCaptured = true;
                }, Destination,
                () => Destination() && receipt.PostMoveCaptured && clan.Influence == receipt.PostMoveInfluence
                    && clan.DebtToKingdom == receipt.PostMoveDebt && clan.Color == receipt.PostMoveColor
                    && clan.Color2 == receipt.PostMoveColor2
                    && (clan.ClanOriginalBanner?.Serialize() ?? "") == (receipt.PostMoveBanner?.Serialize() ?? ""),
                () =>
                {
                    clan.Influence = receipt.Influence;
                    clan.DebtToKingdom = receipt.Debt;
                    clan.Banner = receipt.OriginalBanner == null ? null : new Banner(receipt.OriginalBanner);
                    clan.Color = receipt.Color;
                    clan.Color2 = receipt.Color2;
                    KingdomVisualHelper.MarkClanVisualsDirty(clan);
                },
                () => Destination() && BannerMatches() && clan.Influence == receipt.Influence && clan.DebtToKingdom == receipt.Debt,
                out reason);
            if (!result) journal.PendingReason = clan.StringId + ": " + reason;
            return result;
        }
    }
}
