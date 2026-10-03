using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;

namespace BellumCivile.Behaviors
{
    public partial class PartitionSuccessionBehavior
    {
        internal bool TryMoveCrownPromotionHouses(CrownPartitionPromotionRecord journal, out string reason)
        {
            reason = "house movement requires a registered promotion and prepared diplomacy";
            if (journal == null || journal.Completed || _pendingPartitions == null
                || !_pendingPartitions.Any(p => p?.CrownPromotions?.Contains(journal) == true)
                || !journal.TransferDiplomacyPrepared || !journal.GovernmentStarted || !journal.GovernmentReturned
                || !journal.GovernmentVerified || !journal.HasVerifiedEstateReceipts()
                || !journal.CrownRegistrationStarted || !journal.CrownRegistrationReturned
                || !journal.TryRecoverFounder(journal.Founder) || !journal.TryRecoverRealm(journal.Successor)
                || journal.Parent.IsEliminated || journal.Parent.RulingClan != journal.RetainedHouse
                || journal.Parent == journal.Successor || journal.Parent.IsAtWarWith(journal.Successor)
                || journal.Houses == null || journal.Houses.Count == 0) return false;
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var house in journal.Houses)
            {
                var receipt = house?.Transfer;
                if (receipt?.Clan == null || string.IsNullOrWhiteSpace(receipt.Clan.StringId)
                    || !ids.Add(receipt.Clan.StringId) || receipt.EndMercenaryContract
                    || receipt.Clan == journal.RetainedHouse || receipt.Holdings == null
                    || receipt.Holdings.Any(string.IsNullOrWhiteSpace)
                    || receipt.Holdings.Distinct().Count() != receipt.Holdings.Count
                    || float.IsNaN(receipt.Influence) || float.IsInfinity(receipt.Influence))
                { reason = "frozen house manifest is incomplete or ambiguous"; return false; }
            }
            if (!ids.Contains(journal.FounderId)) return false;
            // Native joining adjusts diplomatic stances. Do not use it until the new
            // realm has inherited the required war participants, including temporary foes.
            if (Kingdom.All.Any(k => k != null && !k.IsEliminated && k != journal.Parent && k != journal.Successor
                && journal.Parent.IsAtWarWith(k) != journal.Successor.IsAtWarWith(k)))
            { reason = "successor wars do not match the parent realm before movement"; return false; }
            var factions = Campaign.Current?.GetCampaignBehavior<FactionManagerBehavior>();
            if (factions == null) { reason = "ruler-repair suppression service is unavailable"; return false; }
            var titles = Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>();
            if (titles == null || !titles.TryRegisterCrownPromotion(journal, out reason)) return false;
            var batch = GetCrownPromotionBatch(journal);
            foreach (var house in journal.Houses.OrderBy(h => h.Transfer.Clan == journal.Founder ? 0 : 1)
                .ThenBy(h => h.Transfer.Clan.StringId, StringComparer.Ordinal))
            {
                var receipt = house.Transfer;
                Clan clan = receipt.Clan;
                var expectedHoldings = new HashSet<string>(receipt.Holdings, StringComparer.Ordinal);
                if (clan == journal.Founder) expectedHoldings.UnionWith(journal.EstateFiefIds);
                bool HoldingsMatch() => expectedHoldings.SetEquals(clan.Settlements.Select(s => s.StringId));
                bool BannerMatches() => (clan.ClanOriginalBanner?.Serialize() ?? "") == (receipt.OriginalBanner?.Serialize() ?? "")
                    && clan.Color == receipt.Color && clan.Color2 == receipt.Color2;
                bool Eligible() => !clan.IsEliminated && !clan.IsUnderMercenaryService && !clan.IsBanditFaction
                    && !NobleClanEligibilityHelper.IsNonPlayerMinorClan(clan);
                bool SafeToMove() => clan.Heroes.All(h => h.PartyBelongedTo?.MapEvent == null
                    && h.PartyBelongedTo?.SiegeEvent == null && h.PartyBelongedTo?.Army == null)
                    && clan.Settlements.All(s => s.SiegeEvent == null && s.Party?.MapEvent == null);
                bool Destination() => Eligible() && clan.Kingdom == journal.Successor && HoldingsMatch();
                bool restored = CrownPartitionHouseTransfer.Execute(house,
                    () => Eligible() && clan.Kingdom == journal.Parent && SafeToMove() && HoldingsMatch()
                        && BannerMatches() && clan.Influence == receipt.Influence && clan.DebtToKingdom == receipt.Debt,
                    () =>
                    {
                        factions.RunWithRulerRepairSuppressed(journal.Parent, () =>
                            CourtPoliticalPositionBehavior.MoveToSuccessorRealm(clan, journal.Parent, journal.Successor,
                                () => KingdomVisualHelper.ApplyJoinToKingdomPreservingCustomBanner(clan, journal.Successor, false)));
                        house.PostMoveInfluence = clan.Influence;
                        house.PostMoveDebt = clan.DebtToKingdom;
                        house.PostMoveBanner = clan.ClanOriginalBanner == null ? null : new Banner(clan.ClanOriginalBanner);
                        house.PostMoveColor = clan.Color;
                        house.PostMoveColor2 = clan.Color2;
                        house.PostMoveBalancesCaptured = true;
                    }, Destination,
                    () => Destination() && house.PostMoveBalancesCaptured
                        && clan.Influence == house.PostMoveInfluence && clan.DebtToKingdom == house.PostMoveDebt
                        && clan.Color == house.PostMoveColor && clan.Color2 == house.PostMoveColor2
                        && (clan.ClanOriginalBanner?.Serialize() ?? "") == (house.PostMoveBanner?.Serialize() ?? ""),
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
                if (!restored) { journal.PendingReason = clan.StringId + ": " + reason; return false; }
                if (batch != null && !TryVerifyCrownBatchWorld(batch, out reason))
                { journal.PendingReason = reason; return false; }
            }
            journal.PendingReason = null;
            reason = null;
            return true;
        }
    }
}
