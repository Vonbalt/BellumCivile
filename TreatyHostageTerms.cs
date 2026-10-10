using System.Collections.Generic;
using System.Linq;
using BellumCivile.Behaviors;
using TaleWorlds.CampaignSystem;

namespace BellumCivile
{
    // Shared clause validation for player and NPC drafts.
    internal static class TreatyHostageTerms
    {
        internal static TaleWorlds.CampaignSystem.Settlements.Settlement SettlementHolding(Clan receiver,
            WarScoreRecord war, IEnumerable<TreatyTermRecord> terms)
        {
            if (receiver?.Kingdom == null || war == null) return null;
            var territory = new ClientWarTerritory(war);
            return HostagePactBehavior.SelectHolding(receiver, holding =>
            {
                var snapshot = war.GetSnapshot(holding.StringId);
                return HoldingSurvivesSettlement(territory.Opposing(snapshot?.OwnerKingdomId, receiver.Kingdom.StringId),
                    receiver.Kingdom.StringId, receiver.StringId, holding.StringId, terms);
            });
        }
        // SelectHolding already verifies current ownership. Peace restores enemy
        // occupations, not confiscations or grants between houses of the same side.
        internal static bool HoldingSurvivesSettlement(bool occupationWouldReturn, string realm,
            string house, string holding, IEnumerable<TreatyTermRecord> terms)
        {
            var transfers = (terms ?? Enumerable.Empty<TreatyTermRecord>()).Where(t => t != null
                && ClientWarTerritory.IsTerritorial(t.Type) && t.SettlementId == holding).ToList();
            if (transfers.Count > 1) return false;
            var transfer = transfers.FirstOrDefault();
            if (transfer == null) return !occupationWouldReturn;
            return transfer.Type == TreatyTermType.TransferFief ? transfer.ToKingdomId == realm
                : transfer.ThirdKingdomId == realm && transfer.ClanId == house;
        }
        internal static int DurationForDraft(IEnumerable<TreatyTermRecord> terms)
            => terms?.FirstOrDefault(t => t?.Type == TreatyTermType.HostagePeace)?.DurationDays
                ?? HostagePactRules.DefaultDurationDays;

        internal static IReadOnlyList<TreatyHostageCandidate> Available(Kingdom supplier, Kingdom receiver, int? durationDays = null)
            => Available(supplier, receiver, out _, durationDays);

        internal static IReadOnlyList<TreatyHostageCandidate> Available(Kingdom supplier, Kingdom receiver, out string reason, int? durationDays = null)
        {
            reason = null;
            int duration = durationDays ?? HostagePactRules.DefaultDurationDays;
            if (duration <= 0 || !HostagePactRules.IsValidDuration(duration))
            { reason = "hostage selection requires a valid positive pact duration"; return new List<TreatyHostageCandidate>(); }
            if (!BellumCivileOptions.EnableWarPeaceLogicRevamp || supplier == null || receiver == null || supplier == receiver
                || supplier.IsEliminated || receiver.IsEliminated || supplier.RulingClan == null || receiver.RulingClan == null
                || BellumKingdomVisibilityHelper.IsTemporaryBellumKingdom(supplier)
                || BellumKingdomVisibilityHelper.IsTemporaryBellumKingdom(receiver)
                || CivilWarConflictBehavior.IsRealmTransferPending(supplier) || CivilWarConflictBehavior.IsRealmTransferPending(receiver))
            { reason = "hostage peace requires two established sovereign realms"; return new List<TreatyHostageCandidate>(); }
            if (HostagePactBehavior.SelectHolding(receiver.RulingClan) == null)
            { reason = "the receiving ruling house has no town or castle free of siege for the hostage"; return new List<TreatyHostageCandidate>(); }
            var candidates = TreatyHostageEligibility.GetCandidates(supplier);
            if (candidates.Count == 0) reason = "the supplying ruler has no eligible relative available as a hostage";
            return candidates;
        }
        internal static TreatyTermRecord Create(Kingdom supplier, Kingdom receiver, TreatyHostageCandidate candidate, bool offering,
            int? durationDays = null, bool durationPriced = true)
            => new TreatyTermRecord(TreatyTermType.HostagePeace,
                durationPriced ? HostagePactRules.GetNegotiatedCost(candidate.Tier, durationDays ?? HostagePactRules.DefaultDurationDays)
                    : candidate.Cost,
                durationDays: durationDays ?? HostagePactRules.DefaultDurationDays, fromKingdomId: supplier.StringId, toKingdomId: receiver.StringId,
                heroId: candidate.Hero.StringId, clanId: supplier.RulingClan.StringId, wasVoluntaryOffering: offering,
                hostageTier: candidate.Tier, hostageReceivingClanId: receiver.RulingClan.StringId, hostageDurationPriced: durationPriced);

