using System.Linq;
using TaleWorlds.CampaignSystem;

namespace BellumCivile.Behaviors
{
    public sealed partial class CrownAccessionBehavior
    {
        private bool ReconcileRegencyAvailability(CrownAccessionRecord record)
        {
            if (BellumCivileOptions.AllowNewRegencies || record.Emergency || record.Heir == null
                || RegencyPolicy.CanReceiveCrown(record.Heir)) return true;

            Hero formerHeir = record.Heir;
            if (record.Cadet == null && !string.IsNullOrWhiteSpace(record.CadetId))
                record.Cadet = Clan.All.FirstOrDefault(c => c.StringId == record.CadetId);
            if (record.GoldDebited && !record.GoldCredited && !DeliverAbdicationGold(record)) return false;
            var partition = Campaign.Current.GetCampaignBehavior<PartitionSuccessionBehavior>();
            if (record.Cadet != null && partition?.TryRetireUnstaffedMinorCadet(record) != true)
                return false;

            bool committed = HasCommittedHouseholdSettlement(record);
            // A partially delivered inheritance is never charged again or reassigned as
            // a fresh endowment. The remaining political Crown uses the emergency ballot.
            Hero successor = committed ? null : HereditaryRealmSuccession.GetLine(record.Realm,
                record.RegentReplacement ? formerHeir : record.Predecessor,
                new SuccessionLawSet(record.GenderLaw, record.HouseLaw))
                .FirstOrDefault(h => h != formerHeir && h.IsAlive);
            if (!committed) ResetUncommittedCrownHousehold(record, successor?.Clan);
            record.Heir = successor;
            record.RegentReplacement = false;
            record.Emergency = successor == null;
            // Political completion belongs to the replacement, not the abandoned heir.
            // Household and treasury delivery receipts above are never cleared.
            record.TitleTransferred = false;
            record.OutcomeApplied = false;
            record.ElectiveSelected = true;
            record.RequiresRegency = successor != null && successor.Age < SuccessionLawHelper.GetAgeOfMajority();
            BellumCivileLogger.Log($"Crown succession reconciled without new regency; realm={record.Realm.StringId}; former_heir={formerHeir.StringId}; successor={successor?.StringId ?? "election"}; delivery_receipts_retained={committed}.");
            return true;
        }
    }
}
