using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using C = BellumCivile.BellumCivileConstants;

namespace BellumCivile.Behaviors
{
    /// <summary>
    /// Why did I do this file?
    /// To serve as the central hub tracking every active faction in every kingdom. It handles daily cleanup, synchronizes vanilla defections with mod rosters, processes succession crises, and executes the King's weekly loyalty audit.
    /// </summary>
    public class FactionManagerBehavior : CampaignBehaviorBase
    {
        public static FactionManagerBehavior Instance { get; private set; }

        private const int CompatibilityRepairWindowDays = 5;

        private List<FactionObject> _activeFactions = new List<FactionObject>();
        private Dictionary<Clan, CampaignTime> _pacifiedClans = new Dictionary<Clan, CampaignTime>();
        private Dictionary<Clan, CampaignTime> _settlementSyncStamps = new Dictionary<Clan, CampaignTime>();
        private List<Clan> _pendingSettlementRefreshClans = new List<Clan>();
        private CampaignTime _compatibilityRepairWindowUntil = CampaignTime.Zero;
        private List<string> _temporaryKingdomRepairOrder = new List<string>();
        private Dictionary<string, CampaignTime> _temporaryKingdomRepairExpirations = new Dictionary<string, CampaignTime>();
        private Dictionary<string, string> _temporaryKingdomExpectedRulerIds = new Dictionary<string, string>();
        private Dictionary<string, string> _temporaryKingdomRepairReasons = new Dictionary<string, string>();
        private Dictionary<string, string> _lastKnownRulingClanIds = new Dictionary<string, string>();
        private Dictionary<string, int> _rulerRepairSuppressionDepths = new Dictionary<string, int>();
        private HashSet<string> _rulerRepairFailureLogKeys = new HashSet<string>();
        private readonly Dictionary<Clan, FactionObject> _ideologyByClan = new Dictionary<Clan, FactionObject>();
        private readonly Dictionary<Clan, ActiveCivilWarMembership> _activeCivilWarByClan = new Dictionary<Clan, ActiveCivilWarMembership>();
        private readonly HashSet<string> _rebelKingdomReconciliationFailureKeys = new HashSet<string>();
        private bool _factionLookupCacheDirty = true;

        private sealed class ActiveCivilWarMembership
        {
            public FactionObject Faction;
            public Kingdom RebelKingdom;
        }

        public override void RegisterEvents()
        {
            Instance = this;
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
            CampaignEvents.WeeklyTickEvent.AddNonSerializedListener(this, OnWeeklyTick);
            CampaignEvents.OnClanChangedKingdomEvent.AddNonSerializedListener(this, OnClanChangedKingdom);
            CampaignEvents.OnClanDestroyedEvent.AddNonSerializedListener(this, OnClanDestroyed);
            CampaignEvents.RulingClanChanged.AddNonSerializedListener(this, OnRulingClanChanged);
            CampaignEvents.MakePeace.AddNonSerializedListener(this, OnMakePeace);
            CampaignEvents.HeroKilledEvent.AddNonSerializedListener(this, OnHeroKilled);
            CampaignEvents.KingdomDestroyedEvent.AddNonSerializedListener(this, OnKingdomDestroyed);
        }

        public override void SyncData(IDataStore dataStore)
        {
            Instance = this;
            dataStore.SyncData("BellumCivile_ActiveFactions", ref _activeFactions);
            dataStore.SyncData("BellumCivile_PacifiedClans", ref _pacifiedClans);
            dataStore.SyncData("BellumCivile_SettlementSyncStamps", ref _settlementSyncStamps);
            dataStore.SyncData("BellumCivile_PendingSettlementRefreshClans", ref _pendingSettlementRefreshClans);
            dataStore.SyncData("BellumCivile_CompatibilityRepairWindowUntil", ref _compatibilityRepairWindowUntil);
            dataStore.SyncData("BellumCivile_TemporaryKingdomRepairOrder", ref _temporaryKingdomRepairOrder);
            dataStore.SyncData("BellumCivile_TemporaryKingdomRepairExpirations", ref _temporaryKingdomRepairExpirations);
            dataStore.SyncData("BellumCivile_TemporaryKingdomExpectedRulerIds", ref _temporaryKingdomExpectedRulerIds);
            dataStore.SyncData("BellumCivile_TemporaryKingdomRepairReasons", ref _temporaryKingdomRepairReasons);
            EnsureCollectionsInitialized();
            if (dataStore.IsLoading)
            {
                foreach (FactionObject faction in _activeFactions)
                    faction?.NormalizeCourtFactionLegacyState();
                foreach (var group in _activeFactions.Where(f => f != null && f.IsIdeology)
                    .GroupBy(f => new { f.ParentKingdom, f.Type }).ToList())
                {
                    FactionObject primary = group.First();
                    foreach (FactionObject duplicate in group.Skip(1).ToList())
                    {
                        foreach (Clan member in duplicate.Members.ToList())
                            if (!primary.Members.Contains(member)) primary.Members.Add(member);
                        _activeFactions.Remove(duplicate);
                    }
                }
            }
        }

        private void EnsureCollectionsInitialized()
        {
            if (_activeFactions == null) _activeFactions = new List<FactionObject>();
            if (_pacifiedClans == null)  _pacifiedClans  = new Dictionary<Clan, CampaignTime>();
            if (_settlementSyncStamps == null) _settlementSyncStamps = new Dictionary<Clan, CampaignTime>();
            if (_pendingSettlementRefreshClans == null) _pendingSettlementRefreshClans = new List<Clan>();
            if (_temporaryKingdomRepairOrder == null) _temporaryKingdomRepairOrder = new List<string>();
            if (_temporaryKingdomRepairExpirations == null) _temporaryKingdomRepairExpirations = new Dictionary<string, CampaignTime>();
            if (_temporaryKingdomExpectedRulerIds == null) _temporaryKingdomExpectedRulerIds = new Dictionary<string, string>();
            if (_temporaryKingdomRepairReasons == null) _temporaryKingdomRepairReasons = new Dictionary<string, string>();
            if (_lastKnownRulingClanIds == null) _lastKnownRulingClanIds = new Dictionary<string, string>();
            if (_rulerRepairSuppressionDepths == null) _rulerRepairSuppressionDepths = new Dictionary<string, int>();
            if (_rulerRepairFailureLogKeys == null) _rulerRepairFailureLogKeys = new HashSet<string>();
            if (_compatibilityRepairWindowUntil == CampaignTime.Zero)
                _compatibilityRepairWindowUntil = CampaignTime.Zero;
            InvalidateFactionLookupCache(invalidateRelationBaseline: false);
        }

