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
                ?? BellumCivileOptions.HostagePactDurationDays;

        internal static IReadOnlyList<TreatyHostageCandidate> Available(Kingdom supplier, Kingdom receiver, int? durationDays = null)
        {
            int duration = durationDays ?? BellumCivileOptions.HostagePactDurationDays;
            if (duration <= 0 || !HostagePactRules.IsValidDuration(duration)
                || !BellumCivileOptions.EnableWarPeaceLogicRevamp || supplier == null || receiver == null || supplier == receiver
                || supplier.IsEliminated || receiver.IsEliminated || receiver.RulingClan == null
                || BellumKingdomVisibilityHelper.IsTemporaryBellumKingdom(receiver)
                || CivilWarConflictBehavior.IsRealmTransferPending(receiver)
                || HostagePactBehavior.SelectHolding(receiver.RulingClan) == null) return new List<TreatyHostageCandidate>();
            return TreatyHostageEligibility.GetCandidates(supplier);
        }
        internal static TreatyTermRecord Create(Kingdom supplier, Kingdom receiver, TreatyHostageCandidate candidate, bool offering,
            int? durationDays = null)
            => new TreatyTermRecord(TreatyTermType.HostagePeace, candidate.Cost,
                durationDays: durationDays ?? BellumCivileOptions.HostagePactDurationDays, fromKingdomId: supplier.StringId, toKingdomId: receiver.StringId,
                heroId: candidate.Hero.StringId, clanId: supplier.RulingClan.StringId, wasVoluntaryOffering: offering,
                hostageTier: candidate.Tier, hostageReceivingClanId: receiver.RulingClan.StringId);

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
                    || term.WarScoreCost != HostagePactRules.GetTreatyCost(term.HostageTier))
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
            if (candidate.Tier != term.HostageTier || candidate.Cost != term.WarScoreCost)
            { reason = "the hostage succession rank has changed; select the relative again"; return false; }
            if (HostagePactBehavior.SelectHolding(receiver.RulingClan) == null)
            { reason = "the receiving ruling house has no town or castle free of siege for the hostage"; return false; }
            return true;
        }
    }
}
