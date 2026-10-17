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

        private static void RetainExistingCrownChildren(CrownAccessionRecord record)
        {
            if (record.Heir == null || record.Household == null || !record.CreatesCadet
                || record.Completed || record.EndowmentSettled || record.ChallengeConcession) return;

            // Older Crown plans may include children. Prune only their pending moves;
            // members already in the recorded cadet remain part of its saved household.
            int retained = record.Household.RemoveAll(id =>
            {
                Hero member = ResolveAbdicationHero(id);
                if (member == null || member == record.Heir
                    || (member.Mother != record.Heir && member.Father != record.Heir)) return false;
                Clan house = member.Clan;
                bool alreadyTransferred = record.Cadet != null ? house == record.Cadet
                    : house != null && !string.IsNullOrWhiteSpace(record.CadetId)
                        && house.StringId == record.CadetId;
                return !alreadyTransferred;
            });
            if (retained > 0)
                BellumCivileLogger.Log($"Crown household retained existing children; realm={record.Realm?.StringId}; heir={record.Heir.StringId}; retained={retained}.");
        }

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
            ResetUncommittedCrownHousehold(record, current);
            BellumCivileLogger.Log($"Crown household plan refreshed; realm={record.Realm?.StringId}; heir={record.Heir.StringId}; former_house={original.StringId}; current_house={current.StringId}; no_transfers_replayed=true.");
            return true;
        }

        private static void ResetUncommittedCrownHousehold(CrownAccessionRecord record, Clan current)
        {
            record.HeirHouse = current;
            record.ForcedPrepared = false;
            record.IncomingHousePrepared = false;
            record.IncomingSourceHouse = null;
            record.IncomingSourceHead = null;
            record.IncomingSourceRealm = null;
            record.CreatesCadet = false;
            record.CadetId = "bc_partition_accession_" + Guid.NewGuid().ToString("N");
            record.CadetName = null;
            record.CadetNaming = null;
            record.CadetNamingSettlementId = null;
            record.RequiresRegency = false;
            record.HasLivingEndowment = false;
            record.EndowmentGold = 0;
            record.EndowmentFiefs = new List<string>();
            record.EndowmentTitles = new List<string>();
            record.Household = new List<string>();
            record.HeirPartyCaptured = false;
            record.PreservedHeirParty = null;
            record.AbdicationFailure = null;
        }
    }
}