        private void OnDailyTick()
        {
            InvalidateFactionLookupCache(invalidateRelationBaseline: false);
            AuditRulingClanTransitions("daily tick pre-repair audit");
            ReconcileTrackedRebelKingdoms();
            RepairLeaderlessBellumKingdoms();
            CivilWarResolutionBehavior resolutionBehavior = Campaign.Current.GetCampaignBehavior<CivilWarResolutionBehavior>();
            ClearTemporaryKingdomRepairContexts();
            DestroySupersededEmptyKingdomShells();
            resolutionBehavior?.ReconcileSatisfiedCivilWarDemands(_activeFactions.ToList());
            RepairEliminatedKingdomsWithLiveHoldings(resolutionBehavior);

            foreach (Kingdom destroyedParent in _activeFactions
                .Where(f => !f.IsIdeology && !f.IsChallengeStartupPending && f.ParentKingdom != null && f.ParentKingdom.IsEliminated)
                .Select(f => f.ParentKingdom)
                .Distinct()
                .ToList())
            {
                resolutionBehavior?.HandleDestroyedParentKingdom(destroyedParent);
            }

            foreach (Kingdom collapsedParent in _activeFactions
                .Where(f => !f.IsIdeology
                         && f.ParentKingdom != null
                         && !f.ParentKingdom.IsEliminated
                         && CountStrongholds(f.ParentKingdom) == 0
                         && TryGetLiveRebelKingdom(f, out Kingdom rebelKingdom)
                         && CountStrongholds(rebelKingdom) > 0)
                .Select(f => f.ParentKingdom)
                .Distinct()
                .ToList())
            {
                resolutionBehavior?.HandleCollapsedParentKingdom(collapsedParent);
            }

            foreach (FactionObject stalematedFaction in _activeFactions
                .Where(f => !f.IsIdeology
                         && f.ParentKingdom != null
                         && !f.ParentKingdom.IsEliminated)
                .ToList())
            {
                if (!TryGetLiveRebelKingdom(stalematedFaction, out Kingdom rebelKingdom)) continue;
                if (!rebelKingdom.IsAtWarWith(stalematedFaction.ParentKingdom)) continue;
                if (CountStrongholds(stalematedFaction.ParentKingdom) != 0 || CountStrongholds(rebelKingdom) != 0) continue;

                resolutionBehavior?.ResolveCollapsedCivilWarStalemate(stalematedFaction, rebelKingdom);
            }

            foreach (FactionObject collapsedRebelFaction in _activeFactions
                .Where(f => !f.IsIdeology
                         && f.ParentKingdom != null
                         && !f.ParentKingdom.IsEliminated
                         && CountStrongholds(f.ParentKingdom) > 0)
                .ToList())
            {
                if (!TryGetLiveRebelKingdom(collapsedRebelFaction, out Kingdom rebelKingdom)) continue;
                if (!rebelKingdom.IsAtWarWith(collapsedRebelFaction.ParentKingdom)) continue;
                if (CountStrongholds(rebelKingdom) > 0) continue;

                resolutionBehavior?.ResolveLiegeVictory(collapsedRebelFaction, rebelKingdom);
            }

            foreach (FactionObject externallyResolvedFaction in _activeFactions
                .Where(f => !f.IsIdeology
                         && f.ParentKingdom != null
                         && f.HasTrackedRebelKingdom
                         && !f.IsChallengeStartupPending
                         && f.GetRebelKingdom() == null)
                .ToList())
            {
                resolutionBehavior?.ResolveUnscriptedPeace(externallyResolvedFaction, null);
            }

            foreach (FactionObject peaceFaction in _activeFactions.Where(f => !f.IsIdeology).ToList())
            {
                if (!TryGetLiveRebelKingdom(peaceFaction, out Kingdom rebelKingdom)) continue;
                if (peaceFaction.ParentKingdom.IsEliminated) continue;
                if (rebelKingdom.IsAtWarWith(peaceFaction.ParentKingdom)) continue;

                resolutionBehavior?.ResolveUnscriptedPeace(peaceFaction, rebelKingdom);
            }

            bool compatibilityRepairActive = IsCompatibilityRepairWindowActive();
            if (compatibilityRepairActive)
                RepairBrokenKingdomState(repairEliminatedKingdomClans: true);
            ProcessPendingSettlementRefreshes();
            RepairCivilWarSettlementState();
            SanitizeIdeologyMemberships();

            foreach (FactionObject faction in _activeFactions.ToList())
                if (!CivilWarConflictBehavior.IsFactionTransferPending(faction)) faction.DailyTick();

            int removedFactions = _activeFactions.RemoveAll(f => !CivilWarConflictBehavior.IsFactionTransferPending(f) && (
                f.Members.Count == 0 ||
                f.Leader == null ||
                f.ParentKingdom == null ||
                (f.IsIdeology && f.ParentKingdom.IsEliminated) ||
                (!f.IsIdeology && !f.HasTrackedRebelKingdom && !f.IsChallengeStartupPending
                    && f.ParentKingdom?.RulingClan == f.Leader)));

            if (removedFactions > 0)
                InvalidateFactionLookupCache();

            AuditRulingClanTransitions("daily tick post-repair audit");
        }

        private void RepairLeaderlessBellumKingdoms()
        {
            foreach (Kingdom kingdom in Kingdom.All
                .Where(candidate => candidate != null
                    && !candidate.IsEliminated
                    && (candidate.RulingClan == null
                        || candidate.RulingClan.IsEliminated
                        || candidate.Leader == null
                        || candidate.Leader.IsDead))
                .ToList())
            {
                TryRepairRulingClanForDiplomacy(
                    kingdom,
                    "daily leaderless Bellum kingdom audit",
                    requireBellumReference: true);
            }
        }

        public void ActivateCompatibilityRepairWindow(int durationDays = CompatibilityRepairWindowDays)
        {
            if (durationDays <= 0)
                durationDays = CompatibilityRepairWindowDays;

            CampaignTime newExpiration = CampaignTime.Now + CampaignTime.Days(durationDays);
            if (_compatibilityRepairWindowUntil == CampaignTime.Zero || _compatibilityRepairWindowUntil.IsPast || newExpiration.ToDays > _compatibilityRepairWindowUntil.ToDays)
                _compatibilityRepairWindowUntil = newExpiration;
        }

        public void RunCompatibilityRepairPass(string reason = null)
        {
            RepairBrokenKingdomState(repairEliminatedKingdomClans: true);
            ProcessPendingSettlementRefreshes();

            if (!string.IsNullOrWhiteSpace(reason))
                BellumCivileLogger.Log($"Ran immediate compatibility repair pass: {reason}.");
        }

        private void OnKingdomDestroyed(Kingdom kingdom)
        {
            if (kingdom == null)
                return;

            CivilWarResolutionBehavior resolutionBehavior = Campaign.Current?.GetCampaignBehavior<CivilWarResolutionBehavior>();
            if (resolutionBehavior == null)
                return;

            if (_activeFactions.Any(f => !f.IsIdeology && f.ParentKingdom == kingdom))
                resolutionBehavior.HandleDestroyedParentKingdom(kingdom);

            if (HasLiveHoldingsInEliminatedKingdom(kingdom)
                && resolutionBehavior.TryRepairEliminatedKingdomWithLiveState(kingdom, "kingdom destroyed event"))
            {
                ActivateCompatibilityRepairWindow();
            }
        }

        private void RepairEliminatedKingdomsWithLiveHoldings(CivilWarResolutionBehavior resolutionBehavior)
        {
            if (resolutionBehavior == null)
                return;

            foreach (Kingdom kingdom in Kingdom.All
                .Where(k => k != null
                         && k.IsEliminated
                         && !IsRulerRepairSuppressed(k)
                         && !IsBellumCivileRebelKingdomShell(k)
                         && HasLiveHoldingsInEliminatedKingdom(k))
                .ToList())
            {
                if (resolutionBehavior.TryRepairEliminatedKingdomWithLiveState(kingdom, "daily eliminated kingdom restoration sweep"))
                    ActivateCompatibilityRepairWindow();
            }
        }

        private static bool HasLiveHoldingsInEliminatedKingdom(Kingdom kingdom)
        {
            if (kingdom == null || !kingdom.IsEliminated)
                return false;

            if (kingdom.Fiefs.Any(f => f != null && (f.IsTown || f.IsCastle)))
                return true;

            return Clan.All.Any(c => c != null
                                  && NobleClanEligibilityHelper.IsLiveNobleClan(c)
                                  && c.Kingdom == kingdom
                                  && c.Settlements.Any(s => s != null && (s.IsTown || s.IsCastle)));
        }

        private static bool IsBellumCivileRebelKingdomShell(Kingdom kingdom)
        {
            return kingdom != null
                && !string.IsNullOrEmpty(kingdom.StringId)
                && kingdom.StringId.Contains("_rebels_");
        }

        private void DestroySupersededEmptyKingdomShells()
        {
            foreach (Kingdom kingdom in Kingdom.All
                .Where(BellumKingdomVisibilityHelper.IsSupersededEmptyKingdomShell)
                .Where(k => !CivilWarConflictBehavior.IsRealmTransferPending(k))
                .Where(k => !IsRulerRepairSuppressed(k))
                .ToList())
            {
                try
                {
                    foreach (Clan mercenary in kingdom.Clans
                        .Where(c => c != null && !c.IsEliminated && c.IsUnderMercenaryService)
                        .ToList())
                    {
                        ChangeKingdomAction.ApplyByLeaveKingdomAsMercenary(mercenary, showNotification: false);
                    }

                    foreach (FactionObject faction in GetFactionsInKingdom(kingdom).ToList())
                        RemoveFaction(faction);

                    if (BellumKingdomVisibilityHelper.IsSupersededEmptyKingdomShell(kingdom))
                    {
                        DestroyKingdomAction.Apply(kingdom);
                        BellumCivileLogger.Log($"Destroyed superseded empty kingdom shell {kingdom.StringId} after Bellum successor cleanup.");
                    }
                }
                catch (System.Exception ex)
                {
                    BellumCivileLogger.Log($"Failed to destroy superseded empty kingdom shell {kingdom?.StringId ?? "unknown"}: {ex}");
                }
            }
        }

