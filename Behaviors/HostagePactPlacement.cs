using System;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;

namespace BellumCivile.Behaviors
{
    internal enum VoluntaryPactDeliveryResult { Deferred, Completed, Cancelled }

    public sealed partial class HostagePactBehavior
    {
        // Direct creation uses the standard term; parleys supply their own saved duration.
        internal bool TryEstablish(Kingdom first, Kingdom second, Hero firstHostage, Hero secondHostage)
        {
            int durationDays = BellumCivileOptions.HostagePactMinimumDurationDays;
            return TryEstablishCore(first, second, firstHostage, secondHostage, durationDays, durationDays, false, null, out _);
        }

        // Peacetime negotiations snapshot their baseline and always exchange two hostages.
        internal bool TryEstablishVoluntary(Kingdom first, Kingdom second, Hero firstHostage, Hero secondHostage,
            int durationDays, int baselineDays)
        {
            if (first == null || second == null || first == second || firstHostage == null || secondHostage == null
                || firstHostage == secondHostage
                || firstHostage == Hero.MainHero || secondHostage == Hero.MainHero
                || baselineDays <= 0 || !HostagePactRules.IsNegotiableDuration(durationDays, baselineDays)
                || first.IsAllyWith(second)
                || ClientKingdomBehavior.Instance?.IsClientKingdom(first) == true
                || ClientKingdomBehavior.Instance?.IsClientKingdom(second) == true)
                return false;
            return TryEstablishCore(first, second, firstHostage, secondHostage, durationDays, baselineDays, true, null, out _);
        }

        internal bool TryEstablishVoluntaryForProposal(VoluntaryPactProposalRecord proposal, out HostagePactRecord result)
        {
            result = null;
            if (proposal == null || proposal.Phase != VoluntaryPactProposalPhase.Delivery
                || !proposal.HasValidTerms(true) || !proposal.PaymentPartiesCurrent()) return false;
            HostagePactRecord existing = _pacts.FirstOrDefault(p => p != null && p.VoluntaryProposalId == proposal.Id);
            if (existing != null)
            {
                bool same = existing.VoluntaryPeacetime && existing.FirstRealm == proposal.Source && existing.SecondRealm == proposal.Target
                    && existing.FirstHouse == proposal.SourceHouse && existing.SecondHouse == proposal.TargetHouse
                    && existing.FirstHostage?.Hero == proposal.SourceHostage && existing.SecondHostage?.Hero == proposal.TargetHostage
                    && existing.DurationRecorded && existing.AgreedDurationDays == proposal.DurationDays;
                if (!same || existing.Phase != HostagePactPhase.Active
                    || !HasActiveVoluntaryPact(proposal.Source, proposal.Target)) return false;
                result = existing;
                return true;
            }
            if (proposal.HandoverStarted || _maintaining) return false;
            if (proposal.Source.RulingClan != proposal.SourceHouse || proposal.Target.RulingClan != proposal.TargetHouse
                || proposal.SourceHostage == Hero.MainHero || proposal.TargetHostage == Hero.MainHero
                || proposal.Source.IsAllyWith(proposal.Target)
                || ClientKingdomBehavior.Instance?.IsClientKingdom(proposal.Source) == true
                || ClientKingdomBehavior.Instance?.IsClientKingdom(proposal.Target) == true) return false;
            proposal.HandoverStarted = true;
            return TryEstablishCore(proposal.Source, proposal.Target, proposal.SourceHostage, proposal.TargetHostage,
                proposal.DurationDays, proposal.BaselineDays, true, proposal.Id, out result);
        }

        internal bool TryAbortUnpaidVoluntaryProposal(VoluntaryPactProposalRecord proposal)
        {
            if (_maintaining || proposal == null || proposal.GoldDebited || proposal.GoldCredited
                || string.IsNullOrWhiteSpace(proposal.Id))
                return false;
            var pact = _pacts.FirstOrDefault(p => p != null && p.VoluntaryProposalId == proposal.Id);
            if (pact == null) return true;
            if (!pact.VoluntaryPeacetime || pact.FirstRealm != proposal.Source || pact.SecondRealm != proposal.Target
                || (!string.IsNullOrEmpty(proposal.PactId) && proposal.PactId != pact.Id)) return false;
            if (pact.Phase == HostagePactPhase.Preparing) pact.TryAbortPreparation();
            else if (pact.Phase == HostagePactPhase.Active)
            {
                // A declaration of war has its own hostage consequences; never replace it with a rollback.
                if (pact.FirstRealm == null || pact.SecondRealm == null || pact.FirstRealm.IsAtWarWith(pact.SecondRealm))
                    return false;
                pact.TryBeginResolution(HostagePactEndReason.InvalidCustody);
            }
            MaintainCustody();
            return pact.Phase == HostagePactPhase.Ended;
        }

