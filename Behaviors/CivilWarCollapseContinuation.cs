using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Localization;

namespace BellumCivile.Behaviors
{
    public partial class CivilWarResolutionBehavior
    {
        private List<CivilWarCollapseRecord> _collapses = new List<CivilWarCollapseRecord>();
        private readonly HashSet<CivilWarCollapseRecord> _resumingCollapses = new HashSet<CivilWarCollapseRecord>();

        internal bool IsCollapseRealmPending(Kingdom realm) => _collapses.Any(c => c.Protects(realm));
        internal bool OwnsContinuingCollapse(CivilWarCollapseRecord record) => record != null && !record.Completed && _collapses.Contains(record);
        internal bool IsCollapseScorePending(WarScoreRecord score) => _collapses.Any(c => c.ProtectsScore(score));

        private bool TryResumeChallengeCollapse(SuccessionChallengeRecord challenge)
        {
            var collapse = _collapses.FirstOrDefault(c => c.OwnsChallenge(challenge));
            if (collapse == null) return false;
            ResumeCollapse(collapse);
            return true;
        }

        private void ResumePendingCollapses()
        {
            foreach (var record in _collapses.Where(c => !c.Completed).ToList()) ResumeCollapse(record);
        }

        internal bool ReserveCollapseBeforeDestruction(Kingdom parent)
        {
            if (parent == null || parent.IsEliminated) return false;
            var existing = _collapses.FirstOrDefault(c => c.Parent == parent && !c.Completed);
            if (existing != null) return existing.Stage < 3;
            if (CivilWarConflictBehavior.IsRealmTransferPending(parent)) return true;
            if (CountStrongholds(parent) > 0) return false;
            var manager = Campaign.Current.GetCampaignBehavior<FactionManagerBehavior>();
            if (manager == null) return false;
            var wars = manager.GetFactionsInKingdom(parent).Where(f => !f.IsIdeology)
                .Select(f => new { Faction = f, Realm = ResolveTrackedRebelKingdom(f) })
                .Where(x => x.Realm != null && x.Realm != parent && !x.Realm.IsEliminated)
                .ToDictionary(x => x.Faction, x => x.Realm);
            if (ResolveDecisiveCivilWarVictor(parent, wars, null) != null) return false;
            // Reserve only. Destruction can be requested inside an unfinished map event;
            // kingdom creation and household movement belong to the later recovery tick.
            return TryBeginContinuingCollapse(parent, wars, deferResume: true);
        }

        private bool TryBeginContinuingCollapse(Kingdom parent, Dictionary<FactionObject, Kingdom> wars, bool deferResume = false)
        {
            if (wars.Count == 0 || wars.Keys.Any(f => f.Type != FactionType.InstallRuler)) return false;
            var winner = wars.Keys.OrderByDescending(f => f.CalculateFactionPower())
                .ThenBy(f => wars[f].StringId, StringComparer.Ordinal).First();
            var challenges = SuccessionChallengeBehavior.Instance;
            var challenge = challenges?.GetWarRecord(winner);
            if (challenge != null && (challenge.Phase != SuccessionChallengePhase.ActiveWar
                || challenge.WarOutcome != SuccessionChallengeOutcome.None || !challenges.CanWinChallenge(winner))) return false;
            if (CrownAccessionBehavior.Instance?.IsPending(parent) == true
                || _pendingSuccessionCandidates.ContainsKey(parent.StringId)
                || parent.UnresolvedDecisions.Any(d => d is TaleWorlds.CampaignSystem.Election.KingSelectionKingdomDecision)) return true;
            var journal = CivilWarConflictBehavior.Instance;
            if (journal == null || winner.Leader?.Leader?.IsAlive != true) return false;
            foreach (var entry in wars)
                journal.Observe(entry.Key, entry.Value, Campaign.Current.GetCampaignBehavior<WarScoreBehavior>()?.GetActiveWar(entry.Value, parent));
            var conflict = journal.GetConflict(winner);
            if (conflict == null
                || wars.Keys.Any(f => journal.GetConflict(f) != conflict)
                || conflict.Sides.Count(s => !s.Closed) != wars.Count) return false;
            // Do not take ownership after native history has already been destroyed.
            if (wars.Any(w => !w.Value.IsAtWarWith(parent) || w.Key.IsChallengeStartupPending
                || w.Key.Leader?.Kingdom != w.Value)) return false;
            // Let outstanding choices finish before reserving the realms; otherwise
            // their own resolution guard would prevent the transfer becoming ready.
            if (conflict.Pairs.Any(p => !p.Closed && p.Score != null
                && (p.Score.WhitePeaceOfferPending || p.Score.ParleyPending || p.Score.ResolutionPending))) return true;
            if (WarPeaceRevampBehavior.IsRevampEnabled() && conflict.Pairs.Any(p => !p.Closed && p.Score == null)) return true;
            bool hasRivalry = conflict.Pairs.Any(p => !p.Closed && p.IsRivalry);
            var promotion = hasRivalry ? journal.CaptureRivalCrownVictory(winner) : null;
            if (hasRivalry && promotion == null) return true;
            var nativePairs = hasRivalry ? new List<CivilWarPairTransferRecord>() : journal.CaptureCrownPairs(conflict, winner);
            if (!hasRivalry && (nativePairs == null || wars.Count > 1 && nativePairs.Count == 0)) return true;
            var record = new CivilWarCollapseRecord
            {
                SuccessorId = parent.StringId + "_restored_conflict_" + Guid.NewGuid().ToString("N"),
                Conflict = conflict, Parent = parent, Winner = winner, WinnerRealm = wars[winner],
                WinnerHouse = winner.Leader, WinnerClans = wars[winner].Clans.ToList(),
                ExternalEnemies = GetExternalEnemiesToInherit(parent, wars.Values), Challenge = challenge,
                NativePairs = nativePairs, Promotion = promotion
            };
            if (promotion != null) promotion.CollapseOwner = record;
            winner.CaptureCivilWarStartInfluence(record.WinnerClans);
            if (challenge != null)
            {
                // Victory preparation only records receipts; it does not move or legalize
                // property. Capture the old loyalists before either household moves.
                CaptureCurrentCivilWarFiefSnapshot(winner, record.WinnerRealm);
                if (!challenges.PrepareWarOutcome(winner, SuccessionChallengeOutcome.Victory, parent)) return true;
            }
            _collapses.Add(record);
            ConflictOutcomeBehavior.Current?.BeginCivil(winner, record.WinnerRealm, "collapse:" + record.SuccessorId);
            BellumCivileLogger.Log($"Reserved civil-war collapse; parent={parent.StringId}; winner={winner.Leader.StringId}; sides={wars.Count}; external_enemies={string.Join(",", record.ExternalEnemies.Select(k => k.StringId))}; successor={record.SuccessorId}.");
            if (!deferResume) ResumeCollapse(record);
            return true;
        }

