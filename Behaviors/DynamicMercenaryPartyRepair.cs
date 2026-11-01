using System;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Library;

namespace BellumCivile.Behaviors
{
    public sealed partial class DynamicMercenaryBandBehavior
    {
        private const int PartyPositionRepairVersion = 1;
        private const float LegacyPartySpawnRadiusSquared = 9f;
        private int _partyPositionRepairVersion;
        private bool _partyPositionRepairPending;

        private void InitializePartyPositionRepair(CampaignGameStarter starter) => _partyPositionRepairVersion = PartyPositionRepairVersion;

        private void QueuePartyPositionRepair() => _partyPositionRepairPending = _partyPositionRepairVersion < PartyPositionRepairVersion;

        private static bool CanRepairPartyPosition(MobileParty party, Clan clan)
        {
            if (party == null || !party.IsActive || party == MobileParty.MainParty || !party.IsLordParty
                || party.ActualClan != clan || clan == Clan.PlayerClan || party.Party == null
                || party.CurrentSettlement != null || party.MapEvent != null || party.MapEventSide != null
                || party.BesiegerCamp != null || party.SiegeEvent != null || party.Army != null || party.AttachedTo != null
                || party.IsCurrentlyUsedByAQuest || party.IsCurrentlyAtSea || !party.Position.IsOnLand
                || party.IsTransitionInProgress || party.IsDisbanding || party.Ai == null || party.Ai.IsDisabled
                || party.Ai.DoNotMakeNewDecisions || party.MemberRoster.TotalManCount == 0
                || party.LeaderHero?.IsAlive != true || party.LeaderHero.Clan != clan
                || party.LeaderHero.PartyBelongedTo != party) return false;
            return DynamicMercenaryBandService.HasCreatedPartyId(party, clan);
        }

        private static bool TryRepairPartyPosition(MobileParty party, Clan clan, Settlement home)
        {
            if (!CanRepairPartyPosition(party, clan) || home?.IsTown != true || home.IsUnderSiege) return false;
            CampaignVec2 oldPosition = party.Position;
            float distance = oldPosition.DistanceSquared(home.GatePosition);
            if (float.IsNaN(distance) || float.IsInfinity(distance) || distance > LegacyPartySpawnRadiusSquared
                || !TownPartyPositionHelper.IsValidLandPoint(home.GatePosition)) return false;
            if (TownPartyPositionHelper.IsValidLandPoint(oldPosition)
                && TownPartyPositionHelper.CanReachLandPoint(oldPosition, home.GatePosition,
                    TownPartyPositionHelper.LongRangePathDistance)) return false;
            if (!TownPartyPositionHelper.TryFindSpawn(home, out var position) || !CanRepairPartyPosition(party, clan)) return false;

            // Use the native setter to update the locator; keep the same party and all of its rosters.
            party.SetPositionAfterMapChange(position);
            party.EventPositionAdder = Vec2.Zero;
            party.SetMoveModeHold();
            party.Ai.RethinkAtNextHourlyTick = true;
            party.Party.SetVisualAsDirty();
            BellumCivileLogger.Log($"Repaired legacy Bellum mercenary party position; company={clan.StringId}; party={party.StringId}; home={home.StringId}; from={oldPosition}; to={position}.");
            return true;
        }

        private void RepairLegacyPartyPositions()
        {
            if (!_partyPositionRepairPending) return;
            // Normal ticks follow native saved-battle restoration. Never rescan this save after this migration.
            _partyPositionRepairPending = false;
            _partyPositionRepairVersion = PartyPositionRepairVersion;
            EnsureCollectionsInitialized();
            int inspected = 0, repaired = 0;
            foreach (var record in _bands.ToList())
            {
                if (record == null) continue;
                try
                {
                    Clan clan = ResolveClan(record.ClanId);
                    Settlement home = ResolveSettlement(record.HomeSettlementId);
                    if (!DynamicMercenaryBandService.IsDynamicBand(clan) || clan.IsEliminated || !clan.IsMinorFaction
                        || clan == Clan.PlayerClan || home?.IsTown != true || home.IsUnderSiege) continue;
                    foreach (var party in clan.WarPartyComponents.Select(c => c?.MobileParty).Where(p => p != null).ToList())
                    {
                        inspected++;
                        try { if (TryRepairPartyPosition(party, clan, home)) repaired++; }
                        catch (Exception ex)
                        {
                            BellumCivileLogger.Log($"Legacy mercenary party position repair skipped; company={clan.StringId}; party={party.StringId}; error={ex.GetType().Name}:{ex.Message}.");
                        }
                    }
                }
                catch (Exception ex)
                {
                    BellumCivileLogger.Log($"Legacy mercenary company position check skipped; company={record.ClanId}; error={ex.GetType().Name}:{ex.Message}.");
                }
            }
            BellumCivileLogger.Log($"Legacy Bellum mercenary position repair completed; inspected={inspected}; repaired={repaired}.");
        }
    }
}