        internal VoluntaryPactDeliveryResult DeliverVoluntaryProposal(VoluntaryPactProposalRecord proposal)
        {
            if (proposal == null) return VoluntaryPactDeliveryResult.Cancelled;
            if (proposal.Phase == VoluntaryPactProposalPhase.Completed) return VoluntaryPactDeliveryResult.Completed;
            if (proposal.Phase == VoluntaryPactProposalPhase.Cancelled) return VoluntaryPactDeliveryResult.Cancelled;
            if (proposal.Phase != VoluntaryPactProposalPhase.Delivery || _maintaining)
                return VoluntaryPactDeliveryResult.Deferred;

            // Once a debit is committed, its credit must finish even if the pact later ends.
            if (proposal.GoldDebited)
                return VoluntaryPactPayment.TryPay(proposal, false) && proposal.TryComplete()
                    ? VoluntaryPactDeliveryResult.Completed : VoluntaryPactDeliveryResult.Deferred;

            if (!VoluntaryPactPayment.CanStart(proposal)
                || !TryEstablishVoluntaryForProposal(proposal, out var pact))
                return TryAbortUnpaidVoluntaryProposal(proposal) && proposal.TryCancelAfterUnpaidRecovery()
                    ? VoluntaryPactDeliveryResult.Cancelled : VoluntaryPactDeliveryResult.Deferred;

            proposal.PactId = pact.Id;
            if (VoluntaryPactPayment.TryPay(proposal, true) && proposal.TryComplete())
                return VoluntaryPactDeliveryResult.Completed;
            if (proposal.GoldDebited) return VoluntaryPactDeliveryResult.Deferred;
            return TryAbortUnpaidVoluntaryProposal(proposal) && proposal.TryCancelAfterUnpaidRecovery()
                ? VoluntaryPactDeliveryResult.Cancelled : VoluntaryPactDeliveryResult.Deferred;
        }

        private bool TryEstablishCore(Kingdom first, Kingdom second, Hero firstHostage, Hero secondHostage,
            int durationDays, int baselineDays, bool voluntaryPeacetime, string voluntaryProposalId, out HostagePactRecord result)
        {
            result = null;
            if (_maintaining || !BellumCivileOptions.EnableWarPeaceLogicRevamp || first == null || second == null
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
            var firstRecord = Prepare(a, first.RulingClan, second.RulingClan, baselineDays);
            var secondRecord = Prepare(b, second.RulingClan, first.RulingClan, baselineDays);
            if ((a != null && firstRecord == null) || (b != null && secondRecord == null)) return false;
            if (firstRecord != null) firstRecord.NegotiatedCost = HostagePactRules.GetNegotiatedCost(firstRecord.Tier, durationDays, baselineDays);
            if (secondRecord != null) secondRecord.NegotiatedCost = HostagePactRules.GetNegotiatedCost(secondRecord.Tier, durationDays, baselineDays);
            var pact = new HostagePactRecord { Id = Guid.NewGuid().ToString("N"), FirstRealm = first, SecondRealm = second,
                FirstHouse = first.RulingClan, SecondHouse = second.RulingClan,
                AgreedDurationDays = durationDays, DurationRecorded = true,
                VoluntaryPeacetime = voluntaryPeacetime,
                VoluntaryProposalId = voluntaryProposalId,
                FirstHostage = firstRecord, SecondHostage = secondRecord };
            _pacts.Add(pact);
            _maintaining = true;
            try
            {
                if (Place(firstRecord) && Place(secondRecord)
                    && first.RulingClan == pact.FirstHouse && second.RulingClan == pact.SecondHouse
                    && !first.IsAtWarWith(second)
                    && (!voluntaryPeacetime || !first.IsAllyWith(second)
                        && ClientKingdomBehavior.Instance?.IsClientKingdom(first) != true
                        && ClientKingdomBehavior.Instance?.IsClientKingdom(second) != true)
                    && pact.TryActivate(CampaignTime.Now.ToDays))
                {
                    BellumCivileLogger.Log($"Hostage pact activated; pact={pact.Id}; first={first.StringId}; second={second.StringId}; end={pact.EndDay}.");
                    result = pact;
                    return true;
                }
            }
            catch (Exception ex) { BellumCivileLogger.Log($"Hostage handover failed; pact={pact.Id}; {ex}"); }
            finally { _maintaining = false; }
            pact.TryAbortPreparation();
            MaintainCustody();
            return false;
        }

        private static TreatyHostageRecord Prepare(TreatyHostageCandidate candidate, Clan supplier, Clan receiver, int baselineDays)
        {
            if (candidate == null) return null;
            var holding = SelectHolding(receiver);
            return holding == null ? null : new TreatyHostageRecord { Hero = candidate.Hero, SupplyingHouse = supplier,
                ReceivingHouse = receiver, Holding = holding, PreviousHome = candidate.Hero.CurrentSettlement,
                PreviousHeroState = (int)candidate.Hero.HeroState, Tier = candidate.Tier,
                NegotiatedCost = HostagePactRules.GetTreatyCost(candidate.Tier),
                DurationPriced = true, DurationBaselineDays = baselineDays };
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
