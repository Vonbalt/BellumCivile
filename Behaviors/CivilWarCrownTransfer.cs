using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using TaleWorlds.CampaignSystem;

namespace BellumCivile.Behaviors
{
    public sealed partial class CivilWarConflictBehavior
    {
        private List<CivilWarCrownTransferRecord> _transfers = new List<CivilWarCrownTransferRecord>();
        private readonly HashSet<CivilWarCrownTransferRecord> _resuming = new HashSet<CivilWarCrownTransferRecord>();
        private static readonly System.Reflection.FieldInfo NativeWarStart = AccessTools.Field(typeof(StanceLink), "_warStartDate");

        internal static bool IsRealmTransferPending(Kingdom realm) => realm != null
            && (Instance?._transfers.Any(t => t.Protects(realm)) == true
                || ElectiveContestBehavior.Instance?.IsDispatchRealmPending(realm) == true
                || Instance?._promotions.Any(t => t.Protects(realm)) == true
                || Campaign.Current?.GetCampaignBehavior<CivilWarResolutionBehavior>()?.IsCollapseRealmPending(realm) == true
                || Campaign.Current?.GetCampaignBehavior<CivilWarResolutionBehavior>()?.IsRivalDefeatPending(realm) == true);

        internal static bool IsFactionTransferPending(FactionObject faction) => faction != null
            && (Instance?._transfers.Any(t => t.ProtectsFaction(faction)) == true
                || IsRealmTransferPending(faction.ParentKingdom));

        internal static bool IsScoreTransferPending(WarScoreRecord score) => score != null
            && (Instance?._transfers.Any(t => !t.Completed && t.Pairs.Any(p => p.Pair?.Score == score)) == true
                || ElectiveContestBehavior.Instance?.IsDispatchScorePending(score) == true
                || Instance?._promotions.Any(t => !t.Completed && (t.RivalPair.Score == score || t.OldCrownScore == score
                    || t.Conflict.Pairs.Any(p => p.Score == score))) == true
                || Campaign.Current?.GetCampaignBehavior<CivilWarResolutionBehavior>()?.IsCollapseScorePending(score) == true
                || Campaign.Current?.GetCampaignBehavior<CivilWarResolutionBehavior>()?.IsRivalDefeatScorePending(score) == true);

        private void ResumePendingCrownTransfers()
        {
            foreach (var transfer in _transfers.Where(t => !t.Completed).ToList())
                ResumeCrownTransfer(transfer);
        }

        // Explicit entries only. The collapse coordinator must protect both realm shells
        // from cleanup and terminal resolution until the entire transfer commits.
        internal CivilWarCrownTransferRecord BeginCrownTransfer(CivilWarConflictRecord conflict, Kingdom successor,
            FactionObject retiringFaction = null, List<CivilWarPairTransferRecord> capturedPairs = null)
        {
            if (conflict == null || !_conflicts.Contains(conflict) || successor == null) return null;
            var pending = _transfers.FirstOrDefault(t => t.Conflict == conflict && !t.Completed);
            if (pending != null) return pending.Successor == successor ? pending : null;
            if (conflict.CrownRealm == successor) return _transfers.LastOrDefault(t => t.Conflict == conflict && t.Successor == successor);
            if (conflict.Closed || NativeWarStart == null
                || (retiringFaction == null && !SuccessorReady(conflict.CrownRealm, successor))) return null;
            if (retiringFaction != null && !conflict.Sides.Any(s => !s.Closed && s.Faction == retiringFaction)) return null;
            if (conflict.Pairs.Any(p => !p.Closed && p.DefenderSideId != CivilWarConflictRecord.CrownSide)) return null;
            capturedPairs = capturedPairs?.Count > 0 ? capturedPairs : CaptureCrownPairs(conflict, retiringFaction);
            if (capturedPairs == null) return null;
            var transfer = new CivilWarCrownTransferRecord { Conflict = conflict, Previous = conflict.CrownRealm, Successor = successor };
            var scores = Campaign.Current.GetCampaignBehavior<WarScoreBehavior>();
            foreach (var pair in conflict.Pairs.Where(p => !p.Closed))
            {
                var side = conflict.Sides.FirstOrDefault(s => s.Id == pair.AttackerSideId && !s.Closed);
                if (side?.Faction == retiringFaction && retiringFaction != null) continue;
                if (side?.Realm?.IsEliminated != false || side.Realm == successor || side.Faction?.ParentKingdom != transfer.Previous
                    || (!transfer.Previous.IsEliminated && !side.Realm.IsAtWarWith(transfer.Previous))
                    || side.Realm.IsAtWarWith(successor)) return null;
                if (pair.Score == null) pair.Score = scores?.GetActiveWar(side.Realm, transfer.Previous);
                if (pair.Score != null && scores?.CanRetargetCivilWarDefender(pair.Score, transfer.Previous, successor) != true) return null;
                if (WarPeaceRevampBehavior.IsRevampEnabled() && pair.Score == null) return null;
                var matching = capturedPairs.Where(p => p.Side == side && p.Pair == pair).ToList();
                if (matching.Count != 1 || matching[0].Stage != 0 || matching[0].NativeCounts.Count != 10) return null;
                var step = matching[0];
                transfer.Pairs.Add(step);
            }
            if (transfer.Pairs.Count == 0) return null;
            // Store every original native counter before any old stance can be neutralized.
            _transfers.Add(transfer);
            return transfer;
        }