        public bool ValidateTransitionRulerOnce(Kingdom kingdom, Clan expectedRuler, string reason = null, bool allowFallback = false)
        {
            EnsureCollectionsInitialized();
            if (kingdom == null || kingdom.IsEliminated)
                return false;

            ClearTemporaryKingdomRepair(kingdom);

            Clan intendedRuler = IsValidRulingClanForKingdom(expectedRuler, kingdom)
                ? expectedRuler
                : null;

            if (intendedRuler == null && IsValidRulingClanForKingdom(kingdom.RulingClan, kingdom))
                return true;

            if (intendedRuler != null && kingdom.RulingClan == intendedRuler)
                return true;

            if (intendedRuler == null && kingdom.UnresolvedDecisions.OfType<TaleWorlds.CampaignSystem.Election.KingSelectionKingdomDecision>().Any())
                return false;

            Clan replacementClan = intendedRuler ?? ResolveTitleDeFactoRulerForRepair(kingdom);

            if (replacementClan == null && allowFallback)
            {
                replacementClan = kingdom.Clans
                    .Where(c => IsValidRulingClanForKingdom(c, kingdom))
                    .OrderByDescending(RebellionPowerHelper.CalculateClanPower)
                    .FirstOrDefault();
            }

            if (replacementClan == null)
            {
                BellumCivileLogger.Log($"Skipped one-shot transition ruler validation for {kingdom.StringId}; no valid intended ruler was available. reason={reason ?? "unknown"}; allow_fallback={allowFallback}; {RulerTransitionDebugHelper.BuildValidityDetails(kingdom, kingdom.RulingClan)}.");
                return false;
            }

            if (kingdom.RulingClan == Clan.PlayerClan && replacementClan != Clan.PlayerClan)
            {
                BellumCivileLogger.Log($"Refused one-shot transition ruler validation over player-ruled kingdom {kingdom.StringId}; reason={reason ?? "unknown"}; intended={replacementClan.StringId}; {RulerTransitionDebugHelper.BuildValidityDetails(kingdom, kingdom.RulingClan)}.");
                return false;
            }

            Clan previousRuler = kingdom.RulingClan;
            RulerTransitionDebugHelper.Report(
                "one-shot transition ruler validation",
                kingdom,
                previousRuler,
                replacementClan,
                $"reason={reason ?? "unknown"}; allow_fallback={allowFallback}; {RulerTransitionDebugHelper.BuildValidityDetails(kingdom, replacementClan)}",
                requestInGameDisplay: previousRuler == Clan.PlayerClan || replacementClan == Clan.PlayerClan);

            kingdom.RulingClan = replacementClan;
            Campaign.Current.GetCampaignBehavior<FeudalTitleBehavior>()
                ?.TrySetKingdomTitleRuler(kingdom, replacementClan, legalTransfer: false, reason: reason ?? "one-shot transition ruler validation");
            QueueSettlementRefresh(replacementClan);
            BellumCivileLogger.Log($"Validated transition ruler for {kingdom.StringId}; previous_ruler={previousRuler?.StringId ?? "null"}; new_ruler={replacementClan.StringId}; reason={reason ?? "unknown"}; allow_fallback={allowFallback}.");
            return true;
        }

        public void ClearTemporaryKingdomRepair(Kingdom kingdom)
        {
            ClearTemporaryKingdomRepair(kingdom?.StringId);
        }

        public void ClearTemporaryKingdomRepair(string kingdomId)
        {
            if (string.IsNullOrEmpty(kingdomId))
                return;

            _temporaryKingdomRepairOrder.Remove(kingdomId);
            _temporaryKingdomRepairExpirations.Remove(kingdomId);
            _temporaryKingdomExpectedRulerIds.Remove(kingdomId);
            _temporaryKingdomRepairReasons.Remove(kingdomId);
        }

        public void RunWithRulerRepairSuppressed(Kingdom kingdom, Action action)
        {
            if (action == null)
                return;

            string kingdomId = kingdom?.StringId;
            if (string.IsNullOrEmpty(kingdomId))
            {
                action();
                return;
            }

            BeginRulerRepairSuppression(kingdomId);
            try
            {
                action();
            }
            finally
            {
                EndRulerRepairSuppression(kingdomId);
            }
        }

        public T RunWithRulerRepairSuppressed<T>(Kingdom kingdom, Func<T> action)
        {
            if (action == null)
                return default(T);

            string kingdomId = kingdom?.StringId;
            if (string.IsNullOrEmpty(kingdomId))
                return action();

            BeginRulerRepairSuppression(kingdomId);
            try
            {
                return action();
            }
            finally
            {
                EndRulerRepairSuppression(kingdomId);
            }
        }

        private void BeginRulerRepairSuppression(string kingdomId)
        {
            EnsureCollectionsInitialized();
            _rulerRepairSuppressionDepths.TryGetValue(kingdomId, out int depth);
            _rulerRepairSuppressionDepths[kingdomId] = depth + 1;
        }

        private void EndRulerRepairSuppression(string kingdomId)
        {
            if (string.IsNullOrEmpty(kingdomId) || _rulerRepairSuppressionDepths == null)
                return;

            if (!_rulerRepairSuppressionDepths.TryGetValue(kingdomId, out int depth) || depth <= 1)
                _rulerRepairSuppressionDepths.Remove(kingdomId);
            else
                _rulerRepairSuppressionDepths[kingdomId] = depth - 1;
        }

        private bool IsRulerRepairSuppressed(Kingdom kingdom)
        {
            if (CrownAccessionBehavior.Instance?.IsRealmUnionProtected(kingdom) == true) return true;
            if (Campaign.Current?.GetCampaignBehavior<PartitionSuccessionBehavior>()?
                .IsCrownPromotionRealmProtected(kingdom) == true) return true;
            return kingdom != null
                && _rulerRepairSuppressionDepths != null
                && _rulerRepairSuppressionDepths.TryGetValue(kingdom.StringId, out int depth)
                && depth > 0;
        }

        private bool IsCompatibilityRepairWindowActive()
        {
            return _compatibilityRepairWindowUntil != CampaignTime.Zero && !_compatibilityRepairWindowUntil.IsPast;
        }

        private void RepairBrokenKingdomState(bool repairEliminatedKingdomClans)
        {
            if (repairEliminatedKingdomClans)
                RepairClansStuckInEliminatedKingdoms();
        }

        private void RepairClansStuckInEliminatedKingdoms()
        {
            ExiledClanRecoveryBehavior exileRecovery = Campaign.Current.GetCampaignBehavior<ExiledClanRecoveryBehavior>();

            foreach (Clan clan in Clan.All
                .Where(c => c != null
                         && !c.IsEliminated
                         && c.Kingdom != null
                         && c.Kingdom.IsEliminated)
                .ToList())
            {
                Kingdom eliminatedKingdom = clan.Kingdom;
                if (IsRulerRepairSuppressed(eliminatedKingdom)) continue;
                if (CivilWarConflictBehavior.IsRealmTransferPending(eliminatedKingdom)) continue;

                try
                {
                    if (clan.IsUnderMercenaryService)
                    {
                        ChangeKingdomAction.ApplyByLeaveKingdomAsMercenary(clan, showNotification: false);
                    }
                    else if (clan == Clan.PlayerClan)
                    {
                        ChangeKingdomAction.ApplyByLeaveKingdom(clan, false);
                    }
                    else if (clan.Settlements.Count == 0 && exileRecovery != null)
                    {
                        exileRecovery.ResolveClanExile(clan, eliminatedKingdom, eliminatedKingdom);
                    }
                    else
                    {
                        Kingdom refuge = RefugeSelectionHelper.FindBestRefuge(clan, eliminatedKingdom, eliminatedKingdom);
                        if (refuge != null)
                            ChangeKingdomAction.ApplyByJoinToKingdom(clan, refuge);
                        else
                            ChangeKingdomAction.ApplyByLeaveKingdom(clan, false);
                    }

                    QueueSettlementRefresh(clan);
                    BellumCivileLogger.Log($"Repaired clan {clan.StringId} lingering in eliminated kingdom {eliminatedKingdom?.StringId ?? "unknown"}.");
                }
                catch (System.Exception ex)
                {
                    BellumCivileLogger.Log($"Failed to repair clan {clan?.StringId ?? "unknown"} lingering in eliminated kingdom {eliminatedKingdom?.StringId ?? "unknown"}: {ex}");
                }
            }
        }

        private void ClearTemporaryKingdomRepairContexts()
        {
            foreach (string kingdomId in _temporaryKingdomRepairOrder.ToList())
                ClearTemporaryKingdomRepair(kingdomId);
        }

        private static bool IsValidRulingClanForKingdom(Clan clan, Kingdom kingdom)
        {
            return NobleClanEligibilityHelper.IsValidRulingClan(clan, kingdom);
        }

        private static Clan ResolveExpectedTemporaryRuler(Kingdom kingdom, string expectedRulerId)
        {
            if (kingdom == null || string.IsNullOrEmpty(expectedRulerId))
                return null;

            Clan expectedClan = Clan.All.FirstOrDefault(c => c != null && c.StringId == expectedRulerId);
            return IsValidRulingClanForKingdom(expectedClan, kingdom) ? expectedClan : null;
        }

