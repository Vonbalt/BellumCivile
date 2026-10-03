namespace BellumCivile
{
    internal static class HostagePactLifecycleRules
    {
        internal static HostagePactEndReason Evaluate(bool transferPending, bool realmLost,
            bool houseChanged, bool hostageDied, bool atWar, bool enabled, bool expired)
        {
            // Crown transfers temporarily dismantle realms before restoring their mantle.
            if (transferPending) return HostagePactEndReason.None;
            if (realmLost) return HostagePactEndReason.RealmLost;
            if (houseChanged) return HostagePactEndReason.HouseReplaced;
            if (hostageDied) return HostagePactEndReason.HostageDied;
            if (atWar) return HostagePactEndReason.War;
            if (!enabled) return HostagePactEndReason.RevampDisabled;
            return expired ? HostagePactEndReason.Expired : HostagePactEndReason.None;
        }
    }
}
