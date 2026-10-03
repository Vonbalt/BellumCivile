using System;

namespace BellumCivile
{
    internal static class CrownPartitionWarSetup
    {
        internal static bool TryDeclare(CrownPartitionPromotionRecord journal, string enemyId,
            Func<bool> atWar, Action declare, out string reason)
        {
            reason = null;
            if (journal?.InheritedEnemyIds == null || string.IsNullOrWhiteSpace(enemyId)
                || !journal.InheritedEnemyIds.Contains(enemyId) || journal.WarDeclarationsStarted == null
                || journal.WarDeclarationsReturned == null || atWar == null || declare == null
                || journal.WarDeclarationsReturned.Contains(enemyId) && !journal.WarDeclarationsStarted.Contains(enemyId))
            { reason = "invalid inherited war declaration context"; return false; }
            if (journal.WarDeclarationsStarted.Contains(enemyId) && !journal.WarDeclarationsReturned.Contains(enemyId))
            { reason = "inherited war declaration was interrupted; it will not be repeated"; return false; }
            try
            {
                if (!journal.WarDeclarationsStarted.Contains(enemyId))
                {
                    if (atWar()) { reason = "successor entered an unjournaled war before setup"; return false; }
                    journal.WarDeclarationsStarted.Add(enemyId);
                    declare();
                    journal.WarDeclarationsReturned.Add(enemyId);
                }
                if (!atWar()) { reason = "inherited enemy stance no longer verifies"; return false; }
                return true;
            }
            catch (Exception ex)
            { reason = "inherited war declaration interrupted: " + ex.Message; return false; }
        }
    }
}