        internal static List<TreatyTermRecord> WithDuration(IEnumerable<TreatyTermRecord> terms, int days)
            => terms.Select(t => t.Type != TreatyTermType.HostagePeace ? t : new TreatyTermRecord(
                t.Type, HostagePactRules.GetNegotiatedCost(t.HostageTier, days), durationDays: days,
                fromKingdomId: t.FromKingdomId, toKingdomId: t.ToKingdomId, heroId: t.HeroId, clanId: t.ClanId,
                wasVoluntaryOffering: t.WasVoluntaryOffering, hostageTier: t.HostageTier,
                hostageReceivingClanId: t.HostageReceivingClanId, hostageDurationPriced: true)).ToList();

        internal static bool ValidShape(IEnumerable<TreatyTermRecord> terms, string winner, string loser, out string reason)
        {
            reason = null;
            var all = (terms ?? Enumerable.Empty<TreatyTermRecord>()).Where(t => t != null).ToList();
            var hostages = all.Where(t => t.Type == TreatyTermType.HostagePeace).ToList();
            if (hostages.Count == 0) return true;
            if (string.IsNullOrEmpty(winner) || string.IsNullOrEmpty(loser) || winner == loser
                || hostages.Count > 2 || hostages.Select(t => t.FromKingdomId).Distinct().Count() != hostages.Count
                || hostages.Select(t => t.HeroId).Distinct().Count() != hostages.Count)
            { reason = "a peace pact permits only one hostage from each realm"; return false; }
            foreach (var term in hostages)
            {
                bool demand = term.FromKingdomId == loser && term.ToKingdomId == winner;
                bool offer = term.FromKingdomId == winner && term.ToKingdomId == loser && term.WasVoluntaryOffering;
                if ((!demand && !offer) || string.IsNullOrEmpty(term.HeroId) || string.IsNullOrEmpty(term.ClanId)
                    || string.IsNullOrEmpty(term.HostageReceivingClanId) || term.ClanId == term.HostageReceivingClanId
                    || term.HostageTier < 1 || term.HostageTier > 4 || !HostagePactRules.IsValidDuration(term.DurationDays)
                    || term.DurationDays != hostages[0].DurationDays
                    || !HostagePactRules.IsValidPrice(term.HostageTier, term.DurationDays, term.WarScoreCost, term.HostageDurationPriced))
                { reason = "the draft contains an invalid hostage pledge"; return false; }
                if (all.Any(t => t.Type == TreatyTermType.ArrangeRoyalMarriage
                    && (t.HeroId == term.HeroId || t.SecondaryHeroId == term.HeroId)))
                { reason = "the same relative cannot be promised in marriage and as a hostage"; return false; }
            }
            if (all.Any(t => t.Type == TreatyTermType.WhitePeace || t.Type == TreatyTermType.ForceVassalization
                || t.Type == TreatyTermType.EnforceRebelDemands))
            { reason = "hostage peace cannot accompany white peace, annexation, or enforced rebel demands"; return false; }
            return true;
        }

        internal static bool StillDeliverable(TreatyTermRecord term, Kingdom supplier, Kingdom receiver)
            => CheckDeliverable(term, supplier, receiver, out _);

        internal static bool CheckDeliverable(TreatyTermRecord term, Kingdom supplier, Kingdom receiver, out string reason)
        {
            reason = null;
            if (term?.Type != TreatyTermType.HostagePeace || supplier == null || receiver == null || supplier == receiver
                || !BellumCivileOptions.EnableWarPeaceLogicRevamp
                || BellumKingdomVisibilityHelper.IsTemporaryBellumKingdom(supplier)
                || BellumKingdomVisibilityHelper.IsTemporaryBellumKingdom(receiver)
                || CivilWarConflictBehavior.IsRealmTransferPending(supplier) || CivilWarConflictBehavior.IsRealmTransferPending(receiver)
                || supplier.IsEliminated || receiver.IsEliminated || supplier.StringId != term.FromKingdomId
                || receiver.StringId != term.ToKingdomId)
            { reason = "hostage peace requires two established sovereign realms"; return false; }
            if (supplier.RulingClan?.StringId != term.ClanId || receiver.RulingClan?.StringId != term.HostageReceivingClanId)
            { reason = "a ruling house has changed since the hostage was selected"; return false; }
            var candidate = TreatyHostageEligibility.GetCandidates(supplier).FirstOrDefault(c => c.Hero.StringId == term.HeroId);
            if (candidate == null)
            { reason = "the selected relative is no longer available as a hostage"; return false; }
            if (candidate.Tier != term.HostageTier
                || !HostagePactRules.IsValidPrice(term.HostageTier, term.DurationDays, term.WarScoreCost, term.HostageDurationPriced))
            { reason = "the hostage succession rank has changed; select the relative again"; return false; }
            if (HostagePactBehavior.SelectHolding(receiver.RulingClan) == null)
            { reason = "the receiving ruling house has no town or castle free of siege for the hostage"; return false; }
            return true;
        }
    }
}