        private void ResumeCollapse(CivilWarCollapseRecord record)
        {
            if (record.Completed || !_resumingCollapses.Add(record)) return;
            try
            {
                var resultNotice = ConflictOutcomeBehavior.Current?.BeginCivil(record.Winner, record.WinnerRealm,
                    "collapse:" + record.SuccessorId);
                if (record.Stage < 3 && (record.WinnerHouse?.Leader?.IsAlive != true
                    || record.WinnerHouse.IsEliminated || record.Successor?.IsEliminated == true))
                    throw new InvalidOperationException("Chosen successor is no longer available");
                if (record.Challenge != null && SuccessionChallengeBehavior.Instance?.CanWinChallenge(record.Winner) != true)
                    throw new InvalidOperationException("The original hereditary claimant can no longer take the Crown");
                if (record.Stage == 0)
                {
                    if (record.Promotion != null && CivilWarConflictBehavior.Instance?.ResumeCollapsePromotion(record) != true) return;
                    record.Successor = record.Successor ?? Kingdom.All.FirstOrDefault(k => k.StringId == record.SuccessorId)
                        ?? KingdomCreationSafetyHelper.CreateKingdom(record.SuccessorId, record.WinnerHouse);
                    var successor = record.Successor;
                    if (successor == null || successor.IsEliminated) return;
                    var visuals = KingdomVisualHelper.ResolveInheritedKingdomVisuals(record.Parent, record.WinnerHouse, record.SuccessorId);
                    if (!record.Initialized)
                    {
                        var capital = record.WinnerHouse.Settlements.FirstOrDefault()
                            ?? record.WinnerRealm.Settlements.FirstOrDefault()
                            ?? Settlement.All.FirstOrDefault(s => s.IsTown || s.IsCastle);
                        successor.InitializeKingdom(record.Parent.Name, record.Parent.Name,
                            record.Parent.Culture ?? record.WinnerRealm.Culture, visuals.Banner,
                            visuals.PrimaryColor, visuals.SecondaryColor, capital,
                            new TextObject(""), new TextObject(""), new TextObject(""));
                        record.Initialized = true;
                    }
                    KingdomVisualHelper.ApplyKingdomPalette(successor, visuals);
                    RebelPolicyHelper.CopyPolicies(record.Parent.ActivePolicies.Any() ? record.Parent : record.WinnerRealm, successor);
                    record.Stage = 1;
                }
                if (record.Stage == 1)
                {
                    // Capture every surviving native war before moving either Crown household.
                    // Refresh a deferred reservation while its native history still exists.
                    if (record.Transfer == null && !record.Parent.IsEliminated)
                    {
                        var latest = CivilWarConflictBehavior.Instance?.CaptureCrownPairs(record.Conflict, record.Winner);
                        if (latest == null) return;
                        record.NativePairs = latest;
                    }
                    // A sole claimant has no surviving civil-war pairs to retarget.
                    // It still needs the saved mantle, household and external-war stages.
                    if (record.NativePairs.Count > 0)
                    {
                        record.Transfer = record.Transfer ?? CivilWarConflictBehavior.Instance?
                            .BeginCrownTransfer(record.Conflict, record.Successor, record.Winner, record.NativePairs);
                        if (record.Transfer == null) throw new InvalidOperationException("Surviving wars are not ready for transfer");
                    }
                    record.Stage = 2;
                }
                if (record.Stage == 2)
                {
                    ReleaseMercenariesFromKingdom(record.WinnerRealm);
                    ReleaseMercenariesFromKingdom(record.Parent);
                    TransferClansToKingdom(record.WinnerRealm, record.Successor, record.WinnerHouse);
                    TransferClansToKingdom(record.Parent, record.Successor, record.WinnerHouse);
                    if (record.WinnerRealm.Clans.Any(c => !c.IsEliminated) || record.Parent.Clans.Any(c => !c.IsEliminated)) return;
                    EnsureValidRulingClan(record.Successor, record.WinnerHouse, forcePreferred: true);
                    Campaign.Current.GetCampaignBehavior<FeudalTitleBehavior>()?
                        .RegisterRestoredRealmMantle(record.Parent, record.Successor, record.WinnerHouse, "continuing civil-war collapse");
                    var mantle = Campaign.Current.GetCampaignBehavior<FeudalTitleBehavior>()?.GetIndependentRealmSovereignTitle(record.Successor);
                    if (mantle == null || mantle.AssociatedKingdomId != record.Successor.StringId
                        || mantle.DeFactoHolderClanId != record.WinnerHouse.StringId)
                        throw new InvalidOperationException("Successor mantle has not been transferred");
                    if (record.Transfer != null && CivilWarConflictBehavior.Instance?.ResumeCrownTransfer(record.Transfer) != true) return;
                    record.Stage = 3;
                }
                if (record.Stage == 3)
                {
                    if (record.Challenge != null)
                    {
                        var challenges = SuccessionChallengeBehavior.Instance;
                        if (!challenges.PrepareWarOutcome(record.Winner, SuccessionChallengeOutcome.Victory, record.Successor)
                            || !challenges.LegalizeChallengeVictory(record.Winner, record.Successor)) return;
                        TryQueueChallengeTribunal(record.Winner, record.Successor);
                        if (!record.Challenge.TribunalQueued) return;
                    }
                    CompleteCivilWarTracker(record.Winner, record.WinnerRealm, record.Transfer != null
                        ? "Crown mantle restored; rival claims continue" : "Crown mantle restored after outside conquest");
                    Campaign.Current.GetCampaignBehavior<FactionManagerBehavior>()?.RemoveFaction(record.Winner);
                    PreservePromotedRebelForeignWars(record.WinnerRealm, record.Successor, record.Parent);
                    if (!DrainAndDestroyRebelKingdom(record.WinnerRealm, record.Successor)) return;
                    if (!DrainAndDestroyRebelKingdom(record.Parent, record.Successor)) return;
                    record.Stage = 4;
                }
                if (record.Stage == 4)
                {
                    if (!record.RewardsApplied)
                    {
                        if (!record.InfluenceRestored)
                        {
                            record.InfluenceRestored = true;
                            record.Winner.RestoreCivilWarInfluenceSnapshots(record.WinnerClans);
                        }
                        // Hereditary rewards have per-clan receipts, so a partial delivery
                        // can retry without duplicating rewards or resetting influence.
                        if (record.Challenge == null) record.RewardsApplied = true;
                        ApplyChallengeAwareVictoryRewards(record.Winner, record.WinnerClans, record.WinnerHouse, "continuing parent collapse");
                        record.RewardsApplied = true;
                    }
                    InheritExternalWars(record.Successor, record.ExternalEnemies);
                    Campaign.Current.GetCampaignBehavior<IdeologyBehavior>()?.RefreshKingdomIdeologies(record.Successor);
                    if (record.Challenge != null)
                    {
                        SuccessionChallengeBehavior.Instance.CompleteWarOutcome(record.Winner, record.Successor);
                        if (!record.Challenge.ResolutionReturned || record.Challenge.Phase != SuccessionChallengePhase.Settled) return;
                    }
                    if (!record.Announced)
                    {
                        CaptureOutcomeTribunal(resultNotice, record.Successor);
                        ConflictOutcomeBehavior.Current?.Publish(resultNotice, "collapse",
                            ConflictOutcomeBehavior.RemainingWars(record.Successor, record.ExternalEnemies));
                        record.Announced = true;
                    }
                    record.Completed = true;
                    record.Failure = null;
                    BellumCivileLogger.Log($"Civil-war collapse completed; parent={record.Parent.StringId}; successor={record.Successor.StringId}; surviving_rivals={record.Transfer?.Pairs.Count ?? 0}; external_enemies={string.Join(",", record.ExternalEnemies.Where(k => k != null && !k.IsEliminated).Select(k => k.StringId))}.");
                }
            }
            catch (Exception ex)
            {
                if (record.Failure != ex.Message)
                    BellumCivileLogger.Log($"Civil-war collapse deferred; successor={record.SuccessorId}; stage={record.Stage}; error={ex}");
                record.Failure = ex.Message;
            }
            finally { _resumingCollapses.Remove(record); }
        }
    }
}
