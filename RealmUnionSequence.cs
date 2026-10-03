using System;

namespace BellumCivile
{
    internal static class RealmUnionSequence
    {
        internal static bool Execute(CrownAccessionRecord accession, Func<bool> capture,
            Func<bool> agreements, Func<bool> crown, Func<bool> clans, Func<bool> retire,
            Func<bool> finish, out string reason)
        {
            reason = "absorption sequence requires a journal and all stage handlers";
            var journal = accession?.Union;
            if (journal == null || capture == null || agreements == null || crown == null
                || clans == null || retire == null || finish == null) return false;
            try
            {
                if (journal.Completed)
                {
                    reason = "completed absorption needs outer-record reconciliation";
                    if (!finish()) return false;
                    reason = null;
                    return true;
                }
                if (accession.Completed) return false;
                if (journal.SourceRetired && (!journal.RetirementStarted || !journal.RetirementReturned)) return false;
                if (!journal.RetirementStarted)
                {
                    if (journal.SourceObligations == null || journal.DestinationObligations == null)
                    {
                        reason = "absorption obligation capture is pending";
                        if (!capture()) return false;
                    }
                    reason = "absorption agreement settlement is pending";
                    if (!agreements()) return false;
                    reason = "absorption Crown transfer is pending";
                    if (!crown()) return false;
                    reason = "absorption clan delivery is pending";
                    if (!clans()) return false;
                }
                reason = "absorption retirement verification is pending";
                if (!retire()) return false;
                reason = "absorption completion is pending";
                if (!finish()) return false;
                reason = null;
                return true;
            }
            catch (Exception ex)
            { reason = "absorption sequence interrupted: " + ex.Message; return false; }
        }
    }
}
