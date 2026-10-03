using System;

namespace BellumCivile
{
    internal static class RealmUnionCompletion
    {
        internal static bool Execute(CrownAccessionRecord accession, Func<bool> verify,
            Func<bool> cleanup, Action announce, out string reason)
        {
            var journal = accession?.Union;
            reason = "union completion requires verified native retirement";
            if (journal == null || verify == null || cleanup == null || announce == null) return false;
            if (journal.Completed)
            {
                // Both flags are written together without callbacks. Repair a saved outer
                // flag without repeating cleanup or notifications after guards are released.
                if (!journal.RetirementStarted || !journal.RetirementReturned || !journal.SourceRetired || !journal.Announced) return false;
                accession.Completed = true;
                reason = null;
                return true;
            }
            if (accession.Completed || !journal.RetirementStarted || !journal.RetirementReturned || !journal.SourceRetired) return false;
            try
            {
                if (!verify()) { reason = "retired union state changed before completion"; return false; }
                if (!cleanup()) { reason = "retired court cleanup remains incomplete"; return false; }
                if (!verify()) { reason = "union state changed during court cleanup"; return false; }
                if (!journal.Announced)
                {
                    // Notifications are at-most-once, matching accession messages. They
                    // carry no rewards; an interrupted display must not duplicate benefits.
                    journal.Announced = true;
                    announce();
                }
                if (!verify()) { reason = "union state changed before completion flags could be published"; return false; }
                journal.PendingReason = null;
                journal.Completed = true;
                accession.Completed = true;
                reason = null;
                return true;
            }
            catch (Exception ex)
            { reason = "union completion interrupted: " + ex.Message; return false; }
        }
    }
}
