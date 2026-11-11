using System;
using TaleWorlds.CampaignSystem;

namespace BellumCivile
{
    internal static class VoluntaryPactPayment
    {
        internal static bool CanStart(VoluntaryPactProposalRecord proposal)
        {
            if (proposal == null || proposal.Compensation < 0 || proposal.GoldDebited || proposal.GoldCredited) return false;
            if (proposal.Compensation == 0) return true;
            return proposal.Payer != null && proposal.Payee != null && proposal.Payer != proposal.Payee
                && proposal.Payer.Gold >= proposal.Compensation
                && proposal.Payee.Gold <= int.MaxValue - proposal.Compensation;
        }

        internal static bool TryPay(VoluntaryPactProposalRecord proposal, bool pactActive)
        {
            if (proposal == null || proposal.Phase != VoluntaryPactProposalPhase.Delivery || (!pactActive && !proposal.GoldDebited)
                || string.IsNullOrWhiteSpace(proposal.PactId) || proposal.Compensation < 0
                || (proposal.GoldCredited && !proposal.GoldDebited)) return false;
            if (proposal.Compensation == 0)
            {
                proposal.GoldDebited = proposal.GoldCredited = true;
                return true;
            }

            Hero payer = proposal.Payer, payee = proposal.Payee;
            if (payer == null || payee == null || payer == payee) return false;
            int amount = proposal.Compensation;
            if (!proposal.GoldDebited)
            {
                if (payer.Gold < amount || payee.Gold > int.MaxValue - amount) return false;
                // Like Crown succession's payment journal, each native gold mutation has no campaign callback.
                payer.ChangeHeroGold(-amount);
                proposal.GoldDebited = true;
            }
            if (!proposal.GoldCredited)
            {
                if (payee.Gold > int.MaxValue - amount) return false;
                payee.ChangeHeroGold(amount);
                proposal.GoldCredited = true;
            }
            if (!proposal.TradeEventStarted)
            {
                proposal.TradeEventStarted = true;
                try { PublishTrade(payer, payee, amount); }
                catch (Exception ex)
                {
                    BellumCivileLogger.Log($"Voluntary pact gold event failed after payment; proposal={proposal.Id}; error={ex}");
                }
            }
            return true;
        }

        private static void PublishTrade(Hero payer, Hero payee, int amount)
        {
            CampaignEventDispatcher.Instance.OnHeroOrPartyTradedGold(
                (payer, null), (payee, null), (amount, "bc_voluntary_pact"), false);
        }
    }
}