        internal List<CivilWarPairTransferRecord> CaptureCrownPairs(CivilWarConflictRecord conflict, FactionObject retiringFaction)
        {
            if (conflict == null || !_conflicts.Contains(conflict) || conflict.Closed || conflict.CrownRealm?.IsEliminated != false)
                return null;
            var captured = new List<CivilWarPairTransferRecord>();
            foreach (var pair in conflict.Pairs.Where(p => !p.Closed))
            {
                if (pair.DefenderSideId != CivilWarConflictRecord.CrownSide) return null;
                var side = conflict.Sides.FirstOrDefault(s => s.Id == pair.AttackerSideId && !s.Closed);
                if (side == null) return null;
                if (retiringFaction != null && side.Faction == retiringFaction) continue;
                if (side.Realm?.IsEliminated != false || side.Faction?.ParentKingdom != conflict.CrownRealm
                    || !side.Realm.IsAtWarWith(conflict.CrownRealm)) return null;
                var stance = side.Realm.GetStanceWith(conflict.CrownRealm);
                if (stance?.IsAtWar != true) return null;
                var snapshot = new CivilWarPairTransferRecord { Side = side, Pair = pair, WarStarted = stance.WarStartDate };
                CaptureCounts(snapshot.NativeCounts, stance, side.Realm);
                CaptureCounts(snapshot.NativeCounts, stance, conflict.CrownRealm);
                captured.Add(snapshot);
            }
            return captured;
        }

        internal bool ResumeCrownTransfer(CivilWarCrownTransferRecord transfer)
        {
            if (transfer == null || !_transfers.Contains(transfer) || !_resuming.Add(transfer)) return false;
            try
            {
                if (transfer.Completed) return true;
                if (!SuccessorReady(transfer.Previous, transfer.Successor) || NativeWarStart == null)
                    return Defer(transfer, "Successor mantle or old Crown household transfers are incomplete");
                var scores = Campaign.Current.GetCampaignBehavior<WarScoreBehavior>();
                foreach (var step in transfer.Pairs)
                {
                    if (step.Side.Closed || step.Pair.Closed || step.Side.Realm?.IsEliminated != false
                        || step.Side.Faction.Leader?.Kingdom != step.Side.Realm || step.NativeCounts.Count != 10
                        || (step.Side.Faction.ParentKingdom != transfer.Previous && step.Side.Faction.ParentKingdom != transfer.Successor))
                        return Defer(transfer, "A surviving coalition changed during the Crown transfer");
                    if (step.Pair.Score != null && scores?.CanRetargetCivilWarDefender(step.Pair.Score, transfer.Previous, transfer.Successor) != true)
                        return Defer(transfer, "A war record has a pending settlement or conflicting successor pair");
                }
                transfer.Started = true;
                foreach (var step in transfer.Pairs)
                {
                    if (!CivilWarTransferRules.Advance(step, stage => ApplyTransferStage(transfer, step, stage)))
                        return Defer(transfer, "A Crown transfer stage did not satisfy its postconditions");
                }
                if (transfer.Pairs.Any(p => !TransferPostconditions(transfer, p)))
                    return Defer(transfer, "Surviving wars are not yet fully attached to the successor");
                transfer.Conflict.CrownRealm = transfer.Successor;
                transfer.Completed = true;
                transfer.Failure = null;
                return true;
            }
            catch (Exception ex)
            {
                BellumCivileLogger.Log($"Civil-war Crown transfer deferred; conflict={transfer.Conflict?.Id}; error={ex}");
                return Defer(transfer, ex.Message);
            }
            finally { _resuming.Remove(transfer); }
        }