        private void ReconcileTrackedRebelKingdoms()
        {
            foreach (FactionObject faction in _activeFactions.Where(f => !f.IsIdeology).ToList())
            {
                if (faction.IsChallengeStartupPending || CivilWarConflictBehavior.IsFactionTransferPending(faction)) continue;
                if (faction.TryValidateActiveRebellion(out _, out _))
                    continue;

                if (TryReconcileRebelKingdom(faction, out Kingdom rebelKingdom, out string failureReason))
                {
                    string repairKey = faction.ParentKingdom?.StringId + "|" + faction.Leader?.StringId + "|" + rebelKingdom?.StringId;
                    _rebelKingdomReconciliationFailureKeys.Remove(repairKey);
                    InvalidateFactionLookupCache();
                    BellumCivileLogger.Log(
                        $"Reconciled rebel kingdom transition; faction={faction.Name}; leader={faction.Leader?.StringId ?? "none"}; rebel={rebelKingdom?.StringId ?? "none"}; parent={faction.ParentKingdom?.StringId ?? "none"}.");
                }
                else if (!string.IsNullOrEmpty(failureReason))
                {
                    string candidateId = faction.GetRebelKingdom()?.StringId ?? faction.GetDeterministicRebelKingdomIdPrefix() ?? "none";
                    string repairKey = faction.ParentKingdom?.StringId + "|" + faction.Leader?.StringId + "|" + candidateId;
                    if (_rebelKingdomReconciliationFailureKeys.Add(repairKey))
                    {
                        BellumCivileLogger.Log(
                            $"Failed to reconcile rebel kingdom transition; faction={faction.Name}; leader={faction.Leader?.StringId ?? "none"}; rebel={candidateId}; parent={faction.ParentKingdom?.StringId ?? "none"}; reason={failureReason}.");
                    }
                }
            }
        }

        internal bool TryReconcileRebelKingdom(
            FactionObject faction,
            out Kingdom rebelKingdom,
            out string failureReason)
        {
            rebelKingdom = null;
            failureReason = null;
            if (faction == null || faction.IsIdeology || faction.IsChallengeStartupPending
                || CivilWarConflictBehavior.IsFactionTransferPending(faction))
                return false;

            if (faction.TryValidateActiveRebellion(out rebelKingdom, out _))
                return true;

            if (faction.TryCompletePartialRebellionCreation(out string partialFailure))
                return faction.TryValidateActiveRebellion(out rebelKingdom, out failureReason);

            if (!string.IsNullOrEmpty(partialFailure))
            {
                rebelKingdom = faction.GetRebelKingdom();
                failureReason = "partial rebellion recovery failed: " + partialFailure;
                return false;
            }

            if (faction.TryBackfillRebelKingdomFromLeaderKingdom()
                && faction.TryValidateActiveRebellion(out rebelKingdom, out _))
            {
                return true;
            }

            string deterministicPrefix = faction.GetDeterministicRebelKingdomIdPrefix();
            if (string.IsNullOrEmpty(deterministicPrefix) || faction.ParentKingdom == null)
                return false;

            Kingdom candidate = faction.GetRebelKingdom();
            if (!IsMatchingActiveRebelShell(candidate, faction.ParentKingdom, deterministicPrefix))
            {
                candidate = Kingdom.All
                    .Where(kingdom => IsMatchingActiveRebelShell(kingdom, faction.ParentKingdom, deterministicPrefix))
                    .OrderBy(kingdom => kingdom.StringId == deterministicPrefix ? 0 : 1)
                    .ThenBy(kingdom => kingdom.StringId)
                    .FirstOrDefault();
            }

            if (candidate == null)
                return false;

            rebelKingdom = candidate;
            bool claimedByAnotherFaction = _activeFactions.Any(other =>
                other != faction
                && !other.IsIdeology
                && other.IsTrackedRebelKingdom(candidate));
            if (claimedByAnotherFaction)
            {
                failureReason = "the deterministic rebel kingdom is already tracked by another faction";
                return false;
            }

            if (!faction.TryAdoptExistingRebelKingdom(candidate, out failureReason))
                return false;

            InvalidateFactionLookupCache();
            rebelKingdom = candidate;
            return true;
        }

        private static bool IsMatchingActiveRebelShell(Kingdom candidate, Kingdom parentKingdom, string deterministicPrefix)
        {
            if (candidate == null
                || candidate.IsEliminated
                || parentKingdom == null
                || candidate == parentKingdom
                || string.IsNullOrEmpty(deterministicPrefix))
            {
                return false;
            }

            bool idMatches = candidate.StringId == deterministicPrefix
                || candidate.StringId.StartsWith(deterministicPrefix + "_", StringComparison.Ordinal);
            return idMatches && candidate.IsAtWarWith(parentKingdom);
        }

        private void OnClanChangedKingdom(Clan clan, Kingdom oldKingdom, Kingdom newKingdom, ChangeKingdomAction.ChangeKingdomActionDetail detail, bool showNotification)
        {
            foreach (FactionObject faction in _activeFactions.ToList())
            {
                if (!faction.Members.Contains(clan)) continue;

                if (faction.IsIdeology)
                {
                    if (ShouldKeepIdeologyMembership(faction, oldKingdom, newKingdom))
                    {
                        continue;
                    }

                    faction.RemoveMember(clan);
                    if (faction.Members.Count == 0)
                    {
                        RemoveFaction(faction);
                    }

                    continue;
                }

                bool preserveQueuedLoyalistResolution = oldKingdom != null
                    && faction.IsTrackedRebelKingdom(oldKingdom)
                    && Campaign.Current?.GetCampaignBehavior<WarScoreBehavior>()?
                        .HasQueuedLoyalistCivilWarResolution(faction) == true;
                if (preserveQueuedLoyalistResolution)
                {
                    // Bannerlord can transfer the last rebel clans before dispatching the
                    // kingdom-destroyed callback. Keep the faction snapshot intact until the
                    // deferred loyalist resolver consumes its members and influence snapshots.
                    BellumCivileLogger.Log(
                        $"Preserved queued loyalist civil-war faction snapshot during clan transfer; faction={faction.Name}; clan={clan.StringId}; rebel={oldKingdom.StringId}; destination={newKingdom?.StringId ?? "none"}; detail={detail}.");
                    continue;
                }

                bool movingToRebel = newKingdom != null && faction.ParentKingdom != null &&
                                    (faction.IsTrackedRebelKingdom(newKingdom)
                                     || newKingdom.StringId.StartsWith(faction.ParentKingdom.StringId + "_rebels")
                                     || newKingdom.StringId.StartsWith(faction.ParentKingdom.StringId + "_indep"));

                bool returningFromRebel = oldKingdom != null && faction.ParentKingdom != null &&
                                          (faction.IsTrackedRebelKingdom(oldKingdom)
                                           || oldKingdom.StringId.StartsWith(faction.ParentKingdom.StringId + "_rebels")
                                           || oldKingdom.StringId.StartsWith(faction.ParentKingdom.StringId + "_indep")) &&
                                          newKingdom == faction.ParentKingdom;

                if (movingToRebel || returningFromRebel)
                {
                    continue;
                }

                faction.RemoveMember(clan);

                if (faction.Members.Count == 0)
                {
                    RemoveFaction(faction);
                }
            }

            if (newKingdom != null && NobleClanEligibilityHelper.IsLiveNobleClan(clan))
            {
                FactionObject activeRebellion = GetFactionByRebelKingdom(newKingdom);
                if (activeRebellion != null && !activeRebellion.Members.Contains(clan))
                {
                    activeRebellion.AddMember(clan);
                }
            }

            QueueSettlementRefresh(clan);
            RepairReferencedKingdomRulerState(oldKingdom);
            RepairReferencedKingdomRulerState(newKingdom);
        }

        private void RepairReferencedKingdomRulerState(Kingdom kingdom)
        {
            if (!ShouldAttemptReferencedKingdomRulerRepair(kingdom))
                return;

            TryRepairRulingClanForDiplomacy(kingdom, "clan kingdom transfer", requireBellumReference: true);
        }

        private bool ShouldAttemptReferencedKingdomRulerRepair(Kingdom kingdom)
        {
            if (kingdom == null || kingdom.IsEliminated)
                return false;

            if (IsValidRulingClanForKingdom(kingdom.RulingClan, kingdom)
                && ResolveTitleDeFactoRulerForRepair(kingdom) == kingdom.RulingClan)
                return false;

            if (IsRulerRepairSuppressed(kingdom))
                return false;

            CivilWarResolutionBehavior resolutionBehavior = Campaign.Current.GetCampaignBehavior<CivilWarResolutionBehavior>();
            bool bellumReferenced = resolutionBehavior != null && resolutionBehavior.IsKingdomIdReferencedByBellumCivileState(kingdom.StringId);
            if (!bellumReferenced)
                return false;

            return TryResolveIntendedRulerForRepair(kingdom, resolutionBehavior) != null;
        }

