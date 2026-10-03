using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;

namespace BellumCivile.Behaviors
{
    // Observation does not start conflicts. Recovery resumes only explicitly saved transfers.
    public sealed partial class CivilWarConflictBehavior : CampaignBehaviorBase
    {
        private List<CivilWarConflictRecord> _conflicts = new List<CivilWarConflictRecord>();
        public static CivilWarConflictBehavior Instance => Campaign.Current?.GetCampaignBehavior<CivilWarConflictBehavior>();

        public override void RegisterEvents()
        {
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, _ => ObserveExistingWars());
            CampaignEvents.HourlyTickEvent.AddNonSerializedListener(this, ResumePendingCrownTransfers);
            CampaignEvents.HourlyTickEvent.AddNonSerializedListener(this, ResumePendingRivalPromotions);
        }
        public override void SyncData(IDataStore store)
        {
            store.SyncData("BC_CivilWarConflicts", ref _conflicts);
            store.SyncData("BC_CivilWarCrownTransfers", ref _transfers);
            store.SyncData("BC_CivilWarRivalPromotions", ref _promotions);
            _conflicts = _conflicts ?? new List<CivilWarConflictRecord>();
            _transfers = _transfers ?? new List<CivilWarCrownTransferRecord>();
            _promotions = _promotions ?? new List<CivilWarRivalPromotionRecord>();
        }

        internal void Observe(FactionObject faction, Kingdom rebel, WarScoreRecord score = null)
        {
            if (faction == null || faction.IsIdeology || faction.ParentKingdom == null || rebel == null
                || rebel == faction.ParentKingdom || rebel.IsEliminated) return;
            var existing = _conflicts.FirstOrDefault(c => c.Sides.Any(s => s.Faction == faction));
            if (existing != null) { existing.Observe(faction, rebel, score); return; }
            var crown = Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>()?.GetRealmSovereignTitle(faction.ParentKingdom);
            if (crown == null) return;
            var record = _conflicts.FirstOrDefault(c => !c.Closed && c.SovereignTitleId == crown.TitleId && c.CrownRealm == faction.ParentKingdom);
            if (record == null)
            {
                record = new CivilWarConflictRecord { Id = Guid.NewGuid().ToString("N"), SovereignTitleId = crown.TitleId,
                    OriginalRealm = faction.ParentKingdom, CrownRealm = faction.ParentKingdom };
                _conflicts.Add(record);
            }
            record.Observe(faction, rebel, score);
        }

        internal void Complete(FactionObject faction)
        {
            if (IsFactionTransferPending(faction)) return;
            var conflict = _conflicts.FirstOrDefault(c => c.Sides.Any(s => s.Faction == faction));
            var side = conflict?.Sides.FirstOrDefault(s => s.Faction == faction);
            if (side == null) return;
            foreach (var pair in conflict.Pairs.Where(p => !p.Closed && p.IsRivalry
                && (p.AttackerSideId == side.Id || p.DefenderSideId == side.Id)))
                Campaign.Current.GetCampaignBehavior<WarScoreBehavior>()?.CompleteCivilWarRivalry(pair.Score, "claimant coalition settled");
            conflict.Complete(faction);
        }

        internal CivilWarConflictRecord GetConflict(FactionObject faction) =>
            _conflicts.FirstOrDefault(c => !c.Closed && c.Sides.Any(s => s.Faction == faction && !s.Closed));

        // Reward delivery happens after Complete, so retain access to this exact conflict's history.
        internal CivilWarConflictRecord GetRewardHistory(FactionObject faction) => faction == null ? null
            : _conflicts.FirstOrDefault(c => c.Sides.Any(s => s.Faction == faction));

        private void ObserveExistingWars()
        {
            var manager = Campaign.Current.GetCampaignBehavior<FactionManagerBehavior>();
            if (manager == null) return;
            var scores = Campaign.Current.GetCampaignBehavior<WarScoreBehavior>();
            foreach (var faction in Kingdom.All.SelectMany(manager.GetFactionsInKingdom)
                .Where(f => !f.IsIdeology && !f.IsChallengeStartupPending && f.HasTrackedRebelKingdom).ToList())
            {
                var rebel = faction.GetTrackedRebelKingdomIncludingEliminated();
                if (rebel?.IsEliminated == false && rebel.IsAtWarWith(faction.ParentKingdom))
                    Observe(faction, rebel, scores?.GetActiveWar(rebel, faction.ParentKingdom));
            }
        }
    }
}
