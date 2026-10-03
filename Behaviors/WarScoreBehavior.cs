using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.SaveSystem;
using C = BellumCivile.BellumCivileConstants;

namespace BellumCivile.Behaviors
{
    public sealed partial class WarScoreBehavior : CampaignBehaviorBase
    {
        private List<WarScoreRecord> _wars = new List<WarScoreRecord>();
        private int _runtimeRevision;

        public int RuntimeRevision => _runtimeRevision;

        public override void RegisterEvents()
        {
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
            CampaignEvents.WarDeclared.AddNonSerializedListener(this, OnWarDeclared);
            CampaignEvents.MakePeace.AddNonSerializedListener(this, OnMakePeace);
            CampaignEvents.KingdomDestroyedEvent.AddNonSerializedListener(this, OnKingdomDestroyed);
            CampaignEvents.OnSettlementOwnerChangedEvent.AddNonSerializedListener(this, OnSettlementOwnerChanged);
            CampaignEvents.MapEventEnded.AddNonSerializedListener(this, OnMapEventEnded);
            CampaignEvents.VillageLooted.AddNonSerializedListener(this, OnVillageLooted);
            CampaignEvents.HeroPrisonerTaken.AddNonSerializedListener(this, OnHeroPrisonerTaken);
            CampaignEvents.HeroPrisonerReleased.AddNonSerializedListener(this, OnScorePrisonerReleased);
            CampaignEvents.HeroKilledEvent.AddNonSerializedListener(this, (hero, killer, detail, show) => RemoveCustodyScore(hero));
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
        }

        public override void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData("BellumCivile_WarScore_Wars", ref _wars);
            EnsureCollectionsInitialized();
            foreach (var war in _wars) war?.EnsureRaidHistory();
        }

        public IReadOnlyList<WarScoreRecord> GetActiveWars()
        {
            EnsureCollectionsInitialized();
            return _wars.Where(war => war != null && war.IsActive).ToList();
        }

        public bool HasQueuedLoyalistCivilWarResolution(FactionObject faction)
        {
            EnsureCollectionsInitialized();
            if (faction == null
                || faction.IsIdeology
                || faction.ParentKingdom == null
                || string.IsNullOrWhiteSpace(faction.ParentKingdom.StringId))
            {
                return false;
            }

            string parentKingdomId = faction.ParentKingdom.StringId;
            return _wars.Any(war =>
                war != null
                && war.IsActive
                && war.ConflictType == WarScoreConflictType.CivilWar
                && war.TerminalResolutionQueued
                && war.Score <= -C.WarScoreForcePeaceThreshold
                && war.DefenderKingdomId == parentKingdomId
                && (faction.IsTrackedRebelKingdomId(war.AttackerKingdomId)
                    || faction.IsTrackedRebelKingdomId(war.ContextId)));
        }

        public IReadOnlyList<WarScoreRecord> GetDisplayableWars(Kingdom viewingKingdom)
        {
            EnsureCollectionsInitialized();
            if (viewingKingdom == null || viewingKingdom.IsEliminated)
                return new List<WarScoreRecord>();

            string viewingKingdomId = viewingKingdom.StringId;
            return _wars
                .Where(war => war != null
                    && war.IsActive
                    && (war.AttackerKingdomId == viewingKingdomId || war.DefenderKingdomId == viewingKingdomId))
                .Where(war =>
                {
                    Kingdom opposingKingdom = GetOpposingKingdom(war, viewingKingdom);
                    return opposingKingdom != null
                        && !opposingKingdom.IsEliminated
                        && viewingKingdom.IsAtWarWith(opposingKingdom);
                })
                .OrderBy(war => war.StartedDay)
                .ThenBy(war => war.WarKey)
                .ToList();
        }

        public Kingdom GetOpposingKingdom(WarScoreRecord war, Kingdom viewingKingdom)
        {
            if (war == null || viewingKingdom == null)
                return null;

            if (war.AttackerKingdomId == viewingKingdom.StringId)
                return ResolveKingdom(war.DefenderKingdomId);
            if (war.DefenderKingdomId == viewingKingdom.StringId)
                return ResolveKingdom(war.AttackerKingdomId);
            return null;
        }

        public IReadOnlyList<WarScoreRecord> GetTrackedWars()
        {
            EnsureCollectionsInitialized();
            return _wars.Where(war => war != null).ToList();
        }

        public WarScoreRecord GetActiveWar(Kingdom first, Kingdom second)
        {
            if (first == null || second == null)
                return null;

            EnsureCollectionsInitialized();
            string key = BuildWarKey(first, second);
            return _wars.FirstOrDefault(war => war != null && war.IsActive && war.WarKey == key);
        }

        public WarScoreRecord GetActiveWar(string warKey)
        {
            if (string.IsNullOrWhiteSpace(warKey))
                return null;

            EnsureCollectionsInitialized();
            return _wars.FirstOrDefault(war => war != null && war.IsActive && war.WarKey == warKey);
        }

        // Explicit transition primitive only. The coordinator must transfer the native
        // hostility and retarget the owning faction in the same saved transaction.
        internal bool RetargetCivilWarDefender(WarScoreRecord record, Kingdom previous, Kingdom successor)
        {
            if (!CanRetargetCivilWarDefender(record, previous, successor)) return false;
            if (!record.TryRetargetCivilWarDefender(previous.StringId, successor.StringId)) return false;
            _runtimeRevision++;
            return true;
        }

        internal bool CanRetargetCivilWarDefender(WarScoreRecord record, Kingdom previous, Kingdom successor)
        {
            if (record == null || previous == null || successor?.IsEliminated != false) return false;
            EnsureCollectionsInitialized();
            if (!_wars.Contains(record)) return false;
            string key = WarScoreRecord.PairKey(record.AttackerKingdomId, successor.StringId);
            if (_wars.Any(w => w != record && w.IsActive && w.WarKey == key)) return false;
            return record.CanRetargetCivilWarDefender(previous.StringId, successor.StringId);
        }

        internal bool PromoteRivalry(CivilWarRivalPromotionRecord transfer)
        {
            var war = transfer.RivalPair.Score;
            var old = transfer.OldCrownScore;
            string winner = transfer.Winner.Realm.StringId, survivor = transfer.Survivor.Realm.StringId;
            string crown = transfer.Conflict.CrownRealm.StringId;
            EnsureCollectionsInitialized();
            if (!_wars.Contains(war) || !_wars.Contains(old) || war == old
                || !war.CanPromoteRivalry(winner, survivor, crown)
                || old.IsActive && (old.ResolutionPending || old.TerminalResolutionQueued || old.ParleyPending || old.WhitePeaceOfferPending)
                || _wars.Any(w => w != war && w != old && w.IsActive && w.WarKey == WarScoreRecord.PairKey(survivor, crown))) return false;
            // Retire the obsolete opponent before reusing its pair key; do not combine histories.
            string previousKey = war.WarKey;
            float previousScore = war.Score;
            bool firstPromotion = war.ContextId.StartsWith(CivilWarPairRecord.RivalryPrefix, StringComparison.Ordinal);
            if (old.IsActive) MarkWarEnded(old);
            if (!war.TryPromoteRivalry(winner, survivor, crown)) return false;
            if (firstPromotion)
                BellumCivileLogger.Log($"Rivalry score promoted; winner_shell={winner}; survivor={survivor}; crown={crown}; "
                    + $"previous_war={previousKey}; previous_score={previousScore:0.000}; continued_war={war.WarKey}; continued_score={war.Score:0.000}; "
                    + $"retired_crown_score={old.Score:0.000}; started_day={war.StartedDay:0.000}; history=existing rivalry, oriented to surviving challenger; old Crown history discarded.");
            _runtimeRevision++;
            return true;
        }

        public bool CompleteClaimFeudWar(string firstKingdomId, string secondKingdomId, string reason)
        {
            if (string.IsNullOrWhiteSpace(firstKingdomId) || string.IsNullOrWhiteSpace(secondKingdomId))
                return false;

            EnsureCollectionsInitialized();
            WarScoreRecord record = _wars.FirstOrDefault(war => war != null
                && war.IsActive
                && war.ConflictType == WarScoreConflictType.ClaimFeud
                && ((war.AttackerKingdomId == firstKingdomId && war.DefenderKingdomId == secondKingdomId)
                    || (war.AttackerKingdomId == secondKingdomId && war.DefenderKingdomId == firstKingdomId)));
            if (record == null)
                return false;

            Kingdom attacker = ResolveKingdom(record.AttackerKingdomId);
            Kingdom defender = ResolveKingdom(record.DefenderKingdomId);
            AddEvent(record, WarScoreEventType.WarEnded, 0f, attacker, defender, debugText: $"claim feud resolved; {reason ?? "unknown"}");
            MarkWarEnded(record);
            BellumCivileLogger.Log($"War Score claim-feud fallback completed; war={record.WarKey}; score={record.Score:+0.0;-0.0;0.0}; reason={reason ?? "unknown"}.");
            return true;
        }

        public bool CompleteCivilWar(FactionObject faction, Kingdom rebelKingdom, string reason)
        {
            if (CivilWarConflictBehavior.IsFactionTransferPending(faction)
                || CivilWarConflictBehavior.IsRealmTransferPending(rebelKingdom)) return false;
            if (faction == null && rebelKingdom == null)
                return false;

            EnsureCollectionsInitialized();
            string rebelKingdomId = rebelKingdom?.StringId;
            string parentKingdomId = faction?.ParentKingdom?.StringId;
            WarScoreRecord record = _wars.FirstOrDefault(war => war != null
                && war.IsActive
                && war.ConflictType == WarScoreConflictType.CivilWar
                && (war.AttackerKingdomId == rebelKingdomId
                    || (faction != null && faction.IsTrackedRebelKingdomId(war.AttackerKingdomId)))
                && (string.IsNullOrEmpty(parentKingdomId) || war.DefenderKingdomId == parentKingdomId));
            if (record == null)
                return false;

            Kingdom attacker = rebelKingdom ?? ResolveKingdom(record.AttackerKingdomId);
            Kingdom defender = faction?.ParentKingdom ?? ResolveKingdom(record.DefenderKingdomId);
            AddEvent(record, WarScoreEventType.WarEnded, 0f, attacker, defender, debugText: $"civil war resolved; {reason ?? "unknown"}");
            MarkWarEnded(record);
            BellumCivileLogger.Log($"War Score civil-war lifecycle completed; war={record.WarKey}; score={record.Score:+0.0;-0.0;0.0}; reason={reason ?? "unknown"}.");
            return true;
        }

