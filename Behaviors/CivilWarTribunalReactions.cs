using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;

namespace BellumCivile.Behaviors
{
    public partial class CivilWarResolutionBehavior
    {
        private List<CourtTribunalReactionRecord> _tribunalReactions = new List<CourtTribunalReactionRecord>();
        private Hero _activeTribunalExecution;

        internal bool OwnsExecutionReaction(Hero victim) => victim != null && victim == _activeTribunalExecution;

        private CourtTribunalReactionRecord BeginTribunalReactions(string id, Kingdom realm, IEnumerable<Clan> clans)
        {
            var record = _tribunalReactions.FirstOrDefault(r => r.Id == id);
            if (record != null) return record;
            record = new CourtTribunalReactionRecord { Id = id, Realm = realm };
            var manager = Campaign.Current.GetCampaignBehavior<FactionManagerBehavior>();
            var factions = manager?.GetFactionsInKingdom(realm).Where(f => f.IsIdeology).ToList();
            foreach (var clan in clans.Where(c => c != null).Distinct())
            {
                // A temporary rebel sovereign can still belong to the original court.
                var faction = factions?.FirstOrDefault(f => f.Members.Contains(clan))
                    ?? manager?.GetIdeologicalFaction(clan);
                record.Members.Add(new CourtTribunalMemberReaction
                {
                    Clan = clan,
                    Bloc = faction?.IsIdeology == true ? (int)faction.Type : -1
                });
            }
            _tribunalReactions.Add(record);
            return record;
        }

        private void RecordTribunalReaction(string id, Clan clan, int amount, bool pardon = false)
        {
            var record = _tribunalReactions.FirstOrDefault(r => r.Id == id);
            var member = record?.Members.FirstOrDefault(m => m.Clan == clan);
            if (member != null && string.IsNullOrEmpty(member.ExecutionId))
                record.Settle(member, amount, pardon);
        }

        private void CloseTribunalReactions(string id, bool apply = true)
        {
            var record = _tribunalReactions.FirstOrDefault(r => r.Id == id);
            if (record == null) return;
            if (!apply) { _tribunalReactions.Remove(record); return; }
            record.Closed = true;
            FlushTribunalReactions();
        }

        private bool HasTribunalExecutionReaction(string executionId) => _tribunalReactions
            .Any(r => r.Members.Any(m => m.ExecutionId == executionId));

        private void LinkTribunalExecution(string groupId, Clan clan, string executionId)
        {
            var member = _tribunalReactions.FirstOrDefault(r => r.Id == groupId)?.Members.FirstOrDefault(m => m.Clan == clan);
            if (member != null && !member.Settled) member.ExecutionId = executionId;
        }

        private void CompleteTribunalExecution(string executionId, bool executed, bool exiled = false)
        {
            foreach (var record in _tribunalReactions)
                foreach (var member in record.Members.Where(m => m.ExecutionId == executionId).ToList())
                    record.Settle(member, executed ? -30 : exiled ? -10 : 0);
            FlushTribunalReactions();
        }

        private void FlushTribunalReactions()
        {
            if (_tribunalReactions.Count == 0) return;
            var shocks = Campaign.Current.GetCampaignBehavior<IdeologyEventShockBehavior>();
            if (shocks == null) return;
            foreach (var record in _tribunalReactions.Where(r => r.Ready).ToList())
            {
                record.Applied = true;
                if (record.Realm?.IsEliminated == false)
                {
                    foreach (var bloc in new[] { FactionType.Nobility, FactionType.Glory, FactionType.Liberty })
                    {
                        int memberTotal = record.MemberTotal(bloc);
                        // Clemency is a separate realm-wide reaction, outside the member cap.
                        int total = memberTotal + (bloc == FactionType.Liberty && record.Clemency ? 30 : 0);
                        if (total != 0) shocks.RecordTribunalAftermath(record.Realm, bloc, total);
                        BellumCivileLogger.Log($"Tribunal reaction; group={record.Id}; kingdom={record.Realm.StringId}; bloc={bloc}; members={memberTotal}; clemency={record.Clemency}; total={total}.");
                    }
                }
                _tribunalReactions.Remove(record);
            }
        }
    }
}
