using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;

namespace BellumCivile.Behaviors
{
    public sealed partial class CivilWarConflictBehavior
    {
        private List<CivilWarRivalPromotionRecord> _promotions = new List<CivilWarRivalPromotionRecord>();
        private readonly HashSet<CivilWarRivalPromotionRecord> _promoting = new HashSet<CivilWarRivalPromotionRecord>();

        internal bool PrepareRivalCrownVictory(FactionObject faction)
        {
            var prior = _promotions.LastOrDefault(p => p.Winner.Faction == faction);
            if (prior != null) return prior.CollapseOwner == null && (prior.Completed || ResumeRivalPromotion(prior));
            var conflict = GetConflict(faction);
            if (conflict == null) return true;
            var winner = conflict.Sides.First(s => s.Faction == faction);
            if (!conflict.Pairs.Any(p => !p.Closed && p.IsRivalry
                && (p.AttackerSideId == winner.Id || p.DefenderSideId == winner.Id))) return true;
            var transfer = CaptureRivalCrownVictory(faction);
            if (transfer == null) return false;
            _promotions.Add(transfer);
            return ResumeRivalPromotion(transfer);
        }

        // Capture only: a destruction callback must not change scores, stances or households.
        internal CivilWarRivalPromotionRecord CaptureRivalCrownVictory(FactionObject faction)
        {
            var conflict = GetConflict(faction);
            if (conflict == null) return null;
            var winner = conflict.Sides.First(s => s.Faction == faction);
            var rivals = conflict.Pairs.Where(p => !p.Closed && p.IsRivalry
                && (p.AttackerSideId == winner.Id || p.DefenderSideId == winner.Id)).ToList();
            if (faction.Type != FactionType.InstallRuler || rivals.Count != 1 || NativeWarStart == null
                || conflict.Sides.Count(s => !s.Closed) != 2 || IsRealmTransferPending(conflict.CrownRealm)
                || conflict.CrownRealm?.IsEliminated != false) return null;
            var rival = rivals[0];
            var survivor = conflict.Sides.First(s => !s.Closed && s != winner);
            var crownPair = conflict.Pairs.FirstOrDefault(p => !p.Closed && p.AttackerSideId == survivor.Id
                && p.DefenderSideId == CivilWarConflictRecord.CrownSide);
            if (winner.Realm?.IsEliminated != false || survivor.Realm?.IsEliminated != false
                || winner.Realm.RulingClan != faction.Leader || faction.Leader?.Kingdom != winner.Realm
                || !winner.Realm.IsAtWarWith(survivor.Realm) || !survivor.Realm.IsAtWarWith(conflict.CrownRealm)
                || crownPair?.Score?.IsActive != true || rival.Score?.CanPromoteRivalry(winner.Realm.StringId,
                    survivor.Realm.StringId, conflict.CrownRealm.StringId) != true) return null;
            var old = crownPair.Score;
            if (old.ResolutionPending || old.TerminalResolutionQueued || old.ParleyPending || old.WhitePeaceOfferPending) return null;
            var stance = survivor.Realm.GetStanceWith(winner.Realm);
            var transfer = new CivilWarRivalPromotionRecord { Conflict = conflict, Winner = winner, Survivor = survivor,
                RivalPair = rival, CrownPair = crownPair, OldCrownScore = old, WarStarted = stance.WarStartDate };
            CaptureCounts(transfer.NativeCounts, stance, survivor.Realm);
            CaptureCounts(transfer.NativeCounts, stance, winner.Realm);
            return transfer;
        }

        internal bool ResumeCollapsePromotion(CivilWarCollapseRecord owner)
        {
            var promotion = owner?.Promotion;
            if (promotion == null || promotion.CollapseOwner != owner || promotion.Winner.Faction != owner.Winner
                || promotion.Conflict != owner.Conflict || Campaign.Current.GetCampaignBehavior<CivilWarResolutionBehavior>()?
                    .OwnsContinuingCollapse(owner) != true) return false;
            if (!_promotions.Contains(promotion)) _promotions.Add(promotion);
            return ResumeRivalPromotion(promotion);
        }

