using System;

namespace BellumCivile
{
    internal enum HostageDeliveryResult { Rejected, Applied, RecoveryRequired }

    // Never retry irreversible treaty concessions after their saved start receipt.
    internal static class HostageTreatyDelivery
    {
        internal static HostageDeliveryResult Run(HostagePactRecord pact, Func<bool> validate,
            Func<bool> place, Func<bool> settle, Func<bool> peaceful, Func<bool> activate, Action rollback)
        {
            if (pact == null || string.IsNullOrEmpty(pact.TreatyProposalId))
                return HostageDeliveryResult.Rejected;
            if (pact.Phase == HostagePactPhase.Active) return HostageDeliveryResult.Applied;
            if (pact.Phase != HostagePactPhase.Preparing) return HostageDeliveryResult.Rejected;
            if (pact.TreatySettlementStarted)
                return Recover(pact, peaceful, activate);
            bool rollbackAttempted = false;
            void RollBackOnce()
            {
                if (rollbackAttempted) return;
                rollbackAttempted = true;
                rollback();
            }
            try
            {
                if (!validate() || !place())
                {
                    RollBackOnce();
                    return HostageDeliveryResult.Rejected;
                }
                pact.TreatySettlementStarted = true;
                if (!settle()) return HostageDeliveryResult.RecoveryRequired;
                pact.TreatySettlementCompleted = true;
                return Recover(pact, peaceful, activate);
            }
            catch
            {
                if (!pact.TreatySettlementStarted) RollBackOnce();
                // After the boundary, custody must not be released as though no bargain existed.
                // The caller logs and recovers; gold, land and political actions are not replayed.
                throw;
            }
        }

        private static HostageDeliveryResult Recover(HostagePactRecord pact, Func<bool> peaceful, Func<bool> activate)
        {
            if (!pact.TreatySettlementCompleted || !peaceful())
                return HostageDeliveryResult.RecoveryRequired;
            return activate() ? HostageDeliveryResult.Applied : HostageDeliveryResult.RecoveryRequired;
        }
    }
}
