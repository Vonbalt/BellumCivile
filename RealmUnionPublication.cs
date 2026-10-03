using System;

namespace BellumCivile
{
    internal static class RealmUnionPublication
    {
        internal static bool TryPublish(CrownAccessionRecord accession, RealmUnionRecord draft,
            Func<bool> preflight, Func<RealmUnionRecord, bool> started, out string reason)
        {
            reason = "union publication requires an unjournaled accession and an unstarted draft";
            if (accession == null || accession.Completed || accession.Union != null || draft == null
                || preflight == null || started == null || draft.Completed || started(draft)) return false;
            // Existing validators require journal identity on the registered accession.
            // No native transfers may occur in this bounded preflight scope.
            accession.Union = draft;
            bool accepted = false;
            try
            {
                accepted = preflight() && accession.Union == draft && !started(draft);
                if (!accepted) { reason = "union diplomatic preflight did not complete"; return false; }
                draft.PendingReason = null;
                reason = null;
                return true;
            }
            catch (Exception ex)
            { reason = "union publication interrupted: " + ex.Message; return false; }
            finally
            {
                // Never erase evidence of a native write or overwrite a replacement journal.
                if (!accepted && accession.Union == draft && !started(draft)) accession.Union = null;
            }
        }
    }
}
