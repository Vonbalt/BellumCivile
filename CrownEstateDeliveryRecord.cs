using System;
using TaleWorlds.SaveSystem;

namespace BellumCivile
{
    public sealed class CrownEstateDeliveryRecord
    {
        [SaveableField(1)] public string AssetId;
        [SaveableField(2)] public bool IsTitle;
        [SaveableField(3)] public bool Started;
        [SaveableField(4)] public bool ActionReturned;
        [SaveableField(5)] public bool Verified;

        internal bool TryDeliver(Func<bool> originalState, Action deliver, Func<bool> deliveredState, out string reason)
        {
            reason = null;
            if (string.IsNullOrWhiteSpace(AssetId) || originalState == null || deliver == null || deliveredState == null
                || ActionReturned && !Started || Verified && !ActionReturned)
            { reason = "invalid estate delivery receipt"; return false; }
            if (Started && !ActionReturned)
            { reason = "native estate action was interrupted; it will not be repeated"; return false; }
            try
            {
                if (!Started)
                {
                    if (!originalState()) { reason = "estate ownership changed before delivery"; return false; }
                    Started = true;
                    deliver();
                    ActionReturned = true;
                }
                if (!deliveredState()) { reason = "delivered estate state does not match its receipt"; return false; }
                Verified = true;
                return true;
            }
            catch (Exception ex)
            {
                reason = "estate delivery interrupted: " + ex.Message;
                return false;
            }
        }
    }
}
