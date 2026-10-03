using System;

namespace BellumCivile
{
    internal static class RealmUnionRetirement
    {
        internal static bool Execute(RealmUnionRecord journal, Func<bool> beforeVerified,
            Action retire, Func<bool> afterVerified, out string reason)
        {
            reason = "invalid absorption retirement receipts";
            if (journal == null || journal.Completed || beforeVerified == null || retire == null || afterVerified == null
                || journal.RetirementReturned && !journal.RetirementStarted
                || journal.SourceRetired && !journal.RetirementReturned) return false;
            try
            {
                if (!journal.RetirementStarted)
                {
                    if (!beforeVerified()) { reason = "source is not ready for retirement"; return false; }
                    journal.RetirementStarted = true;
                    retire();
                    journal.RetirementReturned = true;
                }
                if (!journal.RetirementReturned)
                { reason = "native retirement was interrupted; destruction will not be replayed"; return false; }
                if (!afterVerified())
                { reason = "retired source or surviving union state does not verify"; return false; }
                journal.SourceRetired = true;
                reason = null;
                return true;
            }
            catch (Exception ex)
            { reason = "absorption retirement interrupted: " + ex.Message; return false; }
        }
    }
}