        public bool TryPrepareKingdomForDiplomacyScore(Kingdom kingdom, string reason = null)
        {
            if (kingdom == null || kingdom.IsEliminated)
                return false;

            if (IsValidRulingClanForKingdom(kingdom.RulingClan, kingdom)
                && (ResolveTitleDeFactoRulerForRepair(kingdom) == null || ResolveTitleDeFactoRulerForRepair(kingdom) == kingdom.RulingClan))
                return true;

            if (IsRulerRepairSuppressed(kingdom))
                return false;

            CivilWarResolutionBehavior resolutionBehavior = Campaign.Current.GetCampaignBehavior<CivilWarResolutionBehavior>();
            bool bellumReferenced = resolutionBehavior != null && resolutionBehavior.IsKingdomIdReferencedByBellumCivileState(kingdom.StringId);
            if (!bellumReferenced)
            {
                BellumCivileLogger.Log($"Refused diplomacy-score ruler repair for non-Bellum kingdom {kingdom.StringId}; reason={reason ?? "unknown"}; {RulerTransitionDebugHelper.BuildValidityDetails(kingdom, kingdom.RulingClan)}.");
                return false;
            }

            return TryRepairRulingClanForDiplomacy(kingdom, reason, requireBellumReference: true);
        }

        public bool TryRepairRulingClanForDiplomacy(Kingdom kingdom, string reason = null, bool requireBellumReference = true)
        {
            if (CivilWarConflictBehavior.IsRealmTransferPending(kingdom)) return false;
            if (kingdom == null || kingdom.IsEliminated)
                return false;

            if (IsValidRulingClanForKingdom(kingdom.RulingClan, kingdom)
                && (ResolveTitleDeFactoRulerForRepair(kingdom) == null || ResolveTitleDeFactoRulerForRepair(kingdom) == kingdom.RulingClan))
                return true;

            if (IsRulerRepairSuppressed(kingdom))
                return false;

            if (kingdom.UnresolvedDecisions.OfType<TaleWorlds.CampaignSystem.Election.KingSelectionKingdomDecision>().Any())
                return false;

            CivilWarResolutionBehavior resolutionBehavior = Campaign.Current.GetCampaignBehavior<CivilWarResolutionBehavior>();
            bool bellumReferenced = resolutionBehavior != null && resolutionBehavior.IsKingdomIdReferencedByBellumCivileState(kingdom.StringId);
            if (requireBellumReference && !bellumReferenced)
                return false;

            Clan replacementClan = bellumReferenced
                ? TryResolveIntendedRulerForRepair(kingdom, resolutionBehavior)
                : null;

            replacementClan = replacementClan ?? ResolveTitleDeFactoRulerForRepair(kingdom);

            if (replacementClan == null)
            {
                if (ShouldLogMissingRulerRepairCandidate(kingdom, reason, bellumReferenced))
                    BellumCivileLogger.Log($"Unable to emergency-repair kingdom {kingdom.StringId}; no valid intended replacement ruling clan was found. reason={reason ?? "unknown"} referenced_by_bc={bellumReferenced}.");
                return false;
            }

            if (kingdom.RulingClan == Clan.PlayerClan
                && (resolutionBehavior == null || !resolutionBehavior.IsPlayerRulerReplacementAuthorized(kingdom, replacementClan)))
            {
                BellumCivileLogger.Log($"Refused emergency ruler repair over player-ruled kingdom {kingdom.StringId}; reason={reason ?? "unknown"} referenced_by_bc={bellumReferenced}; intended={replacementClan.StringId}; {RulerTransitionDebugHelper.BuildValidityDetails(kingdom, kingdom.RulingClan)}.");
                return false;
            }

            Clan previousRuler = kingdom.RulingClan;
            string previousRulerId = previousRuler?.StringId ?? "null";
            RulerTransitionDebugHelper.Report(
                "referenced kingdom ruler repair",
                kingdom,
                previousRuler,
                replacementClan,
                $"reason={reason ?? "unknown"}; referenced_by_bc={bellumReferenced}; {RulerTransitionDebugHelper.BuildValidityDetails(kingdom, replacementClan)}",
                requestInGameDisplay: previousRuler == Clan.PlayerClan || replacementClan == Clan.PlayerClan);

            kingdom.RulingClan = replacementClan;
            Campaign.Current.GetCampaignBehavior<FeudalTitleBehavior>()
                ?.TrySetKingdomTitleRuler(kingdom, replacementClan, legalTransfer: false, reason: reason ?? "referenced kingdom ruler repair");
            QueueSettlementRefresh(replacementClan);
            BellumCivileLogger.Log($"Emergency-repaired kingdom {kingdom.StringId}; previous_ruler={previousRulerId}; new_ruler={replacementClan.StringId}; reason={reason ?? "unknown"} referenced_by_bc={bellumReferenced}.");
            return true;
        }

        private Clan TryResolveIntendedRulerForRepair(Kingdom kingdom, CivilWarResolutionBehavior resolutionBehavior)
        {
            if (kingdom == null)
                return null;

            Clan replacementClan = resolutionBehavior?.GetExpectedRulerForReferencedKingdom(kingdom.StringId);
            if (IsValidRulingClanForKingdom(replacementClan, kingdom))
                return replacementClan;

            FactionObject activeRebelFaction = GetFactionByRebelKingdom(kingdom);
            if (activeRebelFaction != null
                && activeRebelFaction.IsTrackedRebelKingdom(kingdom)
                && activeRebelFaction.IsCivilWarActive()
                && IsValidRulingClanForKingdom(activeRebelFaction.Leader, kingdom))
            {
                return activeRebelFaction.Leader;
            }

            return null;
        }

        private static Clan ResolveTitleDeFactoRulerForRepair(Kingdom kingdom)
        {
            FeudalTitleBehavior titleBehavior = Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>();
            Clan titleRuler = titleBehavior?.GetDeFactoSovereignClan(kingdom);
            return IsValidRulingClanForKingdom(titleRuler, kingdom) ? titleRuler : null;
        }

        private bool ShouldLogMissingRulerRepairCandidate(Kingdom kingdom, string reason, bool bellumReferenced)
        {
            if (kingdom == null)
                return false;

            if (IsBellumCivileRebelKingdomShell(kingdom))
            {
                FactionObject activeRebelFaction = GetFactionByRebelKingdom(kingdom);
                if (activeRebelFaction == null || !activeRebelFaction.IsCivilWarActive())
                    return false;
            }

            return ShouldLogRulerRepairFailure(kingdom, reason, bellumReferenced);
        }

        private bool ShouldLogRulerRepairFailure(Kingdom kingdom, string reason, bool bellumReferenced)
        {
            EnsureCollectionsInitialized();
            if (kingdom == null)
                return false;

            int day = (int)CampaignTime.Now.ToDays;
            string key = $"{kingdom.StringId}|{reason ?? "unknown"}|{bellumReferenced}|{day}";
            return _rulerRepairFailureLogKeys.Add(key);
        }

        private static bool IsCivilWarRebelKingdom(Kingdom kingdom, Kingdom parentKingdom)
        {
            return kingdom != null
                && parentKingdom != null
                && kingdom.StringId.StartsWith(parentKingdom.StringId + "_rebels");
        }

        private bool ShouldKeepIdeologyMembership(FactionObject faction, Kingdom oldKingdom, Kingdom newKingdom)
        {
            if (!faction.IsIdeology || faction.ParentKingdom == null) return false;
            if (newKingdom == faction.ParentKingdom) return true;

            bool movingIntoTrackedCivilWar = newKingdom != null
                && GetFactionByRebelKingdom(newKingdom)?.ParentKingdom == faction.ParentKingdom;
            bool returningFromTrackedCivilWar = oldKingdom != null
                && GetFactionByRebelKingdom(oldKingdom)?.ParentKingdom == faction.ParentKingdom
                && newKingdom == faction.ParentKingdom;

            bool movingIntoActiveCivilWar = IsCivilWarRebelKingdom(newKingdom, faction.ParentKingdom);
            bool returningFromActiveCivilWar = IsCivilWarRebelKingdom(oldKingdom, faction.ParentKingdom) && newKingdom == faction.ParentKingdom;
            return movingIntoTrackedCivilWar || returningFromTrackedCivilWar || movingIntoActiveCivilWar || returningFromActiveCivilWar;
        }

        private void OnClanDestroyed(Clan clan)
        {
            _settlementSyncStamps.Remove(clan);
            _pendingSettlementRefreshClans.Remove(clan);

            foreach (FactionObject faction in _activeFactions.ToList())
            {
                if (faction.Members.Contains(clan))
                {
                    bool preserveQueuedLoyalistResolution = !faction.IsIdeology
                        && Campaign.Current?.GetCampaignBehavior<WarScoreBehavior>()?
                            .HasQueuedLoyalistCivilWarResolution(faction) == true;
                    if (!preserveQueuedLoyalistResolution)
                        faction.RemoveMember(clan);
                    else
                        BellumCivileLogger.Log(
                            $"Preserved queued loyalist civil-war faction snapshot during clan destruction; faction={faction.Name}; clan={clan.StringId}.");

                    if (!preserveQueuedLoyalistResolution && faction.Members.Count == 0)
                    {
                        RemoveFaction(faction);
                        continue;
                    }
                }

                faction.HandleClanDestroyed(clan);
            }
        }