        private static bool SuccessorReady(Kingdom previous, Kingdom successor)
        {
            if (previous == null || previous == successor || successor?.IsEliminated != false
                || successor.RulingClan?.Leader?.IsAlive != true
                || previous.Clans.Any(c => !c.IsEliminated)
                || CrownAccessionBehavior.Instance?.IsPending(successor) == true) return false;
            var title = Campaign.Current.GetCampaignBehavior<FeudalTitleBehavior>()?.GetRealmSovereignTitle(successor);
            return title != null && title.DeJureHolderClanId == successor.RulingClan.StringId
                && title.DeFactoHolderClanId == successor.RulingClan.StringId;
        }

        private static bool ApplyTransferStage(CivilWarCrownTransferRecord transfer, CivilWarPairTransferRecord step, int stage)
        {
            var rebel = step.Side.Realm;
            switch (stage)
            {
                case 0:
                    return step.Pair.Score == null || Campaign.Current.GetCampaignBehavior<WarScoreBehavior>()?
                        .RetargetCivilWarDefender(step.Pair.Score, transfer.Previous, transfer.Successor) == true;
                case 1:
                    return step.Side.Faction.RetargetCivilWarParent(transfer.Previous, transfer.Successor);
                case 2:
                    // Low-level stance transfer avoids new-war momentum, recruitment,
                    // prisoner releases and negotiated-peace callbacks.
                    if (!rebel.IsAtWarWith(transfer.Successor)) FactionManager.DeclareWar(rebel, transfer.Successor);
                    if (!rebel.IsAtWarWith(transfer.Successor)) return false;
                    var stance = rebel.GetStanceWith(transfer.Successor);
                    NativeWarStart.SetValue(stance, step.WarStarted);
                    RestoreCounts(step.NativeCounts, 0, stance, rebel);
                    RestoreCounts(step.NativeCounts, 5, stance, transfer.Successor);
                    return stance.WarStartDate == step.WarStarted;
                case 3:
                    Campaign.Current.GetCampaignBehavior<WarPeaceRevampBehavior>()?
                        .RetargetCivilWarMomentum(rebel, transfer.Previous, transfer.Successor);
                    if (rebel.GetStanceWith(transfer.Previous).IsAtWar) FactionManager.SetNeutral(rebel, transfer.Previous);
                    return TransferPostconditions(transfer, step);
                default: return false;
            }
        }

        private static bool TransferPostconditions(CivilWarCrownTransferRecord transfer, CivilWarPairTransferRecord step) =>
            step.Side.Faction.ParentKingdom == transfer.Successor && step.Side.Realm.IsAtWarWith(transfer.Successor)
            && !step.Side.Realm.GetStanceWith(transfer.Previous).IsAtWar
            && (step.Pair.Score == null || step.Pair.Score.IsActive && step.Pair.Score.DefenderKingdomId == transfer.Successor.StringId);

        private static bool Defer(CivilWarCrownTransferRecord transfer, string reason)
        {
            transfer.Failure = reason;
            return false;
        }

        internal static void CaptureCounts(List<int> counts, StanceLink stance, IFaction side)
        {
            counts.Add(stance.GetCasualties(side));
            counts.Add(side == stance.Faction1 ? stance.ShipCasualties1 : stance.ShipCasualties2);
            counts.Add(stance.GetSuccessfulSieges(side));
            counts.Add(stance.GetSuccessfulTownSieges(side));
            counts.Add(stance.GetSuccessfulRaids(side));
        }

        internal static void RestoreCounts(List<int> counts, int offset, StanceLink stance, IFaction side)
        {
            if (stance.Faction1 == side)
            {
                stance.TroopCasualties1 = counts[offset]; stance.ShipCasualties1 = counts[offset + 1];
                stance.SuccessfulSieges1 = counts[offset + 2]; stance.SuccessfulTownSieges1 = counts[offset + 3];
                stance.SuccessfulRaids1 = counts[offset + 4];
            }
            else if (stance.Faction2 == side)
            {
                stance.TroopCasualties2 = counts[offset]; stance.ShipCasualties2 = counts[offset + 1];
                stance.SuccessfulSieges2 = counts[offset + 2]; stance.SuccessfulTownSieges2 = counts[offset + 3];
                stance.SuccessfulRaids2 = counts[offset + 4];
            }
            else throw new InvalidOperationException("War counter recipient is not a stance participant");
        }
    }
}