        private void ResumePendingRivalPromotions()
        {
            foreach (var transfer in _promotions.Where(p => !p.Completed && p.CollapseOwner == null).ToList())
                if (ResumeRivalPromotion(transfer))
                    Campaign.Current.GetCampaignBehavior<CivilWarResolutionBehavior>()?
                        .ResolveRebelVictory(transfer.Winner.Faction, transfer.Winner.Realm);
        }

        private bool ResumeRivalPromotion(CivilWarRivalPromotionRecord transfer)
        {
            if (transfer.Completed) return true;
            if (!_promoting.Add(transfer)) return false;
            try
            {
                var crown = transfer.Conflict.CrownRealm;
                var winner = transfer.Winner.Realm;
                var survivor = transfer.Survivor.Realm;
                if (crown?.IsEliminated != false || winner?.IsEliminated != false || survivor?.IsEliminated != false
                    || transfer.Winner.Faction.ParentKingdom != crown || transfer.Survivor.Faction.ParentKingdom != crown
                    || winner.RulingClan != transfer.Winner.Faction.Leader
                    || transfer.Winner.Faction.Leader?.Kingdom != winner || transfer.NativeCounts.Count != 10
                    || !survivor.IsAtWarWith(crown) || !winner.IsAtWarWith(crown)) return false;
                if (transfer.Stage == 0)
                {
                    // A deferred reservation may precede the end of a map event.
                    // Refresh native counters only before score promotion has begun.
                    if (transfer.RivalPair.Score.ContextId.StartsWith(CivilWarPairRecord.RivalryPrefix, StringComparison.Ordinal)
                        && survivor.IsAtWarWith(winner))
                    {
                        var live = survivor.GetStanceWith(winner);
                        var counts = new List<int>();
                        CaptureCounts(counts, live, survivor);
                        CaptureCounts(counts, live, winner);
                        transfer.NativeCounts = counts;
                        transfer.WarStarted = live.WarStartDate;
                    }
                    if (Campaign.Current.GetCampaignBehavior<WarScoreBehavior>()?.PromoteRivalry(transfer) != true) return false;
                    transfer.Stage = 1;
                }
                if (transfer.Stage == 1)
                {
                    var stance = survivor.GetStanceWith(crown);
                    NativeWarStart.SetValue(stance, transfer.WarStarted);
                    RestoreCounts(transfer.NativeCounts, 0, stance, survivor);
                    RestoreCounts(transfer.NativeCounts, 5, stance, crown);
                    Campaign.Current.GetCampaignBehavior<WarPeaceRevampBehavior>()?
                        .EndSupersededCivilWarMomentum(survivor, crown);
                    transfer.Stage = 2;
                }
                if (transfer.Stage == 2)
                {
                    // Promote the existing rivalry tracker; Complete(winner) must not close it.
                    transfer.CrownPair.Score = transfer.RivalPair.Score;
                    transfer.RivalPair.Closed = true;
                    transfer.Stage = 3;
                }
                if (transfer.Stage == 3)
                {
                    Campaign.Current.GetCampaignBehavior<WarPeaceRevampBehavior>()?
                        .RetargetCivilWarMomentum(survivor, winner, crown);
                    if (survivor.IsAtWarWith(winner)) FactionManager.SetNeutral(survivor, winner);
                    transfer.Completed = true;
                    transfer.Failure = null;
                    BellumCivileLogger.Log($"Rival Crown promotion completed; winner_house={transfer.Winner.Faction.Leader.StringId}; "
                        + $"survivor_house={transfer.Survivor.Faction.Leader.StringId}; crown={crown.StringId}; "
                        + $"continued_war={transfer.CrownPair.Score.WarKey}; score={transfer.CrownPair.Score.Score:0.000}; "
                        + $"crown_hostility={survivor.IsAtWarWith(crown)}; old_shell_hostility={survivor.IsAtWarWith(winner)}; "
                        + $"native_counters={string.Join(",", transfer.NativeCounts)}.");
                }
                return transfer.Completed;
            }
            catch (Exception ex)
            {
                if (transfer.Failure != ex.Message) BellumCivileLogger.Log($"Rival Crown promotion deferred; stage={transfer.Stage}; error={ex}");
                transfer.Failure = ex.Message;
                return false;
            }
            finally { _promoting.Remove(transfer); }
        }
    }
}