        // Clans can retain their fiefs when defecting between the loyalist and rebel sides.
        // Replaying a no-op ownership change refreshes settlement-side state for the new kingdom.
        private void QueueSettlementRefresh(Clan clan)
        {
            if (!NobleClanEligibilityHelper.IsLiveNobleClan(clan)) return;
            if (_pendingSettlementRefreshClans.Contains(clan)) return;
            _pendingSettlementRefreshClans.Add(clan);
        }

        private void ProcessPendingSettlementRefreshes()
        {
            foreach (Clan clan in _pendingSettlementRefreshClans.ToList())
            {
                if (TryRefreshTransferredSettlements(clan))
                {
                    _pendingSettlementRefreshClans.Remove(clan);
                }
            }
        }

        private bool TryRefreshTransferredSettlements(Clan clan)
        {
            if (!NobleClanEligibilityHelper.IsLiveNobleClan(clan)) return true;

            CampaignTime lastFactionChange = clan.LastFactionChangeTime;
            if (_settlementSyncStamps.TryGetValue(clan, out CampaignTime syncedAt) && syncedAt == lastFactionChange)
                return true;

            if (clan.Fiefs.Count == 0)
            {
                _settlementSyncStamps[clan] = lastFactionChange;
                return true;
            }

            bool completed = true;
            foreach (Town town in clan.Fiefs.ToList())
            {
                Settlement settlement = town?.Settlement;
                Hero owner = town?.OwnerClan?.Leader ?? settlement?.Owner ?? clan.Leader;
                if (settlement == null || owner == null) continue;

                bool needsRefresh = settlement.MapFaction != clan.Kingdom || town.OwnerClan != clan;
                if (!needsRefresh) continue;

                Hero priorGovernor = town.Governor;
                try
                {
                    ChangeOwnerOfSettlementAction.ApplyByGift(settlement, owner);

                    if (priorGovernor != null
                        && !priorGovernor.IsDead
                        && priorGovernor.Clan == town.OwnerClan
                        && town.Governor != priorGovernor)
                    {
                        ChangeGovernorAction.Apply(town, priorGovernor);
                    }
                }
                catch (System.Exception ex)
                {
                    completed = false;
                    BellumCivileLogger.Log($"Delaying settlement sync for {clan?.StringId ?? "unknown"} / {settlement?.StringId ?? "unknown"} after kingdom change: {ex}");
                }
            }

            if (completed)
            {
                _settlementSyncStamps[clan] = lastFactionChange;
            }

            return completed;
        }

        private void RepairCivilWarSettlementState()
        {
            HashSet<Clan> clansToCheck = new HashSet<Clan>();

            foreach (FactionObject faction in _activeFactions.Where(f => !f.IsIdeology))
            {
                if (!TryGetLiveRebelKingdom(faction, out Kingdom rebelKingdom)) continue;
                if (!rebelKingdom.IsAtWarWith(faction.ParentKingdom)) continue;

                foreach (Clan rebelClan in rebelKingdom.Clans)
                    clansToCheck.Add(rebelClan);
            }

            foreach (Clan clan in clansToCheck)
                QueueSettlementRefresh(clan);
        }

        private void SanitizeIdeologyMemberships()
        {
            foreach (FactionObject faction in _activeFactions.Where(f => f.IsIdeology).ToList())
            {
                HashSet<Kingdom> validRebelKingdoms = new HashSet<Kingdom>(_activeFactions
                    .Where(f => !f.IsIdeology && f.ParentKingdom == faction.ParentKingdom && f.IsCivilWarActive())
                    .Select(f => f.GetRebelKingdom())
                    .Where(k => k != null));

                foreach (Clan member in faction.Members.ToList())
                {
                    bool isValidMember = member != null
                        && !member.IsEliminated
                        && (
                            member.Kingdom == faction.ParentKingdom
                            || validRebelKingdoms.Contains(member.Kingdom)
                            || IsCivilWarRebelKingdom(member.Kingdom, faction.ParentKingdom)
                        );

                    if (isValidMember) continue;

                    faction.RemoveMember(member);
                }

                if (faction.Members.Count == 0)
                {
                    RemoveFaction(faction);
                }
            }
        }

        public void DetachForeignIdeologiesFromKingdom(Kingdom kingdom)
        {
            if (kingdom == null) return;

            foreach (FactionObject faction in _activeFactions.Where(f => f.IsIdeology && f.ParentKingdom != kingdom).ToList())
            {
                foreach (Clan member in faction.Members.Where(c => c?.Kingdom == kingdom).ToList())
                {
                    faction.RemoveMember(member);
                }

                if (faction.Members.Count == 0)
                {
                    RemoveFaction(faction);
                }
            }
        }

        private void OnRulingClanChanged(Kingdom kingdom, Clan newRulingClan)
        {
            ReportRulingClanChangedEvent(kingdom, newRulingClan);
            CourtAgendaBehavior.Current?.ReconcileCrown(kingdom);

            foreach (FactionObject faction in _activeFactions.ToList())
            {
                if (faction.ParentKingdom == kingdom)
                {
                    if (faction.IsIdeology) continue;

                    // Existing wars keep their pledges and war state. Resolve satisfied
                    // demands after accession completes, outside this synchronous callback.
                    if (faction.HasTrackedRebelKingdom || faction.IsChallengeStartupPending || faction.IsCivilWarActive()) continue;

                    if (faction.Type == FactionType.Abdication)
                    {
                        // Hereditary demands target a person. The accession coordinator
                        // settles their war only after the lawful transfer is complete.
                        if (faction.IsHereditaryAbdication) continue;
                        RemoveFaction(faction);
                        NotificationHelper.ShowFactionDisbanded(faction, new TextObject("{=BC_FactionManager_DisbandReason_OldRuler}the old ruler is no longer in power").ToString());
                        continue;
                    }

                    if (faction.Leader == newRulingClan)
                    {
                        RemoveFaction(faction);
                        NotificationHelper.ShowFactionDisbanded(faction, new TextObject("{=BC_FactionManager_DisbandReason_LeaderAscended}their leader ascended to the throne").ToString());
                        continue;
                    }
                    else if (faction.Members.Contains(newRulingClan))
                    {
                        faction.RemoveMember(newRulingClan);
                        NotificationHelper.ShowClanLeftFaction(newRulingClan, faction, new TextObject("{=BC_FactionManager_LeftReason_Ascended}they ascended to the throne").ToString());
                    }

                    if (faction.Type == FactionType.InstallRuler)
                    {
                        float factionPower = faction.CalculateFactionPower();
                        float loyalistPower = faction.CalculateLoyalistPower();

                        float dynamicThreshold = 0.8f;
                        Hero factionLeader = faction.Leader?.Leader;

                        if (factionLeader != null)
                        {
                            int calculating = factionLeader.GetTraitLevel(DefaultTraits.Calculating);
                            if (calculating <= -2) dynamicThreshold -= 0.20f;
                            else if (calculating == -1) dynamicThreshold -= 0.10f;
                            else if (calculating == 1) dynamicThreshold += 0.10f;
                            else if (calculating >= 2) dynamicThreshold += 0.20f;

                            int valor = factionLeader.GetTraitLevel(DefaultTraits.Valor);
                            if (valor >= 2) dynamicThreshold -= 0.20f;
                            else if (valor == 1) dynamicThreshold -= 0.10f;
                            else if (valor == -1) dynamicThreshold += 0.10f;
                            else if (valor <= -2) dynamicThreshold += 0.20f;
                        }

                        if (dynamicThreshold < 0.40f) dynamicThreshold = 0.40f;

                        if (factionPower >= loyalistPower * dynamicThreshold)
                        {
                            if (faction.Leader == Clan.PlayerClan)
                            {
                                NotificationHelper.ShowPlayerSuccessionOpportunity(() => faction.TriggerUltimatum());
                            }
                            else
                            {
                                TextObject text = new TextObject("{=BC_FactionManager_SuccessionChaos}Seizing the chaos of succession, the {FACTION_NAME} has issued their demands!");
                                text.SetTextVariable("FACTION_NAME", NotificationHelper.GetFactionDisplayName(faction));
                                BellumCivileNotifications.Show(text, BellumNotificationColors.Rebellion, primaryKingdom: kingdom, primaryClan: faction.Leader);
                                faction.TriggerUltimatum();
                            }
                        }
                    }
                }
            }
        }

        public void ApplyPacifiedCooldown(Clan clan, int days) => _pacifiedClans[clan] = CampaignTime.Now + CampaignTime.Days(days);
        public bool IsClanPacified(Clan clan)
        {
            if (_pacifiedClans.TryGetValue(clan, out CampaignTime expirationTime))
            {
                if (expirationTime.IsFuture) return true;
                _pacifiedClans.Remove(clan);
            }
            return false;
        }