        public string BuildDebugReport(Kingdom first, Kingdom second)
        {
            if (first == null || second == null)
                return "Error: kingdom not found.";

            WarScoreRecord war = GetActiveWar(first, second);
            if (war == null)
                return $"No active Bellum War Score record for {first.Name} and {second.Name}.";

            Kingdom attacker = ResolveKingdom(war.AttackerKingdomId);
            Kingdom defender = ResolveKingdom(war.DefenderKingdomId);
            bool whitePeaceEligible = IsMutualWhitePeaceEligible(war, out float attackerWill, out float defenderWill);
            List<string> lines = new List<string>
            {
                $"War Score: {attacker?.Name?.ToString() ?? war.AttackerKingdomId} vs {defender?.Name?.ToString() ?? war.DefenderKingdomId}; kind={war.ConflictType}; context={war.ContextId}",
                $"score={war.Score:+0.0;-0.0;0.0} (positive favors attacker)",
                $"components: occupation={war.OccupationScore:+0.0;-0.0;0.0}; battles={war.BattleScore:+0.0;-0.0;0.0}/{C.WarScoreBattleCap:0}; raids={war.RaidScore:+0.0;-0.0;0.0}/{C.WarScoreRaidCap:0}; prisoners={war.PrisonerScore:+0.0;-0.0;0.0}/{C.WarScorePrisonerCap:0}; ticking={war.TickingScore:+0.0;-0.0;0.0}/{C.WarScoreTickingCap:0}; landless={war.LandlessPressureScore:+0.0;-0.0;0.0}/{C.WarScoreLandlessPressureCap:0}; objectives={war.ObjectiveScore:+0.0;-0.0;0.0}",
                $"started_day={war.StartedDay:0.0}; fief_snapshot={war.FiefSnapshots?.Count ?? 0}; events={war.Events?.Count ?? 0}",
                $"white_peace: eligible={whitePeaceEligible}; primary_will={attackerWill:0.0}; opposing_will={defenderWill:0.0}; threshold={BellumCivileOptions.WarWillMutualWhitePeaceThreshold:0.0}; next_check_day={war.NextWhitePeaceCheckDay:0.0}; player_offer_pending={war.WhitePeaceOfferPending}",
                $"parley: pending={war.ParleyPending}; forced={war.ParleyForced}; opened_day={war.ParleyOpenedDay:0.0}; terminal_resolution_queued={war.TerminalResolutionQueued}; terminal_reason={war.TerminalResolutionReason ?? string.Empty}"
            };

            if (war.ConflictType == WarScoreConflictType.CivilWar || war.ConflictType == WarScoreConflictType.ClaimFeud)
                lines.Add(BuildInternalResolutionDiagnostic(war, attacker, defender));

            foreach (WarScoreEventRecord evt in war.Events
                .OrderByDescending(evt => evt.Day)
                .Take(12))
            {
                Kingdom actor = ResolveKingdom(evt.ActorKingdomId);
                Kingdom target = ResolveKingdom(evt.TargetKingdomId);
                lines.Add($"- day {evt.Day:0.0}: {evt.EventType} {evt.Delta:+0.0;-0.0;0.0}; score={evt.ScoreAfter:+0.0;-0.0;0.0}; actor={actor?.Name?.ToString() ?? evt.ActorKingdomId}; target={target?.Name?.ToString() ?? evt.TargetKingdomId}; {evt.DebugText}");
            }

            return string.Join(Environment.NewLine, lines);
        }

        private string BuildInternalResolutionDiagnostic(WarScoreRecord war, Kingdom primary, Kingdom opposing)
        {
            if (war == null || primary == null || opposing == null || !primary.IsAtWarWith(opposing))
                return "internal_resolution: blocked; conflict sides are unavailable or no longer at war";

            float elapsedDays = CurrentDay - war.StartedDay;
            float primaryWill = CalculatePowerWeightedWarWill(primary, war);
            float opposingWill = CalculatePowerWeightedWarWill(opposing, war);
            float leverage = Math.Abs(war.Score);
            if (leverage >= C.WarScoreForcePeaceThreshold)
                return "internal_resolution: terminal war score reached; decisive resolution pending";

            bool primaryVictory = war.Score > 0f;
            float losingWill = primaryVictory ? opposingWill : primaryWill;
            if (PeaceReadiness.CanConcede(leverage, losingWill, elapsedDays, BellumCivileOptions.WarDurationReluctanceDays))
            {
                Kingdom losingSide = primaryVictory ? opposing : primary;
                return $"internal_resolution: exhausted surrender eligible; winner={((primaryVictory ? primary : opposing).Name)}; loser={losingSide.Name}; losing_will={losingWill:0.0}; leverage={leverage:0.0}; age_days={elapsedDays:0.0}";
            }

            if (PeaceReadiness.CanConsiderWhitePeace(war.Score, primaryWill, opposingWill, elapsedDays, BellumCivileOptions.WarDurationReluctanceDays))
            {
                float chance = CalculateInternalWhitePeaceChance(primaryWill, opposingWill);
                return $"internal_resolution: mutual white peace eligible; weekly_chance={chance:P0}; leverage={leverage:0.0}/{C.WarScoreWhitePeaceMaximumScore:0.0}";
            }

            float readiness = PeaceReadiness.Evaluate(elapsedDays, war.Score, losingWill, BellumCivileOptions.WarDurationReluctanceDays).Total;
            return $"internal_resolution: continued fighting; leverage={leverage:0.0}/{C.WarScoreExhaustedVictoryThreshold:0.0}; primary_will={primaryWill:0.0}; opposing_will={opposingWill:0.0}; losing_readiness={readiness:0.0}; age_days={elapsedDays:0.0}";
        }

        public bool IsOrdinaryTrackedWar(Kingdom first, Kingdom second)
        {
            return GetActiveWar(first, second)?.ConflictType == WarScoreConflictType.ForeignWar;
        }

        public float GetSelfRelativeWarScore(Kingdom viewingKingdom, Kingdom first, Kingdom second)
        {
            WarScoreRecord war = GetActiveWar(first, second);
            return war?.GetSelfRelativeScore(viewingKingdom?.StringId) ?? 0f;
        }

        public bool TryResolveForeignWhitePeaceFromProposal(Clan proposer, out string report)
        {
            if (!TryGetForeignParleyProposal(proposer, out WarScoreRecord selectedWar, out bool preferWhitePeace, out report))
                return false;

            ForeignTreatyBehavior treaties = Campaign.Current?.GetCampaignBehavior<ForeignTreatyBehavior>();
            return treaties?.TryQueueAiParley(
                selectedWar,
                forced: false,
                preferWhitePeace,
                preferWhitePeace ? "mutual exhaustion" : "low-war-will peace proposal",
                proposer.Kingdom,
                out report) == true;
        }

        public bool TryGetForeignParleyProposal(
            Clan proposer,
            out WarScoreRecord selectedWar,
            out bool preferWhitePeace,
            out string report,
            bool requireLowWill = true)
        {
            selectedWar = null;
            preferWhitePeace = false;
            report = "no eligible foreign war";
            if (proposer == null || proposer == Clan.PlayerClan || proposer.Kingdom == null)
                return false;

            List<WarScoreRecord> candidates = GetActiveWars()
                .Where(war => war.ConflictType == WarScoreConflictType.ForeignWar
                    && (war.AttackerKingdomId == proposer.Kingdom.StringId || war.DefenderKingdomId == proposer.Kingdom.StringId))
                .Where(war => !IsAuxiliaryClientWar(war))
                .Where(war => !IsStorylineProtectedForeignWar(war))
                .Where(war => war.CanReconsiderPeace(CurrentDay))
                .Where(war => !requireLowWill || CourtAgendaBehavior.EffectiveWarWill(proposer,war,
                    Campaign.Current.GetCampaignBehavior<WarPeaceRevampBehavior>()?.GetWarWill(proposer) ?? 50) < BellumCivileOptions.WarWillPeaceThreshold)
                .ToList();
            if (candidates.Count == 0)
                return false;

            WarScoreRecord whitePeace = candidates
                .Where(war => IsMutualWhitePeaceEligible(war, out _, out _))
                .OrderBy(war => Math.Abs(war.Score))
                .FirstOrDefault();
            if (whitePeace != null)
            {
                selectedWar = whitePeace;
                preferWhitePeace = true;
                report = "mutual exhaustion supports white peace";
                return true;
            }

            WarScoreRecord settlement = candidates
                .Where(war => Math.Abs(war.GetSelfRelativeScore(proposer.Kingdom.StringId)) > C.WarScoreWhitePeaceMaximumScore)
                .OrderByDescending(war => Math.Abs(war.GetSelfRelativeScore(proposer.Kingdom.StringId)))
                .FirstOrDefault();
            if (settlement == null)
            {
                report = "war score leverage is too low for a negotiated settlement";
                return false;
            }

            selectedWar = settlement;
            report = "low war will supports a negotiated settlement";
            return true;
        }

        public WarScoreRecord RegisterCivilWar(FactionObject faction, Kingdom rebelKingdom)
        {
            if (!WarPeaceRevampBehavior.IsRevampEnabled()
                || faction == null
                || faction.IsIdeology
                || faction.ParentKingdom == null
                || rebelKingdom == null)
            {
                return null;
            }

            var record = RegisterConflict(
                WarScoreConflictType.CivilWar,
                rebelKingdom,
                faction.ParentKingdom,
                rebelKingdom.StringId,
                "civil war declared");
            CivilWarConflictBehavior.Instance?.Observe(faction, rebelKingdom, record);
            return record;
        }

        internal WarScoreRecord RegisterCivilWarRivalry(string pairId, Kingdom attacker, Kingdom defender)
        {
            if (pairId?.StartsWith(CivilWarPairRecord.RivalryPrefix, StringComparison.Ordinal) != true) return null;
            var existing = GetActiveWar(attacker, defender);
            if (existing != null) return existing.ConflictType == WarScoreConflictType.CivilWar
                && existing.ContextId == pairId && existing.AttackerKingdomId == attacker.StringId
                && existing.DefenderKingdomId == defender.StringId ? existing : null;
            return RegisterConflict(WarScoreConflictType.CivilWar, attacker, defender, pairId, "rival claimant war");
        }

