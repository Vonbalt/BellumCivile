using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;

namespace BellumCivile.Behaviors
{
    public sealed partial class CrownAccessionBehavior
    {
        internal bool IsHouseholdReservedForMarriage(Hero hero) => hero != null
            && _accessions.Any(r => !r.Completed && !r.Emergency
                && (r.Heir == hero || r.Household?.Contains(hero.StringId) == true));

        private static bool HasCommittedHouseholdSettlement(CrownAccessionRecord record) =>
            record.Cadet != null || record.CadetInitialized || record.CadetAnnounced || record.EndowmentSettled
            || record.DeliveredFiefs?.Count > 0 || record.DeliveredTitles?.Count > 0
            || record.DeliveredGold != 0 || record.GoldDebited || record.GoldCredited || record.GoldRecipient != null
            || record.ForeignMovingClan != null || record.ForeignMoveStarted || record.ForeignMoveCompleted
            || record.ForeignInfluenceRestored || record.TitleTransferred || record.OutcomeApplied
            || record.Announced || record.MandateStarted || record.Union != null;

        private bool ReconcileCrownHousehold(CrownAccessionRecord record)
        {
            if (record.Completed || record.Emergency || record.ElectiveElection || record.Union != null) return true;
            Clan original = record.HeirHouse ?? record.IncomingSourceHouse;
            Clan current = record.Heir?.Clan;
            if (original == null || current == null || current == original || current == record.Cadet) return true;
            if (HasCommittedHouseholdSettlement(record))
                return DeferAbdication(record, "the successor changed house after settlement began; existing delivery receipts retained");
            if (record.Heir.IsAlive != true || record.Heir.IsDisabled || current.IsEliminated)
                return true; // Normal accession validation owns unavailable heirs.

            // Only an unexecuted household plan can be rebuilt. The lawful heir,
            // Crown, cause and succession laws remain those of the original journal.
            record.HeirHouse = current;
            record.ForcedPrepared = false;
            record.IncomingHousePrepared = false;
            record.IncomingSourceHouse = null;
            record.IncomingSourceHead = null;
            record.IncomingSourceRealm = null;
            record.CreatesCadet = false;
            record.CadetId = "bc_partition_accession_" + Guid.NewGuid().ToString("N");
            record.CadetName = null;
            record.RequiresRegency = false;
            record.HasLivingEndowment = false;
            record.EndowmentGold = 0;
            record.EndowmentFiefs = new List<string>();
            record.EndowmentTitles = new List<string>();
            record.Household = new List<string>();
            record.HeirPartyCaptured = false;
            record.PreservedHeirParty = null;
            record.AbdicationFailure = null;
            BellumCivileLogger.Log($"Crown household plan refreshed; realm={record.Realm?.StringId}; heir={record.Heir.StringId}; former_house={original.StringId}; current_house={current.StringId}; no_transfers_replayed=true.");
            return true;
        }
    }
}