        public FactionObject GetRebelFaction(Clan clan) => _activeFactions.FirstOrDefault(f => f.Members.Contains(clan) && !f.IsIdeology);
        public FactionObject GetIdeologicalFaction(Clan clan)
        {
            if (clan == null || CourtMembershipEligibility.IsRuler(clan))
                return null;

            EnsureFactionLookupCache();
            return _ideologyByClan.TryGetValue(clan, out FactionObject faction) ? faction : null;
        }

        public void InvalidateFactionLookupCache(bool invalidateRelationBaseline = true)
        {
            _factionLookupCacheDirty = true;
            if (invalidateRelationBaseline)
                Campaign.Current?.GetCampaignBehavior<DynamicRelationBehavior>()?.InvalidateBaselineCache();
        }

        private void EnsureFactionLookupCache()
        {
            if (!_factionLookupCacheDirty)
                return;

            _ideologyByClan.Clear();
            _activeCivilWarByClan.Clear();
            foreach (FactionObject faction in _activeFactions)
            {
                if (faction == null)
                    continue;

                if (faction.IsIdeology)
                {
                    foreach (Clan member in faction.Members)
                    {
                        if (member != null && !member.IsEliminated)
                            _ideologyByClan[member] = faction;
                    }
                    continue;
                }

                if (!TryGetLiveRebelKingdom(faction, out Kingdom rebelKingdom))
                    continue;

                ActiveCivilWarMembership membership = new ActiveCivilWarMembership
                {
                    Faction = faction,
                    RebelKingdom = rebelKingdom
                };

                foreach (Clan member in faction.Members)
                {
                    if (member != null && !member.IsEliminated && !_activeCivilWarByClan.ContainsKey(member))
                        _activeCivilWarByClan[member] = membership;
                }

                foreach (Clan rebelClan in rebelKingdom.Clans)
                {
                    if (rebelClan != null && !rebelClan.IsEliminated && !_activeCivilWarByClan.ContainsKey(rebelClan))
                        _activeCivilWarByClan[rebelClan] = membership;
                }
            }

            _factionLookupCacheDirty = false;
        }

        public bool IsClanOnActiveCivilWarRebelSide(Clan clan, out FactionObject activeFaction, out Kingdom rebelKingdom)
        {
            activeFaction = null;
            rebelKingdom = null;

            if (!NobleClanEligibilityHelper.IsLiveNobleClan(clan))
                return false;

            EnsureFactionLookupCache();
            if (!_activeCivilWarByClan.TryGetValue(clan, out ActiveCivilWarMembership membership)
                || membership?.Faction?.ParentKingdom == null
                || membership.RebelKingdom == null
                || !membership.RebelKingdom.IsAtWarWith(membership.Faction.ParentKingdom))
            {
                return false;
            }

            activeFaction = membership.Faction;
            rebelKingdom = membership.RebelKingdom;
            return true;
        }

        public bool ShouldBlockExternalCivilWarRecruitment(Clan clan, Kingdom targetKingdom)
        {
            if (targetKingdom == null)
                return false;

            if (!IsClanOnActiveCivilWarRebelSide(clan, out FactionObject activeFaction, out Kingdom rebelKingdom))
                return false;

            return targetKingdom != rebelKingdom && targetKingdom != activeFaction.ParentKingdom;
        }

        public FactionObject GetFactionByRebelKingdom(Kingdom rebelKingdom)
        {
            if (rebelKingdom == null || rebelKingdom.IsEliminated) return null;

            FactionObject trackedMatch = _activeFactions.FirstOrDefault(f =>
                !f.IsIdeology
                && f.ParentKingdom != null
                && f.ParentKingdom != rebelKingdom
                && f.IsTrackedRebelKingdom(rebelKingdom));

            if (trackedMatch != null) return trackedMatch;

            return _activeFactions.FirstOrDefault(f =>
                !f.IsIdeology
                && !f.HasTrackedRebelKingdom
                && f.Leader != null
                && f.ParentKingdom != null
                && f.ParentKingdom != rebelKingdom
                && f.Leader.Kingdom == rebelKingdom
                && rebelKingdom.IsAtWarWith(f.ParentKingdom));
        }

        public FactionObject GetFactionByTrackedRebelKingdomId(string rebelKingdomId)
        {
            if (string.IsNullOrEmpty(rebelKingdomId)) return null;

            return _activeFactions.FirstOrDefault(f =>
                f != null
                && !f.IsIdeology
                && f.ParentKingdom != null
                && f.IsTrackedRebelKingdomId(rebelKingdomId));
        }

        public List<FactionObject> GetFactionsInKingdom(Kingdom kingdom) => _activeFactions.Where(f => f.ParentKingdom == kingdom).ToList();
        public void RegisterNewFaction(FactionObject newFaction)
        {
            if (newFaction == null || newFaction.Type == FactionType.Royalists) return;
            if (newFaction.IsIdeology && !CourtMembershipEligibility.CanBelong(newFaction.Leader, newFaction.ParentKingdom)) return;
            if (_activeFactions.Contains(newFaction)) return;

            _activeFactions.Add(newFaction);
            InvalidateFactionLookupCache();
            Campaign.Current.GetCampaignBehavior<RebellionSummaryBehavior>()?.RecordFactionFormed(newFaction);
        }
        public void RemoveFaction(FactionObject faction)
        {
            if (CivilWarConflictBehavior.IsFactionTransferPending(faction)) return;
            if (faction == null || !_activeFactions.Contains(faction)) return;

            if (!faction.IsIdeology)
            {
                faction.ClearRebelKingdom();
            }

            _activeFactions.Remove(faction);
            InvalidateFactionLookupCache();
        }

        private void OnWeeklyTick()
        {
            ApplyKingsFury();
        }

        private void ApplyKingsFury()
        {
            var kingdomsWithRebellions = new Dictionary<Kingdom, int>();
            foreach (var faction in _activeFactions)
            {
                if (!faction.IsIdeology && TryGetLiveRebelKingdom(faction, out Kingdom rebelKingdom) && faction.ParentKingdom != null && rebelKingdom.IsAtWarWith(faction.ParentKingdom))
                {
                    if (!kingdomsWithRebellions.ContainsKey(faction.ParentKingdom))
                    {
                        kingdomsWithRebellions[faction.ParentKingdom] = 0;
                    }
                    kingdomsWithRebellions[faction.ParentKingdom]++;
                }
            }

            foreach (var kvp in kingdomsWithRebellions)
            {
                Kingdom kingdom = kvp.Key;
                int numRebellions = kvp.Value;

                if (numRebellions > 0)
                {
                    Hero ruler = kingdom.RulingClan.Leader;
                    if (ruler == null) continue;

                    float paranoiaMultiplier = 1.0f;

                    int calcLevel = ruler.GetTraitLevel(DefaultTraits.Calculating);
                    if (calcLevel < 0) paranoiaMultiplier += C.KingsFuryHotheadAdd; 
                    else if (calcLevel > 0) paranoiaMultiplier -= C.KingsFuryCalcReduce; 

                    int mercyLevel = ruler.GetTraitLevel(DefaultTraits.Mercy);
                    if (mercyLevel > 0) paranoiaMultiplier -= C.KingsFuryMercifulReduce; 
                    else if (mercyLevel < 0) paranoiaMultiplier += C.KingsFuryCruelAdd; 

                    if (numRebellions > 1) paranoiaMultiplier += C.KingsFuryMultiRebellionAdd; 

                    paranoiaMultiplier = MathF.Clamp(paranoiaMultiplier, C.KingsFuryParanoiaMin, C.KingsFuryParanoiaMax);

                    foreach (Clan clan in kingdom.Clans.Where(c => c != kingdom.RulingClan && NobleClanEligibilityHelper.IsLiveNobleClan(c)))
                    {
                        if (clan.Leader == null) continue;

                        FactionObject ideology = GetIdeologicalFaction(clan);
                        if (ideology == null) continue;

                        int relationChange = 0;
                        string playerMsg = "";
                        Color msgColor = BellumNotificationColors.Neutral;

                        if (ideology.Mood > C.MoodThresholdHappy)
                        {
                            relationChange = C.KingsFuryLoyalistBonus;
                            TextObject text = new TextObject("{=BC_FactionManager_FuryLoyal}{RULER_NAME} recognizes your loyalty during the ongoing civil war. (+{RELATION_CHANGE} Relation)");
                            text.SetTextVariable("RULER_NAME", ruler.Name);
                            text.SetTextVariable("RELATION_CHANGE", C.KingsFuryLoyalistBonus);
                            playerMsg = text.ToString();
                            msgColor = BellumNotificationColors.Success;
                        }
                        else if (ideology.Mood >= C.MoodThresholdUnhappy && ideology.Mood <= C.MoodThresholdHappy)
                        {
                            relationChange = MathF.Round(-C.KingsFuryNeutralBase * paranoiaMultiplier);
                            TextObject text = new TextObject("{=BC_FactionManager_FuryNeutral}{RULER_NAME} sends angry letters demanding you fulfill your feudal contract and fight the rebels! ({RELATION_CHANGE} Relation)");
                            text.SetTextVariable("RULER_NAME", ruler.Name);
                            text.SetTextVariable("RELATION_CHANGE", relationChange);
                            playerMsg = text.ToString();
                            msgColor = BellumNotificationColors.Warning;
                        }
                        else if (ideology.Mood < C.MoodThresholdUnhappy)
                        {
                            relationChange = MathF.Round(-C.KingsFuryTraitorBase * paranoiaMultiplier);
                            TextObject text = new TextObject("{=BC_FactionManager_FuryTraitor}{RULER_NAME} considers your apathy during this civil war to be borderline treason. ({RELATION_CHANGE} Relation)");
                            text.SetTextVariable("RULER_NAME", ruler.Name);
                            text.SetTextVariable("RELATION_CHANGE", relationChange);
                            playerMsg = text.ToString();
                            msgColor = BellumNotificationColors.Danger;
                        }

                        if (relationChange != 0)
                        {
                            ChangeRelationAction.ApplyRelationChangeBetweenHeroes(clan.Leader, ruler, relationChange, false);

                            if (clan == Clan.PlayerClan)
                            {
                                BellumCivileNotifications.ShowPersonal(playerMsg, msgColor);
                            }
                        }
                    }
                }
            }
        }
        
