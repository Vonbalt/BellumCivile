using System;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;

namespace BellumCivile.Behaviors
{
    public sealed partial class HostagePactBehavior
    {
        // Called only after peace is concluded. Treaty UI integration remains gated.
        internal bool TryEstablish(Kingdom first, Kingdom second, Hero firstHostage, Hero secondHostage)
        {
            int durationDays = BellumCivileOptions.HostagePactDurationDays;
            if (_maintaining || durationDays == 0 || !BellumCivileOptions.EnableWarPeaceLogicRevamp || first == null || second == null
                || first == second || first.IsEliminated || second.IsEliminated || first.IsAtWarWith(second)
                || BellumKingdomVisibilityHelper.IsTemporaryBellumKingdom(first)
                || BellumKingdomVisibilityHelper.IsTemporaryBellumKingdom(second)
                || CivilWarConflictBehavior.IsRealmTransferPending(first) || CivilWarConflictBehavior.IsRealmTransferPending(second)
                || first.RulingClan == null || second.RulingClan == null
                || (firstHostage == null && secondHostage == null)
                || _pacts.Any(p => p != null && p.Phase != HostagePactPhase.Ended
                    && ((p.FirstRealm == first && p.SecondRealm == second) || (p.FirstRealm == second && p.SecondRealm == first))))
                return false;
            var a = firstHostage == null ? null : TreatyHostageEligibility.GetCandidates(first).FirstOrDefault(c => c.Hero == firstHostage);
            var b = secondHostage == null ? null : TreatyHostageEligibility.GetCandidates(second).FirstOrDefault(c => c.Hero == secondHostage);
            if ((firstHostage != null && a == null) || (secondHostage != null && b == null)) return false;
            var firstRecord = Prepare(a, first.RulingClan, second.RulingClan);
            var secondRecord = Prepare(b, second.RulingClan, first.RulingClan);
            if ((a != null && firstRecord == null) || (b != null && secondRecord == null)) return false;
            var pact = new HostagePactRecord { Id = Guid.NewGuid().ToString("N"), FirstRealm = first, SecondRealm = second,
                FirstHouse = first.RulingClan, SecondHouse = second.RulingClan,
                AgreedDurationDays = durationDays, DurationRecorded = true,
                FirstHostage = firstRecord, SecondHostage = secondRecord };
            _pacts.Add(pact);
            _maintaining = true;
            try
            {
                if (Place(firstRecord) && Place(secondRecord)
                    && first.RulingClan == pact.FirstHouse && second.RulingClan == pact.SecondHouse
                    && !first.IsAtWarWith(second) && pact.TryActivate(CampaignTime.Now.ToDays))
                {
                    BellumCivileLogger.Log($"Hostage pact activated; pact={pact.Id}; first={first.StringId}; second={second.StringId}; end={pact.EndDay}.");
                    return true;
                }
            }
            catch (Exception ex) { BellumCivileLogger.Log($"Hostage handover failed; pact={pact.Id}; {ex}"); }
            finally { _maintaining = false; }
            pact.TryAbortPreparation();
            MaintainCustody();
            return false;
        }

        private static TreatyHostageRecord Prepare(TreatyHostageCandidate candidate, Clan supplier, Clan receiver)
        {
            if (candidate == null) return null;
            var holding = SelectHolding(receiver);
            return holding == null ? null : new TreatyHostageRecord { Hero = candidate.Hero, SupplyingHouse = supplier,
                ReceivingHouse = receiver, Holding = holding, PreviousHome = candidate.Hero.CurrentSettlement,
                PreviousHeroState = (int)candidate.Hero.HeroState, Tier = candidate.Tier, NegotiatedCost = candidate.Cost };
        }

        private static bool Place(TreatyHostageRecord record)
        {
            if (record == null) return true;
            var ruler = RegencyBehavior.Instance?.GetLegalClanHead(record.SupplyingHouse) ?? record.SupplyingHouse.Leader;
            if (!TreatyHostageEligibility.IsAvailableRelative(record.Hero, ruler, record.SupplyingHouse)
                || record.Holding.OwnerClan != record.ReceivingHouse || record.Holding.IsUnderSiege) return false;
            // Reserve protection before native capture callbacks (score, ransom, war-will shocks).
            record.CustodyEstablished = true;
            record.CaptivityStartDay = CampaignTime.Now.ToDays;
            TakePrisonerAction.Apply(record.Holding.Party, record.Hero);
            return record.Hero.IsPrisoner && record.Hero.PartyBelongedToAsPrisoner == record.Holding.Party;
        }
    }
}
