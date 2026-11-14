using System;

namespace BellumCivile
{
    internal static class DefensivePactCallExecution
    {
        // An interrupted native action is inspected on the next pass, never issued twice.
        internal static void Step(DefensivePactRecord pact, DefensivePactCallRecord call,
            Func<string, string, bool> isAvailable, Func<string, string, bool> isAtWar,
            Func<string, string, bool> canDeclare, Action<string, string> declare)
        {
            if (pact == null || !pact.Activated || call == null || call.Completed || isAvailable == null
                || isAtWar == null || canDeclare == null || declare == null) return;
            try
            {
                if (!isAvailable(pact.TargetId, call.RealmId))
                {
                    if (call.Attempted) call.TryFinish(DefensivePactCallOutcome.Unavailable);
                    else call.TrySkip();
                    return;
                }
                if (isAtWar(pact.TargetId, call.RealmId)) { call.TryAlreadyAtWar(); return; }
                if (call.Attempted) { call.TryFinish(DefensivePactCallOutcome.DeclarationFailed); return; }
                if (!canDeclare(pact.TargetId, call.RealmId)) { call.TrySkip(); return; }
                if (!call.TryStart()) return;
                declare(pact.TargetId, call.RealmId);
                call.TryFinish(isAtWar(pact.TargetId, call.RealmId)
                    ? DefensivePactCallOutcome.WarConfirmed : DefensivePactCallOutcome.DeclarationFailed);
            }
            catch (Exception ex)
            {
                BellumCivileLogger.Log($"Defensive pact call interrupted; target={pact.TargetId}; member={call.RealmId}; error={ex}");
            }
        }
    }
}