        private void OnMakePeace(IFaction faction1, IFaction faction2, MakePeaceAction.MakePeaceDetail detail)
        {
            if (CivilWarResolutionBehavior.IsPeaceHandlingSuppressed) return;

            CivilWarResolutionBehavior resolutionBehavior = Campaign.Current.GetCampaignBehavior<CivilWarResolutionBehavior>();
            if (resolutionBehavior == null) return;

            foreach (FactionObject faction in _activeFactions.Where(f => !f.IsIdeology).ToList())
            {
                Kingdom rebelKingdom = faction.GetRebelKingdom();
                if (rebelKingdom == null || rebelKingdom == faction.ParentKingdom) continue;

                if ((rebelKingdom == faction1 && faction.ParentKingdom == faction2) ||
                    (rebelKingdom == faction2 && faction.ParentKingdom == faction1))
                {
                    resolutionBehavior.ResolveUnscriptedPeace(faction, rebelKingdom);
                }
            }
        }

        private void ReportRulingClanChangedEvent(Kingdom kingdom, Clan newRulingClan)
        {
            EnsureCollectionsInitialized();
            if (kingdom == null)
                return;

            Clan previousRuler = null;
            if (_lastKnownRulingClanIds.TryGetValue(kingdom.StringId, out string previousRulerId))
                previousRuler = Clan.All.FirstOrDefault(c => c != null && c.StringId == previousRulerId);

            if (previousRuler != newRulingClan)
            {
                string details = previousRuler == null && !string.IsNullOrEmpty(previousRulerId)
                    ? $"event fired; previous ruler id {previousRulerId} could not be resolved."
                    : "event fired by game.";
                RulerTransitionDebugHelper.Report(
                    "RulingClanChanged event",
                    kingdom,
                    previousRuler,
                    newRulingClan,
                    details,
                    requestInGameDisplay: previousRuler == Clan.PlayerClan || newRulingClan == Clan.PlayerClan);
            }

            _lastKnownRulingClanIds[kingdom.StringId] = newRulingClan?.StringId ?? string.Empty;
        }

        private void AuditRulingClanTransitions(string source)
        {
            EnsureCollectionsInitialized();

            foreach (Kingdom kingdom in Kingdom.All.Where(k => k != null).ToList())
            {
                string currentRulerId = kingdom.RulingClan?.StringId ?? string.Empty;
                if (!_lastKnownRulingClanIds.TryGetValue(kingdom.StringId, out string previousRulerId))
                {
                    _lastKnownRulingClanIds[kingdom.StringId] = currentRulerId;
                    continue;
                }

                if (previousRulerId == currentRulerId)
                {
                    TryCorrectRulerFromTitleMantle(kingdom, source);
                    continue;
                }

                Clan previousRuler = Clan.All.FirstOrDefault(c => c != null && c.StringId == previousRulerId);
                RulerTransitionDebugHelper.Report(
                    source,
                    kingdom,
                    previousRuler,
                    kingdom.RulingClan,
                    "snapshot detected a ruler change that did not pass through a Bellum direct assignment log.",
                    requestInGameDisplay: previousRuler == Clan.PlayerClan || kingdom.RulingClan == Clan.PlayerClan);

                _lastKnownRulingClanIds[kingdom.StringId] = currentRulerId;
                if (TryCorrectRulerFromTitleMantle(kingdom, source))
                    _lastKnownRulingClanIds[kingdom.StringId] = kingdom.RulingClan?.StringId ?? string.Empty;
            }
        }

        private bool TryCorrectRulerFromTitleMantle(Kingdom kingdom, string source)
        {
            if (kingdom == null || kingdom.IsEliminated || IsRulerRepairSuppressed(kingdom))
                return false;

            if (kingdom.UnresolvedDecisions.OfType<TaleWorlds.CampaignSystem.Election.KingSelectionKingdomDecision>().Any())
                return false;

            Clan titleRuler = ResolveTitleDeFactoRulerForRepair(kingdom);
            if (titleRuler == null || kingdom.RulingClan == titleRuler)
                return false;

            if (kingdom.RulingClan == Clan.PlayerClan && titleRuler != Clan.PlayerClan)
            {
                BellumCivileLogger.Log($"Refused title-mantle ruler correction over player-ruled kingdom {kingdom.StringId}; source={source ?? "unknown"}; title_ruler={titleRuler.StringId}; {RulerTransitionDebugHelper.BuildValidityDetails(kingdom, kingdom.RulingClan)}.");
                return false;
            }

            Clan previousRuler = kingdom.RulingClan;
            RulerTransitionDebugHelper.Report(
                "title-mantle ruler correction",
                kingdom,
                previousRuler,
                titleRuler,
                $"source={source ?? "unknown"}; title de facto sovereign is authoritative.",
                requestInGameDisplay: previousRuler == Clan.PlayerClan || titleRuler == Clan.PlayerClan);

            kingdom.RulingClan = titleRuler;
            QueueSettlementRefresh(titleRuler);
            BellumCivileLogger.Log($"Corrected kingdom ruler from title mantle; kingdom={kingdom.StringId}; previous_ruler={previousRuler?.StringId ?? "null"}; title_ruler={titleRuler.StringId}; source={source ?? "unknown"}.");
            return true;
        }

        private static bool TryGetLiveRebelKingdom(FactionObject faction, out Kingdom rebelKingdom)
        {
            rebelKingdom = faction?.GetRebelKingdom();
            return faction != null
                && !faction.IsIdeology
                && !faction.IsChallengeStartupPending
                && faction.ParentKingdom != null
                && rebelKingdom != null
                && rebelKingdom != faction.ParentKingdom
                && !rebelKingdom.IsEliminated;
        }

        private static int CountStrongholds(Kingdom kingdom)
        {
            return kingdom?.Fiefs.Count(f => f != null && (f.IsTown || f.IsCastle)) ?? 0;
        }

        private void OnHeroKilled(Hero victim, Hero killer, KillCharacterAction.KillCharacterActionDetail detail, bool showNotification)
        {
            foreach (FactionObject faction in _activeFactions.ToList())
            {
                if (faction.Leader?.Leader != victim) continue;
                if (faction.HasTrackedRebelKingdom || faction.IsChallengeStartupPending
                    || CivilWarConflictBehavior.IsFactionTransferPending(faction)) continue;

                bool clanHasSurvivors = faction.Leader.Heroes
                    .Any(h => h != victim && h.IsAlive && !h.IsChild);
                if (clanHasSurvivors) continue;

                Clan newLeader = faction.Members
                    .Where(c => c != faction.Leader && !c.IsEliminated && c.Leader != null && c.Kingdom == faction.ParentKingdom)
                    .OrderByDescending(RebellionPowerHelper.CalculateClanPower)
                    .FirstOrDefault();

                if (newLeader != null)
                {
                    faction.Leader = newLeader;
                    if (Clan.PlayerClan.Kingdom == faction.ParentKingdom)
                    {
                        TextObject msg = new TextObject("{=BC_FactionLeaderDied}Following the death of their champion, the {FACTION_NAME} rallies under {NEW_LEADER}.");
                        msg.SetTextVariable("FACTION_NAME", NotificationHelper.GetFactionDisplayName(faction));
                        msg.SetTextVariable("NEW_LEADER", newLeader.Leader.Name);
                        BellumCivileNotifications.Show(msg, BellumNotificationColors.LowPriority, primaryKingdom: faction.ParentKingdom, primaryClan: faction.Leader, secondaryClan: newLeader);
                    }
                }
                else if (!faction.IsIdeology)
                {
                    RemoveFaction(faction);
                }
            }
        }
    }
}