        internal void CompleteCivilWarRivalry(WarScoreRecord war, string reason)
        {
            if (war?.IsActive != true || !CivilWarConflictBehavior.IsRivalryScore(war)) return;
            AddEvent(war, WarScoreEventType.WarEnded, 0f, ResolveKingdom(war.AttackerKingdomId),
                ResolveKingdom(war.DefenderKingdomId), debugText: reason);
            MarkWarEnded(war);
        }

        public WarScoreRecord RegisterClaimFeudWar(ClaimFeudWarRecord feudWar)
        {
            if (!WarPeaceRevampBehavior.IsRevampEnabled() || feudWar == null)
                return null;

            return RegisterConflict(
                WarScoreConflictType.ClaimFeud,
                ResolveKingdom(feudWar.ClaimantKingdomId),
                ResolveKingdom(feudWar.HolderKingdomId),
                feudWar.WarId,
                "claim feud declared");
        }

        public bool SetFeudObjectiveControl(
            Kingdom primarySide,
            Kingdom opposingSide,
            bool? claimantControls,
            WarScoreEventType eventType,
            string settlementId,
            string debugText)
        {
            WarScoreRecord war = GetActiveWar(primarySide, opposingSide);
            if (war == null || war.ConflictType != WarScoreConflictType.ClaimFeud)
                return false;

            float previous = war.Score;
            float objectiveBefore = war.ObjectiveScore;
            float tickingBefore = war.TickingScore;
            if (!war.UpdateFeudObjectiveControl(primarySide?.StringId, claimantControls, CurrentDay)) return false;
            if (objectiveBefore != war.ObjectiveScore || tickingBefore != war.TickingScore)
                AddEvent(war, eventType, war.Score - previous, primarySide, opposingSide, settlementId, debugText: debugText);
            QueueTerminalWarScoreResolution(war, debugText);
            return true;
        }

        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            ReconcileExistingWars();
            ReconcileReversibleScores();
        }

        private void OnDailyTick()
        {
            ReconcileExistingWars();
            ReconcileInternalConflicts();
            ReconcileReversibleScores();
            ProcessQueuedTerminalResolutions();
            ProcessActiveWars();
            ProcessInternalWhitePeaceOpportunities();
        }

        private void OnWarDeclared(IFaction firstFaction, IFaction secondFaction, DeclareWarAction.DeclareWarDetail detail)
        {
            Kingdom attacker = firstFaction as Kingdom;
            Kingdom defender = secondFaction as Kingdom;
            if (!ShouldTrackForeignWar(attacker, defender))
                return;

            RegisterConflict(WarScoreConflictType.ForeignWar, attacker, defender, string.Empty, "war declared");
        }

        internal bool EnsureProtectionInterventionTracked(Kingdom protector, Kingdom threat, bool declarationAttempted)
        {
            if (!protector.IsAtWarWith(threat)) return false;
            if (GetActiveWar(protector, threat) != null) return true;
            // Recover a missed listener without declaring war again or resetting an existing record.
            return declarationAttempted && RegisterConflict(WarScoreConflictType.ForeignWar, protector, threat, string.Empty,
                "protection intervention receipt recovery") != null;
        }

        private void OnMakePeace(IFaction firstFaction, IFaction secondFaction, MakePeaceAction.MakePeaceDetail detail)
        {
            Kingdom first = firstFaction as Kingdom;
            Kingdom second = secondFaction as Kingdom;
            WarScoreRecord record = GetActiveWar(first, second);
            if (record == null)
                return;

            Kingdom attacker = ResolveKingdom(record.AttackerKingdomId);
            Kingdom defender = ResolveKingdom(record.DefenderKingdomId);
            AddEvent(record, WarScoreEventType.WarEnded, 0f, attacker, defender, debugText: $"peace detail={detail}");
            MarkWarEnded(record);
            BellumCivileLogger.Log($"War Score tracking ended; war={record.WarKey}; score={record.Score:+0.0;-0.0;0.0}; detail={detail}.");
        }

        private void OnKingdomDestroyed(Kingdom kingdom)
        {
            if (!WarPeaceRevampBehavior.IsRevampEnabled() || kingdom == null)
                return;

            foreach (WarScoreRecord record in GetActiveWars()
                .Where(war => war.ConflictType == WarScoreConflictType.ForeignWar
                    && (war.AttackerKingdomId == kingdom.StringId || war.DefenderKingdomId == kingdom.StringId))
                .ToList())
            {
                Kingdom attacker = record.AttackerKingdomId == kingdom.StringId
                    ? kingdom
                    : ResolveKingdom(record.AttackerKingdomId);
                Kingdom defender = record.DefenderKingdomId == kingdom.StringId
                    ? kingdom
                    : ResolveKingdom(record.DefenderKingdomId);
                AddEvent(record, WarScoreEventType.WarEnded, 0f, attacker, defender, debugText: $"kingdom destroyed; kingdom={kingdom.StringId}");
                MarkWarEnded(record);
                BellumCivileLogger.Log($"War Score foreign-war lifecycle completed after kingdom destruction; war={record.WarKey}; score={record.Score:+0.0;-0.0;0.0}; kingdom={kingdom.StringId}.");
            }
        }

        private void OnSettlementOwnerChanged(
            Settlement settlement,
            bool openToClaim,
            Hero newOwner,
            Hero oldOwner,
            Hero capturerHero,
            ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail detail)
        {
            if (BellumTreatyTransferContext.IsTreatyTransfer)
                return;
            if (!WarPeaceRevampBehavior.IsRevampEnabled()
                || detail != ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail.BySiege
                || settlement == null
                || (!settlement.IsTown && !settlement.IsCastle))
            {
                return;
            }

            Kingdom oldKingdom = oldOwner?.Clan?.Kingdom;
            Kingdom newKingdom = newOwner?.Clan?.Kingdom ?? settlement.OwnerClan?.Kingdom;
            ClearResolvedLandlessPressureAfterSettlementGain(newKingdom);
            WarScoreEventType eventType = settlement.IsTown ? WarScoreEventType.TownCaptured : WarScoreEventType.CastleCaptured;
            foreach (var war in GetActiveWars().ToList())
            {
                var territory = new ClientWarTerritory(war);
                if (!territory.Opposing(oldKingdom?.StringId, newKingdom?.StringId)) continue;
                UpdateOccupationScore(war, eventType, newKingdom, oldKingdom, settlement.StringId, debugText: settlement.Name?.ToString() ?? settlement.StringId);
                Kingdom attacker = ResolveKingdom(war.AttackerKingdomId);
                Kingdom defender = ResolveKingdom(war.DefenderKingdomId);
                if (!TryForceFullOccupation(war, attacker, defender))
                    QueueTerminalWarScoreResolution(war, "settlement ownership changed after siege");
            }
        }

        private void OnMapEventEnded(MapEvent mapEvent)
        {
            if (!WarPeaceRevampBehavior.IsRevampEnabled()
                || mapEvent?.Winner == null
                || mapEvent.AttackerSide == null
                || mapEvent.DefenderSide == null)
            {
                return;
            }

            MapEventSide winningSide = mapEvent.Winner;
            MapEventSide losingSide = winningSide == mapEvent.AttackerSide ? mapEvent.DefenderSide : mapEvent.AttackerSide;
            List<Kingdom> winners = GetSideKingdoms(winningSide).ToList();
            List<Kingdom> losers = GetSideKingdoms(losingSide).ToList();
            if (winners.Count == 0 || losers.Count == 0)
                return;

            int casualties = CountCasualties(losingSide);
            int startingStrength = CountStartingHealthyTroops(mapEvent.AttackerSide) + CountStartingHealthyTroops(mapEvent.DefenderSide);
            bool major = startingStrength >= C.WarScoreMajorBattleStrength;
            float score = Math.Max(C.WarScoreBattleMinimum, Math.Min(C.WarScoreBattleMaximum, casualties / C.WarScoreBattleCasualtyDivisor));
            if (major)
                score = Math.Min(C.WarScoreBattleMaximum, score + C.WarScoreMajorBattleBonus);

            foreach (Kingdom winner in winners)
            {
                foreach (Kingdom loser in losers)
                {
                    WarScoreRecord war = GetActiveWar(winner, loser);
                    if (war == null)
                        continue;

                    float delta = SignedDeltaFor(war, winner, score);
                    float before = war.Score;
                    war.AddBattleScore(delta, C.WarScoreBattleCap);
                    AddEvent(war, major ? WarScoreEventType.MajorBattleWon : WarScoreEventType.BattleWon, war.Score - before, winner, loser, debugText: $"casualties={casualties}; strength={startingStrength}");
                    QueueTerminalWarScoreResolution(war, "battle ended at terminal war score");
                }
            }
        }

        private void OnVillageLooted(Village village)
        {
            if (!WarPeaceRevampBehavior.IsRevampEnabled())
                return;

            Kingdom victim = village?.Settlement?.OwnerClan?.Kingdom;
            Kingdom attacker = village?.Settlement?.LastAttackerParty?.MapFaction as Kingdom;
            WarScoreRecord war = GetActiveWar(attacker, victim);
            if (war == null)
                return;

            float before = war.Score;
            if (!war.TryCreditVillageRaid(village.Settlement.StringId, attacker.StringId, C.WarScoreVillageRaided, C.WarScoreRaidCap)) return;
            AddEvent(war, WarScoreEventType.VillageRaided, war.Score - before, attacker, victim, village.Settlement.StringId, debugText: village.Name?.ToString() ?? village.StringId);
            QueueTerminalWarScoreResolution(war, "village raid reached terminal war score");
        }

        private void OnHeroPrisonerTaken(PartyBase captor, Hero prisoner)
        {
            if (HostageCustodyGuard.IsProtected(prisoner)) return;
            if (!WarPeaceRevampBehavior.IsRevampEnabled())
                return;

            Kingdom victim = prisoner?.Clan?.Kingdom;
            Kingdom captorKingdom = captor?.MobileParty?.LeaderHero?.Clan?.Kingdom ?? captor?.MapFaction as Kingdom;
            WarScoreRecord war = GetActiveWar(captorKingdom, victim);
            if (war == null)
                return;

            bool ruler = IsWarLeader(victim, prisoner);
            bool heir = !ruler && IsPrimaryHeirOfWarLeader(victim, prisoner);
            float score = ruler ? C.WarScoreRulerCaptured : heir ? C.WarScoreHeirCaptured : C.WarScoreNobleCaptured;
            WarScoreEventType type = ruler ? WarScoreEventType.RulerCaptured : heir ? WarScoreEventType.HeirCaptured : WarScoreEventType.NobleCaptured;
            float delta = SignedDeltaFor(war, captorKingdom, score);
            float before = war.Score;
            war.RecordPrisoner(prisoner.StringId, delta, C.WarScorePrisonerCap);
            AddEvent(war, type, war.Score - before, captorKingdom, victim, heroId: prisoner.StringId, debugText: prisoner.Name?.ToString() ?? prisoner.StringId);
            QueueTerminalWarScoreResolution(war, "prisoner capture reached terminal war score");
        }

        private void ProcessActiveWars()
        {
            foreach (WarScoreRecord war in GetActiveWars().ToList())
            {
                Kingdom attacker = ResolveKingdom(war.AttackerKingdomId);
                Kingdom defender = ResolveKingdom(war.DefenderKingdomId);
                if (!ShouldProcessWar(war, attacker, defender) || !attacker.IsAtWarWith(defender))
                    continue;

                UpdateOccupationScore(war, WarScoreEventType.OccupationUpdated, attacker, defender, debugText: "daily occupation refresh");
                if (TryForceFullOccupation(war, attacker, defender))
                    continue;
                if (ApplyLandlessRealmPressure(war, attacker, defender))
                    continue;

                ApplyTickingLeverage(war, attacker, defender);
                QueueTerminalWarScoreResolution(war, "daily war score evaluation");
            }
        }

        private void ProcessQueuedTerminalResolutions()
        {
            // Ownership, battle, and prisoner callbacks can run before Bannerlord has finished
            // dismantling their map event. Commit realm-wide cleanup only from a later daily tick.
            foreach (WarScoreRecord war in GetActiveWars()
                .Where(record => record.TerminalResolutionQueued)
                .ToList())
            {
                ResolveQueuedTerminalWarScore(war);
            }
        }

        private void ReconcileExistingWars()
        {
            if (!WarPeaceRevampBehavior.IsRevampEnabled())
                return;

            ReconcileOrphanedForeignWarTrackers();

            foreach (Kingdom first in Kingdom.All.Where(IsValidForeignWarParticipant))
            {
                foreach (Kingdom second in Kingdom.All.Where(IsValidForeignWarParticipant))
                {
                    if (string.CompareOrdinal(first.StringId, second.StringId) >= 0)
                        continue;
                    if (!first.IsAtWarWith(second) || GetActiveWar(first, second) != null)
                        continue;
                    if (!IsForeignWarPair(first, second)) continue;

                    // Bannerlord preserves when an existing war began, but not which side originally declared it.
                    // Assign that missing role once when Bellum creates the conflict record; the record then becomes
                    // the shared source of truth for War Will, war score, and treaty logic.
                    bool firstIsAttacker = MBRandom.RandomFloat < 0.5f;
                    Kingdom attacker = firstIsAttacker ? first : second;
                    Kingdom defender = firstIsAttacker ? second : first;
                    RegisterConflict(WarScoreConflictType.ForeignWar, attacker, defender, string.Empty, "existing war reconciliation; aggressor inferred");
                }
            }
        }

        private void ReconcileOrphanedForeignWarTrackers()
        {
            foreach (WarScoreRecord war in GetActiveWars()
                .Where(record => record.ConflictType == WarScoreConflictType.ForeignWar)
                .ToList())
            {
                Kingdom attacker = ResolveKingdom(war.AttackerKingdomId);
                Kingdom defender = ResolveKingdom(war.DefenderKingdomId);
                TryCloseOrphanedForeignWarTracker(war, attacker, defender, "daily foreign-war reconciliation");
            }
        }

        private void ReconcileInternalConflicts()
        {
            if (!WarPeaceRevampBehavior.IsRevampEnabled())
                return;

            ClaimFeudWarBehavior feudWars = Campaign.Current?.GetCampaignBehavior<ClaimFeudWarBehavior>();
            List<ClaimFeudWarRecord> activeFeudWars = feudWars?.GetActiveWars().ToList()
                ?? new List<ClaimFeudWarRecord>();
            foreach (WarScoreRecord trackedFeud in GetActiveWars()
                .Where(war => war.ConflictType == WarScoreConflictType.ClaimFeud)
                .ToList())
            {
                bool hasActiveFeud = activeFeudWars.Any(feud => feud != null
                    && ((feud.ClaimantKingdomId == trackedFeud.AttackerKingdomId && feud.HolderKingdomId == trackedFeud.DefenderKingdomId)
                        || (feud.ClaimantKingdomId == trackedFeud.DefenderKingdomId && feud.HolderKingdomId == trackedFeud.AttackerKingdomId)));
                if (!hasActiveFeud)
                {
                    CompleteClaimFeudWar(
                        trackedFeud.AttackerKingdomId,
                        trackedFeud.DefenderKingdomId,
                        "orphaned claim-feud tracker reconciliation");
                }
            }

            foreach (ClaimFeudWarRecord feudWar in activeFeudWars)
            {
                Kingdom claimant = ResolveKingdom(feudWar.ClaimantKingdomId);
                Kingdom holder = ResolveKingdom(feudWar.HolderKingdomId);
                if (claimant != null && holder != null && claimant.IsAtWarWith(holder) && GetActiveWar(claimant, holder) == null)
                    RegisterClaimFeudWar(feudWar);
            }

            FactionManagerBehavior factionManager = Campaign.Current?.GetCampaignBehavior<FactionManagerBehavior>();
            if (factionManager == null)
                return;

            foreach (WarScoreRecord trackedCivilWar in GetActiveWars()
                .Where(war => war.ConflictType == WarScoreConflictType.CivilWar)
                .ToList())
            {
                if (CivilWarConflictBehavior.IsScoreTransferPending(trackedCivilWar)) continue;
                if (CivilWarConflictBehavior.IsRivalryScore(trackedCivilWar)) continue;
                Kingdom rebelKingdom = ResolveKingdom(trackedCivilWar.AttackerKingdomId);
                Kingdom parentKingdom = ResolveKingdom(trackedCivilWar.DefenderKingdomId);
                FactionObject faction = factionManager.GetFactionByTrackedRebelKingdomId(trackedCivilWar.ContextId)
                    ?? factionManager.GetFactionByTrackedRebelKingdomId(trackedCivilWar.AttackerKingdomId);

                if (faction != null
                    && rebelKingdom?.IsEliminated == true
                    && parentKingdom != null
                    && !parentKingdom.IsEliminated)
                {
                    if (!trackedCivilWar.TerminalResolutionQueued)
                    {
                        ForceWarScore(
                            trackedCivilWar,
                            -C.WarScoreForcePeaceThreshold,
                            WarScoreEventType.FullOccupation,
                            parentKingdom,
                            rebelKingdom,
                            "eliminated rebel shell reconciled as loyalist victory");
                        QueueTerminalWarScoreResolution(trackedCivilWar, "rebel kingdom eliminated before civil-war resolution");
                        BellumCivileLogger.Log($"War Score recovered eliminated civil-war shell; war={trackedCivilWar.WarKey}; faction={faction.Name}; rebel={trackedCivilWar.AttackerKingdomId}; parent={trackedCivilWar.DefenderKingdomId}.");
                    }

                    continue;
                }

                if (faction == null
                    && (rebelKingdom == null
                        || rebelKingdom.IsEliminated
                        || parentKingdom == null
                        || parentKingdom.IsEliminated
                        || !rebelKingdom.IsAtWarWith(parentKingdom)))
                {
                    AddEvent(
                        trackedCivilWar,
                        WarScoreEventType.WarEnded,
                        0f,
                        parentKingdom,
                        rebelKingdom,
                        debugText: "orphaned civil-war tracker reconciliation");
                    MarkWarEnded(trackedCivilWar);
                    BellumCivileLogger.Log($"War Score closed orphaned civil-war tracker; war={trackedCivilWar.WarKey}; rebel={trackedCivilWar.AttackerKingdomId}; parent={trackedCivilWar.DefenderKingdomId}.");
                }
            }

            foreach (FactionObject faction in Kingdom.All
                .Where(kingdom => kingdom != null)
                .SelectMany(factionManager.GetFactionsInKingdom)
                .Where(faction => faction != null && !faction.IsIdeology && faction.IsCivilWarActive())
                .Distinct())
            {
                Kingdom rebel = faction.GetRebelKingdom();
                if (rebel != null && faction.ParentKingdom != null && rebel.IsAtWarWith(faction.ParentKingdom) && GetActiveWar(rebel, faction.ParentKingdom) == null)
                    RegisterCivilWar(faction, rebel);
            }
        }

        private void ProcessInternalWhitePeaceOpportunities()
        {
            if (!WarPeaceRevampBehavior.IsRevampEnabled())
                return;

            float today = CurrentDay;
            foreach (WarScoreRecord war in GetActiveWars()
                .Where(war => war.ConflictType == WarScoreConflictType.CivilWar || war.ConflictType == WarScoreConflictType.ClaimFeud)
                .ToList())
            {
                if (CivilWarConflictBehavior.IsScoreTransferPending(war) || CivilWarConflictBehavior.IsRivalryScore(war) || !war.CanCheckWhitePeace(today))
                    continue;

                war.ScheduleNextWhitePeaceCheck(today + C.WarScoreInternalWhitePeaceCheckDays);
                if (TryResolveInternalExhaustedVictory(war))
                    continue;

                if (!IsMutualWhitePeaceEligible(war, out float primaryWill, out float opposingWill))
                    continue;

                if (MBRandom.RandomFloat > CalculateInternalWhitePeaceChance(primaryWill, opposingWill))
                    continue;

                if (IsPlayerConflictLeader(war))
                {
                    ShowPlayerInternalWhitePeaceInquiry(war, primaryWill, opposingWill);
                    continue;
                }

                ResolveInternalWhitePeace(war, "both sides exhausted without decisive leverage");
            }
        }

        private void AddEvent(
            WarScoreRecord war,
            WarScoreEventType type,
            float delta,
            Kingdom actor,
            Kingdom target,
            string settlementId = "",
            string heroId = "",
            string debugText = "")
        {
            if (war == null)
                return;

            war.AddEvent(new WarScoreEventRecord(
                type,
                CurrentDay,
                delta,
                war.Score,
                actor?.StringId,
                target?.StringId,
                settlementId,
                heroId,
                debugText));
            TouchRuntimeRevision();
        }

        private void MarkWarEnded(WarScoreRecord war)
        {
            if (war == null)
                return;

            war.MarkEnded(CurrentDay);
            TouchRuntimeRevision();
        }

        private void TouchRuntimeRevision()
        {
            unchecked
            {
                _runtimeRevision++;
            }
        }

        private void UpdateOccupationScore(
            WarScoreRecord war,
            WarScoreEventType eventType,
            Kingdom actor,
            Kingdom target,
            string settlementId = "",
            string debugText = "")
        {
            if (war == null)
                return;

            float previousOccupation = war.OccupationScore;
            float previousScore = war.Score;
            float occupation = CalculateOccupationScore(war);
            if (Math.Abs(occupation - previousOccupation) < 0.0001f)
                return;

            war.SetOccupationScore(occupation);
            AddEvent(war, eventType, war.Score - previousScore, actor, target, settlementId, debugText);
        }

        private float CalculateOccupationScore(WarScoreRecord war)
        {
            if (war?.FiefSnapshots == null)
                return 0f;

            var territory = new ClientWarTerritory(war);
            float total = 0f;
            foreach (WarScoreFiefSnapshotRecord snapshot in war.FiefSnapshots)
            {
                if (snapshot == null || string.IsNullOrWhiteSpace(snapshot.SettlementId))
                    continue;

                Settlement settlement = ResolveSettlement(snapshot.SettlementId);
                Kingdom currentKingdom = settlement?.OwnerClan?.Kingdom;
                if (currentKingdom == null)
                    continue;

                float value = snapshot.IsTown ? C.WarScoreTownCaptured : snapshot.IsCastle ? C.WarScoreCastleCaptured : 0f;
                if (value <= 0f)
                    continue;

                if (territory.Side(snapshot.OwnerKingdomId) == 1 && territory.Side(currentKingdom.StringId) == -1)
                    total -= value;
                else if (territory.Side(snapshot.OwnerKingdomId) == -1 && territory.Side(currentKingdom.StringId) == 1)
                    total += value;
            }

            if (total > 100f)
                return 100f;
            if (total < -100f)
                return -100f;
            return total;
        }

        private void ApplyTickingLeverage(WarScoreRecord war, Kingdom attacker, Kingdom defender)
        {
            if (war == null || war.ConflictType == WarScoreConflictType.ClaimFeud || CurrentDay <= war.LastTickDay)
                return;

            float direction = war.Score > 0.5f ? 1f : war.Score < -0.5f ? -1f : 0f;
            if (Math.Abs(direction) < 0.0001f)
            {
                war.AddTickingScore(0f, C.WarScoreTickingCap, CurrentDay);
                return;
            }

            float days = Math.Max(0f, CurrentDay - war.LastTickDay);
            float delta = direction * C.WarScoreTickingDailyGain * days;
            float previous = war.Score;
            war.AddTickingScore(delta, C.WarScoreTickingCap, CurrentDay);
            if (Math.Abs(war.Score - previous) < 0.0001f)
                return;

            AddEvent(
                war,
                WarScoreEventType.TickingLeverage,
                war.Score - previous,
                direction > 0f ? attacker : defender,
                direction > 0f ? defender : attacker,
                debugText: $"days={days:0.0}; daily={C.WarScoreTickingDailyGain:0.00}");
        }

        private bool ApplyLandlessRealmPressure(WarScoreRecord war, Kingdom attacker, Kingdom defender)
        {
            if (war == null
                || attacker == null
                || defender == null
                || war.ConflictType != WarScoreConflictType.ForeignWar
                || war.ResolutionPending
                || war.ParleyPending
                || war.TerminalResolutionQueued)
            {
                return false;
            }

            int attackerStrongholds = CountStrongholds(attacker);
            int defenderStrongholds = CountStrongholds(defender);
            float direction = attackerStrongholds == 0 && defenderStrongholds > 0
                ? -1f
                : defenderStrongholds == 0 && attackerStrongholds > 0
                    ? 1f
                    : 0f;

            float previousPressure = war.LandlessPressureScore;
            float nextPressure = previousPressure;
            if (Math.Abs(direction) < 0.0001f)
            {
                nextPressure = 0f;
            }
            else if (CurrentDay > war.LastTickDay)
            {
                if ((direction > 0f && nextPressure < 0f) || (direction < 0f && nextPressure > 0f))
                    nextPressure = 0f;

                float days = Math.Max(0f, CurrentDay - war.LastTickDay);
                nextPressure += direction * C.WarScoreLandlessPressureDailyGain * days;
            }

            if (Math.Abs(nextPressure - previousPressure) > 0.0001f)
            {
                war.SetLandlessPressureScore(nextPressure, C.WarScoreLandlessPressureCap);
                TouchRuntimeRevision();

                if (Math.Abs(previousPressure) < 0.0001f && Math.Abs(war.LandlessPressureScore) > 0.0001f)
                {
                    Kingdom landless = direction > 0f ? defender : attacker;
                    Kingdom landed = direction > 0f ? attacker : defender;
                    BellumCivileLogger.Log($"War Score landless pressure started; war={war.WarKey}; landless={landless.StringId}; opposing={landed.StringId}; daily={C.WarScoreLandlessPressureDailyGain:0.0}.");
                }
                else if (Math.Abs(war.LandlessPressureScore) < 0.0001f && Math.Abs(previousPressure) > 0.0001f)
                {
                    BellumCivileLogger.Log($"War Score landless pressure cleared; war={war.WarKey}; attacker_fiefs={attackerStrongholds}; defender_fiefs={defenderStrongholds}.");
                }
            }

            if (Math.Abs(war.LandlessPressureScore) < C.WarScoreLandlessPressureCap - 0.01f)
                return false;
            if (IsAuxiliaryClientWar(war))
                return false;

            bool attackerWins = war.LandlessPressureScore > 0f;
            Kingdom winner = attackerWins ? attacker : defender;
            Kingdom loser = attackerWins ? defender : attacker;
            ForceWarScore(
                war,
                attackerWins ? C.WarScoreForcePeaceThreshold : -C.WarScoreForcePeaceThreshold,
                WarScoreEventType.LandlessRealmPressure,
                winner,
                loser,
                "landless realm failed to secure a seat of power");
            return QueueTerminalWarScoreResolution(war, "landless realm pressure reached total surrender");
        }

        private static int CountStrongholds(Kingdom kingdom)
        {
            return kingdom?.Settlements?.Count(settlement => settlement != null && (settlement.IsTown || settlement.IsCastle)) ?? 0;
        }

        private void ClearResolvedLandlessPressureAfterSettlementGain(Kingdom kingdom)
        {
            if (kingdom == null || CountStrongholds(kingdom) == 0)
                return;

            foreach (WarScoreRecord war in GetActiveWars()
                .Where(record => record.ConflictType == WarScoreConflictType.ForeignWar
                    && Math.Abs(record.LandlessPressureScore) > 0.0001f
                    && (record.AttackerKingdomId == kingdom.StringId || record.DefenderKingdomId == kingdom.StringId))
                .ToList())
            {
                Kingdom attacker = ResolveKingdom(war.AttackerKingdomId);
                Kingdom defender = ResolveKingdom(war.DefenderKingdomId);
                bool pressureStillApplies = war.LandlessPressureScore > 0f
                    ? CountStrongholds(defender) == 0 && CountStrongholds(attacker) > 0
                    : CountStrongholds(attacker) == 0 && CountStrongholds(defender) > 0;
                if (pressureStillApplies)
                    continue;

                float previousPressure = war.LandlessPressureScore;
                war.SetLandlessPressureScore(0f, C.WarScoreLandlessPressureCap);
                if (Math.Abs(war.LandlessPressureScore - previousPressure) < 0.0001f)
                    continue;

                TouchRuntimeRevision();
                BellumCivileLogger.Log($"War Score landless pressure cleared after settlement gain; war={war.WarKey}; realm={kingdom.StringId}.");
            }
        }

        private bool TryForceFullOccupation(WarScoreRecord war, Kingdom attacker, Kingdom defender)
        {
            if (war == null || attacker == null || defender == null)
                return false;

            if (IsFullyOccupied(war, defender, attacker))
            {
                ForceWarScore(war, 100f, WarScoreEventType.FullOccupation, attacker, defender, "defender fully occupied");
                QueueTerminalWarScoreResolution(war, "defender fully occupied");
                return true;
            }

            if (IsFullyOccupied(war, attacker, defender))
            {
                ForceWarScore(war, -100f, WarScoreEventType.FullOccupation, defender, attacker, "attacker fully occupied");
                QueueTerminalWarScoreResolution(war, "attacker fully occupied");
                return true;
            }

            return false;
        }

        private static bool IsFullyOccupied(WarScoreRecord war, Kingdom occupied, Kingdom occupier)
        {
            if (war?.FiefSnapshots == null || occupied == null || occupier == null)
                return false;

            var territory = new ClientWarTerritory(war);
            List<WarScoreFiefSnapshotRecord> startingFiefs = war.FiefSnapshots
                .Where(snapshot => snapshot != null && snapshot.OwnerKingdomId == occupied.StringId)
                .ToList();
            if (startingFiefs.Count == 0)
                return false;

            return startingFiefs.All(snapshot => territory.OnSide(ResolveSettlement(snapshot.SettlementId)?.OwnerClan?.Kingdom, occupier));
        }

        private void ForceWarScore(WarScoreRecord war, float score, WarScoreEventType type, Kingdom actor, Kingdom target, string debugText)
        {
            if (war == null)
                return;

            float previous = war.Score;
            war.ForceScore(score);
            AddEvent(war, type, war.Score - previous, actor, target, debugText: debugText);
        }

        private bool QueueTerminalWarScoreResolution(WarScoreRecord war, string reason)
        {
            if (war == null
                || !war.IsActive
                || war.ResolutionPending
                || war.ParleyPending
                || Math.Abs(war.Score) < C.WarScoreForcePeaceThreshold)
            {
                return false;
            }

            if (war.TerminalResolutionQueued)
                return true;

            if (war.ConflictType == WarScoreConflictType.ForeignWar && IsAuxiliaryClientWar(war))
                return false;

            if (war.ConflictType == WarScoreConflictType.ForeignWar && IsStorylineProtectedForeignWar(war))
                return false;

            if (!war.QueueTerminalResolution(reason))
                return false;

            // Once decisive war score is reached, the conflict has passed from campaigning into
            // capitulation. Latch the result so late battle or prisoner events cannot reopen it.
            war.ForceScore(war.Score >= 0f
                ? C.WarScoreForcePeaceThreshold
                : -C.WarScoreForcePeaceThreshold);
            TouchRuntimeRevision();
            BellumCivileLogger.Log($"War Score terminal resolution queued; war={war.WarKey}; kind={war.ConflictType}; score={war.Score:+0.0;-0.0;0.0}; reason={reason ?? "terminal threshold reached"}.");
            return true;
        }

        private bool ResolveQueuedTerminalWarScore(WarScoreRecord war)
        {
            if (CivilWarConflictBehavior.IsScoreTransferPending(war)) return false;
            if (war == null || !war.IsActive || !war.TerminalResolutionQueued)
                return false;

            string reason = string.IsNullOrWhiteSpace(war.TerminalResolutionReason)
                ? "war score reached decisive threshold"
                : war.TerminalResolutionReason;

            if (war.ResolutionPending || war.ParleyPending || Math.Abs(war.Score) < C.WarScoreForcePeaceThreshold)
            {
                war.ClearTerminalResolution();
                TouchRuntimeRevision();
                return false;
            }

            Kingdom attacker = ResolveKingdom(war.AttackerKingdomId);
            Kingdom defender = ResolveKingdom(war.DefenderKingdomId);
            if (war.ConflictType == WarScoreConflictType.ForeignWar
                && TryCloseOrphanedForeignWarTracker(war, attacker, defender, "terminal foreign-war reconciliation"))
            {
                return true;
            }

            if (attacker == null || defender == null)
            {
                BellumCivileLogger.Log($"War Score terminal resolution delayed because a realm could not be resolved; war={war.WarKey}; kind={war.ConflictType}; attacker={war.AttackerKingdomId}; defender={war.DefenderKingdomId}; reason={reason}.");
                return false;
            }

            bool internalConflict = war.ConflictType == WarScoreConflictType.CivilWar
                || war.ConflictType == WarScoreConflictType.ClaimFeud;
            if (!internalConflict && (!ShouldProcessWar(war, attacker, defender) || !attacker.IsAtWarWith(defender)))
            {
                war.ClearTerminalResolution();
                TouchRuntimeRevision();
                return false;
            }

            if (internalConflict
                && !attacker.IsEliminated
                && !defender.IsEliminated
                && !attacker.IsAtWarWith(defender))
            {
                war.ClearTerminalResolution();
                TouchRuntimeRevision();
                return false;
            }

            if (war.ConflictType == WarScoreConflictType.ForeignWar)
            {
                if (StorylineWarProtectionHelper.IsPeaceBlocked(attacker, defender))
                {
                    war.ClearTerminalResolution();
                    TouchRuntimeRevision();
                    BellumCivileLogger.Log($"Discarded Bellum terminal peace resolution for a Story Mode protected war; war={war.WarKey}; attacker={attacker.StringId}; defender={defender.StringId}.");
                    return false;
                }

                if (IsAuxiliaryClientWar(war))
                {
                    war.ClearTerminalResolution();
                    TouchRuntimeRevision();
                    return false;
                }

                war.ClearTerminalResolution();
                ForeignTreatyBehavior treaties = Campaign.Current?.GetCampaignBehavior<ForeignTreatyBehavior>();
                if (treaties?.TryQueueAiParley(war, forced: true, preferWhitePeace: false, reason, war.Score >= 0f ? attacker : defender, out _) == true)
                {
                    TouchRuntimeRevision();
                    return true;
                }
            }
            else
            {
                war.ClearTerminalResolution();
            }

            if (!war.BeginResolution())
            {
                war.QueueTerminalResolution(reason);
                return false;
            }

            bool primaryVictory = war.Score > 0f;
            Kingdom winner = primaryVictory ? attacker : defender;
            Kingdom loser = primaryVictory ? defender : attacker;
            AddEvent(war, WarScoreEventType.ForcedPeace, 0f, winner, loser, debugText: reason);

            bool resolved = ResolveTerminalConflict(war, attacker, defender, primaryVictory, reason);
            if (!resolved)
            {
                war.CancelResolution();
                war.QueueTerminalResolution(reason);
                BellumCivileLogger.Log($"War Score terminal resolution will retry; war={war.WarKey}; kind={war.ConflictType}; score={war.Score:+0.0;-0.0;0.0}; attacker_eliminated={attacker.IsEliminated}; defender_eliminated={defender.IsEliminated}; context={war.ContextId}; reason={reason}.");
                return false;
            }

            MarkWarEnded(war);
            return true;
        }

        private bool TryCloseOrphanedForeignWarTracker(
            WarScoreRecord war,
            Kingdom attacker,
            Kingdom defender,
            string source)
        {
            if (war == null || !war.IsActive || war.ConflictType != WarScoreConflictType.ForeignWar)
                return false;

            string reason = null;
            if (attacker == null || defender == null)
                reason = "a tracked realm could no longer be resolved";
            else if (attacker.IsEliminated || defender.IsEliminated)
                reason = "a tracked realm was eliminated";
            else if (!IsForeignWarPair(attacker, defender))
                reason = "the realms were no longer eligible foreign opponents";
            else if (!attacker.IsAtWarWith(defender))
                reason = "the realms were no longer at war and no peace callback closed the tracker";

            if (reason == null)
                return false;

            float endedDay = GetLastRecordedWarActivityDay(war);
            war.AddEvent(new WarScoreEventRecord(
                WarScoreEventType.WarEnded,
                endedDay,
                0f,
                war.Score,
                attacker?.StringId,
                defender?.StringId,
                debugText: $"{source}; {reason}"));
            war.MarkEnded(endedDay);
            TouchRuntimeRevision();
            BellumCivileLogger.Log(
                $"War Score closed orphaned foreign-war tracker; war={war.WarKey}; score={war.Score:+0.0;-0.0;0.0}; recorded_end_day={endedDay:0.0}; source={source}; reason={reason}.");
            return true;
        }

        private static float GetLastRecordedWarActivityDay(WarScoreRecord war)
        {
            if (war == null)
                return CurrentDay;

            float lastDay = Math.Max(war.StartedDay, war.LastTickDay);
            if (war.Events != null)
            {
                foreach (WarScoreEventRecord evt in war.Events)
                {
                    if (evt != null)
                        lastDay = Math.Max(lastDay, evt.Day);
                }
            }

            return Math.Min(CurrentDay, lastDay);
        }

        private static bool IsAuxiliaryClientWar(WarScoreRecord war)
        {
            if (war == null)
                return false;
            Kingdom attacker = ResolveKingdom(war.AttackerKingdomId);
            Kingdom defender = ResolveKingdom(war.DefenderKingdomId);
            return ClientKingdomBehavior.Instance?.IsAuxiliaryClientWar(attacker, defender) == true;
        }

        internal static bool IsStorylineProtectedForeignWar(WarScoreRecord war)
        {
            if (war == null || war.ConflictType != WarScoreConflictType.ForeignWar)
                return false;

            Kingdom attacker = ResolveKingdom(war.AttackerKingdomId);
            Kingdom defender = ResolveKingdom(war.DefenderKingdomId);
            return StorylineWarProtectionHelper.IsPeaceBlocked(attacker, defender);
        }

        internal bool CanNegotiateInternalOutcome(WarScoreRecord war, string winnerId, out TextObject reason)
        {
            reason = new TextObject("{=BC_InternalPeace_NotReady}The opposing houses are not yet willing to accept these terms.");
            if (war == null || (war.ConflictType != WarScoreConflictType.CivilWar && war.ConflictType != WarScoreConflictType.ClaimFeud)
                || !war.IsActive || war.ResolutionPending || CivilWarConflictBehavior.IsScoreTransferPending(war)) return false;
            var attacker = ResolveKingdom(war.AttackerKingdomId);
            var defender = ResolveKingdom(war.DefenderKingdomId);
            if (attacker == null || defender == null || attacker.IsEliminated || defender.IsEliminated || !attacker.IsAtWarWith(defender)) return false;
            if (string.IsNullOrEmpty(winnerId)) return !CivilWarConflictBehavior.IsRivalryScore(war)
                && IsMutualWhitePeaceEligible(war, out _, out _);
            float relative = winnerId == war.AttackerKingdomId ? war.Score : winnerId == war.DefenderKingdomId ? -war.Score : -1000f;
            if (relative >= C.WarScoreForcePeaceThreshold) return true;
            var loser = ResolveKingdom(winnerId == war.AttackerKingdomId ? war.DefenderKingdomId : war.AttackerKingdomId);
            return PeaceReadiness.CanConcede(relative, CalculatePowerWeightedWarWill(loser, war),
                CurrentDay - war.StartedDay, BellumCivileOptions.WarDurationReluctanceDays);
        }

        internal bool IsMutualWhitePeaceEligible(WarScoreRecord war, out float primaryWill, out float opposingWill)
        {
            primaryWill = 0f;
            opposingWill = 0f;
            if (war?.ConflictType == WarScoreConflictType.ForeignWar && !war.CanReconsiderPeace(CurrentDay)) return false;
            if (war == null
                || !war.IsActive
                || war.ResolutionPending
                || Math.Abs(war.Score) >= C.WarScoreForcePeaceThreshold)
            {
                return false;
            }

            Kingdom primary = ResolveKingdom(war.AttackerKingdomId);
            Kingdom opposing = ResolveKingdom(war.DefenderKingdomId);
            if (!ShouldProcessWar(war, primary, opposing) || !primary.IsAtWarWith(opposing))
                return false;

            primaryWill = CalculatePowerWeightedWarWill(primary,war);
            opposingWill = CalculatePowerWeightedWarWill(opposing,war);
            return PeaceReadiness.CanConsiderWhitePeace(war.Score, primaryWill, opposingWill,
                CurrentDay - war.StartedDay, BellumCivileOptions.WarDurationReluctanceDays);
        }

        private bool TryResolveInternalExhaustedVictory(WarScoreRecord war)
        {
            if (war == null
                || (war.ConflictType != WarScoreConflictType.CivilWar && war.ConflictType != WarScoreConflictType.ClaimFeud)
                || !war.IsActive
                || war.ResolutionPending
                || Math.Abs(war.Score) <= C.WarScoreWhitePeaceMaximumScore
                || Math.Abs(war.Score) >= C.WarScoreForcePeaceThreshold)
            {
                return false;
            }

            Kingdom primary = ResolveKingdom(war.AttackerKingdomId);
            Kingdom opposing = ResolveKingdom(war.DefenderKingdomId);
            if (!ShouldProcessWar(war, primary, opposing) || !primary.IsAtWarWith(opposing))
                return false;

            bool primaryVictory = war.Score > 0f;
            Kingdom losingSide = primaryVictory ? opposing : primary;
            float losingWill = CalculatePowerWeightedWarWill(losingSide, war);
            if (!PeaceReadiness.CanConcede(Math.Abs(war.Score), losingWill, CurrentDay - war.StartedDay,
                BellumCivileOptions.WarDurationReluctanceDays) || !war.BeginResolution())
                return false;

            Kingdom winner = primaryVictory ? primary : opposing;
            BellumCivileLogger.Log($"Internal exhausted surrender requested; war={war.WarKey}; winner={winner.StringId}; loser={losingSide.StringId}; "
                + $"score={war.Score:0.000}; losing_will={losingWill:0.000}; will_threshold={BellumCivileOptions.WarWillMutualWhitePeaceThreshold:0.000}; "
                + $"leverage_threshold={C.WarScoreExhaustedVictoryThreshold:0.000}; age_days={CurrentDay - war.StartedDay:0.000}; reluctance_days={BellumCivileOptions.WarDurationReluctanceDays}.");
            AddEvent(
                war,
                WarScoreEventType.ForcedPeace,
                0f,
                winner,
                losingSide,
                debugText: $"exhausted surrender; losing_will={losingWill:0.0}; leverage={Math.Abs(war.Score):0.0}");

            bool resolved = ResolveTerminalConflict(
                war,
                primary,
                opposing,
                primaryVictory,
                "losing side exhausted under decisive war score pressure");
            if (!resolved)
            {
                war.CancelResolution();
                return false;
            }

            MarkWarEnded(war);
            return true;
        }

        private static float CalculateInternalWhitePeaceChance(float primaryWill, float opposingWill)
        {
            float threshold = BellumCivileOptions.WarWillMutualWhitePeaceThreshold;
            if (threshold <= 0f)
                return 0f;

            float higherWill = Math.Max(primaryWill, opposingWill);
            float depletion = Math.Max(0f, Math.Min(1f, (threshold - higherWill) / threshold));
            return C.WarScoreInternalWhitePeaceBaseChance
                + (C.WarScoreInternalWhitePeaceMaximumChance - C.WarScoreInternalWhitePeaceBaseChance) * depletion;
        }

        private static float CalculatePowerWeightedWarWill(Kingdom kingdom, WarScoreRecord war = null)
        {
            WarPeaceRevampBehavior warWill = Campaign.Current?.GetCampaignBehavior<WarPeaceRevampBehavior>();
            if (kingdom == null || warWill == null)
                return 100f;

            float weightedTotal = 0f;
            float totalWeight = 0f;
            foreach (Clan clan in kingdom.Clans.Where(IsEligibleWarWillClan))
            {
                float weight = Math.Max(1f, clan.CurrentTotalStrength);
                weightedTotal += CourtAgendaBehavior.EffectiveWarWill(clan,war,warWill.GetWarWill(clan)) * weight;
                totalWeight += weight;
            }

            // An internal side with no field-capable noble clans is spent, not eager to continue.
            return totalWeight > 0f ? weightedTotal / totalWeight : 0f;
        }

        private bool ResolveForeignWhitePeace(WarScoreRecord war, string reason, out string report)
        {
            report = "foreign white peace could not be resolved";
            if (!IsMutualWhitePeaceEligible(war, out _, out _)
                || !war.BeginResolution())
            {
                return false;
            }

            Kingdom primary = ResolveKingdom(war.AttackerKingdomId);
            Kingdom opposing = ResolveKingdom(war.DefenderKingdomId);
            AddEvent(war, WarScoreEventType.WhitePeace, 0f, primary, opposing, debugText: reason);
            ShowWhitePeaceMessage(primary, opposing);
            BellumPeaceResolutionContext.RunBellumPeaceResolution(() => MakePeaceAction.Apply(primary, opposing));
            MarkWarEnded(war);
            CourtAgendaBehavior.Current?.RecordRallyOutcome(war,true,true,null,0,"mutual_white_peace");
            report = $"mutual white peace resolved against {opposing.Name}";
            return true;
        }

        private bool ResolveInternalWhitePeace(WarScoreRecord war, string reason)
        {
            if (CivilWarConflictBehavior.IsRivalryScore(war) || war == null || !war.BeginResolution())
                return false;
            Kingdom primary = ResolveKingdom(war.AttackerKingdomId);
            Kingdom opposing = ResolveKingdom(war.DefenderKingdomId);
            if (InternalPeaceSettlementBehavior.Current?.QueueWhitePeace(primary, opposing, resolutionAlreadyBegun: true) != true)
            {
                war.CancelResolution();
                return false;
            }
            AddEvent(war, WarScoreEventType.WhitePeace, 0f, primary, opposing, debugText: reason);
            // The conflict resolver, not successful queueing, owns the completion receipt.
            return true;
        }

        private bool IsPlayerConflictLeader(WarScoreRecord war)
        {
            Kingdom primary = ResolveKingdom(war?.AttackerKingdomId);
            Kingdom opposing = ResolveKingdom(war?.DefenderKingdomId);
            return Clan.PlayerClan != null
                && (primary?.RulingClan == Clan.PlayerClan || opposing?.RulingClan == Clan.PlayerClan);
        }

        private void ShowPlayerInternalWhitePeaceInquiry(WarScoreRecord war, float primaryWill, float opposingWill)
        {
            if (war == null)
                return;

            Kingdom primary = ResolveKingdom(war.AttackerKingdomId);
            Kingdom opposing = ResolveKingdom(war.DefenderKingdomId);
            if (primary == null || opposing == null)
                return;

            war.SetWhitePeaceOfferPending(true);
            TextObject description = new TextObject("{=BC_WarScore_PlayerWhitePeaceBody}Envoys report that both sides are exhausted by the conflict between {FIRST_REALM} and {SECOND_REALM}. Neither holds enough advantage to dictate terms.\n\nWar Score: {WAR_SCORE}\n{FIRST_REALM} War Will: {FIRST_WILL}\n{SECOND_REALM} War Will: {SECOND_WILL}");
            description.SetTextVariable("FIRST_REALM", primary.Name);
            description.SetTextVariable("SECOND_REALM", opposing.Name);
            description.SetTextVariable("WAR_SCORE", war.Score.ToString("+0;-0;0"));
            description.SetTextVariable("FIRST_WILL", primaryWill.ToString("0"));
            description.SetTextVariable("SECOND_WILL", opposingWill.ToString("0"));

            InformationManager.ShowInquiry(new InquiryData(
                new TextObject("{=BC_WarScore_PlayerWhitePeaceTitle}White Peace Proposed").ToString(),
                description.ToString(),
                true,
                true,
                new TextObject("{=BC_WarScore_PlayerWhitePeaceAccept}Accept White Peace").ToString(),
                new TextObject("{=BC_WarScore_PlayerWhitePeaceContinue}Continue the Conflict").ToString(),
                () => ResolvePlayerInternalWhitePeaceChoice(war.WarKey, accept: true),
                () => ResolvePlayerInternalWhitePeaceChoice(war.WarKey, accept: false)));
        }

        private void ResolvePlayerInternalWhitePeaceChoice(string warKey, bool accept)
        {
            WarScoreRecord war = _wars.FirstOrDefault(record => record != null && record.IsActive && record.WarKey == warKey);
            if (war == null)
                return;

            war.SetWhitePeaceOfferPending(false);
            if (accept)
            {
                if (!IsMutualWhitePeaceEligible(war, out _, out _)
                    || CivilWarConflictBehavior.IsScoreTransferPending(war))
                    return;
                ResolveInternalWhitePeace(war, "player accepted mutual white peace");
                return;
            }

            war.ScheduleNextWhitePeaceCheck(CurrentDay + C.WarScorePlayerWhitePeaceDeclineCooldownDays);
        }

        private static void ShowWhitePeaceMessage(Kingdom first, Kingdom second)
        {
            TextObject message = new TextObject("{=BC_WarScore_WhitePeace}With neither side able to press its advantage, {FIRST_REALM} and {SECOND_REALM} agree to a white peace.");
            message.SetTextVariable("FIRST_REALM", first?.Name ?? new TextObject("?"));
            message.SetTextVariable("SECOND_REALM", second?.Name ?? new TextObject("?"));
            InformationManager.DisplayMessage(new InformationMessage(message.ToString(), BellumNotificationColors.Warning));
        }

        private bool ResolveTerminalConflict(
            WarScoreRecord war,
            Kingdom primary,
            Kingdom opposing,
            bool primaryVictory,
            string reason = "war score reached decisive threshold")
        {
            switch (war.ConflictType)
            {
                case WarScoreConflictType.ClaimFeud:
                    return Campaign.Current?.GetCampaignBehavior<ClaimFeudWarBehavior>()?
                        .TryResolveWarScore(primary, opposing, primaryVictory, reason) == true;

                case WarScoreConflictType.CivilWar:
                {
                    if (CivilWarConflictBehavior.IsRivalryScore(war))
                        return Campaign.Current?.GetCampaignBehavior<CivilWarResolutionBehavior>()?.TryResolveRivalVictory(war, primaryVictory) == true;
                    FactionManagerBehavior factionManager = Campaign.Current?.GetCampaignBehavior<FactionManagerBehavior>();
                    CivilWarResolutionBehavior resolution = Campaign.Current?.GetCampaignBehavior<CivilWarResolutionBehavior>();
                    FactionObject faction = factionManager?.GetFactionByRebelKingdom(primary)
                        ?? factionManager?.GetFactionByTrackedRebelKingdomId(war.ContextId)
                        ?? factionManager?.GetFactionByTrackedRebelKingdomId(war.AttackerKingdomId);
                    if (faction == null || resolution == null || faction.ParentKingdom != opposing)
                        return false;

                    BellumCivileLogger.Log($"Civil-war terminal resolution requested; war={war.WarKey}; score={war.Score:0.0}; rebel_victory={primaryVictory}; reason={reason}.");
                    if (reason == "losing side exhausted under decisive war score pressure")
                        ConflictOutcomeBehavior.Capture(ConflictOutcomeBehavior.Current?.BeginCivil(faction, primary), "EXHAUSTION",
                            new TextObject("{=BC_Result_Exhaustion}{LOSER} could no longer sustain the fighting and was forced to concede.")
                                .SetTextVariable("LOSER", primaryVictory ? opposing.Name : primary.Name));

                    if (primaryVictory)
                    {
                        if (primary.IsEliminated)
                            return false;
                        resolution.ResolveRebelVictory(faction, primary);
                        // A saved continuation may defer settlement. Do not retire its
                        // winning matchup until the resolver actually completes it.
                        return !war.IsActive;
                    }
                    else
                    {
                        return resolution.TryResolveTerminalLiegeVictory(faction, primary);
                    }
                }

                case WarScoreConflictType.ForeignWar:
                default:
                    BellumCivileLogger.Log($"War Score forced foreign peace; winner={primary?.StringId}; loser={opposing?.StringId}; score={war.Score:+0.0;-0.0;0.0}; war={war.WarKey}.");
                    TextObject message = new TextObject("{=BC_WarScore_ForcedPeace}With {LOSER_REALM} unable to continue the war, {WINNER_REALM} imposes a settlement and the fighting comes to an end.");
                    message.SetTextVariable("WINNER_REALM", primaryVictory ? primary.Name : opposing.Name);
                    message.SetTextVariable("LOSER_REALM", primaryVictory ? opposing.Name : primary.Name);
                    InformationManager.DisplayMessage(new InformationMessage(message.ToString(), BellumNotificationColors.Warning));
                    BellumPeaceResolutionContext.RunBellumPeaceResolution(() => MakePeaceAction.Apply(primary, opposing));
                    CourtAgendaBehavior.Current?.RecordRallyOutcome(war,true,false,
                        primaryVictory?primary.StringId:opposing.StringId,0,"forced_foreign_settlement");
                    return true;
            }
        }

        private static float SignedDeltaFor(WarScoreRecord war, Kingdom actor, float magnitude)
        {
            if (war == null || actor == null || Math.Abs(magnitude) < 0.0001f)
                return 0f;

            float abs = Math.Abs(magnitude);
            if (actor.StringId == war.AttackerKingdomId)
                return abs;
            if (actor.StringId == war.DefenderKingdomId)
                return -abs;
            return 0f;
        }

        private static IEnumerable<WarScoreFiefSnapshotRecord> BuildFiefSnapshot(Kingdom attacker, Kingdom defender)
        {
            foreach (Kingdom kingdom in new[] { attacker, defender }.Where(IsValidWarScoreSnapshotKingdom))
            {
                foreach (Settlement settlement in kingdom.Settlements.Where(settlement => settlement != null && (settlement.IsTown || settlement.IsCastle)))
                {
                    yield return new WarScoreFiefSnapshotRecord(
                        settlement.StringId,
                        kingdom.StringId,
                        settlement.OwnerClan?.StringId,
                        settlement.IsTown,
                        settlement.IsCastle);
                }
            }
        }

        private WarScoreRecord RegisterConflict(
            WarScoreConflictType conflictType,
            Kingdom primary,
            Kingdom opposing,
            string contextId,
            string debugText)
        {
            if (!CanRegisterConflict(conflictType, primary, opposing))
                return null;

            EnsureCollectionsInitialized();
            string key = BuildWarKey(primary, opposing);
            WarScoreRecord existing = _wars.FirstOrDefault(war => war != null && war.IsActive && war.WarKey == key);
            if (existing != null)
                return existing;

            WarScoreRecord record = new WarScoreRecord(
                key,
                primary.StringId,
                opposing.StringId,
                CurrentDay,
                BuildFiefSnapshot(primary, opposing),
                conflictType,
                contextId);
            _wars.Add(record);
            AddEvent(record, WarScoreEventType.WarStarted, 0f, primary, opposing, debugText: debugText);
            UpdateOccupationScore(record, WarScoreEventType.OccupationUpdated, primary, opposing, debugText: "initial occupation state");
            BellumCivileLogger.Log($"War Score tracking started; kind={conflictType}; war={key}; primary={primary.StringId}; opposing={opposing.StringId}; fiefs={record.FiefSnapshots.Count}; context={contextId ?? "none"}.");
            return record;
        }

        private static bool CanRegisterConflict(WarScoreConflictType conflictType, Kingdom primary, Kingdom opposing)
        {
            if (!WarPeaceRevampBehavior.IsRevampEnabled() || primary == null || opposing == null || primary == opposing)
                return false;

            return conflictType == WarScoreConflictType.ForeignWar
                ? IsForeignWarPair(primary, opposing)
                : !primary.IsEliminated && !opposing.IsEliminated;
        }

        private static bool ShouldTrackForeignWar(Kingdom first, Kingdom second)
        {
            return CanRegisterConflict(WarScoreConflictType.ForeignWar, first, second);
        }

        private static bool ShouldProcessWar(WarScoreRecord war, Kingdom primary, Kingdom opposing)
        {
            return war != null
                && !CivilWarConflictBehavior.IsScoreTransferPending(war)
                && CanRegisterConflict(war.ConflictType, primary, opposing);
        }

        private static bool IsValidPermanentKingdom(Kingdom kingdom)
        {
            if (kingdom == null
                || kingdom.IsEliminated
                || kingdom.RulingClan == null
                || string.IsNullOrWhiteSpace(kingdom.StringId))
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(kingdom.StringId) && kingdom.StringId.StartsWith("bc_feud_"))
                return false;

            // Diplomacy represents its civil wars with temporary Kingdom objects as well. Keep
            // those out of Bellum's foreign-war ledger so Diplomacy can consolidate them normally.
            if (ModIntegrationHelper.IsDiplomacyRebelKingdom(kingdom))
                return false;

            FactionManagerBehavior factionManager = Campaign.Current?.GetCampaignBehavior<FactionManagerBehavior>();
            return factionManager?.GetFactionByRebelKingdom(kingdom) == null
                && !BellumKingdomVisibilityHelper.IsTemporaryBellumKingdom(kingdom);
        }

        private static bool IsValidForeignWarParticipant(Kingdom kingdom) => IsValidPermanentKingdom(kingdom)
            || (kingdom != null && !kingdom.IsEliminated && kingdom.RulingClan != null
                && Campaign.Current?.GetCampaignBehavior<FactionManagerBehavior>()?.GetFactionByRebelKingdom(kingdom) != null);

        internal static bool IsForeignWarPair(Kingdom first, Kingdom second)
        {
            if (first == second || !IsValidForeignWarParticipant(first) || !IsValidForeignWarParticipant(second)) return false;
            var manager = Campaign.Current?.GetCampaignBehavior<FactionManagerBehavior>();
            var firstParent = manager?.GetFactionByRebelKingdom(first)?.ParentKingdom ?? first;
            var secondParent = manager?.GetFactionByRebelKingdom(second)?.ParentKingdom ?? second;
            // Parent/rebel and rival-claimant pairs belong exclusively to the civil-war ledger.
            return firstParent != secondParent;
        }

        internal void CompleteReunifiedForeignWar(Kingdom rebel, Kingdom enemy)
        {
            var war = GetActiveWar(rebel, enemy);
            if (war == null || war.ConflictType != WarScoreConflictType.ForeignWar) return;
            AddEvent(war, WarScoreEventType.WarEnded, 0f, rebel, enemy, debugText: "rebel realm reunified; foreign war not inherited");
            MarkWarEnded(war);
            BellumCivileLogger.Log($"Rebel foreign war ended on reunification; war={war.WarKey}; score={war.Score}; inherited=False.");
        }

        private static bool IsValidWarScoreSnapshotKingdom(Kingdom kingdom)
        {
            return kingdom != null && !kingdom.IsEliminated;
        }

        private static bool IsEligibleWarWillClan(Clan clan)
        {
            return clan != null
                && !clan.IsEliminated
                && !clan.IsBanditFaction
                && !clan.IsUnderMercenaryService
                && clan.Leader != null
                && !clan.Leader.IsDead
                && clan.Kingdom != null
                && clan.CurrentTotalStrength > 0f;
        }

        private static string BuildWarKey(Kingdom first, Kingdom second)
        {
            string firstId = first?.StringId ?? string.Empty;
            string secondId = second?.StringId ?? string.Empty;
            return WarScoreRecord.PairKey(firstId, secondId);
        }

        private static Kingdom ResolveKingdom(string kingdomId)
        {
            if (string.IsNullOrWhiteSpace(kingdomId))
                return null;
            return Kingdom.All.FirstOrDefault(kingdom => kingdom != null && kingdom.StringId == kingdomId);
        }

        private static Settlement ResolveSettlement(string settlementId)
        {
            if (string.IsNullOrWhiteSpace(settlementId))
                return null;
            return Settlement.All.FirstOrDefault(settlement => settlement != null && settlement.StringId == settlementId);
        }

        private static IEnumerable<Kingdom> GetSideKingdoms(MapEventSide side)
        {
            return side?.Parties?
                .Select(party => party?.Party?.MapFaction as Kingdom)
                .Where(kingdom => kingdom != null && !kingdom.IsEliminated)
                .Distinct() ?? Enumerable.Empty<Kingdom>();
        }

        private static int CountCasualties(MapEventSide side)
        {
            if (side == null)
                return 0;

            return side.Parties.Sum(party => Math.Max(0, (party?.HealthyManCountAtStart ?? 0) - (party?.Party?.NumberOfHealthyMembers ?? 0)));
        }

        private static int CountStartingHealthyTroops(MapEventSide side)
        {
            return side?.Parties?.Sum(party => Math.Max(0, party?.HealthyManCountAtStart ?? 0)) ?? 0;
        }

        private static bool IsWarLeader(Kingdom kingdom, Hero hero)
        {
            return hero != null && (kingdom?.RulingClan?.Leader == hero || kingdom?.Leader == hero);
        }

        private static bool IsPrimaryHeirOfWarLeader(Kingdom kingdom, Hero hero)
        {
            if (kingdom?.RulingClan == null || hero == null)
                return false;

            Hero leader = kingdom.RulingClan.Leader;
            Hero heir = kingdom.RulingClan.Heroes
                .Where(candidate => candidate != null && candidate != leader && candidate.IsAlive && !candidate.IsChild)
                .OrderBy(candidate => candidate.Age)
                .FirstOrDefault();
            return heir == hero;
        }

        private static float CurrentDay => (float)CampaignTime.Now.ToDays;

        private void EnsureCollectionsInitialized()
        {
            _wars = _wars ?? new List<WarScoreRecord>();
        }
    }
}
